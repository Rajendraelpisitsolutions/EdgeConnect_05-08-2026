<!--
File:        docs/marketing/page-architecture-spec-v1.md
Purpose:     Page spec for /architecture — the cross-pillar technical
             walkthrough of the Industrial Intelligence Stack. Sixth of
             10 Phase 2 per-page specs. Different page structure from
             CapabilityDeepDive (the 5 pillar pages); built around the
             ArchitecturePanel.interactive centerpiece.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), Phase 2 step 8-11 spec authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   architecture-diagram-spec-v3.md (the diagram itself —
                4-column layout with stacked-peer middle)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                emerging-pattern governance; per-page-type FAQ
                governance; metadata governance)
             phase2-ia-scope-memo-v2.md §3 + amendment v3 §1.3
                (interactivity scope: hover + progressive zoom +
                click-to-focus; NO free-pan / NO animated topology /
                NO diagram-editor behavior)
             buyer-taxonomy-v1.md §2.3 (OT Architect — primary buyer)
                + §2.7 (Procurement / compliance reviewer — tertiary
                via the inline FAQ)
             proof-architecture-v1.md (proof discipline; no customer
                logos / metrics / certification claims on this page)
             design-system-v3.md §5.A (ArchitecturePanel.interactive
                variant), §17 (cross-lens content pattern)
             page-capabilities-connectivity-edge-spec-v1.md (sister
                pillar deep-dive — cross-references /architecture)
             page-capabilities-operational-intelligence-spec-v1.md
                (cross-references /architecture)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview)
             security-page-copy-v2.md (cross-link target — full
                operational trust posture)
Version:     v2.2 — LOCKED. Original v2 locked 2026-05-29 (anti-overclaim
                  softening on named integrations + new Q7 FAQ +
                  defensive anti-patterns + scan-friendliness polish).
                  v2.1 amendment 2026-05-29 retroactively M2-softened
                  §3.7 FAQ Q4 missed during the original M2 pass on
                  §3.6 Pattern B. v2.2 amendment 2026-06-04 dropped
                  MT-LINKi from §3.2 diagram annotation + §3.5
                  deployment paths grid per platform-team direction
                  (no customer demand; engineering deferred to low
                  priority); MT-LINKi REST integration moved to
                  roadmap mention.
Date:        2026-05-29 (v2 lock) / 2026-05-29 (v2.1) / 2026-06-04 (v2.2)
Status:      LOCKED.

v2.1 amendment (2026-05-29): §3.7 FAQ Q4 (historian integration)
named-vendor softening retroactively applied.

The original v2 lock applied M2 softening to §3.6 Pattern B (historian
integration patterns) and Pattern D (data lake) — removing the named-
vendor list (PI / Wonderware / Aveva / Snowflake / Databricks /
BigQuery) and replacing with the safer "customer-approved integration
paths" framing. However, §3.7 FAQ Q4 (inline FAQ surface for the same
historian topic) retained the named-vendor list — the M2 softening pass
did not propagate into the inline FAQ.

Surfaced by the /solutions/edge-connectivity v2 pre-lock workflow's
cross-spec drift validator (the edge-connectivity solution spec §3.5 Q5
uses the correctly-measured wording, matching §3.6 Pattern B; the
workflow flagged the inconsistency between architecture v2 §3.6 (M2-
softened) and architecture v2 §3.7 Q4 (still named-vendor)).

v2 Q4 wording: "EREMOS V2 publishes to time-series databases (InfluxDB,
TimescaleDB) and enterprise historians (PI, Wonderware, Aveva). The
historian stays the long-term archive of record..."

v2.1 amendment wording: "EREMOS V2 supports time-series database and
enterprise historian integration patterns through customer-approved
export, API, MQTT, or database integration paths. The historian stays
the long-term archive of record... Specific historian destinations are
validated per deployment during architecture review."

Net effect: anti-overclaim discipline now consistent between §3.6
Pattern B (Pattern A/B/C/D integration patterns) and §3.7 FAQ Q4
(inline FAQ for the same topic). No other content changes.

Sixth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing
step 7. First non-CapabilityDeepDive Phase 2 spec — has its own page
structure built around ArchitecturePanel.interactive centerpiece per
design-system v3 §5.A.

Page-structure approval: structure approved by user direction 2026-05-29
before drafting. 9-section layout: Hero → Interactive diagram →
4-column walkthrough → Peer architecture → 3 deployment paths →
Integration patterns → Inline FAQ → Cross-lens → Final CTA.

Word-count target: 1,200-1,800 words per /capabilities hub spec §9
page-type guidance for /architecture. v2 draft: ~1,750 words.

Inline FAQ included per /capabilities hub §9 per-page-type FAQ
governance — OT Architect / SCADA reviewer asks predictable architecture
/ integration / security questions on this surface (Q1-Q7).

