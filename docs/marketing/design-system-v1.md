<!--
File:        docs/marketing/design-system-v1.md
Purpose:     Component library blueprint for the Elpis DXP. Names, purpose,
             tokens, motion, sizing, anti-patterns for every component used
             in Phase 1 (homepage) and named for Phases 2-4 extension.
Audience:    Internal — Claude (v2 author), user + ChatGPT (reviewers),
             engineering team (Angular implementers).
Format:      Markdown component spec. Each component has: purpose, props
             surface, token references, motion, anti-patterns, growth path.
Companion:   homepage-spec-v1.md (which sections use which components)
             assets/brand/brand-tokens.css (the resolved CSS variables)
             BRAND_TOKENS.md (the source of truth for token values)
Version:     v1
Date:        2026-05-25
Status:      DRAFT — pending user + ChatGPT review pass.
-->

# Elpis DXP — Design System v1

**Component library blueprint. Phase 1 deliverable for Homepage v1; Phases 2-4 extend additively.**

This is not the Angular code. It is the *design contract* — what each component is, what it carries, what it must never become. The Angular team implements against this contract.

---

## 0. Foundational principles

Five rules every component obeys:

1. **Token discipline.** No hex values in component source. Every color resolves to a CSS variable from `assets/brand/brand-tokens.css`.
2. **One accent.** Brand teal is the only accent. The five-pillar tonal palette is the authorized exception inside `CapabilityCard` accent lines and `ProofBand` pillar markers — used very restrained, never decoratively.
3. **Premium industrial, never SaaS.** No drop shadows, no card-elevation hovers, no gradient text, no glass effects. Solid surfaces. Restrained motion. Generous spacing.
4. **Material primitives, never Material aesthetics.** Use Angular CDK for overlays, a11y, focus management. Never use Material's visual styles, button shapes, ripple effects, color schemes.
5. **Composable, not prescriptive.** Components compose from primitives (`SectionShell` wraps everything; `Button` is the only button). Prefer composition over options proliferation.

---

## 1. `Button`

**The only button primitive. Every CTA, every link-styled-as-button, every action uses this.**

**Variants:**

| Variant | Use | Background | Border | Text |
|---|---|---|---|---|
| `primary` | The page's primary CTA | `brand.teal` | none | `text.heading` (white) |
| `secondary` | Outlined alternative | transparent | `border.strong` 1.5px | `text.body` |
| `ghost` | Tertiary, nav use | transparent | none | `text.body` |
| `primary-light` | Primary on light backgrounds | `brand.teal-light` `#0080BC` | none | white |
| `secondary-light` | Outlined on light backgrounds | transparent | `border.light.strong` 1.5px | `text.body-light` |

**Sizing:**

| Size | Padding (Y × X) | Font | Use |
|---|---|---|---|
| `lg` | 16 × 32 | size.md / semibold | Hero CTAs, CTA section |
| `md` | 12 × 24 | size.base / semibold | Section-level CTAs |
| `sm` | 8 × 16 | size.sm / semibold | Inline, secondary actions |

**Behavior:**
- Hover: 120ms brightness shift on background (`primary`), or accent-color fill (`secondary`, `ghost`)
- Focus: 2px `brand.teal` outline at 2px offset (keyboard a11y, always visible)
- Disabled: 50% opacity, no cursor change beyond `not-allowed`
- Arrow `→` is rendered inside the button when the destination is a flow step; never decorative

**Anti-patterns:**
- ❌ No drop-shadow on hover (SaaS feel)
- ❌ No rounded-pill shape (uses 4px corner radius — angular industrial)
- ❌ No gradient backgrounds
- ❌ No ripple effect (Material aesthetic)
- ❌ No icon-only buttons in marketing surfaces (icon-only acceptable in product UI, not here)

**Growth path:** Phase 4 adds a `loading` state for calculator/demo interactions.

---

## 2. `SectionShell`

**The outer wrapper for every page section. Establishes vertical rhythm, container width, background mode.**

**Props surface:**

| Prop | Values | Effect |
|---|---|---|
| `mode` | `dark` · `light` · `light-tinted` · `dark-deep` | Sets background and text color tokens for the whole subtree |
| `padding` | `default` · `tight` · `loose` · `flush` | Vertical padding scale |
| `width` | `contained` · `narrow` · `full-bleed` | Container max-width |

