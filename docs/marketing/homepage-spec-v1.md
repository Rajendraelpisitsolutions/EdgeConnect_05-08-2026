<!--
File:        docs/marketing/homepage-spec-v1.md
Purpose:     Homepage v1 specification — IA, section structure, verbatim
             copy, CTAs, visual treatment, component references, anti-patterns.
             Becomes the build spec for the Phase 1.5 static HTML reference
             and the Phase 1 Angular implementation.
Audience:    Internal — Claude (v2 author), user + ChatGPT (reviewers),
             engineering team (eventual implementers).
Format:      Markdown spec. Every section is buildable from this document
             alone — no separate "copy doc" or "wireframe doc" needed.
Companion:   digital-experience-platform-strategy-v1.md (worldview)
             web-platform-roadmap-v1.md (4-phase plan)
             design-system-v1.md (component library)
             assets/homepage-v1-wireframe.svg (visual layout)
Version:     v1
Date:        2026-05-25
Status:      DRAFT — pending user + ChatGPT review pass.

Companion to digital-experience-platform-strategy-v1.md. Spec for the
single page that begins Phase 1 of the Elpis DXP.
-->

# Homepage v1 — Spec

**The first page of the Elpis Digital Experience Platform. Production-grade. Visible front door of the Industrial Intelligence Ecosystem worldview.**

This spec is buildable on its own. Every section is defined: copy verbatim, component references, visual treatment notes, CTAs, anti-patterns. The static HTML reference (Phase 1.5) and the Angular implementation (Phase 1) both consume this document.

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
- Primary CTA — *Book a discovery call* — opens contact / calendar modal

**Nav behavior:**
- Sticky on scroll, with subtle background-opacity transition (dark transparent → dark solid as user scrolls past hero)
- Mega-menu pattern on hover for Platform, Capabilities, Solutions, Industries (Phase 2+ populated)
- In Phase 1: hover reveals "Coming soon — Phase 2" placeholder dropdowns (better than dead links)
- Mobile: hamburger → full-screen overlay, vertically stacked, brand-restrained (NOT a Material drawer)

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
| **1** | Hero | Dark | `NavMegaMenu`, `SectionShell` (dark), `CTAGroup` |
| **2** | Five-pillar capability strip | Dark | `SectionShell` (dark), `CapabilityCard` × 5 |
| **3** | Architecture deep-dive | Light | `SectionShell` (light), `ArchitecturePanel`, embedded `architecture-diagram-v2-light.svg` |
| **4** | Hardware ecosystem | Light | `SectionShell` (light), `CapabilityCard` × 5 (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway) |
| **5** | EdgeConnect — the runtime backbone | Light | `SectionShell` (light tinted), `MetricStrip`, `DiagramFrame` |
| **6** | EREMOS V2 — the intelligence layer | Light | `SectionShell` (light tinted), `MetricStrip` |
| **7** | Proof band — defense / space-agency / AMC | Dark (band) | `ProofBand`, `QuoteBlock` |
| **8** | Who it's for — audience cards | Light | `SectionShell` (light), `AudienceCard` × 3 |
| **9** | CTA + Footer | Dark | `CTASection`, footer |

**Transition rule:** Dark → light at the bottom of section 2 (one full-bleed transition); light → dark briefly at section 7 (proof band, intentional contrast for credibility weight); light → dark at section 9 (CTA + footer).

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

**Visual:** dark background `bg.deep` (`#0F1419`) — slightly deeper than `bg.default` to give the hero gravity. Top nav floats over it. Architecture diagram appears as a subtle right-side composition element (not full-bleed, not over-saturated).

**Layout:** 60/40 left-right split on desktop. Left = copy + CTAs. Right = stylized rendering of `architecture-diagram-v2-dark.svg` (cropped to the EdgeConnect + Acquisition + EREMOS V2 + arrows region — the "Elpis-owned middle"). Diagram has a subtle teal-glow vignette around the EREMOS V2 box.

**Mobile:** copy stacks first, diagram becomes a smaller below-the-fold composition.

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

**Trust micro-strip** below CTAs (size.sm, text.muted):
> *Trusted by defense and space-agency deployments · Deployed across India and the Middle East · AMC-partner ready*

**Anti-patterns for this section:**
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

Each card has a subtle teal accent line on hover, no card-elevation shadow on dark (would feel SaaS-y). The accent line uses pillar-restrained tones from `architecture-diagram-v2-poster.svg` §13.2 (P1 `#00A0E0`, P2 `#4FBBC9`, P3 `#7AB5C6`, P4 `#5C9DB5`, P5 `#1A8FC2`) — the same VERY restrained palette already established.

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

**Anti-patterns:**
- No animated diagram in Phase 1 (Phase 2 introduces hover annotations on `/architecture`)
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

**Anti-patterns:**
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

### 3.7 Section 7 — Proof band

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

Restrained. Premium-industrial. Never theatrical.

