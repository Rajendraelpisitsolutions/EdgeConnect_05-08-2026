<!--
File:        docs/marketing/page-solutions-cnc-machining-spec-v1.md
Purpose:     Page spec for /solutions/cnc-machining — solution depth-
             example for the multi-vendor CNC shop (the most common
             entry vertical). PATTERN-SETTER for the Phase E bulk
             migration of the 5 existing v2 solution pages onto the
             SolutionPanel §15 layout. Migrates solution-cnc-machining-
             v2.md (Phase 1 page copy) into the §9 canonical per-page-
             spec format.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim copy), user + ChatGPT
             (reviewers), Phase E batch-migration authors (this spec is
             the pattern the other 4 inherit).
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   solution-cnc-machining-v2.md (Phase 1 page copy being
                migrated — voice + structure precedent; SUPERSEDED by
                this spec at v3 lock; retained as voice reference)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED —
                sister SolutionPanel exemplar; structural model + the
                "How this differs from per-vendor monitoring tools"
                callout precedent + source-of-truth alignment discipline)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED
                — sister SolutionPanel exemplar; whatsIncluded bucket-
                narrative pattern + cross-spec drift discipline)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page-type FAQ governance — /solutions/<solution> =
                YES; metadata governance; "How this differs from…"
                emerging-pattern governance; Typical-Engagement optional-
                section guidance via §15 baseline)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED
                — source-of-truth for EdgeConnect + Edge Gateway
                positioning + the today-protocol list)
             page-capabilities-operational-intelligence-spec-v1.md v1
                (LOCKED — source-of-truth for EREMOS V2 / OEE / alarms
                pillar cross-ref)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-link
                target for "See full architecture"; multi-plant-
                EdgeConnect FAQ Q6; integration patterns §3.6)
             page-solutions-hub-spec-v1.md v2 (LOCKED — /solutions hub
                directory this depth-example sits under)
             buyer-taxonomy-v1.md §2.2 (Plant manager / Ops VP — primary
                buyer) + §2.3 (OT Architect — secondary)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics,
                no customer names, no competitor names)
             design-system-v3.md §15 (SolutionPanel — LOCKED; structural
                baseline anchored to THIS page's v2 template) + §16
                (trust cue content pattern) + §17 (cross-lens LOCKED
                preset for /solutions/<solution>: line 529) + §5.A
                (ArchitecturePanel.interactive solution-annotated variant)
             shared-knowledge/contracts/cnc-vocabulary.md (canonical CNC
                tag set — spindle_rpm, feed_rate, parts_count, cycle_time,
                axes, tool, alarm codes)
             solution-oem-machine-monitoring-v2.md (anti-overclaim "cut"-
                verb hedging precedent)
             2026-06-04-phase-e-solution-migration-plan.md (the bulk-
                migration plan-trail this spec executes; D1-D6 decisions)
Version:     v1 — LOCKED 2026-06-04 (page content v3). ChatGPT review
                  APPLIED (verdict "Approve with changes"; 5 refinements
                  R1-R5). Pre-lock validation workflow PASSED (run
                  wf_9cb7e5e6-f68): 0 HIGH / 6 MED, verdict
                  LOCK-AFTER-FIXES; all 6 must-fixes + 3 optional-polish
                  items applied (see workflow note below). Page content =
                  v3 (SolutionPanel migration of the Phase 1 v1→v2 page
                  copy). Spec doc = v1 in the §9 template sense.
                  PATTERN-SETTER — locked precedents for the other 4
                  migrations enumerated in the workflow note + §3 / §6 /
                  §3.8 / §3.9 / §3.10.
Date:        2026-06-04
Status:      LOCKED (pattern-setter).

PATTERN-SETTER ROLE. design-system v3 §15 explicitly anchors the
SolutionPanel structural baseline to solution-cnc-machining-v2.md. This
spec is therefore the canonical migration pattern; the other 4 Phase E
migrations (precision-manufacturing, brownfield-modernization, oem-
machine-monitoring, multi-site-operations) inherit the structure,
the §15 ecosystem-framing additions, and the source-of-truth discipline
locked here. See the migration plan-trail (D1-D6) for the cross-cutting
decisions.

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
    governance; v2 §5 "questions you can finally answer" is reworked into
    scoping/procurement Q&A in outcome context).
  + "How this differs from per-vendor CNC monitoring tools" callout in
    §3.3 (§9 emerging-pattern governance).

Source-of-truth alignment baked into this v1 draft (migration plan D5):
  - MT-LINKi → ROADMAP mention, removed from the today-list. The Phase 1
    v2 page listed MT-LINKi as operator-available today across §1/§3/§4/
    §7; this migration corrects it per side-flag #1 resolution (2026-06-
    04) + /platform v2.1 §6 re-add governance. (These 5 solution pages
    were among the "53 untouched" files; the migration is where the stale
    claim is fixed.)
  - S7 + OPC UA Client are NOW operator-available (CLAUDE.md §8 + locked
    connectivity-edge v2). The Phase 1 v2 page §7 listed them as roadmap;
    this migration corrects them to today. This is a real correctness fix
    the migration surfaces.
  - CNC today-protocol list (this page surfaces the CNC-relevant subset):
    FOCAS2, MTConnect, Brother HTTP, Modbus TCP — plus OPC UA Client + S7
    available; FANUC MT-LINKi REST on the roadmap. Full protocol matrix
    lives on Phase E /edgeconnect; this page stays at solution-level
    vocabulary.
  - EdgeConnect = Windows service today; Linux near-term roadmap (on Edge
    Gateway). Edge Gateway is an OPTIONAL deployment note on this page
    (CNC shops typically run software-only on a small control-cabinet
    box) — NOT positioned as required.
  - Per-gateway identity / anti-multi-plant-EdgeConnect; "beside not
    replacing" SCADA/MES; offline-first.
  - Anti-overclaim: "cut" / "reduce" verbs only (OEM v2 precedent).

Voice preservation (migration plan D6): the Phase 1 v2 page deliberately
kept "each dashboard speaks its own dialect" and "reports your team will
actually read" for voice character. This migration preserves both, plus
the inevitability anchor "The next CNC vendor added to your floor should
not require a new monitoring stack" (v2 §3).

Structural note — Typical Engagement section INCLUDED (migration plan
D4). §15's structural baseline marks "Typical engagement (4-step rollout
timeline)" as OPTIONAL ("earned by verticals where deployment anxiety is
real"). The two Phase 2 SolutionPanel exemplars (edge-connectivity,
predictive-maintenance) omitted it. CNC RESTORES it because deployment
anxiety is an explicit Plant-manager objection (buyer-taxonomy §2.2:
"How long does this take?" → typical-engagement timeline) and the Phase
1 v2 §7 timeline is strong, buyer-validated content. Including the
optional baseline section is WITHIN the locked SolutionPanel contract
(not §9 template evolution). This makes CNC an 11-section page; the other
4 migrations each decide Typical-Engagement inclusion per D4.

