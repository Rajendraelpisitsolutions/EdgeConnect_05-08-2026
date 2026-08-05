<!--
File:        docs/marketing/phase2-ia-scope-memo-v1.md
Purpose:     Resolve the Phase 2 nav-conceptual-overlap concern (flagged
             in web-platform-roadmap-v2 §3 + homepage-spec v2 §1.1.7)
             BEFORE any of the 10 Phase 2 page specs are written. Locks
             what each page IS / IS NOT, the reusable capability deep-dive
             layout, the solution depth-example layout, cross-page nav,
             and the spec sequencing.
Audience:    Internal — Claude (drafts the per-page specs that follow),
             user + ChatGPT (review this memo before per-page specs), the
             engineering team (so they understand the IA model that Phase
             2 components implement).
Companion:   industrial-intelligence-ecosystem-positioning-v3.md (parent
             worldview)
             web-platform-roadmap-v2.md (Phase scope)
             homepage-spec-v3.md (Phase 1 IA baseline this extends)
             design-system-v2.md (component library Phase 2 extends)
             design-governance-v1.md (discipline rules)
Version:     v1
Date:        2026-05-28

Lands BEFORE any Phase 2 page spec. The per-page specs cite this memo as
the IA / scope parent.
-->

# Phase 2 IA / Scope Memo — Reconciliation v1

**Resolves the nav-conceptual-overlap risk flagged at homepage lock. Locks the 10-page Phase 2 scope. Defines reusable layouts. Recommends spec sequencing.**

Phase 2 ships 10 pages across `/platform`, `/capabilities` (hub + 5 deep-dives), `/architecture`, and `/solutions` (hub + 2 depth examples). The roadmap explicitly flags that without IA discipline these surfaces will overlap — visitors won't know where to land, content will duplicate across pages, and the Phase 2 component library will fragment as similar UI gets recreated three different ways.

This memo locks the decisions that prevent that. It does **not** write individual page specs — each page gets its own v1 → review → v2 spec, citing this memo as parent.

---

## 1. The four conceptual surfaces and why they overlap

Phase 2 introduces four high-level surfaces that all describe the Industrial Intelligence Ecosystem from different angles:

- **`/platform`** — the platform identity, top-down summary
- **`/capabilities`** — the five-pillar capability model
- **`/architecture`** — the Industrial Intelligence Stack walkthrough
- **`/solutions`** — the outcome-organised hub

Each could plausibly contain similar content: every one of them has reason to show the 5 pillars, mention the products, embed (or reference) the architecture diagram, and link to a customer outcome.

If we don't decide explicitly what each page IS and ISN'T, three failure modes follow:

1. **Visitor confusion.** A first-time visitor lands on `/platform`, then `/capabilities`, and asks: *"didn't I just read this?"* They lose trust in the navigation.
2. **Content duplication.** The 5 pillars get described in 4 different pages with subtle wording drift. Maintaining consistency becomes a coordination tax.
3. **SEO cannibalisation.** Search engines see four pages competing for similar queries; ranking weakens across all of them instead of concentrating on the right one.

The fix is to map each surface to **one distinct primary reader question** — and discipline the page to answer only that question, deferring everything else.

---

## 2. The mental model — different angles on the same product set

The four surfaces resolve to four distinct primary questions:

| Surface | Primary question it answers | Reader's organising lens | "Verb" |
|---|---|---|---|
| **`/platform`** | *"What is Elpis as a vendor?"* | Identity, vendor evaluation | **WHAT** |
| **`/capabilities`** | *"Which capability domain do I need?"* | Capability-first navigation | **WHY** |
| **`/architecture`** | *"How does the data flow end-to-end?"* | Technical / integration reading | **HOW** |
| **`/solutions`** | *"What outcome am I trying to achieve?"* | Outcome-first navigation | **FOR WHAT** |

These four questions are distinct. A vendor-evaluation reader (board paper, RFP shortlist, sales briefing) asks WHAT. A capability-focused reader (an industrial IT lead trying to find a vibration-monitoring tool) asks WHY. A technical reader (an architect, a security reviewer, an integrator) asks HOW. An outcome-focused reader (an operations VP under OEE pressure) asks FOR WHAT.

**The same product set serves all four — but the page shape must serve the reader's organising lens, not just restate the products.**

This memo treats the four surfaces as four navigation systems over the same content graph, each optimised for a different reader. The cross-links between them are the navigation aids; the content boundaries below prevent duplication.

---

