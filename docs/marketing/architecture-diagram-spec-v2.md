<!--
File:        docs/marketing/architecture-diagram-spec-v2.md
Purpose:     Design spec for the branded SVG architecture diagram that replaces
             the Mermaid placeholder in the datasheet and the pitch deck.
Audience:    Designer (in-house or contracted) producing the final SVG/PNG assets
             in the Elpis visual identity.
Format:      Markdown spec. Pair with the Mermaid block in
             docs/marketing/elpis-industrial-intelligence-platform-v3.md
             (the structural source of truth) and the v1 pitch deck outline
             (slide 8 context).
Version:     v2 (post-review)
Date:        2026-05-24

Changes from v1 (all additive operational constraints — no narrative changes):
  - Multi-site selection rule tightened to prioritize projection legibility
    over aesthetic preference (§4)
  - Minimum readable font size at 16:9 projection export specified (§6)
  - New §7: Accessibility & contrast — WCAG-comparable text and arrows,
    projector washout and PDF compression resilience (subsequent sections
    renumbered)
  - Stroke weight consistency rule across all variants added (§9)
  - Safe-area margin guidance (5% from outer edges) added (§10)
  - File version naming convention specified (§10)
  - Export QA checklist items added to sign-off (§14)
  - Source file preservation requirement added to delivery (§15)

ChatGPT v1 review explicitly recommended NOT iterating heavily after v2 —
hand v2 to the designer.

Structural source of truth: the Mermaid block in
docs/marketing/elpis-industrial-intelligence-platform-v3.md §"Architecture at
a glance." Treat that as the locked structural skeleton — names, boxes,
arrows, layer count. This spec adds the visual layer on top.

Assumption: Elpis does not yet have a formally published brand book accessible
to this session. Color and typography defaults below are professional industrial-
software conventions, NOT a brand mandate. If a brand book exists, the designer
MUST defer to it on palette, typography, and logo treatment, and the suggested
defaults here become advisory only.
-->

# Architecture Diagram — Design Spec v2

**Brief for the branded SVG that replaces the Mermaid placeholder.**

This diagram appears in three contexts: the datasheet (web one-pager + future print PDF), the pitch deck (slide 8 — "Architecture at a glance"), and likely a website hero or product-architecture page later. One asset, multiple uses — design for the most demanding context (print PDF) and derive the rest.

---

## 1. What the diagram must communicate

In 5 seconds: *"This is a layered industrial platform — edge collects, integration carries, intelligence aggregates, consumers consume."*

In 30 seconds: *"EdgeConnect runs at every plant and pulls from every controller. EREMOS V2 aggregates many plants. Standard MQTT and OPC UA make it interoperable with whatever SCADA / cloud / dashboard you already use."*

In 2 minutes (technical buyer): every box and arrow is meaningful; nothing is decorative. The protocol names matter. The directionality matters. The fact that the consumers tier has three different consumer types matters (operations, SCADA, cloud).

---

## 2. Structural content — locked

These elements are non-negotiable. Names, layers, and flow direction come from the v3 Mermaid block, which traces to CLAUDE.md and shared-knowledge contracts.

**Four columns or layers, left to right (or top to bottom for portrait variants):**

1. **Factory floor (per plant)**
   - Controllers cluster: *CNCs · Modbus PLCs · Meters*
   - Protocol labels: *FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP*
   - **EdgeConnect — Edge runtime** as the consolidating element below or beside the controllers

2. **Integration layer**
   - **MQTT broker** (drawn as a recognizable bus/broker shape, e.g. a stack or pipe)
   - **OPC UA Server** (drawn as a server endpoint)

3. **Intelligence layer**
   - **EREMOS V2** — single hero element
   - Sublabels: *Multi-tenant analytics · OEE · Alarms · Incidents · Reports*

4. **Consumers**
   - **Operations team** — *Dashboards · Alerts · Reports*
   - **SCADA / MES / HMI** — *OPC UA clients*
   - **Cloud platforms** — *AWS · Azure · custom*

