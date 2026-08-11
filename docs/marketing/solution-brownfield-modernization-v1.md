<!--
File:        docs/marketing/solution-brownfield-modernization-v1.md
Purpose:     Full copy for the /solutions/brownfield-modernization page on the
             Elpis website. The second vertical solution page; inherits the
             CNC machining template pattern (per CNC solution page v2's
             "Template inheritance notes" section).
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy.
Version:     v1 (first draft)
Date:        2026-05-24

Inherits structure from docs/marketing/solution-cnc-machining-v2.md
"Template inheritance notes" — same 9-section pattern, with the §5
operator-questions and §7 engagement-timeline sections retained
because brownfield deployments have heavy operational and deployment
anxiety that those sections directly defuse.

Per-vertical positioning (locked in CNC v2): "lead with 'the iron stays,
the data layer modernizes'; FOCAS2 on Fanuc 16i/18i specifically;
modernization-without-replacement framing"

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §8 (solution-page template)
  - docs/marketing/solution-cnc-machining-v2.md (template source)
  - shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC tag set)
  - CLAUDE.md §1, §3 (locked architectural decisions)

Strategic frame: this page is where Elpis positions against the
forklift-modernization / rip-and-replace / cloud-first IIoT vendor
category — without naming competitors. The central anti-positioning
is "you don't have to replace the iron to modernize the data layer."
-->

# Brownfield Modernization — Solution Page Copy v1

**Page URL:** `/solutions/brownfield-modernization`
**Primary audience:** Production manager, plant manager, engineering manager at a manufacturing plant with mixed-generation controllers (older Fanuc CNCs, mixed PLC vintages, modernization-anxious operations leadership)
**Secondary audience:** Industrial IT lead at a brownfield plant evaluating monitoring/analytics platforms

---

## §1 Hero

### Copy

> ### The iron stays. The data layer modernizes.
>
> Native FOCAS2 on Fanuc 16i/18i and 0i. MTConnect, Brother HTTP, MT-LINKi, Modbus TCP on whatever else you run. Modern operational visibility from machines you already own, validated, and trust.
>
> [ Book a scoping call for your brownfield floor ]   ·   Download the datasheet

### Visual notes

- **Full-bleed hero image:** an older Fanuc 16i or 18i controller in real production use — slightly worn paint, an operator's hand on the MPG, clear sign of years of duty. The visual cue is *"this machine is still working, and that's the point"*, not *"this machine is old and embarrassing."*
- **Avoid:** images of empty factory floors, new shiny CNCs, abstract modernization graphics, "before/after" cliches.
- **Headline at display weight.** Two short sentences for maximum punch.
- **Trust strip under hero:** *Live integrations on Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i · Brother S700Xd1 · MTConnect · MT-LINKi · Modbus TCP for PLC-fronted CNCs*

### Reader-effect notes

- The brownfield plant manager should feel *"this person isn't going to ask me to replace my floor."*
- "The iron stays. The data layer modernizes." is the central narrative anchor for the entire page — every subsequent section reinforces it.
- The FOCAS2-on-old-Fanuc detail in the subhead earns trust within 5 seconds — most monitoring platforms quietly drop support for the 16i/18i generation.

---

## §2 The challenge

### Copy

> ### The brownfield reality
>
> Real plants don't have one generation of controllers. They have five. A Fanuc 16i installed in 2009 next to a 32i from last year. A Brother S700Xd1 that's been running parts for eight years. A handful of older CNCs fronted by Modbus PLCs because the original controller's interface was never going to survive corporate IT review.
>
> Every one of those machines is making good parts. Operators know them by feel. Tooling and fixtures are validated. The capital was depreciated years ago. The case for ripping them out and replacing them with newer iron exists only in vendor presentations.
>
> What corporate wants — OEE numbers, alarm history, shift reports, audit trails — is a *data* requirement, not a *hardware* requirement. The trap most plants fall into is accepting that modernization means replacement. It doesn't. The iron can stay. The data layer is what needs to catch up.

