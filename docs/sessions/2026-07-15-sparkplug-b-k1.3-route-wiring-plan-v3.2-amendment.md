# Sparkplug B — K1.3 route-wiring plan v3.2 (amendment — final pre-slice-1 contract locks)

**Status:** ✅ **Amendment to the frozen v3 baseline** (2026-07-15). Folds the FINAL external-review
pass (post-v3.1): two contract blockers + four consistency corrections + supersession notes. **This is
the last architectural amendment** — the reviewer's disposition is that once these exact contracts are
committed, review moves directly to implementation go (a mechanical confirmation, no further
architectural pass). Read with v3 + v3.1; where this conflicts with v3/v3.1, **v3.2 wins**.

**Grounding facts (verified in code for the B1 decision):** there is NO canonical route-schema
fingerprint/identity in the config source (`grep` found none — only compiled DLLs); the Host
`RuntimeReloadCoordinator` classifies the config diff into `ReloadOp` Add/Remove/**Restart** per
entity but does NOT classify schema-equivalence; boot reopen (HostStartup → RegisterRouteAsync) has
the NEW config but not the old. **Consequence:** without a persisted identity, schema equivalence on
reopen can only be TRUSTED, not VERIFIED — so Option A (persist+compare) would require inventing a
fingerprint the reviewer cautioned against, and Option B cannot cover boot reopen either.

---

## B1 (resolved) — A1 becomes Option C: honest external invariant + only the guards K1.3 can enforce

v3.1 A1 over-claimed a "fail-closed K1.3 guard" for material schema changes; the persisted-sink-id
match catches only `same routeId + different replaySinkId`, NOT `same routeId + same sinkId + changed
filters/transforms/removed metric/changed identity/datatype`. **Locked model: Option C.**

- **K1.3 does NOT detect same-route-id schema drift.** No fingerprint source exists, and K1.3 does not
  invent a casual hash over config objects (reviewer-cautioned). On reopen, reusing a `routeId`
  **asserts** schema equivalence; K1.3 cannot verify it.
- **External invariant (documented, deployment/config responsibility):** a **material route-schema
  change must use a NEW `routeId` (new buffer identity)** — deployment/configuration tooling is
  responsible for assigning it. K1.3 never restarts against the old DB and calls it a schema transition.
- **Enforceable guards K1.3 DOES deliver (kept, not over-claimed):**
  1. **Persisted identity mismatch → fail closed.** `route_id` is already validated on open (K1.2b
     `LoadReplayState` → `RouteStoreRouteMismatch`); K1.3 additionally requires the incoming replay
     sink id to match the persisted `replay_sink_id` (mismatch → persisted-mismatch failure). This
     catches a changed **sink identity** — it does NOT catch filter/transform/metric changes.
  2. **Registration downgrade guard** (see B2-B).
- **Deferred (named follow-up — the SAME milestone as generation-changing rebirth):** code-level
  material-schema-reuse ENFORCEMENT — either a persisted `ReplayRouteSchemaIdentity` (Option A, once a
  stable canonical fingerprint exists) or a coordinator-supplied `ReplayRouteReplacementKind`
  classification (Option B, once the reload coordinator classifies schema-equivalence). Material-schema
  handling (detect + advance-and-seed a new generation manifest + reuse-enforcement) is ONE deferred
  milestone.
- **Test 21 is REMOVED** (it would test the wrong thing / fabricate runtime knowledge). Replaced by the
  enforceable tests (§ test plan): changed-sink-identity cannot reuse (30), downgrade rejected (25).

> ⚠️ **User decision surfaced:** Option C accepts a residual risk — if deployment reuses a `routeId`
> across a *material* schema change, K1.3 re-births stale/removed metrics (mitigated only by the
> documented invariant). The stronger alternative is to build a persisted `ReplayRouteSchemaIdentity`
> (Option A) INTO K1.3 — but that needs a stable canonical fingerprint source that does not exist today
> and expands K1.3 scope into the config layer. v3.2 locks Option C as the honest default; escalate to
> Option A only on explicit direction.

## B2 (resolved) — capability expresses activation lifecycle + prevents auto-downgrade

Supersedes v3.1 A3's interface. Two fixes: providers are returned FROM activation (honoring K1.2d's
null-before / valid-after snapshot semantics — never non-null property getters that would have to throw
or bypass the handle); and an explicit enabled-state read makes the no-downgrade rule enforceable at
registration.

```csharp
internal interface IReplayRouteBuffer
{
    bool IsReplayTrackingEnabled { get; }              // reads the owner's ALREADY-LOADED state
                                                       // (SqliteRouteStore.IsReplayStateTrackingEnabled, K1.2b) — no 2nd connection
    ValueTask<ReplayRouteActivation> ActivateReplayAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken);
    ValueTask<AssignedSequenceRange> AppendTrackedAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        RouteSchemaGeneration expectedGeneration, CancellationToken cancellationToken);
}

internal sealed record ReplayRouteActivation(
    RouteSchemaGeneration Generation,                  // the PERSISTED generation (may be non-zero on reopen)
    IReplayBoundaryProvider BoundaryProvider,          // valid ONLY as of successful activation
    IReplaySessionStateProvider SessionStateProvider);
```
`SqliteBuffer` implements `IReplayRouteBuffer` (delegating to the owner + the K1.2d handle).
**Registration becomes deterministic** (capability-gated, not concrete-type-gated):
```csharp
var buffer = await _bufferFactory.CreateAsync(routeId, policy, ct);
var replayBuffer = buffer as IReplayRouteBuffer;
if (replayAwareSink is null)
{
    if (replayBuffer?.IsReplayTrackingEnabled == true)                 // B2-B: fail-closed downgrade guard
        throw ReplayRouteConfigurationException.AutomaticDowngradeNotAllowed(routeId);
    // ordinary route — unchanged legacy path
}
else
{
    if (replayBuffer is null)
        throw ReplayRouteConfigurationException.BufferNotReplayCapable(routeId);
    var activation = await replayBuffer.ActivateReplayAsync(routeId, replayAwareSink.InstanceId, ct);
    var context = new ReplayRouteContext(
        replayBuffer, replayAwareSink, replayAwareSink.InstanceId,
        activation.Generation, activation.BoundaryProvider, activation.SessionStateProvider);
}
```
`ReplayRouteContext` carries the providers + the typed generation from the activation result.

## C1 (correction) — the fixed generation is the PERSISTED value, not necessarily 0

Supersedes v3 R3's "activation generation (0)" wording. The route is fixed at **the persisted
generation returned by `ActivateReplayAsync`, whatever its value** — a reopened DB may legitimately
carry a non-zero generation even though K1.3 never advances it. The intake caches the RETURNED typed
`RouteSchemaGeneration` and never assumes 0. (Test 26.)

## C2 (correction) — a concrete Core reasoned-stop seam for config replacement

Supersedes v3.1 A4's "coordinator signals the driver" (not an API). Lock an additive internal seam:
```csharp
internal ValueTask StopRouteAsync(string routeId, ReplaySessionEndReason endReason, CancellationToken ct);
```
Explicit ordered teardown for a config replace:
```
reload coordinator classifies the replacement
RoutingEngine.StopRouteAsync(routeId, ConfigurationReplaced)  — AWAITED
    worker stops accepting rebirth requests
    driver calls EndSessionAsync(ConfigurationReplaced) exactly once
    driver completes
only THEN: SinkSupervisor.RestartAsync(...) / route re-registration
```
The end reason is threaded from the coordinator — NEVER inferred from a bare cancellation token.
**Replacement cases (resolves the v3 tension the reviewer flagged):**
- **Same stable sink id + schema-equivalent:** the same `routeId` may be stop→started (reuses the DB).
- **Changed sink id OR material schema change:** the same replay DB may NOT be reused — the persisted
  `replay_sink_id` mismatch guard (B1) fails a changed sink id closed, and a material change requires a
  new `routeId` (B1 invariant). v3's "a changed replay-sink identity is handled by full stop/start" is
  **superseded** — a changed sink id needs a new `routeId`, not a same-DB restart.

## C3 (correction) — first-observed-metric happens-before rule

Adds to v3.1 A2 one ordering guarantee so an async rebirth request can't race the driver's queue
check: **the adapter must await `RequestRebirthAsync` RETURNING (request accepted into the host queue)
BEFORE its `PublishAsync` returns the not-full-success result.** That gives the driver a reliable
happens-before: request-queued → publish-returns-not-full → driver-consumes-pending-rebirth →
retry-deferred-until-rebirth. Driver rule (protocol-neutral; the Sparkplug adapter later owns detecting
the absent metric):
```csharp
if (!IsFullSuccess(result))                    // Success && Accepted==Count && Rejected==0
{
    if (_rebirthQueue.TryTakeCurrent(out var request))
        await ProcessRebirthAsync(request, ct);
    else
        await ApplyPublishRetryPolicyAsync(ct);
    // ALWAYS re-dequeue; never retain or ack the failed subrange.
}
```

## C4 (correction) — A4 supersession scope + End-failure must not abort reverse-phase cleanup

- **A4 (v3.1) supersedes more v3 text than it named:** it supersedes R4's "guard-only" implication,
  R5 slice-5 Host scope, R6's Host-touch description, and R7's hot-replacement summary — the Host-side
  change is the coordinator guard PLUS the ordered `StopRouteAsync(ConfigurationReplaced)`-before-restart
  teardown (C2), not a guard alone. (Still contained; sink ownership stays in the Host.)
- **End-failure isolation (approved-item note):** the worker must CATCH and REPORT an `EndSessionAsync`
  failure such that `RoutingEngine.StopAllAsync` still completes and the Host proceeds to sink shutdown.
  An End failure must never abort reverse-phase cleanup (it would strand the Host stop sequence).

## Slice-1 entry gate — re-confirmed against v3 + v3.1 + v3.2

| # | Gate item | Where locked |
|---|-----------|--------------|
| 1 | Material route-schema replacement cannot reuse the same replay store | **B1 (Option C)** — invariant + identity guards; enforcement deferred to the material-schema milestone |
| 2 | First-observed metric forces rebirth before successful publish/ack | v3.1 A2 + **C3** happens-before |
| 3 | `AdvanceGenerationAsync` absent from the K1.3 route capability | **B2** interface (also removes v3 R3's "exposed-but-unused" wording) |
| 4 | Config replacement reaches `EndSessionAsync(ConfigurationReplaced)` before Host sink restart | v3.1 A4 + **C2** concrete `StopRouteAsync` seam |
| 5 | Activation is the final fallible registration op before publishing the route | v3 R-ledger 7 |
| 6 | No automatic downgrade from an enabled replay DB to legacy enqueue | **B2-B** `IsReplayTrackingEnabled` registration guard |

## Test plan — final (supersedes test 21; adds 25–31)

- **21 REMOVED.** ~~material schema change cannot reuse the store~~ (not runtime-detectable in K1.3;
  it's the deferred milestone's test).
- 25 — an already replay-enabled DB configured with an ordinary sink is REJECTED at registration
  (downgrade guard) before the worker / legacy enqueue starts.
- 26 — activation returns and intake uses a persisted **non-zero** generation (no gen-0 assumption).
- 27 — provider references are unavailable before activation and are returned atomically WITH successful
  activation.
- 28 — a rebirth-triggering adapter awaits host acceptance before returning its failed publish; the
  driver observes the queued request before retry.
- 29 — same-sink / schema-equivalent config replacement uses `ConfigurationReplaced` and may reuse the
  `routeId`.
- 30 — a changed sink identity cannot reuse the same replay DB, even through full stop→start (new
  `routeId` required).
- 31 — `EndSessionAsync` failure is reported but does NOT prevent the Host sink-stop phase.

## Reviewer-approved items (no change — recorded for the implementer)

Same-generation operational rebirth (no advance/drain/empty manifest); generation-advance removed from
the route capability; single serialized `ReplayRouteDriver` in Core; Host lifecycle bracket for
boot/shutdown; H/C barriers + strict full-subrange ack + re-dequeue-after-every-boundary; first-observed
metric as same-generation rebirth (with C3); StoreAndForward-only replay routes (reject None/InMemory;
tracked-append failure faults, never drops).
