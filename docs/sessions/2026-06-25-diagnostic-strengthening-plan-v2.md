# Diagnostic strengthening + blocking-I/O stall containment — Plan v2

**Date:** 2026-06-25  
**Status:** v2 — review pass complete; design candidate for repository review and reality-check. Do not implement the recovery half until the adapter-abort assumptions in §7 are proven.  
**Supersedes for planning:** `2026-06-24-diagnostic-strengthening-plan-v1.md`  
**Keeps as historical input:** v1 remains part of the plan trail and should not be rewritten.  
**Trigger:** 2026-06-24 FOCAS2 silent-stall incident, plus the same symptom reported on Modbus TCP.

---

## 0. Review outcome

The v1 reframe is correct: EdgeConnect has a platform-level **detection and containment gap** around
source progress, and that gap is broader than FOCAS2. The review makes five material corrections:

1. **Do not call the exact native hang “confirmed” yet.** The failure boundary is confirmed upstream
   in the source, and the code proves that an unbounded blocking call is possible. A live thread dump
   is still required to identify the exact blocked fwlib function and initiating trigger.
2. **Do not use `lastObservation` alone as liveness.** A source can poll successfully and emit no
   points. Health needs separate progress clocks for scheduling, poll completion, transport success,
   and data emission.
3. **`CancelAfter` is not a watchdog for non-cooperative I/O.** Passing a cancelled token to
   `PollAsync` does not make an already-running blocking call finish. An independent monitor can
   detect the hang; a bounded wait such as `WaitAsync`/`WhenAny` can stop the supervisor waiting, but
   it leaves the original operation running.
4. **A shared supervisor cannot safely “recycle” every adapter by itself.** Safe recovery requires
   generation fencing plus a proven adapter-specific abort/teardown path. Otherwise retries can leak
   threads, handles, sockets, and late data.
5. **Arbitrary native hangs are only fully containable at a process boundary.** In-process recovery is
   an optimization that must be proven per adapter. Process isolation or controlled gateway restart
   is the guaranteed final containment mechanism.

The resulting design remains cross-protocol, but it distinguishes:

- **shared detection and orchestration**, which belongs in the host/supervisor; and
- **adapter-specific interruption and resource reclamation**, which cannot be assumed universal.

---

## 1. Precise incident model

Use five layers when describing this and future incidents. Keeping these layers separate prevents an
architectural weakness from being mistaken for an environmental root cause.

| Layer | What is known for this incident | Confidence |
|---|---|---|
| Symptom | Source data stopped while lifecycle state remained `Running`; downstream buffer/sink continued to drain. | Confirmed |
| Failure boundary | Progress stopped upstream of the route buffer, inside or immediately around the source poll path. | Confirmed |
| Platform gap | No independent progress/liveness evaluator bounded or reported the blocked poll; health remained green. | Confirmed from behavior and cited code |
| Adapter mechanism | A non-cancellable blocking fwlib operation can park the single FOCAS worker; Modbus has the same cancellation mismatch around synchronous socket I/O. | Confirmed exposure; exact active call during the incident not yet captured |
| Initiating trigger | Network state, controller behavior/client limits, library defect, or another adapter-local condition. | Unknown pending live capture |

Therefore, the shared finding is best named a **blocking-I/O failure-containment gap**, not a single
cross-protocol environmental root cause.

---

## 2. Non-negotiable design invariants

The implementation must preserve all of these invariants:

1. **Diagnostics never call device I/O.** Health pages, alert evaluation, and diagnostic-bundle
   generation read cached/atomic snapshots only. A stuck adapter must not be able to hang diagnostics.
2. **The liveness monitor is independent of the poll execution path.** It must continue running when
   `PollAsync` is permanently blocked.
3. **Lifecycle and health are separate.** A source may remain lifecycle `Running` while health is
   `Degraded` or `Unhealthy`; the UI must show both rather than relabeling lifecycle state as `Stale`.
4. **Only the current generation may publish.** Results, metrics, and callbacks from a retired adapter
   generation are rejected even if the old blocking operation eventually returns.
