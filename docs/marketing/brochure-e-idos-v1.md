<!--
File:        docs/marketing/brochure-e-idos-v1.md
Purpose:     Content/copy for the E-IDOS field brochure (the asset the
             /resources/brochures page shows as "Request access").
             INHERITS the 9-block field-sheet template + discipline from
             brochure-edge-gateway-v1 (the shape-setter). E-IDOS is the
             Condition Monitoring HARDWARE instrument, Pillar 4 (oil / fluid
             health), sibling to VAS (vibration).
             This is the COPY (markdown); the print/PDF design is a separate
             step. Condensed verbatim from the LOCKED product spec — no new
             facts introduced.
Audience:    Maintenance Manager / AMC provider (buyer-taxonomy §2.4) + the
             reliability engineer / service contractor who hands it across a
             table on a site visit.
Source-of-truth (do not exceed): page-product-e-idos-spec-v1 (LOCKED)
             + hardware-ecosystem-map-v3 §5.2/§5.3. Specs are orientation-only
             / provisional — verify before any external print run.
Version:     v2 — LOCKED 2026-06-06 (ChatGPT Pass-1 edits applied).
Date:        2026-06-06
Status:      LOCKED.
v1 -> v2: kept standalone-today / EREMOS-roadmap distinction; softened
          lab/vendor REPLACEMENT language to "first-line contamination
          screening" + "routine in-field cleanliness checks"; changed
          "continuously measures … online and offline" to "supports continuous
          inline monitoring and offline / visit-based measurement".

DISCIPLINE (inherited from the field-sheet shape-setter):
  - Specs VERBATIM from the locked product spec; no invented numbers
    (proof-architecture §3). Values are orientation-only / "confirmed during
    BOM scope" where the spec says so (hardware-ecosystem-map §5.2).
  - NO formal certification CLAIMS. IP65/IP67-COMPATIBLE only (never
    "certified"/"rated"/"IP-rated"); cert/IP/site-compliance handled
    case-by-case during BOM scope (§24.B.0).
  - HONESTY LOCK (THE key lock): E-IDOS is a STANDALONE reliability instrument
    TODAY (on-site HMI + thermal-printed report + auto-email + BLE Android
    app). Streaming into EREMOS V2 (alarms / dashboards / incident workflows)
    is NEAR-TERM ROADMAP — never imply it streams to EREMOS today.
  - Sensor-agnostic: HYDAC / Parker / MP Filter / Argo-Hytos are SUPPORTED
    SENSOR PARTNERS (proof of openness, §2.4-endorsed), NOT competitors.
  - ISO 4406 / NAS 1638 are oil-cleanliness REPORT CODES, not Elpis certs.
  - Early-warning / detection framing, NOT a shutdown-prevention guarantee;
    no "self-healing" / "zero downtime".
  - No customer names, no competitor names, no fabricated metrics.
  - Maintenance/AMC CTA: "Bring us your hydraulic system" /
    "Talk to a reliability engineer".
-->

# E-IDOS — hardware brochure (v2 LOCKED)

**Category:** Condition Monitoring — hydraulic & lubrication (Pillar 4)
**One-line:** Know your hydraulic and lubrication oil health continuously — particle contamination, water saturation, and flow — logged to ISO/NAS cleanliness standards. The controller is Elpis; the contamination sensor stays your choice.

---

## What it is

E-IDOS is a rugged appliance for hydraulic and lubrication **oil condition monitoring**. It supports continuous inline monitoring and offline / visit-based measurement of oil health — solid particle contamination, water saturation, and oil flow — and logs results to **ISO 4406 / NAS 1638 cleanliness standards**. The result is **first-line contamination screening** and **routine in-field cleanliness checks** — early warning on the fluid condition that quietly damages pumps, valves, bearings, and seals, between send-out lab analyses.

**Today E-IDOS is a standalone reliability instrument** — it auto-emails reports, prints an ISO/NAS-coded report on-site via a built-in thermal printer, and exposes data through a BLE Android app. **Streaming into EREMOS V2 (alarms, dashboards, incident workflows) is on the near-term roadmap** — until it ships, E-IDOS delivers its value as a standalone instrument.

## What it does

- **Continuous inline + visit-based measurement.** Solid particle contamination, water saturation, and oil flow — continuous inline monitoring and offline / visit-based checks — for first-line contamination screening between grab-samples.
- **Logs to ISO/NAS cleanliness standards.** ISO 4406 / NAS 1638 cleanliness codes, trended over time — the language a reliability program already speaks.
- **Sensor-agnostic by design.** The Elpis Sensor/HMI Controller is ours; the contamination sensor is your choice — supported vendors include HYDAC, Parker, MP Filter, and Argo-Hytos.
- **Reports on-site, today.** Built-in touch HMI, a 58 mm thermal printer for an on-the-spot ISO 4406 / NAS 1638-coded report, and a BLE Android app — no platform dependency to get a diagnostic in hand.

## What it replaces

E-IDOS brings oil-health diagnostics in-house and on-site:
- **first-line contamination screening** on-site — reducing reliance on send-out oil-lab turnaround for routine cleanliness checks (full lab analysis still applies where required);
- **routine in-field cleanliness checks** in place of manual oil-sample collection and shipping for day-to-day monitoring;
- a separate **standalone oil-condition monitor** — folded into one rugged appliance;
- an end to **service-interval guesswork** on hydraulic and lubrication systems.

## Key specifications

*Values are orientation-only / provisional (hardware-ecosystem-map §5.2 "key positioning anchors (orientation only)") — confirmed per deployment. No formal certification claims (IP65 / IP67-compatible only).*

