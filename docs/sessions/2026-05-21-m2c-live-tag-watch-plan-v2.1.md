# M.2c — Live Tag Watch (v2.1 amendment, LOCKED)

**Status:** v2.1 — Locked amendment to v2. Eight items: five from ChatGPT round-2 review pass (cache cap, register-first ordering, `Channel<T>` reality-check defer, `IngestTimestamp` wording, lifecycle handling) PLUS three from ChatGPT round-3 review (explicit snapshot phase frames, snapshot sequence-number policy, explicit `source-restarted` event).
**Date:** 2026-05-21
**Predecessor:** [v2 (locked)](2026-05-21-m2c-live-tag-watch-plan-v2.md)
**Trigger:** Round-2 review surfaced five clarifications + flagged the `Channel<T>` peek assumption as overconfident. Round-3 review ratified the round-2 amendments but caught one remaining concurrency issue — the "FIFO guarantees snapshot precedes live" claim in my round-2 verdict was only partially true. v2.1 closes that gap with explicit frame-phase semantics (Option A) rather than implicit FIFO timing.

---

## 0. What v2.1 changes from v2

Eight items. **All accept; zero rejected.** Five are correctness locks; one is honestly deferred to v3 reality-check; two are clean architectural additions surfaced in round-3.

| # | Item | Where in v2 it modifies | Reason it changed |
|---|---|---|---|
| 1 | Latest-value cache hard cap (200 tags/source, LRU + warning) | §4.2.2 (was: risk note only) | Realistic catalog sizes (Brother ~55, FOCAS2 ~65) push close to v2's <1MB ceiling; the cap protects against pathological growth |
| 2 | Subscribe ordering — **register first, then seed** | §4.2.2 (was: construct → seed → register) | Closes a race where live publishes between snapshot-seed and registration would be dropped |
| 3 | `Channel<T>` lazy-prune feasibility — **honestly defer to v3 reality-check** | §4.2.1 (was: claimed `Channel<T>` peek is O(1)) | `ChannelReader<T>` does not natively expose peek semantics; v2 was overconfident. v3 picks the data structure during implementation. |
| 4 | `IngestTimestamp` monotonicity wording | §4.6 (was: "monotonic" — wrong) | `DateTime.UtcNow` can jump on NTP corrections; honest wording without introducing `TimeProvider` |
| 5 | Lifecycle handling — unknown / deleted / restarted source | §6 + §11.4 (was: only unknown-source 404 test from v1) | Operational gaps that the 7-day soak would otherwise surface as subtle bugs |
| **6** | **Explicit snapshot-phase frames (`snapshot-start` / `snapshot-value` / `snapshot-end` / `live-value`) — supersedes the FIFO-ordering assumption in item #2** | §4.2.2 + §6 wire shape (was: `snapshot: true \| false` discriminator + implicit FIFO) | **FIFO does not guarantee cross-thread ordering** between snapshot writes (subscribe thread) and live writes (supervisor thread). Phase frames push ordering to client logic; server doesn't need cross-thread synchronization. |
| 7 | Snapshot sequence-number policy | §4.2.3 (was: ambiguous) | Snapshot frames preserve the original publish-time sequence number; client's "latest-sequence-wins" naturally favors live over older snapshot for the same tag |
| 8 | Explicit `source-restarted` lifecycle event | §6 + §11.4 (was: UI infers restart from `newSeq < lastSeq`) | Explicit signal is cleaner architecturally; supervisor knows about restarts authoritatively; UI logic becomes simpler |

---

## 1. The 8 changes in detail

### 1.1 Latest-value cache cap — promoted from risk note to lock

**Lock — v2 §4.2.2 amendment:**

> The latest-value cache is capped per source at **200 distinct canonical tag paths**. When a publish for a tag path beyond the cap arrives, the cache evicts the **least-recently-updated** entry and logs a single warning per (source, eviction round):
>
> `[runtime-tap] latest-value cache cap exceeded for source '{sourceId}' — evicting LRU. Cache size: 200. Consider auditing the source's configured DataPoints set.`
>
> The cap value lives as a `RuntimeTap.LatestValueCacheCapPerSource` const. **Non-configurable in v1.** If the customer install reveals legitimate sources emitting >200 tags, that's a v1.1 amendment with operator-input on the right cap.

