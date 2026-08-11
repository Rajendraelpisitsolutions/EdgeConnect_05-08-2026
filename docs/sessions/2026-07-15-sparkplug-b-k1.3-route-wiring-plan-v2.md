# Sparkplug B — K1.3 route-wiring execution plan (v2, external-review-folded)

**Status:** 🟠 **v2 — external review folded; reality-check pending (→ v3 freeze).** Supersedes v1
(same date). This folds the external-review verdict (5 blocking design issues + decisions) into a
corrected design. **Do not implement from v2** — v3 (reality-checked) is the execution baseline.

**Date:** 2026-07-15 · **Branch:** `feat/sparkplug-b-k1.3-route-wiring`
**Plan-trail:** v1 → **external review** → **v2 (this)** → reality-check → v3 (frozen).
**Most important correction (review Blocker 3):** **remove `AdvanceGenerationAsync` from
host-requested rebirth.** Advancing the generation deliberately empties the current-generation
manifest (K1.2b/c fact — `AdvanceGenerationAsync` writes only `meta.current_schema_generation`;
`CaptureBirthStateAsync` filters `WHERE schema_generation = current`), so v1's
"advance → capture fresh birth" would birth an EMPTY snapshot. K1.3 does **same-generation
operational rebirth only**; generation-changing (material schema) rebirth is a **named deferred
follow-up**.

---

## 1. Locked resolutions to the 5 review blockers

### B1 — capability interface, NOT a concrete `SqliteBuffer` cast
Reject v1's Q1(b) `buffer as SqliteBuffer`. Add an **internal optional capability interface** the
returned buffer may implement; `SqliteBuffer` implements both `IMessageBuffer` and it:
```csharp
internal interface IReplayRouteBuffer
{
    ValueTask<ReplayRouteActivation> ActivateReplayAsync(string routeId, string replaySinkId, CancellationToken ct);
    IReplaySessionStateProvider SessionStateProvider { get; }             // post-activation
    IReplayBoundaryProvider BoundaryProvider { get; }                     // post-activation
    ValueTask<AssignedSequenceRange> AppendTrackedAsync(IReadOnlyList<CanonicalDataPoint> points,
        RouteSchemaGeneration expectedGeneration, CancellationToken ct);
    ValueTask AdvanceGenerationAsync(RouteSchemaGeneration expectedCurrent, RouteSchemaGeneration next, CancellationToken ct);
}
internal sealed record ReplayRouteActivation(RouteSchemaGeneration CurrentGeneration /* + activation head if needed */);
```
`RoutingEngine` capability-gates, never names `SqliteBuffer`:
```csharp
var buffer = await _bufferFactory.CreateAsync(routeId, policy, ct);
if (isReplayRoute && buffer is not IReplayRouteBuffer)
    throw new RouteConfigurationException("Replay-aware routes require a replay-capable StoreAndForward buffer.");
```
The `Route` holds a **neutral internal `ReplayRouteContext`**, not a `SqliteRouteStoreHandle`:
```csharp
internal sealed record ReplayRouteContext(
    IReplayRouteBuffer Buffer, IReplayAwareSinkAdapter Sink, string SinkId, RouteSchemaGeneration CurrentGeneration);
```
`IRouteBufferFactory` stays unchanged; `DefaultRouteBufferFactory` stays the sole constructor; the
migration + quarantine wiring stay intact. Fallback if a returned-buffer capability is impractical:
a separate optional factory capability (Q1(c)). The concrete cast is dropped.

### B2 — irreversible activation happens at the registration COMMIT boundary
Replay activation is persistent + one-way + drained-only + sink-bound + makes legacy `EnqueueAsync`
throw. It must be the LAST fallible step before the route is published, so a later failure cannot
strand a permanently-enabled DB behind a rolled-back non-replay route. Locked order:
1. Resolve the sink binding + adapter; **require exactly one sink**; require `IReplayAwareSinkAdapter`;
   require `StoreAndForward`; require a **stable configured sink id** (match any persisted replay
   sink id).
2. Create the buffer via `IRouteBufferFactory`; require it implements `IReplayRouteBuffer`.
3. Complete every OTHER fallible registration/config check.
4. **Activate** (`ActivateReplayAsync`) — the commit boundary.
5. Capture a fresh post-activation `ReplayRouteContext` (incl. current generation).
6. Publish the route into the registry. **Never start the worker/intake before activation succeeds.**

**No automatic downgrade:** an enabled DB never silently falls back to the legacy path. Config
replacement must explicitly (a) preserve replay semantics + the same stable sink id, (b) use a new
route/buffer identity, or (c) perform an explicit reset migration (out of K1.3). Stable sink id must
be the configured id used for register/dequeue/ack/birth/persisted-ownership — never a runtime
object identity.

### B3 — same-generation operational rebirth ONLY (generation-changing rebirth deferred)
Two distinct operations, separated:
- **A. Operational rebirth (IMPLEMENT in K1.3):** NCMD/operator/reconnect rebirth. Do NOT advance
  generation; do NOT drain-to-head; pause only sink DATA publication; leave intake running; capture
  a fresh coherent start state at a NEW H; mint a candidate epoch; `RebirthAsync`; promote the epoch
  only on success; restart Replay→CatchUp→Live from the persisted cursor. The H/C design handles this
  cleanly — a new H is a finite historical boundary while acquisition continues.
