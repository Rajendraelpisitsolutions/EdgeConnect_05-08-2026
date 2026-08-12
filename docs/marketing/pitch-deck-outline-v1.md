<!--
File:        docs/marketing/pitch-deck-outline-v1.md
Purpose:     Executive pitch-deck outline for the Elpis Industrial Intelligence
             Platform (EdgeConnect + EREMOS V2). Derived from the v3 datasheet.
Audience:    Sales conversations, OEM discussions, partner pitches, expo storytelling,
             executive buyer briefings. (Investor-deck variant deferred — overlaps but
             needs financial slides this outline omits.)
Format:      Markdown outline. Each slide has: key message, content, visual notes,
             speaker notes. A designer takes this outline and produces the final
             branded deck in the Elpis visual identity (dark premium palette, steel
             grey, deep navy).
Version:     v1 (first draft)
Date:        2026-05-24

Source narrative: docs/marketing/elpis-industrial-intelligence-platform-v3.md
Locked-truth sources: see datasheet header for the full list. Every slide claim
traces back to one of those sources. Do not add claims without a matching entry.

Structure: 12 slides. Order is operational-outcome-first, architecture-second
(per ChatGPT v2 review of the datasheet). Outcomes appear before architecture.
-->

# Elpis Industrial Intelligence Platform — Pitch Deck Outline v1

**12 slides. Sales / OEM / partner / expo audience. Outcome-first.**

A designer should take this outline and produce the final branded deck. Each slide section below specifies:

- **Key message** — the single takeaway the slide must deliver
- **Content** — what appears on the slide (bullets, headlines, supporting text)
- **Visual notes** — what the designer should render
- **Speaker notes** — what the presenter says or emphasizes

Aim for one idea per slide. Resist the urge to cram. Visual hierarchy beats word count for this audience.

---

## Slide 1 — Title

**Key message:** Set the category and confidence in 8 seconds.

**Content**

- Title: **Elpis Industrial Intelligence Platform**
- Subtitle: *Unified industrial connectivity and operational intelligence for modern manufacturing*
- Presenter name + role + date
- Elpis IT Solutions wordmark / logo

**Visual notes**

- Full-bleed background photo: clean shop-floor scene, CNC controllers in focus, dark moody lighting
- Title typography: large, confident, sans-serif, white on dark
- Logo in lower-right corner, restrained

**Speaker notes**

- Don't read the slide. Open with one sentence: "I'm going to show you how to put your whole plant — every machine, every shift, every controller — on one operational view, without ripping out anything you already own."

---

## Slide 2 — The problem

**Key message:** Plants don't have a data problem. They have a decision problem.

**Content**

- Headline: **The data is already on the floor.**
- Three short stat-style bullets:
  - Fanuc, Brother, Siemens, energy meters — each speaks a different language
  - OEE numbers stitched together from spreadsheets and operator memory
  - Downtime detected in hindsight, not in the moment
- Subline: *Plants don't have a data problem. They have a decision problem.*

**Visual notes**

- Left half: three small icons representing different controllers, each with a different cable color (the chaos)
- Right half: the punchline subline, large
- Color: muted, slightly desaturated — set up the contrast for the next slide

**Speaker notes**

- Anchor with a specific scenario: "Your line manager finds out about a 47-minute downtime event at 6 AM the next morning, when she opens her email."
- Pause. Let it land.

---

## Slide 3 — Designed for

**Key message:** Five customer types should self-identify on this slide.

**Content**

- Headline: **Designed for**
- Five short bullets:
  - Multi-vendor CNC manufacturing plants
  - Automotive parts and precision machining operations
  - Brownfield modernization projects
  - OEM machine monitoring deployments
  - Multi-site industrial operations teams

**Visual notes**

- Five columns or five-row layout
- Each row: a small icon + the customer-type line
- Icons should be functional (a controller for CNC, a wrench for brownfield, a multi-site map for fleet) — not abstract

**Speaker notes**

- Pause at each bullet. Pick the one your prospect is, read it directly to them, then move on.
- If the prospect doesn't fit any of these five, this is the moment to qualify out gracefully.

---

## Slide 4 — The solution

**Key message:** One platform, from controller to dashboard, built on two products.

**Content**

- Headline: **The Elpis Industrial Intelligence Platform**
- Body paragraph (3 lines max):
  - *EdgeConnect collects from every controller on your floor.*
  - *EREMOS V2 turns that data into OEE, alarms, incident workflows, and reports.*
  - *Standard MQTT and OPC UA tie them together. From the spindle to the dashboard, on one foundation.*
- Tagline at bottom: *Two products. One platform. One operational view.*

**Visual notes**

- Two-column block: left for EdgeConnect (edge runtime), right for EREMOS V2 (intelligence)
- A simple arrow between them labeled "MQTT / OPC UA"
- No deep architecture yet — that's slide 8

**Speaker notes**

- Establish that this is two products that work together, sold and licensed independently. Don't dive into how yet — that's later.

---

