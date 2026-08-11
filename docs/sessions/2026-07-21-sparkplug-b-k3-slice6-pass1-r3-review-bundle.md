# K3 Slice 6 pass 1 — Review Bundle r3 (single-lock handoff closes the two-word races)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `edbcbd5` — *fix(sparkplug): K3 slice 6 pass 1 review r3*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice6-pass1-r3-source-diff.md` (full `git show -W`).
**Build:** SparkplugB src `0/0`; tests `0/0`. **498 tests pass**, broker-free. All prior tests stay green.

The three r2 defects were all two-word (transport-state ↔ episode) races. r3 unifies the transport state, the control episode, and the pending reason under **ONE short internal lock** — the reviewer's explicitly-permitted "small synchronized control-episode object." The lock is only ever held for a few field assignments (never across an await), so there is no contention or deadlock risk. No Core change.

---

## R3.1 — the `TryCompleteRebirth → publish → complete` window is now one handoff
The healthy rebirth is `Rebirthing → RebirthCommitting → Active`:
- `TryCompleteRebirth` (under lock): `Rebirthing → RebirthCommitting`, **consumes** the fulfilled episode, and raises a `_committing` latch.
- While `_committing`, `TryTakeForQueue` is **suppressed** — an async control event/disconnect anywhere in the commit window re-arms a **fresh** episode but cannot queue against the old epoch.
- The caller publishes the new authority, then `FinishRebirthCommit` (`RebirthCommitting → Active`, or leaves `Suspect` if a drop raced) clears `_committing` and reports whether a fresh episode is pending — drained against the **new** epoch.

**Evidence** — `Rebirth_SecondNodeCommandDuringEpisodeCompletion_QueuesSecondRequest_NewEpoch` (NCMD in the window → second request, epoch 1) and `Rebirth_DisconnectDuringCommit_InstallsNewEpochSuspect_QueuesWake` (disconnect in the window → new epoch installed **suspect** + a queued wake against epoch 1, no DATA needed).

## R3.2 — Live commitment is atomic with the episode
`TryCommitLive` now requires **both** `state == Active` **and** `!_pending` in one locked check, so a healthy pending rebirth (NCMD/first-observed) racing the cutover prevents Live without fabricating suspicion. On a blocked commit the actor defers to the pending rebirth (drains it) and latches suspect only if the transport is actually suspect.

**Evidence** — `Cutover_NodeCommandRacesLiveCommit_NoLive_StaysHealthy` (`PreLiveCommitBarrier` injects an NCMD → not Live, not suspect, one HostCommand pending).

## R3.3 — the reason is bound to the episode
`MarkRebirthNeeded` installs the cause only when it **opens** a fresh episode; a coalescing event never overwrites it (first cause wins). `PendingReason` returns `Other` whenever the transport is suspect (suspect precedence).

**Evidence** — `Publish_FirstObservedThenNodeCommand_RequestReasonStaysSchemaChange` (a first-observed SchemaChange episode is not rewritten to HostCommand by a coalescing NCMD); `Establish_NodeCommandThenDisconnectBeforePublish_DrainsOnce_TransportSuspectWins` (suspect precedence).

---

## The handoff now
One lock guards: transport state `Establishing → Invalidated | Active ↔ Live | Active/Live → Rebirthing → RebirthCommitting → Active | {…} → Suspect`, plus `(_pending, _queued, _committing, _reason)`. Public reads (`IsInvalidated`, `SuspectAfterPromotion`, `RebirthPending`, `PendingReason`) and every transition take the lock, so no cross-word race remains.

The exact `git show -W` diff (3 files) is attached for line-level sign-off. Pass 2 (bounded recovery loop with the injected delay seam + recovery token, and graceful `EndSessionAsync` + Stop/Dispose idempotence) resumes once these semantics are locked.
