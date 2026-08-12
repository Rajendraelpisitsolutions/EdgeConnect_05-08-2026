<!--
File:        docs/marketing/solution-precision-manufacturing-v2.md
Purpose:     Full copy for the /solutions/precision-manufacturing page on the
             Elpis website. The fifth and final vertical solution page.
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy.
Version:     v2 (post-review — three small refinements)
Date:        2026-05-24

Changes from v1 (3 small refinements; no structural revision):
  - §1 hero subhead: added closing sentence "Designed for environments
    operating under strict customer quality and audit requirements" —
    references the audience's operational reality without claiming any
    specific industry certification.
  - §4: tightened "Multi-tenant by design" bullet body (~20% trim,
    substance preserved).
  - §5: shortened the "Do we still have to maintain separate quality
    records?" answer from ~50 words to ~38 words for Q&A rhythm.

Per ChatGPT v1 review: v2 is final — freeze and hand to design. Closes
out the five-vertical solution-page set.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §8 (solution-page template)
  - docs/marketing/solution-cnc-machining-v2.md (template source)
  - docs/marketing/solution-brownfield-modernization-v2.md (mixed-vendor pattern source)
  - shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC tag set)
  - CLAUDE.md §1, §3 (locked architectural decisions)
-->

# Precision Manufacturing — Solution Page Copy v2 (final)

**Page URL:** `/solutions/precision-manufacturing`
**Primary audience:** Production manager / operations director at a precision-manufacturing shop or Tier-2/Tier-3 supplier; quality manager responsible for OEE and quality reporting to corporate or customers
**Secondary audience:** Industrial IT lead supporting the production environment

---

## §1 Hero

### Copy

> ### OEE you can defend, on the cells you actually run.
>
> Native FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP across high-mix, mixed-vendor production cells. Every input — cycle time, parts count, alarm state, tool wear — collected directly from the controller and timestamped at the edge. Designed for environments operating under strict customer quality and audit requirements.
>
> [ Book a scoping call for your production cells ]   ·   Download the datasheet

### Visual notes

- **Full-bleed hero image:** a precision-machining cell mid-production — close-up of a part being machined, coolant spray, focus on the precision detail rather than the overall machine. The visual cue is *"this is the work that has to be defensible"*, not generic factory imagery.
- **Headline at display weight.** "OEE you can defend" is the emotional anchor.
- **Trust strip under hero:** *Live integrations on Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i · Brother S700Xd1 · MTConnect · MT-LINKi · Modbus TCP. Canonical CNC vocabulary across vendors.*

### Reader-effect notes

- The precision production manager should feel *"this is built for the OEE pressure I'm under"* within 5 seconds.
- "OEE you can defend" speaks directly to the audience reality — corporate quarterly reviews, Tier-1 customer audits, internal continuous-improvement programs all demand defensible numbers.
- The closing subhead sentence — *"Designed for environments operating under strict customer quality and audit requirements"* — qualifies the audience precisely without overclaiming any specific certification framework.

---

## §2 The challenge

### Copy

> ### The precision-manufacturing reality
>
> A precision-manufacturing shop runs more product families in a week than most general CNC shops run in a month. Setup changes are constant. Tooling lifetimes are tracked to fractions of a percent. Tolerances are measured in microns. Every part has a customer attached to it, and every customer expects OEE numbers their auditor will accept.
>
> The shop floor that produces those parts is rarely uniform. A Fanuc 30i from 2022 runs alongside an 18i from 2011. A Brother S700Xd1 handles one product family; a Mazak Integrex handles another. Each machine's vendor-supplied dashboard reports OEE differently. Tool-life tracking lives in one system, parts counts in another, alarm history on a third. The numbers don't reconcile. The customer asks "what's the OEE for the cell that machines our part?" and the answer takes a week to stitch together.
>
> The trap is treating OEE as a reporting problem — building a spreadsheet that pulls from each system's quarterly export. That works until the customer audit team asks for the underlying signal data, the timestamp of every cycle, and the proof that the math is consistent with the contract. Stitched OEE numbers don't survive that level of scrutiny. Defensible OEE requires the signals to be collected at the controller, normalized at the edge, and computed centrally with one consistent definition.

