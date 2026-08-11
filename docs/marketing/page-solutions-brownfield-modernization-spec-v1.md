<!--
File:        docs/marketing/page-solutions-brownfield-modernization-spec-v1.md
Purpose:     Page spec for /solutions/brownfield-modernization — solution
             depth-example for the mixed-generation manufacturing plant
             that wants modern operational visibility WITHOUT replacing
             validated controllers ("the iron stays, the data layer
             modernizes"). Migrates solution-brownfield-modernization-
             v2.md (Phase 1 page copy) onto the SolutionPanel §15 layout
             / §9 canonical per-page-spec format. Part of the Phase E
             bulk migration of the 5 v2 solution pages.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim copy), user + ChatGPT
             (reviewers), Phase E batch-migration authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-solutions-cnc-machining-spec-v1.md v1 (LOCKED PATTERN-
                SETTER — this migration inherits its structure, the four
                §15 ecosystem-framing additions, the §9 governance
                additions, and the locked precedents P-A..P-G; mirror its
                shape + quality bar)
             solution-brownfield-modernization-v2.md (Phase 1 page copy
                being migrated — voice + structure precedent; SUPERSEDED
                by this spec at v3 lock; retained as voice reference)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED —
                sister SolutionPanel depth-example; the EdgeConnect+Edge-
                Gateway-specific protocol-agnostic OT-consolidation story.
                Read §1.1 + §8 to stay DISTINCT — this page is the
                broader modernization-without-replacement framing, not the
                product-specific edge depth-example)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED —
                sister SolutionPanel exemplar; whatsIncluded bucket-
                narrative discipline + cross-spec drift discipline)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page FAQ governance — /solutions/<solution> = YES;
                metadata governance; "How this differs from…" emerging-
                pattern governance; Typical-Engagement optional-section
                guidance via §15 baseline)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED —
                source-of-truth for EdgeConnect + Edge Gateway positioning
                + the today-protocol list)
             page-capabilities-operational-intelligence-spec-v1.md v1
                (LOCKED — source-of-truth for EREMOS V2 / OEE / alarms
                pillar cross-ref)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-link target
                for "See full architecture"; multi-plant-EdgeConnect FAQ
                Q6; integration patterns §3.6)
             page-solutions-hub-spec-v1.md v2 (LOCKED — /solutions hub
                directory this depth-example sits under)
             buyer-taxonomy-v1.md §2.3 (OT Architect / SCADA engineer —
                primary buyer) + §2.2 (Plant manager — secondary)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics,
                no customer names, no competitor names)
             design-system-v3.md §15 (SolutionPanel — LOCKED) + §16
                (trust cue content pattern) + §17 (cross-lens LOCKED
                preset for /solutions/<solution>) + §5.A
                (ArchitecturePanel.interactive solution-annotated variant)
             shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC
                tag set — spindle_rpm, feed_rate, parts_count, cycle_time,
                axes, tool, alarm codes)
             solution-oem-machine-monitoring-v2.md (anti-overclaim "cut"-
                verb hedging precedent)
             2026-06-04-phase-e-solution-migration-plan.md (the bulk-
                migration plan-trail this spec executes; D1-D6 decisions +
                P-A..P-G precedents)
Version:     v1 — LOCKED 2026-06-04 (page content v3). Migrates the Phase 1
                  v1→v2 page copy onto the SolutionPanel §15 layout. Spec
                  doc = v1 in the §9 template sense. Inherits the locked
                  pattern-setter precedents P-A..P-G from
                  page-solutions-cnc-machining-spec-v1.md. Part of the
                  Phase E batch-of-4 (precision-manufacturing,
                  brownfield-modernization, oem-machine-monitoring,
                  multi-site-operations); one coordinated ChatGPT review +
                  one pre-lock validation workflow over the batch, then
                  lock + ship all 5 as one wave.
Date:        2026-06-04
Status:      LOCKED 2026-06-04 (page content v3). Batch ChatGPT review
             applied + pre-lock validation workflow PASSED — this page
             carried the batch's only HIGH (OT-Architect CTA misalignment,
             fixed → precedent P-H) + 1 MED (meta-title band); all applied.
             Locks + ships as one wave with the other 4.

