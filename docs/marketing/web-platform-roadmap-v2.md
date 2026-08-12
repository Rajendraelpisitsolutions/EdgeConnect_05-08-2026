<!--
File:        docs/marketing/web-platform-roadmap-v2.md
Purpose:     The four-phase roadmap for the Elpis Digital Experience Platform.
             v2 sharpens Phase 1 anti-scope-creep discipline per user Pass 1
             feedback: "DO NOT over-engineer Phase 1."
Audience:    Internal — strategy doc for Claude, user, engineering team.
Format:      Markdown roadmap memo. Companion to the strategy doc.
Companion:   digital-experience-platform-strategy-v2.md (the WHY)
             design-governance-v1.md (the design discipline)
             homepage-spec-v2.md (Phase 1 spec)
Version:     v2
Date:        2026-05-26
Status:      LOCKED after Pass 1 review pass.

v1 → v2 changes:
  - Adds §1 "What Phase 1 explicitly is NOT" — the user's strongest
    feedback ("DO NOT over-engineer Phase 1") promoted from inline
    bullet to top-section discipline lock.
  - Strategic-meaning column added to phase table for cross-doc
    consistency with strategy v2 §7.
  - Cross-references design-governance-v1.md throughout.
  - No phase definitions changed; only discipline framing strengthened.

v1 (web-platform-roadmap-v1.md) retained as historical reference.
v2 is canonical going forward.
-->

# Elpis DXP — Four-Phase Roadmap v2

**Companion to `digital-experience-platform-strategy-v2.md`. Locked after Pass 1 review.**

This is the planning horizon that shapes every Phase 1 decision. Component names, IA structure, design system primitives — all of them are sized to grow into Phases 2-4 without rewrite.

---

## 1. Phase 1 — the single most important discipline lock

**Phase 1 is lean. Phase 1 is fast. Phase 1 is visually exceptional. Phase 1 is sharply scoped.**

The single largest risk to Phase 1 is not engineering complexity — it is *scope creep from Phase 2-4 conversations leaking into Phase 1 deliverables*. Every conversation about CMS choice, localization scaffolding, partner workflow gates, lead-scoring integrations, ROI-calculator architecture, demo environments — all of those are Phase 2-4 work.

If Phase 1 absorbs even one of them, the homepage launch slips by months and the strategic momentum from the positioning lock evaporates.

### 1.1 What Phase 1 explicitly IS

| In scope for Phase 1 |
|---|
| Homepage at `/` — production-grade, SSR, Lighthouse ≥ 90 |
| Top nav shell with 7-nav structure (placeholder routes for Phase 2-4 items) |
| Component library v1 (11 components per `design-system-v2.md`) |
| Brand-tokens.css wired through Tailwind config |
| Responsive design across mobile / tablet / desktop / large desktop |
| Discovery-call CTA (form-based — see homepage spec v2 §8.1) |
| Datasheet download CTA (open, no gating) |
| Footer with 5 columns + legal strip |
| Analytics minimum: page views + CTA clicks + time-on-page |
| WCAG AA compliance across every visible surface |
| Design Governance enforcement per `design-governance-v1.md` |

### 1.2 What Phase 1 explicitly IS NOT

The user's most important Pass 1 lock. These items do **not** enter Phase 1 under any pressure:

| Out of scope for Phase 1 | Belongs to |
|---|---|
| CMS architecture, headless-CMS evaluation, content-modeling debates | Phase 3 (when resource center demands it) |
| Localization / i18n scaffolding (multi-language support) | Phase 4 |
| Partner portal authentication and workflows | Phase 4 |
| Customer portal, gated asset library | Phase 4 |
| Lead-scoring integrations (Salesforce, HubSpot, Marketo) | Phase 4 |
| ROI calculators or interactive tools | Phase 4 |
| Demo environments or embedded product UI | Phase 4 |
| Documentation portal (versioned docs, full-text search) | Phase 4 |
| Interactive architecture page (hover annotations, zoom) | Phase 2 — homepage gets the static embed |
| Solution pages, capability deep-dives, industries pages | Phase 2 / Phase 3 |
| Customer stories or testimonials | Phase 3 |
| Email-gated downloads, lead-capture forms beyond the discovery-call CTA | Phase 3 |
| A/B testing infrastructure, feature flags for marketing | Phase 4 |
| Personalization based on visitor industry or company size | Phase 4 |
| Chat widget, support deflection | Permanent — see homepage spec anti-patterns |
| Multi-brand or sub-brand support | Out of horizon |

**Discipline rule:** when a Phase 1 conversation drifts toward any item in §1.2, the response is: *"That belongs to Phase X. Capturing it there. Phase 1 stays scoped."*

If a Phase 1 deliverable cannot be shipped without a §1.2 item, surface the dependency immediately and discuss Phase 1 re-scoping — never absorb the new item silently.

---

## 2. Phase 1 — Homepage (this milestone)

