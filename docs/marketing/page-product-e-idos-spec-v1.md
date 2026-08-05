<!--
File:        docs/marketing/page-product-e-idos-spec-v1.md
Purpose:     Page spec for /e-idos — the PRODUCT detail page for E-IDOS (Oil
             Health Intelligence appliance; Pillar 4, Condition Monitoring
             HARDWARE). FIFTH (final) hardware product page; INHERITS the LOCKED
             §24.B hardware ProductDetail variant. Completes the 7 product pages.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E hardware product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the LOCKED §24.B
             hardware ProductDetail layout (design-system-v4.md §24.B). Inherits the /edge-gateway shape-setter + the /mdaq +
             /mtracker + /vas precision-hardening lessons.
Companion:   design-system-v4.md §24.B (LOCKED)
             page-product-vas-spec-v1.md (LOCKED — sibling Condition-Monitoring
                instrument; §2.4 buyer + trust-anchor pattern; mirror it)
             page-product-edge-gateway-spec-v1.md (LOCKED — §24.B shape-setter)
             hardware-ecosystem-map-v3.md §5.2 + §5.3 (source-of-truth for E-IDOS
                positioning, sensor-agnostic design, standalone-vs-EREMOS roadmap,
                BOM-elimination, anchors; key anchors flagged "orientation only"
                → provisional)
             page-capabilities-condition-monitoring-spec-v1.md v1 (LOCKED —
                Pillar 4 capability story; cross-link UP)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED — lead
                solution; cross-link ACROSS)
             page-product-eremos-v2-spec-v1.md (LOCKED — EREMOS V2; E-IDOS
                streaming into it is ROADMAP)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.4 (Maintenance Manager / AMC provider —
                primary; §2.4 proof-expectations EXPLICITLY endorse naming the
                E-IDOS sensor partners HYDAC / Filtrec / Parker / MP Filter /
                Argo-Hytos as sensor-agnostic proof)
             industrial-intelligence-ecosystem-positioning-v3.md §4 + positioning
                -amendment-v4.md §3/§5 (LOCKED trust anchors)
             proof-architecture-v1.md §3/§4/§8
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v1 — LOCKED (user + ChatGPT review passed; honesty lock confirmed).
                  Inherits §24.B. FINAL product page (7/7) — Track B complete.
Date:        2026-06-04 (locked 2026-06-05)
Status:      LOCKED (Track B hardware; inherits §24.B).

INHERITS §24.B + the /mdaq + /mtracker + /vas precision-hardening lessons. Same
11-section hardware composition; no new component. Completes all 7 product pages.

GOVERNANCE LOCKS INHERITED FROM §24.B:
  - CERTIFICATIONS / IP: NO formal cert CLAIMS; cert / IP / site-compliance
    case-by-case during BOM scope; IP65 / IP67-COMPATIBLE (not certified/rated).
  - PHASE E. Buyer = Maintenance Manager / AMC (§2.4) → CTA "Bring us your
    hydraulic system" / "Talk to a reliability engineer" (P-H).

SPECS — PROVISIONAL (hardware-ecosystem-map §5.2 "key positioning anchors
(orientation only)"): treat all E-IDOS spec values as orientation, confirmed per
deployment. No invented numbers (proof-architecture §3).

E-IDOS-SPECIFIC — THE KEY HONESTY LOCK (hardware-ecosystem-map §5.2 / §5.3):
  - **E-IDOS is a STANDALONE reliability instrument TODAY** — it auto-emails
    reports, prints on-site via a built-in thermal printer, and exposes data via
    a BLE Android app. **Streaming into EREMOS V2 (alarms / dashboards / incident
    workflows) is NEAR-TERM ROADMAP.** The page MUST NOT imply E-IDOS streams
    into EREMOS V2 today. This is the single most important honesty lock on the
    page (contrast with VAS, which feeds EREMOS V2 today).
  - **Sensor-agnostic** (the differentiator): the Elpis **Sensor/HMI Controller**
    is Elpis IP (electronics, signal conditioning, ISO/NAS analytics, touch HMI,
    on-board thermal printer, BLE, mobile app, comms). The device is sensor-
    agnostic on the contamination input side — it supports leading vendor sensors
    **HYDAC, Parker, MP Filter, Argo-Hytos** (and similar). "The controller is
    Elpis; the customer keeps the sensor choice" — mirrors EdgeConnect's
    protocol-agnostic philosophy at the hardware-acquisition layer. NOTE: these
    are SUPPORTED SENSOR PARTNERS (proof of sensor-agnostic design), NOT
    competitors — buyer-taxonomy §2.4 proof-expectations explicitly endorse
    naming them; this is NOT a proof-architecture §8 competitor-naming violation.
  - Measures hydraulic + lubrication-oil health: solid particle contamination,
    water saturation, oil flow — online and offline. Logs to ISO/NAS cleanliness
    standards. Anti-overclaim: it drives predictive maintenance and helps prevent
    unexpected shutdown — framed as early-warning/detection, NOT a guarantee it
    prevents shutdowns; "cut"/"reduce" verbs.
  - AMC form-factor story: built-in HMI + 58 mm thermal printer + BLE Android app
    are exactly what an AMC provider needs — go to a customer site, run a
    measurement, hand over a printed ISO 4406 / NAS 1638-coded cleanliness report, walk away with a
    documented diagnostic. Designed for in-house maintenance AND the service-
    contractor channel.
  - TRUST ANCHORS (LOCKED, verbatim, anonymized): "Deployed in defense and
    space-agency programs" (defense ministry-tier fluid-condition deployments via
    third-party supplier integration) + "Maintenance and AMC providers across
    India and the Middle East" + "Operating across India and the Middle East".

