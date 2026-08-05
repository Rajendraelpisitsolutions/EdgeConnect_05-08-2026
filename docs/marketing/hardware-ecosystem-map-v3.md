<!--
File:     docs/marketing/hardware-ecosystem-map-v3.md
Purpose:  LOCKED capability-pillar mapping of Elpis's 5-product hardware
          ecosystem. Supports the v3 positioning manifesto, Phase C
          architecture diagram, and Phase D homepage.
Version:  v3 — LOCKED
Date:     2026-05-25
v2 -> v3 changes:
  - 5-product catalog confirmed locked (no near-term roadmap products).
  - E-IDOS sensor sourcing softened — contamination sensors are
    OEM/supplier-partner-supplied; Elpis owns the appliance, HMI,
    printer, communication, analytics, and ecosystem integration.
  - E-IDOS deployments surfaced: defense/space-agency anchors
    (anonymous), AMC providers in India and Middle East.
  - VAS deployments surfaced: defense/space-agency monitoring of
    rotating equipment (anonymous), maintenance teams and AMC
    providers.
  - Buyer mapping refined: Maintenance Manager + AMC provider as the
    primary Condition Monitoring audience (same function, two
    specialisms — vibration and hydraulics).
  - AMC channel acknowledged as existing reality, not formalized
    partner program.
  - E-IDOS -> EREMOS V2 integration explicitly noted as roadmap.

v1 and v2 retained as historical reference.
-->

# Elpis hardware ecosystem — capability-pillar map v3 (LOCKED)

This is the locked capability-pillar map. The five Elpis hardware and software products are organized by **customer-facing capability domain**, the primary commercial lens for the website IA, homepage, and downstream marketing.

The three-layer data-flow worldview is retained as supporting architectural explanation (§7) for technical readers — architecture diagrams, integration documentation, deck slide 8. For the buyer, the capability pillar is the entry point.

---

## 1. The five capability pillars

| # | Pillar | Customer question | Products inside |
|---|---|---|---|
| 1 | **Connectivity & Edge** | "How do I get my controllers' data into one operational view, on-premise and offline-capable?" | EdgeConnect + Edge Gateway |
| 2 | **Data Acquisition** | "What if I don't have a PLC, or I want to bypass it and read sensors directly?" | mDAQ |
| 3 | **Asset Intelligence** | "How do I track utilization, location, and OEE on equipment I've shipped or deployed across multiple sites?" | mTracker |
| 4 | **Condition Monitoring** | "How do I move from break-fix to predict-and-prevent on rotating equipment and hydraulic systems?" | VAS + E-IDOS |
| 5 | **Operational Intelligence** | "How do I turn all of this into OEE, alarms, incidents, and reports the team actually uses?" | EREMOS V2 |

**Catalog locked at five products** for the foreseeable future. No "coming soon" placeholders needed in the IA.

The hardware/software distinction is hidden at the buyer layer. Two pillars (Connectivity & Edge, Operational Intelligence) span both. Three pillars (Data Acquisition, Asset Intelligence, Condition Monitoring) deliver capability through hardware-plus-specialized-application.

---

## 2. Pillar 1 — Connectivity & Edge

### 2.1 EdgeConnect (software)

Protocol-agnostic edge runtime. Already documented in the canonical marketing stack (datasheet v3, pitch deck v5). Deploys as a Windows service today; Linux on the roadmap (becomes the Edge Gateway host then).

### 2.2 Edge Gateway — industrial PLC-to-cloud appliance

**Category:** ruggedized industrial gateway running embedded Linux.

**What it does:** bridges existing PLC fleets (Modbus TCP, Ethernet/IP) to the network. Web-configurable, USB firmware updates.

**Strategic dual identity:**
- **Today:** standalone PLC-to-cloud gateway with built-in Modbus TCP and cellular publish
- **Tomorrow** (when EdgeConnect Linux support ships): the canonical EdgeConnect appliance

**What it eliminates from a customer BOM:**
- A separate industrial PC for edge software
- A separate Linux gateway for protocol bridging
- A separate cellular modem for remote sites
- The need to host EdgeConnect on customer-owned Windows infrastructure

**Key positioning anchors:** embedded Linux, Modbus TCP server/client, 4G/Wi-Fi/Ethernet, 256 MB RAM / 2 GB Flash, 24 V DC, 200 × 150 × 75 mm rugged enclosure.

---

## 3. Pillar 2 — Data Acquisition

### 3.1 mDAQ — general-purpose field acquisition

**Category:** ruggedized IoT data acquisition device.

