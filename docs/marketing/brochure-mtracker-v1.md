<!--
File:        docs/marketing/brochure-mtracker-v1.md
Purpose:     Content/copy for the mTracker field brochure (one of the 5 hardware
             brochures; inherits the field-sheet shape from
             brochure-edge-gateway-v1). This is the COPY (markdown); the
             print/PDF design is a separate step. Condensed VERBATIM from the
             LOCKED product spec — no new facts introduced.
Audience:    Plant engineer (BOM/spec buyer, buyer-taxonomy §2.5, primary) + the
             OEM machine builder (§2.6, strong secondary) + the AMC / reseller
             who hands it across a table.
Source-of-truth (do not exceed): page-product-mtracker-spec-v1 (LOCKED v2)
             + hardware-ecosystem-map-v3 §4.1. Specs trace to the map; verify
             before any external print run.
Version:     v2 — LOCKED 2026-06-06 (ChatGPT Pass-1 cleanup applied).
Date:        2026-06-06
Status:      LOCKED.
v1 -> v2: "location and compliance" -> "location and site-policy tracking".

DISCIPLINE (inherited from brochure-edge-gateway-v1, the shape-setter):
  - Specs VERBATIM from the locked product spec; no invented numbers
    (proof-architecture §3). "Confirmed during BOM scope" where the spec says so.
  - NO formal certification CLAIMS. IP65/IP67-COMPATIBLE only (never
    "certified"/"rated"/"IP-rated"); cert/IP/site-compliance handled
    case-by-case during BOM scope (§24.B.0).
  - OEE honesty: mTracker reports OEE *inputs* from equipment-level digital
    signals + location; EREMOS V2 computes the OEE. Never claim mTracker
    "computes OEE." Not an analog DAQ (that's mDAQ).
  - Customer-controlled routing: the customer decides which signals route to an
    OEM / AMC (service-hours, warranty triggers, multi-site).
  - No customer names, no competitor names, no fabricated metrics.
  - Plant-engineer CTA: "Get hardware specifications" / "Request a BOM scope".
-->

# mTracker — hardware brochure (v2 LOCKED)

**Category:** Asset Intelligence — hardware (Pillar 3)
**One-line:** The miniature GSM/GPS device that puts utilization and OEE inputs on the assets that don't report them today — fixed or mobile.

---

## What it is

mTracker is a miniature GSM/GPS asset-tracking and telemetry device. It reads **equipment-level digital signals** — run / stop, cycle, output counts — and reports **OEE *inputs*** (production time, downtime, idle time, output quantity) along with location, over cellular, with battery backup. It is designed for **retrofit attachment** to fixed or mobile assets.

It is the **Asset Intelligence** pillar. mTracker provides the **inputs**; **EREMOS V2 computes the OEE** and the utilization analytics. For direct analog *sensor* acquisition — pressure, flow, temperature — see **mDAQ**; mTracker is asset tracking + digital-signal telemetry, not an analog DAQ.

## What it does

- **Reads equipment-level signals.** Digital inputs capture run / stop, cycle, and output-count signals straight from the asset — no manual logs.
- **Tracks location + utilization.** GSM/GPS reports where an asset is and whether it's running, idle, or stopped — fixed plants or mobile fleets.
- **Geo-fence alerts.** Boundary entry/exit alerts for location and site-policy tracking.
- **Retrofit + remote.** A miniature attachment with battery backup and cellular publish — fits assets that ship without telemetry.

## What it replaces

One mTracker removes from the customer BOM:
- **manual production-hour spreadsheets**;
- a separate **GPS / geo-fence tracker**;
- a **service-hours odometer** for warranty triggers;
- **asset-presence audits**.

## Key specifications

| Category | Value |
|---|---|
| Connectivity | Cellular publish — 4G / LTE class (SIM / carrier / region / bands confirmed during BOM scope) — plus GPS / GNSS positioning |
| Inputs | Equipment-level digital inputs for run / stop, cycle, and output-count signals. Channel count, voltage / contact type, counter rate, debounce, sink / source behavior, and input protection confirmed during BOM scope. |
| Location | GPS / GNSS positioning + **geo-fence** entry / exit alerts |
| Power + battery | Battery backup for assets without continuous power. Supply range, backup-vs-primary role, charging / replacement, runtime by reporting interval, and low-power behavior confirmed during BOM scope. |
| Form factor | Miniature, designed for **retrofit attachment** (fixed or mobile assets) |
| Environmental | Operating temperature, humidity / condensation, shock / vibration (mobile assets), and UV / outdoor exposure confirmed during BOM scope |
| Mechanical | Dimensions, weight, enclosure material, connector / harness, and cable ingress confirmed during BOM scope |
| Ingress protection | IP65 / IP67-**compatible** configurations can be scoped where a site requires it; final protection level, enclosure approach, and any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification is currently claimed.)* |
| Mounting | Retrofit attachment; mounting method, exposure, and antenna placement confirmed during BOM scope |

