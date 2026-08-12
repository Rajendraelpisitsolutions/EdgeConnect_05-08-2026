# Slice 0 commit 3.1 — attestation proof-matrix + deadline-inputs lock (v1 DRAFT)

**Date:** 2026-06-26
**Status:** v1 draft — for ChatGPT review pass, then v2. **No 3.1 implementation until this is reviewed.**
**Purpose:** the lock between the inert 3.0 attestation (commit `4baa5cd`) and the behaviour-changing
atomic supervisor cutover (3.1). It pins, per adapter, exactly what each retirement surface proves and
what single deadline the supervisor observes against — so the deadline comes from **verified adapter
proof inputs, not a guessed value**.

Read with: `2026-06-26-slice-0-commit-3-cutover-plan-v3.md` (the cutover steps) and
`2026-06-26-slice-0-commit-3-complete-diff.md` (the 3.0 attestation as built).

---

## A. 3.1 lock decisions (behaviour-changing gates — confirm before coding)

1. **One absolute monotonic deadline.** The supervisor sets a single deadline (monotonic clock) at
   retirement start and observes the *whole* operation against it. **No summed pump+adapter waits** — the
   deadline is `max(per-surface max-legitimate-duration) + margin`, taken once, not a sum of sequential
   budgets.
2. **Admission guards every activation path.** Every path that could activate/replace a generation goes
   through the same admission gate — no back door that skips quiescence.
3. **Replacement requires full proven quiescence.** A replacement generation is admitted only when the
   retiring generation's attestation `IsFullyProven` (every applicable surface Proven; none Unproven).
4. **Denied replacement is operator-visible immediately.** A withheld replacement surfaces a
   `SourceLifecycleBlockReason` (`AwaitingQuiescence` / `QuiescenceUnprovenAtDeadline` /
   `QuiescenceTerminallyUnproven`) to the operator at once — never a silent stall.
5. **Expected-key retirement cannot touch a successor.** Retirement targets a specific
   generation key; it must never act on (cancel/quiesce/clear) a later generation that already replaced it.
6. **M.P2.4 route-rebind cascade is removed in the cutover.** The stable-slot intake (commit 2) makes the
   per-reconfigure route-rebind cascade obsolete; 3.1 deletes it.
7. **Leak-harness multi-hour run is mandatory at 3.1.** Deferred at 3.0 (inert); at 3.1 the supervisor
   wiring is live, so the full multi-hour leak-harness pass is a hard gate.

---

## B. Deadline-inputs lock (the single deadline's ingredients)

The one absolute deadline = `max` over all live adapters of each adapter's **max-legitimate-in-flight
duration** + a margin. Each input below is the longest a *healthy* operation can legitimately stay
in-flight; anything beyond it is a wedge. **Every input marked ⚠ must be verified (config + field/bench),
not assumed — this is the explicit reason the matrix precedes coding.**

| Adapter | Deadline input (max legitimate in-flight) | Source | Verified? |
|---------|-------------------------------------------|--------|-----------|
| Modbus TCP | socket read + connect timeout | `ReceiveTimeout`/`SendTimeout`/connect timeout on the conn-mgr | ⚠ confirm effective values |
| S7 | Sharp7 read + connect timeout | S7 conn-mgr timeouts | ⚠ confirm effective values |
| FOCAS2 | longest legitimate fwlib call (handle/EW response timeout) | fwlib handle timeout (G342) | ⚠ **field-measure** — this is the incident surface |
| OPC UA | dispatcher drain budget + coordinator dispose budget | `NotificationDispatcher._shutdownDrainTimeout` (1 s default) + coordinator `DisposeAsync` | ⚠ confirm budget vs. worst-case backlog drain |
| MTConnect | HTTP request timeout | `MTConnectSourceConfiguration.TimeoutSeconds` → `HttpClient.Timeout` | ⚠ confirm HttpClient.Timeout is actually set |
| Brother | HTTP request timeout | Brother conn-mgr `HttpClient.Timeout` | ⚠ confirm HttpClient.Timeout is actually set |

**Open deadline question for review:** OPC UA `CallbackDrain` currently goes **terminal Unproven** when the
dispatcher's own drain budget expires (an adapter-internal contract, approved at 3.0), whereas
Modbus/S7/FOCAS/MTConnect/Brother stay **pending** indefinitely on a wedge (durable, late-proof possible).
Decide for 3.1: keep OPC UA's bounded-terminal CallbackDrain, or make it durable-pending like the others
so the single host deadline is the *only* terminal authority. (Recommendation: make all surfaces
durable-pending; let the single host deadline be the sole terminal authority, so no surface self-terminates
inside the deadline window.)

