<!--
File:     docs/marketing/hardware-ecosystem-map-v1.md
Purpose:  Strategic-level mapping of Elpis's existing industrial hardware
          products to their positions in the unified data path of the
          Industrial Intelligence Ecosystem. Captures product name,
          category, deployment role, and integration with EdgeConnect /
          EREMOS V2 — NOT full technical datasheets.
Scope:    Phase B of the positioning amendment cadence
          (A: manifesto, B: hardware mapping, C: arch diagram v2,
          D: homepage, E: asset realignment + product detail pages).
          This doc supports A and C; full per-product detail pages
          come later in Phase E.
Source:   Existing Elpis hardware brochures (mDAQ, mTracker, VAS,
          Edge Gateway) — see docs/marketing/source-hardware-datasheets/
          (if committed) or local Downloads originals.
Version:  v1
Date:     2026-05-25
Status:   First-pass mapping — strategic positioning only. Detailed
          specifications and per-product positioning copy mature in
          Phase E when full product detail pages are produced.
-->

# Elpis hardware ecosystem — strategic data-path map v1

This document maps Elpis's existing industrial hardware products to their **deployment roles in the unified data path**. It is the source-of-truth artifact that the positioning manifesto, architecture diagram v2, and homepage all descend from.

It deliberately does **not** include full technical datasheets — those mature later in Phase E. The goal here is strategic clarity: what each product *is*, where it sits in the data path, and what it eliminates from a customer's BOM.

---

## 1. The data path, revised

The previous architecture diagram treated the **Factory floor** as a column containing customer-owned controllers (CNCs, PLCs, meters). EdgeConnect was the leftmost Elpis component.

The amended data path inserts Elpis-owned hardware **between raw machine signals and EdgeConnect**:

```
Raw machine signals
        │
        ▼
[ Customer controllers ]   ←  customer-owned (CNCs, PLCs, meters, drives)
        │
        ▼
[ Elpis acquisition hardware ]   ←  NEW LAYER — mDAQ · mTracker · VAS · Edge Gateway
        │
        ▼
[ EdgeConnect ]   ←  edge runtime (software, runs on appropriate host)
        │
        ▼
[ Integration — MQTT broker · OPC UA Server ]
        │
        ▼
[ EREMOS V2 ]   ←  industrial intelligence
        │
        ▼
[ Consumers — Operations · SCADA · Cloud ]
```

The hardware layer is **optional but native**. Customers who already have controllers with usable protocols can still feed EdgeConnect directly. Customers without that — or who want a clean Elpis-end-to-end signal path — buy Elpis acquisition hardware.

This is the strategic differentiator: **Elpis can be the entire data path, or any subset of it**.

---

## 2. The four products, mapped

### 2.1 mDAQ — general-purpose field acquisition

**Category:** ruggedized IoT data acquisition device.

**What it does:** captures industrial sensor signals (pressure, flow, level, temperature, vibration, etc.) directly from the field, with no PLC in the loop.

**Where it sits:** between raw sensors (4–20 mA, 0–10 V, digital state) and the network. Publishes acquired data via MQTT or HTTPS to EREMOS V2 (or any compliant broker) over 4G, Wi-Fi, or Ethernet.

**What it eliminates from a customer BOM:**
- A standalone PLC for sensor acquisition
- A separate cellular modem
- A separate edge appliance
- A site-specific battery backup

**Why it matters strategically:** mDAQ is the **field-replacement** product. It targets sites where the customer has no PLC infrastructure or wants to bypass it entirely — typically remote sites, retrofits, or single-machine deployments. Combined with EdgeConnect + EREMOS V2, mDAQ delivers a complete Elpis-only signal-to-dashboard path with no third-party hardware in the chain.

**Key signal-acquisition properties** (for orientation, not full specs):
- 4 analog channels (0–10 V or 4–20 mA), 16-bit, 860 S/s
- 8 × 24 V digital inputs, 8 × 24 V digital outputs
- Acquisition: Modbus TCP / Modbus RTU. Publish: HTTPS / MQTT
- Communication: 4G (SIM slot), Wi-Fi, GPS, Ethernet (optional)
- Operating range −10 °C to +85 °C, ruggedized enclosure, optional battery
- Form factor: 180 × 150 × 60 mm

---

### 2.2 mTracker — asset utilization telemetry

**Category:** miniature GSM/GPS asset-tracking and OEE telemetry device.

**What it does:** tracks utilization of industrial assets — both fixed and mobile — and reports OEE inputs (production time, downtime, idle time, output quantity) directly from equipment-level digital signals.

**Where it sits:** attached to an asset (machine, vehicle, mobile rig, fixed equipment). Captures equipment running state via digital inputs and reports cellular-direct to EREMOS V2.

**What it eliminates from a customer BOM:**
- Manual production-hour spreadsheets
- Separate GPS / geo-fence trackers
- Service-hours odometers for warranty triggers
- Asset-presence audits