**Required arrows (with implied labels):**

- Controllers → EdgeConnect (native protocols)
- EdgeConnect → MQTT broker (publish)
- EdgeConnect → OPC UA Server (serve)
- MQTT broker → EREMOS V2 (subscribe)
- EREMOS V2 → Operations team (visualize)
- OPC UA Server → SCADA/MES/HMI (standard OPC UA)
- MQTT broker → Cloud platforms (forward)

**Caption beneath the diagram (verbatim):**

*One EdgeConnect deploys at each plant. One EREMOS V2 tenant aggregates many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*

---

## 3. Visual hierarchy

Three tiers of prominence:

- **Primary (hero):** EdgeConnect and EREMOS V2. These are the two products being sold. They should be the largest containers, slightly elevated (subtle drop shadow or gradient), and visually anchored.
- **Secondary:** MQTT broker and OPC UA Server. These are the contracts/standards that make the platform interoperable. Medium prominence; clearly named.
- **Tertiary:** Controllers (left edge) and Consumers (right edge). These are context — what the platform connects from and to. Smaller, lighter weight, but legible.

---

## 4. Multi-site emphasis

The single most important *non-obvious* concept the diagram needs to convey: *one EdgeConnect per plant, one EREMOS V2 aggregating many plants.*

Designer options to render this:

- **Stacking visual cue:** show EdgeConnect with a subtle "stack of copies" effect (slight offset duplicates behind the main box), suggesting the same element repeats per site
- **Plant 1 / Plant 2 / Plant N labels:** show two or three EdgeConnect instances explicitly, with the "..." or "Plant N" cue indicating fleet scale
- **Dotted boundary around the Factory floor column** labeled *"per plant"*

**Prefer the option that remains understandable at pitch-deck projection scale before aesthetic preference.** Communication clarity outranks visual cleverness here.

---

## 5. Palette — suggested defaults (defer to brand book if it exists)

Dark premium industrial:

| Element | Suggested color |
|---|---|
| Background | Deep navy or near-black, e.g. `#1A1F26`, `#0F1419`, or `#1E2329` |
| Container fills (EdgeConnect, EREMOS V2 hero blocks) | Slightly elevated steel grey, e.g. `#2A2F36` with subtle gradient |
| Container fills (secondary tier — MQTT, OPC UA) | Mid steel grey, e.g. `#3A4049` |
| Tertiary boxes (controllers, consumers) | Lighter grey or transparent with thin border |
| Borders / dividers | Cool grey, low contrast, e.g. `#4A5560` |
| Body text | Off-white, e.g. `#E8ECF1` |
| Accent (used sparingly for arrows or highlighting) | One brand-accent color — suggest amber/orange if Elpis brand uses warm accent, or cyan/teal if cool. **Only one.** |

Light variant (for white-background contexts like a website hero): invert backgrounds to off-white, containers to soft cool grey, text to dark navy. Same accent color.

---

## 6. Typography

- Sans-serif, geometric or humanist (Inter, IBM Plex Sans, Manrope, or the Elpis brand face if defined)
- Three weights only: regular for labels, semibold for box titles, bold for the diagram caption
- Avoid all-caps except for very short single-word labels (`MQTT`, `OPC UA`)
- Generous letter spacing on small text to maintain legibility at slide-projection scale

**Minimum readable font size at 16:9 projection export:** no text smaller than the equivalent of **18–20 px at 3200×1800 render scale**. Designers often optimize for desktop zoom and forget that conference projectors degrade text 30–50% before it reaches the back-row viewer. Anything below this floor will be illegible at projection scale and must be either enlarged, simplified, or removed.

---

## 7. Accessibility and contrast

Treat the diagram as a public-facing technical asset that must remain legible across degraded display conditions:

- **Text contrast** against background should meet WCAG AA equivalents — minimum 4.5:1 for body labels, 3:1 for large box titles. Test against both the dark master and the light variant.
- **Arrow contrast** against background should meet at least 3:1, especially for accent-color arrows that may be a single hue against dark navy. Projector washout and compressed PDF exports both reduce effective contrast — design with margin to spare.
- **Color is never the only carrier of meaning.** Directionality is shown by arrow heads, not by color. Layer separation is shown by box position and grouping, not by color. A monochrome rendering of the diagram should still communicate the architecture correctly.
- **Validate against compression:** export a sample PNG at 50% quality and confirm text and arrows remain legible. PDFs distributed by email often lose the original resolution.

---

## 8. Iconography

Functional icons inside each container, not decorative:

| Element | Icon direction |
|---|---|
| Controllers cluster | Small line icons for a CNC controller, a PLC, an energy meter — three different functional silhouettes |
| EdgeConnect | An edge appliance or compact server icon — implies "runs on a small box at the plant" |
| MQTT broker | Pub/sub broadcast motif (a central node with radiating connections) |
| OPC UA Server | Industrial bus / endpoint icon — could lean on standard OPC UA visual cues if the brand allows |
| EREMOS V2 | Analytics dashboard motif — a stylized chart or layered panel |
| Operations team | Single person + screen, or just a screen with KPIs |
| SCADA / MES / HMI | Industrial workstation icon |
| Cloud platforms | Generic cloud silhouette (do not use AWS or Azure logos — too partner-specific) |

**Icon set:** prefer a single coherent set (Tabler, Lucide, Phosphor, or custom). Mixing icon styles breaks the premium feel.

---

## 9. Flow arrows

- Directionality is mandatory — every arrow points in the data flow direction
- Line weight: medium, not hairline (visible at slide scale)
- Color: cool grey for non-emphasized arrows; the single accent color for emphasized arrows (use sparingly — at most two arrows highlighted)
- Optional: small protocol labels on the arrows (e.g. *"FOCAS2 · Modbus TCP · MTConnect"* alongside the Controllers → EdgeConnect arrow). Keep label text minimal — under 30 characters per arrow.
- Avoid curved spaghetti routing — orthogonal arrows read cleaner in technical diagrams

**Stroke weight consistency:** arrow stroke widths and container border weights must remain visually consistent across all variants (master, slide, simplified, light/dark). Inconsistent stroke weights between variants make the architecture feel improvised. Pick a stroke-weight scale once (e.g. 1.5 px for borders, 2.5 px for arrows at the master scale) and reuse it everywhere.

---

## 10. Output variants and dimensions

Deliver as SVG (master) plus PNG fallbacks at the resolutions below:

| Use context | Aspect | Resolution | Notes |
|---|---|---|---|
| Datasheet — web | Landscape, ~3:2 | 2400×1600 @ 2x retina | The default — fits inside the markdown one-pager |
| Pitch deck — slide 8 | 16:9 | 3200×1800 | Larger margins for safe area; caption rendered as slide text, not embedded in the SVG |
| Print PDF | Landscape, ~3:2 | Vector SVG; PDF embedding handles scale | Ensure all text remains text, not outlines, for accessibility |
| Website hero or product page | TBD | Discuss with web designer once site IA is locked | Probably a simplified 3-box variant |

**Safe-area margin:** maintain at least **5% safe-area margin** from the outer edges of every export. No critical labels, boxes, or arrows within that margin. Slide projection cropping and PDF print trimming both eat into edges — content placed too close to a boundary disappears in distribution.

**Simplified 3-box variant (separate deliverable):** for executive contexts where the full diagram is too dense — three boxes only: *Factory floor → Elpis Platform → Operations / Cloud*. Single arrow each. Same palette, same typography. The full diagram is the substance; the 3-box version is the elevator pitch.

**File version naming convention** — for long-term asset governance:

```
architecture-diagram-v1-dark.svg
architecture-diagram-v1-light.svg
architecture-diagram-v1-slide.svg
architecture-diagram-v1-simple.svg
architecture-diagram-v1-dark@2x.png
architecture-diagram-v1-light@2x.png
```

