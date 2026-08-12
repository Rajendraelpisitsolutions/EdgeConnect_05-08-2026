<!--
File:        docs/marketing/page-capabilities-operational-intelligence-spec-v1.md
Purpose:     Page spec for /capabilities/operational-intelligence —
             Pillar 5 deep-dive (EREMOS V2). Content-only spec
             inheriting the CapabilityDeepDive layout locked in
             page-capabilities-condition-monitoring-spec-v1.md.
Audience:    Internal — Angular engineering team, copywriters, user +
             ChatGPT (reviewers).
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-condition-monitoring-spec-v1.md
                (LOCKED — layout precedent)
             buyer-taxonomy-v1.md §2.2 (Plant manager / Ops VP primary)
                + §2.1 (CTO / CIO secondary)
             hardware-ecosystem-map-v3.md §6 (EREMOS V2 pillar source)
             positioning-amendment-v4.md (customer-logo authorization
                context — informs proof discipline on this page)
Version:     v1 — LOCKED after Pass 1 ChatGPT review (4 refinements applied)
Date:        2026-05-28
Status:      LOCKED.

Pass 1 ChatGPT review verdict (2026-05-28): "Approve after a small
v2 pass" — 4 refinements applied + 1 rejected:

  Accepted:
    - §3.2 EREMOS V2 card body tightened (was slightly dense per
      ChatGPT; restructured to lead with punchy claim, demoted
      details; report names moved to dedicated callout)
    - §1.4 NEW page metadata block (Meta Title, Meta Description,
      Canonical URL, Schema Intent) — also propagated to §9
      canonical template via separate commit on PR #71
    - §3.3 NEW "What the platform produces in return" callout —
      reporting examples as tangible outputs per ChatGPT (operations
      buyers love tangible outputs)
    - EREMOS V2 card body reinforces OEE-definition-nuance —
      "against your OEE definition, not a vendor preset" — per
      ChatGPT recommendation

  Rejected:
    - ChatGPT suggested replacing Industries column with operational
      characteristics (Multi-site operations / Mixed-vendor fleets
      / Shift-driven operations / Audit-heavy operations). Rejected
      by user-direction-locked decision: consistency across all 5
      pillar pages matters more than per-pillar optimization;
      operational characteristics already appear elsewhere on this
      page (hero, BOM, BUYERS column, trust cue, related solutions);
      industries are NOT generic in industrial B2B (real self-ID
      anchors); per-industry parenthetical descriptors already add
      operational context; Phase 3 /industries/<industry> pages
      will need cross-link from capability pages.

Sixth per-page spec in the Phase 2 wave per amendment v3 §6 step 5.
Content-only. COMPLETES THE 5 PILLAR DEEP-DIVE SET.
-->

# `/capabilities/operational-intelligence` — Page Spec v1

**Capability deep-dive for the Operational Intelligence pillar (EREMOS V2). Inherits the locked `CapabilityDeepDive` layout. Content-only spec. Completes the 5 pillar deep-dive set.**

This is the page where Plant managers / Ops VPs land when they're evaluating the analytics layer — the place where collected signals from every other pillar become OEE / alarms / incidents / reports the operations team actually uses. It is **not** an EREMOS V2 product detail page (Phase E `/eremos-v2`). It is **not** the solution narrative for any specific outcome (those are `/solutions/<solution>` pages).

Target length: **800-1,200 words page copy** per `/capabilities` hub spec §9.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Capability deep-dive for Operational Intelligence. Reader leaves with *"I now understand what the analytics layer covers, what it eliminates from my reporting stack, and which solutions it powers."*

**IS NOT:** A full EREMOS V2 product detail page (Phase E `/eremos-v2`). A solution narrative (lives on `/solutions/<solution>` pages). The full asset-hierarchy / multi-tenant data-model documentation (lives on Phase E product page).

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Plant manager / Ops VP (§2.2)
- Lands here when evaluating the analytics layer of the platform — *"this is where OEE numbers actually come from, this is where shift reports actually generate"*
- Wants: defensible OEE, less manual reporting, audit-ready data, mixed-fleet dashboard scaling
- CTA preference: *"Book a scoping call"* / *"Bring us your X"* > *"Talk to engineering"*
- Vocabulary that lands: OEE Segments, shift handover, mixed-vendor cells, audit-ready, replace spreadsheet operations, persistent alarms
- Vocabulary that backfires: *"smart factory"*, *"AI insights"*, *"digital transformation"*