**Why it matters strategically:** mTracker is the **lightweight asset-OEE** product. It targets fleets (machine vendors with deployed equipment at customer sites, multi-site operators with mobile rigs, OEMs running warranty / service-hours billing). Combined with EREMOS V2 dashboards, mTracker delivers running-hours and OEE for assets that don't have PLCs or where a full mDAQ deployment is overkill.

**Strategic adjacencies:**
- Machine builders / OEMs running service-hours billing and warranty programs
- Multi-site operators tracking idle assets across plants
- Asset-rental businesses needing usage-based billing
- Geo-fenced compliance (alerts when an asset leaves an allowed zone)

**Key positioning anchors** (for orientation):
- GSM/GPS 4G, battery backup, geo-fence alerts
- Digital inputs from equipment-level signals
- Compact, designed for retrofit attachment to existing machines

---

### 2.3 VAS — Vibration Analyser System (specialized predictive maintenance)

**Category:** vibration condition-monitoring application built on the mDAQ platform.

**What it does:** detects deviations from normal vibration patterns on rotating machinery, conveyor systems, gearboxes, and structural components. Identifies bearings issues, imbalance, misalignment, looseness, and cracks **before** failure.

**Where it sits:** specialized application of the mDAQ hardware paired with vibration-specific sensors (displacement, velocity, acceleration) and analytics (time-domain + frequency-domain). Outputs feed EREMOS V2 as a specialized data stream alongside general OEE telemetry.

**What it eliminates from a customer BOM:**
- A dedicated vibration analyser console
- A separate condition-monitoring software stack
- Manual handheld vibration spot-checks
- A third-party predictive-maintenance vendor

**Why it matters strategically:** VAS is the **predictive-maintenance** product. It's not a separate hardware SKU — it's the same mDAQ platform configured for vibration acquisition with vibration-specific analytics on the EREMOS side. The strategic message: *one hardware platform, multiple acquisition specialties, same intelligence stack*.

**Analytical capabilities** (for orientation):
- Time-domain analysis (peak detection, RMS severity)
- Frequency-domain analysis (FFT, spectrum)
- Order analysis, Bode plot, polar plot, cascade, waterfall
- Maps frequency peaks to failure modes (bearing, gear, structural)

**Strategic anchor:** *condition monitoring without a separate condition-monitoring vendor*. The platform that runs your OEE also runs your vibration program.

---

### 2.4 Edge Gateway — industrial PLC-to-cloud gateway

**Category:** ruggedized industrial gateway appliance running embedded Linux.

**What it does:** bridges existing PLC fleets (Modbus TCP, Ethernet/IP) to the network. Web-configurable. Acts as the deployment vehicle for the EdgeConnect runtime in environments where a Windows host isn't appropriate (e.g. control cabinets, harsh environments, embedded deployments).

**Where it sits:** between PLCs (or other industrial controllers) and the network. Hosts the EdgeConnect runtime when EdgeConnect is deployed as an appliance rather than as software-on-customer-host.

**What it eliminates from a customer BOM:**
- A separate Industrial PC for edge software
- A separate Linux box for protocol gateway functions
- The need to host EdgeConnect on a customer-owned Windows server
- A separate cellular modem for remote deployments

**Why it matters strategically:** Edge Gateway is the **packaged deployment vehicle** for EdgeConnect. It transforms EdgeConnect from "a Windows service the customer hosts" into "a ruggedized appliance you mount in the cabinet." This dramatically broadens the addressable market — IT-light customers, OEM machine builders, and remote-site deployments can buy a single device instead of provisioning a host.

**Strategic dual identity:**
- **Standalone:** PLC-to-cloud gateway with built-in MODBUS TCP and cellular publish
- **EdgeConnect host:** when EdgeConnect Linux support ships (roadmap), the Edge Gateway becomes the canonical EdgeConnect appliance

**Key positioning anchors** (for orientation):
- Embedded Linux OS, web-configurable, USB firmware updates
- Modbus TCP server/client, 4G/Wi-Fi/Ethernet, 24 V DC
- 200 × 150 × 75 mm rugged enclosure

---

## 3. How the four products relate

```
                ┌─── general field acquisition ────────┐
                │                                       │
                │   mDAQ  ─────────┐                    │
                │   (analog/digital                     │
                │    direct-from-sensor)                │
                │                  │                    │
                │   VAS  ─ uses mDAQ + vibration sensors│
                │   (predictive maintenance specialty)  │
                │                  │                    │
                └──────────────────┼────────────────────┘
                                   │
                ┌─── asset-level telemetry ────────┐    │
                │   mTracker                        │    │
                │   (GSM/GPS, asset OEE)            │    │
                └───────────────────────────────────┘    │
                                                          │
                ┌─── PLC-side gateway ─────────────┐      │
                │   Edge Gateway                    │      │
                │   (Linux appliance, PLC bridge,   │      │
                │    future EdgeConnect host)       │      │
                └───────────────────────────────────┘      │
                                                          ▼
                                          [ EdgeConnect (edge runtime) ]
                                                          │
                                                          ▼
                                          [ MQTT broker · OPC UA Server ]
                                                          │
                                                          ▼
                                                  [ EREMOS V2 ]
                                                          │
                                                          ▼
                                          [ Operations · SCADA · Cloud ]
```

