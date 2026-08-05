<!--
File:        docs/marketing/digital-experience-platform-strategy-v2.md
Purpose:     Strategic framing for the Elpis web presence. v2 of the
             Digital Experience Platform (DXP) strategy.
Audience:    Internal — strategy doc for Claude, the user, the engineering
             team (Angular + .NET), and any future partner agency.
Format:      Markdown strategy memo. Supersedes v1 as the canonical
             governing framing.
Companion:   web-platform-roadmap-v2.md (phasing)
             design-governance-v1.md (the fourth governance track — new)
             design-system-v2.md (component library blueprint)
             homepage-spec-v2.md (Phase 1 spec)
Version:     v2
Date:        2026-05-26
Status:      LOCKED after Pass 1 review pass (user verdict: "Excellent across
             strategic direction, architectural direction, long-term thinking,
             governance discipline — Ready for implementation").

v1 → v2 changes:
  - Adds §6.5 "The four governance tracks" formalizing Design Governance
    as a peer to brand, architecture, and roadmap tracks (per user
    refinement: "you should also introduce design governance").
  - Sharpens §5 engineering constraints — Lighthouse ≥ 90 floor is now
    a hard-locked constraint (was implied; now explicit) and the no-Material
    aesthetic lock is moved from "expected" to "permanent and non-debatable."
  - Adds §3.5 — explicit note that the DXP and EREMOS V2 UI share TOKENS
    but never share components or runtime (was implicit; now explicit
    per user agreement during Pass 1).
  - §7 roadmap summary updated to reference v2 roadmap doc.
  - §9 strategic-intent test unchanged — confirmed locked in Pass 1.

v1 (digital-experience-platform-strategy-v1.md) retained as historical
reference. v2 is canonical going forward.
-->

# Elpis Digital Experience Platform — Strategy v2

**The reframe that governs every web decision from this point forward. Locked after Pass 1 review.**

We are not redesigning a marketing website. We are building the long-term **digital experience platform (DXP)** for the Elpis ecosystem.

That mindset shift is not cosmetic. It changes architectural decisions, engineering boundaries, and the leverage of every hour spent building.

---

## 1. What this is, what it is not

| This IS | This is NOT |
|---|---|
| A dedicated Angular application — its own codebase, its own deployment target, its own design system | A folder of marketing pages inside EREMOS V2 |
| Production-grade from day one — SSR/SSG, Lighthouse-clean, enterprise-readable | A "temporary website while we figure things out" |
| A reusable web platform — extensible to docs, resources, calculators, partner portals, demo environments, lead-gen workflows | A static set of marketing-only landing pages |
| Built on a custom tokenized design system that honors `BRAND_TOKENS.md` | Built on Angular Material aesthetics or any framework default |
| The visible front-end of the Industrial Intelligence Ecosystem worldview locked in positioning v3 | A separate marketing track that drifts from product positioning |

If a decision under debate would make the platform feel more like a marketing microsite and less like a digital operating surface, the answer is the latter.

---

## 2. Why the DXP framing matters now

Elpis is mid-reposition:

- **Positioning is mature** — manifesto v3 locks the five-pillar Industrial Intelligence Ecosystem worldview.
- **Brand is locked** — `BRAND_TOKENS.md` v1, locked 2026-05-24.
- **Architecture worldview is locked** — `architecture-diagram-v2-dark.svg` rev3, locked 2026-05-25.
- **Sales surfaces are mature** — pitch deck v5, datasheet v3, hardware ecosystem map v3.
- **Engineering capability exists in-house** — Angular + .NET teams already shipping EREMOS V2.

This is a once-per-decade convergence. Picking the right architectural framing right now means every subsequent ecosystem surface (docs, partner portal, ROI calculators, customer demos) compounds on the same platform instead of being one-off bolt-ons.

The wrong move is to write a clever set of marketing pages, ship them, and then in six months be unable to extend them into a docs portal without a rebuild. The right move is to architect for that future from day one — even though Phase 1 only ships the homepage.

---

## 3. The boundary that protects this

A single boundary makes the rest of the strategy executable:

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   elpis-global-web    ←——  DXP. Marketing, docs, partner,       │
│   (Angular 19, SSR)         resources, calculators, demos.      │
│                             Owned by Elpis, public-facing.      │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   EREMOS V2 UI       ←——  Operational app. Tenants, OEE,        │
│   (existing Angular)        alarms, segments, reports.          │
│                             Authenticated, customer-data-bearing.│
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   EdgeConnect Studio ←——  Local admin UI per gateway.           │
│   (Razor / Blazor)         On-prem, gateway-local, technical.   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

Each app has its own design language tuned to its purpose. They may share **tokens** (brand colors, typography) but they do not share components or runtime. The DXP is marketing-grade tonal restraint and premium-industrial polish. EREMOS V2 is operational density and information design. Studio is technical configuration. Mixing them blurs all three.

### 3.5 What is shared, what is not (v2 — explicit)

| Shared across all three apps | Not shared |
|---|---|
| `BRAND_TOKENS.md` color values | Component libraries |
| `brand-tokens.css` custom properties (subset) | Runtime / build pipeline |
| Inter font stack | Routing models |
| Logo lockup + clearspace rules | Interaction patterns |
| The "one accent" discipline (brand teal) | Page chrome / nav patterns |
| WCAG AA floor | Release cadence |
| The DO-NOT list (no Angular Material aesthetics, no stock photography, etc.) | State management, API contracts, authentication |

**Hard rule:** the DXP code never lives in the EREMOS V2 repository. It is its own Angular app, its own deployment, its own release cadence. Tokens may flow downstream from `BRAND_TOKENS.md`; components never.

---

## 4. What this platform must support (the 24-month horizon)

The DXP must be capable of growing into all of the following without architectural rewrite:

1. **Marketing surface** (Phase 1 — Homepage; Phase 2 — Core ecosystem pages)
2. **Resource center** (Phase 3) — datasheets, brochures, whitepapers, downloadable PDFs, gated assets
3. **Industries pages** (Phase 3) — Automotive, Pharma, Oil & Gas, Defense, Aerospace, Heavy Manufacturing
4. **Solution pages** (Phase 2) — OEE, Predictive Maintenance, Downtime Reduction, Energy Monitoring, Multi-site Visibility
5. **Architecture deep-dive** (Phase 1–2) — interactive Industrial Intelligence Stack walkthrough
6. **Customer stories** (Phase 3) — case studies, defense / space-agency anchors (anonymized), AMC channel proof
7. **Documentation portal** (Phase 4) — integration guides, adapter specs, MQTT/OPC UA contracts, API references
8. **ROI calculators** (Phase 4) — OEE uplift estimator, downtime cost calculator, brownfield migration TCO
9. **Demo environments** (Phase 4) — interactive sandboxes, hosted Studio screenshots, embedded EREMOS dashboards
10. **Partner / customer portal** (Phase 4) — AMC partner enablement, customer asset library, white-label collateral
11. **Lead-gen workflows** (Phase 4) — Discovery call booking, RFP intake, demo request, technical pre-sales handoff
12. **SEO and content marketing** (continuous) — blog, technical articles, whitepapers, indexed properly via SSR/SSG

Every component in the design system is named with this 24-month horizon in mind. `CapabilityCard` is not a homepage-only widget — it appears on Capabilities, Solutions, Industries, and Architecture pages.

---

## 5. Engineering constraints (locked, v2 sharpened)

These are non-negotiable for the DXP track. v2 sharpens prior wording.

| Constraint | Reason |
|---|---|
| **Separate Angular application** — `elpis-global-web`, not merged into EREMOS V2 UI | Different audience, different design tone, different cadence, different deployment target. Coupling creates compounding drag. |
| **Angular 19** | Latest LTS-track; aligns with EREMOS V2 stack; SSR/SSG first-class. |
| **SSR/SSG enabled** | Without it: SEO weakens, Lighthouse scores weaken, enterprise procurement perception weakens. Customers vetting Elpis run Lighthouse audits — this is real. |
| **Tailwind + BRAND_TOKENS CSS variables** | Tailwind for utility velocity; CSS custom properties from `BRAND_TOKENS.md` for token discipline. No raw hex in components. |
| **Custom tokenized component library** | Every component (`Button`, `SectionShell`, `CapabilityCard`, etc.) is hand-built against tokens. |
| **No Angular Material visual aesthetics — permanent lock** | Material's defaults destroy premium-industrial feel. Use Material's behavioral primitives (CDK overlays, a11y helpers) when valuable; never use its visual styles. **This lock is non-debatable and applies indefinitely**, not just Phase 1. Use of `mat-*` selectors with default Material styling is a design-governance breach. |
| **Lighthouse ≥ 90 floor — hard-locked** | Performance, Accessibility, Best Practices, SEO. Every shipped page. Non-negotiable for enterprise credibility. |
| **WCAG AA across every visible surface — hard-locked** | The contrast matrix in BRAND_TOKENS §6 is binding. Failure on any contrast check blocks ship. |

---

## 6. What Claude produces vs what the engineering team owns

The boundary captured in the user's directive:

| Layer | Owner | Notes |
|---|---|---|
| Brand strategy | **Locked** | BRAND_TOKENS v1, manifesto v3, ecosystem map v3 |
| Visual system & design language | **Claude → user review** | Token CSS, component library blueprint, motion language, anti-patterns |
| Copy (verbatim, ready to ship) | **Claude → user + ChatGPT review** | Every word of the homepage and downstream pages |
| Page IA and section structure | **Claude → user review** | 7-nav, page hierarchies, section-by-section breakdowns |
| Static HTML/CSS reference implementation | **Claude (Phase 1.5)** | Pixel-accurate, brand-honest, becomes visual ground truth for the Angular team |
| Production Angular implementation | **Engineering team** | Angular 19, SSR, Tailwind config, components, routing, CI/CD |
| Backend integrations (forms, CMS, lead capture) | **Engineering team** | .NET API, existing infra |
| Performance, deployment, observability | **Engineering team** | Hosting, CDN, analytics |
| Long-term design system evolution | **Shared** | Engineering team extends; Claude consults on tokens and visual fidelity |
| **Design Governance enforcement** (new) | **Shared — design owner watches per-PR; user signs phase audits** | Per `design-governance-v1.md` §5 cadence |

**Why this boundary works:** Claude adds the highest leverage at brand fidelity, copy precision, IA discipline, and design-system discipline. The engineering team adds the highest leverage at framework expertise, build pipelines, deployment, and integration. Each side does what it is best at. Neither side waits on the other once the static reference exists.

### 6.5 The four governance tracks (v2 — new)

The DXP is governed by four peer tracks, all owned by the user, none subordinating another:

| Track | Locked artifact | Governs |
|---|---|---|
| Brand | `BRAND_TOKENS.md` | Color, type, logo, contrast, single-accent discipline |
| Architecture | `ARCHITECTURE_BLUEPRINT.md`, `docs/decisions/` | Product architecture, ADRs, locked technical decisions |
| Roadmap | `web-platform-roadmap-v2.md` | Phasing, scope per phase, what stays OUT |
| **Design** (new — v2) | **`design-governance-v1.md`** | Spacing, motion, illustration, interaction, responsive, visual hierarchy — discipline as the surface scales |

Design Governance was introduced after Pass 1 review at user direction: *"once Angular implementation begins, design drift becomes the biggest risk."* See `design-governance-v1.md` for the six discipline areas, escalation paths, drift signals, and review cadence.

---

## 7. The four-phase roadmap (summary; detail in `web-platform-roadmap-v2.md`)

| Phase | Scope | Outcome | Strategic meaning |
|---|---|---|---|
| **Phase 1 — Homepage** | Production-grade homepage, Angular SSR, full responsive, architecture section, capability overview | Live at elpisitsolutions.com root; replaces existing site | Brand trust |
| **Phase 2 — Core ecosystem pages** | Platform · Capabilities · Architecture · Predictive Maintenance · Edge & Connectivity | Five canonical pages live; nav fully populated | Platform legitimacy |
| **Phase 3 — Industries + Resources** | Industries (Automotive, Pharma, Oil & Gas, Defense, Aerospace, Heavy Manufacturing) · Resources (datasheets, brochures, whitepapers) · Customer stories | Sales surface complete; downloadable assets gated and tracked | Market credibility |
| **Phase 4 — Advanced ecosystem** | ROI calculators · Demo environments · Documentation portal · Interactive architecture · Lead-gen workflows · Partner portal | DXP fully operational as ecosystem digital surface | Ecosystem operationalization |

The Phase ↔ Strategic-meaning mapping (rightmost column) was crystallized during Pass 1 review and captures the maturity arc of the platform.

---

## 8. Anti-patterns the DXP explicitly refuses

| Don't | Why |
|---|---|
| Ship the homepage on a stack we cannot extend | Phase 1 is the foundation; if it cannot grow into Phases 2–4, we rebuilt the marketing microsite trap. |
| Merge the DXP codebase into EREMOS V2 UI | Different audiences, different tones, different release cadences. Coupling = compounding drag. |
| Adopt Angular Material visual defaults | Permanent lock — premium industrial feel dies in 30 seconds of Material's button styles. |
| Hardcode hex values inside components | Token discipline collapses immediately. Every color resolves to a CSS variable from `brand-tokens.css`. |
| Treat copy as filler the engineers will replace later | Marketing intent dilutes the moment that happens. Every word ships as written or revised by the marketing track. |
| Build pages page-by-page without a component library | Spacing drifts, animations diverge, polish degrades by Page 3. |
| Skip SSR/SSG because "it's just marketing" | SEO + Lighthouse + enterprise perception all fail. This is a procurement-stage deliverable, not a brochure. |
| Treat the DXP as one-off marketing project | The 24-month horizon (§4) gets foreclosed before Phase 2 starts. |
| **Let Phase 1 scope creep into Phase 2-4 territory** (v2 addition) | Phase 1 = lean, fast, visually exceptional, sharply scoped. CMS debates, localization, partner workflows, advanced content modeling all wait. See roadmap v2 §1. |

---

## 9. Strategic intent — the single test

When a downstream decision is in debate, the test is:

> *"Would this decision make sense if we were building the digital operating surface of the Elpis ecosystem for the next decade — not just a website refresh for this quarter?"*

If yes — proceed.
If no — pause and surface the tradeoff before deciding.

The wrong moves all look like time-savers in the short term and architectural debt by month 18.

---

*Digital Experience Platform Strategy v2, 2026-05-26. LOCKED after Pass 1 review. This document governs every architectural and design decision for `elpis-global-web` across Phases 1–4. Supersedes v1 as the canonical strategic framing.*
