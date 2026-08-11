# Sparkplug B — K1.3 route-wiring execution plan (v1, DRAFT for external review)

**Status:** 🟡 **v1 draft — NOT frozen.** Goes to external review → v2 → reality-check → v3 (frozen)
before any code, per the planning cadence. This v1 is written against the REAL code on `master`
(post-#184), with file:line integration points, so the review pass argues against facts.

**Date:** 2026-07-15 · **Branch:** `feat/sparkplug-b-k1.3-route-wiring` (cut from `master` `c69cdb7`)
**Milestone:** K1.3 — the **Core-side route plumbing** that lets ANY `IReplayAwareSinkAdapter`
drive the replay-then-rebirth lifecycle over a replay-enabled `SqliteRouteStore`, using the K1.2d
capability handle. **Not** the Sparkplug assembly itself (that is K2/K3).

**Governing locks (do not violate):** Core stays protocol-agnostic (CLAUDE.md §3.1 — no Sparkplug
types in `ElpisEdgeConnect.Core`); the locked `IMessageBuffer` / `ISinkAdapter` base contracts are
NOT amended (additive only); AI/data-path rules N/A. Delivery lock #12: Sparkplug B is a
`LocalTransport` acknowledgement boundary. Replay lifecycle = ADR-0036 (replay → rebirth, epoch
gating, Rules 4/6/7); value mapping = ADR-0035 R5.

---

## 1. What K1.3 consumes (K1.2d, merged) — the fixed surface

- **Capability handle:** `SqliteBuffer.GetCapabilityHandle()` → `SqliteRouteStoreHandle(Buffer,
  IReplayBoundaryProvider?, IReplaySessionStateProvider?)`; `SqliteBuffer.ActivateReplayStateTrackingAsync(
  routeId, replaySinkId, ct)`. Providers non-null iff tracking enabled.
- **Birth/cutover capture:** `IReplaySessionStateProvider.CaptureBirthStateAsync(routeId, sinkId, ct)`
  → `ReplaySessionStartState` (boundary H + birth snapshot); `CaptureCutoverAsync(routeId, ct)` →
  `ReplaySessionCutoverState` (cutoff C + snapshot). Coherent, single-tx, decode off-lock.
- **Lifecycle contract (K1.1):** `IReplayAwareSinkAdapter` (`Adapters/IReplayAwareSinkAdapter.cs`):
  `BeginReplaySessionAsync` (birth) → `PublishAsync(points, PublishContext, ct)` (phase-tagged) →
  `CompleteCatchUpAsync(ReplaySessionCutover)` → Live; `RebirthAsync`, `EndSessionAsync`.
- **Phase context (K1.1):** `PublishContext.Create(routeId, sessionId, epoch, phase, H, C?, first,
  last)` — validated two-watermark ranges (Replay `seq<H`; CatchUp `H≤seq<C`; Live `seq≥C`); Core
  must split boundary-straddling batches so each `PublishAsync` batch is a single phase.
- **Reverse handshake (K1.1):** `IReplaySessionHost.RequestRebirthAsync(RebirthRequest, ct)` —
  async, queued, non-reentrant, epoch-coalesced.
- **Session identity (K1.1):** `ReplaySessionId`, `ReplayEpochId`, `RouteSchemaGeneration`.
- **Tracked append + generation (K1.2b/c, internal on `SqliteRouteStore`):** `AppendAsync(points,
  expectedGeneration, ct)` → `AssignedSequenceRange` (atomic points + manifest + next_sequence);
  `AdvanceGenerationAsync(expectedCurrent, next, ct)` (CAS + drain fence); legacy `EnqueueAsync` is
  **rejected** on an enabled store (`RouteStoreLegacyAppendOnEnabledStore`).

## 2. Current route path (facts from `master`) — the integration points K1.3 changes

- **Composition:** `RoutingEngine.RegisterRouteAsync` (`RoutingEngine.cs:86`) calls
  `_bufferFactory.CreateAsync(routeId, policy, ct)` → `IMessageBuffer`, then
  `new Route(def, buffer, dispatcher, lifecycle)` (`:97`). `StartRouteAsync` (`:143`) builds
  `new RouteWorker(route, …)` and runs `RunAsync`.
- **Factory:** `IRouteBufferFactory.CreateAsync` (PUBLIC interface, `IRouteBufferFactory.cs:34`)
  returns only `IMessageBuffer`; `DefaultRouteBufferFactory.CreateAsync`
  (`DefaultRouteBufferFactory.cs:51`) resolves the buffer path, runs `MigrateLegacyBufferIfPresent`
  (`:128`), wires quarantine→`IRoutingEngineDiagnostics`, and returns a `SqliteBuffer` façade (for
  `StoreAndForward`) or `InMemoryBuffer`.
- **Intake pump:** `RouteWorker.RunIntakePumpAsync` (`RouteWorker.cs:112`) reads source → filter →
  transform → backpressure decision → **`_route.Buffer.EnqueueAsync(toEnqueue, ct)`** (`:224`) →
  `_dispatcher.NotifyAll()`. Backpressure (Pass/Spill/Drop) is depth-driven off `GetStatsAsync`.
- **Sink loop:** `RouteWorker.RunSinkLoopAsync` (`:281`) per sink: `DequeueBatchAsync` →
  `SinkPublisher.PublishWithRetryAsync` → `AckAsync`. `SinkPublisher` (`SinkPublisher.cs:36/78`) holds
  an `ISinkAdapter _sink` and calls the **context-free** `_sink.PublishAsync(batch, ct)`.
- **Route holds only `IMessageBuffer`** (`Route.cs:38`) — no handle, no tracked-append, no generation.

**Net:** three integration surfaces change for a replay route — (A) construction/activation +
handle propagation, (B) intake → tracked append, (C) sink loop → lifecycle driver. None touch the
locked base contracts.

## 3. Target architecture (replay route path)

A route is **replay-capable** when its single sink implements `IReplayAwareSinkAdapter` (Sparkplug B
is one-route/one-sink — sink plan §K4 cardinality). For such a route:

**(A) Construction + activation + handle.** At `RegisterRouteAsync`, when the route is
replay-capable and `StoreAndForward`, obtain the `(SqliteBuffer façade, SqliteRouteStoreHandle)`
pair through the factory (NOT by opening the owner directly), activate replay-state tracking with the
sink's `InstanceId` as the replay sink id, and store the handle on `Route`. A restart re-opens an
already-enabled store (activation is idempotent). The `Route` gains an optional
`SqliteRouteStoreHandle? ReplayHandle`.

**(B) Intake → tracked append.** For a replay-enabled route the intake pump must call the tracked
`AppendAsync(points, currentGeneration, ct)` instead of `EnqueueAsync` (which now throws). The
tracked path applies NO depth eviction (retention = replay-sink cursor + MaxAge, K1.2c), so the
depth-backpressure Pass/Spill/Drop model does not apply as-is — a replay route buffers all points
until the replay sink acks them. The intake must know the current generation and hold it coherently
against a concurrent generation advance.

**(C) Sink loop → replay lifecycle driver.** Replace the context-free publish+ack for a replay-aware
sink with a driver:
1. **Birth:** `CaptureBirthStateAsync(routeId, replaySinkId)` → `ReplaySessionStartState` (H +
   snapshot). Mint a new `ReplaySessionId` + initial `ReplayEpochId`; call
   `sink.BeginReplaySessionAsync(ReplaySessionStart.Create(id, epoch, routeId, state, host))`.
2. **Replay (`seq < H`):** dequeue → split at H → `PublishContext(Replay, H, C=null, first, last)` →
   `sink.PublishAsync(points, ctx)` → ack.
3. **Cutover:** when the replay-sink cursor reaches H, `CaptureCutoverAsync(routeId)` → C + snapshot;
   drain `[H, C)` as `CatchUp`; then `sink.CompleteCatchUpAsync(ReplaySessionCutover)` → Live.
4. **Live (`seq ≥ C`):** ongoing batches as `Live`.
5. **Rebirth:** on a `RebirthRequest` (host NCMD or schema change), Core pauses the route path,
   drains the replay sink to head, `AdvanceGenerationAsync`, captures a fresh birth state, calls
   `sink.RebirthAsync` (new epoch), resumes.
6. **End:** on stop / config replace, `sink.EndSessionAsync` before `ISinkAdapter.StopAsync`.

**(D) Host implementation.** Core supplies an `IReplaySessionHost` to the sink at birth;
`RequestRebirthAsync` enqueues a coalesced, epoch-gated rebirth the driver processes non-reentrantly.

**(E) Generation / schema-change ownership.** Core owns `AdvanceGenerationAsync`; a schema change is
signalled by the sink via `RebirthRequest(SchemaChange)` (the sink compares the fresh snapshot's
manifest metadata against its birth generation — ADR-0036 R7). Core does not sniff payloads.

## 4. Proposed decomposition (slices — refine in review/v2)

1. **Factory seam + Route handle propagation.** Additive replay-capable construction path
   (`(façade, handle)`); activate tracking; `Route.ReplayHandle`. Decide the `IRouteBufferFactory`
   shape (§6 Q1). Tests: replay route activates + handle present; non-replay unchanged; restart
   idempotent.
2. **Tracked-append intake.** Expose tracked `AppendAsync` on the façade (internal) + a generation
   source; branch `RunIntakePumpAsync` to the tracked path for a replay route; define the
   replay-route backpressure/retention model (no depth eviction). Tests: replay intake appends +
   manifest maintained; legacy path untouched for non-replay routes.
3. **Replay lifecycle driver — birth + replay + cutover + live.** The `ReplaySinkPublisher` (or a
   RouteWorker replay branch) driving 3(1)–3(4) with H/C batch splitting via `PublishContext`. Tests:
   empty-buffer birth; backlog replay; cutover to live; epoch/session on every batch; batch-splitting
   at H and C.
4. **Rebirth + host handshake + generation advance.** `IReplaySessionHost` impl; coalesced,
   non-reentrant, epoch-gated rebirth; drain-fence + `AdvanceGenerationAsync` + fresh birth +
   `RebirthAsync`. Tests: host-requested rebirth; schema-change rebirth; superseded-epoch request
   ignored; pause-DATA-during-birth.
5. **End-session + lifecycle integration + regressions.** `EndSessionAsync` before `StopAsync` (no
   double death); config-replace teardown; full Core.Tests + Management.Tests; diff hygiene.

## 5. Reality-check targets (for the v2→v3 pass)

- Confirm `def.Sinks` elements are `ISinkAdapter` so `sink is IReplayAwareSinkAdapter` detection is
  direct (verify against `RouteDefinition`/`SinkBinding`).
- Confirm the tracked `AppendAsync` semantics under the intake batch sizes (256) and the
  `MaxBatchSize` split; confirm no depth eviction is safe for an unbounded replay backlog (disk).
- Confirm the RouteWorker concurrency model can host the non-reentrant rebirth queue without
  deadlocking the intake vs sink loops (the writer mutex is held only inside capture/append).
- Confirm restart semantics: an enabled store reopened mid-session — birth re-capture from the
  persisted manifest + cursor is coherent (K1.2d capture already proven on reopen).
- Confirm generation-advance drain-fence interplay with a live intake (advance requires the replay
  sink at head; intake keeps appending — the pause/drain sequencing must be explicit).

## 6. Open questions — DECISIONS NEEDED (surface in the external review)

1. **`IRouteBufferFactory` shape.** `IRouteBufferFactory` is PUBLIC and stubbed in tests. Options:
   (a) add `CreateReplayCapableAsync(routeId, policy, replaySinkId, ct)` → `(IMessageBuffer,
   SqliteRouteStoreHandle)` to the interface (touches all impls/stubs); (b) keep the interface,
   have `RoutingEngine` obtain the handle from the concrete `SqliteBuffer` façade after
   `CreateAsync` (`buffer as SqliteBuffer` → `GetCapabilityHandle`) + activate, avoiding a public-API
   change; (c) a separate optional `IReplayRouteBufferFactory`. **Recommend (b)** — least public-seam
   churn, keeps the factory the sole constructor, matches "additive, don't amend locked contracts."
2. **Where does activation happen** — register vs first start? Register (before the worker) is
   cleaner (drained fresh store); confirm against config apply/rollback.
3. **Replay-route backpressure/retention.** With no depth eviction, an unbounded replay backlog is a
   disk-exhaustion risk. Policy: rely on MaxAge + the replay-sink cursor; do we need a disk-guard /
   route-fail on overflow? (Sparkplug B first customer is bounded, but the design should state it.)
4. **Tracked-append exposure.** Add an internal `AppendTrackedAsync` on `SqliteBuffer` + a generation
   accessor, or route the intake through the handle? Keep it off the public `IMessageBuffer`.
5. **Scope boundary vs K2/K3.** Confirm K1.3 = the Core route-side DRIVER (calls the
   `IReplayAwareSinkAdapter` lifecycle); the `Sinks.SparkplugB` actor IMPLEMENTING that interface is
   K3. A test-only fake replay-aware sink validates K1.3 end-to-end without any Sparkplug code.
6. **Sequencing vs K1.2e.** K1.2e (route-store corruption matrix + the honest under-lock-scan perf
   measurement) is still open. Does it gate K1.3, run in parallel, or follow? (K1.3 does not depend
   on it functionally.)
7. **In-memory O-C.** If any replay-aware route can use the in-memory buffer, the in-memory
   `IReplayBoundaryProvider`/`IReplaySessionStateProvider` (O-C, deferred) becomes a K1.3 dependency.
   Recommend: replay routes require `StoreAndForward` in v1; reject in-memory at config validation.

## 7. Risks

- **Highest:** the RouteWorker replay branch is cross-cutting and load-bearing (intake + sink loops +
  lifecycle + rebirth queue). A regression here stalls the data path. Mitigate with a fake
  replay-aware sink + deterministic phase/cutover tests before any Sparkplug code.
- **Parallel-dev collisions:** Sony (onboarding + EtherNet/IP) and Bhanu (TCP sink) touch routing.
  Rebase carefully; assume `DefaultRouteBufferFactory`/`RouteWorker` may move.
- **Unbounded replay backlog** on disk (no depth eviction on the tracked path) — see §6 Q3.
- **Generation/rebirth deadlock** if the rebirth drain-fence fights the live intake — sequencing must
  be explicit (§5).

## 8. Definition of done (target for v3)

A route whose sink is `IReplayAwareSinkAdapter` + `StoreAndForward`: activates replay tracking at
registration; the intake appends via the tracked path (manifest + next_sequence maintained); the
sink loop drives birth (as-of-H) → replay → cutover (C) → live with correct `PublishContext`
phase/epoch/split on every batch; a host- or schema-triggered rebirth advances the epoch after a
drain-fenced generation advance and fresh birth; `EndSessionAsync` runs once before `StopAsync`.
Non-replay routes are byte-for-byte unchanged. Validated end-to-end with a **fake** replay-aware
sink (no Sparkplug code). Full Core.Tests + Management.Tests green; solution 0/0; diff hygiene (no
Sparkplug types in Core).

## 9. Explicitly OUT of K1.3

The `Sinks.SparkplugB` assembly/actor + payload/topic/mappers/connection profile (K2/K3); licensing
+ config validation + DI triad (K4); Studio wizard (K5); K1.2e route-store finalization; the
in-memory O-C providers (unless §6 Q7 pulls them in). K1.3 ships the Core-neutral replay route driver
+ a fake replay-aware sink for coverage — nothing protocol-specific.
