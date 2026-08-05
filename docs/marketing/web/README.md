<!--
File:        docs/marketing/web/README.md
Purpose:     How to read, view, and use the static HTML/CSS reference
             implementation of Homepage v2.
Audience:    Engineering team (Phase 1 Angular implementers), user,
             ChatGPT (Pass 2 reviewer), future hires.
Version:     v1
Date:        2026-05-26
-->

# Elpis DXP Homepage — Static Reference (Phase 1.5)

**Visual ground truth for the Phase 1 Angular implementation.**

This directory contains a working static HTML/CSS implementation of `docs/marketing/homepage-spec-v2.md`. It is meant to be opened in a browser and used as the pixel-accurate, copy-verbatim reference for the Angular team. The Angular team owns Phase 1 production; this reference removes ambiguity about what the production page should look and behave like.

---

## What's in here

```
docs/marketing/web/
├── README.md      ← this file
├── index.html     ← full 9-section homepage
├── styles.css     ← complete visual system, organized by:
│                    1. Brand tokens (synced from brand-tokens.css)
│                    2. Reset / base
│                    3. Typography utilities
│                    4. Layout primitives
│                    5. Mode aliases ([data-mode] flipping)
│                    6. Components (button, nav, cards, metric strip, etc.)
│                    7. Section-specific layouts
│                    8. Responsive
│                    9. A11y (focus-visible + reduced motion)
└── assets/        ← self-contained copies of referenced media:
                     - architecture-diagram-v2-light.svg  (architecture section)
                     - architecture-diagram-v2-simple.svg (hero mobile diagram)
                     - architecture-diagram-v2-dark@2x.png ("See architecture in detail" link)
                     - datasheet-v3-a4.pdf                (Download CTA target)
```

**Note on `assets/`:** these are copies of the canonical files in `docs/marketing/assets/`. They live here so the static reference is self-contained — `file://` viewing and local-server viewing both work without parent-directory path resolution. When the canonical assets update, re-copy them into this directory.

---

## How to view

**Option 1 — open in a browser directly:**

```bash
# Open index.html in your default browser
start docs/marketing/web/index.html        # Windows
open  docs/marketing/web/index.html        # macOS
xdg-open docs/marketing/web/index.html     # Linux
```

The page loads from `file://`. The architecture diagram (`../assets/architecture-diagram-v2-light.svg`) renders inline. Inter loads from Google Fonts (requires network).

**Option 2 — local HTTP server (recommended for accurate SVG / Lighthouse testing):**

```bash
# From repo root
cd docs/marketing/web
python -m http.server 8080
# then visit http://localhost:8080/
```

This avoids any `file://` quirks with image references and lets you run Lighthouse against a realistic URL.

---

## What this reference IS

- **Pixel-accurate** to the locked Homepage v2 spec — every section, every CTA, every word
- **Copy verbatim** — the Angular team should lift text directly from this HTML
- **Brand-honest** — every color, font, spacing value resolves to a CSS custom property from `brand-tokens.css`
- **Responsive** — mobile (375), tablet (768), desktop (1280), large desktop (1920)
- **Accessible** — semantic HTML, ARIA labels where needed, skip-to-content link, visible focus rings, reduced-motion respect
- **Self-contained** — opens directly in any modern browser, no build step

## What this reference IS NOT

- **Not the production code.** The Angular team rewrites this in Angular 19 components.
- **Not SSR/SSG-rendered.** That's a Phase 1 Angular concern; this is static HTML.
- **Not wired to backend.** Forms link to `mailto:` placeholders; downloads point to local PDF paths.
- **Not analytics-enabled.** Add Plausible/GA4 in the Angular implementation.
- **Not the design system.** That lives in `design-system-v2.md`. This file implements those components in plain HTML/CSS as a visual sanity check.

---

## Mapping to Angular components

Each section of `index.html` corresponds to a component in `design-system-v2.md`:

| HTML region | Angular component | Source classes |
|---|---|---|
| `<header class="nav">` | `<NavMegaMenu>` | `.nav`, `.nav__items`, `.nav__item`, `.nav__cta` |
| `<section class="hero">` | `<SectionShell mode="dark-deep">` + `<HeroBlock>` (page-specific composition) | `.hero`, `.hero__inner`, `.hero__copy`, `.hero__diagram` |
| Capability cards | `<CapabilityCard>` × 5 inside `<SectionShell mode="dark">` | `.capability-card`, `.capability-card--p1` … `.capability-card--p5` |
| Architecture section | `<ArchitecturePanel>` inside `<SectionShell mode="light">` | `.arch-panel`, `.arch-panel__diagram`, `.arch-panel__annotations` |
| Hardware grid | `<CapabilityCard variant="hardware">` × 5 | `.hardware-grid`, `.capability-card` |
| EdgeConnect / EREMOS depth | `<MetricStrip>` inside `<SectionShell>` | `.depth-section__inner`, `.metric-strip`, `.metric` |
| Proof band | `<ProofBand>` inside `<SectionShell mode="dark-deep">` | `.proof-band__head`, `.proof-anchors`, `.proof-anchor` |
| Audience cards | `<AudienceCard>` × 3 | `.audience-grid`, `.audience-card` |
| CTA section | `<CTASection>` inside `<SectionShell mode="dark-deep">` | `.cta-section`, `.cta-section__head`, `.cta-section__group` |
| Footer | `<Footer>` | `.footer`, `.footer__grid`, `.footer__col` |
| Every button | `<Button>` | `.btn`, `.btn--primary`, `.btn--secondary`, `.btn--lg`, etc. |

---

## How to consume this in the Angular implementation

1. **Read the spec first.** `homepage-spec-v2.md` is the canonical document. This static reference is the visual translation, not the source of truth.
2. **Import `brand-tokens.css` into the Angular project's global styles.** Do not copy the tokens — reference them.
3. **Configure Tailwind to read the CSS variables.** See `brand-tokens.css` bottom comment block for the Tailwind config snippet.
4. **Build the components in `design-system-v2.md` order.** Start with `Button` and `SectionShell` — every other component composes from them.
5. **Match the static reference visually.** Open both side-by-side; if Angular drifts, the static reference wins (until a homepage-spec v3 supersedes it).
6. **Honor design-governance.** `design-governance-v1.md` §4 drift signals apply per-PR.

---

## Known limitations of this static reference

| Limitation | Why | Resolved by |
|---|---|---|
| No JavaScript interactions | Static reference is HTML/CSS only | Angular components implement interactions |
| Mobile mega-menu is unstyled placeholder | No JS to drive the toggle | Angular `NavMegaMenu` implements full mobile overlay |
| Discovery-call CTA is a `mailto:` link | No backend in static reference | Angular implements structured contact form per homepage-spec v2 §8.1 |
| Datasheet download link assumes `/assets/datasheet-v3-a4.pdf` | Static reference doesn't reach the actual asset hosting | Angular routes to actual download endpoint |
| Sticky nav doesn't transition opacity on scroll | No JS to detect scroll position | Angular implements scroll-state transition (200ms ease-out, per spec v2 §5) |
| Architecture diagram is single-zoom | Phase 2 introduces hover annotations on `/architecture` | Phase 2 work, not Phase 1 |

These are all intentionally deferred. The static reference establishes structure and visual treatment; behavior is engineering's domain.

---

## Sign-off checklist (Phase 1.5)

Before declaring this reference locked and handing off to engineering:

- [ ] Opens in Chrome / Edge / Firefox / Safari without console errors
- [ ] Renders cleanly at 375 / 768 / 1280 / 1920 widths
- [ ] All 9 sections present in spec-v2 order
- [ ] Every text block is verbatim from homepage-spec-v2 §3.x
- [ ] Architecture diagram renders inline in section 3
- [ ] All 5 hardware products named in section 4
- [ ] All 3 audience cards present
- [ ] Primary CTA "Book a discovery call" appears in nav + hero + section 9
- [ ] Footer has 5 columns + legal strip
- [ ] Skip-to-content link works (tab from page load)
- [ ] Focus rings visible on keyboard navigation
- [ ] `prefers-reduced-motion: reduce` disables transitions (test in DevTools)
- [ ] No raw hex values in `styles.css` (all colors resolve to CSS custom properties)
- [ ] No Angular Material patterns (no ripples, no card-elevation hovers, no rounded-pill buttons)
- [ ] design-governance-v1 §2.x discipline rules visibly applied

When all boxes are ✓, the reference is ready to hand off to engineering.

---

## How to update this reference

The static reference is downstream of the homepage spec. If you find something that needs to change:

1. **First check `homepage-spec-v2.md`** — is the spec already specifying the right behavior, and the reference is just out of sync? Then update the reference to match.
2. **If the spec itself is wrong** — propose a homepage-spec v3 amendment first. Update the spec, get user sign-off, then update this reference.
3. **Do not silently change the reference.** Drift here cascades to engineering.

The reference is the second-most-canonical artifact after the spec. Treat it accordingly.

---

*Static HTML/CSS Reference v1, 2026-05-26. Phase 1.5 deliverable of the Elpis Digital Experience Platform. Ready for Angular team handoff once the sign-off checklist above is green.*
