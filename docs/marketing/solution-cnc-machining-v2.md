<!--
File:        docs/marketing/solution-cnc-machining-v2.md
Purpose:     Full copy for the /solutions/cnc-machining page on the Elpis
             website. The first vertical solution page; establishes the
             pattern for the other four (precision manufacturing, brownfield
             modernization, OEM machine monitoring, multi-site operations).
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy.
Version:     v2 (post-review — surgical polish only)
Date:        2026-05-24

Changes from v1 (3 surgical changes; no structural revision):
  - §3 Elpis approach: added one sentence near "One semantics, many vendors"
    — "The next CNC vendor added to your floor should not require a new
    monitoring stack." Strategic addition; strongest inevitability framing.
  - §5 Common operator questions: softened "Did the night-shift CNC
    operators run the right program?" to "Which program ran on the machine
    during the night shift?" Removes accusatory tone while preserving
    operational meaning.
  - §7 Typical engagement: softened "by end of day three" to "typically
    within the first few days." Avoids accidental contractual reading.

Two optional ChatGPT recommendations DELIBERATELY NOT applied — both
would have removed voice character:
  - "Each dashboard speaks its own dialect" kept (memorable metaphor)
  - "Your team will actually read" kept (humanity, rhythm, realism)

Per ChatGPT v1 review: v2 is final — freeze the CNC template; the other
four solution pages now inherit this pattern. No v3 planned.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §8 (solution-page template)
  - shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC tag set)
  - CLAUDE.md §1, §3 (locked architectural decisions)
-->

# CNC Machining — Solution Page Copy v2 (final)

**Page URL:** `/solutions/cnc-machining`
**Primary audience:** CNC machine-shop owner, production manager, plant manager at a multi-vendor CNC manufacturing facility (job shop, contract machining, precision components manufacturer)
**Secondary audience:** Industrial IT lead evaluating the platform for the shop's CNC fleet

---

## §1 Hero

### Copy

> ### One operational view across every Fanuc, Brother, and Mazak on your floor.
>
> Native FOCAS2, MT-LINKi, MTConnect, and Brother HTTP. Canonical CNC vocabulary across vendors. No per-machine custom scripting. From the spindle to the dashboard, on one foundation.
>
> [ Book a scoping call for your CNC floor ]   ·   Download the datasheet

### Visual notes

- **Full-bleed hero image:** real Fanuc 0i / Brother S700Xd1 / Mazak controller close-up — not a generic factory floor wide shot. The visual cue should be specifically *"these are the controllers we collect from"*, not *"this is manufacturing."*
- **Headline at display weight.** Subhead at body size, two-line max desktop.
- **Trust strip under hero:** *Live integrations: FOCAS2 · MT-LINKi · MTConnect · Brother HTTP — and Modbus TCP for the PLCs in front of older CNCs.*

### Reader-effect notes

- The reader (CNC shop owner) should think *"these are the exact controllers I own"* within 5 seconds.
- The "no per-machine custom scripting" line is the central pain reliever — every CNC shop with mixed vendors has felt this pain.

---

## §2 The challenge

### Copy

> ### The CNC shop reality
>
> Modern CNC shops don't have a CNC problem. They have an *integration* problem.
>
> A single shop floor typically runs three to seven different CNC vendors — Fanuc lathes from one era, Brother machining centers from another, an Okuma multi-axis, a Mazak Integrex, maybe a Heidenhain or two. Each vendor ships its own diagnostic software. Each diagnostic tool produces its own dashboard. Each dashboard speaks its own dialect.
>
> The result is predictable: production managers stitch OEE numbers together from spreadsheets. Maintenance learns about an alarm from the operator who happened to walk by. Tool changes get scheduled from clipboard memory. Cycle-time variance shows up in retrospect, after the shift is already lost. The data is on the floor — it just doesn't reach the people who need it.
>
> Replacing the controllers isn't the answer. They cost too much, they're already validated for the parts they're running, and operators know them by feel. The data layer is what needs to modernize, not the iron.

### Visual notes

