# K3 Slice 7 — Review Bundle r3 (atomic authority-bound handoff overlay + symmetric attempt evidence)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `b3b19a9` — *fix(sparkplug): K3 slice 7 review r3*
**Exact source diff:** `docs/sessions/2026-07-25-sparkplug-b-k3-slice7-r3-source-diff.md` (full `git show`, 2 files, 0 elision).
**Build:** SparkplugB src `0/0` (warnings-as-errors); solution `0` errors. **Regressions green: Core 1250, Host 225, Management 1149 (full project), SparkplugB 581** — broker-free, deterministic. **No Core change.**

Folds the R3.1 coherence blocker and the R3.2 test completions.

---

## R3.1 — one atomic, authority-bound control read

**Torn triple (fixed).** The live overlay read the handoff through **three separate lock acquisitions** (`SuspectAfterPromotion` / `RebirthPending` / `PendingReason`), so a disconnect racing between them could expose a triple that never existed atomically — e.g. `suspect=false` with `reason=Other`. New **`AttemptHandoff.ReadDiagnostics()`** captures the `(suspect, pending, reason)` triple under **one** `_sync` acquisition and returns an immutable `HandoffDiagnostics`. Both `PublishTransition` (baked values) and the live overlay now use this single atomic read, so the control triple is never internally torn.

**Authority binding strengthened.** The overlay was bound on connection generation alone. A healthy same-connection rebirth **retains the generation while advancing the epoch**, so during the authority-swap window a new-epoch control condition could be attached to the old-epoch semantic root. The overlay now binds on the **full authority** — `SessionId` **and** `Epoch` **and** `ConnectionGeneration` must all match — otherwise the semantic root's own coherent baked values stand. Session+epoch+generation are the frozen authoritative identity.

**Deterministic evidence** (a `PostAuthorityPublishBarrier` test seam makes the sub-instruction swap window observable):
- `Diagnostics_HostCommandThenDisconnect_ControlTripleIsCoherent` — after a HostCommand pending then a racing disconnect, the snapshot is `suspect=true, pending=true, reason=Other`; the forbidden `suspect=false + reason=Other` can never appear.
- `Diagnostics_HealthyEpochPromotion_DoesNotLeakNewEpochControlOntoOldRoot` — during the epoch-0→1 swap window (`_activeSession` epoch 1, semantic still epoch 0), a fresh NCMD opens an epoch-1 episode; the overlay does **not** attach it to the epoch-0 root (`Epoch=0`, `PendingRebirth=false`); after publish, the epoch-1 authority's overlay is coherent and visible (`Epoch=1`, `PendingRebirth=true`).
- Retained: `Health_LiveThenAsyncDisconnect…`, `Health_LiveThenValidNodeCommand…`, `Health_DisposalWinsWhileRetirementBlocked…`, `Health_AfterHealthyRebirth_ClearsPendingAndSuspect`.

## R3.2 — symmetric attempt-boundary + coalescing evidence

The `transportRecoveryAttempts` code location (after generation/store/request/factory preparation, immediately before CONNECT) was accepted; these are the requested evidence completions:
- `Diagnostics_EstablishmentFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt` — **Theory over connect / subscribe / nbirth**: each records exactly **one** attempt and its causing code (`TRANSPORT_CONNECT_FAILED` / `TRANSPORT_SUBSCRIBE_FAILED` / `BIRTH_PUBLISH_FAILED`) is visible **during** backoff; then 2 on recovery.
- `Diagnostics_FactoryFailureDuringRecovery_CountsNoAttempt_OrdinalZero` — a transport-factory failure records **no** attempt and ordinal `0`.
- `Diagnostics_StoreReserveFailureDuringRecovery_CountsNoAttempt` and the generation-exhaustion test (`TransportRecoveryAttempts == 0`) — fatal preparation counts no attempt.
- `Diagnostics_CutoverRedrainWhilePending_DoesNotInflateCoalesced` — a cutover re-drain while pending carries no new signal and does **not** increment `rebirthRequestsCoalesced`; `Health_RepeatedBlockedNodeCommands…` covers the DATA-block/repeat case.
- Final exhaustion retains the final failure code and resets the current ordinal to 0 (`Diagnostics_TransportRecovery_Exhaustion…`).

---

## Frozen outage envelope (explicit — health/handoff documentation requirement)

K3 health reports the supported envelope truthfully and does **not** claim legacy-MQTT-style indefinite store-and-forward outage parity:

- an outage **within** the configured actor recovery budget (`TransportRecoveryMaxAttempts`) may recover automatically (no route fault);
- exhausting that budget **terminally faults** the replay route (`transportRecoveryExhaustions`++, Unhealthy);
- recovery from that terminal state currently requires an operator **configuration re-apply** (no auto-restart — plan §10.2);
- K3 does **not** yet provide legacy-MQTT-style **indefinite Degraded / store-and-forward outage parity** — that is the Core Degraded + store-and-forward follow-up (plan §13). This limitation is frozen in the K3 plan and is carried into the health documentation and the final K3 handoff.

---

## Accepted r1/r2 work (retained)

Redacted NCMD classification (+ unknown-extra diagnosis, + ambiguous-duplicate rejection); handoff-identity stale detection; coalescing counted at `MarkRebirthNeeded`; semantic transition publication (no lifecycle/session reconstruction on read); immediate Degraded after Live disconnect/NCMD; live `TerminalDisposed`; health mapping on lifecycle + session presence + control conditions; sanitized fallback `SPARKPLUG.ACTOR_FAILURE`; in-transport DATA/NBIRTH cancellation tallies, pre-gate cancellation counting nothing; real recovery-attempt boundary before CONNECT; per-attempt `LastRecoveryFailureCode`; `lastStateChangeAt` exposure; full-project regression execution; no Core change.

---

## Slice 7 status

Folded r0 → r1 → r2 → r3. The diagnostic snapshot is now fully coherent: the lifecycle/session root is published atomically at gated transitions, and the operational-control triple is read in one atomic handoff acquisition and overlaid only when bound to the complete authority (session+epoch+generation) — so no field combination that never existed can be observed, including across a healthy same-generation epoch promotion. Recovery-attempt accounting fires only at the true CONNECT boundary with the causing code visible during backoff, across all of connect/subscribe/nbirth/factory/store/generation. All redacted, deterministic, Core-clean. The exact `git show` diff (2 files) is attached for line-level sign-off. On approval, this closes the last K3 slice — the §12 exit gate + K3 handoff follow.
