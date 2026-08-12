<!--
File:        docs/marketing/page-product-vas-spec-v1.md
Purpose:     Page spec for /vas — the PRODUCT detail page for VAS (Vibration
             Analyser System; Pillar 4, Condition Monitoring HARDWARE, built on
             the mDAQ platform). Fourth hardware product page; INHERITS the
             LOCKED §24.B hardware ProductDetail variant.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E hardware product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the LOCKED §24.B
             hardware ProductDetail layout (design-system-v4.md §24.B). Inherits the /edge-gateway shape-setter + the /mdaq +
             /mtracker precision-hardening lessons.
Companion:   design-system-v4.md §24.B (LOCKED)
             page-product-mdaq-spec-v1.md (LOCKED — VAS is built on the mDAQ
                hardware platform; cross-link)
             page-product-edge-gateway-spec-v1.md (LOCKED — §24.B shape-setter)
             hardware-ecosystem-map-v3.md §5.1 + §5.3 (source-of-truth for the
                VAS positioning, analytics, BOM-elimination, anchor deployments;
                NOTE: VAS analytics anchors are flagged "orientation only" →
                provisional)
             page-capabilities-condition-monitoring-spec-v1.md v1 (LOCKED — the
                Pillar 4 capability story; cross-link UP)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED — the
                lead solution that uses VAS; cross-link ACROSS)
             page-product-eremos-v2-spec-v1.md (LOCKED — VAS feeds EREMOS V2)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.4 (Maintenance Manager / AMC provider —
                primary)
             industrial-intelligence-ecosystem-positioning-v3.md §4 + positioning
                -amendment-v4.md §3/§5 (LOCKED trust anchors — defense/space-
                agency + AMC; condition monitoring is where they apply)
             proof-architecture-v1.md §3/§4/§8
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v2 — LOCKED 2026-06-04 after ChatGPT review (verdict "Lock after
                  revisions"; claim-discipline + vibration-buyer hardening
                  applied). Inherits §24.B.
Date:        2026-06-04
Status:      LOCKED (Track B hardware; inherits §24.B). First page on the §2.4
                  Maintenance-Manager / AMC buyer — confirms §24.B flexes to a
                  reliability buyer. E-IDOS is the last hardware page.

ChatGPT review (2026-06-04) — "Lock after revisions" (the §2.4 buyer flip works;
mDAQ-platform honesty + EREMOS boundary + trust anchors all correct). v2 applied:
  - P0: removed the time-specific lead claim — §3.2 title "three weeks early" →
    "Find the fault early enough to plan the intervention".
  - P0: softened prevention-coded language — "predict-and-prevent" → "condition-
    based planning" (§1.2, §3.7); §3.11 "prove the early warning" → "validate the
    warning logic"; §3.3 "before failure" → "as the vibration signature develops".
  - P1: analytics re-labeled as configured-per-machine-class (§3.4 header +
    intro sentence) — not a locked SKU matrix.
  - P1: +Speed/order-reference BOM-scope row; expanded Sensors + Acquisition rows
    (axis count, sensitivity, bandwidth, retention).
  - P2: hero trust strip made analysis-focused (dropped the defense anchor — the
    full LOCKED anchors stay verbatim in §3.8).
  - P2: FAQ Q8 sharpened — VAS feeds EREMOS V2 today; E-IDOS streaming status on
    the E-IDOS page.

INHERITS §24.B + the /mdaq + /mtracker precision-hardening lessons. Same
11-section hardware composition; no new component.

GOVERNANCE LOCKS INHERITED FROM §24.B:
  - CERTIFICATIONS / IP: NO formal cert CLAIMS; cert / IP / site-compliance
    case-by-case during BOM scope; IP65 / IP67-COMPATIBLE (not certified/rated).
    Forbidden cert wording per §24.B.0.
  - PHASE E.

