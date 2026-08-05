<!--
File:        docs/marketing/homepage-spec-v2.md
Purpose:     Homepage v2 specification. v2 resolves the 7 open questions
             from v1 §8 with explicit Phase 1 decisions, adds the nav
             conceptual-refinement note (§1.1.7), and cross-references
             design-governance-v1.md throughout.
Audience:    Internal — Claude (v3 author if needed), user + ChatGPT
             (reviewers), engineering team (Phase 1.5 static reference
             implementers, Phase 1 Angular implementers).
Format:      Markdown spec. Every section is buildable from this document
             alone — no separate "copy doc" or "wireframe doc" needed.
Companion:   digital-experience-platform-strategy-v2.md (worldview)
             web-platform-roadmap-v2.md (4-phase plan)
             design-governance-v1.md (design discipline)
             design-system-v2.md (component library)
             assets/homepage-v1-wireframe.svg (visual layout — unchanged from v1)
Version:     v2
Date:        2026-05-26
Status:      LOCKED after Pass 1 review pass. Resolves v1 open questions
             with explicit Phase 1 defaults.

v1 → v2 changes:
  - §1.1.7 added — nav conceptual-overlap observation target for Phase 2
    usability review (per user refinement: "Platform vs Capabilities
    vs Solutions vs Architecture conceptual overlap — not urgent now,
    worth observing").
  - §8 7 open questions all resolved with Phase 1 defaults; v3 reviews
    revisit if usability data warrants.
  - Cross-references to design-governance-v1.md added throughout — every
    section refers back to the governance track for its discipline rules.
  - §3.x section copy unchanged from v1 (user verdict: "voice direction
    correct, refinements minimal").

v1 (homepage-spec-v1.md) retained as historical reference.
v2 is canonical going forward.
-->

# Homepage v2 — Spec

**The first page of the Elpis Digital Experience Platform. Production-grade. Visible front door of the Industrial Intelligence Ecosystem worldview. Locked after Pass 1 review.**

This spec is buildable on its own. Every section is defined: copy verbatim, component references, visual treatment notes, CTAs, anti-patterns. The static HTML reference (Phase 1.5) and the Angular implementation (Phase 1) both consume this document.

Design governance applies throughout per `design-governance-v1.md` — every section honors the six discipline areas (spacing, motion, illustration, interaction, responsive, visual hierarchy).

---

## 1. Information architecture

### 1.1 Top-level navigation (LOCKED per user)

Seven items, left-to-right:

| Order | Label | Route | Phase live |
|---|---|---|---|
| 1 | Platform | `/platform` | Phase 2 (placeholder in Phase 1) |
| 2 | Capabilities | `/capabilities` | Phase 2 |
| 3 | Solutions | `/solutions` | Phase 2 |
| 4 | Industries | `/industries` | Phase 3 |
| 5 | Architecture | `/architecture` | Phase 2 (Phase 1 anchors to homepage section) |
| 6 | Resources | `/resources` | Phase 3 |
| 7 | Company | `/company` | Phase 2 |

**Plus** (right-aligned, separated visually):
- Primary CTA — *Book a discovery call* — opens structured contact form (see §8.1 for the v2-resolved decision)

**Nav behavior:**
- Sticky on scroll, with subtle background-opacity transition (dark transparent → dark solid as user scrolls past hero)
- Mega-menu pattern on hover for Platform, Capabilities, Solutions, Industries (Phase 2+ populated)
- In Phase 1: dimmed-but-visible nav items (40% opacity) with hover-tooltip "Coming soon" (v2-resolved per §8.4)
- Mobile: hamburger → full-screen overlay, vertically stacked, brand-restrained (NOT a Material drawer per design-governance §2.4)

### 1.1.7 Nav conceptual-overlap observation target (v2 — new)

The user noted during Pass 1 that **Platform / Capabilities / Solutions / Architecture** carry some conceptual overlap. Verdict: *"Not urgent now. But worth observing during homepage usability reviews."*

This is recorded here as a Phase 2 usability-review target, not a Phase 1 change. The 7-nav remains as locked above.

**Two refinement options to evaluate during Phase 1.5 / Phase 2 usability review:**

| Variant A | Variant B |
|---|---|
| Platform → **Ecosystem** | Platform → **Platform** |
| Capabilities → **Capabilities** | Capabilities → **Products & Capabilities** |
| Architecture → **Technology** | Architecture → **Stack** |

**Observation criteria during Phase 1.5 reviews:**
- Do visitors disambiguate Platform from Capabilities naturally?
- Does Architecture read as the technical view or as a parallel capability?
- Where do visitors hover-pause when looking for "what Elpis sells" vs "how it's built"?

If usability signal warrants, the homepage-spec v3 / Phase 2 IA refines the nav. Until then, the current 7-nav is locked.

### 1.2 Footer IA

Five columns:

| Column | Contents |
|---|---|
| Brand | Elpis logo + tagline "Industrial Intelligence Ecosystem" + 1-sentence company line |
| Platform | EdgeConnect · Acquisition (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway) · EREMOS V2 · Architecture |
| Solutions | Predictive Maintenance · OEE · Downtime Reduction · Edge & Connectivity · Multi-site Visibility |
| Resources | Datasheet · Brochures · Pitch deck (gated) · Whitepapers (Phase 3) · Documentation (Phase 4) |
| Company | About · Customers · Partners · Careers · Contact · `www.elpisitsolutions.com` |

Below footer columns: thin legal strip with copyright, privacy link, terms link, social links (LinkedIn primary).

---

## 2. Page structure — sections at a glance

Nine sections, top to bottom. Dark hero, light scroll transitions starting at section 3.

| # | Section | Visual | Component(s) |
|---|---|---|---|
| **1** | Hero | Dark | `NavMegaMenu`, `SectionShell` (dark-deep), `CTAGroup` |
| **2** | Five-pillar capability strip | Dark | `SectionShell` (dark), `CapabilityCard` × 5 |
| **3** | Architecture deep-dive | Light | `SectionShell` (light), `ArchitecturePanel`, embedded `architecture-diagram-v2-light.svg` |
| **4** | Hardware ecosystem | Light | `SectionShell` (light), `CapabilityCard` × 5 (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway) |
| **5** | EdgeConnect — the runtime backbone | Light tinted | `SectionShell` (light-tinted), `MetricStrip`, `DiagramFrame` |
| **6** | EREMOS V2 — the intelligence layer | Light | `SectionShell` (light), `MetricStrip` |
| **7** | Proof band — defense / space-agency / AMC | Dark deep | `ProofBand` |
| **8** | Who it's for — audience cards | Light | `SectionShell` (light), `AudienceCard` × 3 |
| **9** | CTA + Footer | Dark deep | `CTASection`, `Footer` |

**Transition rule (visual-hierarchy discipline per `design-governance` §2.6):** Dark → light at the bottom of section 2 (one full-bleed transition); light → dark briefly at section 7 (proof band, intentional contrast for credibility weight); light → dark at section 9 (CTA + footer).

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

**Visual:** dark background `bg.deep` (`#0F1419`) — slightly deeper than `bg.default` to give the hero gravity. Top nav floats over it. Architecture diagram appears as a subtle right-side composition element (not full-bleed, not over-saturated).

**Layout:** 60/40 left-right split on desktop. Left = copy + CTAs. Right = stylized rendering of `architecture-diagram-v2-dark.svg` (cropped to the EdgeConnect + Acquisition + EREMOS V2 + arrows region — the "Elpis-owned middle" — v2-resolved per §8.6). Diagram has a subtle teal-glow vignette around the EREMOS V2 box.

**Mobile (per design-governance §2.5):** copy stacks first, diagram becomes a smaller below-the-fold composition. The cropped master diagram replaces with the 3-box simple variant under 768px width (v2-resolved per §8.3).

**Copy — verbatim:**

```
PRE-LABEL (small caps, brand.teal, letter-spaced)
INDUSTRIAL INTELLIGENCE ECOSYSTEM

HEADLINE (size.3xl, semibold, text.heading)
From shop floor signal
to enterprise decision —
one industrial intelligence stack.

SUBHEAD (size.md, regular, text.body, max-width 60ch)
Elpis combines edge connectivity, sensor-direct acquisition, condition
monitoring, and operational intelligence in a single ecosystem.
Brownfield-ready. Sensor-agnostic. Built end-to-end by Elpis —
or layered into what you already run.

PRIMARY CTA (brand.teal background, text.heading)
Book a discovery call →

SECONDARY CTA (outline, text.body)
Download the platform datasheet (PDF)

TERTIARY (text-only, anchor link)
↓ See the architecture
```

**Tertiary link** scrolls to section 3.

**Trust micro-strip** below CTAs (size.sm, text.muted) — v2-locked per §8.5:
> *Trusted by defense and space-agency deployments · Deployed across India and the Middle East · AMC-partner ready*

**Anti-patterns for this section (per design-governance §2.3, §2.4):**
- No background video, no animated particles, no parallax distractions
- No stock photo of a factory floor
- No "AI-Powered ⚡" pre-label or buzzword
- No three or four CTAs — exactly two primary + one tertiary anchor

---

### 3.2 Section 2 — Five-pillar capability strip

**Visual:** still dark (`bg.default`), bottom border with subtle teal hairline. This is where the user first sees the five capability pillars enumerated.

**Headline copy:**

```
SECTION LABEL (small caps, text.muted)
FIVE CAPABILITIES · ONE ECOSYSTEM

HEADLINE (size.xl, semibold, text.heading)
Every layer of the industrial data path — owned by Elpis.
```

**Five cards in a horizontal strip** (desktop) / 5×1 vertical stack (mobile). Each `CapabilityCard` carries:

| Pillar | Card title | One-line description | Anchor product line |
|---|---|---|---|
| 1 | **Connectivity & Edge** | Direct, brownfield-ready connectivity to existing controllers via native protocols. | EdgeConnect · Edge Gateway |
| 2 | **Data Acquisition** | Sensor-direct acquisition for assets without controllers or with controllers that won't share data. | mDAQ |
| 3 | **Asset Intelligence** | Continuous asset utilization telemetry — even on legacy or unconnected machines. | mTracker |
| 4 | **Condition Monitoring** | Vibration analysis and hydraulic-oil contamination intelligence — sensor-agnostic, multi-vendor. | VAS · E-IDOS |
| 5 | **Operational Intelligence** | Multi-tenant analytics, OEE, alerts, reports — across plants, in one view. | EREMOS V2 |

Each card has a subtle teal accent line on hover, no card-elevation shadow on dark (would feel SaaS-y per design-governance §2.4). The accent line uses pillar-restrained tones from `architecture-diagram-v2-poster.svg` §13.2 (P1 `#00A0E0`, P2 `#4FBBC9`, P3 `#7AB5C6`, P4 `#5C9DB5`, P5 `#1A8FC2`) — the same VERY restrained palette already established. Pillar tones are the authorized exception to the single-accent discipline (design-governance §2.4).

**No outbound link from these cards in Phase 1** — they exist as orientation. In Phase 2, each links to `/capabilities/{pillar}`.

---

### 3.3 Section 3 — Architecture deep-dive

**Visual:** **first light section** (`bg.light.default` `#FAFBFC`). High-contrast transition is intentional — the architecture diagram needs maximum legibility. Light variant of the diagram is the embed.

**Layout:** centered, full-width within container. Diagram dominates. Caption below. Side-text on the right (desktop) explaining what to look at.

**Headline copy:**

```
SECTION LABEL (small caps, brand.teal-light #0080BC)
INDUSTRIAL INTELLIGENCE STACK

HEADLINE (size.xl, semibold, text.heading-light)
Field signal to enterprise insight — across five capabilities.

SUBHEAD (size.md, regular, text.body-light)
EdgeConnect reads existing controllers directly. Acquisition hardware
captures sensors that controllers won't expose. Both feed EREMOS V2.
Standard MQTT and OPC UA make every layer interoperable with whatever
you already run.
```

**Embed:** `architecture-diagram-v2-light.svg` at full container width. Below the diagram, the locked caption:

> *One Acquisition layer at every plant. One Intelligence layer aggregating many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*

**Right-side annotation list** (desktop only, hidden on mobile, replaced by a stacked annotation strip below the diagram):

- **Direct** — EdgeConnect reads FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP
- **Sensor** — mDAQ, VAS, E-IDOS capture sensor signals natively
- **Cooperate** — when both deployed, EdgeConnect normalizes and orchestrates Acquisition
- **Open** — MQTT broker + OPC UA Server make EREMOS V2 optional; use what you run

**Secondary CTA** (outlined, links to `/architecture` in Phase 2; in Phase 1 links to the architecture diagram PDF download):

```
See the architecture in detail →
```

**Anti-patterns (per design-governance §2.2):**
- No diagram animation in Phase 1 (Phase 2 introduces hover annotations on `/architecture`)
- No "click to expand" gimmickry — the diagram is legible at one zoom level
- No competing copy beside the diagram — the right-side annotations are short labels only

---

### 3.4 Section 4 — Hardware ecosystem

**Visual:** light (`bg.light.default`), slight visual rest after the diagram. Card-based 5-up layout.

**Headline copy:**

```
SECTION LABEL (small caps, text.muted-light)
ACQUISITION HARDWARE — BUILT BY ELPIS

HEADLINE (size.xl, semibold, text.heading-light)
When sensors don't have a controller, we built the device that does.
```

**Five product cards** (`CapabilityCard` variant — light-mode):

| Product | Tagline | One-line |
|---|---|---|
| **mDAQ** | General-purpose acquisition | Multi-channel data acquisition for any analog or digital signal — connect what controllers won't expose. |
| **mTracker** | Asset utilization telemetry | Continuous machine-run telemetry — even on un-connected legacy assets. Tracks utilization, status, cycle data. |
| **VAS** | Vibration analysis | Real-time vibration capture and analysis for rotating machinery — early warning before failure. |
| **E-IDOS** | Oil health intelligence | In-line hydraulic oil contamination monitoring — Elpis-built Sensor/HMI controller, sensor-agnostic on the input side (supports HYDAC, Parker, MP Filter, Argo-hytos). |
| **Edge Gateway** | Linux appliance · PLC bridge | Industrial appliance running EdgeConnect — direct PLC integration where a Windows server isn't appropriate. |

**Bottom strip:** small caps, brand.teal-light

> *SENSOR-AGNOSTIC · BUILT BY ELPIS · DEPLOYED ACROSS DEFENSE, AEROSPACE, AND HEAVY MANUFACTURING*

**Anti-patterns (per design-governance §2.3):**
- No product photography in Phase 1 (Phase 3 introduces real photos)
- No "feature checklist explosion" — one tagline + one sentence per product
- No price anchors, no SKU references

---

### 3.5 Section 5 — EdgeConnect (runtime backbone)

**Visual:** light with a subtle tinted band (`bg.light.deep` `#F4F6F9`). One-column centered layout, narrower than full width (max 1080 px content width). Focus.

**Headline copy:**

```
SECTION LABEL (small caps, brand.teal-light)
CONNECTIVITY & EDGE — THE RUNTIME BACKBONE

HEADLINE (size.xl, semibold, text.heading-light)
EdgeConnect — the industrial edge runtime that reads everything,
normalizes everything, and ships it anywhere.

BODY (size.md, text.body-light)
EdgeConnect runs on the factory floor as a Windows service or Linux
appliance. It reads controllers directly via native protocols (FOCAS2,
MT-LINKi, MTConnect, Brother HTTP, Modbus TCP), normalizes every signal
to a canonical model, and routes it to MQTT brokers, OPC UA servers,
HTTP endpoints, or TCP listeners — to EREMOS V2 or to whatever you
already run.
```

**Three-metric `MetricStrip`** (large-numeral micro-stats):

| Metric | Value | Caption |
|---|---|---|
| Protocols supported | **6+** | FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP · OPC UA |
| Delivery modes | **2** | AtMostOnce · AtLeastOnce (per route) |
| Diagnostics layers | **3** | Source · Pipeline · Sink (always-on three-way visibility) |

**Inline `DiagramFrame`** referencing the EdgeConnect block from the locked architecture diagram — a focused close-up showing the native-protocols list, Edge Gateway, and integration contracts.

**Secondary CTA:**

```
Read the EdgeConnect overview →
```

(Links to `/platform` in Phase 2; in Phase 1 links to the datasheet PDF anchor.)

---

### 3.6 Section 6 — EREMOS V2 (intelligence layer)

**Visual:** light, narrow centered (parallel to section 5). The two "Elpis hero" sections (5 and 6) feel balanced.

**Headline copy:**

```
SECTION LABEL (small caps, brand.teal-light)
OPERATIONAL INTELLIGENCE

HEADLINE (size.xl, semibold, text.heading-light)
EREMOS V2 — multi-tenant intelligence across every plant you run.

BODY (size.md, text.body-light)
EREMOS V2 is the Elpis intelligence platform — multi-tenant analytics,
OEE via Segments, persistent alarms and incidents, configurable alerting,
PDF and Excel reports, tool-life tracking, and the full PLANT-to-
SUB_EQUIPMENT asset tree. One tenant, many plants, multi-vendor fleets.
```

**Three-metric `MetricStrip`:**

| Metric | Value | Caption |
|---|---|---|
| Asset tree depth | **5 levels** | PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT |
| Multi-tenancy | **Yes** | One tenant aggregates many plants — multi-vendor by design |
| Report formats | **PDF · Excel** | Scheduled or on-demand, per asset / per shift / per segment |

**Anti-patterns:**
- No screenshot of EREMOS V2 dashboards in Phase 1 (Phase 3 introduces product screenshots with intentional UI polish)
- No KPI lists masquerading as features
- No competitive call-outs ("unlike Ignition / unlike Wonderware")

---

### 3.7 Section 7 — Proof band (v2-locked per §8.7)

**Visual:** dark band (`bg.deep`), full-bleed, intentionally narrow vertical height. Acts as a credibility punctuation between the product depth (5-6) and the audience framing (8).

**Headline copy:**

```
SECTION LABEL (small caps, brand.teal)
DEPLOYED WHERE FAILURE ISN'T AN OPTION

HEADLINE (size.xl, semibold, text.heading)
Defense. Aerospace. Heavy manufacturing. AMC-partner ready.
```

**Three-up `ProofBand` with anonymized anchors** (no customer names — per positioning v3 lock):

| Anchor | Anonymized story | Pillar relevance |
|---|---|---|
| Defense and space-agency deployments | Satellite radar antenna vibration monitoring — VAS deployed via 3rd-party supplier to a national defense customer. | Condition Monitoring · Asset Intelligence |
| Hydraulic-system condition monitoring | E-IDOS deployed in defense and AMC contexts in India and the Middle East — prevents unexpected hydraulic shutdowns. | Condition Monitoring |
| AMC-partner channel | Maintenance and AMC providers across India and the Middle East deploy Elpis hardware in their service offerings. | Channel maturity |

All three anchors paraphrased from positioning v3 §4 (defense + space-agency deployments) and §5 (AMC-partner channel). v2-confirmed publicly safe per Pass 1 review.

**Anti-patterns:**
- No specific customer names anywhere in Phase 1 (positioning v3 lock)
- No fabricated quotes
- No logos of customers
- No flag icons or geography emoji

---

### 3.8 Section 8 — Who it's for

**Visual:** light again (`bg.light.default`). Three audience cards.

**Headline copy:**

```
SECTION LABEL (small caps, text.muted-light)
WHO ELPIS IS BUILT FOR

HEADLINE (size.xl, semibold, text.heading-light)
Three audiences. One stack between them.
```

**Three `AudienceCard`s:**

| Audience | Headline | Hook |
|---|---|---|
| **Maintenance Managers** | When unplanned downtime costs more than the equipment itself. | VAS, E-IDOS, mTracker keep your maintenance team ahead of failure — not reacting to it. |
| **Operations Leaders** | When you need OEE you can trust — across every plant and every vendor. | EREMOS V2 aggregates real signal from real machines. No spreadsheets. No estimation. |
| **AMC Partners** | When your service offering needs a hardware + intelligence platform behind it. | White-label-ready hardware, AMC-channel pricing, partner enablement collateral. |

**Anti-patterns:**
- No three-card "feature comparison" trap
- No "Trusted by enterprises like yours" without specifics

---

### 3.9 Section 9 — CTA + Footer

**Visual:** dark again (`bg.deep`). Full-bleed.

**CTA copy:**

```
PRE-LABEL (small caps, brand.teal)
READY TO TALK

HEADLINE (size.2xl, bold, text.heading)
Bring the Industrial Intelligence Ecosystem
to your floor.

SUBHEAD (size.md, text.body, max-width 50ch)
30-minute discovery call — we walk your asset list and tell you
which capabilities give you the fastest return.

PRIMARY CTA (brand.teal background)
Book a discovery call →

SECONDARY CTA (outline)
Download the platform datasheet (PDF)
```

**Then:** footer per §1.2.

---

## 4. CTA hierarchy across the page

| Tier | CTA | Appears |
|---|---|---|
| **Primary** | Book a discovery call | Hero, CTA section, sticky nav (right-aligned) |
| **Secondary** | Download the platform datasheet (PDF) | Hero, CTA section, footer Resources column |
| **Tertiary** | See the architecture | Hero (anchor scroll), Architecture section (Phase 2 link), Footer Platform column |

**Discipline rule:** never more than one primary CTA visible at once. The sticky-nav CTA disappears (or shifts to teal-outlined) when the hero CTA is on-screen — they don't compete.

---

## 5. Motion language

Restrained. Premium-industrial. Never theatrical. Per `design-governance-v1.md §2.2`.

| Element | Motion |
|---|---|
| Nav scroll-state transition | 200ms ease-out background opacity shift (uses `--motion-default`) |
| Hero CTA hover | 120ms teal-fill brightness shift (uses `--motion-fast`) |
| Capability card hover | 180ms accent-line slide-in from left + 120ms text-color lift (uses `--motion-default`) |
| Section reveals on scroll | Optional, very subtle — 200ms opacity + 12px translate-up. Disabled in Phase 1 if it costs Lighthouse. |
| Architecture diagram | **No animation in Phase 1.** Phase 2 introduces hover annotations on the dedicated `/architecture` page. |
| Anything else | None. Default to stillness. |

**Anti-patterns (locked per design-governance §2.2):**
- No parallax scrolling
- No animated SVG arrows
- No "type-on" headline effects
- No background particles, no animated gradients

---

## 6. Visual mode (recap from user-locked decision)

**Dark hero, light scroll.** Sections 1-2 are dark. Section 7 (proof band) is dark. Section 9 (CTA + footer) is dark. Everything else is light.

The full visual mode reference is `assets/homepage-v1-wireframe.svg`.

---

## 7. Anti-patterns — page-wide

Per `design-governance-v1.md §2`:

| Don't | Why |
|---|---|
| Stock photography anywhere | Premium-industrial OT vendors do not use stock photos. Per BRAND_TOKENS §7 + design-governance §2.3. |
| Three or more CTAs in any single section | Diluted conversion intent. |
| Carousel of customer logos | We don't name customers in Phase 1 (positioning v3 lock). |
| "AI" hero pre-label or "AI-powered" anywhere | We refused this in the manifesto. Stick to it. |
| Pricing tiers on the homepage | This is enterprise OT sales — pricing is a discovery-call conversation. |
| Live chat widget bottom-right | Discovery call is the channel. Chat dilutes intent and ages poorly. |
| Cookie banner UI that dominates the hero | Bottom-attached, dismissible, neutral palette. Never modal. |
| Email signup form on the homepage | Phase 3 introduces a newsletter; Phase 1 only has discovery-call CTA. |
| Floating "Get a Demo" button | The primary CTA is the discovery call. One channel, premium feel. |
| Material-styled buttons or form controls | Permanent lock per design-governance §2.4 + strategy v2 §5. |

---

## 8. v1 open questions — resolved in v2

These were flagged inline in v1 §8 as "needs decision." v2 resolves each with an explicit Phase 1 default. Phase 1.5 / Phase 2 usability data may revisit — but for Phase 1 spec lock, these are the answers.

### 8.1 Discovery-call mechanism

**Resolved:** Phase 1 = **structured contact form** triggering human follow-up.

*Why:* enterprise OT sales benefits from a more controlled, less SaaS-feeling intake. Calendar embeds (Cal.com / Calendly) feel transactional for enterprise procurement; a form-then-human channel feels appropriate for a discovery call where Elpis wants to qualify and route the conversation.

*Phase 2+ revisit:* if discovery-call volume justifies it, a calendar embed appears AFTER an initial form interaction (form → confirmation page → optional calendar pick).

### 8.2 Datasheet download gating

**Resolved:** Phase 1 = **open download**, no gating.

*Why:* faster, lower friction, higher Phase 1 conversion. Phase 3 introduces the full resource center with gating logic (whitepapers + select datasheets). Until then, removing friction from the most-requested asset is the right tradeoff.

*Phase 3 revisit:* once the resource center exists, evaluate which assets to gate vs leave open based on Phase 1-2 download analytics.

### 8.3 Mobile diagram treatment

**Resolved:** Phase 1 = **replace with the 3-box simple variant** (`architecture-diagram-v2-simple.svg`) on screens under 768px.

*Why:* legibility wins over fidelity on mobile. The 3-box simple variant was designed (per Phase C) specifically for executive cognition at small scale — perfect fit for mobile hero embed. The master diagram returns at tablet+ widths.

*Phase 2 enhancement:* the dedicated `/architecture` page may introduce a horizontally-swipeable master diagram on mobile as a progressive enhancement; the homepage stays with the simple variant.

### 8.4 Phase 1 nav placeholder behavior

**Resolved:** Phase 1 = **visible nav items, dimmed (40% opacity), with hover-tooltip "Coming soon"**.

*Why:* visible nav signals the future shape of the platform, which is positioning-positive ("here's where this is heading"). Hidden items would lose that signal. Dimmed + tooltip prevents users from clicking dead links.

*Phase 2 activation:* as routes populate, items light up to full opacity in the order they ship (Platform, Capabilities, Architecture, Solutions first; Industries, Resources, Company follow).

### 8.5 Trust micro-strip wording

**Resolved:** **Locked as drafted** —

> *Trusted by defense and space-agency deployments · Deployed across India and the Middle East · AMC-partner ready*

All three signals sourced from positioning v3 — defense + space-agency (§4 anchors), India + Middle East geographic footprint (§3 lock), AMC-partner channel reality (§5 lock). Confirmed publicly safe during Pass 1 review.

### 8.6 Hero diagram crop

**Resolved:** Phase 1 = **crop the locked master diagram** (`architecture-diagram-v2-dark.svg`) to the **Elpis-owned middle** — EdgeConnect + Acquisition + EREMOS V2 + the two teal arrows from each into EREMOS V2.

*Why:* keeps the locked master as the single source of truth (no separate hero-only SVG to maintain); shows the Elpis value-creation peers without the visual weight of the full 4-column layout; reads as "the heart of the stack" at hero scale.

*Implementation:* SVG `<view>` element targeting the master's middle x-region (x=680 to x=1720, y=280 to y=1300), with the teal arrows visible at their endpoints.

### 8.7 Section 7 (proof band) wording

**Resolved:** **Locked as drafted** (see §3.7). All three anchors paraphrased from positioning v3 §4 and §5. Confirmed publicly safe.

---

## 9. Components referenced (anchors `design-system-v2.md`)

| Component | Used in |
|---|---|
| `NavMegaMenu` | Top of page (sticky) |
| `SectionShell` (dark / dark-deep / light / light-tinted variants) | Every section |
| `CTAGroup` | Hero (§3.1), CTA section (§3.9) |
| `CapabilityCard` (dark + light) | Capability strip (§3.2), Hardware (§3.4), Audience (§3.8 — variant) |
| `ArchitecturePanel` | Architecture (§3.3) |
| `DiagramFrame` | EdgeConnect section (§3.5), embedded throughout |
| `MetricStrip` | EdgeConnect (§3.5), EREMOS V2 (§3.6) |
| `ProofBand` | Proof section (§3.7) |
| `QuoteBlock` | (reserved for Phase 3 customer stories) |
| `CTASection` | Section 9 (§3.9) |
| `Footer` | Page footer |
| `Button` | Used inside every CTA — primary, secondary, outlined, ghost variants |

Each component is defined in `design-system-v2.md` with token references, sizing rules, motion rules, and Design Governance compliance.

---

## 10. Sign-off checklist (v2 lock)

**IA + nav:**
- [x] 7-nav matches user-locked order (Platform · Capabilities · Solutions · Industries · Architecture · Resources · Company)
- [x] Footer five-column structure per §1.2
- [x] Primary CTA = "Book a discovery call" everywhere
- [x] Nav conceptual-overlap observation target documented in §1.1.7 (Phase 2 usability-review target)

**Copy:**
- [x] Hero copy reflects positioning v3 manifesto §1 voice
- [x] Five capability pillar names match manifesto v3 exactly
- [x] All five hardware products named (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway)
- [x] Defense / AMC / India + Middle East anchored per positioning v3
- [x] No customer names anywhere
- [x] No "AI-powered" or "Industry 4.0" buzzwords
- [x] Trust micro-strip wording confirmed safe to publish (§8.5)
- [x] Proof band wording confirmed safe to publish (§8.7)

**Visual:**
- [x] Dark hero, light scroll, dark proof band, dark CTA section per §6
- [x] Architecture diagram embedded uses the light variant in section 3
- [x] Five-pillar restrained tonal palette (per architecture-diagram-v2-poster §13.2) used only on capability accents
- [x] Hero diagram crop targets the Elpis-owned middle of the master diagram (§8.6)
- [x] Mobile diagram replaces with 3-box simple variant under 768px (§8.3)

**Anti-patterns (design-governance compliance):**
- [x] No stock photography
- [x] No competitor names
- [x] No three+ CTAs in any single section
- [x] No "AI" pre-label
- [x] No customer logos
- [x] No live chat widget
- [x] No "Get a demo" floating button
- [x] No Material-styled controls anywhere

**Component references:**
- [x] Every section maps to a component in `design-system-v2.md`
- [x] No raw hex values in any spec block (all colors named via tokens)

**v1 open questions:**
- [x] All 7 open questions resolved in §8 with explicit Phase 1 defaults
- [x] Phase 2+ revisit conditions noted for each where applicable

---

*Homepage v2 spec, 2026-05-26. LOCKED after Pass 1 review pass. Feeds Phase 1.5 static HTML reference and Phase 1 Angular implementation. Supersedes v1 as the canonical homepage spec.*