**Scope:** the homepage at `elpisitsolutions.com/`, production-grade, replacing the existing site at root.

**Pages shipped (1):**
- `/` — Industrial Intelligence Ecosystem homepage

**Engineering deliverables:**
- New `elpis-global-web` Angular 19 app, SSR enabled
- Tailwind config wired to `brand-tokens.css`
- Component library v1 (11 components — see `design-system-v2.md`)
- Routing scaffolding for the 7-nav (placeholder routes for Phases 2-4)
- Footer with company info, contact details, social links
- Top nav (mega-menu pattern, premium-industrial styling — no Material aesthetics)
- Mobile-first responsive layout, breakpoint tokens locked
- Analytics wired (Plausible / GA4 — Phase 1 minimum: page views, CTA clicks, time-on-page)
- Lighthouse ≥ 90 across Performance / Accessibility / Best Practices / SEO
- WCAG AA across every visible surface

**Outcomes:**
- The homepage is the visible front door of the Industrial Intelligence Ecosystem worldview
- The component library and CSS token plumbing become the foundation for Phases 2-4
- The 7-nav exists in the codebase even if Phases 2-4 routes return placeholder pages

**Exit criteria:**
- Lighthouse green on /
- Architecture section embeds `architecture-diagram-v2-light.svg` cleanly with the spec v3 §4.2 caption
- Hardware capability strip references all 5 products (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway)
- Primary CTA (Book a discovery call) and secondary CTA (Download the datasheet) both functional
- All copy verbatim from `homepage-spec-v2.md` (final-locked version)
- Page renders cleanly on mobile (375w), tablet (768w), desktop (1280w), large desktop (1920w)
- Design Governance §4 drift signals zero — no scope-creep leaks from §1.2 above
- The page is the single source of truth for: hero treatment, scroll patterns, motion language, nav behavior — all reused in Phase 2

**Strategic meaning of Phase 1: Brand trust.**

---

## 3. Phase 2 — Core Ecosystem Pages

**Scope:** the five canonical pages that complete the platform-positioning narrative. Nav fully populated.

**Pages shipped (5):**
- `/platform` — Platform overview (Industrial Intelligence Ecosystem in full)
- `/capabilities` — Five capability pillars in depth (Connectivity & Edge, Data Acquisition, Asset Intelligence, Condition Monitoring, Operational Intelligence)
- `/architecture` — Interactive Industrial Intelligence Stack walkthrough
- `/solutions/predictive-maintenance` — Predictive maintenance solution page (depth example)
- `/solutions/edge-connectivity` — Edge & connectivity solution page (depth example)

**Engineering deliverables:**
- Solutions hub at `/solutions/` (list page)
- Capability deep-dive layout (reusable for all 5 pillars)
- Interactive Architecture page — diagram zoom states, optional hover annotations
- Cross-page navigation patterns (sidebar TOC, in-page anchor scroll)
- Two new components: `CapabilityDeepDive`, `SolutionPanel`

**Outcomes:**
- Sales conversations stop relying on PDFs as primary collateral
- The five capability pillars from manifesto v3 each get a dedicated story page
- Architecture page becomes the linkable canonical reference (shared in emails, RFPs, sales decks)
- Phase 2 components extend the library without breaking Phase 1 pages

**Phase 2 entrance review:**
- [ ] Phase 1 design-governance drift signals zero across 30 days of production
- [ ] Phase 1 Lighthouse scores stable ≥ 90 on monthly audit
- [ ] Phase 2 specs locked under same v1→review→v2 cadence as Phase 1
- [ ] Phase 2 component additions vetted against §6 of design-system v2 (additive only — never break Phase 1 components)

**Phase 2 exit criteria:**
- All 5 pages Lighthouse green
- The `/architecture` page replaces the architecture-diagram-v2-light.svg embedded on the homepage with the interactive zoom-state version
- Sales team validates: every common pre-qualification question is answered by a Phase 2 URL

**Strategic meaning of Phase 2: Platform legitimacy.**

**Phase 2 usability-review observation target:** the Platform / Capabilities / Solutions / Architecture nav-conceptual-overlap noted in homepage-spec v2 §1.1.7 — observe whether visitors disambiguate naturally, or whether nav refinement is needed. See homepage-spec v2 §1.1.7 for the alternative naming options.

---

## 4. Phase 3 — Industries + Resources + Customer Proof

**Scope:** complete the sales surface. Add industry-specific landing pages and the resource center.

**Pages shipped (~12):**
- `/industries/automotive`
- `/industries/pharma`
- `/industries/oil-and-gas`
- `/industries/defense`
- `/industries/aerospace`
- `/industries/heavy-manufacturing`
- `/industries/` (hub)
- `/resources/datasheets` — gated and ungated PDFs
- `/resources/brochures` — hardware brochures (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway)
- `/resources/whitepapers`
- `/resources/` (hub)
- `/customers` — case studies, defense / space-agency anchor (anonymized), AMC partner stories

