<!--
File:        docs/marketing/architecture-diagram-spec-v3.md
Purpose:     Design spec for the branded architecture diagram v2 — the
             5-layer "Industrial Intelligence Stack" rendering that
             becomes the commercial operating model of Elpis.
Audience:    The session producing the SVG variants (this one + future).
             Designer-grade brief.
Format:      Markdown spec. Supersedes architecture-diagram-spec-v2.md
             which assumed a 4-column software-only platform model.
Version:     v3
Date:        2026-05-25

v2 -> v3 changes (in lockstep with positioning amendment v3):
  - 4 columns become 5 columns: Industrial Environment / Acquisition /
    Connectivity & Edge / Intelligence / Consumption.
  - Elpis hardware acquisition layer (mDAQ, mTracker, VAS, E-IDOS) is
    now an explicit hero column, not an implicit input.
  - "Industrial Intelligence Stack" becomes the named narrative anchor.
  - Caption pivots from a multi-site claim to a five-capability claim.
  - Hero columns reduce to two (Acquisition + Intelligence) — the
    Elpis-owned value-creation layers.
  - Visual storytelling priority: 10 seconds (understandable) →
    30 seconds (impressive) → 3 minutes (deep).
  - The diagram is the commercial operating model of Elpis, not the
    technical architecture documentation it was in v2.

v2 (architecture-diagram-spec-v2.md) retained as historical reference.
v3 is canonical going forward.
-->

# Architecture Diagram v2 — Design Spec v3

**Brief for the Industrial Intelligence Stack diagram that supersedes architecture-diagram-v1.**

The diagram appears in three primary contexts: the datasheet (page 2), the pitch deck (slide 8), and the homepage `/architecture` page (Phase D). One asset, multiple uses — designed for print PDF demands, derived down to web and slide.

**Structural model (rev2):** 4-column main row + 1 optional sidecar. The sidecar (Integrated Acquisition & Monitoring) sits above the central Connectivity & Edge column and drops in via a vertical teal arrow. The main horizontal flow is the brownfield-direct path: Environment → Connectivity & Edge → Intelligence → Consumption.

---

## 1. What the diagram must communicate

**At 10 seconds:** *"Elpis covers the entire industrial data path — from the floor to the enterprise — across five layers."*

**At 30 seconds:** *"Elpis owns the acquisition hardware AND the intelligence platform. The customer keeps everything else. End-to-end Elpis OR layered into what you already run, by customer choice."*

**At 3 minutes:** *"Each layer has named products with specific capabilities. The two Elpis-owned layers are heroes; the customer-owned ends are context. The flow is left-to-right, irreversible, with two emphasized arrows on the central Elpis value-creation path."*

This three-tier visual hierarchy (10s / 30s / 3min) is the design discipline. Every element earns its place by serving one of those readings.

---

## 2. The architecture — 4 columns with stacked-peer middle (locked rev3)

Four column-positions left-to-right. The middle column-position contains TWO stacked peer blocks (EdgeConnect on top, Acquisition on bottom) that cooperate bi-directionally. Names, positions, and flow direction are locked.

**Layout:**

```
                                        ┌────────────┐
                                        │ EdgeConnect│ ───→ ┐
┌────────────┐  ┬──→  ╔══════════════╗  └─────↕──────┘     ├───→ ┌────────────┐ ──→ ┌────────────┐
│ Floor      │  │     ║   COL 2      ║  (bi-directional)   │     │ EREMOS V2  │     │ Your       │
│            │  │     ║  EdgeConnect ║  ┌─────↕──────┐     │     │            │     │ Enterprise │
│            │  └──→  ║      +       ║  │ Acquisition│ ───→ ┘     │            │     │            │
│            │        ║ Acquisition  ║  └────────────┘           │            │     │            │
└────────────┘        ╚══════════════╝                            └────────────┘     └────────────┘
```

**The peer relationship is the rev3 architectural truth.** EdgeConnect and Acquisition are not parent/child or upstream/downstream — they are **peer entry points** into the intelligence layer:

- EdgeConnect reads existing controllers directly via native protocols (FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP) and publishes to EREMOS V2 via MQTT and OPC UA Server.
- Acquisition (Elpis hardware — mDAQ, mTracker, VAS, E-IDOS) captures field signals directly from sensors and publishes to EREMOS V2 via MQTT / HTTPS.
- Both paths converge at EREMOS V2. Customer can deploy one, the other, or both.
- The two blocks cooperate bi-directionally when both are deployed: Acquisition can feed EdgeConnect for normalization/routing; EdgeConnect can configure / orchestrate Acquisition devices.

