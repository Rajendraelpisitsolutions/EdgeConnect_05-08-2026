# K3 Slice 5 — Review Bundle (Replay/CatchUp/Live DATA + catch-up cutover)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `054c18f` — *feat(sparkplug): K3 slice 5 — Replay/CatchUp/Live DATA + catch-up cutover*
**Plan (frozen):** `docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md` (§1.2–1.9, §4.2, §4.4, §5.1–5.3, §7, §9 slice 5)
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice5-source-diff.md` (full `git show -W`, attached for line-level sign-off).
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests `0/0`.
**Tests:** `440 passed / 0 / 0`, broker-free. Slice 6 (`RebirthAsync`, `EndSessionAsync`) remains deferred.

Slice 5 implements the two replay DATA surfaces on `SparkplugSessionActor`; **no Core change**. Below maps each frozen requirement to the implementation and the locking test.

---

## `PublishAsync(points, context, ct)` — one phase-tagged DATA batch

Ordered gating (each under the single actor gate):

1. **Active-session + (session, epoch) invariant** — `RequireActiveSession` (→ `PUBLISH_NO_SESSION`) then `RequireContextMatches` (→ `PUBLISH_SESSION_MISMATCH` / `PUBLISH_EPOCH_MISMATCH`). All three are hard **fail-closed** violations: they throw a typed `AdapterException` and the actor goes `Failed/Faulted` (§7). Cancellation is re-thrown **without** faulting.
2. **Suspect latch (carry-forward #1)** — if `Handoff.SuspectAfterPromotion`, the batch is not published; the actor requests a rebirth and returns zero-accepted non-success. A suspect authority accepts no normal DATA.
3. **Empty batch** — `Successful(0)`, no seq, no publish.
4. **Whole-batch classification** (no partial publish) — a **first-observed** metric (key absent from the announced manifest) requests a `SchemaChange` rebirth and returns zero-accepted non-success with **no seq and no publish** (healthy transport → not suspect); a **material mutation** (announced key, changed datatype) **fails closed** (`MATERIAL_SCHEMA_MUTATION`).
5. **Encode + send** — `EncodeNData` with the **current** seq (not yet advanced); `is_historical = Replay || CatchUp`, `false` for Live (ADR-0036 Rule 2); QoS-0 local-boundary publish via the transport seam.
6. **Commit point** — only on local publish success: advance `seq` modulo 256 and `Observe` each point into the baseline (`dirtySinceBirth`). A failed/uncertain send latches suspect, requests a current-session rebirth, and returns zero-accepted **Retryable** non-success (`PUBLISH_REBIRTH_REQUESTED`) — so Core rebirths-then-retries the same subrange (§4.2). **No seq is consumed** by validation failure, first-observed, suspect, send failure, empty batch, or a fail-closed throw.

**Evidence** — `SparkplugSessionActorReplayTests`:
- historical/seq/accept: `Publish_Replay_IsHistorical_AdvancesSeq_FullAccept` (byte-parity vs an independently-built K2 NDATA: seq=1, `is_historical=true`), `Publish_Live_IsNotHistorical`, `Publish_CatchUp_IsHistorical`, `Publish_EmptyBatch_AcceptsZero_ConsumesNoSeq_PublishesNothing`.
- failure/suspect: `Publish_SendFails_LatchesSuspect_RequestsRebirth_ZeroAccept_NoSeq`, `Publish_WhenAlreadySuspect_AcceptsNothing_RequestsRebirth_PublishesNothing`.
- first-observed / material: `Publish_FirstObservedMetric_RequestsSchemaChangeRebirth_NoSeq_NoPublish_NotSuspect`, `Publish_MaterialMutation_FailsClosed_Faults`.
- gating: `Publish_StaleSession_FailsClosed_Faults`, `Publish_StaleEpoch_FailsClosed_Faults`, `Publish_NoActiveSession_FailsClosed`, `Publish_Cancellation_Throws_DoesNotFault`.

---

## `CompleteCatchUpAsync(cutover, ct)` — final update + Live

1. Active-session + (session, epoch) invariant (fail closed / fault on mismatch).
2. **Suspect** → §4.4: latch suspect, await a current-session rebirth request, **return without entering Live** (no final update claimed).
3. Map the cutover snapshot → wire-exact states; `SparkplugBirthBaseline.Compare` (slice-3) yields the final-update set (**dirty ∪ changed-at-cutover**) plus manifest deltas.
4. **Missing announced** metric → fail closed (`MANIFEST_INVARIANT_VIOLATION`, fault).
5. **First-observed** at cutover → `SchemaChange` rebirth, **do not enter Live**.
6. Emit **one non-historical** NDATA for the final-update set (seq advances on success); if nothing changed, emit nothing (no seq). A failed final-update send → §4.4 (suspect + rebirth, **not** Live). Then enter Live.

**Evidence**
- `Cutover_DirtyMetricReturnsToBirthValue_StillEmitsFinalUpdate_EntersLive` (the 1→2→1 case: byte-parity final update, seq=2, only the dirty metric, non-historical, → Live), `Cutover_NoChangeSinceBirth_EmitsNothing_ConsumesNoSeq_EntersLive`.
- `Cutover_MissingAnnouncedMetric_FailsClosed_Faults`, `Cutover_FirstObservedMetric_RequestsSchemaChangeRebirth_DoesNotEnterLive`.
- cutover-suspect composition: `Cutover_FinalUpdateSendFails_LatchesSuspect_RequestsRebirth_DoesNotEnterLive`, `Cutover_WhenAlreadySuspect_RequestsRebirth_DoesNotEnterLive`, `Cutover_StaleEpoch_FailsClosed_Faults`.

---

## Design decisions surfaced for your ruling
1. **Phase → `is_historical`** — pinned `Replay = CatchUp = true`, Live + final update = `false`, per ADR-0036 Rule 2 ("mark any records that arrived during replay as historical … final non-historical latest-value update"). Confirm you agree CatchUp is historical (it is pre-cutover backlog).
2. **Hard-violation disposition** — no-session, session/epoch mismatch, material mutation, and missing-announced-at-cutover throw a typed `AdapterException` **and** set the actor `Failed/Faulted` (so health reports Unhealthy and the driver faults the route). This treats them as lifecycle-invariant violations per §7, not retryable publish failures. Cancellation never faults; send-failure/first-observed/suspect never fault (they return non-success + rebirth).
3. **seq is an actor field** (`_nextSeq`, reset to 1 at promotion, advanced only post-publish-success), gate-guarded, promoted atomically with the session — not stored inside the immutable `ActiveSession` record (it is inherently mutable per publish). Exposed as `internal NextSeq` for test verification only.
4. **Dirty tracking timing** — `Observe` runs **after** a successful publish (accepted data marks dirty). A failed batch → suspect → rebirth → a fresh baseline, so the old dirty set is discarded.

## Carry-forwards still open (from the slice-4 sign-off) — for slice 6
- Slice 6 turns the suspect latch + active-generation disconnects into the coalesced Core rebirth request, ignoring stale generations (the generation gate must be authoritative, not rely on transport-side suppression).
- Generation-exhaustion (`long.MaxValue`) check before the `bdSeq` reservation.

Slice 6 is the next slice (operational rebirth: NCMD parse/coalesce, healthy vs transport-suspect branches, the bounded recovery budget §4.6/§4.7, async idle disconnect, stale-callback suppression, graceful End/Stop idempotence).