- **Three short paragraphs.** No bullet lists in this section — the challenge is a narrative.
- **Optional sidebar pull-quote** in the right margin (desktop): *"The data is on the floor — it just doesn't reach the people who need it."*
- **Subdued visual treatment** — this is the empathy section, not the pitch.

### Reader-effect notes

- The reader should feel *"this person understands my shop."*
- The three-to-seven vendor count is realistic; calling out specific brands (Fanuc, Brother, Okuma, Mazak, Heidenhain) signals OT authenticity.

---

## §3 The Elpis approach

### Copy

> ### How Elpis solves CNC integration
>
> **EdgeConnect speaks every controller you own.** One service running on a small box in your control cabinet polls each CNC over its native protocol — FOCAS2 for Fanuc, MT-LINKi for Fanuc's REST stack, MTConnect for the open-standard machines, Brother HTTP for Brother's built-in web interface, Modbus TCP for older CNCs fronted by a PLC.
>
> **Every tag normalizes to the same vocabulary.** A spindle RPM reading from a Fanuc 0i, a Brother S700Xd1, and an Okuma OSP all become `spindle_rpm` in the canonical pipeline. The same applies to feed rate, parts count, cycle time, tool number, alarm code, axis positions. One semantics, many vendors — so the same dashboard works across your whole floor regardless of which CNC produced the signal. **The next CNC vendor added to your floor should not require a new monitoring stack.**
>
> **Data flows to EREMOS V2 for OEE, alarms, and reports.** EdgeConnect publishes to MQTT (or any compliant broker — Mosquitto, HiveMQ, AWS IoT Core, your existing infrastructure). EREMOS V2 subscribes, computes OEE Segments from the cycle-time and parts-count signals, tracks alarms as persistent records with incident workflows, and produces shift reports your team will actually read.
>
> **Nothing depends on the cloud.** EdgeConnect runs offline. If your network or broker goes down, the platform buffers locally and replays on reconnect. No lost cycles, no missing parts counts, no apologetic emails to the operations team.

### Visual notes

- **Four bolded-lead paragraphs.** Each lead is the central claim; the body is the supporting detail.
- **Consider small inline icons** next to each bolded lead — a controller silhouette for "speaks every controller", a unified-data icon for "normalizes to the same vocabulary", an analytics icon for "flows to EREMOS V2", a shield/offline icon for "nothing depends on the cloud."
- **The closing sentence of paragraph 2** — *"The next CNC vendor added to your floor should not require a new monitoring stack."* — should render slightly more prominently than the surrounding body. Designer's call: bold weight, accent color, or a subtle visual break that anchors the line.
- **Medium density.** This section is doing the heaviest explanatory work on the page.

### Reader-effect notes

- The reader (technical buyer mode) should now understand *what the platform does and how it does it* without needing a separate technical deep-dive.
- The new vendor-expansion sentence is the inevitability anchor — it reframes the platform from *"a thing you install"* to *"the architecture that protects you from doing this exercise again next year."*
- The "Nothing depends on the cloud" closing earns trust from shops that have been burned by cloud-dependent monitoring tools.

---

## §4 What's included

### Copy

> ### What's included for CNC machining
>
> **From EdgeConnect (edge runtime):**
>
> - **FOCAS2 collector** — Fanuc CNCs (0i, 16i, 18i, 21i, 30i, 31i, 32i). Axes, spindle, alarms, tool, production counters, programs.
> - **MT-LINKi collector** — Fanuc's REST-based machine-data product.
> - **MTConnect collector** — the industry-standard CNC streaming protocol; covers most modern multi-vendor CNCs.
> - **Brother HTTP collector** — Brother S700Xd1 and similar models via the built-in web-monitoring interface.
> - **Modbus TCP collector** — for older CNCs fronted by a PLC gateway.
> - **Canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions (`axes/x/absolute`, etc.), tool number and offsets, alarm codes. The same names appear regardless of which CNC produced them.
> - **Store-and-forward buffering** — never lose a cycle or a parts-count update because the broker was down.
> - **Three-way diagnostics** — source, pipeline, sink. Operators always know where the data flow broke.
> - **Connectivity Studio** — web admin UI to add machines, configure tag maps, and run Test Connection probes before anything goes live.
>
> **From EREMOS V2 (intelligence layer):**
>
> - **OEE Segments** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Computed from edge-collected signals; auditable.
> - **Persistent alarm tracking** — every CNC alarm becomes a tracked record with open/close state and incident grouping. No more "the alarm history was on machine 12's HMI."
> - **Tool-life ingestion** — dedicated path for tool-wear telemetry. Maintenance gets ahead of failures.
> - **Shift reports** — PDF and Excel. Built from edge-collected signals, not operator memory.
> - **Multi-tenant** — one platform, many sites or business units, no data leakage.
> - **Dashboards split by device class** — CNC, PLC, meter. Mixed fleets render cleanly.

