# K3 Slice 7 — Review Bundle r1 (NCMD classification, counter semantics, coherent snapshot, failure diagnostics)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `7df9e86` — *fix(sparkplug): K3 slice 7 review r1*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice7-r1-source-diff.md` (full `git show`, 7 files, 0 elision).
**Build:** SparkplugB src `0/0` (warnings-as-errors); solution `0` errors. **Regressions green: Core 1250, Host 225, Management 1149 (full project), SparkplugB 567** — broker-free, deterministic. **No Core change.**

Folds the four review blockers.

---

## B1 — NCMD classification + diagnostics

`SparkplugNodeCommand.Classify(payload)` now returns a **redacted** `SparkplugNodeCommandKind` instead of a bool: `RebirthRequested`, `RebirthRequestedWithUnknownExtras`, `IgnoredMalformed`, `IgnoredMissing`, `IgnoredNull`, `IgnoredWrongType`, `IgnoredFalse`. The actor's NCMD handler:
- actions a rebirth **once** for either actionable kind (a valid Rebirth = true, with or without unknown extras);
- for `…WithUnknownExtras`, still requests once and records the extras via the diagnostic code;
- tallies every ignored kind (`nodeCommandsIgnored`) and surfaces a sanitized `lastNodeCommandDiagnosticCode` (`"rebirth"`, `"rebirth+unknown-extras"`, `"ignored:false"`, `"ignored:null"`, `"ignored:wrong-type"`, `"ignored:missing"`, `"ignored:malformed"`) — **never a raw metric name or payload byte**.

**Tests:** `SparkplugNodeCommandTests` (one per kind + a secret-free-code guard over every kind); actor-level `NodeCommand_RebirthWithUnknownExtras_RequestsOnce_DiagnosesExtras`; `NodeCommand_IgnoredKind_TallyAndDiagnostic_NoRequest` (Theory: false/null/wrong-type/missing).

## B2 — stale + coalesced counters measure their documented events

**Stale** is now decided by **handoff identity** (`IsStaleCallback`): a callback is stale unless its handoff is the authoritative session's handoff **or** the current in-progress establishment handoff (a new `_establishingHandoff`, set when an attempt's client is created, cleared on promotion/abort). This catches a **replaced client's delayed callback carrying its own real generation** — which the concrete transport echoes, so the old `arg != captured-generation` check missed it. The `arg != captured-generation` case remains a silent defensive ignore (an inconsistent argument, not a stale-generation event).

**Coalescing** accounting moved to `MarkRebirthNeeded`, which now returns opened-new vs folded; a caller increments `rebirthRequestsCoalesced` only when a genuine new signal folds into an open episode. `DrainRebirthAsync` no longer counts — so a **re-drain from a blocked DATA/cutover path** (which carries no new signal) can no longer inflate the tally.

**Tests:** `Diagnostics_StaleCallbacks_FromReplacedClient_Counted_LiveUnaffected` (birth A → replace with B → A's real-generation delayed disconnect **and** NCMD both increment stale; B's epoch/suspect untouched); `Diagnostics_RepeatedCoalescingNodeCommands_DoNotInflateBeyondFolds` (one queued, exactly the genuine folds counted).

## B3 — coherent semantic snapshot + health mapping

Lifecycle/protocol/session are captured as **one mutually-consistent set** and published only at a completed **gated** transition (`PublishTransition`, under `_diagLock`, reading the atomic `_snapshot` + immutable `_activeSession`). A **read** (`DiagnosticsSnapshot`/`CheckHealthAsync`) overlays the independent, monotonic counters/timestamps/last-event codes onto that last coherent record — it **never reconstructs semantic state from independent field reads**, so the torn `HasSession=true + ProtocolState=Stopped` combination cannot be observed. `diagnosticsVersion` is a transition **change-token** and does **not** advance on a read.

Health now maps using **both** `HasSession` and `ProtocolState`: **Healthy** is only ready-no-session (`Running + Stopped + no session`) **or** active-Live (`Running + Live + session`); every other Running establishment/recovery/suspect state is **Degraded**. `lastStateChangeAt` is exposed in `Metrics`.

**Tests:** `Health_ReplayingBeforeCutover_IsDegraded`, `Health_Live_IsHealthy`, `Health_ReadyNoSession_IsHealthy`, `Diagnostics_Version_IsStrictlyMonotonicAcrossTransitions`, `Diagnostics_LastTransition_ReflectsPrecedingStateChange`, and the redaction guard.

## B4 — complete failure diagnostics + counter boundaries

- An **untyped** failure (illegal lifecycle transition / actor-loop exception with no `AdapterException`) now records a sanitized **fallback** `SPARKPLUG.ACTOR_FAILURE` code + timestamp via `RecordError`, so a Faulted actor **always** exposes a last-error code + time (§8). No message, exception type, or customer data.
- `publishFailures`/`birthFailures` now also count **in-transport (uncertain) send cancellations** — `SendAsync` invokes an `onSuspectSendFailure` callback in its OCE catch (the call was entered → suspect), and the direct NBIRTH publish counts on its OCE. A **pre-send** cancellation aborts at the gate before any send and counts **nothing**. A healthy-rebirth uncertain send that pivots to recovery is now counted too.
- `transportRecoveryAttempts` is incremented **after** the admission check inside `AttemptConnectionAsync`, so a disposal/supersession that rejects admission records **no** completed attempt.

**Tests:** `Diagnostics_UntypedFailure_RecordsSanitizedFallbackErrorCodeAndTime`; `Diagnostics_InTransportDataCancellation_IncrementsPublishFailures` + `…PreSendDataCancellation_CountsNoPublishFailure`; `Diagnostics_InTransportNBirthCancellation_IncrementsBirthFailures` + `…PreSendNBirthCancellation_CountsNoBirthFailure`; the r4 disposal test now asserts `TransportRecoveryAttempts` unchanged by the rejected re-admission.

---

## Frozen outage envelope (explicit, per the documentation requirement)

K3 health reports the supported envelope truthfully and does **not** claim legacy-MQTT-style indefinite store-and-forward outage parity:

- a **short** transport outage **within** `TransportRecoveryMaxAttempts` recovers automatically (no route fault);
- a **sustained** outage **beyond** the budget **terminally faults** the route (`transportRecoveryExhaustions`++, Unhealthy);
- recovery from that terminal fault currently requires an operator **configuration re-apply** (no auto-restart — §10.2);
- this is a **local** bounded-reconnect mitigation, **not** indefinite outage parity — the Core Degraded + store-and-forward follow-up (plan §13) is the path to parity.

## Accepted r0 work (retained)

Immutable rich snapshot (no ring buffer); no Sparkplug-local `System.Diagnostics.Metrics`; injected clock; redacted `AdapterError` copy with empty message; lifetime Interlocked tallies + separate current recovery ordinal/budget; honest `RecoveringTransport` during backoff; no credential/endpoint/topic/payload/value fields; `LastSuccessAt` from successful birth; full-project regression execution; no Core change.

---

## Slice 7 status

Pass 1 (r0) folded to r1 across the four blockers. The K3 session actor's operational/explainability surface is now correct: distinguishable NCMD diagnostics, identity-based stale accounting, non-inflating coalescing, a genuinely coherent (non-torn) versioned snapshot with a session-aware health mapping, and complete failure diagnostics with correct counter boundaries — all redacted, deterministic, and Core-clean. The exact `git show` diff (7 files) is attached for line-level sign-off. On approval, the §12 K3 exit gate + handoff can be reviewed.