### Visual notes

- **Three narrative paragraphs.** No bullet lists in this section.
- **Optional pull-quote in the margin (desktop):** *"The case for ripping them out exists only in vendor presentations."*
- **Subdued visual treatment.** Empathy section — quiet, deliberate.

### Reader-effect notes

- The reader (brownfield plant manager) should feel *"this person has walked a floor like mine."*
- The vendor-presentation dig is intentional — most brownfield plant managers have sat through that exact pitch. The recognition earns credibility.
- The closing line (*"The iron can stay. The data layer is what needs to catch up."*) sets up §3.

---

## §3 The Elpis approach

### Copy

> ### How Elpis modernizes the data layer
>
> **EdgeConnect speaks the controllers you already own.** FOCAS2 polls Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i — every CNC generation Fanuc has shipped that exposes the protocol. MTConnect covers the newer multi-vendor fleet. Brother HTTP reads Brother S700Xd1 and similar models via their built-in web interface. MT-LINKi handles Fanuc's REST stack. Modbus TCP covers the older CNCs you've already fronted with a PLC gateway. One service on a small box in your control cabinet — no per-machine custom scripting, no per-machine HMI replacement.
>
> **The data layer becomes uniform without touching the iron.** A spindle RPM reading from a 2009 Fanuc 16i and a 2024 Mazak Integrex both arrive at the platform as `spindle_rpm` in the canonical vocabulary. Cycle time, parts count, alarm code, tool number, axis positions — same names, same semantics, regardless of which controller generated the signal. The same dashboard works across every generation on your floor. **Your operators don't need to change a single behavior.**
>
> **EREMOS V2 produces the modern analytics layer your customers and auditors want.** OEE Segments computed from edge-collected signals — auditable, defensible, exportable. Persistent alarm tracking with incident workflows. Shift reports in PDF and Excel. Tool-life trends. The reports corporate wants now flow from the iron you already own.
>
> **Deployment is incremental and reversible.** Start with one machine. If it works for that machine, you expand. If it doesn't, you stop. No forklift upgrade. No multi-month rebuild project. No mandatory replacement of any controller before the platform earns its place on your floor.

### Visual notes

- **Four bolded-lead paragraphs.** Each lead is a claim mapped to brownfield reality.
- **The "Your operators don't need to change a single behavior" sentence** at the end of paragraph 2 should render with slight visual prominence — bold weight or a subtle accent. This is the single biggest objection-killer on the page for brownfield buyers.
- **Optional small inline icons** next to each bolded lead — restrained, functional.
- **Medium-high density section.**

### Reader-effect notes

- The reader (production manager) should feel *"every concern I'd raise has just been addressed."*
- The full Fanuc model list in paragraph 1 is doing trust-building — most monitoring platforms support the newest one or two generations and quietly drop the older ones. Naming 0i through 32i signals real coverage.
- The "operators don't need to change behavior" line addresses the unspoken cultural risk — operators trust their machines and resent imposed change.

---

## §4 What's included

### Copy

