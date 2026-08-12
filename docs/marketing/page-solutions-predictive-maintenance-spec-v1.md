<!--
File:        docs/marketing/page-solutions-predictive-maintenance-spec-v1.md
Purpose:     Page spec for /solutions/predictive-maintenance — solution
             depth-example for the condition-monitoring outcome. Eighth
             of 10 Phase 2 per-page specs. Uses SolutionPanel layout
             from design-system v3 §15.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), Phase 2 step 10 + 11 spec authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   solution-cnc-machining-v2.md (LOCKED Phase 1 marketing
                track — solution-page voice + structure precedent)
             page-capabilities-condition-monitoring-spec-v1.md (LOCKED
                source-of-truth for VAS + E-IDOS positioning; cross-
                linked as the capability page)
             page-capabilities-data-acquisition-spec-v1.md (LOCKED v2
                — mDAQ hardware-platform cross-link)
             page-capabilities-operational-intelligence-spec-v1.md
                (LOCKED v1 — EREMOS V2 incident-workflow cross-link)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page-type FAQ governance — /solutions/<solution>
                = YES; metadata governance; "How this differs from..."
                emerging pattern)
             page-solutions-hub-spec-v1.md (LOCKED v2 — Card 2
                Predictive Maintenance summary points here)
             page-architecture-spec-v1.md (LOCKED v2 — cross-link
                target for "See full architecture")
             phase2-ia-scope-memo-v2.md §3 (IA parent — /solutions
                scope) + amendment v3 (sequencing step 9)
             buyer-taxonomy-v1.md §2.4 (Maintenance Manager / AMC
                provider — primary buyer) + §2.2 (Plant manager —
                secondary, downtime-prevention angle)
             proof-architecture-v1.md (proof discipline — no
                fabricated downtime-reduction percentages; AMC channel
                economics stay soft until commercial-engagement teaser
                lands on /platform)
             design-system-v3.md §15 (SolutionPanel layout — LOCKED),
                §16 (TrustCueBlock), §17 (CrossLensBlock — LOCKED
                preset for /solutions/<solution>: /capabilities/
                <related-pillar> + /architecture + /solutions)
             hardware-ecosystem-map-v3.md §5 (Condition Monitoring
                pillar — VAS + E-IDOS source-of-truth)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview — §Glossary "Condition Monitoring
                duo" + "Fluid intelligence")
             solution-oem-machine-monitoring-v2.md (LOCKED — anti-
                overclaim discipline precedent for hedged claim
                wording; "cut" not "no" for outcome verbs)
             security-page-copy-v2.md (cross-link target for trust
                cues — full operational trust posture)
Version:     v2 — LOCKED after Pass 1 ChatGPT review + 4-agent pre-lock
                  validation workflow (cross-spec drift check +
                  SolutionPanel §15 coverage check + asset-criticality
                  coverage check + synthesizer integration). Workflow
                  caught 2 HIGH-severity blocking issues ChatGPT missed
                  (E-IDOS streaming roadmap honesty + mDAQ-E-IDOS
                  conflation) + 9 must-apply cross-spec drift items;
                  all resolved before v2 lock.
Date:        2026-05-29 (v2 lock)
Status:      LOCKED.

Eighth per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 9. Solution depth-example page — uses SolutionPanel
layout from design-system v3 §15 (LOCKED, 10-section structure with
ArchitecturePanel solution-annotated subset + inline FAQ +
TrustCueBlock + CrossLensBlock).

Page-structure approval: SolutionPanel §15 structure is LOCKED in
design-system v3 — no structural decision needed. Content
commitments (hero outcome direction, "How this differs from
calendar-based PM" callout, 3 deployment shapes, 6 FAQ topics,
2 trust cues, architecture-annotated diagram approach, anti-pattern
lock against E-IDOS thermal/electrical regression, vertical-localized
final CTA) all approved by user direction 2026-05-29 before drafting.

Word-count target: 1,500-1,800 words per /capabilities hub §9
page-type guidance for /solutions/<solution>. Current draft:
~1,640 words page copy.

Inline FAQ (§3.5 "Common questions") included per /capabilities hub
§9 per-page-type FAQ governance (/solutions/<solution> is YES — and
SolutionPanel §15 §3.5 already specifies inline Q&A as part of the
locked structure).

§1.4 Page metadata block included per /capabilities hub §9 metadata
governance lock (PR #71).

"How this differs from calendar-based preventive maintenance" callout
(§3.3) applied per /capabilities hub §9 emerging-pattern governance
(reviewer-validated on /capabilities/asset-intelligence v2). Locked
pattern: place inside the "How Elpis Solves This" section as a
bordered callout block.

Source-of-truth-verified content (workflow precedent from /solutions
hub v2 lock):
  - E-IDOS positioned as hydraulic + lubrication oil-health (NOT
    thermal/electrical) per locked condition-monitoring spec v1,
    hardware-ecosystem-map v3 §5.2, positioning v3 §Glossary
  - "Cut downtime" / "Cut emergency dispatches" framing per OEM v2
    spec hedging precedent (verbatim verb pattern from
    solution-oem-machine-monitoring-v2.md §1 hero "Cut truck rolls")
  - mDAQ + VAS + E-IDOS deployment shape per data-acquisition v2 +
    condition-monitoring v1 specs

Cross-link discipline: this page cross-links to /capabilities/
condition-monitoring as the authoritative capability source. Per
the /solutions hub v2 governance side-flag: PR #72 (condition-
monitoring) is LOCKED (at v1.1 amendment 2026-05-29) but not yet
merged to master — verify PR #72 merge status before publishing
this page to the live website. Same governance applies here as on
the /solutions hub.

v2 changes from v1 (applied 2026-05-29):

BLOCKING fixes (workflow HIGH-severity findings ChatGPT missed):
  - B1: E-IDOS streaming integration honest framing. Added explicit
    "near-term roadmap, not current behavior" caveats in §3.3 ¶2
    (italicized callout below the paragraph) and §3.4 'From Hardware
    / E-IDOS' bucket header. Revised §3.5 FAQ Q5 to NOT imply E-IDOS
    streams condition data today. Added §6 anti-pattern row guarding
    against regression. Per positioning v3 §6 commitment #3 + locked
    condition-monitoring v1 §3.5 + locked operational-intelligence v1
    §6 anti-pattern.
  - B2: mDAQ platform claim for E-IDOS corrected. VAS runs on mDAQ;
    E-IDOS is a standalone sensor-agnostic appliance with its own
    hardware platform (HMI + thermal printer + BLE Android app).
    Corrected §3.7 architecture annotation, §3.5 FAQ Q5, §3.4 'From
    Hardware' bucket, and §3.3 ¶1 ("E-IDOS ... is a standalone
    sensor-agnostic appliance"). Added §6 anti-pattern row.