| Category | Value (orientation — confirmed per deployment) |
|---|---|
| Measurements | Solid particle contamination, water saturation, oil flow — online and offline |
| Cleanliness logging | ISO 4406 / NAS 1638 cleanliness codes, trended |
| Controller | Elpis **Sensor/HMI Controller** — Elpis IP: signal conditioning, ISO/NAS analytics, touch HMI, on-board thermal printer, BLE, mobile app, comms stack |
| Sensor compatibility (sensor-agnostic) | Contamination sensor is the customer's choice; supported vendors include **HYDAC, Parker, MP Filter, Argo-Hytos** (and similar). The controller is Elpis; the sensor choice is yours. |
| On-site reporting | Touch-screen HMI · 58 mm thermal printer (printed ISO 4406 / NAS 1638-coded report) · BLE Android companion app · auto-email reporting |
| Connectivity | 4G · Wi-Fi · BLE; GPS for service-site / report geotagging where required. Exact connectivity set confirmed during BOM scope. |
| Sensor connectors | M12 |
| EREMOS V2 streaming | **Roadmap** (near-term) — alarms / dashboards / incident workflows. Standalone today. |
| Ingress protection | IP65 / IP67-**compatible** configurations can be scoped where the placement requires it; protection level + enclosure approach + any certification requirements confirmed during BOM scope — *compatibility, not a certified rating; no formal IP certification is currently claimed* |

*Confirmed during BOM scope: contamination sensor vendor/model + ranges + oil compatibility + M12 wiring; online (inline) vs. offline measurement, hydraulic connection / sampling point, mounting; ISO 4406 / NAS 1638 target codes + alarm thresholds; report cadence (printed / email / BLE); calibration approach + interval + traceability; oil type / viscosity / additive chemistry / operating-temperature range; power, exposure, and enclosure approach.*

## In the field

E-IDOS measures both **online** — installed inline on the hydraulic / lubrication circuit for continuous monitoring — and **offline / visit-based**, where an AMC provider brings the instrument to the system, takes a reading, and hands over a report. The customer's choice of contamination sensor (HYDAC / Parker / MP Filter / Argo-Hytos / similar) connects on M12. On-site, the touch HMI gives on-the-spot readings, the 58 mm thermal printer produces a printed ISO 4406 / NAS 1638-coded report, and the BLE Android app works without any platform connection. Reporting today is auto-email + printed + BLE app; EREMOS V2 streaming (alarms / dashboards / incidents) is near-term roadmap. Connectivity is 4G / Wi-Fi / BLE (GPS for service-site / report geotagging where required); which measurement mode, the hydraulic connection / sampling point, power, exposure, and IP65 / IP67-compatible configuration are confirmed during BOM scope.

## Where it fits

Customer's oil sensor (sensor-agnostic) → **E-IDOS** (Elpis Sensor/HMI Controller — ISO/NAS analytics, on-site HMI + thermal-printed report + auto-email + BLE app) → **standalone today**. The EREMOS V2 streaming path — alarms / dashboards / incident workflows — is **near-term roadmap, not available today**. It is the oil / fluid-health instrument of the Condition Monitoring pillar; VAS is the vibration instrument for rotating machinery.

## Field-readiness

Built for the field, and for the service visit: a rugged appliance with on-site touch HMI, thermal-printed reports, and a BLE app — a complete diagnostic in hand without a platform connection. Sensor-agnostic on the contamination input; the controller is Elpis. Designed for in-house maintenance **and** the service-contractor channel — an AMC provider can go to a customer site, run a measurement, hand over a printed ISO 4406 / NAS 1638-coded cleanliness report, and walk away with a documented diagnostic. E-IDOS gives early warning on rising particle contamination or water saturation — early enough to investigate, filter, flush, or service the system — but it does not guarantee against every failure.

*Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, and certified/rated claims are published only when formal evidence exists for the specific product/configuration.*

## Next step

Bring the hydraulic system you can't afford to lose, the oil it runs on, and your cleanliness targets — that's what we scope an E-IDOS deployment against. We'll match the sensor, set the ISO/NAS targets, and put a documented oil-health diagnostic in your hand on-site.
**Bring us your hydraulic system · Talk to a reliability engineer** — contact@elpisitsolutions.com

---

*E-IDOS brochure **v2 LOCKED 2026-06-06** (ChatGPT Pass-1: lab/vendor replacement language softened to first-line screening / routine in-field checks; "continuously measures online and offline" → "supports continuous inline + offline/visit-based measurement"; standalone-today / EREMOS-roadmap kept) — fifth of the 5 hardware brochures, inheriting the 9-block field-sheet template from `brochure-edge-gateway-v1`. Copy condensed verbatim from the LOCKED `page-product-e-idos-spec-v1` + `hardware-ecosystem-map-v3 §5.2/§5.3`; no new facts. KEY HONESTY LOCK: E-IDOS is a standalone reliability instrument today (on-site HMI, 58 mm thermal printer, auto-email, BLE Android app); EREMOS V2 streaming (alarms/dashboards/incidents) is near-term ROADMAP — never implied as today. Sensor-agnostic: HYDAC / Parker / MP Filter / Argo-Hytos named as supported sensor PARTNERS (proof of openness, §2.4-endorsed), NOT competitors; ISO 4406 / NAS 1638 are oil-cleanliness report codes, not Elpis certifications. Early-warning framing, NOT a shutdown-prevention guarantee. No formal cert claims (IP65/IP67-compatible only). Specs orientation-only / provisional — verify before external print run. ChatGPT Pass 1 applied; v2 LOCKED.*
