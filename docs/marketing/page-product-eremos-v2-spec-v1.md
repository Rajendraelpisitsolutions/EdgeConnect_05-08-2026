<!--
File:        docs/marketing/page-product-eremos-v2-spec-v1.md
Purpose:     Page spec for /eremos-v2 — the PRODUCT detail page for EREMOS V2
             (Pillar 5, Operational Intelligence software product). The second
             software product page; INHERITS the LOCKED §24 ProductDetail shape
             proven by /edgeconnect.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the LOCKED
             ProductDetail page layout (design-system-v4
             .md §24). Inherits the /edgeconnect shape-setter (page-product-
             edgeconnect-spec-v1.md, LOCKED).
Companion:   design-system-v4.md §24/§24.A/§24.3 (LOCKED)
             page-product-edgeconnect-spec-v1.md (LOCKED — the shape-setter this
                inherits; mirror its structure + disciplines)
             elpis-industrial-intelligence-platform-v5.md (datasheet — EREMOS V2
                product facts: asset model, OEE Segments, alarms/incidents,
                alerts, reports, tool-life, multi-tenant, dashboards)
             page-capabilities-operational-intelligence-spec-v1.md v1 (LOCKED —
                the Pillar 5 capability story; cross-link UP)
             page-solutions-multi-site-operations-spec-v1.md v3 + page-solutions
                -predictive-maintenance-spec-v1.md v2 (LOCKED — OI-led solutions
                that use EREMOS V2; cross-link ACROSS)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.3 (OT Architect — product-page primary per
                §24.0) + §2.2 (Plant manager / Ops VP — secondary; the OEE/
                operational-outcome interest)
             proof-architecture-v1.md §3/§4/§8
             hardware-ecosystem-map-v3.md §5.2/§6 (E-IDOS → EREMOS V2 streaming
                is near-term ROADMAP — honesty constraint)
             CLAUDE.md §3 (locked decisions) + §8 (current state)
             shared-knowledge/contracts/eremos-per-tag-mqtt.md (the canonical
                MQTT contract EREMOS V2 consumes)
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v1 — LOCKED 2026-06-04 after ChatGPT review (verdict "Approve with
                changes"; buyer decision RESOLVED — keep OT Architect primary).
                Second page on the §24 ProductDetail shape.
Date:        2026-06-04
Status:      LOCKED (Track B, inherits §24).

ChatGPT review (2026-06-04) — "Approve with changes"; confirmed the §24 shape
generalizes from a connectivity product (/edgeconnect) to an analytics product.
Buyer RESOLVED: keep OT Architect primary (CTA "Request an architecture review").
Applied: moved AI operations agents OUT of the §3.4 integration matrix into a
roadmap decision-support callout; added Quality-input handling to the OEE
explanation (§3.4); softened "no data leakage" → "tenant-scoped data boundaries /
no cross-tenant blending by design" corpus-wide (§3.1/§3.3/§3.6/§3.8/§3.9/§6);
added §3.5 rows — Identity + access, Data retention + backup, API + webhook
security + integration-contract versioning; added FAQ Q8 (retention) + Q9 (roles/
approvals); editions title → "License the tenants, sites, and capabilities you
operate"; architecture annotation "ONE INTELLIGENCE STACK" → "OEE + INCIDENTS".
"Local-LLM mandatory" kept (locked decision CLAUDE.md §3 #17, accurate).

INHERITS §24 from the /edgeconnect shape-setter. Same 11-section software-variant
composition; no new component (v3 components + §24.A spec-table). This spec
mirrors page-product-edgeconnect-spec-v1.md structure + disciplines, swapping in
the EREMOS V2 (Operational Intelligence) product content.

What ProductDetail owns here (§24.1): opens with "what it is" (not a pain
narrative); OWNS the EREMOS V2 product depth — the inbound/outbound integration
matrix, the asset model + OEE Segment definitions, multi-tenant deployment +
system requirements + versioning/support, editions + licensing mechanics. Cross-
links UP to /capabilities/operational-intelligence and ACROSS to the OI-led
solutions; never re-tells their narrative.

Source-of-truth alignment (datasheet v5 + locked decisions):
  - EREMOS V2 receives the canonical stream from EdgeConnect + mDAQ + mTracker +
    VAS TODAY. **E-IDOS → EREMOS V2 streaming (alarms/dashboards/incidents) is
    near-term ROADMAP** (hardware-ecosystem-map v3 §5.2/§6) — until it ships,
    E-IDOS is a standalone instrument. Reflected in §3.4 inbound matrix + FAQ.
  - Multi-tenant isolation (tenant-scoped data boundaries), customer-controlled cloud (opt-in),
    OEE computed on canonical signals, the OEE definition stays the customer's.
  - AI operations agents (Diagnostic / Configuration / Tag Mapping / Intelligent
    Alerting) are decision-support, human-confirmed, local-LLM-capable —
    ROADMAP (Phase 4.5), framed honest-as-roadmap (CLAUDE.md §3 #14-#17).
  - No fabricated metrics / customer names / competitor names.
  - Editions = structure + licensing mechanics, NOT pricing (labels illustrative
    until packaging approved) — inherited §24 discipline.

BUYER — RESOLVED (ChatGPT review 2026-06-04): keep OT Architect / SCADA engineer
(§2.3) PRIMARY. ChatGPT explicitly endorsed this — the product-detail page is
where the technical evaluator validates product truth (data model / integration
/ multi-tenancy / deployment / security / API); Plant managers meet EREMOS V2
through /capabilities/operational-intelligence + the OI-led /solutions pages.
CTA stays "Request an architecture review" (P-H); NO flip to "Book a scoping
call". Plant manager / Ops VP (§2.2) remains the secondary, with the §3.11 final
CTA ("Bring us your fleet and your OEE definition") giving them a hook without
changing the page's primary buyer.

Word-count target: 1,200-1,800 words page copy (spec tables + diagram
annotations not prose-counted). Post-review draft ~1,550 words (+Q8/Q9 + Quality note + AI roadmap callout).

Note: the §24 shape is proven by the /edgeconnect mockup; a /eremos-v2 static
mockup has now also been built at web/eremos-v2.html (derived from
edgeconnect.html — same styles.css + shape; copy lifted from this locked spec).
-->

# `/eremos-v2` — Page Spec v1 (ProductDetail, inherits §24)

**Product detail page for EREMOS V2 — the multi-tenant operational-intelligence platform (Pillar 5). The deepest factual EREMOS V2 surface: the integration matrix, asset model + OEE Segment definitions, multi-tenant deployment + system requirements, and editions + licensing that the capability and solution pages defer here. Second page on the LOCKED `ProductDetail` shape (§24), inheriting the `/edgeconnect` shape-setter.**

This is where a technical evaluator lands when they want to know **what EREMOS V2 is, exactly** — what it consumes and exposes, how its asset model and OEE math work, how multi-tenancy and deployment work, and how it's licensed. It is **not** the capability story (`/capabilities/operational-intelligence`) and **not** the outcome story (the OI-led `/solutions` pages); it is the **product truth**.

Target length: **1,200-1,800 words page copy** per §24 (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The EREMOS V2 product detail page. Reader leaves with *"I now know exactly what EREMOS V2 consumes and exposes, how its asset model + OEE Segments work, how multi-tenant isolation and deployment work, how it's licensed, and how it integrates with what I run — enough to take it into an architecture review."*

**IS NOT:**
- The capability page (`/capabilities/operational-intelligence`, LOCKED — the Pillar 5 *capability* story; this page provides its *spec depth* and cross-links up)
- A solution / outcome page (the OI-led `/solutions/multi-site-operations` v3 + `/solutions/predictive-maintenance` v2 cover the *outcomes*; this page cross-links across)
- The EdgeConnect product page (`/edgeconnect`, LOCKED — the sibling software ProductDetail page, Pillar 1 connectivity)
- A hardware product page (the deferred §24.B variant)
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3 — this page describes edition *structure* + licensing *mechanics*, never pricing tables)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.0)

**Primary buyer:** OT Architect / SCADA engineer (§2.3) — per §24.0, the product-detail page is read by the technical evaluator (asset model, integration, multi-tenancy, deployment, security). *(RESOLVED at ChatGPT review — OT-Architect-primary kept, not flipped to Plant-manager; see header.)*
- Lands here from `/capabilities/operational-intelligence` (cross-link for spec depth), the Platform menu, or a search for *"multi-tenant OEE platform"* / *"OEE from MQTT canonical stream"* / *"industrial incident workflow platform"*
- Wants: what it consumes/exposes (MQTT canonical stream in; REST API + webhooks out), the asset model + OEE Segment math, multi-tenant isolation, deployment options (on-prem / private cloud / managed), per-tag quality-code handling, security/RBAC
- CTA: *"Request an architecture review"* > *"Talk to an engineer about Operational Intelligence"* > datasheet. **NOT** *"Book a scoping call"* (§2.3 backfire; P-H)
- Vocabulary that lands: *multi-tenant*, *canonical signals*, *OEE Segments*, *asset model*, *per-tag quality codes*, *incident workflow*, *REST API*, *webhooks*, *data sovereignty*, *tenant-scoped isolation*

**Secondary buyer:** Plant manager / Ops VP (§2.2) — the OEE/operational-outcome interest
- Wants: defensible OEE, persistent alarms, shift reports, multi-site rollups
- Served via the §3.3 capabilities + cross-lens to the OI-led solution pages (the OUTCOME surfaces), per buyer-taxonomy §5 step 3

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Product-page pattern (inherits `/edgeconnect` §1.4).

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *EREMOS V2 — Multi-Tenant Operational Intelligence · Elpis* |
| **Meta description** (140-160 chars) | *EREMOS V2 turns canonical machine signals into auditable OEE, persistent alarms and incidents, alerts, and reports — multi-tenant, offline-capable.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/eremos-v2` |
| **Schema intent** | `schema.org/SoftwareApplication` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/operational-intelligence` + `/solutions/multi-site-operations` + `/architecture` + `/security` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` layout per §24 (LOCKED). **11 sections** (software variant; inherits `/edgeconnect`).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line definition + CTAs + trust strip | `dark-deep` | `SectionShell` + `Button` ×2 | ~90 |
| **2** | What it is — product definition + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~140 |
| **3** | Capabilities — the platform's feature set | `dark` | `CapabilityCard` grid (compact) | ~230 |
| **4** | Integration + data model — inbound/outbound matrix + asset model + OEE Segments | `light` | §24.A spec-table (Direction · Status · What) | spec (not prose) |
| **5** | Deployment + system requirements (incl. versioning/support) | `light-tinted` | §24.A spec-table + narrative | ~140 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~80 |
| **7** | Editions + licensing (structure + mechanics, not pricing) | `dark` | editions cards + narrative | ~150 |
| **8** | Trust + security posture | `light-tinted` | trust-cue content pattern (§16) | ~110 |
| **9** | Common questions (inline FAQ) — 9 Q&A | `light` | inline FAQ + `FAQPage` schema | ~520 |
| **10** | Related — cross-lens (§24.3 preset) | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · OPERATIONAL INTELLIGENCE
> HEADLINE: EREMOS V2
> SUBHEAD (max-width 64ch):
> The multi-tenant operational-intelligence platform at the top of the Elpis stack. EREMOS V2 consumes the canonical signal stream, computes OEE you can audit, tracks every alarm as a persistent incident, and produces the reports and alerts your operations team runs on — across many sites, with per-tenant isolation.
>
> PRIMARY CTA (`Button.primary.lg`): Request an architecture review → HREF `/contact?intent=eremos-v2-architecture-review`
> SECONDARY CTA (`Button.secondary.lg`): Download the datasheet → HREF `/resources/datasheet`
>
> TRUST STRIP:
> Multi-tenant · tenant-scoped data boundaries · OEE on canonical signals · customer-controlled cloud (opt-in) · offline-capable · per-tag quality codes end to end.
>
> HERO VISUAL (right column, per §24 hero-visual slot): two-column hero (`hero__inner`) — copy left, a product-relevant `HeroComposite`-style SVG right (matches the homepage hero; no blank right half). For EREMOS V2: an **OEE / incident dashboard panel** (OEE tile, active-alarms tile, plants-online tile, production-signal sparkline). Decorative (`aria-hidden`), token-only; dashboard numbers are illustrative chrome (not OEE claims), with an "illustrative" caption. See the mockup `web/eremos-v2.html`.

**Anti-patterns:** Headline is the product name + value, not a feature list. Product-led, not a customer-pain narrative. CTA "Request an architecture review" (P-H), not "Book a scoping call". No "single source of truth" / "single pane of glass" / "AI insights".

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: Canonical signals in. Operational decisions out.
>
> BODY:
> EREMOS V2 is a multi-tenant analytics platform that turns the canonical signal stream — the normalized output of EdgeConnect and the acquisition pillars — into the things operations runs on: OEE Segments, persistent alarms and incident workflows, alerts on your channels, and reports your team will actually read. It models your plant as a real industrial hierarchy and computes against the same canonical vocabulary every source already speaks.
>
> BODY ¶2 (muted):
> It is the **Operational Intelligence** pillar of the Industrial Intelligence Ecosystem. For the capability story, see → `/capabilities/operational-intelligence`. This page is the product detail — the integration model, asset model + OEE math, multi-tenancy, deployment, and licensing.

### 3.3 Section 3 — Capabilities

> EYEBROW: CAPABILITIES
> SECTION TITLE: What the platform does.

Feature grid (compact `CapabilityCard`s):

> - **A real industrial asset model.** PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT, with first-class Devices and Tags, units, engineering ranges, and quality codes.
> - **OEE you can audit.** Availability × Performance × Quality, computed from time-bounded Segments built on edge-collected signals. The OEE definition stays yours — segment classification, shift schedule, and targets are configured to how your plant already defines OEE.
> - **Persistent alarms + incidents.** Inbound alarms become tracked records with open/close state and incident grouping — triage, assignment, resolution, closure, with operator notes at each step.
> - **Alerts on your channels.** Notifications routed to email, chat, and ticketing webhooks your operations team already uses.
> - **Reports the team will actually read.** Shift reports, OEE summaries, downtime breakdowns, tool-life trends — PDF and Excel export.
> - **Tool-life tracking.** Dedicated ingestion for tool-wear and remaining-life telemetry, so maintenance happens before failures.
> - **Multi-tenant by design.** One deployment serves many sites or business units with per-tenant isolation — tenant data is isolated by tenant boundary; no cross-tenant blending by design.
> - **Dashboards that scale to mixed fleets.** Panes split automatically by device class — CNC, PLC, DAQ, asset tracker, meter.

**Note:** tenant-isolation phrasing ("tenant-scoped data boundaries", "no cross-tenant blending by design") is a *mechanism* description, not an absolute marketing guarantee — keep it mechanism-tethered (avoid the bare absolute "no data leakage").

### 3.4 Section 4 — Integration + data model (§24.A spec-table)

> EYEBROW: INTEGRATION & DATA MODEL
> SECTION TITLE: What it consumes, what it exposes.

Integration matrix per §24.A — **Direction (Inbound/Outbound) + Status** columns (the EREMOS V2 analogue of the EdgeConnect protocol matrix). Status per the datasheet v5 + the E-IDOS roadmap honesty constraint.

| Interface | Direction | Status | What |
|---|---|---|---|
| **Canonical MQTT stream** | Inbound | Available | The normalized stream from **EdgeConnect** (+ **mDAQ**, **mTracker**, **VAS**) per the canonical per-tag MQTT contract. Per-tag quality codes (Good / Uncertain / Bad / Stale) carried end to end. |
| **Tool-life ingestion** | Inbound | Available | Dedicated path for tool-wear / remaining-life telemetry. |
| **E-IDOS streaming** | Inbound | Roadmap | Oil-health alarms / dashboards / incidents into the same stack. Near-term roadmap; until it ships, E-IDOS is a standalone instrument. |
| **REST API** | Outbound | Available | OEE rollups + incident records for MES / ERP / BI consumers. |
| **Alert webhooks** | Outbound | Available | Email, chat, and ticketing webhooks. |
| **Report export** | Outbound | Available | Shift / OEE / downtime / tool-life reports — PDF and Excel. |

> **Asset model:** PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT, with Devices + Tags (units, engineering ranges, quality codes). **OEE Segments:** RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP — time-bounded, computed from cycle-time + parts-count signals. **Quality inputs** — good count, reject count, scrap reason, or a customer-approved quality source — are configured per deployment; when Quality data isn't available directly from the controller, the customer-approved source is mapped during architecture review.
>
> **Roadmap — decision-support layer:** Diagnostic, Configuration, Tag Mapping, and Intelligent Alerting agents (Phase 4.5). Decision-support only — they propose actions for a human to confirm, never autonomously change state; local-LLM support is mandatory, cloud LLMs optional. (Not an inbound/outbound interface — kept out of the matrix above.)
>
> CAPTION (size.sm): This page carries the product-level integration model. Deployment-specific schemas, **integration-contract versioning**, retention, and API detail are confirmed during the architecture review.

### 3.5 Section 5 — Deployment + system requirements

> EYEBROW: DEPLOYMENT & REQUIREMENTS
> SECTION TITLE: How it runs.

| | |
|---|---|
| **Deployment** | In your data center, a private cloud, or as a managed service. Multi-tenant by design — one deployment, many sites / business units. |
| **Connectivity** | Consumes the canonical stream over standard MQTT (any compliant broker); the platform does not require Elpis to provide the broker. Cloud connectivity is opt-in, not required. |
| **Tenancy + isolation** | Per-tenant data isolation — tenant-scoped data boundaries; no cross-tenant blending by design. |
| **Identity + access** | RBAC, SSO / identity integration, user roles, tenant boundaries, incident ownership, and service-account responsibilities are confirmed during architecture review. |
| **Network + host requirements** | Host / runtime class, database + storage sizing, broker endpoints, and certificate handling are confirmed during architecture review against tenant count, site count, and tag volume. No fixed figures published — sizing depends on sites, tags, publish frequency, and retention. |
| **Data retention + backup** | Retention windows for operational history, report history, and alarm + incident history, plus backup / restore and disaster-recovery expectations, are scoped during architecture review against site count, tag volume, and customer policy. |
| **API + webhook security** | API authentication, the webhook security model (delivery / retry behavior), payload-schema + integration-contract versioning, and integration ownership are confirmed during architecture review. |
| **Versioning + support** | Released as a versioned platform; configuration changes are audited. Support boundary spans EREMOS V2, its ingestion of the canonical stream, and the EdgeConnect integration. |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: EREMOS V2 in the stack.

`ArchitecturePanel.interactive` (product-annotated, §5.A): Floor controllers → EdgeConnect (+ acquisition pillars) → canonical MQTT stream → **EREMOS V2** (highlighted) → operations (dashboards / alerts / reports) + MES / ERP / BI (via REST API). Annotation eyebrow-as-title per §24 P-E.

**Annotations (4, per §5.A):**

| Annotated region | Eyebrow | Body |
|---|---|---|
| Canonical stream → EREMOS V2 | CANONICAL IN | Consumes the normalized stream from EdgeConnect + the acquisition pillars; per-tag quality codes carried end to end. |
| EREMOS V2 core | OEE + INCIDENTS | OEE Segments, persistent alarms + incidents, reports, tool-life — computed once on canonical signals across every source. |
| Multi-tenant overlay | PER-TENANT ISOLATION | One deployment, many sites / business units; tenant-scoped data boundaries, no cross-tenant blending by design. |
| EREMOS V2 → enterprise | API OUT | REST API + webhooks expose OEE rollups + incident records to MES / ERP / BI — beside, not replacing. |

> CAPTION: EREMOS V2 aggregates across per-plant EdgeConnect runtimes; multi-site visibility comes from the platform, not from one runtime spanning plants. See the full cross-pillar story → `/architecture`.

### 3.7 Section 7 — Editions + licensing

> EYEBROW: EDITIONS & LICENSING
> SECTION TITLE: License the tenants, sites, and capabilities you operate.
>
> *Edition labels are illustrative until commercial packaging is approved; this section describes packaging + licensing mechanics, not pricing.*

Editions cards (illustrative labels):

> - **Starter** — single-tenant, single-site operational intelligence.
> - **Professional** — multi-tenant analytics for a plant or a small fleet.
> - **Enterprise** — fleet-scale multi-tenant operations across many sites + business units.

> BODY:
> EREMOS V2 deploys in your data center, a private cloud, or as a managed service. Tenancy and site scale are licensed per edition. Contact Elpis for edition feature lists and deployment-scale scoping; detailed pricing is scoped after architecture review.

### 3.8 Section 8 — Trust + security posture

> EYEBROW: TRUST POSTURE
> SECTION TITLE: Built for OT review.

Trust-cue content pattern (§16), 2 cues, cross-link `/security`:

> CUE 1 — **Multi-tenant isolation, customer-controlled data.** One deployment serves many tenants with tenant-scoped data boundaries, role separation, and customer-controlled routing — no cross-tenant blending by design. Cloud connectivity is opt-in, not required; sensitive plant data stays where you choose.
>
> CUE 2 — **Auditable + role-separated.** Configuration changes are audited; role separation is supported. AI features, when they ship, propose actions for humans to confirm — they never autonomously alter the data path (local-LLM-capable; cloud LLMs optional).
>
> CROSS-LINK: Read the full operational trust posture → `/security`

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 9 Q&A.

> #### Q1. What does EREMOS V2 consume, and from where?
> The canonical MQTT signal stream — the normalized output of EdgeConnect and the acquisition pillars (mDAQ, mTracker, VAS) — per the canonical per-tag MQTT contract, with per-tag quality codes carried end to end. E-IDOS streaming into EREMOS V2 is near-term roadmap; until it ships, E-IDOS is a standalone oil-health instrument.
>
> #### Q2. How is OEE computed, and can we defend it?
> OEE Segments (RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP) are computed from edge-collected cycle-time and parts-count signals, each timestamped at the edge and retained. The OEE definition stays yours — segment classification, shift schedule, and targets are configured to how your plant already defines OEE; the platform computes against your definition, not one of ours.
>
> #### Q3. How does multi-tenancy isolate our data?
> One deployment serves many sites or business units with per-tenant data isolation — tenant-scoped data boundaries; no cross-tenant blending by design. Role separation is supported. Tenant and site scale are confirmed during architecture review.
>
> #### Q4. Can we run it on-prem / air-gapped, or must it be cloud?
> EREMOS V2 deploys in your data center, a private cloud, or as a managed service. Cloud connectivity is opt-in, not required — sensitive plant data stays on premise; filtered rollups flow out only on your terms.
>
> #### Q5. How do downstream systems (MES / ERP / BI) consume it?
> Via the REST API (OEE rollups + incident records) and alert webhooks (email / chat / ticketing). Reports export to PDF and Excel. EREMOS V2 sits beside your existing systems — they consume its rollups, it doesn't replace them.
>
> #### Q6. What AI is in it, and does it change anything automatically?
> AI operations agents (Diagnostic, Configuration, Tag Mapping, Intelligent Alerting) are on the roadmap (Phase 4.5). They are decision-support: they propose actions for a human to confirm and never autonomously alter the data path. Local-LLM support is mandatory; cloud LLMs are optional.
>
> #### Q7. How is EREMOS V2 sized for sites and tag volume?
> Sizing depends on tenant count, site count, tag volume, publish frequency, and retention window. The architecture review scopes the host, database, and storage requirements against your real fleet; no fixed figures are published.
>
> #### Q8. How long is operational history retained?
> Retention depends on tenant count, site count, tag volume, report history, and customer policy. Retention windows and archive / export requirements are scoped during architecture review.
>
> #### Q9. How are roles and approvals handled?
> RBAC with tenant admins, incident ownership, and audit permissions; SSO / identity integration is confirmed during architecture review. Role separation is supported, and configuration changes are audited.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 product-page cross-lens preset:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · OPERATIONAL INTELLIGENCE | The Pillar 5 capability story | `/capabilities/operational-intelligence` |
| 2 | SOLUTION · MULTI-SITE OPERATIONS | The fleet-scale OEE outcome built on EREMOS V2 | `/solutions/multi-site-operations` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

*(Lead solution = `/solutions/multi-site-operations` — the most EREMOS-V2-centric OI outcome. `/solutions/predictive-maintenance` is the secondary OI showcase if a 4th card is ever allowed. Flag for review.)*

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your fleet and your OEE definition.
> SUBHEAD: A site list, a tenancy model, the canonical stream from your floor, and the OEE definition you already run — that's what we scope an architecture review against. Demos run on real signals, not slideware.
> PRIMARY CTA: Request an architecture review → `/contact?intent=eremos-v2-architecture-review`
> SECONDARY CTA: Download the datasheet → `/resources/datasheet`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (inherits `/edgeconnect`).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3; §3.7 editions |
| `ArchitecturePanel.interactive` (product-annotated) | §3.6 |
| §24.A spec-table content pattern | §3.4 integration matrix; §3.5 deployment table |
| Trust-cue content pattern (§16) | §3.8 |
| Cross-lens content pattern (§17, §24.3 preset) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,550 words page copy** (within the §24 1,200-1,800 target; post-ChatGPT-review with +Q8/Q9, Quality note, and the AI roadmap callout). Spec-table cell text (§3.4, §3.5) + §3.6 annotations are NOT prose-counted, per §24.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + §24.4:

| Don't | Why |
|---|---|
| Re-tell the capability or solution narrative as primary content | §24.1 — cross-link UP (capability) + ACROSS (OI solutions); ProductDetail owns the spec. |
| Open with a "customer pain" empathy narrative | §24 — opens with "what it is" (§3.2). |
| List E-IDOS streaming as Available | E-IDOS → EREMOS V2 streaming is near-term ROADMAP (hardware-ecosystem-map v3 §5.2). §3.4 + §3.9 Q1 carry the honest framing. |
| Present AI agents as shipped or autonomous | AI agents are roadmap (Phase 4.5), decision-support, human-confirmed, local-LLM-capable (CLAUDE.md §3 #14-#17). §3.4 + §3.8 + §3.9 Q6 frame them honest-as-roadmap. |
| Publish pricing tables or treat edition labels as locked packaging | §24 anti-pattern — structure + mechanics only; labels illustrative until Pricing governance (Phase 3). |
| Use the bare absolute "no data leakage" | Use mechanism-tethered phrasing ("tenant-scoped data boundaries", "no cross-tenant blending by design") — per-tenant isolation is a mechanism, not a marketing absolute. |
| Imply EREMOS V2 needs the cloud | Opt-in, not required; on-prem / air-gapped supported (§3.5 + §3.9 Q4). |
| Imply EREMOS V2 aggregates via one EdgeConnect across plants | Per-gateway identity — EREMOS V2 aggregates ACROSS per-plant EdgeConnect runtimes (§3.6 caption). |
| Use "Book a scoping call" as the primary CTA | §2.3 backfire; P-H — product page uses "Request an architecture review" (pending the buyer REVIEW FLAG). |
| Drop the integration matrix's Direction or Status column | §24.A — inbound vs outbound, today vs roadmap, must be explicit. |
| Fabricated metrics, customer names, or competitor names | proof-architecture §3/§4/§8. |
| Introduce a new visual primitive | §24 governance — v3 components + §24.A only. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,450); spec tables not prose-counted
- [x] All 11 ProductDetail sections present per §24 (inherits `/edgeconnect`)
- [x] §3.1 hero product-led; CTA "Request an architecture review" (P-H) — buyer REVIEW FLAG resolved
- [x] §3.2 opens with "what it is"; cross-links UP to `/capabilities/operational-intelligence`
- [x] §3.4 integration matrix carries Direction + Status; E-IDOS streaming = Roadmap; AI agents = Roadmap
- [x] §3.4 asset model + OEE Segments stated; per-tag quality codes end to end
- [x] §3.5 deployment honest (on-prem / private cloud / managed; opt-in cloud); Network+host row; versioning/support
- [x] §3.6 uses `ArchitecturePanel.interactive` (product-annotated); per-tenant isolation + aggregates-across-runtimes
- [x] §3.7 editions = structure + mechanics only; labels illustrative; NO pricing
- [x] §3.8 trust cues cover multi-tenant isolation + customer-controlled data + audited/role-separated + AI-proposes-humans-decide
- [x] §3.9 FAQ uses `FAQPage` schema; Q1 sources (E-IDOS roadmap), Q2 OEE-definition-stays-yours, Q3 multi-tenant isolation, Q6 AI roadmap/non-autonomous
- [x] §3.10 cross-lens matches §24.3 (operational-intelligence + multi-site-operations + architecture)
- [x] OEE-definition-stays-yours present (R2 consistency)
- [x] No new component beyond v3 + §24.A
- [x] §1.4 metadata (SoftwareApplication); no banned vocabulary
- [x] No fabricated metrics / customer names / competitor names
- [x] **Inherited §24 shape applied consistently with `/edgeconnect`** (the shape's second instance — confirms the pattern generalizes)
- [x] **Buyer decision RESOLVED** — OT-Architect-primary kept (ChatGPT-endorsed); CTA "Request an architecture review" (no flip)
- [x] ChatGPT review pass applied (verdict "Approve with changes"); v2 items — AI agents moved out of the §3.4 matrix to a roadmap callout; Quality-input handling added; "no data leakage" softened corpus-wide; §3.5 +Identity/+Retention+Backup/+API-webhook-security rows; FAQ +Q8 (retention) +Q9 (roles); editions title; annotation "OEE + INCIDENTS"

---

## 8. Out of scope for v1

- **Full API / schema reference.** Scoped at architecture-review / developer-docs time; §3.4 stays at the product-level integration matrix.
- **EdgeConnect product detail.** `/edgeconnect` (LOCKED) — cross-link.
- **Capability + solution narratives.** `/capabilities/operational-intelligence` + the OI-led `/solutions` pages (LOCKED) — cross-link, don't duplicate.
- **AI agent detail.** Phase 4.5 interactive-agent work; this page frames them honest-as-roadmap only.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3). Structure + mechanics only here.
- **Security walkthrough.** `/security` — cross-link from §3.8.

---

*`/eremos-v2` Page Spec **v1 LOCKED 2026-06-04** (ProductDetail; inherits the LOCKED §24 shape from the `/edgeconnect` shape-setter) after ChatGPT review (verdict "Approve with changes"; buyer RESOLVED to OT-Architect-primary; all changes applied). Second page on the ProductDetail shape — confirms the shape generalizes from a connectivity product to an analytics product with no new component (v3 components + §24.A spec-table; the §4 matrix is an inbound/outbound integration matrix rather than a protocol matrix, same pattern). Source-of-truth aligned: E-IDOS → EREMOS V2 streaming + AI operations agents framed honest-as-roadmap; multi-tenant isolation / customer-controlled cloud / OEE-definition-stays-yours; editions = structure + mechanics, not pricing. Buyer REVIEW FLAG: OT-Architect-primary kept for §24.0 consistency (CTA "Request an architecture review", P-H) — flagged for user/ChatGPT to confirm vs a Plant-manager-primary exception. Next: user + ChatGPT review → lock. Cites: design-system-v4 §24/§24.A/§24.3, page-product-edgeconnect-spec-v1 (shape-setter), page-capabilities-hub-spec-v1 §9, design-system-v3 §5.A/§16/§17, buyer-taxonomy-v1 §2.3/§2.2, proof-architecture-v1 §3/§4/§8, CLAUDE.md §3/§8, elpis-industrial-intelligence-platform-v5 (datasheet), page-capabilities-operational-intelligence-spec-v1 v1, page-solutions-multi-site-operations-spec-v1 v3, page-solutions-predictive-maintenance-spec-v1 v2, page-architecture-spec-v1 v2.1, shared-knowledge/contracts/eremos-per-tag-mqtt.md, hardware-ecosystem-map-v3 §5.2/§6, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
