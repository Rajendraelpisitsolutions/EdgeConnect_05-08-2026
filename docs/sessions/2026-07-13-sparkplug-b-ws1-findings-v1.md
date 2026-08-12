# Sparkplug B Spike — WS1 Findings: Replay Context (v1)

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** **Complete** — design + executable evidence delivered (§9–§10): 15 spike
tests + 983 Core.Tests green. One carry-forward: SqliteBuffer replay-boundary parity
(plan v2.3, K1). Ack/split/cursor ownership stays Core-side (v2.3 §1 — corrects the
v2.2 placement).
**Charter:** `2026-07-13-sparkplug-b-spike-tasks-v1.md` WS1. **ADR:** 0036 Rule 6.

> **Headline:** the replay-phase signal Sparkplug needs **does not exist today**, and
> the reason is now located precisely in code. The watermark `H` is capturable
> atomically with no new race. A minimal, protocol-neutral optional seam is the clean
> answer. **The Core seam is needed** — carried to plan **v2.3** (the current plan;
> "v2.2" references in §§8–10 below are the then-current target and are superseded).

---

## 1. Hot path (WS1 deliverable #1 — code-path trace)

Per route, `RouteWorker` (`src/ElpisEdgeConnect.Core/Routing/RouteWorker.cs`):

```
RunAsync (RouteWorker.cs:78)
  ├─ for each sink: Buffer.RegisterSinkAsync(sinkId)        :86-89   ← cursor = _tail (oldest held)
  ├─ for each sink: Task.Run(RunSinkLoopAsync(publisher))  :92-97
  └─ RunIntakePumpAsync                                     :112
        source.Read → filter → tap(source) → pipeline.Execute → backpressure.Decide
        → Buffer.EnqueueAsync(points)  :223  (assigns monotonic _head seq)
        → dispatcher.NotifyAll()       :248

RunSinkLoopAsync (RouteWorker.cs:281) — one per sink:
  loop:
    batch = Buffer.DequeueBatchAsync(sinkId, 256)   :296   → BufferBatch{Points, FirstSequence, LastSequence}
    if batch.IsEmpty: publisher.NotifyBufferEmpty() :306   → single exit from Draining
    tap(sink)                                        :313
    outcome = publisher.PublishWithRetryAsync(batch.Points)  :321   ← ★ no context passed
    if Acked/Dropped: Buffer.AckAsync(sinkId, batch.LastSequence)  :336   → cursor = LastSequence+1
    else RetriesExhausted: leave cursor, wait for next wake
```

The `★` line is the seam point: `PublishAsync(batch, ct)` carries **no phase / epoch
/ sequence-range context**.

## 2. Cold-start `IsDraining` gap — CONFIRMED (deliverable #2, analysis)

`ReplayCoordinator.IsDraining` (`ReplayCoordinator.cs:47`) is set **only** by
`BeginDrain()`, and `SinkPublisher` calls `BeginDrain()` **only on the first success
after a failure** (`SinkPublisher.cs:99-107`, guarded by `_isDegraded` which starts
`false` at `:42`).

On cold start with backlog:
- `RegisterSinkAsync` sets the cursor to `_tail` = oldest held sequence
  (`InMemoryBuffer.cs:315-318`), so the first `DequeueBatchAsync` returns the
  **backlog**.
- The publisher has never failed → `_isDegraded == false` → `BeginDrain()` never
  runs → **`IsDraining == false` for the entire cold-start backlog replay.**

**Conclusion:** `IsDraining` cannot be the historical-vs-live authority. It models
*post-degradation drain only*, not *replay*. This is the core justification for a new
phase signal.

## 3. Watermark `H` is capturable atomically — no new race (deliverable #3)

`H` = the buffer head at epoch start (all points with seq `< H` are backlog; every
later append gets seq `≥ H`).

Key facts from `InMemoryBuffer`:
- `_head` = next sequence to assign; incremented **only** in `WriteLocked`
  (`:173-179`) together with `_totalEnqueued`. `EvictOldestLocked` moves `_tail`, not
  `_head` (`:181-202`). ⇒ **`_head == _totalEnqueued` invariantly**, and
  `GetStatsAsync().TotalEnqueued` (`:373`) exposes it under `_lock`, atomically.
- `DequeueBatchAsync` returns `FirstSequence`/`LastSequence` for the batch
  (`:249-254`).

**Race analysis (answers 2nd-review tightening #1):** capturing `H` once (one locked
read) is sufficient. Because sequences are monotonic and each batch carries its exact
`[FirstSequence, LastSequence]`, the sink classifies each batch against the *single*
captured `H` with **no** dependence on a `GetStats`-then-`Dequeue` ordering. There is
no TOCTOU: a point enqueued after the `H` capture necessarily has seq `≥ H` and is
therefore (correctly) not historical.