INHERITED PATTERN-SETTER PRECEDENTS (from the LOCKED CNC pattern-setter
page-solutions-cnc-machining-spec-v1.md; see its header workflow note +
the migration plan-trail §"Pattern-setter outcome"). How this page applies
each:
  P-A (Typical Engagement = optional §15 section, included where deployment
    anxiety is a real buyer objection, with documented word-budget
    consequence): INCLUDED. Brownfield modernization is phased-by-nature,
    and deployment anxiety is the page's CENTRAL objection — "will this
    require a forklift upgrade / a 6-month rebuild / a machine offline?".
    The Phase 1 v2 §7 timeline ("Pick the controller you're most skeptical
    about") is the page's strongest deployment-confidence content. Word-
    budget consequence per P-A: the 10-section SolutionPanel core stays in
    the 1,500-1,800 band (~1,700); the optional Typical Engagement section
    (~210) is the documented reason the full page sits over the ceiling at
    ~1,910.
  P-B (whatsIncluded buckets follow product-narrative groupings, not
    literal schema field names; document bucket choice + omissions): 2
    buckets — edgeConnect + eremosV2. The standalone hardwareProducts
    bucket is OMITTED — brownfield plants run EdgeConnect software-only on
    an existing control-cabinet box; the Edge Gateway appliance is a
    deployment option mentioned inline, not a lead bucket (a brownfield
    plant modernizing the data layer is precisely NOT buying new iron).
    Documented in §3.4.
  P-C (multi-pillar → cross-lens related-pillar card leads with the
    DIFFERENTIATING capability; other pillars cross-linked inline in §3.3):
    brownfield touches Connectivity & Edge (lead) + Operational
    Intelligence (inline §3.3). Cross-lens card leads with Connectivity &
    Edge — the cross-generation protocol coverage (FOCAS2 across every
    Fanuc generation incl. 16i/18i) is what MAKES modernization-without-
    replacement possible; OEE/reports are the outcome, cross-linked inline.
  P-D (trust-cue placement follows the realized exemplar order — after
    Architecture, before Cross-lens/CTA — not the literal §15 prose):
    applied in §3.9.
  P-E (architecture-annotation eyebrow doubles as the ≤4-word title):
    applied in §3.8.
  P-F (vertical hero subhead carries the relevant protocol SUBSET; full
    list in trust strip / §3.4): the hero subhead leads with FOCAS2 on
    Fanuc 16i/18i/0i (the brownfield-central proof point) plus the mixed-
    fleet protocols; the full six-protocol today-list lives in the trust
    strip.
  P-G (MT-LINKi → roadmap only; S7 + OPC UA Client → today — the two
    correctness fixes the migration surfaces vs the stale Phase 1 v2
    pages): BOTH applied. The Phase 1 v2 page listed MT-LINKi as operator-
    available today across §1/§3/§4 and listed S7 + OPC UA Client as
    roadmap in §7. This migration corrects both per CLAUDE.md §8 + locked
    connectivity-edge v2 + side-flag #1 resolution.

What the migration ADDS vs the Phase 1 v2 page copy (the four §15
ecosystem-framing additions + §9 governance additions):
  1. Pillar cross-references — §3.3 names the contributing capability
     pillars (Connectivity & Edge + Operational Intelligence) with inline
     /capabilities/<pillar> cross-links (NEW vs v2).
  2. Trust cue — §3.9 applies the §16 content pattern (2 cues, /security
     cross-link) (NEW vs v2).
  3. ArchitecturePanel.interactive (variant=solution-annotated) — §3.8
     replaces the v2 static SVG diagram with the §5.A interactive
     annotated subset (NEW vs v2).
  4. Cross-lens navigation — §3.10 applies the §17 LOCKED preset (NEW vs
     v2).
  + §1.4 metadata block (§9 metadata governance).
  + Inline FAQ reframed with FAQPage schema (§9 per-page-type FAQ
    governance; v2 §5 deployment-anxiety Q&A is preserved + reframed).
  + "How this differs from rip-and-replace modernization" callout in §3.3
    (§9 emerging-pattern governance; the adjacent category is forklift
    controller-replacement projects — a real, named-by-category objection
    this page exists to counter).

DISTINCT-FROM-EDGE-CONNECTIVITY discipline (read edge-connectivity v2.1
§1.1 + §8 before editing). /solutions/edge-connectivity is the LOCKED
EdgeConnect+Edge-Gateway-specific protocol-agnostic OT-consolidation
depth-example — its story is the product surface (deployment shapes
software-only vs appliance, OPC UA Server, multi-site fleet patterns,
controller-mix consolidation across ALL controller classes). This page is
the BROADER "modernization-without-replacement" business story — led with
FOCAS2 on older Fanuc 16i/18i specifically, anchored on "the iron stays,
the data layer modernizes", and built to counter the forklift-upgrade
trap. edge-connectivity §1.1 explicitly carves this page out ("a
brownfield-modernization framing … this page is the EdgeConnect+Edge-
Gateway-specific depth-example"); we hold the reciprocal boundary in §1.1
IS-NOT. Overlap is intentional only on shared platform facts (canonical
vocabulary, store-and-forward, beside-not-replacing); the FRAMING is
distinct — product-edge-depth (edge-connectivity) vs modernization-
business-case (this page).

Source-of-truth alignment baked into this v1 draft (migration plan D5 +
P-G):
  - MT-LINKi → ROADMAP mention, removed from the today-list. The Phase 1
    v2 page listed MT-LINKi as operator-available today (hero subhead,
    trust strip, §3 ¶1, §4 bullet); this migration corrects it per side-
    flag #1 resolution (2026-06-04) + /platform v2.1 §6 re-add governance.
  - S7 + OPC UA Client are NOW operator-available (CLAUDE.md §8 + locked
    connectivity-edge v2). The Phase 1 v2 §7 "Ongoing" line listed them as
    roadmap; this migration corrects them to today. This is a real
    correctness fix the migration surfaces (and it lands with extra force
    on THIS page — "when the iron eventually changes, the data layer
    already handles it" is the page's long-term economic argument, so the
    today/roadmap split has to be exact).
  - Brownfield today-protocol list (this page surfaces the brownfield-
    relevant subset): FOCAS2 (every Fanuc generation 0i…32i), MTConnect,
    Brother HTTP, Modbus TCP (PLC-fronted older CNCs) — plus OPC UA Client
    + S7 available; FANUC MT-LINKi REST on the roadmap. Full protocol
    matrix lives on Phase E /edgeconnect; this page stays at solution-
    level vocabulary.
  - EdgeConnect = Windows service today; Linux near-term roadmap (on Edge
    Gateway). The Edge Gateway appliance is an OPTIONAL deployment note —
    a brownfield plant typically runs software-only on an existing control-
    cabinet box, NOT a turnkey appliance purchase.
  - Per-gateway identity / anti-multi-plant-EdgeConnect; "beside, not
    replacing" SCADA/MES/historian; offline-first.
  - Anti-overclaim: "cut" / "reduce" verbs only (OEM v2 precedent), never
    "eliminate" / "no" / "zero".

Voice preservation (migration plan D6): the Phase 1 v2 page carries strong
voice the migration must NOT sand off. Retained verbatim:
  - The central narrative anchor "The iron stays. The data layer
    modernizes." (v2 §1 hero headline) — kept as the hero headline and
    reinforced throughout.
  - "The case for ripping them out exists only in vendor presentations."
    (v2 §2 pull-quote) — kept as the §3.2 margin pull-quote.
  - "Your operators don't need to change a single behavior." (v2 §3 ¶2) —
    kept with visual prominence; it is the page's single biggest objection-
    killer.
  - "Pick the controller you're most skeptical about." (v2 §7 + §9) — kept
    as the §3.7 Week-1 framing and the §3.11 final-CTA inversion.
  - The long-term economic close "Your modernization investment is in the
    data layer, not in the iron — so when the iron eventually changes, the
    data layer doesn't." (v2 §7 Ongoing) — kept, with the corrected
    protocol today/roadmap split.

Structural note — Typical Engagement section INCLUDED (P-A; migration plan
D4 marked it "YES (likely)"; confirmed YES at draft). Rationale: brownfield
modernization is phased-by-nature and deployment anxiety is the PAGE'S
defining buyer objection (more central here than on any sibling — the whole
narrative answers "will modernization disrupt my floor?"). The Phase 1 v2
§7 four-step timeline, led by "pick the controller you're most skeptical
about", is buyer-validated, high-confidence content. This makes brownfield
an 11-section page; the optional section is the documented over-ceiling
reason (P-A word-budget consequence).

Word-count target: 1,500-1,800 words page copy per /capabilities hub §9
page-type guidance for /solutions/<solution>. Reconciled ~1,910 total; the
10-section SolutionPanel core ~1,700 (within band); +~210 optional Typical
Engagement section = documented over-ceiling (P-A; see §5).

Carry-forward side-flag (publish-orchestration, not a spec blocker): when
this page ships live (as part of the 5-page wave), /solutions hub v2 Card
(Brownfield) "Coming soon" status pill + pre-live link swap per the
/solutions hub pre-live link policy.
-->

# `/solutions/brownfield-modernization` — Page Spec v1 (page content v3)

**Solution depth-example for the mixed-generation manufacturing plant that wants modern operational visibility without replacing validated controllers. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want the outcome view of "the iron stays, the data layer modernizes" — FOCAS2 on older Fanuc 16i/18i alongside the newer fleet, OEE and audit trails from the machines they already own, no forklift upgrade.**

This is the page where OT Architects, SCADA engineers, and the plant managers who back them land when they want the **outcome view** of modernizing a brownfield floor: the analytics layer corporate wants — OEE, alarm history, shift reports, audit trails — without ripping out controllers that are already validated, depreciated, and making good parts. It is **not** the capability page (`/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability; `/capabilities/operational-intelligence` covers EREMOS V2 / OEE). It is **not** the architecture walkthrough (`/architecture`). It is the **modernization-without-replacement narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the mixed-generation brownfield plant. Reader leaves with *"I now understand that I can get modern operational visibility — defensible OEE, alarm history, audit trails — from the controllers I already own, including my oldest Fanuc iron; that deployment is incremental and reversible rather than a forklift upgrade; that my operators don't change a single behavior; and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability; `/capabilities/operational-intelligence` covers EREMOS V2 / OEE / alarms; both LOCKED — this page cross-links rather than duplicating)
- A product detail page (Phase E `/edgeconnect` covers the full protocol matrix, semantic modes, FOCAS2 connection-pool sizing, model-by-model coverage)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2.1)
- The protocol-agnostic edge / OT-consolidation depth-example (`/solutions/edge-connectivity` v2.1 is the EdgeConnect+Edge-Gateway-specific cut — deployment shapes, OPC UA Server, multi-site fleet patterns across all controller classes; this page is the broader **modernization-without-replacement business case**, led with older-Fanuc coverage, not the product-edge depth view)
- A rip-and-replace / controller-upgrade pitch (the entire page is the opposite — see the §3.3 "How this differs from rip-and-replace modernization" callout)
- A pricing or commercial page (`/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** OT Architect / SCADA engineer (§2.3) — the industrial-IT lead at a brownfield plant evaluating a monitoring/analytics platform against a mixed-generation floor
- Lands here from `/solutions` hub, from `/capabilities/connectivity-edge` via cross-link, or from a Google search for *"FOCAS2 Fanuc 16i monitoring"* / *"brownfield CNC OEE without replacement"* / *"modernize old CNC without new controller"* / *"Modbus PLC retrofit OEE"*
- Wants: real protocol coverage that doesn't quietly drop the older Fanuc generations, canonical normalization across vintages, SCADA/MES coexistence honesty, read-only/observational architecture, store-and-forward on flaky plant networks, incremental + reversible deployment, hash-chained config audit
- CTA preference (per §2.3 OT Architect): *"Request an architecture review"* > *"Bring us your oldest CNC"* (vertical, §2.3-compatible) / *"Talk to an engineer"* > datasheet download. NOTE — *"Book a scoping call"* is a §2.3 **backfire** and is deliberately NOT used here; it fits the §2.2 Plant-manager buyer of the CNC pattern-setter, not this page's OT-Architect primary (pre-lock workflow HIGH fix; see §3.11 note + plan-trail precedent P-H)
- Vocabulary that lands: *the iron stays*, *the data layer modernizes*, *read-only / observational*, *FOCAS2 on 16i/18i*, *canonical vocabulary across generations*, *incremental and reversible*, *no forklift upgrade*, *beside not in place of*, *audit trail from day one*, and real protocol/model names (FOCAS2, Brother HTTP, Modbus TCP, Fanuc 0i/16i/18i) as trust signals
- Vocabulary that backfires: *"rip and replace"*, *"digital transformation"*, *"smart factory"*, *"future-proof"*, *"AI insights"*, *"single source of truth"*, *"seamless"*, *"easy"*

**Secondary buyer:** Plant manager / engineering manager (§2.2) — the operations leader who has to defend the modernization decision to corporate without a capital project
- Lands here when the OT Architect forwards the page, or from the homepage hero for the "OEE without capital expense" angle
- Wants: modern reports corporate is asking for, no multi-month rebuild project, no machine downtime for installation, a rollout that proves its place before commitment
- Served via cross-lens to `/capabilities/operational-intelligence` + `/architecture` (per buyer-taxonomy §5 step 3 — secondary buyers via cross-lens, not primary page content)

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/solutions/cnc-machining` spec v1 §1.4 + `/solutions/edge-connectivity` v2.1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Brownfield CNC Modernization — The Iron Stays · Elpis* |
| **Meta description** (140-160 chars) | *Modern OEE, alarms, and audit trails from the controllers you already own. FOCAS2 on Fanuc 16i/18i, MTConnect, Brother HTTP, Modbus TCP. No forklift upgrade.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/brownfield-modernization` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities/connectivity-edge` + `/capabilities/operational-intelligence` + `/architecture` + `/security` use `relatedLink`. Product cards for EdgeConnect + EREMOS V2 (when Phase E product pages ship) via `SoftwareApplication` schema. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). **11 sections** — the 10-section SolutionPanel shape (same as `/solutions/edge-connectivity` v2.1) plus the **optional Typical Engagement** section, included per P-A (deployment anxiety is this page's defining objection).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~80 |
| **2** | The Brownfield Reality (customer pain) — narrative empathy, 3 paragraphs | `light` | Narrative copy + optional margin pull-quote | ~210 |
| **3** | How Elpis Modernizes the Data Layer — 4 bolded-lead paragraphs + pillar cross-refs + "How this differs from rip-and-replace modernization" callout | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block | ~390 |
| **4** | What's Included — From EdgeConnect + From EREMOS V2 | `light` | Bulleted feature lists with bolded leads | ~250 |
| **5** | Questions Brownfield Deployments Raise (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions + answers + `FAQPage` schema | ~360 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column desktop | `dark` | Bolded outcome leads + supporting clauses | ~120 |
| **7** | How Brownfield Plants Typically Roll This Out — 4-step timeline *(optional §15 section, included)* | `light-tinted` | 4-step horizontal timeline | ~210 |
| **8** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" | ~80 |
| **9** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | Trust cue content pattern (design-system v3 §16) | ~80 |
| **10** | Cross-lens navigation — LOCKED preset per §17 | `light-tinted` | Cross-lens content pattern (3 cards) | ~50 |
| **11** | Final CTA — vertical-localized "Bring us your oldest CNC" | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · BROWNFIELD MODERNIZATION
>
> HEADLINE (size.3xl semibold):
> The iron stays. The data layer modernizes.
>
> SUBHEAD (size.lg, max-width 60ch):
> Native FOCAS2 on Fanuc 16i, 18i, and 0i — not just the newest controllers. MTConnect, Brother HTTP, and Modbus TCP on whatever else you run. Modern operational visibility from the machines you already own, validated, and trust.
>
> PRIMARY CTA (`Button.primary.lg`):
> Request an architecture review
> HREF: `/contact?intent=brownfield-architecture-review`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Download the datasheet
> HREF: `/resources/datasheet`

> TRUST STRIP (under hero, size.sm):
> *Live integrations: FOCAS2 (Fanuc 0i · 16i · 18i · 21i · 30i · 31i · 32i) · MTConnect · Brother HTTP · Modbus TCP — and OPC UA Client and Siemens S7 for the rest of the floor. FANUC MT-LINKi REST on the roadmap.*

**Anti-patterns:** No *"seamless"* / *"intuitive"* / *"easy"* / *"single source of truth"* / *"future-proof"* framing (buyer-taxonomy §2.3 vocabulary discipline). No outcome metric in the headline. Hero leads with the **outcome / narrative anchor** ("The iron stays. The data layer modernizes."), not the products (EdgeConnect + EREMOS V2) — per §15 anti-pattern. Headline preserved verbatim from Phase 1 v2 §1 (voice; the page's central narrative anchor). The full-bleed hero image cue stays from v2: a worn-but-working older Fanuc controller with an operator's hand on the MPG — *"this machine is still working, and that's the point"*, never *"this machine is old and embarrassing."*

> **Pattern-setter inheritance (P-F) — solution-page hero subhead protocol enumeration.** The hero subhead names the **vertical-relevant protocol subset**, leading with **FOCAS2 across the older Fanuc generations** (the brownfield-central proof point — most monitoring platforms quietly drop the 16i/18i), with the full six-protocol today-list carried in the trust strip directly beneath. This follows the CNC pattern-setter's P-F: vertical solution-page hero subhead = relevant subset; full protocol list lives in the trust strip / §3.4. **MT-LINKi is dropped from both the subhead and the trust strip today-list (roadmap mention only) — a correction vs the Phase 1 v2 hero, which named it as a live integration (P-G).**

---

### 3.2 Section 2 — The Brownfield Reality

> EYEBROW: THE BROWNFIELD REALITY
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Real plants don't have one generation of controllers. They have five. A Fanuc 16i installed in 2009 next to a 32i from last year. A Brother S700Xd1 that's been running parts for eight years. A handful of older CNCs fronted by Modbus PLCs because the original controller's interface was never going to survive corporate IT review. Every one of those machines is making good parts.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> Operators know them by feel. Tooling and fixtures are validated. The capital was depreciated years ago. And yet what corporate now wants — OEE numbers, alarm history, shift reports, audit trails — keeps getting pitched back as a hardware problem. The trap most plants fall into is accepting that modernization means replacement. It doesn't. What corporate wants is a *data* requirement, not a *hardware* requirement.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The iron can stay. The data layer is what needs to catch up. The plants that get there put one protocol-agnostic runtime in front of every controller — old and new — normalize every signal to one vocabulary at the edge, and let the existing systems keep doing their jobs. No machine comes offline. No operator changes a thing.

> OPTIONAL MARGIN PULL-QUOTE (desktop, size.lg italic):
> *"The case for ripping them out exists only in vendor presentations."*

**Note (voice):** the margin pull-quote *"The case for ripping them out exists only in vendor presentations."* preserved verbatim from Phase 1 v2 §2 per migration plan D6 — the vendor-presentation dig is intentional; most brownfield buyers have sat through that exact pitch, and the recognition earns credibility. No bullet lists in this section — the challenge is a narrative (subdued empathy treatment, not pitch). The closing line *"The iron can stay. The data layer is what needs to catch up."* sets up §3.

---

### 3.3 Section 3 — How Elpis Modernizes the Data Layer

> EYEBROW: HOW ELPIS MODERNIZES THE DATA LAYER

> CALLOUT — HOW THIS DIFFERS FROM RIP-AND-REPLACE MODERNIZATION (size.base, single paragraph; bordered card or left-rule callout, sits before the bolded-lead paragraphs):
>
> > **How this differs from rip-and-replace modernization.** The other way to get modern reporting off a brownfield floor is to forklift the controllers — replace the older CNCs with new iron that ships with a modern data interface. It works, eventually, on someone else's capital schedule: a multi-month rebuild project, re-validation of tooling and fixtures, operators relearning machines they knew by feel, and a controller-replacement bill that exists to solve a *reporting* gap. Elpis does the opposite. One protocol-agnostic runtime reads the controllers you already own — including the oldest Fanuc generations — over their native protocols, normalizes every signal to **canonical vocabulary**, and feeds the modern analytics layer corporate wants. **The machine stays. The validation stays. The operator workflow stays. Only the data layer modernizes.** A controller upgrade, if it ever comes, happens on your capital schedule — not as a precondition for visibility.

#### Bolded-lead paragraphs (4 paragraphs):

> **EdgeConnect speaks the controllers you already own.** One service running on a small box in your control cabinet polls each machine over its native protocol — FOCAS2 across every Fanuc generation that exposes it (0i, 16i, 18i, 21i, 30i, 31i, 32i), MTConnect for the newer multi-vendor fleet, Brother HTTP for Brother's built-in web interface, and Modbus TCP for older CNCs you've already fronted with a PLC gateway. OPC UA Client and Siemens S7 cover the rest of the floor; FANUC MT-LINKi REST integration is on the roadmap. No per-machine custom scripting, no per-machine HMI replacement. This is the **Connectivity & Edge** capability applied to a mixed-generation floor — see the underlying capability story → `/capabilities/connectivity-edge`.

> **The data layer becomes uniform without touching the iron.** A spindle-RPM reading from a 2009 Fanuc 16i and a 2024 Mazak Integrex both arrive as the same canonical `spindle_rpm` in the pipeline. A cycle-complete signal off the 16i, the Brother, and the Mazak collapses the same way into a canonical `cycle_time` — the exact signal EREMOS V2 turns into OEE. Feed rate, parts count, tool number, axis positions, alarm codes — same names, same semantics, regardless of which controller generation produced the signal. The same dashboard works across every vintage on your floor. **Your operators don't need to change a single behavior.**
>
> *(Render "Your operators don't need to change a single behavior." with visual prominence — bold weight or a subtle accent. It is the single biggest objection-killer on the page for brownfield buyers, preserved verbatim from Phase 1 v2 §3.)*

> **And that canonical stream is what finally gives corporate the modern layer it wants.** This is the outcome the plant runs on: EdgeConnect publishes the normalized signals to MQTT (Mosquitto, HiveMQ, AWS IoT Core, or your existing broker), and EREMOS V2 turns them into the things that used to require a capital project — OEE Segments computed from cycle-time and parts-count signals, every alarm from the oldest CNC on the floor tracked as a persistent record with incident workflows, shift reports in PDF and Excel, tool-life trends. This is the **Operational Intelligence** capability → `/capabilities/operational-intelligence`. Because every machine's signals arrive in the same canonical shape, the OEE math is the same across every generation and every shift — so the number holds up whether you're comparing two cells or defending it in a customer audit. **And the OEE definition stays yours** — segment classification, shift schedule, and targets are configured to how your plant already defines OEE; the platform computes against that definition instead of forcing a new one.

> **Deployment is incremental, reversible, and observational.** Start with one machine. If it works for that machine, you expand; if it doesn't, you stop — no forklift upgrade, no multi-month rebuild, no controller replaced before the platform earns its place. EdgeConnect connects as a **read-only** client over the native protocol, so the CNC operates entirely independently of the platform's status — if EdgeConnect is offline for maintenance, the floor keeps producing parts. On the flaky plant networks brownfield floors actually run, per-route store-and-forward buffers locally and replays in source order on reconnect, and three-way diagnostics (source / pipeline / sink) tell the OT team exactly which leg broke.

**Note (voice + pillars):** the objection-killer *"Your operators don't need to change a single behavior."* preserved verbatim from Phase 1 v2 §3 and given visual prominence per D6. Pillar cross-refs (Connectivity & Edge + Operational Intelligence) are the NEW §15 ecosystem-framing addition vs v2. **Pillar-balance note (inherits P-C reasoning):** the 4-paragraph arc runs Cross-generation Connectivity → Canonical Vocabulary (the "operators don't change" payoff) → **Operational Intelligence (the modern reporting corporate wants)** → Incremental/observational deployment. Connectivity & Edge is the architectural lead and the differentiator (cross-generation protocol coverage is what makes modernization-without-replacement possible — hence the cross-lens card leads with it, P-C); Operational Intelligence carries the outcome weight (OEE / alarms / reports = what corporate is actually asking for). **The §3.3 ¶4 store-and-forward instance is the most prominent live mechanism description on the page; keep it tied to the mechanism ("buffers and replays in source order"), never framed as an absolute "never lose data" guarantee (anti-overclaim).**

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema and P-B (bucket-narrative discipline): 2 buckets — `edgeConnect` (the edge runtime; the floor-side story) + `eremosV2` (the intelligence layer). The standalone `hardwareProducts` bucket is **omitted** — a brownfield plant modernizing the data layer is precisely the buyer NOT buying new iron; EdgeConnect runs software-only on an existing control-cabinet box, and the Edge Gateway appliance is a deployment option mentioned inline, not a lead bucket. (Bucket-narrative governance per P-B + migration plan D2/D5: solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names. Brownfield keeps the discrete `edgeConnect` bucket because "software on the hardware you already own" is the entire premise.)

#### From EdgeConnect (edge runtime, Windows service today)

> - **FOCAS2 collector across every Fanuc generation** — 0i, 16i, 18i, 21i, 30i, 31i, 32i. Axes, spindle, alarms, tool, production counters, programs. The protocol coverage does not quietly drop your oldest iron.
> - **MTConnect collector** — for the newer multi-vendor CNCs already speaking the open standard.
> - **Brother HTTP collector** — Brother S700Xd1 and similar models via the built-in web-monitoring interface.
> - **Modbus TCP collector** — for older PLC-fronted CNCs, and the energy meters, drives, and instrumentation you may have already wired.
> - **Also today:** OPC UA Client and Siemens S7 for the rest of the floor. **On the roadmap:** FANUC MT-LINKi REST. For the full protocol matrix with semantic modes, see Phase E `/edgeconnect` (coming soon).
> - **Canonical vocabulary across vendors and generations** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions (`axes/x/absolute`, etc.), tool number and offsets, alarm codes. Same names regardless of which controller generation produced them.
> - **Read-only / observational by design** — EdgeConnect polls the controller; it does not change control logic, operator HMIs, or alarm acknowledgment. The CNC runs independently of the platform's status.
> - **Per-route store-and-forward buffering** — older plant networks aren't always reliable; signals queue at the source and replay in source order when connectivity returns.
> - **Three-way diagnostics** — source / pipeline / sink. When something goes wrong on the floor or in IT, operators know where it broke.
> - **Connectivity Studio** — web admin to add machines, configure tag maps, and run Test Connection probes before anything goes live. No command-line config required.
> - **Hash-chained configuration audit log** — tamper-evident change history from day one, even where formal change-control was previously informal.

> > *Deployment note — EdgeConnect ships as a Windows service today; brownfield plants typically run it on a small box that's already in the control cabinet, not on new hardware. A Linux runtime is near-term roadmap, arriving on the Edge Gateway appliance for plants that prefer a turnkey DIN-rail box. The appliance is an option, not a requirement.*

#### From EREMOS V2 (intelligence layer, consuming the canonical stream)

> - **OEE Segments** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Computed from edge-collected signals; auditable.
> - **Persistent alarm tracking with incident grouping** — alarms from the oldest CNC on the floor become tracked records with open/close state, not just blinking lights on a machine's HMI.
> - **Tool-life ingestion** — a dedicated path for tool-wear telemetry, so maintenance gets ahead of failures.
> - **Shift reports** — PDF and Excel, built from edge-collected signals — the reports corporate has been asking for.
> - **Multi-tenant by design** — one EREMOS V2 across multiple sites if you operate more than one plant; no data leakage.
> - **Dashboards split by device class** — CNC, PLC, meter. Mixed-generation fleets render cleanly.

---

### 3.5 Section 5 — Questions Brownfield Deployments Raise

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to brownfield deployment-anxiety — the Phase 1 v2 §5 Q&A set preserved and reframed in outcome context. This is the heaviest deployment-anxiety defusion on the page.

> EYEBROW: QUESTIONS BROWNFIELD DEPLOYMENTS RAISE
>
> SECTION TITLE:
> What every brownfield floor asks before scoping.

#### Q1. Will deploying this require us to take a machine offline?

> No. EdgeConnect connects to the controller as a read-only client over its native protocol; polling happens alongside normal operation. No machine downtime is required for installation, and the platform doesn't touch control logic or operator workflow. If EdgeConnect is offline for maintenance, the CNC keeps producing parts — it operates entirely independently of the platform's status.

#### Q2. What about our oldest CNC — does it work with that?

> If it's a Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i, yes — FOCAS2 covers all those generations, not just the newest one or two. If it's older or proprietary and doesn't expose a native data interface, a Modbus TCP gateway in front of the controller bridges it to the platform. OPC UA Client and Siemens S7 cover the rest of the floor today. Bring the controller list to the scoping call and we'll confirm the collection path per machine before quoting — every brownfield floor has at least one weird controller that needs case-by-case evaluation.

#### Q3. Do operators need to retrain?

> No. The platform reads from the controller; it doesn't change how the controller is operated. Operators continue using the same MPG, the same HMI, and the same parts programs they already know. Nothing about the modernization lands on the operator.

#### Q4. If the platform breaks, do our machines stop?

> No. EdgeConnect is observational — the CNC operates independently of EdgeConnect's status. If the platform is offline for maintenance or the network drops, your floor keeps producing parts. On the network side, per-route store-and-forward queues every signal at the source with its quality code preserved and replays in source order when connectivity returns, and three-way diagnostics (source / pipeline / sink) surface immediately so the OT team sees exactly which leg was affected.

#### Q5. Can we start with one machine and decide whether to expand?

> Yes — that's the recommended deployment pattern, and it's the point of an incremental, reversible rollout. Pick the controller you're most skeptical about, prove the platform against it on your real protocol, and expand only if it earns its place. No multi-year commitment, no forklift upgrade. The full rollout cadence is in "How brownfield plants typically roll this out" below.

#### Q6. How does this integrate with the SCADA, MES, or historian we already have?

> Elpis sits beside them, not in place of them. EdgeConnect publishes the canonical stream to MQTT (or OPC UA Server) — standards your existing systems likely already consume — so your SCADA, MES, and historian keep their jobs and receive consistent canonical signals instead of vendor-specific ones. The modernization adds the cross-generation data layer; it doesn't replace the operational systems you've already invested in.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **Modern visibility from machines you already own** — OEE, alarms, and reports without replacing a single controller
> - **OEE reporting without capital expense on new iron** — the platform earns its place in months, not over a multi-year capital cycle
> - **Audit trail from day one** — hash-chained change history starts the moment you turn the platform on, without ripping out the existing configuration plane
> - **Mixed-generation fleets behave like one operational system** — canonical vocabulary normalizes everything from a 2009 Fanuc to last year's Mazak
> - **Cut the multi-month rebuild project** — incremental deployment, reversible at every step, no forklift upgrade
> - **Operator workflow unchanged** — the machines operate the way operators already know
> - **The data layer outlasts the iron** — when a controller does eventually change, on your capital schedule, the platform already handles whatever you put in

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific OEE-percentage or dollar-cost-savings claims. The "OEE reporting without capital expense" bullet does the commercial work by anti-positioning against controller-replacement projects **without naming a number** (the Phase 1 v2 "$500K+" framing is dropped — the `/platform` commercial teaser and Phase 3 customer-story registry handle quantified outcomes once the customer-evidence registry is in place). Outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero" (OEM v2 anti-overclaim precedent).

---

### 3.7 Section 7 — How Brownfield Plants Typically Roll This Out

*Optional §15 section, included per P-A — deployment anxiety is this page's defining buyer objection (the whole narrative answers "will modernization disrupt my floor?"); the Phase 1 v2 §7 timeline led by "pick the controller you're most skeptical about" is buyer-validated content. This is the documented over-ceiling reason (P-A word-budget consequence).*

> EYEBROW: TYPICAL ENGAGEMENT
>
> SECTION TITLE:
> How brownfield plants typically roll this out.

**Four-step horizontal timeline on desktop; vertical stack on mobile. Each step: label, headline, 2-3 line description. Subtle accent-color progress markers between steps; the "Ongoing" step visually extends to suggest continuous architectural value.**

> **Week 1 — Proof on the oldest machine.** Pick the controller you're most skeptical about. EdgeConnect installed on a small Windows box already in your control cabinet, polling that machine over FOCAS2 or Brother HTTP. Data flowing to a Mosquitto broker we set up alongside, or to your existing MQTT broker. EREMOS V2 displaying real cycle-time and alarm data, typically within the first few days. If it works for that machine, it'll work for the rest of your floor.
>
> **Weeks 2-4 — Expansion to a cell.** Add the rest of the machines on one line or cell. Tag-map authoring done together with your team — your operators know the names that matter. Shift-report templates configured against your actual shift schedule; OEE Segments aligned to how your plant already defines OEE.
>
> **Weeks 5-8 — Fleet rollout.** Remaining CNCs onboarded — the newer ones, the Modbus-fronted older ones, anything PLC-fronted. Multi-site or multi-line aggregation in EREMOS V2 where applicable. Alerting routed to the channels your operations team already uses.
>
> **Ongoing.** When you eventually do replace a controller — years from now, on your own capital schedule — the platform already handles whatever you put in. FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 all ship today; FANUC MT-LINKi REST is on the roadmap. Your modernization investment is in the data layer, not in the iron — so when the iron eventually changes, the data layer doesn't.

**Note (voice + correctness fix):** the "typically within the first few days" phrasing and the long-term economic close *"Your modernization investment is in the data layer, not in the iron — so when the iron eventually changes, the data layer doesn't"* preserved verbatim from Phase 1 v2 §7 (D6). **Correctness fix surfaced by the migration (P-G):** the Phase 1 v2 §7 "Ongoing" line listed **Siemens S7 + OPC UA Client as *roadmap*** — both are operator-available **today** per CLAUDE.md §8; corrected here. (This fix lands with extra force on this page: the "data layer already handles the next controller" argument depends on the today/roadmap split being exact.) MT-LINKi is the only roadmap protocol named.

---

### 3.8 Section 8 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How it fits together on a brownfield floor.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15). Replaces the Phase 1 v2 static SVG (NEW §15 ecosystem-framing addition):

Solution-annotated subset of the Industrial Intelligence Stack 4-column layout. Highlights:
- **Col 1 — Floor:** mixed-generation controllers — *Fanuc 16i/18i (legacy)* · *Fanuc 31i/32i (newer)* · *Brother S700Xd1* · *Modbus PLCs in front of older CNCs* — highlighted as the signal sources. Render older controllers in slightly muted styling and newer ones sharper, to convey mixed-generation reality *without making the older ones look "bad."*
- **Col 2 — EdgeConnect peer (highlighted):** one read-only runtime polling every controller, old and new, in its native protocol, normalizing to canonical vocabulary. *For a brownfield floor, the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required — EdgeConnect carries the floor-side.*
- **Col 3 — EREMOS V2 (highlighted):** consuming the canonical stream; OEE Segments, persistent alarms, shift reports, multi-tenant analytics
- **Col 4 — Customer Enterprise:** SCADA / MES / historian (highlighted as systems FED by the canonical stream, not replaced — explicit "beside, not replacing" arrow direction)

**Annotations (4 specific to this solution, per §5.A + P-E: the eyebrow doubles as the ≤4-word annotation title, followed by a 1-2 sentence body; max 8 annotations per zoom level):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 1 → Col 2 protocol arrows | EVERY GENERATION | EdgeConnect polls each controller over its native protocol — FOCAS2 across Fanuc 0i through 32i (the legacy 16i/18i included), MTConnect, Brother HTTP, Modbus TCP for PLC-fronted older machines, plus OPC UA Client + S7. *MT-LINKi REST on roadmap.* |
| Col 1 EdgeConnect link | READ-ONLY, OBSERVATIONAL | EdgeConnect connects as a read-only client; the CNC operates independently of the platform's status. No machine offline for install, no operator workflow change. |
| Col 2 → Col 3 canonical stream arrow | UNIFORM ACROSS VINTAGES | A 2009 Fanuc 16i and a 2024 Mazak arrive in the same canonical shape — spindle RPM, cycle time, parts count, alarm codes. Per-route store-and-forward survives the flaky plant networks brownfield floors run on, without losing source ordering. |
| Col 3 → Col 4 SCADA / MES arrow | BESIDE, NOT REPLACING | EREMOS V2 publishes OEE rollups + incident records via API; your SCADA / MES / historian stay where they are and consume canonical signals instead of vendor-specific ones. No rip-and-replace. |

> CAPTION (below diagram, size.sm italic):
> *For a brownfield floor, Col 2 is the EdgeConnect peer; the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required for this solution. EdgeConnect runs at your plant; EREMOS V2 aggregates across plants if you operate more than one. See the full peer-architecture story → `/architecture`.*

---

### 3.9 Section 9 — Trust Cue

Per design-system v3 §16 trust cue content pattern. 2 cues, both linking to `/security` (NEW §15 ecosystem-framing addition vs v2):

> **Placement note (inherits P-D).** This spec follows the realized SolutionPanel order locked across the sister exemplars (`/solutions/cnc-machining` v1, `/solutions/edge-connectivity` v2.1, `/solutions/predictive-maintenance` v2): trust cue placed **after Architecture, immediately before Cross-lens + Final CTA** (Architecture → Trust Cue → Cross-lens → CTA), which supersedes the literal §15 prose ("between Outcomes and Architecture"). This is the inherited pattern, not a silent deviation (flagged corpus-wide for a future design-system v3.x amendment).

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Read-only on the floor; nothing depends on the cloud.** EdgeConnect connects to every controller as a read-only client and never changes control logic — your machines run independently of the platform. It also runs offline by default: the license validates locally, with no phone-home. If your network or broker drops, per-route store-and-forward buffers locally and replays in source order on reconnect. Plants on an isolated network install and run the platform the same way as plants with internet; cloud connectivity is opt-in, not required.
>
> CUE 2 (size.base):
> **Per-gateway identity + hash-chained configuration audit from day one.** Each plant runs its own EdgeConnect runtime with a per-gateway UUID established at first start. Every change — a machine added, a tag-map edit, a threshold change — is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain, even on a floor where formal change-control was previously informal.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.10 Section 10 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages**: `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub). (NEW §15 ecosystem-framing addition vs v2.)

> Pattern-setter inheritance (P-C): brownfield touches two pillars; the related-pillar card leads with **Connectivity & Edge** (the cross-generation protocol coverage that *makes modernization-without-replacement possible* — FOCAS2 reaching the older Fanuc iron is the differentiator), with Operational Intelligence cross-linked inline in §3.3. Per P-C, the cross-lens card points to what makes the solution *distinct*, not what the buyer values *most*: OEE/reporting exists across the platform, but reaching every controller generation without replacing it is what makes this specifically a brownfield solution.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONNECTIVITY & EDGE | The underlying capability — EdgeConnect as Pillar 1 | `/capabilities/connectivity-edge` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.11 Section 11 — Final CTA

Per buyer-taxonomy v1 §2.3 OT-Architect CTA preference: the primary button is the §2.3-endorsed *"Request an architecture review"*; the *"Bring us your oldest CNC"* headline is the §2.3-compatible vertical consultative framing. *"Book a scoping call"* is deliberately NOT used — it is a §2.3 backfire that fits the §2.2 Plant-manager pages (CNC, precision, multi-site), not this OT-Architect-primary page. **Pattern-setter precedent (P-H, pre-lock workflow HIGH fix):** a migrated page that flips its primary buyer off §2.2 must RE-DERIVE its CTA from the new buyer's profile — never inherit the CNC pattern-setter's §2.2 "Book a scoping call" verbatim. Vertical-localized per design-system v3 §15 anti-pattern (final CTA must be solution-specific, not generic). Voice preserved from Phase 1 v2 §9.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your oldest CNC.
>
> SUBHEAD:
> Pick the controller you're most skeptical about. We'll scope a proof of value against that machine specifically — on its real protocol, against its real signals. Demos run on your real iron, not on a polished newer machine staged for the camera. No canned data, no slideware, no forklift upgrade.
>
> PRIMARY CTA: Request an architecture review
> HREF: `/contact?intent=brownfield-architecture-review`
>
> SECONDARY CTA: Download the datasheet
> HREF: `/resources/datasheet`

**Note (voice):** *"Bring us your oldest CNC"* + *"Pick the controller you're most skeptical about"* + *"not on a polished newer machine staged for the camera"* preserved verbatim from Phase 1 v2 §9 (D6). The inversion — vendors usually demo on the newest, most cooperative machine; asking for the skeptical one signals confidence — is the page's strongest closing move.

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.11 final CTA |
| `ArchitecturePanel.interactive` (variant=`solution-annotated` per §5.A + §15) | §3.8 architecture-for-this-solution diagram |
| Trust cue content pattern (design-system v3 §16) | §3.9 trust cues |
| Cross-lens content pattern (design-system v3 §17 — LOCKED preset for /solutions/<solution>) | §3.10 cross-lens |
| `CTASection` | §3.11 final CTA |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 questions |
| 4-step timeline (composed from `SectionShell` + cards; no new primitive) | §3.7 typical engagement |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure + optional Typical Engagement = 11 sections here).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.11. **~1,910 words total.** The **10-section SolutionPanel core is ~1,700 words — within** the 1,500-1,800 target for `/solutions/<solution>` per `/capabilities` hub §9; the **optional** Typical Engagement section (§3.7, ~210 words) is the documented reason the full page sits over the ceiling at ~1,910 (P-A — an intentional, justified inclusion, not drift). The band is guidance calibrated to the 10-section shape. The per-section figures below are **approximate targets**; architecture-diagram annotation bodies (§3.8) are counted as diagram content, not prose, per the locked exemplar convention. If a trim is wanted, the §3.2 narrative is the lowest-risk candidate; the content as drafted is all earning its place.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~80 |
| 3.2 | The Brownfield Reality (3 paragraphs) | ~210 |
| 3.3 | How Elpis Modernizes the Data Layer (callout + 4 bolded-lead paragraphs) | ~400 |
| 3.4 | What's Included (2 buckets) | ~250 |
| 3.5 | Questions Brownfield Deployments Raise (6 Q&A) | ~360 |
| 3.6 | Outcomes You Can Hold Us To (7 outcomes) | ~120 |
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
| List MT-LINKi as operator-available today | Per side-flag #1 resolution (2026-06-04) + `/platform` v2.1 §6 re-add governance — MT-LINKi has no Studio wizard / modular adapter today. The Phase 1 v2 page listed it as today (hero subhead, trust strip, §3 ¶1, §4 bullet, §7 "Ongoing"); this migration corrects it to a roadmap mention. Future edits must NOT re-add MT-LINKi to the today-list until the engineering milestone ships. (P-G) |
| List S7 / OPC UA Client as roadmap | They are operator-available today (CLAUDE.md §8 + locked connectivity-edge v2). The Phase 1 v2 §7 "Ongoing" line had them as roadmap; corrected. The fix is load-bearing here — the "data layer already handles the next controller" argument depends on the split being exact. (P-G) |
| Use *"rip and replace"* / *"forklift upgrade"* as Elpis's own framing, or imply Elpis replaces the vendor CNC software / SCADA / MES | The page IS the "the iron stays, the data layer modernizes" story (§3.1 headline + §3.2 ¶3 + §3.3 callout + §3.5 Q6 + §3.8 "beside, not replacing"). "Rip and replace" / "forklift" appear ONLY as the named adjacent category being countered (§3.3 callout), never as a description of what Elpis does. |
| Drop the older-Fanuc (16i/18i) coverage proof point | The full Fanuc model list (0i…32i) is the page's primary trust signal — most monitoring platforms quietly drop the older generations. The hero subhead leads with it (P-F); §3.4 + §3.5 Q2 carry it. Don't trim to "Fanuc" generically. |
| Imply EdgeConnect Linux is current behavior | EdgeConnect is Windows today; Linux is near-term roadmap on the Edge Gateway appliance. The §3.4 deployment note carries the honest framing; don't drop it. |
| Position the Edge Gateway appliance as required (or as the modernization purchase) | A brownfield plant modernizing the data layer is precisely NOT buying new iron — it runs software-only on an existing control-cabinet box. The appliance is an option (§3.4 deployment note), not a requirement. (P-B bucket omission rationale + carry-forward from connectivity-edge v2 §6.) |
| Imply one EdgeConnect runtime serves multiple plants | Per locked `/architecture` v2.1 FAQ Q6 — each plant runs its own runtime with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregation. §3.8 caption + §3.9 Cue 2 carry the per-gateway-identity guard. |
| Reinstate the "$500K+" controller-replacement number (or any specific OEE-% / downtime-% / dollar figure) | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome or comparison metrics. The §3.6 "OEE reporting without capital expense" bullet does the commercial anti-positioning without a number; the Phase 1 v2 "$500K+" framing is dropped. Quantified outcomes wait for the `/platform` teaser + Phase 3 customer-story registry. |
| Use absolute claims ("zero downtime", "never lose a cycle" as an absolute promise, "no machine ever stops") | Anti-overclaim discipline (OEM v2 precedent) — outcome verbs use "cut" / "reduce". Note: store-and-forward "buffers and replays in source order" (§3.3 ¶4, §3.4, §3.5 Q4) is a *mechanism* description, not a headline guarantee — keep it tied to the mechanism. |
| Add competitor / forklift-upgrade-vendor names (Kepware, Ignition, MachineMetrics, or named controller-replacement programs) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory. The §3.3 "How this differs" callout names the CATEGORY (rip-and-replace modernization) without naming specific vendors or products beyond the Fanuc/Brother/Mazak controller examples that are the floor reality. |
| Add customer logos, customer names, or named deployment stories | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2/E has no customer-logo authorization; named stories wait for Phase 3 sign-off. |
| Use *"single source of truth"* / *"seamless"* / *"intuitive"* / *"easy"* / *"smart factory"* / *"digital transformation"* / *"AI insights"* / *"future-proof"* | Per buyer-taxonomy §2.3 vocabulary discipline — OT Architects / SCADA engineers read these as consultant-speak or cliché. (Full backfire list parity with §1.2.) |
| Lead the hero with products instead of the narrative anchor | Per §15 SolutionPanel anti-pattern — the hero leads with "The iron stays. The data layer modernizes.", not "EdgeConnect + EREMOS V2". |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per §15 anti-pattern — solution pages need annotated subsets, not generic diagrams. (This is precisely what the migration upgrades vs the Phase 1 v2 static SVG.) |
| Sand off the Phase 1 voice character | Per migration plan D6 — "The iron stays. The data layer modernizes.", "The case for ripping them out exists only in vendor presentations.", "Your operators don't need to change a single behavior.", "Pick the controller you're most skeptical about.", and the data-layer-outlasts-the-iron close are retained voice choices. |
| Duplicate the `/solutions/edge-connectivity` product-edge depth-example | edge-connectivity v2.1 is the EdgeConnect+Edge-Gateway-specific protocol-agnostic OT-consolidation story (deployment shapes, OPC UA Server, multi-site fleet patterns across all controller classes). This page is the broader modernization-business-case framing led with older-Fanuc coverage. Hold the §1.1 IS-NOT boundary; overlap only on shared platform facts, never on framing. |

---

## 7. Sign-off checklist (v3 lock — batch-of-4)

- [x] Page copy word count reconciled: **~1,910 total**; 10-section SolutionPanel core ~1,700 (within the 1,500-1,800 band); +~210 optional Typical Engagement section = documented over-ceiling per §15 + P-A. All three statements (header / §5 / this line) agree.
- [x] All 11 sections present per SolutionPanel layout + the optional Typical Engagement section (design-system v3 §15)
- [x] §3.1 hero leads with the narrative anchor ("The iron stays. The data layer modernizes."), not products
- [x] §3.1 subhead leads with FOCAS2 on older Fanuc (16i/18i/0i) per P-F; subhead + trust strip drop MT-LINKi from the today-list (roadmap mention only — P-G)
- [x] §3.3 "How this differs from rip-and-replace modernization" callout present per §9 emerging-pattern governance; names the category, not vendors
- [x] §3.3 names the contributing pillars (Connectivity & Edge + Operational Intelligence) with inline `/capabilities/<pillar>` cross-links (NEW §15 ecosystem-framing addition)
- [x] §3.3 ¶2 objection-killer ("Your operators don't need to change a single behavior.") preserved with visual prominence (voice)
- [x] §3.4 What's Included follows §15 schema (2 buckets: EdgeConnect + EREMOS V2; `hardwareProducts` omitted — P-B bucket-narrative rationale documented: brownfield = not buying new iron)
- [x] §3.4 EdgeConnect deployment note honest (Windows today, Linux roadmap on Edge Gateway, appliance optional, runs on existing control-cabinet box)
- [x] §3.4 + §3.5 Q2 protocol lists: FOCAS2 (0i…32i) / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 today; MT-LINKi REST roadmap (P-G)
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 governance; 6 deployment-anxiety Q&A preserved + reframed from Phase 1 v2 §5
- [x] §3.5 Q1/Q4 surface the read-only/observational architectural truth (machines run independently of the platform)
- [x] §3.5 Q6 (SCADA/MES/historian) explicitly says "beside, not in place of"
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero"; the "$500K+" Phase 1 number is dropped (proof-architecture v1 §3 + §4)
- [x] §3.7 Typical Engagement included with documented rationale (P-A; deployment anxiety is the page's defining objection); "Pick the controller you're most skeptical about" preserved; "Ongoing" protocol line corrected (S7 + OPC UA Client today, MT-LINKi roadmap — P-G)
- [x] §3.8 architecture uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image; annotations honor §5.A + P-E (eyebrow = ≤4-word title); includes the "Acquisition peer not required" Col-2 clarifier + read-only annotation + mixed-generation muted/sharp styling cue
- [x] §3.9 trust cues cover read-only + offline-first AND per-gateway identity + hash-chained audit; cross-link `/security`; placement after Architecture per P-D
- [x] §3.10 cross-lens cards match the LOCKED §17 preset; related-pillar card leads with Connectivity & Edge per P-C (differentiating capability), Operational Intelligence cross-linked inline in §3.3
- [x] §3.11 final CTA uses §2.3-aligned framing ("Bring us your oldest CNC" headline + "Request an architecture review" primary button — NOT "Book a scoping call", a §2.3 backfire; pre-lock workflow HIGH fix / precedent P-H); "staged for the camera" inversion preserved
- [x] EdgeConnect + EREMOS V2 positioning matches the LOCKED `/capabilities/connectivity-edge` + `/capabilities/operational-intelligence` specs
- [x] DISTINCT from `/solutions/edge-connectivity` v2.1 — modernization-business-case framing, not the product-edge depth-example; §1.1 IS-NOT boundary held
- [x] No vocabulary that backfires per buyer-taxonomy §2.3 (no *"rip and replace"* as Elpis framing, *"single source of truth"*, *"seamless"*, *"smart factory"*, *"future-proof"*, *"AI insights"*)
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names (Fanuc/Brother/Mazak/Okuma are floor reality, not competitive comparison)
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] Phase 1 v2 voice character preserved (D6)
- [x] Inherited pattern-setter precedents P-A..P-G applied + documented (header + section notes)
- [x] Batch ChatGPT review pass applied (with the other 3 migrations)
- [x] Pre-lock validation workflow PASSED over the batch (cross-spec drift + §15/§9 coverage + discipline-lock guard)

---

## 8. Out of scope for v1 (v3 content)

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers the full matrix with semantic modes (FOCAS2 polled vs subscription, model-by-model coverage, OPC UA Server security profiles), per-protocol integration test patterns, FOCAS2 connection-pool sizing, MT-LINKi REST detail.
- **Full EREMOS V2 capability detail.** `/capabilities/operational-intelligence` (LOCKED) covers OEE / alarms / multi-tenant as a Pillar 5 capability; this page cross-links rather than duplicating.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2.1) covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1) covers the cross-pillar Industrial Intelligence Stack; cross-link for the full stack story.
- **The product-edge OT-consolidation depth-example.** `/solutions/edge-connectivity` (LOCKED v2.1) covers the EdgeConnect+Edge-Gateway-specific cross-vendor edge story (deployment shapes, OPC UA Server, multi-site fleet patterns across all controller classes); this page is the broader modernization-without-replacement business case.
- **CNC-shop / precision-mfg / multi-site / OEM framings.** The sibling solution pages (their own v3 migrations in this Phase E wave) cover those outcomes.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links from §3.9.
- **Industries-specific framings.** Phase 3 `/industries/<industry>` (or Phase 2.5 single-industry exception per amendment v3 §2).
- **Pricing / commercial engagement detail.** `/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail.
- **Quantified OEE-gain / capital-avoidance / cost-savings figures (including the dropped "$500K+" controller-replacement number).** Wait for Phase 3 customer-story registry + the `/platform` commercial teaser.
- **Real customer case studies / named deployment stories.** Phase 3 customer-story sign-off process.

---

*`/solutions/brownfield-modernization` Page Spec **v1 LOCKED 2026-06-04** (page content v3 — SolutionPanel migration of the Phase 1 v1→v2 page copy) after the batch ChatGPT review + pre-lock validation workflow (run wf_e86046ac-cdb — this page carried the batch's single HIGH: OT-Architect CTA misalignment, fixed via precedent P-H, + 1 MED meta-title band). Part of the Phase E batch-of-4 migration (precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations), inheriting the LOCKED pattern-setter precedents P-A through P-G from `page-solutions-cnc-machining-spec-v1.md`. Migrates `solution-brownfield-modernization-v2.md` into the §9 canonical per-page-spec format + SolutionPanel §15 layout, adding the four §15 ecosystem-framing additions (pillar cross-refs, trust cue, ArchitecturePanel.interactive, cross-lens), §1.4 metadata, inline FAQ with FAQPage schema, and the "How this differs from rip-and-replace modernization" callout. Includes the optional Typical Engagement section (P-A; deployment anxiety is this page's defining buyer objection). Source-of-truth alignment baked into the draft: MT-LINKi → roadmap (P-G; corrected from the Phase 1 v2 today-list across hero / trust strip / §3 / §4 / §7); S7 + OPC UA Client corrected to today (P-G); EdgeConnect Windows-today/Linux-roadmap; appliance optional; per-gateway identity; beside-not-replacing; read-only/observational; anti-overclaim "cut"-verb hedging; the Phase 1 "$500K+" number dropped. Held DISTINCT from `/solutions/edge-connectivity` v2.1 (modernization-business-case framing, not the product-edge depth-example). Phase 1 voice character preserved (D6): "The iron stays. The data layer modernizes." / "the case for ripping them out exists only in vendor presentations." / "your operators don't need to change a single behavior." / "pick the controller you're most skeptical about." / the data-layer-outlasts-the-iron close. Locked as part of the 5-page wave; ships together (merge is the maintainer's call). Cites: page-capabilities-hub-spec-v1 §9, design-system-v3 §15/§16/§17/§5.A, buyer-taxonomy-v1 §2.3/§2.2, proof-architecture-v1 §3/§4/§8, page-capabilities-connectivity-edge-spec-v1 v2.1, page-capabilities-operational-intelligence-spec-v1 v1, page-architecture-spec-v1 v2.1, page-solutions-cnc-machining-spec-v1 v1 (LOCKED pattern-setter), page-solutions-edge-connectivity-spec-v1 v2.1 (sister exemplar — distinctness boundary), page-solutions-predictive-maintenance-spec-v1 v2, solution-brownfield-modernization-v2 (migrated source), solution-oem-machine-monitoring-v2 (anti-overclaim precedent), shared-knowledge/contracts/cnc-vocabulary.md, 2026-06-04-phase-e-solution-migration-plan.md.*
