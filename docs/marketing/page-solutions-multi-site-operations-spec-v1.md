<!--
File:        docs/marketing/page-solutions-multi-site-operations-spec-v1.md
Purpose:     Page spec for /solutions/multi-site-operations — solution
             depth-example for the multi-plant manufacturer running 10+
             plants under one operational umbrella. Part of the Phase E
             bulk migration of the 5 existing v2 solution pages onto the
             SolutionPanel §15 layout. Migrates solution-multi-site-
             operations-v2.md (Phase 1 page copy) into the §9 canonical
             per-page-spec format.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim copy), user + ChatGPT
             (reviewers), Phase E batch-migration authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   solution-multi-site-operations-v2.md (Phase 1 page copy being
                migrated — voice + structure precedent; SUPERSEDED by this
                spec at v3 lock; retained as voice reference)
             page-solutions-cnc-machining-spec-v1.md v1 (LOCKED PATTERN-
                SETTER — structure, §15 ecosystem-framing additions, §9
                governance, source-of-truth discipline, and the locked
                precedents P-A..P-G this spec inherits)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED —
                sister SolutionPanel exemplar; IS/IS-NOT boundary — the
                protocol-agnostic cross-vendor OT-consolidation story)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED —
                sister SolutionPanel exemplar; IS/IS-NOT boundary — the
                reliability / condition-monitoring story)
             page-capabilities-hub-spec-v1.md §9 (canonical template; FAQ
                + metadata + "How this differs from…" governance)
             page-capabilities-operational-intelligence-spec-v1.md v1
                (LOCKED — LEAD pillar source-of-truth; EREMOS V2 / OEE /
                multi-tenant aggregation)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED —
                inline-pillar source-of-truth for EdgeConnect + Edge
                Gateway positioning + the today-protocol list)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-link target
                for "See full architecture"; FAQ Q6 multi-plant-EdgeConnect
                anti-pattern is LOAD-BEARING for this page)
             page-solutions-hub-spec-v1.md v2 (LOCKED — /solutions hub
                directory this depth-example sits under)
             buyer-taxonomy-v1.md §2.2 (Plant manager / Ops VP — primary
                buyer, multi-site lens) + §2.1 (CTO / CIO — strong
                secondary, vendor-consolidation lens) + §2.3 (OT Architect
                — tertiary, served via cross-lens)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics,
                no customer names, no competitor names)
             design-system-v3.md §15 (SolutionPanel — LOCKED) + §16 (trust
                cue content pattern) + §17 (cross-lens LOCKED preset for
                /solutions/<solution>) + §5.A (ArchitecturePanel.interactive
                solution-annotated variant)
             shared-knowledge/contracts/eremos-per-tag-mqtt.md (per-gateway
                identity contract — load-bearing for this page)
             2026-06-04-phase-e-solution-migration-plan.md (the bulk-
                migration plan-trail this spec executes; D1-D6 + P-A..P-G)
Version:     v1 — LOCKED 2026-06-04 (page content v3). Drafted in the Phase E batch wave under
                the CNC pattern-setter precedents. Spec doc = v1 in the §9
                template sense; page content = v3 (SolutionPanel migration
                of the Phase 1 v1→v2 page copy).
Date:        2026-06-04
Status:      LOCKED 2026-06-04 (page content v3). Batch ChatGPT review +
                pre-lock validation workflow PASSED — per-site / per-gateway
                identity discipline verified clean in all 4 live-copy
                locations (the highest-risk check); 0 HIGH, 3 MED (word-count
                self-cert, meta-title band, §6 banned-vocab parity) applied.
                Locks + ships as one wave with the other 4.

INHERITED PATTERN-SETTER PRECEDENTS (from page-solutions-cnc-machining-
spec-v1.md; full detail in that file's header workflow note + the
migration plan-trail P-A..P-G). This page applies each as follows:
  P-A (Typical Engagement optional): INCLUDED here — multi-plant phased
     rollout + capacity-paced pacing is the core deployment-anxiety story
     for the Ops-VP / CTO buyer ("how does this scale as we acquire?").
     11-section page; the optional section is the documented over-ceiling
     reason (10-section core stays in the 1,500-1,800 band).
  P-B (whatsIncluded bucket-narrative): 2 buckets — edgeConnect (per-site
     runtime) + eremosV2 (central aggregation). hardwareProducts bucket
     OMITTED — see §3.4 preamble. The per-site / central split is the
     product narrative; the bucket structure mirrors it.
  P-C (cross-lens card leads with the DIFFERENTIATING capability): this
     page's LEAD pillar is Operational Intelligence (cross-site OEE
     consistency + fleet aggregation = the differentiator that makes the
     multi-site story unique), so the §17 cross-lens related-pillar card
     leads with Operational Intelligence; Connectivity & Edge is cross-
     linked inline in §3.3. NOTE: this is the INVERSE lead-pillar of CNC
     (CNC led the card with Connectivity & Edge because protocol coverage
     was its differentiator) — both obey the same P-C rule: lead with the
     differentiator, not the outcome. Documented in §3.10.
  P-D (trust-cue placement after Architecture): followed — Architecture →
     Trust Cue → Cross-lens → CTA, per the realized exemplar order.
  P-E (architecture-annotation eyebrow doubles as the ≤4-word title):
     followed in §3.8.
  P-F (vertical hero subhead = relevant protocol subset; full list in
     trust strip / §3.4): followed — the multi-site hero leads with the
     ARCHITECTURAL promise (per-site runtime → central aggregation), and
     the protocol breadth lives in the trust strip / §3.4, since for this
     buyer "same southbound stack at every plant" matters more than the
     specific protocol names.
  P-G (MT-LINKi → roadmap only; S7 + OPC UA Client → today): APPLIED. The
     Phase 1 v2 page listed MT-LINKi as operator-available today across
     §4/§5; this migration corrects it to a roadmap mention and adds S7 +
     OPC UA Client to the today-list. This is the same correctness fix the
     CNC pattern-setter surfaced; the multi-site v2 page carried the same
     stale claim.

