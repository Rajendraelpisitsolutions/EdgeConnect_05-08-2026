# Runtime reconfigure — systemic "data stops after a runtime edit" — Plan v2

**Date:** 2026-06-23
**Status:** v2 — incorporates ChatGPT review of v1. Next step: **reality-check pass → v3** before
implementation. Do not implement from v2.
**Author:** session handoff
**Supersedes for working purposes:** `2026-06-23-runtime-reconfigure-systemic-plan-v1.md` (kept
unchanged as the review artifact; read it for the original recon evidence table, which v2 does not
repeat in full).
**Trigger:** Field report — *"adding Modbus tags at runtime makes EdgeConnect stop pushing data;
sometimes a server restart is needed."* Plus the systemic question: **do other source modules share
this, and how do we guarantee they don't?**

> Cadence: v1 → review → **v2** → reality-check → v3, each its own dated file. v2 folds in the review;
> it is **not** a lock.

---

## 0. What changed from v1 (review deltas)

ChatGPT accepted v1's direction and recon but flagged that v1 **overstated causality** and that two
proposed fixes (M3 evict-and-re-add, "unconditional" route rebind) were unsafe as written. v2's
substantive changes:

1. **M1 demoted** from "confirmed root cause" to "leading hypothesis" (§1, §3).
2. **New central design: a stable, supervisor-owned ingress endpoint per source ID** that survives
   adapter generations — this makes route correctness independent of whether an adapter supports
   live reconfigure (§5, Layer A). This replaces "unconditional route rebind" as the primary fix.