Word-count target: 1,200-1,800 words page copy. Current draft ~1,550 words.

Note: a /e-idos static mockup can be derived from edge-gateway.html (the §24.B
hardware shape) once this spec locks (hero visual = an ISO/NAS oil-cleanliness
panel).
-->

# `/e-idos` — Page Spec v1 (§24.B hardware; final product page, 7/7)

**Product detail page for E-IDOS — the Oil Health Intelligence appliance (Pillar 4, Condition Monitoring). The deepest factual surface for the instrument: what it measures, the sensor-agnostic controller, what it replaces, how it deploys on hydraulic/lubrication systems, and how to engage. Fifth (final) page on the LOCKED §24.B HARDWARE ProductDetail variant — completing all 7 product pages.**

This is where a **Maintenance Manager or AMC provider** lands when they want to know **what E-IDOS is** — the oil-health measurements it runs (ISO/NAS cleanliness), the sensors it supports, that it's a standalone on-site instrument today, and how it fits the maintenance workflow. It is **not** the capability page (`/capabilities/condition-monitoring`) and **not** the predictive-maintenance solution page; it is the **instrument's product truth**.

Target length: **1,200-1,800 words page copy** per §24.B (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The E-IDOS product detail page. Reader leaves with *"I now know E-IDOS measures hydraulic/lubrication oil health (particle contamination, water, flow) to ISO/NAS standards, that the controller is Elpis but the contamination sensor is my choice, that it's a standalone on-site instrument today (HMI + printed reports + BLE app) with EREMOS V2 streaming on the roadmap, what it replaces, and how to bring it to my hydraulic system."*

**IS NOT:**
- The capability page (`/capabilities/condition-monitoring`, LOCKED — the Pillar 4 *capability* story; cross-link up)
- The VAS page (the *vibration* Condition-Monitoring instrument; E-IDOS is *oil / fluid health*)
- An EREMOS V2-streaming product **today** — **E-IDOS is a standalone instrument today; EREMOS V2 streaming is near-term roadmap** (§3.2 / §3.6 / §3.9 Q4 — the key honesty lock)
- A solution / outcome page (`/solutions/predictive-maintenance` covers the *outcome* — cross-link)
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3)
- A certifications datasheet — **no formal certifications are currently claimed; cert / ingress-protection (IP65 / IP67-*compatible*) / site-compliance handled case-by-case during BOM scope** (§24.B.0)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.B.0)

**Primary buyer:** Maintenance Manager / AMC provider (§2.4) — reliability-engineering buyer on hydraulic and lubrication systems; the AMC provider in particular takes the instrument to customer sites.
- Lands here from `/capabilities/condition-monitoring`, `/solutions/predictive-maintenance`, the Platform menu, or a search for *"oil contamination monitor ISO 4406"* / *"hydraulic fluid condition monitoring"* / *"online oil cleanliness sensor"*
- Wants: the measurements (particle contamination, water, flow), the ISO/NAS logging, which sensors it supports (sensor-agnostic), the on-site reporting (HMI, printed report, BLE app), online vs. offline measurement, and the defense + AMC credibility
- CTA preference: *"Bring us your hydraulic system"* > *"Talk to a reliability engineer"* > *"Book a scoping call"*. **NOT** *"Get hardware specifications"* / *"Request an architecture review"*
- Vocabulary that lands: *oil health*, *ISO 4406 / NAS 1638 cleanliness*, *particle contamination*, *water saturation*, *hydraulic and lubrication systems*, *condition monitoring*, *predictive maintenance*, *sensor-agnostic*, *AMC*
- Vocabulary that backfires: *"real-time analytics"* (vague), *"big data"*, *"ML/AI predictions"* without detail, *"self-healing"*

