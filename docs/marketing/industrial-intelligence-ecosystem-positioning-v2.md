<!--
File:     docs/marketing/industrial-intelligence-ecosystem-positioning-v2.md
Purpose:  Master strategic positioning document for Elpis, v2.
          Captures the category shift from "Industrial Intelligence
          Platform" (software) to "Industrial Intelligence Ecosystem"
          (vertically integrated capability stack), now organized
          around five capability pillars instead of an
          artifact-category framework.
Audience: Every future Claude session, every designer, every
          salesperson, every product manager working on Elpis marketing.
Format:   Strategic worldview document.
Source:   v1 of this manifesto + ChatGPT review pass (2026-05-25) +
          E-IDOS hardware catalog reveal.
Version:  v2 proposed (pending Elpis sign-off)
Date:     2026-05-25
v1 -> v2 changes:
  - E-IDOS (oil / fluid condition monitoring appliance) added to the
    ecosystem. Pairs with VAS as a Condition Monitoring duo.
  - PRIMARY ORGANIZING FRAMEWORK shifted from "three layers" to
    "five capability pillars" — Connectivity & Edge / Data Acquisition
    / Asset Intelligence / Condition Monitoring / Operational
    Intelligence. The pillar view is how customers think; the three-
    layer view is retained as supporting architectural explanation.
  - IA restructured: pillars become the homepage organizing principle.
    Industries becomes a cross-cutting filter rather than a top-level
    nav item.
  - Cascade table expanded: solution pages add a Predictive
    Maintenance solution anchoring VAS + E-IDOS.
  - "Reliability engineering" recognized as a new buyer category
    Elpis now addresses (the maintenance / reliability persona, not
    just the operations persona).
  - Voice and visual identity unchanged.

v1 retained as historical reference (industrial-intelligence-
ecosystem-positioning-v1.md). v2 supersedes v1 once Elpis sign-off
lands.
-->

# Elpis Industrial Intelligence Ecosystem — strategic positioning v2

This document supersedes v1 of the positioning manifesto. The substantive change in v2 is the move from a **data-path-layer** organizing framework (acquisition → edge → intelligence) to a **capability-pillar** organizing framework (five customer-facing capability domains). The three-layer view is retained as supporting architectural explanation, but the pillar view is the primary commercial lens going forward.

This evolution was triggered by the addition of E-IDOS (oil / fluid condition monitoring) to the ecosystem — which, paired with VAS (vibration condition monitoring), revealed Condition Monitoring as a coherent capability pillar in its own right rather than a scattered set of specialized hardware. Once that pillar emerged, the rest of the ecosystem reorganized naturally around the same logic.

---

## 1. The category shift, refined

### 1.1 Prior framing (v1 of this manifesto)

> "Industrial Intelligence Ecosystem — three layers (field acquisition + edge runtime + industrial intelligence)."

This was a step forward from the software-only framing, but it organized around **how the system works internally**, not around **what the customer is trying to buy**.

### 1.2 New framing (v2)

> **"Elpis is the industrial intelligence ecosystem organized around five capabilities — Connectivity & Edge, Data Acquisition, Asset Intelligence, Condition Monitoring, and Operational Intelligence — delivered as a vertically integrated stack from physical signal to enterprise insight."**

The hardware/software distinction disappears at the buyer-facing layer. The buyer reads about a *capability*, sees the products that deliver it, and moves on.

### 1.3 Why this matters

The three-layer view was internally correct but commercially weak. It asked the buyer to learn a *system architecture* before they could understand what to buy.

The pillar view inverts this:
- The buyer arrives looking for **a capability** (predictive maintenance, asset utilization, OEE, etc.)
- They land on the pillar page that addresses that capability
- They learn that the pillar is delivered by Elpis products that work together
- The hardware/software boundary is hidden — it is an implementation detail

This is how mature industrial companies organize their commercial surface. It is how Siemens organizes Industry Online Services. It is how Rockwell organizes FactoryTalk. It is how Honeywell organizes Forge. The pattern is well-known; the prior v1 framing simply hadn't adopted it.

---

## 2. The five capability pillars

| # | Pillar | One-sentence definition | Products inside |
|---|---|---|---|
| 1 | **Connectivity & Edge** | "Bring every signal on your floor into one operational data layer — without ripping out what you already have." | EdgeConnect + Edge Gateway |
| 2 | **Data Acquisition** | "Capture industrial signals directly from sensors when no PLC is available — or when you want a clean Elpis path." | mDAQ + future I/O devices |
| 3 | **Asset Intelligence** | "Know where every machine is, how it's running, and how much value it's producing — wherever it's deployed." | mTracker |
| 4 | **Condition Monitoring** | "Move from break-fix to predict-and-prevent on rotating equipment and hydraulic systems." | VAS + E-IDOS |
| 5 | **Operational Intelligence** | "Turn collected data into OEE, alarms, incidents, and reports the team actually uses." | EREMOS V2 |

