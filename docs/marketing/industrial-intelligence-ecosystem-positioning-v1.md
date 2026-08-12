<!--
File:     docs/marketing/industrial-intelligence-ecosystem-positioning-v1.md
Purpose:  Master strategic positioning document for Elpis. Captures the
          category shift from "Industrial Intelligence Platform" (software)
          to "Industrial Intelligence Ecosystem" (vertically integrated
          hardware + edge + intelligence). Every subsequent homepage,
          deck, datasheet, diagram, solution page, and sales asset cites
          this doc as the parent worldview.
Audience: Every future Claude session, every designer, every salesperson,
          every product manager working on Elpis marketing.
Format:   Strategic worldview document. Long-form prose with sectioning;
          NOT a tactical brief.
Source:   Elpis user-initiated strategic positioning amendment, 2026-05-25.
          Informed by docs/marketing/hardware-ecosystem-map-v1.md.
Version:  v1
Date:     2026-05-25
Status:   First-pass strategic worldview. Subject to user sign-off before
          downstream assets are realigned. Once signed off, becomes the
          authoritative parent narrative.
Cadence:  This document is at the same level of governance as
          ARCHITECTURE_BLUEPRINT.md and platform-principles.md — it
          governs marketing assets the way those govern engineering.
-->

# Elpis Industrial Intelligence Ecosystem — strategic positioning v1

This is the master worldview document for how Elpis is positioned to the market. It supersedes the implicit "software platform company" positioning that ran through the marketing-content session of 2026-05-24, restoring strategic truth that the prior framing had quietly removed.

Every downstream asset — homepage, pitch deck, datasheet, solution pages, architecture diagram, sales-objection guide, ROI calculator, outreach templates — descends from the worldview captured here. Where downstream assets contradict this doc, this doc wins.

---

## 1. The category shift

### 1.1 What we were saying

| Layer | Frame in prior marketing |
|---|---|
| Industrial Intelligence Platform | EdgeConnect + EREMOS V2 — two software products |
| Hardware | Customer-owned controllers (Elpis is *adjacent*) |
| Competitive identity | "Industrial software vendor with deep protocol coverage" |

### 1.2 What we are now saying

| Layer | Frame going forward |
|---|---|
| Industrial Intelligence Ecosystem | A vertically integrated stack from machine signal to enterprise insight |
| Field acquisition | **Elpis hardware** — mDAQ, mTracker, VAS, Edge Gateway, and additional devices |
| Edge runtime | EdgeConnect (software, deployable as a Windows service or as an Elpis appliance) |
| Industrial intelligence | EREMOS V2 (multi-tenant analytics, OEE, alarms, incidents, reports) |
| Integration | MQTT and OPC UA — interoperable with whatever else the customer runs |
| Competitive identity | "Industrial intelligence ecosystem company — we control the whole data path, by choice, end to end" |

### 1.3 Why this matters

The prior framing was strategically incomplete. It implicitly conceded that:
- Elpis competes only with software vendors
- Elpis's value is "integration and analytics on top of someone else's iron"
- Hardware buyers have to look elsewhere

The amended framing reflects what is actually true:
- Elpis already ships hardware (mDAQ, mTracker, VAS, Edge Gateway)
- The hardware is designed for the same intelligence platform — it isn't a separate business
- The complete data path — from sensor signal through enterprise dashboard — can be Elpis-end-to-end, or any subset where the customer prefers

Restoring the hardware layer to the marketing surface is not "adding products to the catalog." It is **restoring category truth**. Elpis was never just a software vendor; the prior marketing simply did not say so.

---

## 2. The three-layer worldview

The platform now formally communicates three vertically integrated layers:

### Layer 1 — Field acquisition (Elpis hardware)

Ruggedized, industrial-grade acquisition devices that convert physical signals into structured digital data.

- **mDAQ** — general-purpose analog + digital field acquisition
- **mTracker** — asset-level OEE telemetry with GSM/GPS
- **VAS** — vibration condition monitoring on the mDAQ platform
- **Edge Gateway** — Linux appliance that bridges existing PLC fleets and hosts the EdgeConnect runtime

This layer is **optional but native**. Customers who already have controllers with usable protocols can skip it and feed EdgeConnect directly. Customers who don't — or who want a clean Elpis-end-to-end signal path — buy Elpis acquisition hardware.

