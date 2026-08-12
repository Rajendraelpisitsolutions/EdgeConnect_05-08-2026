# K3 Slice 6 pass 1 — Review Bundle r2 (control-plane semantics stabilized)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `a3425d7` — *fix(sparkplug): K3 slice 6 pass 1 review r2*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass1-r2-source-diff.md` (full `git show -W`; the one flagged `...` is a code comment "host command...", not a schematic omission).
**Build:** SparkplugB src `0/0`; tests `0/0`. **495 tests pass**, broker-free. All prior tests stay green.

Folds the four remaining control-plane boundaries + the NCMD test-contract fix. No Core change. Recovery loop + `EndSessionAsync` remain pass 2.

---

## R2.1 — healthy pending rebirth ≠ transport suspect
The handoff now exposes `SuspectAfterPromotion` (transport loss) and `RebirthPending` (episode pending) **separately**. The DATA/cutover gate branches:
- **suspect** → `FailWithRebirthAsync(latchSuspect: true)` (transport recovery);
- **healthy pending** (NCMD/first-observed) → block DATA, ensure the request is queued, accept nothing, no seq, **do not mark suspect** — the ensuing `RebirthAsync` stays a same-connection healthy re-birth.

**Evidence** — `Publish_WhenRebirthPendingFromNodeCommand_…` (now asserts `CurrentSessionSuspect == false`, `Configuration` category), `Cutover_WhenRebirthPendingFromNodeCommand_NoLive_NoSuspect`, `Publish_FirstObservedTwice_StaysHealthyPending_NotSuspect`, `NodeCommand_PendingRebirth_StaysHealthy_RebirthReusesConnection`.

## R2.2 — race-safe episode completion
The episode gains a `Completing` phase. The healthy rebirth publishes the new authority, calls `BeginEpisodeCompletion` (Queued→Completing), then `TryCompleteEpisode` (Completing→Idle). A control event landing during completion re-arms the episode to `Needed` (via `MarkRebirthNeeded`, which now handles Completing→Needed), so `TryCompleteEpisode` fails and the actor drains a **fresh** episode against the **new** epoch — a concurrent command is never erased by the reset.

**Evidence** — `Rebirth_SecondNodeCommandDuringEpisodeCompletion_QueuesSecondRequest_NewEpoch` (a `PreEpisodeCompleteBarrier` seam injects NCMD #2; the second request is queued against epoch 1).

## R2.3 — the pending reason is preserved (with suspect precedence)
The episode remembers its cause (`_pendingReason`); `DrainRebirthAsync` derives the reason from `PendingReason` — `HostCommand`/`SchemaChange`/`Other` — and **transport suspicion always reports `Other`** (it forces the new-CONNECT branch, so it takes precedence). An NCMD that lands in the post-promotion/pre-publication window now drains as `HostCommand`, not `Other`.

**Evidence** — `Establish_NodeCommandBeforePublish_DrainsAsHostCommand`, `Establish_NodeCommandThenDisconnectBeforePublish_DrainsOnce_TransportSuspectWins`.

## R2.4 — healthy-NBIRTH cancellation cleanup
The healthy re-birth NBIRTH now goes through the slice-5 uncertain-send boundary (`SendAsync`): an in-transport `OperationCanceledException` marks the reused handoff suspect (`Rebirthing → Suspect`) and rethrows — never stranding it in `Rebirthing`, never promoting the candidate epoch. A pre-send cancellation (at the gate) never enters `Rebirthing`. A genuine local NBIRTH failure with no transport loss stays fatal (§4.5).

**Evidence** — `Rebirth_HealthyNBirthPreCancelled_DoesNotSend_NotSuspect`, `Rebirth_HealthyNBirthInTransportCancellation_MarksSuspect_RetainsPriorEpoch_NotStuckRebirthing`.

## NCMD test-contract fix
`RebirthWrongDatatype` → `RebirthWrongValueArm` with a comment that the protobuf oneof value arm is authoritative (a value set on the Int arm is rejected regardless of any `Datatype` field).

---

The handoff is now: transport word `Establishing → Invalidated | Active ↔ Live ↔ Rebirthing → Suspect`, and an independent episode word `Idle → Needed → Queued → Completing → (re-armed) Idle`. The two are queried separately (`SuspectAfterPromotion`, `RebirthPending`, `PendingReason`).

The exact `git show -W` diff (4 files) is attached for line-level sign-off. Pass 2 (bounded recovery loop with the injected delay seam + recovery token, and graceful `EndSessionAsync` + Stop/Dispose idempotence) resumes once these episode/promotion semantics are locked.