LOAD-BEARING DISCIPLINE — per-gateway identity / anti-multi-plant-
EdgeConnect (CRITICAL on this page). Per locked /architecture v2.1 FAQ Q6:
one EdgeConnect runtime serving multiple plants is an ANTI-PATTERN Elpis
explicitly does not recommend. Each plant runs its OWN EdgeConnect runtime
with its own per-gateway UUID established at first start; multi-site
visibility comes from EREMOS V2 aggregating ACROSS the per-plant runtimes —
NEVER from a single multi-plant EdgeConnect. This page makes that explicit
in FOUR places (the locked-spec discipline): §3.3 ¶2 (the architectural
spine of the page), §3.5 FAQ Q2 + Q5, §3.8 architecture annotation
("PER-SITE RUNTIME, CENTRAL AGGREGATION"), and §3.9 Trust Cue 2. The Phase
1 v2 page already carried this correctly ("Persistent site identity across
the fleet"); the migration preserves and strengthens it. Getting this wrong
is a HIGH-severity error.

What the migration ADDS vs the Phase 1 v2 page copy (the four §15
ecosystem-framing additions + §9 governance additions):
  1. Pillar cross-references — §3.3 names the contributing pillars
     (Operational Intelligence LEAD + Connectivity & Edge inline) with
     inline /capabilities/<pillar> cross-links (NEW vs v2).
  2. Trust cue — §3.9 applies the §16 content pattern (2 cues, /security
     cross-link) (NEW vs v2).
  3. ArchitecturePanel.interactive (variant=solution-annotated) — §3.8
     replaces the v2 static SVG with the §5.A annotated subset, rendering
     the "many → one" multiplicity (NEW vs v2).
  4. Cross-lens navigation — §3.10 applies the §17 LOCKED preset (NEW vs
     v2).
  + §1.4 metadata block (§9 metadata governance).
  + Inline FAQ reframed with FAQPage schema (§9 per-page-type FAQ
    governance; the v2 §5 "questions multi-site operations raise" list is
    reworked into scoping/procurement Q&A).
  + "How this differs from single-plant monitoring scaled by hand" callout
    in §3.3 (§9 emerging-pattern governance — the real adjacent category
    for this buyer).

IS / IS-NOT vs the two LOCKED sister depth-examples (so this page doesn't
overlap them):
  - /solutions/edge-connectivity v2.1 = the protocol-agnostic cross-vendor
    OT-consolidation story across ALL controller classes, at ONE site.
    Multi-site is the FLEET-SCALE story: same edge story repeated per plant
    + central aggregation. Multi-site cross-links edge-connectivity for the
    per-site protocol depth and does NOT re-derive it.
  - /solutions/predictive-maintenance v2 = the reliability / condition-
    monitoring story. Multi-site is an OPERATIONS / OEE-consistency /
    fleet-visibility story, not a reliability story.

Source-of-truth alignment baked into this draft (migration plan D5):
  - MT-LINKi → ROADMAP mention, removed from the today-list (P-G). The
    Phase 1 v2 page listed MT-LINKi as today in §4 + §5; corrected here.
  - S7 + OPC UA Client → TODAY (P-G; CLAUDE.md §8 + locked connectivity-
    edge v2). Surfaced as part of "the same southbound stack at every
    plant."
  - Today protocol list (this page stays at fleet-level vocabulary; full
    matrix on Phase E /edgeconnect): FOCAS2, MTConnect, Brother HTTP,
    Modbus TCP, OPC UA Client, S7; FANUC MT-LINKi REST on the roadmap.
  - EdgeConnect = Windows service today; Linux near-term roadmap (on Edge
    Gateway). Honest-framing callout in §3.4.
  - Per-gateway identity / anti-multi-plant-EdgeConnect (LOAD-BEARING —
    above); "beside not replacing" SCADA / MES / historian; offline-first
    per site.
  - Anti-overclaim: "cut" / "reduce" verbs only; no "eliminate" / "no" /
    "zero" as outcome promises (OEM v2 precedent inherited via CNC).

Voice preservation (migration plan D6): the Phase 1 v2 page's strongest
lines are lifted and retained — "a fleet view that doesn't actually view
the fleet" (§2), "the numbers reconcile because the definitions are
platform-level, not plant-level" (§3, the page's strongest architectural
argument), "every plant works on its own, the fleet works as a whole"
(§8 caption), and the pre-emptive objection-handler "No multi-year platform
commitment required to prove the architecture works" (§9 CTA). The
migration restructures into the 11-section SolutionPanel shape; it does not
flatten the voice.

Word-count target: 1,500-1,800 words page copy per /capabilities hub §9
page-type guidance for /solutions/<solution>. Reconciled ~1,950 words
total; the 10-section SolutionPanel core ~1,750 (within band); +~210
optional Typical Engagement section = documented over-ceiling (P-A;
migration plan D4 — intentional, justified inclusion, not drift).

Carry-forward side-flag (publish-orchestration, not a spec blocker): when
this page ships live, /solutions hub v2 Card (Multi-Site Operations)
"Coming soon" status pill + pre-live link swap per the /solutions hub
pre-live link policy. Ships as part of the 5-page bulk-migration wave (no
solution page goes live in v3 while a sibling is still v2).
-->

# `/solutions/multi-site-operations` — Page Spec v1 (page content v3)

**Solution depth-example for the multi-plant manufacturer running 10+ plants under one operational umbrella. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want the outcome view of one operational picture across a fleet of plants — consistent OEE, consistent alarm semantics, consistent shift reports — where every plant runs its own edge runtime and the fleet view comes from aggregation, not from a central data-warehouse stitching project.**