**Recommendation:** even though `TotalEnqueued == _head` works today, add a
purpose-built, protocol-neutral op so we don't couple to that invariant and so
`SqliteBuffer` is safe:
```csharp
// IMessageBuffer (additive) — captures the epoch atomically, does NOT move any cursor
ValueTask<ReplayEpoch> BeginReplayEpochAsync(string sinkId, CancellationToken ct);
public readonly record struct ReplayEpoch(long EpochId, long InitialCursor, long HighWaterMark);
```
`InitialCursor` = the sink's current cursor; `HighWaterMark` = `_head` snapshot.
⚠️ `IMessageBuffer` is documented as the **FINAL C2a contract** ("no shape change
permitted after C2a closes") — this additive method needs explicit sign-off in v2.2.
Fallback if we refuse to touch the contract: read `H` from
`GetStatsAsync().TotalEnqueued` (works, but couples to the `_head==_totalEnqueued`
invariant — document it).

## 4. Boundary-straddling batch → split at `H` in the publisher (tightening #2)

A dequeued batch can straddle `H` (`FirstSequence ≤ H ≤ LastSequence`), since
`DequeueBatchAsync` just returns up to `maxCount` from the cursor. Decision: a
**replay-aware publisher splits the batch at `H`** and calls `PublishAsync` twice —
one sub-batch `≤ H` (Phase=Replay/CatchUp) and one `> H` (later phase) — never a
mixed batch labeled wholly one phase. `AckAsync(upToSequence)` (`InMemoryBuffer.cs:259`,
`TryAdvance` to `upToSequence+1`) supports acking each sub-range independently, so the
split acks cleanly. The split is **protocol-neutral** and lives in the
publisher/worker, keeping the Sparkplug sink simple.

## 5. Epoch-entry matrix mapped to code (tightening #3)

| Situation | Mechanism today | Initial phase |
|---|---|---|
| Fresh sink, empty buffer | Register at `_tail==_head`; first dequeue empty | **Live** |
| Fresh sink, existing backlog | Register at `_tail<_head`; dequeues backlog; `IsDraining=false` (§2) | **Replay**, epoch `H=_head` |
| Existing sink recovering after failure | `BeginDrain` sets `IsDraining=true` on first post-failure success | **Replay/CatchUp** under a fresh epoch (capture new `H` at recovery) |
| Process restart, persisted cursor (`SqliteBuffer`) | cursor rehydrated from store | compare cursor vs captured `H` |
| Buffer empties during replay | `NotifyBufferEmpty → TryCompleteDrain` (`SinkPublisher.cs:185`) | cross an **explicit barrier** into Live — not merely `IsDraining=false` |

The new epoch/phase becomes the authority; `IsDraining` is subsumed (it remains valid
for the recovery case but is no longer the *only* signal).

## 6. Phase neutrality (tightening #4)

Core exposes only `enum ReplayPhase { Replay, CatchUp, Live }`. The Sparkplug actor's
states (Connecting/SubscribingNCMD/Birthing/Replaying/CatchingUp/Live/Rebirthing/…,
ADR-0036 Rule 7) stay **inside the sink assembly**. `SinkPublisher` never learns
Sparkplug states; the actor never infers buffer phase from timestamps —
`PublishContext.Phase` is the sole phase authority.

## 7. Proposed seam (WS1 deliverable #5 — minimal API diff)

Additive, capability-gated, protocol-neutral. Base `ISinkAdapter` unchanged (LOCKED):

```csharp
// New optional capability — SinkPublisher supplies context only to sinks that implement it
public interface IReplayAwareSinkAdapter : ISinkAdapter
{
    Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        PublishContext context,
        CancellationToken ct);
}

public sealed record PublishContext(
    string RouteId,
    ReplayPhase Phase,          // Replay | CatchUp | Live
    long ReplayEpoch,
    long ReplayHighWaterMark,   // H
    long BatchFirstSequence,
    long BatchLastSequence);

public enum ReplayPhase { Replay, CatchUp, Live }
```

Threading: `RouteWorker.RunSinkLoopAsync` already holds the `BufferBatch` (with its
sequence range) — it (or a replay-aware `SinkPublisher`) captures the epoch via
`BeginReplayEpochAsync` at sink start / recovery, splits at `H` (§4), and calls the
replay-aware overload when the sink implements it; otherwise the existing
`PublishAsync(batch, ct)` path is untouched. `SinkPublisher` is `internal sealed`, so
this is an internal threading change plus one new public interface + record in Core.

## 8. Decision (WS1 deliverable #7)

**The optional Core seam is needed.** Existing contracts cannot tell a sink whether a
batch is historical replay or live (§2), and Sparkplug birth/replay correctness
depends on it (ADR-0036). The seam is:
1. `IReplayAwareSinkAdapter` + `PublishContext` + `ReplayPhase` (new, in Core).
2. `IMessageBuffer.BeginReplayEpochAsync` (additive — needs FINAL-contract sign-off;
   fallback via `TotalEnqueued`).
3. `SinkPublisher`/`RouteWorker` threading + split-at-`H` (internal).

**Open sub-questions for v2.2 (not blocking the remaining WS1 tests):**
- Touch the FINAL `IMessageBuffer` contract, or use the `TotalEnqueued` fallback?
- Does the split-at-`H` live in `SinkPublisher` or a new replay-aware wrapper, to keep
  the non-replay path zero-cost?
- `SqliteBuffer` (`src/…/Buffer/SqliteBuffer.cs`, not yet read) must expose the same
  atomic head/epoch — verify parity in WS1's remaining step.

## 9. As-built prototype + executable results (WS1 exit package)

**Design refinement (supersedes §3/§7's `BeginReplayEpochAsync` sketch).** Per the
2nd-review "pasteable call," the boundary capture is a **separate, protocol-neutral
optional capability** — NOT a method on the locked `IMessageBuffer`, and the
publisher (not the buffer) owns the epoch. `CutoffExclusive` replaces the inclusive
`H` to kill the off-by-one. As-built (all **internal**, additive, spike):

| Type / change | File |
|---|---|
| `ReplayBoundary(FirstPendingSequence, CutoffExclusive)` + `IReplayBoundaryProvider` | `src/…/Core/Buffer/IReplayBoundaryProvider.cs` (new) |
| `ReplayPhase {Replay,CatchUp,Live}` + `PublishContext(…, ReplayCutoffExclusive, BatchFirstSequence, BatchLastSequence)` | `src/…/Core/Adapters/PublishContext.cs` (new) |
| `IReplayAwareSinkAdapter : ISinkAdapter` (optional overload) | `src/…/Core/Adapters/IReplayAwareSinkAdapter.cs` (new) |
| `InMemoryBuffer` implements `IReplayBoundaryProvider` **explicitly** (reads cursor + `_head` under `_lock`; `TotalEnqueued` stays encapsulated) | `src/…/Core/Buffer/InMemoryBuffer.cs` (+1 base iface, +1 method) |
| `ReplayAwareSinkPublisher` prototype (capture epoch → split at cutoff → single-phase publish → independent sub-range ack → retry-without-republish) | `src/…/Core/Routing/ReplayAwareSinkPublisher.cs` (new, spike, NOT wired to RouteWorker) |

**`IMessageBuffer` is unchanged. `SinkPublisher`/`RouteWorker` are unchanged.** No
production code path touches the new types (only tests) → non-production until the
v2.2 go/no-go.

**Executable results (`dotnet test … -p:NuGetAudit=false`):**
- **10/10 spike tests pass** (`FullyQualifiedName~Routing.Spike`):
  - `ColdStartReplayGapTests` — proves `IsDraining==false` through cold-start
    backlog replay (deliverable #2).
  - `ReplayBoundaryCaptureTests` (5) — empty vs backlog, cursor-not-advanced,
    post-capture appends ≥ cutoff, concurrent-writer boundary consistency (#3).
  - `ReplayAwareSinkPublisherTests` (4) — split-at-cutoff Replay→CatchUp,
    Live/Replay-only (empty side not dispatched), and **CatchUp-failure retries
    without re-publishing the acked Replay sub-range** (#4).
- **Regression: full Core.Tests green — 978/978, 0 failed** (#6). Additive changes
  broke nothing.
- **Build blocker note:** the `MessagePack NU1902` blocker is **remediated**
  (Core.csproj now pins `MessagePack 2.5.302`); the build succeeded and
  `-p:NuGetAudit=false` was not actually needed. (Memory updated.)

**One remaining WS1 sub-item:** implement `IReplayBoundaryProvider` on `SqliteBuffer`
(capture cursor + append cutoff in one read transaction) and prove parity. Deferred
to the kernel/v2.2 step — the in-memory prototype proves the shape; SqliteBuffer
parity is a mechanical follow-up, flagged so it isn't forgotten.

## 10. Decision → carry to plan v2.2
The optional Core seam is **validated by executable evidence**: `IReplayBoundaryProvider`
+ `PublishContext`/`ReplayPhase` + `IReplayAwareSinkAdapter`, with epoch ownership in
the publisher and split-at-`CutoffExclusive`. Recommend **promoting these from
internal to public** and integrating into the production `SinkPublisher`/`RouteWorker`
(ack ownership + the Live barrier / WS7 second watermark) at the v2.2 go/no-go — plus
the `SqliteBuffer` parity impl. No `IMessageBuffer` amendment is required.