**What it does:** captures industrial sensor signals (pressure, flow, level, temperature, etc.) directly from the field, with no PLC in the loop.

**What it eliminates from a customer BOM:**
- A standalone PLC for sensor acquisition
- A separate cellular modem and edge appliance
- A site-specific battery backup

**Why it matters strategically:** the **field-replacement** product. Combined with EdgeConnect + EREMOS V2, mDAQ delivers a complete Elpis-only signal-to-dashboard path with no third-party hardware in the chain.

**Key positioning anchors:** 4 analog channels (0–10 V or 4–20 mA), 16-bit, 860 S/s; 8 × 24 V digital inputs + 8 × 24 V digital outputs; Modbus TCP/RTU acquisition, HTTPS/MQTT publish; 4G, Wi-Fi, GPS, optional Ethernet; −10 °C to +85 °C; optional battery; 180 × 150 × 60 mm.

---

## 4. Pillar 3 — Asset Intelligence

### 4.1 mTracker — asset utilization and OEE telemetry

**Category:** miniature GSM/GPS asset-tracking and OEE telemetry device.

**What it does:** tracks utilization of industrial assets — fixed and mobile — and reports OEE inputs (production time, downtime, idle time, output quantity) directly from equipment-level digital signals.

**What it eliminates from a customer BOM:**
- Manual production-hour spreadsheets
- Separate GPS / geo-fence trackers
- Service-hours odometers for warranty triggers
- Asset-presence audits

**Strategic adjacencies:** machine builders / OEMs with service-hours billing and warranty programs; multi-site operators tracking idle assets; geo-fenced compliance.

**Key positioning anchors:** GSM/GPS 4G with battery backup; equipment-level digital inputs; geo-fence alerts; designed for retrofit attachment.

---

## 5. Pillar 4 — Condition Monitoring

This is the pillar where Elpis enters **reliability-engineering territory**. Two specialized products, both pairing physical-world measurement with domain-specific analytics, both reaching the same buyer function (maintenance and AMC) through different equipment specialisms (rotating machinery vs hydraulics).

### 5.1 VAS — Vibration Analyser System

**Category:** vibration condition-monitoring application built on the mDAQ platform.

**What it does:** detects deviations from normal vibration patterns on rotating machinery, conveyor systems, gearboxes, and structural components. Identifies bearings issues, imbalance, misalignment, looseness, and cracks **before** failure.

**Anchor deployments:** defense and space-agency programs (precision monitoring of high-value rotating equipment); industrial customers via maintenance teams and AMC providers across India and the Middle East.

**What it eliminates from a customer BOM:**
- A dedicated vibration analyser console (often a five-figure standalone instrument)
- A separate condition-monitoring software stack
- Manual handheld vibration spot-checks
- A third-party predictive-maintenance vendor relationship

**Strategic positioning:** *same hardware platform as mDAQ, specialized acquisition and analytics. One acquisition platform, multiple application specialties, one intelligence stack.*

**Analytical capabilities** (orientation only): time-domain (peak detection, RMS severity); frequency-domain (FFT, spectrum); order analysis, Bode plot, polar plot, cascade, waterfall; failure-mode mapping (bearing, gear, structural).

### 5.2 E-IDOS — Oil Health Intelligence appliance

**Category:** rugged industrial appliance for hydraulic / lubrication oil condition monitoring.

**What it does:** continuously measures hydraulic and lubrication-oil health — solid particle contamination, water saturation, oil flow — in both online and offline states. Logs to ISO/NAS cleanliness standards. Generates real-time analysis that drives predictive maintenance and prevents unexpected shutdown.

**Anchor deployments:** defense Ministry-of-Defence-tier programs (via third-party supplier integration); AMC providers across India and the Middle East serving industrial hydraulic customers.

**Sensor-agnostic design (in-house controller, multi-vendor sensor support):** Elpis developed the **Sensor/HMI Controller** in-house — the device's electronics, signal-conditioning logic, ISO/NAS-compliant analytics, touch-screen HMI, on-board thermal printer, BLE connectivity, mobile app, and communication stack are all Elpis IP. The device is **sensor-agnostic on the contamination input side**: it supports leading vendor sensors including **HYDAC, Parker, MP Filter, and Argo-Hytos**. This mirrors EdgeConnect's protocol-agnostic philosophy at the hardware acquisition layer — *the controller is Elpis; the customer keeps the sensor choice*.

This is a meaningful positioning differentiator. Most condition-monitoring appliances lock the customer into the vendor's preferred sensor element; E-IDOS leaves the choice open, which matters for customers with existing sensor inventory, established supplier relationships, or specialized application requirements (different sensors handle different oil types, viscosity ranges, and contamination profiles).

