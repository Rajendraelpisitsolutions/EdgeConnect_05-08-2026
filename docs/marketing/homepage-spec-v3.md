<!--
File:        docs/marketing/homepage-spec-v3.md
Purpose:     Homepage v3 specification. Captures the locked decisions
             from the Phase 1.5 static-reference review pass:
               - Direction D-2 hero (composite: hardware + intelligence)
               - Section 1.5 trust band (customer logos)
               - Positioning amendment v4 referenced for the logo unlock
Audience:    Internal — Claude (governance enforcer), user + ChatGPT
             (reviewers), engineering team (Phase 1 Angular implementers).
Format:      Markdown spec. Every section is buildable from this document
             alone — no separate "copy doc" or "wireframe doc" needed.
Companion:   digital-experience-platform-strategy-v2.md (worldview)
             web-platform-roadmap-v2.md (4-phase plan)
             design-governance-v1.md (design discipline)
             design-system-v2.md (component library)
             positioning-amendment-v4.md (customer-name unlock — new)
             web/index.html (locked static reference implementation)
Version:     v3
Date:        2026-05-26
Status:      LOCKED after Phase 1.5 static-reference review pass.

v2 → v3 changes (in lockstep with the static-reference work):
  - §3.1 — Hero refined to Direction D-2 (composite of stylized
    EREMOS V2 dashboard + teal data-flow beam + mDAQ hardware product
    render + "ONE PLATFORM" caption). Replaces the previous cropped-
    diagram hero per static-reference review pass.
  - §3.1.5 — NEW Section 1.5 (trust band) added between Hero and
    Capabilities. 8 customer logos displayed in natural brand colors.
    Triggered by positioning amendment v4 (customer-name unlock).
  - §4 CTA hierarchy unchanged.
  - §5 motion language unchanged.
  - §6 visual mode unchanged.
  - §7 anti-patterns unchanged (Material lock, no stock photos, etc.).
  - §8 v1 open questions remain resolved per v2 (no further opens).
  - §9 components list extended — trust-band added.
  - §11 sign-off checklist updated for D-2 hero + trust band.

v1 (homepage-spec-v1.md) and v2 (homepage-spec-v2.md) retained as
historical reference. v3 is canonical going forward. The static
reference implementation at docs/marketing/web/ matches this spec
file-for-file.
-->

# Homepage v3 — Spec

**The first page of the Elpis Digital Experience Platform. Production-grade. Visible front door of the Industrial Intelligence Ecosystem worldview. Locked after Phase 1.5 static-reference review.**

This spec is buildable on its own. Every section is defined: copy verbatim, component references, visual treatment notes, CTAs, anti-patterns. The static HTML reference at `docs/marketing/web/index.html` matches this spec; the Angular implementation (Phase 1) consumes both.

Design governance applies throughout per `design-governance-v1.md`. Customer-logo usage is unlocked per `positioning-amendment-v4.md`.

---

## 1. Information architecture

### 1.1 Top-level navigation (LOCKED per user)

Seven items, left-to-right (unchanged from v2):

| Order | Label | Route | Phase live |
|---|---|---|---|
| 1 | Platform | `/platform` | Phase 2 (placeholder in Phase 1) |
| 2 | Capabilities | `/capabilities` | Phase 2 |
| 3 | Solutions | `/solutions` | Phase 2 |
| 4 | Industries | `/industries` | Phase 3 |
| 5 | Architecture | `/architecture` | Phase 2 (Phase 1 anchors to homepage section) |
| 6 | Resources | `/resources` | Phase 3 |
| 7 | Company | `/company` | Phase 2 |

Plus right-aligned primary CTA: *Book a discovery call*.

### 1.2 Footer IA (unchanged from v2)

Five columns: Brand · Platform · Solutions · Resources · Company. Legal strip below.

---

## 2. Page structure — sections at a glance (v3 — adds Section 1.5)

Ten sections, top to bottom. Dark hero, light scroll transitions starting at section 3.