**Why this structure (vs rev2 sidecar, vs rev1 5-column inline):**

- **rev1** had Acquisition as an inline column between Environment and Connectivity. Implied Elpis Hardware was *mandatory in the path*. Wrong.
- **rev2** moved Acquisition to a sidecar above Connectivity, with a vertical drop-in. Better than rev1 but still implied "Acquisition feeds Connectivity, which then feeds Intelligence." Acquisition was visually subordinate to EdgeConnect — also not architecturally accurate.
- **rev3** stacks EdgeConnect and Acquisition as peers, both publishing to EREMOS V2 directly. Captures the operational reality: mDAQ publishes via MQTT/HTTPS directly to EREMOS V2 today; E-IDOS will do the same (roadmap). EdgeConnect and Acquisition coexist as cooperating peers, not hierarchically.

The rev3 structure was proposed by Elpis on a whiteboard sketch (2026-05-25) with explicit correction that the EdgeConnect ↔ Acquisition arrow must be bi-directional.

### 2.1 Main row, Layer 1 — Industrial Environment

**Customer-owned, not Elpis.** The physical signal source.

Contents (icon + label):
- Sensors (pressure, flow, temperature)
- PLCs
- CNCs
- Hydraulic systems
- Rotating machinery
- Energy meters

Bottom hint: *"Customer-owned · Your floor"*

Visual tier: **tertiary** — outline-only, no fill. Conveys: "this is your domain."

### 2.2 SIDECAR — Integrated Acquisition & Monitoring (above Connectivity & Edge)

**Elpis hardware. Hero treatment, sidecar position.** Optional augmentation when sensor-direct or specialty acquisition is needed.

Contents:
- **mDAQ** — general-purpose acquisition
- **mTracker** — asset utilization telemetry
- *Sub-divider: CONDITION MONITORING*
- **VAS** — vibration analysis
- **E-IDOS** — oil health intelligence

Subtitle: *"Optional — when you need sensor-direct or specialty acquisition"*
Bottom hint: *"Sensor-agnostic · Built by Elpis · Use only when needed"*

Visual tier: **hero** (gradient fill, teal accent rule) but positioned ABOVE the main row as a sidecar. Drops into Connectivity & Edge via vertical teal arrow. Conveys: "Elpis builds the acquisition layer when needed; otherwise the customer's existing controllers feed EdgeConnect directly."

### 2.3 Layer 2 — Connectivity & Edge

**Elpis software + appliance. Central hub of the main row.** Edge runtime + integration boundary.

Contents:
- **EdgeConnect** (HERO title) — *Industrial edge runtime*
- *Native protocols list:* FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP
- **Edge Gateway** — Linux appliance · PLC bridge
- *Sub-divider: INTEGRATION CONTRACTS*
- MQTT broker — any compliant broker
- OPC UA Server — standard endpoint

Bottom hint: *"Protocol-agnostic · Store-and-forward · Three-way diagnostics"*

Visual tier: **hero** — gradient fill, teal accent rule, large EdgeConnect title. Connectivity & Edge is now visually the most central column on the main row, reflecting its role as the architectural hub that reads from both Elpis acquisition (sidecar drop-in) AND customer controllers directly (native protocols).

### 2.4 Layer 3 — Intelligence

**Elpis analytics platform. Hero column.** Operational intelligence.

Contents:
- **EREMOS V2** (large product title)
- Multi-tenant analytics
- OEE via Segments
- Persistent alarms + incidents
- Configurable alerting
- PDF + Excel reports

Bottom hint: *"One tenant · Many plants · Multi-vendor"*

Visual tier: **hero** — same gradient + teal stroke treatment as Acquisition. Conveys: "this is the second Elpis-owned value-creation layer."

### 2.5 Layer 4 — Consumption

**Customer-facing destinations.** Where the intelligence is consumed.

Contents:
- Operations team
- Maintenance / Reliability
- SCADA · MES · HMI
- Management / Executive dashboards
- Cloud platforms (AWS · Azure · custom)
- Mobile + multi-site

Bottom hint: *"Use what you already run"*

Visual tier: **tertiary** — outline-only, no fill. Conveys: "this is your enterprise."

---

## 3. Required arrows (locked rev3)

**Six arrows** total, organized as inputs → peer cooperation → outputs:

| # | From | To | Style | Why |
|---|---|---|---|---|
| 1 | Floor | EdgeConnect | Grey, horizontal, "DIRECT" label | Direct controller acquisition via native protocols |
| 2 | Floor | Acquisition | Grey, horizontal, "SENSOR" label | Sensor-direct acquisition via Elpis hardware |
| 3 | EdgeConnect ↕ Acquisition | (bi-directional vertical) | Grey, "COOPERATE" label | Bi-directional coordination when both deployed |
| 4 | EdgeConnect | EREMOS V2 | **Teal, solid (emphasized — Elpis value path #1)** | EdgeConnect publishes via MQTT / OPC UA |
| 5 | Acquisition | EREMOS V2 | **Teal, solid (emphasized — Elpis value path #2)** | Acquisition devices publish via MQTT / HTTPS |
| 6 | EREMOS V2 | Your Enterprise | Grey, horizontal (output) | Intelligence delivered to consumers |

**Arrows 4 and 5 are the two emphasized teal arrows.** Both paths into EREMOS V2 are first-class value-creation entries — neither is subordinate to the other. This is the rev3 architectural commitment: EdgeConnect and Acquisition are peer value contributors.

**The bi-directional arrow (#3)** uses an SVG marker with `orient="auto-start-reverse"` so the same shape renders on both ends of the line, flipped 180° on the start. Acquisition → EdgeConnect (data normalization, routing through the edge runtime) and EdgeConnect → Acquisition (configuration, polling, orchestration) both happen in practice; the bi-directional arrow captures both directions in one visual.

**Labels on input arrows** ("DIRECT" / "SENSOR") clarify the two parallel paths from the floor — direct controller acquisition (DIRECT) vs sensor-direct acquisition through Elpis hardware (SENSOR). Reader understands at a glance that these are alternative entry methods, not stages of one pipeline.

Inside the EdgeConnect block, the explicit native-protocol list (FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP) clarifies what "DIRECT" means.

Inside the Acquisition block, the four products are named (mDAQ · mTracker · VAS · E-IDOS) with a Condition Monitoring sub-divider grouping VAS + E-IDOS.

---

## 4. Headline and caption (locked)

### 4.1 Top headline (above the columns)

> **INDUSTRIAL INTELLIGENCE STACK**

Style: small caps, letter-spaced, BRAND_TEAL, the recurring narrative anchor from positioning v3 §5. Sized for "this is the title of the asset."

Optional subtitle directly under:

> *Field signal to enterprise insight — five capabilities, one ecosystem.*

Style: italic, TEXT_CAPTION color, smaller. Supports the headline; optional if the slide context already provides framing.

### 4.2 Caption (below the columns)

> *One Acquisition layer at every plant. One Intelligence layer aggregating many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*

Verbatim — locked. Adapted from v2 caption to reflect the five-layer worldview while preserving the multi-site + interoperability claim.

---

## 5. Visual hierarchy (locked rev2)

Three prominence tiers:

| Tier | Elements | Treatment |
|---|---|---|
| **Hero** | Connectivity & Edge (main row), Intelligence (main row), Acquisition (sidecar) | Gradient fill (HERO_GRADIENT), teal accent rule at top, larger title text. All three are Elpis-owned value-creation surfaces. |
| **Tertiary** | Industrial Environment (main row), Consumption (main row) | Outline-only (no fill), border.subtle stroke. Customer-owned boundary tiers. |

The three heroes collectively communicate: *Elpis owns the acquisition optionality, the edge runtime, and the analytics platform. The boundaries (environment, consumption) are the customer's domain.*

The sidecar position of Acquisition is the key visual signal that this hardware layer is *optional* — heroes inline are mandatory; heroes in sidecars are augmentation. The visual asymmetry (one hero above + two heroes inline) is the rev2 structural communication.

This differs from rev1 which used 5 inline columns with two heroes (Acquisition + Intelligence). The rev1 inline structure implied Acquisition was always in the path; the rev2 sidecar structure correctly communicates optionality.

---

## 6. Palette (BRAND_TOKENS v1 — unchanged)

Identical to v1 specs. Dark master uses dark palette; light variant uses light palette. Single teal accent across both.

| Element | Token | Hex |
|---|---|---|
| Background (master) | `bg.default` | `#1A1F26` |
| Hero container | gradient `surface.hero → surface.hero-br` | `#2A2F36 → #232830` |
| Secondary container | `surface.secondary` | `#3A4049` |
| Tertiary container | none (outline only) | — |
| Tertiary stroke | `border.subtle` | `#4A5560` |
| Secondary stroke | `border.strong` | `#5E6B78` |
| Hero stroke | `brand.teal` (subtler than v1 — `#4A5560` is fine, teal can be implied by accents inside) | `#4A5560` or `#5E6B78` |
| Text body | `text.body` | `#E8ECF1` |
| Text muted | `text.muted` | `#A8B3BD` |
| Text heading | `text.heading` | `#FFFFFF` |
| Text caption | `text.caption` | `#C8D0D8` |
| Accent (single) | `brand.teal` | `#00A0E0` |

---

## 7. Typography (Inter, BRAND_TOKENS-locked)

| Element | Size (master) | Weight |
|---|---|---|
| Top headline "INDUSTRIAL INTELLIGENCE STACK" | 32pt | Semibold, letter-spaced 0.15em, BRAND_TEAL |
| Top subtitle (optional) | 18pt | Regular italic, TEXT_CAPTION |
| Column layer label (top of column) | 14pt | Semibold, letter-spaced 0.18em, TEXT_MUTED |
| Hero column title (Acquisition, EREMOS V2) | 32pt | Bold, white |
| Secondary column title | 24pt | Semibold, white |
| Tertiary column heading | 22pt | Semibold, TEXT_BODY |
| Product / item names inside columns | 18pt | Regular, TEXT_BODY |
| Bottom hint per column | 14pt | Semibold, letter-spaced 0.12em, TEXT_MUTED |
| Bottom caption (below columns) | 20pt | Italic, TEXT_CAPTION, center-aligned |

Minimum readable size at 16:9 projection (3200×1800): 18pt.

---

## 8. Layout (master 2400×1600, rev2 sidecar)

- viewBox: `0 0 2400 1600` (3:2 aspect for master + datasheet embed)
- Safe margin: 5% from edges (120 px each side)
- Title row: y=140 to y=240
- **Sidecar (Integrated Acquisition & Monitoring):** x=680 to x=1160 (480 wide), y=240 to y=660 (420 tall) — positioned above Connectivity column
- **Main row:** y=720 to y=1300 (580 tall)
- Main row columns (4 × 480 wide + 3 × 80 px gutters = 2160 horizontal, centered):
  - Environment: x=120 to x=600
  - Connectivity & Edge: x=680 to x=1160
  - Intelligence: x=1240 to x=1720
  - Consumption: x=1800 to x=2280
- **Vertical drop arrow:** from sidecar bottom (920, 664) to Connectivity top (920, 716)
- Horizontal arrows on the main row at y=1010 (vertical center of main row)
- Caption row: y=1380 to y=1480

---

## 9. Anti-patterns — do NOT do

Carrying forward from spec v2 §12:

- No abstract "AI brain" imagery
- No spinning gears, "Industry 4.0" clichés
- No stock photography (handshakes, smiling operators)
- No more than one accent color (teal stays singular; the poster variant remains the only authorized exception)
- No AWS / Azure / GCP logos — vendor-neutral on cloud
- No emoji
- No "smart factory" buzzword captions
- No competitor names

v3-specific additions:
- No customer logos in the master diagram itself (defense / space-agency anchors live in surrounding page chrome, not inside the diagram)
- No protocol logos (MQTT, OPC UA stay as text labels)
- No "Industrial IoT" buzzword anywhere — the term "Industrial Intelligence" is the deliberate replacement

---

## 10. Output variants (5 to produce after master is locked)

Per v1 set:

| Variant | viewBox | Use |
|---|---|---|
| **Dark master** | 2400×1600 | Datasheet page 2 (caption embedded), default web embed |
| **Light** | 2400×1600 | White-background pages, white-paper PDFs |
| **16:9 slide** | 3200×1800 | Pitch deck slide 8 — caption omitted (deck carries it) |
| **3-box simple** | 1920×1080 | Executive "Industrial Intelligence Stack" elevator — 3 boxes: Field → Elpis → Enterprise |
| **Multicolor poster** | 2400×1600 | Large-format print, multi-color (authorized exception to one-accent rule) |

PNG fallbacks per variant at 2x resolution per spec v2 §10.

---

## 11. Sign-off checklist (extends v2 §14)

**Structural fidelity (rev2 sidecar):**
- [ ] Main row has 4 columns in order: Environment / Connectivity & Edge / Intelligence / Consumption
- [ ] Sidecar (Integrated Acquisition & Monitoring) positioned ABOVE the Connectivity & Edge column
- [ ] Sidecar labelled "Optional — when you need sensor-direct or specialty acquisition"
- [ ] All 4 Elpis hardware products named in sidecar (mDAQ, mTracker, VAS, E-IDOS)
- [ ] Condition Monitoring sub-grouping visible inside sidecar (VAS + E-IDOS under "CONDITION MONITORING" label)
- [ ] EdgeConnect prominent in Connectivity & Edge column with "Industrial edge runtime" subtitle
- [ ] EdgeConnect's native-protocol list visible (FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP)
- [ ] Edge Gateway named in Connectivity & Edge column
- [ ] EREMOS V2 named in Intelligence column
- [ ] 4 arrows present: 1 vertical sidecar→Connectivity (teal), 1 horizontal Env→Connectivity (grey, "DIRECT" label), 1 horizontal Connectivity→Intelligence (teal), 1 horizontal Intelligence→Consumption (grey)
- [ ] "DIRECT" label visible above Environment → Connectivity arrow
- [ ] Headline "INDUSTRIAL INTELLIGENCE STACK" present
- [ ] Caption verbatim per §4.2, at 22pt with TEXT_BODY color (brightened per rev2)
- [ ] Three heroes visually distinct (sidecar Acquisition + main-row Connectivity + main-row Intelligence)

**Anti-pattern checks:**
- [ ] No AI / cyberpunk imagery
- [ ] No customer or vendor logos
- [ ] No more than one accent color (master / light / slide / simple)
- [ ] No emoji or buzzwords
- [ ] No abstract decorative texture

**Export QA:**
- [ ] All 5 variants generated
- [ ] PNG fallbacks at 2x
- [ ] Text remains text in SVG (not outlined)
- [ ] Legible at projection scale
- [ ] Dark + light variants both pass contrast for body text + arrows

---

## 12. Strategic intent reminder

The architecture diagram v2 is no longer just *a diagram*. It is the **commercial operating model of Elpis**, rendered visually. Every executive, sales conversation, expo backdrop, investor pitch, and homepage hero references it.

The discipline:
- **10-second read** comes from clean column structure + two visible heroes
- **30-second read** comes from named products + named layers + clear flow
- **3-minute read** comes from the bottom hints + the caption + the surrounding deck/page narrative

The diagram should feel like *premium OT systems engineering*, not *SaaS marketing*. Dark palette, restrained motion, precise spacing, technical clarity. The current direction is correct — stay on it.

---

*Architecture Diagram v2 Design Spec v3, 2026-05-25. Locked. Replaces architecture-diagram-spec-v2.md as the design brief. Feeds Phase C SVG production.*

---

## 13. rev3 implementation log — variants delivered (2026-05-25)

### 13.1 Variant set produced

| Variant | File | viewBox | @2x PNG |
|---|---|---|---|
| Dark master | `assets/architecture-diagram-v2-dark.svg` | 2400×1600 | 4800×3200 |
| Light | `assets/architecture-diagram-v2-light.svg` | 2400×1600 | 4800×3200 |
| 16:9 slide | `assets/architecture-diagram-v2-slide.svg` | 3200×1800 | 6400×3600 |
| 3-box executive | `assets/architecture-diagram-v2-simple.svg` | 2400×900 | 4800×1800 |
| Multicolor poster | `assets/architecture-diagram-v2-poster.svg` | 2400×1880 | 4800×3760 |

PNG fallbacks rendered via `assets/render-v2-pngs.py` using PyMuPDF (`fitz`) at scale=2.

### 13.2 Notable variant decisions

- **Slide variant** centers the master content inside a 3200×1800 (16:9) canvas with 400px horizontal and 100px vertical padding; the descriptive caption block is omitted because the deck slide carries it externally.
- **Simple variant** is a deliberate redesign (not a simplification of the master) per ChatGPT lock-in note: "FIELD OPERATIONS → ELPIS INDUSTRIAL INTELLIGENCE STACK → ENTERPRISE OPERATIONS & DECISIONS." Emphasis is on operational transformation language; no products, protocols, or capability columns are shown.
- **Poster variant** preserves the master layout and adds (a) per-block pillar wayfinding chips on EdgeConnect and EREMOS V2, (b) per-product pillar markers inside Acquisition, and (c) a five-pillar legend strip in a footer band (canvas extended to 1880 high). Pillar tonal palette is restrained — five hues live within a 30° cyan-family band (P1 #00A0E0, P2 #4FBBC9, P3 #7AB5C6, P4 #5C9DB5, P5 #1A8FC2). No warm hues, no saturated chroma jumps — the differentiation reads as wayfinding up close and blends into a single tonal field at distance.

### 13.3 Hero fill — gradient → solid (portability fix)

The original spec §6 named a `surface.hero` gradient (`#2A2F36 → #232830` dark / `#FFFFFF → #F4F7FB` light). During PNG export QA, PyMuPDF was found to drop `<linearGradient>` references silently and fall back to black, making the light variant unusable.

**Decision:** swap the hero gradient for a solid midpoint color in all five variants:
- Dark heroes: `#272C33` (midpoint of `#2A2F36` and `#232830`)
- Light heroes: `#FFFFFF` (pure white card on `#FAFBFC` page bg)

The `<linearGradient id="heroGrad">` definitions remain in `<defs>` as historical reference but are no longer referenced. The visual difference from the original gradient is imperceptible at projection / print scale; the portability gain is significant (works in every renderer — PyMuPDF, browsers, Inkscape, ImageMagick).

### 13.4 rev3 peer-architecture sign-off

Replaces §11 sidecar checklist with the rev3 peer-architecture checks. **All items pass on the locked master and all 4 variants.**

**Structural fidelity (rev3 peer architecture):**
- [x] 4 column-positions left-to-right: Floor / Stacked-peer middle / EREMOS V2 / Your Enterprise
- [x] Column 2 contains two stacked hero blocks — EdgeConnect on top, Acquisition on bottom
- [x] OPTIONAL pill on Acquisition block (top-right corner)
- [x] All 4 Elpis hardware products named in Acquisition (mDAQ, mTracker, VAS, E-IDOS)
- [x] CONDITION MONITORING sub-grouping visible (VAS + E-IDOS)
- [x] EdgeConnect hero block carries "Industrial edge runtime" subtitle
- [x] Native-protocol list visible inside EdgeConnect (FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP)
- [x] Edge Gateway named inside EdgeConnect
- [x] EREMOS V2 named in Intelligence column
- [x] Floor and Your Enterprise rendered as tertiary (outline-only) tall columns

**Arrows (rev3 peer set — 6 total):**
- [x] Arrow 1: Floor → EdgeConnect (grey, "DIRECT" label)
- [x] Arrow 2: Floor → Acquisition (grey, "SENSOR" label)
- [x] Arrow 3: EdgeConnect ↕ Acquisition (grey, **bi-directional**, "COOPERATE" label, `orient="auto-start-reverse"` marker)
- [x] Arrow 4: EdgeConnect → EREMOS V2 (**teal, emphasized**)
- [x] Arrow 5: Acquisition → EREMOS V2 (**teal, emphasized**)
- [x] Arrow 6: EREMOS V2 → Your Enterprise (grey, output)

**Headline / caption / tagline (locked):**
- [x] Top headline "INDUSTRIAL INTELLIGENCE STACK" present (all variants except 3-box, which uses its own executive headline)
- [x] Subtitle "Field signal to enterprise insight — five capabilities, one ecosystem."
- [x] Tagline "DIRECT BROWNFIELD CONNECTIVITY · INTEGRATED ACQUISITION · ONE PLATFORM" on dark / light / slide / poster
- [x] Caption (verbatim per §4.2) on dark / light / poster; omitted on slide (deck-carried) and simple (own executive caption)

**Anti-pattern checks (§9):**
- [x] No AI / cyberpunk imagery
- [x] No customer or vendor logos
- [x] Single accent color on dark / light / slide / simple; poster uses the authorized exception (5-pillar restrained palette)
- [x] No emoji, no buzzwords, no decorative texture
- [x] No protocol logos (MQTT, OPC UA stay as text)
- [x] No "Industrial IoT" / "smart factory" / "Industry 4.0" anywhere

**Export QA:**
- [x] All 5 variants generated (dark / light / slide / simple / poster)
- [x] PNG fallbacks at 2x for all 5
- [x] Text remains text in SVG (no outlined paths)
- [x] Legible at projection scale (16:9 slide @ 3200×1800 readable from back of room)
- [x] Dark + light variants both pass body-text + arrow contrast on their respective backgrounds (per BRAND_TOKENS §6 contrast matrix — teal `#0080BC` substitution on light bg confirmed)

*rev3 implementation log added 2026-05-25 after the master + 4 variants + 5 PNGs were produced and verified. Phase C deliverable closed.*