Cross-spec drift / governance must-apply:
  - A3: VAS acronym expansion "Vibration Analyser System" (NOT
    "Vibration Analytics Service"). Matches locked condition-
    monitoring v1 §3.2.
  - A4: VAS failure-mode list restored 'cracks' + uses 'bearing
    issues' (not 'bearing degradation'). Matches locked condition-
    monitoring v1 §3.2.
  - A5: E-IDOS oil-health failure-mode coverage bullet added —
    introduces "lubrication breakdown / oil degradation" vocabulary
    that maintenance buyers expect (also consensus with ChatGPT #4).
  - A6: 'Asset criticality' named explicitly in §3.3 (deployment
    shapes prepended sentence) and §3.6 (renamed Outcomes bullet 4)
    — was previously only in FAQ Q6. Consensus with ChatGPT #2.
  - A7: Concrete asset types repeated in §3.6 Outcomes bullet 1
    (pump, motor, gearbox, fan, compressor, hydraulic system) —
    was previously only abstract "instrumented assets". Consensus
    with ChatGPT #3.
  - A8: Governance side-flag for /solutions hub coordination —
    when this page ships live, /solutions hub v2 §3.2 Card 2 must
    remove the "Coming soon" status pill and swap the pre-live
    link from /capabilities/condition-monitoring to /solutions/
    predictive-maintenance per solutions-hub-v2 pre-live link
    policy.
  - A9: §3.7 Col-2 solution-scoped clarifier — "For predictive
    maintenance, Col 2 is the Acquisition peer (mDAQ + VAS +
    E-IDOS); the EdgeConnect peer is not required for this
    solution." Prevents reader misreading the solution-annotated
    diagram as the full peer-architecture truth.
  - A10: ChatGPT-proposed anti-pattern added — predictive
    maintenance does not replace reliability engineering practice.
    Platform improves trigger quality; engineering judgment stays
    with the reliability function.
  - A11: §3.4 mDAQ Ethernet bullet trimmed to "optional Ethernet"
    + cross-link to /capabilities/data-acquisition for full hardware
    specs. Matches locked data-acquisition v2 §3.2 ("optional"
    Ethernet, not standard).

Judgement-call decisions:
  - J1 (whatsIncluded schema): collapsed §3.4 from 4 product-buckets
    (mDAQ / VAS / E-IDOS / EREMOS V2) to §15-compliant 2 buckets
    (From Hardware + From EREMOS V2). EdgeConnect bucket omitted
    because not used in predictive-maintenance. Per design-system
    v3 §15 schema `{ edgeConnect?, eremosV2?, hardwareProducts? }`
    with `?` optional. Sub-grouping within From Hardware names mDAQ
    / VAS / E-IDOS as product sub-sections.
  - J2 (VAS equipment list reconciliation): updated condition-
    monitoring v1 upstream (v1.1 amendment 2026-05-29) to include
    maintenance-buyer-tuned vocabulary ("rotating machinery (pumps,
    motors, gearboxes, fans, compressors), conveyors, structural
    components"). Predictive-maintenance v2 now uses the aligned
    list. Pre-launch governance rule "prefer clean redesign/
    unification over compat hedges" — fixed upstream rather than
    downstream.
  - J3 (FAQ Q4 sensor-fitness sentence): applied per ChatGPT
    optional #1 + workflow recommendation.
  - J6 (asset-criticality vocabulary roster): applied per validator
    Item A nice-to-have — Maintenance Managers Ctrl-F for this term.
  - J4 (architecture annotation trim): NOT applied per workflow
    NO-CHANGE recommendation — 4 annotations is at the §5.A discipline
    limit, not over; all four serve distinct buyer concerns.
  - J5 (§3.6 title rename): NOT applied per workflow NO-CHANGE
    recommendation — "Outcomes You Can Hold Us To" is locked
    /capabilities + /solutions hub vocabulary; the accountability
    framing IS the anti-overclaim signal.

Sections receiving "no change" (validator + ChatGPT consensus,
preserved verbatim):
  §3.1 hero outcome-led headline, §3.2 customer pain (3 narrative
  paragraphs), §3.3 elpisApproach structure (4 bolded-lead paragraphs
  + calendar-based-PM callout — except corrections within them),
  §3.5 FAQ Q1-Q3 + Q6 (Q4 + Q5 amended for must-fix), §3.6 Outcomes
  bullets 2-3 + 5-7 (bullet 1 + bullet 4 amended for must-fix),
  §3.8 trust cues, §3.9 cross-lens LOCKED preset, §3.10 final CTA
  "Bring us your most-watched asset", anti-overclaim "cut" verb
  framing throughout, CMMS/SCADA "beside-not-replacing" framing.

Pass 1 ChatGPT review verdict (2026-05-29):
  "This is the strongest solution-page spec in the entire program
   so far. Approve after a very small v2 refinement pass. No
   structural issues. No buyer issues. No proof-governance issues."

Pre-lock workflow verdict (2026-05-29):
  "v1 spec is structurally faithful to §15 and ChatGPT verdict is
   correct on the high level, but TWO HIGH-severity blocking issues
   were missed: (1) E-IDOS streams condition data into EREMOS V2
   today contradicts positioning v3 §6 commitment #3 + locked specs;
   (2) E-IDOS runs on mDAQ contradicts locked condition-monitoring
   v1 §3.2 (E-IDOS is standalone sensor-agnostic appliance, not
   mDAQ-based)." All blocking issues resolved in v2.
-->

# `/solutions/predictive-maintenance` — Page Spec v1

**Solution depth-example for the condition-monitoring outcome. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want to understand the predictive-maintenance outcome built on the Industrial Intelligence Ecosystem — VAS + E-IDOS + EREMOS V2 incident workflows — and how it fits a maintenance program (in-house, AMC-delivered, or hybrid).**

This is the page where Maintenance Managers and AMC providers land when they want the **outcome view** of condition monitoring — failure-signature detection, oil-health diagnostics, and incident workflows their maintenance team actually uses. It is **not** the capability page (`/capabilities/condition-monitoring` covers VAS + E-IDOS as a capability story). It is **not** the architecture walkthrough (`/architecture` covers cross-pillar composition). It is the **predictive-maintenance solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for predictive maintenance. Reader leaves with *"I now understand what predictive maintenance on the Elpis platform actually does for my maintenance program, how it fits whether I'm running in-house or via an AMC channel, what questions I'd have on a scoping call, and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/condition-monitoring` covers VAS + E-IDOS as a Pillar 4 capability story; LOCKED v1)
- A product detail page (Phase E `/vas`, `/e-idos`, `/eremos-v2` will each cover their product surface)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2)
- A pricing or commercial-engagement page (Phase 2 step 11 `/platform` covers commercial-engagement teaser; Phase 3 `/pricing` covers detail)
- An AMC partner-recruitment page (the AMC channel is named here as a deployment shape, not pitched at AMC operators as a partner program)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Maintenance Manager / AMC provider (§2.4)
- Lands here from `/solutions` hub (Card 2 Predictive Maintenance), from `/capabilities/condition-monitoring` via cross-link, from a Google search for *"predictive maintenance industrial platform"* / *"vibration analytics OEE"* / *"oil health monitoring HYDAC"* / *"AMC condition monitoring"*, or via the homepage hero
- Wants: failure-signature detection (not generic "AI"), oil-health diagnostics they can act on, incident workflows that survive shift handoffs, AMC channel economics they can scope, customer-controlled routing when AMC providers are in the loop
- CTA preference: *"Bring us your most-watched asset"* > *"Talk to engineering"* > *"Book a scoping call"*
- Vocabulary that lands: vibration analytics, vibration spectra, oil health, particle contamination, water saturation, ISO 4406, NAS 1638, HYDAC, Parker, MP Filter, Argo-Hytos, condition signatures, incident workflows, AMC channel, customer-controlled routing, predictive vs reactive vs calendar-based, failure-mode detection, **asset-criticality ranking**, **criticality-ranked rollout**, **lubrication breakdown**, **oil degradation**
- Vocabulary that backfires: *"AI insights"*, *"smart factory"*, *"digital twin"*, *"transformation"*, *"intelligent maintenance"* (generic), *"machine learning predicts failures"* (overclaim — predictive maintenance here is threshold-based on actual condition signatures, not LLM-style prediction)

