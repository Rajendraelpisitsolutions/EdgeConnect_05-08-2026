<!--
File:        docs/marketing/brochure-vas-v1.md
Purpose:     Content/copy for the VAS (Vibration Analyser System) field brochure
             — the condition-monitoring instrument (Pillar 4), built on the mDAQ
             hardware platform. Inherits the 9-block field-sheet template +
             discipline header from brochure-edge-gateway-v1 (the shape-setter).
             This is the COPY (markdown); the print/PDF design is a separate
             step. Condensed verbatim from the LOCKED product spec — no new facts.
Audience:    Maintenance Manager / AMC provider (buyer-taxonomy §2.4 — the
             reliability buyer), and the AMC / reseller who hands it across a table.
Source-of-truth (do not exceed): page-product-vas-spec-v1 (LOCKED, v2 2026-06-04)
             + hardware-ecosystem-map-v3 §5.1/§5.3. Specs trace to the map and
             are ORIENTATION-ONLY / confirmed per deployment; verify before any
             external print run.
Version:     v2 — LOCKED 2026-06-06 (ChatGPT Pass-1 proof-anchor fix applied).
Date:        2026-06-06
Status:      LOCKED.
v1 -> v2: defense/space-agency proof bullet made VERBATIM + standalone
          ("Deployed in defense and space-agency programs.") — removed
          "precision monitoring of high-value rotating equipment" elaboration;
          clarified named stories need written approval, defense/space-agency
          names off-record permanently.

DISCIPLINE (inherited field-sheet shape):
  - Specs VERBATIM from the locked product spec; no invented numbers
    (proof-architecture §3). Analytics are "orientation only" → confirmed per
    deployment / machine class. "Confirmed during BOM scope" where the spec says so.
  - NO formal certification CLAIMS. IP65/IP67-COMPATIBLE only (never
    "certified"/"rated"/"IP-rated"); cert/IP/site-compliance case-by-case
    during BOM scope (§24.B.0).
  - mDAQ honesty: VAS is "built on the mDAQ hardware platform, configured for
    vibration" — one platform, multiple specialties, NOT a separate hardware
    line and NOT "same hardware".
  - EREMOS honesty: VAS feeds EREMOS V2 (alarms / incidents) TODAY.
  - Overclaim discipline: detection / early warning where the vibration signature
    shows it — NOT a guarantee against every failure; no "self-healing", no bare
    "ML/AI", no "real-time analytics" / "big data".
  - No customer names, no competitor names, no fabricated metrics.
  - Reliability-buyer CTA: "Bring us your most-watched machine" / "Talk to a
    reliability engineer" (§2.4 — NOT "Get hardware specifications").
-->

# VAS — Vibration Analyser System brochure (v2 LOCKED)

**Category:** Condition Monitoring — vibration (Pillar 4)
**One-line:** Catch bearing wear, imbalance, misalignment, and looseness on rotating machinery — before they become a breakdown — with continuous vibration analysis that feeds alarms and incidents into EREMOS V2.

---

## What it is

VAS is a vibration condition-monitoring system. It watches rotating machinery — motors, pumps, fans, gearboxes, conveyors — for deviations from normal vibration patterns, and identifies the developing fault: bearing wear, imbalance, misalignment, looseness, or structural cracking, before it becomes a breakdown. It runs the analysis a reliability engineer would run by hand, continuously.

VAS is **built on the mDAQ hardware platform**, configured for specialized vibration acquisition and analytics — one acquisition platform, multiple application specialties, one intelligence stack. It is part of the Condition Monitoring pillar, and it **feeds EREMOS V2**: every flagged deviation becomes an alarm and a tracked incident in the same operational stack your team already uses.

## What it does

- **Detects developing faults early.** Watches for deviations from normal vibration patterns and flags bearing, imbalance, misalignment, looseness, and structural issues as the vibration signature develops.
- **Runs reliability-grade analysis, continuously.** Time-domain severity and frequency-domain (FFT / spectrum) analysis on rotating equipment — not a once-a-quarter handheld spot-check.
- **Built on one platform.** The same mDAQ acquisition platform, configured for vibration — one platform across your acquisition and condition-monitoring needs.
- **Feeds the maintenance workflow.** Flagged deviations become EREMOS V2 alarms and tracked incidents — triage, assign, resolve, close.

## What it replaces

VAS removes from the customer BOM:
- a **dedicated vibration-analyser console** (often a five-figure standalone instrument);
- a separate **condition-monitoring software stack**;
- **manual handheld vibration spot-checks** (which miss everything between visits);
- a **third-party predictive-maintenance vendor relationship**.

## Key specifications

*Analysis capabilities are orientation-level and confirmed per deployment / machine class — not a locked feature matrix. The analysis package is configured per machine class: FFT/spectrum, RMS severity, order analysis, and advanced plots are selected where the machine and sensor setup require them.*