### Visual notes

- **Two clearly separated sub-sections** (EdgeConnect and EREMOS V2) with their own headers.
- **Bolded leads + plain-body explanations** for each capability.
- **Optional:** small "product badge" icons next to each subsection header (an edge-runtime mark for EdgeConnect, an analytics mark for EREMOS V2).
- **Higher density section** — this is the "what you get" reference. Readers will scan it; they don't need every word on first pass.

### Reader-effect notes

- The reader should feel *"this covers the controllers I actually own"* — the FOCAS2 model list (0i, 16i, 18i, etc.) and the explicit Brother / MTConnect / Modbus mentions earn that credibility.
- The canonical-vocabulary bullet is the differentiator from competing CNC monitoring tools that maintain per-machine tag mappings.

---

## §5 Common operator questions the platform answers

### Copy

> ### Questions you can finally answer
>
> If your shift supervisors and maintenance team currently answer these by walking the floor with a clipboard, here's what the platform changes:
>
> - **"Which CNC is down right now, and why?"** Real-time machine status across every controller, with the active alarm code and message surfaced immediately.
> - **"How does this shift's OEE compare to last shift's, by machine and by line?"** OEE Segments aggregated by shift, machine, and line — instead of stitched together from spreadsheets after the shift is over.
> - **"Which tool is nearing end-of-life across the floor?"** Tool-life telemetry trended across every CNC, surfaced before the tool fails mid-cycle.
> - **"Are the alarms on machine 12 a one-off or a pattern?"** Persistent alarm history with incident grouping. Patterns become visible across days and weeks, not just within one shift.
> - **"What was machine 8's cycle time at 3 AM Tuesday?"** Every reading timestamped at the edge and retained. No more "the operator who knew is on holiday."
> - **"Which program ran on the machine during the night shift?"** Program execution history captured per controller, per shift.

### Visual notes

- **Each question rendered as a bold pull-quote** with the answer in regular body weight underneath.
- **Optional small icon** next to each question — a question-mark variant for visual cue.
- **Generous line spacing** — these are scan-bait, not paragraphs.

### Reader-effect notes

- The reader should recognize at least three of these as questions they currently *can't* answer easily.
- This is the section that converts the operations-side reader from "interested" to "we need to talk."
- The night-shift question is phrased neutrally (about the program, not the operators) — preserves the operational signal without setting up an accusatory frame inside the prospect's shop.

---

## §6 Customer outcomes

### Copy

> ### What CNC shops see when they deploy
>
> - **Single OEE truth across mixed-vendor CNCs** — no more reconciling numbers from three vendor dashboards
> - **Tool failures caught before they damage parts** — tool-life trending flags wear weeks ahead of replacement
> - **Cycle-time variance trended over shifts, days, weeks** — root-cause analysis becomes possible, not just retrospective
> - **Alarm patterns visible across the floor** — recurring faults surface as patterns, not isolated incidents
> - **New CNC vendors added without a new dashboard** — the next Brother, Mazak, or Heidenhain machine plugs into the same platform
> - **Shift handover becomes a record, not a phone call** — every shift's data is preserved, not lost to operator memory

### Visual notes

- **Bulleted outcome list, two-column on desktop, single-column on mobile.**
- **Bolded outcome lead + light-weight supporting clause** — same pattern as the homepage Outcomes section.
- **No icons** — the typography carries the rhythm.

