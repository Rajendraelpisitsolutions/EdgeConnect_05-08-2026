<!--
File:        docs/marketing/design-system-v3.md
Purpose:     Component library blueprint for the Elpis DXP — v3. Additive
             over v2. Formalizes Phase 1 components that shipped inline in
             homepage-spec v3 (HeroComposite, TrustBand) and introduces
             the Phase 2 component layer (CapabilityDeepDive, SolutionPanel,
             ArchitecturePanel interactive variant) plus two cross-cutting
             patterns (trust cue, cross-lens navigation).
Audience:    Internal — Claude (page-spec author once v3 locks), user +
             ChatGPT (reviewers), engineering team (Angular implementers).
Format:      Markdown component spec. Same per-component structure as v2:
             purpose, props surface, token references, motion, anti-patterns,
             growth path.
Companion:   phase2-ia-scope-memo-v2.md + amendment v3 (parent IA model)
             design-system-v2.md (predecessor — components §1-§11 unchanged)
             homepage-spec-v3.md (Phase 1 surfaces that retroactively use
                §12 HeroComposite, §13 TrustBand)
             design-governance-v1.md (the discipline parent)
             BRAND_TOKENS.md (the visual values source of truth)
             assets/brand/brand-tokens.css (the resolved CSS variables)
Version:     v3 — LOCKED after Pass 1 user + ChatGPT review
Date:        2026-05-28
Status:      LOCKED.

Pass 1 ChatGPT review verdict (2026-05-28): "Design System v3 is
ready to become canonical." Four minor refinements applied at lock:
  - §0.5 NEW — three-layer architectural framing (Identity /
    Navigation / Content) as a mental model for where future
    components belong
  - §5.A — explicit mobile/desktop input model added (hover+click
    desktop / tap-only mobile); forbid list extended (no long-press,
    no swipe, no multi-touch gestures) per "industrial buyers value
    predictability over interaction novelty"
  - §24 — open questions transformed to resolved decisions:
      Q1 HeroComposite reuse on /platform → NO (keep homepage-only
         for now; brand identity vs platform explanation are
         different jobs)
      Q2 TrustBand on /customers → grid variant (per existing
         growth path)
      Q3 SolutionPanel migration → bulk migration (Phase E starts
         with the 5 migrations together — avoids piecemeal
         language / layout / pillar-reference inconsistency)
      Q4 Cross-lens refinement → defer until first real page-spec
         encounter

v2 → v3 changes:
  - ADDITIVE ONLY. v2 components §1-§11 are unchanged. v3 inserts new
    component sections §12-§15 + two content-pattern sections §16-§17
    before the global concerns.
  - §12 NEW HeroComposite — formalizes the composite hero shipped in
    homepage-spec v3 §3.1.
  - §13 NEW TrustBand — formalizes the customer-logo trust band shipped
    in homepage-spec v3 §3.1.5.
  - §14 NEW CapabilityDeepDive — Phase 2 reusable layout for the
    five /capabilities/<pillar> deep-dive pages.
  - §15 NEW SolutionPanel — Phase 2 reusable layout for the two
    /solutions/<solution> depth examples. Tighter than the first-draft
    v3 — references the existing CNC v2 solution-page template as the
    structural baseline; specifies only the ecosystem-framing
    additions (pillar cross-references, trust-cue placement,
    ArchitecturePanel.interactive embed).
  - §5 ArchitecturePanel — extended with §5.A "interactive variant"
    addendum covering Phase 2 interactivity. Uses existing motion.slow
    (280ms) — no new motion token introduced.
  - §16 NEW Trust cue content pattern — cross-cutting trust per memo v2
    §5.5. Content pattern + example markup, NOT a standalone component.
    Uses existing CapabilityCard variant for visual implementation.
  - §17 NEW Cross-lens navigation pattern — "see this from another
    angle" per memo v2 §5.2. Content pattern + locked per-surface
    presets table. Uses existing CapabilityCard variant.
  - §23 (formerly §16) Coverage map — refreshed for v3 additions.

Self-audit cuts from the first v3 draft (DELETED before LOCK):
  - CapabilityLayerCard — speculative; defer to /architecture page-spec
    authoring time when actual need is concrete
  - motion.architecture (240ms) — unnecessary token proliferation;
    existing motion.slow (280ms) covers the architecture zoom case
  - TrustCueBlock standalone component — demoted to content pattern §16
  - CrossLensBlock standalone component — demoted to content pattern §17

The cuts preserve the additive-only commitment and reduce v3 from a
+7-components / +1-extension / +1-motion-token first draft to a more
disciplined +4-components / +1-extension / +2-content-patterns final
draft.

v1 (design-system-v1.md) and v2 (design-system-v2.md) retained as
historical reference. v3 is canonical going forward once locked.
-->

# Elpis DXP — Design System v3

**Component library blueprint. v3 = v2 + Phase 1 retroactive formalizations + Phase 2 new components + two cross-cutting content patterns. Additive only. Implements the design discipline locked in `design-governance-v1.md`.**

This is not the Angular code. It is the *design contract* — what each component is, what it carries, what it must never become. The Angular team implements against this contract. Design Governance enforces the contract.

**v2 components §1-§11 are unchanged.** v3 inserts:
- New components §12-§15 (`HeroComposite`, `TrustBand`, `CapabilityDeepDive`, `SolutionPanel`)
- A `§5.A` extension to `ArchitecturePanel` for Phase 2 interactivity
- Two content patterns §16-§17 (trust cue, cross-lens navigation) that compose from existing components rather than introducing new ones

Global concerns (motion, typography, spacing, anti-patterns, coverage map) follow at §18-§22.

---

## 0. Foundational principles (unchanged from v2)

Five rules every component obeys. Each maps to a design-governance discipline area.

