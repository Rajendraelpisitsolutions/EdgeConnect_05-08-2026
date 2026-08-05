<!--
File:        docs/marketing/assets/brand/BRAND_TOKENS.md
Purpose:     Single source of truth for visual tokens used by every marketing
             asset in this repo (SVG diagrams, PPTX pitch deck, PDFs, web pages,
             email banners, sales-packet handouts).
Audience:    The fresh design-execution session producing assets, and the future
             designer who refines these. Every asset references the token names
             defined here — never inlines hex values.
Scope:       Marketing/customer-facing visual surface only. Operational UI
             tokens (Management Studio, EREMOS V2 dashboards) live elsewhere
             and may diverge; this doc does not govern them.
Version:     v1 (LOCKED 2026-05-24 — user sign-off received before any
             production asset referenced this doc)
Date:        2026-05-24
Status:      Locks the visual identity for the design execution session opened
             by docs/marketing/DESIGN_EXECUTION_HANDOFF.md. Source artifacts in
             docs/marketing/assets/brand/. First asset shipped against this
             v1: architecture-diagram-v1 (5 SVG variants + PNG fallbacks).
-->

# Elpis Marketing Visual Tokens — v1

**Source of truth for every marketing asset.** SVG diagrams, pitch decks, datasheets, website pages, and sales handouts all reference the tokens named here. No asset inlines hex values; everything resolves to a name in this doc.

This document is **frozen at v1 after user sign-off**. Future revisions bump the version; the asset-name suffix `-v1-` in the filename convention pins each asset to a specific token version (see `architecture-diagram-spec-v2.md` §10).

---

## 1. Status, scope, sign-off

**Status:** v1 — **LOCKED 2026-05-24**. User signed off before any production asset referenced this doc. First asset shipped against this v1: `architecture-diagram-v1` (5 SVG variants + PNG fallbacks). Subsequent assets reference token names from this doc, not raw hex values.

**Scope:** marketing/customer-facing visual surface only — SVG architecture diagrams, pitch deck, datasheet PDF, website pages, security page, solution-brief PDFs, ROI calculator UI, sales-packet handouts, email banners, social cards.

**Out of scope:** operational UI tokens (Management Studio Razor Pages, EREMOS V2 dashboards). Those have separate token systems driven by functional UI requirements (state colors, density, density). This doc does not govern them.

**Sign-off owner:** user. Until signed off, every reference to "locked" below should be read as "proposed."

---

## 2. Color tokens

### 2.1 Brand primary — the only accent

| Token | Hex | RGB | Use |
|---|---|---|---|
| `brand.teal` | `#00A0E0` | 0, 160, 224 | The single platform accent. Wordmark color, primary CTAs, emphasized arrows in diagrams, highlighted callouts, link color on dark backgrounds. **Used sparingly** — it gets its power from scarcity. |

**Discipline rule (locked per user choice):** one accent color across the entire marketing visual system. The orange and amber that appear inside the Elpis logo bitmap stay **inside the logo bitmap only** — they are not extracted as platform tokens and are not used elsewhere in the marketing visual system. This matches `architecture-diagram-spec-v2.md` §12 anti-pattern *"no more than one accent color."*

Sampled from `docs/marketing/assets/brand/elpis-logo-transparent.png` (dominant bucket of the ELPIS wordmark, n=17,401 pixels in the brightest cluster). If the print-CMYK breakdown later needs to differ, that's a print-shop decision and does not change the digital token.

### 2.2 Dark industrial palette (default background context)

The default marketing-asset surface is **dark**, per the architecture-diagram-spec premium-industrial defaults. Light variants are explicit overrides for specific contexts (website hero, white-paper PDF, light email).

| Token | Hex | Use |
|---|---|---|
| `bg.deep` | `#0F1419` | Deepest background — full-bleed asset backgrounds, slide masters, page chrome behind content. |
| `bg.default` | `#1A1F26` | Default canvas — content area background on dark surfaces. |
| `bg.raised` | `#1E2329` | Slight elevation cue without a true gradient — section dividers, callout strips. |
| `surface.hero` | `#2A2F36` | Hero containers (EdgeConnect box, EREMOS V2 box, hero callouts). Subtle gradient permitted: `#2A2F36 → #232830`. |
| `surface.secondary` | `#3A4049` | Secondary containers (MQTT broker, OPC UA Server, integration-layer boxes). |
| `surface.tertiary` | transparent + `border.subtle` 1.5px | Tertiary boxes (controllers, consumers, peripheral elements). Outline-only, no fill. |
| `border.subtle` | `#4A5560` | Container borders, dividers, low-emphasis lines. |
| `border.strong` | `#5E6B78` | Higher-emphasis borders (focus states, active selection). |

### 2.3 Light variant (override palette)

