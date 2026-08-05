<!--
File:        docs/marketing/design-governance-v1.md
Purpose:     The fourth governance track for the Elpis ecosystem — Design
             Governance. Sits alongside brand governance (BRAND_TOKENS.md),
             roadmap governance (web-platform-roadmap-v*.md), and
             architecture governance (ARCHITECTURE_BLUEPRINT.md, decisions/).

             Where BRAND_TOKENS.md says what the visual values ARE, and
             design-system-v*.md says what the components ARE, this document
             says HOW DESIGN DECISIONS GET MADE — what is allowed, what is
             forbidden, who decides, how exceptions happen, how drift is
             detected.

Audience:    Internal — Claude (governance enforcer), user (governance owner),
             engineering team (governance subject), future design hires
             (governance inheritor).

Format:      Markdown governance memo. Companion to but smaller than
             ARCHITECTURE_BLUEPRINT.md — design governance is meant to be
             memorizable, not encyclopedic.

Companion:   digital-experience-platform-strategy-v2.md (the WHY)
             web-platform-roadmap-v2.md (the WHEN)
             design-system-v2.md (the WHAT — components)
             BRAND_TOKENS.md (the WHAT — visual values)
             assets/brand/brand-tokens.css (the IMPLEMENTATION)

Version:     v1
Date:        2026-05-26
Status:      DRAFT — introduced after Pass 1 review pass at user direction:
             "you should also introduce design governance — once Angular
             implementation begins, design drift becomes the biggest risk."

This document closes the missing governance gap. Once locked, every
implementation choice that touches visual surface area is governed against
the principles below.
-->

# Elpis Design Governance — v1

**The fourth governance track. Brand says what; design system says how to compose; this says how decisions get made.**

Once Angular implementation begins, design drift is the single largest risk to premium-industrial positioning. Every page added without governance is a chance to leak SaaS aesthetics, introduce a stray accent color, ship a shadow that wasn't there yesterday, or compress a section padding because it "looked tight on this screen." Design governance is the discipline that prevents that drift.

This document is short on purpose. It is meant to be memorized, not referenced under stress.

---

## 1. The four tracks (where this fits)

| Track | Lives in | Owner | Governs |
|---|---|---|---|
| **Brand Governance** | `BRAND_TOKENS.md` | User-signed | Colors, typography, logo, contrast, the singular accent rule |
| **Architecture Governance** | `ARCHITECTURE_BLUEPRINT.md`, `docs/decisions/` | User-locked | Product architecture, ADRs, locked technical decisions |
| **Roadmap Governance** | `web-platform-roadmap-v*.md` | User-signed | Phasing, scope per phase, what stays OUT |
| **Design Governance** (this doc) | `design-governance-v1.md` | User-signed | Spacing, motion, illustration, interaction, responsive, visual hierarchy — how design stays disciplined as the surface scales |

The four are peers. None subordinates another. A design decision that conflicts with brand governance is a brand-governance violation, not a design-governance question. A scope decision that conflicts with the roadmap is a roadmap-governance issue.

When a decision is ambiguous, the test is which track it primarily affects. Visual feel and discipline → design. Color hex or token addition → brand. New page / new feature scope → roadmap. New product capability → architecture.

---

## 2. The six discipline areas

Six dimensions, each with a hard rule and an escalation path.

### 2.1 Spacing discipline

**Rule:** Every margin, padding, and gap value resolves to a token in `brand-tokens.css` (the 4px-base scale: `--space-1` through `--space-48`).

**Forbidden:** raw pixel values inside components (e.g. `padding: 22px;`). Inline magic numbers. "Eyeball-tightened" spacing that doesn't snap to the scale.

**Allowed exception:** components may use sub-token math for visual centering inside fixed-size primitives (e.g., the optical baseline shift inside an icon). Escalation: design discussion before introducing.

**Drift signal:** a code review showing more than three raw-pixel spacing values across two components in the same week.

### 2.2 Motion discipline

**Rule:** Three durations only — `--motion-fast` (120ms), `--motion-default` (180ms), `--motion-slow` (280ms). No motion lives longer than 320ms anywhere on the marketing surface. `prefers-reduced-motion: reduce` is respected globally.

**Forbidden:** spring physics, bouncing transitions, parallax, animated gradients, type-on effects, infinite-loop animations, decorative background motion, scroll-jacked animations, "skeuomorphic" hover lifts.

**Allowed exception:** the architecture diagram's Phase 2 hover annotations may exceed 180ms if (and only if) the interaction is intentional discovery rather than ambient decoration. Escalation: explicit design + brand sign-off.

**Drift signal:** any motion longer than 320ms shipping to production. Any new easing curve introduced.