**Secondary buyer:** Plant manager / Ops VP (§2.2) — downtime-prevention angle
- Lands here when scoping how to reduce unscheduled downtime on critical assets
- Wants: outcome framing tied to downtime + emergency dispatch reduction
- CTA preference: *"Book a scoping call"* > *"Bring us your most-watched asset"*

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Predictive Maintenance — VAS + E-IDOS + EREMOS V2 · Elpis* |
| **Meta description** (140-160 chars) | *Vibration analytics on rotating equipment + hydraulic and lubrication oil-health diagnostics + incident workflows. In-house, AMC, or hybrid.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/predictive-maintenance` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Product cards for VAS + E-IDOS (when Phase E `/vas` and `/e-idos` ship) cross-link via `Product` schema. Page-to-page cross-links to `/capabilities/condition-monitoring` + `/architecture` + `/security` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). 10 sections:

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome-led headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~100 |
| **2** | The Customer Pain — narrative empathy (2-3 paragraphs) | `light` | Narrative copy + optional pull-quote in margin | ~200 |
| **3** | How Elpis Solves This — 3-4 bolded-lead paragraphs + "How this differs from calendar-based PM" callout + 3 deployment shapes | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block + 3-column grid | ~350 |
| **4** | What's Included — split into From mDAQ / From VAS / From E-IDOS / From EREMOS V2 | `light` | Bulleted feature list with bolded leads | ~200 |
| **5** | Common Questions (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions with answers below + `FAQPage` schema | ~350 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column on desktop | `dark` | Bolded outcome leads + light-weight supporting clauses | ~150 |
| **7** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" link | ~80 |
| **8** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | `TrustCueBlock` (design-system v3 §16) | ~80 |
| **9** | Cross-lens navigation — LOCKED preset per design-system v3 §17 line 559 | `light-tinted` | `CrossLensBlock` (3 cards: `/capabilities/condition-monitoring` + `/architecture` + `/solutions`) | ~50 |
| **10** | Final CTA — vertical-localized | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · PREDICTIVE MAINTENANCE
>
> HEADLINE (size.3xl semibold):
> Detect failure signatures before downtime — not after.
>
> SUBHEAD (size.lg, max-width 60ch):
> Vibration analytics on rotating equipment, oil-health diagnostics on hydraulic and lubrication systems, and incident workflows your maintenance team actually uses. Run it in-house, deliver it through an AMC channel, or both — on the same platform.
>
> PRIMARY CTA (`Button.primary.lg`):
> Bring us your most-watched asset
> HREF: `/contact?intent=predictive-maintenance-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Book a scoping call
> HREF: `/contact?intent=predictive-maintenance-call`

**Anti-patterns:** No *"AI predicts everything"* framing (per buyer-taxonomy §2.4 vocabulary discipline). No *"transformation"* / *"intelligent maintenance"* generic-marketing language. No outcome metric in headline.

---

### 3.2 Section 2 — The Customer Pain

> EYEBROW: THE PAIN
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Most maintenance programs sit somewhere between reactive and calendar-based. Reactive means waiting for the bearing to seize, the hydraulic pump to fail, the lubrication system to contaminate to the point that production stops. Calendar-based means swapping filters and servicing components at fixed intervals — whether the oil is degraded or not, whether the bearing is worn or not. Both work. Neither is predictive.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> The teams that want to move beyond either pattern usually face the same three problems: (1) the condition data exists at the asset but doesn't reach the maintenance team in time to act, (2) when alerts do arrive they're noisy enough that the team starts ignoring them, (3) when AMC providers are part of the maintenance program, signal routing between the customer's operations and the AMC's service team is either over-shared or under-shared. The promise of "predictive maintenance" gets diluted by tooling that doesn't survive the operational reality.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The teams that succeed do something different. They instrument the assets that matter most. They read the actual condition signatures — vibration spectra on rotating equipment, particle contamination and water saturation on hydraulic and lubrication oil. They build incident workflows the team trusts. They route signals deliberately when AMC providers are in the loop. That's what this page is about.

