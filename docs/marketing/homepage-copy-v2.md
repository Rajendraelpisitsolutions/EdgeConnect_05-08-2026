<!--
File:        docs/marketing/homepage-copy-v2.md
Purpose:     Production-grade homepage copy for elpisitsolutions.com (or
             equivalent). Every section of the homepage's body content, ready
             to drop into a wireframe and ship after one approval pass.
Audience:    Web designer, web developer, the user (final approval),
             copywriter (light polish pass before publication).
Format:      Markdown copy file. Each section presented as:
               - Copy (the words that appear on the page)
               - Visual / pacing notes (for the designer)
               - Reader-effect notes (what the visitor should feel / do)
Version:     v2 (post-review — final refinement pass)
Date:        2026-05-24

Changes from v1 (3 surgical refinements, no architecture change):
  - §1 Hero: locked Variation A as canonical. Variations B and C moved to
    the Appendix as future reuse candidates (B's "respects the data path"
    line is too strong to discard but doesn't belong as homepage hero).
  - §6 Why now: tightened first paragraph lead from "Controller fleets keep
    getting more mixed" → "Most plants now run mixed-vendor controller
    fleets" (more immediate, more operationally recognizable).
  - §8 Use cases: Precision Manufacturing card body weaves in "high-mix"
    qualifier — "high-mix, mixed-vendor production cells" — strengthens
    the operational vocabulary.

Per ChatGPT v1 review: no v3 planned. v2 is final — freeze copy and move
to wireframes, visual hierarchy, SVG integration, and production build.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md
    (canonical product narrative)
  - docs/marketing/website-messaging-architecture-v2.md §4
    (homepage section structure — locked)
  - docs/marketing/architecture-diagram-spec-v2.md
    (the SVG that embeds in §3)

Locked-truth sources: see datasheet v4 header. Every claim on the
homepage traces back to those sources. Do not add claims without a
matching entry.

Voice (inherits from datasheet v4):
  - Confident, technical, operational
  - Outcomes first, architecture second
  - Real protocol names as trust signals
  - No buzzwords: no "revolutionary," "Industry 4.0," "smart factory,"
    "AI-powered," "transformation," "next-gen"
  - No fabricated customer names, ROI percentages, or testimonials
-->

# Homepage Copy — Production v2 (final)

**Every section of the elpisitsolutions.com homepage, in the words that should appear on the page.**

This is the production-grade copy a designer drops into wireframes and a developer drops into HTML. Structural choices are locked by the [website messaging architecture v2](website-messaging-architecture-v2.md) §4 — this document fills in the words. Per ChatGPT v1 review, v2 is the final iteration before design execution.

Read this top-to-bottom in order; the pacing only works if each section follows the last. Visual hierarchy and density notes accompany each section so the designer knows how much real estate to allocate.

---

## Overall pacing guidance (designer brief)

The homepage should feel like a confident industrial product, not a SaaS marketing site. Concrete rules:

- **Short hero, dense mid-page, breathing CTA close.** The hero gets generous whitespace; the architecture and outcomes sections compress for scannability; the final CTA reopens to whitespace.
- **No autoplay video. No parallax. No spinning anything.** A static hero photo or subtle animated gradient is fine. Animation budget per §18 of the messaging architecture.
- **Dark premium palette throughout.** Light variant only if used inside specific sections (e.g. the Replace Spreadsheets section may invert for visual contrast).
- **Real shop-floor photography or geometric illustration only.** No stock-photo handshakes. No "happy worker with tablet."
- **One accent color used sparingly** — primary CTAs, key data points, the diagram's emphasized arrows. Restraint signals seriousness.
- **Mobile-first responsive design.** Industrial buyers read on phones during plant walks; the homepage must read perfectly at 375px wide.
- **First-contentful paint under 1.5s on 4 Mbps.** Performance is part of the brand (§18 of messaging architecture).

---

## §1 Hero (above the fold)

**Purpose:** Land the visitor in the category in 8 seconds. Establish what the platform is and who it's for.
**Position:** Top of page, full-bleed.
**Word count target:** Headline 8–12 words, subhead 25–40 words.

### Copy (canonical)

> **Unified industrial connectivity and operational intelligence for modern manufacturing.**
>
> Connect every controller on your floor. Measure OEE on signals collected at the source. Reduce downtime with persistent alarms and incident workflows. From the spindle to the dashboard, on one foundation.
>
> [ Book a scoping call ]   ·   Download the datasheet

### Visual notes

- **Background:** full-bleed photo or rendered scene of a CNC controller in low light. Avoid generic factory imagery (no aerial assembly lines). The image should imply specific OT realism — a real Fanuc or Brother panel close-up beats a generic "Industry 4.0 factory."
- **Typography:** headline at large display weight (suggest 48–72 px desktop, 32–40 px mobile). Subhead at body size, two-line max on desktop.
- **Trust strip immediately below the hero, before scrolling:** `Built for plants running Fanuc · Brother · Mazak · Siemens · Modbus TCP`
- **Primary CTA:** accent-color button. Secondary CTA: text link in light grey or off-white.
- **No animation on first paint.** Keep performance ruthless.

### Reader-effect notes

- The hero must answer "what is this?" in under 5 seconds. This version does that with category clarity (`Unified industrial connectivity and operational intelligence`) plus three concrete outcome verbs (`Connect / Measure / Reduce`).
- The closing line — *"From the spindle to the dashboard, on one foundation"* — is the platform-language anchor; signals architectural confidence without going technical.

---

## §2 Three-pillar section

**Purpose:** Translate the platform into three scannable claims, each leading to a deeper page.
**Position:** Immediately below hero, three-column grid (desktop) or stacked cards (mobile).
**Word count target:** 12–18 words per pillar body.

### Copy

> ### What the platform does
>
> | EDGE CONNECTIVITY | OPERATIONAL INTELLIGENCE | EDGE-FIRST BY DESIGN |
> |---|---|---|
> | **Speak every controller** | **Trust your OEE number** | **Run for years on a small box** |
> | One service for FOCAS2, MT-LINKi, MTConnect, Brother HTTP, and Modbus TCP. Adding a new vendor doesn't mean adding a new monitoring tool. | Multi-tenant analytics that turn edge-collected signals into OEE, alarms, incident workflows, and shift reports your team will actually read. | Offline-capable. Store-and-forward built in. Signed offline licensing. Air-gapped factories are first-class. |
> | Explore EdgeConnect → | Explore EREMOS V2 → | See the architecture → |

### Visual notes

- **Three equal cards on desktop.** Stack on mobile.
- **Eyebrow label** (`EDGE CONNECTIVITY`, etc.) in small caps, accent color, sits above each pillar headline.
- **Pillar headline** in large bold (one line each — keep the headlines short so they don't wrap on mobile).
- **Body copy** in body weight, 3 lines max per card.
- **Icons:** functional, not decorative. An edge-server silhouette for Edge Connectivity, an analytics chart for Operational Intelligence, a small-form-factor server for Edge-First. Single coherent icon set across all three (Tabler, Lucide, Phosphor, or custom).
- **Subtle hover state** on each card for clickability cue.

### Reader-effect notes

- The reader should feel "this is comprehensive without being overwhelming."
- Each pillar must work as a standalone tweet — no required context from the others.
- The three eyebrow labels become the navigation language used elsewhere on the site.

---

## §3 Architecture at a glance

**Purpose:** Show the layered platform visually. Most important visual on the page.
**Position:** Below the three-pillar grid. Full-width on desktop.
**Word count target:** Caption 30–45 words. CTA line 10 words.

### Copy

> ### Architecture at a glance
>
> [ branded SVG diagram per `architecture-diagram-spec-v2.md` ]
>
> *One EdgeConnect deploys at each plant. One EREMOS V2 tenant aggregates many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*
>
> [ See how it fits your plant — Book a scoping call ]

### Visual notes

- **Use the branded SVG from the v2 design spec.** Until the designer produces it, the Mermaid block in datasheet v4 is the structural reference.
- **Caption sits directly below the diagram** in italic, body size, centered or left-aligned to match the diagram's visual anchor.
- **CTA below the caption** as a text-link button (not the primary accent button — the primary CTA is reserved for the hero and final CTA).
- **Generous whitespace around the diagram** — at least 80px top and bottom. The diagram is doing all the visual work; don't crowd it.

### Reader-effect notes

- The reader should leave this section thinking "I understand how the platform fits together architecturally" without having read a single technical word.
- This is the slide that gets screenshotted and pasted into internal slide decks at the prospect's company — design it to survive that crop.

---

## §4 Designed for

**Purpose:** Qualification. Five customer types should self-identify (or qualify out gracefully).
**Position:** Below architecture. Five-column row on desktop, stacked on mobile.
**Word count target:** 8–12 words per customer-type line.

### Copy

> ### Designed for
>
> - **Multi-vendor CNC manufacturing plants** — one operational view across mixed Fanuc, Brother, and Mazak controllers
> - **Precision manufacturing operations** — OEE accountability across mixed-vendor production cells
> - **Brownfield modernization projects** — modernize the data layer without replacing the controllers
> - **OEM machine monitoring deployments** — ship connected equipment, diagnose remotely, no truck rolls for tag changes
> - **Multi-site industrial operations teams** — one platform, many sites, consistent KPIs

Each bullet links to the relevant solution page (`/solutions/cnc-machining`, `/solutions/precision-manufacturing`, etc.).

### Visual notes

- **Vertical list on mobile; horizontal grid on desktop** if space allows, otherwise stay vertical with generous spacing.
- **Small functional icon next to each line** — a CNC controller for CNC, a precision-machined component for precision manufacturing, a wrench for brownfield, a shipping carton for OEM, a multi-site map for multi-site.
- **Bolded customer-type name + light-weight outcome line.** Visual rhythm: bold–regular, bold–regular.
- **Each line is clickable** to its solution page. Hover state mandatory.

### Reader-effect notes

- The reader should think either "that's me" (and click into a solution page) or "that's not me" (and qualify out quickly without negative friction).
- Keep the outcome lines simple — they're not full sales pitches, just enough to confirm fit.

---

## §5 Outcomes you can hold us to

**Purpose:** Six concrete outcome promises. The "what you'll get" answer.
**Position:** Below Designed-for. Two-column grid of three outcomes each (desktop) or stacked (mobile).
**Word count target:** Outcome lead 4–6 words; supporting line 10–15 words.

### Copy

> ### Outcomes you can hold us to
>
> | | |
> |---|---|
> | **Cut unplanned downtime** — surface state changes the moment they happen, with persistent alarm tracking | **See your whole fleet** — multiple plants, multiple shifts, multiple vendors on one operational view |
> | **Trust your OEE number** — every input collected at the controller and timestamped at the edge | **Keep sensitive data where it belongs** — offline-capable; cloud is opt-in, not required |
> | **Modernize legacy controllers** — Fanuc 16i/18i, Brother S700Xd1, Modbus PLCs; no replacements needed | **Pass your audit** — hash-chained config history, per-tag quality codes, signed offline licensing |

### Visual notes

- **Bolded lead** for each outcome (3–4 words). Supporting line in regular body weight.
- **No icons in this section** — let the typography carry the rhythm. Too many icons in adjacent sections (pillars, designed-for, outcomes) creates visual fatigue.
- **Generous line spacing** — these are meant to be skimmed in 15 seconds.
- **Optional:** a single accent-color rule between the two columns to anchor the grid visually.

### Reader-effect notes

- The reader should pick one outcome and feel it land specifically — "that's exactly the problem I'm trying to solve right now."
- Don't overpromise. These are outcomes EdgeConnect + EREMOS V2 can deliver against, given the customer's own production constants.

---

## §6 Why plants are solving this now

**Purpose:** Operational urgency without fear-marketing. Bridge from qualification to outcomes.
**Position:** Between Outcomes and Replace Spreadsheets.
**Word count target:** Headline 6–8 words. Each paragraph 30–40 words.

### Copy

> ### Why plants are solving this now
>
> **Most plants now run mixed-vendor controller fleets.** Adding a new Brother cell next to an existing Fanuc line used to mean adding a new monitoring tool. Today's operations teams refuse to maintain three dashboards for one plant.
>
> **OEE accountability is climbing.** Customers, auditors, and corporate leadership want OEE numbers they can defend — collected from the controller, not stitched together from operator memory.
>
> **Manual reporting is the silent cost.** Shift handovers built on spreadsheets and phone calls drift, lose detail, and don't survive turnover. The reporting work is real labor — and recoverable.

### Visual notes

- **Three short paragraphs, vertically stacked.** Each lead sentence bolded.
- **Subdued visual treatment** — no icons, no big numbers, no animation. The text carries the section.
- **Subtle visual break before and after** to set this section apart from the more declarative sections around it.
- **No "act now" pressure language.** No countdown timers. No urgency manipulation.

### Reader-effect notes

- The reader should feel validated — "I'm not the only one dealing with this; the timing is right."
- Operational reality, not manufactured fear. The platform doesn't need fear-marketing to convert.

---

## §7 Replace spreadsheet operations

**Purpose:** The strongest commercial angle. Dedicated section.
**Position:** Below "Why now." Visually distinct — consider inverting palette here for contrast.
**Word count target:** Headline 4 words. Lead sentence 12–15 words. Bullets 8–12 words each. Closing line 25–30 words.

### Copy

> ### Replace spreadsheet operations
>
> Most plants already have the data. What they lack is a system that produces:
>
> - **Trusted timestamps** — every reading collected at the edge, not transcribed from a clipboard
> - **Auditable OEE** — Segment-based math you can show an auditor
> - **Persistent alarm history** — every fault on the record, not in someone's memory
> - **Unified machine visibility** — one operational view across CNCs, PLCs, and meters
> - **Centralized operational workflows** — shift reports as a record, not a phone call
>
> The Elpis platform replaces disconnected spreadsheets and manual shift reporting with a real-time operational system built directly on machine data.

### Visual notes

- **Visually distinct from surrounding sections.** Consider an inverted palette (light background, dark text) here, or a different background texture. This is the strongest commercial moment on the page — give it real estate.
- **Optional decorative element:** a faded screenshot of a generic Excel sheet on the left, the bulleted "after" state on the right. Visual contrast between "before" and "after." If used, keep the Excel image generic and unbranded — never a real customer's spreadsheet.
- **Bolded bullet leads** with regular-weight supporting clauses.
- **Closing line in slightly larger body weight** to give it presence.

### Reader-effect notes

- The reader should think "yes, I have exactly that spreadsheet problem right now."
- This section is doing the heaviest commercial work on the page. Don't rush past it visually.

---

## §8 Use cases

**Purpose:** Bridge to vertical pages. Five cards, one per solution page.
**Position:** Below Replace Spreadsheets. Five-card grid on desktop, carousel or stacked on mobile.
**Word count target:** Card headline 4–6 words. Card body 15–20 words. Link 4 words.

### Copy

> ### Where customers use it
>
> | Card 1 | Card 2 | Card 3 | Card 4 | Card 5 |
> |---|---|---|---|---|
> | **Multi-vendor CNC floors** | **Precision manufacturing** | **Brownfield modernization** | **OEM machine monitoring** | **Multi-site fleets** |
> | Twenty to a hundred CNCs across Fanuc, Brother, and Mazak controllers on one operational view, without per-machine custom scripting. | OEE accountability across high-mix, mixed-vendor production cells — every input collected directly from the controller. | Fifteen-year-old Fanuc 16i/18i controllers brought into a modern analytics stack via native FOCAS2 polling. | Ship connected equipment. Diagnose remotely. No truck rolls for tag changes when customers update their tag maps. | Ten-plus plants reporting into a single EREMOS V2 tenant. Outages buffer locally and replay on reconnect. |
> | Read solution brief → | Read solution brief → | Read solution brief → | Read solution brief → | Read solution brief → |

Each card links to its solution page.

### Visual notes

- **Five equal cards on desktop**, narrower than the three-pillar cards (these are denser).
- **On mobile:** horizontal-scroll carousel (with visible scrollbar) so visitors can swipe through.
- **Each card:** small icon at the top, headline, body, link CTA.
- **Hover state** lifts the card slightly or applies a subtle accent border.

### Reader-effect notes

- This is the navigation bridge — the reader picks a vertical they identify with and clicks through.
- The card descriptions are deliberately specific (`twenty to a hundred CNCs`, `high-mix, mixed-vendor production cells`, `fifteen-year-old Fanuc 16i/18i`, `ten-plus plants`) — specific operational language signals real customer pattern, not abstract marketing.

---

## §9 Customer logos strip (placeholder)

**Purpose:** Trust signal — when real logos exist.
**Position:** Below use cases.
**v1 state:** Placeholder only.

### Copy

> *Customer logos will appear here as our reference customers go public. Speaking with a current customer under NDA? [Contact us →](/contact)*

### Visual notes

- **Grey, low-contrast placeholder bar** with the text above. Designer-styled to look intentional, not "missing content."
- **Do NOT fabricate logos.** Do NOT use stock-photo brand marks. Do NOT mock up fake clients.
- **When real logos arrive,** replace this placeholder with a horizontal logo strip (5–8 logos, monochrome treatment, all on the same baseline).

### Reader-effect notes

- The placeholder communicates "we have customers, but we treat their references with care" — that's the trust signal until real logos arrive.

---

## §10 Final CTA

**Purpose:** Convert. Single primary action.
**Position:** Bottom of page, before footer. Full-width, generous whitespace.
**Word count target:** Headline 12–18 words. Body 20–30 words. CTA 5 words.

### Copy

> ### Bring us a plant.
>
> Bring us a representative plant — a controller mix, a target broker, an OEE definition. We will scope a proof of value against it. Demos run on real protocols against your real signals, not on canned data.
>
> [ Book a scoping call ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace top and bottom** (at least 120px desktop).
- **Headline in display weight**, slightly smaller than the hero headline (this is the second-strongest moment on the page; not the first).
- **Single primary CTA button** in accent color. Secondary as a text link.
- **No additional content below this section** before the footer. The footer is the only thing after the final CTA.

### Reader-effect notes

- The reader should feel "this is the next step, not the next page to read."
- The "bring us a plant" framing is deliberate — it signals consultative, not transactional.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 50–60 | Headline + subhead + CTAs |
| §2 Three-pillar | 90 | 3 cards × 30 words each |
| §3 Architecture | 55 | Caption + CTA line |
| §4 Designed for | 80 | 5 lines × 15 words each |
| §5 Outcomes | 110 | 6 outcomes × 18 words each |
| §6 Why now | 130 | 3 paragraphs × 40 words each |
| §7 Replace spreadsheets | 130 | Lead + 5 bullets + close |
| §8 Use cases | 135 | 5 cards × 27 words each |
| §9 Customer logos | 25 | Placeholder text |
| §10 Final CTA | 60 | Headline + body + CTAs |
| **Total page copy** | **~870 words** | Plus diagram + visual elements |

870 words is the right density for a premium industrial homepage. Long enough to do the work; short enough to scan in 90 seconds.

---

## What's out of scope for v2 of the copy

- **Localization** — English only; Japanese / German / Mandarin variants are a later deliverable
- **A/B test variants** — hero locked to single canonical version; testing infrastructure deferred to post-launch
- **SEO meta copy** — title tags, meta descriptions, OG cards are a separate deliverable derived from this
- **Real customer testimonials** — wait until customers are ready to be referenced
- **Animation copy / microinteraction labels** — designer's call, not part of body copy

---

## Sign-off checklist

Before this copy goes into production:

- [ ] Hero variation locked (Variation A, per v2)
- [ ] All five solution-page URLs confirmed (matches website-messaging-architecture-v2 §8.2)
- [ ] Voice and tone reaffirmed against datasheet v4 — no buzzwords slipped in
- [ ] No fabricated customer names, ROI percentages, or testimonials anywhere
- [ ] Designer briefed on architecture diagram (use SVG from `architecture-diagram-spec-v2.md`)
- [ ] Word count fits the visual density targets per section
- [ ] Performance budget targets (FCP < 1.5s) communicated to developer
- [ ] Accessibility audit committed for the page (contrast, keyboard nav, mobile)
- [ ] Analytics conversion events wired before launch (per messaging-architecture v2 §20)

---

## Appendix — Strategic notes on retired hero variations

Per ChatGPT v1 review, Variation A is the homepage hero. Variations B and C are not discarded — they're parked for reuse in contexts where they will land harder than the homepage front door.

### Variation B (parked) — *"respects the data path"*

> **The industrial intelligence platform that respects the data path.**
>
> Native protocol coverage for FOCAS2, MT-LINKi, MTConnect, Brother HTTP, and Modbus TCP. OEE you can audit. Alarms that close the loop. Offline-capable at the edge, multi-tenant at the centre.

**Why it's not the homepage hero:** the "respects the data path" line is too insider/architect-oriented for first-touch buyers. It assumes a reader who already knows that AI-in-the-data-path, fragile cloud dependencies, or opaque transforms are concerns. That reader exists — but they're not the median visitor landing on the homepage from a Google search.

**Where to use B instead:**

- **About page hero or brand-line.** When the visitor has already qualified themselves and wants to know what Elpis stands for, this line lands.
- **Campaign or expo tagline.** On a booth banner, a print ad, or a LinkedIn campaign aimed at the industrial-IT audience, "respects the data path" is a strong tribal signal.
- **Architecture-section tagline.** Could appear as a small overlay or section caption on the `/platform` page or the architecture-diagram section.

### Variation C (parked) — technical, IT-leaning

> **One platform, from every controller to every dashboard.**
>
> EdgeConnect speaks every protocol on your plant floor. EREMOS V2 turns the data into OEE, alarms, incident workflows, and shift reports. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.

**Why it's not the homepage hero:** too dense for first impression. Introduces too many nouns immediately (EdgeConnect, EREMOS V2, MQTT, OPC UA) for a visitor who hasn't yet decided whether to keep reading.

**Where to use C instead:**

- **EdgeConnect product page hero.** The reader who lands on `/edgeconnect` is already filtered — they came looking for the product. The density is appropriate.
- **Solution-page hero (technical verticals).** For `/solutions/oem-machine-monitoring` or `/solutions/multi-site-operations` where the buyer is OT-savvy, this kind of opening works.
- **Internal sales-deck opener.** When the audience is already engaged (in a scheduled meeting, not a cold web visit), C's density is a strength, not a liability.

---

*Homepage Copy — v2 (final), 2026-05-24. Derived from datasheet v4 and website messaging architecture v2. Per ChatGPT v1 review, no v3 planned — freeze copy and move to wireframes, visual hierarchy, SVG integration, and production build.*
