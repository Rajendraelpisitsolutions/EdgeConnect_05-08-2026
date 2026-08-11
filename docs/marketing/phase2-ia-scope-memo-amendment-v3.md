<!--
File:        docs/marketing/phase2-ia-scope-memo-amendment-v3.md
Purpose:     Scoped amendment to the Phase 2 IA / scope memo v2.
             Resolves the 7 open questions from v2 §7 + adds the
             Phase 2.5 industry-exception governance note + commits to
             two new pre-spec foundation docs (buyer-taxonomy-v1,
             proof-architecture-v1).
Audience:    Internal — Claude (drafts the per-page specs that follow),
             user (governance owner), engineering team (consumes
             resolved decisions).
Format:      Markdown amendment memo. Sits alongside
             phase2-ia-scope-memo-v2.md as a scoped delta — the memo
             is NOT rewritten, this file carries the resolutions.
             Mirrors the positioning-amendment-v4 pattern.
Version:     v3 (amendment to v2 — Pass 2 ChatGPT review resolutions)
Date:        2026-05-28
Status:      LOCKED.

The full memo stays at v2. This file is the §7-open-questions resolution
+ Phase 2.5 industry-exception governance note + foundation-docs
commitment. Future amendments (v4, v5, …) follow the same pattern.
-->

# Phase 2 IA / Scope Memo — Amendment v3

**Scoped resolution of v2 §7 open questions, plus governance additions from Pass 2 ChatGPT review.**

The full memo stays at v2 (LOCKED). This file documents the 7 resolved decisions, adds the Phase 2.5 industry-exception governance note, and commits to two new pre-spec foundation documents.

---

## 1. Resolved decisions (formerly v2 §7 open questions)

All 7 questions from v2 §7 are now resolved.

### 1.1 Q1 — `/platform` vs `/capabilities` reader split: **KEEP the split as written**

The distinction is strategically valuable and locked:

| `/platform` | `/capabilities` |
|---|---|
| Vendor worldview | Capability navigation |
| Executive / commercial lens | Technical / operational lens |
| "Why Elpis exists" | "Which problem domain do I need?" |
| Narrative synthesis | Structured exploration |

Merging them would bloat `/platform`, make `/capabilities` redundant, and blur SEO intent clusters. The current split (v2 §3.1 + §3.2) stays as-is.

### 1.2 Q2 — Phase 2 capability deep-dive count: **All 5, kept**

All 5 pillar deep-dives ship in Phase 2. The capability model is now a core navigation primitive, and incomplete pillar coverage would weaken the ecosystem framing. Per positioning v3, the Industrial Intelligence Ecosystem requires the full 5-pillar map to land as a coherent worldview, not a partial one.

Lighter-weight content is acceptable for pillars where the source material is thinner today (e.g. mDAQ, Edge Gateway), but all 5 must exist.

### 1.3 Q3 — `/architecture` interactivity scope: **hover + progressive zoom + click-to-focus**

Scoped interaction model:

| Feature | In scope? |
|---|---|
| Layer hover annotations | Yes |
| Progressive zoom states | Yes |
| Click-to-focus layer | Yes |
| Free-pan infinite canvas | **No** |
| Animated topology simulation | **No** |
| Full diagram editor behavior | **No** |

Design intent: premium, inspectable, technical — not a SCADA designer. Constrained and purposeful interaction. Engineering should size the `/architecture` Phase 2 build against this scope.

### 1.4 Q4 — Industries in Phase 2 or Phase 3: **Phase 3 — with a Phase 2.5 single-industry exception (see §2)**

Industries remain Phase 3 as locked in positioning v3 §9.2 + roadmap v2 §4. They are multiplicative complexity, require proof / case-study maturity, and create taxonomy explosion if expanded prematurely.

**Exception governance:** see §2 below — if a strategic sales motion, expo campaign, distributor, or government opportunity requires it, **exactly one Phase 2.5 industry page** is authorized without reopening the IA model. Specific governance rules apply.

