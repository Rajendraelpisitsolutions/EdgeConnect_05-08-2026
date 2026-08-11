<!--
File:     docs/marketing/industrial-intelligence-ecosystem-positioning-v3.md
Purpose:  LOCKED master strategic positioning for Elpis. Every future
          homepage, deck, datasheet, diagram, solution page, and sales
          asset cites this doc as the parent worldview.
Audience: Every future Claude session, every designer, every salesperson,
          every product manager working on Elpis marketing.
Version:  v3 — LOCKED
Date:     2026-05-25
v2 -> v3 changes:
  - Five-product catalog formally locked (no near-term roadmap products).
  - E-IDOS -> EREMOS V2 integration acknowledged as roadmap, not
    shipped — Option A framing throughout (label as roadmap, ship now).
  - E-IDOS sensor sourcing softened — Elpis owns appliance integration,
    sensing element from OEM/supplier partners.
  - Defense and space-agency deployments surfaced (anonymous) as a
    credibility anchor — covers ISRO (VAS) and MoD via 3rd party (E-IDOS).
  - Geographic footprint (India + Middle East) surfaced.
  - AMC channel acknowledged as existing buyer reality, not formal
    partner program.
  - "Industrial Intelligence Stack" adopted as recurring narrative phrase.
  - Predictive Maintenance solution split into two — rotating machinery
    monitoring + hydraulic oil monitoring — within the Condition
    Monitoring pillar.
  - §13 added — terminology convergence as future opportunity.

v1 and v2 retained as historical reference. v3 is the parent worldview.
-->

# Elpis Industrial Intelligence Ecosystem — strategic positioning v3 (LOCKED)

This is the locked master positioning document. It supersedes v1 and v2. Every future marketing asset cites this doc as parent narrative.

The strategic shift completed in v3:

> **From:** software platform vendor (EdgeConnect + EREMOS V2)
>
> **To:** vertically integrated **Industrial Intelligence Stack** — five capability pillars (Connectivity & Edge, Data Acquisition, Asset Intelligence, Condition Monitoring, Operational Intelligence) delivered as one coherent ecosystem from machine signal to enterprise insight.

The five-product catalog is locked. The capability pillars are the primary commercial organizing lens. The hardware/software distinction is hidden at the buyer-facing layer.

---

## 1. The category Elpis occupies

> **"Elpis is the industrial intelligence ecosystem that delivers a complete vertically integrated capability stack — connectivity, acquisition, asset intelligence, condition monitoring, and operational intelligence — across one coherent platform, deployable end-to-end or layered into what you already run."**

For shorter homepage / deck use:

> *Elpis is the only industrial intelligence vendor that can be the entire data path — from the raw machine signal to the enterprise dashboard — or any subset of it, by customer choice.*

The phrase **"Industrial Intelligence Stack"** is the recurring narrative anchor across homepage, deck, datasheet, expo branding, and sales conversation.

---

## 2. The five capability pillars (locked)

| # | Pillar | One-sentence definition | Products |
|---|---|---|---|
| 1 | **Connectivity & Edge** | "Bring every signal on your floor into one operational data layer — without ripping out what you already have." | EdgeConnect + Edge Gateway |
| 2 | **Data Acquisition** | "Capture industrial signals directly from sensors when no PLC is available — or when you want a clean Elpis path." | mDAQ |
| 3 | **Asset Intelligence** | "Know where every machine is, how it's running, and how much value it's producing — wherever it's deployed." | mTracker |
| 4 | **Condition Monitoring** | "Move from break-fix to predict-and-prevent on rotating equipment and hydraulic systems." | VAS + E-IDOS |
| 5 | **Operational Intelligence** | "Turn collected data into OEE, alarms, incidents, and reports the team actually uses." | EREMOS V2 |

Full per-product detail is in `hardware-ecosystem-map-v3.md`. Catalog is locked at five products for the foreseeable future.

---

## 3. The three competitive frames

