<!--
File:        docs/marketing/solution-multi-site-operations-v1.md
Purpose:     Full copy for the /solutions/multi-site-operations page on the
             Elpis website. The third vertical solution page.
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy.
Version:     v1 (first draft)
Date:        2026-05-24

Inherits structure from docs/marketing/solution-cnc-machining-v2.md
"Template inheritance notes" — same 9-section pattern. Per-vertical
positioning (locked in CNC v2): "lead with 'ten-plus plants on one
operational view'; per-site EdgeConnect with central EREMOS V2
aggregation; outage-resilience story prominent."

Audience differs significantly from CNC/brownfield: corporate operations
director, multi-plant VP, COO at multi-site manufacturer. Less interested
in specific controller models; more interested in fleet visibility,
standardization, cross-site benchmarking, and platform economics at
scale. The page leans more strategic, less operational.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §8 (solution-page template)
  - docs/marketing/solution-cnc-machining-v2.md (template source)
  - docs/marketing/solution-brownfield-modernization-v2.md (sister vertical)
  - shared-knowledge/contracts/eremos-per-tag-mqtt.md (per-gateway identity contract)
  - CLAUDE.md §1, §3 (locked architectural decisions — especially #4, #8, #10)

Strategic frame: this page is where Elpis positions against per-plant
monitoring tool fragmentation, custom corporate-level aggregation
projects, and SCADA suites that scale per-site but not per-fleet.
The central anti-positioning is "fleet visibility shouldn't require
a custom integration project per plant."
-->

# Multi-Site Operations — Solution Page Copy v1

**Page URL:** `/solutions/multi-site-operations`
**Primary audience:** Corporate operations director, multi-plant VP, COO at a multi-site manufacturer (3+ plants under one operational umbrella)
**Secondary audience:** Enterprise IT lead responsible for the fleet's monitoring infrastructure

---

## §1 Hero

### Copy

> ### Ten-plus plants on one operational view.
>
> EdgeConnect deploys at every plant. One EREMOS V2 tenant aggregates the fleet. Per-site identity. Per-site offline resilience. Consistent OEE definitions, alarm semantics, and shift reports across every site you operate.
>
> [ Book a scoping call for your fleet ]   ·   Download the datasheet

### Visual notes

- **Full-bleed hero image:** an aerial or wide shot suggesting multiple plants — could be a map-style visual with several plant locations marked, or a corporate operations dashboard view. Avoid generic "globalization" imagery.
- **Headline at display weight.** Subhead at body size, three short clauses for rhythm.
- **Trust strip under hero:** *Per-gateway UUID · Customer/site binding · Multi-tenant aggregation · Offline-resilient at every site*

### Reader-effect notes

- The corporate ops director should feel *"this is built for the way our fleet actually operates"* within 5 seconds.
- The four-part subhead — *deploys at every plant / aggregates / per-site identity / per-site offline resilience* — sets up the page's central architectural promise: each plant works on its own, the fleet works as a whole.
- "Consistent OEE definitions" is the line that lands hardest for this audience. Every multi-site manufacturer has the problem of plants computing OEE differently.

---

## §2 The challenge

### Copy

> ### The multi-site reality
>
> Every plant in your fleet has its own monitoring story. Plant A runs an old vendor SCADA. Plant B has a custom monitoring stack a previous IT director built. Plant C just acquired you, and you haven't even surveyed its floor yet. Each one produces its own OEE number, its own alarm list, its own shift report. The numbers don't reconcile. The semantics don't match. The reports arrive in different formats on different cadences.
>
> Corporate operations gets the worst of every world: a fleet view that doesn't actually view the fleet. Someone in the central office stitches plant-level reports into a quarterly board deck and labels the math "approximate." Performance comparisons across sites become aspirational. Acquiring a new plant means another six-month integration project before the new site shows up in the corporate view.
>
> The trap is treating fleet visibility as an aggregation problem — building a central data warehouse that pulls from each plant's existing tools. That works until the next plant's tools change, or the next acquisition brings a system you've never seen. The real answer is the inverse: standardize the data layer at the edge, and the fleet view becomes a subscription, not an integration project.

### Visual notes

- **Three narrative paragraphs.** No bullet lists.
- **Optional pull-quote in the margin (desktop):** *"A fleet view that doesn't actually view the fleet."*
- **Subdued visual treatment.** Strategic-reflective tone, not urgent.

### Reader-effect notes

- The reader (corporate ops director) should feel *"this person understands what corporate ops actually deals with."*
- "The numbers don't reconcile. The semantics don't match." is doing recognition work — every multi-site ops director has been in that meeting.
- The closing reframe (*"standardize the data layer at the edge, and the fleet view becomes a subscription"*) sets up §3's architectural argument.

---

## §3 The Elpis approach

### Copy

> ### How Elpis solves fleet visibility
>
> **One platform, deployed at every plant.** EdgeConnect runs locally at each site — Windows service on a small box in the control cabinet, sized to that plant's controller count. Every site uses the same platform, the same protocols, the same canonical vocabulary. New sites onboard the same way the first one did. There is no per-plant integration project.
>
> **One EREMOS V2 tenant aggregates the fleet.** Each EdgeConnect publishes to a central MQTT broker (yours or one we set up); EREMOS V2 subscribes and aggregates across every site. The corporate ops team sees the entire fleet on one operational view — every plant, every shift, every machine — without any per-plant data warehouse stitching.
>
> **Per-gateway identity, established at first start.** Each EdgeConnect carries a stable UUID and customer/site binding from the moment it's installed. Plant A's data is unambiguously Plant A's data. Acquisitions, divestitures, plant renames, regional reorganizations — the identity model survives all of them.
>
> **Per-site offline resilience is non-negotiable.** When a plant's network drops or the central broker is unreachable, that plant's EdgeConnect buffers locally with store-and-forward. When connectivity returns, the buffered data replays in order. No fleet outage when one site disconnects. No lost production data when corporate WAN has a bad afternoon.
>
> **Consistent KPIs across the fleet, not despite the fleet.** OEE Segments use the same definitions at every site. Alarm semantics use the same canonical names at every controller. Shift reports use the same templates configured against each site's actual shift schedule. The numbers reconcile because the definitions are platform-level, not plant-level.

### Visual notes

- **Five bolded-lead paragraphs.** Each lead is a fleet-architectural commitment; the body explains how it manifests.
- **Optional small inline icons** next to each bolded lead — restrained, functional.
- **The "Per-gateway identity" paragraph deserves slight visual prominence** — it's the unique architectural promise corporate ops directors don't realize they need until they encounter it.

### Reader-effect notes

- The reader should feel *"this is real fleet thinking, not plant thinking applied N times."*
- The closing line — *"the numbers reconcile because the definitions are platform-level, not plant-level"* — is the strongest architectural argument on the page for corporate ops directors.
- "Per-site offline resilience is non-negotiable" addresses the multi-site failure mode that most monitoring platforms quietly handle badly.

---

## §4 What's included

### Copy

> ### What's included for multi-site operations
>
> **From EdgeConnect (at every site):**
>
> - **Per-site Windows service** — sized to that plant's controller count. Linux on the roadmap for sites that need it.
> - **Native protocol coverage** — FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP. Every plant uses the same southbound stack regardless of vendor mix.
> - **Per-gateway UUID and customer/site binding** — established at first start. Plant identity is clean from day one and survives reorganizations.
> - **Store-and-forward at every site** — local SQLite buffering with per-sink cursors. Plants disconnected from the central broker continue collecting and replay on reconnect.
> - **Three-way diagnostics per site** — source, pipeline, sink. Central ops can see which site's data flow broke, and why.
> - **Connectivity Studio per site** — local plant teams can manage their own configuration without needing corporate IT for every tag change.
> - **Hash-chained audit log per site** — tamper-evident change history at every plant; aggregated for corporate audit review if needed.
>
> **From EREMOS V2 (central):**
>
> - **Multi-tenant aggregation** — one deployment, many sites or business units. Plant data is isolated per tenant.
> - **Fleet-wide asset model** — PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT, with each plant slotting into the corporate hierarchy.
> - **Consistent OEE Segments across sites** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Same definitions, same math, every site.
> - **Cross-site dashboards** — fleet-level views with the ability to drill into any plant, any line, any machine.
> - **Per-site alerting routes** — corporate alerts go to corporate channels; plant-level alerts go to plant channels.
> - **Reporting templates that scale** — same shift report template applied to every site against each site's actual schedule.
> - **Per-tenant access control** — corporate ops sees the fleet; plant teams see their plant; business unit leads see their BU.

### Visual notes

- **Two clearly separated sub-sections** (EdgeConnect at every site / EREMOS V2 central) with their own headers.
- **Bolded leads + plain-body explanations** for each capability.
- **Optional visual cue:** show the per-site/central split in the section layout itself — left column for EdgeConnect (per-site), right column for EREMOS V2 (central) with a subtle arrow or rule between them.

### Reader-effect notes

- The reader should feel *"this maps to how corporate ops actually thinks about the fleet — local autonomy + central visibility."*
- The per-site / central split is doing important architectural work — most enterprise IT buyers want to know who controls what before they sign anything.

---

## §5 Common fleet-management questions the platform answers

### Copy

> ### Questions multi-site operations raise
>
> If your fleet currently runs on per-plant monitoring tools held together by quarterly reports and email threads, here's what the platform changes:
>
> - **"How do we standardize OEE definitions across plants that currently calculate it differently?"** EREMOS V2 computes OEE Segments centrally from edge-collected signals using one consistent definition. Each plant's existing calculations become legacy; the platform's calculation becomes the source of truth.
> - **"What happens when a plant's internet drops?"** That plant's EdgeConnect buffers locally and replays on reconnect. No fleet outage. No lost production data. The plant continues collecting; only the central view temporarily lags for that site.
> - **"Can we benchmark performance across sites?"** Yes — fleet-level dashboards in EREMOS V2 compare any metric across any subset of sites. Same OEE definitions, same alarm semantics, same time alignment make the comparisons defensible.
> - **"Who owns the platform at each site vs centrally?"** Plant teams manage their site's EdgeConnect configuration (sources, sinks, tag maps) through the local Connectivity Studio. Corporate ops manages the EREMOS V2 tenant, dashboards, reports, and cross-site policies. Both layers have their own access control.
> - **"How does this scale when we acquire a new plant?"** Install EdgeConnect at the new site, point it at the central broker, register the gateway in the EREMOS V2 tenant — the new plant shows up in the fleet view. No per-plant integration project.
> - **"Can different business units have their own walled-off data?"** Yes. Each BU can be its own EREMOS V2 tenant with its own users, dashboards, and reports. Corporate ops can have an aggregate view across BUs if the org chart calls for it.
> - **"What happens if a plant runs older controllers our other plants don't have?"** EdgeConnect's protocol coverage is the same at every site — FOCAS2 (every Fanuc generation), MT-LINKi, MTConnect, Brother HTTP, Modbus TCP all ship today. Newer plants and brownfield sites use the same platform.

### Visual notes

- **Each question as a bold pull-quote**, answer in regular body weight underneath.
- **Generous line spacing.** Scan-bait, not paragraphs.
- **Optional small icon** next to each question.

### Reader-effect notes

- This section converts the corporate ops director from "evaluating" to "ready to scope a pilot site."
- Questions are written in real corporate-ops language — recognizable to anyone who has run a multi-plant operations review.
- The acquisition question is one of the strongest commercial moments — multi-site manufacturers often grow through acquisition, and integration friction is a real bottleneck.

---

## §6 Customer outcomes

### Copy

> ### What multi-site operations see when they deploy
>
> - **Fleet visibility that actually views the fleet** — one operational dashboard across every plant, every shift, every machine
> - **OEE numbers that reconcile across sites** — consistent definitions, consistent math, no more "approximate" board-deck footnotes
> - **Outage resilience per site** — one plant disconnecting never affects the others; buffered data replays on reconnect
> - **Acquisition integration in weeks, not quarters** — new plants onboard with the same architecture, not a custom project
> - **Local plant autonomy preserved** — plant teams manage their site's configuration without corporate IT bottleneck
> - **Cross-site benchmarking made defensible** — same OEE definitions, same alarm semantics, same reporting templates

### Visual notes

- **Bulleted outcome list, two-column on desktop, single-column on mobile.**
- **Bolded outcome lead + light-weight supporting clause** — same pattern as homepage.
- **No icons** — typography carries the rhythm.

### Reader-effect notes

- Each outcome maps to a real corporate-ops pain.
- The acquisition bullet is doing commercial work for growth-through-acquisition companies, which is most multi-site manufacturers.

---

## §7 What a typical multi-site engagement looks like

### Copy

> ### How multi-site fleets typically roll this out
>
> **Phase 1 — Pilot at one plant.** Pick the plant that's most representative of the fleet (or the most painful one). Standard CNC or brownfield engagement at that site — week 1 proof-of-value, weeks 2–4 cell expansion, weeks 5–8 full plant rollout. The pilot proves the platform against your actual production reality at one site.
>
> **Phase 2 — Second plant onboarding.** Once the pilot is operational, the second plant goes faster. Same architecture, same protocols, same canonical vocabulary. Plant teams trained from the pilot's playbook. Central EREMOS V2 tenant configured to aggregate both sites. Typical timing: 4–6 weeks for the second plant.
>
> **Phase 3 — Fleet rollout.** Remaining plants brought online in parallel where capacity allows. Each plant uses the same EdgeConnect installer, the same gateway registration flow, the same tag-mapping playbook. Cross-site dashboards in EREMOS V2 activate as each new plant comes online. Typical pace: 2–3 plants per month at sustained capacity.
>
> **Ongoing.** New acquisitions, new lines at existing plants, new business units — all onboard through the same architecture. The platform's per-gateway identity model survives every reorganization. Fleet visibility scales with the fleet, not with the integration team.

### Visual notes

- **Four-step horizontal timeline** on desktop; vertical stack on mobile.
- **Each step:** phase label, headline, 3-line description.
- **Subtle accent-color progress markers** between steps.
- **The "Ongoing" step** could visually loop or extend.

### Reader-effect notes

- The reader (corporate ops director thinking about timing and capacity) should feel *"this is a realistic rollout plan, not a vendor fantasy."*
- The "2–3 plants per month at sustained capacity" line is honest pacing — multi-site rollouts move at the speed of plant capacity for tag-map authoring and acceptance testing, not vendor-promised speed.
- The "Fleet visibility scales with the fleet, not with the integration team" closing line is the long-term architectural argument worth visual emphasis.

---

## §8 Architecture for multi-site operations

### Copy

> ### How it fits together across the fleet
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, with explicit multiplicity: three or more EdgeConnect instances at three or more plants, all publishing to a single central MQTT broker, feeding a single EREMOS V2 tenant. The Consumers tier shows central operations team plus per-plant local views. ]
>
> *EdgeConnect runs locally at each plant — sized to that site's controllers, resilient to local network outages, managed by the local plant team. EREMOS V2 aggregates centrally — fleet-level dashboards, consistent KPIs, per-tenant access control. Per-gateway identity makes the fleet view unambiguous. Per-site buffering makes the fleet view resilient.*

