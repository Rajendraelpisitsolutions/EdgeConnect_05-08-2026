<!--
File:        docs/marketing/elpis-industrial-intelligence-platform-v1.md
Purpose:     Long-form joint datasheet for the Elpis Industrial Intelligence Platform
             (EdgeConnect + EREMOS V2). Plant manager / Ops VP audience.
Format:      Long-form web one-pager. A trimmed print PDF will derive from this.
Version:     v1 (draft)
Date:        2026-05-24

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

**Connect every machine. Measure every shift. Run a better plant.**

The Elpis Industrial Intelligence Platform pairs **EdgeConnect** — a protocol-agnostic edge runtime that collects data from any controller on your factory floor — with **EREMOS V2** — the multi-tenant analytics platform that turns that data into OEE, alarms, incident workflows, and operational reports. One platform, from the spindle to the dashboard, built for the people who run production.

---

## Built for the people who run the plant

Plant managers and operations leaders don't have a data problem. They have a **decision** problem. The data is already on the shop floor — locked inside Fanuc controllers, Brother CNCs, Siemens PLCs, and energy meters that each speak a different language. The cost is invisible: an hour of unplanned downtime here, a missing alarm there, an OEE number that nobody trusts because it's stitched together from three spreadsheets.

The Elpis Industrial Intelligence Platform closes that gap. It connects to every controller you own, normalizes the data once, and delivers real-time operational intelligence to the teams who can act on it. Without ripping out hardware. Without rebuilding your OT. Without sending sensitive plant data to a cloud you didn't choose.

---

## Outcomes you can hold us to

- **Cut unplanned downtime** by surfacing machine state changes the moment they happen, with persistent alarm tracking and incident workflows that close the loop from detection to resolution.
- **Trust your OEE number** because every input — cycle time, parts count, alarm state, planned stops — is collected directly from the controller and timestamped at the edge.
- **Modernize legacy controllers** — Fanuc 16i/18i, Brother S700Xd1, Modbus-fronted PLCs — without replacing them. EdgeConnect speaks their native protocols.
- **See your whole fleet** — multiple plants, multiple shifts, multiple vendors — on one operational view, with per-site identity and tenant isolation built into the platform.
- **Keep sensitive data where it belongs.** EdgeConnect is fully offline-capable. Send only what matters to the cloud, on your terms.
- **Pass your audit.** Hash-chained configuration history, per-tag quality codes, and signed offline licensing satisfy regulated-industry review without retrofitting.

The numbers you'll plug into a business case are yours. The platform that makes those numbers credible is ours.

---

## How the platform is built

Two products, one architecture, one MQTT contract between them.

### Edge connectivity — EdgeConnect

EdgeConnect runs as a service on the factory floor — typically a small Windows server next to the network switch in the control cabinet. It polls or subscribes to your controllers, normalizes every reading into a canonical data point, and routes it to one or more downstream systems.

What that means in practice:

- **Native support for the protocols your controllers actually speak.** FOCAS2 for Fanuc CNCs (axes, spindle, alarms, tool, production, programs). MT-LINKi for Fanuc's REST-based diagnostics. MTConnect for the industry-standard CNC streaming protocol. Brother HTTP for Brother CNCs via their built-in web-monitoring interface. Modbus TCP for PLCs, drives, and energy meters. OPC UA Server for everything that needs to consume EdgeConnect data over the standard industrial bus.
- **Store-and-forward built in, not bolted on.** Every route persists its data to local SQLite with per-sink cursors. When the MQTT broker, the cloud, or the corporate network goes down, EdgeConnect buffers locally and replays in order on reconnect. No lost cycles, no lost parts counts, no apologetic emails to the operations team.
- **Per-adapter isolation.** A failing FOCAS2 connection cannot affect a healthy Modbus connection. A misbehaving sink cannot block another sink. This is an architectural lock, verified by tests, not a runtime hope.
- **Three-way diagnostics.** Source, pipeline, sink — operators see exactly where the data flow broke. No more "the data's not arriving and we don't know why."
- **Connectivity Studio.** A modern web admin UI for adding sources, defining routes, configuring sinks, and running Test Connection probes before anything goes live. Default at `http://127.0.0.1:5080` on the edge node itself.
- **Configuration discipline.** Draft → validate → apply → rollback. No untested config ever reaches the data path. Optimistic concurrency prevents two operators from silently overwriting each other.