This is the page where corporate operations directors, multi-plant Ops VPs, and the CTO/CIO evaluating fleet-wide monitoring land when they want the **outcome view** of running a fleet: OEE numbers that reconcile across sites, fleet visibility that survives an acquisition, local plant autonomy preserved alongside central visibility. It is **not** the capability page (`/capabilities/operational-intelligence` covers EREMOS V2 / OEE / multi-tenant aggregation; `/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability). It is **not** the architecture walkthrough (`/architecture`). It is the **multi-site operations solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the multi-plant manufacturer. Reader leaves with *"I now understand how one operational view across my whole fleet actually works — that every plant runs its own resilient edge runtime, that the fleet view is aggregation not a warehouse project, that the OEE numbers finally reconcile across sites, how a new acquired plant onboards, and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/operational-intelligence` covers EREMOS V2 / OEE / multi-tenant aggregation as the lead Pillar; `/capabilities/connectivity-edge` covers EdgeConnect + Edge Gateway as Pillar 1; both LOCKED — this page cross-links rather than duplicating)
- The protocol-agnostic / single-site OT-consolidation depth-example (`/solutions/edge-connectivity` v2.1 covers the cross-vendor edge story across all controller classes **at one site**; this page is the **fleet-scale** cut — the same edge story repeated per plant, plus central aggregation)
- The reliability / condition-monitoring depth-example (`/solutions/predictive-maintenance` v2 covers the reliability story; this is the **operations / OEE-consistency / fleet-visibility** story, a different outcome)
- A product detail page (Phase E `/edgeconnect` covers the full protocol matrix, semantic modes, per-protocol detail)
- The architecture walkthrough (`/architecture` covers cross-pillar composition + the multi-plant-EdgeConnect anti-pattern FAQ Q6; LOCKED v2.1)
- A pricing or commercial page (`/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Plant manager / Ops VP — multi-site lens (§2.2): the corporate operations director / multi-plant VP / COO at a manufacturer running 3+ plants under one operational umbrella.
- Lands here from `/solutions` hub, from the homepage hero, from `/platform`, or from a Google search for *"multi-plant OEE"* / *"fleet manufacturing dashboard"* / *"standardize OEE across plants"* / *"corporate operations monitoring multiple sites"*
- Wants: one operational view that actually views the fleet, OEE numbers that reconcile across sites, resilience when one plant's network drops, a new acquired plant onboarded in weeks not quarters, local plant autonomy preserved, cross-site benchmarking that's defensible
- CTA preference: *"Book a scoping call for your fleet"* > *"Bring us your fleet"* > datasheet download
- Vocabulary that lands: *OEE you can defend*, *consistent OEE definitions across plants*, *fleet visibility*, *per-site resilience*, *audit-ready*, *acquisition onboarding*, *replace spreadsheet operations*
- Vocabulary that backfires: *"digital transformation"*, *"smart factory"*, *"AI insights"*, *"single source of truth"* (cliché), *"single pane of glass"*, *"seamless"*, *"easy"*

**Strong secondary buyer:** CTO / CIO — vendor-consolidation lens (§2.1)
- Lands here when the page is forwarded for an enterprise-architecture sanity check, or arrives via `/platform`
- Wants: one vendor relationship across the fleet instead of one per plant's legacy tools; defensible architecture for the next 5 years; predictable cost as the fleet grows; an architecture that scales with acquisitions instead of accreting integration debt
- Served partly inline (the "one platform deployed at every plant, no per-plant integration project" architectural argument speaks directly to vendor-sprawl pain) and via cross-lens to `/platform` + `/architecture` + `/security`
- Vocabulary that lands: *vendor-agnostic at the protocol layer*, *edge-first by design*, *auditable from day one*; vocabulary that backfires: *"digital transformation"*, *"Industry 4.0"*, *"AI-powered"*

**Tertiary buyer:** OT Architect (§2.3) — served via cross-lens to `/capabilities/connectivity-edge` + `/architecture` (per buyer-taxonomy §5 step 3 — non-primary buyers via cross-lens, not primary page content).

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/solutions/cnc-machining` v1 §1.4 + `/solutions/edge-connectivity` v2.1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Multi-Site Operations — One Fleet View, Every Plant · Elpis* |
| **Meta description** (140-160 chars) | *One operational view across 10+ plants. Each site runs its own resilient edge runtime; EREMOS V2 aggregates the fleet. Consistent OEE, alarms and shift reports.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/multi-site-operations` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities/operational-intelligence` + `/capabilities/connectivity-edge` + `/architecture` + `/security` + `/platform` use `relatedLink`. Product cards for EdgeConnect + EREMOS V2 (when Phase E product pages ship) via `SoftwareApplication` schema. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). **11 sections** — the 10-section SolutionPanel shape (same as `/solutions/edge-connectivity` v2.1) plus the **optional Typical Engagement** section, included per P-A / migration plan D4.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~80 |
| **2** | The Multi-Site Reality (customer pain) — narrative empathy, 3 paragraphs | `light` | Narrative copy + optional margin pull-quote | ~210 |
| **3** | How Elpis Solves Fleet Visibility — 4 bolded-lead paragraphs + pillar cross-refs + "How this differs from single-plant monitoring scaled by hand" callout | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block | ~380 |
| **4** | What's Included — From EdgeConnect (per site) + From EREMOS V2 (central) | `light` | Bulleted feature lists with bolded leads, per-site / central split | ~270 |
| **5** | Common Questions (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions + answers + `FAQPage` schema | ~360 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column desktop | `dark` | Bolded outcome leads + supporting clauses | ~130 |
| **7** | How Multi-Site Fleets Typically Roll This Out — 4-step timeline *(optional §15 section, included)* | `light-tinted` | 4-step horizontal timeline | ~210 |
| **8** | Architecture For This Solution — solution-annotated diagram (many → one) | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" | ~80 |
| **9** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | Trust cue content pattern (design-system v3 §16) | ~80 |
| **10** | Cross-lens navigation — LOCKED preset per §17 | `light-tinted` | Cross-lens content pattern (3 cards) | ~50 |
| **11** | Final CTA — vertical-localized "Bring us your fleet" | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · MULTI-SITE OPERATIONS
>
> HEADLINE (size.3xl semibold):
> Ten-plus plants on one operational view.
>
> SUBHEAD (size.lg, max-width 60ch):
> EdgeConnect deploys at every plant — its own runtime, its own identity, its own offline resilience. One EREMOS V2 tenant aggregates the fleet. Consistent OEE definitions, alarm semantics, and shift reports across every site you operate.
>
> PRIMARY CTA (`Button.primary.lg`):
> Book a scoping call for your fleet
> HREF: `/contact?intent=multi-site-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Download the datasheet
> HREF: `/resources/datasheet`

> TRUST STRIP (under hero, size.sm):
> *Per-gateway UUID · Customer/site binding · Multi-tenant aggregation · Offline-resilient at every site · Same southbound stack everywhere: FOCAS2 · MTConnect · Brother HTTP · Modbus TCP · OPC UA Client · Siemens S7 (MT-LINKi REST on the roadmap).*

**Anti-patterns:** No *"seamless"* / *"single source of truth"* / *"single pane of glass"* framing (buyer-taxonomy §2.2 vocabulary discipline). No outcome metric in the headline. Hero leads with the **outcome** ("Ten-plus plants on one operational view"), not the products — per §15 anti-pattern. Headline + the four-part subhead rhythm preserved from Phase 1 v2 §1 (voice).

> **Pattern-setter note (P-F) — solution-page hero subhead.** The hero subhead carries the **architectural promise** (per-site runtime → central aggregation → consistent KPIs) rather than enumerating protocols, because for the multi-site buyer the load-bearing point is *"every plant works on its own, the fleet works as a whole"* — the protocol breadth is a per-site detail. The full southbound stack lives in the trust strip directly beneath (and §3.4). This follows the locked P-F rule: vertical solution-page hero subhead carries the vertical-relevant lead (here, the architecture shape); full protocol list lives in the trust strip / §3.4.

---

### 3.2 Section 2 — The Multi-Site Reality

> EYEBROW: THE MULTI-SITE REALITY
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Every plant in your fleet has its own monitoring story. Plant A runs an old vendor SCADA. Plant B has a custom monitoring stack a previous IT director built. Plant C just joined the group through an acquisition, and you haven't even surveyed its floor yet. Each one produces its own OEE number, its own alarm list, its own shift report. The numbers don't reconcile. The semantics don't match. The reports arrive in different formats on different cadences.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> Corporate operations gets the worst of every world: a fleet view that doesn't actually view the fleet. Someone in the central office stitches plant-level reports into a quarterly board deck and labels the math "approximate." Performance comparisons across sites stay aspirational. Acquiring a new plant means another long integration project before the new site even shows up in the corporate view.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The trap is treating fleet visibility as an aggregation problem — building a central data warehouse that pulls from each plant's existing tools. That works until the next plant's tools change, or the next acquisition brings a system you've never seen. The real answer is the inverse: standardize the data layer at the edge, plant by plant, and the fleet view becomes a subscription, not an integration project.

> OPTIONAL MARGIN PULL-QUOTE (desktop, size.lg italic):
> *"A fleet view that doesn't actually view the fleet."*

**Note (voice):** *"a fleet view that doesn't actually view the fleet"* and the closing reframe *"standardize the data layer at the edge … and the fleet view becomes a subscription, not an integration project"* preserved from Phase 1 v2 §2 per migration plan D6. No bullet lists — strategic-reflective empathy treatment, not pitch. (Acquisition phrasing softened from v2's "Plant C just acquired you" to "joined the group through an acquisition" — clearer for first-touch executive readers; no claim change.)

---

### 3.3 Section 3 — How Elpis Solves Fleet Visibility

> EYEBROW: HOW ELPIS SOLVES FLEET VISIBILITY

> CALLOUT — HOW THIS DIFFERS FROM SINGLE-PLANT MONITORING SCALED BY HAND (size.base, single paragraph; bordered card or left-rule callout, sits before the bolded-lead paragraphs):
>
> > **How this differs from single-plant monitoring scaled by hand.** The usual path to a fleet view is to keep each plant's monitoring tool and reconcile centrally — a data warehouse pulling per-plant exports, or a central team re-keying numbers into a board deck. It holds together until the next plant's tools change, or an acquisition arrives with a system nobody has seen. Elpis takes the inverse path: standardize the **data layer at the edge** of every plant, so each site emits the same canonical signals in the same shape, and the fleet view becomes a **subscription to a consistent stream** instead of a reconciliation project. The per-plant tools stay where they are; Elpis adds the consistent cross-fleet layer beside them.

#### Bolded-lead paragraphs (4 paragraphs):

> **One platform, deployed at every plant.** EdgeConnect runs locally at each site — a Windows service on a small box in the control cabinet, sized to that plant's controller count. Every site uses the same platform, the same southbound protocols, the same canonical vocabulary. New sites onboard the way the first one did. There is no per-plant integration project — which is the part the **Connectivity & Edge** capability makes possible. See the underlying capability story → `/capabilities/connectivity-edge`.

> **Each plant runs its own runtime — and the fleet view comes from aggregation, not from one central runtime.** This is the architectural spine of the whole solution. Each EdgeConnect carries a stable per-gateway UUID and customer/site binding established at first start, so Plant A's data is unambiguously Plant A's data. Each plant publishes its canonical stream to a central MQTT broker (yours or one we set up), and **one EREMOS V2 tenant subscribes and aggregates across every site** — every plant, every shift, every machine on one operational view, without any per-plant data-warehouse stitching. There is deliberately *no* single multi-plant EdgeConnect; fleet visibility is built by aggregating the per-plant runtimes, which is what keeps plant-level isolation, per-site identity, and offline operability intact when one plant disconnects. Acquisitions, divestitures, plant renames, regional reorganizations — the identity model survives all of them. This is the **Operational Intelligence** capability applied at fleet scale → `/capabilities/operational-intelligence`.

> **The numbers reconcile because the definitions are platform-level, not plant-level.**
>
> *(Render this line as a standalone, emphasized callout — a divider/accent treatment, not just inline bold. It is the page's signature architectural argument and the strongest single line of retention copy for the corporate-ops buyer.)*

> **Consistent KPIs across the fleet, not despite the fleet.** Because every plant's signals arrive in the same canonical shape, EREMOS V2 computes OEE Segments (RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP) from the same definitions at every site, tracks alarms under the same canonical names at every controller, and renders shift reports from the same templates configured against each site's actual shift schedule. The OEE math is the same across every machine, every shift, and every site — so a cross-site comparison holds up, and a number that lands in a board deck traces back to real signals instead of a hand-reconciled spreadsheet. And the OEE definition stays yours: segment classification, shift schedules, and targets are configured to how your group already defines OEE.

> **Per-site offline resilience is non-negotiable.** When a plant's network drops or the central broker is unreachable, that plant's EdgeConnect buffers locally with per-route store-and-forward and replays in source order when connectivity returns — no fleet outage when one site disconnects, no lost production data when the corporate WAN has a bad afternoon. Three-way diagnostics (source / pipeline / sink) tell central ops exactly which site's data flow broke, and on which leg, before the plant feels the symptom.

**Note (voice + pillars + LOAD-BEARING discipline):** the signature line *"the numbers reconcile because the definitions are platform-level, not plant-level"* preserved from Phase 1 v2 §3 and elevated to a standalone emphasized callout (P-C lead-pillar signature treatment, mirroring CNC R3). Pillar cross-refs (Operational Intelligence LEAD + Connectivity & Edge inline) are the NEW §15 ecosystem-framing addition vs v2. **Paragraph 2 is the load-bearing per-gateway-identity / anti-multi-plant-EdgeConnect instance** (the most prominent of the three required instances; the others are §3.5 Q2 + Q5 and §3.8 + §3.9 Cue 2) — it must explicitly state that each plant runs its own runtime and the fleet view comes from EREMOS V2 aggregation, never from one multi-plant EdgeConnect, per locked `/architecture` v2.1 FAQ Q6. **Pillar-balance note:** the 4-paragraph arc runs Per-site deployment (Connectivity) → Per-gateway identity + aggregation (Operational Intelligence, the differentiator) → Consistent KPIs (Operational Intelligence outcome) → Offline resilience. Operational Intelligence carries the narrative lead because cross-site OEE consistency + fleet aggregation is what makes this solution unique; Connectivity & Edge is the inline enabler.

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets — `edgeConnect` (the per-site edge runtime) + `eremosV2` (the central aggregation layer). The standalone `hardwareProducts` bucket is **omitted** — multi-site fleets typically run EdgeConnect software-only on a small control-cabinet box per site; the Edge Gateway appliance is a per-site deployment option mentioned inline, not a lead bucket on this page. (Bucket-narrative governance per P-B + migration plan D2/D5: solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names. The per-site / central split IS the product narrative for this buyer — left bucket = "at every plant," right bucket = "central" — so the two buckets map cleanly to the two halves of the fleet architecture.)

#### From EdgeConnect (at every site — Windows service today)

> - **Per-site runtime** — a Windows service on a small box in the control cabinet, sized to that plant's controller count. Every plant uses the same installer and the same onboarding flow.
> - **Same southbound stack at every plant** — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 all ship today, so a brownfield site and a newer plant collect through the same protocol coverage regardless of vendor mix. **On the roadmap:** FANUC MT-LINKi REST. For the full protocol matrix with semantic modes, see Phase E `/edgeconnect` (coming soon).
> - **Per-gateway UUID and customer/site binding** — established at first start. Plant identity is clean from day one and survives reorganizations and acquisitions.
> - **Per-route store-and-forward** — local SQLite buffering with per-sink cursors. A plant disconnected from the central broker keeps collecting and replays in source order on reconnect.
> - **Three-way diagnostics per site** — source / pipeline / sink. Central ops can see which site's data flow broke, and on which leg.
> - **Connectivity Studio per site** — local plant teams manage their own sources, sinks, and tag maps without routing every change through corporate IT.
> - **Hash-chained audit log per site** — tamper-evident change history at every plant, available for corporate audit review.

> > *Deployment note — EdgeConnect ships as a Windows service today; fleets typically run it on a small box in each plant's control cabinet. A Linux runtime is near-term roadmap, arriving on the Edge Gateway appliance for plants that prefer a turnkey DIN-rail box. The appliance is a per-site option, not a requirement, and there is no single appliance that spans plants — each plant runs its own.*

#### From EREMOS V2 (central — aggregating across the per-site runtimes)

> - **Multi-tenant aggregation** — one deployment, many sites or business units, aggregating across the per-plant runtimes. Plant data is isolated per tenant.
> - **Fleet-wide asset model** — PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT, with each plant slotting into the corporate hierarchy.
> - **Consistent OEE Segments across sites** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Same definitions, same math, every site.
> - **Cross-site dashboards** — fleet-level views with drill-down into any plant, any line, any machine.
> - **Per-site alerting routes** — corporate alerts go to corporate channels; plant-level alerts go to plant channels.
> - **Reporting templates that scale** — the same shift-report template applied to every site against each site's actual schedule.
> - **Per-tenant access control** — corporate ops sees the fleet; plant teams see their plant; business-unit leads see their BU.

**Note:** §4's density (14 bullets across the two buckets) is preserved from Phase 1 v2 §4 — per the v2 changelog, the density "contributes to enterprise plausibility" for the corporate-ops / CTO buyer evaluating operating-model completeness, and was deliberately not trimmed. **Correctness fixes surfaced by the migration (P-G):** the Phase 1 v2 §4 "native protocol coverage" bullet listed *MT-LINKi* in the today-list and omitted S7 / OPC UA Client; corrected here — MT-LINKi → roadmap; S7 + OPC UA Client → today.

---

### 3.5 Section 5 — Common Questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to corporate-ops / CTO scoping concerns (the Phase 1 v2 §5 "questions multi-site operations raise" list is reworked here into procurement-stage Q&A; the strongest commercial question — acquisition onboarding — is retained).

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What corporate operations ask before scoping a fleet.

#### Q1. How do we standardize OEE across plants that currently calculate it differently?

> EREMOS V2 computes OEE Segments centrally from edge-collected signals using one consistent definition, configured to how your group defines OEE. Each plant's existing calculation becomes legacy; the platform's calculation becomes the number you defend in a board review or a customer audit. Because every plant's signals arrive in the same canonical shape, the math is the same at every site — so a cross-site comparison is defensible rather than "approximate."

#### Q2. Does one EdgeConnect serve all our plants?

> No — and that's deliberate. Each plant runs its own EdgeConnect runtime with its own per-gateway UUID established at first start. Multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes, not from a single multi-plant EdgeConnect. That's what keeps each plant isolated, identifiable, and able to keep collecting when its network drops — and it's why one plant disconnecting never affects the others.

#### Q3. What happens when a plant's internet drops?

> That plant's EdgeConnect buffers locally with per-route store-and-forward and replays in source order on reconnect. No fleet outage, no lost production data — the plant keeps collecting, and only the central view for that one site lags until connectivity returns. Three-way diagnostics surface immediately during the outage, so central ops sees exactly which site and which leg were affected. Plants on isolated networks operate the same way; cloud connectivity is opt-in, not required.

#### Q4. How does this scale when we acquire a new plant?

> Install EdgeConnect at the new site, point it at the central broker, register the gateway in the EREMOS V2 tenant — and the new plant shows up in the fleet view. Same installer, same protocol coverage, same canonical vocabulary, same tag-mapping playbook. There is no per-plant integration project, which is what turns acquisition onboarding from a quarters-long effort into a weeks-long one.

#### Q5. Who owns the platform at each site versus centrally?

> Plant teams manage their site's EdgeConnect configuration — sources, sinks, tag maps — through the local Connectivity Studio, because the per-gateway runtime model gives each site its own administrable instance. Corporate ops manages the EREMOS V2 tenant, the cross-site dashboards, the reports, and the cross-site policies. Both layers have their own access control. Local autonomy and central visibility coexist by design, rather than one being traded for the other.

#### Q6. Can different business units keep their data walled off?

> Yes. Each business unit can be its own EREMOS V2 tenant with its own users, dashboards, and reports, with plant data isolated per tenant. Corporate ops can hold an aggregate view across BUs if the org chart calls for it. And none of it replaces your SCADA, MES, or historian — those keep their jobs at each plant and consume canonical signals instead of vendor-specific ones.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands across the fleet.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **Fleet visibility that actually views the fleet** — one operational dashboard across every plant, every shift, every machine
> - **OEE numbers that reconcile across sites** — consistent definitions, consistent math, no more "approximate" board-deck footnotes
> - **Per-site outage resilience** — one plant disconnecting never affects the others; buffered data replays in source order on reconnect
> - **Acquisition onboarding in weeks, not quarters** — a new plant onboards with the same architecture, not a custom integration project
> - **Local plant autonomy preserved** — plant teams manage their site's configuration without a corporate-IT bottleneck
> - **Cut the manual fleet-report reconciliation** — cross-site rollups built from canonical signals, not re-keyed spreadsheets
> - **Cross-site benchmarking made defensible** — same OEE definitions, same alarm semantics, same reporting templates across the fleet

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific OEE-percentage, downtime-reduction, or dollar-cost-savings claims. The `/platform` commercial teaser and Phase 3 customer-story registry handle quantified outcomes once the customer-evidence registry is in place. Outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero" (OEM v2 anti-overclaim precedent, inherited via CNC).

---

### 3.7 Section 7 — How Multi-Site Fleets Typically Roll This Out

*Optional §15 section, included per P-A / migration plan D4 — for the multi-site buyer, "how does this scale as we grow and acquire?" is the explicit deployment-anxiety objection (buyer-taxonomy §2.2: "how long does this take?" extended to fleet capacity), and the Phase 1 v2 §7 phased-rollout timeline is strong, buyer-validated content. This is the documented reason the page sits ~210 words over the 10-section ceiling.*

> EYEBROW: TYPICAL ENGAGEMENT
>
> SECTION TITLE:
> How multi-site fleets typically roll this out.

**Four-step horizontal timeline on desktop; vertical stack on mobile. Each step: phase label, headline, 2-3 line description. The "Ongoing" step visually loops or extends.**

> **Phase 1 — Pilot at one plant.** Pick the plant that's most representative of the fleet, or the most painful one. A standard single-site engagement at that plant — proof of value in the first week, cell expansion over the next few weeks, full-plant rollout over the following weeks. The pilot proves the platform against your actual production reality at one site, on its real protocols.
>
> **Phase 2 — Second plant onboarding.** Once the pilot is operational, the second plant goes faster: same architecture, same protocols, same canonical vocabulary, plant teams trained from the pilot's playbook. The central EREMOS V2 tenant is configured to aggregate both sites, and the cross-site view comes alive.
>
> **Phase 3 — Fleet rollout.** Remaining plants are brought online in parallel where plant capacity allows. Each plant uses the same EdgeConnect installer, the same gateway-registration flow, the same tag-mapping playbook. Cross-site dashboards activate as each plant comes online. Pace tracks plant capacity for tag-map authoring and acceptance testing, not a vendor-promised number.
>
> **Ongoing.** New acquisitions, new lines at existing plants, new business units — all onboard through the same architecture, each new plant getting its own per-gateway runtime. The protocol coverage is already there: FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 all ship today; FANUC MT-LINKi REST is on the roadmap. Fleet visibility scales with the fleet, not with the integration team.

**Note (voice):** *"Fleet visibility scales with the fleet, not with the integration team"* preserved from Phase 1 v2 §7 — the long-term architectural argument worth visual emphasis. The honest, capacity-paced rollout framing (rather than a vendor-promised cadence) is preserved per D6. **Correctness fix surfaced by the migration (P-G):** the Phase 1 v2 §7 "Ongoing" protocol line listed MT-LINKi as today and omitted S7 / OPC UA Client; corrected here (S7 + OPC UA Client ship today per CLAUDE.md §8; MT-LINKi is roadmap).

---

### 3.8 Section 8 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How the pieces fit together across the fleet — every plant works on its own, the fleet works as a whole.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15). Replaces the Phase 1 v2 static SVG (NEW §15 ecosystem-framing addition). The defining visual is the **"many → one" multiplicity** — N per-site EdgeConnect runtimes feeding one central EREMOS V2 tenant:

Solution-annotated variant of the Industrial Intelligence Stack layout, rendered with explicit fleet multiplicity. Highlights:
- **Col 1 — Floor (×N plants):** each plant's controllers, three or more plants explicitly rendered as parallel stacks
- **Col 2 — EdgeConnect peer (×N, highlighted):** **one runtime per plant**, each with its own per-gateway UUID, polling that plant's controllers and normalizing to canonical vocabulary. *For a multi-site fleet, the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required — EdgeConnect carries the per-plant floor-side.*
- **Col 3 — EREMOS V2 (×1, highlighted):** a single central tenant subscribing across every plant's stream; multi-tenant aggregation, consistent OEE Segments, cross-site dashboards, per-tenant access control. The "many → one" convergence is the visual centerpiece.
- **Col 4 — Customer Enterprise:** corporate SCADA / MES / historian + central + per-plant operations consumers (highlighted as systems FED by the canonical stream, not replaced — explicit "beside, not replacing" arrow direction)

**Annotations (4 specific to this solution, per §5.A / P-E: the eyebrow doubles as the ≤4-word annotation title, followed by a 1-2 sentence body; max 8 annotations per zoom level):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 1 → Col 2 per-plant arrows | PER-SITE RUNTIME, CENTRAL AGGREGATION | Each plant runs its own EdgeConnect with its own per-gateway UUID. There is no single multi-plant runtime — fleet visibility is built by aggregating the per-plant runtimes, which keeps each site isolated, identifiable, and offline-resilient. |
| Col 2 ×N → Col 3 convergence | MANY EDGES, ONE TENANT | Every plant publishes its canonical stream to a central broker; one EREMOS V2 tenant subscribes and aggregates. Per-route store-and-forward at each site survives connectivity gaps without losing source ordering. |
| Col 3 EREMOS V2 | CONSISTENT KPIs FLEET-WIDE | OEE Segments, alarm semantics, and shift-report templates use the same definitions at every site — so the numbers reconcile because the definitions are platform-level, not plant-level. |
| Col 3 → Col 4 SCADA / MES arrow | BESIDE, NOT REPLACING | EREMOS V2 publishes fleet rollups + incident records via API; each plant's SCADA / MES / historian stay where they are and consume canonical signals instead of vendor-specific ones. |

> CAPTION (below diagram, size.sm italic):
> *EdgeConnect runs locally at each plant — sized to that site's controllers, resilient to local network outages, managed by the local plant team. EREMOS V2 aggregates centrally — fleet-level dashboards, consistent KPIs, per-tenant access control. Per-gateway identity makes the fleet view unambiguous; per-site buffering makes it resilient. For a multi-site fleet, Col 2 is the EdgeConnect peer (one per plant); the Acquisition peer is not required. See the full peer-architecture story → `/architecture`.*

**Note (voice + LOAD-BEARING discipline):** *"every plant works on its own, the fleet works as a whole"* preserved from Phase 1 v2 §8 caption per D6, and the two-parallel-pairs caption structure retained. The "PER-SITE RUNTIME, CENTRAL AGGREGATION" annotation is the third of the three required per-gateway-identity / anti-multi-plant-EdgeConnect instances (with §3.3 ¶2 and §3.5 Q2/Q5), per locked `/architecture` v2.1 FAQ Q6.

---

### 3.9 Section 9 — Trust Cue

Per design-system v3 §16 trust cue content pattern. 2 cues, both linking to `/security` (NEW §15 ecosystem-framing addition vs v2). Placement follows P-D: the realized SolutionPanel order (Architecture → Trust Cue → Cross-lens → CTA), not the literal §15 prose.

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Nothing depends on the cloud — per site.** Each plant's EdgeConnect runs offline by default — license validates locally, no phone-home. If a plant's network or the central broker drops, that site's per-route store-and-forward buffers locally and replays in source order on reconnect. Plants on isolated networks install and run the platform the same way as connected ones; cloud connectivity is opt-in, not required.
>
> CUE 2 (size.base):
> **Per-gateway identity + hash-chained configuration audit, at every plant.** Each plant runs its own EdgeConnect runtime with a per-gateway UUID and customer/site binding established at first start — so the fleet view is unambiguous and survives reorganizations and acquisitions. Every change at every site — a new machine added, a tag-map edit, a threshold change — is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain, available for corporate audit review.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

**Note (LOAD-BEARING discipline):** Cue 2 carries the per-gateway-identity guard for the trust-posture surface, reinforcing the anti-multi-plant-EdgeConnect discipline that §3.3 ¶2, §3.5 Q2/Q5, and §3.8 establish.

---

### 3.10 Section 10 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages** (design-system v3 §17): `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub). (NEW §15 ecosystem-framing addition vs v2.)