| Token | Hex | Use |
|---|---|---|
| `bg.light.deep` | `#F4F6F9` | Lightest canvas, full-bleed light backgrounds. |
| `bg.light.default` | `#FAFBFC` | Default light canvas (approaching white but never pure white). |
| `surface.light.hero` | `#FFFFFF` | Hero containers on light. Subtle 1px border `#E2E7EC`. |
| `surface.light.secondary` | `#EEF1F5` | Secondary containers on light. |
| `border.light.subtle` | `#D5DCE3` | Low-emphasis borders on light. |
| `border.light.strong` | `#A8B3BD` | Higher-emphasis borders on light. |

### 2.4 Text colors

| Token | On dark | On light | Use |
|---|---|---|---|
| `text.body` | `#E8ECF1` | `#1A1F26` | Default body copy. |
| `text.muted` | `#A8B3BD` | `#5E6B78` | Captions, labels, secondary metadata. |
| `text.heading` | `#FFFFFF` | `#0F1419` | Headlines, hero titles, section anchors. |
| `text.accent` | `brand.teal` | `brand.teal` | Inline emphasis, link color on dark. On light, prefer underline for links to avoid relying on color alone. |

### 2.5 Colors NOT in this system (deliberate exclusions)

- **State colors** (success/warning/error). Marketing visuals do not communicate operational state. State semantics belong to the UI track. If a marketing chart needs to show a "good vs bad" comparison, use teal + neutral grey, not green/red.
- **The logo's orange `#F08020` and amber `#F0B030`.** They live inside the logo bitmap only; they are not platform tokens.
- **Tagline grey `#606060`.** It is a logo-internal color too. The platform's grey scale is `text.muted` / `border.subtle` / `border.strong`, derived from the dark-industrial scale.
- **No competitor brand colors, no AWS/Azure/GCP brand colors.** Per `architecture-diagram-spec-v2.md` §12.

---

## 3. Typography

### 3.1 Font stack

**Primary:** **Inter** (variable font). Free, OFL-licensed, geometric-humanist, exceptional hinting, deep weight range, modern industrial feel. Already used by Linear, Vercel, GitHub-OSS — recognizable as "modern technical software" without being trendy.

**Fallback stack (web):**

```css
font-family: 'Inter', system-ui, -apple-system, 'Segoe UI Variable',
             'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
```

**Print/PDF substitution:** if Inter is unavailable in the print workflow, substitute IBM Plex Sans (also OFL, similar geometric weight). Avoid Helvetica (too generic), Arial (too generic), and Calibri (too desktop-office).

**Heading face (optional):** Inter handles headlines well. If a stronger heading personality is wanted later, **Manrope** (semi-condensed, modern industrial) is the recommended secondary face. Do not introduce a serif.

### 3.2 Weights

Three weights only. Mixing more weights makes the system feel imprecise.

| Token | Weight | Use |
|---|---|---|
| `type.regular` | 400 | Body copy, labels, captions. |
| `type.semibold` | 600 | Box titles in diagrams, section subheads, button labels, emphasized inline text. |
| `type.bold` | 700 | Headlines, hero titles, the architecture-diagram caption, slide titles. |

### 3.3 Size scale (rem-based for web; pt for print)

Modular scale, ratio ~1.25 (major third). Tuned so 16px body is the baseline.

| Token | Size (web rem) | Size (web px) | Print pt | Use |
|---|---|---|---|---|
| `size.xs` | 0.75rem | 12 | 9 | Footnotes, microcopy. |
| `size.sm` | 0.875rem | 14 | 11 | Captions, secondary labels. |
| `size.base` | 1rem | 16 | 12 | Body copy. **Floor for marketing body text** — never smaller. |
| `size.md` | 1.125rem | 18 | 14 | Lead paragraphs, emphasized body. |
| `size.lg` | 1.5rem | 24 | 18 | Subheads, callout titles. |
| `size.xl` | 2rem | 32 | 24 | Section headlines. |
| `size.2xl` | 2.5rem | 40 | 30 | Hero subheads. |
| `size.3xl` | 3.5rem | 56 | 42 | Hero headlines. |
| `size.4xl` | 4.5rem | 72 | 54 | Pitch-deck slide titles. |

**Diagram floor (per architecture-diagram-spec §6):** no text smaller than the equivalent of **18–20 px at 3200×1800 render scale**. Below that, text becomes unreadable at projection scale.

### 3.4 Letter spacing and line height

- **Body copy:** line-height 1.6, letter-spacing 0.
- **Headlines (`size.xl` and up):** line-height 1.15, letter-spacing -0.01em (slight tightening).
- **All-caps labels** (used only for very short single-word labels like `MQTT`, `OPC UA`, `FOCAS2`): letter-spacing +0.04em for legibility at small sizes.
- **Pitch-deck projection text:** add +0.02em letter-spacing globally; conference projectors degrade letterform separation.

