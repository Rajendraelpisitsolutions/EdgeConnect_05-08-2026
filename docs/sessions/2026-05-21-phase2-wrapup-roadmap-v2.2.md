# Phase 2 wrap-up — roadmap (v2.2 amendment)

**Status:** v2.2 — three small additions from the v2.1 ratification review.
**Date:** 2026-05-21
**Predecessor:** [v2.1 (locked)](2026-05-21-phase2-wrapup-roadmap-v2.1.md), which in turn extends [v2](2026-05-21-phase2-wrapup-roadmap-v2.md).
**Trigger:** ChatGPT review of v2.1 ratified the runtime-observability.md removal AND surfaced three remaining recommendations that strengthen the layered governance stack without invalidating any prior lock.

---

## 1. The three additions

### 1.1 Principle-escalation threshold (NEW governance rule)

**Locked rule for this and future roadmaps:**

> An implementation invariant is promoted into `docs/platform-principles.md` ONLY when **either**:
> 1. it is **reused across multiple milestones** (e.g., a rule that survives untouched through three or more milestones suggests platform-wide relevance), OR
> 2. violating it would **alter platform-wide behavioral guarantees** (e.g., the rule's absence breaks a contract a downstream consumer depends on across the entire system).
>
> Otherwise, implementation invariants stay in their milestone plan trail.

**Why this matters:** without an explicit threshold, future milestones will face the same question I faced with M.2c — "is this M.2c-specific invariant a platform principle?" The threshold gives a clear answer: stay in the milestone plan unless one of the two criteria is met.

**Where this lives:** in the roadmap docs (here) for now. If the rule proves load-bearing across several milestones, it gets promoted into `docs/platform-principles.md`'s "When to amend" subsection in a future amendment of THAT doc (which itself requires the strategic review pass per the existing "when to amend" governance — meta-applied).

**Acknowledged precedent of layering:** ChatGPT's review of v2.1 mapped the project's architecture/governance stack as:

| Layer | Purpose |
|---|---|
| Platform principles | Non-negotiable philosophy (P1-P6) |
| ADRs | Major architectural decisions |
| Roadmaps | Sequencing + coordination |
| Milestone plans | Concrete implementation constraints |
| Tests + DoD | Enforcement |

This threshold rule lives at the **Roadmap layer** (Layer 3) — it governs how items flow between layers. v2.2 is the first explicit codification of this; future cross-milestone roadmaps inherit it as precedent.

---

### 1.2 Runtime Tap deserves a dedicated ADR eventually (deferred follow-up)

**Locked decision:** **No ADR now.** Runtime Tap currently has:

- P1 of `docs/platform-principles.md` (the principle)
- M.2c's eventual plan trail (the implementation invariants)

That's sufficient for v1 implementation.

**Deferred follow-up:** once Runtime Tap is reused by ≥2 other systems (diagnostics, AI substrate, support telemetry, fleet tooling, etc.), it qualifies for promotion to a dedicated ADR. The ADR would document the **contract** between the data path and the tap subsystem — what tap subscribers can rely on, what runtime guarantees they MUST NOT violate, and the per-system extension points.

**File destination when written:** `docs/decisions/ADR-00XX-runtime-tap-contract.md` (number assigned at write time; current latest is ADR-0010 per session memory).

**Trigger for writing:** the second non-M.2c system that wants to consume Runtime Tap (whichever comes first: AI substrate, diagnostics evolution, or fleet tooling). At that point the cross-cutting contract becomes load-bearing and deserves ADR treatment.

---

### 1.3 M.2c scope-policing reinforcement (acknowledged execution risk)

**v2.1 fixed the governance problem** — Runtime Tap observability is anchored at P1; M.2c-specific invariants extend rather than parallel that authority.

**v2.1 did NOT fix the expansion risk.** Per ChatGPT's review:

> Runtime Tap can still quietly become: historian, streaming platform, diagnostics bus, analytics feed, support telemetry framework. So when the M.2c plan starts, you still need strict scope policing. That remains the biggest execution risk.

**Locked mitigation (carried into M.2c plan trail v1):**

The M.2c plan trail's "Out of scope" subsection MUST explicitly call out each of these temptations:

- **NOT a historian.** Persistence is Phase 5 historian milestone, not M.2c.
- **NOT a streaming platform.** Multi-consumer fanout, durable subscriptions, replay semantics — all out.
- **NOT a diagnostics bus.** The /diagnostics page surfaces fault registry + audit chain; M.2c is per-tag live values for an operator-driven session.
- **NOT an analytics feed.** No aggregation, no statistical summarisation, no derived signals.
- **NOT a support telemetry framework.** Operator-initiated, browser-bound, transient.

These five anti-scope statements get pinned in the M.2c v1 plan's Out-of-Scope section before any implementation question (subscription model, history buffer, etc.) is addressed. If the M.2c implementation is tempted to violate one of these mid-flight, **pause and surface** rather than silently expanding.

---

## 2. What stays unchanged

All v2 + v2.1 locks remain in force. v2.2 adds; it does not retract.

- Timeline 7-9 weeks total
- Sequencing across Tracks A/B/C/D
- 7 dedicated v1 plan trails (Chip 3, EREMOS V2, M.2c, M.2d.1/.2/.3/.4)
- Implementation kicks off with Chip 4 + Chip 5 + offline-scenario test
- P1 of `docs/platform-principles.md` is the Runtime Tap observability lock
- M.2c-specific invariants live in the M.2c plan trail, not parallel to P1

---

## 3. Knock-on effects

- **Principle-escalation threshold** is the first explicit codification of a layer-boundary rule between Roadmap and Platform-Principles layers. Future cross-milestone roadmaps inherit it.
- **Runtime Tap ADR (deferred)** added to the "future-work" notes — surfaces when the trigger condition (second non-M.2c consumer of Runtime Tap) is met.
- **M.2c scope-policing reinforcement** is a hard input into the M.2c v1 plan trail when written — the "Out of scope" subsection has its 5 anti-scope bullets pre-defined.

No code changes. No retracted locks. No new doc files needed beyond this amendment.

---

## 4. Next steps (unchanged from v2.1 §5 except for the locked v2.2 inputs)

1. **You ChatGPT-review v2.2** (short pass — just the three additions).
2. **If v2.2 ratified:** I produce the 7 dedicated v1 plan-trail files. The M.2c plan trail v1 incorporates §1.3's 5 anti-scope bullets as locked Out-of-Scope content.
3. **Implementation kicks off** with Chip 4 + Chip 5 + offline-scenario test.

---

**End of v2.2 amendment. LOCKED — ready for review.**