Detail on each product and what it eliminates from a customer's BOM is in `hardware-ecosystem-map-v2.md`.

---

## 3. The defining differentiator, refined for v2

The v1 differentiator sentence:

> *"Elpis is the only industrial intelligence vendor that can be the entire data path — from the raw machine signal to the enterprise dashboard — or any subset of it, by customer choice."*

This is still true. v2 refines it:

> **"Elpis is the only industrial intelligence vendor that delivers a complete vertically integrated capability stack — connectivity, acquisition, asset intelligence, condition monitoring, and operational intelligence — across one coherent platform, deployable end-to-end or layered into what you already run."**

The longer sentence is for the manifesto. For homepage / deck use, both versions are valid; the shorter one is more memorable.

---

## 4. The three competitive frames Elpis now beats

The pillar view makes the competitive story tighter. Three distinct competitive sets, each addressed by the integrated platform:

### 4.1 vs. acquisition / gateway vendors (Advantech, Moxa, ICP DAS, HMS, Phoenix Contact, B+B SmartWorx)

These companies sell *boxes*. The customer assembles a stack themselves — buy the gateway, buy the cloud, buy the dashboard.

> **Elpis frame:** *"We ship the same box, but it already knows where to send the data — to a multi-tenant analytics platform built for OT, not assembled from cloud-IoT parts."*

### 4.2 vs. IIoT analytics vendors (MachineMetrics, Sight Machine, Tulip, Litmus Automation)

These companies sell *software* and depend on third-party hardware. The customer is responsible for the physical-layer purchase, integration, certification, and support.

> **Elpis frame:** *"We ship the same software-analytics layer, but we also make the field-acquisition hardware that feeds it — which means we own the entire signal-to-insight path, not just half of it."*

### 4.3 vs. condition-monitoring vendors (IFM, SKF CM, Bently Nevada, PCB Piezotronics — vibration; Bureau Veritas, Castrol Labcheck — oil)

These companies sell **point products** for specific reliability use cases. The customer ends up with one vendor for vibration, another for oil analysis, another for OEE.

> **Elpis frame:** *"We deliver vibration condition monitoring AND oil-health intelligence AND OEE AND asset tracking — on one integrated platform. One vendor relationship instead of four."*

These three frames are what the homepage, the pitch deck, and the datasheet should land on different audiences. They are also what the sales objection guide v3 should address.

---

## 5. Voice and vocabulary changes (additive to v1)

The v1 vocabulary changes still hold (replace "platform" with "ecosystem", replace "two products" with "five capabilities", etc.). v2 adds:

### 5.1 New vocabulary

| Term | Meaning |
|---|---|
| **Capability pillar** | One of the five customer-facing organizing domains |
| **Reliability engineering** | The persona Elpis now addresses via the Condition Monitoring pillar — maintenance manager, reliability engineer, plant maintenance lead |
| **Fluid intelligence** | E-IDOS-specific positioning: oil and lubrication health as a first-class operational signal |
| **Condition Monitoring duo** | VAS + E-IDOS as a paired capability — rotating-machinery condition + fluid condition |
| **End-to-end reliability** | Marketing framing for selling VAS + E-IDOS + EREMOS V2 together |

### 5.2 Sharpened framing

| Before (v1) | After (v2) |
|---|---|
| "Three-layer worldview" | "Five-capability worldview, expressed as three architectural layers" |
| "Field acquisition layer" (presented as a pillar) | "Field acquisition layer" (presented as a data-flow concept; the customer-facing terms are 'Data Acquisition', 'Asset Intelligence', and 'Condition Monitoring') |
| "Industries top-level nav" | "Industries cross-cutting filter — orthogonal to the pillar nav" |

---

## 6. Trust posture (unchanged + extended)

The pitch deck v5 trust posture trio (Air-gapped factories / Lapsed license / AI proposes — humans decide) stays in place. The v1 §6 hardware-trust-posture extension still applies — pending firmware-behavior validation.

v2 adds **one more trust-posture commitment**, this one specific to condition monitoring:

> **The reliability data stays on your floor.** Condition signals from VAS and E-IDOS feed your operational stack, not Elpis's. We don't aggregate condition data across customers, we don't run shared models against your bearings, and we don't sell anonymized predictive-maintenance insights to your competitors.

This matters because the predictive-maintenance vendor category has historically traded customer data for model-improvement, and OT-conscious buyers are wary of it. Elpis explicitly does not.

Like the v1 hardware-trust-posture extension, this is **positioning subject to validation**. The condition-data isolation needs to be operationally true (no shared training, no cross-tenant leakage in EREMOS V2) before the claim ships externally.

