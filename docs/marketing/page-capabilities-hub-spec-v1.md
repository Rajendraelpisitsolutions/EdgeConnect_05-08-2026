<!--
File:        docs/marketing/page-capabilities-hub-spec-v1.md
Purpose:     Page spec for /capabilities — the capability-first
             navigation entry point. First of 10 Phase 2 per-page specs.
             Locks the pillar-at-a-glance pattern that downstream pillar
             pages and other hub pages echo.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), future page-spec authors.
Format:      Markdown page spec. Same structure every Phase 2 per-page
             spec uses: page purpose, IA + buyer alignment, page
             structure overview, section-by-section detail with
             verbatim copy, components used, anti-patterns, sign-off
             checklist, out of scope.
Companion:   phase2-ia-scope-memo-v2.md + amendment v3 (IA parent —
                §3.2 locks /capabilities scope; §2 WHY framing; §4
                anti-duplication map; §5.2 cross-lens; §6 sequencing)
             buyer-taxonomy-v1.md (primary buyer = OT Architect;
                secondary = Plant engineer; CTA preferences locked)
             proof-architecture-v1.md (proof discipline applied —
                no customer logos here, no trust band, no outcome
                metrics, no compliance claims)
             design-system-v3.md (CapabilityCard pillar variants,
                SectionShell, Button, §17 cross-lens content pattern)
             hardware-ecosystem-map-v3.md §1 (the 5 pillars +
                customer questions, verbatim source for pillar copy)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview)
Version:     v1 — LOCKED after Pass 1 user + ChatGPT review
Date:        2026-05-28
Status:      LOCKED.

Pass 1 ChatGPT review verdict (2026-05-28): "Very strong first page
spec. Successfully establishes the template for the remaining Phase 2
pages. Approve as-is."

Specific approvals from review (no refinements requested):
  - Section structure (hero → 5-pillar grid → compose → cross-lens → CTA)
    "exactly right for a capability hub"
  - 370-word page copy length: appropriately scan-friendly for OT
    Architect directory consumption
  - OT Architect targeting via the customer-question structure (verbatim
    from hardware-ecosystem-map v3 §1) "extremely effective"
  - CTA pairing (Talk to an engineer + Request an architecture review)
    "much better than Book a demo / Contact sales / Request a quote
    for this audience"
  - Cross-lens placement before final CTA "exactly where it belongs"
  - §3 single-paragraph "How the pillars compose" bridge — "the right
    length; quietly solves a very hard IA problem here"
  - Page obeys proof-architecture v1 discipline (no customer logos,
    no outcome metrics, no compliance claims)

Meta-decision from review: the §1-§8 spec template structure becomes
CANONICAL for all 10 Phase 2 per-page specs. See new §9 below for the
template-canonicalization governance.