> Pattern-setter precedent applied (P-C): multi-site touches two pillars; the related-pillar card leads with **Operational Intelligence** — the cross-site OEE consistency + fleet-aggregation differentiator that *makes this solution unique* — with Connectivity & Edge cross-linked inline in §3.3. This is the **inverse lead-pillar choice** from the CNC pattern-setter (which led its card with Connectivity & Edge because protocol coverage was *its* differentiator), and both obey the same locked P-C rule: the cross-lens card leads with the **differentiating** capability, not the outcome capability. Here the differentiator IS an Operational Intelligence capability (fleet aggregation across per-plant runtimes), so it leads the card; for CNC the differentiator was protocol-agnostic collection (Connectivity & Edge). Rationale: OEE itself exists across the platform, but *consistent OEE aggregated across many independent per-plant runtimes into one tenant* is the distinctly multi-site Operational-Intelligence capability — so the cross-lens points there.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · OPERATIONAL INTELLIGENCE | The underlying capability — EREMOS V2, OEE, and multi-tenant fleet aggregation | `/capabilities/operational-intelligence` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.11 Section 11 — Final CTA

Per buyer-taxonomy v1 §2.2 Plant-manager / Ops-VP CTA preference (with the CTO/CIO secondary served by the pre-emptive commitment-anxiety handler). Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic). Voice preserved from Phase 1 v2 §9.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your fleet.
>
> SUBHEAD:
> Tell us about your plants — how many, where, what controllers, what your current monitoring looks like. We'll scope a pilot at one site and a path to fleet rollout against your actual operational reality. No multi-year platform commitment required to prove the architecture works.
>
> PRIMARY CTA: Book a scoping call for your fleet
> HREF: `/contact?intent=multi-site-scoping`
>
> SECONDARY CTA: Download the datasheet
> HREF: `/resources/datasheet`