1. **Token discipline.** No hex values in component source. *(design-governance §2.1, §2.6)*
2. **One accent.** Brand teal is the only accent. The five-pillar tonal palette is the authorized exception inside `CapabilityCard` accent lines and `ProofBand` pillar markers. *(design-governance §2.4)*
3. **Premium industrial, never SaaS.** No drop shadows, no card-elevation hovers, no gradient text, no glass effects. Solid surfaces. Restrained motion. Generous spacing. *(design-governance §2.2, §2.4, §7)*
4. **Material primitives, never Material aesthetics.** Use Angular CDK for overlays, a11y, focus management. Never use Material's visual styles. *(design-governance §2.4 + strategy v2 §5 permanent lock)*
5. **Composable, not prescriptive.** Components compose from primitives. Prefer composition over options proliferation. **A "content pattern" — composition guidance using existing components — is preferred over a new component when no genuinely new visual primitive is needed.**

**The single test (design-governance §7):**

> *"Would this decision, if applied across every page in Phases 1-4, still feel like premium industrial ecosystem positioning, or would it start to feel like enterprise SaaS marketing?"*

---

## 0.5 Architectural layers (added at v3 lock per Pass 1 ChatGPT review)

v3 converges into three architectural layers. Every new component (Phase 2+) belongs to exactly one. Use this as the mental model when proposing future additions.

| Layer | Purpose | Components |
|---|---|---|
| **Identity layer** | Establishes Elpis as a vendor — what visitors recognize at first paint | `HeroComposite` (§12), `TrustBand` (§13) |
| **Navigation layer** | Moves visitors through the IA — between pages, between conceptual lenses | `NavMegaMenu` (§10), Cross-lens content pattern (§17) |
| **Content layer** | Carries the substance of each page — capability detail, solution narrative, architectural walkthrough | `CapabilityCard` (§3), `MetricStrip` (§4), `ArchitecturePanel` (§5 + §5.A), `ProofBand` (§6), `CapabilityDeepDive` (§14), `SolutionPanel` (§15), Trust cue content pattern (§16), `DiagramFrame` (§9), `QuoteBlock` (§7), `CTASection` (§8) |
| *Primitive layer* (implicit) | The composable foundation under everything else | `Button` (§1), `SectionShell` (§2), `Footer` (§11) |

**Why this matters for future work:**
- A new component proposal that doesn't fit cleanly into one layer is a signal to reconsider — either the component is conflating responsibilities, or the layer model itself needs to evolve.
- The Identity layer is intentionally small (2 components) and slow-growing. Identity components shape brand perception; their addition warrants brand + design review.
- The Navigation layer is small (2 entries — one component, one pattern). New navigation patterns are rare and high-leverage.
- The Content layer is where most growth happens. Phase 3+ extensions land here unless they introduce a genuine new visual primitive.

This framing is observational — it describes what v3 already is. It is not a hard partitioning rule. It is the mental model future contributors apply when asking "where does this belong?"

---

## 1–11. v2 components (unchanged)

§1 `Button` · §2 `SectionShell` · §3 `CapabilityCard` · §4 `MetricStrip` · §5 `ArchitecturePanel` (see §5.A below for Phase 2 extension) · §6 `ProofBand` · §7 `QuoteBlock` · §8 `CTASection` · §9 `DiagramFrame` · §10 `NavMegaMenu` · §11 `Footer`

Full v2 definitions in `design-system-v2.md`. Re-stated here only by reference; v3 does not modify any of them.

---

## 5.A `ArchitecturePanel` — Phase 2 interactive variant (extends §5)

**Purpose:** Phase 2 extension of the Phase 1 `ArchitecturePanel`. Adds inspectable interactivity per Phase 2 IA memo amendment v3 §1.3 (hover annotations + progressive zoom + click-to-focus; NO free-pan canvas, NO animated topology, NO diagram-editor behavior).

**Where used:** `/architecture` page (Phase 2). The Phase 1 homepage continues to use the non-interactive variant per `design-system-v2 §5`.

**Design Governance compliance:** §2.2 (motion — uses existing `motion.slow` 280ms for zoom transitions, well under 320ms ceiling); §2.3 (illustration — annotations are inline-text overlays, not decorative imagery); §2.4 (interaction — single-accent rule respected; annotations use brand-teal accents only).

**Props surface (Phase 2 additions to the v2 props):**

| Prop | Values | Effect |
|---|---|---|
| `interactive` | `true` (Phase 2) · `false` (default, Phase 1 behavior) | Enables hover/zoom/focus interactions |
| `annotations` | array of `{ region: { x, y, w, h }, eyebrow, title, body }` | Hover-revealed annotations anchored to SVG viewBox regions |
| `zoomLevels` | array of `{ name, viewBox }` | Discrete zoom states (default 1 = full diagram; 2-3 = layer-focused views) |
| `defaultZoom` | string (zoom-level name) | Which zoom level loads first (default: `full`) |

**Input model (locked at v3 — per Pass 1 ChatGPT review):**

| Device | Allowed inputs | Forbidden inputs |
|---|---|---|
| **Desktop** | hover, click, keyboard (Tab / Enter / Esc) | drag, scroll-zoom, pinch, multi-touch |
| **Mobile** | tap-only | long-press, swipe, multi-touch gestures |

**Industrial buyers value predictability over interaction novelty.** The input model is intentionally narrow — no clever gestures, no progressive disclosure through unusual touches, no input-method experimentation. If a future need genuinely requires a richer input model, it earns a design-governance §3 escalation (not a silent expansion).

**Interaction model (constrained per amendment v3 §1.3):**