**EREMOS V2 integration status:** Today E-IDOS operates as a **standalone reliability instrument** — auto-emails reports, prints on-site via thermal printer, exposes data via BLE Android app. The streaming integration into EREMOS V2 (alarms, dashboards, incident workflows) is on the near-term roadmap and is not architecturally complex. Until that ships, E-IDOS positioning emphasizes its standalone instrument value; the ecosystem promise is honest about being roadmap.

**What it eliminates from a customer BOM:**
- A separate oil-contamination laboratory contract
- Manual oil-sample collection and shipping
- A standalone oil-condition monitor
- A third-party fluid-analysis vendor
- Service interval guesswork on hydraulic and lubrication systems

**Why it matters strategically:** the **expansion product** that takes Elpis from "machine telemetry" into **fluid condition intelligence**. Few IIoT platforms touch this category — fluid analytics has historically been a separate vendor relationship (Bureau Veritas, Castrol Labcheck, oil-lab subscription services). With E-IDOS, Elpis collapses that vendor into the same operational intelligence stack as everything else.

**Form factor strategic note:** E-IDOS's built-in HMI, thermal printer, and BLE Android app aren't accidental features — they are exactly what an **AMC provider** needs to take the instrument to a customer site, run a measurement, hand the printed ISO/NAS-compliant report to the customer, and walk away with a documented diagnostic. The product was designed for both in-house maintenance teams and the service-contractor channel.

**Strategic adjacencies:** Heavy engineering vehicles; excavators; industrial hydraulics; hydraulic test stands; mining and construction equipment; aerospace ground-support equipment; Oil & Gas downhole and surface hydraulic systems.

**Key positioning anchors** (orientation only): ISO/NAS cleanliness logging; touch-screen HMI; 58 mm thermal printer; 4G / Wi-Fi / GPS / BLE; M12 sensor connectors; auto-email reporting; Android companion app.

### 5.3 Why these two products belong together

Both are **condition-monitoring instruments** taking Elpis beyond telemetry into reliability engineering. They share:

- **Output:** time-series condition data + alerts + diagnostic reports
- **Buyer function:** the maintenance organization (in-house Maintenance Manager + the AMC provider channel)
- **Customer pain solved:** "I find out my machine is broken at the moment it stops working. I want to find out three weeks earlier."
- **EREMOS V2 destination:** both feed (or will feed — see E-IDOS roadmap note) the same alarms / dashboards / incident workflows
- **Strategic narrative pull:** both expand the addressable buyer set from operations into maintenance and reliability

They serve **two equipment specialisms within one buyer function** — VAS for rotating machinery, E-IDOS for hydraulic and lubrication systems. The shared buyer function is what makes Condition Monitoring a coherent pillar.

---

## 6. Pillar 5 — Operational Intelligence

### 6.1 EREMOS V2

Multi-tenant analytics platform that turns acquired signals into operational decisions. Documented in the canonical marketing stack. PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT asset tree, auditable OEE via Segments, persistent alarm and incident workflows, configurable alerting, PDF + Excel reports, tool-life and tag mapping, mixed-fleet dashboard scaling.

EREMOS V2 receives signals from all four other pillars (with the E-IDOS streaming integration as a near-term roadmap completion).

---

## 7. The Industrial Intelligence Stack (data-flow view, retained from v2)

The pillar view is the primary commercial lens. For technical explanation, the **Industrial Intelligence Stack** captures the data-flow architecture in five words:

```
                Field Signals
                     │
                     ▼
    ┌────────────────────────────────────┐
    │     ACQUISITION                    │   ← Pillars 2, 3, 4
    │     mDAQ · mTracker · VAS · E-IDOS │
    └────────────────────────────────────┘
                     │
                     ▼
    ┌────────────────────────────────────┐
    │     CONNECTIVITY                   │   ← Pillar 1
    │     EdgeConnect + Edge Gateway     │
    └────────────────────────────────────┘
                     │
                     ▼
    ┌────────────────────────────────────┐
    │     CONDITION MONITORING           │   ← (Pillar 4 specialty
    │     VAS + E-IDOS analytics         │      analytics path)
    └────────────────────────────────────┘
                     │
                     ▼
    ┌────────────────────────────────────┐
    │     OPERATIONAL INTELLIGENCE       │   ← Pillar 5
    │     EREMOS V2                      │
    └────────────────────────────────────┘
                     │
                     ▼
    Operations · SCADA · Cloud platforms
```

The phrase **"Industrial Intelligence Stack"** is the recurring narrative anchor across homepage, deck, datasheet, expo branding, and sales conversation.

