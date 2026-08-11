# Slice 0 commit 3.0 — adapter-attestation review & correction record

**Date:** 2026-06-26
**Reviews:** `2026-06-26-slice-0-commit-3-complete-diff-review-gate.md` (send-for-review gate) →
`2026-06-26-slice-0-commit-3-complete-diff-review.md` (hold for three corrections).
**Outcome:** three correction gates applied; same green gate re-run; finalized as
`runtime: add adapter retirement attestation across source adapters (slice 0, commit 3.0)`.

3.0 is the **inert adapter-attestation precursor**; 3.1 is the later behaviour-changing supervisor
cutover. The complete unified diff (47 files, +2,967/−7) was produced and reviewed in full; this file
is the durable decision record (the raw diff dump is not retained in-repo).

## Design calls (approved)

1. **`PollQuiescenceGate` / `PullAdapterRetirement` in `Core/Adapters/Retirement/`** — protocol-agnostic
   adapter-facing utilities. No Host dependency, no replacement-admission policy, no lifecycle severity,
   no deadline decision, no live supervisor wiring. They keep MTConnect/Brother thin.
2. **Wedged poll modelled as `Worker`** for MTConnect/Brother (not all-`NotApplicable`). A wedged HTTP
   poll is an executing adapter operation and the exact silent-stall class this workstream contains.
   `CallbackDrain`/`BackgroundWork` are `NotApplicable` only after inspection confirmed no callback,
   subscription, reconnect loop, timer, dispatcher, or adapter-owned background work surface exists.

## Corrections applied (from the complete-diff review)

### Blocker 1 — OPC UA must not convert the host observation deadline into terminal adapter evidence
- `OpcUaRetirement` no longer threads `context.ObservationToken` into the drain. `DrainCallbacksAsync`
  calls `NotificationDispatcher.RetireAndDrainAsync(CancellationToken.None)`, so **only the dispatcher's
  own drain budget** can produce a terminal not-fully-drained (`CallbackDrain = Unproven`). Host-deadline
  expiry leaves `Completion` pending so a late drain still resolves `Proven`.
- Tests: host token cancelled → `Completion` pending; late drain after the host deadline → `Proven`;
  dispatcher-owned drain timeout → terminal `CallbackDrain = Unproven` (retained).

### Blocker 2 — OPC UA `BeginRetirement` must stay non-blocking (no resourceful cleanup under `_stateLock`)
- Under `_stateLock` we now ONLY capture dispatcher/coordinator/subscription references and set the
  authoritative dispatcher ingress flag (cheap, no UA-stack calls); the durable operation returns
  promptly. Subscription unwire/delete, coordinator detach/dispose, and dispatcher drain all run in the
  async resolution path over the captured locals, off the lock.
- The synchronous Begin path is the ingress flag only; `unwireSubscriptions` is a best-effort async step
  that does NOT gate proof (the flag is authoritative).
- Tests: `BeginRetirement` returns promptly even if a delegate would block/throw; ingress rejection is
  active immediately; best-effort unwire failure does not prevent a fully-proven attestation.

### Blocker 3 — MTConnect/Brother inertness is corrected, not overstated
- **Wording (this record + commit):** there is *no supervisor retirement wiring*, but MTConnect and
  Brother carry a **behaviour-neutral live poll-path guard** (`PollQuiescenceGate.TryEnterPoll`/`ExitPoll`
  wrap the outermost `PollAsync`). The helper/operation is unreachable; the gate wrapper is reachable
  through normal polling and is semantically inert while `_quiescing == false`.
- **`PollQuiescenceGate.ExitPoll` hardened** against underflow (public Core primitive): a spurious/double
  exit is ignored rather than driving the counter negative (which would wedge quiescence). Deterministic
  underflow + double-exit tests added.
- **Live poll-path smoke** added for both adapters (in addition to their existing 59/182-test suites that
  already exercise the gate-wrapped poll): a normal poll while not retiring reaches the real body and the
  gate enter/exit is paired (a subsequent `BeginRetirement` resolves `Proven` only because `ExitPoll`
  returned the in-flight count to zero — proven even when the poll body throws).

### Non-blocking cleanups also applied
- FOCAS2: a faulted thread-exit now maps to the precise `FOCAS2.RETIRE_CLEANUP_FAILED` (the exit task
  faults only when the affine final cleanup threw); the generic `RETIRE_FAULT` code was removed as dead.
- OPC UA: a non-concrete `INotificationDispatcher` on an initialized adapter now **fails closed**
  (`FullyDrained = false`) rather than optimistically reporting drained; only a genuinely null dispatcher
  (constructed adapter) reports drained.
- OPC UA: added the callback-race test — a notification that passes the ingress check but races channel
  completion is **counted as dropped** (received + backpressure-dropped), never silently lost.

## Accepted as-is (verified in the actual diff)
- Modbus **M1**: snapshot/TCS created before `initiateClose`; a close-initiation throw returns a durable
  operation with terminal `Unproven` / `MODBUS.RETIRE_CLOSE_FAILED`.
- FOCAS2 checkpoint gates: true thread-exit proof (no `Join`-as-proof), post-shutdown `RunAsync`
  rejection, final cleanup observed via thread-exit failure, idempotent shutdown.
- OPC UA surface model: `Worker=NotApplicable`, `CallbackDrain=Applicable`, `BackgroundWork=Applicable`;
  ingress closed before drain; session close never used as callback-drain proof.

## Full-gate accounting (post-correction, all green; build 0/0)

| Gate | Result |
|------|--------|
| `dotnet build ElpisEdgeConnect.sln` | 0 warnings / 0 errors |
| Core.Tests | 969 |
| Host.Tests | 211 |
| Management.Tests (full) | 1074 |
| Integration.Tests | 87 passed, 1 skipped (Mosquitto-dependent MQTT) |
| OpcUaClient / MTConnect / Brother / Focas2 / Modbus / S7 | 291 / 59 / 182 / 140 / 245 / 213 |
| Leak-harness (4-hour) | Builds 0/0; multi-hour RUN deferred to 3.1. 3.0 has **no supervisor retirement wiring**; the only live change is MTConnect/Brother's behaviour-neutral poll-path guard, covered by the per-adapter suites + the added poll-path smoke. The harness re-run is owed at 3.1 when retirement is wired into the supervisor. |

## Next
Proceed to **3.1 (atomic supervisor cutover)** only after the attestation proof matrix and deadline
inputs are locked, per the cutover plan v3.