**Modes:**

| Mode | Background | Default text |
|---|---|---|
| `dark-deep` | `bg.deep` `#0F1419` | `text.heading` / `text.body` |
| `dark` | `bg.default` `#1A1F26` | `text.heading` / `text.body` |
| `light` | `bg.light.default` `#FAFBFC` | `text.heading-light` / `text.body-light` |
| `light-tinted` | `bg.light.deep` `#F4F6F9` | `text.heading-light` / `text.body-light` |

**Vertical rhythm (padding values):**

| Padding | Mobile | Tablet | Desktop |
|---|---|---|---|
| `tight` | 48 | 64 | 80 |
| `default` | 64 | 96 | 128 |
| `loose` | 96 | 128 | 192 |

**Container widths:**

| Width | Max |
|---|---|
| `contained` | 1280px |
| `narrow` | 960px |
| `full-bleed` | 100vw |

**Why this matters:** every section wraps in `SectionShell`. Consistency across the page (and later, across all DXP pages) flows from this one component. Spacing never drifts.

---

## 3. `CapabilityCard`

**The card that represents one capability pillar (5 used in capability strip), one hardware product (5 used in hardware section), or one audience (3 used in audience section). Same shape, three uses.**

**Props surface:**

| Prop | Values | Effect |
|---|---|---|
| `mode` | `dark` · `light` | Token mode |
| `accent` | `pillar-1` … `pillar-5` · `teal` · `none` | Top or left accent line color |
| `eyebrow` | string | Small-caps label above title |
| `title` | string | Card title |
| `description` | string (≤ 200 chars) | One-line body |
| `footer` | string (optional) | Optional sub-line (e.g., "EdgeConnect · Edge Gateway") |
| `href` | string (optional) | If set, entire card is a link (Phase 2+) |

**Visual structure:**

```
┌─────────────────────────────┐
│ ╶ accent line (teal/pillar) │
│                             │
│ EYEBROW LABEL               │  ← small caps, letter-spaced, text.muted
│                             │
│ Card title                  │  ← size.lg, semibold
│                             │
│ One-line description text   │  ← size.base, regular, text.body
│ spans up to ~3 lines.       │
│                             │
│ Footer · subline            │  ← size.sm, text.muted (optional)
└─────────────────────────────┘
```

**Tokens:**

| Element | Dark | Light |
|---|---|---|
| Card background | `bg.default` (subtle) or transparent | `surface.light.hero` `#FFFFFF` |
| Border | `border.subtle` | `border.light.subtle` |
| Title | `text.heading` | `text.heading-light` |
| Body | `text.body` | `text.body-light` |
| Eyebrow | `text.muted` | `text.muted-light` |
| Accent | `brand.teal` or pillar tone | `brand.teal-light` `#0080BC` or pillar tone |

**Sizing:** internal padding `space.6` (24px) tablet, `space.8` (32px) desktop. Min-height equalized across cards in a row (CSS grid).

**Motion:**
- Accent line slide-in from left, 180ms ease-out, on hover
- Title color lift toward `brand.teal` (200ms), only when `href` is set
- No card elevation, no shadow change, no scale transform

**Anti-patterns:**
- ❌ No card-elevation drop-shadow on hover (SaaS feel)
- ❌ No icon-on-card decoration in Phase 1 (Phase 3 introduces optional icon slot for hardware cards)
- ❌ No "Learn more →" inside the card when the card itself is a link

**Growth path:** Phase 3 adds an optional `icon` slot for hardware product cards (with the Elpis hardware product imagery). Phase 4 adds an `metric` overlay variant for calculator-result cards.

---

## 4. `MetricStrip`

**Three-up large-numeral metric display. Used inside EdgeConnect and EREMOS V2 sections; reused in Phase 3 customer stories and Phase 4 calculators.**

**Props surface:**

| Prop | Effect |
|---|---|
| `mode` | dark / light token mode |
| `metrics` | array of `{ value, label, caption }` — typically 3 items, max 4 |
| `align` | `left` · `center` · `right` |

**Visual structure:**