5. **No overlapping work on one adapter instance.** A timed-out call cannot be followed by another
   call against the same object unless the adapter explicitly proves reentrancy and safe abort.
6. **Retries are resource-bounded.** Automatic recovery cannot create an unbounded number of orphaned
   tasks, threads, sockets, native handles, or controller sessions.
7. **Every timeout is observable.** The original task is retained and its eventual completion/fault is
   observed; no fire-and-forget exception or silent late return.
8. **Durations use monotonic time.** UTC timestamps are retained for display and correlation, but
   timeout decisions cannot depend on wall-clock changes.
9. **Operation budgets are not poll cadence.** `pollInterval` controls when work starts;
   `maxPollDuration` controls how long one cycle may run. One must not be derived as a small multiple
   of the other without adapter/workload evidence.
10. **Recovery has a terminal state.** After a bounded retry/orphan budget, the source becomes
    `RecoveryExhausted`/`Faulted` and raises an actionable alert rather than looping forever.

---

## 3. A liveness model that does not confuse “idle” with “hung”

Replace the single ambiguous `lastObservation` concept with a cached progress snapshot per source.
At minimum record:

- `lastScheduleDue` — when the next cycle should have started;
- `lastPollStarted`;
- `lastPollCompleted` — regardless of whether the cycle returned points;
- `lastTransportSuccess` — last successful device/protocol exchange;
- `lastDataEmitted` — last point accepted into the route;
- `inFlightOperationName`, `inFlightStarted`, and `generationId`;
- `lastFailure`, `consecutiveFailures`, timeout count, recovery count, and retired-generation count.

Derive explicit reason codes rather than one generic “stale” flag:

| Reason | Detection rule | Typical meaning |
|---|---|---|
| `SchedulerOverdue` | No poll started by its due time plus scheduler slack | Supervisor/scheduler progress problem |
| `PollOverdue` | Current poll exceeds a soft operation budget | Slow or potentially stuck adapter call |
| `PollTimedOut` | Current poll exceeds the hard operation budget | Recovery/containment decision required |
| `TransportStale` | Polls complete or retry, but no successful device exchange within policy | Device/network/protocol unavailable |
| `DataSilent` | Transport remains healthy but no data is emitted within a configured data-cadence policy | Idle machine, filters/configuration, or data-side issue |
| `SubscriptionSilent` | No protocol heartbeat/notification/control-loop progress within policy | Subscription or stream pump stalled |
| `RecoveryLoop` | Recovery rate exceeds budget | Persistent environmental or adapter defect |

`DataSilent` must be policy-driven and may be informational for legitimately idle machines. By
contrast, `PollOverdue` and `SchedulerOverdue` are progress failures and do not depend on whether the
machine should be producing values.

### Ownership

- **Core** defines the progress snapshot, reason codes, severity rules, and route-health propagation.
- **Host/Supervisor** owns monotonic timing, periodic evaluation, generation lifecycle, and recovery
  orchestration.
- **Adapters** publish operation progress and implement optional abort/recovery capabilities; they do
  not decide the product-wide health presentation.

---

## 4. Delivery layers

### L1 — Independent progress monitor and truthful health

Implement a host-owned periodic monitor that reads the cached progress snapshot independently of the
poll loop.

On a threshold transition it must:

- set source health to `Degraded` or `Unhealthy` with a stable reason code and elapsed duration;
- propagate the reason into route health and the global health summary;
- emit one flight-recorder event per transition, with debounce/hysteresis to prevent alert flapping;
- expose the same reason through the health endpoint and Studio.

This layer is observational. It does **not** detach a task, dispose an adapter, or start a replacement.
It can ship first and closes the 18-hour blind spot without introducing recovery races.

### L2 — Safe per-operation instrumentation

Instrument adapter calls with a small, non-blocking probe:

1. Write operation name, generation, and monotonic start time immediately before the device/library
   call.
2. On normal return, atomically record duration and success/failure metadata.
3. Do not hold a lock across the external call.
4. Do not make diagnostic snapshot reads wait for the adapter worker or an adapter-owned lock.

Protocol mappings may differ while retaining one schema:

- FOCAS2: collector and native fwlib operation;
- Modbus: unit/block/function read;
- S7/EtherNet/IP: request or tag batch;
- OPC UA: connect/session/subscription/keepalive callback and monitored-item progress.

Histograms are useful, but the minimum viable evidence is current in-flight operation, start time,
last completion, and last failure.

### L3 — Runtime diagnostic bundle

Add a bounded `Runtime` capability that reads cached state only:

- source/sink/route lifecycle and health snapshots;
- the L1/L2 progress snapshots and timeout/recovery history;
- buffer counters and queue depth;
- recent flight-recorder events and fault registry;
- bounded recent logs after redaction;
- process/runtime indicators useful for systemic stalls: process uptime, thread-pool queue/worker
  counts, managed thread count, memory/GC pressure, and active recovery/orphan counts.

Each contributor receives its own short deadline and fails soft. The manifest records completion,
truncation, redaction, timeout, or skip reason. Bundle generation must succeed even when every source
adapter is blocked.

### L4 — Bounded waiting and generation retirement

This is the first behavior-changing layer and must not be confused with L1 detection.

A supervisor token passed into `PollAsync` is insufficient when the implementation is inside a
synchronous/native call. To bound the supervisor's wait, use a timeout-aware await (`WaitAsync` or an
explicit `WhenAny` pattern appropriate to the target framework). On hard timeout:

1. Mark the current generation **retired** atomically.
2. Record `PollTimedOut` with operation, elapsed duration, and generation.
3. Attach observation to the original task so its eventual result/fault is collected.
4. Reject every late result/callback from that generation.
5. Do **not** start another call on the same adapter instance.
6. Hand the retired generation to the recovery orchestrator.

Illustrative shape only:

```csharp
var generation = sourceGeneration.Current;
var pollTask = adapter.PollAsync(hostStopToken);

try
{
    var points = await pollTask.WaitAsync(maxPollDuration, hostStopToken);
    if (!generation.IsCurrent)
        return; // retired generation: discard late output

    Publish(points, generation.Id);
}
catch (TimeoutException)
{
    generation.Retire("PollTimedOut");
    ObserveLateCompletion(pollTask, generation.Id);
    recoveryQueue.Enqueue(generation);
}
```

The real implementation must ensure the adapter cannot publish through side channels that bypass the
generation fence.

### L5 — Adapter recovery capability and bounded orchestration

Define an explicit capability instead of assuming `DisposeAsync` can interrupt every call. A useful
contract would expose facts such as:

- can the adapter abort an in-flight operation;
- is abort safe from another thread;
- can the connection/handle be recreated in-process;
- can disposal itself block;
- what resource remains if abort fails;
- what isolation scope is required for guaranteed termination.

Recovery state machine:

`TimedOut → AbortRequested → Quarantined/Stopped → Backoff → StartingNewGeneration → Probing → Healthy`

with terminal alternatives:

`AbortFailed`, `RecoveryExhausted`, or `ProcessRestartRequired`.

Mandatory controls:

- exponential backoff with jitter;
- maximum recoveries per source per time window;
- maximum retired/orphaned generations per process;
- no replacement generation until resource policy permits it;
- one post-recovery probe before declaring healthy;
- diagnostic capture before optional process restart;
- clear operator-facing reason and next action when the budget is exhausted.

### L6 — Protocol-specific recovery implementations

#### FOCAS2

- Quarantine the old `Focas2Thread`; never queue additional work to it.
- Do not assume `cnc_freelibhndl` or equivalent is safe from a different thread while another fwlib
  call is in progress. Verify FANUC/fwlib requirements and test the exact version in use.
- If cross-thread abort/free is proven safe and unblocks the call, reclaim and reconnect in-process.
- If it is not proven safe, retain the old generation as an orphan, apply a very small orphan budget,
  and escalate to process restart or an isolated worker process. Repeatedly abandoning threads and
  handles is not a sustainable self-heal strategy.

#### Modbus TCP

- Test whether closing/disposing the underlying socket from the control path reliably interrupts the
  synchronous FluentModbus read for the deployed library/runtime versions.