### Visual notes

- **Three narrative paragraphs.** No bullet lists.
- **Optional pull-quote in the margin (desktop):** *"Stitched OEE numbers don't survive that level of scrutiny."*
- **Subdued visual treatment.** Empathy section — recognition over urgency.

### Reader-effect notes

- The reader (precision production manager) should feel *"this person understands the OEE-defensibility pressure I'm actually under."*
- The "customer audit team asks for the underlying signal data" line is doing recognition work — every Tier-2 supplier has experienced this kind of evidence-burden request.
- The closing line (*"defensible OEE requires the signals to be collected at the controller..."*) sets up §3's architectural argument.

---

## §3 The Elpis approach

### Copy

> ### How Elpis solves precision-manufacturing OEE
>
> **One platform reads every controller in the cell.** EdgeConnect polls each CNC over its native protocol — FOCAS2 for Fanuc (every generation), MTConnect for the newer multi-vendor machines, Brother HTTP for the Brother fleet, MT-LINKi for Fanuc's REST stack, Modbus TCP for older CNCs fronted by a PLC. One service on a small box in your control cabinet, regardless of how many vendors and generations the cell contains.
>
> **Every signal normalizes to the same vocabulary.** A spindle RPM from a 2022 Fanuc 30i and a 2011 Brother S700Xd1 both arrive at the platform as `spindle_rpm`. Cycle time, parts count, tool number, alarm code, axis positions — same names across every controller. The OEE math doesn't have to translate between vendors because the inputs already speak the same language.
>
> **EREMOS V2 computes OEE Segments with one definition.** Availability, Performance, Quality — calculated centrally from edge-collected signals using one consistent set of rules across every cell on your floor. The number you ship to corporate this week is computed the same way as the number you shipped last quarter. Customers asking for the underlying signal data get it.
>
> **Tool-life and cycle-time precision matter as much as the OEE rollup.** Tool-life telemetry trended per cell, per tool family, per product. Cycle-time variance visible per machine, per shift, per part run. When a customer asks "why did this batch's cycle time drift 3%?", the platform has the per-cycle history to answer.
>
> **Audit-ready by architecture.** Hash-chained configuration audit log captures every change to the gateway. Per-tag quality codes propagate end-to-end so downstream consumers can distinguish a real signal from a stale or uncertain one. The data layer is built for the audit before the audit happens, not after the auditor asks.

### Visual notes

- **Five bolded-lead paragraphs.** Each lead maps the platform to precision-manufacturing reality.
- **The "EREMOS V2 computes OEE Segments with one definition" paragraph should render with slight visual prominence** — this is the central OEE-defensibility promise.
- **Optional small inline icons** next to each bolded lead.

### Reader-effect notes

- The reader (precision production manager) should feel *"every OEE-defensibility concern I have is addressed architecturally."*
- The "one definition" framing is the strongest single message on the page — defensible OEE is fundamentally a consistency problem, not an analytics problem.
- The "audit-ready by architecture" closing reframes audit-readiness from a compliance burden into a platform property.

---

## §4 What's included

### Copy