| Element | Motion |
|---|---|
| Nav scroll-state transition | 200ms ease-out background opacity shift |
| Hero CTA hover | 120ms teal-fill brightness shift (small) |
| Capability card hover | 180ms accent-line slide-in from left + 120ms text-color lift |
| Section reveals on scroll | Optional, very subtle — 200ms opacity + 12px translate-up. Disabled in Phase 1 if it costs Lighthouse. |
| Architecture diagram | **No animation in Phase 1.** Phase 2 introduces hover annotations on the dedicated `/architecture` page. |
| Anything else | None. Default to stillness. |

**Anti-patterns:**
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

| Don't | Why |
|---|---|
| Stock photography anywhere | Premium-industrial OT vendors do not use stock photos. Per BRAND_TOKENS §7. |
| Three or more CTAs in any single section | Diluted conversion intent. |
| Carousel of customer logos | We don't name customers in Phase 1 (positioning v3 lock). |
| "AI" hero pre-label or "AI-powered" anywhere | We refused this in the manifesto. Stick to it. |
| Pricing tiers on the homepage | This is enterprise OT sales — pricing is a discovery-call conversation. |
| Live chat widget bottom-right | Discovery call is the channel. Chat dilutes intent and ages poorly. |
| Cookie banner UI that dominates the hero | Bottom-attached, dismissible, neutral palette. Never modal. |
| Email signup form on the homepage | Phase 3 introduces a newsletter; Phase 1 only has discovery-call CTA. |
| Floating "Get a Demo" button | The primary CTA is the discovery call. One channel, premium feel. |

---

## 8. Open questions — flagged for v2 review

These are surfaced inline rather than silently decided. ChatGPT review pass + user input refines them in v2.

1. **Discovery-call mechanism** — calendar embed (Cal.com / Calendly) or a structured contact form that triggers human follow-up? Calendar embed = lower friction, but may feel SaaS-y. Form = more controlled, more enterprise-feeling.
2. **Datasheet download gating** — open download (faster, lower friction, no lead capture) or email-gated (lead capture, but slower)? Phase 1 default: open. Email gating arrives in Phase 3 with the resource center.
3. **Mobile diagram treatment** — the architecture diagram is dense for mobile. Phase 1 option A: replace with the 3-box simple variant on mobile. Phase 1 option B: keep the master diagram but make it horizontally swipeable.
4. **Phase 1 nav placeholder behavior** — hover-shows-"Phase 2 coming soon" placeholder vs. fully-functional nav that hides items not yet built. Default: visible nav items but inactive (dimmed) with hover-tooltip "Coming soon."
5. **Trust micro-strip wording** — current draft: *"Trusted by defense and space-agency deployments · Deployed across India and the Middle East · AMC-partner ready"*. Open: are all three signals safe to claim publicly per the positioning v3 lock?
6. **Hero diagram crop** — current draft crops `architecture-diagram-v2-dark.svg` to the Elpis-owned middle (EdgeConnect + Acquisition + EREMOS V2 + arrows into EREMOS). Alternative: use the 3-box simple variant for higher legibility at hero scale.
7. **Section 7 (proof band) wording** — the three "anonymized anchors" are paraphrased from positioning v3. Confirming wording is publicly safe.

---

## 9. Components referenced (anchors `design-system-v1.md`)

| Component | Used in |
|---|---|
| `NavMegaMenu` | Top of page (sticky) |
| `SectionShell` (dark + light variants) | Every section |
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

Each component is defined in `design-system-v1.md` with token references, sizing rules, and motion rules.

---

## 10. Sign-off checklist (v1 review)

**IA + nav:**
- [ ] 7-nav matches user-locked order (Platform · Capabilities · Solutions · Industries · Architecture · Resources · Company)
- [ ] Footer five-column structure per §1.2
- [ ] Primary CTA = "Book a discovery call" everywhere

**Copy:**
- [ ] Hero copy reflects positioning v3 manifesto §1 voice
- [ ] Five capability pillar names match manifesto v3 exactly
- [ ] All five hardware products named (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway)
- [ ] Defense / AMC / India + Middle East anchored per positioning v3
- [ ] No customer names anywhere
- [ ] No "AI-powered" or "Industry 4.0" buzzwords

**Visual:**
- [ ] Dark hero, light scroll, dark proof band, dark CTA section per §6
- [ ] Architecture diagram embedded uses the light variant in section 3
- [ ] Five-pillar restrained tonal palette (per architecture-diagram-v2-poster §13.2) used only on capability accents

**Anti-patterns:**
- [ ] No stock photography
- [ ] No competitor names
- [ ] No three+ CTAs in any single section
- [ ] No "AI" pre-label
- [ ] No customer logos
- [ ] No live chat widget
- [ ] No "Get a demo" floating button

**Component references:**
- [ ] Every section maps to a component in `design-system-v1.md`
- [ ] No raw hex values in any spec block (all colors named via tokens)

**Open questions:**
- [ ] All 7 open questions in §8 have a v2 decision or remain explicitly deferred

---

*Homepage v1 spec, 2026-05-25. DRAFT. Pending user + ChatGPT review. Feeds Phase 1.5 static HTML reference and Phase 1 Angular implementation.*