---

### 3.3 Section 3 — How Elpis Solves This

> EYEBROW: HOW ELPIS SOLVES THIS

> CALLOUT — HOW THIS DIFFERS FROM CALENDAR-BASED PREVENTIVE MAINTENANCE (size.base, single paragraph; visual treatment: bordered card or left-rule callout, sits before the bolded-lead paragraphs below):
>
> > **How this differs from calendar-based preventive maintenance.** Calendar-based PM replaces oil filters at fixed intervals whether the oil is degraded or not, services bearings on a schedule whether they're worn or not. Predictive maintenance reads the actual condition signatures — vibration spectra on rotating equipment, particle contamination counts (ISO 4406 / NAS 1638) and water saturation on hydraulic and lubrication systems — and triggers maintenance when the signature crosses a threshold the maintenance team defines. **Same maintenance team. Same workflows. Different trigger.** The platform doesn't replace your CMMS or your maintenance discipline; it gives them a better trigger signal to act on.

#### Bolded-lead paragraphs (3 paragraphs):

> **Read what the asset is actually telling you.** VAS (Vibration Analyser System) runs on `mDAQ` hardware mounted at the rotating equipment — pumps, motors, gearboxes, fans, compressors, conveyors, structural components — and captures vibration spectra continuously. E-IDOS (Hydraulic & Lubrication Condition Monitoring) is a **standalone sensor-agnostic appliance** that measures particle contamination, water saturation, and oil flow on hydraulic and lubrication systems — supporting HYDAC, Parker, MP Filter, and Argo-Hytos sensors. Both run online (continuous monitoring) and offline (spot-check / portable mode) per the maintenance team's preference. See the underlying capability story → `/capabilities/condition-monitoring`.

> **Turn signals into workflows your team actually uses.** Raw condition data is noise without a trigger discipline. EREMOS V2 builds the persistent alarm and incident workflow layer on top of the VAS signal stream today — thresholds you define per asset, alarm escalation that survives shift handoffs, incident records that close out with operator notes and resolution detail. The signal becomes an incident; the incident becomes a workflow; the workflow becomes an audit trail. See the underlying capability story → `/capabilities/operational-intelligence`.
>
> > *Honest framing — E-IDOS streaming integration into EREMOS V2 is near-term roadmap. **Today** E-IDOS operates as a standalone reliability instrument with on-board HMI, thermal printer, BLE Android app, and email reports — the maintenance team reads condition data at the appliance, prints reports, and acts on alarms locally. The VAS-and-E-IDOS-together signal-stream story above describes the integrated end-state we are building toward; VAS-side incident workflows are live in EREMOS V2 today.*

> **Deploy the shape that fits how you operate.** All three shapes start the same way: rank assets by criticality (downtime cost × consequence × likelihood) and instrument the top tier first. Then pick the shape that fits your maintenance program: **(1) In-house** — VAS + E-IDOS direct to your in-house maintenance team, VAS signals route to your EREMOS V2 tenant. **(2) AMC-delivered** — VAS + E-IDOS deployed by your AMC provider, VAS signals route to the AMC operations team (under a customer-authorized routing scope). **(3) Hybrid** — in-house and AMC providers share VAS signals via customer-controlled routing, where the customer decides which signals route back to the AMC for which assets. The same hardware and software pieces deploy in all three shapes; the routing changes per asset and per maintenance contract.

> **No new monitoring stack to learn.** The same platform that runs your `/capabilities/connectivity-edge` for protocol integration, your `/capabilities/data-acquisition` for direct-sensor reads, and your `/capabilities/operational-intelligence` for OEE and alarms — that's the same platform running predictive maintenance. Condition signatures arrive at EREMOS V2 in the same canonical vocabulary as everything else. Maintenance teams that already use the platform for OEE or alarms learn no new tool.

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets (`hardwareProducts` + `eremosV2`). EdgeConnect peer is not used for predictive-maintenance — Acquisition peer carries the floor-side of this solution. See §3.7 for the architecture story.

#### From Hardware

**mDAQ (the acquisition platform that runs VAS):**

> - **Ruggedized acquisition hardware** mounted at the rotating equipment, running the VAS analytics — 4 analog channels (0-10 V or 4-20 mA), 16-bit, 860 S/s
> - **Offline operation** with optional battery backup for remote / unmanned sites (pipeline pump stations, mining outposts, off-grid water infrastructure)
> - **4G / Wi-Fi / optional Ethernet** for signal delivery back to EREMOS V2 — see `/capabilities/data-acquisition` for full hardware specs

**VAS (vibration analytics on rotating equipment, runs on mDAQ):**

> - **Continuous vibration spectra capture** on rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, and structural components
> - **Failure-signature thresholds** per asset (defined by your maintenance team)
> - **Online + offline modes** — continuous monitoring or spot-check via portable deployment
> - **Equipment-class library** for common rotating-equipment failure modes (bearing issues, imbalance, misalignment, looseness, and cracks)

**E-IDOS (standalone sensor-agnostic appliance for hydraulic + lubrication oil-health):**

> > *Honest framing — E-IDOS today operates as a standalone reliability instrument; signaling integration with EREMOS V2 is near-term roadmap. The features below describe what E-IDOS does at the appliance today.*
>
> - **Standalone sensor-agnostic appliance** — NOT mDAQ-based. Its own hardware platform with on-board HMI, thermal printer, BLE Android app, and email reports.
> - **Particle contamination monitoring** logged to ISO 4406 and NAS 1638 cleanliness standards
> - **Water saturation + oil flow measurement** in both online and offline states
> - **Sensor-agnostic input** — HYDAC, Parker, MP Filter, Argo-Hytos all supported
> - **Oil-health failure-mode coverage** for common hydraulic and lubrication failure modes — particle contamination, water ingress, lubrication breakdown / oil degradation, additive depletion

#### From EREMOS V2 (incident workflows)