### Reader-effect notes

- Each outcome should map to something the reader has actually felt the absence of.
- *"New CNC vendors added without a new dashboard"* is the long-term retention argument — every shop adds machines over time.

---

## §7 What a typical engagement looks like

### Copy

> ### How CNC shops typically roll this out
>
> **Week 1 — Proof of value.** One CNC, one shift, one OEE definition. EdgeConnect installed on a small Windows box in your control cabinet. FOCAS2 or Brother HTTP polling that machine. Data flowing to a Mosquitto broker we set up alongside, or to your existing MQTT broker if you already have one. EREMOS V2 displaying real cycle-time and alarm data typically within the first few days.
>
> **Weeks 2–4 — Expansion to a cell.** Add the rest of the machines on one line or one cell. Tag-map authoring done together with your team — your operators know the names that matter. Shift report templates configured. OEE Segments aligned to your shift schedule.
>
> **Weeks 5–8 — Fleet rollout.** Remaining CNCs onboarded. Multi-site or multi-line aggregation in EREMOS V2 if applicable. Alerting routed to the channels your operations team already uses.
>
> **Ongoing.** New CNC vendor added to the floor? The platform already handles it — FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP all ship today. Siemens S7 and OPC UA Client are on the roadmap for plants that need them.

### Visual notes

- **Four-step horizontal timeline** on desktop; vertical stack on mobile.
- **Each step:** week label, headline, 2-line description.
- **Subtle accent-color progress markers** between steps.
- **The "Ongoing" step** could visually loop back to the beginning, suggesting continuous expansion.

### Reader-effect notes

- The reader (deployment-anxious buyer) should feel *"this is methodical, not chaotic."*
- The week-by-week framing defuses the biggest unspoken question: *"How long until something is actually working?"*
- The "typically within the first few days" phrasing preserves deployment momentum without sounding contractual — important for enterprise buyers who read marketing copy carefully for implied SLAs.
- The "Ongoing" framing surfaces the platform's architectural flexibility — new CNCs don't break the deployment.

---

## §8 Architecture for CNC machining

### Copy

> ### How it fits together
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, with the Controllers cluster relabeled specifically: *"CNCs — Fanuc · Brother · Mazak · Okuma · Heidenhain"* + *"FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP"* ]
>
> *EdgeConnect runs at each plant. EREMOS V2 aggregates across plants if you operate more than one. Standard MQTT and OPC UA make the integration interoperable with whatever else you run — including the SCADA or MES you already have.*

### Visual notes

- **CNC-specific variant of the master architecture diagram.** Same structure as the master, but the Controllers cluster names specific CNC vendors and the southbound protocols emphasize CNC ones.
- **If a CNC-specific diagram doesn't exist yet,** use the master architecture SVG with a small inset callout zooming into the Controllers cluster.
- **Caption in italic** beneath the diagram.

### Reader-effect notes

- The diagram earns a specific kind of trust — the reader sees the architecture is *real*, not a generic block diagram.
- Mentioning *"the SCADA or MES you already have"* in the caption addresses the integration-anxiety question for shops that already have some monitoring infrastructure.

---

## §9 Final CTA

### Copy

