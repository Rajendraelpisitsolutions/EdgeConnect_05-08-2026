# Shared source-generation foundation — Slice 0 specification

**Date:** 2026-06-25  
**Status:** Shared implementation contract  
**Consumers:** Diagnostic-strengthening v3 and runtime-reconfigure v2  
**Owner:** One Core/Host runtime DRI; mandatory review by both workstreams

## 1. Purpose

Provide one supervisor-owned generation primitive for a stable source slot so stop, reconfigure, and later recovery can revoke an adapter generation without allowing late work to affect the current runtime.

Slice 0 adds lifecycle correctness only. It does **not** add poll timeouts, automatic recovery, retry policy, adapter abort logic, liveness verdicts, or UI changes.

## 2. Identity and vocabulary

- **Source slot:** stable runtime identity and ingress for one configured source instance.
- **Runtime instance id:** unique id created at gateway-process startup.
- **Generation id:** unsigned 64-bit counter, monotonic within a source slot for the current runtime instance.
- **Generation key:** `(RuntimeInstanceId, SourceSlotId, GenerationId)`; this is the correlation identity written to snapshots and events.
- **Current / publish-authorized:** the only generation whose effects may commit to current runtime state.
- **Retired:** publish authority has been atomically revoked.
- **Quarantined:** retired work is isolated while bounded cleanup is attempted.
- **Orphaned:** quarantined work remains physically active after the cleanup deadline.

**Invariant:** each source slot has zero or one publish-authorized generation. Retired work may remain physically active, but it has no route, counter, callback, health, or error authority.

## 3. Immutable generation lease

A generation receives one immutable lease at construction. The lease contains its generation key and a reference to the owning slot gate. It never reads a mutable `CurrentGenerationId` field from the supervisor.

Every generation-originated execution path captures the lease, including poll/subscription work, intake publication, adapter callbacks, reconnect callbacks, health/error reporting, and source counters.

Authorization must be checked **at the side-effect commit point**, not by a prior `IsCurrent` check followed by an unprotected write.

## 4. Commit-point fencing

### 4.1 Data ingress

Each generation writes through a generation-scoped ingress writer. The stable source slot owns the commit into the route-facing ingress.

A point is accepted only when its lease is still current at commit/dequeue time. Retirement detaches the retired generation from the stable ingress before cancellation begins. Queued or late points from that generation are rejected or discarded and may update retired-generation diagnostics only.

### 4.2 Health, errors, counters, and callbacks

Current-state sinks accept `(generationKey, update)` and perform generation validation and state mutation under the same synchronization boundary. A retired generation may append a bounded terminal/history record, but cannot mutate current source state or lifetime totals intended to count accepted current-generation work.

No adapter receives an unfenced reference to a current-state sink.

## 5. Slot lifecycle

1. Allocate the next generation key.
2. Construct the adapter and all generation-scoped wrappers with the immutable lease.
3. Atomically authorize the generation for the slot.
4. Run the generation.
5. On stop/reconfigure/recovery request, atomically revoke publish authority and detach ingress **before** cancellation or disposal.
6. Request cancellation and adapter-specific cleanup.
7. Await cleanup for the caller-provided bounded deadline.
8. If cleanup completes, record the retired terminal outcome.
9. If cleanup exceeds the deadline, quarantine and observe the task; mark it orphaned once the deadline expires and account for it against source/process budgets.
10. Authorize a replacement only when the caller's replacement policy and adapter capability allow physical overlap; otherwise return a terminal escalation requiring operator action or controlled process restart.

A replacement must never be started merely because the old await timed out.

## 6. Required API responsibilities

The implementation may choose concrete names, but must expose these responsibilities:

- Allocate a generation key for a stable source slot.
- Create an immutable generation lease.
- Atomically authorize, replace, and retire a lease.
- Commit generation-scoped data and current-state updates.
- Observe retired tasks through completion/fault without letting them mutate current state.
- Expose cached generation snapshots and bounded history.
- Track per-source and process-wide retired, quarantined, and orphaned counts.
- Query adapter replacement capability/policy without embedding adapter-specific logic in the generic slot.

## 7. State and accounting policy

### Per-generation state

Start/end UTC, monotonic elapsed data, lifecycle state, terminal reason, cleanup result, accepted/rejected side-effect counts, and late completion/fault.

### Per-source lifetime state

Total generations, successful retirements, cleanup timeouts, quarantines, orphans, and accepted current-generation points/polls. These survive generation replacement for the lifetime of the source slot.

History is bounded. Process restart starts a new runtime instance id, preventing generation-id ambiguity across boots.

## 8. Adapter capability boundary

Slice 0 records and enforces a conservative replacement decision:

- Default: a replacement is **not** allowed while retired work remains physically active.
- An adapter may opt into overlap only after its cleanup/abort behavior is proven and a later ADR defines its resource budget.
- FOCAS2, Modbus, or any other protocol must not gain automatic overlap merely because the shared primitive exists.

The runtime-reconfigure workstream may choose a stricter “old generation must terminate before replacement” rule. It must still use this same lease and fencing primitive.

## 9. Integration points

- `SourceSupervisor` / `SupervisedSource`: stable slot, lease lifecycle, retirement ordering, task observation.
- Source-to-route intake: generation-scoped writer and commit-point validation.
- Runtime diagnostics collector: generation-keyed current snapshot plus bounded generation history.
- Existing stop path: revoke/detach before cancellation and bounded wait.
- Runtime reconfigure: retire old lease and consume the same replacement admission result.
- Diagnostic strengthening: consume generation identity in progress, liveness, and event snapshots.

## 10. Non-goals

- No supervisor poll watchdog or task detachment policy.
- No automatic adapter recycle.
- No adapter-specific socket close, native-handle free, or process isolation.
- No liveness reason evaluation, alerting, bundle contributors, or Studio changes.
- No claim that only one generation is physically alive.

## 11. Acceptance tests

1. A late point after retirement cannot enter the stable route ingress.
2. A callback that passed an earlier `IsCurrent` check but reaches its commit after retirement is rejected.
3. A late fault cannot replace the current generation's health/error state.
4. Retirement detaches ingress before cancellation/disposal is invoked.
5. A task exceeding the cleanup deadline is retained for observation and counted exactly once as orphaned.
6. A later completion/fault of an orphan is recorded in history without changing current state.
7. Reusing the same source instance id increments the generation id and preserves source-lifetime counters.
8. Generation keys remain unambiguous across process restart because the runtime instance id changes.
9. Concurrent retire/authorize attempts preserve zero-or-one publish-authorized generation.
10. Replacement is denied while old work is active unless an explicit proven adapter capability and caller policy permit overlap.
11. Stop and runtime-reconfigure use the same primitive; no parallel generation implementation exists.

## 12. Delivery and ownership

Ship Slice 0 as one standalone PR/commit series with no reconfigure, recovery, liveness, or UI behavior mixed in. Assign one DRI from the Core/Host runtime area. Require approval from the diagnostic-strengthening owner and Sony's runtime-reconfigure workstream before merge.

Suggested commit subject:

```text
runtime: add shared source-generation lease and publish fencing
```