> - **Persistent alarm state** that survives shift handoffs (alarm doesn't disappear when the operator who saw it goes home) — fed by VAS signals today; E-IDOS streaming integration is near-term roadmap
> - **Incident workflow** — alarm → triage → assigned-to → resolution → closure, with operator notes at each step
> - **Customer-controlled routing** for AMC channel deployments (you decide which signals route to which AMC provider for which assets)
> - **Audit-ready configuration history** — every threshold change and routing change captured with actor identity and timestamp

---

### 3.5 Section 5 — Common Questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to Maintenance Manager / AMC provider concerns.

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What maintenance teams ask before scoping a predictive program.

#### Q1. Does this replace our CMMS?

> No. EREMOS V2's incident workflows are operational — alarm triage, assigned-to, resolution. Your CMMS stays as the system of record for work-order management, spare-parts inventory, maintenance scheduling, and labor tracking. EREMOS V2 publishes incident records that your CMMS can ingest via API or webhook — the maintenance team works in the CMMS they already use, with a better-triggered incident stream feeding it.

#### Q2. What does "predictive" actually mean here?

> Threshold-based detection on real condition signatures — vibration spectra crossing failure-mode thresholds, particle contamination counts exceeding ISO 4406 cleanliness limits, water saturation passing the equipment-class limit. The maintenance team defines the thresholds; the platform monitors continuously and triggers when the signature crosses. This is not "AI predicts what will fail" — it's "the asset is telling you the bearing is degrading, here's the threshold you set, here's the alert." Honest framing matters: predictive vs reactive is a workflow improvement, not a machine-learning claim.

#### Q3. How do we onboard an AMC provider into this?

> Customer-controlled routing. You define which assets the AMC provider sees signals from, which thresholds they can configure, and which incident workflows they can act on. Per-tenant isolation in EREMOS V2 means the AMC provider sees only what you authorize — not your full operations. The AMC provider deploys VAS + E-IDOS on the assets under their contract, signals route into a scope you control, and the AMC team uses the same EREMOS V2 incident workflows your in-house team would.

#### Q4. What if our hydraulic system uses sensors that aren't HYDAC?

> E-IDOS is sensor-agnostic on the contamination input side — HYDAC, Parker, MP Filter, and Argo-Hytos are all supported today. The hardware reads the sensor; the analytics happen in E-IDOS regardless of the sensor brand. If you have a sensor that isn't on the supported list, scope that with engineering during the architecture review — the integration pattern is well-understood and additions are common. Where existing sensors don't support the failure modes you need to detect, the scoping call covers a sensor-fitness review — adding accelerometers on critical bearings, or installing an in-line particle counter on a hydraulic return-line, is a small mechanical project we can scope alongside the analytics deployment.

#### Q5. Can we run this on assets in plants without internet?

> Yes. Same offline-first posture as the rest of the platform. **VAS** runs on mDAQ hardware with optional battery backup; condition data buffers locally and forwards when connectivity returns. **E-IDOS** is a standalone appliance — its offline capability is built into the appliance itself: the built-in HMI and on-board thermal printer mean the maintenance technician can read condition data and print a report without any network at all, and the BLE Android app + email reports cover signal delivery patterns when network is partially available. Plants on isolated OT VLANs install both products the same way as plants with internet.

#### Q6. How should we roll this out — bottom-up by asset, or top-down by plant?

> Bottom-up by asset, sized to criticality. Start with the assets where unscheduled downtime hurts most — the critical pump, the high-utilization gearbox, the hydraulic system that takes a production line down when it loses cleanliness. Instrument those first, build the threshold + incident workflow discipline on a small number of assets the team trusts, then scale out. Top-down rollouts (instrument every asset in the plant at once) usually generate alert noise the maintenance team learns to ignore — and that's the failure mode that kills predictive programs.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **Cut unscheduled downtime on rotating equipment and hydraulic systems** — failure signatures detected on the pump, motor, gearbox, fan, compressor, or hydraulic system before the failure event; maintenance acts on threshold-crossings, not on failure events
> - **Cut emergency dispatches on AMC-served assets** — AMC providers act on condition signatures arriving via customer-authorized routing, not on customer phone calls after downtime starts
> - **Incident workflows that survive shift handoffs** — persistent alarm state and resolution audit trail mean the night-shift incident closes out cleanly even when the day-shift team takes over
> - **Maintenance discipline ranked by asset criticality** — the bottom-up rollout pattern means you instrument the highest-criticality assets first and build threshold + workflow discipline there before scaling out
> - **Customer-controlled AMC channel routing** — AMC providers see only what you authorize, per asset, with full audit
> - **Audit-ready configuration history** — every threshold change, every routing change, every workflow assignment captured with actor identity and timestamp
> - **No new monitoring stack to learn** — same platform as `/capabilities/operational-intelligence` for OEE and alarms; maintenance teams that use the platform for OEE already know how to read it

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific percentage downtime reductions or dollar-cost-savings claims. The /platform commercial-engagement teaser and Phase 3 case studies handle quantified outcomes once the customer-evidence registry is in place.

---

### 3.7 Section 7 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How the pieces compose for predictive maintenance.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15):

Solution-annotated subset of the Industrial Intelligence Stack 4-column layout. Highlights:
- **Col 1 — Floor:** rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, structural components + hydraulic systems (highlighted as the signal sources for this solution)
- **Col 2 — Acquisition peer (highlighted):** `mDAQ` hardware running `VAS` for rotating equipment + `E-IDOS` standalone appliance for hydraulic/lubrication oil-health. *For predictive maintenance, the EdgeConnect peer is not required for this solution — Acquisition carries the floor-side.*
- **Col 3 — EREMOS V2 (highlighted):** persistent alarm state + incident workflows + customer-controlled routing
- **Col 4 — Customer Enterprise:** CMMS (read-only incident-record integration arrow) + optional AMC channel routing arrow (dashed, customer-authorized scope)

**Annotations (4 specific to this solution):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 2 mDAQ + VAS | THE SIGNAL LAYER | mDAQ hardware platform runs VAS (vibration on rotating equipment). E-IDOS is a standalone sensor-agnostic appliance (on-board HMI + thermal printer + BLE Android app) for hydraulic + lubrication oil-health. Both support online + offline modes. *E-IDOS streaming integration into EREMOS V2 is near-term roadmap; today its signals live at the appliance.* |
| Col 3 EREMOS V2 incident workflows | INCIDENT WORKFLOWS | Persistent alarm state that survives shift handoffs. Alarm → triage → assigned-to → resolution → closure. Operator notes at each step. Audit-ready. *Fed by VAS today; E-IDOS streaming via roadmap.* |
| Col 3 → Col 4 CMMS integration arrow | CMMS INTEGRATION | EREMOS V2 publishes incident records via API or webhook. Your CMMS stays the system of record for work orders + parts + scheduling + labor tracking. |
| Col 3 → Col 4 AMC channel arrow | AMC CHANNEL ROUTING | Customer-controlled routing means the AMC provider sees only the signals you authorize, for the assets they're contracted on. Per-tenant isolation by design. |

> CAPTION (below diagram, size.sm italic):
> *For predictive maintenance, Col 2 is the Acquisition peer (mDAQ + VAS + E-IDOS); the EdgeConnect peer is not required for this solution. See the full peer-architecture story → `/architecture`.*

---

### 3.8 Section 8 — Trust Cue

Per design-system v3 §16 `TrustCueBlock`. 2 cues for predictive maintenance, both linking to `/security`:

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Customer-controlled routing for AMC channel deployments.** When AMC providers are part of your maintenance program, they see only the signals you authorize, for the assets they're contracted on. Per-tenant isolation in EREMOS V2 makes this enforceable by design — not a configuration discipline you have to maintain.
>
> CUE 2 (size.base):
> **Offline-first operation, including air-gapped plants.** VAS + E-IDOS + mDAQ deploy in plants on isolated OT VLANs the same way they deploy in plants with internet access. License validates locally — no phone-home. Condition data buffers locally and forwards when connectivity returns.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.9 Section 9 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages** (design-system v3 §17 line 559): `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub).

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONDITION MONITORING | The underlying capability — VAS + E-IDOS as Pillar 4 | `/capabilities/condition-monitoring` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.10 Section 10 — Final CTA

Per buyer-taxonomy v1 §2.4 Maintenance Manager / AMC provider CTA preference. Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic).

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your most-watched asset. We'll scope what predictive looks like for it.
>
> SUBHEAD:
> Whether you're running in-house maintenance, delivering through an AMC channel, or building a hybrid program — start with the asset where unscheduled downtime hurts most. We'll scope the VAS + E-IDOS + incident-workflow shape that fits it, with deployment economics that reflect the real shape of your program.
>
> PRIMARY CTA: Bring us your most-watched asset
> HREF: `/contact?intent=predictive-maintenance-scoping`
>
> SECONDARY CTA: Book a scoping call
> HREF: `/contact?intent=predictive-maintenance-call`

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.10 final CTA |
| `ArchitecturePanel.interactive` (variant=`solution-annotated` per §5.A + §15) | §3.7 architecture-for-this-solution diagram |
| `TrustCueBlock` (design-system v3 §16) | §3.8 trust cues |
| `CrossLensBlock` (design-system v3 §17 — LOCKED preset for /solutions/<solution>) | §3.9 cross-lens |
| `CTASection` | §3.10 final CTA |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 common questions |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.10. **~1,760 words total** (within 1,500-1,800 target for `/solutions/<solution>` page-type per `/capabilities` hub spec §9 page-type guidance). Increase from v1 (~1,640) reflects: B1 E-IDOS roadmap caveats (+50 words), B2 mDAQ correction expansion (+15 words), A5 E-IDOS failure-mode bullet (+15 words), A6 + A7 criticality + asset-type expansions (+15 words), J3 sensor-fitness sentence (+40 words). §3.4 restructure from 4-bucket to 2-bucket maintained roughly same total word count via consolidation.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~100 |
| 3.2 | Customer Pain (3 paragraphs) | ~200 |
| 3.3 | How Elpis Solves This (callout + 4 bolded-lead paragraphs) | ~350 |
| 3.4 | What's Included (4 sub-sections) | ~200 |
| 3.5 | Common Questions (6 Q&A) | ~350 |
| 3.6 | Outcomes You Can Hold Us To (7 bulleted outcomes) | ~150 |
| 3.7 | Architecture For This Solution (caption + 4 annotations) | ~80 |
| 3.8 | Trust Cue (2 cues + cross-link) | ~80 |
| 3.9 | Cross-lens | ~50 |
| 3.10 | Final CTA | ~80 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and §15 SolutionPanel anti-patterns:

| Don't | Why |
|---|---|
| Position E-IDOS as "thermal / electrical condition signatures" | Source-of-truth lock: per `/capabilities/condition-monitoring` v1, `hardware-ecosystem-map-v3.md §5.2`, and `positioning-v3.md §Glossary`, E-IDOS is **hydraulic + lubrication oil-health** (particle contamination, water saturation, oil flow; ISO 4406 / NAS 1638). "Thermal / electrical" appears in ZERO locked sources and describes a different (unbuilt) product category. This anti-pattern carries forward the lesson the source-of-truth verification workflow caught on the /solutions hub v1 draft. |
| Frame "predictive" as machine-learning-style prediction | Overclaim. Predictive maintenance here is **threshold-based detection on real condition signatures** — vibration spectra crossing failure-mode thresholds, particle contamination counts exceeding ISO 4406 limits. The maintenance team defines the thresholds. The platform monitors continuously and triggers when the signature crosses. "AI predicts what will fail" is the wrong framing and reads as marketing-speak to maintenance teams who know how condition monitoring actually works. |
| Claim percentage downtime reductions or specific dollar-cost savings | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics on this page. Quantified outcomes wait for the `/platform` commercial-engagement teaser + Phase 3 customer-story registry. |
| Position the AMC channel as the primary deployment shape | In-house, AMC-delivered, and hybrid are equally valid deployment shapes. Per buyer-taxonomy §2.4 AMC channel governance — the AMC channel is a real and important deployment path, but pitching it as the default would alienate the in-house maintenance manager who is half of the primary buyer. |
| Imply this replaces the customer's CMMS | The CMMS stays the system of record for work orders + parts + scheduling + labor tracking. EREMOS V2 incident workflows feed the CMMS via API or webhook. Q1 in the FAQ explicitly answers this; the page voice everywhere must preserve "feeds, not replaces" for CMMS. |
| Use *"AI insights"* / *"smart factory"* / *"digital twin"* / *"intelligent maintenance"* | Per buyer-taxonomy §2.4 vocabulary discipline — backfires with Maintenance Managers + AMC providers who read these as marketing-speak. |
| Use absolute outcome claims ("zero downtime", "eliminate emergency dispatches") | Per anti-overclaim discipline carried from the OEM v2 solution spec precedent — outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero". `solution-oem-machine-monitoring-v2.md §1 + §6 + §9` is the locked precedent for this hedging. |
| Add customer logos, customer names, or specific deployment stories with named customers | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2 has no customer-logo authorization; named customer stories wait for Phase 3 customer-story sign-off process. |
| Add competitor names (Bently Nevada, SKF, Emerson, etc.) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory, not solution page. |
| Lead the hero with products instead of the outcome | Per design-system v3 §15 SolutionPanel anti-pattern — defeats the FOR WHAT framing of /solutions per memo v2 §2. The hero leads with "Detect failure signatures before downtime" (outcome), not "VAS + E-IDOS + EREMOS V2" (products). |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per design-system v3 §15 anti-pattern — solution pages need annotated subsets of the master architecture diagram, not generic images. The interactive variant is what surfaces the solution-specific annotations to OT Architects evaluating the deployment shape. |
| Imply E-IDOS streams condition data into EREMOS V2 today | Per positioning v3 §6 commitment #3 + locked condition-monitoring spec v1 §3.5 + locked operational-intelligence spec v1 §6 anti-pattern — E-IDOS-to-EREMOS V2 streaming is **near-term roadmap, not current behavior**. Today E-IDOS operates as a standalone reliability instrument with HMI + thermal printer + Android app + email reports. The spec carries explicit honest-framing callouts in §3.3 and §3.4 to prevent regression; any future edit that drops them violates this anti-pattern. |
| Conflate mDAQ hardware platform with E-IDOS appliance | Per locked condition-monitoring spec v1 §3.2 + hardware-ecosystem-map v3 §5.2 + data-acquisition v2 §3.5 — **VAS is built on the mDAQ acquisition platform; E-IDOS is a separate standalone sensor-agnostic appliance** with its own hardware platform (on-board HMI, thermal printer, BLE Android app). Saying "VAS and E-IDOS run on mDAQ" or "mDAQ is the hardware platform for both" is factually wrong against locked source. The spec carries this guard in §3.3, §3.4, §3.5 FAQ Q5, and §3.7 architecture annotations. |
| Present predictive maintenance as a replacement for reliability engineering practice | The platform improves **trigger quality** (data-driven thresholds, persistent alarm workflows, audit-ready history) but does not replace engineering judgment — reliability engineers still set failure-mode hypotheses, threshold semantics, and root-cause interpretation. The maintenance team and reliability function remain the discipline; Elpis is the trigger and workflow layer. |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 1,500-1,800 word target (current: ~1,760 words)
- [x] All 10 sections present per SolutionPanel layout (design-system v3 §15)
- [x] §3.1 hero leads with outcome ("Detect failure signatures before downtime"), not products
- [x] §3.3 "How this differs from calendar-based PM" callout present per /capabilities hub §9 emerging-pattern governance
- [x] §3.3 names the 3 deployment shapes (in-house / AMC-delivered / hybrid)
- [x] §3.3 includes cross-links to `/capabilities/condition-monitoring` + `/capabilities/operational-intelligence` + `/capabilities/connectivity-edge` + `/capabilities/data-acquisition`
- [x] §3.4 What's Included follows design-system v3 §15 schema (2 buckets: `hardwareProducts` + `eremosV2`; EdgeConnect bucket omitted because not used by predictive-maintenance solution) — see v2 J1 decision
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 inline-FAQ-with-schema-markup governance
- [x] §3.5 FAQ Q1 (CMMS replacement) explicitly says "feeds, not replaces"
- [x] §3.5 FAQ Q2 ("predictive" definition) honestly disambiguates threshold-based detection from ML-prediction overclaim
- [x] §3.5 FAQ Q3 (AMC onboarding) explains customer-controlled routing
- [x] §3.5 FAQ Q4 (non-HYDAC sensors) names the sensor-agnostic supported list (HYDAC, Parker, MP Filter, Argo-Hytos) + sensor-fitness scoping sentence
- [x] §3.5 FAQ Q5 (offline operation) confirms air-gapped plant support — and explicitly distinguishes VAS (runs on mDAQ) from E-IDOS (standalone appliance with on-board HMI + thermal printer + BLE Android app)
- [x] §3.5 FAQ Q6 (rollout shape) recommends bottom-up by asset criticality
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero" (per OEM v2 hedging precedent)
- [x] §3.6 omits percentage downtime reductions and dollar-cost claims (per proof-architecture v1 §3 + §4)
- [x] §3.7 architecture diagram uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image
- [x] §3.7 annotations honor §5.A discipline (eyebrow + ≤4-word title + 1-2 sentence body; max 8 per zoom level)
- [x] §3.7 includes solution-scoped Col-2 clarifier ("EdgeConnect peer is not required for this solution") to prevent reader misreading as full peer-architecture truth
- [x] §3.8 trust cues cover BOTH customer-controlled routing (AMC channel) AND offline-first (air-gapped plants)
- [x] §3.9 cross-lens cards match the LOCKED design-system v3 §17 preset for `/solutions/<solution>`: `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub)
- [x] §3.10 final CTA uses Maintenance-Manager-preferred framing ("Bring us your most-watched asset") and is vertical-localized (not generic)
- [x] E-IDOS positioning across the page matches the LOCKED `/capabilities/condition-monitoring` spec v1.1 — hydraulic + lubrication oil-health (NOT thermal/electrical)
- [x] VAS positioning across the page matches the LOCKED `/capabilities/condition-monitoring` spec v1.1 — vibration analytics on rotating equipment; VAS expansion is "Vibration Analyser System" (NOT "Vibration Analytics Service")
- [x] VAS equipment list matches LOCKED condition-monitoring spec v1.1 — "rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, structural components" (v1.1 amendment 2026-05-29)
- [x] VAS failure-mode list matches LOCKED condition-monitoring v1.1 — "bearing issues, imbalance, misalignment, looseness, and cracks"
- [x] mDAQ named as the hardware platform for **VAS only** per `/capabilities/data-acquisition` v2 + condition-monitoring v1.1 (E-IDOS is a separate standalone sensor-agnostic appliance — NOT mDAQ-based)
- [x] **E-IDOS streaming integration into EREMOS V2 explicitly framed as near-term roadmap** (NOT current behavior) per positioning v3 §6 commitment #3 + locked condition-monitoring v1 §3.5 + locked operational-intelligence v1 §6 anti-pattern
- [x] EREMOS V2 incident workflows named per `/capabilities/operational-intelligence` v1 — fed by VAS today; E-IDOS via roadmap
- [x] No vocabulary that backfires per buyer-taxonomy v1 §2.4 (no *"AI insights"* / *"smart factory"* / *"digital twin"* / *"intelligent maintenance"*)
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names, no certification claims
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] **v2 §6 contains 4 new defensive anti-patterns**: E-IDOS-streams-today regression guard, mDAQ-E-IDOS conflation guard, predictive-maintenance-replaces-reliability-engineering guard, plus 1 carried forward from v1
- [x] **§1.2 buyer vocabulary roster** includes "asset-criticality ranking", "criticality-ranked rollout", "lubrication breakdown", "oil degradation" per v2 J6 + A5
- [x] **Asset criticality named in §3.3 + §3.6** (not only §3.5 Q6) per v2 A6 cross-validator + ChatGPT consensus
- [x] **Concrete asset types repeated in §3.6 Outcomes bullet 1** (pump, motor, gearbox, fan, compressor, hydraulic system) per v2 A7
- [x] **Governance side-flag for /solutions hub coordination** documented per v2 A8: when this page ships live, update /solutions hub v2 §3.2 Card 2 to remove "Coming soon" status pill and swap pre-live link from /capabilities/condition-monitoring to /solutions/predictive-maintenance

