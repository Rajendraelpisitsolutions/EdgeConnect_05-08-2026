<!--
File:        docs/sessions/2026-05-26-phase-d-engineering-handoff.md
Purpose:     Phase 1 Angular engineering handoff brief. Consolidates every
             artifact the Angular team needs to begin the production
             elpis-global-web implementation. One document, no chase.
Audience:    Angular team (Phase 1 implementers), Claude (future sessions)
Date:        2026-05-26
Status:      LOCKED — accompanies Phase 1.5 static-reference lock.
-->

# Elpis DXP — Phase 1 Engineering Handoff
**Angular team briefing for `elpis-global-web` Phase 1**

---

## What you are building

The Elpis Digital Experience Platform homepage — a standalone Angular 19 application (`elpis-global-web`) deployed at `www.elpisitsolutions.com`. It is **not** part of EREMOS V2. It is not a microsite. It is the permanent, production-grade front door of the Industrial Intelligence Ecosystem.

**Phase 1 scope: homepage only.** One route, 9 sections. Full-stack Angular 19 with SSR/SSG, Lighthouse ≥ 90, WCAG AA.

---

## Canonical artifacts (read these first)

| Artifact | Location | Role |
|---|---|---|
| **Homepage spec v3** | `docs/marketing/homepage-spec-v3.md` | Source of truth for every section, every word, every visual decision. Start here. |
| **Static HTML/CSS reference** | `docs/marketing/web/index.html` + `styles.css` | Pixel-accurate, browser-renderable reference implementation. The Angular output should match this visually. |
| **Design Governance v1** | `docs/marketing/design-governance-v1.md` | 6 discipline areas that apply per-PR. Anti-patterns, drift signals, review protocol. |
| **Design System v2** | `docs/marketing/design-system-v2.md` | Component library definition — all Angular components to be built derive from this. |
| **Brand Tokens** | `docs/marketing/assets/brand-tokens.css` | All CSS custom properties. Import globally; never hardcode hex. |
| **DXP Strategy v2** | `docs/marketing/digital-experience-platform-strategy-v2.md` | Architecture decisions — why Angular 19, why SSR/SSG, why separate app, shared-tokens boundary. |
| **Web Platform Roadmap v2** | `docs/marketing/web-platform-roadmap-v2.md` | 4-phase plan. Phase 1 scope and "not in scope" list. |
| **Positioning Amendment v4** | `docs/marketing/positioning-amendment-v4.md` | Customer-logo authorization. Which logos are cleared, which aren't, what's still locked. |

---

## How to consume the static reference

```bash
# Option 1 — double-click (file://)
start docs/marketing/web/index.html

# Option 2 — local HTTP server (recommended for Lighthouse)
cd docs/marketing/web
python -m http.server 8080
# → http://localhost:8080/
```

Open `index.html` and `homepage-spec-v3.md` side-by-side. The spec is the source of truth; the HTML is the visual translation. When Angular drifts, the static reference wins.

---

## Component build order

Build in this sequence — lower components compose from higher ones:

1. **`Button`** — `.btn`, `.btn--primary`, `.btn--secondary`, `.btn--lg`, `.btn--ghost`
2. **`SectionShell`** — 4 mode variants: `dark-deep`, `dark`, `light`, `light-tinted` via `[data-mode]`
3. **`NavMegaMenu`** — sticky nav, 7 items + right-aligned CTA. Mobile overlay deferred (Phase 2 mega-menu content).
4. **`CTAGroup`** — primary / secondary / tertiary CTA composition
5. **`HeroComposite`** — D-2 composition: dashboard SVG + beam SVG + mDAQ PNG + caption
6. **`TrustBand`** — 8 logos, natural brand colors, 64px height, 92% opacity, 1.04× scale hover
7. **`CapabilityCard`** — dark variant (Section 2) + light variant (Section 4 hardware) + pillar accent system (5 teal shades)
8. **`ArchitecturePanel`** — diagram + annotation overlay
9. **`MetricStrip`** + **`DiagramFrame`** — depth sections (EdgeConnect §3.5, EREMOS V2 §3.6)
10. **`ProofBand`** — anonymized deployment anchors
11. **`AudienceCard`** × 3
12. **`CTASection`** — full-bleed dark-deep CTA panel
13. **`Footer`** — 5-column grid + legal strip

Full HTML-to-component mapping in `docs/marketing/web/README.md` §"Mapping to Angular components."

---

## Brand tokens — how to wire up

```typescript
// angular.json (global styles)
"styles": [
  "src/styles.css",
  "docs/marketing/assets/brand-tokens.css"   // ← import once, globally
]
```

Do **not** copy the token file — reference it. All component styles use `var(--color-brand-teal)`, `var(--spacing-16)`, etc. No hardcoded hex anywhere in components.