Post-lock additions (propagated back into §9 governance, not changes to
this page's own content):
  - 2026-05-28: §9 per-page-type FAQ governance added per user direction
  - 2026-05-28: §9 per-page metadata governance added — every Phase 2
    spec must include a §1.4 Page metadata (SEO + HTML head) block.
    Pattern first introduced on /capabilities/operational-intelligence
    spec v1; canonicalized here after user-direction lock.
  - 2026-05-29: §9 "How this differs from..." emerging content pattern
    documented as available-but-optional. Surfaced by ChatGPT v2 review
    of /capabilities/asset-intelligence; reviewer-validated as "the
    single most important improvement before lock" on that spec.
    Pattern is content-only (not a design-system v3 component); honors
    anti-overclaim + no-competitor-names + retrofit-policy discipline.
-->

# `/capabilities` hub — Page Spec v1

**Capability-first navigation entry point. Directory-style page presenting the 5 capability pillars at-a-glance. Each pillar gets a one-paragraph descriptor and a link to its deep-dive. Reader leaves with: *"I now know which pillar fits my problem and how to dive into it."***

This is a directory page, not a long-read. It does **not** describe any pillar in depth (each pillar has its own `/capabilities/<pillar>` page). It does **not** walk the architectural data flow (`/architecture` does that). It does **not** restate the vendor narrative (`/platform` does that). It does **not** list solutions (`/solutions` does that).

Target length: **600-900 words of page copy**.

---

## 1. IA + buyer alignment

### 1.1 What this page IS (per Phase 2 IA memo v2 §3.2)

- The capability-organising entry point — the 5 pillars laid out as the buyer's organising question (WHY framing per memo v2 §2)
- A directory: each pillar gets a one-paragraph description here + a clear "explore this pillar" link to its deep-dive page
- The page where the OT Architect lands when they want capability-first navigation

### 1.2 What this page IS NOT

- A deep description of any single pillar — those live at `/capabilities/<pillar>` (5 separate pages)
- The Industrial Intelligence Stack data-flow walkthrough — that's `/architecture`
- A full vendor narrative — that's `/platform`
- A solutions list — that's `/solutions`
- A trust-signaling page — no `TrustBand`, no customer logos, no testimonials (those live on `/platform` and `/customers`)
- An outcome-metrics page — no OEE numbers, no downtime percentages, no ROI claims (those live on `/solutions/<solution>` per proof-architecture v1 §3)

### 1.3 Buyer alignment (per buyer-taxonomy v1 §3)

**Primary buyer:** OT Architect / SCADA engineer
- Lands here from capability-first navigation (top nav "Capabilities" mega-menu, or direct from a capability-specific search query)
- Wants: architectural clarity, fast self-selection into the right pillar
- CTA preference: *"Talk to an engineer about <pillar>"* > "Book a scoping call"
- Vocabulary that lands: protocol-agnostic, real protocol names (FOCAS2, Modbus TCP, OPC UA), edge runtime, integration patterns

**Secondary buyer:** Plant engineer (retrofit / greenfield)
- Lands here when browsing what's available before diving into a specific capability page
- Wants: scannable summary, ability to find their pillar fast
- CTA preference: "Get hardware specifications" / "Talk to an engineer about <pillar>"

**Both buyers reward:**
- Directness (no marketing language; just what each pillar is)
- Real protocol names and product names (trust signals)
- Fast scan-ability (they don't want to read the page; they want to find their pillar and leave)

**Both buyers punish:**
- *"Industry 4.0"*, *"smart factory"*, *"AI-powered"*, *"transformation"* (per buyer-taxonomy vocabulary discipline)
- Long marketing prose (this is a directory; they're skimming)
- Card-elevation hovers, animated decorations (per design-governance §2.4)

---

## 2. Page structure — sections at a glance

Five sections, top to bottom. Mode rhythm per design-system v3 §2 SectionShell tokens. Total ~700 words of page copy (within the 600-900 target).

| # | Section | Visual mode | Primary component(s) |
|---|---|---|---|
| **1** | Hero (no CTAs — page IS the directory) | `dark-deep` | `SectionShell` (dark-deep) + verbatim copy |
| **2** | Five-pillar grid (the core of the page) | `dark` | `SectionShell` (dark) + `CapabilityCard` × 5 with pillar accent |
| **3** | How the pillars compose (one short paragraph + arch cross-link) | `light` | `SectionShell` (light) + inline copy + outbound link to `/architecture` |
| **4** | Cross-lens block (§17 pattern) | `light-tinted` | `SectionShell` (light-tinted, padding="tight") + 3 `CapabilityCard` variants per §17 presets |
| **5** | Final CTA section | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

**Visual:** dark-deep background. No hero composite. No CTAs (the page itself is the directory; the pillar cards in §2 are the navigation; CTAs come at the final section).

**Copy (verbatim):**

> EYEBROW (small-caps brand-teal, letter-spaced 0.18em)
> CAPABILITIES
>
> HEADLINE (size.3xl semibold, text.heading)
> Pick the capability that fits your problem.
>
> SUBHEAD (size.lg, text.body, max-width 60ch)
> Five capability pillars across one integrated platform. Each pillar answers a specific industrial question. Start with the one that matches what you're trying to solve.

**Behavior:** static. No animation. No background imagery. Padding default per `SectionShell.padding="default"`.

**Anti-patterns:**
- ❌ No `HeroComposite` here (locked to homepage only per buyer-taxonomy lock + design-system v3 §24 Q1 resolution)
- ❌ No trust micro-strip here (trust signaling is platform-level — `/platform` and homepage)
- ❌ No CTAs in hero (the pillar grid in §2 IS the CTA equivalent — each card is the next step)

---

### 3.2 Section 2 — The five-pillar grid

**Visual:** dark background. Five `CapabilityCard` instances in a grid (5-column desktop ≥ 1280px, 3-column tablet, 2-column small tablet, single column mobile). Each card uses its pillar accent color from `BRAND_TOKENS.md §2.2` (`--color-pillar-1` through `--color-pillar-5`).

**Section title (above the grid):**

> EYEBROW (small-caps brand-teal)
> THE FIVE CAPABILITY PILLARS

**Per-card content (verbatim from `hardware-ecosystem-map-v3.md §1`):**

#### Card 1 — Connectivity & Edge (pillar-1 accent)

> EYEBROW: PILLAR 1
> TITLE: Connectivity & Edge
> BODY (size.base, regular):
> *"How do I get my controllers' data into one operational view, on-premise and offline-capable?"*
> FOOTER (size.sm, text.muted): EdgeConnect · Edge Gateway
> LINK (text-only with arrow → suffix, brand-teal): Explore Connectivity & Edge →
> HREF: `/capabilities/connectivity-edge`

#### Card 2 — Data Acquisition (pillar-2 accent)

> EYEBROW: PILLAR 2
> TITLE: Data Acquisition
> BODY: *"What if I don't have a PLC, or I want to bypass it and read sensors directly?"*
> FOOTER: mDAQ
> LINK: Explore Data Acquisition →
> HREF: `/capabilities/data-acquisition`

#### Card 3 — Asset Intelligence (pillar-3 accent)

> EYEBROW: PILLAR 3
> TITLE: Asset Intelligence
> BODY: *"How do I track utilization, location, and OEE on equipment I've shipped or deployed across multiple sites?"*
> FOOTER: mTracker
> LINK: Explore Asset Intelligence →
> HREF: `/capabilities/asset-intelligence`

#### Card 4 — Condition Monitoring (pillar-4 accent)

> EYEBROW: PILLAR 4
> TITLE: Condition Monitoring
> BODY: *"How do I move from break-fix to predict-and-prevent on rotating equipment and hydraulic systems?"*
> FOOTER: VAS · E-IDOS
> LINK: Explore Condition Monitoring →
> HREF: `/capabilities/condition-monitoring`

#### Card 5 — Operational Intelligence (pillar-5 accent)

> EYEBROW: PILLAR 5
> TITLE: Operational Intelligence
> BODY: *"How do I turn all of this into OEE, alarms, incidents, and reports the team actually uses?"*
> FOOTER: EREMOS V2
> LINK: Explore Operational Intelligence →
> HREF: `/capabilities/operational-intelligence`

**Card visual treatment (per design-system v3 §3 CapabilityCard):**
- Top or left accent line in the pillar color
- Eyebrow → Title → Body → Footer → Link (vertical order)
- Card hover: accent-line slide-in (180ms `motion.default`); title color lift toward `brand.teal`; no elevation change, no shadow
- Equal min-height across the row (CSS grid)
- Internal padding `space.6` (24px tablet) to `space.8` (32px desktop)

**Anti-patterns:**
- ❌ No outcome metrics in card bodies (e.g., *"reduces downtime by N%"*)
- ❌ No customer logos in cards
- ❌ No icon decoration in Phase 2 (per design-system v3 §3 anti-patterns — Phase 3 introduces optional icon slot for hardware variant)
- ❌ No CTA buttons inside cards beyond the "Explore <pillar> →" link

---

### 3.3 Section 3 — How the pillars compose

**Visual:** light background. Single short paragraph + an architectural cross-link. Acts as a bridge — tells the reader the pillars aren't isolated and points them to `/architecture` if they want the data-flow view.

**Copy (verbatim):**

> EYEBROW (small-caps, dark-mode-equivalent on light)
> ONE INTEGRATED PLATFORM
>
> BODY (size.md, text.body-light, max-width 70ch):
> The five pillars are not five separate products bolted together. They compose into one integrated platform — the **Industrial Intelligence Stack** — that runs from raw field signal to enterprise decision. Pick a pillar to start; expand into the others as your data needs grow.
>
> CTA (text-link with arrow):
> See the full architecture →
> HREF: `/architecture`

**Anti-patterns:**
- ❌ No embedded architecture diagram here (the diagram lives on `/architecture` and the homepage; this page just cross-links)
- ❌ No long explanation of the stack (one paragraph max; the stack is `/architecture`'s territory)

---

### 3.4 Section 4 — Cross-lens block

**Visual:** light-tinted background, `SectionShell.padding="tight"`. Per §17 cross-lens content pattern.

**Per memo v2 §5.2 + design-system v3 §17 per-surface presets:**

From `/capabilities` hub, render 3 cross-lens cards pointing to:

| Card | Eyebrow | Description |
|---|---|---|
| 1 | PLATFORM | Looking for the full vendor evaluation? |
| 2 | ARCHITECTURE | How does the data flow end-to-end? |
| 3 | SOLUTIONS | Outcome-organised stories |

**Section headline (above the cards):**

> Looking for the same thing from another angle?

**Card behavior:** standard `CapabilityCard` (compact variant, no pillar accent — these are cross-lens navigation, not pillar selection). Each links to the destination surface.

**Anti-patterns:**
- ❌ No more than 3 cards (per §17 lock)
- ❌ No primary-button styling on cards (cross-lens is exploration, not conversion — final CTA in §5 is the conversion moment)
- ❌ No self-reference (filtered via `currentSurface="/capabilities"` per §17)

---

### 3.5 Section 5 — Final CTA section

**Visual:** dark-deep background. `CTASection` component per design-system v2 §8.

**Copy (verbatim) — per buyer-taxonomy v1 §2.3 OT Architect CTA preference:**

> EYEBROW (small-caps brand-teal):
> NEXT STEP
>
> HEADLINE (size.2xl bold):
> Pick a pillar. Talk to an engineer.
>
> SUBHEAD (size.md, text.body, max-width 60ch):
> If you have a specific capability question, talk to the Elpis engineer who owns that pillar. If you want to evaluate the platform architecturally before picking a pillar, request an architecture review.
>
> PRIMARY CTA (`Button.primary.lg`):
> Talk to an engineer
> HREF: `/contact?intent=engineering`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`

**Why these CTAs:** per buyer-taxonomy v1 §2.3 — OT Architect rewards *"Talk to an engineer"* (collegial, direct, peer-to-peer) and *"Request an architecture review"* (signals serious technical engagement). Generic *"Book a demo"* and *"Talk to sales"* both qualify out the OT Architect audience.

**Anti-patterns:**
- ❌ No "Book a demo" CTA
- ❌ No "Get started free" / "Start your trial" (consumer-SaaS framing; backfires per buyer-taxonomy)
- ❌ No three CTAs (max one primary + one secondary per design-system v2 §8)

---

## 4. Components used

| Component | Source | Used in section |
|---|---|---|
| `SectionShell` | design-system v2 §2 | every section (mode variants per §2-§5 above) |
| `CapabilityCard` (pillar variants) | design-system v2 §3 + design-system v3 §22 coverage map | §3.2 five-pillar grid |
| `CapabilityCard` (compact variant) | design-system v2 §3 + design-system v3 §17 cross-lens pattern | §3.4 cross-lens block |
| `Button` (primary + secondary, size lg) | design-system v2 §1 | §3.5 final CTA |
| `CTASection` | design-system v2 §8 | §3.5 final CTA |
| Cross-lens content pattern | design-system v3 §17 | §3.4 |

**No new components introduced.** This spec composes entirely from design-system v3 (which is LOCKED on PR #68). Per design-system v3 §0 principle 5: composable, not prescriptive.

---

## 5. Verbatim copy summary

For the engineering team and copywriters, all page copy in one place:

```
SECTION 1 — HERO

CAPABILITIES

Pick the capability that fits your problem.

Five capability pillars across one integrated platform. Each pillar
answers a specific industrial question. Start with the one that
matches what you're trying to solve.


SECTION 2 — FIVE-PILLAR GRID

THE FIVE CAPABILITY PILLARS

[Card 1]  PILLAR 1
          Connectivity & Edge
          "How do I get my controllers' data into one operational
          view, on-premise and offline-capable?"
          EdgeConnect · Edge Gateway
          Explore Connectivity & Edge →

[Card 2]  PILLAR 2
          Data Acquisition
          "What if I don't have a PLC, or I want to bypass it and
          read sensors directly?"
          mDAQ
          Explore Data Acquisition →

[Card 3]  PILLAR 3
          Asset Intelligence
          "How do I track utilization, location, and OEE on
          equipment I've shipped or deployed across multiple
          sites?"
          mTracker
          Explore Asset Intelligence →

[Card 4]  PILLAR 4
          Condition Monitoring
          "How do I move from break-fix to predict-and-prevent on
          rotating equipment and hydraulic systems?"
          VAS · E-IDOS
          Explore Condition Monitoring →

[Card 5]  PILLAR 5
          Operational Intelligence
          "How do I turn all of this into OEE, alarms, incidents,
          and reports the team actually uses?"
          EREMOS V2
          Explore Operational Intelligence →


SECTION 3 — HOW THE PILLARS COMPOSE

ONE INTEGRATED PLATFORM

The five pillars are not five separate products bolted together.
They compose into one integrated platform — the Industrial
Intelligence Stack — that runs from raw field signal to enterprise
decision. Pick a pillar to start; expand into the others as your
data needs grow.

See the full architecture →


SECTION 4 — CROSS-LENS BLOCK

Looking for the same thing from another angle?

[Card 1]  PLATFORM
          Looking for the full vendor evaluation?
          → /platform

[Card 2]  ARCHITECTURE
          How does the data flow end-to-end?
          → /architecture

[Card 3]  SOLUTIONS
          Outcome-organised stories
          → /solutions


SECTION 5 — FINAL CTA

NEXT STEP

Pick a pillar. Talk to an engineer.

If you have a specific capability question, talk to the Elpis
engineer who owns that pillar. If you want to evaluate the
platform architecturally before picking a pillar, request an
architecture review.

[Talk to an engineer]  [Request an architecture review]
```

**Total page copy:** ~370 words (within 600-900 target — at the low end deliberately, because the page is a directory and lower density serves the OT Architect scan pattern).

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21:

| Don't | Why |
|---|---|
| Add long-form pillar descriptions in the card bodies | Each pillar has its own deep-dive page; duplication violates phase2-ia-scope-memo v2 §4.0 authoritative-explanation invariant |
| Embed the architecture diagram | The diagram lives on `/architecture` (interactive) and the homepage (static); this page cross-links |
| Include customer logos / trust band | Trust signaling is platform-level — lives on `/platform` and homepage per proof-architecture v1 §3 |
| Include outcome metrics (OEE %, downtime hours, ROI) | Outcome metrics live on `/solutions/<solution>` in context — never on capability pages per proof-architecture v1 §3 |
| Add solution cards / case study teasers | Solutions live at `/solutions` — this page cross-links via §17, doesn't duplicate |
| Use "Book a demo" / "Start free trial" / "Talk to sales" CTAs | Wrong buyer alignment — OT Architect rewards "Talk to an engineer" / "Request an architecture review" |
| Use marketing-flavored card eyebrows ("INDUSTRY 4.0 PILLAR" / "AI-POWERED CAPABILITY") | Vocabulary backfires per buyer-taxonomy v1 §2.3 — kills credibility with OT Architect on first read |
| Show pricing on cards | Pricing detail is Phase 3; commercial engagement teaser is `/platform` only per amendment v3 §1.7 |
| Animate the pillar cards (count-up, stagger entrance, glow effects) | Design-governance §2.2 motion ceiling + premium-industrial stillness |

---

## 7. Sign-off checklist (v1 lock)

- [ ] Page copy fits the 600-900 word target (current draft: ~370 words page copy — at low end of range, deliberately, per directory scan pattern)
- [ ] All 5 pillar cards use the verbatim customer question from `hardware-ecosystem-map-v3.md §1`
- [ ] All 5 pillars use their pillar accent color (per `BRAND_TOKENS §2.2`)
- [ ] No outcome metrics, no customer logos, no trust band on this page
- [ ] No `HeroComposite` (locked to homepage per design-system v3 §24 Q1)
- [ ] Cross-lens block uses exactly 3 cards per memo v2 §5.2 + design-system v3 §17 preset for `/capabilities` source
- [ ] Final CTA uses "Talk to an engineer" + "Request an architecture review" per buyer-taxonomy v1 §2.3 OT Architect preference
- [ ] All copy passes vocabulary discipline — no "transformation", no "AI-powered", no "Industry 4.0", no "smart factory"
- [ ] All cross-links resolve to valid Phase 2 routes
- [ ] Components used are all design-system v3 LOCKED — no new components introduced
- [ ] Mobile responsive — 5-col desktop → 3 → 2 → 1 column mobile per design-system v2 §2 SectionShell behavior
- [ ] WCAG AA contrast across all sections (design-governance §2.5)

---

## 8. Out of scope for v1

- **Per-pillar deep-dive content.** Lives in the 5 separate `/capabilities/<pillar>` page specs (the next 5 deliverables in the Phase 2 sequencing per amendment v3 §6).
- **Architecture data-flow narrative.** Lives in `/architecture` page spec.
- **Vendor identity / competitive frames.** Lives in `/platform` page spec.
- **Solution outcome stories.** Live in `/solutions` hub spec + per-solution page specs.
- **Pricing detail.** Phase 3 `/pricing` page.
- **Industry-specific pillar framing.** Phase 3 `/industries/<industry>` pages may filter pillar relevance to industry context.

---

## 9. Phase 2 per-page spec template — CANONICAL (added at v1 lock per Pass 1 ChatGPT review)

The §1-§8 structure above is now the **canonical template for all 10 Phase 2 per-page specs.** Every remaining per-page spec (`/capabilities/<pillar>` × 5, `/architecture`, `/solutions`, `/solutions/predictive-maintenance`, `/solutions/edge-connectivity`, `/platform`) follows the same structure:

| § | Section | Purpose |
|---|---|---|
| Header | File metadata + status + changelog | Discovery, versioning |
| §1.1 | What the page IS / IS NOT | Scope locks per phase2-ia-scope-memo v2 §3 |
| §1.2 | Buyer alignment | Primary + secondary buyer per buyer-taxonomy v1 §2-§3; CTA preference and vocabulary discipline |
| §1.3 | (formerly §1.3 if applicable) | Per-page IA-adjacent context |
| **§1.4** | **Page metadata (SEO + HTML head)** *(added at \/capabilities/operational-intelligence v1 lock 2026-05-28)* | **Meta title (50-60 chars), meta description (140-160 chars), canonical URL, schema.org intent. Source of truth for the engineering team's HTML `<head>` configuration. Authoring meta information at spec-time (not implementation-time) prevents SEO drift and ensures every page has a verifiable meta footprint reviewable against the page's actual content.** |
| §2 | Page structure — sections at a glance | Section count, mode rhythm, primary components — at-a-glance table |
| §3 | Section-by-section detail | Per-section: visual mode, verbatim copy, anti-patterns specific to that section |
| §4 | Components used | All design-system v3 (or v2) components referenced; flag NEW component introductions if any |
| §5 | Verbatim copy summary | All page copy collected in one place for the Angular team and copywriters to lift directly |
| §6 | Anti-patterns specific to this page | Beyond the design-system v3 system-wide anti-patterns — page-specific risks |
| §7 | Sign-off checklist | What must be true before the spec locks |
| §8 | Out of scope for v1 | Explicit deferrals to other specs / phases |

**Why this structure works (per ChatGPT review):**

- **Reusable without being rigid** — every spec uses the same skeleton, but the per-section content adapts to the page's primary buyer, IA scope, and content density
- **Engineering team gets verbatim copy in one place** (§5) — no need to grep across sections during implementation
- **Anti-patterns specific to this page** (§6) — explicit page-level risks that go beyond the system-wide design-system anti-patterns
- **Out of scope discipline** (§8) — explicit deferrals prevent scope creep mid-spec

**What every subsequent Phase 2 per-page spec must do:**

1. Follow the §1-§8 structure above. Section ordering, naming, and purpose are LOCKED.
2. Cite the same foundation docs as v1 (phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1, proof-architecture v1, design-system v3, hardware-ecosystem-map v3, positioning v3).
3. Use no new components unless genuinely required — design-system v3 is LOCKED; additions require v4 governance.
4. Honor the §6 design-governance discipline rules.
5. Apply the §16 trust-cue content pattern and §17 cross-lens content pattern from design-system v3 where the page-type calls for them.
6. **Include a §1.4 page metadata block** (Meta title, Meta description, Canonical URL, Schema intent) — see "Per-page metadata governance" below for required content and conventions.

**Page-type-specific accommodations within the template:**

- **Hub pages** (this spec, `/solutions` hub, `/capabilities` hub) — sized at the low end of word-count targets (300-600 words page copy), no embedded trust signaling, scan-pattern optimized
- **Capability deep-dive pages** (5 pillar pages) — use `CapabilityDeepDive` (design-system v3 §14), include trust cue per §16, include cross-lens per §17, ~800-1,200 words page copy
- **Solution depth-example pages** (predictive-maintenance, edge-connectivity) — use `SolutionPanel` (design-system v3 §15), inherit existing solution-page template structure from `solution-cnc-machining-v2.md`, include trust cue + cross-lens, ~1,500-1,800 words page copy
- **`/architecture` page** — use `ArchitecturePanel.interactive` (design-system v3 §5.A), uniquely heavy on technical content, ~1,200-1,800 words page copy
- **`/platform` page** — synthesizes everything; written last per amendment v3 §6 sequencing; cross-references every other Phase 2 page; ~1,500-2,000 words page copy

**Template evolution governance:** if a per-page spec discovers a section that the template doesn't accommodate, the spec author surfaces the gap rather than silently adding a §3.5 or §6.5. Template evolution requires explicit acknowledgment + (if substantive) a new template-canonical lock entry, not silent expansion across the spec wave.

### Emerging content pattern — "How this differs from..." callout (reviewer-validated 2026-05-29)

Pillar pages where the buyer is likely to confuse the pillar with an adjacent / familiar category benefit from an explicit *"How this differs from..."* callout near the architecture-position section (§3.5). The pattern was surfaced by ChatGPT v2 review of `/capabilities/asset-intelligence` (LOCKED 2026-05-29) and validated as *"the single most important improvement before lock"* on that spec.

**Worked examples — pillar pages where the pattern applies:**

| Pillar | Adjacent category buyer will conflate with | Callout framing |
|---|---|---|
| Asset Intelligence | Controller monitoring | *How this differs from controller monitoring* ✓ applied on v2 |
| Connectivity & Edge | General-purpose IoT gateway | *How this differs from a general-purpose IoT gateway* |
| Data Acquisition | PLC-based data acquisition | *How this differs from PLC-based data acquisition* |
| Condition Monitoring (already LOCKED — NOT retrofitted) | SCADA alarms | *How this differs from SCADA alarms* — only if/when the spec is next reviewed |
| Operational Intelligence (already LOCKED — NOT retrofitted) | Reporting tools / dashboards | *How this differs from reporting tools* — only if/when the spec is next reviewed |

**Discipline rules for using the pattern:**

- **Available-but-optional.** Only worth the page-space cost when there is a real adjacent category the buyer will conflate the pillar with. Don't add the callout just because the pattern exists.
- **Place inside §3.5** (or whichever section is the page's "where this fits architecturally" anchor) — NOT as a separate top-level page section.
- **Honor anti-overclaim discipline.** Name the differentiator honestly; do not put down the adjacent category for its legitimate use cases. Cross-link to the adjacent pillar / capability when applicable (the asset-intelligence example doesn't put down controller monitoring; it explains where each is the right answer).
- **No competitor names.** Per proof-architecture v1 §8 — describe the category, not the specific vendor products in it.
- **Pattern is reviewer-validated as emerging, not codified as a design-system component.** Per design-system v3 LOCKED + judicious-additions discipline, no new component is introduced. The pattern is a content technique; visual treatment is a bordered callout block or left-rule call using existing `SectionShell` variants.
- **Retrofit policy.** Already-locked specs (condition-monitoring, operational-intelligence) are NOT reopened to add the pattern. They passed review without it; reopening locked specs for "this would also be nice" additions violates the pause-and-report discipline in reverse.

### Per-page-type FAQ governance (added at template lock per user direction)

Modern industrial B2B has moved away from dedicated `/faq` pages toward **inline "Common questions" sections on the pages where the questions naturally arise** (with FAQ schema markup for SEO). User-direction-locked decision on which Phase 2 page types include an inline Q&A section:

| Page type | Include inline FAQ? | Rationale |
|---|---|---|
| Homepage | No | Brand / conversion surface; FAQs dilute |
| `/platform` | **Yes** | CTOs and procurement ask predictable platform-level questions (commercial model, competitive framing, deployment philosophy) |
| `/capabilities` hub | No | Directory page; per-pillar questions live elsewhere |
| `/capabilities/<pillar>` × 5 | **No** | Stay capability-level. Per-pillar questions belong on Phase E product pages (specific) or `/solutions/<solution>` (in outcome context) |
| `/architecture` | **Yes** | OT Architect / SCADA reviewer asks predictable architecture / integration / security questions |
| `/solutions` hub | No | Directory page |
| `/solutions/<solution>` × 2 | **Yes — already locked** in `SolutionPanel` (design-system v3 §15) | Operational questions in outcome context (per existing `solution-cnc-machining-v2.md §5` precedent) |
| `/security` | **Yes — already locked** in `security-page-copy-v2.md §7` | Predictable procurement / compliance review questions |
| Phase E product pages (when they ship) | **Yes** | Product evaluation includes "does it work with X?" questions |

**The pattern when included:** "Common questions" section (NOT "FAQ" labelled — operationally-grounded vocabulary lands better with industrial buyers per buyer-taxonomy v1 cross-cutting patterns). 5-9 questions per page, scannable as bold pull-quotes with answers underneath. FAQ schema markup applied at engineering implementation time to support SEO discovery.

**The pattern when NOT included:** the page does not get a Q&A section just for SEO reasons. Buyer questions still get answered — through cross-links to the page where the question naturally belongs (e.g., capability-page visitors with "does this work with X?" questions get routed to the related `/solutions/<solution>` page or to a Phase E product page).

**No dedicated `/faq` page in any phase.** That pattern is archaic; modern industrial buyers expect contextual answers, and a one-page FAQ dilutes SEO across the platform.

### Per-page metadata governance (added at template lock per user direction 2026-05-28)

Every Phase 2 per-page spec MUST include a **§1.4 Page metadata (SEO + HTML head)** block. The pattern was introduced at the lock of `/capabilities/operational-intelligence` v1 (see that spec's §1.4 for the reference example) and is canonicalized here.

**Why this is canonical (not optional):**

- **Meta information drifts when authored at implementation time.** Engineering ships pages without meta footprint review; copywriters discover the gap after launch; SEO audits surface inconsistencies months later. Putting meta in the spec means it is reviewed alongside page copy by the same governance loop.
- **Single source of truth for `<head>` configuration.** Engineering lifts directly from the spec rather than re-interpreting page intent into title/description fragments.
- **Schema intent is an architectural decision, not a markup detail.** Choosing `WebPage` vs `Product` vs `Article` vs `FAQPage` affects how search engines surface the page and whether SERP rich snippets appear; the spec is the right place to lock that decision.

**Required fields and conventions:**

| Field | Convention | Notes |
|---|---|---|
| **Meta title** | 50-60 characters | Pattern: `<Page topic> — <secondary descriptor> · Elpis`. The em-dash and middle-dot are the locked separators. Title must work as the browser tab label AND as the SERP headline. |
| **Meta description** | 140-160 characters | Single sentence, plain industrial vocabulary, describes the page outcome (what the reader learns / what the page is for) — NOT marketing claims. No exclamation marks. No first-person ("we"). |
| **Canonical URL** | Full `https://www.elpisitsolutions.com/<route>` | The locked route from phase2-ia-scope-memo v2 §3. Used for `<link rel="canonical">` and analytics. |
| **Schema intent** | `schema.org/<Type>` reference + structural notes | At minimum: page-level schema type (typically `WebPage`) + `BreadcrumbList`. Pages that embed FAQs add `FAQPage`. Pages anchored to a product (Phase E `/eremos-v2`, `/edge-gateway`, etc.) link via `Product` schema. Page-to-page cross-links use `relatedLink`. |

**Reference example:** `docs/marketing/page-capabilities-operational-intelligence-spec-v1.md` §1.4 is the first instance and the pattern template.

**Anti-patterns:**

- ❌ Meta description that paraphrases page copy verbatim — search engines penalize duplicate content patterns. Write meta-as-meta, not meta-as-excerpt.
- ❌ Meta title that includes a customer-facing brand tagline — taglines drift; structural titles last.
- ❌ Skipping schema intent because "it's an engineering detail" — schema intent shapes SERP appearance, which shapes click-through, which shapes whether the page is discoverable at all.
- ❌ Per-page meta inconsistency on separator characters — the em-dash + middle-dot pattern is locked across all Phase 2 pages so search results read as a coherent site.

---

*`/capabilities` hub Page Spec v1 — LOCKED 2026-05-28 after Pass 1 user + ChatGPT review. First per-page spec in the Phase 2 wave per amendment v3 §6 sequencing. The §1-§8 spec structure is now CANONICAL for all 10 Phase 2 per-page specs per §9 above. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1, proof-architecture v1, design-system v3, hardware-ecosystem-map v3 §1, positioning v3.*