Each variant gets an explicit suffix. Future revisions bump the version (`-v2-…`); never overwrite a published variant. This protects past slide decks and PDFs that reference specific filenames.

---

## 11. Animation — web only

If the diagram is used on the website:

- Subtle flow animation along the arrows (slow particle drift, 4-6 second loop, low-opacity) — optional
- Hover state on the EdgeConnect and EREMOS V2 boxes: subtle glow, no movement
- No spinning. No fades that delay the presenter. No parallax. No "loading" animation.

In the pitch deck and print PDF: **no animation.** Static only.

---

## 12. Anti-patterns — do not do this

- No abstract "AI brain" imagery anywhere in the diagram
- No spinning gears or "Industry 4.0" visual clichés
- No stock-photo cutouts (handshakes, smiling operators)
- No gradient overload — one subtle gradient on hero boxes max
- No more than one accent color
- No AWS / Azure / GCP logos — the diagram is vendor-neutral on cloud
- No emoji
- No "smart factory" buzzword captions
- No competitor names (Kepware, Ignition, etc.) on the diagram, even by implication

---

## 13. Reference: the structural source

The locked structural skeleton is the Mermaid block in:

`docs/marketing/elpis-industrial-intelligence-platform-v3.md` § *"Architecture at a glance"*

Open that file, render the Mermaid block, and use it as the literal structural reference. The designer is free to interpret VISUAL choices but must preserve every box, every arrow, every label name.

---

## 14. Sign-off checklist (before declaring done)

**Content fidelity:**

- [ ] Every box from the Mermaid source is present and named identically
- [ ] Every arrow from the Mermaid source is present with correct direction
- [ ] Caption text is reproduced verbatim
- [ ] Hero boxes (EdgeConnect, EREMOS V2) are visually prominent above secondary/tertiary tiers
- [ ] Multi-site cue is visible (stacking, plant labels, or boundary)
- [ ] No anti-patterns from §12 present

**Export QA:**

- [ ] SVG master renders correctly in Chrome, Firefox, and Safari
- [ ] Embedded font references resolve correctly across browsers (or fonts are properly outlined where needed)
- [ ] PNG fallbacks generated at the resolutions in §10
- [ ] PNG export at 50% scale does not blur text or arrows
- [ ] Diagram is legible at pitch-deck-projection scale (test by exporting at 3200×1800 and viewing at 50% on a typical monitor)
- [ ] Both dark and light variants validated for contrast (per §7)
- [ ] SVG master file size is reasonable (under 500 KB)
- [ ] No content sits within the 5% safe-area margin (per §10)
- [ ] Simplified 3-box variant produced as a separate file

---

## 15. Delivery

Place final assets in `docs/marketing/assets/` (create the directory):

- `architecture-diagram-v1-dark.svg` — full dark version (master)
- `architecture-diagram-v1-light.svg` — light variant
- `architecture-diagram-v1-slide.svg` — pitch deck 16:9 variant
- `architecture-diagram-v1-simple.svg` — 3-box executive variant
- `architecture-diagram-v1-dark@2x.png` — datasheet web fallback
- `architecture-diagram-v1-light@2x.png` — light-context web fallback

Update the datasheet (`elpis-industrial-intelligence-platform-v3.md`) and the pitch deck outline (`pitch-deck-outline-v1.md`) to reference the SVG instead of the Mermaid block once the final asset is approved.

**Source file preservation:** the designer's editable source files (`.fig`, `.ai`, `.sketch`, or whichever native format) must be retained in version-controlled or backed-up storage — not only the exported SVG/PNG outputs. Brand refreshes, future variants, and minor edits all require the editable source. Losing it forces a rebuild from scratch, which is expensive and risks drift from the original. Recommended: a dedicated `marketing-design-source/` folder under Elpis's primary asset repository or cloud drive, with semantic file names matching the export naming convention above.

---

*Architecture Diagram Spec — v2, 2026-05-24. Pair with datasheet v3 Mermaid for structural truth. Per ChatGPT v1 review, no further iteration planned — hand this version to the designer.*
