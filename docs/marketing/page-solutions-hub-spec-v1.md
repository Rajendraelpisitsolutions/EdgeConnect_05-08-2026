<!--
File:        docs/marketing/page-solutions-hub-spec-v1.md
Purpose:     Page spec for /solutions — the outcome-first navigation
             entry point. Directory-style hub page presenting the 7
             canonical solutions at-a-glance, each with a one-paragraph
             descriptor and a link to its deep-dive. Seventh of 10
             Phase 2 per-page specs.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), Phase 2 step 9 + 10 + 11 spec authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-hub-spec-v1.md (sister hub page — same
                hub-page pattern, organized by capability instead of
                outcome)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                hub-page word-count guidance: 300-600 words; no
                embedded trust signaling; scan-pattern optimized)
             phase2-ia-scope-memo-v2.md §3 (IA parent — /solutions
                scope) + amendment v3 (sequencing step 8)
             buyer-taxonomy-v1.md (cross-buyer — different solutions
                serve different primary buyers; the hub speaks to all
                of them via outcome framing)
             proof-architecture-v1.md (proof discipline — no customer
                logos / metrics / certification claims on the hub)
             design-system-v3.md §3 (CapabilityCard solution-variant),
                §17 (cross-lens content pattern)
             solution-cnc-machining-v2.md, solution-precision-
                manufacturing-v2.md, solution-brownfield-modernization-
                v2.md, solution-oem-machine-monitoring-v2.md,
                solution-multi-site-operations-v2.md (existing v2
                solution pages; v3 in Phase E)
             page-architecture-spec-v1.md (LOCKED v2 — cross-references
                /solutions/edge-connectivity)
             page-capabilities-connectivity-edge-spec-v1.md (LOCKED v2
                — cross-references /solutions/cnc-machining +
                /solutions/edge-connectivity)
             page-capabilities-asset-intelligence-spec-v1.md (LOCKED v2
                — cross-references /solutions/multi-site-operations +
                /solutions/oem-machine-monitoring)
             page-capabilities-data-acquisition-spec-v1.md (LOCKED v2
                — cross-references /solutions/edge-connectivity +
                /solutions/brownfield-modernization)
             page-capabilities-operational-intelligence-spec-v1.md
                (LOCKED v1 — cross-references /solutions/predictive-
                maintenance + /solutions/multi-site-operations)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview)
Version:     v2 — LOCKED after Pass 1 ChatGPT review + Workflow
                  source-of-truth verification pass (3 must-fixes
                  verified accurate against locked specs; 1 reviewer
                  recommendation upgraded with stronger v2-verbatim
                  fix; 3 optional refinements applied)
Date:        2026-05-29 (v2 lock)
Status:      LOCKED.

Seventh per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 8. Hub page — sized at the low end of word-count
targets per /capabilities hub spec §9 page-type guidance (300-600
words; scan-pattern optimized; no embedded trust signaling). 5-section
structure (not 9 like the pillar deep-dives).