EdgeConnect runs today on Windows. Linux support is on the roadmap.

### Operational intelligence — EREMOS V2

EREMOS V2 is the multi-tenant analytics and visualization platform that consumes the data EdgeConnect produces. It is the layer where plant data becomes operational decisions.

What that means in practice:

- **A real industrial asset model.** Hierarchical equipment tree from PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT. First-class Device and Tag entities with units, engineering ranges, and quality codes. The data structure mirrors how operations teams already think about the plant.
- **OEE that holds up.** EREMOS V2 computes Overall Equipment Effectiveness as time-bounded Segments — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP — with availability, performance, and quality factored independently. The inputs come from the controller. The math is auditable.
- **Persistent alarms and incident workflows.** Inbound CNC alarms become tracked Alarm records with open/close state and incident grouping. The shift handover becomes a record, not a phone call.
- **Configurable alerting.** Notifications routed to the channels your operations team already uses — email, chat, ticketing, webhook.
- **Reporting that the operations team will actually read.** Shift reports, OEE summaries, downtime breakdowns, tool-life trends. PDF and Excel export for the people who still want them on paper.
- **Tool life tracking.** A dedicated ingestion path for tool wear and remaining-life telemetry, so maintenance can be scheduled before a tool fails mid-cycle.
- **Multi-tenant by design.** Each plant or business unit is a tenant with isolated data. The same EREMOS V2 deployment serves many sites without leakage between them.
- **Dashboards that scale to mixed fleets.** Panes split automatically by device class — CNCs, PLCs, DAQs, asset trackers, meters — so a multi-vendor plant doesn't force a multi-tool workflow.

---

## Connectivity coverage

| Direction | Protocol | Status | What it covers |
|---|---|---|---|
| Southbound (source) | **FOCAS2** | Available | Fanuc CNCs — axes, spindle, alarms, tool, production, programs |
| Southbound (source) | **MT-LINKi** | Available | Fanuc's REST-based machine-data product |
| Southbound (source) | **MTConnect** | Available | The industry-standard CNC streaming protocol |
| Southbound (source) | **Brother HTTP** | Available | Brother CNCs (S700Xd1 and similar) via built-in web monitoring |
| Southbound (source) | **Modbus TCP** | Available | PLCs, drives, energy meters — any Modbus TCP device |
| Northbound (sink) | **MQTT** | Available | Batch or per-tag, EREMOS V2-native or any standard broker |
| Northbound (sink) | **OPC UA Server** | Available | SCADA, MES, HMI, and OPC UA client integrations |

Every source delivers tags using a shared **canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions, and so on. The same dashboard layout works across Fanuc, Brother, and Modbus-fronted machines. One semantics, many vendors.

---

## How it deploys

- **EdgeConnect at the edge.** One service per plant or per cell, sized to the controller count. Runs offline; no cloud dependency.
- **EREMOS V2 in your data center, in a private cloud, or as a managed service.** Multi-tenant by design — one deployment, many sites.
- **Connected by MQTT.** Standard industrial messaging. Works with Mosquitto, HiveMQ, EMQX, AWS IoT Core, Azure IoT Hub, or any compliant broker. The platform does not require Elpis to provide the broker.
- **Fleet-shaped.** Multi-plant deployments run an EdgeConnect at each site and aggregate to a single EREMOS V2 tenant. Per-gateway UUID and customer/site binding give the fleet a clean identity model.

---

## Where customers use it