- If it does, use socket close as the hard abort, await worker completion within a short cleanup
  deadline, then create a fresh client.
- If the library hides the socket, disposal blocks, or the read remains stuck, quarantine the
  generation and use the same bounded escalation policy.
- Avoid unbounded `Task.Run` retries; a persistent peer that accepts but never replies must not consume
  one additional thread-pool worker per recovery attempt.

#### Other protocols

Audit S7, OPC UA, and EtherNet/IP before claiming automatic-recovery coverage. They inherit shared
health and timeout reporting immediately, but automatic restart is enabled only after their abort and
late-callback behavior passes the recovery contract tests.

### L7 — Hard containment boundary

For native or third-party calls that cannot be safely interrupted in-process, an OS process boundary
is the only general hard timeout. Evaluate an adapter-worker process model:

- ideally one source per worker for maximum fault isolation, or a deliberately chosen smaller scope;
- supervisor heartbeat and operation deadline;
- kill/restart worker on timeout;
- generation/epoch in every IPC message so late data cannot cross a restart;
- bounded restart policy and diagnostic dump before kill where feasible.

This is a larger architectural step and need not block L1–L3. It should, however, be recorded as the
long-term answer if fwlib cannot provide a safe in-process abort.

### L8 — Proactive source-gap explainer and alerts

Drive the existing “why data is missing” path from the new reason codes, not only from a compare
verdict. Examples:

- `PollOverdue`: “FOCAS2 collector `ToolCollector` has been in flight for 47 s; last completed poll was
  51 s ago.”
- `TransportStale`: “Poll loop is progressing, but no device exchange has succeeded for 6 min.”
- `DataSilent`: “Device exchanges are healthy; no points have been emitted for 20 min. Machine idle or
  tag/filter policy may explain this.”
- `RecoveryExhausted`: “Three recoveries failed in 15 min; automatic retries stopped to prevent
  resource leakage.”

Studio and the existing health endpoint are the first delivery surfaces. External paging can consume
the same stable reason codes without coupling the core design to a specific alert provider.

---

## 5. FOCAS2 trigger-capture methodology

The next recurrence should be captured **before** restart whenever operationally safe. Priority order:

1. Process dump with native stacks and thread names; identify the exact `Focas2-<id>` frame and fwlib
   function.
2. Runtime bundle from L3, or the best available current bundle plus logs.
3. Connection table for CNC port 8193, including owning PID and TCP state.
4. Reachability test from the gateway to each affected CNC while the stall is present.
5. fwlib DLL version, process bitness, CNC model/series/options, and configured connection limits.
6. Whether another FOCAS client was connected concurrently.
7. Network/firewall/switch session logs and configuration around the first observed stall time.
8. Exact per-source first-stale time to determine whether failure was simultaneous, sequential, or
   correlated with a shared network event.

Treat network idle/session handling, client-count contention, library defects, and particular
collector calls as **hypotheses**, not ranked conclusions, until the dump and timing evidence exist.

A controlled reproduction should start with one CNC, then two only if controller connection limits
are known. Instrumentation must be enabled before the run, and the test must define a stop condition
for orphan count, resource growth, or production impact.

---

## 6. Sequencing and implementation slices

### Slice A — Detection without behavior change

1. Add the progress snapshot and monotonic timestamps.
2. Add the independent L1 evaluator and stable reason codes.
3. Correct Studio, route health, footer, and health endpoint.
4. Emit flight-recorder transitions with debounce.

**Outcome:** a blocked poll becomes visible within policy even though the poll task remains stuck.

### Slice B — Forensics

1. Add per-operation probes to FOCAS2 and Modbus first.
2. Add cached runtime bundle contributors and process-level runtime metrics.
3. Add proactive source-gap explanations.

**Outcome:** the next incident identifies the active operation and captures evidence without touching
the blocked adapter.

### Slice C — Safe recovery foundation

1. Implement generation IDs and publish fencing.
2. Implement timeout-aware supervisor waiting and late-task observation.
3. Add recovery state machine, backoff, retry/orphan budgets, and terminal escalation.
4. Add deterministic fake adapters for never-returning calls and late returns.