## Slide 5 — Outcomes you can hold us to

**Key message:** Six concrete outcomes. Not promises — operational deliverables.

**Content**

- Headline: **Outcomes you can hold us to**
- Six bullets with bolded leads:
  - **Cut unplanned downtime** — persistent alarm tracking and incident workflows
  - **Trust your OEE number** — every input collected at the controller
  - **Modernize legacy controllers** — no replacements required
  - **See your whole fleet** — multiple plants, one operational view
  - **Keep sensitive data where it belongs** — fully offline-capable at the edge
  - **Pass your audit** — hash-chained config history, signed offline licensing

**Visual notes**

- Two-column grid of three outcomes each
- Each outcome: bolded lead in steel-grey, supporting line in lighter weight
- Avoid stock-photo "happy worker" imagery — use icons or geometric markers

**Speaker notes**

- This is the slide to spend time on. Read each outcome out loud. Ask the prospect which one matters most to them right now.
- The answer to that question shapes the rest of the conversation.

---

## Slide 6 — Replace spreadsheet operations

**Key message:** The platform replaces the work the spreadsheets are doing today.

**Content**

- Headline: **Replace spreadsheet operations**
- Subhead: *Most plants already have the data. What they lack is a system that produces:*
- Five bullets:
  - Trusted timestamps — not transcribed from a clipboard
  - Auditable OEE — Segment-based math you can show an auditor
  - Persistent alarm history — every fault on the record
  - Unified machine visibility — one view across CNCs, PLCs, meters
  - Centralized operational workflows — shift reports as a record, not a phone call

**Visual notes**

- Left half: a faded screenshot of a generic Excel sheet (the "before")
- Right half: the five bullets (the "after")
- Color shift between halves: muted on the left, sharp on the right

**Speaker notes**

- This is the moment to ask: "How many spreadsheets does your shift handover currently depend on?"
- Most prospects say three to seven. That's the opening.

---

## Slide 7 — Connectivity coverage

**Key message:** We support the protocols your controllers already speak.

**Content**

- Headline: **Connectivity coverage**
- Four small tables (or four columns):

| CNC controllers (southbound) |
| --- |
| FOCAS2, MT-LINKi, MTConnect, Brother HTTP |

| PLC + instrumentation (southbound) |
| --- |
| Modbus TCP |

| Messaging (northbound) |
| --- |
| MQTT (any compliant broker) |

| Enterprise integration (northbound) |
| --- |
| OPC UA Server |

- Footer line: *One canonical CNC vocabulary across every source. The same dashboard layout works across Fanuc, Brother, and Modbus-fronted machines.*

**Visual notes**

- Four colored category blocks, side by side or in a 2×2 grid
- Each block has a small icon (CNC controller, PLC, message bubble, server stack)
- Roadmap items (Siemens S7, OPC UA Client, HTTP / TCP sinks) shown in faint type at the bottom of the relevant block, marked "Coming"

**Speaker notes**

- This is the trust-signal slide for industrial IT buyers in the room.
- If you're in front of a CNC-heavy crowd, spend time on the CNC column. If you're in front of a multi-vendor fleet, spend time on the canonical-vocabulary footer line — that's the unique differentiator.

---

## Slide 8 — Architecture at a glance

**Key message:** Edge collects → integration carries → intelligence aggregates → consumers consume.

**Content**

- Headline: **Architecture at a glance**
- The Mermaid diagram from the datasheet (designer to render as branded SVG):
  - **Factory floor**: Controllers (CNCs, Modbus PLCs, meters) → EdgeConnect
  - **Integration layer**: MQTT broker + OPC UA Server
  - **Intelligence layer**: EREMOS V2 (multi-tenant analytics)
  - **Consumers**: Operations team (dashboards/alerts/reports), SCADA/MES/HMI (OPC UA clients), Cloud platforms (AWS / Azure / custom)
- Caption: *One EdgeConnect per plant. One EREMOS V2 tenant aggregates many sites. Standard MQTT and OPC UA make the integration interoperable.*

**Visual notes**

- Horizontal layered diagram: four columns from left to right (Edge / Integration / Intelligence / Consumers)
- Dark premium palette: steel grey backgrounds, navy boundaries, white text, single accent color for arrows
- This is the most important visual in the deck — invest design time here

**Speaker notes**

- Walk left to right. One sentence per layer.
- Don't dwell on the OPC UA Server box unless asked — most plant managers don't care; most SCADA engineers do.

---

## Slide 9 — Why Elpis

**Key message:** Eight differentiators, every one of them outcome-led.

**Content**

- Headline: **Why Elpis**
- Eight bullets, outcome-first phrasing:
  - **New protocols ship without breaking the old ones** — protocol-agnostic core by architecture
  - **Built to run for years on a small box in a control cabinet** — edge-first, not cloud-first
  - **Operators always know where the data flow broke** — three-way diagnostics by design
  - **Air-gapped factories are first-class** — RSA-signed, fully offline license
  - **A lapsed license never stops production data** — expiration blocks config changes only
  - **AI proposes; humans decide** — never silently alters the data path; local-LLM-capable
  - **Pay for the connectivity you actually use** — modular per-protocol activation
  - **Built for industrial workloads, not adapted IoT software**