---

## 7. Industry positioning — restructured

The v1 manifesto put **Industries** as a top-level nav item. v2 moves it to a **cross-cutting filter**. Reasoning:

- A buyer in Oil & Gas might care about Condition Monitoring (E-IDOS for hydraulics) AND Asset Intelligence (mTracker for remote pumps) AND Operational Intelligence (EREMOS V2 dashboards)
- That same E-IDOS use case is also relevant to Mining, Aerospace, and Construction
- Promoting Industries to a top-level nav forces the same content to repeat across industries
- Demoting Industries to a *filter view of the pillar nav* lets one piece of pillar content serve many industries

Concretely:
- The Connectivity & Edge pillar page has an "By industry" section showing how the pillar applies to Oil & Gas vs. Manufacturing vs. Water — same products, different framing
- The Condition Monitoring pillar page has a similar industry filter
- A buyer can also enter from the Industries page (a slimmer top-level item, but not the primary navigation axis)

This is the same pattern Cisco uses, Dell EMC uses, and Schneider Electric uses for their commercial site IA.

---

## 8. Information architecture v2

### 8.1 Top navigation (proposed)

The simplest reading: **5 pillar items + Solutions + Industries + Company**. Eight items is too many for clean top nav, so the practical structure is one of:

#### Option A — Capabilities mega-menu (recommended)

| Top nav | Behavior |
|---|---|
| **Capabilities** | Mega-menu opening to all 5 pillars with one-sentence descriptors and product chips |
| **Solutions** | The 5 vertical solution pages (CNC, brownfield, multi-site, OEM, precision) + a new **Predictive Maintenance** solution anchoring VAS + E-IDOS |
| **Industries** | Industries landing page that filters pillar content (Oil & Gas, Power, Water, Mfg discrete, Mfg process, OEM) |
| **Architecture** | Standalone architecture page with the branded diagram |
| **Company** | About / contact / customers / resources |

Five top-nav items. Capabilities mega-menu carries the pillar structure. Solutions remain the vertical-narrative anchor.

#### Option B — Pillars-as-top-nav

| Top nav | Behavior |
|---|---|
| **Connectivity & Edge** | Direct landing |
| **Data Acquisition** | Direct landing |
| **Asset Intelligence** | Direct landing |
| **Condition Monitoring** | Direct landing |
| **Operational Intelligence** | Direct landing |
| **Solutions** | Vertical narrative anchor |
| **Industries** | Cross-cutting filter |
| **Company** | About + contact |

Eight top-nav items. Pillars are first-class but the nav bar gets crowded.

**Recommended:** Option A. The pillar names are valuable but they're more useful as second-level navigation organized around a "Capabilities" entry point. This keeps the top nav at the standard 5-item visual cleanliness.

### 8.2 Routes (revised from v1)

```
/                                      Homepage (capability-pillar hero)
/capabilities                          Capability overview (5 pillars)
  /capabilities/connectivity-edge      Pillar landing
    /edgeconnect                       Product page
    /edge-gateway                      Product page
  /capabilities/data-acquisition       Pillar landing
    /mdaq                              Product page
  /capabilities/asset-intelligence     Pillar landing
    /mtracker                          Product page
  /capabilities/condition-monitoring   Pillar landing
    /vas                               Application page
    /e-idos                            Product page
  /capabilities/operational-intelligence  Pillar landing
    /eremos-v2                         Product page
/solutions                             Solutions landing
  /solutions/cnc-machining             (existing v2)
  /solutions/precision-manufacturing   (existing v2)
  /solutions/brownfield-modernization  (existing v2)
  /solutions/multi-site-operations     (existing v2)
  /solutions/oem-machine-monitoring    (existing v2)
  /solutions/predictive-maintenance    NEW — anchors VAS + E-IDOS
/industries                            Industries landing (filter UI)
  /industries/oil-gas
  /industries/power-energy
  /industries/water-utilities
  /industries/manufacturing-discrete
  /industries/manufacturing-process
  /industries/mining-construction      NEW (E-IDOS-driven)
  /industries/aerospace                NEW (E-IDOS-driven)
  /industries/oem-monitoring
/security
/pricing
/architecture                          Standalone architecture page
/resources                             Downloads (datasheets, deck, etc.)
/customers
/company
/contact
```

About 25 routes in the full build-out. The homepage + capability landings (6 pages) and the new Predictive Maintenance solution page (1 page) are the highest-leverage Phase D + early Phase E targets.

---

## 9. Cascade (revised from v1)

No existing canonical asset is mutated in place. Each amendment creates a new major version.

