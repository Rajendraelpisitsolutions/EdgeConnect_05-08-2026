<!--
File:     docs/marketing/hardware-ecosystem-map-v2.md
Purpose:  Strategic-level mapping of Elpis's industrial hardware products
          to their positions in the Industrial Intelligence Ecosystem,
          organized by CAPABILITY PILLAR rather than by data-path layer.
Scope:    v2 of the hardware ecosystem map. Supports Phase A (manifesto
          v2), Phase C (architecture diagram v2), Phase D (homepage).
Source:   Elpis hardware brochures — mDAQ, mTracker, VAS, Edge Gateway,
          E-IDOS. v1 covered the first four; v2 adds E-IDOS and
          restructures by capability pillar.
Version:  v2
Date:     2026-05-25
v1 -> v2 changes:
  - Added E-IDOS as fifth hardware product (oil / fluid condition
    monitoring — pairs with VAS as a Condition Monitoring duo).
  - Reorganized from product-by-product narrative to CAPABILITY-PILLAR
    structure: Connectivity & Edge / Data Acquisition / Asset
    Intelligence / Condition Monitoring / Operational Intelligence.
  - Three-layer data-flow worldview retained as supporting
    architectural explanation, but the pillar structure is the
    primary organizing lens for marketing and IA.
  - "What it eliminates from a customer BOM" framing retained per
    product.
-->

# Elpis hardware ecosystem — capability-pillar map v2

This document maps Elpis's five existing industrial hardware products to **capability pillars** — the customer-facing organizing principle that shapes the website IA, homepage narrative, and downstream marketing assets.

The previous v1 of this document used a data-path layer view (field acquisition → edge → intelligence). That view is retained in §5 as the supporting architectural explanation. The **primary organizing lens going forward is the capability pillar**, because that is how industrial buyers think about needs.

---

## 1. The five capability pillars

| # | Pillar | Customer question it answers | Products inside |
|---|---|---|---|
| 1 | **Connectivity & Edge** | "How do you get my existing controllers' data into a unified operational view, on-premise and offline-capable?" | EdgeConnect (software) + Edge Gateway (hardware appliance) |
| 2 | **Data Acquisition** | "What if I don't have a PLC, or I want to bypass it and read sensors directly?" | mDAQ + future I/O devices |
| 3 | **Asset Intelligence** | "How do I track utilization, location, and OEE on equipment I've shipped or deployed across multiple sites?" | mTracker |
| 4 | **Condition Monitoring** | "How do I move from break-fix to predictive maintenance on rotating machinery and hydraulic / lubrication systems?" | VAS (vibration) + E-IDOS (oil / fluid) |
| 5 | **Operational Intelligence** | "How do I turn all of this into OEE, alarms, incidents, and reports the team will actually use?" | EREMOS V2 (multi-tenant analytics) |

Two pillars (Connectivity & Edge, Operational Intelligence) span hardware *and* software. Three pillars (Data Acquisition, Asset Intelligence, Condition Monitoring) are primarily hardware-plus-specialized-application. **No pillar is "hardware-only" or "software-only"** — the artifact distinction is hidden from the buyer because the buyer thinks in capabilities.

---

## 2. Pillar 1 — Connectivity & Edge

### 2.1 EdgeConnect (software)

Already documented in the canonical marketing stack (datasheet v3, pitch deck v5). Protocol-agnostic edge runtime that normalizes any industrial signal into a canonical data model and routes it to the integration layer. Deploys as a Windows service on customer-owned hosts today; Linux on the roadmap.

### 2.2 Edge Gateway — industrial PLC-to-cloud gateway appliance

**Category:** ruggedized industrial gateway running embedded Linux.

**What it does:** bridges existing PLC fleets (Modbus TCP, Ethernet/IP) to the network. Web-configurable, USB firmware updates. Hosts the EdgeConnect runtime when EdgeConnect is deployed as an appliance rather than software-on-customer-host.

**What it eliminates from a customer BOM:**
- A separate industrial PC for edge software
- A separate Linux gateway for protocol bridging
- A separate cellular modem for remote sites
- The need to host EdgeConnect on a customer-owned Windows server

**Strategic dual identity:**
- **Standalone:** PLC-to-cloud gateway with built-in Modbus TCP and cellular publish
- **EdgeConnect appliance:** the canonical deployment vehicle for EdgeConnect when Linux support ships