*Connectivity, inputs, geo-fence, and form factor trace to the Elpis hardware ecosystem map and are confirmed at quoting time. Signal mapping and reporting are confirmed during BOM scope.*

## In the field

mTracker retrofits onto fixed or mobile assets without a redesign — mounting method, location, and antenna placement confirmed during BOM scope. Digital inputs wire to existing run / stop / cycle / output signals; signal voltage/threshold, contact type, and OEE-input mapping confirmed during BOM scope. Battery backup covers assets without continuous power. GSM / GPS / 4G cellular publishes from anywhere with cellular; it buffers locally and publishes when connectivity returns. Geo-fence entry/exit alerts are configured per deployment. Exposure, humidity, and enclosure approach are confirmed during BOM scope; IP65 / IP67-compatible configurations can be scoped where the placement (incl. mobile/outdoor assets) requires it (no certified rating claimed).

## Where it fits

Equipment-level signals → **mTracker** (GSM/GPS telemetry; battery backup) → cellular → **EREMOS V2** (utilization + OEE). mTracker reports the OEE *inputs* + location; **EREMOS V2 computes the OEE** and utilization analytics. **You decide which signals route where**: the same equipment-level run-hours and utilization can feed an OEM / AMC for service-hours billing, warranty triggers, and fleet visibility across multiple sites — customer-controlled routing, your call. For direct analog *sensor* acquisition, see **mDAQ**.

## Field-readiness

Built to retrofit, fixed or mobile: a miniature attachment with battery backup, designed to go onto assets that ship without telemetry — including mobile equipment in the field. Remote-ready and offline-capable — GSM / GPS / 4G publishes from anywhere with cellular, buffers locally, and publishes on reconnect, with geo-fence alerts for location and site-policy tracking.

*Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, and certified/rated claims are published only when formal evidence exists for the specific product/configuration.*

## Next step

Bring the assets to instrument, the signals available on each, the connectivity, and where they sit (fixed or mobile) — that's what we scope a BOM against. We confirm the inputs, power, and mounting for your assets, not for a brochure.
**Get hardware specifications · Request a BOM scope** — contact@elpisitsolutions.com

---

*mTracker brochure **v2 LOCKED 2026-06-06** (ChatGPT Pass-1: "location and compliance" → "location and site-policy tracking") — field-sheet copy condensed verbatim from the LOCKED `page-product-mtracker-spec-v1` (v2) + `hardware-ecosystem-map-v3 §4.1`; no new facts. mTracker reports OEE *inputs* from equipment-level digital signals + location; **EREMOS V2 computes the OEE** (no "computes OEE" claim). Not an analog DAQ (that's mDAQ). Customer-controlled routing — the customer decides which signals route to an OEM / AMC (service-hours / warranty / multi-site). No formal cert claims (IP65/IP67-compatible only; case-by-case during BOM scope). No customer/competitor names, no fabricated metrics. Plant engineer (§2.5) primary, OEM machine builder (§2.6) strong secondary. ChatGPT Pass 1 applied; v2 LOCKED. Inherits the 9-block field-sheet template from brochure-edge-gateway-v1.*