**Engineering deliverables:**
- Asset gating (email-capture before download for whitepapers and select datasheets)
- Resource library taxonomy (filterable by type, industry, product, audience)
- Customer stories template (`CaseStudyShell` component) — reusable structure for every story
- Industry-specific layouts (`IndustryShell`) — common scaffolding, page-specific content
- CMS evaluation begins (decision point: headless CMS like Strapi/Sanity, or .NET-native content store)

**Outcomes:**
- AMC channel partners have white-label-ready collateral
- Defense / space-agency credibility surfaces without naming customers
- Industries pages become the SEO surface for vertical-specific search queries

**Phase 3 exit criteria:**
- All 12 pages Lighthouse green
- Resource downloads tracked through analytics
- At least one customer story published with explicit customer sign-off
- AMC partner portal scoping document written (sets up Phase 4 partner portal)

**Strategic meaning of Phase 3: Market credibility.**

---

## 5. Phase 4 — Advanced Ecosystem Surfaces

**Scope:** the DXP becomes operational — interactive tools, demos, documentation, and partner-grade workflows.

**Pages shipped:**
- `/calculators/oee-uplift` — OEE estimator
- `/calculators/downtime-cost` — downtime cost calculator
- `/calculators/brownfield-tco` — migration TCO calculator
- `/demos/` — hosted demo environments
- `/docs/` — documentation portal (integration guides, adapter specs, MQTT/OPC UA contracts)
- `/partners/` — AMC partner enablement portal (gated)
- `/customer-portal/` — customer asset library (gated)
- `/contact/lead-intake` — structured lead-gen workflow

**Engineering deliverables:**
- Interactive calculator components (`MetricSlider`, `CalculatorPanel`, `ResultsCard`)
- Demo environment iframe scaffolding + hosted Studio screenshots
- Documentation portal — markdown-driven, full-text search, versioned
- Partner portal authentication + content gating
- CMS integration completed (or .NET content-API alternative)
- Multi-language readiness (i18n scaffolding even if Phase 4 ships English-only)

**Outcomes:**
- DXP is the digital operating surface of the ecosystem
- Pre-sales conversations are partly self-service
- Partners onboard themselves without Elpis hand-holding
- SEO surface is fully indexed across every product, capability, industry, and solution query

**Phase 4 exit criteria:**
- 80% of inbound qualification questions answerable via the DXP without sales-team intervention
- At least one calculator drives a measurable conversion uplift
- Documentation portal indexed and ranked for core technical search queries
- Partner portal onboarding flow validated by at least 3 AMC partners

**Strategic meaning of Phase 4: Ecosystem operationalization.**

---

## 6. Cross-phase commitments

These remain true from Phase 1 through Phase 4:

1. **No Angular Material aesthetics anywhere, ever.** Permanent lock per strategy v2 §5.
2. **Every component resolves to `brand-tokens.css` — no hardcoded hex.**
3. **Lighthouse ≥ 90 on every shipped page.** Hard floor per strategy v2 §5.
4. **WCAG AA across every visible surface.** Hard floor per strategy v2 §5.
5. **SSR/SSG enabled for SEO and Lighthouse performance.**
6. **The 7-nav structure is stable from Phase 1** — only the populated routes change between phases.
7. **Component library grows additively** — Phase 2 adds; Phase 3 adds; Phase 4 adds. Existing components never break.
8. **Copy locks before engineering builds** — marketing track approves every word, every section, every CTA.
9. **Design Governance enforced per `design-governance-v1.md`** — six discipline areas, escalation paths, drift signals, per-PR + per-phase + annual cadences.
10. **Phase 1 stays Phase 1.** §1.2 items never absorb mid-flight. New items go to their owning phase.

---

## 7. Timing and sequencing

This roadmap is scope, not schedule. The schedule is the engineering team's call.

**Reasonable cadence indicators (for budgeting conversations, not commitments):**

| Phase | Calendar-ish | Engineering effort |
|---|---|---|
| Phase 1 | 4-8 weeks from spec lock to homepage live | Dominated by component library build + SSR plumbing |
| Phase 2 | 8-12 weeks from Phase 1 live | Five pages, interactive Architecture page is the heaviest piece |
| Phase 3 | 12-16 weeks from Phase 2 live | Twelve pages, CMS evaluation, gating infrastructure |
| Phase 4 | 16-24 weeks from Phase 3 live | Calculators, demos, docs portal — multi-stream work |

The point of publishing these indicators is to set expectations that the full DXP is **a 6-12 month engineering investment**, not a one-quarter sprint. Treating it as a sprint is how the marketing-microsite trap re-opens — and treating Phase 1 as a sprint is how the §1.2 scope creep happens.

---

*Web Platform Roadmap v2, 2026-05-26. LOCKED after Pass 1 review pass. Sets the planning horizon for every Phase 1 architectural decision. Supersedes v1 as the canonical roadmap.*
