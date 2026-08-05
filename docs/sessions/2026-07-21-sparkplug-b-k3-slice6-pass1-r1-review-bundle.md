# K3 Slice 6 pass 1 — Review Bundle r1 (control-plane races fixed)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `b37b699` — *fix(sparkplug): K3 slice 6 pass 1 review r1*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass1-r1-source-diff.md` (full `git show -W`, 0 ellipses).
**Build:** SparkplugB src `0/0`; tests `0/0`. **487 tests pass**, broker-free. All prior Begin/handoff/replay/rebirth tests stay green.

Folds the four control-plane/promotion races + the NCMD null fix. No Core change. Recovery loop + `EndSessionAsync` remain pass 2.

---

## The `AttemptHandoff` redesign (the spine of B1/B2)
One lock-free **transport/authority state word** — `Establishing → Invalidated | Active → Live | Rebirthing → Active | Suspect` — plus a **separate control-request episode** word — `Idle → Needed → Queued`. Transport health and the Core-request coalescing are now independent concerns.

## B1 — episode is per-rebirth, DATA-visible, resettable, failure-safe
- Replaced the permanent `_rebirthClaimed` bool with the episode (`MarkRebirthNeeded`/`TryTakeForQueue`/`ReleaseQueue`/`ResetEpisode`).
- **`PublishBlocked` = suspect OR episode pending** now gates DATA and cutover — a host NCMD stops new DATA and consumes no seq.
- The episode **resets only on a successful rebirth promotion**; if `RequestRebirthAsync` throws before acceptance, the claim is **released** so a later request requeues.

**Evidence** — `Rebirth_Healthy_ThenSecondNodeCommand_StartsNewEpisode_QueuesSecondRequest`, `NodeCommand_Repeated_BeforeRebirth_CoalesceToOneRequest`, `HostRequestFailure_ReleasesClaim_AllowsLaterRebirthRequest`, `Publish_WhenRebirthPendingFromNodeCommand_AcceptsNothing_NoSeq_NoPublish`.

## B2 — atomic healthy-rebirth completion vs. a racing disconnect
`TryBeginRebirth` (Active/Live → Rebirthing) then `TryCompleteRebirth` (Rebirthing → Active). A disconnect racing the completion moves the word to `Suspect`, the completion CAS **fails**, and the actor **pivots** to the transport-suspect new-CONNECT branch (new bdSeq). Host command + transport loss coalesces to suspect. A `PreRebirthCommitBarrier` seam drives the race deterministically; an `OnPublishOnce` hook drives "disconnect during NBIRTH".

**Evidence** — `Rebirth_DisconnectBeforeHealthyCompletion_PivotsToSuspect_NewConnect`, `Rebirth_DisconnectDuringHealthyNBirth_PivotsToSuspect`, `Rebirth_NodeCommandThenDisconnect_UsesNewConnectionAndBdSeq`, `Rebirth_DisconnectAfterHealthyPromotion_RequestsAgainstNewEpoch`.

## B3 — candidate-only establishment preserves the previous authority
`EstablishNewConnectionAsync` now **returns** a candidate `ActiveSession` and never writes `_activeSession`; the caller (`PromoteAndDrainAsync`) promotes it. The suspect rebirth retires the old transport but **keeps** `_activeSession` until a fresh candidate succeeds — a failed CONNECT/SUBSCRIBE/NBIRTH replacement leaves the previous epoch/bdSeq/manifest/baseline as the recorded authority.

**Evidence** — `Rebirth_SuspectReplacementFails_PreservesPreviousAuthority` (Theory: connect/subscribe/nbirth) asserts the previous session/epoch/bdSeq/manifest remain after the failure.

## B4 — pending-disconnect drain across establishment promotion
A disconnect between the promotion CAS and `_activeSession` publication is retained (`MarkRebirthNeeded`) and **drained after publication** (`DrainRebirthAsync`), so an idle drop always wakes Core with exactly one request — no DATA arrival needed. A `PostPromotionBarrier` seam lands the disconnect in that window.

**Evidence** — `Establish_DisconnectAfterPromotionBeforePublish_DrainsExactlyOneRebirth`.

## Focused NCMD fix
`SparkplugNodeCommand.IsRebirthRequest` now rejects an explicitly-null `Node Control/Rebirth` metric (`&& !metric.IsNull`). Test: `IsRebirthRequest_RebirthTrueButExplicitlyNull_ReturnsFalse`.

---

## Notes on the rulings
- Same-handoff reuse for healthy rebirth is retained, now with the `Rebirthing` state + atomic completion + resettable episode (per your conditional approval).
- Coalescing is now per pending/in-progress episode; DATA observes it via `PublishBlocked`.
- `§4.5` fatal is preserved for a *genuine* local healthy-rebirth NBIRTH failure (no transport loss); a *disconnect* during the healthy NBIRTH pivots to suspect (your B2 requirement).

The exact `git show -W` diff (5 files, 0 ellipses) is attached for line-level sign-off. Pass 2 (bounded recovery loop + graceful End/Stop) resumes after this checkpoint is locked.