Word-count target: 1,500-1,800 words page copy per /capabilities hub §9
page-type guidance for /solutions/<solution>. Reconciled ~1,950 words
total; 10-section SolutionPanel core ~1,750 (within band); +~200 optional
Typical Engagement section = documented over-ceiling (migration plan D4;
see §5).

Open question (migration plan §5 Q-A) — RESOLVED at ChatGPT review (R4):
the §17 cross-lens preset card is singular /capabilities/<related-pillar>,
but CNC touches two pillars. Decision: lead the cross-lens card with
Connectivity & Edge (the differentiator that MAKES the solution possible);
Operational Intelligence cross-linked inline in §3.3. Becomes the
precedent for the other 4 migrations: cross-lens card leads with the
differentiating capability, not the outcome capability.

ChatGPT review (2026-06-04) — verdict "Approve with changes." 5
refinements applied before pre-lock workflow:
  - R1: added a concrete canonical-vocabulary worked example in §3.3 ¶2
    (Fanuc / Brother / Mazak cycle signal → canonical cycle_time → OEE),
    mirroring the locked edge-connectivity v2 M2 pattern.
  - R2: added explicit "the OEE definition stays yours" statement in §3.5
    FAQ Q3 (segment classification / shift schedule / targets configured
    to the shop's own OEE definition).
  - R3: the "next CNC vendor" inevitability anchor (§3.3) elevated from
    inline bold to a standalone emphasized callout treatment (signature
    retention line).
  - R4: cross-lens related-pillar card decision RESOLVED (above) — keep
    Connectivity & Edge per ChatGPT endorsement.
  - R5: §3.3 ¶3 reweighted to elevate Operational Intelligence presence
    (OEE / downtime visibility / shift reports as the operational
    outcome), keeping Connectivity & Edge as architectural lead.
ChatGPT verdict highlights: "best FAQ after Predictive Maintenance";
"How this differs" callout "earns its space"; Typical Engagement
"correctly restored"; the MT-LINKi + S7/OPC UA Client corrections
"alone validate the migration effort."

Pre-lock validation workflow (run wf_9cb7e5e6-f68, 2026-06-04) — 3
validators (cross-spec drift / SolutionPanel §15 + §9 coverage /
discipline-lock guard) + 1 synthesizer. Verdict: LOCK-AFTER-FIXES — 0
HIGH, 0 BLOCKING, 6 MED (all quick edits). As the pattern-setter, ALL 6
must-fixes + ALL 3 optional-polish items were applied before lock:
  - MF1: word count reconciled across header / §5 / §7 — ~1,950 total
    (10-section core ~1,750 in-band; +~200 optional Typical Engagement =
    documented over-ceiling). Earlier draft carried an inconsistent
    1,720/1,810 split; fixed.
  - MF2: Trust Cue placement — documented that the spec follows the
    LOCKED sister-exemplar order (Architecture → Trust Cue → Cross-lens →
    CTA), which supersedes the literal §15 prose ("between Outcomes and
    Architecture"). Now an explicit inherited pattern, not a silent
    deviation (§3.9). Flagged for a future design-system v3.x amendment.
  - MF3: §3.6 outcome "Tool failures caught before they damage parts"
    (unhedged absolute) → "Tool wear trended ahead of failure" (mechanism-
    tethered), per the spec's own §6 anti-overclaim rule.
  - MF4: §6 store-and-forward guard location list expanded to (§3.3,
    §3.4, §3.5 Q4) — §3.3 ¶4 is the most prominent live instance and was
    omitted.
  - MF5: §6 banned-vocabulary guard row gained "AI insights" + "future-
    proof" for parity with §1.2 + §7.
  - MF6: §1.2 broken citation "buyer-taxonomy §5.3" → "§5 step 3" (also
    fixed in the migration plan-trail).
  - OP1: §3.8 annotation-model intro corrected — eyebrow doubles as the
    ≤4-word title (matches the rendered table + locked exemplar).
  - OP2: protocol-pair ordering normalized to "OPC UA Client and Siemens
    S7" corpus-wide (§3.3 ¶1, §3.5 Q2).
  - OP3: documented the locked rule — vertical solution-page hero subhead
    carries the relevant protocol SUBSET (full list in trust strip),
    intentionally diverging from edge-connectivity's all-six subhead.

LOCKED PATTERN-SETTER PRECEDENTS the other 4 migrations inherit:
  P-A (D4) Typical Engagement is an optional §15 section; include it
    where deployment anxiety is a real buyer objection, documenting the
    rationale + the word-budget consequence (10-section core stays
    in-band; the optional section is the documented over-ceiling reason).
  P-B (D2/D5) whatsIncluded bucket-narrative: buckets follow product-
    narrative groupings, not literal schema field names; document the
    bucket choice + omissions.
  P-C (R4) when a solution touches multiple pillars, the §17 cross-lens
    related-pillar card leads with the DIFFERENTIATING capability, not
    the outcome capability (other pillars cross-linked inline in §3.3).
  P-D (MF2) trust-cue placement follows the realized exemplar order
    (after Architecture), not the literal §15 prose.
  P-E (OP1) architecture-annotation eyebrow doubles as the ≤4-word title.
  P-F (OP3) vertical hero subhead = relevant protocol subset; full list
    in trust strip / §3.4.
  P-G (D5) MT-LINKi → roadmap only; S7 + OPC UA Client → today (the two
    correctness fixes the migration surfaces vs the Phase 1 v2 pages).

Carry-forward side-flag (publish-orchestration, not a spec blocker): when
this page ships live, /solutions hub v2 Card (CNC) "Coming soon" status
pill + pre-live link swap per the /solutions hub pre-live link policy.
-->

# `/solutions/cnc-machining` — Page Spec v1 (page content v3)

**Solution depth-example for the multi-vendor CNC machine shop — the most common entry vertical for the platform. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want the outcome view of running one operational picture across a mixed-vendor CNC floor — Fanuc, Brother, Mazak, Okuma, Heidenhain — without ripping out the controllers they already run.**

This is the page where CNC shop owners, production managers, and plant managers land when they want the **outcome view** of a mixed-vendor CNC floor: one OEE truth across every controller, canonical CNC vocabulary across vendors, no per-machine custom scripting. It is **not** the capability page (`/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability; `/capabilities/operational-intelligence` covers EREMOS V2 / OEE). It is **not** the architecture walkthrough (`/architecture`). It is the **CNC-shop solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the multi-vendor CNC machine shop. Reader leaves with *"I now understand what one operational view across my mixed-vendor CNC floor actually gets me, which controllers it collects from today, how OEE is computed and whether I can defend it, how long a rollout takes, and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/connectivity-edge` covers EdgeConnect as a Pillar 1 capability; `/capabilities/operational-intelligence` covers EREMOS V2 / OEE / alarms; both LOCKED — this page cross-links rather than duplicating)
- A product detail page (Phase E `/edgeconnect` covers the full protocol matrix, semantic modes, FOCAS2 connection-pool sizing, etc.)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2.1)
- The protocol-agnostic / OT-consolidation depth-example (`/solutions/edge-connectivity` v2.1 covers the cross-vendor edge story across all controller classes including S7 / Modbus / OPC UA; this page is the **CNC-shop-specific** cut)
- A pricing or commercial page (`/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Plant manager / Ops VP (§2.2)
- Lands here from `/solutions` hub, from the homepage hero, or from a Google search for *"mixed-vendor CNC OEE"* / *"Fanuc Brother Mazak monitoring"* / *"FOCAS2 OEE dashboard"* / *"CNC shop OEE without spreadsheets"*
- Wants: one defensible OEE across mixed-vendor cells, less manual report-stitching, downtime caught in the moment (not in retrospect), shift handover that's a record not a phone call, a rollout that won't disrupt production
- CTA preference: *"Book a scoping call for your CNC floor"* > *"Bring us your oldest CNC"* > datasheet download
- Vocabulary that lands: *OEE you can defend*, *cycle-time variance*, *shift handover*, *mixed-vendor cells*, *audit-ready*, *replace spreadsheet operations*, and real protocol/model names (FOCAS2, Brother HTTP, Fanuc 0i/16i/18i) as trust signals
- Vocabulary that backfires: *"digital transformation"*, *"smart factory"*, *"AI insights"*, *"single source of truth"* (cliché), *"seamless"*, *"easy"*

**Secondary buyer:** OT Architect / SCADA engineer (§2.3) — the "industrial IT lead evaluating the platform for the shop's CNC fleet"
- Lands here when the Plant manager forwards the page for a technical sanity check
- Wants: real protocol coverage with specific Fanuc model support, canonical vocabulary, SCADA coexistence honesty, store-and-forward mechanics
- Served via cross-lens to `/capabilities/connectivity-edge` + `/architecture` (per buyer-taxonomy §5 step 3 — secondary buyers via cross-lens, not primary page content)

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4 + `/solutions/edge-connectivity` v2.1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *CNC Machining — One OEE Across Every Vendor · Elpis* |
| **Meta description** (140-160 chars) | *One operational view across mixed-vendor CNC floors. Native FOCAS2, MTConnect, Brother HTTP, Modbus TCP. Canonical CNC vocabulary, no per-machine scripting.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/cnc-machining` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities/connectivity-edge` + `/capabilities/operational-intelligence` + `/architecture` + `/security` use `relatedLink`. Product cards for EdgeConnect + EREMOS V2 (when Phase E product pages ship) via `SoftwareApplication` schema. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). **11 sections** — the 10-section SolutionPanel shape (same as `/solutions/edge-connectivity` v2.1) plus the **optional Typical Engagement** section (§15 structural-baseline CNC template §7), included per migration plan D4.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~80 |
| **2** | The CNC Shop Reality (customer pain) — narrative empathy, 3 paragraphs | `light` | Narrative copy + optional margin pull-quote | ~200 |
| **3** | How Elpis Solves CNC Integration — 4 bolded-lead paragraphs + pillar cross-refs + "How this differs from per-vendor CNC monitoring tools" callout | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block | ~380 |
| **4** | What's Included — From EdgeConnect + From EREMOS V2 | `light` | Bulleted feature lists with bolded leads | ~250 |
| **5** | Common Questions (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions + answers + `FAQPage` schema | ~360 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column desktop | `dark` | Bolded outcome leads + supporting clauses | ~130 |
| **7** | How CNC Shops Typically Roll This Out — 4-step timeline *(optional §15 section, included)* | `light-tinted` | 4-step horizontal timeline | ~200 |
| **8** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" | ~80 |
| **9** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | Trust cue content pattern (design-system v3 §16) | ~80 |
| **10** | Cross-lens navigation — LOCKED preset per §17 | `light-tinted` | Cross-lens content pattern (3 cards) | ~50 |
| **11** | Final CTA — vertical-localized "Bring us your CNC floor" | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · CNC MACHINING
>
> HEADLINE (size.3xl semibold):
> One operational view across every Fanuc, Brother, and Mazak on your floor.
>
> SUBHEAD (size.lg, max-width 60ch):
> Native FOCAS2, MTConnect, and Brother HTTP — plus Modbus TCP for the PLCs in front of older CNCs. Canonical CNC vocabulary across vendors. No per-machine custom scripting. From the spindle to the dashboard, on one foundation.
>
> PRIMARY CTA (`Button.primary.lg`):
> Book a scoping call for your CNC floor
> HREF: `/contact?intent=cnc-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Download the datasheet
> HREF: `/resources/datasheet`

> TRUST STRIP (under hero, size.sm):
> *Live integrations: FOCAS2 · MTConnect · Brother HTTP · Modbus TCP — and OPC UA Client and Siemens S7 for the rest of the floor. FANUC MT-LINKi REST on the roadmap.*

**Anti-patterns:** No *"seamless"* / *"intuitive"* / *"easy"* / *"single source of truth"* framing (buyer-taxonomy §2.2 vocabulary discipline). No outcome metric in the headline. Hero leads with the **outcome** ("one operational view across every Fanuc, Brother, and Mazak"), not the products (EdgeConnect + EREMOS V2) — per §15 anti-pattern. Headline preserved verbatim from Phase 1 v2 §1 (voice).

> **Pattern-setter note (pre-lock workflow, OP3) — solution-page hero subhead protocol enumeration.** The hero subhead names the **vertical-relevant protocol subset** (the 4 CNC-central protocols), with the full six-protocol list carried in the trust strip directly beneath. This is an intentional divergence from `/solutions/edge-connectivity` v2.1, whose subhead enumerates all six — because edge-connectivity IS the all-protocol OT-consolidation story, whereas a vertical solution page leads with the protocols that match the buyer's floor and relegates the full list to the trust strip. **Locked rule the other 4 migrations inherit:** vertical solution-page hero subhead = relevant subset; full protocol list lives in the trust strip or §3.4.

---

### 3.2 Section 2 — The CNC Shop Reality

> EYEBROW: THE CNC SHOP REALITY
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Modern CNC shops don't have a CNC problem. They have an *integration* problem. A single shop floor typically runs three to seven different CNC vendors — Fanuc lathes from one era, Brother machining centers from another, an Okuma multi-axis, a Mazak Integrex, maybe a Heidenhain or two. Each vendor ships its own diagnostic software. Each diagnostic tool produces its own dashboard. Each dashboard speaks its own dialect.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> The result is predictable: production managers stitch OEE numbers together from spreadsheets. Maintenance learns about an alarm from the operator who happened to walk by. Tool changes get scheduled from clipboard memory. Cycle-time variance shows up in retrospect, after the shift is already lost. The data is on the floor — it just doesn't reach the people who need it.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> Replacing the controllers isn't the answer. They cost too much, they're already validated for the parts they're running, and operators know them by feel. The data layer is what needs to modernize, not the iron. The shops that get there put one protocol-agnostic runtime in front of every controller, normalize every signal to one vocabulary, and let the existing systems keep doing their jobs.

> OPTIONAL MARGIN PULL-QUOTE (desktop, size.lg italic):
> *"The data is on the floor — it just doesn't reach the people who need it."*

**Note (voice):** *"each dashboard speaks its own dialect"* preserved verbatim from Phase 1 v2 §2 per migration plan D6. No bullet lists in this section — the challenge is a narrative (subdued empathy treatment, not pitch).

---

### 3.3 Section 3 — How Elpis Solves CNC Integration

> EYEBROW: HOW ELPIS SOLVES CNC INTEGRATION

> CALLOUT — HOW THIS DIFFERS FROM PER-VENDOR CNC MONITORING TOOLS (size.base, single paragraph; bordered card or left-rule callout, sits before the bolded-lead paragraphs):
>
> > **How this differs from per-vendor CNC monitoring tools.** Every CNC vendor ships a monitoring tool — and each one works, for the vendor it ships with. They each speak vendor-specific vocabulary, render their own dashboard, and report on their own schedule. Run a mixed-vendor floor and you run all of them at once, reconciling by hand. Elpis puts one protocol-agnostic runtime in front of every controller, normalizes every signal to **canonical CNC vocabulary**, and feeds one operational view for the whole floor. **Same Fanuc, same Brother, same Mazak. Same production team. One OEE definition across them all.** The per-vendor tools stay where they are; Elpis adds the cross-vendor layer.

#### Bolded-lead paragraphs (4 paragraphs):

> **EdgeConnect speaks every controller you own.** One service running on a small box in your control cabinet polls each CNC over its native protocol — FOCAS2 for Fanuc, MTConnect for the open-standard machines, Brother HTTP for Brother's built-in web interface, and Modbus TCP for older CNCs fronted by a PLC. OPC UA Client and Siemens S7 cover the rest of the floor; FANUC MT-LINKi REST integration is on the roadmap. No per-machine custom scripting; no per-vendor middleware. This is the **Connectivity & Edge** capability applied to a CNC floor — see the underlying capability story → `/capabilities/connectivity-edge`.

> **Every tag normalizes to the same vocabulary.** A spindle-RPM reading from a Fanuc 0i, a Brother S700Xd1, and an Okuma OSP all become the same canonical `spindle_rpm` in the pipeline. A Fanuc cycle-complete signal, a Brother cycle signal, and a Mazak cycle signal collapse the same way into a canonical `cycle_time` — the exact signal EREMOS V2 turns into OEE. The same applies to feed rate, parts count, tool number, axis positions, and alarm codes. One semantics, many vendors — so the same dashboard works across your whole floor regardless of which CNC produced the signal.
>
> **The next CNC vendor added to your floor should not require a new monitoring stack.**
>
> *(Render this line as a standalone, emphasized callout — a divider/accent treatment, not just inline bold. It is the page's signature inevitability anchor and the strongest single line of retention copy on the page.)*

> **And that canonical stream is what finally gives you one operational picture.** This is the outcome the floor actually runs on: EdgeConnect publishes the normalized signals to MQTT (Mosquitto, HiveMQ, AWS IoT Core, or your existing broker), and EREMOS V2 turns them into the things a production manager lives in — OEE Segments computed from cycle-time and parts-count signals, every alarm tracked as a persistent record with incident workflows, downtime visible as it happens instead of in retrospect, and shift reports your team will actually read. This is the **Operational Intelligence** capability → `/capabilities/operational-intelligence`. Because every machine's signals arrive in the same canonical shape, the OEE math is the same across every machine, every shift, and every vendor — so the number holds up whether you're comparing two cells or defending it in a customer audit.

> **Nothing depends on the cloud.** EdgeConnect runs offline. If your network or broker goes down, the platform buffers locally with per-route store-and-forward and replays in source order on reconnect — no lost cycles, no missing parts counts, no apologetic emails to the operations team. Three-way diagnostics (source / pipeline / sink) tell the OT team exactly which leg broke before the production team feels the symptom.

**Note (voice + pillars):** the inevitability anchor *"The next CNC vendor added to your floor should not require a new monitoring stack"* preserved verbatim from Phase 1 v2 §3 and given a standalone emphasized callout treatment per R3 (ChatGPT review) — it is the page's signature retention line. Pillar cross-refs (Connectivity & Edge + Operational Intelligence) are the NEW §15 ecosystem-framing addition vs v2. **Pillar-balance note (R5):** the 4-paragraph arc runs Connectivity → Canonical Vocabulary → **Operational Intelligence (OEE & operations)** → Offline resilience. Paragraph 3 is deliberately weighted toward the operational outcome (OEE, downtime visibility, shift reports) because that is what the Plant-manager buyer ultimately runs on — Connectivity & Edge remains the architectural lead and differentiator, but Operational Intelligence carries equal narrative weight in the solution story.

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets — `edgeConnect` (the edge runtime; the CNC floor-side story) + `eremosV2` (the intelligence layer). The standalone `hardwareProducts` bucket is **omitted** — CNC shops typically run software-only on a small control-cabinet box; the Edge Gateway appliance is a deployment option mentioned inline, not a lead bucket on this page. (Bucket-narrative governance per migration plan D2/D5 + edge-connectivity v2.1 §3.4 preamble: solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names. `/solutions/edge-connectivity` folded EdgeConnect under a relabeled `hardwareProducts` bucket because the appliance was its story; CNC keeps the discrete `edgeConnect` bucket because the software runtime on customer hardware is the CNC reality.)

#### From EdgeConnect (edge runtime, Windows service today)

> - **FOCAS2 collector** — Fanuc CNCs (0i, 16i, 18i, 21i, 30i, 31i, 32i). Axes, spindle, alarms, tool, production counters, programs.
> - **MTConnect collector** — the industry-standard CNC streaming protocol; covers most modern multi-vendor CNCs.
> - **Brother HTTP collector** — Brother S700Xd1 and similar models via the built-in web-monitoring interface.
> - **Modbus TCP collector** — for older CNCs fronted by a PLC gateway.
> - **Also today:** OPC UA Client and Siemens S7 for the rest of the floor. **On the roadmap:** FANUC MT-LINKi REST. For the full protocol matrix with semantic modes, see Phase E `/edgeconnect` (coming soon).
> - **Canonical CNC vocabulary** — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions (`axes/x/absolute`, etc.), tool number and offsets, alarm codes. The same names appear regardless of which CNC produced them.
> - **Per-route store-and-forward buffering** — never lose a cycle or a parts-count update because the broker was down.
> - **Three-way diagnostics** — source / pipeline / sink. Operators always know where the data flow broke.
> - **Connectivity Studio** — web admin to add machines, configure tag maps, and run Test Connection probes before anything goes live.

> > *Deployment note — EdgeConnect ships as a Windows service today; CNC shops typically run it on a small box in the control cabinet. A Linux runtime is near-term roadmap, arriving on the Edge Gateway appliance for shops that prefer a turnkey DIN-rail box. The appliance is an option, not a requirement.*

#### From EREMOS V2 (intelligence layer, consuming the canonical stream)

> - **OEE Segments** — RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP. Computed from edge-collected signals; auditable.
> - **Persistent alarm tracking** — every CNC alarm becomes a tracked record with open/close state and incident grouping. No more "the alarm history was on machine 12's HMI."
> - **Tool-life ingestion** — a dedicated path for tool-wear telemetry, so maintenance gets ahead of failures.
> - **Shift reports** — PDF and Excel, built from edge-collected signals, not operator memory.
> - **Multi-tenant** — one platform, many sites or business units, no data leakage.
> - **Dashboards split by device class** — CNC, PLC, meter. Mixed fleets render cleanly.

---

### 3.5 Section 5 — Common Questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to Plant-manager / Ops-VP scoping concerns (the Phase 1 v2 §5 "questions you can finally answer" operator-facing list is reworked here into procurement-stage Q&A in outcome context).

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What production managers ask before scoping a CNC floor.

#### Q1. Does this replace the software our CNCs already came with?

> No. The vendor diagnostic tools stay where they are — Elpis sits beside them and adds the cross-vendor layer. EdgeConnect collects from each controller over its native protocol and normalizes every signal to one canonical CNC vocabulary, so your downstream view is consistent regardless of which machine produced the data. It also doesn't replace your SCADA, MES, or historian; those keep their jobs and consume canonical signals instead of vendor-specific ones.

#### Q2. Which CNC controllers do you collect from today?

> Today: Fanuc over FOCAS2 (0i, 16i, 18i, 21i, 30i, 31i, 32i), the open-standard machines over MTConnect, Brother over Brother HTTP, and older CNCs fronted by a PLC over Modbus TCP. OPC UA Client and Siemens S7 cover the rest of the floor. FANUC MT-LINKi REST integration is on the roadmap. For the full protocol matrix with semantic modes and model coverage, see Phase E `/edgeconnect` (coming soon); the exact controller mix is confirmed during the scoping call.

#### Q3. Can we defend the OEE numbers in a customer audit?

> Yes — that's the point of computing OEE on canonical signals. EREMOS V2 derives OEE Segments (RUNNING, PLANNED_STOP, UNPLANNED_STOP, IDLE, SETUP) from edge-collected cycle-time and parts-count signals, each timestamped at the edge and retained. The math is the same across every machine and every shift, so the number a customer audit asks about traces back to the actual signals, not a hand-reconciled spreadsheet. **And the OEE definition stays yours** — segment classification, shift schedule, and targets are configured to how your shop already defines OEE; the platform computes against your definition, not one of ours.

#### Q4. What happens when the network or the broker drops?

> Per-route store-and-forward. Every signal queues at the source with its quality code preserved, and replays in source order when connectivity returns — no lost cycles, no missing parts counts. Three-way diagnostics (source / pipeline / sink) surface immediately during the outage, so the OT team sees exactly which leg was affected. Shops on an isolated network operate the same way; cloud connectivity is opt-in, not required.

#### Q5. How long until something is actually running?

> A first machine is usually streaming real cycle-time and alarm data within the first few days — one CNC, one shift, one OEE definition — and a cell follows over the next few weeks. The full rollout cadence is in "How CNC shops typically roll this out" below. We run the proof of value on your real protocols against your real signals, not on canned data.

#### Q6. What about the older Fanuc machines and the ones behind a PLC?

> The older Fanuc controllers (16i / 18i and similar) are collected over FOCAS2 the same as the newer ones — the protocol coverage doesn't quietly drop your oldest iron. CNCs that don't expose a native data interface are collected over Modbus TCP through the PLC in front of them. Bring the controller list to the scoping call and we'll confirm the collection path per machine.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **One OEE truth across mixed-vendor CNCs** — no more reconciling numbers from three vendor dashboards by hand
> - **Tool wear trended ahead of failure** — tool-life telemetry flags wear before a tool fails mid-cycle, instead of discovering it after a scrapped part
> - **Cycle-time variance trended over shifts, days, and weeks** — root-cause analysis becomes possible, not just retrospective
> - **Alarm patterns visible across the floor** — recurring faults surface as patterns, not isolated incidents on one machine's HMI
> - **New CNC vendors added without a new dashboard** — the next Brother, Mazak, or Heidenhain plugs into the same platform
> - **Cut the manual report-stitching** — shift handover becomes a record built from edge-collected signals, not a phone call and a spreadsheet
> - **Audit-ready production history** — every reading timestamped at the edge and retained; OEE traces back to real signals

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific OEE-percentage or dollar-cost-savings claims. The `/platform` commercial teaser and Phase 3 customer-story registry handle quantified outcomes once the customer-evidence registry is in place. Outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero" (OEM v2 anti-overclaim precedent).

---

### 3.7 Section 7 — How CNC Shops Typically Roll This Out

*Optional §15 section (structural-baseline CNC template §7), included per migration plan D4 — deployment-anxiety is an explicit Plant-manager objection (buyer-taxonomy §2.2).*

> EYEBROW: TYPICAL ENGAGEMENT
>
> SECTION TITLE:
> How CNC shops typically roll this out.

**Four-step horizontal timeline on desktop; vertical stack on mobile. Each step: label, headline, 2-line description.**

> **Week 1 — Proof of value.** One CNC, one shift, one OEE definition. EdgeConnect installed on a small Windows box in your control cabinet, polling that machine over FOCAS2 or Brother HTTP. Data flowing to a Mosquitto broker we set up alongside, or to your existing MQTT broker. EREMOS V2 displaying real cycle-time and alarm data, typically within the first few days.
>
> **Weeks 2-4 — Expansion to a cell.** Add the rest of the machines on one line or cell. Tag-map authoring done together with your team — your operators know the names that matter. Shift-report templates configured; OEE Segments aligned to your shift schedule.
>
> **Weeks 5-8 — Fleet rollout.** Remaining CNCs onboarded. Multi-site or multi-line aggregation in EREMOS V2 where applicable. Alerting routed to the channels your operations team already uses.
>
> **Ongoing.** New CNC vendor added to the floor? The platform already handles it — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 all ship today; FANUC MT-LINKi REST is on the roadmap. The next machine plugs into the same platform, not a new monitoring stack.

**Note:** the "typically within the first few days" phrasing (Phase 1 v2 §7) preserved — preserves deployment momentum without sounding contractual. **Correctness fix surfaced by the migration:** the Phase 1 v2 §7 "Ongoing" line listed S7 + OPC UA Client as *roadmap* and MT-LINKi as *today*; both are corrected here (S7 + OPC UA Client ship today per CLAUDE.md §8; MT-LINKi is roadmap).

---

### 3.8 Section 8 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How the pieces fit together for a CNC floor.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15). Replaces the Phase 1 v2 static SVG (NEW §15 ecosystem-framing addition):

Solution-annotated subset of the Industrial Intelligence Stack 4-column layout. Highlights:
- **Col 1 — Floor:** CNC controllers (Fanuc · Brother · Mazak · Okuma · Heidenhain) — highlighted as the signal sources for this solution
- **Col 2 — EdgeConnect peer (highlighted):** one runtime polling every CNC in its native protocol, normalizing to canonical CNC vocabulary. *For a CNC floor, the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required — EdgeConnect carries the floor-side.*
- **Col 3 — EREMOS V2 (highlighted):** consuming the canonical stream; OEE Segments, persistent alarms, shift reports, multi-tenant analytics
- **Col 4 — Customer Enterprise:** SCADA / MES / historian (highlighted as systems FED by the canonical stream, not replaced — explicit "beside, not replacing" arrow direction)

**Annotations (4 specific to this solution, per §5.A: the eyebrow doubles as the ≤4-word annotation title, followed by a 1-2 sentence body; max 8 annotations per zoom level. This eyebrow-as-title convention matches the locked edge-connectivity exemplar and is the pattern the other 4 migrations inherit):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 1 → Col 2 protocol arrows | NATIVE PROTOCOLS | EdgeConnect polls each CNC over its native protocol — FOCAS2 (Fanuc), MTConnect, Brother HTTP, Modbus TCP for PLC-fronted older machines, plus OPC UA Client + S7. *MT-LINKi REST on roadmap.* |
| Col 2 → Col 3 canonical stream arrow | CANONICAL CNC VOCABULARY | Spindle RPM, feed rate, parts count, cycle time, tool, axes, alarm codes arrive in the same shape regardless of vendor. Per-route store-and-forward survives connectivity gaps without losing source ordering. |
| Col 3 EREMOS V2 | OEE ON CANONICAL SIGNALS | OEE Segments computed from edge-collected cycle-time + parts-count signals — auditable, the same math across every machine and shift. |
| Col 3 → Col 4 SCADA / MES arrow | BESIDE, NOT REPLACING | EREMOS V2 publishes OEE rollups + incident records via API; your SCADA / MES / historian stay where they are and consume canonical signals instead of vendor-specific ones. |

> CAPTION (below diagram, size.sm italic):
> *For a CNC floor, Col 2 is the EdgeConnect peer; the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required for this solution. See the full peer-architecture story → `/architecture`.*

---

### 3.9 Section 9 — Trust Cue

Per design-system v3 §16 trust cue content pattern. 2 cues, both linking to `/security` (NEW §15 ecosystem-framing addition vs v2):

> **Placement note (pre-lock workflow, MF2).** §15 ecosystem-framing addition #2 describes trust-cue placement as "between Customer Outcomes and Architecture." The realized SolutionPanel order in BOTH locked sister exemplars (`/solutions/edge-connectivity` v2.1, `/solutions/predictive-maintenance` v2) places the trust cue **after Architecture, immediately before Cross-lens + Final CTA**. This spec follows the locked-precedent order (Architecture → Trust Cue → Cross-lens → CTA), not the literal §15 prose. **This is the intended, documented pattern the other 4 migrations inherit** — the §15 prose is superseded by the realized exemplar order; not a silent deviation. (Flag for a future design-system v3.x amendment to reconcile the §15 prose with the realized order.)

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Nothing depends on the cloud.** EdgeConnect runs offline by default — license validates locally, no phone-home. If your network or broker drops, per-route store-and-forward buffers locally and replays in source order on reconnect. Shops on an isolated network install and run the platform the same way as shops with internet; cloud connectivity is opt-in, not required.
>
> CUE 2 (size.base):
> **Per-gateway identity + hash-chained configuration audit.** Each plant runs its own EdgeConnect runtime with a per-gateway UUID established at first start. Every change — a new machine added, a tag-map edit, a threshold change — is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.10 Section 10 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages** (design-system v3 §17 line 529): `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub). (NEW §15 ecosystem-framing addition vs v2.)

> Pattern-setter decision (migration plan §5 Q-A — **RESOLVED at ChatGPT review, R4**): CNC touches two pillars; the related-pillar card leads with **Connectivity & Edge** (the protocol-coverage differentiator that *makes the solution possible*), with Operational Intelligence cross-linked inline in §3.3. Rationale (ChatGPT-endorsed): the cross-lens card should point to what makes the solution *unique*, not what the buyer likes *most* — OEE exists across the platform; protocol-agnostic collection is what makes this CNC solution distinct. **This becomes the precedent for the other 4 migrations**: when a solution touches multiple pillars, the cross-lens card leads with the differentiating capability, not the outcome capability.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONNECTIVITY & EDGE | The underlying capability — EdgeConnect as Pillar 1 | `/capabilities/connectivity-edge` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.11 Section 11 — Final CTA

Per buyer-taxonomy v1 §2.2 Plant-manager / Ops-VP CTA preference. Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic). Voice preserved from Phase 1 v2 §9.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your CNC floor.
>
> SUBHEAD:
> A controller mix, a target broker, an OEE definition — that's all we need to scope a proof of value. We run demos on real protocols against your real signals. No canned data, no slideware, no vague promises.
>
> PRIMARY CTA: Book a scoping call for your CNC floor
> HREF: `/contact?intent=cnc-scoping`
>
> SECONDARY CTA: Download the datasheet
> HREF: `/resources/datasheet`

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
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 common questions |
| 4-step timeline (composed from `SectionShell` + cards; no new primitive) | §3.7 typical engagement |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure + optional Typical Engagement = 11 sections here).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.11. **~1,950 words total** (reconciled post-pre-lock-workflow, MF1). The **10-section SolutionPanel core is ~1,750 words — within** the 1,500-1,800 target for `/solutions/<solution>` per `/capabilities` hub §9; the **optional** Typical Engagement section (§3.7, ~200 words) is the documented reason the full page sits over the ceiling at ~1,950 (migration plan D4 — an intentional, justified inclusion, not drift). The band is guidance calibrated to the 10-section shape. The per-section figures below are **approximate targets** (they sum to ~1,960 ≈ the stated ~1,950); architecture-diagram annotation bodies (§3.8) are counted as diagram content, not prose, per the locked edge-connectivity exemplar convention. If a trim is wanted, the §3.2 narrative is the lowest-risk candidate; the content as drafted is all earning its place.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~80 |
| 3.2 | The CNC Shop Reality (3 paragraphs) | ~200 |
| 3.3 | How Elpis Solves CNC Integration (callout + 4 bolded-lead paragraphs; +R1 cycle-time example, +R5 OI elevation) | ~430 |
| 3.4 | What's Included (2 buckets) | ~250 |
| 3.5 | Common Questions (6 Q&A; +R2 OEE-ownership) | ~390 |
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
| List MT-LINKi as operator-available today | Per side-flag #1 resolution (2026-06-04) + `/platform` v2.1 §6 re-add governance — MT-LINKi has no Studio wizard / modular adapter today. The Phase 1 v2 page listed it as today across §1/§3/§4/§7; this migration corrects it to a roadmap mention. Future edits must NOT re-add MT-LINKi to the today-list until the engineering milestone ships. |
| List S7 / OPC UA Client as roadmap | They are operator-available today (CLAUDE.md §8 + locked connectivity-edge v2). The Phase 1 v2 §7 had them as roadmap; this is corrected. |
| Use *"rip and replace"* framing or imply Elpis replaces the vendor CNC software / SCADA / MES | The page IS the "the iron stays, the data layer modernizes" story (§3.2 ¶3 + §3.5 Q1 + §3.8 "beside, not replacing"). Any drift toward replacement framing regresses the core promise. |
| Imply EdgeConnect Linux is current behavior | EdgeConnect is Windows today; Linux is near-term roadmap on the Edge Gateway appliance. The §3.4 deployment note carries the honest framing; don't drop it. |
| Position the Edge Gateway appliance as required | CNC shops typically run software-only on a control-cabinet box. The appliance is an option (§3.4 deployment note), not a requirement — carry-forward from connectivity-edge v2 §6. |
| Imply one EdgeConnect runtime serves multiple plants | Per locked `/architecture` v2.1 FAQ Q6 — each plant runs its own runtime with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregation. §3.9 Cue 2 carries the per-gateway-identity guard. |
| Claim specific OEE-percentage gains, downtime-reduction percentages, or dollar savings | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics. Quantified outcomes wait for the `/platform` teaser + Phase 3 customer-story registry. |
| Use absolute outcome claims ("zero downtime", "never lose a cycle" as an absolute promise) | Anti-overclaim discipline (OEM v2 precedent) — outcome verbs use "cut" / "reduce". Note: "never lose a cycle" / "no lost cycles" appears as a store-and-forward *mechanism* description (§3.3 ¶4, §3.4, §3.5 Q4), not as a headline outcome promise — keep it tied to the mechanism, not framed as a guarantee. |
| Add competitor names (Kepware, Ignition, MachineMetrics, etc.) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory. The §3.3 "How this differs" callout names the CATEGORY (per-vendor CNC monitoring tools) without naming specific vendor products beyond the Fanuc/Brother/Mazak/Okuma/Heidenhain controller examples that are the floor reality. |
| Add customer logos, customer names, or named deployment stories | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2/E has no customer-logo authorization; named stories wait for Phase 3 sign-off. |
| Use *"single source of truth"* / *"seamless"* / *"intuitive"* / *"easy"* / *"smart factory"* / *"digital transformation"* / *"AI insights"* / *"future-proof"* | Per buyer-taxonomy §2.2 vocabulary discipline — Plant managers / Ops VPs read these as consultant-speak or cliché. (Full backfire list parity with §1.2 + the §7 checklist.) |
| Lead the hero with products instead of the outcome | Per §15 SolutionPanel anti-pattern — the hero leads with "One operational view across every Fanuc, Brother, and Mazak", not "EdgeConnect + EREMOS V2". |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per §15 anti-pattern — solution pages need annotated subsets, not generic diagrams. (This is precisely what the migration upgrades vs the Phase 1 v2 static SVG.) |
| Sand off the Phase 1 voice character | Per migration plan D6 — "each dashboard speaks its own dialect", "reports your team will actually read", and the vendor-expansion inevitability anchor are retained voice choices. |

---

## 7. Sign-off checklist (v3 lock)

- [x] Page copy word count reconciled: **~1,950 total**; 10-section SolutionPanel core ~1,750 (within the 1,500-1,800 band); +~200 optional Typical Engagement section = documented over-ceiling per §15 (migration plan D4). All three statements (header / §5 / this line) agree.
- [x] All 11 sections present per SolutionPanel layout + the optional Typical Engagement section (design-system v3 §15)
- [x] §3.1 hero leads with outcome ("One operational view across every Fanuc, Brother, and Mazak on your floor"), not products
- [x] §3.1 subhead + trust strip drop MT-LINKi from the today-list (roadmap mention only)
- [x] §3.3 "How this differs from per-vendor CNC monitoring tools" callout present per §9 emerging-pattern governance
- [x] §3.3 names the contributing pillars (Connectivity & Edge + Operational Intelligence) with inline `/capabilities/<pillar>` cross-links (NEW §15 ecosystem-framing addition)
- [x] §3.3 inevitability anchor ("The next CNC vendor added to your floor should not require a new monitoring stack") preserved (voice)
- [x] §3.4 What's Included follows §15 schema (2 buckets: EdgeConnect + EREMOS V2; `hardwareProducts` omitted — bucket-narrative rationale documented)
- [x] §3.4 EdgeConnect deployment note honest (Windows today, Linux roadmap on Edge Gateway, appliance optional)
- [x] §3.4 + §3.5 Q2 protocol lists: FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 today; MT-LINKi REST roadmap
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 governance
- [x] §3.5 Q1 (vendor-software / SCADA replacement) explicitly says "beside, not replacing"
- [x] §3.5 Q3 (defensible OEE) ties OEE to canonical signals + auditability
- [x] §3.5 Q4 (network drop) describes store-and-forward + three-way diagnostics
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero"
- [x] §3.6 omits OEE-percentage and dollar-cost claims (proof-architecture v1 §3 + §4)
- [x] §3.7 Typical Engagement included with documented rationale (optional §15 section; deployment-anxiety buyer objection); "Ongoing" protocol line corrected (S7 + OPC UA Client today, MT-LINKi roadmap)
- [x] §3.8 architecture uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image; annotations honor §5.A discipline; includes the "Acquisition peer not required" Col-2 clarifier
- [x] §3.9 trust cues cover offline-first AND per-gateway identity + hash-chained audit; cross-link `/security`
- [x] §3.10 cross-lens cards match the LOCKED §17 preset for `/solutions/<solution>`; related-pillar card choice (Connectivity & Edge) flagged for review
- [x] §3.11 final CTA uses Plant-manager-preferred framing ("Book a scoping call for your CNC floor" / "Bring us your CNC floor") and is vertical-localized
- [x] EdgeConnect + EREMOS V2 positioning matches the LOCKED `/capabilities/connectivity-edge` + `/capabilities/operational-intelligence` specs
- [x] No vocabulary that backfires per buyer-taxonomy §2.2 (no *"single source of truth"* / *"seamless"* / *"smart factory"* / *"digital transformation"* / *"AI insights"*)
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names (Fanuc/Brother/Mazak/Okuma/Heidenhain are floor reality, not competitive comparison)
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] Phase 1 v2 voice character preserved (D6)
- [x] **Pattern-setter decisions documented** for the other 4 migrations to inherit (D1-D6 + Typical-Engagement-optional precedent + bucket-narrative choice + MT-LINKi/S7 correctness fixes)
- [x] ChatGPT review pass applied (verdict "Approve with changes"; R1-R5 applied)
- [x] Pre-lock validation workflow PASSED — 0 HIGH / 6 MED (LOCK-AFTER-FIXES); all 6 must-fixes + 3 optional-polish items applied (cross-spec drift + §15/§9 coverage + discipline-lock guard)

---

## 8. Out of scope for v1 (v3 content)

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers the full matrix with semantic modes (FOCAS2 polled vs subscription, OPC UA Server security profiles), per-protocol integration test patterns, FOCAS2 connection-pool sizing, MTConnect probe-document conformance, MT-LINKi REST detail.
- **Full EREMOS V2 capability detail.** `/capabilities/operational-intelligence` (LOCKED) covers OEE / alarms / multi-tenant as a Pillar 5 capability; this page cross-links rather than duplicating.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2.1) covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1) covers the cross-pillar Industrial Intelligence Stack; cross-link for the full stack story.
- **Protocol-agnostic / OT-consolidation depth-example.** `/solutions/edge-connectivity` (LOCKED v2.1) covers the cross-vendor edge story across all controller classes (S7 / Modbus / OPC UA); this page is the CNC-shop-specific cut.
- **Precision-manufacturing / brownfield / multi-site / OEM framings.** The four sibling solution pages (their own v3 migrations in this Phase E wave) cover those outcomes.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links from §3.9.
- **Industries-specific framings.** Phase 3 `/industries/<industry>` (or Phase 2.5 single-industry exception per amendment v3 §2).
- **Pricing / commercial engagement detail.** `/platform` covers the commercial teaser; Phase 3 `/pricing` covers detail.
- **Quantified OEE-gain / downtime-reduction / cost-savings percentages.** Wait for Phase 3 customer-story registry + the `/platform` commercial teaser.
- **Real customer case studies / named deployment stories.** Phase 3 customer-story sign-off process.

---

*`/solutions/cnc-machining` Page Spec **v1 LOCKED 2026-06-04** (page content v3 — SolutionPanel migration of the Phase 1 v1→v2 page copy) after ChatGPT review (verdict "Approve with changes"; R1-R5 applied) + the 3-validator + 1-synthesizer pre-lock validation workflow (run wf_9cb7e5e6-f68; 0 HIGH / 6 MED, verdict LOCK-AFTER-FIXES; all 6 must-fixes + 3 optional-polish items applied). PATTERN-SETTER for the Phase E bulk migration of the 5 v2 solution pages (design-system v3 §15 Q3 LOCKED); locked precedents P-A through P-G enumerated in the header workflow note. Migrates `solution-cnc-machining-v2.md` into the §9 canonical per-page-spec format + SolutionPanel §15 layout, adding the four §15 ecosystem-framing additions (pillar cross-refs, trust cue, ArchitecturePanel.interactive, cross-lens), §1.4 metadata, inline FAQ with FAQPage schema, and the "How this differs from per-vendor CNC monitoring tools" callout. Includes the optional Typical Engagement section (§15 baseline; deployment-anxiety buyer objection). Source-of-truth alignment baked into the draft: MT-LINKi → roadmap (side-flag #1 resolution); S7 + OPC UA Client corrected to today; EdgeConnect Windows-today/Linux-roadmap; per-gateway identity; beside-not-replacing; anti-overclaim "cut"-verb hedging. Phase 1 voice character preserved (D6). Next: user + ChatGPT review → pre-lock validation workflow → v3 LOCK → batch the other 4 (precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations) → lock + ship all 5 as one wave. Cites: page-capabilities-hub-spec-v1 §9, design-system-v3 §15/§16/§17/§5.A, buyer-taxonomy-v1 §2.2/§2.3, proof-architecture-v1 §3/§4/§8, page-capabilities-connectivity-edge-spec-v1 v2.1, page-capabilities-operational-intelligence-spec-v1 v1, page-architecture-spec-v1 v2.1, page-solutions-edge-connectivity-spec-v1 v2.1 (sister SolutionPanel exemplar), page-solutions-predictive-maintenance-spec-v1 v2, solution-cnc-machining-v2 (migrated source), solution-oem-machine-monitoring-v2 (anti-overclaim precedent), shared-knowledge/contracts/cnc-vocabulary.md, 2026-06-04-phase-e-solution-migration-plan.md.*