### Layer 2 — Edge runtime (EdgeConnect)

Protocol-agnostic edge software that normalizes signals from any acquisition source — Elpis hardware *or* third-party controllers — into a canonical data model and delivers them to the integration layer.

Architectural commitments unchanged from v1:
- Protocol-agnostic by architecture, not by accident
- Three-way diagnostics (source / pipeline / sink)
- Per-route store-and-forward
- Signed offline licensing
- Configuration is draft → validate → apply → rollback
- Audit-defensible hash-chained config history

Deployment shapes:
- As a Windows service on customer-owned hosts (shipping today)
- As a packaged appliance on the Elpis Edge Gateway (when EdgeConnect Linux support ships)

### Layer 3 — Industrial intelligence (EREMOS V2)

Multi-tenant analytics platform that turns acquired signals into operational decisions.

Unchanged from v1:
- PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT asset tree
- Auditable OEE via time-bounded Segments
- Persistent alarms and incident workflows
- Configurable alerting (email, chat, ticketing webhooks)
- Shift reports, OEE summaries, downtime breakdowns, tool-life trends (PDF + Excel)
- Dashboards that scale across mixed-vendor fleets

### The integration boundary

Between layers 2 and 3 sits the protocol contract — **MQTT** and **OPC UA Server** — that makes the platform interoperable with whatever else the customer runs. Cloud platforms (AWS, Azure, custom) subscribe to MQTT. SCADA / MES / HMI clients connect via OPC UA. This boundary is not new; it is reinforced by the amendment.

---

## 3. The defining differentiator

The single sentence that captures why this matters:

> **Elpis is the only industrial intelligence vendor that can be the entire data path — from the raw machine signal to the enterprise dashboard — or any subset of it, by customer choice.**

The competitive set fragments around this:

- **PLC and gateway vendors** (Advantech, Moxa, ICP DAS, HMS) sell hardware. They don't have the intelligence layer.
- **IIoT software vendors** (MachineMetrics, Sight Machine, Tulip, generic Industry 4.0 dashboards) sell software. They depend on third-party hardware.
- **Vertical solution stacks** (PTC ThingWorx, Siemens MindSphere) sell platforms but typically lock customers into a single ecosystem and a single deployment topology.
- **Elpis** is none of the above — Elpis is the **integrated ecosystem with optionality**. The customer keeps the choice; Elpis ships the full stack so the choice is always available.

This is a category Elpis defines, not a competitive niche Elpis fights for.

---

## 4. Voice and vocabulary changes

The category shift propagates into specific language changes. The marketing-content session locked voice; the amendment locks vocabulary on top of it.

### 4.1 Replacements

| Out | In |
|---|---|
| "Industrial Intelligence **Platform**" | "Industrial Intelligence **Ecosystem**" |
| "Two products" (referring to EdgeConnect + EREMOS V2) | "Three layers" or "Three-layer ecosystem" (acquisition + edge + intelligence) |
| "Edge runtime + Industrial intelligence" (when describing the whole company) | "Field acquisition + Edge runtime + Industrial intelligence" |
| "We connect to your controllers" (when introducing the platform) | "We acquire from sensors, integrate with your controllers, and feed everything into one operational view" |
| "Software for industrial connectivity" | "Vertically integrated industrial intelligence ecosystem" |

### 4.2 Additions (vocabulary previously absent)

| New term | Meaning |
|---|---|
| **Field acquisition layer** | Elpis hardware layer (mDAQ, mTracker, VAS, Edge Gateway) |
| **Signal-direct acquisition** | When Elpis hardware reads sensors directly, bypassing the customer PLC |
| **PLC-bridge acquisition** | When Elpis hardware (Edge Gateway) reads from existing PLC fleets |
| **Acquisition specialty** | A configured application of the acquisition platform (e.g. vibration analysis = VAS, asset utilization = mTracker mode) |
| **End-to-end Elpis** | When a customer's entire data path is Elpis hardware → EdgeConnect → EREMOS V2 |
| **Hybrid Elpis** | When some signals come from Elpis hardware and some come from third-party controllers — both feed the same intelligence layer |
| **Ecosystem optionality** | The defining promise: customer keeps choice of where Elpis enters the data path |