Rationale for 200: Brother's `BrotherTagMap` has ~55 entries; FOCAS2's catalog ~65; Modbus typically <50 per source. 200 covers all realistic configurations + headroom for catalog evolution. Above 200 indicates either dynamic-tag misconfiguration or a future high-tag protocol — both warrant a warning, not silent growth.

**Test added** (§11.1):

- `LatestValueCache_CapAt200_EvictsLeastRecentlyUpdated_AndLogsWarning`
- `LatestValueCache_SteadyStateAtCap_DoesNotGrowUnbounded`

### 1.2 Register-first subscribe ordering (with item 6's correction below)

**Lock — v2 §4.2.2 amendment, paragraph "Snapshot semantics — single subscriber":**

The corrected sequence of operations in `Subscribe(sourceInstanceId, IReadOnlySet<string> tagPaths, ct)`:

1. Validate `sourceInstanceId` via `ICanonicalCatalogProvider.GetCatalog(sourceInstanceId)` returning non-empty; otherwise throw `SourceNotFoundException` (caught by the API layer → HTTP 404).
2. Validate every `tagPath` in `tagPaths` is a member of the source's catalog; otherwise throw `UnknownTagPathException` (→ HTTP 400).
3. Construct the subscriber's bounded channel (capacity = `tagPaths.Count + 100` per v2 §4.2.2 burst headroom).
4. **Register the subscriber in the per-source `_subscribers` list** (atomic — `lock` or `ConcurrentBag.Add`). After this point, concurrent `TryPublish` calls on the supervisor thread observe this subscriber and may begin enqueuing live frames into its channel.
5. **Emit `snapshot-start` frame** to the channel (see §1.6 below for the new frame shape).
6. Iterate the source's latest-value cache for each tag in `tagPaths`. For each entry whose `IngestTimestamp` is within the last 5 min, emit a `snapshot-value` frame carrying the cached point + its original publish-time sequence number + `phase: "snapshot"`.
7. **Emit `snapshot-end` frame.**
8. Return the subscription handle to the caller. From this point the subscriber is in normal live-streaming mode.

Steps 5-7 run on the subscribe thread synchronously. Steps 4 (registration) precedes 5-7 specifically so the supervisor thread CAN'T miss a publish — any concurrent live publish during steps 5-7 enqueues into the channel and reaches the client (see §1.6 for how the client handles interleaved live frames during the snapshot phase).

### 1.3 `Channel<T>` lazy-prune feasibility — honest v3 defer

**Lock — v2 §4.2.1 amendment, replace the "the ring is a `Channel<T>` underneath — peeking the head is O(1)" claim with:**

> **v3 reality-check Q-V2.7 (NEW):** How does the implementation enforce the 5-minute age bound on the per-subscriber ring? Two candidate structures:
>
> **(a) Parallel `Queue<DateTime>` alongside `Channel<T>`** — on `TryWrite` to the channel, before adding the new entry, pop & drop oldest-age entries from both the queue and the channel until age ≤ 5 min. The channel and queue stay in lock-step. Standard `Channel<T>.CreateBounded(capacity, FullMode = DropOldest)` handles the ≤100 count bound; the parallel queue + manual prune handles the ≤5 min age bound.
>
> **(b) Custom `SubscriberRing` deque** — explicit circular-buffer with head/tail pointers, age-prune on write/read. Expose a `ChannelReader<T>`-shaped async surface (`ReadAsync`, `WaitToReadAsync`). Cleaner architecturally; ~50-80 LOC custom code; no fight with `Channel<T>` API.
>
> Implementation session picks. If (a) gets awkward (e.g., the parallel queue synchronization complicates lock-free guarantees), **switch to (b) rather than fighting `Channel<T>`**. The plan is NOT committed to `Channel<T>` for the ring.

**What v2.1 confirms is unchanged:**

- Per-subscriber ≤100 count bound — `Channel<T>` with `FullMode = DropOldest` handles this regardless of choice.
- Per-subscriber ≤5 min age bound — REAL enforcement, three pruning points (publish/read/snapshot) per v2 §4.2.1.
- The supervisor's `TryPublish` API contract — non-blocking, never throws, O(1) per subscriber.