> ### What's included for brownfield modernization
>
> **From EdgeConnect (edge runtime):**
>
> - **FOCAS2 collector across every Fanuc generation** — 0i, 16i, 18i, 21i, 30i, 31i, 32i. Axes, spindle, alarms, tool, production counters, programs.
> - **MTConnect collector** — for newer multi-vendor CNCs already speaking the open standard.
> - **Brother HTTP collector** — Brother S700Xd1 and similar via the built-in web-monitoring interface.
> - **MT-LINKi collector** — Fanuc's REST-based machine-data product.
> - **Modbus TCP collector** — for older CNCs fronted by a PLC gateway, plus any energy meters, drives, or instrumentation you already have.
> - **Canonical vocabulary across vendors and generations** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions, tool numbers, alarm codes. Same names, regardless of which controller generation produced them.
> - **Store-and-forward buffering** — older plant networks aren't always reliable; the platform handles the gaps.
> - **Three-way diagnostics** — source, pipeline, sink. When something goes wrong on the floor or in IT, operators know where it broke.
> - **Connectivity Studio** — web admin UI for adding machines, configuring tag maps, and running Test Connection probes before anything goes live. No command-line config required.
> - **Hash-chained configuration audit log** — even on brownfield deployments where formal change-control may have been informal historically, the platform brings a tamper-evident record from day one.
>
> **From EREMOS V2 (intelligence layer):**
>
> - **OEE Segments** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Computed from edge-collected signals; auditable.
> - **Persistent alarm tracking with incident grouping** — alarms from the oldest CNC on the floor become tracked records, not just blinking lights.
> - **Tool-life ingestion** — dedicated path for tool-wear telemetry.
> - **Shift reports in PDF and Excel** — the reports corporate has been asking for.
> - **Multi-tenant by design** — one EREMOS V2 across multiple sites if you operate more than one plant.
> - **Dashboards split by device class** — CNC, PLC, meter. Mixed-generation fleets render cleanly.

### Visual notes

- **Two clearly separated sub-sections** (EdgeConnect and EREMOS V2) with their own headers.
- **Bolded leads + plain-body explanations** for each capability.
- **Higher density section** — readers scan, not read.

### Reader-effect notes

- The reader should think *"this covers every machine on my floor, including the old ones."*
- The Modbus TCP bullet's note about energy meters and drives is intentional — it surfaces that the platform extends naturally beyond the CNCs to whatever else you've already instrumented.

---

## §5 Common operator and management questions the platform answers

### Copy

> ### Questions brownfield deployments raise
>
> If you've been burned by a previous modernization attempt or are evaluating this carefully because the last vendor pitch promised more than it delivered, here's what the platform changes:
>
> - **"Will deploying this require us to take a machine offline?"** No. EdgeConnect connects to the controller as a read-only client over its native protocol. Polling happens alongside normal operation; no machine downtime required for installation.
> - **"What about our oldest CNC — does it work with that?"** If it's a Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i, yes — FOCAS2 covers all those generations. If it's older or proprietary, a Modbus TCP gateway in front of the controller bridges to the platform. We'll scope your specific machines before quoting.
> - **"Do operators need to retrain?"** No. The platform reads from the controller; it doesn't change how the controller is operated. Operators continue using the same MPG, the same HMI, the same parts programs they already know.
> - **"If the platform breaks, do our machines stop?"** No. EdgeConnect is observational. The CNC operates entirely independently of EdgeConnect's status. If the platform is offline for maintenance, your floor keeps producing parts.
> - **"Can we start with one machine and decide whether to expand?"** Yes — that's the recommended deployment pattern. No multi-year commitment required to prove the platform earns its place.
> - **"How does this integrate with the SCADA we already have?"** EdgeConnect publishes to MQTT (or OPC UA Server) — both standards your existing SCADA likely already consumes. The platform sits alongside what you have, not in place of it.

### Visual notes

- **Each question as a bold pull-quote**, answer in regular body weight underneath.
- **Generous line spacing.** Scan-bait, not paragraphs.
- **Optional small icon** next to each question.

### Reader-effect notes

- This section is doing the heaviest deployment-anxiety defusion on the page.
- The "EdgeConnect is observational" answer in question 4 is one of the most important architectural truths to surface — many brownfield managers have been burned by monitoring systems that became single points of failure.
- The "scope your specific machines before quoting" line in question 2 is honest and sets expectations — every brownfield floor has at least one weird controller that needs case-by-case evaluation.

---

## §6 Customer outcomes

### Copy