BUYER FLIP (per §24.B.0 + P-H): VAS is a **condition-monitoring instrument** →
PRIMARY buyer is the **Maintenance Manager / AMC provider (§2.4)**, NOT the
plant engineer. CTA = **"Bring us your most-watched machine"** / **"Talk to a
reliability engineer"** (§2.4-endorsed; NOT "Get hardware specifications" — that
fits the §2.5 acquisition-hardware pages). Reliability-engineering vocabulary
(§2.4): vibration analysis, FFT, order analysis, bearing fault detection,
predictive maintenance, condition monitoring, reliability engineering.
Backfires (§2.4): "real-time analytics" (vague), "big data", "ML/AI predictions"
without architectural detail, "self-healing".

SPECS — PROVISIONAL (hardware-ecosystem-map §5.1 flags the analytics anchors
"orientation only"): treat all VAS analysis/spec values as **orientation,
confirmed per deployment** — firmer "verify before publish" discipline than the
Edge Gateway / mDAQ / mTracker pages. No invented numbers (proof-architecture §3).

VAS-SPECIFIC positioning + honesty (hardware-ecosystem-map §5.1 / §5.3):
  - VAS is a **vibration condition-monitoring application built on the mDAQ
    hardware platform** (configured for vibration acquisition + analytics — not a
    separate hardware line; "one acquisition platform, multiple application
    specialties, one intelligence stack"). Use the /mdaq-lesson wording ("built
    on the mDAQ hardware platform, configured for…"), NOT "same hardware".
  - It detects deviations from normal vibration patterns on rotating machinery,
    conveyors, gearboxes, structural components — identifying bearing issues,
    imbalance, misalignment, looseness, cracks **before failure**. Frame as
    *detection / early identification* (a mechanism), NOT a guarantee that it
    prevents failures; anti-overclaim "cut"/"reduce" verbs; no "self-healing".
  - **VAS feeds EREMOS V2** today (alarms / dashboards / incident workflows) —
    unlike E-IDOS, whose EREMOS streaming is roadmap. (hardware-ecosystem-map
    §5.3: "both feed (or will feed — see E-IDOS roadmap note)…".)
  - TRUST ANCHORS (LOCKED, verbatim — condition monitoring is where they apply):
    "Deployed in defense and space-agency programs" (precision monitoring of
    high-value rotating equipment) + "Maintenance and AMC providers across India
    and the Middle East" + "Operating across India and the Middle East". Used in
    §3.8 as anonymized category descriptors (proof-architecture §4 — no named
    customers).

Word-count target: 1,200-1,800 words page copy. Post-v2 draft ~1,530 words.

Note: a /vas static mockup can be derived from edge-gateway.html (the §24.B
hardware shape) once this spec locks (hero visual = a vibration-spectrum panel).
-->

# `/vas` — Page Spec v1 (§24.B hardware; inherits the /edge-gateway shape-setter)

**Product detail page for VAS — the Vibration Analyser System (Pillar 4, Condition Monitoring), built on the mDAQ hardware platform. The deepest factual surface for the instrument: what it detects, what it removes from your BOM, the analysis it runs, how it deploys on rotating equipment, and how to engage. Fourth page on the LOCKED §24.B HARDWARE ProductDetail variant — and the first on the §2.4 Maintenance-Manager buyer.**

This is where a **Maintenance Manager or AMC provider** lands when they want to know **what VAS is** — the vibration analysis it runs, the failure modes it catches, what it replaces, and how it feeds the operational stack. It is **not** the capability page (`/capabilities/condition-monitoring`) and **not** the predictive-maintenance solution page; it is the **instrument's product truth**.

Target length: **1,200-1,800 words page copy** per §24.B (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The VAS product detail page. Reader leaves with *"I now know what VAS detects (bearing, imbalance, misalignment, looseness, cracks), the analysis it runs (time- and frequency-domain), that it's built on the mDAQ platform and feeds EREMOS V2, what it replaces, and how to bring it to my most-watched machine."*

**IS NOT:**
- The capability page (`/capabilities/condition-monitoring`, LOCKED — the Pillar 4 *capability* story; cross-link up)
- The mDAQ page (`/mdaq`, LOCKED — the general-purpose acquisition platform VAS is **built on**; cross-link)
- The E-IDOS page (the oil-health instrument — the *other* Condition Monitoring product; its own §24.B page)
- An OEE / operational-analytics product — VAS is **reliability / vibration analysis**; it **feeds EREMOS V2** for alarms/dashboards/incidents
- A solution / outcome page (`/solutions/predictive-maintenance` covers the *outcome* — cross-link)
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3)
- A certifications datasheet — **no formal certifications are currently claimed; cert / ingress-protection (IP65 / IP67-*compatible*) / site-compliance handled case-by-case during BOM scope** (§24.B.0)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.B.0)

