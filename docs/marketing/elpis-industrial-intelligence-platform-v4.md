<!--
File:        docs/marketing/elpis-industrial-intelligence-platform-v4.md
Purpose:     Long-form joint datasheet for the Elpis Industrial Intelligence Platform
             (EdgeConnect + EREMOS V2). Plant manager / Ops VP audience.
Format:      Long-form web one-pager. A trimmed print PDF will derive from this.
Version:     v4 (consistency-alignment pass with website messaging architecture v2)
Date:        2026-05-24

Changes from v3 (single, minimal alignment edit — no narrative changes):
  - "Designed for" vertical renamed: "Automotive parts and precision machining
    operations" → "Precision manufacturing operations" to match the website
    solution-page naming. Broader category, less narrow specialization claim
    (per ChatGPT review of website messaging v1).

All other content identical to v3 — voice, structure, claims, Mermaid diagram,
placeholder contact line. v3 is preserved as the version that captured the
ChatGPT-reviewed cadence; v4 is the canonical alignment with the website.

Locked-truth sources every claim traces to:
  - CLAUDE.md §1, §3 (EdgeConnect: product definition + locked architectural decisions)
  - docs/ARCHITECTURE_BLUEPRINT.md Appendix A
  - docs/platform-principles.md (P1–P6)
  - shared-knowledge/architecture-overview.md
  - shared-knowledge/common-modules.md
  - shared-knowledge/glossary.md
  - shared-knowledge/contracts/eremos-per-tag-mqtt.md
  - shared-knowledge/contracts/cnc-vocabulary.md
  - shared-knowledge/contracts/opcua-namespace-policy.md
  - User-confirmed EREMOS V2 features (incident workflow, alerting, reporting)
  - User-confirmed Northbound: OPC UA Server treated as shipped alongside MQTT

Do NOT add claims to this document without a matching entry in the sources above.
-->

# Elpis Industrial Intelligence Platform

**Unified industrial connectivity and operational intelligence for modern manufacturing.**

Connect CNCs, Modbus PLCs, and instrumentation into one real-time operational platform. Measure OEE on signals collected directly from the controller. Reduce downtime with persistent alarms and incident workflows. From the spindle to the dashboard, on one foundation.

---

## Designed for

- **Multi-vendor CNC manufacturing plants** running mixed Fanuc, Brother, Mazak and other controllers
- **Precision manufacturing operations** with strict OEE accountability
- **Brownfield modernization projects** bringing legacy controllers into a modern analytics stack
- **OEM machine monitoring deployments** for builders shipping connected equipment
- **Multi-site industrial operations teams** standardizing the way every plant reports

---

## Outcomes you can hold us to

- **Cut unplanned downtime.** Surface machine state changes the moment they happen, with persistent alarm tracking and incident workflows that close the loop from detection to resolution.
- **Trust your OEE number.** Every input — cycle time, parts count, alarm state, planned stops — is collected directly from the controller and timestamped at the edge.
- **Modernize legacy controllers.** Fanuc 16i/18i, Brother S700Xd1, Modbus-fronted PLCs — EdgeConnect speaks their native protocols. No replacements required.
- **See your whole fleet.** Multiple plants, multiple shifts, multiple vendors on one operational view, with per-site identity built in.
- **Keep sensitive data where it belongs.** EdgeConnect is fully offline-capable. Send only what matters to the cloud, on your terms.
- **Pass your audit.** Hash-chained configuration history, per-tag quality codes, and signed offline licensing satisfy regulated-industry review without retrofitting.

---

## Replace spreadsheet operations

Most plants already have the data. What they lack is a system that produces:

- **Trusted timestamps** — every reading collected at the edge, not transcribed from a clipboard
- **Auditable OEE** — Segment-based math you can show an auditor
- **Persistent alarm history** — every fault on the record, not in someone's memory
- **Unified machine visibility** — one operational view across CNCs, PLCs, and meters
- **Centralized operational workflows** — shift reports as a record, not a phone call

The Elpis platform replaces disconnected spreadsheets and manual shift reporting with a real-time operational system built directly on machine data.

---

## How the platform is built

### Edge connectivity — EdgeConnect

A protocol-agnostic edge runtime that collects from every controller on your floor, normalizes the data once, and delivers it where it needs to go.

- **Native protocol coverage.** FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP. One service speaks them all.
- **No lost production data.** Built-in edge buffering preserves data during network or broker outages and replays it on reconnect.
- **Faults isolated, not contagious.** A failing protocol cannot affect a healthy one. A misbehaving sink cannot block another sink.
- **Three-way diagnostics.** Source, pipeline, sink — operators always see where the data flow broke.
- **Connectivity Studio.** A web admin UI for sources, routes, sinks, and Test Connection probes before anything goes live.
- **Safe configuration.** Draft → validate → apply → rollback. Untested config never reaches the data path.
- **Auditable changes.** Hash-chained configuration history records every change with actor and timestamp.