| Interaction | Behavior |
|---|---|
| Hover over annotated region (desktop) | Annotation tooltip fades in (`motion.default` 180ms), anchored to region center, max-width 320px, dismissed on pointer-out |
| Tap on annotated region (mobile) | Annotation tooltip toggles open (same 180ms fade); tap elsewhere or tap the tooltip-close affordance dismisses |
| Click on layer region (desktop) / tap on layer region (mobile) | Progressive zoom to that layer's `zoomLevel`, `motion.slow` 280ms. Re-click/re-tap to zoom back out. |
| Click on "Reset view" button | Returns to `defaultZoom` |
| Keyboard navigation (desktop) | Tab cycles through annotated regions; Enter activates zoom; Esc resets to default zoom |

**Forbidden interactions (per amendment v3 §1.3 + Pass 1 ChatGPT review):**
- ❌ Free-pan infinite canvas (no drag-to-pan)
- ❌ Animated topology simulation (no flowing data particles, no pulsing nodes)
- ❌ Full diagram editor behavior (no add/move/delete nodes, no save state)
- ❌ Zoom outside the discrete `zoomLevels` (no pinch-zoom, no scroll-zoom)
- ❌ Motion exceeding `motion.slow` 280ms (uses locked motion vocabulary; no new tokens)
- ❌ **Long-press triggers** (mobile) — predictable taps only
- ❌ **Swipe-based navigation** between zoom states (mobile) — explicit tap on the next zoom region
- ❌ **Multi-touch gestures** (pinch-zoom, two-finger rotate) — single-touch interactions only
- ❌ **Hover-based zoom** — hover reveals annotations; explicit click is required to change zoom state

**Mobile behavior:** interactive variant degrades to static — tap-to-show-tooltip in place of hover; zoom levels become tabs at the top of the panel. Touch users get equivalent content access without depending on hover semantics.

**Annotations content discipline:** annotations are short (eyebrow + 4-word title + 1-2 sentence body, max). They are inspectable enrichment, not primary content — primary content lives in `caption` and the `/architecture` surrounding sections.

**Anti-patterns:**
- ❌ More than 8 annotations per zoom level (visual noise)
- ❌ Annotation tooltips that block the diagram content underneath
- ❌ Auto-cycling annotations (no carousel, no auto-rotate)
- ❌ Animated annotation entrance beyond `motion.default` 180ms

**Growth path:** Phase 4 may extend to a fully interactive overview-detail pattern (calculator-style exploration) if customer research justifies it. Not in scope until then.

---

## 12. `HeroComposite` (Phase 1 retroactive formalization)

**Purpose:** the composite hero pattern shipped in `homepage-spec v3 §3.1` — vertical 3-piece composition pairing a stylized intelligence-layer panel + teal data-flow beam + hardware product render + caption. Communicates "Industrial Intelligence Ecosystem" (software + hardware as one platform) at first paint.

**Where used:** Homepage hero (Section 1). Reusable on `/platform` hero (Phase 2) if visual variation is desired vs a copy-only hero — see §24 Q1.

**Design Governance compliance:** §2.2 (motion — composite is FULLY STATIC per homepage-spec v3 §5; no animated beam, no count-up KPIs); §2.3 (illustration — stylized dashboard panel is signifier, not real screenshot; hardware product render is a real PNG, never stock); §2.4 (interaction — no hover effects on composite).

**Props surface:**

| Prop | Effect |
|---|---|
| `dashboardKPIs` | array of `{ label, value, delta? }` — typically 3 KPIs (OEE / Active alarms / Plants online) |
| `dashboardSparkline` | optional `{ data: number[], gradient: 'teal' }` — small upward-trending sparkline in panel |
| `hardwareImage` | `{ src, alt, caption }` — PNG of a hardware product (mDAQ default per homepage-spec v3 §3.1) |
| `captionEyebrow` | string (small caps brand-teal) — default "ONE PLATFORM" |
| `captionLine` | string (size.sm italic) — the platform-anchor sentence below the composite |

**Visual structure (top-to-bottom on desktop; same vertical order on mobile):**

```
┌─────────────────────────────────┐
│ ╭──────────────────────────╮   │  ← Stylized intelligence-layer
│ │  OEE — PLANT 03   87.2%  │   │     dashboard panel
│ │  ACTIVE ALARMS    4      │   │     (max-width 460px)
│ │  PLANTS ONLINE    12/12  │   │     teal border, perspective
│ │  [sparkline ─────────── ]│   │     tilt 2° rotateX,
│ ╰──────────────────────────╯   │     drop-shadow + teal halo
│                                 │
│             ▲                   │  ← Teal data-flow beam
│             │                   │     (~100px tall)
│             ·                   │     three ascending dots,
│             ·                   │     upward arrow into panel,
│             ·                   │     "DATA" micro-label
│           DATA                  │     STATIC (no animation)
│                                 │
│ ┌──────────────────────────┐   │  ← Hardware product render
│ │   [mDAQ product photo]   │   │     real PNG, layered shadows
│ │                          │   │     max-width 320px
│ │   mDAQ · industrial      │   │     caption below
│ │   acquisition hardware   │   │
│ └──────────────────────────┘   │
│                                 │
│       ONE PLATFORM              │  ← Eyebrow (small caps, teal)
│       Hardware that captures   │  ← Caption (size.sm italic)
│       signal. Intelligence     │
│       that turns it into       │
│       decisions.                │
└─────────────────────────────────┘
```

**Tokens:**
- Dashboard panel background: `bg.deep` `#0F1419` with subtle teal-tinted border (1px `brand.teal` 40% opacity)
- KPI value: `text.heading` (white), size.xl semibold
- KPI label: `text.muted`, size.sm small-caps letter-spaced 0.18em
- KPI delta: `brand.teal-positive` for up-trend, `text.muted` for neutral
- Sparkline: `brand.teal` line on `bg.deep` background, subtle gradient fill below
- Beam: `brand.teal` gradient (10% opacity bottom → 100% opacity top), three ascending dots `brand.teal` at 60%/80%/100% opacity
- Hardware caption: `text.muted`, size.sm
- Composite caption eyebrow: `brand.teal`, size.sm small-caps letter-spaced 0.18em
- Composite caption line: `text.body`, size.sm italic