**Secondary buyer:** CTO / CIO (§2.1)
- Lands here when evaluating EREMOS V2 as the multi-tenant analytics platform — particularly the data-model, tenant isolation, and integration posture
- Wants: defensible architecture, multi-tenant isolation guarantees, vendor-consolidation story
- CTA preference: *"Talk to engineering"* / *"Request an architecture review"*

### 1.4 Page metadata (SEO + HTML head) — NEW PATTERN per Pass 1 review

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Operational Intelligence — OEE, alarms, reports · Elpis* |
| **Meta description** (140-160 chars) | *Multi-tenant analytics platform for industrial operations. Auditable OEE, persistent alarms, incident workflows, and shift reports the team uses.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/capabilities/operational-intelligence` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`; product card uses `Product` schema linked to Phase E `/eremos-v2`; cross-link to `/architecture` page uses `relatedLink` |

**Why this block exists:** Phase 2 page specs are the source of truth for the engineering team's HTML `<head>` configuration. Authoring meta information here (instead of leaving it to engineering judgement at implementation time) prevents SEO drift and ensures every page has a verifiable meta footprint reviewable against the page's actual content.

**Template-level note:** added to `/capabilities` hub spec §9 canonical template by separate commit on PR #71 — every subsequent Phase 2 page spec (and future content specs) includes this block.

---

## 2. Page structure — sections at a glance

Per design-system v3 §14 `CapabilityDeepDive` layout. Same 9 sections.

| # | Section | Mode | Component(s) |
|---|---|---|---|
| 1 | Hero | `dark-deep` | `SectionShell` + `Button` × 2 |
| 2 | Products in this pillar (EREMOS V2) | `dark` | `CapabilityCard` × 1 with pillar-5 accent |
| 3 | What this pillar eliminates from your BOM | `light` | Bulleted list |
| 4 | Strategic adjacencies | `light` | 3-column grid |
| 5 | Where this fits in the Industrial Intelligence Stack | `light-tinted` | `DiagramFrame` focused on Pillar 5 + cross-link to `/architecture` |
| 6 | Trust posture for this pillar | `light-tinted` | §16 trust cue content pattern |
| 7 | Related solutions | `light` | `CapabilityCard` × 2 (solution-card variant) |
| 8 | Cross-lens navigation | `light-tinted` | §17 cross-lens pattern (3 cards) |
| 9 | Final CTA | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: CAPABILITY · OPERATIONAL INTELLIGENCE
>
> HEADLINE (size.3xl semibold):
> Turn machine data into OEE, alarms, incident workflows, and reports your team actually uses.
>
> CUSTOMER QUESTION LEAD (italic):
> *"How do I turn all of this into OEE, alarms, incidents, and reports the team actually uses?"*
>
> PRIMARY CTA: Book a scoping call
> HREF: `/contact?intent=operational-intelligence-scoping`
>
> SECONDARY CTA: Talk to an engineer about Operational Intelligence
> HREF: `/contact?intent=operational-intelligence-engineering`

---

### 3.2 Section 2 — Products in this pillar

> EYEBROW: PRODUCTS IN THIS PILLAR

#### Card — EREMOS V2 (pillar-5 accent)

> EYEBROW: MULTI-TENANT ANALYTICS PLATFORM
> TITLE: EREMOS V2 — Industrial intelligence for operations teams
> BODY:
> Multi-tenant analytics platform that turns collected signals into operational decisions. Models the real industrial hierarchy — **PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT** — with first-class Devices, Tags, and quality codes. Computes **OEE via Segments** (RUNNING / PLANNED_STOP / UNPLANNED_STOP / IDLE / SETUP) on edge-collected signals — **against your OEE definition, not a vendor preset**. **Persistent alarms with incident workflows**, configurable alerting on the channels your operations team already uses. **Per-tenant isolation by design**. Dashboards split automatically by device class — mixed fleets render cleanly.
> FOOTER: Multi-tenant SaaS · PLANT→…→SUB_EQUIPMENT hierarchy · OEE Segments · per-tenant isolation
> LINK: *(Phase E product detail — coming soon)*

---

