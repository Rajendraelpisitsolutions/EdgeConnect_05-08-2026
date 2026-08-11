# K3 Slice 7 — Review Bundle r2 (live control overlay, real attempt boundary, NCMD duplicate hardening)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `4ba868a` — *fix(sparkplug): K3 slice 7 review r2*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice7-r2-source-diff.md` (full `git show`, 4 files, 0 elision).
**Build:** SparkplugB src `0/0` (warnings-as-errors); solution `0` errors. **Regressions green: Core 1250, Host 225, Management 1149 (full project), SparkplugB 575** — broker-free, deterministic. **No Core change.**

Folds the two r1 re-review blockers plus the focused NCMD hardening.

---

## R2.1 — asynchronous control-plane changes are now in the snapshot

The semantic root (lifecycle/protocol/session) still publishes **only** at a completed gated transition (no torn reads). What changed: `DiagnosticsSnapshot` now also applies a **live operational-control overlay** on read:

- `SuspectTransport`, `PendingRebirth`, `PendingRebirthReason` are read **live from the authoritative handoff** (under the handoff's own lock, via its existing `SuspectAfterPromotion` / `RebirthPending` / `PendingReason`), **bound by connection generation** — the overlay is applied only when `_activeSession.TransportGeneration == semantic.ConnectionGeneration`, so a promotion-window swap can never pair a new handoff with an old lifecycle root;
- `TerminalDisposed` is read **live from `DisposalWon`**, so it is true the instant disposal wins — before the terminal gated publication runs.

So an **asynchronous disconnect/NCMD** that latches suspect or opens a pending episode, or a disposal that has won while retirement is still blocked, is **visible immediately** — without reconstructing lifecycle/session from independent reads.

**Health is reordered** to the frozen mapping, evaluated against the live control state:

```
Failed                                                   → Unhealthy
Running + (disposal in progress OR suspect OR pending
           OR transitional protocol)                     → Degraded
Running + no session + Stopped                           → Healthy
Running + active session + Live                          → Healthy
```

A Live session with a pending NCMD rebirth (DATA blocked) — or a Live session whose transport just dropped async — is now correctly **Degraded**, not Healthy.

**Tests:** `Health_LiveThenAsyncDisconnect_IsDegraded_SuspectAndPending` (suspect + pending `Other`); `Health_LiveThenValidNodeCommand_IsDegraded_PendingNotSuspect` (pending `HostCommand`, not suspect); `Health_RepeatedBlockedNodeCommands_DoNotChangeControlStateOrHealth` (no new request, stays Degraded); `Health_AfterHealthyRebirth_ClearsPendingAndSuspect`; `Health_DisposalWinsWhileRetirementBlocked_TerminalDisposed_NotHealthy`.

## R2.2 — recovery attempt counted only at the real attempt boundary

`transportRecoveryAttempts` and `currentRecoveryAttempt` are now incremented/published **inside `AttemptConnectionAsync`**, after the generation-capacity check, the bdSeq reservation, the request build, and the client creation — **immediately before CONNECT**. Therefore:

- generation exhaustion, a `ReserveNextBdSeq` store failure, a connect-request/factory failure (fatal preparation), or a disposal-rejected admission record **no** attempt and advertise **no** ordinal;
- a CONNECT / SUBSCRIBE / NBIRTH failure counts as exactly **one** attempt.

`LastRecoveryFailureCode` is now recorded on **each failed retryable attempt before backoff** (not only at final exhaustion), so the failure that caused the current delay is visible **during** it.

**Tests:** `Diagnostics_ConnectFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt` (attempts == 1, `LastRecoveryFailureCode == TRANSPORT_CONNECT_FAILED` visible during backoff, then 2 on recovery); `Diagnostics_StoreReserveFailureDuringRecovery_CountsNoAttempt`; the generation-exhaustion test now asserts `TransportRecoveryAttempts == 0`; the r4 disposal test already asserts attempts unchanged by a rejected admission; the exhaustion test retains the final code and resets the ordinal to 0.

## Focused NCMD hardening

Duplicate `Node Control/Rebirth` metrics now classify as **`IgnoredAmbiguous`** (`"ignored:ambiguous"`) — order-independent, no action taken — instead of silently actioning the first. **Test:** `Classify_DuplicateRebirthMetrics_IsIgnoredAmbiguous_OrderIndependent` (both orderings ignored).

---

## Frozen outage envelope (explicit — documentation requirement)

K3 health reports the supported envelope truthfully and does **not** claim legacy-MQTT-style indefinite store-and-forward outage parity:

- an outage **within** the configured actor recovery budget (`TransportRecoveryMaxAttempts`) may recover automatically (no route fault);
- budget **exhaustion terminally faults** the replay route (`transportRecoveryExhaustions`++, Unhealthy);
- recovery from that terminal state currently requires an operator **configuration re-apply** (no auto-restart — plan §10.2);
- K3 does **not** yet provide legacy-MQTT-style **indefinite Degraded / store-and-forward outage parity** — that is the Core Degraded + store-and-forward follow-up (plan §13).

---

## Accepted r1 work (retained)

Redacted NCMD classification + unknown-extra diagnosis; handoff-identity stale detection; coalescing counted at `MarkRebirthNeeded`; semantic transition publication (no lifecycle/session reconstruction on read); Healthy requiring both session presence and protocol state; sanitized fallback `SPARKPLUG.ACTOR_FAILURE`; in-transport DATA/NBIRTH cancellation tallies; pre-gate cancellation counting nothing; `lastStateChangeAt` exposure; full-project regression execution; no Core change.

---

## Slice 7 status

Pass folded r0 → r1 → r2. The operational/explainability surface is now correct **including asynchronous control-plane visibility**: health reflects suspect/pending/disposal the instant they occur (bound coherently to the authority), and recovery-attempt accounting fires only at the true CONNECT boundary with the causing failure code visible during backoff. All redacted, deterministic, Core-clean. The exact `git show` diff (4 files) is attached for line-level sign-off. On approval, the §12 K3 exit gate + handoff can be reviewed.