```
6+              2                  3
PROTOCOLS       DELIVERY MODES     DIAGNOSTICS LAYERS
FOCAS2 ·        AtMostOnce ·       Source ·
MT-LINKi ·      AtLeastOnce        Pipeline · Sink
MTConnect …
```

- `value` — size.3xl (3.5rem / 56px), bold, `text.heading-light` (or `text.heading` on dark)
- `label` — size.sm, semibold, letter-spaced 0.18em, small-caps, `text.muted`
- `caption` — size.sm, regular, `text.body-light` (or `text.body`)

**Tokens:** value color stays neutral by default; an optional `accent` prop tints the value with `brand.teal` for the single most-important metric in the strip.

**Anti-patterns:**
- ❌ No animated count-up effect (premium-industrial feel = stillness)
- ❌ No "+" or "%" decoration unless the value is genuinely additive/percentage
- ❌ No more than 4 metrics in a single strip (signal dilution)

---

## 5. `ArchitecturePanel`

**The container for the embedded architecture diagram. Section 3 of the homepage. Reused on `/architecture` in Phase 2.**

**Props surface:**

| Prop | Effect |
|---|---|
| `variant` | `dark` · `light` (selects which SVG to embed) |
| `caption` | string (rendered below the diagram) |
| `annotations` | optional array (Phase 2 — hover annotations on specific diagram regions) |
| `cta` | optional `{ label, href }` for "See the architecture" link |

**Visual structure:**

```
┌─────────────────────────────────────────┐
│        [embedded architecture-diagram   │
│         -v2-{light|dark}.svg here]      │
│                                         │
│        Caption line 1                   │
│        Caption line 2                   │
└─────────────────────────────────────────┘
```

**Embed mechanics:**
- The SVG is referenced via `<img>` tag in static reference (Phase 1.5) and via Angular SVG-component inlining in production (Phase 1 Angular)
- Caption is rendered as text — never baked into the SVG. The locked caption from `architecture-diagram-spec-v3.md §4.2` is the default.
- Annotations (Phase 2) are absolutely-positioned tooltips anchored to coordinate hotspots in the SVG viewBox

**Anti-patterns:**
- ❌ No diagram animation in Phase 1 (Phase 2 introduces hover annotations on `/architecture`)
- ❌ No "click to zoom" modal — the diagram is legible at one zoom
- ❌ No alternate decorative SVG variants in homepage hero (use cropped master diagram)

---

## 6. `ProofBand`

**Narrow dark band for credibility punctuation. Section 7 of the homepage. Reused in Phase 3 customer stories.**

**Props surface:**

| Prop | Effect |
|---|---|
| `eyebrow` | small-caps label |
| `headline` | size.xl section title |
| `anchors` | array of `{ title, body, pillar }` — typically 3 items |

**Visual structure:**

```
┌────────────────────────────────────────────────────────────┐
│ EYEBROW                                                    │
│ Section headline.                                          │
│                                                            │
│ Anchor 1               Anchor 2          Anchor 3          │
│ Title                  Title             Title             │
│ One-line body that     One-line body     One-line body     │
│ spans 2-3 lines.       at the same line  at the same line  │
└────────────────────────────────────────────────────────────┘
```

**Tokens:** dark mode only (`bg.deep`). Brand teal eyebrow. Pillar tonal accents per anchor (very restrained).

**Anti-patterns:**
- ❌ No customer logos
- ❌ No fabricated quotes — the anchors are paraphrased proof statements, not invented testimonials
- ❌ No flags, no national symbols, no industry stock icons

---

## 7. `QuoteBlock`

**Reserved for Phase 3 customer stories. Not used in Homepage v1.**

Defined here so the engineering team knows it exists and the design system reserves the slot.

**Props surface (Phase 3):**

| Prop | Effect |
|---|---|
| `quote` | the quote text |
| `attribution` | `{ name, role, anonymizedOrAnonymous }` |
| `image` | optional headshot or anonymized silhouette |
| `pillarContext` | which capability pillar the quote anchors |

**Anti-patterns (when used in Phase 3):**
- ❌ No fabricated quotes
- ❌ No "Trusted by enterprises like yours" without attribution
- ❌ No giant cursive quote-mark decoration (SaaS feel)

---

## 8. `CTASection`