- **B. Material route-schema transition (DEFER):** needs an authoritative new-generation manifest
  seed (an atomic "advance + seed" op, or a route-schema inventory, or a completeness signal) that
  Core does not have today. **Removed from K1.3 DoD:** sink-signalled schema-generation advancement;
  `RebirthRequest(SchemaChange)` as authority; immediate fresh birth after `AdvanceGenerationAsync`.
  Carried as a named follow-up (a schema/config milestone). `RebirthRequest.Reason` stays diagnostics,
  never a schema-mutation command.

### B4 — one serialized `ReplayRouteDriver` actor + an explicit rebirth wake path
Lock a single internal `ReplayRouteDriver` that makes ALL replay-aware sink calls (Begin / phase
Publish / CompleteCatchUp / Rebirth / End). `RouteWorker` only SELECTS: ordinary sink → existing
`SinkPublisher`; replay-aware sink → `ReplayRouteDriver`. One task serializes all sink lifecycle +
publish (no sink method concurrent with another); intake is a separate producer; session/epoch/H/C/
phase live in one state object. **Rebirth must wake an empty Live route** — a buffer notification
alone is insufficient (no point was appended), so the driver waits on the union:
```csharp
await Task.WhenAny(bufferSignal.WaitAsync(ct), rebirthSignal.WaitAsync(ct)); // + cancellation, + retry-delay
```
The host request is async/queued/non-reentrant, bounded single-slot/coalescing, epoch-gated
(current-epoch → accept/coalesce; duplicate → coalesce; older → ignore; future/unknown → deterministic
ignore/reject), and returns on ACCEPT (not on rebirth completion) per the K1.1 contract.

### B5 — exact split / retry / ack + a lifecycle failure table
- **Full-subrange ack only:** advance the cursor for a published subrange ONLY when
  `Success && AcceptedCount == points.Count && RejectedCount == 0`. Any partial/ambiguous result acks
  NOTHING and retries the whole subrange (duplicates beat cursor loss). (The base `SinkPublisher`
  advances on `Success` alone — the replay driver uses the STRICTER rule.)
- **H and C are barriers, not labels:** for an H-straddling batch, publish only the Replay prefix,
  ack only it, reach cursor H, capture C, re-dequeue from H as CatchUp. For a C-straddling batch,
  publish+ack the CatchUp prefix `< C`, `CompleteCatchUpAsync`, enter Live, re-dequeue from C as Live.
  Never retain an unacked in-memory remainder across a transition — re-dequeue (keeps restart/cursor
  authority simple).
- **Lifecycle failure policy (locked):**

  | Call | Failure behavior |
  |---|---|
  | `BeginReplaySessionAsync` | no DATA, no epoch promotion; retry or fault route |
  | replay `PublishAsync` | no ack unless full success; retry per delivery policy |
  | `CompleteCatchUpAsync` | remain CatchUp; do NOT enter Live |
  | `RebirthAsync` | candidate epoch NOT promoted; old epoch stays authoritative |
  | `EndSessionAsync` | report failure but STILL run `StopAsync`/disposal |

  K1.1 locks "a failed birth must not promote its candidate epoch" — K1.3 IMPLEMENTS it (not the sink).

## 2. Decisions on v1's open questions (folded)

- **Q1 factory shape:** `IReplayRouteBuffer` capability on the returned buffer (B1). Separate optional
  factory interface is the fallback. No concrete cast.
- **Q2 activation timing:** registration commit boundary (B2); no worker/intake before activation;
  documented no-auto-downgrade rule.
- **Q3 backpressure/retention:** replay routes require `StoreAndForward`; NO depth Drop/Spill; NO
  silent eviction for `MaxDepth`; a tracked-append storage failure **faults the route**; `MaxAge` is
  an explicit retention/delivery window (not an unlimited at-least-once guarantee); disk-quota
  protection is a later storage/ops feature. Config must reject/flag drop-implying overflow policies
  for a replay route. K1.3 states this limit now; K1.2e measures/hardens.
- **Q4 tracked-append exposure:** via `IReplayRouteBuffer` (B1) — not `IMessageBuffer`, not a concrete
  cast, not provider downcasts. The activation result carries the current generation; the intake
  caches that typed generation and passes it to each tracked append. A **stale-generation append is an
  invariant failure → fault the route** (never silently reload/retry — could mis-assign a schema
  epoch).
- **Q5 K1.3 vs K2/K3:** confirmed — K1.3 = the protocol-neutral Core driver; K3 implements
  `IReplayAwareSinkAdapter`; K1.3 end-to-end tests use a FAKE replay-aware sink, no Sparkplug types.
- **Q6 K1.2e sequencing:** parallel, non-blocking to K1.3; must complete before production ENABLEMENT
  (owns the large-stale-manifest scan measurement + corruption/perf gates).
