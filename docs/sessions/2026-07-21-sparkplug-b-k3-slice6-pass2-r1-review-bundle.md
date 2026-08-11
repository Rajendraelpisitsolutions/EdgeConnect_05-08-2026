# K3 Slice 6 pass 2 — Review Bundle r1 (retry classification, safe Dispose, gated End)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `40d1351` — *fix(sparkplug): K3 slice 6 pass 2 review r1*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass2-r1-source-diff.md` (full `git show -W`).
**Build:** SparkplugB src `0/0`; tests `0/0`. **520 tests pass**, broker-free and deterministic (injected delay). All prior tests stay green.

Folds the four pass-2 boundaries. No Core change.

---

## B1 — only retry transport failures; fatal preparation fails once
`EstablishNewConnectionAsync` is split into:
- **`PrepareBirth(snapshot)`** — non-retryable: snapshot planning, alias-store resolution, manifest/baseline + bdSeq-alias. Run **once**, before the loop.
- **`AttemptConnectionAsync(prepared, …)`** — one retryable attempt: generation-exhaustion check (before bdSeq → consumes none, fatal), fresh bdSeq + Will + client + generation, CONNECT → SUBSCRIBE → NBIRTH → promotion.

The recovery loop retries **only** `IsRetryableEstablishmentFailure` (a narrow whitelist: `TransportConnectFailed`, `TransportSubscribeFailed`, `BirthPublishFailed`, `SessionSuspectDuringBegin`). A store/mapping/alias/config/generation failure fails **once with no backoff**.

**Evidence** — `Rebirth_Recovery_FatalPreparationFailure_FailsOnce_NoBackoff` (pre-epoch snapshot → fails once, 0 delays), `Rebirth_Recovery_TransportFailure_RetriesWithinBudget` (Theory: connect/subscribe/nbirth all retry then recover), `Rebirth_Recovery_DistinctGenerationAndBdSeqPerAttempt` (generation 3, bdSeq 2 after one failed + one good attempt), `Rebirth_Recovery_DelaySequence_IsCappedExponential_NoDelayAfterLastAttempt` (delays == [1000ms, 2000ms] for budget 3), `Rebirth_Recovery_MaxAttemptsOne_FailsWithNoBackoff`.

## B2 — exactly-one recovery + safe Dispose
- A re-entrant recovery (a second `RebirthAsync` acquiring the gate in a backoff window) is **rejected** (`SESSION_ALREADY_ACTIVE`) and never overwrites the token.
- `DisposeAsync` is **atomically idempotent** (`Interlocked` on `_disposed`), serializes the retire on the gate, and **does not dispose the gate** — so a recovery parked in `BackoffWithGateReleasedAsync` safely reacquires it and observes the nulled token (no `ObjectDisposedException`). `Stop`/`End`/`Dispose` each null the token.

**Evidence** — `Rebirth_Recovery_DisposeDuringBackoff_AbortsCleanly_NoObjectDisposed`, `Dispose_Concurrent_RetiresTransportOnce` (DisposeCount == 1), `Rebirth_Recovery_CancellationDuringBackoff_PreventsNextAttempt` (caller-token cancel in the delay → the next attempt never runs).

## B3 — NDEATH-success-gated clean DISCONNECT
`EndSessionAsync` issues the clean DISCONNECT **only** on a confirmed local NDEATH publish; an unconfirmed/uncertain NDEATH (`false`/exception/cancellation) **aborts-disposes** instead, so the broker publishes the Will — never "born with no death."

**Evidence** — `EndSession_NDeathReturnsFalse_NoCleanDisconnect_AbortDisposes`, `EndSession_NDeathThrows_NoCleanDisconnect_AbortDisposes`, `EndSession_Success_OrderIsNDeathThenDisconnectThenDispose_BytesMatchBdSeq` (exact order `publish NDEATH → disconnect → dispose`; NDEATH bytes == `EncodeNDeath(bdSeq)`, no seq).

## B4 — authoritative End + ready-no-session
`EndSessionAsync` validates the `ReplaySessionId` **and** `RouteId` before tearing down — a stale End from a superseded session/route is a no-op. After retirement the actor is **ready-no-session**: coarse `Running`, protocol `Stopped`, health `Healthy`, and a fresh `Begin` succeeds.

**Evidence** — `EndSession_StaleIdentity_DoesNotEndActiveSession` (Theory: wrong session / wrong route), `EndSession_Success_ReadyNoSession_HealthyAndRebeginnable`.

---

## Slice 6 status
Pass 1 approved; pass 2 committed (r0) then folded (r1, this bundle). The K3 session actor now implements the full lifecycle — Begin → Replay/CatchUp/Live DATA → cutover → operational rebirth (healthy + transport-suspect with bounded, gate-released, single-owner recovery) → graceful End — with a race-free single-lock control plane, authoritative lifecycle gating, and no Core change. The exact `git show -W` diff (2 files) is attached for line-level sign-off.