> ### What brownfield plants see when they deploy
>
> - **Modern visibility from machines you already own** — OEE, alarms, reports without replacing a single controller
> - **OEE reporting without capital expense on new iron** — the platform earns its place in months, not over a 5-year capital cycle
> - **Audit trail without ripping out the existing configuration plane** — hash-chained change history starts the day you turn the platform on
> - **Mixed-generation fleets behave like one operational system** — the canonical vocabulary normalizes everything from 2009 Fanuc to last year's Mazak
> - **Modernization without a 6-month rebuild project** — incremental deployment, reversible at every step
> - **Operator workflow unchanged** — the machines operate the way operators already know

### Visual notes

- **Bulleted outcome list, two-column on desktop, single-column on mobile.**
- **Bolded outcome lead + light-weight supporting clause** — same pattern as the homepage.
- **No icons** — the typography carries the rhythm.

### Reader-effect notes

- Each outcome explicitly addresses a brownfield anxiety the reader has already felt.
- The capital-expense bullet is doing heavy commercial work — it implicitly positions the platform against $500K+ controller-replacement projects without ever naming a number.

---

## §7 What a typical brownfield engagement looks like

### Copy

> ### How brownfield plants typically roll this out
>
> **Week 1 — Proof on the oldest machine.** Pick the controller you're most skeptical about. EdgeConnect installed on a small Windows box in your control cabinet. FOCAS2 or Brother HTTP polling that machine. Data flowing to a Mosquitto broker we set up alongside, or to your existing MQTT broker. EREMOS V2 displaying real cycle-time and alarm data typically within the first few days. If it works for that machine, it'll work for the rest of your floor.
>
> **Weeks 2–4 — Expansion to a cell.** Add the rest of the machines on one line or one cell. Tag-map authoring done together with your team — your operators know the names that matter. Shift report templates configured against your actual shift schedule. OEE Segments calibrated against your real production reality.
>
> **Weeks 5–8 — Fleet rollout.** Remaining CNCs onboarded — the newer ones, the Modbus-fronted older ones, anything PLC-fronted. Multi-site or multi-line aggregation in EREMOS V2 if applicable. Alerting routed to the channels your operations team already uses.
>
> **Ongoing.** When you eventually do replace a controller (years from now, on your own capital schedule), the platform already handles whatever you put in. FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP all ship today. Siemens S7 and OPC UA Client are on the roadmap. Your modernization investment is in the data layer, not in the iron — so when the iron eventually changes, the data layer doesn't.

### Visual notes

- **Four-step horizontal timeline** on desktop; vertical stack on mobile.
- **Each step:** week label, headline, 2-3 line description.
- **Subtle accent-color progress markers** between steps.
- **The "Ongoing" step** could visually loop or extend — suggesting continuous architectural value.

### Reader-effect notes

- "Pick the controller you're most skeptical about" is the strongest framing on the page for a brownfield buyer. It signals confidence and respects the buyer's caution.
- The closing sentence — *"Your modernization investment is in the data layer, not in the iron — so when the iron eventually changes, the data layer doesn't"* — is the long-term economic argument. Worth visual emphasis.

---

## §8 Architecture for brownfield modernization

### Copy

> ### How it fits together on a brownfield floor
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, with the Controllers cluster annotated specifically for brownfield: *"Fanuc 16i/18i (legacy)"* · *"Brother S700Xd1"* · *"Modbus PLCs in front of older CNCs"* · *"Newer CNCs via MTConnect"* — visually conveying mixed-generation reality ]
>
> *EdgeConnect runs at your plant. EREMOS V2 aggregates across plants if you operate more than one. Standard MQTT and OPC UA make the integration interoperable with whatever existing SCADA, MES, or historian you already have — no rip-and-replace required.*

### Visual notes

- **Brownfield-specific variant of the master architecture diagram.** Same structure as the master, but the Controllers cluster names old and new explicitly to convey mixed-generation reality.
- **Visual cue worth considering:** show one or two of the controllers in slightly muted/faded styling (representing older iron) and others in sharper/newer styling — without making the older ones look "bad."
- **Caption in italic** beneath the diagram.

