# K3 Slice 6 — Review Bundle (pass 2: bounded recovery + graceful End) — SLICE 6 COMPLETE

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `3c4a577` — *feat(sparkplug): K3 slice 6 pass 2 (slice 6 complete)*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass2-source-diff.md` (full `git show -W`).
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests `0/0`.
**Tests:** `504 passed / 0 / 0`, broker-free and **deterministic** (injected delay — no wall-clock). All prior tests stay green.

Pass 2 implements the deferred recovery/end half of slice 6. **This completes slice 6 and the K3 session-actor implementation.** No Core change.

---

## Bounded transport-recovery loop (plan v3 §4.6/§4.7)
The transport-suspect rebirth now retries the **whole** session establishment within the frozen budget:
- `TransportRecoveryMaxAttempts` (default 3) complete attempts; each consumes a **distinct generation + bdSeq** (via the shared establish-core), so a failed attempt's `bdSeq` is never reused.
- **Capped exponential backoff, no jitter** (`InitialDelay → …×2 → MaxDelay`).
- On budget exhaustion the establishment throw propagates → terminal `Failed`, with the **previous authority preserved** (candidate-only, from pass-1 r1 B3).

**Injected delay seam** — `Func<TimeSpan, CancellationToken, Task>` (default `Task.Delay`); tests inject instant/controllable backoff.

**Gate released during backoff, under a recovery token** — the loop releases the actor gate for the delay, always reacquires it, and revalidates `_activeRecoveryToken`. A lifecycle call (`End`/`Stop`/`Dispose`/cancel) that runs in the released-gate window **nulls the token**, so the recovery aborts (`OperationCanceledException`) instead of racing a competing transition. MQTT callbacks during recovery still only touch the handoff's atomic latches.

**Evidence** — `Rebirth_TransportSuspect_RecoversWithinBudget_NoFault_DistinctBdSeqPerAttempt` (attempt 1 fails, attempt 2 succeeds → no fault, bdSeq 2), `Rebirth_TransportSuspect_ExhaustsBudget_Faults_PreservesPreviousAuthority`, `Rebirth_Recovery_AbortedByStopDuringBackoff` (a deterministic TCS-gated delay lets `StopAsync` run in the backoff window → the recovery aborts on reacquire, actor `Stopped`).

## Graceful `EndSessionAsync`
Invalidate any in-flight recovery → publish **one** explicit NDEATH for the born session → **clean MQTT DISCONNECT** (broker discards the Will, so no second death) → retire the transport. Idempotent (no active session → no-op); death/disconnect failure is best-effort/diagnostic; no rebirth during shutdown (§4.5).

## Stop/Dispose idempotence + no second death
`StopAsync`/`DisposeAsync` invalidate the recovery token. After `EndSession` nulls the active session, `Stop`/`Dispose`/a second `End` retire nothing — no second death.

**Evidence** — `EndSession_PublishesNDeathThenCleanDisconnect_Once_RetiresSession`, `EndSession_Twice_SecondIsNoOp_NoSecondDeath`, `Stop_AfterEndSession_NoSecondDeath`, `EndSession_WithNoActiveSession_IsNoOp`.

## Carry-forward folded
The diagnostic substate is now normalized to `Suspect` (not `Replaying`) whenever a promoted or rebirth-committed authority is suspect (both the establishment-promotion and healthy-commit paths).

---

## Slice 6 — complete
| Pass | Content | Status |
|--|--|--|
| pass 1 (r0→r3) | Both rebirth branches, NCMD, coalesced disconnect, the single-lock control-plane handoff | ✅ approved |
| pass 2 | Bounded recovery loop + graceful End + idempotence + substate normalization | this bundle |

The K3 session actor now implements the full replay lifecycle: Begin → Replay/CatchUp/Live DATA → cutover → operational rebirth (healthy + transport-suspect with bounded recovery) → graceful End, with a race-free control plane and no Core change. The exact `git show -W` diff (3 files) is attached for line-level sign-off.