| Frame | Competitive set | Elpis line |
|---|---|---|
| **vs. acquisition / gateway vendors** | Advantech, Moxa, ICP DAS, HMS, Phoenix Contact, B+B SmartWorx | *"We ship the same box, but it already knows where to send the data — to a multi-tenant analytics platform built for OT."* |
| **vs. IIoT analytics vendors** | MachineMetrics, Sight Machine, Tulip, Litmus Automation, generic IIoT dashboards | *"We ship the same software-analytics layer, but we also make the field-acquisition hardware that feeds it — we own the entire signal-to-insight path."* |
| **vs. point condition-monitoring vendors** | IFM, SKF CM, Bently Nevada, PCB Piezotronics (vibration); Bureau Veritas, Castrol Labcheck, oil-lab subscriptions (fluid analysis) | *"We deliver vibration condition monitoring AND oil-health intelligence AND OEE AND asset tracking — on one integrated platform. One vendor instead of four."* |

These three frames anchor different homepage hero treatments, sales conversations, and objection-handling responses. The sales objection guide v3 (Phase E) builds out specific responses against each.

---

## 4. Credibility anchors

Two defensible external-facing credibility signals that the homepage and "Customers" page can land:

- **"Deployed in defense and space-agency programs"** — anonymous framing covering both space-agency rotating-equipment monitoring (VAS) and defense-ministry oil/fluid condition-monitoring (E-IDOS via third-party supplier integration). The named customers stay confidential per Elpis-confirmed external-claim policy; the category descriptor is defensible.
- **"Operating across India and the Middle East"** — current deployment footprint. Establishes international relevance without overclaiming "global."

Both can ship on the homepage in Phase D without any further confirmation.

---

## 5. The Industrial Intelligence Stack — narrative phrase

For recurring use across all assets:

> **Industrial Intelligence Stack**
>
> *Field Signals → Acquisition → Connectivity → Condition Monitoring → Operational Intelligence*

This is the **data-flow view** of the same ecosystem the pillars describe commercially. It is the right phrase for:
- Homepage architecture section
- Deck slide 8 (Architecture at a glance — supersedes the prior "four columns" framing)
- Datasheet page 2 (Architecture)
- Expo / trade-show banner headlines
- Sales walkthroughs

The pillar view is for navigation and buyer self-identification. The Stack view is for architectural explanation. The two coexist; the Stack name is recurring across both.

---

## 6. Trust posture (extended, with one clarification)

The pitch deck v5 trust posture trio stays in place:

- *Air-gapped factories are first-class*
- *A lapsed license never stops production data*
- *AI proposes — humans decide*

v3 adds three positioning commitments specific to the expanded ecosystem:

1. **The hardware is the same trust posture as the software** — same offline-capable, audit-defensible, OT-aware philosophy. (Pending firmware-behavior validation before external claim — see §10.)
2. **The reliability data stays on your operational platform, not Elpis's** — condition signals from VAS and E-IDOS feed your EREMOS V2 deployment; Elpis does not aggregate condition data across customers. (Pending EREMOS V2 condition-data isolation validation before external claim.)
3. **E-IDOS streaming integration to EREMOS V2 is on the near-term roadmap.** Today E-IDOS operates as a standalone reliability instrument (HMI + thermal printer + Android app + email reports). The streaming path into EREMOS V2 alarms/dashboards/incidents is not complex and is scheduled. We name it as roadmap — we do not overclaim the ecosystem closure that hasn't shipped yet.

The third commitment is part of the trust posture itself. Honest-roadmap signaling is a trust signal in OT, not a weakness.

---

## 7. Vocabulary changes (consolidated from v1, v2, v3)

| Out | In |
|---|---|
| "Industrial Intelligence Platform" | "Industrial Intelligence Ecosystem" or "Industrial Intelligence Stack" |
| "Two products — EdgeConnect + EREMOS V2" | "Five capabilities across one integrated stack" |
| "We connect to your controllers" | "We acquire from sensors, integrate with your controllers, and feed everything into one operational view" |
| "Software-only platform" | "Vertically integrated capability stack" |
| Mermaid architecture with controllers as leftmost layer | Updated diagram (Phase C) with Elpis hardware as field-acquisition layer |

### New vocabulary

