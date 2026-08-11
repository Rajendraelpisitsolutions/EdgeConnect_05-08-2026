<!--
File:        docs/sessions/2026-05-26-phase-d-handoff.md
Purpose:     Session handoff for Phase D (DXP homepage + Phase 1.5 static
             reference). Captures locked decisions, what shipped, and what
             the next session should know before picking up.
Audience:    Claude (next session), user
Date:        2026-05-26
-->

# Session Handoff — Phase D (DXP Homepage + Phase 1.5 Static Reference)
**2026-05-26**

---

## What shipped this session

### Phase C (architecture diagram variants) — completed, committed to PR #35
- Architecture diagram v2 rev3 (peer architecture: EdgeConnect + Acquisition stacked as peers, both feeding EREMOS V2) locked in SVG
- 4 derived variants: light (presentation), 16:9 slide, 3-box executive summary, multicolor poster
- PNG fallbacks at 2× via PyMuPDF (linearGradient replaced with solid fills — PyMuPDF limitation)
- `docs/marketing/architecture-spec-v3.md` locked

### Phase D (DXP strategy + governance) — fully locked
- `docs/marketing/digital-experience-platform-strategy-v2.md` — DXP framing, Angular 19 decision, shared-tokens boundary
- `docs/marketing/web-platform-roadmap-v2.md` — 4-phase plan with explicit Phase 1 scope and "not in scope"
- `docs/marketing/design-governance-v1.md` — 4th governance track, 6 discipline areas, motion ceiling (320ms), drift signals
- `docs/marketing/design-system-v2.md` — component library including new `HeroComposite` and `TrustBand`

### Phase 1.5 (static HTML/CSS reference) — fully locked
- `docs/marketing/web/index.html` — complete 9-section homepage (+ Section 1.5 trust band)
- `docs/marketing/web/styles.css` — full visual system, all values via CSS custom properties
- `docs/marketing/web/README.md` — how to view, consume, and update the reference
- `docs/marketing/homepage-spec-v3.md` — canonical homepage spec, Direction D-2 hero + trust band locked
- `docs/marketing/positioning-amendment-v4.md` — customer-name unlock (reverses v3 §4)
- `docs/marketing/assets/brand/elpis-logo-nav.png` — cropped nav logo (tagline removed, 1068×165)
- `docs/marketing/web/assets/` — self-contained asset copies (hardware PNGs, customer logos, brand logos, diagram files)

### PR stack opened and merged
- PR #34: `claude/marketing-positioning-amendment` → positioning v1→v3 + amendment v4
- PR #35: `claude/marketing-design-arch-svg-v2` → architecture diagram v2 + spec v3 + Phase C variants
- PR #36: `claude/marketing-design-homepage-v1` → full DXP homepage (Phase D + Phase 1.5)
- All three merged to master in order: #34 → #35 → #36

---

## Key locked decisions (do not relitigate)

### DXP Architecture
- `elpis-global-web` is a **standalone Angular 19 app** — never inside EREMOS V2 UI, never a microsite
- Angular 19 + SSR/SSG + Tailwind reading CSS custom properties from `brand-tokens.css`
- **Angular Material aesthetics permanently banned** — no ripples, no card-elevation hovers, no rounded-pill buttons

### Design Governance
- Motion ceiling: **320ms max**. No infinite-loop animations anywhere.
- Brand teal is the **only general accent**. Customer brand colors are scoped to trust band only.
- No raw hex values in components — all colors via CSS custom properties.
- Single test for every design decision: "Does this feel like premium industrial ecosystem positioning or generic enterprise SaaS marketing?"

### Hero — Direction D-2 (LOCKED)
- Composite: stylized EREMOS V2 dashboard panel SVG + teal data-flow beam SVG + mDAQ product PNG
- Caption eyebrow: "ONE PLATFORM" (not "BUILT BY ELPIS" — would re-signal hardware identity)
- Fully **static** — no animation on beam, no count-up on KPI values, no sparkline motion
- Dashboard values are abstract signifiers (87.2% OEE, 4 alarms, 12/12 plants) — not real EREMOS V2 screenshots
- Both columns top-align at desktop (`align-items: start` on `.hero__inner`)