§1.4 Page metadata block included per /capabilities hub §9 metadata
governance lock (PR #71).

§3.4 (peer architecture) implements the rev3 architectural truth from
architecture-diagram-spec-v3 §2 — EdgeConnect and Acquisition are peer
entry points into EREMOS V2, NOT parent/child. This is uniquely a
/architecture story; no other Phase 2 surface tells it cleanly.

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve after a focused v2 refinement pass. This is one of the
   strongest Phase 2 specs so far. It does what the pillar pages cannot
   do: it explains the full Industrial Intelligence Stack as a system.
   The structure is right, the OT Architect targeting is strong, the
   peer-architecture story is clear, and the SCADA / historian / MES
   coexistence framing is commercially and technically important.
   After v2 corrections, this page should become the canonical
   technical explanation of the Elpis Industrial Intelligence Stack."

User decision on v2 refinements (2026-05-29):
  - M1 (replace motion.architecture with motion.slow)                  REJECTED
        — reviewer memory error. design-system v3 §5.A LOCKED defines
          motion.architecture 240ms as an explicit scoped exception
          for the /architecture interactive panel (lines 49, 97, 113,
          646, 648, 650, 676, 729). Replacing it would violate the
          LOCKED §5.A spec and undo design-governance §2.2's deliberate
          240ms calibration ("above motion.default 180ms, below
          motion.slow 280ms — inspectable rather than abrupt").
  - M2 (soften named integration claims)                                APPLIED
        — §3.6 Pattern B (historians) and Pattern D (data lakes) now
          use customer-approved-paths + per-deployment-verified framing
        — removes PI / Wonderware / Aveva / Snowflake / Databricks /
          BigQuery name-checks until per-vendor verification lives in
          proof-architecture v1's verified-integrations registry
  - M3 (add Q7 "Where does EdgeConnect run?" FAQ)                       APPLIED
        — bridges software-vs-appliance story; helps Plant Engineer
          secondary buyer
  - M4 (anti-pattern preventing named-integration overclaim)            APPLIED
        — §6 new row; pairs with M2 defensively
  - M5 (anti-pattern preventing peer-mandatory drift)                   APPLIED
        — §6 new row; protects rev3 peer-architecture truth from
          future copy regression
  - §3.3 OPC UA wording precision                                       APPLIED
        — "publishes to EREMOS V2 via MQTT and exposes via OPC UA
          Server" (was "publishes via MQTT or OPC UA Server" — minor
          but technically more correct for OT readers)
  - O2 ("Choose EdgeConnect. Choose Acquisition. Choose both.")         APPLIED
        — rhythmic pre-callout line in §3.4
  - O3 (§3.5 heading change to "Choose the deployment path that
       fits each plant")                                                APPLIED
        — reinforces per-plant per-fleet message from §3.5 body
  - O4 (visual micro-pattern hints)                                     APPLIED
        — §3.2 4-bullet "what this page explains" strip after hero
        — §3.4 ENTRY-POINT label strip above peer-architecture callout
        — §3.5 visual emphasis directive on "When it fits" row
  - O1 (single-sentence "what you'll learn" orientation)                NOT APPLIED
        — superseded by O4's 4-bullet strip (richer same-purpose pattern)

Sections receiving "no change" reviewer approval:
  Page structure (9-section rhythm), buyer targeting + CTA hierarchy,
  §3.1 hero core wording, §3.2 8-annotation distribution across 3 zoom
  levels, §3.3 four-column walkthrough (except OPC UA wording),
  §3.4 peer-architecture core sentence, §3.5 three-deployment-paths
  grid structure, §3.6 SCADA / MES / historian / data-lake pattern
  structure (only the named-vendor mentions softened),
  §3.7 FAQ Q1-Q6, §3.8 cross-lens, §3.9 final CTA
  ("Bring us your floor topology" — reviewer: "one of the best CTAs in
  Phase 2"), §6 existing anti-patterns.
-->

# `/architecture` — Page Spec v1

**Cross-pillar technical walkthrough of the Industrial Intelligence Stack. NOT a CapabilityDeepDive — has its own page structure built around the `ArchitecturePanel.interactive` centerpiece. OT Architect lands here to understand how the 5 pillars compose into one operational data layer, how the platform sits beside existing SCADA / historian / MES infrastructure, and which deployment shape fits their environment.**

This is the page where OT Architects and SCADA engineers land after they've understood *what* each pillar does (`/capabilities/<pillar>`) and want to understand *how the whole thing fits together* — column structure, peer relationships, deployment shapes, integration patterns, and the technical questions they'll ask before scoping any engagement.

Target length: **1,200-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/architecture`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Cross-pillar architecture walkthrough. Reader leaves with *"I now understand the column structure, how EdgeConnect and Acquisition cooperate, which of the three deployment paths fits my environment, how Elpis sits beside what I already have, and answers to the architecture / integration / security questions I'd otherwise raise on a call."*

**IS NOT:**
- A capability deep-dive (those are `/capabilities/<pillar>` × 5 — LOCKED)
- A protocol reference table (Phase E `/edgeconnect` covers full protocol coverage with semantic modes, security profiles, and integration test patterns)
- A solution narrative (`/solutions/edge-connectivity` covers the outcome-organized version of the brownfield-direct deployment path; `/solutions/predictive-maintenance` covers the condition-monitoring outcome)
- A security walkthrough (`/security` covers the full operational trust posture)
- A product detail page (Phase E `/edgeconnect`, `/edge-gateway`, `/mdaq`, `/mtracker`, `/eremos-v2` will each cover their product surface)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** OT Architect / SCADA engineer (§2.3)
- Lands here from `/capabilities` hub, from one of the 5 pillar deep-dives via the §3.8 cross-lens "ARCHITECTURE" card, from a Google search for *"industrial intelligence stack"* / *"OT data layer architecture"* / *"protocol-agnostic edge gateway architecture"*, or via the homepage hero
- Wants: column structure, integration patterns, deployment-shape clarity, technical answers to predictable architecture / SCADA-coexistence / security-boundary questions
- CTA preference: *"Request an architecture review"* > *"Talk to an engineer"*
- Vocabulary that lands: column structure, peer architecture, store-and-forward, three-way diagnostics, canonical CNC vocabulary, hash-chained audit, OPC UA Server, MQTT publish, brownfield-direct, PLC-bypass, multi-edge, per-gateway identity, integration patterns
- Vocabulary that backfires: *"intuitive"*, *"easy"*, *"seamless integration"*, *"future-proof"*, *"end-to-end"* (without specifying the actual ends), *"single pane of glass"* (often a tell that the underlying architecture is opinionated about your operations team's existing tools)

**Secondary buyer:** Plant engineer (retrofit / greenfield) (§2.5)
- Lands here when scoping a deployment BOM and wanting to know which combination of hardware / software / per-plant runtime they need
- Wants: deployment-shape options, multi-site fleet patterns, hardware-vs-software-only deployment trade-offs
- CTA preference: *"Get hardware specifications"* (cross-links to `/capabilities/data-acquisition` or `/capabilities/connectivity-edge` for the per-pillar hardware detail)

**Tertiary buyer (via the inline FAQ):** Procurement / compliance reviewer (§2.7)
- Lands here when asked by an OT Architect to validate that the architecture story holds together for their procurement-policy review
- Wants: clear "does Elpis replace our SCADA?" / "what happens when the network drops?" / "what's the security boundary?" answers without needing a sales call
- The inline FAQ in §3.7 is calibrated to surface these answers without the procurement reviewer needing to read every preceding section

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Architecture — the Industrial Intelligence Stack · Elpis* |
| **Meta description** (140-160 chars) | *How the 5 capability pillars compose into one operational data layer. Column structure, deployment shapes, SCADA coexistence, store-and-forward.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/architecture` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.7 inline FAQ uses `FAQPage` schema (per §9 inline-FAQ-with-schema-markup governance). Diagram references the architecture-diagram v2 SVG asset. Cross-links to `/capabilities/<pillar>` × 5 + `/security` + `/solutions/edge-connectivity` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

NOT the `CapabilityDeepDive` layout. Built around the `ArchitecturePanel.interactive` centerpiece per design-system v3 §5.A. 9 sections:

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + CTAs) | `dark-deep` | `SectionShell` + `Button` × 2 | ~120 |
| **2** | The Industrial Intelligence Stack — interactive diagram (centerpiece) | `light` | `ArchitecturePanel.interactive` (4-column layout, 6-8 annotations across 3 zoom levels) + `DiagramFrame` wrapper | ~80 (caption) |
| **3** | The 4 columns — what each represents | `light-tinted` | 4-column textual walkthrough (one paragraph per column) | ~400 |
| **4** | The peer architecture — why EdgeConnect + Acquisition are peers, not parent/child | `light` | Callout block (left-rule or bordered card) | ~200 |
| **5** | Three deployment paths — brownfield-direct / Elpis-acquisition / hybrid | `light-tinted` | 3-column comparison grid + textual narrative | ~250 |
| **6** | Integration patterns — SCADA / MES / historian coexistence | `light` | Sub-grid: how Elpis sits beside existing systems | ~200 |
| **7** | Common questions (inline FAQ, OT Architect-calibrated) | `light` | 6 Q&A pairs with `FAQPage` schema markup | ~300 |
| **8** | Cross-lens navigation | `light-tinted` | §17 cross-lens content pattern (3 cards) | ~50 |
| **9** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> THE INDUSTRIAL INTELLIGENCE STACK
>
> HEADLINE (size.3xl semibold):
> The architecture of an industrial intelligence ecosystem — how the 5 pillars compose into one operational data layer.
>
> SUBHEAD (size.lg, max-width 60ch):
> Four columns. Two peer entry points into the intelligence layer. Three deployment shapes. Designed to sit beside the SCADA / MES / historian infrastructure you already run — not to replace it.
>
> PRIMARY CTA (`Button.primary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Talk to an engineer
> HREF: `/contact?intent=architecture-engineering`

**Anti-patterns:** No *"end-to-end"* without specifying the ends. No *"single pane of glass"* framing (per buyer-taxonomy §2.3 vocabulary discipline — OT Architects read it as a tell that the underlying architecture is opinionated about their existing tools). No outcome metric in headline.

---

### 3.2 Section 2 — The Industrial Intelligence Stack (interactive diagram)

The centerpiece of the page. Implementation per design-system v3 §5.A `ArchitecturePanel.interactive` variant.

> EYEBROW: WHAT THIS PAGE EXPLAINS
>
> WHAT-YOU'LL-LEARN STRIP (size.base, 4 short bullets — visual hierarchy: 2×2 grid on desktop, single column on mobile; placed between hero and diagram):
>
> - **What runs where** — the column structure of the Industrial Intelligence Stack
> - **Which deployment path fits** — your floor topology, per plant
> - **How Elpis coexists with SCADA / MES / historian** — sits beside, doesn't replace
> - **What changes when the network drops** — store-and-forward + three-way diagnostics
>
> ---
>
> EYEBROW: THE STACK
>
> CAPTION (above diagram, size.base):
> One operational data layer with two peer entry points. Hover any column to inspect what runs there. Click a column to zoom in.

**Diagram structure** (per `architecture-diagram-spec-v3.md §2` — 4 columns with stacked-peer middle, LOCKED in rev3):

```
┌────────────┐  ┬──→  ╔══════════════╗  ┌────────────┐ ──→ ┌────────────┐
│ Floor      │  │     ║  Col 2:      ║  │ EREMOS V2  │     │ Your       │
│            │  │     ║  EdgeConnect ║  │            │     │ Enterprise │
│            │  └──→  ║      +       ║  │            │     │            │
│            │        ║ Acquisition  ║  │            │     │            │
└────────────┘        ╚══════════════╝  └────────────┘     └────────────┘
                       (EdgeConnect on top + Acquisition on bottom — peers,
                        bi-directional cooperation when both deployed)
```

**Annotations (6-8 across 3 zoom levels per amendment v3 §1.3 interactivity scope):**

| Zoom level | Annotated region | Eyebrow | Annotation body |
|---|---|---|---|
| 1 (full diagram) | Floor (Col 1) | EXISTING INFRASTRUCTURE | PLCs, controllers, sensors. Brownfield or greenfield. Elpis reads what's there — doesn't require ripping anything out. |
| 1 (full) | Col 2 EdgeConnect peer | PROTOCOL-AGNOSTIC EDGE RUNTIME | Polls existing controllers over native protocols (FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7). Normalizes to canonical CNC vocabulary. MT-LINKi REST integration on the roadmap. |
| 1 (full) | Col 2 Acquisition peer | DIRECT-SENSOR ACQUISITION | mDAQ + mTracker + VAS + E-IDOS. Direct sensor reads where no PLC exists, where the PLC is locked, or where adding a PLC layer would be more expensive than acquiring the signal directly. |
| 1 (full) | EREMOS V2 (Col 3) | MULTI-TENANT ANALYTICS PLATFORM | OEE, alarms, incidents, reports. Models the real PLANT → AREA → LINE → EQUIPMENT hierarchy. Per-tenant isolation by design. |
| 2 (Connectivity-Edge zoom) | EdgeConnect runtime | THREE-WAY DIAGNOSTICS | Source / pipeline / sink health surfaced separately. Tells you immediately where the data path broke. |
| 2 (Connectivity-Edge zoom) | Edge Gateway hardware | DUAL-IDENTITY APPLIANCE | Today: standalone PLC-to-cloud gateway. Tomorrow (when EdgeConnect Linux ships): the canonical EdgeConnect appliance. Same hardware, two lifecycles. |
| 3 (Acquisition zoom) | mDAQ direct-sensor row | FIELD-EDGE ACQUISITION | 4 analog channels (0-10 V or 4-20 mA), 16-bit, 860 S/s. 8 DI + 8 DO. Operates at remote and unmanned sites with battery + 4G. |
| 3 (Acquisition zoom) | EdgeConnect ↔ Acquisition bi-directional arrow | PEER COOPERATION | When both peers deploy, Acquisition can feed EdgeConnect for normalization / routing. EdgeConnect can orchestrate Acquisition devices. Optional, not required. |

**Visual treatment:** the diagram itself uses `architecture-diagram-spec-v3` as the canonical source asset (SVG). The `ArchitecturePanel.interactive` props are populated from the annotations table above. Mobile degrades to static tap-to-show-tooltip per §5.A mobile behavior.

> CAPTION (below diagram, size.sm italic):
> *Customer owns Col 1 (Floor) and Col 4 (Enterprise). Elpis ships Col 2 (EdgeConnect + Acquisition) and Col 3 (EREMOS V2). The flow is left-to-right; the peer relationship inside Col 2 is bi-directional when both are deployed.*

---

### 3.3 Section 3 — The 4 columns

> EYEBROW: WHAT EACH COLUMN REPRESENTS
>
> SECTION TITLE:
> Four columns, left to right.

#### Col 1 — Floor

> Your existing PLCs, controllers, sensors, instrumentation, and machine assets. Mixed-vendor: FANUC + Brother + Mazak on one floor; Modbus PLCs beside Siemens S7 beside OPC UA endpoints. Greenfield installs where there's no controller yet, with the sensors connected directly. Brownfield retrofits where the controller is locked behind a vendor's proprietary boundary. The Floor is whatever you already have — Elpis reads it, doesn't replace it.

#### Col 2 — EdgeConnect + Acquisition (the two peer entry points)

> The Elpis-owned layer where signals become canonical operational data. Two stacked peers that cooperate bi-directionally when both are deployed:
>
> **EdgeConnect (top peer)** — the protocol-agnostic edge runtime. Polls existing controllers over their native protocols, normalizes signals to canonical CNC vocabulary, publishes to EREMOS V2 via MQTT and exposes signals via OPC UA Server. Runs Windows today; Linux on the roadmap.
>
> **Acquisition (bottom peer)** — the direct-sensor hardware layer. mDAQ for general-purpose industrial signals (flow / pressure / temperature / vibration). mTracker for utilization + OEE telemetry on assets that don't speak a controller protocol. VAS + E-IDOS for vibration analysis and condition monitoring. Publishes to EREMOS V2 via MQTT / HTTPS direct.
>
> See `/capabilities/connectivity-edge` for the EdgeConnect / Edge Gateway capability story. See `/capabilities/data-acquisition`, `/capabilities/asset-intelligence`, and `/capabilities/condition-monitoring` for the Acquisition-layer pillars.

#### Col 3 — EREMOS V2

> The multi-tenant analytics platform that turns collected signals into operational decisions. Models the real industrial hierarchy — PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT. Computes OEE via Segments (RUNNING / PLANNED_STOP / UNPLANNED_STOP / IDLE / SETUP) on the signals delivered from Col 2. Persistent alarms with incident workflows. Per-tenant isolation by design.
>
> See `/capabilities/operational-intelligence` for the EREMOS V2 capability story.

#### Col 4 — Your Enterprise

> Your existing SCADA, MES, historian, ERP, IT analytics, BI dashboards, data lake. EREMOS V2 publishes to your enterprise systems via the integration patterns described in §3.6 below. Elpis doesn't replace these layers — it feeds them.

---

### 3.4 Section 4 — The peer architecture

> EYEBROW: THE PEER ARCHITECTURE — REV3 ARCHITECTURAL TRUTH
>
> PRE-CALLOUT RHYTHMIC LEAD (size.lg, italic; sits between eyebrow and callout):
> *Choose EdgeConnect. Choose Acquisition. Choose both.*
>
> ENTRY-POINT LABEL STRIP (size.sm, small-caps brand-teal letter-spaced 0.18em, 3 inline items separated by middle-dots; sits between rhythmic lead and callout):
> SOFTWARE ENTRY POINT · ACQUISITION ENTRY POINT · BOTH OPTIONAL
>
> CALLOUT BLOCK (left-rule callout or bordered card, size.base):
>
> > **EdgeConnect and Acquisition are peers, not parent and child.** They are two independent entry points into the intelligence layer. Customer deploys one, the other, or both.
> >
> > Earlier architecture revisions implied that Acquisition feeds EdgeConnect, which then feeds EREMOS V2. That was wrong. mDAQ publishes to EREMOS V2 directly via MQTT / HTTPS — no EdgeConnect required. mTracker publishes directly. VAS / E-IDOS publish directly. EdgeConnect handles the existing-controller side of the floor; Acquisition handles the sensor-direct side.
> >
> > When both peers are deployed, they cooperate bi-directionally: Acquisition signals can route through EdgeConnect for canonical normalization (useful when you want every signal — whether from a PLC or directly from a sensor — to arrive at EREMOS V2 in the same canonical shape). EdgeConnect can orchestrate Acquisition devices when deployed in the same plant (useful for device-management consolidation). **Both forms of cooperation are optional. Neither is required.**
>
> SUBLINE (size.sm italic):
> *This peer relationship is unique to the Elpis stack. It's why the same platform serves a CNC shop with mixed-vendor controllers, an oil-and-gas pipeline with no controllers at all, and a multi-site plant manager with both — without forcing any of them through a deployment shape that doesn't fit.*

---

### 3.5 Section 5 — Three deployment paths

> EYEBROW: THREE DEPLOYMENT SHAPES
>
> SECTION TITLE:
> Choose the deployment path that fits each plant.

**3-column comparison grid:**

> VISUAL EMPHASIS DIRECTIVE FOR ENGINEERING: the **"When it fits"** row is the self-selection mechanism — the row that lets an OT Architect immediately decide which path applies to their floor. Engineering should give it visual weight relative to the other rows (e.g., row-level shading, larger text, eyebrow treatment above the row, or a left-rule accent). The other rows are reference detail; this row drives the decision.

| | **Brownfield-direct** | **Elpis-acquisition** | **Hybrid (both peers)** |
|---|---|---|---|
| **When it fits** | Existing PLCs and controllers expose the signals you need | Greenfield install OR PLC-bypass retrofit where the controller doesn't expose the signal | Mixed floor: some signals come from PLCs, some come from direct sensors |
| **What's deployed** | EdgeConnect runtime + EREMOS V2 | Acquisition hardware (mDAQ / mTracker / VAS / E-IDOS) + EREMOS V2 | Both peers + EREMOS V2 |
| **What it reads** | FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 (MT-LINKi REST on roadmap) | Direct sensor signals (4-20 mA, 0-10 V, 24 V DC, Modbus RTU, pulse / counter inputs) | Both: native protocols + direct sensors |
| **Hardware footprint** | Software-only or Edge Gateway appliance (per plant) | Acquisition hardware per signal type | Edge Gateway + Acquisition hardware as needed |
| **Typical examples** | CNC machining floors, process manufacturing with existing PLCs, OEM machines exposing their controller telemetry | Pipeline pump stations, mining outposts, well heads, off-grid water infrastructure, retrofits where the PLC is locked | Multi-site plant operator standardizing across diverse floor topologies |

> BODY (after the grid, size.base):
> The deployment shape is decided per plant, not per fleet. A multi-site operator can run brownfield-direct in one plant, Elpis-acquisition-only in another, and hybrid in a third — all reporting into one EREMOS V2 instance with consistent canonical vocabulary, OEE definitions, and alarm semantics across every site.

---

### 3.6 Section 6 — Integration patterns

> EYEBROW: HOW ELPIS SITS BESIDE WHAT YOU ALREADY RUN
>
> SECTION TITLE:
> Designed to feed your enterprise, not replace it.

**Sub-grid: Elpis ↔ existing systems integration patterns**

#### Pattern A — SCADA coexistence

> EdgeConnect publishes to MQTT and exposes signals via OPC UA Server. Your existing SCADA system can subscribe to either. Elpis doesn't take over operator HMIs, doesn't take over control logic, doesn't take over alarm acknowledgment in the SCADA. EREMOS V2 provides the cross-site analytics layer; SCADA stays where it is, with its existing operator interfaces and operational responsibilities.

#### Pattern B — Historian integration

> EREMOS V2 supports historian and time-series integration patterns through customer-approved export, API, MQTT, or database integration paths. The customer's historian remains the long-term archive of record if that's the operational policy; EREMOS V2 surfaces analytics-grade views on top of the same signals. Specific historian destinations are validated per deployment during architecture review.

#### Pattern C — MES / ERP integration

> EREMOS V2 exposes OEE rollups, downtime breakdowns, incident records, and shift reports via REST API. MES / ERP systems consume those rollups for production planning, maintenance scheduling, or service-hours billing. EREMOS V2 does not push transactions into MES / ERP — it provides the operational signal layer that those systems can read.

#### Pattern D — IT analytics / data lake

> For organizations operating an enterprise data lake, EREMOS V2 can provide scoped operational exports through customer-approved integration paths — anonymized, scoped to operational signals, on a customer-controlled schedule. Specific cloud destinations are validated during architecture review.

> BODY (after the sub-grid, size.base):
> Every integration pattern is opt-in per plant. No integration is required for Elpis to deliver value on the floor. The integration patterns become relevant when the customer wants Elpis's operational signals to inform their broader enterprise systems — which is typical for multi-site operators and uncommon for single-plant deployments.

---

### 3.7 Section 7 — Common questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/architecture` includes an inline FAQ with `FAQPage` schema markup for SEO. 7 questions calibrated to OT Architect / SCADA reviewer concerns + procurement-policy-review concerns + Plant Engineer deployment-ownership concerns.

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What OT Architects ask before scoping an engagement.

#### Q1. Does this replace our SCADA?

> No. EdgeConnect and EREMOS V2 sit beside your SCADA — they don't take over operator HMIs, control logic, or alarm acknowledgment workflows. SCADA stays where it is; Elpis adds the cross-site analytics layer and consolidates protocol coverage so your enterprise systems receive consistent canonical signals regardless of which controller produced them.

#### Q2. Can EdgeConnect run on Linux today?

> Today, EdgeConnect ships as a Windows service. Linux is on the roadmap — when it ships, it will deploy on the Edge Gateway appliance as the canonical Linux footprint. For Linux-required customers today, the Edge Gateway hardware (standalone PLC-to-cloud gateway today, future canonical EdgeConnect appliance) handles the most common protocol bridging via its embedded Linux runtime.

#### Q3. What happens when the network drops?

> Per-route store-and-forward. Every signal queues at the source with its quality code preserved. When connectivity returns, signals replay in source order — no lost cycles, no manual recovery step. Three-way diagnostics (source / pipeline / sink) surface immediately during the outage so you can see exactly which path was affected.

#### Q4. How do we integrate with our existing historian?

> EREMOS V2 supports time-series database and enterprise historian integration patterns through customer-approved export, API, MQTT, or database integration paths. The historian stays the long-term archive of record if that's your operational policy. EREMOS V2 provides the analytics layer on top of the same signals — you don't have to choose between them. Specific historian destinations are validated per deployment during architecture review.

#### Q5. What's the security boundary between OT and IT?

> EdgeConnect runs entirely on the OT side. License validates locally — no phone-home. Cloud connectivity is opt-in, not required. Plants on isolated OT VLANs install the same way as plants with internet access. Hash-chained configuration audit captures every change with actor identity and timestamp — tamper-evident, replay-ready. Full operational trust posture: see `/security`.

#### Q6. Can one EdgeConnect deployment serve multiple plants?

> No — that's an anti-pattern, and Elpis explicitly doesn't recommend it. Each plant runs its own EdgeConnect (typically on the Edge Gateway appliance) with a per-gateway UUID established at first start. Multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes — not from a single multi-plant EdgeConnect runtime. This protects plant-level isolation, per-site identity, and offline operability per plant.

#### Q7. Where does EdgeConnect run?

> EdgeConnect runs per plant, typically on customer-owned Windows infrastructure today, or on the Edge Gateway appliance when the Linux runtime ships. The deployment choice is made per plant based on network topology, security posture, and hardware preference. Single-plant deployments often start software-only on existing Windows infrastructure; multi-site standardizations typically converge on the Edge Gateway appliance once EdgeConnect Linux is available, for hardware consistency across sites. Either path is supported — neither is a prerequisite for the other.

---

### 3.8 Section 8 — Cross-lens navigation

Per design-system v3 §17 cross-lens content pattern. Preset for `/architecture`:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITIES | What each pillar does, on its own | `/capabilities` |
| 2 | SOLUTION · EDGE CONNECTIVITY | The outcome-organized version of the brownfield-direct deployment path | `/solutions/edge-connectivity` |
| 3 | SECURITY | The operational trust posture in detail | `/security` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.3 OT Architect + §2.5 Plant engineer CTA preferences:

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your floor topology. We'll scope the architecture.
>
> SUBHEAD:
> Whether you're scoping a brownfield retrofit, a greenfield install, a multi-site standardization, or a SCADA-coexistence integration — bring us the controller mix, sensor inventory, and existing-systems boundary. Architecture review runs against real protocols and real integration patterns, not slideware.
>
> PRIMARY CTA: Request an architecture review
> HREF: `/contact?intent=architecture-review`
>
> SECONDARY CTA: Talk to an engineer
> HREF: `/contact?intent=architecture-engineering`

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.9 final CTA |
| `ArchitecturePanel.interactive` (§5.A, the centerpiece) | §3.2 stack diagram (full interactive variant: 3 zoom levels, 8 annotations, click-to-focus + hover + keyboard nav) |
| `DiagramFrame` | §3.2 diagram wrapper |
| `CapabilityCard` (cross-lens variant) | §3.8 cross-lens (3 cards) |
| `CTASection` | §3.9 final CTA |
| §17 cross-lens content pattern | §3.8 cross-lens |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.7 common questions |

Layout & motion: `motion.architecture` 240ms for the click-to-zoom transition per design-system v3 §5.A + §22 motion governance (the only place this token applies on the page).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.9. **~1,750 words total** (within 1,200-1,800 target for `/architecture` page-type per §9 page-type guidance). Increase from v1 baseline (~1,680 words) reflects v2 R3 Q7 FAQ addition (~70 words) + O2 + O4 visual hints (~30 words combined) − M2 softened integration wording (~20 words shorter than the named-vendor v1 version).

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~120 |
| 3.2 | 4-bullet "what you'll learn" strip + diagram caption + annotations | ~30 (strip) + ~80 (caption) + ~250 (annotations, inspectable enrichment per §5.A discipline) |
| 3.3 | 4 columns | ~400 |
| 3.4 | Pre-callout rhythmic lead + ENTRY-POINT label strip + peer architecture callout | ~15 (lead + labels) + ~200 (callout) |
| 3.5 | Three deployment paths + visual-emphasis directive for engineering | ~270 |
| 3.6 | Integration patterns (M2 softened) | ~210 |
| 3.7 | Inline FAQ (7 questions) | ~370 |
| 3.8 | Cross-lens | ~50 |
| 3.9 | Final CTA | ~80 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21:

| Don't | Why |
|---|---|
| Make the diagram a replacement for `/edgeconnect` protocol coverage table | Capability stays at architecture level on this page; full protocol coverage (with semantic modes, security profiles, integration test patterns) belongs on Phase E `/edgeconnect` |
| Oversell the interactivity ("explore the architecture") | The diagram is **inspectable**, not a SCADA designer. Per amendment v3 §1.3: hover + zoom + click-to-focus YES; pan / animate / edit NO |
| Promise topology simulation, flowing data particles, or pulsing nodes | Explicitly forbidden per design-system v3 §5.A. Animated topology reads as "marketing demoware" to OT Architects |
| Duplicate per-pillar capability content | Cross-link to `/capabilities/<pillar>` instead. Capability deep-dives are LOCKED; this page must not re-derive their content |
| Add competitor architecture comparisons | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory, not architecture page |
| Use *"single pane of glass"* / *"end-to-end"* without specifying the ends | Per buyer-taxonomy §2.3 — OT Architects read these as tells that the underlying architecture is opinionated about their existing tools |
| Imply EREMOS V2 takes over SCADA / historian responsibilities | The integration-patterns section explicitly positions Elpis as a layer **beside** existing systems, not **replacing** them. This is the most-asked OT Architect question; if the page accidentally reads as "rip and replace," the OT Architect leaves |
| Imply one EdgeConnect can serve multiple plants | FAQ Q6 explicitly denies this — per-gateway identity discipline + multi-site visibility comes from EREMOS V2 aggregating across per-plant runtimes |
| Add customer logos, fabricated metrics, certification claims | Per proof-architecture v1 §3 + §4 — architecture pages stay on architectural proof (store-and-forward, canonical vocabulary, diagnostics, audit chain), not on social/outcome/compliance proof |
| Include diagram annotations longer than the §5.A discipline allows | Per design-system v3 §5.A: annotations are eyebrow + 4-word title + 1-2 sentence body max. Long annotations defeat the inspectable-enrichment intent |
| Treat named historians / data lakes (PI, Wonderware, Aveva, Snowflake, Databricks, BigQuery, etc.) as supported integrations unless verified | Per anti-overclaim discipline (proof-architecture v1 §3 + §8) — naming a specific vendor product as a supported integration is an implicit support claim. Use the verified-per-deployment framing (§3.6 Pattern B + D) until per-vendor verification is locked into proof-architecture v1's verified-integrations registry |
| Present Acquisition as mandatory for EdgeConnect, or EdgeConnect as mandatory for Acquisition | Protects the rev3 peer-architecture truth (§3.4). Either peer can deploy standalone; cooperation is optional. Copy that frames one as a prerequisite for the other regresses the architectural truth back to the parent/child model that rev3 explicitly corrected |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 1,200-1,800 word target (current: ~1,750 words)
- [x] All 9 sections present per the approved page structure (§2)
- [x] §3.2 diagram is `ArchitecturePanel.interactive` per design-system v3 §5.A (not the static §5 variant)
- [x] Diagram annotations honor §5.A discipline (eyebrow + ≤4-word title + 1-2 sentence body; max 8 per zoom level; no auto-cycling)
- [x] §3.2 annotations align with `architecture-diagram-spec-v3` 4-column layout (locked rev3)
- [x] §3.4 peer architecture story matches the rev3 architectural truth from `architecture-diagram-spec-v3 §2`
- [x] §3.5 three deployment paths grid is honest about when each applies
- [x] §3.6 integration patterns explicitly position Elpis as a layer **beside** existing SCADA / historian / MES — not replacing them
- [x] §3.7 inline FAQ uses `FAQPage` schema markup per §9 inline-FAQ-with-schema-markup governance
- [x] §3.7 Q2 (EdgeConnect Linux roadmap) framing honest (Windows today, Linux on roadmap)
- [x] §3.7 Q5 (security boundary) cross-links to `/security` rather than re-deriving the trust walkthrough
- [x] §3.7 Q6 (multi-plant EdgeConnect) explicitly denies the anti-pattern
- [x] §3.8 cross-lens cards point to `/capabilities` + `/solutions/edge-connectivity` + `/security`
- [x] Final CTA uses OT-Architect-preferred framings ("Request an architecture review" + "Talk to an engineer")
- [x] No vocabulary that backfires per buyer-taxonomy v1 §2.3
- [x] No customer logos, no fabricated metrics, no competitor names, no certification claims
- [x] All components are design-system v3 LOCKED (the §5.A interactive variant is the only one specific to this page; `motion.architecture` 240ms is the LOCKED scoped exception per §5.A)
- [x] Page-spec structure follows §9 canonical template (§1 IA + buyer + §1.4 metadata, §2 sections at a glance, §3 section-by-section, §4 components, §5 verbatim copy summary, §6 anti-patterns, §7 sign-off, §8 out of scope)
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/architecture` is YES)
- [x] **v2 M2 applied** — §3.6 Patterns B + D use customer-approved-paths + per-deployment-verified framing (no PI / Wonderware / Aveva / Snowflake / Databricks / BigQuery name-checks)
- [x] **v2 M3 applied** — §3.7 Q7 "Where does EdgeConnect run?" FAQ added
- [x] **v2 M4 applied** — §6 anti-pattern guards against named-historian / data-lake overclaim
- [x] **v2 M5 applied** — §6 anti-pattern guards against peer-mandatory drift (protects rev3 peer-architecture truth)
- [x] **v2 OPC UA wording precision** — §3.3 EdgeConnect peer paragraph now technically accurate (publishes via MQTT, exposes via OPC UA Server)
- [x] **v2 O2 applied** — §3.4 pre-callout rhythmic lead "Choose EdgeConnect. Choose Acquisition. Choose both."
- [x] **v2 O3 applied** — §3.5 heading "Choose the deployment path that fits each plant" (was "Pick the shape that fits your floor")
- [x] **v2 O4 applied** — 3 visual micro-pattern hints (§3.2 4-bullet what-you'll-learn strip; §3.4 ENTRY-POINT label strip; §3.5 deployment-grid emphasis directive)
- [x] **v2 ChatGPT M1 rejected with evidence** — `motion.architecture` 240ms retained per design-system v3 §5.A LOCKED scoped exception (lines 49, 97, 113, 646, 648, 650, 676, 729)

---

## 8. Out of scope for v1

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers: full protocol coverage with semantic modes, OPC UA Server security profile detail, integration test patterns, FOCAS2 connection-pool sizing, MT-LINKi licensing, MTConnect probe-document conformance.
- **Full security walkthrough.** `/security` (existing v2; v3 in Phase E) covers: operational trust posture, hash-chained audit detail, license signature verification, OPC UA Server security modes, secrets handling, certificate trust center.
- **Per-pillar capability detail.** `/capabilities/<pillar>` × 5 (LOCKED) cover each pillar's capability story; this page cross-links to them rather than duplicating their content.
- **Solution narratives.** `/solutions/edge-connectivity` (Phase 2 step 10) covers the outcome-organized brownfield-direct story. `/solutions/predictive-maintenance` (Phase 2 step 9) covers the condition-monitoring outcome.
- **Product detail pages.** Phase E `/edgeconnect`, `/edge-gateway`, `/mdaq`, `/mtracker`, `/eremos-v2` will each cover their product surface.
- **Industry-specific architectures.** Phase 3 `/industries/<industry>` (or the Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2).
- **Pricing / commercial engagement.** `/platform` (Phase 2 step 11) covers the commercial-engagement teaser. Phase 3 `/pricing` covers detailed pricing.
- **Multi-tenant deployment architecture for EREMOS V2.** `/capabilities/operational-intelligence` (LOCKED) covers per-tenant isolation at the capability level; deeper deployment architecture (cluster topology, scaling characteristics, regional replication) lives on Phase E `/eremos-v2`.

---

*`/architecture` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review (verdict: "one of the strongest Phase 2 specs so far… should become the canonical technical explanation of the Elpis Industrial Intelligence Stack"). Sixth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 7. First non-CapabilityDeepDive Phase 2 spec — built around `ArchitecturePanel.interactive` centerpiece per design-system v3 §5.A. Page structure approved by user direction before drafting. Includes inline FAQ (Q1-Q7) per §9 per-page-type FAQ governance and §1.4 metadata block per §9 metadata governance. **v2 changes from v1:** M2 named-integration softening (anti-overclaim discipline on PI / Wonderware / Aveva / Snowflake / Databricks / BigQuery), M3 Q7 FAQ addition, M4 + M5 defensive anti-patterns, OPC UA wording precision, O2 + O3 + O4 scan-friendliness polish. **ChatGPT M1 rejected with evidence** — `motion.architecture` 240ms retained per design-system v3 §5.A LOCKED scoped exception (reviewer made a memory error; the token IS in v3, not "cut"). Cites: phase2-ia-scope-memo v2 + amendment v3 §1.3, buyer-taxonomy v1 §2.3 + §2.5 + §2.7, proof-architecture v1, design-system v3 §5.A + §17, architecture-diagram-spec-v3, page-capabilities-hub-spec-v1 §9, positioning v3.*
