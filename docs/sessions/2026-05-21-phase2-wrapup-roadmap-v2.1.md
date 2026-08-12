# Phase 2 wrap-up — roadmap (v2.1 amendment)

**Status:** v2.1 — focused amendment to v2. Removes a redundant doc proposal that would have duplicated existing platform-principles content.
**Date:** 2026-05-21
**Predecessor:** [v2 (locked)](2026-05-21-phase2-wrapup-roadmap-v2.md)
**Trigger:** user pointed out that v2's proposed `docs/platform-principles/runtime-observability.md` would create a folder named `platform-principles` alongside the existing `docs/platform-principles.md` file — operationally confusing, and on inspection the existing **P1 Runtime Tap is strictly observational** already specifies the principle at depth.

---

## 1. What v2.1 changes

### 1.1 Background — what v2 missed

When v2 proposed `docs/platform-principles/runtime-observability.md` as a hard prerequisite gate before M.2c, I had not re-read the existing `docs/platform-principles.md`. P1 of that doc already specifies the runtime-observability lock comprehensively:

- The observational invariant ("subscribers can READ; nothing in the runtime path can READ from subscribers")
- 5 enforcement-in-practice rules (zero-cost-when-no-subscribers, isolated subscriber backpressure with bounded ring-buffer + dropped-oldest, publisher-only from runtime's perspective, license-gateable, replay-reproducibility test)
- 4 explicit anti-patterns ruled out (trace-mode mutation, adaptive sampling, tap-affecting audit chain, sink-delivery decisions influenced by tap state)

The "Allowed / Forbidden" table I drafted in v2 §3.6.2 substantially duplicates P1's "Enforcement in practice" + "What this rules out" sections, just in different format. Writing a parallel platform-principle expansion doc would:

- Duplicate already-locked content.
- Create a confusing file-vs-folder naming overlap (`docs/platform-principles.md` next to `docs/platform-principles/`).
- Dilute the existing P1's governance authority (the "when to amend" clause in `platform-principles.md` would compete with whatever governance the new doc invented).
- Push M.2c-specific implementation details (bounded retention, SSE subscription, per-source history buffer location) into a platform-principles surface where they don't architecturally belong — those are M.2c milestone decisions, not platform-wide commitments.

### 1.2 The three amendments

#### A. §1 Sequencing — remove `runtime-observability.md` prerequisite gate

v2 §1 declared:

> Hard prerequisite gate added: `docs/platform-principles/runtime-observability.md` MUST land before M.2c implementation.

**v2.1 supersedes this.** M.2c implementation gates on **the M.2c plan trail being ratified (v1 → v2 → v3 reality-check)**, with explicit reference + adherence to **P1 of the existing `docs/platform-principles.md`**. No new platform-principles doc is created.

#### B. §3.6.1 "Hard prerequisite" — replace with citation of existing P1

v2 §3.6.1 said:

> `docs/platform-principles/runtime-observability.md` MUST land before M.2c implementation begins. ... It requires its own strategic review pass + explicit user approval per the platform-principles governance clause.

**v2.1 supersedes this with:**

> M.2c implementation reads and enforces **P1 of [`docs/platform-principles.md`](../platform-principles.md)** — "Runtime Tap is strictly observational." The architectural lock is already established (P1 has the principle statement, 5 enforcement-in-practice rules, and 4 explicit anti-patterns ruled out). M.2c's own plan trail (`docs/sessions/2026-05-XX-m2c-live-tag-watch-plan.md`) cites P1 directly and adds M.2c-specific implementation invariants below.

#### C. §3.6.2 "Allowed / Forbidden table" — relabel as M.2c implementation invariants

v2 §3.6.2 framed the Allowed/Forbidden table as "locked invariants ... promoted into runtime-observability.md's body" — i.e., as a new platform-principle expansion.

**v2.1 supersedes this:** the table stays in M.2c's plan trail as **M.2c-specific implementation invariants that EXTEND P1, not REPLACE or PARALLEL it.** It captures:

- Retention semantics (last ≤100 values / last ≤5 min) — M.2c implementation detail
- Subscription scope (transient sessions, per-route + per-source) — M.2c implementation detail
- Performance budget (≤1% CPU overhead at 100-CNC scale) — M.2c implementation detail

These are not platform-wide principles — they're M.2c's concrete bounds derived from P1. They live where they're enforced (M.2c plan trail), not in a parallel platform-principles surface.

---

## 2. What stays unchanged in v2

All other v2 locks remain in force:

- Timeline 7-9 weeks total
- Sequencing across Tracks A/B/C/D
- Chip 3 Provisioning Subsystem framing + `_provisioning` configuration-identity block
- EREMOS V2 — 8 explicit success gates
- M.2d 4-phase split
- §4.6 Coordination risk + §4.7 Convergence note
- Q1-Q20 resolutions

Only the `runtime-observability.md` proposal is removed.

---

## 3. Knock-on effects

- **No new file is created** at `docs/platform-principles/runtime-observability.md` (or anywhere else for this purpose).
- **No folder is created** at `docs/platform-principles/` (avoiding the file-vs-folder naming overlap with the existing `docs/platform-principles.md`).
- **`docs/platform-principles.md` stays exactly as-is** — no edits, no migration.
- **M.2c's eventual plan trail** (`2026-05-XX-m2c-live-tag-watch-plan.md`) cites P1 directly in its architectural-lock section + adds M.2c-specific implementation invariants below P1's authority, not parallel to it.
- **Plan-trail discipline table in v2 §2** — strike the "`runtime-observability.md`" row entirely. The follow-up plan files reduce from 7 to 6 (Chip 3, EREMOS V2, M.2c, M.2d.1, M.2d.2, M.2d.3, M.2d.4 = 7 actually — only the runtime-observability.md row is removed; everything else stays).

Actually that last math is wrong. Let me recount: v2 §2 had Chip 3, EREMOS V2, M.2c, M.2d.1, M.2d.2, M.2d.3, M.2d.4 = 7 plan files **plus** runtime-observability.md = 8 total. v2.1 removes runtime-observability.md → **7 plan files**.

---

## 4. Why this is the right call

- **One source of truth for platform principles.** `docs/platform-principles.md` already has its own governance ("when to amend" clause). Adding parallel files invites future contributors to wonder which is authoritative.
- **M.2c implementation details belong in M.2c.** Decisions about SSE vs WebSocket, bounded retention size, subscription scope are milestone-specific. They don't generalize beyond M.2c, so they shouldn't be parked in a platform-wide surface.
- **No naming conflict.** Avoiding `docs/platform-principles/` next to `docs/platform-principles.md` is the simplest possible fix.
- **Faster path to implementation.** One less strategic-review gate before M.2c can start.

---

## 5. Next steps (updated from v2 §6)

1. **You ChatGPT-review v2.1** — short review, just the amendment.
2. **If v2.1 ratified:** I produce the 7 dedicated v1 plan-trail files (Chip 3, EREMOS V2, M.2c, M.2d.1, M.2d.2, M.2d.3, M.2d.4). M.2c's plan trail cites P1 directly in its architectural-lock section.
3. **Implementation kicks off** with Chip 4 + Chip 5 + offline-scenario test (small, well-scoped, no plan-trail dependencies).

---

**End of v2.1 amendment. LOCKED — ready for review.**