### Trust Band — Section 1.5 (LOCKED)
- 8 logos: GE · Hitachi · Toyota · Schneider Electric · BHEL · TVS · HYDAC · Filtrec
- **L2 treatment**: natural brand colors, no `filter: brightness(0) invert(1)`, 64px height, 92% opacity
- Authorized per `positioning-amendment-v4.md` §3 (all publicly displayed on `www.elpisitsolutions.com`)

### Customer name rules (positioning amendment v4)
- Named customer logos: **authorized** for Phase 1 (trust band, datasheet, pitch deck)
- Specific deployment stories: **locked until Phase 3** customer-story sign-off
- Defense / space-agency customer names: **permanently anonymized** (not in authorized list)
- AMC partner names: **anonymized** until Phase 4 partner portal

### Logo treatment
- **Nav**: `elpis-logo-nav.png` — cropped (no tagline), 24px mobile / 28px desktop
  - **Phase 1 production TODO**: generate white-tagline knockout from `.ai` source (see `brand-tokens.css` §4.1)
  - Tagline #606060 fails WCAG AA on dark backgrounds — cropped PNG is an acceptable interim
- **Footer**: `elpis-logo-transparent.png` — full logo with tagline, 56px rendered height

---

## Mobile overflow fixes (reference for Phase 1 Angular)

Root causes found and resolved in static reference:
1. `.btn { white-space: nowrap }` — forced wide buttons to overflow. Fixed: `white-space: normal` by default, `nowrap` only at 640px+
2. `<br>` tags in hero headline — forced horizontal width. Removed; relies on `text-wrap: balance`
3. Grid children inheriting `min-width: auto` — caused overflow in card grids. Fixed: `min-width: 0` on all grid wrappers
4. `html, body { overflow-x: clip }` — belt-and-suspenders containment

---

## What comes next (Phase 1 Angular implementation)

1. **Angular team begins** — consumes `homepage-spec-v3.md` + `web/index.html` + `design-system-v2.md` + `brand-tokens.css`
2. **`design-system-v3.md`** — formalize `HeroComposite` and `TrustBand` components (can happen alongside Angular build)
3. **White-tagline knockout** — generate from `.ai` source; replaces cropped nav logo in Angular production assets
4. **Phase 2 scope review** — after Phase 1 ships: Architecture deep-dive, Platform, Capabilities pages

---

## File locations

All locked marketing artifacts live under `docs/marketing/`. Summary:

```
docs/marketing/
├── homepage-spec-v3.md                          ← canonical homepage spec (LOCKED)
├── positioning-amendment-v4.md                  ← customer-name unlock (LOCKED)
├── digital-experience-platform-strategy-v2.md   ← DXP strategy (LOCKED)
├── web-platform-roadmap-v2.md                   ← 4-phase plan (LOCKED)
├── design-governance-v1.md                      ← design discipline (LOCKED)
├── design-system-v2.md                          ← component library (LOCKED)
├── architecture-spec-v3.md                      ← architecture diagram spec (LOCKED)
├── assets/
│   ├── brand-tokens.css                         ← CSS custom properties
│   ├── brand/
│   │   ├── elpis-logo-nav.png                   ← nav logo (cropped, no tagline)
│   │   └── elpis-logo-transparent.png           ← footer logo (full)
│   ├── architecture-diagram-v2-*.svg/png        ← canonical diagram files
│   └── hardware/                                ← product renders
└── web/
    ├── index.html                               ← static reference (LOCKED)
    ├── styles.css                               ← visual system CSS (LOCKED)
    ├── README.md                                ← how to use the reference
    └── assets/                                  ← self-contained copies for file:// viewing
```

---

## In-flight / carry-forward

| Item | Status | Note |
|---|---|---|
| White-tagline knockout for nav logo | Carry-forward | Needs `.ai` source + export. Acceptable interim: cropped PNG. |
| `design-system-v3.md` | Carry-forward | Formalizes `HeroComposite` + `TrustBand`. Not blocking Angular Phase 1 — spec + static ref are sufficient. |
| PR #33 (datasheet), #32 (pitch deck), #31 (brand tokens + arch v1), #30 (handoff), #17 (chips recovery), #28 (M.2d.2) | Still open | These predate Phase D and are not related. Handle in separate sessions. |

---

*Session handoff, 2026-05-26. Phase D complete. Static reference locked. PR stack #34 → #35 → #36 merged to master. Angular team ready for Phase 1 implementation.*