**Secondary buyer:** Plant manager / reliability-pressured operations (§2.4 reverse-map secondary) + the OEM/heavy-equipment angle (excavators, mining, aerospace ground-support) — served via cross-lens to `/solutions/predictive-maintenance`.

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Hardware-product-page pattern (inherits `/edge-gateway` §1.4); §2.4 buyer tone.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *E-IDOS — Oil Health Intelligence Appliance · Elpis* |
| **Meta description** (140-160 chars) | *Continuous hydraulic + lubrication oil health to ISO/NAS standards — particle contamination, water, flow. Sensor-agnostic; on-site reporting; AMC-ready.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/e-idos` |
| **Schema intent** | `schema.org/Product` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/condition-monitoring` + `/solutions/predictive-maintenance` + `/eremos-v2` + `/architecture` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` HARDWARE layout per §24.B (LOCKED). **11 sections** (inherits the §24.B siblings; §2.4 buyer + trust-anchor element in §3.8; standalone-vs-roadmap honesty in §3.2/§3.6/§3.9).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line + CTAs + hero visual (ISO/NAS panel) | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + `hero__composite` | ~90 |
| **2** | What it is — instrument definition + standalone-today/EREMOS-roadmap + pillar | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~160 |
| **3** | What it does + what it replaces (BOM) | `dark` | `CapabilityCard` grid + BOM-elimination list | ~200 |
| **4** | What it measures + the sensor-agnostic controller | `light` | §24.A spec-table (measurements + controller + supported sensors; orientation-only) + BOM-scope mini-table | spec (not prose) |
| **5** | Deployment on hydraulic / lubrication systems | `light-tinted` | spec-table + narrative | ~130 |
| **6** | Architecture — where it fits (today standalone; EREMOS streaming roadmap) | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~100 |
| **7** | How to engage (AMC + maintenance) | `dark` | narrative (mechanics, not pricing) | ~120 |
| **8** | Field-readiness + where it's deployed (trust anchors) | `light-tinted` | trust-cue content pattern (§16), reframed + anchors | ~120 |
| **9** | Common questions (inline FAQ) — 8 Q&A | `light` | inline FAQ + `FAQPage` schema | ~430 |
| **10** | Related — cross-lens | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · CONDITION MONITORING — HARDWARE
> HEADLINE: E-IDOS — Oil Health Intelligence
> SUBHEAD (max-width 64ch):
> Know your hydraulic and lubrication oil health continuously — particle contamination, water saturation, and flow — logged to ISO/NAS cleanliness standards. The controller is Elpis; the contamination sensor stays your choice. A standalone on-site instrument today, with EREMOS V2 streaming on the roadmap.
>
> PRIMARY CTA (`Button.primary.lg`): Bring us your hydraulic system → HREF `/contact?intent=eidos-system`
> SECONDARY CTA (`Button.secondary.lg`): Talk to a reliability engineer → HREF `/contact?intent=eidos-reliability`
>
> TRUST STRIP (size.sm):
> ISO 4406 / NAS 1638 cleanliness · particle contamination · water saturation · oil flow · sensor-agnostic · on-site HMI + printed report + BLE app · standalone today, EREMOS V2 streaming on the roadmap.
>
> HERO VISUAL (right column, §24 hero-visual slot): a hardware-relevant SVG — an **ISO/NAS oil-cleanliness panel** (cleanliness code + a contamination-trend line + a "report" glyph). Decorative (`aria-hidden`), token-only, "illustrative" caption.

**Anti-patterns:** Product name + value headline. CTA "Bring us your hydraulic system" / "Talk to a reliability engineer" (§2.4 — NOT "Get hardware specifications" / "Request an architecture review"). No formal certification claim (IP65/IP67 *compatible* only). **Never imply E-IDOS streams into EREMOS V2 today** — standalone today, streaming roadmap. Frame as early-warning/detection, not a guarantee it prevents shutdowns; no "self-healing".

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: Know the oil before damage becomes downtime.
>
> BODY:
> E-IDOS is a rugged appliance for hydraulic and lubrication **oil condition monitoring**. It continuously measures oil health — solid particle contamination, water saturation, and oil flow — in both online and offline states, and logs results to **ISO 4406 / NAS 1638 cleanliness standards**. The result is early warning on the fluid condition that quietly damages pumps, valves, bearings, and seals, instead of a quarterly lab report that arrives too late to act on.
>
> BODY ¶2 (muted):
> It is part of the **Condition Monitoring** pillar — see the capability story → `/capabilities/condition-monitoring`. **Today E-IDOS is a standalone reliability instrument** — it auto-emails reports, prints an ISO/NAS-compliant report on-site via a built-in thermal printer, and exposes data through a BLE Android app. **Streaming into EREMOS V2 (alarms, dashboards, incident workflows) is on the near-term roadmap** — until it ships, E-IDOS delivers its value as a standalone instrument. (See → `/eremos-v2` for the platform it will stream into.)

### 3.3 Section 3 — What it does + what it replaces

> EYEBROW: WHAT IT DOES
> SECTION TITLE: What it does — and what it replaces.

Feature cards (what it does):

> - **Measures oil health continuously.** Solid particle contamination, water saturation, and oil flow — online and offline — instead of a periodic grab-sample.
> - **Logs to ISO/NAS cleanliness standards.** ISO 4406 / NAS 1638 cleanliness codes, trended over time — the language a reliability program already speaks.
> - **Sensor-agnostic by design.** The Elpis Sensor/HMI Controller is ours; the contamination sensor is your choice — supported vendors include HYDAC, Parker, MP Filter, and Argo-Hytos.
> - **Reports on-site, today.** Built-in touch HMI, a 58 mm thermal printer for an on-the-spot ISO 4406 / NAS 1638-coded report, and a BLE Android app — no platform dependency to get a diagnostic in hand.

What it replaces (BOM-elimination, per hardware-ecosystem-map §5.2):

> E-IDOS removes from the customer BOM:
> - a separate **oil-contamination laboratory contract**;
> - **manual oil-sample collection and shipping**;
> - a **standalone oil-condition monitor**;
> - a **third-party fluid-analysis vendor relationship**;
> - **service-interval guesswork** on hydraulic and lubrication systems.

### 3.4 Section 4 — What it measures + the sensor-agnostic controller (§24.A, hardware)

> EYEBROW: WHAT IT MEASURES
> SECTION TITLE: The measurement, the controller, and your sensor choice.

Spec-table per §24.A (hardware variant). **Values are orientation-only / provisional** (hardware-ecosystem-map §5.2 "key positioning anchors (orientation only)") — confirmed per deployment. **No formal certification *claims*** (IP65 / IP67-*compatible* only — §24.B.0).

| Category | Value (orientation — confirmed per deployment) |
|---|---|
| **Measurements** | Solid particle contamination, water saturation, oil flow — online and offline |
| **Cleanliness logging** | ISO 4406 / NAS 1638 cleanliness codes, trended |
| **Controller** | Elpis **Sensor/HMI Controller** — Elpis IP: signal conditioning, ISO/NAS analytics, touch HMI, on-board thermal printer, BLE, mobile app, comms stack |
| **Sensor compatibility (sensor-agnostic)** | Contamination sensor is the customer's choice; supported vendors include **HYDAC, Parker, MP Filter, Argo-Hytos** (and similar). The controller is Elpis; the sensor choice is yours. |
| **On-site reporting** | Touch-screen HMI · 58 mm thermal printer (printed ISO 4406 / NAS 1638-coded report) · BLE Android companion app · auto-email reporting |
| **Connectivity** | 4G · Wi-Fi · BLE; GPS for service-site / report geotagging where required. Exact connectivity set confirmed during BOM scope. |
| **Sensor connectors** | M12 |
| **EREMOS V2 streaming** | **Roadmap** (near-term) — alarms / dashboards / incident workflows. Standalone today. |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where the placement requires it; protection level + enclosure approach + any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification currently claimed.)* |

**Confirmed during BOM scope** (deployment-specific detail a reliability/fluids buyer expects — no invented numbers):

| Item | Confirmed during BOM scope |
|---|---|
| **Sensor selection** | Contamination sensor vendor/model, measurement ranges, oil type / viscosity / temperature compatibility, M12 wiring |
| **Mounting + plumbing** | Online (inline) vs. offline measurement, hydraulic connection / sampling point, flow path, mounting |
| **Cleanliness targets** | ISO 4406 / NAS 1638 target codes + alarm thresholds per system |
| **Reporting + workflow** | Report cadence (printed / email / BLE), and — when EREMOS V2 streaming ships — alarm/incident mapping |
| **Report contents** | ISO 4406 code, NAS 1638 class, water-saturation + flow readings, timestamp, site / system ID, sensor identity, target-code comparison, and service notes — exact report layout confirmed during BOM scope |
| **Calibration + traceability** | Sensor calibration approach, calibration interval, and any traceability / documentation requirements confirmed during BOM scope |
| **Fluid compatibility** | Oil type, viscosity grade, additive chemistry, and operating-temperature range the measurement must tolerate confirmed during BOM scope |
| **Power + environment** | Supply, exposure, enclosure approach, IP65/IP67-compatible configuration |

> CAPTION (size.sm): Measurements, cleanliness logging, and form-factor anchors are orientation-level and confirmed per deployment. Sensor selection, plumbing, targets, and reporting are confirmed during BOM scope.

### 3.5 Section 5 — Deployment on hydraulic / lubrication systems

> EYEBROW: IN THE FIELD
> SECTION TITLE: How it goes on the system.

| | |
|---|---|
| **Online or offline** | Measures both online — installed inline on the hydraulic / lubrication circuit for continuous monitoring — and offline / visit-based, where an AMC provider brings the instrument to the system, takes a reading, and hands over a report. Which mode (continuous-inline or visit-based) and the hydraulic connection / sampling point are confirmed during BOM scope. |
| **Sensor** | Customer's choice of contamination sensor (HYDAC / Parker / MP Filter / Argo-Hytos / similar) on M12 connectors; selection + oil-compatibility confirmed during BOM scope. |
| **On-site use** | Touch HMI for on-the-spot readings; 58 mm thermal printer for a printed ISO 4406 / NAS 1638-coded report; BLE Android app — usable without any platform connection. |
| **Reporting** | Auto-email + printed + BLE app today. EREMOS V2 streaming (alarms / dashboards / incidents) is near-term roadmap. |
| **Connectivity / power / environment** | 4G / Wi-Fi / BLE (GPS for service-site / report geotagging where required); power, exposure, and IP65 / IP67-compatible configuration confirmed during BOM scope (no certified rating claimed). |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: The instrument in the stack — today, and on the roadmap.

`ArchitecturePanel.interactive` (product-annotated, §5.A). **The diagram must show two states honestly:** TODAY E-IDOS is standalone (on-site HMI / printed report / email / BLE app); the EREMOS V2 streaming path is drawn as **ROADMAP** (dashed / labelled). Annotation eyebrow-as-title (§24 P-E). Annotations:

| Annotated region | Eyebrow | Body |
|---|---|---|
| Oil sensor → E-IDOS | SENSOR-AGNOSTIC | Customer's contamination sensor (HYDAC / Parker / MP Filter / Argo-Hytos) on M12; the Elpis Sensor/HMI Controller does the ISO/NAS analytics. |
| E-IDOS core | STANDALONE TODAY | On-site touch HMI, thermal-printed ISO 4406 / NAS 1638-coded report, auto-email, BLE Android app — a complete diagnostic without any platform connection. |
| E-IDOS → EREMOS V2 | EREMOS STREAMING (ROADMAP) | Near-term: stream alarms / dashboards / incident workflows into EREMOS V2. **Not available today** — drawn as roadmap. |
| Form factor | AMC-READY | HMI + printer + app are designed for an AMC provider to measure on-site and hand over a documented report. |

> CAPTION: Today E-IDOS stands alone; the EREMOS V2 streaming path is roadmap (shown dashed). For the platform it will stream into, see → `/eremos-v2`. The full stack → `/architecture`.

### 3.7 Section 7 — How to engage

> EYEBROW: HOW TO ENGAGE
> SECTION TITLE: Start with the hydraulic system you can't afford to lose.
>
> *Packaging labels are illustrative until commercial packaging is approved; this section describes how to engage + what it pairs with, not pricing.*
>
> BODY:
> E-IDOS engagements start with a specific hydraulic or lubrication system — and the oil that quietly decides its life. It's scoped against the oil type, the contamination sensor, online vs. offline measurement, the ISO/NAS targets, and the reporting workflow. For **AMC providers**, the built-in HMI, thermal printer, and BLE app are the point: go to a customer site, run a measurement, hand over a printed ISO/NAS-compliant report, and walk away with a documented diagnostic — no platform dependency. For **in-house maintenance**, it replaces lab turnaround and service-interval guesswork with on-site oil-health intelligence. Bring the system, the oil, and your cleanliness targets; we'll scope the sensor + reporting. Contact Elpis for availability and scoping; detailed pricing follows the scope. No pricing tables, SKU grids, or per-unit pricing on this page.

### 3.8 Section 8 — Field-readiness + where it's deployed

Trust-cue content pattern (§16), reframed for a condition-monitoring instrument, **with the LOCKED trust anchors** (defense + AMC — anonymized per proof-architecture §4):

> EYEBROW: FIELD-READINESS & PROOF
>
> CUE 1 — **Built for the field, and for the service visit.** A rugged appliance with on-site touch HMI, thermal-printed reports, and a BLE app — a complete diagnostic in hand without a platform connection. Sensor-agnostic on the contamination input; the controller is Elpis. IP65 / IP67-compatible configuration confirmed during BOM scope.
>
> WHERE IT'S DEPLOYED (anonymized; category descriptors only):
> - **Deployed in defense and space-agency programs** — ministry-tier fluid-condition deployments (via third-party supplier integration).
> - **Maintenance and AMC providers across India and the Middle East** use E-IDOS to deliver their own oil-health services.
> - **Operating across India and the Middle East.**
>
> *(Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, but certified/rated claims are published only when formal evidence exists. Specific customer names and case studies arrive with the Phase 3 customer-story program; the category descriptors above are the standing, authorized proof.)*

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 8 questions (§2.4 tone).

> #### Q1. What does E-IDOS measure?
> Hydraulic and lubrication oil health — solid particle contamination, water saturation, and oil flow — online and offline, logged to ISO 4406 / NAS 1638 cleanliness standards. Measurement ranges and oil compatibility depend on the selected sensor and are confirmed during BOM scope.
>
> #### Q2. Which contamination sensors does it work with?
> E-IDOS is sensor-agnostic on the contamination input: the Elpis Sensor/HMI Controller does the conditioning and ISO/NAS analytics, and the contamination sensor is your choice — supported vendors include HYDAC, Parker, MP Filter, and Argo-Hytos (and similar). The controller is Elpis; the sensor choice is yours — which matters if you have existing sensor inventory or supplier relationships.
>
> #### Q3. Does it stream into EREMOS V2?
> **Not today.** E-IDOS is a standalone instrument today — it auto-emails reports, prints an ISO 4406 / NAS 1638-coded report on-site via the built-in thermal printer, and exposes data via a BLE Android app. **Streaming into EREMOS V2 (alarms, dashboards, incident workflows) is on the near-term roadmap.** Until it ships, E-IDOS delivers its value as a standalone instrument; we'll be explicit about what's available at scope time.
>
> #### Q4. We're an AMC provider — how does E-IDOS fit our service?
> It's built for exactly that. The touch HMI, 58 mm thermal printer, and BLE app let you go to a customer site, run a measurement, and hand over a printed ISO 4406 / NAS 1638-coded cleanliness report — a documented diagnostic, on the spot, with no platform dependency. Maintenance and AMC providers across India and the Middle East use E-IDOS to deliver their own oil-health services.
>
> #### Q5. Online or offline — how does it measure?
> Both. It measures inline on the hydraulic / lubrication circuit (online) and in offline states. Which mode, and the hydraulic connection / sampling point, are confirmed during BOM scope against your system.
>
> #### Q6. Does it prevent breakdowns?
> It gives you early warning: catching rising particle contamination or water saturation early enough to investigate, filter, flush, or service the system — before the fluid condition damages pumps, valves, bearings, and seals. It does not guarantee against every failure; it replaces lab-turnaround delay and service-interval guesswork with on-site, trended oil-health evidence.
>
> #### Q7. Is it certified? What about IP65 / IP67?
> No formal third-party certifications are currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope. Where the placement requires IP65 / IP67-compatible protection, Elpis can scope a compatible configuration or enclosure approach; formal certification or rating claims are published only when the specific product/configuration has the required certification or test evidence.
>
> #### Q8. How is E-IDOS different from VAS?
> Both are Condition Monitoring instruments for the maintenance buyer, but they watch different failure evidence. **E-IDOS is oil / fluid health** on hydraulic and lubrication systems (contamination, water, flow; ISO/NAS) — a standalone instrument today, EREMOS V2 streaming on the roadmap. **VAS is vibration** on rotating machinery (bearings, imbalance, misalignment) — and feeds EREMOS V2 today. See VAS for rotating machinery.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 (Condition-Monitoring capability + the predictive-maintenance solution + architecture):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONDITION MONITORING | The Pillar 4 capability story | `/capabilities/condition-monitoring` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | How vibration evidence from VAS and oil-health diagnostics from E-IDOS support predictive maintenance. E-IDOS is standalone today; EREMOS V2 streaming is roadmap. | `/solutions/predictive-maintenance` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us the hydraulic system you can't afford to lose.
> SUBHEAD: The system, the oil it runs on, and your cleanliness targets — that's what we scope an E-IDOS deployment against. We'll match the sensor, set the ISO/NAS targets, and put a documented oil-health diagnostic in your hand on-site.
> PRIMARY CTA: Bring us your hydraulic system → `/contact?intent=eidos-system`
> SECONDARY CTA: Talk to a reliability engineer → `/contact?intent=eidos-reliability`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (inherits §24.B).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3 |
| `ArchitecturePanel.interactive` (product-annotated; today vs. roadmap) | §3.6 |
| §24.A spec-table content pattern (hardware) | §3.4 measurements + controller + BOM-scope mini-table; §3.5 field table |
| Trust-cue content pattern (§16, reframed as field-readiness + anchors) | §3.8 |
| Cross-lens content pattern (§17) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |
| Hero visual (`hero__composite`, §24 slot — ISO/NAS panel) | §3.1 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,550 words page copy** (within the §24.B 1,200-1,800 target). Spec-table cell text (§3.4 incl. the BOM-scope mini-table, §3.5) + §3.6 annotations are NOT prose-counted.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.B.3 hardware anti-patterns:

| Don't | Why |
|---|---|
| **Imply E-IDOS streams into EREMOS V2 today** | **THE key honesty lock** — E-IDOS is standalone today; EREMOS V2 streaming is near-term roadmap (hardware-ecosystem-map §5.2). §3.2, §3.4, §3.6, §3.9 Q3 carry it; §3.6 draws the EREMOS path as roadmap (dashed). |
| Claim CE / UL / FCC / IEC / certified IP65 / certified IP67 (or "IP-rated", "certified rugged") unless formal evidence exists | Inherited §24.B.0. Allowed: "IP65 / IP67-compatible configurations can be scoped during BOM review". |
| Guarantee E-IDOS *prevents* shutdowns, or use "zero downtime" / "self-healing" | Anti-overclaim (§2.4 + OEM-v2 precedent). Frame as early warning on the oil; "cut" / "reduce" verbs (§3.9 Q6). |
| Present the analytics / form-factor anchors as a locked spec matrix | "Orientation only" in hardware-ecosystem-map §5.2 — confirmed per deployment (§3.4 caption). |
| Treat the sensor vendors (HYDAC / Parker / MP Filter / Argo-Hytos) as competitor names | They are **supported sensor partners** = proof of sensor-agnostic design; buyer-taxonomy §2.4 proof-expectations EXPLICITLY endorse naming them. NOT a proof-architecture §8 violation. (Do NOT add *fluid-analysis-vendor* competitor names like Bureau Veritas / Castrol Labcheck as comparisons.) |
| Use "Get hardware specifications" / "Request an architecture review" as the primary CTA | §2.4 buyer — "Bring us your hydraulic system" / "Talk to a reliability engineer" (P-H). |
| Bare "real-time analytics" / "big data" / "ML/AI" without naming the measurement | §2.4 backfires — name the measurement (ISO/NAS cleanliness, particle contamination, water saturation). |
| Name specific defense / AMC customers | proof-architecture §4 + positioning v3 §4 — anonymized category descriptors only; locked anchors verbatim; named stories wait for Phase 3. |
| Introduce a new visual primitive | §24.B composes from v3 components + §24.A. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,550); spec tables not prose-counted
- [x] All 11 §24.B sections present (hardware variant)
- [x] Buyer = Maintenance Manager / AMC (§2.4); CTA "Bring us your hydraulic system" / "Talk to a reliability engineer" (P-H)
- [x] **KEY LOCK: standalone TODAY; EREMOS V2 streaming = near-term ROADMAP — stated in §3.2, §3.4 row, §3.6 (drawn dashed/roadmap), §3.9 Q3; never implied as today**
- [x] **Sensor-agnostic differentiator clear; supported sensor partners (HYDAC / Parker / MP Filter / Argo-Hytos) named as proof (buyer-taxonomy §2.4-endorsed), NOT competitors; no fluid-analysis-vendor comparisons**
- [x] NO formal cert claims; IP65/IP67 *compatible* only; §3.9 Q7 approved wording
- [x] Measurements/analytics framed orientation-only / confirmed-per-deployment (§3.4 caption); ISO 4406 / NAS 1638 named
- [x] "Prevents shutdown" softened to early-warning (§3.9 Q6); no "self-healing"; §2.4 vocabulary; no bare "real-time analytics"
- [x] §3.8 LOCKED trust anchors verbatim + anonymized (defense; AMC; India & Middle East); no named customers
- [x] §3.3 BOM-elimination list (oil-lab contract / sample shipping / standalone monitor / 3rd-party fluid vendor / service-interval guesswork)
- [x] §3.10 cross-lens: condition-monitoring + predictive-maintenance + architecture; §3.9 Q8 distinguishes E-IDOS vs VAS (incl. the EREMOS-status difference)
- [x] AMC form-factor angle (HMI + thermal printer + BLE app) in §3.3 + §3.7 + §3.9 Q4
- [x] No new component beyond v3 + §24.A; §1.4 metadata (`Product` schema)
- [x] Specs VERIFIED against hardware-ecosystem-map v3 §5.2 before external publish ("orientation only" → provisional)
- [x] **Inherited §24.B + mDAQ/mTracker/VAS lessons** (cert/IP, BOM-scope mini-table, qualified claims, trust anchors)
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Certifications.** None currently claimed; cert/IP case-by-case during BOM scope (§24.B.0).
- **EREMOS V2 streaming detail.** Near-term roadmap; `/eremos-v2` (LOCKED) is the platform — cross-link; do not document E-IDOS streaming as a current feature.
- **VAS (vibration).** The other Condition Monitoring instrument — its own §24.B page.
- **Capability + solution narratives.** `/capabilities/condition-monitoring` + `/solutions/predictive-maintenance` (LOCKED) — cross-link.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3).
- **Named customer stories.** Phase 3 customer-story program; only anonymized anchors here.
- **Locked sensor-compatibility / measurement-range / cleanliness-spec matrices.** "Orientation only" in the map → confirmed per deployment; not published as locked until verified.

---

*`/e-idos` Page Spec **v1 LOCKED** (§24.B HARDWARE ProductDetail; inherits the LOCKED §24.B variant + the /mdaq + /mtracker + /vas precision-hardening lessons). FIFTH and FINAL hardware product page — completes all 7 product pages (2 software + 5 hardware). KEY HONESTY LOCK: **E-IDOS is a standalone reliability instrument today** (on-site touch HMI, 58 mm thermal printer, auto-email, BLE Android app); **EREMOS V2 streaming (alarms/dashboards/incidents) is near-term ROADMAP** — never implied as today; §3.6 draws the EREMOS path as roadmap (dashed). Sensor-agnostic differentiator: the Elpis Sensor/HMI Controller is Elpis IP; the contamination sensor is the customer's choice (supported partners HYDAC / Parker / MP Filter / Argo-Hytos — named as sensor-agnostic PROOF per buyer-taxonomy §2.4, NOT competitors). Measures hydraulic/lubrication oil health (particle contamination, water saturation, oil flow; ISO 4406 / NAS 1638) — early-warning framing, NOT a shutdown-prevention guarantee. AMC form-factor angle (measure on-site, hand over a printed ISO 4406 / NAS 1638-coded report). Specs orientation-only / provisional per hardware-ecosystem-map §5.2 (verify before publish). Cert/IP discipline inherited from §24.B; BOM-scope mini-table. §3.8 LOCKED trust anchors verbatim + anonymized (defense ministry-tier fluid-condition via third-party; AMC + India/Middle East). Buyer = Maintenance Manager / AMC (§2.4) → "Bring us your hydraulic system" / "Talk to a reliability engineer" (P-H). LOCKED 2026-06-05 (ChatGPT "lock after revisions" — honesty lock passed; P0/P1/P2 precision pass applied). ALL 7 PRODUCT PAGES COMPLETE; Track B done. Cites: design-system-v4 §24.B/§24.A/§24.3, page-product-vas-spec-v1 (sibling CM instrument) + page-product-edge-gateway-spec-v1 (§24.B shape-setter), hardware-ecosystem-map-v3 §5.2/§5.3, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.4, industrial-intelligence-ecosystem-positioning-v3 §4 + positioning-amendment-v4 §3/§5 (trust anchors), proof-architecture-v1 §3/§4/§8, page-capabilities-condition-monitoring-spec-v1 v1, page-solutions-predictive-maintenance-spec-v1 v2, page-product-eremos-v2-spec-v1 (EREMOS streaming target — roadmap), page-architecture-spec-v1 v2.1, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
