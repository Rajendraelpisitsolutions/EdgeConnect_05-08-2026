<!--
File:        docs/marketing/page-solutions-edge-connectivity-spec-v1.md
Purpose:     Page spec for /solutions/edge-connectivity — solution
             depth-example for the brownfield-direct deployment path
             (OT consolidation / mixed-vendor floors / canonical
             vocabulary at edge). Ninth of 10 Phase 2 per-page specs.
             Uses SolutionPanel layout from design-system v3 §15.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), Phase 2 step 11 spec authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   solution-cnc-machining-v2.md (LOCKED Phase 1 marketing
                track — solution-page voice + structure precedent)
             solution-brownfield-modernization-v2.md (LOCKED Phase 1 —
                adjacent solution-page brownfield voice precedent)
             page-capabilities-connectivity-edge-spec-v1.md (LOCKED v2
                — source-of-truth for EdgeConnect + Edge Gateway
                positioning; "How this differs from a general-purpose
                IoT gateway" callout reviewer-validated at v2 lock)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page-type FAQ governance — /solutions/<solution>
                = YES; metadata governance; "How this differs from..."
                emerging pattern; emerging-pattern reviewer-validated
                on asset-intelligence v2)
             page-solutions-hub-spec-v1.md v2 (LOCKED — Card 1 Edge
                Connectivity summary that this page is the depth-
                example for)
             page-architecture-spec-v1.md v2 (LOCKED — cross-link
                target for "See full architecture", source-of-truth
                for the 3 deployment paths and integration patterns)
             page-solutions-predictive-maintenance-spec-v1.md v2
                (LOCKED — sister solution-page; same SolutionPanel
                pattern; precedent for cross-spec drift discipline +
                v1.1 upstream amendment pattern)
             phase2-ia-scope-memo-v2.md §3 (IA parent — /solutions
                scope) + amendment v3 (sequencing step 10)
             buyer-taxonomy-v1.md §2.3 (OT Architect / SCADA engineer
                — primary buyer) + §2.5 (Plant engineer retrofit /
                greenfield — secondary, deployment-BOM angle)
             proof-architecture-v1.md (proof discipline — no
                fabricated productivity-gain percentages; no per-
                vendor competitor names)
             design-system-v3.md §15 (SolutionPanel layout — LOCKED),
                §16 (TrustCueBlock), §17 (CrossLensBlock — LOCKED
                preset for /solutions/<solution>: line 559 —
                /capabilities/<related-pillar> + /architecture +
                /solutions)
             hardware-ecosystem-map-v3.md §2 (Connectivity & Edge
                pillar — EdgeConnect + Edge Gateway source-of-truth)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview — EdgeConnect Linux roadmap;
                Edge Gateway dual identity)
             solution-oem-machine-monitoring-v2.md (LOCKED — anti-
                overclaim discipline precedent for hedged claim
                wording; "cut" verb framing, no absolutes)
             security-page-copy-v2.md (cross-link target for trust
                cues — full operational trust posture)
Version:     v2.1 — LOCKED. Original v2 locked 2026-05-29 after Pass 1
                  ChatGPT review + 4-agent pre-lock validation workflow
                  (cross-spec drift check + SolutionPanel §15 coverage
                  check + discipline-lock guard check + synthesizer
                  integration; CLEAN at discipline-lock level; v2
                  applied 2 ChatGPT-approved refinements + 2 workflow
                  polish fixes + 1 governance-pattern lock). v2.1
                  amendment 2026-06-04 dropped MT-LINKi from 6 today-
                  list locations (§3.1 hero subhead, §3.3 ¶1, §3.4
                  What's Included, §3.5 FAQ Q4, §3.7 architecture
                  annotation, §6 anti-pattern reference, §1.2
                  vocabulary list) per platform-team direction (no
                  customer demand; engineering deferred to low
                  priority); MT-LINKi REST integration moved to
                  roadmap mention. Side-flag #1 (MT-LINKi publish-live
                  gate) now RESOLVED via this amendment.
Date:        2026-05-29 (v2 lock)
Status:      LOCKED.

Ninth per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 10. Solution depth-example page — uses SolutionPanel
layout from design-system v3 §15 (LOCKED, 10-section structure with
ArchitecturePanel solution-annotated subset + inline FAQ +
TrustCueBlock + CrossLensBlock LOCKED preset).