| Term | Meaning |
|---|---|
| **Industrial Intelligence Stack** | The recurring narrative anchor — Field signals → Acquisition → Connectivity → Condition Monitoring → Operational Intelligence |
| **Capability pillar** | One of the five customer-facing organizing domains |
| **Field acquisition layer** | Architectural-layer term for the hardware that captures field signals (Pillars 2, 3, 4) |
| **Reliability engineering** | The buyer function Elpis now addresses via the Condition Monitoring pillar — Maintenance Manager + AMC provider |
| **AMC provider channel** | Third-party Annual Maintenance Contract service companies that buy Elpis tools to deliver their own services. Existing buyer reality at Elpis; not yet a formalized partner program |
| **Condition Monitoring duo** | VAS + E-IDOS as a paired pillar capability — rotating-machinery condition + hydraulic-fluid condition |
| **Fluid intelligence** | E-IDOS-specific positioning sub-term for oil-and-lubrication-health as a first-class operational signal |
| **End-to-end Elpis** | When a customer's entire data path is Elpis hardware → EdgeConnect → EREMOS V2 |
| **Hybrid Elpis** | When some signals come from Elpis hardware and some from third-party controllers — both feed the same intelligence layer |

---

## 8. Industry positioning

**Industries are a cross-cutting filter, not a primary navigation axis.** The pillar nav is primary; industry is how a buyer narrows pillar content to their context.

Industries Elpis addresses (with example deployment evidence in parentheses):

- **Oil & Gas** (pipeline monitoring; surface and downhole hydraulic systems)
- **Power & Energy** (substation telemetry; generation-equipment OEE)
- **Water & Utilities** (pump-station monitoring; flow + pressure analytics)
- **Manufacturing — discrete** (CNC machining; OEE; existing solution pages)
- **Manufacturing — process** (flow / temperature / pressure analytics)
- **OEM machine monitoring** (service-hours billing; warranty; remote fleet)
- **Mining & Construction** (heavy hydraulic systems via E-IDOS)
- **Aerospace** (ground-support equipment; precision rotating equipment via VAS)
- **Defense and space-agency programs** (precision monitoring; satellite radar antennas; ministry-of-defence fluid-condition deployments — anonymous external-facing framing)

These map to `/industries/*` routes in the IA. Each industry page filters the five pillars to industry-relevant framing.

---

## 9. Information architecture (locked)

### 9.1 Top navigation

**Five top-nav items:** `Capabilities` (mega-menu) + `Solutions` + `Industries` + `Architecture` + `Company`.

The `Capabilities` mega-menu opens to all five pillars with one-sentence descriptors and product chips. `Architecture` is a standalone landing for the Industrial Intelligence Stack diagram (Phase C output). `Industries` filters pillar content to industry context.

### 9.2 Route map (locked)

```
/                                            Homepage
/capabilities                                Capability overview (5 pillars)
  /capabilities/connectivity-edge
    /edgeconnect                             Product page (existing repositioning)
    /edge-gateway                            Product page (new — Phase E)
  /capabilities/data-acquisition
    /mdaq                                    Product page (new — Phase E)
  /capabilities/asset-intelligence
    /mtracker                                Product page (new — Phase E)
  /capabilities/condition-monitoring
    /vas                                     Application page (new — Phase E)
    /e-idos                                  Product page (new — Phase E)
  /capabilities/operational-intelligence
    /eremos-v2                               Product page (existing repositioning)
/solutions
  /solutions/cnc-machining                   (existing v2, repositioned)
  /solutions/precision-manufacturing         (existing v2, repositioned)
  /solutions/brownfield-modernization        (existing v2, repositioned)
  /solutions/multi-site-operations           (existing v2, repositioned)
  /solutions/oem-machine-monitoring          (existing v2, mTracker promoted)
  /solutions/rotating-machinery-monitoring   NEW — anchors VAS, reliability buyer
  /solutions/hydraulic-oil-monitoring        NEW — anchors E-IDOS, reliability buyer
/industries
  /industries/oil-gas
  /industries/power-energy
  /industries/water-utilities
  /industries/manufacturing-discrete
  /industries/manufacturing-process
  /industries/mining-construction
  /industries/aerospace
  /industries/oem-monitoring
/security
/pricing
/architecture                                Standalone — Industrial Intelligence Stack diagram
/resources                                   Downloads (datasheets, deck, etc.)
/customers
/company
/contact
```

Approximately 27 routes in the full Phase E build-out. Phase D scope is the homepage + the five capability landing pages + `/architecture` + the new `/solutions/rotating-machinery-monitoring` and `/solutions/hydraulic-oil-monitoring` pages. The rest is Phase E.

---

## 10. Cascade — locked downstream version bumps

No existing canonical asset is mutated in place. Each amendment creates a new major version.

