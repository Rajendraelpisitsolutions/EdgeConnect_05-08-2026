# Incident — FOCAS2 sources stall while reporting "Running" (data stops, restart-only recovery)

**Incident date:** 2026-06-24  
**Precision review:** 2026-06-25  
**Status:** Source-side failure boundary and platform detection/containment defect confirmed. A blocked
FOCAS worker/native call is the leading code-supported mechanism; the exact fwlib function and
initiating environmental/device trigger remain unconfirmed until a live stack capture.  
**Stopgap:** full gateway-service restart.  
**Severity:** High — silent total data loss while global health reported healthy.  
**Site:** `gw-desktop-019ln49` (DESKTOP-019LN49), 8× FANUC CNC via FOCAS2 → 1 MQTT sink.  
**Reporter:** Sudhakar.

---

## 1. Summary

When the incident was examined, seven previously productive FOCAS2 sources had been silent for
roughly 14–20 hours, consistent with stalls beginning around the 2026-06-23 evening IST period. The
eighth source, `1420311-source`, had never produced and is a separate pre-existing issue. Studio still
showed 8/8 route lifecycles running and “All systems healthy.” Route-buffer evidence showed that the
MQTT sink and store-and-forward path were draining, placing the active incident upstream in the source
path.

Editing one source recreated its adapter/thread/handle and immediately restored points, but the source
stalled again within roughly one minute. This strongly localizes the problem to source poll progress
and shows that restart clears the condition only temporarily.

The code establishes a serious FOCAS2 robustness defect: the source can wait indefinitely for work on
its single fwlib-affine worker thread, and cancellation of the awaiting task cannot interrupt an
already-running native call. The incident evidence is consistent with that mechanism. A process dump
from an actively wedged gateway is still required to prove which fwlib function was executing and
what initiated the wedge.

This incident is separate from runtime-reconfigure planning and is not explained by the MQTT sink.

---

## 2. Symptoms observed

- Seven previously productive FOCAS2 sources were lifecycle `Running` with frozen point counters and
  “last point” ages of roughly 14–20 hours.
- `1420311-source` showed zero lifetime points and is a separate pre-existing reachability/configuration
  issue, not evidence of the shared stall.
- Route `route-1520411` showed buffer counters `Enqueued 33,184`, `Drained 33,184`, `Dropped 0`, with a
  small residual queue, while sink `Menon_KHPL` remained `Running`.
- Live Data Tap on a stuck route captured no new source-side points.
- The global footer still reported “All systems healthy.”
- Editing `1520411-source` restored its point counter from 34,255 to 34,663, followed by another stall
  within about one minute.

---

## 3. Environment and topology

- 8 FOCAS2 sources → 8 routes → shared MQTT sink `Menon_KHPL` at `20.197.8.189:1883`.
- Route buffers used store-and-forward with `DropOldest`, so overflow policy was non-blocking.
- The last recorded configuration change was 2026-06-22 09:11; no configuration event aligned with
  the 2026-06-23 stall window.

---

## 4. What the evidence rules out

1. **MQTT sink/store-and-forward as the immediate blocker.** Enqueued and drained counts matched,
   dropped was zero, and the sink reported running.
2. **Blocking backpressure from the configured overflow policy.** `DropOldest` does not require the
   source intake path to wait for sink recovery.
3. **A normal adapter exception exit.** The cited supervisor behavior would mark a throwing poll loop
   failed; lifecycle remained `Running`, which is more consistent with non-returning work than a
   surfaced exception.
4. **A configuration/reconfigure event at the original stall time.** No matching change was recorded.

These observations confirm the failure boundary upstream of the route buffer. They do not, by
themselves, identify the exact native function or network/device trigger.

---

## 5. Code-supported failure mechanism

FOCAS2 performs source reads on one dedicated `Focas2Thread` because fwlib requires thread affinity.
The collect cycle is dispatched as:

```csharp
var points = await _thread!.RunAsync(() => CollectAll(), ct); // Focas2SourceAdapter.cs:345
```

The cited implementation has two relevant properties:

1. No independent operation deadline bounds an individual fwlib call inside `CollectAll()`.
2. Cancelling the token/await does not abort an already-running native call. If that call does not
   return, the single-consumer worker cannot process subsequent work.

This creates an **unbounded-hang exposure** that explains the observed state: the lifecycle remains
`Running`, no exception is surfaced, point counters stop, and health remains green because health is
not based on independent progress.

`TimeoutSeconds`, as cited in `Focas2ConnectionManager.cs`, applies to handle allocation and is not a
watchdog around every subsequent read.

