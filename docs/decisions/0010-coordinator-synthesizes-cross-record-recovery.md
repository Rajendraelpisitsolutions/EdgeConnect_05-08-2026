# ADR-0010: Coordinator synthesizes Add actions for cross-record validation flips

**Status:** Accepted (2026-05-17)
**Date:** 2026-05-17
**Milestone:** M.P2.3
**Framing:** ADR-0010 extends ADR-0009 without modifying its diff-classifier purity guarantees.

## Context

After M.P2.1 (fail-soft startup), a configuration entity that fails cross-record validation at gateway boot — for example a source registered as `CONFIG.SOURCE_WITHOUT_ROUTE` by the protocol registration extensions — is:

- Recorded in `IConfigurationFaultRegistry` with the failure code.
- **Skipped from the supervisor / routing engine.** The entity is not in any runtime registry.

After M.P2.2 (hot-reload, ADR-0009), the `RuntimeReloadCoordinator` reconciles runtime state on every applied configuration. The coordinator drives off a `ConfigurationReloadPlan` emitted by `RuntimeReloadClassifier`. The classifier is a **pure function of `(newConfig, changes)`** — it emits per-instance actions only for entities whose own config changed in the diff.

This combination produces a stall when an operator fixes a cross-record validation issue without modifying the entity itself. Example: source `Modbus-4` faults at boot with `CONFIG.SOURCE_WITHOUT_ROUTE`. Operator applies a config that adds the missing route. The diff contains `Added(Route, ...)`; it does NOT contain any change for the source. The classifier therefore emits no source action. The coordinator never re-attempts `Modbus-4`, so the original fault stays and the newly-added route then re-faults with `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE` when `BuildOne` can't find the source in the supervisor.

M.P2.2 phase 3 documented this in `docs/ops-runbook.md` §5 with a workaround (touch any field on the source to trigger a `Modified → Restart` action through the classifier). ADR-0009 noted the fix as deferred.

## Decision

The `RuntimeReloadCoordinator` runs a **recovery-synthesis pre-pass** before each reconcile. For each fault in `IConfigurationFaultRegistry` whose code is in the in-scope set, the coordinator inline-checks whether the entity's cross-record validity has flipped against the new configuration AND whether the entity is not already active in its runtime registry / supervisor. Where both hold, the coordinator emits a synthetic `Add` action and routes it through the existing A1-A3 / B1-B3 phases.

### In-scope fault codes

Confirmed by reading the M.P2.1 cross-record fault emission sites on 2026-05-17:

| Code | Emitted by | Source synthesis target |
|---|---|---|
| `CONFIG.SOURCE_WITHOUT_ROUTE` | Focas2 / ModbusTcp / MTConnect / S7 registration extensions | `Add(Source, X)` if a route references X in `newConfig` |
| `CONFIG.SINK_WITHOUT_ROUTE` | Mqtt / OpcUaServer registration extensions | `Add(Sink, X)` if a route references X in `newConfig` |
| `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE` | `RouteDefinitionFactory.BuildOne` | `Add(Route, R)` if R's source + all sinks now exist and are enabled in `newConfig` |
| `CONFIG.ROUTE_REFERENCES_MISSING_SINK` | `RouteDefinitionFactory.BuildOne` | `Add(Route, R)` (same as above) |

### Synthesis precondition (load-bearing invariant)

Synthesize `Add(kind, entityId)` if and only if **all** of:

1. **Fault exists** in the registry with one of the four in-scope codes.
2. **Cross-record validity now passes** against `newConfig` per the inline predicate.
3. **Entity still exists** in `newConfig` (operator hasn't removed it).
4. **Entity is NOT already active** in the relevant runtime registry / supervisor (`_sourceSupervisor`, `_sinkSupervisor`, or `_routingEngine`).
5. **No classifier action already targets** `(kind, entityId)` — dedup by key.

### Ephemeral effective-actions list (Locked H, M.P2.3 plan v2)

The synthesized list is **separate from `plan.Actions`**. The coordinator computes a local:

```
effectiveActions = plan.Actions ∪ synthesized
```

and drives `EnumerateActions(effectiveActions, kind, teardown)` plus `ComputeSinksToStop(effectiveActions, newConfig)` off that. `ConfigurationReloadPlan.Actions` is never mutated. This preserves the classifier's pristine output for downstream inspection, debugging, logging, and audit-chain replay.

### Synthesis runs after stale-skip, before NoOp check

```
ReconcileAsync(e):
  1. Stale-version skip → EnqueueSkipped, return
  2. stopwatch.Start()
  3. plan = RuntimeReloadClassifier.Classify(...)        # pristine
  4. synthesized = ComputeStartupSkipRecoveryActions(...)
  5. effectiveActions = plan.Actions ∪ synthesized        # ephemeral
  6. if effectiveActions.Count == 0 → EnqueueCompleted NoOp, return
  7. A1/A2/A3 teardown over effectiveActions
  8. B1/B2/B3 bring-up over effectiveActions
  9. EnqueueCompleted with populated outcome
```

## Reasoning

1. **Classifier purity preserved.** The classifier remains a pure function of `(newConfig, changes)`. Teaching it about the fault registry was the rejected alternative — would couple replay/debugging to registry state, make `Modified` no longer mean "operator changed this entity", and increase classifier test coupling. ADR-0009's locked separation between "config intent" (classifier-driven) and "runtime projection" (coordinator-driven) is reinforced rather than blurred.

2. **Inline predicates over invoking startup validators.** M.P2.1 startup validators are boot-oriented, broader in scope, fault-producing, and potentially side-effectful. The coordinator's narrow runtime use case (just: "is this entity cross-record-valid against newConfig right now?") is better served by ~10-line inline predicates against `newConfig` records.

3. **`Add` over `Restart` for synthesis.** The skipped entity was never running. `Restart` semantically means "stop the running one + start fresh"; for a never-started entity the teardown half is a no-op and the wording is misleading. `Add` is correct.

4. **Synthesis enters the existing phase ordering.** Synthesized actions are filtered through `EnumerateActions(effectiveActions, kind, teardown)` exactly like classifier-emitted ones. No second orchestration engine emerges; B1-B2-B3 ordering remains authoritative.

5. **Identical outcome surface.** Synthesized actions appear in `AppliedInstances` on success and `FaultedInstances` on failure, exactly like classifier-emitted ones. No `RecoveredInstances` or `SynthesizedActions` field. The operator cares about "did it come up", not how the coordinator internally decided to try.

6. **Ephemeral effectiveActions over plan mutation.** Per Locked H of the M.P2.3 plan v2 (ChatGPT review): folding synthesized actions into `plan.Actions` loses the distinction between "what the operator changed" and "what the coordinator recovered". A future audit-chain debug pass benefits from being able to inspect the pristine classifier output.

## Consequences

- **`EnumerateActions` private signature change.** Was `EnumerateActions(ConfigurationReloadPlan, ConfigurationEntityKind, bool)`. Now `EnumerateActions(IReadOnlyList<ReloadAction>, ConfigurationEntityKind, bool)`. Mechanical refactor; internal to `RuntimeReloadCoordinator`. `ComputeSinksToStop` gets the same treatment.

- **`RuntimeReloadCoordinator` gains private methods** `ComputeStartupSkipRecoveryActions`, `TrySynthesizeSourceAdd`, `TrySynthesizeSinkAdd`, `TrySynthesizeRouteAdd`, plus the three predicates `IsSourceCrossRecordValidNow`, `IsSinkCrossRecordValidNow`, `IsRouteCrossRecordValidNow`, plus an enum mapper `MapFaultKindToEntityKind` (since `ConfigurationFaultKind` and `ConfigurationEntityKind` are distinct enums).

- **Operator workaround documented in M.P2.2 phase 3 is removed.** `docs/ops-runbook.md` §5 entry on "Hot-reload + startup-skipped sources" is rewritten to note that the coordinator now handles this automatically for the four in-scope cross-record codes.

- **No reload-outcome surface change.** `ReloadOutcomeDto` is unchanged. Synthesized actions flow through the existing Applied / Restarted / Faulted lists transparently.

- **No new audit actions.** The audit chain entries (`Applied`, `RuntimeConfigurationFault`) are unchanged. ADR-0006 still covers the shape.

- **No new public APIs.** The synthesis logic is private to the coordinator.

## Out-of-scope follow-ups

- **License-disabled instances.** License state doesn't change via Apply; license-blocked instances use a different signal channel. Not addressed by M.P2.3.

- **Faults emitted from non-cross-record sources** (e.g. adapter-ctor exceptions during the bind phase, license-check failures). The synthesis pass strictly filters by the four in-scope error codes; other faults are not recovery candidates.

- **Fault TTL or independent auto-clear pass.** Not introduced. The existing `IConfigurationFaultRegistry.ClearFor` on successful re-init is still the only mechanism that removes entries.

## References

- ADR-0009 — Runtime hot-reload is per-instance, route-bracketed, stop-then-start (the original lock; M.P2.3 extends it).
- M.P2.3 plan: `docs/sessions/2026-05-17-mp23-plan.md` (v2 locked + Step-1 reality-check amendment).
- M.P2.2 phase 3 plan: `docs/sessions/2026-05-16-mp22-phase3-plan.md` (where the workaround was first documented).
- M.P2.2 ops-runbook: `docs/ops-runbook.md` §5 entry on "Hot-reload + startup-skipped sources" (now rewritten in M.P2.3).
- Review feedback: ChatGPT review pass on M.P2.3 plan v1, 2026-05-17 — required the ephemeral-effectiveActions shape (Locked H) and elevated the "not already active in runtime registry" precondition to a load-bearing invariant (Locked I).