> ### What's included for precision manufacturing
>
> **From EdgeConnect (edge runtime):**
>
> - **FOCAS2 collector across every Fanuc generation** — 0i, 16i, 18i, 21i, 30i, 31i, 32i. Axes, spindle, alarms, tool, production counters, programs.
> - **MTConnect, Brother HTTP, MT-LINKi, Modbus TCP collectors** — every controller in your high-mix cell, regardless of vendor or generation.
> - **Canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions, tool numbers, alarm codes. Same names, every vendor.
> - **Per-tag quality codes** — every signal carries a quality state (Good / Uncertain / Bad / Stale). Downstream consumers can distinguish a real value from a stale one.
> - **Store-and-forward buffering** — no lost cycles or parts counts during network or broker outages.
> - **Three-way diagnostics** — source, pipeline, sink. When data quality looks off, operators see exactly where it broke.
> - **Connectivity Studio** — web admin UI for adding machines, configuring tag maps, running Test Connection probes.
> - **Hash-chained configuration audit log** — tamper-evident change history for every gateway configuration change.
>
> **From EREMOS V2 (intelligence layer):**
>
> - **OEE Segments with one consistent definition** — Availability × Performance × Quality computed centrally from edge-collected signals; auditable; exportable.
> - **Per-cell aggregation** — OEE rollups at the cell level for high-mix shops where cells matter more than individual machines.
> - **Tool-life tracking per tool family** — wear trended across every tool in use; flagged before failure.
> - **Cycle-time variance per machine, per shift, per part run** — root-cause analysis becomes possible when every cycle is on the record.
> - **Persistent alarm tracking with incident grouping** — alarm patterns visible across days and weeks, not just within one shift.
> - **Shift reports in PDF and Excel** — the reports customer-quality teams want, in the formats they accept.
> - **Multi-tenant by design** — one platform, multiple sites; per-customer dashboards for shops reporting to multiple Tier-1 customers.

### Visual notes

- **Two clearly separated sub-sections** (EdgeConnect and EREMOS V2) with their own headers.
- **Bolded leads + plain-body explanations** for each capability.
- **Higher density section** — readers scan, not read.

### Reader-effect notes

- The reader should feel *"this covers the OEE-defensibility infrastructure I need, plus the precision-specific tool-life and cycle-time depth."*
- The "Per-cell aggregation" bullet is precision-specific — general CNC shops think in machines; precision shops think in cells.

---

## §5 Common precision-manufacturing questions the platform answers

### Copy

> ### Questions precision shops raise
>
> If your OEE numbers are getting harder to defend each quarter, or your customer audit team is asking questions your current reporting can't answer, here's what the platform changes:
>
> - **"Can we trace OEE numbers back to specific part runs?"** Yes. Every signal is timestamped at the edge and retained. OEE Segments link back to the cycle-level data that produced them. A customer asking "show me the OEE for this batch" can be answered with the underlying signal history.
> - **"Do we still have to maintain separate quality records?"** The platform handles production-signal OEE inputs. Inspection-equipment data can flow in via Modbus TCP or MQTT if your quality systems publish that way; otherwise quality data stays in your existing system. Less duplication, fewer fragmented records.
> - **"How do we handle small-batch / high-mix production where every cell is different?"** OEE Segments are configured per cell against that cell's actual shift schedule and product mix. The platform doesn't impose a one-size-fits-all OEE model on a high-mix shop.
> - **"Can we share OEE data with our Tier-1 customer without exposing other production data?"** Yes. Route-based architecture lets you publish exactly the data the customer is contracted to see — alarm state, cycle time, parts count for their parts only — without exposing other customers' production data or your shop's full operational picture.
> - **"What about tool-life trending across product families?"** Tool-life telemetry can be aggregated per tool family, per cell, per product. Wear patterns become visible across batches, not just within a single run.
> - **"How does this handle our oldest Fanuc next to our newest Mazak?"** Identically. The canonical vocabulary normalizes both controllers' signals to the same names. Your dashboard doesn't know (or care) which vendor produced which reading.

### Visual notes

- **Each question as a bold pull-quote**, answer in regular body weight underneath.
- **Generous line spacing.** Scan-bait, not paragraphs.
- **Optional small icon** next to each question.

### Reader-effect notes

