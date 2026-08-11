# K3 Slice 6 — Review Bundle (pass 1: operational rebirth, NCMD, coalesced disconnect)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `f0d97a0` — *feat(sparkplug): K3 slice 6 pass 1*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass1-source-diff.md` (full `git show -W`, 0 ellipses).
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests `0/0`.
**Tests:** `474 passed / 0 / 0`, broker-free. All prior Begin/handoff/replay tests stay green (the refactor regression guard).

This is the first of two slice-6 passes (agreed split). **Pass 1** = both rebirth branches + NCMD/disconnect wiring + coalescing + the establish-core refactor. **Pass 2** (next) = the bounded transport-recovery budget loop (§4.7, with an injected delay seam) and graceful `EndSessionAsync` + Stop/Dispose idempotence. `EndSessionAsync` remains a documented stub until pass 2.

Maps to the 7 mandatory carry-forwards: **#1, #2, #3, #4** land here; **#5 (partially), #6, #7** are pass 2.

---

## What landed (pass 1)

### NCMD receive path (carry-forward #3)
- `ISparkplugMqttTransport.NodeCommandReceived(generation, payload)` — wired in the concrete transport from MQTTnet's `ApplicationMessageReceivedAsync`, tagged with the connection generation.
- `SparkplugNodeCommand.IsRebirthRequest` — a **pure, fail-safe** classifier: only a well-formed `Node Control/Rebirth = true` is a rebirth request; false / wrong datatype / wrong name / empty / malformed are all no-ops (never throws, never a side effect). **7 unit tests.**
- The actor's NCMD handler queues a **coalesced `HostCommand` rebirth** and never publishes or mutates protocol counters.

### Shared establish-core refactor (+ carry-forward #2)
- Extracted `EstablishNewConnectionAsync` from `BeginReplaySessionAsync`; both Begin and the transport-suspect rebirth branch use it. The atomic handoff / promotion semantics are byte-identical to the approved slice-4/5 code (all 23 Begin + all handoff/replay tests still green).
- **Carry-forward #2:** the generation-exhaustion (`long.MaxValue`) check now runs **before** the durable `bdSeq` reservation, so the exhausted-counter path can never consume a `bdSeq` with no possible CONNECT.

### `RebirthAsync` — two branches (carry-forward #4)
- **Gating:** retains the `ReplaySessionId` (wrong session → fail closed) and requires a **strictly increasing** epoch (non-increasing → fail closed).
- **Branch decision is the actor-owned latch, never the public reason** (§4.1): `SuspectAfterPromotion || !TryRebirth()` → transport-suspect; else healthy. When a host command and a transport loss coalesce, **transport-suspect wins**.
- **Healthy:** reuse the connection + **retain bdSeq**; re-emit NBIRTH `seq=0` for the new epoch; `AttemptHandoff.TryRebirth()` resets a `Live` authority back to `Promoted` so a future cutover can commit Live again; a drop that raced the re-birth fails closed (no new epoch on a dead connection); a healthy NBIRTH failure is **immediately fatal** (§4.5).
- **Transport-suspect:** abandon the old client (broker publishes its Will), reserve a **new bdSeq**, fresh CONNECT via the establish-core. (Pass 2 wraps this in the bounded budget.)

### Async idle disconnect → coalesced rebirth (carry-forward #1)
- The disconnect handler validates the generation (**stale generations ignored authoritatively**), atomically marks the authority suspect via the handoff, and — post-promotion only — queues **one** coalesced Core rebirth request (§4.3). Before an authoritative birth exists it is a no-op (the slice-4 pre-authoritative test still passes: zero rebirth requests).
- **Coalescing:** disconnect, NCMD, and a failed DATA send all route through `AttemptHandoff.TryClaimRebirth()`, so an episode emits exactly one `RequestRebirthAsync`. The slice-5 rebirth path was routed through the same claim (its tests still assert a single request).

**Evidence** (`SparkplugSessionActorRebirthTests`, 13 tests):
- healthy: `Rebirth_HealthyTransport_ReusesConnection_RetainsBdSeq_AdvancesEpoch`, `Rebirth_HealthyTransport_ReEmitsNBirthSeq0_WithRetainedBdSeq` (byte-parity), `Rebirth_HealthyNBirthFails_IsFatal_Faults`;
- suspect: `Rebirth_TransportSuspect_NewConnect_NewBdSeq_NewGeneration_RetiresOldClient`;
- gating: `Rebirth_WrongSession_FailsClosed`, `Rebirth_NonIncreasingEpoch_FailsClosed`;
- disconnect: `Disconnect_PostPromotion_RequestsOneCoalescedRebirth_Other`, `Disconnect_StaleGeneration_Ignored`;
- NCMD: `NodeCommand_RebirthTrue_RequestsHostCommandRebirth_NoSuspect`, `NodeCommand_NotRebirth_NoRequest`, `NodeCommand_StaleGeneration_Ignored`;
- coalescing: `Disconnect_ThenNodeCommand_CoalesceToOneRequest`.

---

## Design decisions surfaced for your ruling
1. **Healthy rebirth reuses the SAME handoff** (via `TryRebirth()` resetting `Live→Promoted`) rather than re-wiring fresh handlers. Rationale: the generation is unchanged (same connection), so the wired disconnect/NCMD handlers stay valid; a fresh handoff would require re-wiring against the async callback (a new race). A drop racing the healthy re-birth is caught by the post-NBIRTH `SuspectAfterPromotion` check (fail closed). Confirm this is acceptable vs. re-wiring.
2. **Coalescing via `TryClaimRebirth`** is per-handoff (per connection/session episode); a fresh session (new handoff) resets it. The slice-5 on-gate rebirth path now also claims, so a disconnect-then-send-failure emits one request. Confirm.
3. **Suspect rebirth is a single establishment attempt in pass 1** (throws/faults on failure). The bounded retry budget (§4.6/§4.7) wraps it in pass 2 — the delay seam and `RecoveringTransport` loop are deliberately deferred so this checkpoint stays focused.

## Deferred to pass 2 (explicitly)
- Carry-forward #5 (generation-exhaustion is done; the *bounded recovery* around the suspect branch is pass 2), #6 (the bounded complete-attempt recovery loop with gate-released backoff + recovery token), #7 (graceful `EndSessionAsync`: explicit NDEATH then clean DISCONNECT once, + Stop/Dispose idempotence completeness).

The exact `git show -W` diff (all 9 files, 0 ellipses) is attached. Pass 2 resumes after this checkpoint is reviewed.
