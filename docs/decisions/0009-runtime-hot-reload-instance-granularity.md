# ADR-0009: Runtime hot-reload is per-instance, route-bracketed, stop-then-start

**Status:** Implemented (2026-05-16)
**Date:** 2026-05-16
**Milestone:** M.P2.2
**Verification:** `docs/smoke/mp22-hot-reload.md` (manual smoke against the demo gateway), plus 24 automated tests across phases 1-3 (queue / coordinator / API / panel view-model).

## Context

After M.2a (`POST /api/v1/config/drafts/{id}/apply`) the gateway
rewrites `current.json` and emits `IConfigurationManager.CurrentChanged`,
but no host subscriber exists. The boundary is
`Core/Configuration/ConfigurationManager.cs:397` — past that line the
runtime still uses the boot-time `GatewayConfiguration`. Today,
operators must restart the gateway to pick up any configuration change.

M.P2.2 closes that loop: a `RuntimeReloadCoordinator` in the host
subscribes to `CurrentChanged`, computes a diff against the running
state, and drives supervisors + the routing engine to converge on the
new config — without a process restart.

This ADR locks the architectural shape of the reconciliation:
granularity, lifecycle ordering, modify semantics, and failure policy.
The companion session doc `docs/sessions/2026-05-16-mp22-kickoff.md`
holds the full milestone plan, exit criteria, and out-of-scope list.

## Decision

Four locks govern M.P2.2 reconciliation. The fifth (no auto-rollback)
is a reinforcement of ADR-0005 in the new context.

### 1. Per-instance reconciliation granularity

The coordinator computes a `ConfigurationReloadPlan` of `Add | Remove |
Restart` operations keyed by `(EntityKind, EntityId)`. Per-route
reconciliation is too coarse (modifying one tag would churn the route's
sinks); per-field is too fine (forces protocol-specific delta logic).

### 2. Routes are the active data-movement boundary

For both Remove and Restart, **stop affected routes first**, then stop
removed-or-now-unreferenced sources, then stop removed-or-now-
unreferenced destinations. For Add and Restart, **start sources first**,
then destinations, then routes — exact inverse.

Full order:

```
Remove / Restart (teardown):
  1. Stop affected routes
  2. Stop removed or now-unreferenced sources
  3. Stop removed or now-unreferenced destinations

Add / Restart (bring-up):
  4. Start sources (bounded channel buffers any early points)
  5. Start destinations
  6. Start routes (wires source intake → fanout → sinks; dispatch begins)
```

A sink referenced by N routes (per the M.P2.1 phase 3b
`SinkListItemDto.RouteIds` model) is stopped only when N drops to
zero — modifying one of N routes leaves the sink running.

### 3. Modify means stop-then-start in v1

Adapters today expose `InitializeAsync` + `StartAsync` + `StopAsync`;
there is no `ReconfigureAsync` path. Any `Modified` operation in the
plan resolves to `Restart` = `Remove(oldConfig)` + `Add(newConfig)`.
No protocol-specific live-reconfigure path lands in this milestone.

A future `ITryReconfigureLive` adapter opt-in may be added if a real
operational pain emerges (e.g., MQTT broker connection churn becomes
costly for a customer); it is explicitly deferred.

### 4. Reconcile failure registers a fault, never rolls back the apply

Per ADR-0005, **config is operator intent; faults are runtime
observation.** If a reconciliation step fails (adapter init throws,
device unreachable, etc.):

- The fault is registered in `IConfigurationFaultRegistry`
  (`actor="system"`, audit chain entry per ADR-0006).
- The instance appears as `Faulted` in the Studio (precedence per
  ADR-0007 unchanged).
- The coordinator continues with remaining operations.
- The apply itself stays committed — `current.json` reflects what the
  operator authored. The operator decides whether to rollback,
  adjust, or accept the partial outcome.

No "apply only if all runtime starts succeed" mode in v1. If that
policy becomes needed (regulated environments, batch-config tools),
it lands as an explicit flag on `POST /apply` in a later milestone.

## Reasoning

