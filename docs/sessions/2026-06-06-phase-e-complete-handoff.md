<!--
File:        docs/sessions/2026-06-06-phase-e-complete-handoff.md
Purpose:     Cold-start handoff after Phase E (marketing-documentation program)
             reached completion. Supersedes the 2026-06-04-phase-e-handoff.md
             snapshot. Captures everything shipped, where it lives, what's
             locked, and the only items still open.
Audience:    Next-session author (cold start), user, future marketing/DXP authors.
Format:      Markdown session handoff per CLAUDE.md §"Decision records, platform
             principles, and session handoffs".
Date:        2026-06-06

✅ EVERYTHING IS ON MASTER. All Phase E PRs (#86–#95) are merged. A cold start can
read everything directly on master — no open branches to chase.
-->

# Phase E — COMPLETE — session handoff (2026-06-06)

**The Phase E marketing-documentation program is complete.** Track B (all 7 product pages), Track A (security page, sales objection guide, pitch deck, ROI calculator), and every governance carry-forward are done, locked, and merged to master. The only non-doc item still open is a **visual click-through** of the static HTML mockups (built to spec, not yet eyeballed in a browser).

Every artifact went through the established **v1 draft → ChatGPT review → apply → lock** cadence, on feature branches, with the proof-architecture discipline (no fabricated specs/metrics, no customer names, no competitor names on public surfaces; competitors named only in the *internal* objection guide).

---

## 1. What shipped (all merged to master)

| PR | Contents | State |
|---|---|---|
| **#86** | 5 solution-page v3 SolutionPanel migrations + datasheet v5 + migration plan-trail | ✅ merged |
| **#87** | §24 + §24.B ProductDetail shape + all 7 product specs + all 7 static mockups + homepage wiring | ✅ merged |
| **#88** | Phase E interim handoff (now superseded by this doc) | ✅ merged |
| **#89** | **Security page v3** — `page-security-spec-v1.md` (§9 format) + `web/security.html` | ✅ merged |
| **#90** | **Sales objection guide v3** — `sales-objection-handling-internal-v3.md` (internal) | ✅ merged |
| **#91 / #92** | **Pitch deck** — v6 protocol-correction, then canonical **v7** (5-pillar ecosystem) + archived v7a | ✅ merged |
| **#93 / #94** | **ROI calculator v3** — `roi-calculator-spec-v3.md` + `web/roi-calculator.html` (#94 = follow-up that synced the spec §5 fix) | ✅ merged |
| **#95** | **Design System v4** — promote ProductDetail to §24; renumber 7 specs; resolve governance carry-forwards | ✅ merged |

### Track B — product pages (7/7)
- **Software (§24):** `/edgeconnect` (shape-setter), `/eremos-v2`.
- **Hardware (§24.B):** `/edge-gateway` (shape-setter), `/mdaq`, `/mtracker`, `/vas`, `/e-idos`.
- Specs: `docs/marketing/page-product-*-spec-v1.md` (all LOCKED).
- Static mockups: `docs/marketing/web/*.html` (10 pages total incl. homepage + security + roi-calculator), all reuse `web/styles.css` + a standardized nav/footer.

### Track A — collateral (4/4)
- **Security v3** — migrated to the §9 spec format; folded in the shipped universal secret-redaction story (ADR-0020); P3 honesty held (no cert claims, no roadmap leakage, no cyber-vendor language). `/security` is the authoritative trust page (others cross-link in via the §16 trust cue).
- **Objection guide v3** — 10 objections (added MachineMetrics/Sight Machine/Tulip, UNS/HiveMQ Edge/EMQX, Power BI/Grafana) + maturity table + battlecards. Internal-only.
- **Pitch deck** — canonical **`pitch-deck-v7.pptx`** (built by `build-pitch-deck-v7.py`), the 5-pillar ecosystem deck. `v7a` archived as a 13-slide fallback; v6 = protocol correction; v1–v5 historical.
- **ROI calculator v3** — discipline-first worksheet + new customer-supplied hardware-BOM bucket; `web/roi-calculator.html` SAMPLE-mode mockup with correct live recalc.

### Design System v4 (#95)
- `docs/marketing/design-system-v4.md` — additive over v3 (§1–§23 unchanged). Adds **§24 ProductDetail** (software §24 / hardware §24.B / §24.A spec-table / §24.3 cross-lens). No new visual primitive.
- The old `design-system-productdetail-addendum-v1.md` is **superseded** (pointer banner; retained as history). **Cite `design-system-v4.md §24.x`, never the addendum.**

---

## 2. Governance carry-forwards — all RESOLVED

1. **§18 → §24 renumber.** ProductDetail's provisional §18 collided with design-system v3's globals (§18 Motion … §23 Sign-off). Renumbered to **§24** in v4; all 7 product specs updated; addendum superseded.
2. **Hardware pages = Phase E** (not "Phase 3"). Reconciled in v4; supersedes design-system v3 line 278.
3. **Hardware certifications (§264).** Resolved in `hardware-ecosystem-map-v3.md` §264 + v4 §24.B.0: **no formal third-party certifications currently**; products **IP65 / IP67-compatible** (not certified); cert / IP / site-compliance handled **case-by-case during BOM scope**. Hardware pages make no formal cert claims.

---

## 3. Locked precedents + discipline (inherited by all future pages)

- **P-A..P-H** (detailed in `2026-06-04-phase-e-solution-migration-plan.md`). P-H: a page that flips its primary buyer off §2.2 re-derives its CTA from the new buyer's profile.
- **Protocol reality (P-G):** shipped today — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, Siemens S7 (southbound); MQTT, OPC UA Server (northbound). **Roadmap:** FANUC MT-LINKi (REST), HTTP/TCP sinks, Linux host. **AI agents = Phase-4.5 architectural commitment, NOT a shipped/demoable feature.** This was corrected across the datasheet, objection guide, and pitch deck this program.
- **Trust anchors (LOCKED, verbatim, anonymized):** "Deployed in defense and space-agency programs"; "Maintenance and AMC providers across India and the Middle East"; "Operating across India and the Middle East." Used on VAS / E-IDOS / hardware-ecosystem surfaces. Never name a customer.
- **Buyers (buyer-taxonomy v1):** §2.2 Plant manager; §2.3 OT Architect (software product pages); §2.4 Maintenance Manager / AMC (VAS, E-IDOS); §2.5 Plant engineer (hardware product pages); §2.6 OEM; §2.7 Procurement / compliance reviewer (`/security`).
- **Cadence:** v1 draft → ChatGPT review → apply → lock. New page shapes get a static HTML mockup first. ROI/UI surfaces get a mockup before any real UI wiring.

---

## 4. What's still open (none blocking)

- **Visual click-through of the mockups** — `docs/marketing/web/`: `index`, `edgeconnect`, `eremos-v2`, `edge-gateway`, `mdaq`, `mtracker`, `vas`, `e-idos`, `security`, `roi-calculator`, plus `pitch-deck-v7.pptx`. Built faithfully to the locked specs but **not yet eyeballed in a browser**. A click-through pass (open `index.html`, click through everything, check the hero SVGs / spec tables / responsive layout) is the natural next step before any handoff to the Angular implementation team.
- **Angular implementation** — the mockups + specs are the visual/structural ground truth for the real site build (not started; engineering track).
- **Phase 4.5 AI agents** (Diagnostic / Configuration / Tag Mapping / Intelligent Alerting) — not started; gated behind product, not docs.
- **Documentation Copilot** (Phase 4) — not started.
- Unrelated **engineering PRs** remain open and were left untouched this session: #81 (EtherNet/IP), #60–65 (OPC UA Client wizard), #17 (chips recovery).

---

## 5. Process note (for honesty / continuity)

During the ROI work, the option-B §5 reporting-formula correction was applied to the spec but missed a `git add`, so #93 briefly merged a corrected mockup against an un-corrected spec. Caught via `git status` after the merge and fixed in **#94** (merged). Spec + mockup agree on master. Lesson reinforced: `git status` / `git show --stat` before declaring a commit complete when multiple files changed.

---

## 6. Cold-start orientation — files to read

1. **This handoff** — state + what's open.
2. `docs/marketing/design-system-v4.md` §24 — the ProductDetail shape (software + §24.B hardware).
3. `docs/sessions/2026-06-04-phase-e-solution-migration-plan.md` — precedents P-A→P-H.
4. `docs/marketing/page-capabilities-hub-spec-v1.md` §9 — the canonical per-page-spec template.
5. `docs/marketing/buyer-taxonomy-v1.md` + `docs/marketing/proof-architecture-v1.md` — buyer + honesty discipline.
6. `docs/marketing/web/index.html` — the mockup hub; click through from here.

---

*Phase E marketing-documentation program — COMPLETE 2026-06-06. Track B (7 product pages), Track A (security v3, objection guide v3, pitch deck v7, ROI calc v3), and Design System v4 (§24 ProductDetail + all governance reconciliations) all LOCKED and merged (#86–#95). Only open item: visual click-through of the 10 static mockups + pitch deck v7. Protocol reality P-G-correct; trust anchors + buyer taxonomy + cadence carried forward. Supersedes 2026-06-04-phase-e-handoff.md.*