**Note (voice):** *"Bring us your fleet"* localizes the homepage CTA pattern to the corporate-ops audience, and *"No multi-year platform commitment required to prove the architecture works"* (pre-emptive objection-handling against the SaaS-contract anxiety the CTO/CIO secondary carries) — both preserved verbatim from Phase 1 v2 §9 per D6.

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.11 final CTA |
| `ArchitecturePanel.interactive` (variant=`solution-annotated` per §5.A + §15) | §3.8 architecture-for-this-solution diagram (many → one multiplicity) |
| Trust cue content pattern (design-system v3 §16) | §3.9 trust cues |
| Cross-lens content pattern (design-system v3 §17 — LOCKED preset for /solutions/<solution>) | §3.10 cross-lens |
| `CTASection` | §3.11 final CTA |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 common questions |
| 4-step timeline (composed from `SectionShell` + cards; no new primitive) | §3.7 typical engagement |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure + optional Typical Engagement = 11 sections here).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.11. **~1,950 words total.** The **10-section SolutionPanel core is ~1,750 words — within** the 1,500-1,800 target for `/solutions/<solution>` per `/capabilities` hub §9; the **optional** Typical Engagement section (§3.7, ~210 words) is the documented reason the full page sits over the ceiling at ~1,950 (P-A / migration plan D4 — an intentional, justified inclusion, not drift). The per-section figures below are **approximate targets**; architecture-diagram annotation bodies (§3.8) are counted as diagram content, not prose, per the locked exemplar convention. If a trim is wanted, the §3.2 narrative is the lowest-risk candidate; §4's density is deliberately preserved (enterprise plausibility for the corporate-ops / CTO buyer).

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~80 |
| 3.2 | The Multi-Site Reality (3 paragraphs) | ~210 |
| 3.3 | How Elpis Solves Fleet Visibility (callout + 4 bolded-lead paragraphs) | ~390 |
| 3.4 | What's Included (2 buckets, 14 bullets) | ~270 |
| 3.5 | Common Questions (6 Q&A) | ~360 |
| 3.6 | Outcomes You Can Hold Us To (7 outcomes) | ~130 |
| 3.7 | Typical Engagement (4-step timeline) | ~210 |
| 3.8 | Architecture For This Solution (caption + 4 annotations) | ~80 |
| 3.9 | Trust Cue (2 cues + cross-link) | ~80 |
| 3.10 | Cross-lens | ~50 |
| 3.11 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and §15 SolutionPanel anti-patterns:

| Don't | Why |
|---|---|
| Imply one EdgeConnect runtime serves multiple plants | **HIGH-severity.** Per locked `/architecture` v2.1 FAQ Q6 — each plant runs its own runtime with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes, NEVER from one multi-plant EdgeConnect. The discipline is carried in §3.3 ¶2, §3.5 Q2 + Q5, §3.8 annotation, and §3.9 Cue 2. Future edits must NOT regress any of these instances. |
| List MT-LINKi as operator-available today | Per side-flag #1 resolution (2026-06-04) + `/platform` v2.1 §6 re-add governance — MT-LINKi has no Studio wizard / modular adapter today. The Phase 1 v2 page listed it as today in §4 + §5; this migration corrects it to a roadmap mention. Do NOT re-add to the today-list until the engineering milestone ships. |
| List S7 / OPC UA Client as roadmap | They are operator-available today (CLAUDE.md §8 + locked connectivity-edge v2). The Phase 1 v2 page omitted them from the today-list; corrected. |
| Frame fleet visibility as a central data-warehouse / aggregation-only problem | The page IS the "standardize the data layer at the edge, plant by plant" story (§3.2 ¶3 + §3.3 "How this differs" callout). Drifting toward a central-warehouse framing regresses the core architectural argument. |
| Use *"rip and replace"* framing or imply Elpis replaces per-plant SCADA / MES / historian | The page positions Elpis as a layer beside existing per-plant systems (§3.5 Q6 + §3.8 "beside, not replacing"). |
| Imply EdgeConnect Linux is current behavior, or that one appliance spans plants | EdgeConnect is Windows today; Linux is near-term roadmap on the Edge Gateway appliance, and the appliance is per-site. The §3.4 deployment note carries the honest framing; don't drop it. |
| Claim specific OEE-percentage gains, downtime-reduction percentages, or dollar savings | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics. Quantified outcomes wait for the `/platform` teaser + Phase 3 customer-story registry. |
| Use absolute outcome claims ("zero downtime", "never lose data" as an absolute promise) | Anti-overclaim discipline (OEM v2 precedent) — outcome verbs use "cut" / "reduce". "no lost production data" appears as a store-and-forward *mechanism* description (§3.3 ¶4, §3.5 Q3), not as a headline guarantee — keep it tied to the mechanism. |
| Promise a specific multi-plant rollout cadence ("N plants per month") | Phase 1 v2 §7 wisely paced rollout to plant capacity for tag-map authoring + acceptance testing, not a vendor number. The migrated §3.7 keeps the capacity-paced framing; don't reintroduce a hard cadence number. |
| Add competitor names (Kepware, Ignition, MachineMetrics, etc.) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory. The §3.3 "How this differs" callout names the CATEGORY (single-plant monitoring scaled by hand / per-plant tools reconciled centrally) without naming products. |
| Add customer logos, customer names, or named deployment / acquisition stories | Per `proof-architecture-v1` §4 + positioning v3 §4 — Phase 2/E has no customer-logo authorization; named stories wait for Phase 3 sign-off. Acquisition onboarding is framed as a benefit, not a named case. |
| Use *"single source of truth"* / *"single pane of glass"* / *"seamless"* / *"intuitive"* / *"easy"* / *"smart factory"* / *"digital transformation"* / *"AI insights"* / *"future-proof"* / *"Industry 4.0"* / *"AI-powered"* | Per buyer-taxonomy §2.2 + §2.1 vocabulary discipline — corporate-ops VPs and CTOs read these as consultant-speak or cliché. (Includes the §2.1 CTO/CIO backfire terms surfaced in §1.2 — guard parity with the §1.2 list.) |
| Lead the hero with products instead of the outcome | Per §15 SolutionPanel anti-pattern — the hero leads with "Ten-plus plants on one operational view", not "EdgeConnect + EREMOS V2". |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per §15 anti-pattern — solution pages need annotated subsets. The migration specifically upgrades the v2 static "many → one" SVG to the interactive annotated variant. |
| Sand off the Phase 1 voice character | Per migration plan D6 — "a fleet view that doesn't actually view the fleet", "the numbers reconcile because the definitions are platform-level, not plant-level", "every plant works on its own, the fleet works as a whole", and the "no multi-year platform commitment" objection-handler are retained voice choices. |