- **Q7 in-memory:** replay routes require `StoreAndForward`; reject `None`/`InMemory` before
  activation; do not pull the deferred in-memory O-C providers into K1.3.

## 3. Corrected lifecycle (from the review; refine file-level in v3)

**Registration:** resolve route+sink → require exactly one `IReplayAwareSinkAdapter` sink →
require `StoreAndForward` → require stable configured sink id → create buffer via factory → require
`IReplayRouteBuffer` → activate at commit → capture current generation → build `ReplayRouteContext`
→ publish route.

**Worker/session start:** new `ReplaySessionId` + candidate initial `ReplayEpochId` → build a
coalescing `IReplaySessionHost` → capture start state (cursor = `FirstPendingSequence`, H =
`CutoffExclusive`, birth snapshot) → `BeginReplaySessionAsync` → on success promote session/epoch →
run the phase driver. Intake may append once the tracked generation is known, but DATA publication
stays behind the successful-birth barrier.

**Phase loop:** `cursor<H` Replay; `cursor==H && C uncaptured` capture C; `H≤cursor<C` CatchUp;
`cursor==C` `CompleteCatchUpAsync` → Live; `cursor≥C` Live. Continuous intake does not block
termination — C is captured once and fixed.

**Same-generation rebirth:** accept current-session/epoch request → finish the in-flight publish
decision → pause DATA → leave intake running → capture fresh start state at new H → mint candidate
next epoch → `RebirthAsync` → success: promote epoch, reset C, resume Replay→CatchUp→Live from the
persisted cursor; failure: do not promote, retry/fault per the table. No generation advance, no
drain-to-head.

**Stop:** stop accepting rebirth requests → finish/cancel the driver loop → if a session was begun,
`EndSessionAsync(reason)` exactly once → `ISinkAdapter.StopAsync` (Host) → dispose. The end reason is
explicitly `Stop` vs `ConfigurationReplaced` — never inferred from a bare cancellation token.

## 4. Decomposition (revised)

1. **`IReplayRouteBuffer` capability + activation-at-commit + `ReplayRouteContext`** (B1/B2/Q1/Q2).
2. **Tracked-append intake + typed-generation caching + fault-on-stale/storage-failure** (Q3/Q4).
3. **`ReplayRouteDriver` — birth + phase loop (Replay/CatchUp/Live) with H/C barrier splitting +
   strict-ack** (B4/B5).
4. **Rebirth (same-generation) + coalescing `IReplaySessionHost` + empty-route wake + epoch gating +
   failure table** (B4/B5, minus generation advance per B3).
5. **End-session + Stop-vs-ConfigurationReplaced reason + config-replace path + regressions** (B5).

## 5. Reality-check items to verify before v3 freeze (from the review)

Confirm in real code: `RouteDefinition.Sinks` contents; where resolved `ISinkAdapter` instances live;
whether the configured sink id == `ISinkAdapter.InstanceId`; how stop is distinguished from config
replacement; whether the dispatcher can wake one sink; whether `DequeueBatchAsync` guarantees
contiguous ordered ranges; how `SinkPublisher` treats a partial `PublishResult`; **where sink
`StartAsync`/`StopAsync` are owned**; whether activation's returned state includes current generation;
whether any registration step can fail AFTER the proposed activation commit point.

## 6. v2 test plan (folds the review's 20 + v1's set)

Add at least: (1) a non-`SqliteBuffer` `IMessageBuffer` cannot enter replay mode (capability absence
fails deterministically); (2) registration failure before activation leaves the DB disabled;
(3) post-activation cannot fall back to legacy enqueue; (4) restart with the same stable sink id;
changed id → persisted-mismatch error; (5) zero/multiple sinks rejected; (6) `InMemory`/`None`
rejected; (7) empty route births + enters Live without DATA; (8) continuous intake still reaches Live
via fixed H/C; (9) H- and C-straddling batches published+acked as separate subranges;
(10) partial publish advances no cursor; (11) first-subrange success + second-subrange failure keeps
only the first ack; (12) rebirth wakes an empty Live driver; (13) duplicate current-epoch requests
coalesce; (14) stale session/epoch requests do nothing; (15) same-generation rebirth preserves a
POPULATED birth snapshot; (16) failed rebirth does not promote the candidate epoch; (17) intake
continues while DATA is paused for rebirth; (18) `EndSessionAsync` exactly once before `StopAsync`;
(19) config replacement supplies the correct end reason; (20) append storage failure faults the route
(no point drop). Full Core.Tests + Management.Tests green; `Category!=Flaky`; no `Thread.Sleep`.

## 7. Explicitly OUT / deferred

`Sinks.SparkplugB` assembly/actor + payload/topic/mappers/profile (K2/K3); licensing + config
validation + DI (K4); Studio wizard (K5); **generation-changing (material schema) rebirth** (named
follow-up per B3); K1.2e route-store finalization (parallel); in-memory O-C providers (Q7). K1.3 ships
the Core-neutral replay route driver + a fake replay-aware sink — nothing protocol-specific.