### 4.3 Removals

| Removed claim | Why |
|---|---|
| "Software-only platform" | False — Elpis ships hardware |
| "We adapt to any existing infrastructure" (without nuance) | Misleads buyers who think this means Elpis has no hardware |
| Mermaid architecture diagram with controllers as the leftmost layer | Inaccurate — the leftmost Elpis component is the field-acquisition device, when one is deployed |

---

## 5. Competitive set, restated

The amendment moves Elpis into a different competitive landscape. The new map:

### 5.1 Where Elpis now overlaps that it didn't before

- **Acquisition / gateway vendors** — Advantech, Moxa, ICP DAS, HMS, B+B SmartWorx, Phoenix Contact (acquisition + gateways). Elpis competes here on *integrated intelligence* — the buyer doesn't have to assemble a software stack.
- **Vibration / condition-monitoring vendors** — IFM, SKF (CM products), Bently Nevada, PCB Piezotronics. Elpis competes here via VAS on the mDAQ platform — *condition monitoring without a separate condition-monitoring vendor*.
- **Asset-tracking / fleet-OEE vendors** — Samsara, Geotab (light industrial), GE Predix-style fleet platforms. Elpis competes here via mTracker — *fleet visibility without an IIoT vendor lock-in*.

### 5.2 Where Elpis still competes (unchanged)

- **IIoT analytics vendors** — MachineMetrics, Sight Machine, Tulip, Litmus Automation
- **Industrial vertical platforms** — PTC ThingWorx, Siemens MindSphere, GE Predix legacy
- **Build-in-house / open source stacks** — Mosquitto + Grafana + InfluxDB + custom dashboards
- **Cloud-IoT generic** — AWS IoT, Azure IoT Hub paired with downstream tooling

### 5.3 The competitive frame Elpis owns

> *"We don't make you choose between buying hardware and buying intelligence. We don't make you assemble a stack. We ship the entire data path, and you decide where to plug us in."*

That message is unique to Elpis among the competitors in 5.1 and 5.2. It is the strategic asset that the amendment unlocks.

---

## 6. Trust posture — unchanged but extended

The pitch deck v5 trust posture trio (Air-gapped factories / Lapsed license / AI proposes — humans decide) stays in place. The amendment adds a fourth trust-posture commitment specific to the hardware layer:

> **The hardware is the same trust posture as the software.** Elpis hardware ships with the same offline-capable, audit-defensible, OT-aware operational philosophy as EdgeConnect and EREMOS V2. No phone-home, no telemetry-leak, no opaque firmware.

This is positioning, not yet a product commitment. It needs validation against actual firmware behaviour before any external claim. **For internal alignment only until validated** — see open questions §10.

---

## 7. Industry positioning

The amendment unlocks a new top-level navigation item — **Industries** — that the prior software-only positioning could not support credibly. Hardware buyers think in terms of industries (Oil & Gas, Power, Water, Manufacturing). Software-platform buyers think in terms of solutions (CNC machining, brownfield modernization). Both lenses are now valid.

### Industry top-level (new):

- **Oil & Gas** — pipeline monitoring, wellhead telemetry, remote-asset surveillance
- **Power & Energy** — substation monitoring, generation-equipment OEE, energy-meter aggregation
- **Water & Utilities** — pump-station monitoring, flow + pressure analytics, remote-site telemetry
- **Manufacturing — discrete** — CNC machining, OEE, predictive maintenance (current solutions stay valid)
- **Manufacturing — process** — flow / temperature / pressure analytics, batch tracking
- **OEM machine monitoring** — service-hours billing, warranty, remote fleet visibility

### Solutions layer (existing, retained):

CNC machining · Precision manufacturing · Brownfield modernization · OEM machine monitoring · Multi-site operations

The two lenses cross: a brownfield modernization solution might serve a power utility OR a CNC manufacturer; an oil & gas industry buyer might be looking for either remote-asset OEE OR predictive maintenance. The site IA accommodates both navigation paths.

---

## 8. Information architecture implications

The amendment changes the website IA from the locked 15-route map of `website-messaging-architecture-v2.md` to a richer structure:

### Proposed top-level nav (in conversation, not yet locked):

| Nav item | What it contains |
|---|---|
| **Platform** | The ecosystem overview — three layers, one foundation |
| **Edge & Connectivity** | EdgeConnect product page + connectivity-coverage page + the architecture story |
| **Hardware** | mDAQ + mTracker + VAS + Edge Gateway product pages + the field-acquisition story |
| **Solutions** | The 5 existing vertical solution pages, updated with hardware references |
| **Industries** | New top-level — by-industry landing pages |
| **Architecture** | Standalone architecture page with the branded diagram |
| **Company** | About / contact / customers / resources |

### Pages added by the amendment (not yet built):

- `/platform` — ecosystem overview
- `/hardware` — hardware landing page
- `/hardware/mdaq` — product detail
- `/hardware/mtracker` — product detail
- `/hardware/vas` — application page
- `/hardware/edge-gateway` — product detail
- `/industries` — landing
- `/industries/oil-gas` — by-industry
- `/industries/power-energy` — by-industry
- `/industries/water-utilities` — by-industry
- `/industries/manufacturing-discrete` — by-industry (links to solutions)
- `/industries/manufacturing-process` — by-industry
- `/industries/oem-monitoring` — by-industry (links to existing solution)
- `/architecture` — standalone diagram page

### Pages affected by the amendment (existing, need realignment):

- `/` (homepage) — full rewrite around ecosystem worldview
- `/solutions/brownfield-modernization` — explicit hardware option
- `/solutions/oem-machine-monitoring` — mTracker promoted to first-class proof point
- `/solutions/cnc-machining` — minor adjustments (mDAQ as sensor-direct option)
- `/solutions/precision-manufacturing` — minor adjustments
- `/solutions/multi-site-operations` — mTracker fleet visibility added
- `/security` — minor edit (hardware-layer trust posture added once validated)
- `/edgeconnect` — adjust to position EdgeConnect as the runtime that sits between hardware and intelligence (rather than as a standalone software product)
- `/eremos` — minor adjustments
- `/resources` — add hardware datasheets when produced

### Pages unchanged by the amendment:

- `/pricing` — pricing model is unchanged (still per-edition + per-module)
- `/contact` — unchanged
- `/customers` — unchanged
- `/company` — unchanged

---

## 9. Cascade — what changes downstream

This is the master list of marketing assets touched by the amendment. The cadence is: do not retroactively mutate signed-off versions. Create new major revisions instead.

| Asset | Current canonical | Becomes | When |
|---|---|---|---|
| Pitch deck | v5 (signed off, in active sales use) | **v6** — slide 4 = "Three layers, one ecosystem"; slide 7 = hardware row added; slide 8 = architecture diagram v2; slide 9 trust-posture trio retained, hardware-trust posture added as a fourth (pending validation) | Phase E, separate session |
| Datasheet | v3 (signed off) | **v4** — page 1 = ecosystem framing; page 2 = three-layer story + architecture diagram v2; page 3 = hardware + protocol both shown; page 4 = unchanged | Phase E, separate session |
| Architecture diagram | v1 (5 variants) | **v2** — adds Elpis hardware layer; all 5 variants regenerated | Phase C, separate session |
| Architecture diagram spec | v2 | **v3** — adds the hardware layer to the structural truth section | Phase C, alongside diagram |
| Website messaging architecture | v2 | **v3** — adds Hardware and Industries top-level nav; restructures the route tree | Phase D, alongside homepage |
| Homepage copy | v2 | **v3** — full rewrite around the three-layer ecosystem narrative | Phase D, alongside homepage |
| Solution pages (5) | v2 each | **v3 each** — hardware references added per solution | Phase E, batched session |
| Security page | v2 | **v3** — hardware trust posture added (pending firmware validation) | Phase E |
| Sales objection guide (internal) | v2 | **v3** — new objections from the hardware competitive set | Phase E |
| ROI calculator spec | v2 | **v3** — hardware unit-economics inputs added | Phase E |
| Connectivity coverage page | (within messaging architecture) | New standalone page in IA | Phase D |
| Hardware product pages | none | **v1 each** — mDAQ, mTracker, VAS, Edge Gateway | Phase E |
| Industries pages | none | **v1 each** — 6 pages | Phase E |
| Platform overview page | (within messaging architecture) | **v1** — standalone /platform page | Phase D |