**Key positioning anchors** (orientation, not full spec):
- Embedded Linux OS, web-configurable, USB firmware updates
- Modbus TCP server/client, 4G/Wi-Fi/Ethernet
- 256 MB RAM, 2 GB Flash, 24 V DC, RJ45 Gigabit
- 200 × 150 × 75 mm rugged enclosure

---

## 3. Pillar 2 — Data Acquisition

### 3.1 mDAQ — general-purpose field acquisition device

**Category:** ruggedized IoT data acquisition device.

**What it does:** captures industrial sensor signals (pressure, flow, level, temperature, etc.) directly from the field, with no PLC in the loop.

**What it eliminates from a customer BOM:**
- A standalone PLC for sensor acquisition
- A separate cellular modem
- A separate edge appliance
- A site-specific battery backup

**Why it matters strategically:** the **field-replacement** product. Targets sites with no PLC infrastructure or with a desire to bypass it entirely — remote sites, retrofits, single-machine deployments. Combined with EdgeConnect + EREMOS V2, mDAQ delivers a complete Elpis-only signal-to-dashboard path with no third-party hardware in the chain.

**Key signal-acquisition properties** (orientation only):
- 4 analog channels (0–10 V or 4–20 mA), 16-bit, 860 S/s
- 8 × 24 V digital inputs, 8 × 24 V digital outputs
- Acquisition: Modbus TCP / Modbus RTU. Publish: HTTPS / MQTT
- Communication: 4G (SIM slot), Wi-Fi, GPS, Ethernet (optional)
- Operating range −10 °C to +85 °C, ruggedized enclosure, optional battery
- 180 × 150 × 60 mm

**Pillar expansion notes:** the strategic positioning amendment mentioned standalone "Analog I/O" and "Digital I/O" devices — confirming whether these are SKUs distinct from mDAQ, or whether mDAQ is the unified product, is one of the open questions for Phase E.

---

## 4. Pillar 3 — Asset Intelligence

### 4.1 mTracker — asset utilization and OEE telemetry

**Category:** miniature GSM/GPS asset-tracking and OEE telemetry device.

**What it does:** tracks utilization of industrial assets — both fixed and mobile — and reports OEE inputs (production time, downtime, idle time, output quantity) directly from equipment-level digital signals.

**What it eliminates from a customer BOM:**
- Manual production-hour spreadsheets
- Separate GPS / geo-fence trackers
- Service-hours odometers for warranty triggers
- Asset-presence audits

**Why it matters strategically:** the **lightweight asset-OEE** product. Targets fleets — machine vendors with deployed equipment at customer sites, multi-site operators with mobile rigs, OEMs running warranty / service-hours billing, asset-rental businesses needing usage-based billing.

**Strategic adjacencies:**
- Machine builders / OEMs with service-hours billing and warranty programs
- Multi-site operators tracking idle assets across plants
- Geo-fenced compliance (alerts when assets leave allowed zones)

**Key positioning anchors** (orientation only):
- GSM/GPS 4G, battery backup, geo-fence alerts
- Digital inputs from equipment-level signals
- Compact, designed for retrofit attachment to existing machines

---

## 5. Pillar 4 — Condition Monitoring

This pillar contains the products that take Elpis from "operational telemetry" into **reliability engineering territory**. Two specialized condition-monitoring products, both pairing physical-world measurement with domain-specific analytics. Together they make Condition Monitoring a coherent capability rather than scattered single products.

### 5.1 VAS — Vibration Analyser System

**Category:** vibration condition-monitoring application built on the mDAQ platform.

**What it does:** detects deviations from normal vibration patterns on rotating machinery, conveyor systems, gearboxes, and structural components. Identifies bearings issues, imbalance, misalignment, looseness, and cracks **before** failure.

**What it eliminates from a customer BOM:**
- A dedicated vibration analyser console (often a five-figure standalone instrument)
- A separate condition-monitoring software stack
- Manual handheld vibration spot-checks
- A third-party predictive-maintenance vendor

**Why it matters strategically:** **same hardware platform as mDAQ, specialized acquisition and analytics**. The strategic message: *one acquisition platform, multiple application specialties, one intelligence stack*.

**Analytical capabilities** (orientation only):
- Time-domain analysis (peak detection, RMS severity)
- Frequency-domain analysis (FFT, spectrum)
- Order analysis, Bode plot, polar plot, cascade, waterfall
- Maps frequency peaks to failure modes (bearing, gear, structural)