---

## 7. Sign-off checklist (v3 lock)

- [x] Page copy word count reconciled: **~1,950 total**; 10-section SolutionPanel core ~1,750 (within the 1,500-1,800 band); +~210 optional Typical Engagement section = documented over-ceiling per §15 (P-A / migration plan D4). All three statements (header / §5 / this line) agree.
- [x] All 11 sections present per SolutionPanel layout + the optional Typical Engagement section (design-system v3 §15)
- [x] §3.1 hero leads with outcome ("Ten-plus plants on one operational view"), not products
- [x] §3.1 subhead + trust strip carry the architectural promise; protocol list in the trust strip drops MT-LINKi from the today-list (roadmap mention only) and includes S7 + OPC UA Client
- [x] §3.3 "How this differs from single-plant monitoring scaled by hand" callout present per §9 emerging-pattern governance
- [x] §3.3 names the contributing pillars (Operational Intelligence LEAD + Connectivity & Edge inline) with inline `/capabilities/<pillar>` cross-links (NEW §15 ecosystem-framing addition)
- [x] **§3.3 ¶2 explicitly states each plant runs its own runtime + the fleet view comes from EREMOS V2 aggregation, never one multi-plant EdgeConnect** (LOAD-BEARING — per `/architecture` v2.1 FAQ Q6)
- [x] §3.3 signature line ("the numbers reconcile because the definitions are platform-level, not plant-level") preserved (voice)
- [x] §3.4 What's Included follows §15 schema (2 buckets: EdgeConnect per-site + EREMOS V2 central; `hardwareProducts` omitted — bucket-narrative rationale documented per P-B)
- [x] §3.4 EdgeConnect deployment note honest (Windows today, Linux roadmap on Edge Gateway, appliance optional + per-site, no single appliance spans plants)
- [x] §3.4 + §3.5 Q4/§3.7 protocol lists: FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 today; MT-LINKi REST roadmap (P-G correctness fix)
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 governance
- [x] **§3.5 Q2 (one EdgeConnect for all plants?) explicitly denies the anti-pattern + §3.5 Q5 reinforces per-site runtime ownership** (LOAD-BEARING)
- [x] §3.5 Q1 (standardize OEE) ties OEE to canonical signals + defensibility; Q3 (plant network drop) describes store-and-forward + three-way diagnostics; Q6 says "beside, not replacing" SCADA/MES/historian
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero"; omit OEE-percentage and dollar-cost claims
- [x] §3.7 Typical Engagement included with documented rationale (optional §15 section; fleet-scale deployment-anxiety objection); capacity-paced (no hard "N plants/month" cadence); "Ongoing" protocol line corrected (P-G)
- [x] §3.8 architecture uses `ArchitecturePanel.interactive` variant=`solution-annotated` (NOT a static image); renders "many → one" multiplicity; annotations honor §5.A discipline (P-E eyebrow-as-title); includes the "Acquisition peer not required" Col-2 clarifier; **the PER-SITE RUNTIME, CENTRAL AGGREGATION annotation carries the anti-multi-plant discipline** (LOAD-BEARING)
- [x] §3.9 trust cues cover per-site offline-first AND per-gateway identity + hash-chained audit; cross-link `/security`; Cue 2 reinforces per-gateway identity (LOAD-BEARING)
- [x] §3.10 cross-lens cards match the LOCKED §17 preset; related-pillar card leads with **Operational Intelligence** (the differentiator) per P-C; inverse-of-CNC lead documented
- [x] §3.11 final CTA uses Plant-manager-preferred framing ("Book a scoping call for your fleet" / "Bring us your fleet"), vertical-localized, with the "no multi-year commitment" CTO/CIO objection-handler preserved
- [x] EdgeConnect + EREMOS V2 positioning matches the LOCKED `/capabilities/operational-intelligence` + `/capabilities/connectivity-edge` specs
- [x] No vocabulary that backfires per buyer-taxonomy §2.2 + §2.1 (no *"single source of truth"* / *"single pane of glass"* / *"seamless"* / *"smart factory"* / *"digital transformation"* / *"AI insights"* / *"Industry 4.0"* / *"AI-powered"*)
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names
- [x] All components are design-system v3 LOCKED; page-spec structure follows §9 canonical template
- [x] §1.4 metadata block present per §9 metadata governance; inline FAQ present per §9 per-page-type FAQ governance
- [x] IS/IS-NOT distinct from `/solutions/edge-connectivity` v2.1 (single-site OT-consolidation) and `/solutions/predictive-maintenance` v2 (reliability) per §1.1
- [x] Phase 1 v2 voice character preserved (D6)
- [x] Inherited pattern-setter precedents P-A..P-G applied + documented (header + §3.10)
- [x] (Batch) ChatGPT review pass applied + pre-lock validation workflow PASSED over the 4-page batch before the 5-page wave locks