EdgeConnect runs on Windows today; Linux on the roadmap.

### Operational intelligence — EREMOS V2

A multi-tenant analytics platform that turns machine data into operational decisions.

- **A real industrial asset model.** PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT, with first-class Devices and Tags, units, engineering ranges, and quality codes.
- **OEE you can audit.** Availability × Performance × Quality, computed from time-bounded Segments built on edge-collected signals.
- **Alarms and incidents tracked.** Inbound CNC alarms become persistent records with open/close state and incident grouping.
- **Alerts on your channels.** Notifications routed to email, chat, and ticketing webhooks your operations team already uses.
- **Reports the team will actually read.** Shift reports, OEE summaries, downtime breakdowns, tool-life trends — PDF and Excel export included.
- **Tool life tracking.** Dedicated ingestion for tool wear and remaining-life telemetry, so maintenance happens before failures.
- **Multi-tenant by design.** One deployment serves many sites or business units without data leakage.
- **Dashboards that scale to mixed fleets.** Panes split automatically by device class — CNC, PLC, DAQ, asset tracker, meter.

---

## Connectivity coverage

### CNC controllers (southbound)

| Protocol | Status | What it covers |
|---|---|---|
| **FOCAS2** | Available | Fanuc CNCs — axes, spindle, alarms, tool, production, programs |
| **MT-LINKi** | Available | Fanuc's REST-based machine-data product |
| **MTConnect** | Available | The industry-standard CNC streaming protocol |
| **Brother HTTP** | Available | Brother CNCs (S700Xd1 and similar) via the built-in web-monitoring interface |

### PLC and instrumentation (southbound)

| Protocol | Status | What it covers |
|---|---|---|
| **Modbus TCP** | Available | PLCs, drives, energy meters — any Modbus TCP device |

### Messaging (northbound)

| Protocol | Status | What it covers |
|---|---|---|
| **MQTT** | Available | Mosquitto, HiveMQ, EMQX, AWS IoT Core, Azure IoT Hub — any compliant broker. Batch or per-tag publish modes. |

### Enterprise integration (northbound)

| Protocol | Status | What it covers |
|---|---|---|
| **OPC UA Server** | Available | SCADA, MES, HMI, and any OPC UA client. ISA-95-style browse paths configurable per deployment. |

Every source delivers tags using a shared **canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions. The same dashboard layout works across Fanuc, Brother, and Modbus-fronted machines. One semantics, many vendors.

---

## Architecture at a glance

<!--
DESIGNER NOTE: Replace this Mermaid block with a branded SVG diagram before
print PDF and website publication. The Mermaid version below is structurally
correct and renders on GitHub / most markdown viewers, but a hand-drawn
diagram in the Elpis visual identity (dark premium palette, steel grey,
deep navy) will read significantly better at sales-asset quality. See
docs/marketing/architecture-diagram-spec-v2.md for the full designer brief.
-->

```mermaid
flowchart LR
    subgraph Edge["Factory floor (per plant)"]
        direction TB
        Controllers["CNCs · Modbus PLCs · Meters<br/>FOCAS2 · MT-LINKi · MTConnect<br/>Brother HTTP · Modbus TCP"]
        EC["EdgeConnect<br/>Edge runtime"]
        Controllers --> EC
    end

    subgraph Integration["Integration layer"]
        direction TB
        MQTT[("MQTT broker")]
        OPCUA["OPC UA Server"]
    end

    subgraph Intelligence["Intelligence layer"]
        EREMOS["EREMOS V2<br/>Multi-tenant analytics<br/>OEE · Alarms · Incidents · Reports"]
    end

    subgraph Consumers["Consumers"]
        direction TB
        OPS["Operations team<br/>Dashboards · Alerts · Reports"]
        SCADA["SCADA / MES / HMI<br/>OPC UA clients"]
        Cloud["Cloud platforms<br/>AWS · Azure · custom"]
    end

    EC --> MQTT
    EC --> OPCUA
    MQTT --> EREMOS
    EREMOS --> OPS
    OPCUA --> SCADA
    MQTT --> Cloud
```

*One EdgeConnect deploys at each plant. One EREMOS V2 tenant aggregates many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*

---

## Deploy incrementally

Start with one machine, one line, or one plant. EdgeConnect runs side-by-side with whatever else you have today. EREMOS V2 onboards new sites without changing the platform underneath. No big-bang cutover, no platform-wide upgrade that breaks the plants already running.

Typical proof-of-value deployments begin with a single line or machine cell and expand incrementally once operationally validated. The architecture scales by addition, not by replacement.

