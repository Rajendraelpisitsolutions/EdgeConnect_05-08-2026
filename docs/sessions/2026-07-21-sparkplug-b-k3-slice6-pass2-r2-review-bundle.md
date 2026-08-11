# K3 Slice 6 pass 2 — Review Bundle r2 (nonfatal recovery, cancellation normalization, terminal disposal)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `07354fe` — *fix(sparkplug): K3 slice 6 pass 2 review r2*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass2-r2-source-diff.md` (full `git show -W`).
**Build:** SparkplugB src `0/0`; tests `0/0`. **529 tests pass**, broker-free and deterministic. All prior tests stay green.

Folds the three remaining lifecycle defects. No Core change.

---

## R2.1 — nonfatal exactly-one recovery
The re-entrant-recovery rejection now throws `OperationCanceledException` (not a typed `AdapterException`), so `RebirthAsync`'s OCE passthrough does **not** `SetFaulted`. A second `RebirthAsync` entering during recovery A's backoff window is rejected **nonfatally**, does not replace A's token (regardless of B's epoch), and A resumes and succeeds.

**Evidence** — `Rebirth_SecondRebirthDuringBackoff_NonFatalReject_RecoveryASucceeds` (B rejected with OCE, actor not `Failed`; release → A recovers, epoch 1 authoritative).

## R2.2 — cancellation normalizes to the retained suspect authority
Cancellation **anywhere** in the recovery (CONNECT/SUBSCRIBE/NBIRTH **or** backoff) is caught in `SuspectRebirthAsync`. If no lifecycle call superseded the token, the diagnostic substate is normalized to `Suspect` — never a stale `Connecting`/`SubscribingNcmd`/`Birthing` — while the previous authority (session/epoch/manifest/baseline/bdSeq) is retained; then cancellation is rethrown. A lifecycle call that already published its state is not overwritten.

**Evidence** — `Rebirth_Recovery_CancellationDuringBackoff_PreventsNextAttempt` now asserts `State == Running`, `ProtocolState == Suspect`, `CurrentEpoch == 0` (retained), `CurrentSessionSuspect == true`, no next attempt.

## R2.3 — terminal, non-resurrectable disposal
- A `_disposed` guard (`ThrowIfDisposed`) on `Begin`/`Rebirth`/`Publish`/`CompleteCatchUp`/`Start` fails closed **after** the gate is acquired (so a Begin queued behind Dispose cannot resurrect the actor) and **without faulting** (an `ObjectDisposedException` passthrough bypasses `SetFaulted`). `Stop`/`End` become no-ops after disposal.
- `DisposeAsync` publishes a coherent terminal `Stopped/Stopped` snapshot and uses a **shared completion task** (`_disposeTask` installed via `Interlocked.CompareExchange`), so concurrent callers all await the **same** retirement — caller B never completes before caller A's retirement finishes. The gate is still not disposed (a parked recovery can reacquire it to observe the nulled token).

**Evidence** — `LifecycleCall_AfterDispose_FailsClosed_NoStateMutation` (Theory: begin/rebirth/publish → `ObjectDisposedException`, terminal `Stopped/Stopped`, no new birth), `Dispose_LeavesCoherentTerminalStoppedState`, `Dispose_Concurrent_RetiresTransportOnce`.

---

## Focused evidence completions
- `Rebirth_Recovery_BackoffReachesAndRepeatsMaxDelayCap` — initial 100ms, ×2, cap 150ms, budget 4 → delays `[100, 150, 150]` (cap reached and repeated).
- `EndSession_NDeathCancellationAfterTransportEntry_NoCleanDisconnect` — an in-transport NDEATH cancellation → no clean DISCONNECT (abort-dispose, Will preserved).
- `EndSession_ThenNewSession_StaleEndForOldSession_LeavesNewSessionIntact` — a stale End for session 1 cannot end session 2.
- `Rebirth_Recovery_DelayedCallbackFromFailedClient_CannotAffectReplacement` — a delayed disconnect/NCMD from the failed recovery client (stale generation) does not affect the live session.
- End-during-backoff abort is covered by the shared token-invalidation path (`Rebirth_Recovery_AbortedByStopDuringBackoff` proves the Stop variant; End/Dispose null the same token).

---

## Slice 6 status
Pass 1 approved; pass 2 folded across r0 → r1 → r2. The K3 session actor's lifecycle is now complete and hardened: bounded, gate-released, single-owner recovery with correct retry classification and cancellation semantics; NDEATH-gated graceful End; authoritative End identity; and terminal, non-resurrectable disposal — all race-free under the single-lock control plane, with no Core change. The exact `git show -W` diff (2 files) is attached for line-level sign-off.