### 3.3 Section 3 — What this pillar eliminates from your BOM

> EYEBROW: WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
>
> SUBHEAD:
> Operational Intelligence consolidates the reporting / alarm / dashboarding tools that plants typically run separately — into one analytics platform on one canonical data layer.
>
> BULLETED LIST:
>
> - The stitched-spreadsheet OEE process (operator → supervisor → manager → corporate, each adding their own re-calculation)
> - A separate alarm-management system bolted onto the SCADA
> - A separate incident-workflow tool (or worse, a shared inbox + email threads)
> - A separate reporting tool generating shift reports that don't reconcile with the OEE dashboards
> - Per-site SCADA-based dashboards that don't compose across plants
> - The manual effort to keep tool-life records when the tool-life sensors are already publishing data
> - Multi-vendor dashboard sprawl when every CNC / PLC / DAQ vendor ships its own visualization layer

**Callout sub-section — What the platform produces in return (NEW per Pass 1 review):**

Visually distinct from the elimination list above. Same `SectionShell` mode (light), small sub-heading "WHAT YOU GET INSTEAD" + a tight tangible-outputs row:

> EYEBROW (small-caps, dark on light):
> WHAT YOU GET INSTEAD
>
> TANGIBLE OUTPUTS (size.base, inline-comma list or 2-column grid):
> Shift reports · OEE summaries · Downtime breakdowns · Tool-life trends · Incident histories · Per-cell utilization · Cross-site benchmarking views · Audit-ready configuration history
>
> SUBLINE (size.sm italic, max-width 60ch):
> *Same definitions across every plant. Same math across every shift. Same source-of-truth across every dashboard.*

**Why this callout exists:** ChatGPT v1 review observed that operations buyers respond to tangible outputs (not abstract "analytics" claims). The BOM-elimination list above is what the platform removes; this callout is what it produces in exchange. The pair reads as a balanced economic story: less of the noisy stuff, more of the report-team-actually-uses stuff.

**Anti-patterns for this callout:**
- ❌ No screenshots in Phase 2 (per proof-architecture v1 §5.3 — real EREMOS V2 screenshots wait for Phase 3 product readiness + approval)
- ❌ No specific OEE percentages or downtime figures (per proof-architecture v1 §5.1 — outcome metrics live on `/solutions/<solution>` in context)
- ❌ No promise of report customization detail (Phase E `/eremos-v2` product page covers report-template authoring)

---

### 3.4 Section 4 — Strategic adjacencies

> EYEBROW: WHO IT'S FOR · WHERE IT DEPLOYS
>
> COLUMN 1 — BUYERS:
> - **Plant manager / Ops VP** — defensible OEE, less manual reporting, audit-ready data
> - **CTO / CIO** — vendor consolidation, multi-tenant architecture, OT/IT integration
> - **Quality / compliance manager** — audit-defensible OEE, per-tag quality codes, hash-chained configuration history
> - **Multi-site operations leadership** — fleet-level dashboards, per-site identity, cross-plant benchmarking
>
> COLUMN 2 — INDUSTRIES:
> - Manufacturing — discrete (CNC, machining, automotive parts) → OEE / alarms / shift reports
> - Manufacturing — process (flow / temperature / pressure analytics)
> - Oil & Gas (operational dashboards for surface and downhole equipment)
> - Power & Energy (substation operational visibility; generation OEE)
> - Water & Utilities (pump-station operational dashboards; flow / pressure analytics)
> - OEM machine monitoring (installed-base operational visibility for service organizations)
>
> COLUMN 3 — DEPLOYMENT FOOTPRINT:
> - Operating across India and the Middle East
> - Multi-tenant by design — one deployment serves many sites or business units without data leakage
> - On-prem / private-cloud / Elpis-managed deployment options
> - Dashboards split automatically by device class — mixed fleets render cleanly without per-vendor visualization work

---

### 3.5 Section 5 — Where this fits in the Industrial Intelligence Stack