---

## How it deploys

- **EdgeConnect at the edge.** One service per plant or per cell, sized to the controller count. Runs offline; no cloud dependency.
- **EREMOS V2 in your data center, a private cloud, or as a managed service.** Multi-tenant by design — one deployment, many sites.
- **Connected by standard MQTT.** Works with any compliant broker. The platform does not require Elpis to provide the broker.
- **Fleet-shaped.** Multi-plant deployments run an EdgeConnect at each site and aggregate to a single EREMOS V2 tenant. Per-gateway UUID and customer/site binding give the fleet a clean identity model.

---

## Where customers use it

- **Multi-vendor CNC floors.** Twenty to a hundred CNCs across Fanuc, Brother, and Mazak controllers on one operational view, without per-machine custom scripting.
- **Brownfield modernization.** Fifteen-year-old Fanuc 16i/18i controllers brought into a modern analytics stack via native FOCAS2 polling. The controllers stay; the data layer modernizes.
- **OEE and production tracking.** Cycle time, parts count, alarm state, tool wear streaming into a real-time operational view — every input collected directly from the controller.
- **Multi-site fleets.** Ten-plus plants, each running EdgeConnect locally and reporting into a single EREMOS V2 tenant. Outages buffer locally and replay on reconnect.
- **Hybrid edge plus cloud.** Sensitive plant data stays on premise. Filtered, aggregated KPIs flow to the cloud platform of your choice. Pay-per-egress savings are real.
- **Compliance and audit trails.** Hash-chained configuration history, per-tag quality codes, signed offline licensing — built for regulated-industry review, not bolted on afterward.

---

## Why Elpis

- **New protocols ship without breaking the old ones.** EdgeConnect's runtime is protocol-agnostic by architecture, not by accident — adapters plug in; the core stays clean.
- **Built to run for years on a small box in a control cabinet.** Edge-first, not cloud-first. Store-and-forward is mandatory. Offline operation is the default.
- **Operators always know where the data flow broke.** Three-way diagnostics — source, pipeline, sink — by design. No silent failures.
- **Air-gapped factories are first-class.** RSA-signed JSON license files, fully offline. No phone-home to validate the runtime.
- **A lapsed license never stops production data.** Expiration blocks configuration changes only. Your machines keep talking.
- **AI proposes; humans decide.** When AI features ship, they propose actions for humans to confirm — they do not silently alter the data path. Local-LLM support is mandatory; cloud LLMs are optional.
- **Pay for the connectivity you actually use.** Per-edition packaging with modular per-protocol activation.
- **Built for industrial workloads, not adapted IoT software.** The architecture, vocabulary, diagnostics, and licensing are shaped by OT operations practice.

---

## Typical value areas

Where customers typically anchor their business case for the platform:

- **Reduced unplanned downtime** — through faster detection and persistent alarm tracking
- **Faster root-cause analysis** — three-way diagnostics on the data path, hash-chained audit on configuration
- **Improved OEE visibility** — single source of truth across the fleet
- **Reduced manual reporting effort** — automated shift reports replace spreadsheet stitching
- **Planned maintenance from tool-life trends** — schedule changeovers before tools fail mid-cycle
- **Standardized multi-site operations** — one platform, many sites, consistent KPIs

The numbers you plug into a business case are yours. We will help you build a model from your real production constants.

---

## Editions and modules

The Elpis Industrial Intelligence Platform is available in **Starter**, **Professional**, and **Enterprise** editions, with optional industrial connectivity modules including FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, MQTT, and OPC UA Server.

Contact Elpis for licensing details, edition feature lists, and deployment-scale pricing tailored to your fleet.

---

## On the roadmap

Capabilities in active engineering or scheduled for upcoming releases:

- **OPC UA Client** (southbound) — connect to OPC UA-native controllers and gateways
- **Siemens S7** (southbound) — native S7 driver for Siemens PLC fleets
- **HTTP and TCP sinks** (northbound) — direct delivery to REST endpoints and legacy TCP listeners
- **Linux host support** for EdgeConnect
- **AI-assisted operations agents** — Diagnostic, Configuration, Tag Mapping, and Intelligent Alerting — all decision-support, all human-confirmed, all local-LLM-capable

---

## Next step

Bring us a representative plant — a controller mix, a target broker, an OEE definition — and we will scope a proof of value against it. Demos run on real protocols against your real signals, not on canned data.

**Contact:** [contact details — to be filled in by the user]

---

*Elpis Industrial Intelligence Platform — v4, 2026-05-24. EdgeConnect and EREMOS V2 are products of Elpis IT Solutions. Specifications and roadmap items are subject to change; contact us for the current authoritative product status.*