## 3. Per-page scope locks

For each Phase 2 page: what it IS, what it explicitly is NOT, and what it cross-links to instead.

### 3.1 `/platform` — WHAT (the platform-identity summit)

**IS:**
- The single canonical full-narrative summary of the Industrial Intelligence Ecosystem
- Reader leaves with: "I now understand what Elpis is, how it's organised, why it exists, and what makes it different from the alternatives"
- Sections: identity statement, the 5 capability pillars at-a-glance, the three competitive frames (vs gateway vendors / vs IIoT analytics / vs point condition-monitoring), trust posture, credibility anchors (defense / space-agency / geography), AMC channel, CTA
- Length: longer than homepage, shorter than a multi-page deep-dive. Roughly 1,500-2,000 words rendered

**IS NOT:**
- A capability deep-dive (that's `/capabilities` and its sub-pages)
- An architecture walkthrough (that's `/architecture`)
- A solution outcome story (that's `/solutions` and its child pages)
- A product detail page (those are Phase E)
- A pricing page (that's a separate Phase 3 surface)
- The homepage with more words (the homepage is the brand front door; `/platform` is the buyer-evaluation deep-read)

**Cross-links out (where readers go next):**
- "Want the capability detail?" → `/capabilities`
- "Want the technical walkthrough?" → `/architecture`
- "Want the solution for a specific outcome?" → `/solutions`
- "Want to talk to us?" → discovery-call CTA

### 3.2 `/capabilities` (hub) — WHY (capability-first navigation)

**IS:**
- The capability-organising entry point — the 5 pillars laid out as the buyer's organising question
- Each pillar gets a one-paragraph description here with a clear "explore this pillar" link to its deep-dive page
- The page itself is a directory, not a long-read
- Reader leaves with: "I now know which pillar fits my problem and I know how to dive into it"
- Length: shorter, dense, scannable. 600-900 words

**IS NOT:**
- A deep description of any single pillar (each pillar has its own page)
- The Industrial Intelligence Stack data-flow walkthrough (that's `/architecture`)
- A full vendor narrative (that's `/platform`)
- A solutions list (that's `/solutions`)

**Cross-links out:**
- Each pillar card → `/capabilities/<pillar>`
- "Looking for a complete vendor evaluation?" → `/platform`
- "Looking for solutions organised by outcome?" → `/solutions`

### 3.3 The five `/capabilities/<pillar>` deep-dive pages — shared layout

These are the five pillar deep-dives, all using one reusable layout. Per `hardware-ecosystem-map-v3.md`:

- `/capabilities/connectivity-edge` (EdgeConnect + Edge Gateway)
- `/capabilities/data-acquisition` (mDAQ)
- `/capabilities/asset-intelligence` (mTracker)
- `/capabilities/condition-monitoring` (VAS + E-IDOS)
- `/capabilities/operational-intelligence` (EREMOS V2)

**IS (each pillar page):**
- Pillar identity: the customer question this pillar answers (verbatim from hardware-ecosystem-map v3 §1)
- Product(s) inside the pillar — short descriptions, not full product pages
- What it eliminates from the customer's BOM
- Strategic adjacencies (buyer personas, industries, deployments)
- Architecture position: where this pillar sits in the Industrial Intelligence Stack
- Trust posture relevant to this pillar (offline operation, audit trail, AI constraint as applicable)
- Cross-links to related solutions
- CTA

**IS NOT (each pillar page):**
- A full product detail page for any product (those are Phase E)
- A repeat of `/platform` or `/architecture`
- An outcome-organised solution story (that's `/solutions/<solution>`)
- A pricing page

**Why a reusable layout matters:**
- Engineering builds one `CapabilityDeepDive` component, used 5 times with content props
- Visual consistency: the visitor learns the pattern on the first pillar page and reads the next 4 faster
- Spec efficiency: the layout is defined once in this memo; each pillar gets a content-only spec, not a layout spec

**Cross-links out (from any pillar page):**
- "Architecture detail" → `/architecture`
- "Related solutions" → relevant `/solutions/<solution>` pages
- "All pillars" → `/capabilities`
- Discovery-call CTA

### 3.4 `/architecture` — HOW (technical / data-flow walkthrough)

**IS:**
- The Industrial Intelligence Stack walkthrough — the technical reader's surface
- Interactive architecture diagram (zoom states, optional hover annotations per roadmap v2 §3)
- Layer-by-layer explanation: field signals → acquisition → connectivity → condition monitoring → operational intelligence → consumers
- Where each product fits architecturally
- Trust architecture notes — offline operation, audit chain, AI constraint, per-tag quality codes
- Integration patterns: MQTT publish, OPC UA Server, how external SCADA/MES read from us
- The page a technical evaluator screenshots and pastes into an internal architecture-review slide
- Length: medium-long, dense, technical. 1,200-1,800 words

**IS NOT:**
- A platform identity summary (`/platform`)
- A buyer-organised capability map (`/capabilities`)
- A solution outcome story (`/solutions`)
- A security-only page (`/security` exists separately)
- A product detail page

**Cross-links out:**
- Each architectural layer → the corresponding pillar page (`/capabilities/<pillar>`)
- "Security posture detail" → `/security`
- "Solutions built on this architecture" → `/solutions`
- Discovery-call CTA

### 3.5 `/solutions` (hub) — FOR WHAT (outcome-first navigation)

**IS:**
- The outcome-organising entry point — solutions laid out as the buyer's outcome question
- Phase 2 ships 2 depth examples (predictive-maintenance, edge-connectivity) + a hub that previews the 5 existing solution pages (CNC machining, brownfield, multi-site, OEM, precision manufacturing) that bump to v3 in Phase E
- Each solution gets a card with one-line outcome + "read the solution" link
- Length: shorter, directory-style. 500-800 words

**IS NOT:**
- A long-read about any single solution (each solution has its own page)
- A platform summary (`/platform`)
- A capability-organised map (`/capabilities`)

**Cross-links out:**
- Each solution card → `/solutions/<solution>`
- "Looking for the capability instead of the outcome?" → `/capabilities`

### 3.6 The two `/solutions/<solution>` depth examples — shared layout

Phase 2 ships two new solution depth examples that establish the post-ecosystem-reframe pattern:

- `/solutions/predictive-maintenance` — anchors the new Condition Monitoring buyer (Maintenance Manager + AMC provider). Draws from VAS + E-IDOS + EREMOS V2 + Connectivity & Edge
- `/solutions/edge-connectivity` — anchors the Connectivity & Edge pillar as a solution narrative. Draws from EdgeConnect + Edge Gateway primarily, with EREMOS V2 as downstream

The 5 existing solution pages (CNC machining v2, brownfield v2, multi-site v2, OEM v2, precision manufacturing v2) keep their current shape until Phase E bumps them to v3 with the new ecosystem framing.

**IS (each solution depth-example page):**
- Hero: the outcome (not the products) — "Move from break-fix to predict-and-prevent" / "Bring every controller's data into one operational view"
- The customer pain — narrative section, 2-3 paragraphs
- The Elpis approach: which pillars/products contribute, how they work together for this outcome
- What's included — bulleted feature list filtered to this solution
- Common questions raised by this buyer
- Customer outcomes — what they see when they deploy
- Architecture: annotated diagram showing the data flow for this solution specifically
- Final CTA
- Length: ~1,500-1,800 words (matching the existing solution-page template established in CNC v2)

**IS NOT (each solution depth-example page):**
- A capability deep-dive (those live under `/capabilities/<pillar>`)
- A platform summary (`/platform`)
- A product detail page (Phase E)
- A re-positioning of an existing v2 solution page — those bump in Phase E, not Phase 2

**Cross-links out:**
- "Capability detail" → relevant `/capabilities/<pillar>` page(s)
- "Architecture for this solution" → `/architecture`
- "Other solutions" → `/solutions`
- Discovery-call CTA

**Why these two solutions specifically:**
- **Predictive maintenance** — the highest-value Phase 2 commercial moment. The new condition-monitoring buyer (Maintenance Manager + AMC) is a new audience the homepage trust band and proof band don't yet serve directly. This page is where that audience lands and converts.
- **Edge connectivity** — the most architecturally clear solution; lowest creative risk; establishes the depth-example pattern that the other 5 solution pages can inherit when they bump in Phase E.

---

## 4. Content boundaries — what lives WHERE (anti-duplication map)

A specific piece of content should appear in only one place as primary, and be cross-linked from the others. The map:

| Content | Primary home | Referenced from |
|---|---|---|
| Vendor identity statement | `/platform` §1 | Homepage hero (compressed), `/about` (Phase 3) |
| Five capability pillars at-a-glance | `/capabilities` hub | `/platform`, homepage, `/architecture` (as nav cue) |
| Each pillar's deep description | `/capabilities/<pillar>` | `/architecture` (one-line per layer), `/solutions/<solution>` (one-line per contributing pillar) |
| Individual product detail | (Phase E product pages) | `/capabilities/<pillar>` (short descriptions), `/solutions/<solution>` (short mentions) |
| Industrial Intelligence Stack diagram (interactive) | `/architecture` | Homepage (static), `/platform` (static), each `/solutions/<solution>` (annotated subset) |
| Three competitive frames | `/platform` §3 (and in the sales objection guide internally) | NOT on other surfaces — competitive framing is platform-level |
| Trust posture trio | `/security` page (primary) | `/platform`, `/architecture`, each capability page (as relevant) |
| Defense / space-agency credibility | Homepage trust band + `/platform` §4 + `/customers` (Phase 3) | NOT on capability or solution pages — credibility is platform-level |
| AMC channel | `/platform` + `/capabilities/condition-monitoring` (where the buyer lives) + `/solutions/predictive-maintenance` (where the use-case lives) | NOT on other surfaces |
| OEE / downtime / audit outcome metrics | `/solutions/<solution>` pages (outcome-organised) | Homepage outcomes strip (compressed), `/capabilities/operational-intelligence` (as capability outcome) |
| Per-product certifications (CE / UL / FCC / IP) | (Phase E product pages — open question) | NOT in Phase 2 |

**The discipline:** if a page is tempted to add content from another page's primary-home cell, the right move is a cross-link, not a duplicate.

---

## 5. Cross-page navigation logic

Beyond the per-page cross-links, two patterns shape how visitors move through Phase 2:

### 5.1 The four entry-question pattern

A visitor arriving from search, a sales referral, or an ad lands on one of:
- The homepage → routes to /platform, /capabilities, /architecture, or /solutions depending on what they want next
- Directly into /capabilities/<pillar> from a capability-specific search query (e.g. *"vibration condition monitoring"* → `/capabilities/condition-monitoring`)
- Directly into /solutions/<solution> from an outcome-specific query (e.g. *"predictive maintenance for hydraulics"* → `/solutions/predictive-maintenance`)
- Directly into /architecture from a technical-evaluation context (engineer doing due diligence)

The IA respects all four entry patterns. No page assumes the visitor came from any specific predecessor.

### 5.2 The "see this from a different angle" pattern

Every Phase 2 page includes a navigation cue that offers the visitor a different organising lens on the same content:

| From | To (different angle) | Cue phrasing |
|---|---|---|
| `/platform` | `/capabilities` | "Looking for the capability detail?" |
| `/platform` | `/architecture` | "Looking for the technical walkthrough?" |
| `/platform` | `/solutions` | "Looking for the solution for a specific outcome?" |
| `/capabilities/<pillar>` | `/architecture` | "Where does this fit architecturally?" |
| `/capabilities/<pillar>` | `/solutions/<solution>` | "Related solutions" |
| `/architecture` | `/capabilities` | "Capability-first navigation" |
| `/architecture` | `/solutions` | "Solutions built on this architecture" |
| `/solutions/<solution>` | `/capabilities/<pillar>` | "Capability detail" |
| `/solutions/<solution>` | `/architecture` | "Architecture for this solution" |

This pattern is intentional — different readers want different lenses, and the navigation should respect that, not force one path.

### 5.3 Conversion path

Every Phase 2 page leads to the same primary CTA (Book a discovery call) and secondary CTA (Download the platform datasheet). Some pages also offer a tertiary CTA appropriate to the page:

- `/architecture` tertiary: *"Request a security review"* — the technical-reader audience often wants this
- `/solutions/<solution>` tertiary: *"Bring us your <specific scope>"* — mirrors the homepage final-CTA pattern
- `/capabilities/<pillar>` tertiary: *"Talk to an engineer about <pillar>"* — for the capability-specific buyer

---

## 6. Recommended spec sequencing

Per roadmap v2 §3, each Phase 2 page goes through v1 → ChatGPT review → v2. Order matters because some pages depend on others' decisions.

**Recommended sequence:**

1. **This memo (IA / scope) — v1 → review → v2.** Lock decisions before any page spec begins.
2. **`design-system-v3`** — formalise `CapabilityDeepDive`, `ArchitecturePanel` (Phase 2 interactive variant), `SolutionPanel` components. Phase 2 page specs reference these components by name.
3. **`/capabilities` hub spec.** Shortest of the four high-level surfaces; locks the pillar at-a-glance pattern that `/platform` and `/capabilities/<pillar>` both echo.
4. **One pillar deep-dive spec — `/capabilities/condition-monitoring`.** This is the most novel pillar (new buyer, new outcomes) and tests the reusable layout against the hardest content. If the layout works for Condition Monitoring it works for the other 4.
5. **Remaining 4 pillar deep-dive specs.** Each is a content-only spec, citing the locked layout from step 4.
6. **`/architecture` spec.** Depends on the pillar pages being scoped (cross-link targets); independent of `/platform` and `/solutions`.
7. **`/solutions` hub spec.** Shorter; sets the solution-card pattern.
8. **`/solutions/predictive-maintenance` spec.** Most novel solution narrative; tests the depth-example layout against the new condition-monitoring buyer.
9. **`/solutions/edge-connectivity` spec.** Lower-novelty; inherits the layout from step 8.
10. **`/platform` spec.** Written last because every section of it cross-references one of the other Phase 2 pages — writing `/platform` first risks specifying content that the cross-referenced pages haven't yet committed to.

**Note on parallelism:** the 5 pillar deep-dives (step 5) can be written in parallel once the layout locks. The 2 solutions (steps 8-9) cannot — `/solutions/edge-connectivity` should inherit lessons from `/solutions/predictive-maintenance`.

---

## 7. Open questions for the user (resolve before per-page specs)

These need user input before the per-page specs begin:

1. **`/platform` vs `/capabilities` reader split — confirm.** The memo assumes `/platform` is for vendor-evaluation readers (board, RFP, sales briefing) and `/capabilities` is for capability-first navigation readers (industrial IT lead, capability-specific search). Is that the right primary-reader split, or should one of them serve both?
2. **Phase 2 capability sub-page count — confirm 5 or fewer?** The memo locks all 5 pillar deep-dives in Phase 2 per user direction. Reconfirm — or defer one of them (e.g. `/capabilities/data-acquisition` if mDAQ is well-covered by the homepage strip) to Phase 3.
3. **Interactive `/architecture` page — what's "interactive"?** Roadmap v2 §3 says "Interactive Industrial Intelligence Stack walkthrough — diagram zoom states, optional hover annotations." Confirm what's in scope: zoom-only? hover-only? both? Engineering needs this to scope.
4. **Industries surface in Phase 2 or Phase 3?** Positioning v3 §9.2 puts `/industries/*` in Phase 3 (per the route map). Roadmap v2 §3 doesn't include industries in Phase 2. Confirm Phase 3 is correct — or pull industries forward if a specific industry page is unblocked by a sales conversation.
5. **`design-system-v3` timing — before or alongside page specs?** Homepage spec v3 §11 explicitly defers `design-system-v3` and says "Can be written before or alongside the Angular implementation." For Phase 2, the question is whether to formalise the new components first (step 2 in §6) or treat them as "static reference defines them" until the Angular team needs more.

---

## 8. Sign-off checklist for this memo

Before the per-page specs begin:

- [ ] User reviews and approves §2 mental model (WHAT / WHY / HOW / FOR WHAT)
- [ ] User reviews and approves §3 per-page IS / IS NOT locks
- [ ] User reviews §4 anti-duplication map and confirms primary-home cells
- [ ] User reviews §5 cross-page navigation logic
- [ ] User reviews §6 recommended sequencing and confirms order (or specifies an override)
- [ ] User answers §7 open questions (or marks specific ones as "defer to per-page spec")
- [ ] ChatGPT review pass on this memo, take-list applied, v2 produced
- [ ] Memo v2 locked before any Phase 2 page spec begins

---

## 9. Out of scope for this memo

- **Per-page copy.** This memo is IA + scope only. Each Phase 2 page gets its own v1 → review → v2 spec with verbatim copy, citing this memo as parent.
- **Visual design.** Visual treatment lives in `design-system-v2.md` (and the future `design-system-v3.md`).
- **Phase E surfaces.** Pitch deck v6, datasheet v4, solution-page v3 bumps, security page v3, sales objection guide v3, ROI calc v3, hardware product pages — all Phase E, none in this memo's scope.
- **Industries pages.** Phase 3 per positioning v3 §9.2.
- **CMS architecture.** Phase 3 per web-platform-roadmap v2 §1.2.

---

*Phase 2 IA / Scope Memo v1, 2026-05-28. Resolves nav-conceptual-overlap before per-page specs. Subject to user review + ChatGPT review pass per the established Phase 1 cadence. v2 lock is the trigger for Phase 2 page specs to begin.*