| Asset | Current | Becomes | Phase |
|---|---|---|---|
| Architecture diagram | v1 | **v2** — adds Elpis hardware layer, pillar groupings, Industrial Intelligence Stack labeling | C |
| Architecture diagram spec | v2 | **v3** | C |
| Website messaging architecture | v2 | **v3** — pillar-based IA, Industries as cross-cutting filter | D |
| Homepage copy | v2 | **v3** — Industrial Intelligence Stack hero, pillar-organized sections | D |
| Capabilities overview page | none | **v1** | D |
| Pillar landing pages (5) | none | **v1 each** | D / early E |
| `/architecture` standalone | none | **v1** | D |
| `/solutions/rotating-machinery-monitoring` | none | **v1** (VAS-anchored) | D / early E |
| `/solutions/hydraulic-oil-monitoring` | none | **v1** (E-IDOS-anchored) | D / early E |
| Pitch deck | v5 | **v6** — slides 4/7/8/9 updated for pillars, ecosystem, stack | E |
| Datasheet | v3 | **v4** — page 2 = pillars + stack diagram + arch v2 | E |
| Solution pages (5 existing) | v2 each | **v3 each** | E |
| Security page | v2 | **v3** — hardware-trust + condition-data postures (pending validation) | E |
| Sales objection guide | v2 | **v3** — three competitive frames | E |
| ROI calculator spec | v2 | **v3** — hardware unit economics + reliability savings | E |
| Hardware product detail pages (5) | none | **v1 each** | E |
| Industries pages (8) | none | **v1 each** | E |

---

## 11. Open questions remaining

Most v1/v2 open questions resolved in v3. Remaining items, none of which block Phase C or D:

1. **Per-product certifications** — CE / UL / FCC / IEC / IP rating. Needed for Phase E product detail pages.
2. **Firmware trust posture validation** — phone-home behavior, signing, offline updates. Affects whether §6 commitment #1 ships externally.
3. ~~**Existing Elpis website URL**~~ → resolved: <https://www.elpisitsolutions.com>. Phase D references this as the artifact being repositioned.
4. **EREMOS V2 condition-data tenant isolation validation** — for §6 commitment #2.

---

## 12. What stays unchanged

- **BRAND_TOKENS v1** — palette, typography, spacing, contrast matrix
- **Voice and tone** — premium-industrial, confident-technical, outcomes-first, no AI-washing
- **Existing product names** — EdgeConnect, EREMOS V2, mDAQ, mTracker, VAS, Edge Gateway, E-IDOS
- **Locked architectural decisions** from CLAUDE.md §3
- **Customer outcomes** — OEE trust, downtime reduction, modernization, audit defensibility, offline operation
- **Trust posture trio** from deck v5
- **Hardware brand identity** — already coherent with BRAND_TOKENS v1, no realignment needed
- **Existing canonical assets** (deck v5, datasheet v3) — remain valid in active use until Phase E version bumps land

---

## 13. Terminology convergence — future opportunity

A note for record, not for action: existing product names (EdgeConnect, Edge Gateway, mDAQ, mTracker, VAS, E-IDOS, EREMOS V2) are **historically evolved** rather than platform-coherent. Gradual convergence toward consistent naming conventions is a future opportunity, but **not urgent**.

Renaming shipping products has real customer-recognition cost — sales materials, training, customer references all anchor on current names. The cost of renaming is high; the benefit is brand coherence.

**Decision for now:** v3 proceeds with current product names. A future positioning iteration (v4+) can revisit if Elpis decides to invest in name convergence as part of a broader brand initiative.

---

## 14. Governance

v3 is **LOCKED** as the parent worldview. Subsequent assets in Phase C, D, and E descend from v3.

Future amendments to this manifesto create v4, v5, etc., with the same cadence rules as other v-numbered marketing assets. The v1 → ChatGPT review → v2 → ChatGPT review → v3 cadence is the model.

This document sits at the marketing-worldview level of governance, alongside `ARCHITECTURE_BLUEPRINT.md` (engineering worldview) and `platform-principles.md` (cross-cutting principles).

---

*Industrial Intelligence Ecosystem positioning — v3 LOCKED 2026-05-25. Parent worldview for all subsequent Elpis marketing assets. Phase C (architecture diagram v2) begins on top of this version.*