### Visual notes

- **Multi-site-specific variant of the master architecture diagram.** Same structure as the master, but with N plants explicitly rendered (3+ EdgeConnect instances), all feeding one central EREMOS V2 tenant.
- **Visual emphasis on the "many → one" pattern** — the diagram should make the architectural shape obvious at a glance.
- **Caption in italic** beneath the diagram, structured as two parallel two-sentence pairs.

### Reader-effect notes

- The diagram should communicate *"every plant works on its own, the fleet works as a whole"* visually before the caption reinforces it.
- This is the slide a corporate ops director will screenshot and paste into the next board update.

---

## §9 Final CTA

### Copy

> ### Bring us your fleet.
>
> Tell us about your plants — how many, where, what controllers, what your current monitoring looks like. We will scope a pilot at one site and a path to fleet rollout against your actual operational reality. No multi-year platform commitment required to prove the architecture works.
>
> [ Book a scoping call for your fleet ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace.** Same pacing as homepage and other solution-page final CTAs.
- **Primary CTA button in accent color.** Secondary as text link.
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- "Bring us your fleet" localizes the homepage CTA pattern to the corporate-ops audience.
- *"No multi-year platform commitment required to prove the architecture works"* is doing pre-emptive objection-handling against the SaaS-contract anxiety enterprise buyers carry.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 50 | Headline + subhead + CTAs |
| §2 The challenge | 240 | Three narrative paragraphs |
| §3 Elpis approach | 330 | Five bolded-lead paragraphs |
| §4 What's included | 340 | Two sub-sections, dense feature lists |
| §5 Common questions | 280 | Seven Q&A pairs |
| §6 Customer outcomes | 100 | Six bullets |
| §7 Typical engagement | 240 | Four-step phased timeline |
| §8 Architecture | 90 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,740 words** | Plus diagram + visual elements |

Slightly longer than CNC (~1,490) and brownfield (~1,600) because the multi-site audience reads more strategically — they need every fleet-architectural question answered before they'll engage.

---

## Visual / pacing guidance summary

- **Pacing matches other solution pages:** hero anchored → challenge intimate → approach explanatory → features dense → questions scannable → outcomes scannable → engagement phased → architecture visual → CTA confident
- **Imagery:** corporate-ops-aware. Multi-plant visualizations, fleet maps, corporate dashboards. Avoid generic "globalization" imagery or stock photos of executives in boardrooms.
- **Palette:** same dark premium industrial as homepage and other solution pages.
- **Mobile:** every section tested at 375px wide. The phase timeline in §7 stacks vertically; the §4 split sub-sections collapse cleanly.

---

## What's out of scope for v1

- **Real customer case studies** — placeholder language only until multi-site customers go public
- **Specific OEE improvement percentages claimed as "typical"** — never; the calculator handles that
- **Direct comparisons to per-plant SCADA vendors** — that's the objection-handling guide's job
- **Pricing on this page** — solution pages route to `/pricing` for the model; no numbers here
- **Acquisition-integration playbook detail** — surfaced as a benefit but not detailed; that's a separate sales-enablement document

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4, homepage copy v2, CNC solution page v2, brownfield page v2, and security page v2 for voice consistency
- [ ] No fabricated customer names or ROI claims
- [ ] Per-gateway identity language traces to `shared-knowledge/contracts/eremos-per-tag-mqtt.md` and CLAUDE.md §3 lock #19
- [ ] Multi-tenant claims trace to EREMOS V2 confirmed-features list (user-locked in session)
- [ ] Architecture diagram multi-site variant approved (designer brief from `architecture-diagram-spec-v2.md`)
- [ ] §5 fleet-management questions reviewed by Elpis sales lead — language must reflect actual corporate-ops conversations
- [ ] §7 engagement timeline reviewed against realistic multi-site rollout history
- [ ] CTA destinations confirmed (scoping-call form + datasheet download)
- [ ] Page tested at 375px mobile width

---

*Solution page — Multi-Site Operations, v1, 2026-05-24. Derived from CNC machining solution page v2 template, brownfield modernization v2 pattern. Third of five solution pages — two remaining: precision manufacturing, OEM machine monitoring.*