**Visual notes**

- Two-column grid of four bullets each
- Highlight one row in accent color — the row that matters most to the specific audience (the deck has a swappable highlight)

**Speaker notes**

- This is where you stop pitching features and start positioning category.
- The two bullets to emphasize for almost every audience: "AI proposes; humans decide" and "A lapsed license never stops production data." Both are credibility wins competitors can't replicate without rewriting their products.

---

## Slide 10 — Deploy incrementally

**Key message:** Start small. Expand without disruption.

**Content**

- Headline: **Deploy incrementally**
- Body paragraph (3 lines):
  - *Start with one machine, one line, or one plant.*
  - *EdgeConnect runs side-by-side with what you already have. EREMOS V2 onboards new sites without changing the platform underneath.*
  - *No big-bang cutover. No platform-wide upgrade that breaks the plants already running.*
- Callout box: *Typical proof-of-value deployments begin with a single line or machine cell and expand incrementally once operationally validated.*

**Visual notes**

- A simple three-step adoption arrow: "One cell → One line → One plant → Fleet"
- The arrow continues but doesn't end (suggesting open-ended scaling)
- Soft, friendly color — addresses the deployment-anxiety subtext

**Speaker notes**

- This slide exists to defuse the biggest unspoken objection: "Will this disrupt my plant?"
- Read the callout box verbatim. Then pause.

---

## Slide 11 — Editions, modules, roadmap

**Key message:** Flexible licensing today. Disciplined roadmap tomorrow.

**Content**

- Headline: **Editions and modules**
- Three tier headers: **Starter** · **Professional** · **Enterprise** (no prices)
- Subline: *with optional industrial connectivity modules — FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, MQTT, OPC UA Server*
- Divider
- Second headline: **On the roadmap**
- Five bullets:
  - OPC UA Client (southbound)
  - Siemens S7 (southbound)
  - HTTP and TCP sinks (northbound)
  - Linux host support for EdgeConnect
  - AI-assisted operations agents — decision-support, human-confirmed, local-LLM-capable

**Visual notes**

- Top half: three columns for the editions (Starter / Professional / Enterprise), each a clean panel with module-checkbox indicators (designer fills in which modules per edition once that's locked)
- Bottom half: roadmap list with a small calendar icon
- No prices on the slide

**Speaker notes**

- Cover editions in 30 seconds. The detail belongs in the follow-up conversation, not the pitch.
- The roadmap is here as a trust signal: "We have a plan, and it serves the architecture, not the other way around."

---

## Slide 12 — Next step

**Key message:** Bring us a real plant. We'll scope a real proof.

**Content**

- Headline: **Next step**
- Body (3 lines):
  - *Bring us a representative plant — a controller mix, a target broker, an OEE definition.*
  - *We will scope a proof of value against it.*
  - *Demos run on real protocols against your real signals. Not canned data.*
- Contact block:
  - Presenter name + email
  - Elpis IT Solutions website + phone
  - Calendar booking link if available

**Visual notes**

- Centered, calm, white space heavy
- Single accent color call-to-action button equivalent ("Book a scoping call")
- Elpis logo prominent

**Speaker notes**

- Don't end with "Any questions?" End with "What would the first machine look like?"
- That question shifts the conversation from evaluating the pitch to evaluating their own plant.

---

## Designer briefing notes

When this outline becomes a branded deck, apply these consistently:

- **Palette:** dark premium industrial — steel grey backgrounds (#2A2E33 or similar), deep navy accents, single bright accent color (suggest Elpis orange/amber if it exists in the brand book) used sparingly for emphasis
- **Typography:** confident sans-serif (Inter, IBM Plex Sans, or the existing Elpis brand face). Large titles, generous line height, ruthless trimming of body text
- **Imagery:** real shop-floor photography only — no stock-photo handshakes, no abstract "AI brain" visuals. If real photos aren't available, use clean geometric icons over textured backgrounds
- **Architecture diagram (slide 8):** worth the most design time. The Mermaid version is structurally correct but visually flat — needs hand-drawn refinement
- **Slide footer:** subtle wordmark + page number, no clutter
- **Animation:** minimal. One reveal per click. No spinning logos, no fades that delay the speaker

---

## Out of scope for v1

- Localized variants (Japanese for OEM, German for Siemens-heavy markets) — deferred until base deck is approved
- Investor variant (needs financial slides: market size, revenue model, traction) — deferred to a separate outline
- Industry-specific decks (CNC-only, automotive parts, OEM monitoring, energy monitoring) — should derive from this master once approved
- Sales-objection-handling appendix — separate internal document, not part of the customer-facing deck

---

*Pitch Deck Outline — v1, 2026-05-24. Derived from Elpis Industrial Intelligence Platform datasheet v3.*