---

## 8. Buyer-to-pillar map (refined)

| Buyer | Primary pillar | Secondary | Notes |
|---|---|---|---|
| Plant manager / Ops VP | Operational Intelligence | Connectivity & Edge | Outcomes-driven; OEE-focused |
| Industrial IT / OT manager | Connectivity & Edge | Operational Intelligence | Architecture and integration |
| **Maintenance Manager (in-house)** | **Condition Monitoring** | Operational Intelligence | Reliability engineering buyer — pays for VAS and/or E-IDOS to prevent unexpected shutdown |
| **AMC provider (B2B2B channel)** | **Condition Monitoring** | Asset Intelligence | Service-contract delivery — buys VAS/E-IDOS to deliver better diagnostics to *their* customers |
| OEM machine builder / service team | Asset Intelligence | Operational Intelligence | Service-hours billing, warranty, fleet visibility |
| Quality / compliance manager | Operational Intelligence | Data Acquisition | Audit-defensible OEE and provenance |
| Plant engineer (greenfield / retrofit) | Data Acquisition | Connectivity & Edge | Bypassing PLC, direct sensor acquisition |

**The AMC channel note:** AMC providers are an **existing buyer reality** at Elpis, not a formalized partner program. The Condition Monitoring pillar page should acknowledge this audience explicitly. Building out a formal channel program (partner certification, MDF, co-branded materials) is a future strategic option, not a present claim.

---

## 9. Geographic and credibility anchors

For homepage / IA use:

- **"Operating across India and the Middle East"** — current deployment footprint
- **"Deployed in defense and space-agency programs"** — anonymous credibility anchor covering both ISRO (VAS, satellite radar antenna vibration monitoring) and MoD (E-IDOS via third-party supplier integration)

Specific customer names are deliberately omitted per Elpis-confirmed external-claim policy. Defense and space-agency framing provides the credibility signal without crossing NDA or attribution boundaries.

These claims are **defensible today** — they describe real deployments, not aspirations. They can land on the homepage and "Customers" page when the homepage ships in Phase D.

---

## 10. Open questions remaining

Most v1/v2 open questions are now resolved. Remaining items:

1. ~~**Per-product certifications** (CE / UL / FCC / IEC / IP rating per product)~~ → **RESOLVED 2026-06-05** (user + ChatGPT direction, locked in design-system v4 §24.B.0): the Elpis hardware products carry **no formal third-party certifications currently** (CE / UL / FCC / IEC); products are **IP65 / IP67-compatible** but **not separately certified**; certification, ingress-protection, and site-compliance are handled **case-by-case during BOM scope**, and certified/rated claims are published only when formal evidence exists for the specific product/configuration. The Phase E hardware product pages (§24.B) therefore make **no formal cert claims** — they use "IP65 / IP67-compatible" wording and defer cert/IP/site-compliance to BOM scope.
2. **Firmware trust posture validation** — phone-home behavior, signing, offline updates. Still open. Affects whether the §6 hardware-trust extension in the manifesto ships externally.
3. ~~**Existing Elpis website**~~ → resolved: <https://www.elpisitsolutions.com>. Phase D references this as the artifact being repositioned.

Resolved in v3:
- ~~Stand-alone Analog I/O / Digital I/O / OPC products~~ → mDAQ is the unified product for the foreseeable catalog
- ~~Roadmap hardware products~~ → none in development
- ~~E-IDOS contamination sensor sourcing~~ → OEM/supplier partner-supplied
- ~~VAS + E-IDOS combined-buyer reality~~ → same buyer function (Maintenance Manager + AMC), different specialisms (vibration vs hydraulics)
- ~~Reliability-engineering buyer persona~~ → Maintenance Manager + AMC provider, both in §8
- ~~Condition-data isolation in EREMOS V2~~ → moot until E-IDOS integration ships; integration is roadmap, not complex

---

## 11. What stays unchanged from v1/v2

- BRAND_TOKENS v1 palette and typography
- Voice and tone — premium-industrial, confident-technical
- Existing product names (EdgeConnect, EREMOS V2, mDAQ, mTracker, VAS, Edge Gateway, E-IDOS)
- Locked architectural decisions from CLAUDE.md §3
- Customer outcomes (OEE trust, downtime reduction, modernization, audit, offline operation)
- Trust posture trio from deck v5

---

*Hardware ecosystem capability-pillar map v3 — LOCKED 2026-05-25. Feeds positioning manifesto v3 (also LOCKED), Phase C architecture diagram v2, Phase D homepage. Per-product detail pages defer to Phase E.*
