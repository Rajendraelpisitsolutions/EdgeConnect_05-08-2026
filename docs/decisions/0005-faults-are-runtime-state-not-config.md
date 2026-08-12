# ADR-0005: Faults are runtime state, never persisted to config

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** M.P2.1

## Context

ADR-0004 introduced fail-soft startup. The new
`IConfigurationFaultRegistry` records cross-record validation
failures observed at boot (and later, M.P2.2, at hot-reload). The
review question: should faults be persisted to disk so they
survive a gateway restart?

## Decision

`IConfigurationFaultRegistry` is **in-memory only**, **never
persisted into config or to any disk store**. Properties locked
by the design:

- **In-memory** — `ConcurrentDictionary` backed; lost on process
  restart, repopulated on next boot.
- **Runtime-facing** — reflects "what failed to come up," never
  "what the operator intended."
- **Keyed by (Kind, InstanceId)** — re-`Register` for the same
  key REPLACES the prior entry (matches operational model: a
  re-init failure supersedes the prior failure record).
- **Cleared on successful re-init** — supervisors call
  `ClearFor(kind, instanceId)` when an instance transitions to a
  healthy state. Stale fault entries are a bug.
- **Read-only from Studio/API** — `GET /api/v1/diagnostics/
  configuration-faults` surfaces the registry; no write endpoint.

The audit chain (ADR-0006) is the **durable record**. The
registry is the **live view**.

## Reasoning

1. **Faults reflect observation, not desire.** A fault is a fact
   the runtime discovered about the config. The config itself is
   what the operator wants. Mixing them — e.g., adding a
   `faulted: true` field to `gateway.json` — would conflate intent
   with observation and confuse the apply/rollback flow.

2. **Persistence would create stale-fault hazards.** If a fault
   were persisted, what happens when the operator fixes the
   config? Reset the fault on apply? On boot? On rollback to a
   version that had the fault? Every answer is awkward. In-memory
   semantics make this trivial: faults rebuild on every boot from
   the current config.

3. **Audit chain already gives durability where it matters.** The
   forensic question "did this gateway boot with faults yesterday?"
   is answered by the audit chain. The live question "what's
   currently broken?" is answered by the registry. Different
   questions; separate stores.

4. **It generalises to M.P2.2 hot-reload.** When an instance
   re-registers successfully mid-run, its fault entry clears
   automatically — exactly the right behavior. Persisted faults
   would require explicit "fault dismissed" mechanics; in-memory
   semantics get this for free.

## Consequences

- `IConfigurationFaultRegistry` is a Core singleton in
  `Core.Diagnostics`; no persistence dependency.
- The audit chain (`ConfigurationAuditLog`) absorbs the durable
  side: every fault observed gets one
  `RuntimeConfigurationFault` entry written via
  `IConfigurationManager.AppendRuntimeFaultAsync`.
- Studio's `/diagnostics` Configuration-faults panel reads the
  registry directly. After a gateway restart with fixed config,
  the panel goes empty; the audit log retains the history of
  past faults.
- M.P2.2's hot-reload coordinator clears registry entries when
  re-init succeeds — same in-memory mechanics extend cleanly.

## References

- Implementation: `IConfigurationFaultRegistry` interface +
  `ConfigurationFaultRegistry` class (M.P2.1 phase 1, `6d85f38`)
- ADR-0006 (system-actor audit entries) — the durable side
- ADR-0004 (fail-soft startup) — the producer side