Tailwind config snippet is at the bottom of `brand-tokens.css`.

---

## Assets — what exists, where it lives

```
docs/marketing/web/assets/
├── brand/
│   ├── elpis-logo-nav.png          ← nav logo (1068×165, tagline cropped, 24/28px rendered)
│   └── elpis-logo-transparent.png  ← footer logo (1068×260, full with tagline, 56px rendered)
├── hardware/
│   ├── mdaq.png                    ← D-2 hero composite (bottom piece)
│   ├── mdaq-alt.png
│   ├── mtracker.png
│   ├── edge-gateway.png
│   └── e-idos.png
├── customers/
│   ├── ge.png · hitachi.png · toyota.png · schneider.png
│   ├── bhel.png · tvs.png · hydac.png · filtrec.png
│   └── wipro.png · riverway.png · uas-bangalore.png · software-toolbox.png
├── architecture-diagram-v2-light.svg   ← inline in Section 3
├── architecture-diagram-v2-simple.svg  ← reserved (mobile / simplified use)
└── architecture-diagram-v2-dark@2x.png ← "See architecture in detail" link target
```

The `web/assets/` copies are **self-contained copies** of the canonical files in `docs/marketing/assets/`. For the Angular production app, serve from the Angular project's `assets/` folder. Do not use `../` relative paths from the component layer.

**Logo note on nav:** `elpis-logo-nav.png` is a programmatically-cropped variant (tagline removed). The Phase 1 production TODO is to generate a white-tagline knockout from the `.ai` source (see `brand-tokens.css` §4.1) — that is the long-term correct asset for dark backgrounds. The cropped PNG is an acceptable interim.

---

## Locked decisions — do not relitigate

| Area | Decision |
|---|---|
| Framework | Angular 19 + SSR/SSG (no EREMOS V2 integration, no shared runtime) |
| Styling | CSS custom properties from `brand-tokens.css` + Tailwind reading those variables |
| Component system | Custom tokenized components. **Never Angular Material aesthetics** (no ripples, no card-elevation hovers, no rounded-pill buttons — permanent lock) |
| Motion ceiling | 320ms max. No infinite-loop animations. `prefers-reduced-motion: reduce` honored globally. |
| Color rule | Brand teal is the only general accent. Customer brand colors are scoped to the trust band only. |
| Logo treatment | Trust band: natural brand colors (L2), no `filter: brightness(0) invert(1)`. |
| Customer names | 8 logos authorized (per positioning-amendment-v4 §3). No specific deployment stories. Defense/space-agency names stay anonymized. |
| Architecture diagram | No customer logos in the diagram. Protocol/product-focused only. |
| Hero composite | Direction D-2 locked: dashboard panel SVG + data-flow beam SVG + mDAQ PNG. Static — no animation on beam or KPI values. |

---

## Phase 1 exit criteria

From `docs/marketing/web-platform-roadmap-v2.md` Phase 1:

- [ ] Homepage renders at 375 / 768 / 1280 / 1920 px widths
- [ ] Lighthouse Performance ≥ 90 (desktop + mobile)
- [ ] WCAG AA (skip-to-content, semantic HTML, visible focus rings, color contrast)
- [ ] `prefers-reduced-motion: reduce` disables all transitions
- [ ] No Angular Material patterns visible
- [ ] All copy verbatim from `homepage-spec-v3.md`
- [ ] Primary CTA "Book a discovery call" in nav + hero + Section 9
- [ ] Footer 5-column structure + legal strip
- [ ] No raw hex values in component styles (all via CSS custom properties)
- [ ] Architecture diagram renders inline in Section 3

---

## What Phase 1 is NOT

From `web-platform-roadmap-v2.md` §1.2:

- CMS integration (deferred to Phase 2)
- Sub-pages (`/platform`, `/capabilities`, etc. — placeholder routes only in Phase 1)
- Backend contact form (Phase 2 — use `mailto:` in Phase 1)
- Analytics (Phase 2 — add Plausible/GA4 when wiring backend)
- Localization (Phase 3)
- Partner workflows / ROI calculators / community (Phase 3/4)

---

## Questions?

- **Visual/copy question** → `homepage-spec-v3.md` is the answer.
- **Component structure question** → `design-system-v2.md`.
- **Why a specific design decision was made** → `design-governance-v1.md` + `digital-experience-platform-strategy-v2.md`.
- **Which customers are authorized** → `positioning-amendment-v4.md`.
- **Phase 1 scope boundary question** → `web-platform-roadmap-v2.md` §1.2.

---

*Phase 1 Engineering Handoff, 2026-05-26. Static reference locked. Angular implementation begins from this document.*