### 1.5 Q5 — `design-system-v3` timing: **BEFORE the page-spec wave**

This is ChatGPT's strongest single recommendation and is now locked.

`design-system-v3` ships before any individual Phase 2 page spec. Without it:
- Each spec would redefine components ad-hoc
- Component drift begins immediately
- Angular implementation becomes reconciliation work instead of build work

`design-system-v3` must formalize:
- `CapabilityDeepDive` (used by 5 pillar pages)
- `ArchitecturePanel` Phase 2 interactive variant (used by `/architecture`)
- `SolutionPanel` (used by 2 solution depth examples)
- Trust cue system (cross-cutting per memo v2 §5.5)
- Proof strip variants (governed per the upcoming `proof-architecture-v1` — see §3.2)
- CTA hierarchy and "see this from another angle" navigation block (per memo v2 §5.2)
- Responsive behavior rules
- Architecture-layer cards

Per §6 sequencing in memo v2, this is step 2.

### 1.6 Q6 — Canonical buyer taxonomy: **Promote to its own first-class governance doc**

Create `docs/marketing/buyer-taxonomy-v1.md` — short (2-4 pages), canonical buyer table with:
- Buyer label
- Primary pain
- Primary Phase 2 surfaces (initial)
- CTA preference
- Proof expectations
- Vocabulary sensitivity (terms that land vs terms that backfire)

This document will influence copy tone, proof selection, CTA wording, future campaigns, SEO, ad landing pages, distributor enablement, and sales decks. It deserves first-class governance status, not a §7 sub-table.

Proposed buyer set (carries forward from v2 §7 Q6 table, locked here):
- CTO / CIO
- Plant manager / Ops VP
- OT Architect / SCADA engineer
- Maintenance Manager / AMC provider
- Plant engineer (retrofit / greenfield)
- OEM machine builder
- Procurement / compliance reviewer

### 1.7 Q7 — Commercial-packaging surface: **YES — 1-2 paragraph engagement model on `/platform`**

Confirmed. `/platform` includes a short "How we engage commercially" section providing commercial confidence without prematurely exposing pricing.

**In scope for the `/platform` section:**
- Modular deployment model
- Edge + cloud combinations
- OEM / integrator engagement
- AMC / service support
- Phased rollout approach

**Out of scope (stays Phase 3 `/pricing`):**
- SKU grids
- Per-tag pricing
- Subscription tables
- Detailed module-level pricing

Framing: "commercial confidence surface," not "pricing page."

---

## 2. New governance: Phase 2.5 industry-exception rule

Industries pages are Phase 3 (per §1.4 above). However, real-world strategic pressure may require **exactly one** industry page to ship earlier without re-opening the IA model.

**The Phase 2.5 exception is authorized when ALL of the following are true:**

1. A specific named sales motion, expo campaign, distributor partnership, government opportunity, or strategic customer conversation requires industry-specific landing-page support
2. The motion is documented (deal name, expo name, partner name, or campaign reference) — not "we think we might need this someday"
3. The industry page uses the locked Phase 3 `/industries/<industry>` route pattern (per memo v2 §5.6) — no ad-hoc URL
4. The page inherits the `IndustryShell` layout pattern that Phase 3 will use (designed-ahead-in-`design-system-v3`)
5. Engineering capacity is available without delaying any of the 10 Phase 2 page specs

**The exception is NOT authorized for:**
- "We might need this for SEO" — Phase 3 work
- "It would be nice to have" — Phase 3 work
- General industry coverage expansion — Phase 3 work
- More than one industry page — opening that door triggers Phase 3 in fragments and breaks the sequencing

If the exception is invoked, document it as a Phase 2.5 addendum (e.g., `phase2-ia-scope-memo-amendment-v4.md`) naming the specific motion, the industry, and the timeline. The IA model itself is NOT renegotiated — only the surface count is.

---

## 3. New foundation docs committed before per-page specs

