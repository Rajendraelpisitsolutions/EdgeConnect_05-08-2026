<!--
File:        docs/sessions/2026-06-04-phase-e-handoff.md
Purpose:     Phase E session handoff — captures everything locked across this
             Phase E session (solution-page SolutionPanel migration, datasheet
             v5, the NEW §18 ProductDetail page shape, the two software product
             pages), where each artifact lives (PRs #86/#87 + this handoff's
             own PR), the precedents P-A..P-H, the remaining Phase E work, and
             the branch/merge dependencies — so a future session starts cold.
Audience:    Next-session author (cold-start), user, future Phase E authors.
Format:      Markdown session handoff per CLAUDE.md §"Decision records,
             platform principles, and session handoffs".
Date:        2026-06-04
Status:      Phase E — solution migration + datasheet + ALL 7 product pages
             (2 software + 5 hardware) + all 7 static mockups + homepage wiring
             DONE and MERGED TO MASTER (PRs #86, #87). Track B (product pages)
             COMPLETE. Track A collateral (security v3, objection guide v3,
             pitch deck v6, ROI calc v3) is the main pending work.
             Updated 2026-06-05.

✅ NOW ON MASTER (updated 2026-06-05). PRs #86 + #87 are MERGED — the full Phase E
spec corpus + the 7 product-page mockups + homepage wiring are on master. This
handoff (PR #88) merges last. A cold start can read everything directly on master.
-->

# Phase E — session handoff (2026-06-04)

**Phase E delivered the entire product-page surface.** Across this session and its 2026-06-05 continuation: the bulk solution-page SolutionPanel migration (5 pages), the datasheet v5 refresh, a brand-new **§18 ProductDetail page shape** + its **§18.B hardware variant**, **all 7 product page specs** (2 software — `/edgeconnect`, `/eremos-v2`; 5 hardware — `/edge-gateway`, `/mdaq`, `/mtracker`, `/vas`, `/e-idos`), **all 7 static HTML mockups**, and the homepage wiring that cross-links the whole set. **Track B (product pages) is COMPLETE.** The remaining Phase E work is the Track A collateral refresh.

Every artifact went through the established **v1 draft → ChatGPT review → (pre-lock validation workflow where used) → lock** cadence.

---

## 1. Where everything lives (3 open PRs)

| PR | Branch | Contents | State |
|---|---|---|---|
| **#86** | `claude/phase-e-cnc-migration` | 5 solution-page v3 specs (cnc-machining, precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations) + datasheet v5 + the migration plan-trail | ✅ MERGED 2026-06-05 |
| **#87** | `claude/phase-e-product-pages` | §18 + §18.B ProductDetail addendum + all 7 product specs (2 software + 5 hardware) + all 7 static mockups (`web/*.html`) + homepage wiring (`web/index.html`) | ✅ MERGED 2026-06-05 |
| **#88 (this)** | `claude/phase-e-handoff` | this handoff doc | merging last |

**Merge order used:** #86 → #87 → this handoff (no file/build dependency between #86 and #87 — only cross-link hrefs, all resolved once both landed). #86 and #87 are now on master; this handoff merges last.

---

## 2. What's LOCKED

### Solution-page migration (PR #86) — the bulk wave, shipped as one set
All 5 Phase 1 v2 solution pages migrated onto `SolutionPanel` §15 (page content → **v3**), locked together per the design-system v3 §15 Q3 bulk-migration lock:
- `page-solutions-cnc-machining-spec-v1.md` (the pattern-setter)
- `page-solutions-precision-manufacturing-spec-v1.md`
- `page-solutions-brownfield-modernization-spec-v1.md`
- `page-solutions-oem-machine-monitoring-spec-v1.md` (the P-A counter-example — omits Typical Engagement)
- `page-solutions-multi-site-operations-spec-v1.md`
- Plan-trail: `docs/sessions/2026-06-04-phase-e-solution-migration-plan.md`

CNC went draft → ChatGPT → pre-lock validation workflow → lock; the batch of 4 went draft (parallel workflow) → ChatGPT → batch pre-lock validation workflow (caught 1 HIGH — the brownfield CTA/buyer mismatch → precedent **P-H**) → lock.

### Datasheet v5 (PR #86)
`elpis-industrial-intelligence-platform-v5.md` — refresh + light ecosystem context; P-G protocol corrections; locked trust anchors; ChatGPT-reviewed + locked.

### NEW §18 ProductDetail page shape (PR #87) — the 4th content-page shape
`design-system-productdetail-addendum-v1.md` — **LOCKED**. Defines §18 ProductDetail (software variant) + §18.A spec-table content pattern + §18.3 cross-lens preset. Reuses design-system v3 components; **no new visual primitive**. **TO DO: fold §18 + §18.A + §18.3 into a design-system v4 consolidation** (it currently stands as a governing addendum). Proven on two products:
- `page-product-edgeconnect-spec-v1.md` — shape-setter (connectivity product). Static mockup: `docs/marketing/web/edgeconnect.html` (ChatGPT-reviewed visual ground truth; reuses `styles.css`).
- `page-product-eremos-v2-spec-v1.md` — analytics product (inherits §18; §4 became an inbound/outbound integration matrix, same §18.A pattern).

### §18.B HARDWARE ProductDetail variant + all 5 hardware product pages (PR #87)
`design-system-productdetail-addendum-v1.md` **§18.B** — LOCKED. Swaps the software sections for hardware ones (BOM-elimination, hardware-specs, field-deployment, how-to-buy, field-readiness). Governance locks (user direction, recorded in §18.B.0): **NO formal certification claims** — cert / IP / site-compliance handled case-by-case during BOM scope; products **IP65 / IP67-compatible** (NOT certified/rated); resolves hardware-ecosystem-map §264. Hardware pages = **Phase E** (reconciles design-system v3 line 278's "Phase 3"). All 5 hardware specs LOCKED (`/edge-gateway` is the §18.B shape-setter; `/mdaq`, `/mtracker`, `/vas`, `/e-idos` inherit). E-IDOS honesty lock: standalone today; EREMOS V2 streaming = roadmap.

### All 7 static HTML mockups + homepage wiring (PR #87)
`docs/marketing/web/` now holds all 7 product mockups (`edgeconnect`, `eremos-v2`, `edge-gateway`, `mdaq`, `mtracker`, `vas`, `e-idos`) reusing `styles.css`. `index.html` hardware cards + depth-section CTAs link the product pages; a standardized SOFTWARE / HARDWARE footer + nav cross-link the whole set bidirectionally. All internal links verified; cert/IP + trust-anchor + roadmap discipline held. **NOTE: built from the locked specs; a visual click-through sign-off pass has not yet been done.**

---

## 3. Locked precedents P-A → P-H (inherited by all future pages)

P-A..P-G are detailed in the migration plan-trail (`2026-06-04-phase-e-solution-migration-plan.md`); **P-H was added this session**. Summary:

- **P-A** — Typical Engagement is an optional §15 section; include where deployment-anxiety is a real buyer objection (CNC/precision/brownfield/multi-site yes; OEM no).
- **P-B** — whatsIncluded buckets follow product-narrative groupings, not literal schema field names; document the choice.
- **P-C** — when a page touches multiple pillars, the cross-lens related-pillar card leads with the **differentiating** capability, not the outcome capability.
- **P-D** — trust-cue placement follows the realized exemplar order (after Architecture), not the literal §15 prose.
- **P-E** — architecture-annotation eyebrow doubles as the ≤4-word title.
- **P-F** — vertical hero subhead carries the relevant protocol **subset**; full list in the trust strip.
- **P-G** — MT-LINKi → roadmap; Siemens S7 + OPC UA Client → today (per CLAUDE.md §8). Applied across all solution pages + the datasheet + the product pages.
- **P-H (NEW)** — a page that flips its primary buyer off §2.2 must **re-derive its CTA from the new buyer's profile** — never inherit the §2.2 "Book a scoping call" verbatim. (Brownfield → OT-Architect → "Request an architecture review"; OEM → §2.6; product pages → OT-Architect per §18.0; EREMOS V2 confirmed OT-Architect-primary, not flipped.)

---

## 4. Remaining Phase E work

### ✅ DONE — hardware product pages (Track B, §18.B variant)
`/edge-gateway`, `/mdaq`, `/mtracker`, `/vas`, `/e-idos` — all 5 specs LOCKED + mockups built (PR #87). The two former blockers are RESOLVED:
1. **Certification stance — resolved (user direction 2026-06-04):** NO formal third-party certifications currently; cert / IP / site-compliance handled **case-by-case during BOM scope**; products are **IP65 / IP67-compatible** (NOT "certified" / "rated" / "IP-rated"). Recorded in §18.B.0; resolves hardware-ecosystem-map §264. (A §264 amendment in the map itself is still a nice-to-have governance follow-up.)
2. **Phase 3-vs-E — resolved:** hardware product pages are **Phase E** (per user). A design-system v3.x note reconciling line 278 is a governance follow-up.

### Available — Track A collateral refresh (build on the migrated foundation)
Per design-system v3 §24 Q3, in addition to the now-done datasheet:
- **security page v3** (what the solution-page trust cues now cross-link to)
- **sales objection guide v3** (competitive / build-vs-buy framing)
- **pitch deck v6** (re-tells the story on the migrated foundation)
- **ROI calc v3** (interactive — will need a **static HTML mockup** before UI wiring, per the static-mockup discipline)
All four carry the same P-G protocol-correction debt (MT-LINKi → roadmap; S7 + OPC UA Client → today) — refreshing them propagates the fix to the remaining sales-facing collateral.

### Governance follow-ups
- **Fold §18 (ProductDetail) into design-system v4** — the addendum is the governing doc until a v4 consolidation absorbs §18 + §18.A + §18.3.
- **Publish-time orchestration (side-flag #4):** when the solution pages + product pages go live, swap the `/solutions` hub Cards' "Coming soon" pills + pre-live links to live links. Engineering handles at launch.

---

## 5. Cadence + tooling proven this session

- **v1 draft → ChatGPT review → lock** for collateral + product pages (lighter artifacts).
- **+ pre-lock validation workflow** (3 validators reading actual files + synthesizer) for the page specs — caught HIGH-severity drifts ChatGPT missed (CNC word-count + 5 propagation risks; the batch's brownfield CTA/buyer HIGH → P-H). Re-run with `{scriptPath, resumeFromRunId}` from `subagents/workflows/scripts/` if needed.
- **Static HTML mockup before locking a NEW page shape** (done for §18 via `web/edgeconnect.html`); not needed when a page inherits an already-proven shape (e.g., `/eremos-v2`).

---

## 6. Cold-start orientation — files to read

1. **This handoff** — state + branch dependencies.
2. `docs/sessions/2026-06-04-phase-e-solution-migration-plan.md` — D1–D6 decisions + precedents P-A→G (PR #86).
3. `docs/marketing/design-system-productdetail-addendum-v1.md` — the locked §18 ProductDetail shape (PR #87).
4. `docs/marketing/page-product-edgeconnect-spec-v1.md` — the ProductDetail shape-setter (PR #87).
5. `docs/marketing/page-capabilities-hub-spec-v1.md` §9 — the canonical per-page-spec template (master).
6. `docs/marketing/design-system-v3.md` §14/§15/§16/§17 — the locked component/shape system (master).

---

*Phase E session handoff — 2026-06-04, updated 2026-06-05. Solution migration (5) + datasheet v5 + §18 / §18.B ProductDetail shapes + ALL 7 product page specs (2 software + 5 hardware) + all 7 static mockups + homepage wiring — all LOCKED and **MERGED to master** (PRs #86, #87). **Track B (product pages) COMPLETE.** Former hardware blockers resolved: no formal cert claims / IP65-IP67-compatible / case-by-case during BOM scope (§18.B.0, resolves §264); hardware pages = Phase E. Remaining: Track A collateral (security v3, objection guide v3, pitch deck v6, ROI calc v3); visual click-through sign-off of the 7 mockups; governance follow-ups (fold §18/§18.B into design-system v4; hardware-map §264 amendment; design-system v3.x line-278 reconciliation). Precedents P-A→P-H locked. This handoff = PR #88, merging last.*