**No existing canonical asset is overwritten in place.** Each amendment creates a new major version with the prior version retained as historical.

---

## 10. Open questions for Elpis to resolve

Before Phase D (homepage) lands, the following should be confirmed by Elpis. None of these block Phase A (this doc) or Phase C (architecture diagram v2). They block specific external-facing claims.

### 10.1 Hardware product completeness

The four products mapped in `hardware-ecosystem-map-v1.md` are confirmed shipping. The strategic positioning amendment from the user said "Analog I/O, Digital I/O, vibration acquisition, mDAQ, mTracker, etc." — the "etc." suggests there may be more. Worth confirming:

- Is there a stand-alone Analog I/O product distinct from mDAQ?
- Is there a stand-alone Digital I/O product distinct from mDAQ?
- Is there an OPC products family (the brochure footers mention "OPC Products")?
- Are there roadmap hardware products that the homepage should hint at?

### 10.2 Certifications and compliance

Hardware buyers expect certification claims. The brochures don't state these. Need from Elpis:

- CE / UL / FCC / CCC certification status per product
- IEC 61508 (functional safety) status if any
- RoHS / REACH compliance
- IP rating per product (IP65? IP67?)
- Industrial-temperature compliance (the mDAQ brochure says −10 °C to +85 °C — confirm this is correct)

### 10.3 Manufacturing model

Affects supply-chain and trust messaging:

- In-house design and manufacturing?
- White-labeled from an OEM partner?
- A mix (e.g. mDAQ in-house, Edge Gateway white-labeled)?

### 10.4 Hardware-layer trust posture validation

Section 6 above proposes extending the trust posture to the hardware layer. Before external claim:

- Does Elpis firmware phone home for anything? (License, update check, telemetry, diagnostics)
- Is firmware signed?
- Is firmware updatable offline?
- Are device credentials provisioned per device or shared?

If any of these are not yet operationally true, the trust-posture extension stays internal until they are.

### 10.5 Existing Elpis website

The positioning amendment notes that "Existing Angular/.NET website should NOT constrain the new strategic direction." Before Phase D:

- Where is the current site? (URL)
- What's currently positioned (software-only? hardware-only? both?)
- What language is currently in use that the amendment changes?
- Are there hardware product pages today that need to be retired or repositioned?

---

## 11. What this document does NOT change

The amendment is strategic positioning. It does not change:

- **Brand visual identity** — BRAND_TOKENS.md v1 stays canonical. Palette, typography, spacing scale, contrast matrix all unchanged. Hardware brochures already use the same visual language.
- **Voice / tone** — the marketing-content session voice ("set from scratch — confident-technical, premium-industrial, concrete over abstract, outcomes-first, no AI-washing") stays canonical.
- **Existing product names** — EdgeConnect stays EdgeConnect, EREMOS V2 stays EREMOS V2, mDAQ stays mDAQ, etc.
- **Architectural commitments** — the locked architectural decisions from CLAUDE.md §3 are unchanged. EdgeConnect being protocol-agnostic, EREMOS V2 being multi-tenant, etc.
- **Customer outcomes** — the outcome promises ("Trust your OEE number", "Modernize legacy controllers", "Air-gapped factories are first-class", etc.) all stay valid and central.

---

## 12. Sign-off and governance

This document is **v1 proposed**. Before it becomes the parent worldview, Elpis sign-off is required. Once signed off:

- All future marketing assets cite this doc as parent narrative
- Downstream version bumps (deck v6, datasheet v4, diagram v2, homepage v1, etc.) descend from this version
- Future amendments to this doc create v2, v3, etc., with the same cadence rules as other v-numbered marketing assets
- ChatGPT review pass remains the recommended validation step before promoting v1 → v2

This is the first document at the *marketing-worldview* level of governance. It sits alongside `ARCHITECTURE_BLUEPRINT.md` (engineering worldview) and `platform-principles.md` (cross-cutting principles) as a foundational governance document.

---

*Industrial Intelligence Ecosystem positioning — v1 proposed, 2026-05-25. Awaiting Elpis sign-off before downstream realignment begins.*