Two new short governance docs are now committed to ship before any of the 10 Phase 2 per-page specs begin. They join `design-system-v3` (per §1.5) as the pre-spec foundation layer.

### 3.1 `buyer-taxonomy-v1.md`

Per §1.6 above. Short (2-4 pages), canonical buyer table, influences copy tone / proof selection / CTA wording across all Phase 2 page specs.

### 3.2 `proof-architecture-v1.md`

New governance doc covering proof placement discipline. Without it, screenshots, metrics, and customer-evidence references will drift inconsistently across `/solutions`, `/platform`, future `/industries`, sales decks, and downloadable PDFs.

Scope:
- Where screenshots belong (per surface, per page)
- What counts as evidence vs aspirational claim
- How metrics are validated before publication
- Customer-anonymity rules (when names land, when stories land, per positioning v3 §4 + amendment v4)
- Trust-anchor reuse discipline (defense / space-agency / AMC / geography — primary home `/platform` §4, where they may also appear)
- Benchmark discipline (what counts as a defensible benchmark vs an opinion)
- Case-study structure (locked pattern for Phase 3+ customer stories)

Short (~3-4 pages). Mirrors the v1 → ChatGPT review → v2 cadence.

---

## 4. Updated pre-spec sequencing (supersedes memo v2 §6 step 2)

The pre-spec foundation now includes 3 docs, not 1:

| Order | Deliverable | Status |
|---|---|---|
| 1 | Phase 2 IA / scope memo v2 | LOCKED ✓ |
| 1.1 | This amendment v3 (resolves §7) | LOCKED ✓ (this file) |
| 2 | `design-system-v3` | NEXT — primary deliverable |
| 2.1 | `buyer-taxonomy-v1` | After design-system-v3 |
| 2.2 | `proof-architecture-v1` | After design-system-v3 |
| 3 | `/capabilities` hub spec | After all foundation docs |
| 4 | `/capabilities/condition-monitoring` deep-dive (template-setter) | After step 3 |
| 5 | Remaining 4 pillar deep-dives (parallelizable) | After step 4 |
| 6 | `/architecture` spec | After step 5 |
| 7 | `/solutions` hub spec | After step 6 |
| 8 | `/solutions/predictive-maintenance` spec | After step 7 |
| 9 | `/solutions/edge-connectivity` spec | After step 8 |
| 10 | `/platform` spec (last — cross-references everything) | After all other Phase 2 pages |

`buyer-taxonomy-v1` and `proof-architecture-v1` are sized small enough that they can be drafted in parallel with each other (both after `design-system-v3` lands). The page-spec wave begins only after all three foundation docs (`design-system-v3`, `buyer-taxonomy-v1`, `proof-architecture-v1`) reach v2 lock.

---

## 5. Continuing locks from memo v2 (unchanged)

The following v2 commitments remain in force:

- §1 — four conceptual surfaces and overlap risks
- §2 — WHAT / WHY / HOW / FOR WHAT mental model
- §3 — per-page IS / IS NOT scope locks
- §4.0 — authoritative-explanation invariant
- §4.1 — anti-duplication primary-home map
- §5.1–§5.7 — cross-page navigation logic, standalone-landing invariant, `/security` as cross-cutting trust surface, future-route governance, SEO intent-cluster ownership

---

## 6. Sign-off

This amendment was resolved 2026-05-28 in response to the Pass 2 ChatGPT review of v2.

The IA model is now sufficiently governed for the foundation-doc layer (`design-system-v3`, `buyer-taxonomy-v1`, `proof-architecture-v1`) to begin, and for the 10 Phase 2 page specs to begin once those three foundation docs lock.

---

*Phase 2 IA / Scope Memo Amendment v3 — LOCKED 2026-05-28. Resolves v2 §7 open questions. Adds Phase 2.5 industry-exception governance. Commits buyer-taxonomy-v1 and proof-architecture-v1 as additional pre-spec foundation docs. design-system-v3 remains the next deliverable.*
