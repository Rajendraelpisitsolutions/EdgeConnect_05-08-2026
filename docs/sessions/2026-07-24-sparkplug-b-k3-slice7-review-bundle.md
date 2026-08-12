# K3 Slice 7 — Review Bundle (health, diagnostics, failure sweep — the final slice)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `c136883` — *feat(sparkplug): K3 slice 7 - health, diagnostics, counters, redaction*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice7-source-diff.md` (full `git show`, 4 files, 0 elision).
**Build:** SparkplugB src `0/0` (warnings-as-errors); solution `0` errors. **Regressions green: Core 1250, Host 225, Management 1149 (full project), SparkplugB 556** — all broker-free and deterministic. **No Core change.**

This is the last K3 slice: the operational/explainability surface on top of the completed state machine (slices 1–6).

---

## 1. The coherent, versioned diagnostic snapshot (§8)

New `internal sealed record SparkplugActorDiagnostics` — the actor's **complete redacted state at one coherent observation point** (per the approved r-answer: NOT a historical transition log). One immutable record, atomically published under `_diagLock`, carrying a **strictly monotonic `Version`** so health and diagnostics can never report a field combination that never existed.

Fields: coarse `State` + fine `ProtocolState`; the **immediately preceding transition** (`PreviousState`, `PreviousProtocolState`, `LastStateChangeAt`, `LastTransitionReasonCode`); `TerminalDisposed`; session authority (`HasSession`, `SessionId`, `Epoch`, `RouteId`, `ConnectionGeneration`, `LastIssuedGeneration`, `BdSeq`, `NextSeq`); recovery progress (`SuspectTransport`, `PendingRebirth`(+reason), `CurrentRecoveryAttempt`, `RecoveryAttemptBudget`, `LastRecoveryFailureCode`); last-event timestamps (birth/publish/rebirth-request/error); and a **sanitized** `LastError` (code/category only, **Message cleared**) plus lifetime counters.

**Redaction is by construction** — the record only holds ids/generations/bdSeq/epoch/counts/timestamps/sanitized-codes. Never a credential, broker endpoint, client id, topic, payload byte, or metric value. A named test renders the whole health surface to a string and asserts the password, username, and broker host do not appear.

**Coherence + freshness.** Transitions rebuild the record under `_diagLock` (capturing the transition's clock + reason). Counters and liveness timestamps are updated on the hot path with **Interlocked only** (no per-DATA record rebuild); the full record is rebuilt lazily **on read** (`DiagnosticsSnapshot`/`CheckHealthAsync`, both infrequent), so a health poll always sees live counters without hot-path cost. A read is not a state transition — the "preceding transition" fields are preserved.

## 2. Monotonic lifetime counters (§7, §8)

All `Interlocked`, **per actor instance, never reset on rebirth**, exposed in `AdapterHealth.Metrics` under **stable keys** (Studio, support bundles, and K4 meters may depend on them):

`staleDisconnectCallbacks`, `staleNodeCommandCallbacks`, `rebirthRequestsQueued`, `rebirthRequestsCoalesced`, `healthyRebirths`, `transportRecoveryStarts`, `transportRecoveryAttempts`, `transportRecoverySuccesses`, `transportRecoveryExhaustions`, `publishFailures`, `birthFailures`, `deathPublishFailures`.

`currentRecoveryAttempt` (ordinal **within the active episode**, `0` when none) and `recoveryAttemptBudget` are surfaced as **current state**, distinct from the lifetime `transportRecoveryAttempts` tally. `rebirthRequestsQueued` counts a Core request actually issued; `rebirthRequestsCoalesced` counts a signal that folded into an already-open episode (pending, queue already taken).

**System.Diagnostics.Metrics is deferred to K4** (per the approved r-answer) — the future Core meters will project from this same counter source, not a second accounting path.

## 3. Health mapping (§8) + honest recovery substate

`CheckHealthAsync` reads one coherent snapshot and maps: **Unhealthy** ← `Failed`; **Healthy** ← `Running` and (no session **or** `Live`); **Degraded** ← any other active-session substate (connecting/subscribing/birthing/replaying/catching-up/rebirthing/**recovering**/suspect). It populates `AdapterHealth.LastError` (sanitized) and `LastSuccessAt` (last birth), uses the **injected clock** for `CheckedAt` (was `DateTime.UtcNow` — a determinism fix), and emits the stable metric keys.

One small behavior improvement: the recovery loop now sets `RecoveringTransport` **before** each backoff, so the substate during the delay window is honest instead of the failed attempt's stale `Connecting`/`Subscribing`/`Birthing`.

## 4. Redaction, base fail-closed, capability (already present, re-verified)

Credential/Will-byte redaction (config `ToString`, connect-request `ToString`, transport-error normalization) and the health snapshot being credential-free are covered; the context-free `PublishAsync(points, ct)` and `UpdateCurrentValuesAsync` fail closed; advertised `DeliveryCapabilities` = store-and-forward / `LocalTransport`. Slice 7 adds the health-snapshot redaction guard.

---

## 5. Tests (15 new diagnostics tests + 2 augmented DATA tests)

Health-level matrix (`Health_ReadyNoSession_IsHealthy`, `Health_Live_IsHealthy`, `Health_ReplayingBeforeCutover_IsDegraded`, `Health_AfterFatalBirthFailure_IsUnhealthy_WithSanitizedError`); `Diagnostics_SessionFields_CoherentWhenBorn`; `Diagnostics_Version_IsStrictlyMonotonicAcrossTransitions`; `Diagnostics_LastTransition_ReflectsPrecedingStateChange`; stale disconnect/NCMD counters; `Diagnostics_RebirthRequest_QueuedThenCoalesced`; `Diagnostics_HealthyRebirth_IncrementsCounter_AndBirthTimestamp`; `Diagnostics_TransportRecovery_CountsStartsAttemptsSuccesses`; `Diagnostics_TransportRecovery_Exhaustion_CountsExhaustionAndFaults`; `Diagnostics_CurrentRecoveryAttempt_TracksOrdinalDuringBackoff`; `Diagnostics_HealthSnapshot_NeverExposesCredentialsOrEndpoint`; plus `publishFailures`/`lastDataPublishAt` assertions on the existing DATA success/failure tests.

## 6. §11 acceptance matrix + every-phase failure sweep

The §11 matrix is carried green across slices 1–7. Failure at each phase is exercised: pre-promotion disconnect (establishment drain), idle/Live async disconnect (rebirth request + wake), disconnect during the healthy-rebirth commit (atomic pivot to suspect), disconnect during recovery backoff (r4/r5 supersession), DATA send failure (suspect + rebirth + `publishFailures`), NBIRTH failure (`birthFailures` + fault), NDEATH-uncertain end (`deathPublishFailures` + Will-only). Determinism holds throughout: injected clock + injected delay, `TaskCompletionSource`/explicit transport hooks, no `Thread.Sleep`, no external broker.

---

## Slice 7 status

The K3 session actor now has a complete operational/explainability surface — coherent versioned health/diagnostics, redacted lifetime counters, honest recovery substate — with no Core change and full regressions green. This closes the last K3 slice pending your sign-off. The exact `git show` diff (4 files) is attached for line-level review.

### Open scope note (surfaced, not decided)
"Full actor trace" was implemented as the **complete point-in-time coherent snapshot** (per the approved r-answer), **not** a historical transition ring buffer. A bounded transition ring can be added later as a separate observability feature if field evidence shows single-snapshot diagnostics are insufficient.