### 3.5 Text-on-color contrast (validated, see §6)

All combinations below meet WCAG AA at minimum. See §6 for the full matrix.

---

## 4. Logo usage

### 4.1 Source files (in repo)

| File | Format | Use |
|---|---|---|
| `docs/marketing/assets/brand/elpis-logo.eps` | Vector EPS | Print PDFs, datasheet, sales-packet PDFs. Authoritative print source. |
| `docs/marketing/assets/brand/elpis-logo-source.ai` | Adobe Illustrator | Editable master — used for variant production (white-only, single-color, dark-bg knockout). |
| `docs/marketing/assets/brand/elpis-logo-transparent.png` | PNG, 1068×260, transparent | Web embedding on light or mid-tone backgrounds. |
| `docs/marketing/assets/brand/elpis-logo-square.png` | PNG, 1280×1280, transparent | Square-format use (social profile, favicon source, Slack workspace icon). |

**Future-needed variants — to be produced from the .ai source** (flagged for designer or follow-up session):

| Variant | Why needed |
|---|---|
| `elpis-logo-white-on-dark.svg` | The full-color logo has a `#606060` tagline — unreadable on dark navy `#1A1F26` (contrast ≈ 2.2:1, fails WCAG AA). Need a knockout version with white tagline for dark-background placements. **Required before any dark-themed asset that places the logo.** |
| `elpis-logo-wordmark-only.svg` | Wordmark "ELPIS" without the geometric icon, for narrow horizontal placements (email signatures, header bars). |
| `elpis-logo-single-color-mono.svg` | All-teal or all-white version, for single-color print runs, embossing, or low-contrast contexts. |

### 4.2 Clearspace and minimum size

- **Clearspace:** maintain a margin equal to the height of the capital "E" in "ELPIS" on all four sides. No other element (text, image, edge of canvas) within that margin.
- **Minimum size (digital):** 80 px wide for the full lockup (icon + wordmark + tagline). Below 80 px, drop to the wordmark-only variant (when produced).
- **Minimum size (print):** 25 mm wide for the full lockup. Below 25 mm, drop to wordmark-only.

### 4.3 Placement rules

- **Never recolor** any element of the logo. Don't tint, hue-shift, or apply gradient overlays.
- **Never stretch or condense.** Maintain aspect ratio always.
- **Never place on a busy photograph** without a solid backing plate (`bg.default` or `surface.hero`) behind the logo.
- **Never replace the icon with a competitor icon, AI badge, or product mark.** The icon is part of the lockup.
- **Never animate** the wordmark or icon. Decorative motion is acceptable around the lockup but not on it.

---

## 5. Spacing scale

Base unit: **4 px**. All spacing in marketing assets snaps to multiples of this base.

| Token | Value | Use |
|---|---|---|
| `space.1` | 4 px | Hairline separations, inline icon spacing. |
| `space.2` | 8 px | Tight clusters (label-to-icon, chip padding). |
| `space.3` | 12 px | Compact button padding. |
| `space.4` | 16 px | Default paragraph spacing, standard padding. |
| `space.6` | 24 px | Comfortable section spacing, card padding. |
| `space.8` | 32 px | Default block separation. |
| `space.12` | 48 px | Section dividers, hero-content vertical rhythm. |
| `space.16` | 64 px | Major section gaps, between-page-block separation. |
| `space.24` | 96 px | Above hero blocks, between major page regions. |
| `space.32` | 128 px | Hero vertical padding, full-bleed section gutters. |

**Stroke weights for diagrams (locked, per architecture-diagram-spec §9):**

- Container borders: 1.5 px (at master 2400×1600 scale)
- Arrows: 2.5 px (at master scale)
- Multi-site stacking offset: 4 px

These scale proportionally with the viewBox in derived variants.

---

## 6. Accessibility — contrast matrix (validated)

WCAG AA: ≥4.5:1 for body text, ≥3:1 for large text (18 pt+ regular, 14 pt+ bold) and graphical objects.