### 2.3 Illustration discipline

**Rule:** In Phase 1, no decorative imagery on the marketing surface. Geometric illustration only, drawn from a curated icon set (recommendation: Tabler Icons, OFL-licensed). Real shop-floor photography is reserved for Phase 3 customer-story surfaces.

**Forbidden:** stock photography (handshakes, smiling operators, aerial assembly lines, hooded "hacker" silhouettes, generic factory shots). Decorative SVG backgrounds. AI-generated imagery. "Industrial-themed" stock illustrations (those gear-and-cog vector packs).

**Allowed exception:** real Elpis hardware product photography enters in Phase 3 — on hardware product pages only, never decoratively on the homepage.

**Drift signal:** any stock-photo URL referenced from a marketing page. Any "Industry 4.0 abstract" SVG illustration committed.

### 2.4 Interaction discipline

**Rule:** Brand teal is the only general accent. Hover states change accent-line position or accent-color brightness — never card elevation, never shadow. Buttons use 4px radius (angular industrial), never pill shape. Focus rings are always visible at 2px brand-teal outline + 2px offset for keyboard users.

**Forbidden:** ripple effects, card lift-on-hover with drop shadow, gradient backgrounds, glassmorphism, neumorphism, animated "shine" overlays, hover scale transforms, magnetic-cursor effects.

**Allowed exception:** the five-pillar restrained tonal palette (`--color-pillar-1` through `--color-pillar-5`) inside `CapabilityCard.accent` and `ProofBand` pillar markers — used only as wayfinding, never decoratively.

**Drift signal:** any `box-shadow` rule that isn't a focus ring. Any color reference outside the token vocabulary. Any second-accent color introduced.

### 2.5 Responsive discipline

**Rule:** Mobile-first. Breakpoint tokens locked at sm (640), md (768), lg (1024), xl (1280), 2xl (1536). Section padding scales: tight (48/64/80), default (64/96/128), loose (96/128/192) — mobile / tablet / desktop. No layouts that demand horizontal scroll on any breakpoint between 375 and 2560.

**Forbidden:** desktop-first layouts that hide content on mobile. "Mobile version" of the homepage that loses entire sections. Carousels as a way to "fit more content on mobile" (per anti-pattern lock). Pinch-to-zoom required for legibility.

**Allowed exception:** the architecture diagram on mobile — Phase 1 replaces the master diagram with the 3-box simple variant on screens under 768. Phase 2 may introduce the horizontally-swipeable master diagram as a progressive enhancement.

**Drift signal:** any page that fails Lighthouse mobile (any score below 90). Any breakpoint introduced outside the locked five.

### 2.6 Visual hierarchy discipline

**Rule:** Headings honor the locked size scale (BRAND_TOKENS §3.3, `--size-xs` through `--size-4xl`). Eyebrow labels are size.sm + semibold + small-caps + letter-spaced 0.18em — always. Hero headlines are size.3xl + tightened tracking -0.01em — always. Container max widths are `--container-narrow` (960), `--container-default` (1280), or `--container-wide` (1440) — no custom widths.

**Forbidden:** custom font weights between regular/semibold/bold. Custom letter-spacing values not in the four locked tracking tokens. Container widths outside the three locks. Inline `<style>` blocks for one-off type adjustments.

**Allowed exception:** the architecture diagram caption is 22pt italic per `architecture-diagram-spec-v3.md §4.2`. Locked exception, no further deviations.

**Drift signal:** any heading rendered at a size outside the scale. Any `font-weight` value other than 400/600/700.

---

## 3. The escalation paths

When a designer or engineer hits an ambiguous moment, the escalation path is one of three things — in order, never skipped:

```
   ┌─────────────────┐
   │  1. Check the   │ ← Brand tokens, design system, this governance doc
   │     locks.      │   answer 95% of questions.
   └────────┬────────┘
            │  Locks don't resolve it
            ▼
   ┌─────────────────┐
   │  2. Pause and   │ ← Surface the tradeoff to the design owner.
   │     surface.    │   Never decide unilaterally on visual discipline.
   └────────┬────────┘
            │  Owner can't resolve quickly
            ▼
   ┌─────────────────┐
   │  3. Document    │ ← Write a one-paragraph governance memo proposing
   │     a refinement│   the change, attach to the relevant track's
   │     proposal.   │   v-next file.
   └─────────────────┘
```

**Never skip step 1.** The locks are the answer to most decisions. Skipping straight to "I'll just try this and see how it looks" is how SaaS-flavored aesthetics leak in.

**Never skip step 2.** The discipline of pausing is the discipline of premium positioning. Drift is silent. The pause is what catches it.

