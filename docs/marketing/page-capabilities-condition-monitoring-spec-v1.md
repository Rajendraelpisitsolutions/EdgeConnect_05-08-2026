<!--
File:        docs/marketing/page-capabilities-condition-monitoring-spec-v1.md
Purpose:     Page spec for /capabilities/condition-monitoring — the
             template-setter for the 5 pillar deep-dives. Tests the
             CapabilityDeepDive layout (design-system v3 §14) against
             the hardest content: new buyer (Maintenance Manager + AMC
             provider), new vocabulary (FFT, ISO/NAS, reliability
             engineering), new anchor (defense / space-agency + AMC
             channel). If the layout works here, it works for the
             other 4 pillar pages.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), authors of the other 4 pillar deep-dive specs.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-hub-spec-v1.md §9 (canonical template
                this spec inherits)
             phase2-ia-scope-memo-v2.md + amendment v3 (IA parent)
             buyer-taxonomy-v1.md §2.4 (Maintenance Manager / AMC
                provider profile)
             proof-architecture-v1.md (proof discipline: defense
                anchor, AMC channel acknowledgment, no overclaim)
             design-system-v3.md §14 (CapabilityDeepDive layout),
                §16 (trust cue content pattern), §17 (cross-lens)
             hardware-ecosystem-map-v3.md §5 (Condition Monitoring
                pillar — verbatim source for VAS + E-IDOS detail)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview; §6 commitment #3 on E-IDOS
                streaming integration honest framing)
Version:     v1.1 — LOCKED. Original v1 locked 2026-05-28; v1.1
                  amendment 2026-05-29 expanded VAS equipment list with
                  maintenance-buyer-tuned vocabulary per cross-spec
                  alignment with /solutions/predictive-maintenance v2.
Date:        2026-05-28 (original) / 2026-05-29 (v1.1 amendment)
Status:      LOCKED.

v1.1 amendment (2026-05-29): §3.2 Card 1 VAS body expanded equipment
list. Original: "rotating machinery, conveyors, gearboxes, and
structural components". Amended: "rotating machinery (pumps, motors,
gearboxes, fans, compressors), conveyors, and structural components".
Drivers:
  1. Maintenance-Manager buyer (buyer-taxonomy v1 §2.4) recognizes the
     concrete equipment names (pumps, motors, fans, compressors) more
     readily than the abstract "rotating machinery" categorical.
  2. /solutions/predictive-maintenance v1 (Phase 2 step 9) used the
     concrete equipment list; cross-spec drift validator workflow
     surfaced the inconsistency. Per pre-launch governance rule
     "prefer clean redesign/unification over compat hedges", aligned
     upstream rather than reverting downstream. User-approved via
     workflow integration plan (2026-05-29).
  3. The amendment preserves the locked source-of-truth structure
     (rotating machinery as the category; specific items in
     parentheses) plus retains "conveyors" and "structural components"
     unchanged.

Original-lock user direction (2026-05-28): "PR #72, we can lock it and
proceed." Direct lock without ChatGPT review pass — user-authorized to
unblock the remaining 4 pillar deep-dive specs (each parallelizable
once this lock confirms the CapabilityDeepDive layout serves the
hardest pillar content).

Second per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 4. Inherits the §1-§8 canonical template from
/capabilities hub spec §9 (LOCKED). Confirms the CapabilityDeepDive
layout (design-system v3 §14) serves the hardest pillar content
(new buyer, new vocabulary, new credibility anchor) → remaining 4
pillar deep-dive specs (connectivity-edge, data-acquisition,
asset-intelligence, operational-intelligence) inherit this pattern
without per-pillar re-derivation.
-->

# `/capabilities/condition-monitoring` — Page Spec v1

**Capability deep-dive for the Condition Monitoring pillar. Tests the `CapabilityDeepDive` layout (design-system v3 §14) against the hardest pillar content: new buyer audience, new vocabulary domain, new credibility anchor. Template-setter for the remaining 4 pillar pages.**