**Hard rules (per homepage-spec v3 §3.1 + design-governance §2.2):**
- ❌ NO infinite-loop animation on the data-flow beam
- ❌ NO count-up animation on KPI values
- ❌ NO spark-line animation
- ❌ NO real EREMOS V2 screenshot — the dashboard panel is a stylized *signifier*, not UI fidelity
- ❌ NO substituting the hardware product unless approved — mDAQ is locked per spec v3; Edge Gateway / E-IDOS / mTracker are reserved for Phase 3 product pages

**Mobile behavior:** the composite stacks in a single column. The dashboard, beam, hardware, and caption display in the same vertical order, sized down to fit the column width. Composite remains *below* the hero copy on mobile (mobile-first responsive flow per homepage-spec v3).

**Anti-patterns:**
- ❌ Replacing the hardware product image with a render of EdgeConnect software (would re-signal software-only identity, defeating D-2 intent)
- ❌ Adding a "live data" disclaimer (the panel is obviously stylized; a disclaimer admits doubt)
- ❌ Replacing the static beam with an animated SVG (design-governance §2.2 violation)

**Growth path:** Phase 3 may swap the stylized dashboard for a real EREMOS V2 screenshot once the product ships and screenshots are approved. The composite structure stays; only the panel content changes.

---

## 13. `TrustBand` (Phase 1 retroactive formalization)

**Purpose:** narrow dark band displaying authorized customer logos for trust signaling. Shipped in `homepage-spec v3 §3.1.5` between Hero and the five-pillar capability strip.

**Where used:** Homepage Section 1.5. Reserved for reuse on `/customers` (Phase 3), `/platform` if appropriate, and per-vertical solution pages in Phase E once corresponding industry-specific customer logos are authorized.

**Design Governance compliance:** §2.3 (illustration — customer logos are *informational*, not decorative; scoped exception to "no logos" general rule); §2.4 (single-accent exception — customer brand colors authorized inside the trust band ONLY, never beyond it).

**Props surface:**

| Prop | Effect |
|---|---|
| `mode` | `dark-deep` (default — for placement after dark hero) · `light` (for placement on light page) |
| `eyebrow` | small-caps centered headline, e.g. "TRUSTED BY INDUSTRIAL LEADERS ACROSS AUTOMOTIVE, ENERGY, HEAVY MANUFACTURING, AND DEFENSE" |
| `logos` | array of `{ src, alt, name, href? }` — typically 8 logos for the homepage variant; can be 4-12 in other contexts |
| `treatment` | `natural-color` (L2 — default for homepage trust band) · `monochrome` (alternative for Phase 3 customers page if many logos at once) |

**Logo treatment rules (LOCKED per homepage-spec v3 §3.1.5):**
- 64px tall on desktop (≥ 1024px), 56px on tablet, 48px on mobile (≤ 640px)
- 92% opacity at rest, full opacity on hover
- Subtle scale on hover: `transform: scale(1.04)`, 180ms `motion.default`
- No background pills, no white cards — logos display directly on the dark/light surface
- Logos render in their **natural brand colors** for the homepage variant (L2 treatment per amendment v4 + homepage-spec v3 §3.1.5)
- Alternative `monochrome` treatment reserved for Phase 3 contexts with 20+ logos where natural-color becomes visually noisy

**Authorization governance (CRITICAL — per `positioning-amendment-v4.md` §3):**
- Only customers whose logos are already publicly displayed on `www.elpisitsolutions.com` are authorized for trust-band use
- Phase 1 authorized set: GE, Hitachi, Toyota, Schneider Electric, BHEL, TVS, HYDAC, Filtrec (8 logos)
- Adding a new customer logo requires a positioning-amendment update (e.g., amendment v5)
- Customer logos in the trust band are **never** paired with specific deployment stories in Phase 1 (positioning v3 §4 + amendment v4 §2)

**Mobile behavior:** logos shrink to 48px tall; row wraps to multiple lines if needed. Hover degrades to tap (no scale animation on touch).

**Anti-patterns:**
- ❌ Adding an unauthorized customer logo (governance breach — see positioning amendment v4)
- ❌ Pairing a logo with a specific deployment story in Phase 1
- ❌ Using rotating-carousel display (visual noise; design-governance §2.2 forbidden)
- ❌ Decorating logos with badges, borders, or stars
- ❌ Sorting logos by "tier" or "size" in a way that visibly ranks customers
- ❌ Mixing `natural-color` and `monochrome` treatments within a single band

**Growth path:** Phase 3 introduces a logo-grid variant for `/customers` (`TrustBand.grid` extending the row layout to a paginated grid). Phase 4 may introduce per-industry filtering.

---

## 14. `CapabilityDeepDive` (Phase 2 introduction)

**Purpose:** the reusable layout component for the five `/capabilities/<pillar>` deep-dive pages. One component, five instances (Connectivity & Edge / Data Acquisition / Asset Intelligence / Condition Monitoring / Operational Intelligence).

**Where used:** Phase 2 pillar pages. Per Phase 2 IA memo v2 §3.3 + §6 sequencing — the most-used reusable layout in Phase 2.

**Design Governance compliance:** §2.1 (spacing — uses `SectionShell` defaults); §2.4 (interaction — pillar accent line only, no card elevation); §2.6 (visual hierarchy — locked headline scale).

**Props surface:**