| # | Section | Visual | Component(s) |
|---|---|---|---|
| **1**   | Hero (D-2 composite) | Dark-deep | `NavMegaMenu`, `SectionShell` (dark-deep), `CTAGroup`, hero composite SVG + img |
| **1.5** | **Trust band (NEW)** | Dark-deep | `TrustBand` (new component — see design-system addendum below) |
| **2**   | Five-pillar capability strip | Dark | `SectionShell` (dark), `CapabilityCard` × 5 |
| **3**   | Architecture deep-dive | Light | `SectionShell` (light), `ArchitecturePanel`, embedded diagram |
| **4**   | Hardware ecosystem | Light | `SectionShell` (light), `CapabilityCard` × 5 (hardware variant) |
| **5**   | EdgeConnect — the runtime backbone | Light tinted | `SectionShell` (light-tinted), `MetricStrip`, `DiagramFrame` |
| **6**   | EREMOS V2 — the intelligence layer | Light | `SectionShell` (light), `MetricStrip` |
| **7**   | Proof band — defense / space-agency / AMC | Dark deep | `ProofBand` (anonymized — unchanged from v2) |
| **8**   | Who it's for — audience cards | Light | `SectionShell` (light), `AudienceCard` × 3 |
| **9**   | CTA + Footer | Dark deep | `CTASection`, `Footer` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero (Direction D-2 — LOCKED)

**Visual:** dark background `bg.deep` (`#0F1419`). Top nav floats over it. Hero copy on the left (60% column), composite visual on the right (40% column). At desktop, both columns top-align so the hero eyebrow and the dashboard panel's eyebrow share a baseline.

**Hero copy (left column) — verbatim, unchanged from v2:**

```
PRE-LABEL  (small caps, brand.teal, letter-spaced)
INDUSTRIAL INTELLIGENCE ECOSYSTEM

HEADLINE  (size.3xl, semibold, text.heading, text-wrap balance)
From shop floor signal
to enterprise decision —
one industrial intelligence stack.

SUBHEAD  (size.md, regular, text.body, max-width 60ch)
Elpis combines edge connectivity, sensor-direct acquisition, condition
monitoring, and operational intelligence in a single ecosystem.
Brownfield-ready. Sensor-agnostic. Built end-to-end by Elpis —
or layered into what you already run.

PRIMARY CTA   (brand.teal background)   Book a discovery call →
SECONDARY CTA (outline)                  Download the platform datasheet (PDF)
TERTIARY      (text-only anchor)         ↓ See the architecture
```

**Trust micro-strip (under CTAs) — v3 update:**

> *Trusted across automotive, heavy manufacturing, energy, and defense · Deployed across India and the Middle East · AMC-partner ready*