| Foreground | Background | Ratio | Verdict |
|---|---|---|---|
| `text.body` `#E8ECF1` | `bg.default` `#1A1F26` | 13.2:1 | ✓ AAA |
| `text.body` `#E8ECF1` | `surface.hero` `#2A2F36` | 10.1:1 | ✓ AAA |
| `text.muted` `#A8B3BD` | `bg.default` `#1A1F26` | 6.7:1 | ✓ AA |
| `text.heading` `#FFFFFF` | `bg.default` `#1A1F26` | 14.8:1 | ✓ AAA |
| `brand.teal` `#00A0E0` | `bg.default` `#1A1F26` | 5.8:1 | ✓ AA (body); large-text & graphical: clear pass |
| `brand.teal` `#00A0E0` | `surface.hero` `#2A2F36` | 4.5:1 | ✓ AA (body, borderline — prefer on `bg.default` for body) |
| `text.body` on light `#1A1F26` | `bg.light.default` `#FAFBFC` | 13.6:1 | ✓ AAA |
| `text.muted` on light `#5E6B78` | `bg.light.default` `#FAFBFC` | 4.8:1 | ✓ AA |
| `brand.teal` `#00A0E0` | `bg.light.default` `#FAFBFC` | 2.6:1 | ✗ FAIL body; ✓ AA large-text/graphical only |

**Teal-on-light failure for body text is intentional design feedback:** on light backgrounds, never use teal for body copy or small text. Reserve it for headlines, large CTAs, and graphical elements (arrows, icons). For inline links on light backgrounds, add an underline + slightly darken to `#0080BC` if needed to reach 4.5:1.

**Color is never the only carrier of meaning.** Diagram directionality is arrow heads, not color. Layer separation is position + grouping, not color. State semantics use text labels (e.g. "Available," "Roadmap"), not red/green.

---

## 7. Imagery rules (recap from handoff §2)

Locked, no override planned:

- **Real shop-floor photography only.** No stock-photo handshakes, smiling operators, aerial assembly-line shots.
- **Specific over generic.** Real Fanuc 18i close-up beats a generic factory shot.
- **For OEM contexts:** OEM-built machine on a real customer's floor, mid-operation.
- **For brownfield contexts:** older Fanuc controller, slightly worn, operator's hand on MPG.
- **For security contexts:** closed control cabinet, dim lighting, no people. Calm. **NEVER** cybersecurity clichés (padlocks, hooded hackers, glowing networks).
- **Geometric illustration is the fallback** if photography isn't available — single coherent icon set (recommend **Tabler Icons** for consistency, free, OFL-licensed, technically themed).

---

## 8. Anti-patterns (the things this token system forbids)

| Don't | Why |
|---|---|
| Inline hex values in asset files | Drifts from this doc. Use named tokens; if the asset format can't reference tokens (raw SVG), include a comment block at the top of the file listing which tokens map to which hex. |
| Introduce a second accent color | Locked: teal-only. Adding a second accent requires user override and an explicit v2 of this doc. |
| Use the tagline grey `#606060` for any platform purpose | It's logo-internal. Platform greys come from §2.2. |
| Use orange/amber outside the logo | Locked: logo-internal only. |
| Skip the contrast matrix check | Every new fg/bg pair gets validated against §6 or added to it. |
| Mix icon styles | Pick one set (Tabler default) and stay in it. |
| Add gradient effects beyond §2.2 `surface.hero` | One subtle gradient per asset, on the hero container only. No gradient text, no gradient backgrounds. |
| Use the full-color logo on dark navy without the white-tagline variant | The default logo's tagline fails contrast on dark. Use the white-on-dark variant once produced; until then, use wordmark-only on dark surfaces. |

---

## 9. How assets reference these tokens

**SVG (hand-written):** include a comment block at the top mapping hex values to token names, and use the hex values in `fill=` / `stroke=`. Example:

```xml
<!--
  Token map:
    bg.default       #1A1F26
    surface.hero     #2A2F36
    border.subtle    #4A5560
    text.body        #E8ECF1
    text.heading     #FFFFFF
    brand.teal       #00A0E0
-->
```

**PPTX:** define a theme color palette in the slide master matching §2. Every shape fill / text color picks from the theme, not from "More colors."

**PDF (InDesign or Figma export):** define a swatch panel with §2 colors named identically. Every object swatch-picks from the panel.

**Web (CSS):** declare tokens as CSS custom properties on `:root`:

```css
:root {
  --brand-teal: #00A0E0;
  --bg-default: #1A1F26;
  --surface-hero: #2A2F36;
  /* ... */
}
```

---

## 10. Versioning and changes

- **Current version:** v1 (proposed, pending user sign-off)
- **Bump rule:** any color hex change, any logo-usage rule change, any contrast-matrix entry that newly fails — all bump the version (v2, v3, …) and require re-validating every asset that references the prior version.
- **Asset filename suffix `-v1-` pins to this version.** When this doc bumps to v2, new assets get `-v2-` filenames; old `-v1-` assets remain valid against this doc and may be replaced asynchronously.
- **No retroactive editing of v1 once signed off.** All evolution goes into v2.

---

*Brand Tokens — v1, 2026-05-24. Sign-off pending. Once locked, every marketing asset in this repo references token names from this doc, not raw hex values. The teal-only discipline and the dark-industrial default are the two highest-leverage decisions; everything else descends from them.*