| Prop | Effect |
|---|---|
| `pillar` | `connectivity-edge` · `data-acquisition` · `asset-intelligence` · `condition-monitoring` · `operational-intelligence` |
| `customerQuestion` | string — the question this pillar answers (verbatim from `hardware-ecosystem-map v3 §1`) |
| `products` | array of `{ name, category, oneLineDescription, productPageHref? }` — typically 1-2 products per pillar |
| `eliminatesFromBOM` | array of strings — what the pillar removes from the customer's bill of materials |
| `strategicAdjacencies` | object `{ buyers: string[], industries: string[], deploymentNotes?: string }` |
| `architecturePosition` | object `{ stackLayer: string, architecturePageHref: string }` |
| `trustPosture` | object `{ relevantTrustProperties: string[], securityPageHref: string }` — one paragraph + cross-link to `/security` (per trust cue pattern §16) |
| `relatedSolutions` | array of `{ name, oneLineOutcome, href }` |
| `cta` | object `{ primary: { label, href }, secondary?: { label, href } }` |

**Visual structure (top-to-bottom):**

```
[SectionShell mode="dark-deep"]
  EYEBROW · CAPABILITY — <pillar name>
  Hero headline (size.3xl)
  Customer-question lead (size.lg italic)
  [Primary CTA] [Secondary CTA]

[SectionShell mode="dark"]
  PRODUCTS IN THIS PILLAR
  → 1-2 CapabilityCard variants with pillar accent

[SectionShell mode="light"]
  WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
  → bulleted list (drives commercial insight)

  STRATEGIC ADJACENCIES
  → Buyers · Industries · Deployment notes
    (3-column grid desktop, stacked mobile)

[SectionShell mode="light-tinted"]
  WHERE THIS FITS IN THE INDUSTRIAL INTELLIGENCE STACK
  → DiagramFrame (focused on this pillar's layer)
    + caption + "See full architecture →" link

  TRUST POSTURE FOR THIS PILLAR
  → applies the §16 trust cue content pattern
    (cross-link to /security)

[SectionShell mode="light"]
  RELATED SOLUTIONS
  → 2-3 CapabilityCard variants (solution-card variant)

[Cross-lens navigation — §17 content pattern]

[CTASection]
```

**Tokens:** inherits `SectionShell` mode tokens; pillar accent uses `--color-pillar-<n>` from `BRAND_TOKENS.md §2.2`.

**Anti-patterns:**
- ❌ Adding a full product detail section (product detail = Phase E)
- ❌ Listing more than 3 related solutions (signal dilution)
- ❌ Replacing the focused `DiagramFrame` with the full architecture diagram
- ❌ Duplicating trust posture content from `/security` (cross-link via §16 pattern instead)
- ❌ Adding a customer-logo strip inside the capability page (trust signaling is platform-level)

**Growth path:** Phase 3 may extend with a per-industry context box once industries pages introduce vertical filtering.

---

## 15. `SolutionPanel` (Phase 2 introduction)

**Purpose:** the reusable layout component for the two Phase 2 `/solutions/<solution>` depth examples (predictive-maintenance, edge-connectivity). **Structurally identical to the existing solution-page template in `solution-cnc-machining-v2.md` §template-inheritance-notes** — this component formalizes the same structure as an Angular component, with the ecosystem-framing additions noted below.

**Where used:** Phase 2 solution depth examples. Phase E migrates the existing 5 v2 solution pages (CNC, brownfield, multi-site, OEM, precision) to this component for visual consistency across the full solution set.

**Design Governance compliance:** §2.1 (spacing — `SectionShell` defaults); §2.4 (interaction — outcome-led, no decorative interactions); §2.6 (visual hierarchy — same scale as `CapabilityDeepDive`).

**Structural baseline (inherits from `solution-cnc-machining-v2.md`):**

| Section | Content | Source |
|---|---|---|
| Hero | Outcome headline (not products) + 2-line subhead + CTAs | CNC template §1 |
| The customer pain | Narrative empathy, 2-3 paragraphs | CNC template §2 |
| How Elpis solves this | 3-4 bolded-lead paragraphs | CNC template §3 |
| What's included | Bulleted feature list split by product/pillar | CNC template §4 |
| Common questions | 5-7 Q&A pairs | CNC template §5 |
| Customer outcomes | Bulleted outcome list, two-column desktop | CNC template §6 |
| Typical engagement | Optional 4-step rollout timeline | CNC template §7 |
| Architecture | Annotated diagram subset | CNC template §8 |
| Final CTA | Vertical-localized "Bring us your X" | CNC template §9 |