3. **At-most-one-live-generation invariant** with generation tokens + lifecycle serialization;
   teardown timeout → **quarantine, reject replacement**, never evict-then-re-add (§5, M3').
4. **Adapter-owned, fail-closed delta classification** (`AppliedLive / RequiresInPlaceRestart /
   RequiresReplacement / UnsupportedOrInvalid`) instead of a central field whitelist (§5, Layer B).
5. **Channel continuity separated from connection-preserving reconfigure** — two distinct concepts:
   *Restart-in-place* vs *Live reconfigure* (§4).
6. **Failure & concurrency semantics specified before any wiring** (§6).
7. **Data-loss claim corrected** to "bounded acquisition gap" (§7).
8. **Verification rewritten**: deterministic-barrier assertions, not "`pointsIn` resumes"; the
   per-adapter matrix becomes a **gate before enabling live reconfigure for that adapter**, not a
   later phase (§8).

---

## 1. Answer to the question (revised framing)

All source modules traverse the same shared runtime-reconcile machinery, so the risk surface is
**systemic, not Modbus-specific.** Static recon shows that a source edit is currently handled as
**replacement** (`Modified → Restart → Remove + Add`) and that the live edit pipeline **does not
invoke `ISourceAdapter.ReconfigureAsync`.** This establishes a cross-cutting architectural gap —
**but it does not yet prove which failure mechanism caused the reported field incident.** The durable
correction is to **guarantee source-lifecycle and ingress-binding invariants centrally**, then allow
adapters to **opt into connection-preserving reconfiguration** for supported deltas.

> Epistemic stance for the whole doc: "all adapters share the risk surface" is **proven by recon**.
> "M1 (route/channel orphan) caused the Modbus incident" is the **leading explanation, unconfirmed**
> until reproduced or seen in diagnostics (§9).

(Recon evidence F1–F7 — the differ, classifier, coordinator Remove+Add, the zero-caller
`ReconfigureAsync`, the PUT-path proof, and the ADR-0009 D3 ↔ ADR-0015 Rule 11 contradiction — is in
v1 §2 and unchanged.)

---

## 2. Vocabulary (locked for this doc so the design is unambiguous)

| Term | Meaning |
|------|---------|
| **Generation** | One concrete adapter instance + its pump + its device connection for a source ID. A restart creates a *new* generation. |
| **Ingress endpoint** | The supervisor-owned, source-ID-keyed channel + `ISourceIntake` that routes bind to. **Goal: stable across generations.** |
| **Restart-in-place** | Swap the adapter generation (device connection may drop and re-establish) while keeping the **same supervisor entry and same ingress endpoint**. |
| **Live reconfigure** | Adapter **opt-in**: change the acquisition set (tags/poll/subscription) **without** dropping the device connection. A pure optimization on top of restart-in-place. |
| **Replacement** | Full teardown + rebuild (today's only path). Required for identity/connection-defining changes. |

The critical reframe: **route orphaning is prevented by the stable ingress endpoint, not by adapter
live-reconfigure support.** `TryReconfigureLive` then exists only to avoid *device churn*, not to
keep routes bound.

---

## 3. Failure mechanisms (re-stated with corrected confidence)

| # | Mechanism | Stall? | Scope | Confidence |
|---|-----------|--------|-------|------------|
| **M1** | Route bound to an old, completed channel after the source's channel is recreated → Running but frozen. | Permanent | **Shared** (coordinator/supervisor) | **Leading hypothesis** for the field incident; not yet confirmed. The reactive cascade fix `78646e4` is guard-conditional → gaps re-open it. |
| **M2** | Connection-drop fragility on instant reconnect after teardown. | Sometimes | All sources; **severity protocol-specific** | Real in principle; per-protocol severity is **hypothesis until measured** (§ matrix). |
| **M3'** | **Two live generations for one source ID.** v1's proposed "evict the timed-out entry and re-add" could leave the old adapter still owning socket/handle/pump/writer after its map entry is gone. | Corruption / stall | **Shared** | Design hazard introduced by the *fix*, not the current code — must be designed out. |

---

## 4. Two concepts kept separate (review correction #4)

v1 conflated "don't call Remove/Add" with "channel is preserved." A default
`ReconfigureAsync = Stop → Initialize → Start` **may still complete or replace the adapter's channel**
depending on adapter internals — so avoiding Remove/Add is **not** sufficient to guarantee channel
continuity.

v2 makes channel continuity a **supervisor responsibility**, independent of the adapter:

- **Restart-in-place** keeps the supervisor entry and the **ingress endpoint** alive; only the adapter
  generation and its connection cycle. Route binding is untouched because it points at the stable
  ingress, not at a generation-owned channel.
- **Live reconfigure** is an adapter opt-in layered on top: when the adapter can apply the delta
  without dropping its connection, it does; otherwise restart-in-place handles it. Either way the
  ingress endpoint — and therefore every bound route — is undisturbed.

This is the design that makes "are the other modules safe?" answerable with **yes, structurally**:
correctness no longer depends on six adapters each implementing hot-reconfigure correctly.

---

## 5. Solution — central invariants first, adapter optimizations second

### Layer A — Central lifecycle + ingress invariants (protects all adapters; minimal ADR surface)

**A1. Stable ingress endpoint per source ID.** The supervisor owns the channel + `ISourceIntake`
keyed by source ID; it **survives adapter teardown/rebuild**. The pump of the current generation
writes into the stable channel; a restart swaps the pump+adapter, not the channel. Routes bind to the
ingress, so a source restart needs **no route rebind** for binding correctness.
*Feasibility note:* today `RegisterInternal` creates a fresh channel per `SupervisedSource` on every
`AddAsync` (`SourceSupervisor.cs:390`). A2 of this plan separates "ingress (long-lived)" from
"generation (swappable)." To validate in v3: confirm the routing engine captures the **intake**
(stable) and not a specific `ChannelReader` snapshot that would still go stale.

**A2. At-most-one-live-generation invariant + generation tokens.**
- Every generation gets a monotonic token. Pump writes, health, and diagnostics are tagged with it;
  a stale generation can never write into the ingress after a newer one is installed.
- **Lifecycle is serialized per source ID** (one in-flight transition at a time).
- **Teardown timeout ⇒ quarantine, not eviction.** If a generation's termination can't be confirmed
  within bound, mark the source **Faulted/quarantined**, **reject the replacement**, and surface a
  reconcile failure until termination is confirmed. Never remove the map entry as a substitute for
  resource cleanup (kills M3').

**A3. Complete, idempotent dependency handling** (replaces v1's "unconditional rebind"). For routes
dependent on a changed source, handle **every eligible route exactly once**, with explicit, tested
cases for: route **disabled** (do not start), route **deleted** (do not resurrect), route
**concurrently modified in the same apply** (the modify wins; don't double-act), and **multi-source
routes** (rebind correctly w.r.t. the *other* sources too). With A1 in place, most of this collapses
(binding is stable), but the handler must still be provably exhaustive and idempotent.
*Guard discipline:* do **not** delete existing cascade guards blindly — first document why each guard
exists, then prove the replacement covers the same cases (the regression in A-test must demonstrate
the actual missed-dependency case before any guard is removed).

### Layer B — Adapter-owned, fail-closed delta classification + live-reconfigure opt-in

**B1. Adapter classifies the semantic (old → new) delta**, returning one of:

```
AppliedLive               // adapter applied it without dropping the connection
RequiresInPlaceRestart    // swap generation, keep ingress (connection cycles)
RequiresReplacement       // full teardown+rebuild (identity/connection-defining)
UnsupportedOrInvalid      // reject; leave runtime untouched
```

**Unknown / unrecognized fields default to `RequiresReplacement`** (fail closed). This is the
`ITryReconfigureLive` opt-in that ADR-0009 §Decision 3 explicitly anticipated — now realized. The
central whitelist idea from v1 is dropped: a universal "tags/poll/tuning" list will eventually
misclassify a protocol-specific field (byte-order, scan-group membership, subscription params).

**B2. Coordinator consumes the classification** and drives restart-in-place or live reconfigure
through the stable-ingress path. ADR-0015 Rule 11 becomes *implemented* because the live edit pipeline
now has a real reconfigure route to call.

### Layer C — Per-adapter true live-reconfigure overrides (incremental, gated)

True connection-preserving reconfigure per protocol (Modbus: rebuild `ScanPlan` only; OPC UA: its
existing surgical subscription diff; etc.), prioritized by measured fragility. **A protocol may not
enable `AppliedLive` until it passes the §8 matrix gate.** Until then it returns
`RequiresInPlaceRestart` and rides the safe central path.

**Sequencing:** A (stability + structural M1 fix) → B (classification plumbing, all adapters return
`RequiresInPlaceRestart`/`RequiresReplacement`, none claim `AppliedLive` yet) → C (per-adapter
`AppliedLive`, each behind its own gate).

---

## 6. Failure & concurrency semantics (must be settled before wiring — review #6)

Define and test these *before* the coordinator calls any reconfigure:

- **Per-source serialization.** One lifecycle transition in flight per source ID; others queue or are
  superseded by revision check.
- **Revision check.** Each transition carries the config revision it targets; a transition whose
  target is no longer current is skipped (mirrors the existing stale-reconcile skip).
- **Validate/build-before-swap.** Validate the new config and build the new acquisition state (or new
  generation) **before** touching the live active set. Failure leaves the previous runtime active.
- **Atomic active-state swap.** The ingress sees old generation → new generation with no interleaved
  partial state.
- **Exception after partial mutation.** Defined recovery: either complete-forward to the new state or
  roll back to the prior generation; never a half-mutated live set silently left running.
- **Delete during reconfigure / shutdown cancellation.** Both must terminate the in-flight transition
  cleanly under the at-most-one-generation invariant.
- **Config/runtime divergence visibility.** A failed live reconfigure must **not** silently leave
  persisted config and runtime diverged without exposing both revisions (config revision vs active
  runtime revision) via diagnostics + fault registry.

---

## 7. Data-loss statement (corrected — review #7)

> A restart-in-place (or replacement) of a **polling** source may cause a **bounded acquisition gap
> and missed samples** for the disconnect window — store-and-forward protects points **already
> ingested and accepted by the route**, but cannot reconstruct measurements never acquired while the
> source was disconnected. Live reconfigure (Layer C) avoids the gap by not dropping the connection.
> Protocol-specific history or server-side queues may shrink the gap but are not a general guarantee.

The v1 "none, just delayed" claim is withdrawn.

---

## 8. Verification (rewritten — review changes)

**Scope correction:** the mock-source regression proves the **shared coordinator/supervisor
invariant**; it does **not** by itself prove all adapters safe. Adapter safety is proven by the
per-adapter matrix, which is a **gate before enabling live reconfigure for that adapter** — not a
later phase.

**Primary assertions** (deterministic barriers / controlled clock — *not* "`pointsIn` resumes within
N polls", which can pass despite lost points, duplicate producers, or a stale generation):

1. Existing tags **continue from the active generation** (no gap beyond the defined bound).
2. Added tags **begin appearing**.
3. Removed tags **stop appearing**.
4. **No duplicate generation or connection** exists for the source ID.
5. The route binds to the **current ingress generation**.
6. **Invalid reconfiguration leaves the old runtime intact** (validate-before-swap).
7. **Rapid consecutive revisions converge to the newest** revision.
8. A **simultaneous source-and-route edit** executes each lifecycle action **exactly once**.

**Delta cases each test must cover:** add, remove, datatype/byte-order change, poll-rate change,
connection-field change, disable, delete, cancellation, injected failure (teardown timeout + mid-swap
exception), repeated reconfiguration.

**Matrix gate (per operator-available source — FOCAS2, S7, Brother, MTConnect, OPC UA Client, Modbus
TCP):** the protocol runs the full delta-case suite against its existing fake/demo client **before**
it is allowed to return `AppliedLive`. Includes resource-leak assertions (FOCAS2 handle count returns
to baseline; Modbus/S7 exactly one socket; OPC UA: `ReconfigureAsync` invoked, no new session).

**PR gate:** run the **full** `Management.Tests` project (filtered runs bypass cross-cutting
isolation/schema guards — has shipped a broken PR before).

---

## 9. Diagnostics to add (enables both repro confirmation and ongoing safety)

Surface, per source: **source generation token**, **channel/ingress identity**, **route generation**,
**active configuration revision** (vs persisted), **last lifecycle operation**, and **rebind count**.
This lets us (a) confirm M1 on the live field incident if it recurs, and (b) assert invariants 4/5/8
deterministically in tests. Capture a live repro **if available**, but **do not block the central
correctness fix** on reproducing it.

---

## 10. Decisions taken from the review (carried into v3)

- **ADR shape:** author a **new superseding ADR**. Preserve ADR-0009 and ADR-0015 as historical
  records; the new ADR supersedes **only** ADR-0009 §Decision 3's "Modify == Restart" for the
  reconfigurable subset and explains how it makes **ADR-0015 Rule 11 implementable**.
- **Soft-reconfigurable fields:** **adapter-specific, explicit, fail-closed** (B1). No central
  whitelist. Poll scheduling, datatype, byte order, subscription params, and scan-group membership
  require adapter-owned validation.
- **Layer A first:** yes — *provided* its regression demonstrates the **actual missed-dependency
  case**, and guards are understood before any are removed.
- **Live reproduction:** capture if available; don't block the central fix; ship the §9 diagnostics
  regardless.
- **Modbus scheduler (Layer C detail):** build the new `ScanPlan` **off to the side**; **preserve
  deadlines for unchanged groups**, **discard removed groups**, **initialize new or materially-changed
  groups** by one documented rule; then **swap plan + schedule atomically**.
- **Fragility matrix (v1 §4):** retained **only as a test-priority hypothesis**. High/Med/Low labels
  are **not** established findings until measured by the §8 matrix.

---

## 11. Phased execution (refined)

- **Phase 0 — Decide & document.** v2 → reality-check → v3. New superseding ADR drafted once §6
  semantics + §5 A1 ingress design are validated. **No code before v3 lock.**
- **Phase 1 — Layer A.** Stable ingress endpoint (A1), generation tokens + at-most-one-generation +
  quarantine-on-timeout (A2), complete/idempotent dependency handler (A3), + §9 diagnostics.
  Adapter-agnostic mock-source regression proving the missed-dependency case and invariants 1–8.
  No ADR change required (hardens existing `Restart` semantics + stabilizes ingress). Own PR.
- **Phase 2 — Layer B.** Adapter delta-classification API (fail-closed); coordinator consumes it; all
  adapters return `RequiresInPlaceRestart`/`RequiresReplacement` (none claim `AppliedLive`). Gated on
  the new ADR. §6 semantics implemented + tested.
- **Phase 3 — Per-adapter matrix gate (§8).** Stand up the parameterized delta-case suite for every
  operator-available source. This is the gate, run **before** Phase 4 per adapter.
- **Phase 4 — Layer C.** Enable `AppliedLive` per protocol, one at a time, each only after passing its
  Phase 3 gate. Priority order from the (now-measured) fragility data.

---

## 12. Open items for the reality-check pass (→ v3)

1. **Validate A1 feasibility** against the routing engine: does a route capture the stable `ISourceIntake`
   (good) or snapshot a `ChannelReader` that would still go stale (needs change)? Read
   `RouteDefinitionFactory.BuildOne` + the routing engine intake-resolution path and confirm.
2. **Generation-token mechanics:** where does the token live (supervisor entry vs adapter), and how do
   late writes from an abandoned pump get dropped at the ingress boundary?
3. **Quarantine UX:** how does a quarantined-on-timeout source surface and recover (operator action vs
   automatic retry once termination is confirmed)? Tie into existing fault-registry precedence.
4. **§6 atomic swap for polling adapters:** is the swap at a poll-batch boundary (per the ADR-0015
   Rule 11 "active-set snapshot at batch boundary" language) sufficient, or do we need an explicit
   barrier in the pump loop?
5. **Store-and-forward window measurement:** quantify the typical restart-in-place gap per protocol so
   §7's "bounded" has an actual number.
6. **Does A1 alone fully resolve the field incident** without Layer B/C? If yes, B/C become pure
   device-churn optimizations and could be deprioritized.
