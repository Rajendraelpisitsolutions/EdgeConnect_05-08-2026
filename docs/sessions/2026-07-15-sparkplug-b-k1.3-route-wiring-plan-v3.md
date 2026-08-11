# Sparkplug B — K1.3 route-wiring execution plan (v3, reality-checked / execution baseline)

**Status:** ✅ **Reality-check approved / execution baseline (frozen 2026-07-15).** Supersedes v2
(same date). **Unchanged design carries over from v2**; this doc restates only what the reality
check against the REAL code CONFIRMED, CHANGED, or DEFERRED, plus the two findings that reshape the
plan (the Host↔Core lifecycle bracket, and the fixed-generation simplification). No further
architectural review required before implementation.

**Date:** 2026-07-15 · **Branch:** `feat/sparkplug-b-k1.3-route-wiring`
**Plan-trail:** v1 → external review → v2 → **reality-check → v3 (frozen)**.

---

## R. Reality-check ledger (each item → status + evidence)

| # | Item | Status | Evidence (file:line) |
|---|------|--------|----------------------|
| 1 | `RouteDefinition.Sinks` shape | **Confirmed** | `IReadOnlyList<ISinkAdapter>` of RESOLVED adapters (`RouteDefinition.cs:39`). So `sink is IReplayAwareSinkAdapter` detection + `Sinks.Count == 1` are direct — no binding indirection. |
| 2 | Stable sink id == `ISinkAdapter.InstanceId` | **Confirmed** | `InstanceId` = "Stable identifier for this sink connector instance" (`ISinkAdapter.cs:53`); already the buffer cursor key (`RouteWorker.cs:88` register / `:297` dequeue / `:337` ack) and the `SinkSupervisor` registration key (Ordinal, `SinkSupervisor.cs:71`). It IS the configured cursor identity — B2's stable-id requirement is met by `InstanceId`. |
| 3 | `PublishResult` supports the strict-ack rule | **Confirmed** | `Success` / `AcceptedCount` / `RejectedCount` (`PublishResult.cs:27-33`). Base path advances on `Success` alone (`SinkPublisher.cs:97`); the replay driver uses the STRICTER `Success && Accepted==Count && Rejected==0` (B5). |
| 4 | `DequeueBatchAsync` gives contiguous ordered ranges | **Confirmed** | `WHERE sequence >= $c ORDER BY sequence LIMIT $n` (`SqliteRouteStore.cs` DequeueBatch); `BufferBatch.FirstSequence`/`LastSequence`. Barrier-splitting at H/C works directly on the returned range. |
| 5 | Generation advance empties the current-gen manifest | **Confirmed → validates B3** | `AdvanceGenerationAsync` writes only `meta.current_schema_generation`; `CaptureBirthStateAsync`/`ReadCurrentGenerationManifest` filter `WHERE schema_generation = current` (K1.2d, merged). So advance-then-capture = EMPTY birth. Generation-changing rebirth stays DEFERRED. |
| 6 | Where sink `StartAsync`/`StopAsync` are owned | **CHANGED (reshapes plan — favorable)** | The **Host `SinkSupervisor`** owns `Initialize`/`Start`/`Stop`/`Dispose` + hot-reload `Add`/`Remove`/`Restart` (`SinkSupervisor.cs:386/402`); Core only `PublishAsync`. AND `HostStartup` phase order already BRACKETS the replay lifecycle — see R4. |
| 7 | Can any registration step fail AFTER the activation commit? | **Confirmed safe** | `RoutingEngine.RegisterRouteAsync` creates the buffer (`:86`), then publishes to `_routes` under `_gate` (`:99-110`). Activation slots in after buffer-create + all validation, immediately before the `_routes` publish — nothing fallible remains after it. B2's commit boundary is realizable here. |
| 8 | Config-replacement path / `ConfigurationReplaced` reason | **CHANGED → scoped (R5)** | Config replace = `SinkSupervisor.RestartAsync` (Stop old + Start new) driven by the M.P2.2 hot-reload coordinator (Host), NOT the phase order; `RouteLifecycleManager` has no `ConfigurationReplaced` concept. K1.3 restricts hot-replace of a replay sink — see R5. |

## R2. KEY FINDING — the Host↔Core lifecycle bracket is already correct (no Host change for boot/shutdown)

