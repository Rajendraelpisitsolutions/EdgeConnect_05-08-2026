<!--
File:        docs/marketing/brochure-mdaq-v1.md
Purpose:     Content/copy for the mDAQ field brochure (the asset the
             /resources/brochures page lists as "Request access"). Second of the
             5 hardware brochures; INHERITS the field-sheet template + discipline
             set by brochure-edge-gateway-v1 (the shape-setter).
             This is the COPY (markdown); the print/PDF design is a separate
             step. Condensed VERBATIM from the LOCKED product spec — no new
             facts introduced.
Audience:    Plant engineer (BOM/spec buyer, buyer-taxonomy §2.5; here the
             direct-sensor-acquisition / PLC-bypass case) + the AMC / reseller
             who hands it across a table.
Source-of-truth (do not exceed): page-product-mdaq-spec-v1 (LOCKED v2)
             + hardware-ecosystem-map-v3 §3.1. Specs trace to the map; verify
             before any external print run.
Version:     v2 — LOCKED 2026-06-06 (ChatGPT Pass-1 edit applied).
Date:        2026-06-06
Status:      LOCKED.
v1 -> v2: "Elpis-only path … no third-party hardware" -> "Elpis-controlled
          acquisition path … without requiring a separate PLC, industrial PC,
          or gateway" (avoids an absolute no-third-party claim).

DISCIPLINE (inherited from the Edge Gateway shape-setter):
  - Specs VERBATIM from the locked product spec; no invented numbers
    (proof-architecture §3). "Confirmed during BOM scope" where the spec says so.
  - NO formal certification CLAIMS. IP65/IP67-COMPATIBLE only (never
    "certified"/"rated"/"IP-rated"); cert/IP/site-compliance handled
    case-by-case during BOM scope (§24.B.0).
  - Direct-acquisition honesty: mDAQ reads sensors DIRECTLY with no PLC in the
    loop. It does NOT bridge PLCs (that's the Edge Gateway) and does NOT replace
    PLC control/safety/sequencing/interlock. mDAQ is the acquisition platform
    that VAS runs on (configured for vibration acquisition + analytics).
  - No customer names, no competitor names, no fabricated metrics.
  - Plant-engineer CTA: "Get hardware specifications" / "Request a BOM scope".
-->

# mDAQ — hardware brochure (v2 LOCKED)

**Category:** Data Acquisition — hardware (Pillar 2)
**One-line:** The ruggedized field data-acquisition device that captures industrial sensor signals — pressure, flow, level, temperature — directly from the field, with no PLC in the loop.

---

## What it is

mDAQ is a ruggedized field data-acquisition device. It captures industrial sensor signals — pressure, flow, level, temperature, and more — directly from the field over its analog and digital inputs, with no PLC in the loop, and publishes them over cellular or Wi-Fi. It is the **field-replacement** product: where the Edge Gateway *bridges* an existing PLC fleet, mDAQ *removes the need for a PLC when the job is direct sensor acquisition and publishing*. It is an acquisition device — it does not replace a PLC's control, safety, sequencing, or interlock functions.

It is the **Data Acquisition** pillar. The **VAS** vibration analyser is built on the mDAQ hardware platform, configured for specialized vibration acquisition and analytics.

## What it does

- **Direct sensor acquisition.** Four analog channels (0–10 V or 4–20 mA, 16-bit) capture pressure, flow, level, temperature, and similar signals straight from the field.
- **Digital I/O.** Eight 24 V digital inputs and eight 24 V digital outputs for status, counts, alarms, enables, and simple discrete signals.
- **Acquires and publishes.** Modbus TCP/RTU acquisition where a device speaks it; HTTPS / MQTT publish to your broker or to EREMOS V2.
- **Remote-ready.** 4G, Wi-Fi, GPS, and an optional battery for sites without power or cable.

## What it replaces

One mDAQ removes from the customer BOM:
- a **standalone PLC** for sensor acquisition;
- a separate **cellular modem and edge appliance**;
- a **site-specific battery backup** (available as an mDAQ option).

## Key specifications

| Category | Value |
|---|---|
| **Analog inputs** | 4 channels, 0–10 V or 4–20 mA, 16-bit, up to 860 S/s (per-channel / aggregate behavior + reporting interval confirmed during BOM scope) |
| **Digital I/O** | 8 × 24 V digital inputs · 8 × 24 V digital outputs |
| **Acquisition protocols** | Modbus TCP / Modbus RTU |
| **Publish** | HTTPS · MQTT |
| **Connectivity** | 4G (cellular) · Wi-Fi · GPS · optional Ethernet |
| **Power** | 24 V; **optional battery** for sites without power. Current draw, terminal type, and circuit-protection recommendation confirmed during BOM scope. |
| **Environmental** | −10 °C to +85 °C operating range |
| **Enclosure** | Ruggedized, 180 × 150 × 60 mm |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where a site requires it; final protection level, enclosure approach, and any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification is currently claimed.)* |
| **Mounting** | Field mount; cabinet clearance, cable routing, and antenna placement confirmed during BOM scope |