> EYEBROW: WHERE IT FITS
>
> SECTION TITLE:
> Operational Intelligence is the analytics layer of the Industrial Intelligence Stack — where signals become decisions.
>
> BODY:
> Pillar 5 receives signals from all four other pillars: Connectivity & Edge (controller-collected signals), Data Acquisition (direct sensor signals from mDAQ), Asset Intelligence (utilization + location + OEE inputs from mTracker), and Condition Monitoring (vibration alarms from VAS; oil-health data from E-IDOS — streaming integration on the near-term roadmap). EREMOS V2 turns those signals into auditable OEE, persistent alarm records, incident workflows, and shift / OEE / downtime / tool-life reports.
>
> DIAGRAM FRAME (DiagramFrame focused on Pillar 5 layer)
>
> CAPTION:
> *Pillar 5 is the analytics layer — where collected signals become operational decisions. See the full Industrial Intelligence Stack → `/architecture`*

---

### 3.6 Section 6 — Trust posture for this pillar

Per §16 trust cue content pattern. Cue focus per buyer-taxonomy v1 §2.2 + §2.1: multi-tenant isolation, no data leakage.

> EYEBROW: TRUST POSTURE
>
> BODY:
> EREMOS V2 is **multi-tenant by design**. One deployment serves many sites, business units, or customers — and tenant data never blends. For multi-plant operators, that means each plant's data stays isolated within the tenant boundary you define. For OEM machine builders running EREMOS V2 to monitor their installed base, that means each customer's machine data stays isolated from every other customer's machine data. Per-tenant isolation is the foundation of the analytics layer's trust posture.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.7 Section 7 — Related solutions

> EYEBROW: RELATED SOLUTIONS
>
> SUBHEAD (size.md):
> Operational Intelligence powers every solution in the Elpis stack. Two representative outcome stories:

#### Card 1 — Multi-site Operations (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · MULTI-SITE OPERATIONS
> TITLE: One operational view across every plant
> BODY: For multi-plant operators — EREMOS V2 aggregates OEE, alarms, and reports across sites without losing per-site identity or tenant isolation.
> LINK: Read the solution → `/solutions/multi-site-operations` *(existing v2; v3 in Phase E)*

#### Card 2 — CNC Machining (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · CNC MACHINING
> TITLE: Mixed-vendor CNC floors on one operational view
> BODY: For shops running Fanuc + Brother + Mazak + others — EREMOS V2 turns the canonical CNC signals into per-shift OEE, alarm-pattern visibility, and replace-the-spreadsheet operational reporting.
> LINK: Read the solution → `/solutions/cnc-machining` *(existing v2; v3 in Phase E)*

---

### 3.8 Section 8 — Cross-lens navigation

Per §17 preset for `/capabilities/<pillar>`:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | ARCHITECTURE | How does this pillar fit the data flow? | `/architecture` |
| 2 | SOLUTION · MULTI-SITE OPERATIONS | The outcome-organised version for plant operators | `/solutions/multi-site-operations` |
| 3 | CAPABILITIES | Back to all 5 pillars | `/capabilities` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.2 Plant manager / Ops VP CTA preference:

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your OEE definition. We'll scope the deployment.
>
> SUBHEAD:
> Whether you're trying to defend OEE numbers across mixed-vendor cells, consolidate per-site dashboards into one fleet view, or replace the spreadsheet operations that everyone agrees aren't working — the first conversation is about your specific OEE definition and your specific reporting cadence. Bring us those, and we'll scope what EREMOS V2 delivers on top of your existing operational data.
>
> PRIMARY CTA: Book a scoping call
> HREF: `/contact?intent=operational-intelligence-scoping`
>
> SECONDARY CTA: Talk to an engineer about Operational Intelligence
> HREF: `/contact?intent=operational-intelligence-engineering`

---

## 4. Components used

All from design-system v3 LOCKED — no new components.

`SectionShell` (modes), `CapabilityCard` (pillar-5 + compact), `Button` (primary + secondary, lg), `DiagramFrame`, `CTASection`, §16 trust cue pattern, §17 cross-lens pattern.

---

## 5. Verbatim copy summary

Page copy collected in sections §3.1-§3.9. ~960 words total (within 800-1,200 target).

---

## 6. Anti-patterns specific to this page