- This section is doing the heaviest OEE-defensibility-anxiety defusion on the page.
- The customer-data-sharing question (route-based architecture) is the most important commercial moment — it tells Tier-2 suppliers they can deliver customer-contracted data without exposing their entire shop.

---

## §6 Customer outcomes

### Copy

> ### What precision shops see when they deploy
>
> - **OEE you can defend to corporate, to customers, and to auditors** — one consistent definition, edge-collected signals, traceable to the controller
> - **High-mix cells behave as one production system** — the canonical vocabulary normalizes mixed-vendor, mixed-generation controllers
> - **Tool failures caught weeks ahead of replacement** — tool-life trending across every tool in use, surfaced before failure
> - **Cycle-time variance becomes diagnosable** — per-machine, per-shift, per-part-run history makes root-cause analysis possible
> - **Customer-data sharing without exposing the whole shop** — route-based architecture publishes only what the customer is contracted to see
> - **Audit-ready data layer from day one** — hash-chained config history, per-tag quality codes, full signal retention

### Visual notes

- **Bulleted outcome list, two-column on desktop, single-column on mobile.**
- **Bolded outcome lead + light-weight supporting clause** — same pattern as homepage.
- **No icons** — typography carries the rhythm.

### Reader-effect notes

- Each outcome maps to a real precision-manufacturing pressure.
- The customer-data-sharing bullet is doing important commercial work for shops that serve multiple Tier-1 customers under different data-sharing contracts.

---

## §7 What a typical precision-manufacturing engagement looks like

### Copy

> ### How precision shops typically roll this out
>
> **Week 1 — Proof on the most-watched cell.** Pick the cell that produces parts for your most demanding customer (or the one whose OEE numbers you're least sure of). EdgeConnect installed on a small Windows box in your control cabinet, polling that cell's controllers. EREMOS V2 displaying real cycle-time, parts-count, and alarm data typically within the first few days.
>
> **Weeks 2–4 — Expansion across the cell or product family.** Add the rest of the machines in that cell, or the cells that handle related product families. Tag maps authored together with your team. OEE Segments configured against your actual shift schedule and product mix.
>
> **Weeks 5–8 — Full shop rollout.** Remaining cells onboarded. Cell-level and shop-level OEE aggregation in EREMOS V2. Per-customer dashboards if your shop reports to multiple Tier-1 customers separately. Audit-export workflows configured.
>
> **Ongoing.** New cells, new product families, new customers — all onboard through the same architecture. The OEE numbers you defend next year are computed the same way as the numbers you defend this week.

### Visual notes

- **Four-step horizontal timeline** on desktop; vertical stack on mobile.
- **Each step:** week label, headline, 2-3 line description.
- **Subtle accent-color progress markers** between steps.

### Reader-effect notes

- "Pick the cell that produces parts for your most demanding customer" is the right framing — precision shops already think in terms of customer priority.
- The closing line — *"the OEE numbers you defend next year are computed the same way as the numbers you defend this week"* — is the long-term consistency argument worth visual emphasis.

---

## §8 Architecture for precision manufacturing

### Copy

> ### How it fits together for precision production
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, with the Controllers cluster annotated for precision-manufacturing reality: *"Mixed-generation CNCs across high-mix cells"* — Fanuc (multiple generations) · Brother · Mazak · Okuma · Modbus-fronted older CNCs — feeding EdgeConnect, then through MQTT to EREMOS V2 where OEE Segments compute centrally. ]
>
> *EdgeConnect runs at your shop, reading every controller in every cell over its native protocol. EREMOS V2 computes OEE centrally with one consistent definition — across cells, across customers, across audit cycles. The signals trace back to the iron; the numbers trace back to the signals.*

### Visual notes

- **Precision-manufacturing variant of the master architecture diagram.** Controllers cluster names mixed-vendor reality; the OEE-computation path through EREMOS V2 deserves visual emphasis as the unique value flow for this audience.
- **Caption in italic** beneath the diagram. The closing two-sentence pair — *"The signals trace back to the iron; the numbers trace back to the signals"* — is the audit-defensibility anchor.