---

## 8. Out of scope for v1 (v3 content)

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers the full matrix with semantic modes, per-protocol integration test patterns, MT-LINKi REST detail.
- **Full EREMOS V2 capability detail.** `/capabilities/operational-intelligence` (LOCKED) covers OEE / alarms / multi-tenant aggregation as the lead Pillar; this page cross-links rather than duplicating.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2.1) covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1) covers the cross-pillar Industrial Intelligence Stack + the multi-plant-EdgeConnect anti-pattern (FAQ Q6); cross-link for the full stack story.
- **Single-site OT-consolidation depth-example.** `/solutions/edge-connectivity` (LOCKED v2.1) covers the cross-vendor edge story at one site; this page is the fleet-scale cut.
- **Reliability / condition-monitoring depth-example.** `/solutions/predictive-maintenance` (LOCKED v2) covers the reliability story; this is the operations / fleet-visibility story.
- **CNC / precision-manufacturing / brownfield / OEM framings.** The four sibling solution pages (their own v3 migrations in this Phase E wave) cover those outcomes.
- **Acquisition-integration playbook detail.** Surfaced as a benefit (§3.5 Q4 + §3.7) but not detailed; that's a separate sales-enablement document.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links from §3.9.
- **Pricing / commercial engagement detail.** `/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail.
- **Quantified OEE-gain / downtime-reduction / cost-savings percentages, and real customer / acquisition case studies.** Wait for Phase 3 customer-story registry + the `/platform` commercial teaser.

---

*`/solutions/multi-site-operations` Page Spec **v1 LOCKED 2026-06-04** (page content v3 — SolutionPanel migration of the Phase 1 v1→v2 page copy), under the CNC pattern-setter precedents P-A..P-G. LOCKED after the batch ChatGPT review + pre-lock validation workflow (run wf_e86046ac-cdb — per-site/per-gateway identity discipline verified clean across all 4 live-copy locations; 0 HIGH, 3 MED applied). Migrates `solution-multi-site-operations-v2.md` into the §9 canonical per-page-spec format + SolutionPanel §15 layout, adding the four §15 ecosystem-framing additions (pillar cross-refs — Operational Intelligence LEAD + Connectivity & Edge inline; trust cue; ArchitecturePanel.interactive rendering the "many → one" multiplicity; cross-lens), §1.4 metadata, inline FAQ with FAQPage schema, and the "How this differs from single-plant monitoring scaled by hand" callout. Includes the optional Typical Engagement section (P-A; fleet-scale deployment-anxiety objection). LOAD-BEARING discipline: per-gateway identity / anti-multi-plant-EdgeConnect carried in §3.3 ¶2, §3.5 Q2 + Q5, §3.8 annotation, §3.9 Cue 2 per locked `/architecture` v2.1 FAQ Q6. Source-of-truth alignment: MT-LINKi → roadmap (P-G; side-flag #1 resolution); S7 + OPC UA Client corrected to today; EdgeConnect Windows-today/Linux-roadmap, appliance per-site-optional; beside-not-replacing; anti-overclaim "cut"-verb hedging; capacity-paced rollout (no hard cadence number). Cross-lens related-pillar card leads with Operational Intelligence (the differentiator) per P-C — the inverse lead-pillar of CNC, same rule. Phase 1 v2 voice character preserved (D6). Locked as part of the 5-page wave; ships together (merge is the maintainer's call). Cites: page-solutions-cnc-machining-spec-v1 v1 (pattern-setter), page-capabilities-hub-spec-v1 §9, design-system-v3 §15/§16/§17/§5.A, buyer-taxonomy-v1 §2.2/§2.1/§2.3, proof-architecture-v1 §3/§4/§8, page-capabilities-operational-intelligence-spec-v1 v1, page-capabilities-connectivity-edge-spec-v1 v2.1, page-architecture-spec-v1 v2.1 (FAQ Q6), page-solutions-edge-connectivity-spec-v1 v2.1 + page-solutions-predictive-maintenance-spec-v1 v2 (IS/IS-NOT sister exemplars), solution-multi-site-operations-v2 (migrated source), shared-knowledge/contracts/eremos-per-tag-mqtt.md, 2026-06-04-phase-e-solution-migration-plan.md.*