| Don't | Why |
|---|---|
| Use *"AI-powered analytics"* / *"smart factory"* / *"digital transformation"* | Backfires with Plant manager AND CTO per buyer-taxonomy §2.1 + §2.2 |
| Add specific OEE-improvement percentages | Per proof-architecture v1 §5.1 — outcome metrics live on `/solutions/<solution>` in context, never on capability pages |
| Add customer logos or specific named-customer deployment stories | Per proof-architecture v1 §4 — trust signaling is platform-level (homepage + `/platform` + `/customers`); capability pages stay capability-level |
| List specific SCADA / MES / historian product integrations on this page | Lives on `/architecture` (integration patterns) or Phase E `/eremos-v2` (product page) — capability page stays capability-level |
| Add competitor names (Wonderware, GE iFix, Inductive Automation, MachineMetrics, Sight Machine, Tulip) | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory |
| Imply EREMOS V2 ingests E-IDOS condition data today | Per positioning v3 §6 commitment #3 — E-IDOS → EREMOS V2 streaming is near-term roadmap, not current behavior; §3.5 honest framing required |
| Add fabricated screenshots of EREMOS V2 dashboards | Per proof-architecture v1 §5.3 — real EREMOS V2 screenshots wait for Phase 3 product readiness + approval; capability page uses no screenshots in Phase 2 |

---

## 7. Sign-off checklist (v1 lock)

- [ ] Page copy fits 800-1,200 word target (current: ~960 words)
- [ ] All 9 sections per CapabilityDeepDive layout
- [ ] Customer question verbatim from hardware-ecosystem-map v3 §1
- [ ] EREMOS V2 card uses accurate descriptors from hardware-ecosystem-map v3 §6.1 (PLANT→…→SUB_EQUIPMENT hierarchy, OEE Segments enumerated, multi-tenant by design, dashboard split by device class)
- [ ] §3.5 honest framing on E-IDOS streaming integration as ROADMAP per positioning v3 §6 commitment #3
- [ ] §3.6 trust cue focuses on multi-tenant isolation per buyer-taxonomy §2.2 + §2.1
- [ ] §3.7 related solutions are operational-outcome-organised (Multi-site Operations, CNC Machining — both existing v2 / v3 in Phase E)
- [ ] Final CTA uses Plant-manager-preferred framing ("Book a scoping call" + "Talk to an engineer about Operational Intelligence")
- [ ] No vocabulary that backfires per §2.1 / §2.2 (no AI insights, no smart factory, no digital transformation)
- [ ] No outcome percentages, no customer logos, no screenshots, no competitor names
- [ ] All components from design-system v3 LOCKED
- [ ] Page-spec structure follows §9 canonical template

---

## 8. Out of scope for v1

- **Full EREMOS V2 product detail.** Phase E `/eremos-v2` covers: full asset-hierarchy data model, OEE Segment calculation detail, alarm / incident workflow configuration, reporting template authoring, tenant administration, integration patterns (REST API, MQTT, webhooks).
- **Real EREMOS V2 product screenshots.** Phase 3 once product is ready and screenshots are approved per proof-architecture v1 §5.3.
- **Solution narratives.** All `/solutions/<solution>` pages — Multi-Site Operations, CNC Machining, Brownfield Modernization, OEM Machine Monitoring, Precision Manufacturing, Predictive Maintenance, Edge Connectivity.
- **Industry-specific framings.** Phase 3 `/industries/<industry>`.
- **Pricing detail.** Phase 3 `/pricing` or commercial-engagement teaser on `/platform`.
- **SCADA / MES / historian integration patterns.** Lives on `/architecture` (integration patterns) and Phase E `/eremos-v2` (product page).
- **Multi-tenant administration / user-management detail.** Phase E `/eremos-v2` product page.

---

*`/capabilities/operational-intelligence` Page Spec v1 — LOCKED 2026-05-28 after Pass 1 user + ChatGPT review (4 refinements applied: EREMOS card density tightened, §1.4 metadata block introduced, §3.3 "what you get instead" callout added, OEE-definition-nuance reinforced in EREMOS card body; 1 rejected: industries swap to operational characteristics — kept industries for cross-pillar consistency and Phase 3 cross-link enablement). **Sixth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 5 — COMPLETES THE 5 PILLAR DEEP-DIVE SET.** Content-only spec — inherits `CapabilityDeepDive` layout from `page-capabilities-condition-monitoring-spec-v1` (LOCKED). Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.2 + §2.1, proof-architecture v1, design-system v3 §14 + §16 + §17, hardware-ecosystem-map v3 §6, positioning v3 §6 commitment #3.*