**Two complementary acquisition patterns:**
- **Sensor-direct** (mDAQ, VAS, mTracker) — for sites without a PLC, or where the customer wants to bypass the controller layer
- **PLC-bridge** (Edge Gateway) — for sites with existing PLC infrastructure that need to be modernized in place

Both feed the same intelligence stack. The choice is operational, not architectural.

---

## 4. What this means for marketing positioning

| Asset / claim today | Implication of this map |
|---|---|
| "Industrial Intelligence Platform — EdgeConnect + EREMOS V2" | Becomes "Industrial Intelligence Ecosystem — Field acquisition (mDAQ, mTracker, VAS, Edge Gateway) + Edge runtime (EdgeConnect) + Intelligence (EREMOS V2)." |
| "Connect CNCs, Modbus PLCs, and instrumentation" (datasheet hero) | Add: "…or feed signals directly through Elpis acquisition hardware." |
| Pitch deck slide 4 ("Two products. One platform.") | Becomes "Three layers. One ecosystem." (Hardware + Edge + Intelligence) |
| Pitch deck slide 7 (Connectivity coverage) | Add an Elpis-hardware row above the southbound protocols. |
| Architecture diagram (4 columns) | Adds a fifth-layer column or augments the "Factory floor" column with Elpis acquisition devices. |
| Solution pages — brownfield modernization | Hardware is now an explicit option: "If your existing PLC speaks Modbus, EdgeConnect connects directly. If not, mDAQ replaces the missing acquisition layer." |
| Solution pages — OEM machine monitoring | mTracker becomes a first-class proof point for service-hours / warranty / fleet visibility. |
| Connectivity matrix | Distinguish *protocols Elpis supports on the network side* (FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, OPC UA) from *signal types Elpis acquisition hardware accepts directly* (4–20 mA, 0–10 V, 24 V digital, vibration). |

---

## 5. What stays unchanged

- **Brand visual identity** — Elpis hardware brochures already use the same dark-navy + teal + clean-industrial typography we've been building in BRAND_TOKENS v1. No realignment needed.
- **EREMOS V2 positioning** — unchanged. EREMOS V2 is the intelligence layer regardless of which acquisition hardware feeds it.
- **EdgeConnect runtime positioning** — unchanged at the runtime level. The product is the same software; the *deployment story* gets richer (Linux on Edge Gateway, in addition to Windows hosts).
- **Three-way diagnostics, store-and-forward, signed offline licensing, audit-defensible OEE** — all unchanged. These are the platform's deep architectural commitments.
- **Customer outcomes** — unchanged. OEE trust, unplanned downtime reduction, fleet visibility, modernization, audit defensibility, offline operation. The hardware story expands *how* these outcomes are delivered; it doesn't change *what* is delivered.

---

## 6. What needs to mature in Phase E (deferred)

Each of these will eventually become its own per-product detail page:

- **mDAQ** — full datasheet page (channels, accuracy, certifications when known, application examples, sensor compatibility matrix)
- **mTracker** — full datasheet page (cellular regions, geo-fence configuration, integration with EREMOS V2 asset-tracking dashboards)
- **VAS** — full application page (sensor mounting guidance, supported vibration analyses, failure-mode catalog, demo signatures)
- **Edge Gateway** — full appliance datasheet (specs, certifications, EdgeConnect host configuration, supported southbound protocols)

These pages don't block the homepage. They mature as real customer-facing technical documentation in a separate session.

---

## 7. Open questions for Elpis

To complete the strategic narrative, the following are worth confirming before Phase D (homepage) lands:

1. **Are there additional hardware products beyond these four?** The original positioning amendment mentioned "Analog I/O, Digital I/O, vibration acquisition, mDAQ, mTracker, etc." — VAS covers vibration, mDAQ covers analog + digital I/O. Are there stand-alone analog-I/O or digital-I/O SKUs distinct from mDAQ, or is mDAQ the unified product?
2. **Maturity status** — all four products read as shipping (the brochures use present tense, list specs, and ship with the existing E-REMOS connection). Confirm none of these are in-development.
3. **Manufacturing model** — designed and built in-house, white-labeled, or a mix? Affects how we talk about the supply chain and certifications.
4. **Certification status per product** — CE / UL / IEC / RoHS / industrial-environment certifications are key trust signals for hardware buyers. The brochures don't list these explicitly. Worth knowing before any external-facing claim.
5. **Roadmap** — additional hardware products planned (e.g. higher-channel-count mDAQ, isolated-channel variants, intrinsically-safe variants)? Affects roadmap slide on deck v6 + roadmap page on the homepage.

None of these block the manifesto. They mature alongside the per-product detail pages in Phase E.

---

*Hardware ecosystem map — v1, 2026-05-25. Strategic positioning only. Feeds Phase A (positioning manifesto), Phase C (architecture diagram v2), Phase D (homepage). Detailed per-product datasheets defer to Phase E.*