`HostStartup` runs phases in numeric order on start and strict reverse on stop (`HostStartup.cs:16`):
- **Start:** `RegisterRouteAsync` (Core) → `sinkSupervisor.StartAsync` (Host — calls
  `ISinkAdapter.StartAsync`, `:278`) → `routingEngine.StartAllAsync` (Core — runs `RouteWorker.RunAsync`,
  `:282`). So the driver's `BeginReplaySessionAsync` (first action in the worker) **runs after** the
  sink is Started — matching the K1.1 contract ("Begin … always after StartAsync").
- **Stop (reverse):** `routingEngine.StopAllAsync` (Core — stops workers, `:332`) → …
  `sinkSupervisor.StopAsync` (Host — `ISinkAdapter.StopAsync`, `:338`). So the driver's
  `EndSessionAsync` (in the worker's stop/cleanup) **runs before** the sink is Stopped.

**Consequence:** the `ReplayRouteDriver` lives ENTIRELY in Core's `RouteWorker` (B4) and owns
Begin/Publish/CompleteCatchUp/Rebirth/End; **no Host change is needed for boot or shutdown.** One
implementation obligation: `RoutingEngine.StopAllAsync` must AWAIT the worker's `EndSessionAsync`
before it returns (the phase order then guarantees End completes before the Host stops the sink) —
i.e. the worker runs `EndSessionAsync(Stop)` synchronously in its shutdown/cleanup, not fire-and-forget.

## R3. KEY SIMPLIFICATION — K1.3 operates at a FIXED generation

Because B3 removes generation advance from K1.3, the route-schema generation is **fixed at the
activation generation (0) for the route's whole lifetime**. Therefore:
- The intake caches the activation generation ONCE and passes it to every `AppendTrackedAsync`; there
  is NO concurrent `AdvanceGenerationAsync` to race — the `expectedGeneration` CAS can never legitimately
  fail. A stale-generation append is thus a pure invariant guard (must never fire) → fault the route.
- `CaptureBirthStateAsync` / `CaptureCutoverAsync` always read the populated current-generation
  manifest (never the empty post-advance state) — so operational rebirth (B3-A) always re-births a
  POPULATED snapshot. Test 15 holds by construction.
- `AdvanceGenerationAsync` on `IReplayRouteBuffer` stays present but **UNUSED in K1.3** (reserved for
  the deferred material-schema milestone). It is exposed on the capability for that future use only;
  K1.3 never calls it.

## R4. CHANGED — config-replacement of a replay sink is RESTRICTED in K1.3

The `ConfigurationReplaced` teardown of a live replay session would require the Host hot-reload
coordinator (`SinkSupervisor.RestartAsync`) to coordinate with Core's route driver (End the session
before the sink's `StopAsync`, then re-register/re-birth) — a Host↔Core dance the phase order does
NOT provide. **v3 decision (locked):** K1.3 does NOT implement hot-replace of a replay sink. A config
apply that changes a replay route's sink identity/config requires a **full route stop→start** (which
goes through the phase-order bracket, giving a clean `EndSessionAsync(Stop)` → new
`BeginReplaySessionAsync`). The coordinator must **reject/flag** a hot `RestartAsync` of a
replay-aware sink (fail closed, do not silently downgrade — B2). `ReplaySessionEndReason.Configuration
Replaced` is therefore **wired but only reachable via the explicit route-level replace path**, not
the sink hot-swap path, in K1.3. Full hot-replace coordination is a named follow-up. *(This touches
the Host coordinator only to add the reject/flag guard — a small, contained Host change; everything
else stays in Core.)*

## R5. Confirmed decomposition (v2 §4, with R-findings) — the execution slices

1. **`IReplayRouteBuffer` capability + activation-at-commit + `ReplayRouteContext`.** Capability on
   `SqliteBuffer` (delegating to the owner + the K1.2d handle); `RoutingEngine.RegisterRouteAsync`
   detects a replay route (single `IReplayAwareSinkAdapter` sink + `StoreAndForward`), validates,
   activates at the commit boundary (R-ledger 7), caches the generation, stores `ReplayRouteContext`
   on `Route`. Reject `InMemory`/`None`/multi-sink/zero-sink; reject a hot `RestartAsync` of a replay
   sink in the coordinator (R4). Tests: capability-absence fails deterministically; pre-activation
   failure leaves the DB disabled; no legacy fallback post-activation; restart same-id ok / changed-id
   persisted-mismatch.