### 5.2 E-IDOS — Oil Health Intelligence appliance

**Category:** rugged industrial appliance for hydraulic / lubrication oil condition monitoring.

**What it does:** continuously measures hydraulic and lubrication-oil health — solid particle contamination, water saturation, oil flow — in both online and offline states. Logs to ISO/NAS cleanliness standards. Produces real-time analysis used to drive predictive maintenance on hydraulic and lubrication systems.

**Where it sits in the data path:** an instrument-class appliance with built-in HMI (touch screen), thermal printer (on-site reports), BLE for mobile app, and Wi-Fi/4G for cloud reporting. Communicates to EREMOS V2 alongside the rest of the condition-monitoring stream.

**What it eliminates from a customer BOM:**
- A separate oil-contamination laboratory contract
- Manual oil-sample collection and shipping
- A standalone oil-condition monitor
- A third-party fluid-analysis vendor
- Service interval guesswork on hydraulic and lubrication systems

**Why it matters strategically:** this is the **expansion product** that takes Elpis from "machine telemetry" into **fluid condition intelligence**. Few IIoT platforms touch this — fluid analytics has traditionally been a separate vendor category (Bureau Veritas, Castrol Labcheck, oil-lab subscription services). With E-IDOS, Elpis collapses that vendor relationship into the same operational intelligence stack as everything else.

**Strategic adjacencies:**
- Heavy engineering vehicles, excavators, mining equipment
- Industrial hydraulics and hydraulic test stands
- Construction equipment with high-cost hydraulic systems
- Aerospace ground-support equipment with strict fluid-quality requirements
- Oil & Gas downhole and surface hydraulic systems

**Key positioning anchors** (orientation only):
- ISO/NAS cleanliness standards continuous logging
- Touch-screen HMI for on-the-spot diagnostics
- 58 mm thermal printer for printed sample reports (audit-friendly for field service)
- 4G / Wi-Fi / GPS / BLE communication; BLE Android companion app
- M12 sensor connectors (industrial-standard, hot-pluggable)
- Auto-email of reports after each measurement
- Industries: Oil & Gas, Mining & Construction, Aerospace, machine manufacturers

### 5.3 Why these two products belong together

Both products are **condition-monitoring instruments** that take Elpis beyond telemetry into reliability engineering. They share:

- **Output format:** time-series condition data + alerts + diagnostic reports
- **Buyer:** the reliability engineer, the maintenance manager, the OEM service team
- **Customer pain solved:** "I find out my machine is broken at the moment it stops working. I want to find out three weeks earlier."
- **EREMOS V2 integration:** both feed condition signals into the same alarms / alerts / dashboards
- **Strategic narrative pull:** both expand the addressable buyer set from "operations" into "maintenance and reliability"

Treating them as a single Condition Monitoring pillar — rather than two unrelated products in a flat hardware catalog — produces a much stronger commercial story.

---

## 6. Pillar 5 — Operational Intelligence

### 6.1 EREMOS V2

Already documented in the canonical marketing stack. Multi-tenant analytics platform that turns acquired signals into operational decisions. PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT asset tree, auditable OEE via Segments, persistent alarm and incident workflows, configurable alerting, PDF + Excel reports, tool-life and tag mapping, mixed-fleet dashboard scaling.

In the pillar view: EREMOS V2 receives signals from all four other pillars and turns them into the operational view the customer actually uses. It is the *consumer-facing* layer — everything else feeds into it.

---

## 7. Data-path architectural view (retained from v1)

The pillar view is the primary commercial organizing lens. For technical explanation — particularly in architecture diagrams and integration documentation — the **three-layer data-flow view** remains valid. It maps onto the pillars as follows:

```
                ┌───────────────────────────────────┐
                │  Field acquisition layer          │   ← Pillars 2, 3, 4
                │  (signal capture)                 │     (Data Acquisition,
                │                                   │      Asset Intelligence,
                │  mDAQ · mTracker · VAS · E-IDOS   │      Condition Monitoring)
                └───────────────────────────────────┘
                                  │
                                  ▼
                ┌───────────────────────────────────┐
                │  Edge runtime layer               │   ← Pillar 1
                │  (signal normalization, routing)  │     (Connectivity & Edge)
                │                                   │
                │  EdgeConnect on host or appliance │
                │  Edge Gateway (Linux appliance)   │
                └───────────────────────────────────┘
                                  │
                                  ▼
                ┌───────────────────────────────────┐
                │  Integration boundary             │
                │                                   │
                │  MQTT broker · OPC UA Server      │
                └───────────────────────────────────┘
                                  │
                                  ▼
                ┌───────────────────────────────────┐
                │  Industrial intelligence layer    │   ← Pillar 5
                │  (analytics, dashboards, alerts)  │     (Operational Intelligence)
                │                                   │
                │  EREMOS V2                        │
                └───────────────────────────────────┘
                                  │
                                  ▼
                       Consumers (Operations,
                       SCADA, Cloud platforms)
```

**Why both views matter:**
- The **pillar view** is how a customer browses the website — they come looking for a capability (predictive maintenance, asset utilization, OEE)
- The **layer view** is how an architect or integrator understands the system — they need to trace signal flow

The website nav uses the pillar view. The architecture diagram uses the layer view. The two are complementary, not competing.

---

## 8. What this means for marketing positioning

| Claim today | Becomes |
|---|---|
| "Two products — EdgeConnect + EREMOS V2" | "Five capability pillars across a vertically integrated ecosystem" |
| "Connect to your controllers" | "Connect to your controllers — OR feed signals directly through Elpis acquisition, asset, or condition-monitoring hardware" |
| Pitch deck slide 4 ("Two products. One platform.") | "Five capabilities. One ecosystem." (Connectivity & Edge / Data Acquisition / Asset Intelligence / Condition Monitoring / Operational Intelligence) |
| Pitch deck slide 7 (Connectivity coverage) | Add a pillar-level overview row above the protocol matrix. |
| Hardware seen as a "catalog item" | Hardware seen as physical realization of capability pillars. Pages are organized by pillar, not by hardware-vs-software. |
| Solution pages | Each solution maps to one or two pillars. The new `/solutions/predictive-maintenance` page anchors both VAS and E-IDOS together. |

---

## 9. Pillar-to-buyer mapping

Different industrial buyers prioritize different pillars. This shapes both the website's by-pillar nav and the cross-cutting by-industry nav.

| Buyer | Primary pillar interest | Secondary |
|---|---|---|
| Plant manager / Ops VP | Operational Intelligence | Connectivity & Edge |
| Maintenance / reliability engineer | Condition Monitoring | Operational Intelligence |
| Industrial IT / CIO / OT manager | Connectivity & Edge | Operational Intelligence |
| OEM machine builder / service team | Asset Intelligence | Operational Intelligence |
| Quality / compliance manager | Operational Intelligence | Data Acquisition |
| Plant engineer (greenfield / retrofit) | Data Acquisition | Connectivity & Edge |

**Implication for homepage:** the hero must speak to multiple personas at once. The persona-specific path is via the pillar nav, not the homepage hero copy.

---

## 10. Open questions (extended from v1)

In addition to the v1 open questions (Phase E):

1. **Stand-alone Analog I/O / Digital I/O / OPC products** — distinct SKUs or unified under mDAQ? (Unchanged from v1.)
2. **Per-product certifications** — CE / UL / FCC / IEC / IP rating. Unchanged.
3. **Manufacturing model** — in-house / white-labeled / hybrid. Unchanged.
4. **Firmware trust posture** — phone-home behavior, signing, offline updates. Unchanged.
5. **Roadmap hardware products** — any in development that should hint into the pillar structure now? *New in v2.*
6. **Does Elpis sell the contamination sensor used by E-IDOS, or partner-source it?** *New in v2.* Affects how we talk about the supply chain — does Elpis ship "the box including the sensor" or "the box, with a partner sensor"?
7. **VAS + E-IDOS — same buyer in real deals?** *New in v2.* If yes, a combined "Reliability Pack" cross-sell is a natural commercial play.

---

## 11. What stays unchanged from v1

- BRAND_TOKENS v1 still canonical
- Voice and tone unchanged
- Existing product names unchanged
- Architectural commitments unchanged
- Customer outcomes unchanged
- Three-layer data-flow worldview still valid (now as supporting architectural lens)

---

*Hardware ecosystem capability-pillar map v2, 2026-05-25. Adds E-IDOS, restructures by capability pillar. Feeds positioning manifesto v2, architecture diagram v2, and homepage v1.*
