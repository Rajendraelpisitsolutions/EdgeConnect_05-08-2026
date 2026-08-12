<!--
File:        docs/marketing/page-solutions-precision-manufacturing-spec-v1.md
Purpose:     Page spec for /solutions/precision-manufacturing — the
             OEE-defensibility / quality-accountability vertical for the
             high-mix precision shop and Tier-2/Tier-3 supplier. Phase E
             batch migration of solution-precision-manufacturing-v2.md
             (Phase 1 page copy) onto the SolutionPanel §15 layout / §9
             canonical per-page-spec format. INHERITS the locked
             pattern-setter precedents P-A..P-G from
             page-solutions-cnc-machining-spec-v1.md (page content v3).
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim copy), user + ChatGPT
             (reviewers), Phase E batch-migration authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   solution-precision-manufacturing-v2.md (Phase 1 page copy
                being migrated — voice + structure precedent; SUPERSEDED
                by this spec at v3 lock; retained as voice reference)
             page-solutions-cnc-machining-spec-v1.md v1 (LOCKED
                PATTERN-SETTER — the structural model + the four §15
                ecosystem-framing additions + P-A..P-G precedents this
                spec inherits; the SISTER VERTICAL whose IS-NOT boundary
                this page must stay distinct from)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED —
                sister SolutionPanel depth-example; the protocol-agnostic
                cross-vendor OT-consolidation story across ALL controller
                classes — distinct from this page's quality-accountability
                cut)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED
                — sister SolutionPanel depth-example; the reliability /
                condition-monitoring story — distinct from this page)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page-type FAQ governance — /solutions/<solution> =
                YES; metadata governance; "How this differs from…"
                emerging-pattern governance)
             page-capabilities-operational-intelligence-spec-v1.md v1
                (LOCKED — source-of-truth for EREMOS V2 / OEE / alarms;
                this page's LEAD pillar)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED
                — source-of-truth for EdgeConnect + Edge Gateway
                positioning + the today-protocol list; this page's
                inline secondary pillar)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-link
                target for "See full architecture"; multi-plant-
                EdgeConnect FAQ Q6)
             page-solutions-hub-spec-v1.md v2 (LOCKED — /solutions hub
                directory this depth-example sits under)
             buyer-taxonomy-v1.md §2.2 (Plant manager / Ops VP — primary
                buyer; "OEE you can defend" vocabulary) + §2.3 (OT
                Architect — secondary)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics,
                no customer names, no competitor names)
             design-system-v3.md §15 (SolutionPanel — LOCKED) + §16
                (trust cue content pattern) + §17 (cross-lens LOCKED
                preset for /solutions/<solution>) + §5.A
                (ArchitecturePanel.interactive solution-annotated variant)
             shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC
                tag set — spindle_rpm, feed_rate, parts_count, cycle_time,
                axes, tool, alarm codes; per-tag quality codes)
             solution-oem-machine-monitoring-v2.md (anti-overclaim "cut"-
                verb hedging precedent)
             2026-06-04-phase-e-solution-migration-plan.md (the bulk-
                migration plan-trail this spec executes; D1-D6 decisions;
                §3 buyer/pillar map)
Version:     v1 (page content v3 — SolutionPanel migration of the Phase 1
                v1→v2 page copy). LOCKED 2026-06-04 — part of the Phase E batch of 4
                (precision-manufacturing, brownfield-modernization, oem-
                machine-monitoring, multi-site-operations) that follows
                the LOCKED CNC pattern-setter. Pending the single batch
                ChatGPT review + pre-lock validation workflow; locks +
                ships with all 5 as one wave (design-system v3 §15 Q3
                bulk-migration lock).
Date:        2026-06-04
Status:      LOCKED 2026-06-04 (page content v3). Batch ChatGPT review +
             pre-lock validation workflow PASSED — LOCK-clean (0 HIGH /
             0 MED; one LOW §5 reconciling parenthetical applied). Locks +
             ships as one wave with the other 4.

PATTERN INHERITANCE. This spec is NOT a pattern-setter; it applies the
precedents locked on page-solutions-cnc-machining-spec-v1.md (page
content v3). The inherited precedents (full detail in the CNC spec's
header workflow note + the migration plan-trail §"Pattern-setter
outcome"):
  P-A Typical Engagement is an optional §15 section; include where
    deployment anxiety is a real buyer objection, documenting the
    rationale + the word-budget consequence. → INCLUDED here (same
    Plant-manager buyer; the "how long does this take?" objection is
    explicit in buyer-taxonomy §2.2; the Phase 1 v2 §7 timeline is
    strong, buyer-validated content framed around the most-watched
    cell). 11-section page; +~200 over-ceiling documented in §5.
  P-B whatsIncluded buckets follow product-narrative groupings, not
    literal schema field names; document the bucket choice + omissions.
    → 2 buckets (EdgeConnect + EREMOS V2); hardwareProducts omitted
    (precision shops run software-only on a control-cabinet box; Edge
    Gateway is an inline deployment option). See §3.4 preamble.
  P-C when a solution touches multiple pillars, the §17 cross-lens
    related-pillar card leads with the DIFFERENTIATING capability.
    → DIVERGES from CNC's lead-pillar by design (see below): this page
    LEADS Operational Intelligence; Connectivity & Edge is the inline
    secondary. The differentiator for the precision narrative IS the
    OEE/quality-accountability layer (one auditable OEE definition,
    per-tag quality codes, traceable-to-the-cycle provenance), NOT raw
    cross-vendor collection — so the cross-lens related-pillar card
    leads with Operational Intelligence. This is the documented,
    intentional mirror-image of CNC under the SAME P-C rule: "lead with
    the differentiator." Confirmed against the source v2's actual
    emphasis (§3 "EREMOS V2 computes OEE Segments with one definition"
    rendered with visual prominence; §8 caption "the numbers trace back
    to the signals") + the migration-plan §3 map (Precision mfg lead
    pillar = Operational Intelligence, other = Connectivity & Edge).
  P-D trust-cue placement follows the realized exemplar order (after
    Architecture, before Cross-lens/CTA), not the literal §15 prose.
    → §3.9.
  P-E architecture-annotation eyebrow doubles as the ≤4-word title.
    → §3.8.
  P-F vertical hero subhead carries the relevant protocol SUBSET; full
    list in the trust strip / §3.4. → §3.1.
  P-G MT-LINKi → roadmap only; S7 + OPC UA Client → today (the two
    correctness fixes the migration surfaces vs the stale Phase 1 v2
    page). → applied corpus-wide here (see source-of-truth note).

What the migration ADDS vs the Phase 1 v2 page copy (the four §15
ecosystem-framing additions + §9 governance additions):
  1. Pillar cross-references — §3.3 names the contributing pillars
     (Operational Intelligence LEAD + Connectivity & Edge inline) with
     inline /capabilities/<pillar> cross-links (NEW vs v2).
  2. Trust cue — §3.9 applies the §16 content pattern (2 cues, /security
     cross-link) (NEW vs v2). Cue 1 reframes the v2 "audit-ready by
     architecture" + per-tag-quality-code line into the trust posture.
  3. ArchitecturePanel.interactive (variant=solution-annotated) — §3.8
     replaces the v2 static SVG diagram with the §5.A interactive
     annotated subset (NEW vs v2).
  4. Cross-lens navigation — §3.10 applies the §17 LOCKED preset (NEW vs
     v2).
  + §1.4 metadata block (§9 metadata governance).
  + Inline FAQ reframed with FAQPage schema (§9 per-page-type FAQ
    governance; the v2 §5 "questions precision shops raise" list is
    carried over near-verbatim — it is strong scoping/procurement Q&A
    already — with FAQPage markup).
  + "How this differs from spreadsheet / per-machine-export OEE
    reporting" callout in §3.3 (§9 emerging-pattern governance — a REAL
    adjacent category for this buyer: the quarterly-export spreadsheet
    that the source v2 §2 already names as "the trap").