*Channel counts, ranges, sampling, and environmental figures trace to the Elpis hardware ecosystem map and are confirmed at quoting time. Sensor type, loop power, and signal-conditioning per channel are confirmed during BOM scope.*

## In the field

Sensors wire directly to the analog/digital inputs — no PLC in between; per-channel sensor type, loop power, and signal conditioning are confirmed during BOM scope. Power is 24 V, with an optional battery for sites without mains. 4G cellular covers remote sites, Wi-Fi where available, and GPS provides location/geo-context. Operating range is −10 °C to +85 °C; exposure, humidity, and enclosure approach are confirmed during BOM scope, and IP65 / IP67-compatible configurations can be scoped where the placement requires it (no certified rating claimed). It acquires locally and publishes when connectivity returns. Enclosure dimensions, mounting, cabinet clearance, and antenna/cabling are confirmed during BOM scope.

## Where it fits

Field sensors → **mDAQ** (direct acquisition, no PLC) → HTTPS / MQTT → EREMOS V2 (with EdgeConnect optional in the path). Combined with EdgeConnect and EREMOS V2, mDAQ completes an Elpis-controlled acquisition path from the sensor to the dashboard, without requiring a separate PLC, industrial PC, or gateway. For PLC-fronted floors, the **Edge Gateway** bridges existing PLCs instead; the **VAS** vibration analyser is built on the mDAQ platform — see Condition Monitoring.

## Field-readiness

Built for the field, not the office: ruggedized 180 × 150 × 60 mm enclosure, 24 V power with an optional battery, −10 °C to +85 °C operating range. Remote-ready and offline-capable — 4G + Wi-Fi + GPS for sites without cable; it acquires locally and publishes on reconnect, with no PLC or industrial PC in the loop.

*Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, and certified/rated claims are published only when formal evidence exists for the specific product/configuration.*

## Next step

Bring a sensor list (signal types + ranges), your site connectivity (cellular vs. wired), power availability, and the environment it'll sit in — that's what we scope a BOM against.
**Get hardware specifications · Request a BOM scope** — contact@elpisitsolutions.com

---

*mDAQ brochure **v2 LOCKED 2026-06-06** (ChatGPT Pass-1: "Elpis-only path / no third-party hardware" → "Elpis-controlled acquisition path … without requiring a separate PLC, industrial PC, or gateway") — second of the 5 hardware brochures, inheriting the field-sheet shape from the Edge Gateway brochure. Copy condensed verbatim from the LOCKED `page-product-mdaq-spec-v1` (v2) + `hardware-ecosystem-map-v3 §3.1`; no new facts. No formal cert claims (IP65/IP67-compatible only); direct-acquisition honesty (reads sensors directly, no PLC; does not bridge PLCs or replace PLC control/safety); mDAQ is the platform VAS runs on. ChatGPT Pass 1 applied; v2 LOCKED.*