Page-structure approval: structure approved by user direction
2026-05-29 before drafting. 5-section layout: Hero → 7-solution card
grid → Bridge paragraph → Cross-lens → Final CTA. 7 solution cards in
locked order (Edge Connectivity → Predictive Maintenance →
Multi-site → Brownfield → CNC Machining → Precision Manufacturing →
OEM Machine Monitoring). 2 new Phase 2 cards visually marked
"Coming soon" (public-facing pill — internal Phase-2-step references
stay in this spec's metadata only).

Word-count target: 300-600 words per hub-page guidance. v2 draft:
~530 words page copy.

§1.4 Page metadata block included per /capabilities hub §9 metadata
governance lock (PR #71).

NO inline FAQ — per /capabilities hub §9 per-page-type FAQ governance:
hub pages are directory pages; per-solution questions live elsewhere
(specifically: on each /solutions/<solution> page, which has its own
inline FAQ per §9 governance + the solution-page SolutionPanel
precedent).

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve after a focused v2 refinement pass. The overall hub
   structure is right: it mirrors the /capabilities hub pattern,
   stays directory-style, avoids proof/FAQ bloat, and gives visitors
   a clean outcome-first way to navigate the ecosystem. After the
   three must-fix areas are addressed — public status-pill wording,
   Predictive Maintenance/E-IDOS accuracy, and cross-lens preset
   compliance — the /solutions hub should be ready to lock."

Source-of-truth verification workflow (2026-05-29):
  Spawned 4-agent workflow to verify ChatGPT's 3 must-fix claims
  against locked source-of-truth docs in parallel + 1 synthesis pass.
  Pattern mirrors the M1-rejection discipline that caught the
  /architecture spec's motion.architecture reviewer error.
  All 3 must-fixes verified ACCURATE; 1 was upgraded with a stronger
  v2-verbatim fix.

User decision on v2 refinements (2026-05-29):
  - M1 (E-IDOS hydraulic/lubrication positioning)                      APPLIED
        — workflow verdict: ACCURATE; "thermal/electrical condition
          signatures" was a FABRICATION (zero locked sources support
          it). Locked condition-monitoring spec v1, hardware-ecosystem-
          map v3 §5.2, and positioning v3 §Glossary ALL position E-IDOS
          as hydraulic + lubrication oil-health (ISO 4406 / NAS 1638
          cleanliness standards).
  - M2 (cross-lens PLATFORM + CAPABILITIES + ARCHITECTURE)              APPLIED
        — workflow verdict: ACCURATE; design-system v3 §17 line 558
          LOCKS the /solutions hub preset. CONTACT is not even in the
          §17 angle enum (line 523, restricted to platform | capabilities
          | architecture | solutions | security). Double violation.
        — Removed CONTACT card; added PLATFORM card; preserved
          CAPABILITIES + ARCHITECTURE
  - M3 (OEM card truck-rolls verbatim v2 fix)                           APPLIED with stronger fix
        — workflow verdict: ACCURATE — and HARDER than ChatGPT flagged.
          Locked OEM v2 spec uses "Cut truck rolls" deliberately (§1
          hero line 42); §6 outcomes bullet 1 uses verbatim "Cut truck
          rolls on remote-diagnosable issues ... some dispatches become
          phone-resolved" (line 194); §9 explicitly forbids quantified/
          absolute truck-roll claims (line 320).
        — Applied workflow's stronger fix (verbatim v2 wording) instead
          of ChatGPT's softer "reducing tag-change truck rolls" — keeps
          source-alignment AND removes invented "tag changes" angle
  - J1 (Coming in Phase 2 → Coming soon)                                APPLIED
        — public-facing pill uses "Coming soon"; internal step
          references stay in spec metadata + commit messages
  - J2 (Pre-live link policy for Cards 1-2)                             APPLIED
        — locked policy: until /solutions/edge-connectivity and
          /solutions/predictive-maintenance ship, Cards 1-2 link to the
          underlying capability page. Status pill stays. Live-link
          variant documented for activation when destinations ship.
  - J5 (4 new defensive anti-patterns)                                  APPLIED
        — §6: (1) internal-roadmap-language guard; (2) dead-route
          guard; (3) cross-lens conversion guard; (4) capability-
          domain misstatement guard. Locks lessons learned from this
          review into governance.
  - J3+J4 (editorial polish — hero headline + Edge Connectivity in
       bridge paragraph)                                                NOT APPLIED
        — current hero + bridge wording retained per user direction

Sections receiving "no change" reviewer approval (preserved verbatim):
  Cross-buyer outcome framing, 5-section hub structure, card order
  (Edge Connectivity → Predictive Maintenance → Multi-site → Brownfield
  → CNC → Precision → OEM Monitoring; alphabetical reorder forbidden),
  bridge paragraph structure, final CTA ("Bring us the outcome
  you're trying to deliver"), proof discipline (no logos / metrics /
  inline FAQ / customer stories).

Side-flag from workflow (governance note, doesn't block this lock):
  PR #72 (/capabilities/condition-monitoring) is LOCKED on its branch
  but not yet merged to master. This hub spec cross-links it as
  authoritative for the E-IDOS positioning fix. Per user direction
  2026-05-29: hub v2 locks without waiting for PR #72 merge; verify
  PR #72 merge status before publishing the hub to the live website.
-->

# `/solutions` hub — Page Spec v1

**Outcome-first navigation entry point. Directory-style page presenting the 7 canonical solutions at-a-glance. Each solution gets a one-paragraph descriptor and a link to its deep-dive. Reader leaves with: *"I now know which outcome fits my operations and where to read the deep-dive."***

This is the page where Plant managers / Ops VPs, OEM machine builders, OT Architects, and Plant engineers land when they want the **outcome view** rather than the capability view. It is **not** a per-solution detail page (each `/solutions/<solution>` is its own LOCKED v2 spec or upcoming Phase 2 step 9/10 spec). It is **not** the capability hub (`/capabilities` covers the 5-pillar capability-organized view). It is the **solutions directory**.

Target length: **300-600 words page copy** per `/capabilities` hub spec §9 hub-page guidance.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Hub directory for the 7 canonical solutions. Reader leaves with *"I now know which outcome to dive into."*

**IS NOT:**
- A per-solution detail page (those are `/solutions/<solution>` × 7 — 5 of them LOCKED v2 already from the Phase 1 marketing track; 2 of them upcoming as Phase 2 steps 9-10)
- The capability hub (`/capabilities` is the 5-pillar capability-organized view)
- An industries page (Phase 3 `/industries/<industry>`, or the Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2)
- A platform overview (Phase 2 step 11 `/platform` covers vendor-worldview synthesis)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Cross-buyer page.** Different solutions serve different primary buyers; the hub speaks to all of them via outcome framing rather than per-buyer framing:

| Solution | Primary buyer per its v2 spec |
|---|---|
| Edge Connectivity *(internal: Phase 2 step 10 — public pill: "Coming soon")* | OT Architect / SCADA engineer (§2.3) |
| Predictive Maintenance *(internal: Phase 2 step 9 — public pill: "Coming soon")* | Maintenance Manager / AMC provider (§2.4) |
| Multi-site Operations | Plant manager / Ops VP (§2.2) |
| Brownfield Modernization | Plant engineer + OT Architect (§2.5 + §2.3) |
| CNC Machining | Plant manager + OT Architect (§2.2 + §2.3) |
| Precision Manufacturing | Plant manager / Ops VP (§2.2) |
| OEM Machine Monitoring | OEM machine builder (§2.6) |

**CTA preference per buyer-taxonomy v1 (cross-cutting):** the hub uses *"Talk to an engineer"* + *"Request an architecture review"* — the same CTA pattern the OT Architect responds to (per §2.3). For per-buyer-tuned CTAs (e.g., *"Bring us your fleet"* for Plant managers, *"Bring us your installed base"* for OEMs), each `/solutions/<solution>` page handles its own buyer-tuned framing.

**Vocabulary that lands across the cross-buyer hub:** outcome, operations, plant, controllers, OEE, telemetry, retrofit, multi-site, brownfield, mixed-vendor, integration, scoping.

**Vocabulary that backfires (per cross-buyer review of buyer-taxonomy v1):** *"easy"*, *"seamless"*, *"intuitive"*, *"future-proof"*, *"transformation"*, *"end-to-end"* without specifying the ends.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Solutions — outcomes on the Industrial Intelligence Stack · Elpis* |
| **Meta description** (140-160 chars) | *Seven solutions on the Elpis Industrial Intelligence Ecosystem — connectivity, predictive maintenance, multi-site ops, brownfield, CNC, precision, OEM.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList` and `ItemList` (the 7 solutions as `ItemListElement` entries). Each solution card cross-links to `/solutions/<slug>` via `relatedLink`. |

---

## 2. Page structure — sections at a glance

Hub page — 5 sections (not 9 like the pillar deep-dives). Per `/capabilities` hub spec §9 page-type accommodations: hub pages are sized at the low end of word-count targets and scan-pattern optimized.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + CTAs) | `dark-deep` | `SectionShell` + `Button` × 2 | ~100 |
| **2** | The 7 solutions — outcome-organized grid | `light` | `CapabilityCard` × 7 with solution-variant accents (NEW Phase 2 cards marked "(coming in Phase 2)") | ~290 (~40/card) |
| **3** | Bridge — "How solutions compose from pillars" (1-paragraph orientation) | `light-tinted` | Single-column copy + small inline reference to `/capabilities` | ~60 |
| **4** | Cross-lens navigation | `light-tinted` | §17 cross-lens content pattern (3 cards) | ~30 |
| **5** | Final CTA | `dark-deep` | `CTASection` | ~40 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTIONS · OUTCOMES ON THE INDUSTRIAL INTELLIGENCE STACK
>
> HEADLINE (size.3xl semibold):
> What customers come to Elpis to do.
>
> SUBHEAD (size.lg, max-width 60ch):
> Seven solutions built on the same Industrial Intelligence Ecosystem. Pick the outcome that fits your operations — the underlying capabilities, hardware, and software are shared; the configuration, workflows, and outputs differ per outcome.
>
> PRIMARY CTA (`Button.primary.lg`):
> Talk to an engineer
> HREF: `/contact?intent=solutions-engineering`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`

**Anti-patterns:** No *"transformation"* / *"end-to-end"* framing (per cross-buyer vocabulary discipline). No outcome metric in headline. No vertical-specific narrowing in the hero (verticals belong on Phase 3 `/industries/<industry>` or the Phase 2.5 single-industry exception, not on the cross-cutting solutions hub).

---

### 3.2 Section 2 — The 7 solutions (outcome-organized grid)

> EYEBROW: SOLUTIONS

**Grid layout:** 7 cards. Desktop: 3-2-2 row layout (3 cards top row, 2 cards middle row, 2 cards bottom row) for visual hierarchy on the top row. Mobile: single column. Order is locked per §6 anti-pattern (most-cross-pillar + most-asked first; vertical-specific last).

#### Card 1 — Edge Connectivity *(coming soon)*

> EYEBROW: SOLUTION · EDGE CONNECTIVITY
> STATUS PILL (top-right corner, size.sm brand-teal italic): *Coming soon*
> TITLE: One operational view across every controller on your floor
> BODY: For brownfield CNC, mixed-vendor floors, and OT consolidations. EdgeConnect + Edge Gateway + EREMOS V2 working together. Without ripping out what you already have.
> PRE-LIVE LINK (until `/solutions/edge-connectivity` ships): See the underlying capability → `/capabilities/connectivity-edge`
> LIVE LINK (once `/solutions/edge-connectivity` ships): Read the solution → `/solutions/edge-connectivity`

#### Card 2 — Predictive Maintenance *(coming soon)*

> EYEBROW: SOLUTION · PREDICTIVE MAINTENANCE
> STATUS PILL: *Coming soon*
> TITLE: Detect failure signatures before downtime
> BODY: VAS for vibration analytics on rotating equipment + E-IDOS for hydraulic and lubrication oil-health diagnostics + EREMOS V2 incident workflows. For asset-criticality programs that need failure-mode detection, not just runtime tracking.
> PRE-LIVE LINK (until `/solutions/predictive-maintenance` ships): See the underlying capability → `/capabilities/condition-monitoring`
> LIVE LINK (once `/solutions/predictive-maintenance` ships): Read the solution → `/solutions/predictive-maintenance`

#### Card 3 — Multi-site Operations

> EYEBROW: SOLUTION · MULTI-SITE OPERATIONS
> TITLE: One operational view across every plant
> BODY: For multi-plant operators consolidating utilization, OEE, and asset visibility across sites. mTracker captures equipment-level signals; EREMOS V2 aggregates across sites with consistent canonical vocabulary, OEE definitions, and alarm semantics.
> LINK: Read the solution → `/solutions/multi-site-operations`

#### Card 4 — Brownfield Modernization

> EYEBROW: SOLUTION · BROWNFIELD MODERNIZATION
> TITLE: Modernize the data layer without replacing the controllers
> BODY: For sites with PLCs that work but don't expose what the operations team needs. EdgeConnect reads native protocols; mDAQ captures the sensor-level signals the PLC doesn't cover. New data-layer outcome on existing controller infrastructure.
> LINK: Read the solution → `/solutions/brownfield-modernization`

#### Card 5 — CNC Machining

> EYEBROW: SOLUTION · CNC MACHINING
> TITLE: Mixed-vendor CNC floors on one operational view
> BODY: For shops running Fanuc + Brother + Mazak (or any combination of CNC controllers) — every controller speaks the same canonical CNC vocabulary at EREMOS V2. One operator view across every machine.
> LINK: Read the solution → `/solutions/cnc-machining`

#### Card 6 — Precision Manufacturing

> EYEBROW: SOLUTION · PRECISION MANUFACTURING
> TITLE: OEE accountability across mixed-vendor production cells
> BODY: For precision-manufacturing operations with strict OEE accountability — auditable OEE definitions, persistent alarm workflows, and shift-report consistency across every cell. Same OEE math, every shift, every cell.
> LINK: Read the solution → `/solutions/precision-manufacturing`

#### Card 7 — OEM Machine Monitoring

> EYEBROW: SOLUTION · OEM MACHINE MONITORING
> TITLE: Ship connected equipment that customers actually accept
> BODY: For OEM machine builders embedding telemetry in shipped equipment — service-hours billing, warranty triggers, remote diagnostics, cut truck rolls on remote-diagnosable issues. Customer-controlled routing means the customer decides which signals route back to the OEM.
> LINK: Read the solution → `/solutions/oem-machine-monitoring`

---

### 3.3 Section 3 — Bridge: how solutions compose from pillars

> EYEBROW: HOW SOLUTIONS COMPOSE
>
> BODY (single paragraph, size.base):
> Each solution above composes from the 5 capability pillars in different proportions. **The hardware and software pieces are the same; the configuration, workflows, and outputs differ.** Brownfield Modernization is mostly Pillar 1 (Connectivity & Edge) + Pillar 5 (Operational Intelligence). Predictive Maintenance is mostly Pillar 4 (Condition Monitoring) + Pillar 5. Multi-site Operations is Pillar 3 (Asset Intelligence) + Pillar 5 with cross-site aggregation. If you'd rather understand the building blocks than the outcomes, the 5-pillar view is at `/capabilities`.

---

### 3.4 Section 4 — Cross-lens navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions` hub** (design-system v3 §17 line 558 — "Per-surface link presets (locked per memo v2 §5.2)"):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | PLATFORM | Looking for the full vendor evaluation? | `/platform` |
| 2 | CAPABILITIES | The 5 building blocks every solution composes from | `/capabilities` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking for the same thing from another angle?

*Note:* CONTACT-style conversion destinations belong in `CTASection` (§3.5 below) — NOT in `CrossLensBlock`. The §17 angle enum (line 523) is restricted to `'platform' | 'capabilities' | 'architecture' | 'solutions' | 'security'`; CONTACT is not a permitted angle.

---

### 3.5 Section 5 — Final CTA

Per cross-buyer CTA preference (the hub speaks to all buyers; per-buyer-tuned CTAs are handled by each `/solutions/<solution>` page):

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us the outcome you're trying to deliver. We'll scope which solution fits.
>
> PRIMARY CTA: Talk to an engineer
> HREF: `/contact?intent=solutions-engineering`
>
> SECONDARY CTA: Request an architecture review
> HREF: `/contact?intent=architecture-review`

---

## 4. Components used

All from design-system v3 LOCKED — no new components.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.5 final CTA |
| `CapabilityCard` (solution-variant, with status-pill prop for "coming in Phase 2") | §3.2 grid (7 cards) |
| `CapabilityCard` (cross-lens variant) | §3.4 cross-lens (3 cards) |
| `CTASection` | §3.5 final CTA |
| §17 cross-lens content pattern | §3.4 cross-lens |

**Note on `CapabilityCard` status-pill prop:** the status-pill ("coming in Phase 2") is rendered top-right corner of the card in `size.sm brand-teal italic`. Existing `CapabilityCard` per design-system v3 §3 supports a `statusPill` prop variant. Cards 1 (Edge Connectivity) + 2 (Predictive Maintenance) use this prop; Cards 3-7 do not.

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.5. **~530 words total** (within 300-600 hub-page target per `/capabilities` hub spec §9 page-type guidance). Slight increase from v1 baseline (~520 words) reflects M1 E-IDOS positioning fix (+5 words) + J2 pre-live link policy markup (+5 words) − M3 OEM verbatim swap (net neutral).

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~100 |
| 3.2 | 7-solution card grid | ~290 (~40/card incl. eyebrow + title + body) |
| 3.3 | Bridge paragraph | ~60 |
| 3.4 | Cross-lens | ~30 |
| 3.5 | Final CTA | ~40 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21:

| Don't | Why |
|---|---|
| Duplicate per-solution detail on the hub cards | Hub-page guidance per §9: each card is a one-paragraph descriptor + link to the per-solution page. Detail belongs on `/solutions/<solution>`, not here. |
| Add proof / metrics / customer logos on the hub | Per `/capabilities` hub spec §9: hub pages have no embedded trust signaling. Trust cues live on per-solution pages (`SolutionPanel` §16 trust cue), `/security`, `/platform`. |
| Reorder cards by alphabetical | Card order is LOCKED: most-cross-pillar + most-asked first (Edge Connectivity, Predictive Maintenance, Multi-site, Brownfield), then vertical-specific (CNC Machining, Precision Manufacturing, OEM Machine Monitoring). Alphabetical would put Brownfield first and bury Edge Connectivity / Predictive Maintenance — the two NEW Phase 2 solutions the wave depends on. |
| Reframe solutions as capability stories | The hub is OUTCOME-organized. Capability-organized framing belongs at `/capabilities`. The bridge paragraph (§3.3) explicitly invites readers wanting the capability view to switch to `/capabilities` — this is the cross-lens bridge, not a duplication invitation. |
| Add inline FAQ on the hub | Per `/capabilities` hub spec §9 per-page-type FAQ governance: hub pages are NO. Per-solution questions live on each `/solutions/<solution>` page's inline FAQ (per `SolutionPanel` design-system v3 §15). |
| Add industry-specific cards | Per phase2-ia-scope-memo amendment v3 §1.4 + §2: industries are Phase 3. The Phase 2.5 single-industry exception is for `/industries/<industry>` route — NOT a card on the `/solutions` hub. |
| Hide the "(coming in Phase 2)" pill on Cards 1-2 | Honest forward-looking framing per the v1-structure-decision lock. The 2 new Phase 2 solutions are scheduled (steps 9-10); marking them honestly preserves trust if the hub ships before those specs lock. |
| Add a 4-pillar / 6-pillar framing in the bridge paragraph | The 5-pillar model is LOCKED per positioning v3. The bridge paragraph references the 5-pillar view — must stay 5, not drift to 4 or 6. |
| Replace "Talk to an engineer" with "Book a demo" in the final CTA | Per cross-buyer vocabulary discipline — "book a demo" is wrong for OT Architects (per buyer-taxonomy §2.3) and reads as marketing-speak to most operations buyers. |
| Use internal roadmap language ("Phase 2", "Phase 3", "Phase E", "Q2", etc.) in public-facing card status pills | Public visitors don't understand internal phase labels; reads as a roadmap artifact, not a polished website. Use "Coming soon" or a concrete timeline like "Available H2 2026" for public pills. Internal phase references stay in spec metadata + commit messages. |
| Link a card's primary destination to a route that is not live | Prevents dead-route / placeholder-route UX. Per the LOCKED pre-live link policy in §3.2: until `/solutions/edge-connectivity` and `/solutions/predictive-maintenance` ship, Cards 1-2 link to the underlying capability (`/capabilities/connectivity-edge` and `/capabilities/condition-monitoring` respectively). When the destination ships, the link swaps to the solution page. |
| Place conversion destinations inside the `CrossLensBlock` (§3.4) | `CrossLensBlock` is exploration navigation across the five locked lens angles (`platform | capabilities | architecture | solutions | security` per design-system v3 §17 line 523). Conversion / contact / "talk to us" destinations belong in `CTASection` (§3.5) — the design-system §8 conversion primitive. Co-opting cross-lens slots for conversion double-violates §17 (off-enum angle + locked-preset replacement). |
| Misstate the underlying capability domain of a solution card | Every solution card body must accurately describe the underlying capability per the LOCKED source-of-truth (`/capabilities/<pillar>` spec or `hardware-ecosystem-map-v3.md`). Examples of past errors caught by source-of-truth verification: positioning E-IDOS as "thermal/electrical condition signatures" (incorrect — it's hydraulic + lubrication oil-health per locked condition-monitoring spec); claiming "no truck rolls" for OEM cards (incorrect — locked OEM v2 spec deliberately hedges with "cut truck rolls on remote-diagnosable issues"). When in doubt, quote the source-of-truth verbatim. |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 300-600 word target (current: ~530 words)
- [x] All 5 sections present per the approved hub structure (§2)
- [x] §3.2 grid has 7 cards in the LOCKED order (Edge Connectivity → Predictive Maintenance → Multi-site → Brownfield → CNC Machining → Precision Manufacturing → OEM Machine Monitoring)
- [x] Cards 1 (Edge Connectivity) + 2 (Predictive Maintenance) display the "Coming soon" status pill (public-facing — internal Phase-2-step references stay in spec metadata only)
- [x] Cards 1-2 use the LOCKED pre-live link policy (link to underlying capability page until solution page ships)
- [x] Cards 3-7 link to their existing v2 solution pages (LOCKED from Phase 1 marketing track)
- [x] Card body copy is ~40 words each — descriptor-only, not detail
- [x] §3.3 bridge paragraph references the 5-pillar view at `/capabilities` (not 4, not 6)
- [x] §3.4 cross-lens cards match the LOCKED design-system v3 §17 preset for `/solutions` hub: PLATFORM + CAPABILITIES + ARCHITECTURE (NOT CONTACT — CONTACT is not in the §17 angle enum)
- [x] Final CTA uses cross-buyer-safe framings ("Talk to an engineer" + "Request an architecture review")
- [x] No vocabulary that backfires per cross-buyer review (no *"easy"* / *"seamless"* / *"intuitive"* / *"future-proof"* / *"transformation"* / *"end-to-end"*)
- [x] No customer logos, no fabricated metrics, no competitor names, no certification claims
- [x] No inline FAQ (hub-page per-page-type FAQ governance: NO)
- [x] All components are design-system v3 LOCKED (`CapabilityCard.statusPill` prop is existing, not new)
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] **v2 M1 applied** — Card 2 E-IDOS positioning corrected (hydraulic + lubrication oil-health per locked condition-monitoring spec; "thermal/electrical" was fabrication)
- [x] **v2 M2 applied** — Cross-lens block matches design-system v3 §17 LOCKED preset (CONTACT card removed; PLATFORM card added)
- [x] **v2 M3 applied** — Card 7 OEM uses v2 verbatim ("cut truck rolls on remote-diagnosable issues") instead of harder "no truck rolls for tag changes" invented phrase
- [x] **v2 J1 applied** — Public-facing status pill uses "Coming soon" instead of internal "Coming in Phase 2"
- [x] **v2 J2 applied** — Pre-live link policy locked for Cards 1-2 (link to underlying capability page until solution page ships)
- [x] **v2 J5 applied** — 4 new defensive anti-patterns added to §6 (internal-roadmap-language guard; dead-route guard; cross-lens conversion guard; capability-domain misstatement guard)

---

## 8. Out of scope for v1

- **Per-solution detail.** Each `/solutions/<solution>` page is its own LOCKED v2 spec (Phase 1 marketing track) or upcoming Phase 2 step 9-10 spec (`/solutions/predictive-maintenance` + `/solutions/edge-connectivity`).
- **Industry-specific framings.** Phase 3 `/industries/<industry>` (or the Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2).
- **Trust signaling (proof, metrics, logos).** Per `/capabilities` hub spec §9 — hub pages have no embedded trust signaling.
- **Per-buyer CTA tuning.** The hub is cross-buyer (uses cross-buyer-safe framings). Per-buyer-tuned CTAs ("Bring us your fleet" for Plant managers, "Bring us your installed base" for OEMs) belong on each `/solutions/<solution>` page.
- **Capability-organized framing.** Belongs at `/capabilities`. The bridge paragraph (§3.3) cross-references rather than duplicates.
- **Inline FAQ.** Per §9 per-page-type FAQ governance: hub pages are NO. Per-solution questions live on `/solutions/<solution>` pages.
- **Vertical landing pages.** Phase 3 `/industries/<industry>` or Phase 2.5 single-industry exception.
- **Pricing / commercial engagement.** Phase 2 step 11 `/platform` covers commercial-engagement teaser; Phase 3 `/pricing` covers detailed pricing.

---

*`/solutions` hub Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review + 4-agent source-of-truth verification workflow. Seventh per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 8. Hub page (5 sections, ~530 words within 300-600 hub-page target per §9 page-type guidance). **v2 changes from v1:** M1 E-IDOS positioning fix (hydraulic+lubrication, not thermal/electrical — workflow-verified against locked condition-monitoring spec); M2 cross-lens preset compliance (PLATFORM+CAPABILITIES+ARCHITECTURE per design-system v3 §17 LOCKED preset, removing CONTACT card violation); M3 OEM card uses v2 verbatim ("cut truck rolls on remote-diagnosable issues" — workflow upgraded ChatGPT's softer recommendation to v2 verbatim); J1 public-facing "Coming soon" pill; J2 pre-live link policy (link to underlying capability until solution page ships); J5 4 new defensive anti-patterns. **Side-flag:** PR #72 (condition-monitoring) is LOCKED but not yet merged to master; hub spec cross-links it as authoritative for the M1 fix. Verify PR #72 merge status before publishing the hub. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.2 + §2.3 + §2.4 + §2.5 + §2.6 (cross-buyer hub), proof-architecture v1, design-system v3 §3 (CapabilityCard.statusPill) + §17 (cross-lens LOCKED preset for /solutions hub: line 558), page-capabilities-hub-spec-v1 §9, page-capabilities-condition-monitoring-spec-v1 (source-of-truth for E-IDOS positioning), solution-oem-machine-monitoring-v2 (source-of-truth for OEM truck-rolls vocabulary), positioning v3, hardware-ecosystem-map v3 §5.2 (E-IDOS oil-health framing).*