(Replaces v2's "Trusted by defense and space-agency deployments" — broadens the credibility signal to match the customer roster now visible in Section 1.5.)

**Hero composite visual (right column) — Direction D-2, LOCKED.**

Vertical 3-piece composition, top-aligned with hero copy:

1. **Stylized EREMOS V2 intelligence dashboard panel** (top, ~460px max-width)
   - Glassy dark surface with teal border
   - Three KPI tiles:
     - `OEE — PLANT 03` · **87.2%** · ▲ 2.3 vs last shift
     - `ACTIVE ALARMS` · **4** · 2 acknowledged
     - `PLANTS ONLINE` · **12 / 12** · all healthy
   - Production-signal sparkline (last 24 hours, trending upward, subtle teal area fill)
   - Subtle perspective tilt (2° rotateX) for cinematic depth
   - Drop shadow stack + teal ambient halo

2. **Teal data-flow beam** (middle, ~100px tall)
   - Vertical line, gradient (dim at bottom → bright at top — signals upward flow)
   - Three progressively-larger "data packet" dots ascending
   - Upward arrow at top entering the dashboard
   - "DATA" micro-label
   - Static (no animation per design-governance §2.2)

3. **mDAQ hardware product render** (bottom, ~320px max-width)
   - Real PNG (`assets/hardware/mdaq.png`)
   - Layered drop shadows
   - Caption below: `mDAQ · industrial acquisition hardware`

**Caption (below composite, centered):**

```
EYEBROW (small caps, brand.teal): ONE PLATFORM
LINE    (size.sm italic):         Hardware that captures signal.
                                  Intelligence that turns it into decisions.
```

**Why D-2 (not D, A, or B):**

The user surfaced the concern that a hardware-only hero (Direction D) risks signaling "hardware company" rather than "Industrial Intelligence Ecosystem." D-2 resolves this by:
- Pairing real hardware (tangibility) with stylized software UI (intelligence)
- Teal data beam tells the platform story in one glance
- Caption explicitly says "ONE PLATFORM"
- Both visible, neither dominant

The dashboard panel is a *signifier*, not a real EREMOS V2 screenshot. Values (87.2%, 4 alarms, 12/12 plants) are abstract — they communicate "this is the software side" without claiming UI fidelity. Phase 3 may swap for real screenshots once EREMOS V2 ships.

**Mobile behavior:** the composite stacks vertically in a single column. The dashboard, beam, hardware, and caption display in the same vertical order, sized down. Hero copy appears above the composite per mobile-first responsive flow.

### 3.1.5 Section 1.5 — Trust band (NEW in v3 — LOCKED)

**Visual:** dark-deep background (`bg.deep`), full-bleed, narrow vertical band. Sits between Hero (Section 1) and the five-pillar capability strip (Section 2). Visual punctuation that establishes credibility immediately after the hero.

**Headline (caption, centered):**

```
TRUSTED BY INDUSTRIAL LEADERS ACROSS AUTOMOTIVE, ENERGY, HEAVY MANUFACTURING, AND DEFENSE
```

Small caps, letter-spaced 0.18em, text.muted.

**Logo row (8 logos, evenly spaced):**

| Industrial enterprises | E-IDOS sensor partners |
|---|---|
| GE · Hitachi · Toyota · Schneider Electric · BHEL · TVS | HYDAC · Filtrec |

**Logo treatment (LOCKED L2 — natural brand colors):**

- Each logo: 64px tall (48px on mobile), natural brand colors
- 92% opacity at rest, full opacity on hover
- Subtle hover scale (1.04×) for tactile feedback
- Source logos are circular badges with transparent corners — they render as recognizable coin/badge shapes on the dark band
- No background pills, no white cards — logos display directly

**Why this treatment vs the white-silhouette alternative:**

Brand recognition wins over single-accent purity in a trust band. The entire point of the band is customer recognition; muting the colors defeats it. Design-governance §2.4 single-accent discipline gets a scoped exception here — customer brand colors are *informational*, not decorative.

**Authorization:** see `positioning-amendment-v4.md` for the specific list of customer names authorized under Phase 1, and the limits (specific deployment stories remain anonymized).

**Component:** new `TrustBand` component (see design-system addendum below).

### 3.2 – 3.9 Sections 2 through 9 (unchanged from v2)

All section copy, structure, and treatment unchanged from `homepage-spec-v2.md` §3.2–§3.9. See v2 for verbatim copy.

The proof band (Section 7) remains anonymized per positioning v3 §4 + v4 §5 — customer logos are unlocked, but specific deployment stories stay anonymized until Phase 3.

---

## 4. CTA hierarchy (unchanged from v2)

Primary: *Book a discovery call* · Secondary: *Download the platform datasheet (PDF)* · Tertiary: *See the architecture*

---

## 5. Motion language (unchanged from v2, per design-governance §2.2)

`motion.fast` (120ms) · `motion.default` (180ms) · `motion.slow` (280ms). No motion > 320ms. `prefers-reduced-motion: reduce` honored globally.

The hero composite is fully static — no infinite-loop animations on the data-flow beam, no count-up on KPI values, no spark-line animation. Premium-industrial = stillness.

---

## 6. Visual mode (unchanged from v2)

Dark hero (1, 1.5) → light scroll (3, 4, 5, 6) → dark proof band (7) → light audience (8) → dark CTA/footer (9). Visual rhythm pattern locked.

---

## 7. Anti-patterns (v3 — adds two)

In addition to v2 anti-patterns:

| Don't | Why |
|---|---|
| Use customer-brand colors anywhere outside the trust band | The trust band is the scoped exception per §3.1.5. Brand-teal stays the only general accent. |
| Pair customer logos with specific deployment stories in Phase 1 | Positioning v4 §2 — names unlocked, stories still anonymized. Phase 3 introduces customer-story sign-off. |

---

## 8. v1 open questions — resolved status (unchanged from v2)

All 7 open questions remain resolved per v2 §8 defaults. No new opens in v3.

---

## 9. Components referenced (v3 — adds TrustBand)

| Component | Used in |
|---|---|
| `NavMegaMenu` | Top of page (sticky) |
| `SectionShell` (4 mode variants) | Every section |
| `CTAGroup` | Hero, CTA section |
| `CapabilityCard` (dark + light + pillar accents) | Capabilities, Hardware, Audience variants |
| `HeroComposite` (NEW) | Hero (§3.1) — dashboard SVG + beam SVG + hardware img + caption |
| **`TrustBand` (NEW)** | **Section 1.5 (§3.1.5)** |
| `ArchitecturePanel` | Architecture (§3.3) |
| `DiagramFrame` | EdgeConnect (§3.5) |
| `MetricStrip` | EdgeConnect (§3.5), EREMOS V2 (§3.6) |
| `ProofBand` | Proof section (§3.7) |
| `CTASection` | Section 9 |
| `Footer` | Page footer |
| `Button` | All CTAs |

Two new components introduced (`HeroComposite`, `TrustBand`) — both Phase 1 deliverables. Design-system v2 should be amended to v3 to formally define them; until then, the static reference at `web/index.html` + this spec section are sufficient definition for the Angular team.

---

## 10. Sign-off checklist (v3 lock)

**IA + nav (unchanged from v2):**
- [x] 7-nav order locked
- [x] Footer 5-column structure
- [x] Primary CTA "Book a discovery call" everywhere

**Hero (D-2):**
- [x] Hero composite has dashboard panel + data-flow beam + hardware product + caption
- [x] Dashboard panel renders as stylized EREMOS V2 (not real screenshot)
- [x] Data-flow beam is static (no animation)
- [x] mDAQ render used (not Edge Gateway / E-IDOS / mTracker — those reserved for Phase 3 product pages)
- [x] Caption eyebrow = "ONE PLATFORM" (not "BUILT BY ELPIS" — would re-signal hardware identity)
- [x] Hero columns top-align at desktop
- [x] Trust micro-strip rewording: "Trusted across automotive, heavy manufacturing, energy, and defense ..."

**Trust band (Section 1.5, NEW):**
- [x] 8 logos selected per §3.1.5
- [x] Natural brand colors (L2 treatment)
- [x] Caption: "TRUSTED BY INDUSTRIAL LEADERS ACROSS ..."
- [x] Dark-deep background, no card-elevation
- [x] HYDAC + Filtrec inclusion ties to E-IDOS sensor-ecosystem (already cited in positioning v3 §3.4)
- [x] Per-logo authorization confirmed via positioning-amendment-v4.md §3

**Anti-pattern compliance (design-governance):**
- [x] Customer brand colors contained to trust band only
- [x] No specific customer + specific deployment pairings (positioning v4 §2 limit honored)
- [x] No stock photography
- [x] No Material aesthetics
- [x] No motion > 320ms (composite is fully static)

**Static reference:**
- [x] `web/index.html` matches this spec
- [x] `web/styles.css` honors all motion / spacing / token rules
- [x] `web/assets/` self-contained (no external resource dependencies except Google Fonts)

---

## 11. What comes next (after v3 lock)

1. **Angular implementation begins** — engineering team consumes this spec + `web/index.html` + `design-system-v2.md` + `brand-tokens.css`. They build the production `elpis-global-web` Angular app.
2. **`design-system-v3.md` (deferred)** — formalize the `HeroComposite` and `TrustBand` components. Can be written before or alongside the Angular implementation.
3. **Phase 2 scope review** — once Phase 1 ships, the next-page-up (Architecture deep-dive, Platform, Capabilities) consumes the same component library.

---

*Homepage v3 spec, 2026-05-26. LOCKED after Phase 1.5 static-reference review. Direction D-2 (composite hero) + Section 1.5 trust band + L2 natural-color logos all locked. Supersedes v2 as the canonical homepage spec. Static reference at `docs/marketing/web/` matches this spec exactly.*