---

## 8. Out of scope for v1

- **Full VAS product detail.** Phase E `/vas` covers: full sensor compatibility matrix, vibration-spectrum analytics methodology, equipment-class failure-mode library detail, deployment installation guides.
- **Full E-IDOS product detail.** Phase E `/e-idos` covers: full sensor compatibility matrix (HYDAC, Parker, MP Filter, Argo-Hytos models), ISO 4406 / NAS 1638 reporting detail, HMI UI screenshots, BLE Android app screenshots, on-board thermal printer report formats.
- **Full EREMOS V2 incident-workflow product detail.** Phase E `/eremos-v2` covers: incident-workflow UI screenshots, alarm-escalation configuration, customer-controlled-routing UI detail.
- **Full mDAQ hardware detail.** Phase E `/mdaq` covers: full hardware specs, certifications (CE / UL / FCC / IP rating), enclosure dimensions, mounting patterns.
- **AMC partner-recruitment.** This page names the AMC channel as a deployment shape; a separate Phase E `/partners/amc` (or similar) covers AMC partner-recruitment if/when that program ships.
- **Quantified downtime-reduction outcomes.** Wait for Phase 3 customer-story registry + commercial-engagement teaser on `/platform` (Phase 2 step 11).
- **Pricing / commercial engagement detail.** `/platform` (step 11) covers commercial-engagement teaser; Phase 3 `/pricing` covers detailed pricing.
- **Industry-specific predictive-maintenance framings.** Phase 3 `/industries/<industry>` or Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2.
- **The capability story of VAS + E-IDOS as Pillar 4.** Lives at `/capabilities/condition-monitoring` (LOCKED v1).
- **Cross-pillar architecture walkthrough.** Lives at `/architecture` (LOCKED v2).
- **Operational trust posture detail.** Lives at `/security`.