1. **Routes-first stop matches the data-flow direction.** The route
   worker owns the buffer-to-fanout dispatch. Stopping it first prevents
   new points from entering a path that's about to disappear; sources
   and sinks then tear down cleanly with no in-flight dispatcher
   reaching for them. Stopping a source first (the alternative) leaves
   the route worker live with a half-dead intake — a more error-prone
   shape.

2. **Start order is the inverse.** Sources can write to their bounded
   intake channels before a route exists (the channel IS the early
   buffer); destinations are passive consumers waiting for fanout. The
   route start is the moment data actually flows, so it goes last —
   when everything is in place to receive.

3. **Modify-as-restart keeps the milestone small.** Building a generic
   `ReconfigureAsync` across 6 protocol adapters multiplies the surface
   ~6x and forces per-protocol decisions (does Modbus need to redial?
   does MQTT need to resubscribe?). Stop-then-start is uniformly safe
   — the cost is a brief downtime per modified instance, mitigated by
   store-and-forward holding points across the gap.

4. **No-rollback preserves operator intent semantics.** ADR-0005 is
   built on the separation: `current.json` IS the operator's expressed
   will. Auto-rolling back on runtime failure would erase that will
   based on an environmental condition (device down, network flap)
   that may resolve on its own. The audit chain + fault registry give
   the operator the information to decide; the system does not decide
   for them.

5. **Three-layer defence still holds.** Per ADR-0003, validation runs
   at the wizard, the merger, and Core startup. M.P2.2 adds a fourth
   *runtime* observation layer (the coordinator) that registers faults
   without crashing. The earlier layers prevent most bad configs from
   ever reaching the coordinator; the coordinator catches what slips
   through and surfaces it visibly.

## Consequences

- **`IRoutingEngine` grows `UnregisterRouteAsync(string routeId, …)`.**
  Stops the route if running, disposes its buffer + dispatcher + worker,
  removes it from the engine. Idempotent on unknown id.

- **`SourceSupervisor` and `SinkSupervisor` grow per-instance lifecycle
  methods:** `AddAsync(reg, ct)`, `RemoveAsync(id, ct)`, `RestartAsync(
  newReg, ct)`. Internal state moves from a one-shot construct-and-run
  shape to a dictionary keyed by instance id with per-instance CTS.

- **New `RuntimeReloadCoordinator` in the host project.** Subscribes to
  `IConfigurationManager.CurrentChanged`, executes the plan on a single-
  flight `SemaphoreSlim(1,1)` distinct from `ConfigurationManager._mutex`
  (so device I/O doesn't block subsequent applies).

- **Apply response gains `ReloadOutcomeDto` block.** Lists
  `AppliedInstances`, `RestartedInstances`, `FaultedInstances` so the
  Studio's Config page can show what happened. Nullable for the gateway-
  settings-only case where no runtime change occurred. The 200 OK on
  Apply continues to mean "your config is durable" — the reload outcome
  is a separate observation.

- **Audit chain consumes no new action values.** Re-use `Applied = 2`
  (the apply itself) and `RuntimeConfigurationFault = 4` (reconcile-time
  faults). ADR-0006 already covers the shape.

- **ADR-0007 display precedence is unchanged.** Reconcile faults flow
  through the same `IConfigurationFaultRegistry`; the existing
  `Disabled > Faulted > live > Configured / Not running > Configured`
  precedence covers the new case for free.

- **Studio "rolling apply preview"** (showing which instances will
  restart before the operator hits Apply) is explicitly deferred to a
  later wizard milestone. M.P2.2 surfaces post-apply outcome only.

## References

- Implementation: M.P2.2 phase 1 / 2 / 3 (see session doc
  `docs/sessions/2026-05-16-mp22-kickoff.md`)
- ADR-0003 (three-layer defence) — the coordinator is the fourth,
  runtime-observation layer
- ADR-0004 (fail-soft Core startup) — the boot-time mechanism this
  ADR extends into the live runtime
- ADR-0005 (faults are runtime state) — the substrate; reconcile
  failures are observations, not new intents
- ADR-0006 (system-actor audit entries) — re-used unchanged
- ADR-0007 (Disabled > Faulted > live precedence) — extends to
  reconcile-faulted instances with no precedence change
- Review feedback: ChatGPT review pass, 2026-05-16, explicitly
  inverted my original "source-first" stop order in favour of
  "routes-first" — locked here.