> ### Bring us your CNC floor.
>
> A controller mix, a target broker, an OEE definition — that's all we need to scope a proof of value. We run demos on real protocols against your real signals. No canned data, no slideware, no vague promises.
>
> [ Book a scoping call for your CNC floor ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace.** Same pacing as the homepage final CTA.
- **Primary CTA button in accent color.** Secondary as text link.
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- The "Bring us your CNC floor" framing mirrors the homepage CTA but localizes it to the vertical.
- *"No canned data, no slideware, no vague promises"* is doing pre-emptive objection-handling against typical vendor-demo experience.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 50 | Headline + subhead + CTAs |
| §2 The challenge | 200 | Three narrative paragraphs |
| §3 Elpis approach | 270 | Four bolded-lead paragraphs (slightly longer with new vendor-expansion line) |
| §4 What's included | 320 | Two sub-sections, dense feature lists |
| §5 Common operator questions | 200 | Six Q&A pairs |
| §6 Customer outcomes | 110 | Six bullets |
| §7 Typical engagement | 200 | Four-step timeline |
| §8 Architecture | 70 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,490 words** | Plus diagram + visual elements |

Density is unchanged from v1 — the v2 additions are surgical, not expansionary.

---

## Visual / pacing guidance summary

- **Pacing matches the homepage:** hero generous → challenge intimate → approach explanatory → features dense → questions scannable → outcomes scannable → engagement reassuring → architecture visual → CTA spacious
- **Imagery:** all CNC-specific. Real Fanuc / Brother / Mazak controllers, real shop-floor scenes, real Connectivity Studio screenshots. No stock photography.
- **Palette:** same dark premium industrial as the homepage. The "Common operator questions" section is a candidate for a subtle palette shift (lighter background) to mark it visually.
- **Mobile:** every section tested at 375px wide. The timeline in §7 stacks vertically on mobile; the §4 feature lists collapse cleanly.

---

## What's out of scope for v2

- **Real customer case studies** — placeholder language only
- **Specific OEE percentages claimed as "typical"** — never; the calculator handles that
- **Cycle-time benchmarks** — too shop-specific to claim generically
- **Direct competitor comparisons** — that's the objection-handling guide's job
- **Pricing on this page** — solution pages route to `/pricing` for the model; no numbers here

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4 and homepage copy v2 for voice consistency
- [ ] No fabricated customer names or ROI claims
- [ ] Every protocol claim traces to shared-knowledge contracts
- [ ] Architecture diagram variant approved (designer brief from `architecture-diagram-spec-v2.md`)
- [ ] §5 operator questions reviewed by a real CNC shop floor lead — language must sound right to operators, not just marketers
- [ ] §7 engagement timeline reviewed by Elpis services lead — week-counts should reflect actual project history
- [ ] §3 vendor-expansion line (*"The next CNC vendor added to your floor should not require a new monitoring stack"*) rendered with appropriate visual prominence
- [ ] CTA destinations confirmed (scoping-call form + datasheet download)
- [ ] Page tested at 375px mobile width

---

## Template inheritance notes (for the next 4 solution pages)

The CNC machining page is the canonical pattern. The other four solution pages — Precision Manufacturing, Brownfield Modernization, OEM Machine Monitoring, Multi-Site Operations — should inherit the same structure:

1. **Hero** — vertical-specific outcome headline + protocol/capability-rich subhead
2. **The challenge** — narrative empathy section, no bullets, three short paragraphs
3. **The Elpis approach** — 4 bolded-lead paragraphs explaining the platform mapped to this vertical
4. **What's included** — split by EdgeConnect / EREMOS V2, dense feature lists
5. **Common operator questions** *(optional but recommended)* — practical, operator-language questions the platform answers. Earned by verticals where the audience is operations-side.
6. **Customer outcomes** — 5–6 bullets, bolded leads with supporting clauses
7. **Typical engagement** *(optional but recommended)* — week-by-week deployment timeline. Earned by verticals where deployment anxiety is real.
8. **Architecture** — branded SVG variant + caption
9. **Final CTA** — vertical-localized "Bring us your ___" framing

**Per-vertical variations to make in derivative pages:**

- **Precision Manufacturing** — emphasize tolerance/quality angle; OEE accountability across mixed-vendor cells; tighter on the "high-mix" operational reality
- **Brownfield Modernization** — lead with "the iron stays, the data layer modernizes"; FOCAS2 on Fanuc 16i/18i specifically; modernization-without-replacement framing
- **OEM Machine Monitoring** — different audience entirely (equipment builders, not plant operators); lead with "ship connected equipment / diagnose remotely / no truck rolls"; talk about per-customer fleet identity
- **Multi-Site Operations** — lead with "ten-plus plants on one operational view"; per-site EdgeConnect with central EREMOS V2 aggregation; outage-resilience story prominent

---

*Solution page — CNC Machining, v2 (final), 2026-05-24. Per ChatGPT v1 review, no v3 planned. Template pattern locked for inheritance by the other four solution pages.*