| Category | Available / orientation capability — configured per machine class |
|---|---|
| Time-domain analysis | Peak detection, RMS severity (trend against normal) |
| Frequency-domain analysis | FFT / spectrum |
| Advanced analysis | Order analysis, Bode plot, polar plot, cascade, waterfall |
| Failure-mode mapping | Bearing, gear, and structural fault signatures |
| Acquisition platform | Built on the **mDAQ** hardware platform, configured for vibration acquisition — see the mDAQ specs for the platform |
| Output / integration | Feeds **EREMOS V2** (alarms, dashboards, incident workflows) over the canonical stream |
| Ingress protection | IP65 / IP67-**compatible** configurations can be scoped where the placement requires it; protection level + enclosure approach + any certification requirements confirmed during BOM scope — *compatibility, not a certified rating; no formal IP certification currently claimed* |

**Confirmed during BOM scope** (deployment-specific detail a reliability buyer expects — no invented numbers):

| Item | Confirmed during BOM scope |
|---|---|
| Sensors | Accelerometer type, axis count, sensitivity, frequency range, mounting method, cable routing, and placement count per machine |
| Acquisition | Sampling rate, resolution, bandwidth, measurement schedule (continuous vs. interval), and waveform / spectrum / trend retention |
| Speed / order reference | RPM source, tach / speed input if required, variable-speed assumptions, and order-analysis applicability |
| Analysis configuration | Which analyses + alarm thresholds per machine class; baseline / learning period; failure-mode templates |
| Mounting | Sensor mounting (stud / magnet / adhesive), cable routing, enclosure approach, environment |
| Integration | Alarm / incident mapping into EREMOS V2; report cadence |

*Analysis values are orientation-level and confirmed per deployment + machine class. Sensor selection, sampling, thresholds, and baselines are confirmed during BOM scope. Hardware platform specs: see mDAQ.*

## In the field

Accelerometers mount on the monitored equipment — sensor type, mount (stud / magnet / adhesive), and placement confirmed during BOM scope. Acquisition runs on the mDAQ platform: vibration sampling rate / resolution and measurement schedule (continuous vs. interval) confirmed during BOM scope. A baseline / learning period establishes "normal" per machine; alarm thresholds and failure-mode templates are set per machine class. Flagged deviations publish to EREMOS V2 as alarms and incidents. Connectivity, power, environment, and IP65 / IP67-compatible configuration are per the mDAQ platform and confirmed during BOM scope (no certified rating claimed).

## Where it fits

Rotating equipment + accelerometers → **VAS** (built on the mDAQ platform) → canonical stream → EREMOS V2 (alarms / incidents / dashboards). VAS shares the acquisition platform with **mDAQ** and the intelligence stack with **EREMOS V2** — one platform, multiple specialties, one intelligence stack. For the oil-health instrument, see **E-IDOS**.

## Field-readiness

Built for the machine, on one platform: VAS runs on the mDAQ acquisition platform, configured for vibration — ruggedized for rotating-equipment environments. Sensor mounting and IP65 / IP67-compatible configuration are confirmed during BOM scope.

*Where it's deployed (anonymized; category descriptors only):*
- **Deployed in defense and space-agency programs.**
- **Maintenance and AMC providers across India and the Middle East** use VAS to deliver their own condition-monitoring services.
- **Operating across India and the Middle East.**

*Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, and certified/rated claims are published only when formal evidence exists for the specific product/configuration. VAS detects and gives early warning where the vibration signature shows it — it does not guarantee against every failure. Named customer stories are published only with the customer's written approval; defense and space-agency customer, program, and agency names remain off-record permanently. The category descriptors above are the standing, authorized proof.*

## Next step

Start with the machine you worry about most — the motor, pump, fan, or gearbox whose failure would hurt most. Bring its duty cycle and its failure history; we'll scope the sensors, the analysis, and the alarms against that machine, and validate the warning logic before you scale it across the floor.
**Bring us your most-watched machine · Talk to a reliability engineer** — contact@elpisitsolutions.com

---

*VAS brochure **v2 LOCKED 2026-06-06** (ChatGPT Pass-1: defense/space-agency proof bullet made verbatim + standalone, elaboration removed; named-story approval note added) — inherits the 9-block field-sheet template from the Edge Gateway shape-setter. Copy condensed verbatim from the LOCKED `page-product-vas-spec-v1` (v2, 2026-06-04) + `hardware-ecosystem-map-v3 §5.1/§5.3`; no new facts. VAS is **built on the mDAQ hardware platform**, configured for vibration (not a separate hardware line); it **feeds EREMOS V2** (alarms / incidents) today; detection is framed as early warning where the vibration signature shows it, NOT a prevention guarantee. Analytics are orientation-only / confirmed per deployment — verify before external print. No formal cert claims (IP65/IP67-compatible only, case-by-case during BOM scope). Reliability-buyer CTA per §2.4. ChatGPT Pass 1 applied; v2 LOCKED.*