The honest version: v2 overclaimed the implementation surface. v2.1 preserves the invariants and defers the data-structure choice. v3 picks during the implementation session with the option to switch tactics if the chosen path gets awkward.

### 1.4 `IngestTimestamp` monotonicity wording

**Lock — v2 §4.6 amendment, replace "gateway-side, monotonic" with:**

> `IngestTimestamp` is the gateway-side UTC timestamp captured by `CanonicalDataPointFactory` at intake-channel write time. It is **not guaranteed monotonic** under system-clock adjustments (NTP corrections, manual operator changes). For the stale-flag computation, this is acceptable — the worst case is a brief incorrect stale flag during an NTP correction event, which auto-resolves on the next UI tick (1 Hz).
>
> v1 does NOT introduce `TimeProvider` for tap code (overkill for a UI feature). Tests inject a deterministic clock via the existing `Func<DateTime> utcNow` parameter pattern already used in `SqliteBuffer.OpenAsync(...)` and elsewhere in Core — the same pattern is sufficient here.

No code or test impact beyond wording correction + the existing `Func<DateTime>` injection pattern.

### 1.5 Lifecycle handling — unknown / deleted / restarted source

**Lock — v2 §4 + §6 amendments. Three new lifecycle behaviors:**

| Lifecycle event | Locked behavior |
|---|---|
| **Subscribe with unknown `sourceInstanceId`** | API returns **HTTP 404** with `{"error":"source-not-found","sourceInstanceId":"..."}`. The catalog provider's `GetCatalog(sourceId)` returns empty for unknown sources — `LiveTagsApi` checks first and short-circuits before calling `_runtimeTap.Subscribe(...)`. |
| **Source deleted via config draft → apply during active session** | `RuntimeTap` emits a terminal frame to all active subscribers of that source: `event: source-deleted\ndata: {"sourceInstanceId":"..."}` and then disposes their subscriptions cleanly. The catalog provider's cache for the deleted source is invalidated so re-subscribe correctly 404s. Implementation needs a config-change listener (likely subscribing to `IConfigurationManager.CurrentChanged` per the established pattern in `RuntimeReloadCoordinator`). |
| **Source restarted during active session** (supervisor lifecycle: source adapter failed → reconnected, or hot-reload reconfigured the source) | `RuntimeTap` emits an explicit lifecycle frame to active subscribers: `event: source-restarted\ndata: {"sourceInstanceId":"..."}`. The sequence counter resets to 0; the snapshot phase from §1.6 does NOT re-trigger (operator already has a Watch session open — they get the live stream from sequence 0 onward, with the explicit `source-restarted` event giving them visual confirmation in the subtle banner per Q-V1.8). See §1.8 for the explicit-event policy supersession of "infer from sequence reset." |

**New tests** in `LiveTagsApiTests.cs` (§11.4 — 3 additions):

- `Sse_UnknownSource_Returns404_WithSourceNotFoundError` (was `Sse_UnknownSource_Returns404` in v1)
- `Sse_SourceDeletedDuringSession_EmitsTerminalEvent_AndClosesStream`
- `Sse_SourceRestartedDuringSession_EmitsExplicitRestartEvent_AndSequenceResetsToZero` (was implicit-inference in v2; v2.1 §1.8 changes this)

### 1.6 Explicit snapshot-phase frames (NEW round-3 — supersedes implicit FIFO ordering)

**The concurrency issue ChatGPT caught:** v2's snapshot model relied on the claim that "channel FIFO + completing snapshot synchronously before returning" preserves snapshot-before-live ordering for the subscriber. **That's only true for writes from a single thread.** The supervisor thread can interleave live writes between snapshot writes:

```
subscribe thread:  snapshot[A] ─ snapshot[B] ─ snapshot[C] ─ snapshot[D]
supervisor thread:                       live[X] ─                    live[Y]
channel order:     snapshot[A] - snapshot[B] - live[X] - snapshot[C] - snapshot[D] - live[Y]
```

The subscriber sees interleaved snapshot + live in the channel. v2's `snapshot: true | false` discriminator is technically present on each frame, but the client logic has to handle "snapshot mid-stream" cases, which is unnecessarily complex.

**Lock — v2 §6 wire format amendment.** Replace v2's `snapshot: true | false` discriminator with explicit phase frames:

```
event: phase-start
data: {"phase":"snapshot","sourceInstanceId":"src-1"}

event: tag-value
data: {"phase":"snapshot","tagPath":"Status/RunState","value":3,"valueType":"Int","quality":"Good","deviceTimestamp":"...","ingestTimestamp":"...","sequenceNumber":42}

event: tag-value
data: {"phase":"snapshot","tagPath":"Status/Running","value":"Running","valueType":"String","quality":"Good","deviceTimestamp":"...","ingestTimestamp":"...","sequenceNumber":43}

event: phase-end
data: {"phase":"snapshot","sourceInstanceId":"src-1"}

event: tag-value
data: {"phase":"live","tagPath":"Status/RunState","value":3,"valueType":"Int","quality":"Good","deviceTimestamp":"...","ingestTimestamp":"...","sequenceNumber":127}

(... ongoing live frames ...)

event: source-restarted
data: {"sourceInstanceId":"src-1"}

event: tag-value
data: {"phase":"live","tagPath":"Status/RunState","value":3,"valueType":"Int","quality":"Good","deviceTimestamp":"...","ingestTimestamp":"...","sequenceNumber":1}

(... etc ...)
```

| Frame type | When emitted | Purpose |
|---|---|---|
| `phase-start` | First frame on any new subscription | Tells the client "snapshot phase begins" |
| `tag-value` (phase: snapshot) | Each cached-value snapshot emission | Initial state |
| `phase-end` | Last frame of snapshot phase | Tells the client "snapshot complete; subsequent frames are live or interleaved-live" |
| `tag-value` (phase: live) | Live publish | Real-time update |
| `source-restarted` (§1.8) | Source supervisor restart | Lifecycle signal |
| `source-deleted` (§1.5) | Source removed via config apply | Terminal lifecycle signal — stream closes after |

**How the client handles interleaved live frames during snapshot phase:**

Live frames may arrive between `phase-start` and `phase-end` (because of the cross-thread race). The client's logic is simple per-tag: **latest sequence number wins.** Because:

- Snapshot frames carry the original publish-time sequence number (§1.7 below).
- Live frames during the snapshot phase carry CURRENT sequence numbers (which are strictly greater than the cached snapshot sequences).
- The Studio page renders the value per tag from the highest-sequence-number observed so far.
- Result: a live frame arriving during the snapshot phase OVERWRITES the snapshot value for that tag — which is exactly correct (the live frame is fresher).

No client-side buffering needed. The `phase-start` / `phase-end` markers exist for two reasons: (1) UI can show a "loading initial state..." indicator that clears on `phase-end`; (2) operational telemetry / debugging can distinguish snapshot from live emissions.

**Server-side simplicity:** the server does NOT need cross-thread synchronization. The subscribe thread emits `phase-start` → cached snapshots → `phase-end` synchronously; the supervisor thread emits live frames concurrently. The channel's FIFO preserves order within each thread; cross-thread interleaving is explicitly accepted because the client handles it via "latest sequence wins."

**Test coverage** (§11.1 + §11.4 additions):

- `Subscribe_FirstFrameIsPhaseStart_WithPhaseSnapshot`
- `Subscribe_LastSnapshotFrameIsPhaseEnd_WithPhaseSnapshot`
- `Subscribe_LiveFramesArrivingDuringSnapshot_Phase_AreEmittedAsTagValueWithPhaseLive`
- `Sse_PhaseStartIsFirstFrameOnEventStream_BeforeAnyTagValue`
- `Sse_PhaseEndPrecedesFirstPhaseLiveFrame`
- `Sse_ClientObservedHighestSequencePerTag_AfterFullStreamProcessed` (end-to-end test confirming "latest sequence wins" semantics)

### 1.7 Snapshot sequence-number policy

**Lock — v2 §4.2.3 amendment:**

> Snapshot emissions carry the **original publish-time sequence number** of the cached `CanonicalDataPoint`. They do NOT increment the source's sequence counter. Live emissions continue to increment via `Interlocked.Increment` and emit at the current counter value.