**Full-width dark CTA region. Section 9 of the homepage. Reused at the bottom of every page Phase 2+.**

**Props surface:**

| Prop | Effect |
|---|---|
| `eyebrow` | small-caps brand-teal label |
| `headline` | size.2xl bold |
| `subhead` | size.md body |
| `primaryCTA` | `{ label, href }` |
| `secondaryCTA` | optional `{ label, href }` |

**Visual structure:** centered. Dark background. One column. Generous padding (loose).

**Anti-patterns:**
- ❌ No three CTAs (max one primary + one secondary)
- ❌ No background image (premium-industrial = solid)
- ❌ No countdown timers, no urgency manipulation

---

## 9. `DiagramFrame`

**A focused close-up of one region of the architecture diagram. Used in section 5 (EdgeConnect block).**

**Props surface:**

| Prop | Effect |
|---|---|
| `source` | which SVG (or which crop region of `architecture-diagram-v2-light.svg`) |
| `caption` | optional caption below |
| `alignment` | `left` · `center` · `right` |

Implementation: in Phase 1.5 static reference, this is just an `<img>` of a pre-cropped variant. In Phase 1 Angular, it's an inline SVG with `<view>` element pointing to a region of the master diagram.

**Anti-patterns:**
- ❌ No re-creation of the diagram graphics — always crop / view-into the locked master
- ❌ No annotation decorations that diverge from the diagram spec v3

---

## 10. `NavMegaMenu`

**The top navigation. Sticky, transparent-to-solid scroll transition, mega-menu hover behavior for populated dropdowns.**

**Props surface:**

| Prop | Effect |
|---|---|
| `items` | array of `{ label, href, dropdown? }` |
| `primaryCTA` | `{ label, href }` rendered right-aligned |
| `transparentOver` | `dark-hero` · `light-hero` · `none` — controls initial transparency mode |

**Behavior:**
- Sticky at top, default `position: sticky; top: 0`
- Background transitions from transparent (over hero) to `bg.default` 95% opacity + `border.subtle` 1px bottom border (after scroll past hero)
- Mega-menu opens on hover with 150ms ease-out, closes 100ms after pointer leaves both trigger and menu (small intent-window)
- Mobile: hamburger toggle → full-screen overlay
- Items not yet populated (Phase 1): rendered dimmed (40% opacity), hover shows tooltip "Coming soon — Phase 2"

**Tokens:**
- Link text: `text.body` (transparent state) or `text.heading` (solid state, dark mode)
- Hover: brand teal underline 2px, offset 4px
- Active route: brand teal underline + slight bold weight shift

**Anti-patterns:**
- ❌ No hamburger on desktop (full nav visible)
- ❌ No animated logo on scroll
- ❌ No "transparent nav over light background" (loses legibility) — only over dark heroes
- ❌ No nav search box in Phase 1 (Phase 4 introduces search)

---

## 11. `Footer`

**Five-column footer + thin legal strip. Same on every DXP page.**

Defined in `homepage-spec-v1.md §1.2`. No additional component-level options in v1.

---

## 12. Motion language (global)

All components honor a single shared motion language.

| Token | Value | Use |
|---|---|---|
| `motion.fast` | 120ms ease-out | Button hover, color shifts |
| `motion.default` | 180ms ease-out | Card hover, accent slides |
| `motion.slow` | 280ms ease-out | Nav scroll-state transition, modal entrance |
| `motion.reveal` | 200ms ease-out + 12px translate-Y | Section scroll-reveal (Phase 1 optional) |

**Hard rule:** no motion longer than 320ms anywhere on the marketing surface. Long animations feel SaaS-marketing. Short, restrained transitions feel premium-industrial.

**Reduced motion:** `prefers-reduced-motion: reduce` disables all reveals and transitions globally. Required for a11y.

---

## 13. Typography scale (applied)

Inherits BRAND_TOKENS §3.3. Headlines use Inter at:

| Element | Size | Weight | Line-height |
|---|---|---|---|
| Hero headline | size.3xl (56px) | 600 | 1.1 |
| Section headline | size.xl (32px) | 600 | 1.2 |
| Sub-headline | size.lg (24px) | 600 | 1.3 |
| Body | size.base (16px) | 400 | 1.6 |
| Lead | size.md (18px) | 400 | 1.55 |
| Eyebrow / label | size.sm (14px) | 600 | 1.4 (letter-spacing 0.18em, small-caps) |
| Caption | size.sm (14px) | 400 | 1.5 |
| Microcopy | size.xs (12px) | 400 | 1.4 |