---

## C. The proof matrix (per adapter)

Columns: applicable surfaces · retirement operation · idle-stop proof · responsive-in-flight proof ·
max-legitimate-in-flight proof · wedged result · late-proof · terminal-unproven · deadline input ·
required deterministic tests.

### Modbus TCP  (proof class: blocking-socket wire-idle)
- **Applicable surfaces:** Worker. (CallbackDrain N/A, BackgroundWork N/A.)
- **Retirement op:** `ModbusRetirement.Begin(initiateClose = lock-free Disconnect, awaitWorkerExit = WaitForWireIdleAsync)`.
- **Idle stop:** no read in-flight → wire already idle → Worker Proven (`MODBUS.RETIRE_WIRE_IDLE`).
- **Responsive in-flight:** read returns promptly after close → wire idle → Proven.
- **Max legitimate in-flight:** longest legitimate blocking read/connect (socket timeout).
- **Wedged:** read never returns → `WaitForWireIdleAsync` pending → `Completion` pending.
- **Late-proof:** wedged read later returns → wire idle → Proven resolves late (clears barrier, no restart).
- **Terminal-unproven:** close-init throw → terminal `MODBUS.RETIRE_CLOSE_FAILED`; worker-exit fault → terminal `MODBUS.RETIRE_FAULT`.
- **Deadline input:** socket read + connect timeout ⚠.
- **Tests:** idle→proven · responsive→proven · wedged→pending(not-proven) · late-exit→proven · close-failed→terminal · worker-fault→terminal.

### S7  (proof class: blocking-socket wire-idle — same as Modbus)
- **Applicable surfaces:** Worker.
- **Retirement op:** `S7Retirement.Begin(initiateClose, awaitWorkerExit = WaitForWireIdleAsync)`.
- Idle/responsive/max/wedged/late identical to Modbus, over the Sharp7 read worker.
- **Terminal-unproven:** `S7.RETIRE_CLOSE_FAILED` / `S7.RETIRE_FAULT`.
- **Deadline input:** Sharp7 read + connect timeout ⚠.
- **Tests:** mirror Modbus.

### FOCAS2  (proof class: dedicated fwlib-thread true-exit) — the incident surface
- **Applicable surfaces:** Worker.
- **Retirement op:** `Focas2Retirement.Begin(initiateThreadCleanup = enqueue affine Disconnect + complete queue, awaitThreadExit = WaitForThreadExitAsync)`.
- **Idle stop:** no fwlib call in-flight → affine cleanup runs → thread exits → Proven (`FOCAS2.RETIRE_THREAD_EXITED`).
- **Responsive in-flight:** fwlib call returns → affine cleanup → thread exits → Proven.
- **Max legitimate in-flight:** longest legitimate fwlib call (handle/EW response timeout).
- **Wedged:** native fwlib call hangs → thread never exits → `Completion` pending (this is the production stall, now *detected* not silent).
- **Late-proof:** native call later returns → cleanup → thread exits → Proven late.
- **Terminal-unproven:** cleanup-init throw → terminal `FOCAS2.RETIRE_CLEANUP_FAILED`; affine-cleanup fault (thread-exit faults) → terminal `FOCAS2.RETIRE_CLEANUP_FAILED`.
- **Deadline input:** longest legitimate fwlib call ⚠ **field-measure**.
- **Tests:** idle→proven · responsive→proven · wedged→pending · late→proven · no-work-after-shutdown · cleanup-init-throw→terminal · cleanup-fault→terminal-CleanupFailed · idempotent.

