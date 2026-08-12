<!--
File:        docs/marketing/phase2-ia-scope-memo-v2.md
Purpose:     Locked Phase 2 IA / scope memo. Resolves the nav-conceptual-
             overlap concern (flagged in web-platform-roadmap-v2 §3 +
             homepage-spec v2 §1.1.7) BEFORE any of the 10 Phase 2 page
             specs are written.
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
Version:     v2 — LOCKED after Pass 1 ChatGPT review
Date:        2026-05-28

v1 → v2 changes (9 additive items; no structural rewrite):
  - §4 — added "Authoritative explanation" invariant (§4.0) as the
         foundational rule the anti-duplication map enforces
  - §4 table — added deployment-model rows (topology vs commercial
         engagement); added proof/evidence ownership rows (case studies,
         outcome proof in context, trust anchors)
  - §5 — added §5.4 standalone landing-page invariant (every page
         stands alone; cross-links are enrichment, not required reading)
  - §5 — added §5.5 /security as cross-cutting trust surface (not just
         "another page")
  - §5 — added §5.6 future-route governance (reserve /industries/*,
         /regions/*, localization paths now even if implementation is
         Phase 3+)
  - §5 — added §5.7 SEO intent-cluster ownership (which query types
         primarily own which surfaces — prevents copy-driven SEO drift)
  - §7 — added Q6 canonical buyer taxonomy (navigation, CTA phrasing,
         proof selection, page tone all depend on it)
  - §7 — added Q7 commercial-packaging/licensing surface (where
         the commercial engagement model gets briefly explained, even
         though detailed pricing is Phase 3)

Per ChatGPT v1 review: "With those additions, the memo becomes strong
enough to govern the entire Phase 2 spec wave without conceptual
drift." v2 is LOCKED as the parent IA document for Phase 2 per-page
specs.

v1 (phase2-ia-scope-memo-v1.md) retained as historical reference.
-->

# Phase 2 IA / Scope Memo — Reconciliation v2 (LOCKED)

**Resolves the nav-conceptual-overlap risk flagged at homepage lock. Locks the 10-page Phase 2 scope. Defines reusable layouts. Recommends spec sequencing. v2 incorporates the 9 additions from the v1 ChatGPT review pass.**

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
- Sections: identity statement, the 5 capability pillars at-a-glance, the three competitive frames (vs gateway vendors / vs IIoT analytics / vs point condition-monitoring), trust posture, credibility anchors (defense / space-agency / geography), AMC channel, commercial engagement model (brief — see §7 Q7), CTA
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
- "Want the trust posture in detail?" → `/security`
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
- Trust posture relevant to this pillar (offline operation, audit trail, AI constraint as applicable) — with a cross-link to `/security` for the canonical trust philosophy
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
- "Trust posture for this pillar" → `/security` (relevant section)
- "Related solutions" → relevant `/solutions/<solution>` pages
- "All pillars" → `/capabilities`
- Discovery-call CTA

### 3.4 `/architecture` — HOW (technical / data-flow walkthrough)

**IS:**
- The Industrial Intelligence Stack walkthrough — the technical reader's surface
- Interactive architecture diagram (zoom states, optional hover annotations per roadmap v2 §3 — see §7 Q3 for scope confirmation)
- Layer-by-layer explanation: field signals → acquisition → connectivity → condition monitoring → operational intelligence → consumers
- Where each product fits architecturally
- **Deployment topology patterns** — cloud / on-prem / edge-only / hybrid / air-gapped (architectural variants; commercial engagement lives in `/platform`)
- Architecture security mechanics — how trust properties manifest in the data path (offline operation, audit chain, AI constraint boundaries, per-tag quality codes). Security *philosophy* lives in `/security`; this page explains the architectural *mechanics*.
- Integration patterns: MQTT publish, OPC UA Server, how external SCADA/MES read from us
- The page a technical evaluator screenshots and pastes into an internal architecture-review slide
- Length: medium-long, dense, technical. 1,200-1,800 words

**IS NOT:**
- A platform identity summary (`/platform`)
- A buyer-organised capability map (`/capabilities`)
- A solution outcome story (`/solutions`)
- A security-only page (`/security` exists separately for the trust *philosophy*; this page covers the architectural *mechanics*)
- A product detail page

**Cross-links out:**
- Each architectural layer → the corresponding pillar page (`/capabilities/<pillar>`)
- "Security posture philosophy" → `/security`
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
- Customer outcomes — what they see when they deploy (outcome proof *in context* — see §4 ownership)
- Architecture: annotated diagram showing the data flow for this solution specifically
- Trust cues relevant to this solution — short, persistent, with cross-link to `/security` (security is a cross-cutting trust surface per §5.5)
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
- "Trust posture" → `/security`
- "Other solutions" → `/solutions`
- Discovery-call CTA

**Why these two solutions specifically:**
- **Predictive maintenance** — the highest-value Phase 2 commercial moment. The new condition-monitoring buyer (Maintenance Manager + AMC) is a new audience the homepage trust band and proof band don't yet serve directly. This page is where that audience lands and converts.
- **Edge connectivity** — the most architecturally clear solution; lowest creative risk; establishes the depth-example pattern that the other 5 solution pages can inherit when they bump in Phase E.

---

## 4. Content boundaries — what lives WHERE (anti-duplication map)

### 4.0 The foundational invariant (new in v2)

Before the map itself:

> **A page may summarize another surface's content, but may not become a second authoritative explanation of it.**

This is the rule the entire anti-duplication map enforces. It sounds obvious; without explicit statement, future copywriters will slowly duplicate content "for convenience" — once a piece of content has been written twice, the third copy is irresistible, and within a year four pages explain the 5 pillars with subtly drifting wording.

**Applied:** `/platform` may *summarize* the 5 pillars in one paragraph; `/capabilities/<pillar>` is *authoritative* for each one. `/architecture` may *summarize* the trust posture; `/security` is *authoritative*. `/solutions/<solution>` may *summarize* the canonical OEE outcome metrics; the Operational Intelligence pillar page is *authoritative*.

When you find yourself writing more than three sentences on a topic that lives elsewhere as primary, stop and cross-link instead.

### 4.1 The primary-home map

A specific piece of content should appear in only one place as primary, and be cross-linked from the others.

| Content | Primary home | Referenced from |
|---|---|---|
| Vendor identity statement | `/platform` §1 | Homepage hero (compressed), `/company` (Phase 3) |
| Five capability pillars at-a-glance | `/capabilities` hub | `/platform`, homepage, `/architecture` (as nav cue) |
| Each pillar's deep description | `/capabilities/<pillar>` | `/architecture` (one-line per layer), `/solutions/<solution>` (one-line per contributing pillar) |
| Individual product detail | (Phase E product pages) | `/capabilities/<pillar>` (short descriptions), `/solutions/<solution>` (short mentions) |
| Industrial Intelligence Stack diagram (interactive) | `/architecture` | Homepage (static), `/platform` (static), each `/solutions/<solution>` (annotated subset) |
| Three competitive frames | `/platform` §3 (and in the sales objection guide internally) | NOT on other surfaces — competitive framing is platform-level |
| **Deployment topology patterns** (cloud / on-prem / edge / hybrid / air-gapped) | **`/architecture`** | `/platform` (compressed), `/capabilities/connectivity-edge` (one-line) |
| **Commercial deployment engagement** (how Elpis deploys with you, AMC channel, partner model) | **`/platform`** | Each capability page (one-line where relevant), `/solutions/<solution>` (cross-link only) |
| Trust posture philosophy | `/security` | `/platform`, `/architecture`, each capability page (as relevant), each solution depth-example |
| Architecture security mechanics | `/architecture` (the "how" — data path, audit chain, AI boundaries) | `/security` (cross-link), `/capabilities/<pillar>` (where relevant) |
| Procurement / compliance review workflow | `/security` | NOT on other surfaces |
| Product-specific hardening posture | (Phase E product pages) | `/security` (cross-link only) |
| Defense / space-agency credibility | Homepage trust band + `/platform` §4 + `/customers` (Phase 3) | NOT on capability or solution pages — credibility is platform-level |
| AMC channel | `/platform` + `/capabilities/condition-monitoring` (where the buyer lives) + `/solutions/predictive-maintenance` (where the use-case lives) | NOT on other surfaces |
| **Customer case studies / detailed proof** | **`/customers`** (Phase 3) | `/platform` (one-line teasers), `/solutions/<solution>` (one-line outcome quotes once available) |
| **Outcome proof in context** (what a deployment achieves for THIS solution) | **`/solutions/<solution>`** | Homepage outcomes strip (compressed), `/capabilities/<pillar>` (one-line as relevant) |
| **Trust anchors** (defense / space-agency / AMC / geography) | **`/platform`** §4 | Homepage trust band, `/customers` (Phase 3) |
| OEE / downtime / audit outcome metrics | `/solutions/<solution>` pages (outcome-organised, in context) | Homepage outcomes strip (compressed), `/capabilities/operational-intelligence` (as capability outcome) |
| Per-product certifications (CE / UL / FCC / IP) | (Phase E product pages — open question) | NOT in Phase 2 |
| Pricing detail (license tiers, plant pricing, AMC pricing) | `/pricing` (Phase 3) | Brief commercial-engagement teaser on `/platform` only — see §7 Q7 |

**The discipline:** if a page is tempted to add content from another page's primary-home cell, the right move is a cross-link, not a duplicate.

---

## 5. Cross-page navigation logic

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

### 5.4 The standalone landing-page invariant (new in v2)

> **Every Phase 2 page must stand independently as a coherent landing page. Cross-links are enrichment, not required reading.**

Many industrial visitors land on a single page (from a search query, an ad, a sales referral, an emailed link) and never click anywhere else on the site. The maintenance manager who lands on `/solutions/predictive-maintenance` from a Google search may never visit `/architecture` or `/platform`. That is operationally normal.

**Applied:**
- Each page answers its primary reader question fully, without requiring the visitor to leave for context
- Cross-links to "see this from another angle" are *enrichment* — they exist for the curious reader, not the typical reader
- No page assumes the visitor read the homepage first
- No page assumes the visitor will click further

This invariant prevents future specs from becoming over-stitched ("see `/capabilities/condition-monitoring` for the full story") at the cost of standalone usability. The page must be the full story for its primary reader; deeper or different-angle exploration is a bonus.

### 5.5 `/security` as a cross-cutting trust surface (new in v2)

Security is not just another Phase 2 page in a flat list. It is a **cross-cutting trust primitive** that every other Phase 2 surface must reference.

**Why:** industrial buyers increasingly evaluate cyber posture, offline operation, auditability, and OT-boundary discipline *before* features. A visitor on `/capabilities/condition-monitoring` who can't immediately see a trust cue may bounce before reading the capability detail. A technical reviewer on `/architecture` needs to see security architecture is treated as first-class, not appended.

**Applied across Phase 2:**

| Surface | Security treatment |
|---|---|
| `/platform` | Trust posture trio summarized; "Read the full trust posture" cross-link to `/security` |
| `/architecture` | Architecture security mechanics (data-path, audit chain, AI boundaries) — these live HERE as primary, with cross-link to `/security` for trust philosophy |
| `/capabilities/<pillar>` | One-paragraph trust posture relevant to this pillar (offline operation, audit, AI as applicable); cross-link to `/security` for the canonical philosophy |
| `/solutions/predictive-maintenance` | Trust cue: condition data sovereignty, customer-controlled telemetry; cross-link to `/security` |
| `/solutions/edge-connectivity` | Trust cue: offline-first operation, no forced cloud dependency; cross-link to `/security` |
| `/security` | Authoritative for: trust posture philosophy, procurement/compliance review workflow, regulated-industry deployment posture |

**The discipline:** security cues are never decorative. Each page's security cue must be specifically relevant to that page's primary question. `/architecture` cues focus on mechanics; capability pages cue on pillar-specific trust properties; solution pages cue on use-case-specific data sovereignty.

### 5.6 Future-route governance (new in v2)

Phase 2 builds 10 pages. Phase 3+ adds significantly more (industries, regions, customer stories, resources). The Phase 2 IA must reserve route governance now to avoid retrofit pain later.

**Reserved route patterns (governance-locked, implementation-deferred):**

```
/industries/<industry>          Phase 3 — automotive, pharma, oil-and-gas, defense,
                                aerospace, heavy-manufacturing, and per positioning v3 §8
                                additional industries
/regions/<region>               Phase 3+ — initially India, Middle East, then US
/customers                      Phase 3 — case studies, defense/space-agency anchor,
                                AMC partner stories
/resources/<type>               Phase 3 — datasheets, brochures, whitepapers
/resources/<type>/<asset>       Phase 3 — individual downloadable assets
/calculators/<calculator>       Phase 4 — OEE uplift, downtime cost, brownfield TCO
/docs/<section>                 Phase 4 — documentation portal
/partners/<area>                Phase 4 — partner enablement portal (gated)
/customer-portal/<area>         Phase 4 — customer asset library (gated)
```

**Localization-readiness (governance-locked):**
- URL pattern decision: `/<language>/<route>` (subdirectory) preferred over subdomain (`<language>.elpisitsolutions.com`) — better for SEO consolidation and analytics simplicity
- Default language: English (root, no prefix — `/platform` serves English-default)
- Locked future languages (reserved order): Japanese (Fanuc customer base), German (Siemens customer base), Mandarin (Brother + China market), Arabic (Middle East market)
- Phase 2 implementation: English-only; no i18n scaffolding (per web-platform-roadmap v2 §1.2)
- Phase 4 implementation: i18n scaffolding (per roadmap §5)

**Why reserve now:** if Phase 2 ships with absolute paths assuming English-default (e.g. hardcoded `/platform` links in CMS templates), retrofit to `/<lang>/platform` becomes a router-level change touching every Phase 2 component. Reserve the pattern now; defer the implementation.

### 5.7 SEO intent-cluster ownership (new in v2 — optional governance)

Search-engine query intent should map to the same primary-home cells as the §4 anti-duplication map. Without this discipline, copy-driven SEO eventually re-creates content cannibalisation through keyword targeting:

| Query intent | Primary surface |
|---|---|
| *"industrial intelligence ecosystem"* / *"industrial intelligence platform"* | `/platform` |
| *"industrial edge gateway"* / *"PLC-to-cloud gateway"* | `/capabilities/connectivity-edge` |
| *"sensor data acquisition"* / *"PLC-bypass acquisition"* | `/capabilities/data-acquisition` |
| *"asset tracking OEE"* / *"equipment utilization tracking"* | `/capabilities/asset-intelligence` |
| *"vibration condition monitoring"* / *"hydraulic oil monitoring"* | `/capabilities/condition-monitoring` |
| *"multi-tenant OEE platform"* / *"industrial alarm management"* | `/capabilities/operational-intelligence` |
| *"IIoT architecture"* / *"OT data flow"* / *"industrial data layer"* | `/architecture` |
| *"predictive maintenance platform"* / *"break-fix to predict-prevent"* | `/solutions/predictive-maintenance` |
| *"edge connectivity solution"* / *"industrial data unification"* | `/solutions/edge-connectivity` |
| *"OT security posture"* / *"offline-capable industrial software"* | `/security` |
| *"elpis company"* / *"elpis founders"* | `/company` (Phase 3) |
| *"elpis pricing"* / *"industrial intelligence cost"* | `/pricing` (Phase 3) |

**The discipline:** when a Phase 2 page spec is tempted to target a keyword that primarily belongs to another surface, it should defer. SEO competition between Elpis pages weakens all of them.

This table is governance-only — actual SEO optimisation (meta tags, schema, content depth) is a separate per-page concern handled within each page spec.

---

## 6. Recommended spec sequencing

Per roadmap v2 §3, each Phase 2 page goes through v1 → ChatGPT review → v2. Order matters because some pages depend on others' decisions.

**Recommended sequence:**

1. **This memo (IA / scope) — v1 → review → v2.** Lock decisions before any page spec begins. **(v2 = this document)**
2. **`design-system-v3`** — formalise `CapabilityDeepDive`, `ArchitecturePanel` (Phase 2 interactive variant), `SolutionPanel` components. Phase 2 page specs reference these components by name. **(Timing — see §7 Q5 below; user decision)**
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
6. **Canonical buyer taxonomy — lock now? (new in v2).** Navigation, CTA phrasing, proof selection, page tone, solution examples, form routing all depend on the buyer model. The hardware-ecosystem-map v3 §8 names buyer personas (Plant manager, Industrial IT/OT, Maintenance Manager, AMC provider, OEM, Quality/Compliance, Plant engineer) — should this be promoted to a standalone taxonomy doc, or referenced inline in the per-page specs? Recommended lightweight version (table form):

   | Buyer | Primary Phase 2 surface | Secondary |
   |---|---|---|
   | CTO / CIO | `/platform` | `/security` |
   | Plant manager / Ops VP | `/solutions/*` (outcome) | `/capabilities/operational-intelligence` |
   | OT Architect / SCADA engineer | `/architecture` | `/capabilities/connectivity-edge` |
   | Maintenance Manager / AMC provider | `/solutions/predictive-maintenance` | `/capabilities/condition-monitoring` |
   | Plant engineer (retrofit / greenfield) | `/capabilities/data-acquisition` | `/architecture` |
   | OEM machine builder | `/solutions/edge-connectivity` | `/capabilities/asset-intelligence` |
   | Procurement / compliance reviewer | `/security` | `/platform` |

   Lock the table here, or promote to its own doc?

7. **Commercial-packaging / licensing surface — where does the brief explanation live? (new in v2).** Pricing detail is correctly deferred to Phase 3. But visitors *will* ask "how is this actually sold?" Recommended: `/platform` includes a short "How we engage commercially" section (1-2 paragraphs) covering: edition tiers + modular per-protocol activation; gateway-licensing model; AMC partnership channel; partner / distributor model; discovery-call as the next step. Detailed pricing stays Phase 3. Confirm this lands in `/platform`, or specify an alternative.

---

## 8. Sign-off checklist for this memo

Before the per-page specs begin:

- [ ] User reviews and approves §2 mental model (WHAT / WHY / HOW / FOR WHAT)
- [ ] User reviews and approves §3 per-page IS / IS NOT locks
- [ ] User reviews §4.0 foundational invariant ("a page may summarize but not become a second authoritative explanation")
- [ ] User reviews §4.1 anti-duplication map and confirms primary-home cells (including new deployment, proof/evidence, security mechanics rows)
- [ ] User reviews §5 cross-page navigation logic — including new §5.4 standalone-landing invariant, §5.5 `/security` as cross-cutting trust surface, §5.6 future-route governance, §5.7 SEO intent-cluster ownership
- [ ] User reviews §6 recommended sequencing and confirms order (or specifies an override)
- [ ] User answers §7 open questions (or marks specific ones as "defer to per-page spec") — including new Q6 buyer taxonomy and Q7 commercial-packaging surface
- [ ] ChatGPT review pass on this memo, take-list applied, v2 produced **(✓ — v2 = this document)**
- [ ] Memo v2 locked before any Phase 2 page spec begins

---

## 9. Out of scope for this memo

- **Per-page copy.** This memo is IA + scope only. Each Phase 2 page gets its own v1 → review → v2 spec with verbatim copy, citing this memo as parent.
- **Visual design.** Visual treatment lives in `design-system-v2.md` (and the future `design-system-v3.md`).
- **Phase E surfaces.** Pitch deck v6, datasheet v4, solution-page v3 bumps, security page v3, sales objection guide v3, ROI calc v3, hardware product pages — all Phase E, none in this memo's scope.
- **Industries pages content.** Phase 3 per positioning v3 §9.2. **Note:** route governance for `/industries/*` IS in this memo's scope (per §5.6) — only the page content itself is deferred.
- **CMS architecture.** Phase 3 per web-platform-roadmap v2 §1.2.
- **Pricing detail.** Phase 3. (Brief commercial-engagement teaser on `/platform` IS in scope — see §7 Q7.)

---

*Phase 2 IA / Scope Memo v2 — LOCKED 2026-05-28 after Pass 1 ChatGPT review. Resolves nav-conceptual-overlap before per-page specs. Adds 9 governance items (§4.0 authoritative-explanation invariant; §4.1 deployment + proof + security-mechanics primary-home rows; §5.4 standalone-landing invariant; §5.5 `/security` as cross-cutting trust surface; §5.6 future-route governance; §5.7 SEO intent-cluster ownership; §7 Q6 buyer taxonomy; §7 Q7 commercial-packaging surface). v2 lock is the trigger for Phase 2 per-page specs to begin.*