**Letter-spacing rules:**
- Eyebrow labels: 0.18em (already noted)
- Hero headline: -0.01em (slight tightening for size)
- Default body: 0

---

## 14. Spacing scale (applied)

Inherits BRAND_TOKENS §5. Component padding defaults:

| Component | Internal padding |
|---|---|
| `Button.lg` | 16 × 32 |
| `Button.md` | 12 × 24 |
| `CapabilityCard` | 24-32 (mobile→desktop) |
| `SectionShell.default` | 64 / 96 / 128 (mobile / tablet / desktop) |

---

## 15. Anti-patterns — system-wide

| Don't | Why |
|---|---|
| Use Angular Material components or styles | Per strategy v1 §5 — destroys premium feel |
| Introduce a custom shadow scale | Premium-industrial = flat surfaces. Use borders, not elevation. |
| Add a second accent color | Brand teal is the only accent (BRAND_TOKENS §2.1 lock) |
| Use Material Icons | Use a curated set (Tabler Icons recommended per BRAND_TOKENS §7) |
| Build per-page custom components | Every component grows additively in this library |
| Hardcode hex values inside components | Resolves to `brand-tokens.css` only |
| Animate beyond 320ms | Feels SaaS-marketing |
| Use rounded-pill button shapes | 4px corner radius — angular industrial |
| Use card-elevation hovers | Use accent-line slide-in instead |
| Show "Powered by Angular" anywhere | Premium positioning, not framework signaling |

---

## 16. Component coverage map — Phase 1 → Phase 4

| Component | Phase 1 | Phase 2 | Phase 3 | Phase 4 |
|---|---|---|---|---|
| `Button` | ✓ | extends (loading) | — | extends (calculator-binding) |
| `SectionShell` | ✓ | — | — | — |
| `CapabilityCard` | ✓ | extends (icon slot) | extends (industry variant) | extends (metric overlay) |
| `MetricStrip` | ✓ | — | extends (story variant) | extends (calculator output) |
| `ArchitecturePanel` | ✓ (static) | extends (hover annotations) | — | extends (interactive zoom) |
| `ProofBand` | ✓ (anonymized) | — | extends (with QuoteBlock pairing) | — |
| `QuoteBlock` | reserved | — | ✓ (introduced) | — |
| `CTASection` | ✓ | — | — | extends (gated download variant) |
| `DiagramFrame` | ✓ | — | — | — |
| `NavMegaMenu` | ✓ (dimmed Phase 2+ items) | extends (full populated) | extends (Industries) | extends (search) |
| `Footer` | ✓ | — | — | — |
| `CaseStudyShell` | — | — | ✓ (introduced) | — |
| `IndustryShell` | — | — | ✓ (introduced) | — |
| `CapabilityDeepDive` | — | ✓ (introduced) | — | — |
| `SolutionPanel` | — | ✓ (introduced) | — | — |
| `MetricSlider` | — | — | — | ✓ (introduced) |
| `CalculatorPanel` | — | — | — | ✓ (introduced) |
| `ResultsCard` | — | — | — | ✓ (introduced) |

**The point:** Phase 1's library is small (11 components) and grows additively. No Phase 2-4 work requires changes to Phase 1 components — only additions.

---

## 17. Sign-off checklist (v1 review)

- [ ] Every Homepage v1 section maps to a component in this document
- [ ] Every component honors token discipline (no hex in component spec)
- [ ] Every component honors the motion language (§12)
- [ ] No Material aesthetics anywhere
- [ ] Five-pillar tonal palette used only in `CapabilityCard.accent` and `ProofBand` anchors
- [ ] Brand teal is the only general accent
- [ ] Phase 2-4 components named but not specified in detail (deferred)
- [ ] Coverage map (§16) reasonable for the four-phase roadmap

---

*Design System v1, 2026-05-25. DRAFT. Pending user + ChatGPT review. Once locked, every component in the Elpis DXP resolves to a definition in this document.*