| Frame type | Sequence number source |
|---|---|
| `tag-value` (phase: snapshot) | Cached point's original `_sequenceCounter` value at publish time (preserved in the cache entry) |
| `tag-value` (phase: live) | Current `_sequenceCounter` value, post-increment |
| `phase-start` / `phase-end` / `source-restarted` / `source-deleted` | No sequence number (lifecycle frames) |

This makes the client's "latest sequence wins" logic mathematically clean: live frames always have higher sequence numbers than snapshot frames for the same tag (because live frames are emitted at a later time, after the cache was populated by a previous publish).

**Cache structure update:** `RuntimeTap`'s per-source `Dictionary<canonicalTagPath, CanonicalDataPoint>` must store entries with their original sequence numbers. The `CanonicalDataPoint` itself does not carry a sequence number (it's a tap-side concept, not a canonical-pipeline concept), so the cache entry shape is:

```csharp
internal sealed record LatestValueCacheEntry(
    CanonicalDataPoint Point,
    long SequenceNumber,
    DateTime CachedAt);  // for the LRU + 5-min-age prune
```

The cache update path on `TryPublish`:
1. Increment the source's sequence counter.
2. Update (or insert) the cache entry with `(point, newSequenceNumber, DateTime.UtcNow)`.
3. Emit live frames to subscribers at `newSequenceNumber`.

When a new subscriber arrives and the snapshot is served, each cached entry's `SequenceNumber` is included in the emitted `tag-value` frame's `sequenceNumber` field.

### 1.8 Explicit `source-restarted` event (supersedes implicit-inference)

**Lock — v2 §4.5 + §13 amendments:**

v2 said: "the Studio page recognizes the sequence-number reset by checking `newSeq < lastSeq` and surfaces the subtle banner." That's implicit inference — fragile, depends on the client implementing the right detection logic, and may break if future sequence implementations change behavior.

**v2.1 lock — explicit event:**

> When a source supervisor restarts a source adapter (failed-and-reconnected, or hot-reload-reconfigured), the supervisor calls `_runtimeTap.NotifySourceLifecycle(sourceInstanceId, LifecycleEvent.Restarted)` (new API). The tap propagates an `event: source-restarted\ndata: {"sourceInstanceId":"..."}` frame to all active subscribers of that source AND resets the source's sequence counter to 0.
>
> Subscribers' Studio pages handle the explicit event:
>
> 1. Display the subtle reconnect banner (per Q-V1.8) with text "Source reconnected; sequence reset."
> 2. Clear any "sequence gap" warning that may be in the local state.
> 3. Continue consuming the stream — subsequent `tag-value` frames flow normally with sequence numbers starting at 1.

**New API on `IRuntimeTap`:**

```csharp
public interface IRuntimeTap
{
    // ... existing v2 members ...

    // NEW v2.1 — lifecycle signaling from the supervisor to active subscribers.
    void NotifySourceLifecycle(string sourceInstanceId, SourceLifecycleEvent evt);
}

public enum SourceLifecycleEvent
{
    Started,    // supervisor brought the source online
    Restarted,  // supervisor recycled the source (failure or hot-reload)
    Stopped,    // supervisor took the source offline (config apply removed it; emits source-deleted frame)
}
```

The supervisor calls `NotifySourceLifecycle` from the appropriate lifecycle points (existing `SourceSupervisor` transitions — reality-check Q-V2.8 below confirms the exact call sites). For `NullRuntimeTap`, the method is a no-op.

**Tests added** (§11.1 + §11.3 + §11.4):

- `NotifySourceLifecycle_Restarted_EmitsSourceRestartedFrameToAllActiveSubscribers`
- `NotifySourceLifecycle_Restarted_ResetsSequenceCounterToZero`
- `NotifySourceLifecycle_Stopped_EmitsSourceDeletedFrame_AndClosesActiveSubscriptions`
- `Supervisor_OnSourceRestart_CallsTapNotifyLifecycle_WithRestartedEvent`
- `Sse_SourceRestartedFrame_PrecedesFirstSequence1LiveFrame`

---

## 2. What stays unchanged from v2

All v2 locks remain in force. v2.1 adds discipline; it retracts nothing.