- **Multi-vendor CNC floors.** Twenty to one hundred CNCs across Fanuc, Brother, and Mazak controllers, on one operational view, without per-machine custom scripting.
- **Brownfield modernization.** Fifteen-year-old Fanuc 16i/18i controllers brought into a modern analytics stack via native FOCAS2 polling. The controllers stay. The data layer modernizes.
- **OEE and production tracking.** Cycle time, parts count, alarm state, tool wear streaming into a real-time operational view — every input collected directly from the controller, every Segment built from authoritative data.
- **Multi-site fleets.** Ten-plus plants, each running EdgeConnect locally and reporting into a single EREMOS V2 tenant. Outages at any site buffer locally and replay on reconnect.
- **Hybrid edge plus cloud.** Sensitive plant data stays on premise. Filtered, aggregated KPIs flow to the cloud platform of your choice. Pay-per-egress savings are real.
- **Compliance and audit trails.** Hash-chained configuration history, per-tag quality codes, signed offline licensing — built for regulated-industry review, not bolted on afterward.

---

## Why Elpis

- **A protocol-agnostic core, not a gateway with bolted-on protocols.** EdgeConnect's runtime never references a specific protocol. Adapters plug in; the core stays clean. This is the architecture that lets us add new controllers without destabilizing the ones already in production.
- **Edge-first, not cloud-first.** EdgeConnect is built to run for years on a small box in a control cabinet. Store-and-forward is mandatory. Offline operation is the default, not a fallback.
- **Three-way diagnostics, always.** Source, pipeline, sink — operators always know where the data flow broke. No silent failures, no five-hour root-cause hunts.
- **Offline, signed licensing.** RSA-signed JSON license files, fully offline. No phone-home to validate, no cloud dependency to license your edge runtime. Air-gapped factories are first-class.
- **License expiration never cuts customer data.** A lapsed license blocks configuration changes; it never stops the flow of production data. Your machines keep talking.
- **AI as decision support, never in the data path.** When AI features ship, they propose actions for humans to confirm — they do not silently alter the pipeline. Local-LLM support is mandatory; cloud LLMs are optional. No "secrets to a foundation model" anxiety on security review.
- **Per-protocol licensing.** Pay for the connectivity you actually use. The platform packaging is per-edition; the underlying capability is modular.
- **Built for industrial workloads.** This is not generic IoT software with a CNC marketing skin. The architecture, the vocabulary, the diagnostics, and the licensing are all shaped by industrial operations practice.

---

## Editions and modules

The Elpis Industrial Intelligence Platform is available in **Starter**, **Professional**, and **Enterprise** editions, with optional industrial connectivity modules including FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, MQTT, and OPC UA Server.

Contact Elpis for licensing details, edition feature lists, and deployment-scale pricing tailored to your fleet.

---

## On the roadmap

Capabilities currently in active engineering or scheduled for upcoming releases:

- **OPC UA Client** (southbound) — connect to OPC UA-native controllers and gateways
- **Siemens S7** (southbound) — native S7 driver for Siemens PLC fleets
- **HTTP and TCP sinks** (northbound) — direct delivery to REST endpoints and legacy TCP listeners
- **Linux host support** for EdgeConnect
- **AI-assisted operations agents** — Diagnostic, Configuration, Tag Mapping, and Intelligent Alerting — all decision-support, all human-confirmed, all local-LLM-capable

The roadmap exists in service of the platform's locked architectural principles, not the other way around. We add what fits.

---

## Next step

Bring us a representative plant — a controller mix, a target broker, an OEE definition — and we will scope a proof of value against it. Demos run on real protocols against your real signals, not on canned data.

**Contact:** [contact details — to be filled in by the user]

---

*Elpis Industrial Intelligence Platform — v1, 2026-05-24. EdgeConnect and EREMOS V2 are products of Elpis IT Solutions. Specifications and roadmap items are subject to change; contact us for the current authoritative product status.*