**Step 3 is rare.** Most refinement proposals come from user-led review passes, not from engineering surprise. When step 3 does fire, it produces a small versioned-bump doc, not a sweeping rewrite.

---

## 4. The drift detection signals

These are the leading indicators that governance is starting to fray. Each one triggers a design-track review.

| Signal | Likely cause | Response |
|---|---|---|
| Three or more raw-pixel spacing values across two components in a week | Engineering improvising under deadline | Audit + remind of token vocabulary |
| Any `box-shadow` rule outside focus rings | SaaS card-elevation pattern creeping in | Refactor to accent-line interaction |
| Motion duration over 320ms | Marketing-flair instinct | Compress to motion tokens |
| A new color hex anywhere | Brand discipline failure | Either add to BRAND_TOKENS or remove |
| Two button styles emerging | Component library is being forked | Consolidate into `Button` variants |
| A page that doesn't use `SectionShell` | Engineering bypassing the wrapper | Refactor through `SectionShell` |
| "Coming soon — Phase 2" nav items going stale | Phase 2 scope drifting | Roadmap-track conversation, not design |
| Stock photo URL committed | Illustration-discipline breach | Revert + remind |
| `MatButton` / `mat-*` selector in the codebase | Material aesthetic leaking | Remove with prejudice |

The point of listing these explicitly: a design owner reading PRs can quickly spot governance drift without re-reading the full design system every time.

---

## 5. The review cadence

Three cadences, in order of frequency:

| Cadence | Who | What |
|---|---|---|
| **Per-PR** | Engineering reviewer + design owner (Claude during this engagement; future hire after) | Spot-check the drift signals (§4) against the diff. Block PRs that breach discipline. |
| **Per-phase** | User + ChatGPT (review pass) + Claude | At Phase boundary: re-read this doc, audit shipped pages against principles, flag accumulated debt. |
| **Annual** | User + design owner | Full re-audit: does the governance still match Elpis's strategic position? Have new tracks emerged (motion, illustration, voice, etc.)? Should this doc bump to v2? |

Per-phase is the most important cadence. Each phase compounds — Phase 2 surfaces drift that Phase 1 introduced; Phase 3 surfaces drift Phase 2 introduced; left unchecked, by Phase 4 the DXP feels different from the homepage.

---

## 6. What this document is NOT

To prevent governance creep, here is what Design Governance v1 explicitly does NOT do:

- **Not the design system.** That's `design-system-v*.md`. This governs how the design system is enforced, not what's in it.
- **Not the brand tokens.** That's `BRAND_TOKENS.md`. This references those tokens but does not own them.
- **Not the copy guide.** Voice + tone discipline is owned by the marketing track (homepage-spec, manifesto v3). Design governance is silent on word choice.
- **Not the accessibility spec.** A11y is a hard floor, not a discipline — WCAG AA is binding, period. This doc references it but does not own it.
- **Not the performance budget.** Lighthouse ≥ 90 is a roadmap-governance / engineering-governance commitment. This doc references it but does not own it.

Keeping this doc narrow is itself a governance discipline.

---

## 7. The single test

When a design decision is in dispute, the test is:

> *"Would this decision, if applied across every page in Phases 1-4, still feel like premium industrial ecosystem positioning, or would it start to feel like enterprise SaaS marketing?"*

If the first: ship it.
If the second: don't ship it.
If unsure: pause and surface.

That sentence is the entire job of design governance.

---

## 8. Versioning and changes

- **Current version:** v1 (proposed, pending user sign-off)
- **Bump rule:** any change to the six discipline areas (§2), the escalation paths (§3), or the single test (§7) bumps the version
- **Asset-doc references** to this governance doc should pin to a version (`design-governance-v1`); when this doc bumps to v2, downstream docs may stay pinned to v1 until they consciously re-evaluate
- **No retroactive editing of v1 once signed off.** All evolution goes into v2.

---

## 9. Sign-off checklist (v1 lock)

- [ ] §1 four-track model (brand + architecture + roadmap + design) accepted as the governance structure
- [ ] §2 six discipline areas each have a clear rule, forbidden list, allowed exception, drift signal
- [ ] §3 escalation paths (check locks → pause and surface → propose refinement) feel actionable
- [ ] §4 drift signals are detectable in normal PR review
- [ ] §5 review cadence (per-PR + per-phase + annual) is realistic
- [ ] §7 single test crystallizes the governance into one memorable sentence
- [ ] This document is short enough to memorize, not encyclopedic

---

*Design Governance v1, 2026-05-26. DRAFT. Pending user + ChatGPT review. Once locked, this becomes the fourth governance track of the Elpis ecosystem alongside brand, architecture, and roadmap.*