### Confidence boundary

The incident evidence plus code make a blocked worker/native call the leading mechanism, but no live
thread/native stack was captured. The exact active fwlib function, and whether the initiating event
was a half-open connection, library deadlock, controller behavior, resource contention, or another
adapter-local condition, remain open.

---

## 6. Why one-source recreation helped only briefly

The configuration edit caused the affected source generation to be torn down/recreated sufficiently
to obtain a fresh worker/connection path, after which data resumed. The rapid recurrence shows that
recreation removed the immediate stuck state but did not remove its trigger.

The old blocked operation may also remain alive if the native call cannot be interrupted. That is why
future recovery must use generation fencing and a bounded orphan/resource policy rather than
repeatedly abandoning threads and handles without limit.

---

## 7. Cross-protocol update

The same silent-stall symptom has also been reported with Modbus TCP. The cited Modbus path wraps a
synchronous read in `Task.Run(..., ct)`. That token can prevent scheduled work from starting, but it
cannot cancel a synchronous socket read once the delegate is executing.

This establishes a shared **blocking-I/O failure-containment gap**:

- the supervisor has no independent progress deadline/reporting path;
- an in-flight blocking call may ignore cancellation;
- lifecycle health can remain green while data progress has stopped.

It does **not** establish that FOCAS2 and Modbus share the same network/device trigger, nor that every
reported Modbus stall was caused by the identical low-level operation. Shared detection and recovery
orchestration belong in the host; safe interruption and resource reclamation remain adapter-specific.

See `2026-06-25-diagnostic-strengthening-plan-v2.md`.

---

## 8. Trigger hypotheses to verify, not assume

- network path or endpoint behavior leaving a connection established but non-progressing;
- controller/client connection-count or concurrency constraints;
- a defect or lock condition in the deployed fwlib version;
- a particular collector/native function that does not return for a controller state;
- resource contention caused by old generations/handles or another client.

No hypothesis should be ranked as the cause until timing, stack, and environment evidence are
captured.

---

## 9. Immediate response

1. Restart the gateway service to terminate all in-process blocked work and restore a clean process.
2. Confirm each previously productive source resumes and record the exact restart and first-stale
   times.
3. Keep `1420311-source` as a separate investigation.
4. On recurrence, capture a process dump before restart whenever operationally safe.

The restart is a containment measure, not a permanent cure.

---

## 10. Permanent correction

### Shared platform work

1. Independent source-progress/liveness health so a non-returning poll becomes `Degraded`/`Unhealthy`
   without waiting for the poll to return.
2. Cached per-operation instrumentation and runtime diagnostic bundles that remain available while an
   adapter is stuck.
3. Timeout-aware supervisor waiting, generation retirement, late-result fencing, bounded recovery,
   and explicit terminal escalation.

### FOCAS2-specific work

1. Instrument each collector/native call so the exact in-flight function is visible.
2. Verify whether the deployed fwlib permits safe cross-thread abort/handle release while a call is in
   flight. Do not assume that freeing a handle from another thread is safe.
3. If safe in-process abort is proven, quarantine the old worker, abort, reclaim, and create a fresh
   generation.
4. If safe abort is not available, use a strict orphan budget and escalate to process restart or an
   isolated adapter-worker process. Repeatedly abandoning worker threads/handles is not a complete
   self-heal design.

### Modbus-specific follow-up

Verify whether closing the underlying socket reliably interrupts the deployed FluentModbus
synchronous read. Enable automatic in-process recycle only after that behavior and resource cleanup
are tested.

---

## 11. Evidence to capture at the next recurrence

- process dump with native stacks and FOCAS worker thread names;
- exact per-source first-stale time and current in-flight operation, if instrumentation is present;
- gateway logs and runtime diagnostic bundle;
- TCP connection table for port 8193 and owning PID;
- gateway-to-CNC reachability during the active stall;
- fwlib DLL version, process bitness, CNC model/series/options, and connection-limit information;
- concurrent FOCAS client inventory;
- relevant switch/firewall/session events around the stall onset.

---

## 12. Next actions

- [ ] Commit this incident record with the confidence boundary intact.
- [ ] Add `2026-06-25-diagnostic-strengthening-plan-v2.md`; retain v1 as historical draft.
- [ ] Implement detection/forensics before automatic recycle.
- [ ] Build a deterministic blocking-call test shim for FOCAS2 and a non-responsive Modbus test server.
- [ ] Capture a live dump before the next restart when operationally safe.
- [ ] Investigate `1420311-source` separately.