### Reader-effect notes

- The diagram should communicate *"your floor — every generation of it — fits into this architecture."*
- The caption's "no rip-and-replace required" is the closing anti-positioning line.

---

## §9 Final CTA

### Copy

> ### Bring us your oldest CNC.
>
> Pick the controller you're most skeptical about. We will scope a proof of value against that machine specifically. Demos run on real protocols against your real iron — not on a polished newer machine staged for the camera.
>
> [ Book a scoping call for your brownfield floor ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace.** Same pacing as the homepage and CNC final CTAs.
- **Primary CTA button in accent color.** Secondary as text link.
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- "Bring us your oldest CNC" inverts the typical vendor demo dynamic — vendors usually want to demo on the newest, most cooperative machine. Asking for the skeptical one signals confidence.
- *"Not on a polished newer machine staged for the camera"* is pre-emptive objection-handling against the demo-skepticism brownfield managers reasonably carry.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 50 | Headline + subhead + CTAs |
| §2 The challenge | 220 | Three narrative paragraphs |
| §3 Elpis approach | 290 | Four bolded-lead paragraphs |
| §4 What's included | 340 | Two sub-sections, dense feature lists |
| §5 Common questions | 240 | Six Q&A pairs |
| §6 Customer outcomes | 110 | Six bullets |
| §7 Typical engagement | 230 | Four-step timeline |
| §8 Architecture | 80 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,630 words** | Plus diagram + visual elements |

Slightly denser than the CNC solution page because the brownfield audience reads more carefully — they're evaluating against the scars of previous modernization attempts.

---

## Visual / pacing guidance summary

- **Pacing matches the CNC solution page:** hero anchored → challenge intimate → approach explanatory → features dense → questions scannable → outcomes scannable → engagement reassuring → architecture visual → CTA confident
- **Imagery:** all brownfield-aware. Real older Fanuc controllers, real shop-floor scenes with mixed-generation machines side by side, real Connectivity Studio screenshots. No "modernization triumphs over old equipment" cliches.
- **Palette:** same dark premium industrial as homepage and CNC solution page. The §5 questions section is a candidate for a subtle palette shift to mark it visually.
- **Mobile:** every section tested at 375px wide. The timeline in §7 stacks vertically; the §4 feature lists collapse cleanly.

---

## What's out of scope for v1

- **Real customer case studies** — placeholder language only until brownfield customers go public
- **Specific OEE percentages claimed as "typical"** — never; the calculator handles that
- **Direct comparisons to forklift-upgrade vendors** — that's the objection-handling guide's job; the page handles it through anti-positioning, not naming
- **Pricing on this page** — solution pages route to `/pricing` for the model; no numbers here

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4, homepage copy v2, CNC solution page v2, and security page v2 for voice consistency
- [ ] No fabricated customer names or ROI claims
- [ ] Every protocol claim traces to shared-knowledge contracts (CNC vocabulary, MQTT contract)
- [ ] FOCAS2 model list (0i / 16i / 18i / 21i / 30i / 31i / 32i) confirmed accurate against the FOCAS2 collector capabilities
- [ ] Architecture diagram brownfield variant approved (designer brief from `architecture-diagram-spec-v2.md`)
- [ ] §5 deployment-anxiety questions reviewed by Elpis services lead — language must reflect actual brownfield plant manager concerns
- [ ] §7 engagement timeline reviewed for week-counts against actual brownfield project history
- [ ] CTA destinations confirmed (scoping-call form + datasheet download)
- [ ] Page tested at 375px mobile width

---

*Solution page — Brownfield Modernization, v1, 2026-05-24. Derived from CNC machining solution page v2 template. Second of five solution pages — three remaining: precision manufacturing, OEM machine monitoring, multi-site operations.*
