# K3 Slice 5 — Review Bundle r1 (schema preflight, exhaustive classify, in-transport suspect, linearized Live)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `c422cbf` — *fix(sparkplug): K3 slice 5 review r1*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice5-r1-source-diff.md` (full `git show -W`, attached).
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests `0/0`.
**Tests:** `450 passed / 0 / 0`, broker-free.

Folds the four r1 blockers. No architectural redesign; no Core change. The accepted architecture (DATA/cutover shape, aliases, baseline comparator, seq ownership, historical mapping) is unchanged.

---

## B1 — cutover now runs the material classifier before the dynamic comparison

`CompleteCatchUpGatedAsync` classifies **every** cutover metric against `session.Manifest.Schema` **before** any dynamic work. A datatype change on an announced metric fails closed (`MATERIAL_SCHEMA_MUTATION`) and **wins over** missing-announced and first-observed. No host request, publish, `seq`, or Live occurs on a materially-mutated cutover. Precedence: **material mutation → missing-announced (invariant) → first-observed (SchemaChange) → dynamic final update**.

**Evidence** — `Cutover_MaterialMutation_FailsClosed_NoPublish_NoSeq_NoRebirth`, `Cutover_MixedFirstObservedAndMaterialMutation_MaterialWins` (the scan is exhaustive over the snapshot, so material wins regardless of enumeration order).

## B2 — DATA classification is now exhaustive and order-independent

`PublishGatedAsync` inspects **all** points first (side-effect-free), then applies precedence: **material mutation fails closed > first-observed requests a SchemaChange rebirth > publish**. A hard violation can no longer be hidden behind a first-observed metric's position, and no rebirth request or publish escapes before the whole batch is validated. The samples **and** the wire states are pre-built before the send, so no fallible mapping runs after a successful MQTT publish (`Observe` reuses the pre-built states).

**Evidence** — `Publish_MixedFirstObservedAndMaterialMutation_MaterialWins` (Theory, both orders: no publish, no seq, no host request, actor Faulted).

## B3 — the transport boundary makes the authority suspect on any uncertain outcome

A new `SendAsync` wraps the transport publish. Once the send is entered, **any** non-clean outcome makes the authority suspect: a `false` return, a non-cancellation exception (normalized to a local failure → rebirth, **not** a terminal fault), or an in-transport `OperationCanceledException` (rethrown, but suspect first). Only cancellation **before** the send stays clean (it never entered `SendAsync`). Applies identically to DATA and the catch-up final update.

**Evidence** — `Publish_PreCancelledToken_CleanCancellation_NotSuspect`, `Publish_CancellationAfterTransportEntry_MarksSuspect_NoSeq_NotFaulted`, `Publish_TransportThrows_ZeroAccept_Suspect_RequestsRebirth_NoSeq_NotFaulted`, `Cutover_FinalUpdateTransportThrows_Suspect_RequestsRebirth_NotLive_NotFaulted`. (The old `Publish_Cancellation_Throws_DoesNotFault`, which proved the unsafe branch, is deleted and replaced by these.)

## B4 — cutover→Live is linearized against the async suspect latch

`AttemptHandoff` gains `Suspect` and `Live` states so the whole post-promotion lifecycle is one lock-free state word. `MarkSuspect` / a post-promotion `OnDisconnect` compare-exchange `Promoted → Suspect`; the cutover Live commit is `TryCommitLive()` (`Promoted → Live`). The two contend on the same word:
- disconnect/send-failure wins → `TryCommitLive` returns false → cutover requests a rebirth and **does not** install Live;
- Live wins → a later drop is recorded as a **post-Live** suspect event (`_suspectAfterLive`), so the next publish still sees suspicion.

A dead authority can never report Live. A deterministic `PreLiveCommitBarrier` seam drives the race.

**Evidence** — `Cutover_NoChange_DisconnectWinsBeforeLiveCommit_Suspect_NotLive`, `Cutover_SuccessfulFinalUpdate_DisconnectWinsBeforeLiveCommit_Suspect_NotLive`.

---

## Rulings folded
- **seq wrap** — `Publish_SeqWrapsThrough255To0` proves 254→255, 255→0, 0→1.
- **SchemaChange category** — a first-observed rebirth `PublishResult` is now `ErrorCategory.Configuration`, not `Network` (asserted in `Publish_FirstObservedMetric_…`).
- **`PhaseToProtocolState`** fails closed on an undefined phase.
- **Live via cutover** — `Publish_Live_IsNotHistorical` now enters Live through `CompleteCatchUpAsync` before publishing the Live batch.

## Handoff state machine (the one behavioral change to slice-4 code)
`Establishing → Invalidated | Promoted`, then `Promoted → Suspect | Live`, `Live →(post-Live suspect flag)`. The slice-4 Begin race semantics are preserved (all 23 Begin tests + all slice-4 handoff tests still green); the new `Suspect`/`Live` arm only governs the post-promotion lifecycle.

## Carry-forwards still open (for slice 6)
- Slice 6 turns the suspect latch + active-generation disconnects into the coalesced Core rebirth request, with an authoritative generation gate (not relying on transport-side suppression).
- Generation-exhaustion (`long.MaxValue`) check before the `bdSeq` reservation.

The exact `git show -W` diff (both files, no ellipses) is attached for line-level sign-off. Slice 6 remains paused pending this pass.