This page is where Maintenance Managers and AMC providers land when they want to evaluate Elpis for reliability engineering. It is **not** a long-read product brochure (product detail belongs on Phase E `/vas` and `/e-idos` pages). It is **not** the solution-narrative page (that's `/solutions/predictive-maintenance`). It is the **capability** view: what this pillar covers, what it eliminates from your BOM, where it sits architecturally, what trust posture applies, what solutions build on it.

Target length: **800-1,200 words page copy** per `/capabilities` hub spec §9 page-type guidance.

---

## 1. IA + buyer alignment

### 1.1 What this page IS (per Phase 2 IA memo v2 §3.3 + design-system v3 §14)

- The capability deep-dive for the Condition Monitoring pillar
- Reader leaves with: *"I now understand what this pillar covers, what it replaces in my current setup, where it sits architecturally, and which solutions it powers"*
- Uses `CapabilityDeepDive` layout — same component, same 9 sections, content adapted to this pillar specifically

### 1.2 What this page IS NOT

- A full product detail page for VAS or E-IDOS (those land in Phase E `/vas` and `/e-idos` pages)
- A predictive-maintenance solution narrative (that's `/solutions/predictive-maintenance` — Phase 2 step 9)
- A platform-level vendor narrative (that's `/platform`)
- An architecture walkthrough (that's `/architecture`)
- A customer-story page (no logos, no testimonials, no named-customer references — those live on `/customers` Phase 3)
- A pricing page (no commercial detail beyond the platform-level engagement model on `/platform`)

### 1.3 Buyer alignment (per buyer-taxonomy v1 §2.4)

**Primary buyer:** Maintenance Manager / AMC provider
- Lands here from capability-first navigation, a Google search for *"vibration condition monitoring"* or *"hydraulic oil monitoring"*, or a referral from a peer
- Wants: predictive maintenance, condition data, alarm patterns, service-history records
- CTA preference: *"Bring us your most-watched machine"* (consultative, respects skepticism) > *"Talk to a reliability engineer"* (peer-to-peer language)
- Vocabulary that lands: vibration analysis, FFT, order analysis, bearing fault detection, oil health intelligence, ISO/NAS cleanliness, predictive maintenance, condition monitoring, AMC channel, reliability engineering, hydraulic and lubrication systems
- Vocabulary that backfires: *"real-time analytics"*, *"big data"*, *"ML/AI predictions"* (without architectural detail), *"self-healing"*

**Secondary buyer:** Plant manager / Ops VP (reliability-pressured)
- Sometimes lands here when a downtime crisis points at rotating-equipment or hydraulic failures specifically
- Wants: a defensible reliability-engineering investment story
- Routes through this page to `/solutions/predictive-maintenance` for outcome-led narrative

**Critical buyer-specific framing:**
- **AMC channel acknowledgment is mandatory.** The AMC provider is a real existing buyer reality (per positioning v3 §5 + buyer-taxonomy v1 §2.4). The page explicitly names this audience — not as a future channel program, but as existing buyers Elpis serves today.
- **Sensor-agnostic E-IDOS is the differentiator.** E-IDOS supports HYDAC, Parker, MP Filter, Argo-Hytos sensors (per hardware-ecosystem-map v3 §5.2). This matters because most condition-monitoring appliances lock customers to one sensor vendor. The page must name the supported brands explicitly.
- **E-IDOS streaming integration is roadmap, not feature.** Per positioning v3 §6 commitment #3 — today E-IDOS operates as a standalone reliability instrument (HMI + thermal printer + Android app + email reports). The streaming integration to EREMOS V2 is roadmap. Honest framing required.
- **Defense / space-agency anchor uses the locked exact phrasing.** Per proof-architecture v1 §6 + positioning v3 §4: *"Deployed in defense and space-agency programs"* — no paraphrasing variations.

---

## 2. Page structure — sections at a glance

Nine sections, top to bottom. Layout from design-system v3 §14 CapabilityDeepDive. Total ~1,100 words page copy (within 800-1,200 target).

| # | Section | Visual mode | Primary component(s) |
|---|---|---|---|
| **1** | Hero (eyebrow + customer question lead + CTAs) | `dark-deep` | `SectionShell` (dark-deep) + verbatim copy + `Button` × 2 |
| **2** | Products in this pillar (VAS + E-IDOS) | `dark` | `SectionShell` (dark) + `CapabilityCard` × 2 with pillar-4 accent |
| **3** | What this pillar eliminates from your BOM | `light` | `SectionShell` (light) + bulleted list |
| **4** | Strategic adjacencies (buyers, industries, deployments) | `light` | `SectionShell` (light) + 3-column grid (desktop) |
| **5** | Where this fits in the Industrial Intelligence Stack | `light-tinted` | `SectionShell` (light-tinted) + `DiagramFrame` focused on Pillar 4 + caption + cross-link to `/architecture` |
| **6** | Trust posture for this pillar | `light-tinted` | §16 trust cue content pattern + cross-link to `/security` |
| **7** | Related solutions | `light` | `SectionShell` (light) + `CapabilityCard` × 2 (solution-card variant) |
| **8** | Cross-lens navigation | `light-tinted` | `SectionShell` (light-tinted, padding="tight") + §17 cross-lens content pattern (3 cards) |
| **9** | Final CTA | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

**Visual:** dark-deep background. No HeroComposite (locked to homepage only per design-system v3 §24 Q1). Eyebrow + headline + customer-question lead + CTA group.

**Copy (verbatim):**

> EYEBROW (small-caps brand-teal, letter-spaced 0.18em)
> CAPABILITY · CONDITION MONITORING
>
> HEADLINE (size.3xl semibold, text.heading)
> Move from break-fix to predict-and-prevent — on the equipment that costs most when it fails.
>
> CUSTOMER QUESTION LEAD (size.lg italic, text.body)
> *"How do I move from break-fix to predict-and-prevent on rotating equipment and hydraulic systems?"*
>
> PRIMARY CTA (`Button.primary.lg`):
> Bring us your most-watched machine
> HREF: `/contact?intent=condition-monitoring-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Talk to a reliability engineer
> HREF: `/contact?intent=condition-monitoring-engineering`

**Behavior:** static. No animation. No background imagery (per design-governance §2.3 — no decorative imagery on Phase 2 capability pages).

**Anti-patterns:**
- ❌ No outcome metric in headline (e.g., *"Cut MTBF by 40%"*) — per proof-architecture v1 §3, outcome metrics live on `/solutions/predictive-maintenance`, never on capability pages
- ❌ No customer logo strip here (platform-level only)
- ❌ No *"AI-powered predictive maintenance"* framing (vocabulary backfire per buyer-taxonomy)

---

### 3.2 Section 2 — Products in this pillar

**Visual:** dark background. Two `CapabilityCard` instances with pillar-4 accent. Side-by-side on desktop, stacked on mobile.

**Section title:**

> EYEBROW (small-caps brand-teal):
> PRODUCTS IN THIS PILLAR

**Per-card content (verbatim, condensed from hardware-ecosystem-map v3 §5):**

#### Card 1 — VAS (pillar-4 accent)

> EYEBROW: VIBRATION CONDITION MONITORING
> TITLE: VAS — Vibration Analyser System
> BODY (size.base, regular):
> Detects deviations from normal vibration patterns on rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, and structural components. Identifies bearing issues, imbalance, misalignment, looseness, and cracks *before* failure. Built on the mDAQ acquisition platform with specialized analytics — time-domain (peak detection, RMS severity), frequency-domain (FFT, spectrum), order analysis, Bode plot, polar plot, cascade, waterfall, failure-mode mapping.
> FOOTER (size.sm, text.muted): mDAQ platform · specialized vibration analytics
> LINK (text-only with arrow, brand-teal):
> *(Phase E product detail — coming soon)*

#### Card 2 — E-IDOS (pillar-4 accent)

> EYEBROW: OIL HEALTH INTELLIGENCE
> TITLE: E-IDOS — Hydraulic & Lubrication Condition Monitoring
> BODY (size.base, regular):
> Continuously measures hydraulic and lubrication-oil health — solid particle contamination, water saturation, oil flow — in both online and offline states. Logs to ISO 4406 / NAS 1638 cleanliness standards. **Sensor-agnostic on the contamination input side: supports HYDAC, Parker, MP Filter, and Argo-Hytos sensors.** Built-in touch-screen HMI, on-board thermal printer, BLE Android app — designed for both in-house maintenance teams and the AMC provider channel.
> FOOTER (size.sm, text.muted): Sensor-agnostic appliance · HMI + thermal printer + Android app
> LINK (text-only with arrow, brand-teal):
> *(Phase E product detail — coming soon)*

**Anti-patterns:**
- ❌ No outcome metrics in card bodies
- ❌ No marketing-flavored card eyebrows (must use the actual analytical specialty)
- ❌ No "trusted by Fortune 500" overclaim
- ❌ No omission of E-IDOS sensor-agnostic differentiator — it's the strongest single position vs IFM / SKF CM / Bently Nevada / oil-lab vendors

---

### 3.3 Section 3 — What this pillar eliminates from your BOM

**Visual:** light background. Drives commercial insight — these are the line items removed from a customer's existing bill of materials.

**Copy (verbatim):**

> EYEBROW (small-caps, dark on light):
> WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
>
> SUBHEAD (size.md, text.body-light, max-width 70ch):
> Condition Monitoring is an *expansion* product. It removes vendor relationships and instrument categories rather than adding to them.
>
> BULLETED LIST (size.base, regular):
>
> - A dedicated vibration analyser console — often a five-figure standalone instrument with a separate software stack
> - A standalone oil-condition monitor + the oil-laboratory contract that ships samples for off-site analysis
> - Manual handheld vibration spot-checks and the operator hours spent doing them
> - Service-interval guesswork on hydraulic and lubrication systems
> - A separate third-party predictive-maintenance vendor relationship
> - Manual oil-sample collection and shipping logistics
> - The instrument-fragmentation that comes from buying vibration analytics from one vendor and fluid analytics from another

**Anti-patterns:**
- ❌ No specific dollar figures (per proof-architecture v1 — pricing detail is Phase 3 `/pricing`)
- ❌ No competitor names (per proof-architecture v1 §8 + sales-objection-guide governance — competitive framing is `/platform` + internal sales guide territory, not capability pages)
- ❌ No "save up to N%" claims (proof-architecture v1 §5.2 — banned without source)

---

### 3.4 Section 4 — Strategic adjacencies

**Visual:** light background. Three columns on desktop, stacked on mobile.

**Copy (verbatim):**

> EYEBROW:
> WHO IT'S FOR · WHERE IT DEPLOYS
>
> COLUMN 1 — BUYERS:
> - **In-house Maintenance Manager** — the reliability-engineering owner inside the plant
> - **AMC provider channel** — Annual Maintenance Contract service companies delivering reliability diagnostics to *their* customers (existing buyer reality, not future channel program)
> - **Plant engineer (reliability-focused)** — sizing the deployment, owning the install
>
> COLUMN 2 — INDUSTRIES (deployment evidence):
> - Oil & Gas (surface and downhole hydraulic systems)
> - Power & Energy (generation-equipment rotating monitoring)
> - Mining & Construction (heavy hydraulic systems)
> - Manufacturing (CNC spindles, conveyor systems, gearboxes)
> - Aerospace (ground-support equipment; precision rotating equipment)
>
> COLUMN 3 — DEPLOYMENT FOOTPRINT:
> - Deployed in defense and space-agency programs
> - Operating across India and the Middle East
> - AMC providers serving industrial hydraulic customers across both regions
> - On-site reliability diagnostics delivered via portable instrument workflow (E-IDOS) + continuous monitoring (VAS)

**Critical proof discipline (per proof-architecture v1 §4 + §6):**
- Defense / space-agency phrasing is **exact lock**: *"Deployed in defense and space-agency programs"* — no variations
- AMC channel phrasing is **exact lock**: *"AMC provider channel — Annual Maintenance Contract service companies..."* — must acknowledge existing-reality, not future-program
- Geography phrasing is **exact lock**: *"Operating across India and the Middle East"*

**Anti-patterns:**
- ❌ Naming any specific defense or space-agency customer
- ❌ Naming any specific AMC partner (channel partner names stay anonymous until Phase 4 partner portal)
- ❌ Adding *"and growing"* / *"global"* / *"worldwide"* to the geography line

---

### 3.5 Section 5 — Where this fits in the Industrial Intelligence Stack

**Visual:** light-tinted background. `DiagramFrame` focused on Pillar 4's position in the Industrial Intelligence Stack. Caption + cross-link to `/architecture`.

**Copy (verbatim):**

> EYEBROW:
> WHERE IT FITS
>
> SECTION TITLE (size.lg semibold):
> Condition Monitoring is the specialty analytics path of the Industrial Intelligence Stack.
>
> BODY (size.md, text.body-light, max-width 70ch):
> Pillars 1-3 (Connectivity & Edge, Data Acquisition, Asset Intelligence) handle general telemetry capture and edge delivery. Pillar 4 (Condition Monitoring) adds specialized condition-signal analytics on top of the same acquisition platform — VAS for vibration on rotating equipment, E-IDOS for oil health on hydraulic systems. Signals from Pillar 4 feed Pillar 5 (Operational Intelligence) where condition alarms become incident workflows and reports — once the E-IDOS streaming integration into EREMOS V2 ships (near-term roadmap; today E-IDOS operates as a standalone reliability instrument with HMI + thermal printer + Android app + email reports).
>
> DIAGRAM FRAME (DiagramFrame component, source = architecture-diagram-v2-light.svg with viewBox focused on Pillar 4 layer)
>
> CAPTION (size.sm italic, centered):
> *Pillar 4 is the reliability-engineering specialty in the Stack. See the full Industrial Intelligence Stack → `/architecture`*

**Critical honest-framing discipline (per positioning v3 §6 commitment #3):**
- The E-IDOS streaming integration to EREMOS V2 is explicitly named as **roadmap, not feature.** The current standalone-instrument operating mode (HMI + thermal printer + BLE + email) is what ships today.
- No marketing language that implies the streaming integration is already live.

**Anti-patterns:**
- ❌ Embedding the full architecture diagram (the `DiagramFrame` is focused on Pillar 4 — full diagram is `/architecture`'s territory)
- ❌ Implying E-IDOS streaming integration is current behavior

---

### 3.6 Section 6 — Trust posture for this pillar

**Visual:** light-tinted background. Per §16 trust cue content pattern (design-system v3).

**Copy (verbatim):**

> *(rendered as the §16 trust cue pattern — vertical accent line, eyebrow, body, cross-link)*
>
> EYEBROW (small-caps brand-teal):
> TRUST POSTURE
>
> BODY (size.base):
> Condition data is some of the most sensitive operational data a plant produces — bearing failures, hydraulic-system health, and maintenance histories all sit close to capital-equipment value and customer reliability commitments. Customer-controlled telemetry routing in EdgeConnect means you decide which condition signals route to your in-house maintenance system, which route to your AMC provider, and which stay air-gapped at the instrument. Per-tenant isolation in EREMOS V2 ensures cross-customer condition data never blends.
>
> CROSS-LINK (text-only with arrow):
> Read the full operational trust posture → `/security`

**Per buyer-taxonomy v1 §2.4 cue focus for this page:** customer-controlled telemetry, data sovereignty. The cue does NOT duplicate `/security` philosophy — it surfaces ONE relevant property (data sovereignty for condition signals) and cross-links for the rest.

**Anti-patterns:**
- ❌ Repeating the trust posture trio (`/security` is authoritative)
- ❌ Naming a specific compliance framework (proof-architecture v1 §3 + honest compliance posture)
- ❌ Adding more than one trust cue here (signal dilution per §16)

---

### 3.7 Section 7 — Related solutions

**Visual:** light background. Two `CapabilityCard` variants in solution-card mode.

**Copy (verbatim):**

> EYEBROW:
> RELATED SOLUTIONS
>
> SUBHEAD (size.md, max-width 60ch):
> Outcome-organised stories built on the Condition Monitoring pillar.

#### Card 1 — Predictive Maintenance (primary)

> EYEBROW: SOLUTION · PREDICTIVE MAINTENANCE
> TITLE: From break-fix to predict-and-prevent
> BODY: How VAS + E-IDOS + EREMOS V2 work together to catch failures three weeks early instead of three hours late.
> LINK: Read the solution → `/solutions/predictive-maintenance`

#### Card 2 — OEM Machine Monitoring (asset utilization angle for fleet operators)

> EYEBROW: SOLUTION · OEM MACHINE MONITORING
> TITLE: Connected equipment, reliability data included
> BODY: For OEMs shipping equipment with VAS or E-IDOS embedded — service-organisation visibility into installed-base reliability without coercing customer-IT.
> LINK: Read the solution → `/solutions/oem-machine-monitoring` *(existing v2; v3 in Phase E)*

**Anti-patterns:**
- ❌ More than 3 related solutions (signal dilution)
- ❌ Including solutions that don't draw from Condition Monitoring as primary (e.g., `/solutions/cnc-machining` is operational visibility, not condition monitoring)

---

### 3.8 Section 8 — Cross-lens navigation

**Visual:** light-tinted, `SectionShell.padding="tight"`. Per §17 cross-lens pattern + per-surface preset for `/capabilities/<pillar>` (design-system v3 §17 + memo v2 §5.2).

**Three cards rendered:**

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | ARCHITECTURE | How does this pillar fit the data flow? | `/architecture` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | The outcome-organised version of this pillar | `/solutions/predictive-maintenance` |
| 3 | CAPABILITIES | Back to all 5 pillars | `/capabilities` |

**Section headline:**

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

**Visual:** dark-deep background. `CTASection` component.

**Copy (verbatim, per buyer-taxonomy v1 §2.4 Maintenance / AMC CTA preference):**

> EYEBROW (small-caps brand-teal):
> NEXT STEP
>
> HEADLINE (size.2xl bold):
> Pick the machine you're most worried about. We'll scope a deployment.
>
> SUBHEAD (size.md, text.body, max-width 60ch):
> Whether you're an in-house maintenance team running predict-and-prevent on rotating machinery, or an AMC provider delivering reliability diagnostics to customers, the first conversation is about one specific machine — yours or theirs. Bring us the one you're most worried about.
>
> PRIMARY CTA (`Button.primary.lg`):
> Bring us your most-watched machine
> HREF: `/contact?intent=condition-monitoring-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Talk to a reliability engineer
> HREF: `/contact?intent=condition-monitoring-engineering`

**Why these CTAs:** per buyer-taxonomy v1 §2.4 — Maintenance Manager / AMC provider rewards *"Bring us your most-watched machine"* (consultative, respects skepticism, scoped to one specific deployment) and *"Talk to a reliability engineer"* (peer-to-peer; signals technical competence). Generic *"Book a demo"* loses both audiences immediately ("we want to see real signals from real machines, not slideware").

**Anti-patterns:**
- ❌ *"Schedule a free assessment"* (consultant-flavored; backfires)
- ❌ *"Get your free vibration analysis report"* (no fabricated free offers)
- ❌ Three CTAs (max one primary + one secondary)

---

## 4. Components used

| Component | Source | Used in section |
|---|---|---|
| `SectionShell` | design-system v2 §2 | every section (mode variants per §2 table above) |
| `CapabilityCard` (pillar-4 accent variant) | design-system v2 §3 + v3 §14 | §3.2 products; §3.7 related solutions; §3.8 cross-lens |
| `Button` (primary + secondary, size lg) | design-system v2 §1 | §3.1 hero CTAs; §3.9 final CTA |
| `DiagramFrame` | design-system v2 §9 | §3.5 "where it fits" stack diagram |
| `CTASection` | design-system v2 §8 | §3.9 final CTA |
| §16 trust cue content pattern | design-system v3 §16 | §3.6 trust posture for this pillar |
| §17 cross-lens content pattern | design-system v3 §17 | §3.8 cross-lens navigation |

**No new components introduced.** Composes entirely from design-system v3 LOCKED. Per the additive-only commitment.

---

## 5. Verbatim copy summary

For the engineering team and copywriters, all page copy collected in one place:

```
SECTION 1 — HERO

CAPABILITY · CONDITION MONITORING

Move from break-fix to predict-and-prevent — on the equipment that
costs most when it fails.

"How do I move from break-fix to predict-and-prevent on rotating
equipment and hydraulic systems?"

[Bring us your most-watched machine]  [Talk to a reliability engineer]


SECTION 2 — PRODUCTS IN THIS PILLAR

PRODUCTS IN THIS PILLAR

[Card 1 — VAS]
VIBRATION CONDITION MONITORING
VAS — Vibration Analyser System
Detects deviations from normal vibration patterns on rotating
machinery, conveyors, gearboxes, and structural components.
Identifies bearing issues, imbalance, misalignment, looseness,
and cracks before failure. Built on the mDAQ acquisition platform
with specialized analytics — time-domain (peak detection, RMS
severity), frequency-domain (FFT, spectrum), order analysis, Bode
plot, polar plot, cascade, waterfall, failure-mode mapping.
mDAQ platform · specialized vibration analytics
(Phase E product detail — coming soon)

[Card 2 — E-IDOS]
OIL HEALTH INTELLIGENCE
E-IDOS — Hydraulic & Lubrication Condition Monitoring
Continuously measures hydraulic and lubrication-oil health — solid
particle contamination, water saturation, oil flow — in both online
and offline states. Logs to ISO 4406 / NAS 1638 cleanliness
standards. Sensor-agnostic on the contamination input side: supports
HYDAC, Parker, MP Filter, and Argo-Hytos sensors. Built-in touch-
screen HMI, on-board thermal printer, BLE Android app — designed
for both in-house maintenance teams and the AMC provider channel.
Sensor-agnostic appliance · HMI + thermal printer + Android app
(Phase E product detail — coming soon)


SECTION 3 — WHAT THIS PILLAR ELIMINATES FROM YOUR BOM

WHAT THIS PILLAR ELIMINATES FROM YOUR BOM

Condition Monitoring is an expansion product. It removes vendor
relationships and instrument categories rather than adding to them.

- A dedicated vibration analyser console — often a five-figure
  standalone instrument with a separate software stack
- A standalone oil-condition monitor + the oil-laboratory contract
  that ships samples for off-site analysis
- Manual handheld vibration spot-checks and the operator hours
  spent doing them
- Service-interval guesswork on hydraulic and lubrication systems
- A separate third-party predictive-maintenance vendor relationship
- Manual oil-sample collection and shipping logistics
- The instrument-fragmentation that comes from buying vibration
  analytics from one vendor and fluid analytics from another


SECTION 4 — STRATEGIC ADJACENCIES

WHO IT'S FOR · WHERE IT DEPLOYS

[Column 1 — BUYERS]
- In-house Maintenance Manager — the reliability-engineering owner
  inside the plant
- AMC provider channel — Annual Maintenance Contract service
  companies delivering reliability diagnostics to their customers
  (existing buyer reality, not future channel program)
- Plant engineer (reliability-focused) — sizing the deployment,
  owning the install

[Column 2 — INDUSTRIES]
- Oil & Gas (surface and downhole hydraulic systems)
- Power & Energy (generation-equipment rotating monitoring)
- Mining & Construction (heavy hydraulic systems)
- Manufacturing (CNC spindles, conveyor systems, gearboxes)
- Aerospace (ground-support equipment; precision rotating equipment)

[Column 3 — DEPLOYMENT FOOTPRINT]
- Deployed in defense and space-agency programs
- Operating across India and the Middle East
- AMC providers serving industrial hydraulic customers across both
  regions
- On-site reliability diagnostics delivered via portable instrument
  workflow (E-IDOS) + continuous monitoring (VAS)


SECTION 5 — WHERE IT FITS IN THE INDUSTRIAL INTELLIGENCE STACK

WHERE IT FITS

Condition Monitoring is the specialty analytics path of the
Industrial Intelligence Stack.

Pillars 1-3 (Connectivity & Edge, Data Acquisition, Asset
Intelligence) handle general telemetry capture and edge delivery.
Pillar 4 (Condition Monitoring) adds specialized condition-signal
analytics on top of the same acquisition platform — VAS for
vibration on rotating equipment, E-IDOS for oil health on hydraulic
systems. Signals from Pillar 4 feed Pillar 5 (Operational
Intelligence) where condition alarms become incident workflows and
reports — once the E-IDOS streaming integration into EREMOS V2
ships (near-term roadmap; today E-IDOS operates as a standalone
reliability instrument with HMI + thermal printer + Android app +
email reports).

[Diagram: Pillar 4 layer focus of architecture-diagram-v2]

Pillar 4 is the reliability-engineering specialty in the Stack.
See the full Industrial Intelligence Stack → /architecture


SECTION 6 — TRUST POSTURE

TRUST POSTURE

Condition data is some of the most sensitive operational data a
plant produces — bearing failures, hydraulic-system health, and
maintenance histories all sit close to capital-equipment value and
customer reliability commitments. Customer-controlled telemetry
routing in EdgeConnect means you decide which condition signals
route to your in-house maintenance system, which route to your AMC
provider, and which stay air-gapped at the instrument. Per-tenant
isolation in EREMOS V2 ensures cross-customer condition data never
blends.

Read the full operational trust posture → /security


SECTION 7 — RELATED SOLUTIONS

RELATED SOLUTIONS

Outcome-organised stories built on the Condition Monitoring pillar.

[Card 1]
SOLUTION · PREDICTIVE MAINTENANCE
From break-fix to predict-and-prevent
How VAS + E-IDOS + EREMOS V2 work together to catch failures three
weeks early instead of three hours late.
Read the solution → /solutions/predictive-maintenance

[Card 2]
SOLUTION · OEM MACHINE MONITORING
Connected equipment, reliability data included
For OEMs shipping equipment with VAS or E-IDOS embedded — service-
organisation visibility into installed-base reliability without
coercing customer-IT.
Read the solution → /solutions/oem-machine-monitoring


SECTION 8 — CROSS-LENS

Looking for the same thing from another angle?

[Card 1]  ARCHITECTURE
          How does this pillar fit the data flow?
          → /architecture

[Card 2]  SOLUTION · PREDICTIVE MAINTENANCE
          The outcome-organised version of this pillar
          → /solutions/predictive-maintenance

[Card 3]  CAPABILITIES
          Back to all 5 pillars
          → /capabilities


SECTION 9 — FINAL CTA

NEXT STEP

Pick the machine you're most worried about. We'll scope a deployment.

Whether you're an in-house maintenance team running predict-and-
prevent on rotating machinery, or an AMC provider delivering
reliability diagnostics to customers, the first conversation is
about one specific machine — yours or theirs. Bring us the one
you're most worried about.

[Bring us your most-watched machine]  [Talk to a reliability engineer]
```

**Total page copy:** ~1,100 words (within the 800-1,200 target for pillar deep-dives per `/capabilities` hub spec §9).

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 + page-spec template anti-patterns:

| Don't | Why |
|---|---|
| Imply E-IDOS streaming integration to EREMOS V2 is current behavior | Per positioning v3 §6 commitment #3 — it's roadmap, not feature. Honest framing is the trust signal. |
| Add fabricated outcome metrics ("Cut MTBF by 40%", "Reduce maintenance costs by 30%") | Per proof-architecture v1 §5.1 — outcome metrics require customer-supplied input or named customer sign-off; floating "typical" claims banned |
| Name specific defense / space-agency customers | Per proof-architecture v1 §4 + positioning v3 §4 — anchor stays anonymous; specific names confidential |
| Name specific AMC partners | Per proof-architecture v1 §4.2 — AMC partner-level naming waits for Phase 4 partner portal |
| Substitute "Deployed in defense and space-agency programs" with paraphrases ("Defense industry deployments", "Defense / space programs", etc.) | Per proof-architecture v1 §6 — locked exact phrasing; variations drift the proof |
| Use *"AI-powered predictive maintenance"* / *"ML predictions"* / *"self-healing"* framing | Per buyer-taxonomy v1 §2.4 — vocabulary backfires with Maintenance / AMC buyer |
| Add customer logos / TrustBand to this page | Trust signaling is platform-level; trust *cues* (TrustCueBlock content pattern) are page-level |
| Embed the full architecture diagram | Use `DiagramFrame` focused on Pillar 4 only; full diagram is `/architecture`'s territory |
| List more than 3 related solutions | Signal dilution (per design-system v3 §3 anti-patterns); 2 is the right count for Condition Monitoring's current solution coverage |
| Add hardware specification tables (BOM, ports, voltages) | Hardware specs are Phase E product page territory (`/vas`, `/e-idos`); capability pages stay capability-level |
| Add competitor names ("better than IFM / SKF CM / Bently Nevada") | Per proof-architecture v1 §8 — competitive comparison is sales-objection-guide territory, not capability page |
| Include free-instrument-trial / free-vibration-report offers | Per buyer-taxonomy v1 §2.4 — Maintenance / AMC reward consultative engagement, not free offers |

---

## 7. Sign-off checklist (v1 lock)

- [ ] Page copy fits the 800-1,200 word target (current draft: ~1,100 words — well within range)
- [ ] All 9 sections present in `CapabilityDeepDive` layout order per design-system v3 §14
- [ ] Hero customer-question lead uses verbatim text from `hardware-ecosystem-map-v3.md §1` (no paraphrasing)
- [ ] VAS and E-IDOS product cards use accurate analytical capability descriptions from hardware-ecosystem-map v3 §5.1 + §5.2
- [ ] E-IDOS sensor-agnostic positioning explicitly names HYDAC, Parker, MP Filter, Argo-Hytos
- [ ] E-IDOS streaming integration to EREMOS V2 framed as **roadmap, not current behavior** per positioning v3 §6 commitment #3
- [ ] AMC channel explicitly named in §3.4 (existing reality, not future program)
- [ ] Defense / space-agency anchor uses locked exact phrasing *"Deployed in defense and space-agency programs"* per proof-architecture v1 §6
- [ ] Geography anchor uses locked exact phrasing *"Operating across India and the Middle East"*
- [ ] §3.6 trust cue cross-links to `/security` (per memo v2 §5.5 + §16 trust cue content pattern); does NOT duplicate trust philosophy
- [ ] §3.5 diagram is `DiagramFrame` focused on Pillar 4 (NOT the full architecture diagram embedded)
- [ ] Cross-lens block uses 3 cards per §17 preset for `/capabilities/<pillar>`
- [ ] Final CTA uses "Bring us your most-watched machine" + "Talk to a reliability engineer" per buyer-taxonomy v1 §2.4 — NO "Book a demo", NO "Schedule a free assessment"
- [ ] No customer logos, no fabricated outcome metrics, no compliance framework claims, no competitor names
- [ ] All vocabulary passes buyer-taxonomy v1 §2.4 discipline (lands: FFT, ISO/NAS, AMC, reliability engineering; backfires: real-time analytics, big data, ML/AI predictions, self-healing)
- [ ] All components used are design-system v3 LOCKED — no new components introduced
- [ ] Page-spec structure follows the §9 canonical template from `/capabilities` hub spec
- [ ] Mobile responsive — desktop columns collapse cleanly to single column at < 768px
- [ ] WCAG AA contrast (design-governance §2.5)

---

## 8. Out of scope for v1

- **Full VAS product detail.** Phase E `/vas` product page covers: full analytical capability inventory, hardware specs, deployment patterns, certifications, integration patterns.
- **Full E-IDOS product detail.** Phase E `/e-idos` product page covers: full sensor compatibility matrix, ISO/NAS reporting specifics, HMI + thermal printer + BLE workflow, AMC workflow patterns, mobile-app screenshots.
- **Predictive maintenance narrative.** `/solutions/predictive-maintenance` (Phase 2 step 9) covers the outcome-organised story.
- **Industries-specific framings.** Phase 3 `/industries/<industry>` may filter Condition Monitoring relevance to specific verticals (Oil & Gas hydraulic, Aerospace ground-support, Mining heavy hydraulics, Manufacturing CNC rotating equipment).
- **CMMS / EAM integration patterns.** Customer common question per buyer-taxonomy v1 §2.4 — not in Phase 2 scope; honest framing required when raised in sales conversation.
- **AMC partner enablement collateral.** Phase 4 `/partners/<region>` activates the formalized AMC channel program.
- **Pricing detail for VAS / E-IDOS.** Phase 3 `/pricing` or commercial-engagement teaser on `/platform` (per amendment v3 §1.7).
- **Customer stories.** Phase 3 `/customers` and `/customers/<story>` once Phase 3 customer-story sign-off lands.

---

*`/capabilities/condition-monitoring` Page Spec v1 — LOCKED 2026-05-28 by user direction (no ChatGPT review pass requested). Second per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 4. Template-setter for the remaining 4 pillar deep-dives — the `CapabilityDeepDive` layout is now CONFIRMED for use across all 5 pillar pages. Inherits §1-§8 canonical structure from `page-capabilities-hub-spec-v1.md §9`. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.4, proof-architecture v1, design-system v3 §14 + §16 + §17, hardware-ecosystem-map v3 §5, positioning v3 §6 commitment #3.*