---

*`/solutions/predictive-maintenance` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review + 4-agent pre-lock validation workflow (cross-spec drift validator caught 2 HIGH-severity blocking issues that ChatGPT missed — E-IDOS-streams-today regression + mDAQ-E-IDOS conflation — both resolved before lock). Eighth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 9. Uses SolutionPanel layout (design-system v3 §15 LOCKED). **v2 changes from v1:** B1 E-IDOS roadmap-honesty caveats throughout (§3.3 ¶2 + §3.4 + §3.5 Q5 + §6 anti-pattern), B2 mDAQ correction (VAS runs on mDAQ; E-IDOS is standalone appliance), A3 VAS expansion "Vibration Analyser System", A4 'cracks' + 'bearing issues' restored, A5 oil-health failure-mode bullet adds "lubrication breakdown", A6 asset criticality named in §3.3 + §3.6, A7 concrete asset types repeated in §3.6 Outcomes, A8 /solutions hub governance side-flag, A9 §3.7 Col-2 clarifier, A10 reliability-engineering anti-pattern, A11 mDAQ Ethernet bullet trimmed; J1 §3.4 collapsed to §15 2-bucket schema; J2 condition-monitoring v1 amended upstream (v1.1, 2026-05-29) with maintenance-buyer-tuned equipment vocabulary; J3 sensor-fitness sentence added to Q4; J6 asset-criticality vocabulary added to §1.2 roster. **Cross-spec coordination:** condition-monitoring v1 amended to v1.1 in parallel (PR #72) to keep specs aligned. **Side-flag:** PR #72 (condition-monitoring v1.1) is LOCKED but not yet merged to master — verify PR #72 merge before publishing this page. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.4 + §2.2, proof-architecture v1 + §3 + §4 + §8, positioning v3 + §Glossary + §6 commitment #3 (E-IDOS roadmap), design-system v3 §15 (SolutionPanel LOCKED) + §16 (TrustCueBlock) + §17 (CrossLensBlock LOCKED preset for /solutions/<solution>: line 559) + §5.A (ArchitecturePanel.interactive solution-annotated variant), page-capabilities-hub-spec-v1 §9, page-capabilities-condition-monitoring-spec-v1 v1.1 (source-of-truth for VAS + E-IDOS positioning + amended equipment list), page-capabilities-data-acquisition-spec-v1 v2 (mDAQ hardware platform; VAS-only), page-capabilities-operational-intelligence-spec-v1 v1 (EREMOS V2 incident workflows + §6 E-IDOS-streaming anti-pattern), solution-oem-machine-monitoring-v2 (anti-overclaim discipline precedent + "cut" verb hedging), solution-cnc-machining-v2 (Phase 1 solution-page voice precedent), hardware-ecosystem-map-v3 §5, page-solutions-hub-spec-v1 v2 (Card 2 summary that this depth-example supports).*