**Outcome:** the host can retire a generation safely, but automatic adapter restart remains disabled
until the adapter capability is proven.

### Slice D — FOCAS2 and Modbus recovery

1. Prove or reject FOCAS cross-thread abort/handle-release behavior.
2. Prove or reject Modbus socket-close interruption behavior.
3. Enable automatic recovery only for the proven path.
4. Fall back to controlled process restart or operator action on unsupported paths.

### Slice E — Remaining protocols and process isolation decision

Audit S7, OPC UA, and EtherNet/IP, then decide by ADR whether non-interruptible adapters move to worker
processes.

---

## 7. Required proof before enabling automatic recycle

Automatic recycle is gated by tests, not by interface implementation alone.

### Common deterministic tests

- `PollAsync` never completes and ignores cancellation.
- The timed-out task returns successfully later; all late output is discarded.
- The timed-out task faults later; the exception is observed and recorded.
- Adapter `DisposeAsync` hangs.
- Reconfigure occurs while a generation is timed out or recovering.
- Gateway shutdown occurs with one or more retired generations.
- One hundred injected timeouts do not produce unbounded thread, task, handle, socket, or memory
  growth.
- Diagnostic bundle completes within its own deadline while all adapters are blocked.
- Health transitions are deterministic and debounced.

### FOCAS2 tests

Use a shim around fwlib so a chosen collector call can block indefinitely. Verify thread affinity,
abort/free behavior, handle counts, late completion, and process-shutdown behavior. Repeat against the
actual deployed fwlib version before claiming production-safe in-process recovery.

### Modbus tests

Use a test server that accepts a TCP connection and then never returns a response. Verify read timeout,
socket-close interruption, thread-pool behavior, reconnect, and recovery-budget exhaustion.

### Acceptance criteria

- A blocked poll changes health within the configured soft threshold without waiting for `PollAsync`.
- Global health never reports “all healthy” while a source has an active liveness fault.
- A runtime bundle remains producible during the stall and names the in-flight operation when
  instrumentation exists.
- No result from a retired generation reaches a route or changes current-generation counters.
- Automatic recovery never exceeds the configured retry/orphan budget.
- Unsupported hard-abort paths escalate explicitly rather than pretending to self-heal.
- A healthy but data-idle source is distinguishable from a blocked poll.

---

## 8. ADR decisions

1. **Amend ADR-0027:** health has independent lifecycle, progress, transport, and data-cadence
   dimensions; route/global health propagates reason-coded liveness faults.
2. **Amend ADR-0020:** add bounded cached runtime contributors, contributor deadlines, and free-text log
   redaction/truncation policy.
3. **Amend ADR-0023:** permit proactive source-gap explanations driven by liveness reason codes.
4. **New ADR — Blocking operation containment:** record generation retirement, publish fencing,
   adapter abort capability, retry/orphan budgets, and terminal escalation.
5. **Potential follow-up ADR — Adapter process isolation:** required if FOCAS or another library has no
   safe in-process abort.

---

## 9. Repository and commit recommendation

Preserve the documentary trail:

- keep the 2026-06-24 incident note at its original path, with the precision corrections from this
  review;
- keep v1 unchanged and clearly marked “do not implement”;
- add this v2 as a new dated file;
- perform the planned reality-check before creating v3/implementation lock.

Prefer two focused documentation commits:

1. `docs(incident): record FOCAS2 silent-stall evidence and confidence boundaries`
2. `docs(plan): add cross-protocol liveness and blocking-I/O containment plans v1-v2`

If `master` contains unrelated commits not intended for `Sony_Development`, cherry-pick these focused
commits rather than merging the whole branch. Otherwise follow the repository's established docs-only
merge flow.

---

## 10. One-line lesson

The platform-wide issue is not merely that a protocol call can hang; it is that **EdgeConnect had no
independent way to notice, explain, contain, and safely retire blocked work**. Detection is shared and
can ship quickly. Recovery is shared in orchestration but must remain adapter-proven, generation-
fenced, and resource-bounded.