### OPC UA Client  (proof class: supervisor-owned pump; callback + background surfaces)
- **Applicable surfaces:** Worker = **NotApplicable** (pump is supervisor-owned); CallbackDrain; BackgroundWork.
- **Retirement op:** `OpcUaRetirement.Begin(closeIngressFlag, unwireSubscriptions [best-effort], stopBackgroundWork = coordinator detach+dispose, drainCallbacks = RetireAndDrainAsync(None))`.
- **Idle stop:** no queued notifications + coordinator disposes clean → CallbackDrain Proven (drained empty) + BackgroundWork Proven → `OPCUA.RETIRE_PROVEN`.
- **Responsive in-flight:** queued notifications drained by the pump within the dispatcher budget → CallbackDrain Proven.
- **Max legitimate in-flight:** dispatcher drain budget (full bounded channel at consumer rate) + coordinator dispose time.
- **Wedged:** pump not draining → dispatcher budget expires → CallbackDrain terminal `OPCUA.RETIRE_CALLBACK_UNDRAINED` (see §B open question); coordinator dispose hang → BackgroundWork pending.
- **Late-proof:** **host observation token is NOT wired into the drain** — host-deadline expiry leaves `Completion` pending; a late drain/dispose resolves Proven. (Bounded by the dispatcher budget for CallbackDrain unless §B changes it.)
- **Terminal-unproven:** ingress-flag throw → terminal `OPCUA.RETIRE_FAULT` (both surfaces Unproven); drain-not-fully-drained → `CALLBACK_UNDRAINED`; coordinator dispose fault → `BACKGROUND_FAULT`; non-concrete dispatcher on an initialized adapter → fail closed.
- **Deadline input:** dispatcher drain budget + coordinator dispose budget ⚠.
- **Tests:** idle→proven · queued+consumer→proven · queued-no-consumer→terminal-CallbackUndrained · coordinator-dispose-fault→BackgroundWorkFault · host-token-cancel→pending · late-drain→proven · non-concrete-dispatcher→fail-closed · race-callback→counted-as-dropped.

### MTConnect  (proof class: supervisor-driven pull; in-flight-poll drain)
- **Applicable surfaces:** Worker (in-flight poll). (CallbackDrain N/A, BackgroundWork N/A — verified: no callback/subscription/reconnect/timer/dispatcher.)
- **Retirement op:** `PullAdapterRetirement.Begin(PollQuiescenceGate.BeginQuiescingAsync)`.
- **Idle stop:** no poll in-flight → gate drain completes immediately → Worker Proven (`MTCONNECT.RETIRE_POLL_IDLE`).
- **Responsive in-flight:** in-flight poll completes (HTTP returns / ct honored) → `ExitPoll` → gate drains → Proven.
- **Max legitimate in-flight:** `HttpClient.Timeout` (one poll).
- **Wedged:** HTTP poll never returns → gate drain pending → `Completion` pending.
- **Late-proof:** poll later returns → `ExitPoll` → gate drains → Proven late.
- **Terminal-unproven:** none from the gate — purely durable-pending until drain (no internal terminal path).
- **Deadline input:** `TimeoutSeconds` → `HttpClient.Timeout` ⚠ (confirm it is actually applied).
- **Tests:** idle→proven · responsive-poll→proven · wedged-poll→pending · late→proven · refuse-new-poll-after-retiring · ExitPoll-releases-on-throw.

### Brother HTTP  (proof class: supervisor-driven pull — same as MTConnect)
- **Applicable surfaces:** Worker (in-flight poll). Retirement op + behaviours identical to MTConnect, layered over Brother's existing inner single-flight overrun guard (independent lock — does not race the retirement gate).
- **Terminal-unproven:** none from the gate — durable-pending.
- **Deadline input:** Brother `HttpClient.Timeout` ⚠.
- **Tests:** mirror MTConnect, plus single-flight-vs-retirement-gate non-interference.

---

## D. One-glance summary

| Adapter | Surfaces | Wedged → | Late-proof | Terminal path | Deadline input |
|---------|----------|----------|-----------|---------------|----------------|
| Modbus | Worker | pending | yes | close-failed / fault | socket r/w + connect ⚠ |
| S7 | Worker | pending | yes | close-failed / fault | Sharp7 r/w + connect ⚠ |
| FOCAS2 | Worker | pending | yes | cleanup-failed | fwlib call timeout ⚠ field |
| OPC UA | CallbackDrain + BackgroundWork | callback→terminal(budget); bg→pending | yes (host-token not terminal) | callback-undrained / bg-fault / fault | drain budget + dispose ⚠ |
| MTConnect | Worker (poll) | pending | yes | none (durable) | HttpClient.Timeout ⚠ |
| Brother | Worker (poll) | pending | yes | none (durable) | HttpClient.Timeout ⚠ |

## E. Open items for the review pass
1. **§B open question** — unify OPC UA CallbackDrain to durable-pending, or keep dispatcher-budget-terminal?
2. **All ⚠ deadline inputs** — produce the verified values (config audit + FOCAS field measurement) before
   the single deadline is set. The matrix is the gate; coding waits on this.
3. Confirm the **margin** added on top of `max(inputs)` and the **monotonic clock** source.
4. Confirm the 7 §A lock decisions map 1:1 to cutover-plan-v3 steps with no behaviour gap.