| Asset | Current | Becomes | Phase | New in v2 |
|---|---|---|---|---|
| Architecture diagram | v1 | **v2** — adds Elpis hardware layer (pillar grouping reflected in label text) | C | unchanged |
| Architecture diagram spec | v2 | **v3** | C | unchanged |
| Website messaging architecture | v2 | **v3** — pillar-based IA, Industries as filter | D | restructured |
| Homepage copy | v2 | **v3** — capability-pillar hero, pillar-based section ordering | D | restructured |
| Pitch deck | v5 | **v6** — slides 4/7/8/9 touched: 4 = "Five capabilities, one ecosystem"; 7 = pillars-then-protocols; 8 = updated arch; 9 = trust posture + reliability trust | E | updated |
| Datasheet | v3 | **v4** — page 2 = capability pillars + arch diagram v2; page 3 = pillars-based outcomes | E | updated |
| Solution pages (5) | v2 | **v3 each** — hardware references + pillar tags | E | unchanged |
| **NEW — Predictive Maintenance solution** | none | **v1** — anchors VAS + E-IDOS, reliability-engineering buyer | E | **new in v2** |
| Security page | v2 | **v3** — adds hardware-trust + condition-data-isolation postures (pending validation) | E | updated |
| Sales objection guide | v2 | **v3** — three competitive frames from manifesto §4 | E | updated |
| ROI calculator spec | v2 | **v3** — hardware unit-economics + reliability-savings model | E | updated |
| **NEW** — `/capabilities` overview | none | **v1** | D | unchanged |
| **NEW** — pillar landing pages (5) | none | **v1 each** | D + early E | unchanged |
| **NEW** — hardware product pages | none | **v1 each** — mDAQ, mTracker, VAS, E-IDOS, Edge Gateway | E | **5 pages now**, was 4 in v1 |
| **NEW** — industries pages | none | **v1 each** — 6 routes + 2 new (Mining/Construction, Aerospace) | E | **8 industries**, was 6 in v1 |
| **NEW** — connectivity coverage page | none | **v1** | D | unchanged |
| **NEW** — architecture standalone page | none | **v1** | D | unchanged |

---

## 10. Open questions for Elpis (v2)

In addition to the v1 open questions, v2 adds:

### From v1 (still open)

1. Stand-alone Analog I/O / Digital I/O / OPC products — distinct SKUs or unified under mDAQ?
2. Per-product certifications (CE / UL / FCC / IEC / IP rating)
3. Manufacturing model (in-house / white-label / hybrid)
4. Firmware trust posture validation
5. Existing Elpis website URL + current positioning

### New in v2

6. **Roadmap hardware products** — are there others in development that should hint into the pillar structure now? Particularly: any Operational Intelligence-tier products (e.g. an EREMOS-V2-on-premise appliance), or any Data Acquisition variants (higher-channel-count, isolated-channel, intrinsically-safe)?
7. **Elpis-vs-partner sensor sourcing for E-IDOS** — does Elpis ship the contamination sensor itself, or is that a partner-supplied component?
8. **VAS + E-IDOS combined-buyer reality** — in real deals, are these bought together? If yes, the Predictive Maintenance solution page anchors both. If they're bought independently by different buyers, the page may need to be sub-divided.
9. **Reliability-engineering buyer persona** — does Elpis already have a defined persona for the maintenance / reliability buyer? If not, this is worth building before the Condition Monitoring pillar page lands.
10. **Condition-data isolation operational truth** — for the §6 trust-posture extension, confirm that EREMOS V2 does not share condition-data across tenants.

---

## 11. What stays unchanged from v1

- BRAND_TOKENS v1 — palette, typography, spacing scale unchanged
- Voice and tone — premium-industrial-confident-technical
- Existing product names — EdgeConnect, EREMOS V2, mDAQ, mTracker, VAS, Edge Gateway, E-IDOS
- Locked architectural decisions in CLAUDE.md §3
- Customer outcomes (OEE trust, downtime reduction, modernization, audit, offline operation)
- The defining differentiator (refined wording, same intent)
- Trust posture trio in deck v5

---

## 12. Sign-off and governance

This document is **v2 proposed**. v1 is retained as historical reference (industrial-intelligence-ecosystem-positioning-v1.md) for the cadence audit. Before v2 becomes the parent worldview, Elpis sign-off is required.

Recommended sign-off path:
1. Read both v2 docs (this + `hardware-ecosystem-map-v2.md`)
2. ChatGPT (or other) review pass on v2
3. Lock v2 as the parent worldview
4. Phase C (architecture diagram v2) starts on top of locked v2 positioning

This is the second iteration of marketing-worldview governance. It sits alongside `ARCHITECTURE_BLUEPRINT.md` (engineering worldview) and `platform-principles.md` (cross-cutting principles).

---

*Industrial Intelligence Ecosystem positioning — v2 proposed, 2026-05-25. Awaiting Elpis sign-off before downstream realignment begins.*