2. **Tracked-append intake at the fixed generation.** Branch `RunIntakePumpAsync` to
   `AppendTrackedAsync(points, cachedGeneration, ct)` for a replay route; no depth Drop/Spill; storage
   failure OR stale-generation → fault the route (R3). Tests: manifest maintained; non-replay legacy
   path unchanged; storage failure faults (no drop).
3. **`ReplayRouteDriver` — birth + phase loop with H/C barrier splitting + strict-ack.** Lives in
   `RouteWorker` (B4/R2); selected when the sink is `IReplayAwareSinkAdapter`. Begin → Replay/CatchUp/
   Live with barrier re-dequeue and `PublishContext` (epoch/phase/split); strict-ack (B5). Tests:
   empty-route birth→Live without DATA; continuous intake still reaches Live via fixed H/C; H/C
   straddle split+ack; partial publish acks nothing.
4. **Same-generation operational rebirth + coalescing host + empty-route wake.** `IReplaySessionHost`
   (accept/coalesce current epoch, ignore stale, deterministic on unknown; returns on accept);
   `Task.WhenAny(bufferSignal, rebirthSignal, cancel, retryDelay)` so a rebirth wakes an empty Live
   route (B4); new H, candidate epoch, `RebirthAsync`, promote-on-success, failure per the table (B5);
   NO generation advance / drain (B3/R3). Tests: wake empty Live; coalesce duplicates; ignore stale;
   populated re-birth snapshot; failed rebirth keeps old epoch; intake continues while DATA paused.
5. **End-session (reason=Stop via the phase bracket, R2) + regressions.** Worker runs
   `EndSessionAsync(Stop)` synchronously in shutdown before returning from `StopAllAsync`; exactly
   once; `EndSessionAsync` failure still proceeds to Host `StopAsync` (B5). The restricted
   config-replace guard (R4). Full Core.Tests + Management.Tests; solution 0/0; diff-hygiene (no
   Sparkplug types in Core; the only Host touch is the coordinator reject-guard).

## R6. Definition of done (frozen)

A route with exactly one `IReplayAwareSinkAdapter` sink + `StoreAndForward`: is capability-gated
(non-capable buffer → deterministic config failure); activates replay tracking at the registration
commit boundary (a pre-activation failure leaves the DB disabled; no auto-downgrade); intake appends
via the tracked path at the fixed activation generation (storage/stale-gen failure faults the route,
never drops); the `ReplayRouteDriver` drives Begin (as-of-H) → Replay → CatchUp (at C) → Live with
correct `PublishContext` phase/epoch/**barrier-split** and **strict-ack** on every subrange; a
host-requested **same-generation** rebirth wakes even an empty Live route, re-births a POPULATED
snapshot at a new H, and promotes the epoch only on success; `EndSessionAsync(Stop)` runs exactly once
before the Host stops the sink. Non-replay routes are byte-for-byte unchanged. Validated end-to-end
with a FAKE replay-aware sink (no Sparkplug types). The 20 v2 tests pass; Core.Tests + Management.Tests
green; solution 0/0.

## R7. Deferred / OUT (frozen)

- **Generation-changing (material schema) rebirth** — needs an authoritative new-generation manifest
  seed Core lacks today (B3-B). `AdvanceGenerationAsync` stays exposed-but-unused. Named follow-up.
- **Hot config-replace of a replay sink** (Host coordinator ↔ Core driver dance) — restricted to a
  route stop→start in K1.3 (R4). Named follow-up.
- `Sinks.SparkplugB` (K2/K3), licensing/config/DI (K4), Studio wizard (K5); K1.2e route-store
  finalization (parallel, before production enablement); in-memory O-C providers (Q7).

## R8. Residual assumptions to confirm at first build (low-risk)

- `RoutingEngine.StopAllAsync` awaits each worker's completion (so `EndSessionAsync` completes before
  the Host's `sinkSupervisor.StopAsync`) — confirm and, if needed, ensure the worker runs
  `EndSessionAsync` in its shutdown cleanup rather than fire-and-forget.
- The M.P2.2 hot-reload coordinator is the correct place to add the "reject hot-replace of a replay
  sink" guard (R4) — confirm the coordinator sees the sink adapter type.
- Exposing `AppendTrackedAsync` + `SessionStateProvider`/`BoundaryProvider` on `SqliteBuffer` via
  `IReplayRouteBuffer` composes cleanly with the K1.2d handle (the façade already delegates to the
  owner + exposes `GetCapabilityHandle`/`ActivateReplayStateTrackingAsync`).