**Primary buyer:** Maintenance Manager / AMC provider (§2.4) — reliability-engineering buyer (in-house maintenance or contracted AMC), runs on condition data and predictive insight; wants to move from break-fix to condition-based planning on rotating machinery.
- Lands here from `/capabilities/condition-monitoring`, `/solutions/predictive-maintenance`, the Platform menu, or a search for *"vibration analysis bearing fault"* / *"FFT condition monitoring"* / *"online vibration monitoring rotating equipment"*
- Wants: the failure modes it catches, the analyses it runs (FFT, order analysis, time-domain severity), how it mounts on rotating equipment, how it feeds the maintenance workflow (EREMOS V2 alarms/incidents), and the defense/space-agency + AMC credibility
- CTA preference: *"Bring us your most-watched machine"* > *"Talk to a reliability engineer"* > *"Book a scoping call"* (works framed around a specific machine). **NOT** *"Get hardware specifications"* (that's the §2.5 acquisition pages) or *"Request an architecture review"*
- Vocabulary that lands: *vibration analysis*, *FFT*, *order analysis*, *bearing fault detection*, *imbalance / misalignment / looseness*, *predictive maintenance*, *condition monitoring*, *reliability engineering*, *rotating machinery*
- Vocabulary that backfires: *"real-time analytics"* (vague — name the analyses), *"big data"*, *"ML/AI predictions"* without architectural detail (alarms them — they want to know what the analysis does), *"self-healing"* (overpromise)

**Secondary buyer:** Plant manager (reliability-pressured) (§2.4 reverse-map secondary) — served via cross-lens to `/solutions/predictive-maintenance`.

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Hardware-product-page pattern (inherits `/edge-gateway` §1.4); §2.4 buyer tone.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *VAS — Vibration Analyser System for PdM · Elpis* |
| **Meta description** (140-160 chars) | *Catch bearing, imbalance, misalignment, and looseness before failure. Vibration analysis (FFT, order analysis) on rotating machinery, feeding EREMOS V2.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/vas` |
| **Schema intent** | `schema.org/Product` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/condition-monitoring` + `/solutions/predictive-maintenance` + `/mdaq` + `/architecture` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` HARDWARE layout per §24.B (LOCKED). **11 sections** (inherits the §24.B siblings; §2.4 buyer tone + a trust-anchor element in §3.8).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line + CTAs + hero visual (vibration spectrum) | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + `hero__composite` | ~90 |
| **2** | What it is — instrument definition + mDAQ-platform + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~150 |
| **3** | What it does + what it replaces (BOM) | `dark` | `CapabilityCard` grid + BOM-elimination list | ~200 |
| **4** | What it analyses + platform | `light` | §24.A spec-table (analysis suite + platform; orientation-only/provisional) + BOM-scope mini-table | spec (not prose) |
| **5** | Deployment on rotating equipment | `light-tinted` | spec-table + narrative | ~130 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~90 |
| **7** | How to engage | `dark` | narrative (AMC + maintenance; mechanics, not pricing) | ~120 |
| **8** | Field-readiness + where it's deployed (trust anchors) | `light-tinted` | trust-cue content pattern (§16), reframed + anchors | ~120 |
| **9** | Common questions (inline FAQ) — 8 Q&A | `light` | inline FAQ + `FAQPage` schema | ~420 |
| **10** | Related — cross-lens | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · CONDITION MONITORING — HARDWARE
> HEADLINE: VAS — Vibration Analyser System
> SUBHEAD (max-width 64ch):
> Catch bearing wear, imbalance, misalignment, and looseness on rotating machinery — before they become a breakdown. Continuous vibration analysis (time- and frequency-domain) on a single acquisition platform, feeding alarms and incidents into EREMOS V2.
>
> PRIMARY CTA (`Button.primary.lg`): Bring us your most-watched machine → HREF `/contact?intent=vas-machine`
> SECONDARY CTA (`Button.secondary.lg`): Talk to a reliability engineer → HREF `/contact?intent=vas-reliability`
>
> TRUST STRIP (size.sm):
> FFT + order analysis · bearing / imbalance / misalignment / looseness · built on the mDAQ platform · feeds EREMOS V2.
>
> HERO VISUAL (right column, §24 hero-visual slot): a hardware-relevant SVG — a vibration **spectrum / FFT panel** (frequency axis with peaks + a severity band). Decorative (`aria-hidden`), token-only, "illustrative" caption.

**Anti-patterns:** Product name + value headline. CTA "Bring us your most-watched machine" / "Talk to a reliability engineer" (§2.4 — NOT "Get hardware specifications" / "Request an architecture review"). No formal certification claim (IP65/IP67 *compatible* only). Frame as *detection / early identification before failure*, not a guarantee it prevents failures. No "self-healing", no bare "ML/AI" without naming the analysis.

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: Find the fault early enough to plan the intervention.
>
> BODY:
> VAS is a vibration condition-monitoring system. It watches rotating machinery — motors, pumps, fans, gearboxes, conveyors — for deviations from normal vibration patterns, and identifies the developing fault: bearing wear, imbalance, misalignment, looseness, or structural cracking, **before** it becomes a breakdown. It runs the analysis a reliability engineer would run by hand, continuously.
>
> BODY ¶2 (muted):
> It is part of the **Condition Monitoring** pillar — see the capability story → `/capabilities/condition-monitoring`. VAS is **built on the mDAQ hardware platform**, configured for specialized vibration acquisition and analytics — one acquisition platform, multiple application specialties, one intelligence stack (see → `/mdaq`). It **feeds EREMOS V2**: every flagged deviation becomes an alarm and a tracked incident in the same operational stack your team already uses.

### 3.3 Section 3 — What it does + what it replaces

> EYEBROW: WHAT IT DOES
> SECTION TITLE: What it does — and what it replaces.

Feature cards (what it does):

> - **Detects developing faults early.** Watches for deviations from normal vibration patterns and flags bearing, imbalance, misalignment, looseness, and structural issues as the vibration signature develops.
> - **Runs reliability-grade analysis, continuously.** Time-domain severity and frequency-domain (FFT/spectrum) analysis on rotating equipment — not a once-a-quarter handheld spot-check.
> - **Built on one platform.** The same mDAQ acquisition platform, configured for vibration — one platform across your acquisition + condition-monitoring needs.
> - **Feeds the maintenance workflow.** Flagged deviations become EREMOS V2 alarms + tracked incidents — triage, assign, resolve, close.

What it replaces (BOM-elimination, per hardware-ecosystem-map §5.1):

> VAS removes from the customer BOM:
> - a **dedicated vibration-analyser console** (often a five-figure standalone instrument);
> - a separate **condition-monitoring software stack**;
> - **manual handheld vibration spot-checks** (which miss everything between visits);
> - a **third-party predictive-maintenance vendor relationship**.

### 3.4 Section 4 — What it analyses + platform (§24.A, hardware)

> EYEBROW: WHAT IT ANALYSES
> SECTION TITLE: The analysis, and the platform it runs on.

Spec-table per §24.A (hardware variant). **Analysis capabilities are orientation-only / provisional** (hardware-ecosystem-map §5.1) — confirmed per deployment; not a locked feature matrix. **The analysis package is configured per machine class** — FFT/spectrum, RMS severity, order analysis, and advanced plots are selected where the machine and sensor setup require them. **No formal certification *claims*** (IP65 / IP67-*compatible* only — §24.B.0).

| Category | Available / orientation capability — configured per machine class |
|---|---|
| **Time-domain analysis** | Peak detection, RMS severity (trend against normal) |
| **Frequency-domain analysis** | FFT / spectrum |
| **Advanced analysis** | Order analysis, Bode plot, polar plot, cascade, waterfall |
| **Failure-mode mapping** | Bearing, gear, and structural fault signatures |
| **Acquisition platform** | Built on the **mDAQ** hardware platform, configured for vibration acquisition — see `/mdaq` for the platform specs |
| **Output / integration** | Feeds **EREMOS V2** (alarms, dashboards, incident workflows) over the canonical stream |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where the placement requires it; protection level + enclosure approach + any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification currently claimed.)* |

**Confirmed during BOM scope** (deployment-specific detail a reliability buyer expects — no invented numbers):

| Item | Confirmed during BOM scope |
|---|---|
| **Sensors** | Accelerometer type, axis count, sensitivity, frequency range, mounting method, cable routing, and placement count per machine |
| **Acquisition** | Sampling rate, resolution, bandwidth, measurement schedule (continuous vs. interval), and waveform / spectrum / trend retention |
| **Speed / order reference** | RPM source, tach / speed input if required, variable-speed assumptions, and order-analysis applicability |
| **Analysis configuration** | Which analyses + alarm thresholds per machine class; baseline / learning period; failure-mode templates |
| **Mounting** | Sensor mounting (stud / magnet / adhesive), cable routing, enclosure approach, environment |
| **Integration** | Alarm/incident mapping into EREMOS V2; report cadence |

> CAPTION (size.sm): Analysis capabilities are orientation-level and confirmed per deployment + machine class. Sensor selection, sampling, thresholds, and baselines are confirmed during BOM scope. Hardware platform specs: see `/mdaq`.

### 3.5 Section 5 — Deployment on rotating equipment

> EYEBROW: IN THE FIELD
> SECTION TITLE: How it goes on the machine.

| | |
|---|---|
| **Sensors** | Accelerometers mounted on the monitored equipment. Sensor type, mount (stud / magnet / adhesive), and placement confirmed during BOM scope. |
| **Acquisition** | Built on the mDAQ platform — vibration sampling rate / resolution and measurement schedule (continuous vs. interval) confirmed during BOM scope. |
| **Baseline + thresholds** | A baseline / learning period establishes "normal" per machine; alarm thresholds + failure-mode templates set per machine class. |
| **Integration** | Flagged deviations publish to EREMOS V2 as alarms + incidents. |
| **Connectivity / power / environment** | Per the mDAQ platform — connectivity, power, environment, and IP65 / IP67-compatible configuration confirmed during BOM scope (no certified rating claimed). |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: The instrument in the stack.

`ArchitecturePanel.interactive` (product-annotated, §5.A): rotating equipment + accelerometers → **VAS** (highlighted; on the mDAQ platform) → canonical stream → EREMOS V2 (alarms / incidents / dashboards). Annotation eyebrow-as-title (§24 P-E). Annotations:

| Annotated region | Eyebrow | Body |
|---|---|---|
| Machine → VAS | VIBRATION ACQUISITION | Accelerometers on rotating equipment feed VAS, built on the mDAQ acquisition platform. |
| VAS core | RELIABILITY ANALYSIS | Time- and frequency-domain analysis (FFT, order analysis) flags bearing / imbalance / misalignment / looseness before failure. |
| VAS → EREMOS V2 | INTO THE WORKFLOW | Flagged deviations become EREMOS V2 alarms + tracked incidents — the same stack the team already uses. |
| Platform | ONE PLATFORM | The same mDAQ platform serves acquisition (mDAQ) and condition monitoring (VAS) — one platform, multiple specialties. |

> CAPTION: VAS shares the acquisition platform with **mDAQ** and the intelligence stack with **EREMOS V2**. For the oil-health instrument, see **E-IDOS**. The full stack → `/architecture`.

### 3.7 Section 7 — How to engage

> EYEBROW: HOW TO ENGAGE
> SECTION TITLE: Start with the machine you worry about most.
>
> *Packaging labels are illustrative until commercial packaging is approved; this section describes how to engage + what it pairs with, not pricing.*
>
> BODY:
> VAS engagements start with a specific machine — the one whose failure would hurt most. It's scoped against the equipment class, the sensors + mounting, the measurement schedule, and the integration into EREMOS V2. For **AMC providers**, VAS turns a periodic spot-check service into continuous monitoring you can document for the customer; for **in-house maintenance**, it moves a critical machine from break-fix to condition-based planning. Bring the machine, its duty cycle, and its failure history; we'll scope sensors + analysis + alarms. Contact Elpis for availability and scoping; detailed pricing follows the scope. No pricing tables, SKU grids, or per-unit pricing on this page.

### 3.8 Section 8 — Field-readiness + where it's deployed

Trust-cue content pattern (§16), reframed for a condition-monitoring instrument, **with the LOCKED trust anchors** (defense/space-agency + AMC — anonymized per proof-architecture §4; condition monitoring is where these apply):

> EYEBROW: FIELD-READINESS & PROOF
>
> CUE 1 — **Built for the machine, on one platform.** VAS runs on the mDAQ acquisition platform, configured for vibration — ruggedized for rotating-equipment environments. Sensor mounting + IP65 / IP67-compatible configuration confirmed during BOM scope.
>
> WHERE IT'S DEPLOYED (anonymized; category descriptors only):
> - **Deployed in defense and space-agency programs** — precision monitoring of high-value rotating equipment.
> - **Maintenance and AMC providers across India and the Middle East** use VAS to deliver their own condition-monitoring services.
> - **Operating across India and the Middle East.**
>
> *(Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, but certified/rated claims are published only when formal evidence exists. Specific customer names and case studies arrive with the Phase 3 customer-story program; the category descriptors above are the standing, authorized proof.)*

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 8 questions (§2.4 tone).

> #### Q1. What faults does VAS catch?
> Developing faults on rotating machinery — bearing wear, imbalance, misalignment, looseness, and structural issues — by watching for deviations from each machine's normal vibration pattern and flagging them before they become a breakdown. It does not guarantee against every failure; it gives you early warning where the vibration signature shows it.
>
> #### Q2. What analysis does it actually run?
> Time-domain (peak detection, RMS severity) and frequency-domain (FFT / spectrum), with advanced analyses — order analysis, Bode, polar, cascade, waterfall — and failure-mode mapping for bearing, gear, and structural signatures. The specific analyses and alarm thresholds are configured per machine class during the scope.
>
> #### Q3. Is this a separate box, or is it built on mDAQ?
> VAS is built on the mDAQ hardware platform, configured for specialized vibration acquisition and analytics — one acquisition platform across your needs, not a separate hardware line. See `/mdaq` for the platform specs.
>
> #### Q4. How does it fit my maintenance workflow?
> Flagged deviations publish to EREMOS V2 as alarms and tracked incidents — triage, assignment, resolution, closure — in the same operational stack your team already uses. No separate condition-monitoring software silo.
>
> #### Q5. We're an AMC provider — how does VAS help us?
> It turns a periodic handheld spot-check service into continuous monitoring you can document for your customer: catch developing faults between visits, and hand over an evidence-backed diagnostic. Maintenance and AMC providers across India and the Middle East use VAS to deliver their own condition-monitoring services.
>
> #### Q6. Is it certified? What about IP65 / IP67?
> No formal third-party certifications are currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope. Where the placement requires IP65 / IP67-compatible protection, Elpis can scope a compatible configuration or enclosure approach; formal certification or rating claims are published only when the specific product/configuration has the required certification or test evidence.
>
> #### Q7. What sensors does it use, and how are they mounted?
> Accelerometers mounted on the monitored equipment; sensor type, frequency range, channel count per machine, and mount (stud / magnet / adhesive) are confirmed during BOM scope against the machine class and what you're watching for.
>
> #### Q8. How is VAS different from E-IDOS?
> Both are Condition Monitoring instruments for the maintenance buyer, but they watch different failure evidence. **VAS is vibration** on rotating machinery — bearings, imbalance, misalignment, looseness, and structural signatures — and feeds EREMOS V2 alarms / incidents today. **E-IDOS is oil / fluid health** on hydraulic and lubrication systems; its EREMOS V2 streaming status is handled on the E-IDOS page. See E-IDOS for hydraulics.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 (Condition-Monitoring capability + the predictive-maintenance solution that uses VAS + architecture):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONDITION MONITORING | The Pillar 4 capability story | `/capabilities/condition-monitoring` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | The reliability outcome built on VAS | `/solutions/predictive-maintenance` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your most-watched machine.
> SUBHEAD: The motor, pump, fan, or gearbox whose failure would hurt most — its duty cycle and its failure history. We'll scope the sensors, the analysis, and the alarms against that machine, and validate the warning logic before you scale it across the floor.
> PRIMARY CTA: Bring us your most-watched machine → `/contact?intent=vas-machine`
> SECONDARY CTA: Talk to a reliability engineer → `/contact?intent=vas-reliability`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (inherits §24.B).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3 |
| `ArchitecturePanel.interactive` (product-annotated) | §3.6 |
| §24.A spec-table content pattern (hardware) | §3.4 analysis + platform + BOM-scope mini-table; §3.5 field table |
| Trust-cue content pattern (§16, reframed as field-readiness + anchors) | §3.8 |
| Cross-lens content pattern (§17) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |
| Hero visual (`hero__composite`, §24 slot — vibration spectrum) | §3.1 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,530 words page copy** (within the §24.B 1,200-1,800 target; post-ChatGPT-review). Spec-table cell text (§3.4 incl. the BOM-scope mini-table, §3.5) + §3.6 annotations are NOT prose-counted.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.B.3 hardware anti-patterns:

| Don't | Why |
|---|---|
| Claim CE / UL / FCC / IEC / certified IP65 / certified IP67 (or "IP-rated", "certified rugged", "field certified") unless formal evidence exists | Inherited §24.B.0. Allowed: "IP65 / IP67-compatible configurations can be scoped during BOM review". |
| Guarantee VAS *prevents* failures, or use "zero unplanned downtime" / "self-healing" | Anti-overclaim (§2.4 + OEM-v2 precedent). Frame as *detection / early warning where the vibration signature shows it*; "cut" / "reduce" verbs (§3.9 Q1). |
| Use bare "real-time analytics" / "big data" / "ML/AI predictions" without naming the analysis | §2.4 backfires — name the analyses (FFT, order analysis, RMS severity). |
| Present the analytics list as a locked feature matrix | hardware-ecosystem-map §5.1 flags it "orientation only" — confirmed per deployment / machine class (§3.4 caption). |
| Claim it's a separate hardware line / "same hardware as mDAQ" verbatim | "Built on the mDAQ hardware platform, configured for vibration" — one platform, multiple specialties (the /mdaq-lesson wording). |
| Use "Get hardware specifications" / "Request an architecture review" as the primary CTA | §2.4 buyer — "Bring us your most-watched machine" / "Talk to a reliability engineer" (P-H). |
| Name specific defense / space-agency / AMC customers | proof-architecture §4 + positioning v3 §4 — anonymized category descriptors only; the locked anchors are verbatim; named stories wait for Phase 3. |
| Imply VAS computes OEE, or is an operational-analytics product | VAS is reliability/vibration analysis; it **feeds EREMOS V2** for alarms/incidents. |
| Introduce a new visual primitive | §24.B composes from v3 components + §24.A. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,500); spec tables not prose-counted
- [x] All 11 §24.B sections present (hardware variant)
- [x] **Buyer = Maintenance Manager / AMC (§2.4); CTA "Bring us your most-watched machine" / "Talk to a reliability engineer" (P-H) — NOT the §2.5 hardware CTA**
- [x] **NO formal cert claims; IP65/IP67 *compatible* only; cert/IP case-by-case during BOM scope; §3.9 Q6 approved wording**
- [x] **Analytics framed as orientation-only / confirmed-per-deployment (not a locked matrix); §3.4 caption + provisional discipline**
- [x] **"Built on the mDAQ hardware platform, configured for vibration" (not "same hardware"); cross-link `/mdaq`**
- [x] **VAS feeds EREMOS V2 (alarms/incidents) — stated; not an OEE/analytics product**
- [x] Detection framed as early-warning, NOT a failure-prevention guarantee; no "self-healing" / bare "ML/AI"; §2.4 vocabulary (FFT, order analysis, bearing fault); no "real-time analytics"/"big data"
- [x] §3.8 LOCKED trust anchors present + verbatim + anonymized (defense/space-agency; AMC; India & Middle East); no named customers
- [x] §3.3 BOM-elimination list (vibration console / CM software / handheld spot-checks / 3rd-party PdM vendor)
- [x] §3.10 cross-lens: condition-monitoring + predictive-maintenance + architecture; §3.9 Q8 distinguishes VAS vs E-IDOS
- [x] No new component beyond v3 + §24.A; §1.4 metadata (`Product` schema)
- [x] Specs VERIFIED against hardware-ecosystem-map v3 §5.1 before external publish (analytics anchors are "orientation only" → provisional)
- [x] **Inherited §24.B + mDAQ/mTracker lessons** (cert/IP, BOM-scope mini-table, qualified claims)
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Certifications.** None currently claimed; cert/IP case-by-case during BOM scope (§24.B.0).
- **mDAQ platform hardware specs.** That's `/mdaq` (LOCKED) — VAS is built on it; cross-link.
- **E-IDOS (oil health).** The other Condition Monitoring instrument — its own §24.B page.
- **OEE / operational analytics + incident workflow internals.** That's `/eremos-v2` (LOCKED) — VAS feeds it; cross-link.
- **Capability + solution narratives.** `/capabilities/condition-monitoring` + `/solutions/predictive-maintenance` (LOCKED) — cross-link.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3).
- **Named customer stories.** Phase 3 customer-story program; only anonymized anchors here.
- **Locked analysis-capability matrix / sensor-compatibility tables.** "Orientation only" in the map → confirmed per deployment; not published as locked until verified.

---

*`/vas` Page Spec **v2 LOCKED 2026-06-04** (§24.B HARDWARE ProductDetail; inherits the LOCKED §24.B variant + the /mdaq + /mtracker precision-hardening lessons) after ChatGPT review ("Lock after revisions"; claim-discipline + vibration-buyer hardening applied — removed the "three weeks" claim, softened prevention-coded language, analytics configured-per-machine-class, +order-reference scoping, trust anchors kept verbatim in §3.8). Fourth hardware product page — and the FIRST on the §2.4 Maintenance-Manager / AMC buyer (CTA "Bring us your most-watched machine" / "Talk to a reliability engineer", P-H). VAS = vibration condition-monitoring **built on the mDAQ hardware platform**, configured for vibration (not a separate hardware line); detects bearing / imbalance / misalignment / looseness / cracks **before failure** (framed as early-warning detection, NOT a prevention guarantee); **feeds EREMOS V2** (alarms / incidents) today (unlike E-IDOS streaming, which is roadmap). Analytics (FFT, order analysis, time-domain severity, failure-mode mapping) are **orientation-only / provisional** per hardware-ecosystem-map §5.1 (verify before external publish). Cert/IP discipline inherited from §24.B (no formal claims; IP65/IP67-compatible; case-by-case during BOM scope) + a BOM-scope mini-table. §3.8 carries the LOCKED trust anchors verbatim + anonymized (defense/space-agency precision rotating-equipment monitoring; AMC + India/Middle East) — the condition-monitoring products are where these apply. Next: user + ChatGPT review → lock → E-IDOS (the last hardware page; oil health; standalone today, EREMOS streaming roadmap). Cites: design-system-v4 §24.B/§24.A/§24.3, page-product-mdaq-spec-v1 (platform) + page-product-edge-gateway-spec-v1 (§24.B shape-setter), hardware-ecosystem-map-v3 §5.1/§5.3, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.4, industrial-intelligence-ecosystem-positioning-v3 §4 + positioning-amendment-v4 §3/§5 (trust anchors), proof-architecture-v1 §3/§4/§8, page-capabilities-condition-monitoring-spec-v1 v1, page-solutions-predictive-maintenance-spec-v1 v2, page-product-eremos-v2-spec-v1, page-architecture-spec-v1 v2.1, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