**Phase 2 ecosystem-framing additions (what's NEW vs CNC template):**

1. **Pillar cross-references** — the "How Elpis solves this" section explicitly names which capability pillar(s) contribute (each contributing pillar gets an inline link to `/capabilities/<pillar>`)
2. **Trust cue placement** — applies the §16 trust cue content pattern between Customer Outcomes and Architecture sections (cross-links to `/security`)
3. **`ArchitecturePanel.interactive` embed** — the Architecture section uses the §5.A interactive variant with solution-specific annotations, instead of a static `DiagramFrame`
4. **Cross-lens navigation** — applies the §17 cross-lens content pattern between Architecture and the Final CTA

**Props surface:** identical to the CNC v2 template content model — each section's props mirror the markdown structure. See `solution-cnc-machining-v2.md` for the canonical content fields. The Angular component wraps them with `SectionShell` instances and the four new ecosystem-framing additions.

**Anti-patterns:**
- ❌ Leading the hero with products instead of the outcome (defeats FOR WHAT framing per memo v2 §2)
- ❌ Including content from `/capabilities/<pillar>` as primary — cross-link instead (memo v2 §4.0)
- ❌ Adding a customer-logo strip mid-page (proof anchors at platform-level only per amendment v4)
- ❌ Including specific customer names in deployment stories in Phase 2 (positioning v3 §4 lock)
- ❌ Replacing the §5.A interactive diagram with a static image — solution pages need annotated subsets

**Growth path:** Phase E migrates CNC v2, brownfield v2, multi-site v2, OEM v2, precision v2 to this component. Visual consistency across all 7 solution pages by end of Phase E.

---

## 16. Trust cue content pattern (cross-cutting, per memo v2 §5.5)

**Purpose:** standardized way to surface trust cues across Phase 2 pages without creating a new component. `/security` is authoritative for trust philosophy; this pattern lets every other page reference it consistently while honoring memo v2 §4.0 (authoritative-explanation invariant).

**Where used:** every Phase 2 page except `/security` itself — `/platform`, `/capabilities/<pillar>` (in the trust-posture slot), `/architecture` (architecture security mechanics slot), `/solutions/<solution>` (between Customer Outcomes and Architecture).

**Why a content pattern, not a component:** the trust cue is visually a small text block with an eyebrow + sentence(s) + a cross-link. It composes from `CapabilityCard` (compact variant) or even just text + a styled link — no new visual primitive needed. Standardization is editorial discipline, not a new component contract.

**Pattern (markdown example):**

```html
<!-- Compact CapabilityCard variant, mode="light" or "dark-deep" per page -->
<aside class="trust-cue">
  <div class="trust-cue__accent"></div>
  <span class="trust-cue__eyebrow">TRUST POSTURE</span>
  <p class="trust-cue__text">
    {cue text — 1-2 sentences specific to this page's relevant trust property}
  </p>
  <a href="/security" class="trust-cue__link">
    Read the full trust posture →
  </a>
</aside>
```

**Visual specification:**
- Vertical accent line, 2px wide, `brand.teal` color
- Eyebrow: small caps, letter-spaced 0.18em, `brand.teal`
- Body text: regular size.base, inherits mode's body color
- Cross-link: `Button.ghost` variant or text-only with arrow → suffix
- Internal padding: `space.4` to `space.6` (compact)

**Per-page content discipline:**

| Page | Cue focus |
|---|---|
| `/platform` | Trust posture trio summary |
| `/architecture` | Architectural mechanics (data path, audit chain, AI boundaries) |
| `/capabilities/connectivity-edge` | Offline-first operation, no forced cloud dependency |
| `/capabilities/data-acquisition` | Direct sensor acquisition without intermediary trust assumptions |
| `/capabilities/asset-intelligence` | Per-gateway identity, customer/site binding |
| `/capabilities/condition-monitoring` | Customer-controlled telemetry, data sovereignty |
| `/capabilities/operational-intelligence` | Multi-tenant isolation, no data leakage |
| `/solutions/predictive-maintenance` | Who owns the condition data |
| `/solutions/edge-connectivity` | Offline-first operation, broker-agnostic |

**Hard discipline rules:**
- Cues are **never decorative.** Each cue is specifically relevant to the page's primary question.
- Cue text **never** duplicates `/security` content. It surfaces ONE relevant property and cross-links for the rest.
- Maximum **2 cues per page.** More than 2 dilutes the signal; the trust posture is punctuation, not narrative.
- Cross-link destination is **always** `/security` (specific section anchor where appropriate, e.g., `/security#offline-operation`).

**Anti-patterns:**
- ❌ Repeating the full trust posture content (violates memo v2 §4.0)
- ❌ Using a different accent color (single accent only)
- ❌ Stacking 3+ cues on a single page (signal dilution)

---

## 17. Cross-lens navigation pattern (per memo v2 §5.2)

**Purpose:** standardized way to offer visitors a different organizing lens on the same content. Implements memo v2 §5.2 "see this from another angle" navigation without creating a new component.

**Where used:** appended near the end of every Phase 2 page (before the final `CTASection`).

**Why a content pattern, not a component:** the cross-lens block is structurally 2-3 `CapabilityCard` variants in a `SectionShell`. The pattern is in the *content* (which lenses to surface from each page) and the *presets* (the locked per-surface link table below). The visual is just compact cards — no new primitive.

**Pattern:** use `SectionShell` mode="light-tinted" padding="tight" + 2-3 `CapabilityCard` variants (small variant, no pillar accent, optional eyebrow).

**Per-surface link presets (LOCKED per memo v2 §5.2):**

| From surface | Cards rendered |
|---|---|
| `/platform` | `/capabilities`, `/architecture`, `/solutions` |
| `/capabilities` hub | `/platform`, `/architecture`, `/solutions` |
| `/capabilities/<pillar>` | `/architecture`, `/solutions` (filtered to related), `/capabilities` (back to hub) |
| `/architecture` | `/capabilities`, `/solutions` |
| `/solutions` hub | `/platform`, `/capabilities`, `/architecture` |
| `/solutions/<solution>` | `/capabilities/<related-pillar>`, `/architecture`, `/solutions` (back to hub) |
| `/security` | `/platform`, `/architecture` |

**Headline:** default *"Looking for the same thing from another angle?"* — overridable per page if context calls for variant phrasing.

**Card content per destination:**

| Destination | Eyebrow | Description (one-line) |
|---|---|---|
| `/platform` | PLATFORM | "Looking for the full vendor evaluation?" |
| `/capabilities` | CAPABILITIES | "Capability-first navigation" |
| `/capabilities/<pillar>` | CAPABILITY · <pillar> | "Capability detail for <pillar>" |
| `/architecture` | ARCHITECTURE | "How does the data flow end-to-end?" |
| `/solutions` | SOLUTIONS | "Outcome-organised stories" |
| `/solutions/<solution>` | SOLUTION · <name> | "How this looks for <use case>" |
| `/security` | SECURITY | "Trust posture in detail" |

**Hard discipline rules:**
- Maximum **3 cards** per cross-lens block (signal dilution beyond 3)
- **Never** repeat the surface the visitor is already on (filter via `currentSurface`)
- Card styling is **secondary** to page primary CTA (`CTASection` is the conversion moment; cross-lens is exploration)
- Cross-lens is exploration, not conversion — no primary-button styling on cards

**Anti-patterns:**
- ❌ More than 3 cards (signal dilution)
- ❌ Big primary-button styling on cards (the block is exploration, not conversion)
- ❌ Repeating the surface the visitor is already on
- ❌ Animated card transitions (compose from `CapabilityCard` defaults — no new motion)

---

## 18. Motion language (unchanged from v2 §12)

All v2 motion tokens unchanged. v3 introduces NO new motion tokens.

| Token | Value | Use |
|---|---|---|
| `motion.fast` | 120ms ease-out | Button hover, color shifts |
| `motion.default` | 180ms ease-out | Card hover, accent slides, annotation tooltips |
| `motion.slow` | 280ms ease-out | Nav scroll-state transition, modal entrance, `ArchitecturePanel.interactive` zoom transitions |
| `motion.reveal` | 200ms ease-out + 12px translate-Y | Section scroll-reveal (Phase 1 optional) |

**`ArchitecturePanel.interactive` zoom transitions use the existing `motion.slow` (280ms)** — no new token required. Bounded by the 320ms ceiling per design-governance §2.2.

All other motion rules unchanged from v2. No motion > 320ms anywhere; `prefers-reduced-motion: reduce` honored globally.

---

## 19. Typography scale (unchanged from v2 §13)

No changes. v2 §13 typography scale stands. New components use existing scale tokens.

---

## 20. Spacing scale (unchanged from v2 §14)

No changes. v2 §14 spacing scale stands. New components use existing spacing tokens.

---

## 21. Anti-patterns — system-wide (extends v2 §15)

All v2 §15 anti-patterns remain in force. v3 adds:

| Don't | Governance reference |
|---|---|
| Animate the `HeroComposite` beam, dashboard panel, or KPIs | design-governance §2.2 + homepage-spec v3 §3.1 lock |
| Add an unauthorized customer logo to `TrustBand` | positioning-amendment-v4 §3 |
| Introduce a new motion token outside the locked v2 motion language | design-governance §2.2 lock |
| Duplicate `/capabilities/<pillar>` content inside `SolutionPanel` | memo v2 §4.0 authoritative-explanation invariant |
| Stack 3+ trust cues on a single page via the §16 pattern | signal dilution; trust cues are punctuation |
| Render more than 3 cards in the §17 cross-lens pattern | signal dilution |
| Substitute the mDAQ image in `HeroComposite` without amendment | homepage-spec v3 §3.1 lock |

---

## 22. Component coverage map — Phase 1 → Phase 4 (updated from v2 §16)

| Component | Phase 1 | Phase 2 | Phase 3 | Phase 4 |
|---|---|---|---|---|
| `Button` (§1) | ✓ | extends (loading) | — | extends (calculator-binding) |
| `SectionShell` (§2) | ✓ | — | — | — |
| `CapabilityCard` (§3) | ✓ | extends (icon slot, used by §17 cross-lens pattern, used by §16 trust cue compact variant) | extends (industry variant) | extends (metric overlay) |
| `MetricStrip` (§4) | ✓ | — | extends (story variant) | extends (calculator output) |
| `ArchitecturePanel` (§5) | ✓ (static) | **✓ §5.A interactive variant** | — | extends (calculator-style exploration?) |
| `ProofBand` (§6) | ✓ (anonymized) | — | extends (with QuoteBlock pairing) | — |
| `QuoteBlock` (§7) | reserved | — | ✓ (introduced) | — |
| `CTASection` (§8) | ✓ | — | — | extends (gated download variant) |
| `DiagramFrame` (§9) | ✓ | — | — | — |
| `NavMegaMenu` (§10) | ✓ (dimmed Phase 2+ items) | extends (full populated) | extends (Industries) | extends (search) |
| `Footer` (§11) | ✓ | — | — | — |
| **`HeroComposite` (§12)** | **✓ (formalized in v3)** | extends (`/platform` reuse if visual variation desired — see §24 Q1) | — | — |
| **`TrustBand` (§13)** | **✓ (formalized in v3)** | — | extends (grid variant for `/customers`) | extends (per-industry filtering) |
| **`CapabilityDeepDive` (§14)** | — | **✓ (introduced in v3)** | — | — |
| **`SolutionPanel` (§15)** | — | **✓ (introduced in v3)** | — | extends (calculator-result embed for Phase 4 ROI) |
| `CaseStudyShell` | — | — | ✓ (introduced) | — |
| `IndustryShell` | — | — | ✓ (introduced — Phase 2.5 exception possible per memo amendment v3 §2) | — |
| `MetricSlider` | — | — | — | ✓ (introduced) |
| `CalculatorPanel` | — | — | — | ✓ (introduced) |
| `ResultsCard` | — | — | — | ✓ (introduced) |

**Phase 2 introduces 4 new components (§12-§15) + 1 extension (§5.A) + 2 content patterns (§16-§17).** Phase 1 components §1-§11 unchanged.

**Content patterns are NOT new components.** §16 trust cue and §17 cross-lens are composition guidance using existing `CapabilityCard` and `SectionShell` — they appear in the table only by virtue of those components being used by them.

**The additive-only commitment (design-governance + roadmap v2 cross-phase commitment #7) holds.** No Phase 1 page breaks when Phase 2 components ship.

---

## 23. Sign-off checklist (v3 lock)

- [ ] Every component in §12-§15 has: purpose, where used, design-governance compliance line, props surface, visual structure, tokens, anti-patterns, growth path
- [ ] `HeroComposite` (§12) matches the homepage-spec v3 §3.1 inline definition exactly
- [ ] `TrustBand` (§13) matches the homepage-spec v3 §3.1.5 inline definition exactly, and honors `positioning-amendment-v4.md` §3 logo-authorization governance
- [ ] `CapabilityDeepDive` (§14) layout serves all 5 pillar pages without per-pillar variation needed
- [ ] `SolutionPanel` (§15) references the existing solution-cnc-machining-v2 template as structural baseline, with ecosystem-framing additions clearly enumerated
- [ ] `ArchitecturePanel.interactive` variant (§5.A) honors Phase 2 IA memo amendment v3 §1.3 interaction scope, uses existing `motion.slow` for zoom transitions (no new motion token)
- [ ] §16 trust cue content pattern documented with per-page content discipline table and locked discipline rules
- [ ] §17 cross-lens navigation content pattern documented with locked per-surface link presets
- [ ] Motion language §18 honors design-governance §2.2 — NO new motion tokens introduced
- [ ] Anti-patterns §21 extends v2 §15 without contradiction
- [ ] Coverage map §22 reflects 4 new components + §5 extension + 2 content patterns; speculative additions from first v3 draft (CapabilityLayerCard, motion.architecture, TrustCueBlock standalone, CrossLensBlock standalone) have been cut per self-audit
- [ ] All new components reference design-governance §2.x discipline areas explicitly
- [ ] All new components use existing brand-tokens; NO new color hex or motion token introduced
- [ ] The additive-only commitment holds — v2 components §1-§11 unchanged

---

## 24. Resolved decisions (formerly open questions — Pass 1 ChatGPT review)

All four open questions from the v3 revision were resolved during the Pass 1 ChatGPT review and are locked here.

### Q1 — `HeroComposite` reuse on `/platform`: **NO — keep homepage-only for now**

The homepage hero is *brand identity*. The `/platform` hero is *platform explanation*. Those are different jobs. If both pages used `HeroComposite`, `/platform` would risk feeling like a repeated homepage rather than a distinct deep-dive.

`/platform` uses a different hero pattern (copy-led, possibly with a sub-component derived from the Industrial Intelligence Stack diagram, or a layered capability summary) — specified at `/platform` page-spec authoring time. `HeroComposite` reuse may be revisited in a future design-system version once real `/platform` usage patterns clarify whether visual repetition or distinction serves visitors better.

**Locked governance:** any proposal to reuse `HeroComposite` on a non-homepage surface requires explicit design-track review.

### Q2 — `TrustBand` on `/customers`: **Grid variant** (per existing growth path)

Phase 3 introduces `TrustBand.grid` per §13 growth-path note. Row layout (homepage) cleanly scales to a paginated grid for the fuller customer index that `/customers` will need.

The decision is locked at v3; implementation lands when `/customers` page-spec authoring begins in Phase 3.

### Q3 — `SolutionPanel` migration of existing 5 v2 solution pages: **Bulk migration**

Phase E begins by migrating all 5 existing v2 solution pages (CNC, brownfield, multi-site, OEM, precision) to `SolutionPanel` together. Piecemeal migration would create inconsistency in language, layout, and pillar-reference patterns across the solution set during the transition window. Bulk migration trades one larger Phase-E-opening effort for clean ongoing consistency.

The migration is the first Phase E deliverable; subsequent Phase E work (pitch deck v6, datasheet v4, security page v3, sales objection guide v3, ROI calc v3) follows on the migrated solution-page foundation.

### Q4 — Cross-lens pattern card-styling refinement: **Defer**

The §17 cross-lens content pattern is specified as compact `CapabilityCard` variants composed in a `SectionShell`. If during page-spec authoring a visually distinct treatment emerges as clearly better, it earns a `§17.A` extension at that point — not now.

No design speculation in advance of real usage encounter.

---

## 25. What v3 does NOT do (explicit cuts from first-draft self-audit)

To prevent governance creep and keep the design contract disciplined, v3 explicitly:

- **Does NOT introduce `CapabilityLayerCard`** — speculative addition cut from the first draft. The `/architecture` page-spec will determine actual need at authoring time. If needed, it lands as a `CapabilityCard` variant or a `§18` introduction in a later design-system version.
- **Does NOT introduce `motion.architecture` (240ms)** — speculative token cut from the first draft. Existing `motion.slow` (280ms) covers `ArchitecturePanel.interactive` zoom transitions. No new motion token needed.
- **Does NOT introduce `TrustCueBlock` as a standalone component** — demoted to the §16 content pattern. Visual implementation composes from `CapabilityCard` (compact variant) per the locked discipline rules.
- **Does NOT introduce `CrossLensBlock` as a standalone component** — demoted to the §17 content pattern. Visual implementation composes from `CapabilityCard` variants and `SectionShell` per the locked per-surface presets.
- **Does NOT modify v2 components §1-§11.** Additive only.
- **Does NOT introduce new color tokens.** Brand teal is still the only general accent.
- **Does NOT introduce Phase 3+ components.** `CaseStudyShell`, `IndustryShell`, `MetricSlider`, `CalculatorPanel`, `ResultsCard` remain reserved for their respective phases.
- **Does NOT govern proof placement.** That lives in the upcoming `proof-architecture-v1.md`.
- **Does NOT govern buyer-targeted page tone.** That lives in the upcoming `buyer-taxonomy-v1.md`.
- **Does NOT change motion ceiling, spacing scale, or typography scale.** All Phase 2 components fit within v2 locks.

---

*Design System v3 — LOCKED 2026-05-28 after Pass 1 user + ChatGPT review. Additive over v2. Self-audit revision: cut 4 speculative items (`CapabilityLayerCard`, `motion.architecture`, `TrustCueBlock` standalone, `CrossLensBlock` standalone) from the first draft. Pass 1 lock additions: §0.5 three-layer architectural framing (Identity / Navigation / Content), §5.A explicit mobile/desktop input model with locked forbid list, §24 all four open questions resolved. Final v3 = 4 new components (HeroComposite, TrustBand, CapabilityDeepDive, SolutionPanel) + §5.A interactive variant extension + 2 content patterns (trust cue, cross-lens navigation). v2 components §1-§11 unchanged.*