- 6 anti-scope bullets (§2.1) — unchanged.
- P1 enforcement (§3) — unchanged.
- All v1 open question resolutions (Q24, Q25/Q-V1.3, Q-V1.1, Q-V1.2, Q-V1.4, Q-V1.5, Q-V1.6, Q-V1.7, Q-V1.8) — unchanged.
- Performance budgets (§4.4) — unchanged.
- License-disabled three-layer lock (§4.5) — unchanged.
- Stale rule formula (§4.6) — unchanged (only the monotonicity wording corrected per §1.4 above).
- Endpoint limits (§4.7) — unchanged.
- Step-by-step implementation sequence (§9) — unchanged in shape; individual steps now incorporate the v2.1 amendments (e.g., step 6 adds the `phase-start`/`phase-end` emission, step 11 adds the new lifecycle API).

---

## 3. New v3 reality-check items (carried into the implementation session)

| # | Area | Question |
|---|---|---|
| Q-V2.7 (was implicit in v2 §4.2.1, now explicit per §1.3) | Per-subscriber ring data structure | Pick between (a) `Channel<T>` + parallel `Queue<DateTime>` for age-prune, or (b) custom `SubscriberRing` deque with channel-reader-shaped async surface. Confirm during implementation; switch tactics if first choice gets awkward. |
| Q-V2.8 (NEW v2.1 per §1.8) | `IRuntimeTap.NotifySourceLifecycle` call sites in `SourceSupervisor` | Identify the exact `SourceSupervisor` lifecycle transition points where the supervisor knows it just (re)started a source. Most likely: end of `RunSourceLoopAsync` initialization, post-recovery loop reentry, and the `StopAsync` path. v3 picks the exact call sites. |
| Q-V2.9 (NEW v2.1 per §1.5) | `IConfigurationManager.CurrentChanged` subscription for source-deleted | Confirm whether the existing `RuntimeReloadCoordinator` already exposes a "source removed" signal that `RuntimeTap` can subscribe to, or whether the tap needs to subscribe to `IConfigurationManager.CurrentChanged` independently. Avoid duplicating the diff-computation logic. |
| Q-V2.10 (NEW v2.1 per §1.7) | `LatestValueCacheEntry` placement | The cache holds `(CanonicalDataPoint, long sequence, DateTime cachedAt)` tuples. Confirm the record-type lives in `ElpisEdgeConnect.Core.Diagnostics` internal scope; never leaks across the `IRuntimeTap` public surface (subscribers never see `LatestValueCacheEntry` directly — they only see snapshot frames carrying the unpacked sequence number). |

Existing v3 reality-check items from v2 §12.2 (Q-V2.1 through Q-V2.6) carry forward unchanged.

---

## 4. Knock-on effects

- **Step 8 of the implementation sequence** (was: "`LiveTagEventDto` wire shape") now also includes the `PhaseStartDto`, `PhaseEndDto`, `SourceRestartedDto`, `SourceDeletedDto` lifecycle DTOs. Round-trip serialization tests cover all five frame shapes.
- **Step 15** (was: "`LiveTagsApi.cs` SSE endpoint") gains the lifecycle event mapping (subscribing to `IConfigurationManager.CurrentChanged` per Q-V2.9; emitting `source-deleted` on source removal).
- **Step 16** (was: "`LiveTagWatchModel` + `LiveTagWatch.razor` page") explicitly handles the phase frames + `source-restarted` event + the "latest sequence wins" rendering rule. The implementation no longer infers state from sequence gaps.
- **Test target** updates: ~+125 tests now (was +115 in v2). Distribution: +10 around the new phase-frame contract, lifecycle events, and explicit-restart handling.

---

## 5. Final ratification

Per ChatGPT round-3 verdict: "v2.1 amendment, fix snapshot/live ordering semantics explicitly before implementation." v2.1 closes that with the explicit phase-frame model (Option A). The ambiguous FIFO-ordering claim from my round-2 verdict is replaced; the snapshot/live contract is now deterministic and protocol-safe without relying on cross-thread channel synchronization.

**v2.1 status: LOCKED. Proceed to v3 reality-check.** No further ChatGPT review iteration needed before v3 — the reality-check resolves the data-structure and call-site questions from inside the codebase.

---

**End of v2.1 amendment. Implementation gates on this amendment + v2 + v1 jointly; v3 reality-check resolves Q-V2.1 through Q-V2.10 from inside the codebase during the implementation session.**