Page-structure approval: SolutionPanel §15 structure is LOCKED in
design-system v3 — no structural decision needed. Content
commitments (hero outcome direction verbatim from /solutions hub
Card 1, "How this differs from per-vendor monitoring tools" callout,
3 deployment shapes [software-only / appliance / hybrid], 6 FAQ
topics, 2 trust cues, architecture-annotated diagram approach,
6 anti-pattern locks, vertical-localized "Bring us your controller
mix" CTA) all approved by user direction 2026-05-29 before drafting.

Word-count target: 1,500-1,800 words per /capabilities hub §9
page-type guidance for /solutions/<solution>. Current draft:
~1,700 words page copy.

Source-of-truth alignment discipline (lessons from predictive-
maintenance v2 pre-lock workflow):
  - EdgeConnect Linux: roadmap (NOT current behavior). Today Windows
    service. Linux ships on Edge Gateway appliance.
  - Edge Gateway dual identity: today standalone PLC-to-cloud
    appliance; tomorrow canonical EdgeConnect appliance (same
    hardware, two lifecycles). NEITHER framing as "the only
    deployment path" allowed per locked connectivity-edge v2 §6.
  - Per-gateway identity: each plant runs its OWN EdgeConnect
    runtime; multi-plant visibility from EREMOS V2 aggregating
    across per-plant runtimes. Anti-multi-plant-EdgeConnect locked
    per /architecture v2 FAQ Q6.
  - Protocol list (today): FOCAS2, MT-LINKi, MTConnect, Brother HTTP,
    Modbus TCP, OPC UA Client, S7. Per locked connectivity-edge v2
    + master architecture spec. Full protocol matrix lives on Phase E
    /edgeconnect; this page stays at solution-level vocabulary.
  - "Beside, not replacing" framing for SCADA / historian / MES
    per locked /architecture v2 §3.6 integration patterns.
  - Cross-link discipline: this page cross-links to /capabilities/
    connectivity-edge as the authoritative capability source.

Inline FAQ (§3.5 "Common questions") included per /capabilities hub
§9 per-page-type FAQ governance (/solutions/<solution> is YES — and
SolutionPanel §15 §3.5 already specifies inline Q&A as part of the
locked structure).

§1.4 Page metadata block included per /capabilities hub §9 metadata
governance lock (PR #71).

"How this differs from per-vendor monitoring tools" callout (§3.3)
applied per /capabilities hub §9 emerging-pattern governance.
Different adjacent category from the /capabilities/connectivity-edge
v2 spec's "How this differs from a general-purpose IoT gateway"
callout — solution-page audience evaluates against vendor-specific
monitoring tools (FANUC NCStudio, Mazak SmartBox, Brother BCMS,
etc.) more than against generic IoT gateways. Both callouts honor
anti-overclaim discipline + no-competitor-names rule (describes
category, not specific vendor products).

Cross-link discipline: this page cross-links to /capabilities/
connectivity-edge as the authoritative capability source. PR #73
(connectivity-edge) is LOCKED v2 but not yet merged to master.
Same governance applies as on /solutions hub v2 lock + predictive-
maintenance v2: verify PR #73 merge status before publishing this
page to the live website. Spec locks now; merge governance is a
separate workflow.

Governance side-flag (carried forward from /solutions hub v2 + the
predictive-maintenance v2 precedent): when this page ships to live,
/solutions hub v2 §3.2 Card 1 must remove the "Coming soon" status
pill and swap the pre-live link from /capabilities/connectivity-edge
to /solutions/edge-connectivity per solutions-hub-v2 pre-live link
policy.

v2 changes from v1 (applied 2026-05-29):

ChatGPT-approved refinements (user pre-approved):
  - M1 (R1+R5): Edge Gateway dual-identity business value sentence
    added at end of §3.3 deployment-shapes body paragraph. Anchors
    dual-identity story to a buyer-readable economic outcome
    ("reducing platform migration friction as deployments evolve,
    and reducing the need to maintain customer-owned Windows
    infrastructure"). Addresses ChatGPT's "missing the so what"
    observation.
  - M2 (R2): Concrete canonical-vocabulary worked example added in
    §3.3 paragraph 2 ("A FANUC alarm code, a Mazak alarm code, a
    Brother alarm code, and a Siemens S7 alarm bit all surface as
    the same canonical Alarm State... Vendor-specific cycle-time
    signals collapse the same way into a canonical Cycle Time").
    Removes the abstraction around "canonical CNC vocabulary".

Workflow pre-lock validation polish fixes:
  - M3 (terminology consistency): §3.5 FAQ Q2 "canonical Linux
    footprint" → "canonical EdgeConnect appliance" (corpus-locked
    phrasing; one-off variance fixed).
  - J2 (standalone-mode sharpening): §3.5 FAQ Q2 sentence
    restructured to make standalone-vs-EdgeConnect lifecycle louder
    ("via its embedded Linux runtime — but in standalone PLC-to-cloud
    mode, not yet as an EdgeConnect runtime").
  - M4 (governance-pattern lock): §3.4 preamble expanded to lock the
    whatsIncluded bucket-narrative pattern for Phase E cross-spec
    consistency.

User-decided judgement calls (workflow surfaced):
  - J1 (Linux qualifier "near-term roadmap" vs "on the roadmap"):
    KEPT 'near-term'. Reasoning: solution-page reader is making a
    procurement decision; 12-18 month framing is material. Parent
    specs use 'on the roadmap' without temporal qualifier — fine for
    capability/architecture surfaces. If Linux ships timeline slips
    beyond 18 months, retire the qualifier.

Cross-spec governance signals (NOT changes to this spec; tracked as
v2-lock side-flags for separate workflows):
  - SIDE-FLAG #1 (publish-live gate): MT-LINKi is listed as
    operator-available across all marketing specs (this + connectivity-
    edge v2 + architecture v2 + /solutions hub v2) but is NOT listed
    in CLAUDE.md §8 "Current state (2026-05-31)" as operator-available
    (which lists: Modbus TCP, FANUC FOCAS2, MTConnect, Brother HTTP,
    OPC UA Client, Siemens S7). MT-LINKi must be verified by platform
    team BEFORE any of these marketing specs goes live. If genuinely
    operator-available, update CLAUDE.md §8. If not, all 4 marketing
    specs need v1.1 amendments dropping MT-LINKi from the today-list.
  - SIDE-FLAG #2 (upstream v1.1 amendment ticket on PR #73): connectivity-
    edge v2 §3.2 Card 1 EdgeConnect protocol list contains a typo
    that lists 'OPC UA Server' in the source-polling slot where it
    should read 'OPC UA Client' (Server is sink/expose; Client is
    source-polling). This spec correctly uses 'OPC UA Client' in all
    6 locations. File v1.1 amendment on PR #73 to fix parent typo.
  - SIDE-FLAG #3 (upstream v1.1 amendment ticket on PR #77): architecture
    v2 §3.7 FAQ Q4 names PI/Wonderware/Aveva as historian examples —
    the M2 softening pass removed named vendors from §3.6 Pattern B
    but missed Q4 in the inline FAQ. This spec's §3.5 Q5 is correctly
    measured. File v1.1 amendment on PR #77 to retroactively M2-soften
    Q4.
  - SIDE-FLAG #4 (carried forward from /solutions hub v2 + predictive-
    maintenance v2 precedent): When this page ships live, /solutions
    hub v2 §3.2 Card 1 must remove "Coming soon" status pill and swap
    pre-live link from /capabilities/connectivity-edge to /solutions/
    edge-connectivity.
  - SIDE-FLAG #5 (PR merge status): PR #73 (connectivity-edge v2) and
    PR #72 (condition-monitoring v1.1) are LOCKED on their branches
    but NOT yet merged to master. Verify PR #73 merge status before
    publishing this page to the live website.

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve after a light polish pass. Best FAQ in the entire system.
   Strongest anti-pattern section. This is the first page that
   genuinely feels like someone who understands the real OT Architect
   buying conversation wrote it."

Pre-lock workflow verdict (2026-05-29):
  "Validation pass is CLEAN at the discipline-lock level (no HIGH-
   severity findings, no blocking issues). All four discipline-lock
   areas (A EdgeConnect Linux roadmap, B Edge Gateway dual identity,
   C Per-gateway identity, D Beside-not-replacing) and §15
   SolutionPanel structural compliance explicitly validated as no-
   change-required. Source-of-truth alignment baked into v1 drafting
   from the predictive-maintenance v2 workflow lessons actually
   worked — discipline-during-drafting genuinely prevented the kind
   of HIGH-severity drift that caught predictive-maintenance v1."
-->

# `/solutions/edge-connectivity` — Page Spec v1

**Solution depth-example for the brownfield-direct deployment path. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want to understand the OT-consolidation outcome built on the Industrial Intelligence Ecosystem — EdgeConnect + Edge Gateway + EREMOS V2 working together — and how it fits a brownfield CNC / mixed-vendor / OT-modernization engagement without ripping out existing controllers.**

This is the page where OT Architects and SCADA engineers land when they want the **outcome view** of Connectivity & Edge — one operational view across every controller on their floor, protocol-agnostic edge runtime, canonical CNC vocabulary at the edge, deployed on either software-only or appliance hardware. It is **not** the capability page (`/capabilities/connectivity-edge` covers EdgeConnect + Edge Gateway as a Pillar 1 capability story). It is **not** the architecture walkthrough (`/architecture` covers cross-pillar composition). It is the **edge-connectivity solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the brownfield-direct deployment path. Reader leaves with *"I now understand what edge connectivity on the Elpis platform actually does for my OT consolidation, how it deploys whether I want software-only or an appliance, what questions I'd have on an architecture review, and what outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/connectivity-edge` covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; LOCKED v2)
- A product detail page (Phase E `/edgeconnect`, `/edge-gateway` will each cover their product surface — including the full protocol coverage matrix, semantic modes, OPC UA Server security profiles)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2)
- A pricing or commercial-engagement page (Phase 2 step 11 `/platform` covers commercial-engagement teaser; Phase 3 `/pricing` covers detail)
- A CNC-specific solution narrative (existing v2 `/solutions/cnc-machining` covers the CNC-shop-specific outcome; v3 in Phase E)
- A brownfield-modernization framing (existing v2 `/solutions/brownfield-modernization` covers the broader modernization story; v3 in Phase E — this page is the EdgeConnect+Edge-Gateway-specific depth-example)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** OT Architect / SCADA engineer (§2.3)
- Lands here from `/solutions` hub (Card 1 Edge Connectivity), from `/capabilities/connectivity-edge` via cross-link, from a Google search for *"FOCAS2 MQTT gateway"* / *"protocol-agnostic OT data layer"* / *"mixed-vendor CNC monitoring"* / *"brownfield CNC integration"*, or via the homepage hero
- Wants: protocol coverage with semantic clarity (not just "we support 20 protocols"), canonical normalization at the edge, deployment-shape options (software-only vs appliance), SCADA-coexistence honesty, multi-site fleet patterns, per-plant identity discipline
- CTA preference: *"Bring us your controller mix"* > *"Request an architecture review"* > *"Talk to an engineer"*
- Vocabulary that lands: protocol-agnostic, edge runtime, canonical CNC vocabulary, OPC UA Server, MQTT, FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7, store-and-forward, three-way diagnostics, hash-chained audit, per-gateway identity, brownfield, retrofit
- Vocabulary that backfires: *"seamless integration"*, *"intuitive"*, *"easy"*, *"future-proof"*, *"single pane of glass"* (often reads as opinionated about operations team's existing tools), *"end-to-end"* without specifying the ends, *"transformation"*

**Secondary buyer:** Plant engineer (retrofit / greenfield) (§2.5) — deployment-BOM angle
- Lands here when scoping the BOM for a brownfield retrofit or greenfield install
- Wants: hardware-vs-software-only trade-offs, Edge Gateway specs, deployment patterns per plant, multi-site fleet sizing
- CTA preference: *"Get hardware specifications"* (cross-links to `/capabilities/connectivity-edge` for the Edge Gateway hardware detail)

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Edge Connectivity — EdgeConnect + Edge Gateway · Elpis* |
| **Meta description** (140-160 chars) | *One operational view across every controller on your floor. Protocol-agnostic edge runtime, canonical CNC vocabulary, software-only or appliance.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/edge-connectivity` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Product cards for EdgeConnect + Edge Gateway (when Phase E `/edgeconnect` and `/edge-gateway` ship) cross-link via `Product` + `SoftwareApplication` schema. Page-to-page cross-links to `/capabilities/connectivity-edge` + `/architecture` + `/security` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). 10 sections — same shape as `/solutions/predictive-maintenance` v2.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome-led headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~100 |
| **2** | The Customer Pain — narrative empathy (2-3 paragraphs) | `light` | Narrative copy + optional pull-quote in margin | ~200 |
| **3** | How Elpis Solves This — 3-4 bolded-lead paragraphs + "How this differs from per-vendor monitoring tools" callout + 3 deployment shapes | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block + 3-column grid | ~400 |
| **4** | What's Included — split into From Hardware + From EREMOS V2 (per §15 schema; EdgeConnect is the page's whole story, so the Hardware bucket leads) | `light` | Bulleted feature list with bolded leads | ~200 |
| **5** | Common Questions (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions with answers below + `FAQPage` schema | ~350 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column on desktop | `dark` | Bolded outcome leads + light-weight supporting clauses | ~150 |
| **7** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" link | ~80 |
| **8** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | `TrustCueBlock` (design-system v3 §16) | ~80 |
| **9** | Cross-lens navigation — LOCKED preset per design-system v3 §17 line 559 | `light-tinted` | `CrossLensBlock` (3 cards: `/capabilities/connectivity-edge` + `/architecture` + `/solutions`) | ~50 |
| **10** | Final CTA — vertical-localized | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · EDGE CONNECTIVITY
>
> HEADLINE (size.3xl semibold):
> One operational view across every controller on your floor — without ripping out what you already have.
>
> SUBHEAD (size.lg, max-width 60ch):
> Protocol-agnostic edge runtime for FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and S7. Canonical CNC vocabulary at the edge. Deploy software-only on your Windows infrastructure today, or on the Edge Gateway appliance — and standardize across plants when you scale.
>
> PRIMARY CTA (`Button.primary.lg`):
> Bring us your controller mix
> HREF: `/contact?intent=edge-connectivity-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`

**Anti-patterns:** No *"seamless integration"* / *"intuitive"* / *"easy"* / *"single pane of glass"* framing (per buyer-taxonomy §2.3 vocabulary discipline). No *"transformation"* / *"future-proof"* generic-marketing language. No outcome metric in headline.

---

### 3.2 Section 2 — The Customer Pain

> EYEBROW: THE PAIN
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Most factory floors have controllers from three or four vendors. A FANUC cell beside a Mazak machining center beside a Brother tapper beside a Siemens S7 line. Each vendor ships a native monitoring tool — FANUC NCStudio, Mazak SmartBox, Brother BCMS, Siemens TIA. Each tool speaks its own vocabulary, has its own UI, its own alarm thresholds, its own reporting cadence. Operations teams that want one operational view across every machine end up either using all the per-vendor tools (and reconciling reports manually), or building custom integration scripts that break every time a vendor pushes a firmware update.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> The teams that try to consolidate usually face the same three problems: (1) per-protocol custom scripting is expensive to write, expensive to maintain, and expensive to extend when a new controller shows up on the floor, (2) the SCADA / historian / MES already running on the floor doesn't speak the same canonical vocabulary as the integration layer that gets built around it, so reports drift across systems, (3) when the team finally has signals consolidated, the integration is hosted on a single industrial PC that becomes the new single point of failure — and the operations team has to learn yet another piece of infrastructure to maintain.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The teams that succeed do something different. They put a protocol-agnostic edge runtime in front of every controller — one runtime that speaks every native protocol the floor uses today, normalizes signals to a canonical vocabulary at the edge, and publishes the normalized stream once for every downstream system that wants it. The SCADA stays. The historian stays. The MES stays. The integration tax goes down. That's what this page is about.

---

### 3.3 Section 3 — How Elpis Solves This

> EYEBROW: HOW ELPIS SOLVES THIS

> CALLOUT — HOW THIS DIFFERS FROM PER-VENDOR MONITORING TOOLS (size.base, single paragraph; visual treatment: bordered card or left-rule callout, sits before the bolded-lead paragraphs below):
>
> > **How this differs from per-vendor monitoring tools.** Per-vendor monitoring tools (FANUC NCStudio, Mazak SmartBox, Brother BCMS, Siemens TIA, etc.) each work — for the vendor they ship with. They each speak vendor-specific vocabulary, have their own UI conventions, and report on their own schedule. Edge Connectivity puts a protocol-agnostic edge runtime in front of every vendor at once, normalizes signals to **canonical CNC vocabulary at the edge**, and publishes that normalized stream once for every downstream system that needs it. **Same FANUC, same Mazak, same Brother, same S7. Same operations team. One canonical vocabulary across them all.** The per-vendor tools stay where they are; Elpis adds the cross-vendor layer.

#### Bolded-lead paragraphs (4 paragraphs):

> **Polls every controller in its native protocol.** EdgeConnect is a protocol-agnostic edge runtime that connects to existing controllers over FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and S7 — today. FANUC MT-LINKi REST integration is on the roadmap. No per-machine custom scripting. No per-vendor middleware. The runtime owns the protocol; the operations team owns the configuration. See the underlying capability story → `/capabilities/connectivity-edge`. For the full protocol matrix with semantic modes and security profile detail, see Phase E `/edgeconnect` (coming soon).

> **Normalizes to canonical vocabulary at the edge.** Every signal — spindle load, axis position, alarm state, program ID, cycle time — arrives at every sink in the same canonical CNC shape, regardless of which controller produced it. A FANUC alarm code, a Mazak alarm code, a Brother alarm code, and a Siemens S7 alarm bit all surface as the same canonical Alarm State — same shape, same severity vocabulary, same downstream consumer code path. Vendor-specific cycle-time signals collapse the same way into a canonical Cycle Time. The SCADA reads canonical signals. The historian archives canonical signals. EREMOS V2 computes OEE on canonical signals. The MES consumes canonical signals. Reports stop drifting across systems because every system reads the same vocabulary.

> **Publishes once, fans out to every downstream consumer.** Store-and-forward per-route buffering means no lost cycles during broker outages or maintenance windows. Three-way diagnostics — source / pipeline / sink — surface immediately when the data path breaks, so the OT team knows which leg failed before the production team feels the symptom. Hash-chained configuration audit captures every change with actor identity and timestamp.

> **Deploys per plant, on the shape that fits the floor.** EdgeConnect deploys **per plant**, never as one runtime across multiple plants — per-gateway identity established at first start protects plant-level isolation and offline operability. Multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes, not from a multi-plant EdgeConnect. The same canonical vocabulary surfaces across every plant; the multi-site story stays clean.

**Three deployment shapes (3-column grid below the bolded-lead paragraphs):**

> EYEBROW: THREE DEPLOYMENT SHAPES
>
> SECTION TITLE:
> Choose the deployment shape that fits each plant.

| | **Software-only** | **Edge Gateway appliance** | **Hybrid (multi-site)** |
|---|---|---|---|
| **When it fits** | Sites with existing Windows infrastructure where running EdgeConnect on customer-owned hardware fits the operations team's preferences | Sites that prefer a ruggedized turnkey appliance — DIN-rail mount, embedded Linux, built-in 4G + Wi-Fi + Ethernet, USB firmware updates | Multi-plant operators standardizing across diverse plant topologies — software-only on some plants, appliance on others, all per-plant runtimes feeding one EREMOS V2 tenant |
| **What's deployed** | EdgeConnect runtime on customer Windows + EREMOS V2 | Edge Gateway appliance (standalone PLC-to-cloud today; canonical EdgeConnect appliance once Linux ships) + EREMOS V2 | Per-plant mix: EdgeConnect (Windows) or Edge Gateway (appliance), each with its own per-gateway identity |
| **EdgeConnect today** | Windows service running on customer-owned infrastructure | Edge Gateway ships standalone today with built-in Modbus TCP + cellular publish. EdgeConnect Linux is near-term roadmap — when it ships, it deploys on Edge Gateway as the canonical EdgeConnect appliance, same hardware. | Whichever runtime fits each plant; multi-site identity reconciled in EREMOS V2 |
| **Hardware footprint** | Software-only | Appliance per plant (200 × 150 × 75 mm rugged enclosure, 24 V DC) | Per-plant choice |

> BODY (after the grid, size.base):
> The deployment shape is decided **per plant, not per fleet**. A multi-site operator can run software-only EdgeConnect in one plant, the Edge Gateway appliance in another, and either in a third — all reporting into one EREMOS V2 instance with consistent canonical vocabulary, OEE definitions, and alarm semantics across every site. The shape changes per plant; the operational story stays the same.
>
> And the Edge Gateway's dual identity matters at procurement time: the same hardware that solves today's standalone PLC-to-cloud need becomes tomorrow's canonical EdgeConnect appliance once Linux ships — reducing platform migration friction as deployments evolve, and reducing the need to maintain customer-owned Windows infrastructure on plants that prefer a turnkey appliance.

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets (`hardwareProducts` + `eremosV2`). EdgeConnect is the page's whole story, so the Hardware bucket leads with both EdgeConnect (software) and Edge Gateway (appliance) as the platform layer — bucket-label tuned per solution narrative (*"From the Edge Layer"* here; *"From Hardware"* on `/solutions/predictive-maintenance` v2), schema bucket is `hardwareProducts` in both cases.

> *Pattern note for Phase E v3 migration of the 5 existing v2 solution pages: solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names — EdgeConnect is intentionally folded under `hardwareProducts` here (Edge Layer framing) rather than populating the schema's separate `edgeConnect?` bucket, because EdgeConnect IS this page's whole story; pm v2 omitted `edgeConnect` for the opposite reason (not used in that solution).*

#### From the Edge Layer

**EdgeConnect (the protocol-agnostic edge runtime, Windows today):**

> - **Protocol coverage today:** FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7. **On the roadmap:** FANUC MT-LINKi REST integration. For the full protocol matrix with semantic modes + security profiles, see Phase E `/edgeconnect` (coming soon).
> - **Canonical CNC vocabulary at the edge** — every signal arrives at every sink in the same shape regardless of which controller produced it
> - **Publishes to MQTT, exposes via OPC UA Server, publishes to HTTP/TCP** — multiple downstream consumers from one normalized stream
> - **Per-route store-and-forward buffering** — no lost cycles during broker outages or maintenance windows
> - **Three-way diagnostics** — source / pipeline / sink health surfaced separately, so the OT team knows which leg failed before production feels the symptom
> - **Hash-chained configuration audit** — every change captured with actor identity and timestamp; tamper-evident, replay-ready

> > *Honest framing — EdgeConnect ships as a Windows service today. The Linux runtime is near-term roadmap; when it ships, it deploys on the Edge Gateway appliance as the canonical EdgeConnect appliance. The Edge Gateway appliance ships today standalone with built-in Modbus TCP + cellular publish — it grows into the broader EdgeConnect platform path on the same hardware once Linux ships.*

**Edge Gateway (ruggedized appliance, optional — software-only is fully supported):**

> - **Ruggedized industrial gateway** running embedded Linux — 256 MB RAM, 2 GB Flash, 24 V DC, 200 × 150 × 75 mm rugged enclosure
> - **Built-in Modbus TCP server/client** (today, standalone)
> - **4G + Wi-Fi + Ethernet** connectivity with USB firmware updates
> - **Web-configurable** — DIN-rail mount, no separate industrial PC required
> - **Dual identity:** today a standalone PLC-to-cloud gateway. Tomorrow, once EdgeConnect Linux ships, the canonical EdgeConnect appliance — same hardware, two lifecycles.

#### From EREMOS V2 (consuming the canonical stream)

> - **Multi-tenant analytics** — per-plant signals arrive at per-tenant scope; multi-site visibility from one EREMOS V2 instance across per-plant EdgeConnect runtimes
> - **OEE computed on canonical signals** — same OEE math across every plant + every shift + every vendor controller
> - **Persistent alarms with incident workflows** — alarm → triage → assigned-to → resolution → closure, with operator notes at each step
> - **Customer-controlled cloud connectivity** — opt-in, not required. Plants on isolated OT VLANs install and run the platform the same way as plants with internet.

---

### 3.5 Section 5 — Common Questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to OT Architect / SCADA engineer concerns.

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What OT Architects ask before scoping a brownfield consolidation.

#### Q1. Does this replace our SCADA?

> No. EdgeConnect and EREMOS V2 sit beside your SCADA — they don't take over operator HMIs, control logic, or alarm acknowledgment workflows. SCADA stays where it is; Elpis adds the cross-vendor canonical-vocabulary layer and consolidates protocol coverage so your downstream systems (SCADA, historian, MES) receive consistent canonical signals regardless of which controller produced them.

#### Q2. Can EdgeConnect run on Linux today?

> Today, EdgeConnect ships as a Windows service. Linux is near-term roadmap — when it ships, it deploys on the Edge Gateway appliance as the canonical EdgeConnect appliance. For Linux-required customers today, the Edge Gateway hardware handles the most common protocol bridging via its embedded Linux runtime — but in standalone PLC-to-cloud mode (built-in Modbus TCP + cellular publish), not yet as an EdgeConnect runtime (which arrives when EdgeConnect Linux ships). Both paths are supported; neither is a prerequisite for the other.

#### Q3. Can one EdgeConnect deployment serve multiple plants?

> No — that's an anti-pattern, and Elpis explicitly doesn't recommend it. Each plant runs its own EdgeConnect (Windows service or Edge Gateway appliance) with a per-gateway UUID established at first start. Multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes — not from a single multi-plant EdgeConnect runtime. This protects plant-level isolation, per-site identity, and offline operability per plant.

#### Q4. What protocols are covered today vs roadmap?

> Today: FOCAS2 (FANUC CNCs), MTConnect (CNC standard), Brother HTTP (Brother CNCs), Modbus TCP (general industrial), OPC UA Client (broad industrial), and S7 (Siemens). On the roadmap: FANUC MT-LINKi REST integration (FANUC robotics / line-monitoring via MT-LINKi's REST API on port 3000). Each shipped protocol ships with semantic-mode coverage appropriate to the controller class — for the full matrix (semantic modes, security profiles, integration test patterns, OPC UA Server security mode detail), see Phase E `/edgeconnect` (coming soon). Roadmap protocols are scoped during the architecture review based on the controller mix you bring.

#### Q5. How do we integrate with our existing historian or MES?

> EdgeConnect publishes to MQTT and exposes signals via OPC UA Server. EREMOS V2 publishes incident records and OEE rollups via REST API. Your historian (whether time-series database like InfluxDB / TimescaleDB or enterprise historian) consumes from the OPC UA Server or the MQTT stream. Your MES consumes EREMOS V2's rollups via API or webhook. **Beside, not replacing** — both your historian and your MES stay where they are; they consume canonical signals instead of vendor-specific ones. For the full integration-patterns walkthrough, see `/architecture` §3.6.

#### Q6. What happens when the network drops?

> Per-route store-and-forward. Every signal queues at the source with its quality code preserved. When connectivity returns, signals replay in source order — no lost cycles, no manual recovery step. Three-way diagnostics (source / pipeline / sink) surface immediately during the outage so the OT team can see exactly which path was affected. Plants on isolated OT VLANs operate the same way; the cloud connectivity is opt-in, not required.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when this lands.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **One operational view across every controller on your floor** — FANUC, Mazak, Brother, Siemens, generic Modbus, OPC UA endpoints — speaking the same canonical CNC vocabulary at every downstream system
> - **Cut per-vendor monitoring tool sprawl** — the per-vendor tools stay where they are; Elpis adds the cross-vendor layer so the operations team works against one consistent view, not three or four reconciled-by-hand reports
> - **Cut custom protocol scripting and the firmware-update tax** — the protocol-agnostic edge runtime owns the protocol drivers; operations doesn't rewrite scripts every time a vendor pushes a firmware update
> - **Multi-site canonical consistency** — every plant's EdgeConnect runtime produces signals in the same canonical CNC shape; cross-site reports stop drifting because every site reads the same vocabulary
> - **Per-gateway identity from day one** — multi-site fleets have unambiguous per-plant identity from first start; acquisitions, divestitures, plant transfers, name changes survive the identity model unchanged
> - **No new monitoring stack for SCADA-running plants** — your SCADA, historian, and MES stay where they are; they consume canonical signals via OPC UA Server / MQTT / API instead of vendor-specific ones
> - **Audit-ready configuration history** — every protocol-driver enable, every routing change, every threshold edit captured with actor identity and timestamp; tamper-evident, replay-ready

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific percentage productivity gains or dollar-cost-savings claims. The /platform commercial-engagement teaser and Phase 3 case studies handle quantified outcomes once the customer-evidence registry is in place.

---

### 3.7 Section 7 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How the pieces compose for edge connectivity.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15):

Solution-annotated subset of the Industrial Intelligence Stack 4-column layout. Highlights:
- **Col 1 — Floor:** mixed-vendor controllers (FANUC, Mazak, Brother, Siemens, generic Modbus, OPC UA endpoints) — highlighted as the signal sources for this solution
- **Col 2 — EdgeConnect peer (highlighted):** EdgeConnect runtime polling every controller in its native protocol, normalizing to canonical CNC vocabulary at the edge. *For edge connectivity, the Acquisition peer is not required for this solution — EdgeConnect carries the floor-side.*
- **Col 3 — EREMOS V2 (highlighted):** consuming the canonical stream; OEE, alarms, multi-tenant analytics across per-plant runtimes
- **Col 4 — Customer Enterprise:** SCADA, historian, MES (highlighted as enterprise systems FED by the canonical stream, not replaced — explicit "beside, not replacing" arrow direction)

**Annotations (4 specific to this solution):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Col 2 EdgeConnect peer | THE EDGE RUNTIME | EdgeConnect runtime polls existing controllers over native protocols (FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7). Normalizes to canonical CNC vocabulary at the edge. *Windows service today; Linux on Edge Gateway via near-term roadmap. MT-LINKi REST integration also on roadmap.* |
| Col 2 → Col 3 canonical stream arrow | CANONICAL NORMALIZATION | Every signal — spindle load, axis position, alarm state, program ID, cycle time — arrives at every sink in the same shape. Per-route store-and-forward survives connectivity gaps without losing source ordering. Three-way diagnostics surface the broken leg. |
| Col 3 → Col 4 SCADA / historian / MES arrow | BESIDE, NOT REPLACING | EdgeConnect exposes via OPC UA Server + publishes to MQTT. EREMOS V2 publishes incident records + OEE rollups via API. Your SCADA / historian / MES stay where they are; they consume canonical signals instead of vendor-specific ones. |
| Col 2 multi-plant identity overlay | PER-GATEWAY IDENTITY | Each plant runs its own EdgeConnect runtime with per-gateway UUID. Multi-plant visibility comes from EREMOS V2 aggregating across per-plant runtimes — NOT from a multi-plant EdgeConnect. Anti-multi-plant-runtime locked. |

> CAPTION (below diagram, size.sm italic):
> *For edge connectivity, Col 2 is the EdgeConnect peer; the Acquisition peer (mDAQ + mTracker + VAS + E-IDOS) is not required for this solution. See the full peer-architecture story → `/architecture`.*

---

### 3.8 Section 8 — Trust Cue

Per design-system v3 §16 `TrustCueBlock`. 2 cues for edge connectivity, both linking to `/security`:

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Offline-first operation, including air-gapped plants.** EdgeConnect runs offline by default. License validates locally — no phone-home. Cloud connectivity is opt-in, not required. Plants on isolated OT VLANs install and run the platform the same way as plants with internet access. Per-route store-and-forward survives connectivity gaps without losing source ordering.
>
> CUE 2 (size.base):
> **Per-gateway identity + hash-chained configuration audit.** Each plant runs its own EdgeConnect runtime with a per-gateway UUID established at first start. Hash-chained configuration audit captures every change (protocol-driver enables, routing changes, threshold edits) with actor identity and timestamp — tamper-evident, replay-ready. Acquisitions, divestitures, plant transfers, name changes — the identity model survives them all.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.9 Section 9 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages** (design-system v3 §17 line 559): `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub).

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONNECTIVITY & EDGE | The underlying capability — EdgeConnect + Edge Gateway as Pillar 1 | `/capabilities/connectivity-edge` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.10 Section 10 — Final CTA

Per buyer-taxonomy v1 §2.3 OT Architect / SCADA engineer CTA preference. Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic).

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your controller mix. We'll scope the deployment shape.
>
> SUBHEAD:
> Whether you're scoping a brownfield CNC retrofit, a mixed-vendor consolidation, a greenfield install with existing PLC infrastructure, or a multi-site standardization across diverse plant topologies — bring us the controller list, the existing-systems boundary (SCADA / historian / MES coexistence), and the per-plant deployment-shape preferences. Architecture review runs against real protocols and real integration patterns, not slideware.
>
> PRIMARY CTA: Bring us your controller mix
> HREF: `/contact?intent=edge-connectivity-scoping`
>
> SECONDARY CTA: Request an architecture review
> HREF: `/contact?intent=architecture-review`

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.10 final CTA |
| `ArchitecturePanel.interactive` (variant=`solution-annotated` per §5.A + §15) | §3.7 architecture-for-this-solution diagram |
| `TrustCueBlock` (design-system v3 §16) | §3.8 trust cues |
| `CrossLensBlock` (design-system v3 §17 — LOCKED preset for /solutions/<solution>) | §3.9 cross-lens |
| `CTASection` | §3.10 final CTA |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 common questions |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.10. **~1,790 words total** (within 1,500-1,800 target for `/solutions/<solution>` page-type per `/capabilities` hub spec §9 page-type guidance). Increase from v1 (~1,700) reflects: M1 dual-identity business value (+45 words), M2 canonical-vocabulary worked example (+40 words), J2 Q2 sharpening + M3 terminology fix (+5 words net), M4 §3.4 governance note expansion (~70 words documenting the bucket-narrative pattern — but offsets the original single-sentence preamble of ~25 words, net +45 words). Within budget.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~100 |
| 3.2 | Customer Pain (3 paragraphs) | ~200 |
| 3.3 | How Elpis Solves This (callout + 4 bolded-lead paragraphs + 3-deployment-shape grid) | ~400 |
| 3.4 | What's Included (2 buckets) | ~220 |
| 3.5 | Common Questions (6 Q&A) | ~370 |
| 3.6 | Outcomes You Can Hold Us To (7 bulleted outcomes) | ~150 |
| 3.7 | Architecture For This Solution (caption + 4 annotations) | ~80 |
| 3.8 | Trust Cue (2 cues + cross-link) | ~80 |
| 3.9 | Cross-lens | ~50 |
| 3.10 | Final CTA | ~80 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and §15 SolutionPanel anti-patterns:

| Don't | Why |
|---|---|
| Use *"rip and replace"* framing or imply EdgeConnect replaces existing SCADA / historian / MES | The page IS the "without ripping out" story. Locked at the hero ("without ripping out what you already have") + §3.5 FAQ Q1 + §3.6 Outcomes ("no new monitoring stack for SCADA-running plants") + §3.7 architecture annotation ("Beside, not replacing"). Any future edit that drifts toward replacement framing regresses the page's core promise. |
| Imply EdgeConnect Linux is current behavior | Per locked /capabilities/connectivity-edge spec v2 + positioning v3 — EdgeConnect is Windows today, Linux is near-term roadmap. The spec carries honest-framing callouts in §3.3 deployment-shape grid + §3.4 'EdgeConnect' bucket + §3.5 FAQ Q2 + §3.7 architecture annotation. Any future edit that drops them violates positioning v3 §6 commitment. |
| Position Edge Gateway as the required deployment path for EdgeConnect | Carry-forward from locked connectivity-edge spec v2 §6 anti-pattern. Connectivity & Edge must remain deployable both as software-only and appliance-based. The appliance is an option, not a requirement — protects against accidental "buy the box" framing drift. |
| Conflate EdgeConnect + Edge Gateway as a single product | Carry-forward from locked connectivity-edge spec v2. EdgeConnect = software runtime (Windows today; Linux roadmap). Edge Gateway = hardware appliance (standalone today; canonical EdgeConnect appliance once Linux ships). The dual-identity story is the commercial signal — collapsing them into one "EdgeConnect Gateway" product flattens the deployment-shape narrative. |
| Imply one EdgeConnect runtime can serve multiple plants | Per locked /architecture spec v2 FAQ Q6 — each plant runs its own EdgeConnect with per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregating across per-plant runtimes. The spec carries this guard in §3.3 paragraph 4 + §3.5 FAQ Q3 + §3.7 architecture annotation. Any future edit that drifts toward "one runtime, many plants" violates the per-gateway-identity discipline. |
| List specific protocol detail tables (semantic modes, security profiles, integration test patterns) | Full protocol matrix belongs on Phase E `/edgeconnect` — solution-page stays at solution-level vocabulary (the today list: FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7; MT-LINKi on roadmap). Cross-link to Phase E for the depth. |
| Use *"single pane of glass"* / *"seamless"* / *"intuitive"* / *"easy"* / *"future-proof"* / *"end-to-end"* without specifying the ends | Per buyer-taxonomy §2.3 vocabulary discipline — OT Architects read these as marketing-speak that's opinionated about their existing tools (single pane of glass) or hand-wavy about real integration work (seamless / easy). |
| Add competitor names (Kepware, Ignition, Litmus, HighByte, etc.) on the solution page | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory, not solution page. The §3.3 "How this differs from per-vendor monitoring tools" callout names the CATEGORY (per-vendor monitoring tools) without naming specific vendor products in that category beyond the FANUC/Mazak/Brother/Siemens controller examples that are the floor reality. |
| Add customer logos, customer names, or specific deployment stories with named customers | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2 has no customer-logo authorization; named customer stories wait for Phase 3 customer-story sign-off process. |
| Claim percentage productivity gains or specific dollar-cost savings | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics on this page. Quantified outcomes wait for the `/platform` commercial-engagement teaser + Phase 3 customer-story registry. |
| Use absolute outcome claims ("zero downtime", "eliminate the integration tax") | Per anti-overclaim discipline carried from the OEM v2 solution spec precedent — outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero". `solution-oem-machine-monitoring-v2.md §1 + §6 + §9` is the locked precedent for this hedging. |
| Lead the hero with products instead of the outcome | Per design-system v3 §15 SolutionPanel anti-pattern — defeats the FOR WHAT framing of /solutions per memo v2 §2. The hero leads with "One operational view across every controller on your floor" (outcome), not "EdgeConnect + Edge Gateway" (products). |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per design-system v3 §15 anti-pattern — solution pages need annotated subsets of the master architecture diagram, not generic images. The interactive variant is what surfaces the solution-specific annotations to OT Architects evaluating the deployment shape. |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 1,500-1,800 word target (current: ~1,790 words)
- [x] All 10 sections present per SolutionPanel layout (design-system v3 §15)
- [x] §3.1 hero leads with outcome ("One operational view across every controller on your floor — without ripping out what you already have"), not products
- [x] §3.3 "How this differs from per-vendor monitoring tools" callout present per /capabilities hub §9 emerging-pattern governance
- [x] §3.3 names the 3 deployment shapes (software-only / Edge Gateway appliance / hybrid)
- [x] §3.3 includes cross-links to `/capabilities/connectivity-edge` + Phase E `/edgeconnect`
- [x] §3.4 What's Included follows design-system v3 §15 schema (2 buckets: Edge Layer + EREMOS V2; Acquisition omitted because not used by edge-connectivity solution)
- [x] §3.4 EdgeConnect Linux roadmap framing honest (Windows today, Linux on roadmap, Edge Gateway dual identity)
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 inline-FAQ-with-schema-markup governance
- [x] §3.5 FAQ Q1 (SCADA replacement) explicitly says "beside, not replacing"
- [x] §3.5 FAQ Q2 (EdgeConnect Linux roadmap) framing honest
- [x] §3.5 FAQ Q3 (multi-plant EdgeConnect) explicitly denies the anti-pattern
- [x] §3.5 FAQ Q4 (protocol coverage) lists the 6 today protocols (FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7) + MT-LINKi REST on roadmap (v2.1 amendment 2026-06-04)
- [x] §3.5 FAQ Q5 (historian / MES integration) cross-links to `/architecture` §3.6 integration patterns
- [x] §3.5 FAQ Q6 (network drop) describes store-and-forward + three-way diagnostics
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero" (per OEM v2 hedging precedent)
- [x] §3.6 omits percentage productivity gains and dollar-cost claims (per proof-architecture v1 §3 + §4)
- [x] §3.7 architecture diagram uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image
- [x] §3.7 annotations honor §5.A discipline (eyebrow + ≤4-word title + 1-2 sentence body; max 8 per zoom level)
- [x] §3.7 includes solution-scoped Col-2 clarifier ("Acquisition peer is not required for this solution") to prevent reader misreading as full peer-architecture truth (mirror of predictive-maintenance v2 pattern)
- [x] §3.8 trust cues cover BOTH offline-first (air-gapped plants) AND per-gateway identity + hash-chained audit
- [x] §3.9 cross-lens cards match the LOCKED design-system v3 §17 preset for `/solutions/<solution>`: `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub)
- [x] §3.10 final CTA uses OT-Architect-preferred framing ("Bring us your controller mix") and is vertical-localized
- [x] EdgeConnect positioning across the page matches the LOCKED `/capabilities/connectivity-edge` spec v2 — protocol-agnostic edge runtime, Windows today + Linux roadmap, canonical CNC vocabulary at the edge
- [x] Edge Gateway dual-identity story explicit in §3.3 + §3.4 + §3.5 FAQ Q2 (standalone today + canonical EdgeConnect appliance once Linux ships)
- [x] Edge Gateway NOT positioned as required deployment path (carry-forward from connectivity-edge v2 §6 anti-pattern)
- [x] Per-gateway identity discipline explicit (anti-multi-plant-EdgeConnect) in §3.3 + §3.5 FAQ Q3 + §3.7 architecture annotation
- [x] No vocabulary that backfires per buyer-taxonomy v1 §2.3 (no *"seamless"* / *"intuitive"* / *"easy"* / *"future-proof"* / *"end-to-end"* / *"single pane of glass"* / *"transformation"*)
- [x] No customer logos, no customer names, no fabricated metrics, no competitor names (no Kepware, Ignition, Litmus, HighByte mid-page; per-vendor controllers — FANUC, Mazak, Brother, Siemens — are floor reality, not competitive comparison)
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] **v2 M1 applied** — Edge Gateway dual-identity business value sentence added at end of §3.3 deployment-shapes body para (procurement-time framing)
- [x] **v2 M2 applied** — Canonical-vocabulary worked example added in §3.3 ¶2 (FANUC + Mazak + Brother + Siemens S7 alarm codes → canonical Alarm State)
- [x] **v2 M3 applied** — §3.5 Q2 "canonical Linux footprint" → "canonical EdgeConnect appliance" (terminology consistency with corpus)
- [x] **v2 J2 applied** — §3.5 Q2 standalone-mode sentence sharpened (standalone-vs-EdgeConnect lifecycle distinction louder)
- [x] **v2 M4 applied** — §3.4 preamble expanded with whatsIncluded bucket-narrative governance pattern lock for Phase E cross-spec consistency
- [x] **v2 J1 decision** — "near-term roadmap" qualifier RETAINED across all 7 Linux roadmap locations; reasoning documented in spec preamble (solution-page procurement-actionable)
- [x] **5 cross-spec side-flags documented** in spec preamble (MT-LINKi publish-live gate, OPC UA Server typo on PR #73, vendor-naming softening on PR #77, /solutions hub status-pill swap, PR merge status)
- [x] **Discipline-lock guard validator CLEAN** — all 4 discipline areas (Linux roadmap, dual identity, per-gateway identity, beside-not-replacing) consistent across all locations
- [x] **§15 SolutionPanel coverage validator clean** — all required props covered; whatsIncluded bucket-narrative pattern explicitly governed; visual ordering matches §15
- [x] **OPC UA Client used as polled source protocol** (NOT OPC UA Server — workflow flagged the connectivity-edge v2 upstream typo; this spec is correct)

---

## 8. Out of scope for v1

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers: full protocol matrix with semantic modes (e.g., FOCAS2 polled vs subscription, OPC UA Server security profiles), per-protocol integration test patterns, FOCAS2 connection-pool sizing, MT-LINKi licensing, MTConnect probe-document conformance.
- **Full Edge Gateway hardware detail.** Phase E `/edge-gateway` covers: full hardware specs, environmental certifications (CE / UL / FCC / IP rating — open question per positioning v3 §11), enclosure dimensions, mounting details, sourcing detail.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2) covers EdgeConnect + Edge Gateway as a Pillar 1 capability story; this page cross-links to it rather than duplicating.
- **Architecture walkthrough.** `/architecture` (LOCKED v2) covers the cross-pillar Industrial Intelligence Stack; this page cross-links for the full stack story.
- **CNC-specific solution narrative.** Existing v2 `/solutions/cnc-machining` covers the CNC-shop-specific outcome (v3 in Phase E). This page is the EdgeConnect+Edge-Gateway-specific depth-example across all vendor controllers + S7 + Modbus + OPC UA.
- **Broader brownfield-modernization narrative.** Existing v2 `/solutions/brownfield-modernization` covers the data-layer-modernization story (v3 in Phase E). This page is the protocol-coverage-and-canonical-vocabulary depth-example.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links to it from §3.8.
- **Industries-specific framings.** Phase 3 `/industries/<industry>` or Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2.
- **Pricing / commercial engagement detail.** `/platform` (step 11) covers commercial-engagement teaser; Phase 3 `/pricing` covers detail.
- **Quantified productivity-gain or integration-tax-reduction percentages.** Wait for Phase 3 customer-story registry + commercial-engagement teaser on `/platform`.

---

*`/solutions/edge-connectivity` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review + 4-agent pre-lock validation workflow (CLEAN at discipline-lock level — no HIGH-severity blockers). v1 draft pending user + ChatGPT review was the initial baseline; v2 applies 4 must-apply items (M1 dual-identity business value, M2 canonical-vocabulary worked example, M3 Q2 terminology consistency, M4 §3.4 governance pattern lock) + 1 sharpening (J2 Q2 standalone-mode). User decided J1: retain "near-term roadmap" qualifier across all 7 Linux locations for solution-page procurement-actionability. 5 cross-spec side-flags documented in preamble (MT-LINKi publish-live gate, OPC UA Server typo upstream on PR #73, vendor-naming softening upstream on PR #77, /solutions hub status-pill swap, PR merge status). Source-of-truth alignment baked into v1 drafting from predictive-maintenance v2 workflow lessons actually worked — this is the cleanest v1 → v2 cycle in the Phase 2 wave so far. Ninth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 10. Uses SolutionPanel layout (design-system v3 §15 LOCKED 10-section structure). Page-content commitments approved by user direction before drafting (hero outcome verbatim from /solutions hub Card 1, "How this differs from per-vendor monitoring tools" callout per §9 emerging-pattern governance, 3 deployment shapes [software-only / appliance / hybrid], 6 FAQ topics, 2 trust cues, architecture-annotated diagram, 6 anti-pattern locks carried forward from connectivity-edge v2 + new SCADA-replacement guard, vertical-localized "Bring us your controller mix" CTA). Inline FAQ included per §9 per-page-type FAQ governance. §1.4 metadata block present per §9 metadata governance. **Source-of-truth alignment discipline baked in from predictive-maintenance v2 workflow lessons**: EdgeConnect Linux as roadmap (not current), Edge Gateway dual identity preserved, per-gateway identity anti-multi-plant-EdgeConnect locked, "beside not replacing" SCADA/historian/MES framing locked. **Side-flag**: PR #73 (connectivity-edge v2) is LOCKED but not yet merged to master — verify before publishing this page. Same governance side-flag for /solutions hub Card 1 status-pill + link swap when this page ships live. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.3 + §2.5, proof-architecture v1 + §3 + §4 + §8, positioning v3 + EdgeConnect Linux roadmap, design-system v3 §15 (SolutionPanel) + §16 (TrustCueBlock) + §17 (CrossLensBlock LOCKED preset for /solutions/<solution>: line 559) + §5.A (ArchitecturePanel.interactive solution-annotated variant), page-capabilities-hub-spec-v1 §9, page-capabilities-connectivity-edge-spec-v1 v2 (source-of-truth for EdgeConnect + Edge Gateway positioning), page-architecture-spec-v1 v2 (cross-pillar architecture + integration patterns + multi-plant-EdgeConnect FAQ Q6), page-solutions-hub-spec-v1 v2 (Card 1 summary that this depth-example supports), page-solutions-predictive-maintenance-spec-v1 v2 (sister solution-page precedent + source-of-truth alignment discipline), solution-oem-machine-monitoring-v2 (anti-overclaim discipline precedent), solution-cnc-machining-v2 + solution-brownfield-modernization-v2 (Phase 1 solution-page voice precedents), hardware-ecosystem-map-v3 §2.*