### Reader-effect notes

- The diagram should communicate *"your OEE numbers have provenance, end to end."*
- This is the slide a quality manager will use in an internal review to demonstrate the platform's audit-readiness.

---

## §9 Final CTA

### Copy

> ### Bring us your most demanding cell.
>
> Pick the cell whose OEE numbers you're least confident in defending. We will scope a proof of value against that cell specifically, including the audit-ready data trail. Demos run on real protocols against your real production cells — not on a polished demo bench.
>
> [ Book a scoping call for your production cells ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace.** Same pacing as homepage and other solution-page final CTAs.
- **Primary CTA button in accent color.** Secondary as text link.
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- "Bring us your most demanding cell" inverts the typical vendor demo dynamic — vendors usually want to demo on the easiest cell. Asking for the hardest signals confidence.
- *"Not on a polished demo bench"* is pre-emptive objection-handling against vendor-demo skepticism.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 70 | Headline + subhead (now with audience-qualifier line) + CTAs |
| §2 The challenge | 270 | Three narrative paragraphs |
| §3 Elpis approach | 320 | Five bolded-lead paragraphs |
| §4 What's included | 285 | Two sub-sections, tightened multi-tenant bullet |
| §5 Common questions | 225 | Six Q&A pairs, tighter quality-records answer |
| §6 Customer outcomes | 110 | Six bullets |
| §7 Typical engagement | 200 | Four-step timeline |
| §8 Architecture | 90 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,640 words** | Plus diagram + visual elements |

Density essentially unchanged from v1 — the v2 additions and trims cancel out.

---

## Visual / pacing guidance summary

- **Pacing matches other solution pages:** hero anchored → challenge intimate → approach explanatory → features dense → questions scannable → outcomes scannable → engagement reassuring → architecture visual → CTA confident
- **Imagery:** precision-manufacturing-aware. Close-up machining shots, tooling detail, multi-vendor cell views. No "automotive assembly line" stock photos.
- **Palette:** same dark premium industrial as homepage and other solution pages.
- **Mobile:** every section tested at 375px wide.

---

## What's out of scope for v2

- **Real customer case studies** — placeholder language only
- **Specific OEE percentages claimed as "typical"** — never; the calculator handles that
- **Industry-specific certification claims** (IATF 16949 for automotive, AS9100 for aerospace, etc.) — the §1 audience-qualifier line references "strict customer quality and audit requirements" *without* implying compliance with any specific framework the platform hasn't been formally certified against
- **Direct competitor comparisons** — that's the objection-handling guide's job
- **Pricing on this page** — solution pages route to `/pricing` for the model; no numbers here

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4, homepage copy v2, CNC solution page v2, brownfield page v2, security page v2, multi-site page v2, and OEM page v2 for voice consistency
- [ ] No fabricated customer names or ROI claims
- [ ] No industry-certification claims (IATF, AS9100, ISO 9001, etc.) — the audience-qualifier line is intentionally framework-neutral
- [ ] Every protocol claim traces to shared-knowledge contracts (CNC vocabulary, MQTT contract)
- [ ] Architecture diagram precision variant approved (designer brief from `architecture-diagram-spec-v2.md`)
- [ ] §5 OEE-defensibility questions reviewed by Elpis sales lead — language must reflect actual precision-shop production manager concerns
- [ ] §7 engagement timeline reviewed against realistic precision-shop project history
- [ ] CTA destinations confirmed (scoping-call form + datasheet download)
- [ ] Page tested at 375px mobile width

---

*Solution page — Precision Manufacturing, v2 (final), 2026-05-24. Per ChatGPT v1 review, no v3 planned. Completes the five-vertical solution-page set: CNC machining, brownfield modernization, multi-site operations, OEM machine monitoring, precision manufacturing.*