DISTINCT-FROM-CNC discipline (IS-NOT boundary — see §1.1). The CNC
pattern-setter is the multi-vendor-CNC-shop entry vertical: the story is
"one OEE truth + canonical vocabulary + no per-machine scripting across a
mixed-VENDOR floor," led by Connectivity & Edge. Precision manufacturing
is the SAME platform but a DISTINCT buyer reality and a DISTINCT lead
pillar:
  - Quality/tolerance angle — parts measured in microns; every part has a
    customer attached; OEE numbers must survive a Tier-1 customer audit.
  - OEE DEFENSIBILITY (not just OEE visibility) — "one definition,
    traceable to the cycle, per-tag quality codes" is the spine.
  - Higher-mix operational reality — more product families per week;
    constant setup changes; the shop thinks in CELLS, not machines
    (per-cell aggregation; small-batch/high-mix configured per cell).
  - Customer-data-sharing via route-based architecture (Tier-2 supplier
    publishes only the contracted customer's signals).
This page does NOT re-skin CNC: it leads with the audit-defensibility /
quality-accountability layer and the cell-centric high-mix reality. CNC
cross-linked as the sibling vertical inline in §3.3 / §1.1.

Source-of-truth alignment baked into this draft (migration plan D5;
inherited precedent P-G):
  - MT-LINKi → ROADMAP mention, REMOVED from the today-list. The Phase 1
    v2 page listed MT-LINKi as operator-available today across
    §1/§3/§4/§5 (hero subhead, trust strip, §3 ¶1, §4 bullet); this
    migration corrects it per side-flag #1 resolution (2026-06-04) +
    /platform v2.1 §6 re-add governance. (These 5 solution pages were
    among the "53 untouched" files; the migration is where the stale
    claim is fixed.) This is the most invasive single correctness fix on
    THIS page — the v2 carried MT-LINKi in the hero subhead AND the
    trust strip, both prominent.
  - S7 + OPC UA Client are NOW operator-available (CLAUDE.md §8 + locked
    connectivity-edge v2). The Phase 1 v2 page did not surface them at
    all; this migration ADDS them to the today-list (the rest-of-the-
    floor coverage), matching the CNC pattern-setter.
  - Precision today-protocol list (CNC-relevant subset): FOCAS2,
    MTConnect, Brother HTTP, Modbus TCP — plus OPC UA Client + S7
    available; FANUC MT-LINKi REST on the roadmap. Full protocol matrix
    lives on Phase E /edgeconnect; this page stays at solution-level
    vocabulary.
  - EdgeConnect = Windows service today; Linux near-term roadmap (on Edge
    Gateway). Edge Gateway is an OPTIONAL deployment note (precision
    shops typically run software-only on a small control-cabinet box) —
    NOT positioned as required.
  - Per-gateway identity / anti-multi-plant-EdgeConnect; "beside not
    replacing" SCADA/MES/historian + quality systems; offline-first.
  - Anti-overclaim: "cut" / "reduce" verbs only (OEM v2 precedent). NOTE:
    the v2 §6 outcome "Tool failures caught weeks ahead of replacement"
    carries an unhedged-absolute risk (mirrors CNC MF3); rehedged here to
    "Tool wear trended ahead of failure" (mechanism-tethered).
  - No industry-certification claims (IATF 16949 / AS9100 / ISO 9001).
    The v2 §1 audience-qualifier line ("environments operating under
    strict customer quality and audit requirements") is framework-NEUTRAL
    by design and preserved verbatim; the §8 out-of-scope guard is kept.

Voice preservation (migration plan D6): the Phase 1 v2 page's signature
lines are lifted into the SolutionPanel shape —
  - "OEE you can defend, on the cells you actually run." (v2 §1 headline)
    → §3.1 hero headline, verbatim.
  - "Stitched OEE numbers don't survive that level of scrutiny." (v2 §2)
    → §3.2 margin pull-quote, verbatim.
  - "The signals trace back to the iron; the numbers trace back to the
    signals." (v2 §8 caption) → §3.8 closing caption, verbatim — the
    audit-defensibility anchor; elevated to a standalone emphasized
    treatment (the analogue of CNC's R3 inevitability-anchor elevation).
  - "the OEE numbers you defend next year are computed the same way as
    the numbers you defend this week" (v2 §7) → §3.7 "Ongoing" step,
    verbatim.

Word-count target: 1,500-1,800 words page copy per /capabilities hub §9
page-type guidance for /solutions/<solution>. Reconciled ~1,920 words
total; 10-section SolutionPanel core ~1,720 (within band); +~200 optional
Typical Engagement section = documented over-ceiling (P-A; migration plan
D4; see §5). The v2 source measured ~1,640; the migration adds the four
§15 framing additions + metadata + the "How this differs" callout, all
within the documented band logic.

Carry-forward side-flag (publish-orchestration, not a spec blocker): when
this page ships live, /solutions hub v2 Card (Precision Manufacturing)
"Coming soon" status pill + pre-live link swap per the /solutions hub
pre-live link policy. Locks + ships with all 5 as one wave.
-->

# `/solutions/precision-manufacturing` — Page Spec v1 (page content v3)

**Solution depth-example for the high-mix precision-manufacturing shop and Tier-2/Tier-3 supplier — the vertical where OEE has to be *defensible*, not just visible. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they need one OEE definition that survives a customer audit across mixed-vendor, mixed-generation cells — Fanuc, Brother, Mazak, Okuma — where every part has a customer attached and every number has to trace back to the cycle that produced it.**

This is the page where precision-shop production managers, operations directors, and quality managers land when they want the **outcome view** of OEE accountability across high-mix cells: one consistent OEE definition, signals collected at the controller and timestamped at the edge, per-tag quality codes, customer-data sharing without exposing the whole shop. It is **not** the capability page (`/capabilities/operational-intelligence` covers EREMOS V2 / OEE; `/capabilities/connectivity-edge` covers EdgeConnect). It is **not** the architecture walkthrough (`/architecture`). It is the **precision-manufacturing solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the high-mix precision-manufacturing shop / Tier-2/Tier-3 supplier. Reader leaves with *"I now understand how I get one OEE definition I can defend to corporate, customers, and auditors across my mixed-vendor, mixed-generation cells; how the numbers trace back to the actual signals; how I share data with a Tier-1 customer without exposing the rest of my shop; how long a rollout takes; and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/operational-intelligence` covers EREMOS V2 / OEE / alarms as the Pillar 5 capability; `/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability; both LOCKED — this page cross-links rather than duplicating)
- A product detail page (Phase E `/edgeconnect` covers the full protocol matrix, semantic modes, per-tag quality-code propagation detail)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2.1)
- The **multi-vendor CNC-shop entry vertical** (`/solutions/cnc-machining` v3 — the sibling vertical — leads with cross-vendor *connectivity* and "no per-machine scripting"; this page is the **quality-accountability / OEE-defensibility** cut for the high-mix precision shop, led by Operational Intelligence and the cell-centric reality)
- The protocol-agnostic / OT-consolidation depth-example (`/solutions/edge-connectivity` v2.1 — the cross-vendor edge story across all controller classes)
- The reliability / condition-monitoring depth-example (`/solutions/predictive-maintenance` v2 — the maintenance-program story)
- A pricing or commercial page (`/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Plant manager / Ops VP (§2.2), in the precision-shop / quality-manager guise — the operations director or quality manager responsible for OEE and quality reporting to corporate or to Tier-1 customers
- Lands here from `/solutions` hub, from the homepage hero, or from a Google search for *"defensible OEE"* / *"OEE for customer audit"* / *"high-mix CNC OEE"* / *"Tier 2 supplier OEE reporting"* / *"OEE without spreadsheets"*
- Wants: one OEE definition that survives a customer audit, signals traceable to the cycle, less quarterly report-stitching, cell-level rollups for a high-mix floor, the ability to share a Tier-1 customer's contracted data without exposing the rest of the shop, a rollout that won't disrupt production
- CTA preference: *"Book a scoping call for your production cells"* > *"Bring us your most demanding cell"* > datasheet download
- Vocabulary that lands: *OEE you can defend*, *cycle-time variance*, *audit-ready*, *mixed-vendor cells*, *per-cell*, *replace spreadsheet operations*, and real protocol/model names (FOCAS2, Brother HTTP, Fanuc 0i/16i/18i/30i) as trust signals
- Vocabulary that backfires: *"digital transformation"*, *"smart factory"*, *"AI insights"*, *"single source of truth"* (cliché), *"seamless"*, *"easy"*

**Secondary buyer:** OT Architect / industrial IT lead (§2.3) — supporting the production environment
- Lands here when the production manager forwards the page for a technical sanity check
- Wants: real protocol coverage with specific Fanuc generation support, canonical vocabulary, per-tag quality-code mechanics, SCADA/quality-system coexistence honesty, store-and-forward mechanics, route-based data-sharing detail
- Served via cross-lens to `/capabilities/connectivity-edge` + `/architecture` (per buyer-taxonomy §5 step 3 — secondary buyers via cross-lens, not primary page content)

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/solutions/cnc-machining` v3 §1.4 + `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Precision Manufacturing — OEE You Can Defend · Elpis* |
| **Meta description** (140-160 chars) | *One defensible OEE definition across high-mix, mixed-vendor cells. Signals collected at the controller, timestamped at the edge, traceable to the cycle.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/precision-manufacturing` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities/operational-intelligence` + `/capabilities/connectivity-edge` + `/architecture` + `/security` use `relatedLink`. Product cards for EdgeConnect + EREMOS V2 (when Phase E product pages ship) via `SoftwareApplication` schema. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). **11 sections** — the 10-section SolutionPanel shape (same as `/solutions/cnc-machining` v3) plus the **optional Typical Engagement** section, included per P-A / migration plan D4.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~80 |
| **2** | The Precision-Manufacturing Reality (customer pain) — narrative empathy, 3 paragraphs | `light` | Narrative copy + optional margin pull-quote | ~210 |
| **3** | How Elpis Solves Precision-Manufacturing OEE — 4 bolded-lead paragraphs + pillar cross-refs + "How this differs from spreadsheet OEE reporting" callout | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block | ~390 |
| **4** | What's Included — From EdgeConnect + From EREMOS V2 | `light` | Bulleted feature lists with bolded leads | ~260 |
| **5** | Questions Precision Shops Raise (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions + answers + `FAQPage` schema | ~360 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column desktop | `dark` | Bolded outcome leads + supporting clauses | ~130 |
| **7** | How Precision Shops Typically Roll This Out — 4-step timeline *(optional §15 section, included)* | `light-tinted` | 4-step horizontal timeline | ~200 |
| **8** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" | ~80 |
| **9** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | Trust cue content pattern (design-system v3 §16) | ~80 |
| **10** | Cross-lens navigation — LOCKED preset per §17 | `light-tinted` | Cross-lens content pattern (3 cards) | ~50 |
| **11** | Final CTA — vertical-localized "Bring us your most demanding cell" | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · PRECISION MANUFACTURING
>
> HEADLINE (size.3xl semibold):
> OEE you can defend, on the cells you actually run.
>
> SUBHEAD (size.lg, max-width 60ch):
> Native FOCAS2, MTConnect, and Brother HTTP — plus Modbus TCP for the PLC-fronted older CNCs — across high-mix, mixed-vendor production cells. Every input — cycle time, parts count, alarm state, tool wear — collected at the controller and timestamped at the edge. Designed for environments operating under strict customer quality and audit requirements.
>
> PRIMARY CTA (`Button.primary.lg`):
> Book a scoping call for your production cells
> HREF: `/contact?intent=precision-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Download the datasheet
> HREF: `/resources/datasheet`

> TRUST STRIP (under hero, size.sm):
> *Live integrations on Fanuc 0i / 16i / 18i / 21i / 30i / 31i / 32i (FOCAS2) · Brother S700Xd1 (Brother HTTP) · MTConnect · Modbus TCP — and OPC UA Client and Siemens S7 for the rest of the floor. FANUC MT-LINKi REST on the roadmap. Canonical CNC vocabulary across vendors.*

**Anti-patterns:** No *"seamless"* / *"intuitive"* / *"easy"* / *"single source of truth"* framing (buyer-taxonomy §2.2 vocabulary discipline). No outcome metric in the headline. Hero leads with the **outcome** ("OEE you can defend, on the cells you actually run"), not the products (EdgeConnect + EREMOS V2) — per §15 anti-pattern. Headline + the "Designed for environments operating under strict customer quality and audit requirements" qualifier preserved verbatim from Phase 1 v2 §1 (voice; framework-neutral audience qualifier — no IATF/AS9100/ISO claim).

> **Inherited precedent note (P-F) — solution-page hero subhead protocol enumeration.** The hero subhead names the **vertical-relevant protocol subset** (the 4 precision-central CNC protocols), with the full six-protocol list carried in the trust strip directly beneath. **Correctness fix surfaced by the migration (P-G):** the Phase 1 v2 §1 hero subhead AND trust strip both listed MT-LINKi as a current integration; both are corrected here — MT-LINKi REST moves to the roadmap line of the trust strip, and OPC UA Client + Siemens S7 are added to the today-list. The hero subhead lists the four core protocols; the full list lives in the trust strip per P-F.

---

### 3.2 Section 2 — The Precision-Manufacturing Reality

> EYEBROW: THE PRECISION-MANUFACTURING REALITY
>
> NARRATIVE PARAGRAPH 1 (size.base):
> A precision-manufacturing shop runs more product families in a week than most general CNC shops run in a month. Setup changes are constant. Tooling lifetimes are tracked to fractions of a percent. Tolerances are measured in microns. Every part has a customer attached to it, and every customer expects OEE numbers their auditor will accept.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> The floor that produces those parts is rarely uniform. A Fanuc 30i from 2022 runs alongside an 18i from 2011. A Brother S700Xd1 handles one product family; a Mazak Integrex handles another. Each machine's vendor-supplied dashboard reports OEE differently. Tool-life tracking lives in one system, parts counts in another, alarm history on a third. The numbers don't reconcile. The customer asks "what's the OEE for the cell that machines our part?" — and the answer takes a week to stitch together.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The trap is treating OEE as a reporting problem — a spreadsheet that pulls from each system's quarterly export. That works until the customer audit team asks for the underlying signal data, the timestamp of every cycle, and proof that the math is consistent with the contract. Defensible OEE isn't an analytics layer bolted on after the fact; it requires the signals to be collected at the controller, normalized at the edge, and computed centrally with one consistent definition.

> OPTIONAL MARGIN PULL-QUOTE (desktop, size.lg italic):
> *"Stitched OEE numbers don't survive that level of scrutiny."*

**Note (voice):** the margin pull-quote *"Stitched OEE numbers don't survive that level of scrutiny"* is preserved verbatim from Phase 1 v2 §2 per migration plan D6 — the recognition line for this buyer. No bullet lists in this section — the challenge is a narrative (subdued empathy treatment, not pitch). **Distinct-from-CNC note:** where CNC §3.2 frames the pain as "each dashboard speaks its own dialect" (a *vocabulary*-fragmentation pain), precision frames it as "stitched numbers don't survive an audit" (a *defensibility* pain) and the cell-/customer-centric reality ("every part has a customer attached"). Same platform, distinct buyer wound.

---

### 3.3 Section 3 — How Elpis Solves Precision-Manufacturing OEE

> EYEBROW: HOW ELPIS SOLVES PRECISION-MANUFACTURING OEE

> CALLOUT — HOW THIS DIFFERS FROM SPREADSHEET / PER-MACHINE-EXPORT OEE REPORTING (size.base, single paragraph; bordered card or left-rule callout, sits before the bolded-lead paragraphs):
>
> > **How this differs from spreadsheet OEE reporting.** Most precision shops already produce OEE — by exporting each system's quarterly numbers into a spreadsheet and reconciling by hand. It works until an auditor asks for the signal behind the number. A spreadsheet can show you a figure; it can't trace that figure back to the cycle that produced it, prove the math is consistent across cells, or tell you whether a reading was Good, Uncertain, or Stale when it was taken. Elpis computes OEE on canonical signals collected at the controller and timestamped at the edge — one definition, the same math across every cell and shift, traceable end to end. The spreadsheet was reporting; this is provenance.

#### Bolded-lead paragraphs (4 paragraphs):

> **EREMOS V2 computes OEE Segments with one definition.** This is the spine of the solution. Availability, Performance, and Quality are calculated centrally from edge-collected signals using one consistent set of rules across every cell on your floor — so the number you ship to corporate this week is computed the same way as the number you shipped last quarter, and the number a Tier-1 customer audits traces back to the actual cycle data, not a hand-reconciled spreadsheet. OEE Segments (RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP) are derived from cycle-time and parts-count signals, each timestamped at the edge and retained. This is the **Operational Intelligence** capability applied to a precision floor — see the underlying capability story → `/capabilities/operational-intelligence`.
>
> *(Render this opening paragraph with slight visual prominence — it is the page's central OEE-defensibility promise and the differentiating lead.)*

> **One platform reads every controller in the cell.** EdgeConnect polls each CNC over its native protocol — FOCAS2 for Fanuc (every generation), MTConnect for the newer multi-vendor machines, Brother HTTP for the Brother fleet, Modbus TCP for older CNCs fronted by a PLC. OPC UA Client and Siemens S7 cover the rest of the floor; FANUC MT-LINKi REST integration is on the roadmap. One service on a small box in your control cabinet, regardless of how many vendors and generations the cell contains. This is the **Connectivity & Edge** capability beneath the OEE story → `/capabilities/connectivity-edge`. And because every signal normalizes to the same canonical vocabulary, the OEE math doesn't have to translate between vendors: a spindle-RPM reading from a 2022 Fanuc 30i and a 2011 Brother S700Xd1 both arrive as `spindle_rpm`; cycle time, parts count, tool number, alarm code, and axis positions collapse the same way — the inputs already speak the same language.

> **Tool-life and cycle-time precision matter as much as the OEE rollup.** Tool-life telemetry is trended per cell, per tool family, per product, so wear is visible across batches rather than within a single run. Cycle-time variance is visible per machine, per shift, per part run. When a customer asks *"why did this batch's cycle time drift 3%?"*, the platform has the per-cycle history to answer — root-cause analysis becomes possible, not just retrospective. For a high-mix shop the rollups land at the **cell** level, because cells matter more than individual machines when every cell maps to a product family and, often, a customer.

> **Audit-ready by architecture, and nothing depends on the cloud.** Per-tag quality codes (Good / Uncertain / Bad / Stale) propagate end to end, so a downstream consumer can distinguish a real reading from a stale or uncertain one — the data layer is built for the audit before the auditor asks. EdgeConnect runs offline: if your network or broker drops, per-route store-and-forward buffers locally and replays in source order on reconnect — no lost cycles, no missing parts counts. Three-way diagnostics (source / pipeline / sink) tell the OT team exactly which leg broke before the production team feels the symptom.

**Note (voice + pillars):** the *"EREMOS V2 computes OEE Segments with one definition"* lead and the *"audit-ready by architecture"* framing are preserved from Phase 1 v2 §3. Pillar cross-refs (Operational Intelligence LEAD + Connectivity & Edge inline) are the NEW §15 ecosystem-framing addition vs v2. **Pillar-balance note (inherited from CNC R5, mirror-imaged):** the 4-paragraph arc runs **OEE-defensibility (Operational Intelligence) → Connectivity + Canonical Vocabulary → Tool-life/cycle-time precision → Audit-ready + offline resilience.** Paragraph 1 leads with Operational Intelligence because OEE *defensibility* is what makes this solution distinct (the differentiator), the inverse of the CNC pattern-setter where Connectivity & Edge led. Connectivity & Edge remains the architectural foundation and carries real narrative weight in ¶2, but Operational Intelligence is the lead and the cross-lens differentiator (P-C).

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets — `edgeConnect` (the edge runtime; the cell-side story) + `eremosV2` (the intelligence layer; the OEE-defensibility story). The standalone `hardwareProducts` bucket is **omitted** — precision shops typically run software-only on a small control-cabinet box; the Edge Gateway appliance is a deployment option mentioned inline, not a lead bucket on this page. (Bucket-narrative governance per P-B / migration plan D2/D5 + the CNC pattern-setter §3.4: solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names; precision keeps the discrete `edgeConnect` bucket because the software runtime on customer hardware is the precision reality, same as CNC.)

#### From EdgeConnect (edge runtime, Windows service today)

> - **FOCAS2 collector across every Fanuc generation** — 0i, 16i, 18i, 21i, 30i, 31i, 32i. Axes, spindle, alarms, tool, production counters, programs. The protocol coverage doesn't quietly drop your oldest iron next to your newest.
> - **MTConnect collector** — the industry-standard CNC streaming protocol; covers the newer multi-vendor machines in the cell.
> - **Brother HTTP collector** — Brother S700Xd1 and similar models via the built-in web-monitoring interface.
> - **Modbus TCP collector** — for older CNCs fronted by a PLC gateway, and for inspection equipment that publishes over Modbus.
> - **Also today:** OPC UA Client and Siemens S7 for the rest of the floor. **On the roadmap:** FANUC MT-LINKi REST. For the full protocol matrix with semantic modes, see Phase E `/edgeconnect` (coming soon).
> - **Canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions, tool numbers and offsets, alarm codes. Same names, every vendor, every generation.
> - **Per-tag quality codes** — every signal carries a quality state (Good / Uncertain / Bad / Stale) end to end. Downstream consumers can tell a real value from a stale one — the foundation of an audit-defensible number.
> - **Per-route store-and-forward buffering** — never lose a cycle or a parts-count update because the broker was down.
> - **Three-way diagnostics** — source / pipeline / sink. When data quality looks off, operators see exactly where it broke.
> - **Connectivity Studio** — web admin to add machines, configure tag maps, and run Test Connection probes before anything goes live.

> > *Deployment note — EdgeConnect ships as a Windows service today; precision shops typically run it on a small box in the control cabinet. A Linux runtime is near-term roadmap, arriving on the Edge Gateway appliance for shops that prefer a turnkey DIN-rail box. The appliance is an option, not a requirement.*

#### From EREMOS V2 (intelligence layer, consuming the canonical stream)

> - **OEE Segments with one consistent definition** — Availability × Performance × Quality computed centrally from edge-collected signals; auditable; exportable. Configured to *your* shop's OEE definition, not one of ours.
> - **Per-cell aggregation** — OEE rollups at the cell level for high-mix shops where cells matter more than individual machines.
> - **Tool-life tracking per tool family** — wear trended across every tool in use, per cell and per product; surfaced ahead of failure.
> - **Cycle-time variance per machine, per shift, per part run** — root-cause analysis becomes possible when every cycle is on the record.
> - **Persistent alarm tracking with incident grouping** — alarm patterns visible across days and weeks, not just within one shift or one machine's HMI.
> - **Route-based customer-data sharing** — publish exactly the signals a Tier-1 customer is contracted to see, for their parts only, without exposing other customers' production data or the shop's full operational picture.
> - **Shift reports in PDF and Excel** — the reports customer-quality teams want, in the formats they accept, built from edge-collected signals.
> - **Multi-tenant by design** — one platform, multiple sites or business units; per-customer dashboards for shops reporting to multiple Tier-1 customers, no data leakage.

---

### 3.5 Section 5 — Questions Precision Shops Raise

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions carried over near-verbatim from the strong Phase 1 v2 §5 list (it already targets precision production-manager / quality-manager scoping concerns), reframed with `FAQPage` markup and aligned to the corrected protocol list.

> EYEBROW: QUESTIONS PRECISION SHOPS RAISE
>
> SECTION TITLE:
> What production and quality managers ask before scoping a cell.

#### Q1. Can we trace OEE numbers back to specific part runs?

> Yes — that's the point of computing OEE on canonical signals. Every signal is timestamped at the edge and retained, and OEE Segments link back to the cycle-level data that produced them. A customer asking *"show me the OEE for this batch"* can be answered with the underlying signal history, not a hand-reconciled spreadsheet export. **And the OEE definition stays yours** — segment classification, shift schedule, and targets are configured to how your shop already defines OEE; the platform computes against your definition, not one of ours.

#### Q2. Which controllers do you collect from today, including our oldest Fanuc next to our newest Mazak?

> Today: Fanuc over FOCAS2 across every generation (0i, 16i, 18i, 21i, 30i, 31i, 32i), the newer multi-vendor machines over MTConnect, Brother over Brother HTTP, and older CNCs fronted by a PLC over Modbus TCP. OPC UA Client and Siemens S7 cover the rest of the floor. FANUC MT-LINKi REST integration is on the roadmap. Your oldest Fanuc and your newest Mazak are handled identically — the canonical vocabulary normalizes both to the same names, so the dashboard doesn't know (or care) which vendor produced which reading. For the full protocol matrix, see Phase E `/edgeconnect` (coming soon); the exact controller mix is confirmed during the scoping call.

#### Q3. Do we still have to maintain separate quality records?

> The platform handles production-signal OEE inputs. Inspection-equipment data can flow in via Modbus TCP or MQTT if your quality systems publish that way; otherwise quality data stays in your existing system. Elpis sits **beside** your quality system, SCADA, MES, and historian — it doesn't replace them; they keep their jobs and consume canonical signals instead of vendor-specific ones. Less duplication, fewer fragmented records.

#### Q4. How do we handle small-batch / high-mix production where every cell is different?

> OEE Segments are configured per cell against that cell's actual shift schedule and product mix. The platform doesn't impose a one-size-fits-all OEE model on a high-mix shop. Per-cell aggregation means each cell reports against the reality it runs, while the math stays consistent across the floor.

#### Q5. Can we share OEE data with a Tier-1 customer without exposing other production data?

> Yes. Route-based architecture lets you publish exactly the data a customer is contracted to see — alarm state, cycle time, parts count for their parts only — without exposing other customers' production data or your shop's full operational picture. This is the most important commercial moment for a Tier-2 supplier serving several Tier-1 customers under different data-sharing contracts.

#### Q6. What happens when the network or the broker drops?

> Per-route store-and-forward. Every signal queues at the source with its quality code preserved, and replays in source order when connectivity returns — no lost cycles, no missing parts counts. Three-way diagnostics (source / pipeline / sink) surface immediately during the outage, so the OT team sees exactly which leg was affected. Shops on an isolated network operate the same way; cloud connectivity is opt-in, not required.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **OEE you can defend to corporate, customers, and auditors** — one consistent definition, edge-collected signals, traceable back to the controller
> - **High-mix cells behave as one production system** — the canonical vocabulary normalizes mixed-vendor, mixed-generation controllers
> - **Tool wear trended ahead of failure** — tool-life telemetry across every tool in use surfaces wear before a tool fails mid-cycle, instead of discovering it after a scrapped part
> - **Cycle-time variance becomes diagnosable** — per-machine, per-shift, per-part-run history makes root-cause analysis possible
> - **Customer-data sharing without exposing the whole shop** — route-based architecture publishes only what each customer is contracted to see
> - **Cut the quarterly report-stitching** — OEE numbers come from edge-collected signals, not a spreadsheet reconciled from each system's export
> - **Audit-ready data layer from day one** — hash-chained config history, per-tag quality codes, full signal retention; OEE traces back to real signals

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific OEE-percentage or dollar-cost-savings claims. The `/platform` commercial teaser and Phase 3 customer-story registry handle quantified outcomes once the customer-evidence registry is in place. **Anti-overclaim fix surfaced by the migration:** the Phase 1 v2 §6 outcome *"Tool failures caught weeks ahead of replacement"* (unhedged absolute) is rehedged here to *"Tool wear trended ahead of failure"* (mechanism-tethered), mirroring the CNC pattern-setter MF3. Outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero" (OEM v2 anti-overclaim precedent).

---

### 3.7 Section 7 — How Precision Shops Typically Roll This Out

*Optional §15 section (P-A), included per migration plan D4 — deployment-anxiety is an explicit Plant-manager objection (buyer-taxonomy §2.2 "how long does this take?"), and the Phase 1 v2 §7 "start with your most-watched cell" framing is strong, buyer-validated content.*

> EYEBROW: TYPICAL ENGAGEMENT
>
> SECTION TITLE:
> How precision shops typically roll this out.

**Four-step horizontal timeline on desktop; vertical stack on mobile. Each step: label, headline, 2-3 line description.**

> **Week 1 — Proof on the most-watched cell.** Pick the cell that produces parts for your most demanding customer — or the one whose OEE numbers you're least sure of defending. EdgeConnect installed on a small Windows box in your control cabinet, polling that cell's controllers over FOCAS2 or Brother HTTP. EREMOS V2 displaying real cycle-time, parts-count, and alarm data, typically within the first few days.
>
> **Weeks 2-4 — Expansion across the cell or product family.** Add the rest of the machines in that cell, or the cells that handle related product families. Tag maps authored together with your team — your operators know the names that matter. OEE Segments configured against your actual shift schedule and product mix.
>
> **Weeks 5-8 — Full shop rollout.** Remaining cells onboarded. Cell-level and shop-level OEE aggregation in EREMOS V2. Per-customer dashboards if your shop reports to multiple Tier-1 customers separately. Audit-export workflows configured.
>
> **Ongoing.** New cells, new product families, new customers — all onboard through the same architecture. FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 all ship today; FANUC MT-LINKi REST is on the roadmap. *The OEE numbers you defend next year are computed the same way as the numbers you defend this week.*

**Note:** the "typically within the first few days" phrasing and the *"the OEE numbers you defend next year are computed the same way as the numbers you defend this week"* consistency line (both Phase 1 v2 §7) are preserved — the latter is the long-term-consistency anchor. **Correctness fix surfaced by the migration (P-G):** the Phase 1 v2 §7 "Ongoing" line did not name the today-protocol list explicitly; the corrected list (S7 + OPC UA Client ship today; MT-LINKi roadmap) is added here for parity with §3.4 / §3.5 Q2 and the CNC pattern-setter.

---

### 3.8 Section 8 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How it fits together for precision production.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15). Replaces the Phase 1 v2 static SVG (NEW §15 ecosystem-framing addition):

Solution-annotated subset of the Industrial Intelligence Stack 4-column layout. Highlights:
- **Col 1 — Floor:** Mixed-generation CNCs across high-mix cells (Fanuc multiple generations · Brother · Mazak · Okuma · Modbus-fronted older CNCs) — highlighted as the signal sources for this solution
- **Col 2 — EdgeConnect peer (highlighted):** one runtime polling every controller in every cell over its native protocol, normalizing to canonical CNC vocabulary, propagating per-tag quality codes. *For a precision floor, the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required — EdgeConnect carries the floor-side.*
- **Col 3 — EREMOS V2 (highlighted, with visual emphasis on the OEE-computation path):** consuming the canonical stream; OEE Segments computed centrally with one consistent definition, per-cell aggregation, tool-life and cycle-time trending, route-based customer-data sharing
- **Col 4 — Customer Enterprise:** SCADA / MES / historian / quality systems (highlighted as systems FED by the canonical stream, not replaced — explicit "beside, not replacing" arrow direction)

**Annotations (4 specific to this solution, per §5.A / P-E: the eyebrow doubles as the ≤4-word annotation title, followed by a 1-2 sentence body; max 8 annotations per zoom level):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 1 → Col 2 protocol arrows | NATIVE PROTOCOLS | EdgeConnect polls each controller over its native protocol — FOCAS2 (Fanuc, every generation), MTConnect, Brother HTTP, Modbus TCP for PLC-fronted older machines, plus OPC UA Client + S7. *MT-LINKi REST on roadmap.* |
| Col 2 → Col 3 canonical stream arrow | CANONICAL + QUALITY CODES | Cycle time, parts count, spindle RPM, tool, axes, and alarm codes arrive in the same shape regardless of vendor or generation — each carrying a per-tag quality code (Good / Uncertain / Bad / Stale). Per-route store-and-forward survives connectivity gaps without losing source ordering. |
| Col 3 EREMOS V2 | ONE OEE DEFINITION | OEE Segments computed centrally from edge-collected cycle-time + parts-count signals — one consistent definition across every cell, shift, and audit cycle; per-cell aggregation for high-mix shops. |
| Col 3 → Col 4 SCADA / quality arrow | BESIDE, NOT REPLACING | EREMOS V2 publishes OEE rollups, per-customer data slices, and incident records via API; your SCADA / MES / historian / quality systems stay where they are and consume canonical signals instead of vendor-specific ones. |

> CAPTION (below diagram, size.sm italic — standalone emphasized treatment):
> ***The signals trace back to the iron; the numbers trace back to the signals.*** *For a precision floor, Col 2 is the EdgeConnect peer; the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required for this solution. See the full peer-architecture story → `/architecture`.*

**Note (voice):** the audit-defensibility anchor *"The signals trace back to the iron; the numbers trace back to the signals"* is preserved verbatim from Phase 1 v2 §8 and given a standalone emphasized treatment — it is this page's signature retention line (the precision analogue of the CNC inevitability-anchor elevation, R3). The OEE-computation path through EREMOS V2 (Col 3) carries the diagram's visual emphasis, per the v2 §8 visual note — it is the unique value flow for this audience.

---

### 3.9 Section 9 — Trust Cue

Per design-system v3 §16 trust cue content pattern. 2 cues, both linking to `/security` (NEW §15 ecosystem-framing addition vs v2). Placement follows the inherited precedent P-D — **after Architecture, immediately before Cross-lens + Final CTA** (the realized order in both locked sister exemplars + the CNC pattern-setter), superseding the literal §15 prose ("between Outcomes and Architecture"); flagged for the same future design-system v3.x reconciliation noted in the CNC spec MF2.

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Audit-ready by architecture, and nothing depends on the cloud.** Per-tag quality codes propagate end to end so a downstream consumer can tell a real reading from a stale one, and every signal is timestamped at the edge and retained — the data layer is built for the audit before the auditor asks. EdgeConnect runs offline by default: license validates locally, no phone-home. If your network or broker drops, per-route store-and-forward buffers locally and replays in source order on reconnect. Shops on an isolated network install and run the platform the same way; cloud connectivity is opt-in, not required.
>
> CUE 2 (size.base):
> **Per-gateway identity + hash-chained configuration audit.** Each plant runs its own EdgeConnect runtime with a per-gateway UUID established at first start. Every change — a new machine added, a tag-map edit, an OEE-target change — is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain. The configuration history is as defensible as the OEE numbers it produces.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.10 Section 10 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages**: `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub). (NEW §15 ecosystem-framing addition vs v2.)

> Inherited precedent P-C — multiple-pillar related-pillar card choice: precision manufacturing touches two pillars; the related-pillar card leads with **Operational Intelligence** (the OEE-defensibility differentiator that *makes this solution distinct*), with Connectivity & Edge cross-linked inline in §3.3. Rationale: the cross-lens card points to what makes the solution *unique* — for precision, that is the auditable one-definition OEE layer, not raw cross-vendor collection (which is the CNC vertical's differentiator). This is the documented **mirror-image of the CNC pattern-setter under the same P-C rule** (CNC led the card with Connectivity & Edge because protocol-agnostic collection was *its* differentiator). Same rule, opposite lead — confirmed against the migration-plan §3 map.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · OPERATIONAL INTELLIGENCE | The underlying capability — EREMOS V2, OEE Segments, audit-ready analytics | `/capabilities/operational-intelligence` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.11 Section 11 — Final CTA

Per buyer-taxonomy v1 §2.2 Plant-manager / Ops-VP CTA preference. Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic). Voice preserved from Phase 1 v2 §9.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your most demanding cell.
>
> SUBHEAD:
> Pick the cell whose OEE numbers you're least confident in defending. We will scope a proof of value against that cell specifically, including the audit-ready data trail. We run demos on real protocols against your real production cells — no canned data, no polished demo bench, no vague promises.
>
> PRIMARY CTA: Book a scoping call for your production cells
> HREF: `/contact?intent=precision-scoping`
>
> SECONDARY CTA: Download the datasheet
> HREF: `/resources/datasheet`

**Note (voice):** *"Bring us your most demanding cell"* and *"not on a polished demo bench"* preserved from Phase 1 v2 §9 — the inverted demo dynamic (asking for the hardest cell, not the easiest) signals confidence and pre-empts vendor-demo skepticism.

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
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 questions precision shops raise |
| 4-step timeline (composed from `SectionShell` + cards; no new primitive) | §3.7 typical engagement |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure + optional Typical Engagement = 11 sections here).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.11. **~1,920 words total.** The **10-section SolutionPanel core is ~1,720 words — within** the 1,500-1,800 target for `/solutions/<solution>` per `/capabilities` hub §9; the **optional** Typical Engagement section (§3.7, ~200 words) is the documented reason the full page sits over the ceiling at ~1,920 (P-A; migration plan D4 — an intentional, justified inclusion, not drift). The band is guidance calibrated to the 10-section shape. The per-section figures below are **approximate targets** (they sum to ~1,940 ≈ the stated ~1,920; mirrors the CNC §5 reconciling clause so the table and the stated total don't read as two different numbers); architecture-diagram annotation bodies (§3.8) are counted as diagram content, not prose, per the locked exemplar convention. If a trim is wanted, the §3.2 narrative is the lowest-risk candidate.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~80 |
| 3.2 | The Precision-Manufacturing Reality (3 paragraphs) | ~210 |
| 3.3 | How Elpis Solves Precision-Manufacturing OEE (callout + 4 bolded-lead paragraphs) | ~410 |
| 3.4 | What's Included (2 buckets) | ~260 |
| 3.5 | Questions Precision Shops Raise (6 Q&A) | ~370 |
| 3.6 | Outcomes You Can Hold Us To (7 outcomes) | ~130 |
| 3.7 | Typical Engagement (4-step timeline) | ~200 |
| 3.8 | Architecture For This Solution (caption + 4 annotations) | ~80 |
| 3.9 | Trust Cue (2 cues + cross-link) | ~80 |
| 3.10 | Cross-lens | ~50 |
| 3.11 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and §15 SolutionPanel anti-patterns:

| Don't | Why |
|---|---|
| List MT-LINKi as operator-available today | Per side-flag #1 resolution (2026-06-04) + `/platform` v2.1 §6 re-add governance — MT-LINKi has no Studio wizard / modular adapter today. The Phase 1 v2 page listed it as today across §1 hero subhead, §1 trust strip, §3 ¶1, and §4 bullet; this migration corrects all four to a roadmap mention. Future edits must NOT re-add MT-LINKi to the today-list until the engineering milestone ships. |
| List S7 / OPC UA Client as roadmap, or omit them | They are operator-available today (CLAUDE.md §8 + locked connectivity-edge v2). The Phase 1 v2 page did not surface them; this migration adds them to the today-list. |
| Re-skin the CNC page | Precision is a DISTINCT vertical: lead with OEE *defensibility* / quality-accountability (Operational Intelligence), cell-centric high-mix reality, per-tag quality codes, route-based customer-data sharing — NOT CNC's "no per-machine scripting / cross-vendor connectivity" Connectivity-led story. Keep the IS-NOT boundary in §1.1. |
| Claim any industry certification (IATF 16949 / AS9100 / ISO 9001) | The §1 audience-qualifier line ("strict customer quality and audit requirements") is intentionally framework-NEUTRAL. The platform is not formally certified against a named framework; do not imply it is. |
| Use *"rip and replace"* framing or imply Elpis replaces the vendor software / SCADA / MES / quality system | The page IS the "the iron stays, the data layer modernizes" story (§3.2 ¶3 + §3.5 Q3 + §3.8 "beside, not replacing"). Any drift toward replacement framing regresses the core promise. |
| Imply EdgeConnect Linux is current behavior | EdgeConnect is Windows today; Linux is near-term roadmap on the Edge Gateway appliance. The §3.4 deployment note carries the honest framing; don't drop it. |
| Position the Edge Gateway appliance as required | Precision shops typically run software-only on a control-cabinet box. The appliance is an option (§3.4 deployment note), not a requirement. |
| Imply one EdgeConnect runtime serves multiple plants | Per locked `/architecture` v2.1 FAQ Q6 — each plant runs its own runtime with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregation. §3.9 Cue 2 carries the per-gateway-identity guard. |
| Claim specific OEE-percentage gains, downtime-reduction percentages, or dollar savings | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics. Quantified outcomes wait for the `/platform` teaser + Phase 3 customer-story registry. |
| Use absolute outcome claims ("zero scrap", "tool failures eliminated", "never lose a cycle" as a guarantee) | Anti-overclaim discipline (OEM v2 precedent) — outcome verbs use "cut" / "reduce". The v2 §6 "Tool failures caught weeks ahead of replacement" is rehedged to "Tool wear trended ahead of failure". "no lost cycles" / "no missing parts counts" appears as a store-and-forward *mechanism* description (§3.3 ¶4, §3.4, §3.5 Q6), tied to the mechanism, not a headline guarantee. |
| Add competitor names (Kepware, Ignition, MachineMetrics, named SPC/quality-dashboard products, etc.) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory. The §3.3 "How this differs" callout names the CATEGORY (spreadsheet / per-machine-export OEE reporting) without naming specific products beyond the Fanuc/Brother/Mazak/Okuma controller examples that are the floor reality. |
| Add customer logos, customer names, or named deployment stories | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2/E has no customer-logo authorization; named stories wait for Phase 3 sign-off. |
| Use *"single source of truth"* / *"seamless"* / *"intuitive"* / *"easy"* / *"smart factory"* / *"digital transformation"* / *"AI insights"* / *"future-proof"* | Per buyer-taxonomy §2.2 vocabulary discipline — Plant managers / Ops VPs / quality managers read these as consultant-speak or cliché. |
| Lead the hero with products instead of the outcome | Per §15 SolutionPanel anti-pattern — the hero leads with "OEE you can defend, on the cells you actually run", not "EdgeConnect + EREMOS V2". |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per §15 anti-pattern — solution pages need annotated subsets, not generic diagrams. (This is precisely what the migration upgrades vs the Phase 1 v2 static SVG.) |
| Sand off the Phase 1 voice character | Per migration plan D6 — "OEE you can defend, on the cells you actually run", "Stitched OEE numbers don't survive that level of scrutiny", "The signals trace back to the iron; the numbers trace back to the signals", and the "computed the same way next year as this week" consistency line are retained voice choices. |

---

## 7. Sign-off checklist (v3 lock)

- [x] Page copy word count reconciled: **~1,920 total**; 10-section SolutionPanel core ~1,720 (within the 1,500-1,800 band); +~200 optional Typical Engagement section = documented over-ceiling per §15 / P-A (migration plan D4). All three statements (header / §5 / this line) agree.
- [x] All 11 sections present per SolutionPanel layout + the optional Typical Engagement section (design-system v3 §15)
- [x] §3.1 hero leads with outcome ("OEE you can defend, on the cells you actually run"), not products
- [x] §3.1 hero subhead + trust strip drop MT-LINKi from the today-list (roadmap mention only) and ADD OPC UA Client + S7 — the page's two P-G correctness fixes (the v2 carried MT-LINKi in BOTH the subhead and the trust strip)
- [x] §3.3 "How this differs from spreadsheet / per-machine-export OEE reporting" callout present per §9 emerging-pattern governance
- [x] §3.3 names the contributing pillars (Operational Intelligence LEAD + Connectivity & Edge inline) with inline `/capabilities/<pillar>` cross-links (NEW §15 ecosystem-framing addition)
- [x] §3.3 ¶1 leads with the OEE-one-definition differentiator (Operational Intelligence), rendered with visual prominence (per P-C + v2 §3 visual note)
- [x] §1.1 IS-NOT keeps the precision page DISTINCT from the CNC sibling (quality-accountability vs cross-vendor-connectivity) and from edge-connectivity / predictive-maintenance
- [x] §3.4 What's Included follows §15 schema (2 buckets: EdgeConnect + EREMOS V2; `hardwareProducts` omitted — bucket-narrative rationale documented per P-B)
- [x] §3.4 EdgeConnect deployment note honest (Windows today, Linux roadmap on Edge Gateway, appliance optional)
- [x] §3.4 + §3.5 Q2 protocol lists: FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 today; MT-LINKi REST roadmap
- [x] §3.4 carries per-tag quality codes + route-based customer-data sharing (the precision-specific What's-Included items)
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 governance
- [x] §3.5 Q3 (separate quality records / SCADA / quality system) explicitly says "beside, not replacing"
- [x] §3.5 Q1 (trace OEE to part runs) ties OEE to canonical signals + auditability + "the OEE definition stays yours"
- [x] §3.5 Q5 (Tier-1 customer-data sharing) describes route-based architecture (the page's key commercial moment)
- [x] §3.5 Q6 (network drop) describes store-and-forward + three-way diagnostics
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero"; "Tool failures caught weeks ahead" rehedged to "Tool wear trended ahead of failure"
- [x] §3.6 omits OEE-percentage and dollar-cost claims (proof-architecture v1 §3 + §4)
- [x] §3.7 Typical Engagement included with documented rationale (P-A; optional §15 section; deployment-anxiety buyer objection); "Ongoing" protocol line corrected (S7 + OPC UA Client today, MT-LINKi roadmap)
- [x] §3.8 architecture uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image; annotations honor §5.A / P-E; includes the "Acquisition peer not required" Col-2 clarifier; OEE-computation path visually emphasized; "signals trace back to the iron" caption preserved
- [x] §3.9 trust cues cover audit-readiness + offline-first AND per-gateway identity + hash-chained audit; cross-link `/security`; placement after Architecture per P-D
- [x] §3.10 cross-lens cards match the LOCKED §17 preset; related-pillar card leads with Operational Intelligence (the differentiator) per P-C — the documented mirror-image of the CNC pattern-setter
- [x] §3.11 final CTA uses Plant-manager-preferred framing ("Book a scoping call for your production cells" / "Bring us your most demanding cell") and is vertical-localized
- [x] EdgeConnect + EREMOS V2 positioning matches the LOCKED `/capabilities/connectivity-edge` + `/capabilities/operational-intelligence` specs
- [x] No vocabulary that backfires per buyer-taxonomy §2.2
- [x] No industry-certification claims (IATF / AS9100 / ISO 9001) — §1 audience-qualifier line is framework-neutral
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names (Fanuc/Brother/Mazak/Okuma are floor reality, not competitive comparison)
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] Phase 1 v2 voice character preserved (D6)
- [x] Inherited pattern-setter precedents (P-A..P-G) applied + documented; P-C divergence (OI-led card) explicitly justified as the mirror-image of CNC under the same rule
- [x] ChatGPT review pass applied (batch of 4)
- [x] Pre-lock validation workflow PASSED (batch of 4; cross-spec drift + §15/§9 coverage + discipline-lock guard)

---

## 8. Out of scope for v1 (v3 content)

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers the full matrix with semantic modes, per-tag quality-code propagation detail, MT-LINKi REST detail.
- **Full EREMOS V2 capability detail.** `/capabilities/operational-intelligence` (LOCKED) covers OEE / alarms / multi-tenant as a Pillar 5 capability; this page cross-links rather than duplicating.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2.1) covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1) covers the cross-pillar Industrial Intelligence Stack; cross-link for the full stack story.
- **The multi-vendor CNC-shop entry vertical.** `/solutions/cnc-machining` (LOCKED v3 pattern-setter) covers the cross-vendor connectivity / no-per-machine-scripting cut; this page is the quality-accountability / OEE-defensibility cut.
- **Protocol-agnostic / OT-consolidation, reliability, multi-site, and OEM framings.** The sibling solution pages (their own v3 migrations in this Phase E wave) cover those outcomes.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links from §3.9.
- **Industry-certification claims (IATF 16949 / AS9100 / ISO 9001).** The platform is not formally certified against a named framework; the §1 audience-qualifier line is intentionally framework-neutral.
- **Inspection / metrology / SPC system replacement.** The platform handles production-signal OEE inputs and can ingest inspection data via Modbus TCP / MQTT where published; it does not replace the shop's quality/metrology system (§3.5 Q3).
- **Industries-specific framings.** Phase 3 `/industries/<industry>` (or Phase 2.5 single-industry exception per amendment v3 §2).
- **Pricing / commercial engagement detail.** `/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail.
- **Quantified OEE-gain / scrap-reduction / cost-savings percentages.** Wait for Phase 3 customer-story registry + the `/platform` commercial teaser.
- **Real customer case studies / named deployment stories.** Phase 3 customer-story sign-off process.

---

*`/solutions/precision-manufacturing` Page Spec **v1 LOCKED 2026-06-04** (page content v3 — SolutionPanel migration of the Phase 1 v1→v2 page copy). Part of the Phase E batch of 4 (precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations) following the LOCKED CNC pattern-setter; LOCKED after the batch ChatGPT review + pre-lock validation workflow (run wf_e86046ac-cdb — LOCK-clean: 0 HIGH / 0 MED); ships with all 5 as one wave (design-system v3 §15 Q3 bulk-migration lock). Migrates `solution-precision-manufacturing-v2.md` into the §9 canonical per-page-spec format + SolutionPanel §15 layout, adding the four §15 ecosystem-framing additions (pillar cross-refs, trust cue, ArchitecturePanel.interactive, cross-lens), §1.4 metadata, inline FAQ with FAQPage schema, and the "How this differs from spreadsheet / per-machine-export OEE reporting" callout. Includes the optional Typical Engagement section (P-A; deployment-anxiety buyer objection). Applies the inherited precedents P-A..P-G: Typical Engagement included; whatsIncluded 2-bucket narrative (hardwareProducts omitted); cross-lens related-pillar card leads with the DIFFERENTIATOR — here Operational Intelligence, the documented mirror-image of CNC's Connectivity-led card under the same P-C rule; trust cue after Architecture; annotation eyebrow doubles as ≤4-word title; hero subhead = protocol subset; MT-LINKi → roadmap (corrected in the v2 hero subhead AND trust strip), S7 + OPC UA Client → today (added). Distinct from the CNC sibling: leads with OEE defensibility / quality-accountability and the cell-centric high-mix reality, not cross-vendor connectivity. Anti-overclaim "cut"-verb hedging (v2 "tool failures caught" rehedged to "tool wear trended ahead of failure"); framework-neutral audience qualifier preserved (no IATF/AS9100/ISO claim); EdgeConnect Windows-today/Linux-roadmap; per-gateway identity; beside-not-replacing SCADA/MES/quality systems. Phase 1 voice character preserved (D6). Cites: page-capabilities-hub-spec-v1 §9, design-system-v3 §15/§16/§17/§5.A, buyer-taxonomy-v1 §2.2/§2.3, proof-architecture-v1 §3/§4/§8, page-capabilities-operational-intelligence-spec-v1 v1 (lead pillar), page-capabilities-connectivity-edge-spec-v1 v2.1, page-architecture-spec-v1 v2.1, page-solutions-cnc-machining-spec-v1 v1 (LOCKED pattern-setter + distinct sibling vertical), page-solutions-edge-connectivity-spec-v1 v2.1, page-solutions-predictive-maintenance-spec-v1 v2, solution-precision-manufacturing-v2 (migrated source), solution-oem-machine-monitoring-v2 (anti-overclaim precedent), shared-knowledge/contracts/cnc-vocabulary.md, 2026-06-04-phase-e-solution-migration-plan.md.*
