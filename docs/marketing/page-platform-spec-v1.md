<!--
File:        docs/marketing/page-platform-spec-v1.md
Purpose:     Page spec for /platform — vendor-worldview synthesis page.
             Final (10th) Phase 2 per-page spec. Written last per
             amendment v3 §6 sequencing because it cross-references
             every other Phase 2 surface. Includes the commercial-
             engagement teaser per amendment v3 §1.7 + inline FAQ per
             /capabilities hub §9 governance.
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim text), user + ChatGPT
             (reviewers).
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   ALL 9 prior Phase 2 specs cross-referenced. Specifically:
             page-capabilities-hub-spec-v1.md (LOCKED v1)
             page-capabilities-connectivity-edge-spec-v1.md (LOCKED v2)
             page-capabilities-data-acquisition-spec-v1.md (LOCKED v2)
             page-capabilities-asset-intelligence-spec-v1.md (LOCKED v2)
             page-capabilities-condition-monitoring-spec-v1.md (LOCKED v1.1)
             page-capabilities-operational-intelligence-spec-v1.md (LOCKED v1)
             page-architecture-spec-v1.md (LOCKED v2)
             page-solutions-hub-spec-v1.md (LOCKED v2)
             page-solutions-predictive-maintenance-spec-v1.md (LOCKED v2)
             page-solutions-edge-connectivity-spec-v1.md (LOCKED v2)

             Foundation docs:
             phase2-ia-scope-memo-v2.md §3 + amendment v3 §1.7 (the
                commercial-engagement teaser locked decision)
             buyer-taxonomy-v1.md §2.1 (CTO/CIO primary) + §2.3 (OT
                Architect secondary) + §2.7 (Procurement / compliance
                reviewer tertiary via FAQ)
             proof-architecture-v1.md (proof discipline — locked
                trust-anchor phrasings + customer-anonymity rules)
             design-system-v3.md §16 (TrustCueBlock), §17 (CrossLensBlock
                LOCKED preset for /platform: /capabilities + /architecture
                + /solutions per line 554)
             industrial-intelligence-ecosystem-positioning-v3.md §4
                (LOCKED trust-anchor phrasings — "Deployed in defense
                and space-agency programs" + "Operating across India
                and the Middle East") + §5 (Industrial Intelligence
                Stack narrative phrase)
             positioning-amendment-v4.md §3 (customer-name unlock with
                defense/space-agency exclusion) + §5 (AMC partner
                channel remains anonymized: "Maintenance and AMC
                providers across India and the Middle East")
             hardware-ecosystem-map-v3.md (5-pillar canonical model
                source-of-truth)
Version:     v2.1 — LOCKED. Original v2 locked 2026-05-29 after Pass 1
                  ChatGPT review ("strongest strategic page in the
                  entire website architecture") + 4-agent pre-lock
                  validation workflow (caught 2 HIGH-severity blocking
                  issues: FAQ Q5 OPC UA Server misattribution + hero
                  subhead locked-anchor capitalization drift; all
                  resolved; discipline-lock guard CLEAN across 11
                  areas). v2.1 amendment 2026-06-04 dropped MT-LINKi
                  from §3.3 Pillar 1 today-list per platform-team
                  direction (no customer demand; engineering deferred
                  to low priority); MT-LINKi REST integration moved to
                  roadmap mention. Side-flag #1 (MT-LINKi publish-live
                  gate) now RESOLVED via this amendment; §6 anti-pattern
                  row updated from "verification gate" to "RESOLVED"
                  with re-add governance ("future edits must NOT re-add
                  MT-LINKi to the today protocol list until engineering
                  milestone ships").
Date:        2026-05-29 (v2 lock) / 2026-06-04 (v2.1 MT-LINKi amendment)
Status:      LOCKED.

Tenth (FINAL) per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 11. Vendor-worldview synthesis — different page
structure from CapabilityDeepDive (5 pillar pages) and SolutionPanel
(2 new + 5 existing solution pages); has its own 9-section structure
built around vendor identity, commercial engagement, trust posture,
and platform-level FAQ.

Page-structure approval: 9-section structure approved by user
direction 2026-05-29 before drafting. Hero → Why Elpis exists → 5-
pillar synthesis → Commercial engagement → Trust posture summary →
Where we operate + trust anchors → Inline FAQ → Cross-lens → Final
CTA.

Word-count target: 1,500-2,000 words per /capabilities hub §9
page-type guidance for /platform. Current draft: ~1,810 words.

§1.4 Page metadata block included per /capabilities hub §9 metadata
governance lock (PR #71).

Inline FAQ included per /capabilities hub §9 per-page-type FAQ
governance (/platform = YES — CTOs and procurement ask predictable
platform-level questions: commercial model, deployment philosophy,
competitive framing — competitive specifically EXCLUDED from FAQ per
proof-architecture v1 §8).

LOCKED TRUST-ANCHOR PHRASINGS (verified verbatim from positioning v3
§4 + positioning-amendment-v4 §3 + §5; NO paraphrasing variations
per user-memory rule about locked anchors):
  - "Deployed in defense and space-agency programs" (positioning v3
    §4 — anonymous external-facing framing)
  - "Operating across India and the Middle East" (positioning v3 §4
    — current deployment footprint)
  - "Maintenance and AMC providers across India and the Middle East"
    (positioning-amendment-v4 §5 — partner-channel anonymized framing)

Commercial-engagement scope (per amendment v3 §1.7):
  - IN SCOPE: modular deployment model, edge + cloud combinations,
    OEM / integrator engagement, AMC / service support, phased rollout
  - OUT OF SCOPE (defers to Phase 3 /pricing): SKU grids, per-tag
    pricing, subscription tables, detailed module-level pricing

Source-of-truth alignment discipline (lessons from predictive-
maintenance v2 + edge-connectivity v2 workflow):
  - EdgeConnect Linux on roadmap (NOT current — use parent-spec
    phrasing "on the roadmap" or "near-term roadmap" consistently
    per the /solutions/edge-connectivity v2 J1 decision)
  - E-IDOS streaming to EREMOS V2 is near-term roadmap (NOT current
    behavior) per positioning v3 §6 commitment #3
  - mDAQ runs VAS only (E-IDOS is standalone sensor-agnostic appliance)
  - Per-gateway identity discipline (anti-multi-plant-EdgeConnect)
  - "Beside, not replacing" for SCADA/historian/MES/CMMS
  - MT-LINKi protocol-availability is platform-team verification gate
    (carry-forward side-flag from edge-connectivity v2 #1)
  - 5-pillar canonical model (NOT 4, NOT 6) per positioning v3

Cross-spec governance side-flags carried forward:
  - MT-LINKi publish-live gate (4 marketing specs claim operator-
    available; CLAUDE.md §8 disagrees)
  - OPC UA Server → OPC UA Client typo upstream on PR #73
  - PI/Wonderware/Aveva named-vendor softening upstream on PR #77
  - /solutions hub Card 1 + Card 2 status-pill swaps when those
    solution pages ship live
  - PR merge status verification (PR #72 + PR #73 + others before
    publish)

v1.5 → v2 changes (workflow-driven, applied 2026-05-29):

BLOCKING fixes (workflow HIGH-severity findings, both resolved):
  - HIGH-1 (§3.7 FAQ Q5): publish-surface attribution corrected.
    v1.5 misattributed OPC UA Server to EREMOS V2. v2 splits correctly:
    "EdgeConnect publishes to MQTT and exposes signals via OPC UA Server
    (your existing SCADA can subscribe to either). EREMOS V2 exposes
    OEE rollups, alarms, and reports via REST API, MQTT, and webhook
    integrations." Per /architecture v2 §3.6 Pattern A + shared-knowledge
    /contracts/opcua-namespace-policy.md + operational-intelligence
    v1 §8.
  - HIGH-2 (§3.1 hero subhead): locked-anchor capitalization restored.
    v1.5 concatenated both anchors after em-dash with lowercased opening
    words. v2 restructures so each locked anchor opens its own sentence
    with locked capitalization preserved verbatim: "Hardware and
    software designed to grow together. Operating across India and the
    Middle East. Deployed in defense and space-agency programs."
    Per positioning v3 §4 lines 86-87 + user-memory rule.

Additional must-apply fixes:
  - MEDIUM-3 (§3.7 FAQ Q6): "(anonymized customers)" parenthetical
    moved to a separate sentence so Anchor 2 phrase remains sentence-
    pure.
  - MEDIUM-4 (§7 sign-off checklist line 483): capitalization
    reconciled with the locked verbatim form.
  - LOW-5 (§3.6 governance cue typo): "reworords" → "rewords".

Judgement calls (workflow-recommended, applied):
  - J1 (§3.3 Pillar 4 VAS equipment grouping): restored verbatim
    condition-monitoring v1.1 grouping ("rotating machinery (pumps,
    motors, gearboxes, fans, compressors), conveyors, and structural
    components"). v1.5 had reordered "gearboxes" as a sibling category;
    v2 restores it as a sub-item per the LOCKED v1.1 taxonomic grouping.
  - J2 (FAQ Q5 cross-link): kept generic /architecture (consistent
    with spec-wide cross-link pattern; anchor-deep-link deferred).

Discipline-lock guard verdict (workflow): CLEAN across all 11 areas
(9 prior locked discipline areas + 2 v1.5 new additions). No HIGH-
severity drift; no factual contradictions with the locked corpus.

Pre-lock workflow verdict: "After must_apply items are resolved, the
spec is READY TO LOCK with high confidence — no foundational
architecture or discipline gaps remain, only verbatim-anchor and
source-of-truth precision fixes on a synthesis page that procurement
will cross-check against /architecture."
-->

# `/platform` — Page Spec v1

**Vendor-worldview synthesis page. Final (10th) Phase 2 per-page spec. Reader lands here when they want to understand WHO Elpis is, WHY the platform exists, WHAT it actually is at platform scale, HOW we engage commercially, and where we operate today. CTO / CIO primary audience for vendor evaluation; OT Architect secondary; Procurement / compliance reviewer tertiary via inline FAQ.**

This is the page where CTOs land after they've seen the capability story (`/capabilities`), the architecture story (`/architecture`), or one of the solution narratives (`/solutions/<solution>`), and want to evaluate the vendor before scoping an engagement. It is **not** a capability page (those are `/capabilities/<pillar>` × 5). It is **not** an outcome page (those are `/solutions/<solution>`). It is **not** the architecture walkthrough (`/architecture`). It is the **vendor lens** — why Elpis exists, what we stand for, and how we engage.

Target length: **1,500-2,000 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/platform`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Vendor-worldview synthesis. Reader leaves with *"I now understand who Elpis is as a vendor, why the platform exists, what it covers at the platform level, how engagement typically scopes, what trust posture applies, where they operate today, and what questions I'd otherwise raise on a procurement-evaluation call."*

**IS NOT:**
- A capability page (`/capabilities` hub + 5 pillar deep-dives cover the capability story; LOCKED)
- An outcome / solution narrative (`/solutions` hub + 2 new Phase 2 + 5 existing v2 cover the outcome story; partially LOCKED)
- The architecture walkthrough (`/architecture` covers cross-pillar composition; LOCKED v2)
- A pricing page (Phase 3 `/pricing` covers detailed pricing — per amendment v3 §1.7, this page carries the commercial-engagement teaser only)
- A customer-stories page (Phase 3 customer-story registry covers named deployments — per positioning v3 §4 + amendment v4)
- A partner-recruitment page (Phase 4 partner portal covers OEM / AMC / integrator partner programs — this page names AMC channel as a deployment shape, not as a partner program)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** CTO / CIO (§2.1)
- Lands here from the homepage primary nav, from `/capabilities` or `/solutions` hub via cross-lens, or from a Google search for *"Elpis Industrial Intelligence platform"* / *"industrial OT platform vendor"*
- Wants: vendor identity, commercial confidence, deployment philosophy, trust posture summary, predictable engagement shape
- CTA preference: *"Request an architecture review"* > *"Talk to us about scoping"* (NOT *"Book a demo"* — CTOs read demo-booking as marketing-pipeline rather than substantive evaluation)
- Vocabulary that lands: protocol-agnostic, modular per pillar, edge + cloud, offline-first, per-tenant isolation, hash-chained audit, customer-controlled routing, AMC channel, brownfield, multi-site, Industrial Intelligence Stack
- Vocabulary that backfires: *"easy"*, *"seamless"*, *"intuitive"*, *"future-proof"*, *"transformation"*, *"AI-powered"*, *"single pane of glass"*, *"end-to-end"* without specifying the ends, *"all-in-one"*

**Secondary buyer:** OT Architect / SCADA engineer (§2.3) — light weight on this page
- Lands here when scoping a vendor evaluation alongside the architecture review
- Wants: platform-level commitment confidence, architectural philosophy, integration honesty
- Primary surface for OT Architect remains `/architecture` (LOCKED v2)

**Tertiary buyer (via inline FAQ):** Procurement / compliance reviewer (§2.7)
- Lands here when validating a vendor for procurement-policy review
- Wants: predictable commercial model, deployment philosophy, what stays customer-owned, audit posture
- The §3.7 inline FAQ is calibrated to surface these answers

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Platform — Industrial Intelligence Ecosystem · Elpis* |
| **Meta description** (140-160 chars) | *Modular Industrial Intelligence platform — protocol-agnostic edge runtime, canonical vocabulary, deployable per plant. India + Middle East. Defense and space-agency programs.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/platform` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.7 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities`, `/architecture`, `/solutions`, `/security`, and 5 `/capabilities/<pillar>` pages use `relatedLink`. Trust-anchor mentions ("defense and space-agency programs") DO NOT include schema markup at this lock — Phase 3 customer-story registry handles structured proof. |

---

## 2. Page structure — sections at a glance

NOT a `CapabilityDeepDive` or `SolutionPanel`. Own 9-section structure for the vendor-worldview synthesis.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + CTAs) | `dark-deep` | `SectionShell` + `Button` × 2 | ~120 |
| **2** | Why Elpis exists — operations-team perspective | `light` | Narrative paragraphs (2-3) | ~250 |
| **3** | What the platform actually is — 5-pillar synthesis | `light-tinted` | Per-pillar paragraph + cross-link to `/capabilities/<pillar>` | ~300 |
| **4** | How we engage commercially | `light` | Sub-grid of 4 engagement-shape cards + commercial discipline note | ~250 |
| **5** | Trust posture summary | `light-tinted` | 3-bullet summary + `/security` cross-link | ~150 |
| **6** | Where we operate + trust anchors | `light` | Geography + locked anchor phrasings + AMC channel framing | ~200 |
| **7** | Common questions (inline FAQ, CTO/procurement-calibrated) | `light` | 6 Q&A pairs with `FAQPage` schema markup | ~350 |
| **8** | Cross-lens navigation | `light-tinted` | §17 cross-lens content pattern (LOCKED preset for /platform: line 554) | ~50 |
| **9** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> THE PLATFORM
>
> HEADLINE (size.3xl semibold):
> The Industrial Intelligence Ecosystem — one platform for the data path between your factory floor and your operations team.
>
> SUBHEAD (size.lg, max-width 60ch):
> Protocol-agnostic edge runtime. Five capability pillars that deploy independently or together. Hardware and software designed to grow together. Operating across India and the Middle East. Deployed in defense and space-agency programs.
>
> PRIMARY CTA (`Button.primary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Talk to us about scoping
> HREF: `/contact?intent=platform-scoping`

**Anti-patterns:** No *"all-in-one"* framing (defeats the modular-per-pillar story). No *"transformation"* / *"AI-powered"* / *"future-proof"* generic-marketing vocabulary. No *"Book a demo"* CTA (per buyer-taxonomy §2.1 CTO discipline — demo-booking reads as marketing-pipeline rather than substantive evaluation). No outcome metric in headline.

---

### 3.2 Section 2 — Why Elpis exists

> EYEBROW: WHY ELPIS EXISTS
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Industrial OT teams want one operational view across every controller on their floor. They want canonical signals at every downstream system. They want predictive maintenance that actually triggers on real condition signatures, not on calendar schedules. They want multi-site visibility without losing per-plant operability. And they want all of this without ripping out the SCADA, historian, MES, and CMMS infrastructure that already works.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> What they usually get instead is a stack of per-vendor monitoring tools, per-protocol custom scripts that break on firmware updates, integration platforms that are opinionated about the operations team's existing systems, and "AI-powered" dashboards that don't survive contact with real production reality. The promise of "Industry 4.0" gets diluted by tooling that doesn't match how industrial operations actually work.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> Elpis was built differently. One protocol-agnostic edge runtime that speaks every controller's native protocol. Canonical normalization at the edge so every downstream system reads the same vocabulary. Hardware that handles the cases controllers don't cover. An analytics platform that models the real industrial hierarchy. Modular per pillar — pick what fits your floor, scale on your terms. Beside the systems you already run, not replacing them.

> PLATFORM PRINCIPLES CALLOUT (size.base, visual treatment: bordered card or left-rule callout — sits at end of §3.2, surfaces the principles already embedded in the platform's design):
>
> > **Platform principles.** Five operating commitments that shape every architectural and product decision Elpis makes:
> >
> > - **Customer owns operational data.** Every signal, every workflow, every audit record belongs to the customer's tenant. Routing decisions stay customer-controlled even when OEMs or AMC providers are in the loop.
> > - **Offline-first before cloud.** EdgeConnect and EREMOS V2 run offline by default. Cloud connectivity is opt-in, not required. Air-gapped plants are first-class deployments.
> > - **Modular before monolithic.** Pillars deploy independently. Sites scale on their terms — no forced bundling, no all-or-nothing engagements.
> > - **Integrate before replace.** SCADA, historian, MES, and CMMS stay where they are. Elpis adds the cross-vendor canonical layer; the existing systems consume canonical signals instead of vendor-specific ones.
> > - **Per-plant identity before fleet abstraction.** Each plant runs its own EdgeConnect runtime with per-gateway identity. Multi-site visibility comes from EREMOS V2 aggregating across per-plant runtimes — never from a single multi-plant runtime.

---

### 3.3 Section 3 — What the platform actually is

> EYEBROW: WHAT THE PLATFORM ACTUALLY IS
>
> SECTION TITLE:
> Five capability pillars composing one Industrial Intelligence Ecosystem.

**5-pillar synthesis (one paragraph per pillar, ~50 words each, with cross-link to the LOCKED `/capabilities/<pillar>` deep-dive). Pillars in canonical order:**

#### Pillar 1 — Connectivity & Edge

> EdgeConnect (the protocol-agnostic edge runtime, Windows today + Linux on the roadmap) + Edge Gateway (ruggedized appliance, dual identity — standalone today, canonical EdgeConnect appliance once Linux ships). Polls existing controllers in their native protocols (FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7). FANUC MT-LINKi REST integration on the roadmap. Canonical vocabulary at the edge. **See the deep-dive → `/capabilities/connectivity-edge`.**

#### Pillar 2 — Data Acquisition

> mDAQ — ruggedized acquisition hardware for direct sensor reads where no PLC exists, where the PLC is locked, or where adding a PLC layer would be more expensive than acquiring the signal directly. Greenfield installs, PLC-bypass retrofits, remote and unmanned sites. **See the deep-dive → `/capabilities/data-acquisition`.**

#### Pillar 3 — Asset Intelligence

> mTracker — utilization, location, and OEE telemetry on equipment that doesn't speak a controller protocol. Multi-site asset visibility for plant operators; OEM service-hours billing and warranty triggers for OEM machine builders. Customer-controlled routing for OEM-channel deployments. **See the deep-dive → `/capabilities/asset-intelligence`.**

#### Pillar 4 — Condition Monitoring

> VAS (Vibration Analyser System runs on mDAQ — bearing issues, imbalance, misalignment, looseness, and cracks on rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, and structural components) + E-IDOS (standalone sensor-agnostic appliance for hydraulic and lubrication oil-health — particle contamination, water saturation, oil flow; ISO 4406 / NAS 1638). **See the deep-dive → `/capabilities/condition-monitoring`.**

#### Pillar 5 — Operational Intelligence

> EREMOS V2 — multi-tenant analytics platform. Models the real industrial hierarchy (PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT). Computes OEE via Segments against your OEE definition. Persistent alarms with incident workflows. Per-tenant isolation by design. **See the deep-dive → `/capabilities/operational-intelligence`.**

> SECTION FOOTER (size.base):
> **The pillars deploy independently and compose intentionally.** Each ships with its own commercial conversation; combinations are negotiated based on the actual deployment shape. The 4-column Industrial Intelligence Stack architecture shows how the pillars connect → `/architecture`.

---

### 3.4 Section 4 — How we engage commercially

> EYEBROW: HOW WE ENGAGE COMMERCIALLY
>
> SECTION TITLE:
> Commercial confidence without premature pricing.

**Sub-grid: 4 engagement shapes (one paragraph each)**

#### Modular per pillar

> Pillars deploy independently. A site can start with Connectivity & Edge (consolidate mixed-vendor monitoring), add Operational Intelligence (OEE + alarms + incident workflows), then scale into Condition Monitoring (predictive maintenance) when the maintenance program is ready. No "all-or-nothing" forced bundling. Each pillar carries its own commercial conversation; combinations are negotiated based on the actual deployment shape.

#### Edge + cloud, customer-controlled

> EdgeConnect and EREMOS V2 run offline-first by default. Cloud connectivity is **opt-in, not required**. Plants on isolated OT VLANs install and run the platform the same way as plants with internet access. When cloud is part of the deployment, the customer controls the routing — which signals route where, on what schedule, with what scope. Hybrid edge-plus-cloud combinations are common; the customer decides the boundary.

#### OEM / integrator engagement

> OEM machine builders embed mTracker in shipped equipment for service-hours billing, warranty triggers, and remote diagnostics — under customer-controlled routing where the OEM sees only what their customer authorizes. System integrators partner on multi-site standardization projects. Integrator engagements scope per project, not per long-term resale contract.

#### AMC / service support

> Maintenance and AMC providers across India and the Middle East deliver predictive maintenance and condition monitoring on customer floors using Elpis hardware (VAS for vibration, E-IDOS for oil-health) plus EREMOS V2 incident workflows. The AMC channel is an existing deployment reality, not a future partner program — customer-controlled signal routing makes AMC engagements scoped, auditable, and reversible per asset and per contract.

> OEM + AMC SYMMETRY NOTE (size.base, sits between the AMC card and the commercial discipline note):
> **The same platform supports both OEM and AMC business models without requiring separate stacks.** OEM machine builders embedding telemetry in shipped equipment and AMC providers delivering service on customer floors run against the same EdgeConnect runtime, the same EREMOS V2 incident workflows, and the same customer-controlled routing model. The deployment shapes differ; the platform doesn't fragment.

> COMMERCIAL DISCIPLINE NOTE (size.sm italic):
> *Commercial scope is established through architecture review and deployment scoping. Detailed pricing follows approved scope — per engagement, based on pillar combination, deployment footprint, and plant count. SKU grids, per-tag pricing, and subscription detail live on the Phase 3 `/pricing` page once the customer commercial baseline is established.*

---

### 3.5 Section 5 — Trust posture summary

> EYEBROW: TRUST POSTURE
>
> SECTION TITLE:
> Trust posture summary — full walkthrough at `/security`.

**3-bullet summary:**

> - **Offline-first by default, no phone-home.** EdgeConnect license validates locally. Plants on isolated OT VLANs install and run the same way as plants with internet. Cloud connectivity is opt-in.
> - **Hash-chained configuration audit.** Every change — protocol-driver enables, routing changes, threshold edits — captured with actor identity and timestamp. Tamper-evident, replay-ready, audit-survivable.
> - **Per-tenant isolation in EREMOS V2.** Customer-controlled routing for AMC channel, OEM channel, and multi-tenant analytics deployments. Each tenant sees only what's authorized to it.

> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.6 Section 6 — Where we operate

> EYEBROW: WHERE WE OPERATE
>
> SECTION TITLE:
> Today, on the ground.

**3-paragraph framing (locked phrasings from positioning v3 §4 + amendment v4 §5 — verbatim, no paraphrasing variations).**

> **⚠️ GOVERNANCE CUE (engineering + copy team note, NOT displayed on the page): The three phrasings in this section are IMMUTABLE.** They are the locked external-facing trust anchors per positioning v3 §4 + amendment v4 §3 + §5. Any future copy edit that rewords them ("we operate in India and the Middle East", "defense and aerospace deployments", "AMC partners in India", etc.) is a discipline-lock violation and must be rejected at code review. The exact phrasings used below are the only authorized versions until positioning v5 is locked.

> **Operating across India and the Middle East.** Current deployment footprint. Plants, multi-site operators, OEM machine builders, and AMC providers across both regions. International relevance without overclaiming "global."

> **Deployed in defense and space-agency programs.** Anonymous framing covering both space-agency rotating-equipment monitoring (VAS on precision rotating equipment) and defense-ministry oil-and-fluid condition-monitoring (E-IDOS via third-party supplier integration). The named customers stay confidential per the locked external-claim policy; the category descriptor is the proof point Elpis publishes.

> **Maintenance and AMC providers across India and the Middle East.** Active AMC partner channel — existing buyer reality, not a future partner program. Named partners arrive with the Phase 4 partner portal.

> COMMERCIAL CONTEXT NOTE (size.sm italic):
> *Named customer stories arrive in Phase 3 with explicit named-customer sign-off. Defense and space-agency customer names remain off-the-record per the locked external-claim policy.*

---

### 3.7 Section 7 — Common questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/platform` includes an inline FAQ with `FAQPage` schema markup for SEO. 6 questions calibrated to CTO / procurement / compliance-reviewer concerns. **Competitive framing intentionally excluded** per proof-architecture v1 §8 (sales-objection-guide territory, not /platform).

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What CTOs and procurement reviewers ask before scoping a vendor engagement.

#### Q1. What's the commercial model?

> Modular per pillar. Each pillar (Connectivity & Edge, Data Acquisition, Asset Intelligence, Condition Monitoring, Operational Intelligence) carries its own commercial conversation; combinations are negotiated based on actual deployment shape. Pricing scopes per engagement based on pillar combination, deployment footprint (software-only vs appliance, per-plant count), and plant geography. Architecture review and scoping happen first; pricing follows the scope.

#### Q2. How does engagement typically scope?

> Architecture review → scoping → phased rollout. Architecture review starts with the controller mix, sensor inventory, existing-systems boundary (SCADA / historian / MES / CMMS coexistence), and the per-plant deployment shape preference. Scoping translates the architecture into a concrete pillar combination + plant count + timeline. Phased rollout starts bottom-up by asset criticality — instrument the highest-criticality assets first, build threshold + workflow discipline there before scaling out.

#### Q3. Do you support OEM and AMC partner engagements?

> Yes. OEM machine builders embed mTracker for service-hours billing, warranty triggers, and remote diagnostics under customer-controlled routing. AMC providers deliver predictive maintenance and condition monitoring on customer floors using VAS + E-IDOS + EREMOS V2 incident workflows. Both engagement shapes use customer-controlled signal routing — the customer decides which signals route to which OEM or AMC for which assets. AMC is an existing buyer reality across India and the Middle East today, not a future channel program.

#### Q4. What's the deployment philosophy?

> Beside, not replacing. EdgeConnect and EREMOS V2 sit beside your SCADA, historian, MES, and CMMS — they don't take over operator HMIs, control logic, alarm acknowledgment, work-order management, or scheduling. Per-plant identity, not per-fleet — each plant runs its own EdgeConnect runtime with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregating across per-plant runtimes. Offline-first by default; cloud connectivity is opt-in.

#### Q5. What does this NOT do — where do our existing systems stay?

> Your SCADA stays where it is (operator HMIs, control logic, alarm acknowledgment workflows). Your historian stays where it is (long-term archive of record). Your MES stays where it is (work-order management, scheduling, labor tracking). Your CMMS stays where it is (maintenance system of record). EdgeConnect publishes to MQTT and exposes signals via OPC UA Server (your existing SCADA can subscribe to either). EREMOS V2 exposes OEE rollups, alarms, and reports via REST API, MQTT, and webhook integrations. Your existing systems consume canonical signals instead of vendor-specific ones. Full integration patterns → `/architecture`.

#### Q6. Where do you operate today and where do you ship?

> Operating across India and the Middle East. Plants, multi-site operators, OEM machine builders, and AMC providers across both regions. Deployed in defense and space-agency programs. Customer names remain anonymized per the locked external-claim policy. International relevance without overclaiming "global." Geography expansion happens per scoped engagement — when the customer mix and deployment scale justify it, not as a forced multi-region positioning claim.

---

### 3.8 Section 8 — Cross-lens navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/platform`** (design-system v3 §17 line 554): `/capabilities` + `/architecture` + `/solutions`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITIES | The 5 building blocks every solution composes from | `/capabilities` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | The outcomes built on the platform | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.1 CTO / CIO CTA preference. Platform-level CTA, not solution-specific.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your operations problem. We'll scope the platform fit.
>
> SUBHEAD:
> Whether you're evaluating Elpis for a single plant, scoping a multi-site standardization, or scoping an OEM / AMC partner engagement — bring us the operational scope and the existing-systems boundary. Architecture review runs against real protocols, real integration patterns, real floor topology — not slideware.
>
> PRIMARY CTA: Request an architecture review
> HREF: `/contact?intent=architecture-review`
>
> SECONDARY CTA: Talk to us about scoping
> HREF: `/contact?intent=platform-scoping`

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.9 final CTA |
| `CapabilityCard` (cross-lens variant) | §3.8 cross-lens |
| `CTASection` | §3.9 final CTA |
| §17 cross-lens content pattern | §3.8 cross-lens |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.7 common questions |

Page composition is /platform-specific (NOT CapabilityDeepDive or SolutionPanel). Engineering renders it as a custom Angular component drawing on shared `SectionShell` modes + standard primitives.

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.9. **~1,930 words total** (within 1,500-2,000 target for `/platform` page-type per `/capabilities` hub spec §9 page-type guidance). Increase from v1 (~1,810) reflects: v1.5 Platform Principles callout (+85 words), OEM/AMC symmetry note (+50 words), R2/R3 polish (+10 words net), v2 HIGH-1 FAQ Q5 expansion to split publish surfaces correctly (+25 words), v2 MEDIUM-3 FAQ Q6 qualifier restructuring (+5 words), J1 Pillar 4 VAS verbatim restoration (+5 words). All within 1,500-2,000 target.

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~120 |
| 3.2 | Why Elpis exists (3 paragraphs) | ~250 |
| 3.3 | 5-pillar synthesis (5 paragraphs + footer) | ~310 |
| 3.4 | Commercial engagement (4 cards + discipline note) | ~250 |
| 3.5 | Trust posture summary (3 bullets) | ~150 |
| 3.6 | Where we operate (3 paragraphs + commercial context note) | ~210 |
| 3.7 | Common questions (6 Q&A) | ~370 |
| 3.8 | Cross-lens | ~50 |
| 3.9 | Final CTA | ~100 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21:

| Don't | Why |
|---|---|
| Quote SKU pricing, per-tag pricing, or subscription detail | Per amendment v3 §1.7 — `/platform` carries the commercial-engagement teaser only. Detailed pricing belongs on Phase 3 `/pricing`. |
| Drift the locked trust-anchor phrasings (defense / space-agency / India + Middle East / AMC channel) | Per positioning v3 §4 + amendment v4 §3 + §5 — the EXACT phrasings are locked. "Deployed in defense and space-agency programs" / "Operating across India and the Middle East" / "Maintenance and AMC providers across India and the Middle East" are the verbatim anchors. Paraphrasing variations are forbidden per user-memory rule. |
| Name specific customers (defense, space-agency, AMC partners, anyone) | Per positioning v3 §4 + amendment v4 §3-§5 — Phase 2 has no named-customer authorization; named customer stories wait for Phase 3 customer-story sign-off; defense/space-agency customer names remain off-the-record per the locked external-claim policy even after Phase 3; AMC partner names arrive with Phase 4 partner portal. |
| Add competitor names or competitive framing | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory, NOT public /platform page. The §3.7 FAQ explicitly excludes a competitive-comparison question. |
| Claim percentage productivity / uptime / cost-savings metrics | Per proof-architecture v1 §3 + §4 — no fabricated outcome metrics on `/platform`. Quantified outcomes wait for Phase 3 customer-story registry. |
| Frame the platform as "all-in-one" | Defeats the modular-per-pillar story. The page IS the modular-pillar story; "all-in-one" reads as forced bundling and contradicts §3.4 commercial-engagement framing. |
| Imply Elpis replaces customer SCADA / historian / MES / CMMS | Carry-forward discipline lock from `/architecture` v2 + `/solutions/edge-connectivity` v2 + `/solutions/predictive-maintenance` v2. The page IS the beside-not-replacing story across §3.2 + §3.4 + §3.5 + §3.7 FAQ Q4 + §3.7 FAQ Q5. |
| Repeat full capability detail from `/capabilities/<pillar>` pages | Per §15 SolutionPanel and §9 hub-page discipline patterns — `/platform` summarizes + cross-links; it does not duplicate. Each pillar paragraph in §3.3 is ~50 words; the deep story lives at `/capabilities/<pillar>` (LOCKED). |
| Use *"AI-powered"* / *"transformation"* / *"future-proof"* / *"all-in-one"* / *"single pane of glass"* | Per buyer-taxonomy v1 §2.1 CTO/CIO vocabulary discipline — CTOs read these as marketing-pipeline rather than substantive evaluation, undermining vendor-evaluation credibility. |
| Use *"Book a demo"* CTA | Per buyer-taxonomy §2.1 + §2.3 — CTOs and OT Architects prefer architecture-review and scoping framings. "Book a demo" reads as low-substance funnel-pipeline, which is the wrong signal for vendor-evaluation reader. |
| Imply EdgeConnect Linux is current behavior, E-IDOS streams to EREMOS V2 today, or E-IDOS runs on mDAQ | Carry-forward discipline locks from `/capabilities/condition-monitoring` v1.1 + `/capabilities/operational-intelligence` v1 §6 anti-pattern + positioning v3 §6 commitment #3 + the /solutions/predictive-maintenance v2 workflow blocking-issue resolution. The /platform spec §3.3 Pillar 1 + Pillar 4 explicitly frame Linux as roadmap, E-IDOS as standalone-today, and mDAQ as VAS-only platform. |
| Imply one EdgeConnect runtime serves multiple plants | Carry-forward discipline lock from `/architecture` v2 FAQ Q6 + `/solutions/edge-connectivity` v2 + `/solutions/predictive-maintenance` v2. Per-plant identity is non-negotiable. §3.7 FAQ Q4 carries the discipline. |
| Drift the 5-pillar canonical model (NOT 4, NOT 6) | Per positioning v3 lock — the 5-pillar Industrial Intelligence Ecosystem is the canonical commercial framing. Future edits that reorganize into 4 or 6 pillars violate the locked positioning. |
| List MT-LINKi as operator-available today | RESOLVED 2026-06-04 via v2.1 amendments on 4 specs (this + connectivity-edge v2.2 + architecture v2.2 + /solutions/edge-connectivity v2.1). Platform team confirmed no customer demand; engineering deferred to low priority. MT-LINKi REST integration is on the roadmap, not in the today-list. **Future edits must NOT re-add MT-LINKi to the today protocol list until the engineering milestone ships (`Sources.MtLinki` modular adapter + Studio wizard + tests, per ARCHITECTURE_BLUEPRINT.md §791 deliverable). At ship-time, file v2.2 amendments to re-add MT-LINKi to the today-list.** |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 1,500-2,000 word target (current: ~1,810 words)
- [x] All 9 sections present per the approved /platform structure (§2)
- [x] §3.1 hero leads with vendor-worldview headline ("The Industrial Intelligence Ecosystem — one platform...")
- [x] §3.1 hero subhead includes BOTH locked trust anchors verbatim, each opening its own sentence with locked capitalization preserved: "Operating across India and the Middle East." + "Deployed in defense and space-agency programs."
- [x] §3.2 "Why Elpis exists" carries operations-team perspective + beside-not-replacing framing for SCADA/historian/MES/CMMS
- [x] §3.3 5-pillar synthesis names all 5 pillars in canonical order (Connectivity & Edge → Data Acquisition → Asset Intelligence → Condition Monitoring → Operational Intelligence)
- [x] §3.3 Pillar 1 paragraph names BOTH EdgeConnect (Windows today + Linux on roadmap) AND Edge Gateway (dual identity)
- [x] §3.3 Pillar 4 paragraph correctly distinguishes VAS (on mDAQ) from E-IDOS (standalone sensor-agnostic appliance — NOT mDAQ-based)
- [x] §3.3 each pillar paragraph includes cross-link to `/capabilities/<pillar>` deep-dive
- [x] §3.4 commercial-engagement scope stays within amendment v3 §1.7 boundaries (modular per pillar, edge + cloud, OEM/integrator, AMC; NO SKU grids, per-tag pricing, subscription detail)
- [x] §3.5 trust posture summary cross-links to `/security` (NOT duplicates the full walkthrough)
- [x] §3.6 uses LOCKED VERBATIM trust-anchor phrasings: "Operating across India and the Middle East" + "Deployed in defense and space-agency programs" + "Maintenance and AMC providers across India and the Middle East" (no paraphrasing variations)
- [x] §3.6 customer-anonymity discipline explicit — defense / space-agency customer names off-the-record; AMC partner names off-the-record until Phase 4
- [x] §3.7 inline FAQ uses `FAQPage` schema markup per §9 inline-FAQ-with-schema-markup governance
- [x] §3.7 FAQ has 6 questions calibrated to CTO / procurement / compliance reviewer
- [x] §3.7 FAQ explicitly EXCLUDES competitive-comparison question (per proof-architecture v1 §8)
- [x] §3.7 FAQ Q4 carries "beside, not replacing" + "per-plant identity, not per-fleet" + "offline-first by default" discipline
- [x] §3.7 FAQ Q5 cross-links to `/architecture` (NOT duplicates integration-patterns walkthrough)
- [x] §3.8 cross-lens matches LOCKED §17 preset for /platform (line 554): `/capabilities` + `/architecture` + `/solutions`
- [x] §3.9 final CTA uses CTO-preferred framings ("Request an architecture review" + "Talk to us about scoping") — NOT "Book a demo"
- [x] No vocabulary that backfires per buyer-taxonomy v1 §2.1 (no *"AI-powered"* / *"transformation"* / *"future-proof"* / *"all-in-one"* / *"single pane of glass"* / *"easy"* / *"seamless"*)
- [x] No customer logos, no customer names (including defense / space-agency / AMC partners), no fabricated metrics, no competitor names
- [x] No SKU pricing, per-tag pricing, subscription detail (Phase 3 `/pricing` deferred)
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/platform` is YES)
- [x] EdgeConnect Linux as roadmap (NOT current behavior) consistent across §3.3 Pillar 1 + §3.7 FAQ
- [x] E-IDOS streaming to EREMOS V2 NOT implied as current behavior (carry-forward discipline lock from condition-monitoring v1.1 + predictive-maintenance v2 + operational-intelligence v1 §6 anti-pattern)
- [x] Per-gateway identity / anti-multi-plant-EdgeConnect discipline explicit in §3.4 Modular per pillar + §3.7 FAQ Q4
- [x] **v2.1 amendment 2026-06-04** — MT-LINKi moved from §3.3 Pillar 1 today-list to roadmap mention per platform-team direction (no customer demand; engineering deferred to low priority). Original §6 anti-pattern + side-flag #1 resolved.
- [x] **v1.5 R2 applied** — §3.3 pillar synthesis footer sharpened ("deploy independently and compose intentionally")
- [x] **v1.5 R3 applied** — §3.4 commercial discipline note tone strengthened (more executive framing)
- [x] **v1.5 R4 applied** — §3.6 governance cue added (engineering/copy-team-facing immutability instruction)
- [x] **v1.5 A applied** — Platform Principles callout (5 principles) added at end of §3.2
- [x] **v1.5 B applied** — OEM/AMC symmetry note added in §3.4
- [x] **v2 HIGH-1 applied** — §3.7 FAQ Q5 publish surfaces split correctly between EdgeConnect (MQTT + OPC UA Server) and EREMOS V2 (REST API + MQTT + webhook) per /architecture v2 §3.6 Pattern A + shared-knowledge opcua-namespace-policy
- [x] **v2 HIGH-2 applied** — §3.1 hero subhead restructured so each locked anchor opens its own sentence with locked capitalization preserved verbatim
- [x] **v2 MEDIUM-3 applied** — §3.7 FAQ Q6 "(anonymized customers)" parenthetical moved into separate sentence to preserve Anchor 2 sentence boundary verbatim
- [x] **v2 MEDIUM-4 applied** — §7 checklist line 483 reconciled with locked verbatim form
- [x] **v2 LOW-5 applied** — §3.6 governance cue typo "reworords" → "rewords" fixed
- [x] **v2 J1 applied** — §3.3 Pillar 4 VAS equipment list restored to condition-monitoring v1.1 verbatim grouping ("rotating machinery (pumps, motors, gearboxes, fans, compressors), conveyors, and structural components")
- [x] **v2 J2 NO CHANGE** — FAQ Q5 cross-link kept as generic /architecture (consistent with spec-wide cross-link pattern)
- [x] **Discipline-lock guard workflow CLEAN** — all 11 areas validated (9 prior locked + 2 v1.5 additions)
- [x] **Cross-spec synthesis workflow validated** — Pillars 1-5 + FAQ Q4 + Q5 aligned with all LOCKED parent specs
- [x] **Locked-anchor verification workflow validated** — all 3 anchors VERBATIM across all locations (hero + §3.4 AMC card + §3.6 + FAQ Q6)

---

## 8. Out of scope for v1

- **Detailed pricing.** Phase 3 `/pricing` covers SKU grids, per-tag pricing, subscription tables, detailed module-level pricing. /platform carries the commercial-engagement teaser per amendment v3 §1.7 only.
- **Named customer stories.** Phase 3 customer-story registry covers named deployments with explicit customer-story sign-off (per positioning v3 §4 + amendment v4 §3-§5). Defense and space-agency customer names remain off-the-record even after Phase 3 per the locked external-claim policy.
- **Named partner programs.** Phase 4 partner portal covers OEM / AMC / integrator named-partner content. /platform names the AMC channel as an existing deployment reality, not as a named-partner program.
- **Full capability detail.** `/capabilities/<pillar>` × 5 (LOCKED) cover the per-pillar capability stories; this page cross-links to them rather than duplicating.
- **Full architecture walkthrough.** `/architecture` (LOCKED v2) covers the 4-column Industrial Intelligence Stack; this page cross-links for the full stack story.
- **Full solution narratives.** `/solutions` hub + 2 new Phase 2 solution pages + 5 existing v2 solution pages cover the outcome stories.
- **Full security / trust posture walkthrough.** `/security` covers the full operational trust posture; this page carries the 3-bullet summary + cross-link.
- **Industries-specific framings.** Phase 3 `/industries/<industry>` or the Phase 2.5 single-industry exception per phase2-ia-scope-memo-amendment v3 §2.
- **Per-tenant deployment architecture for EREMOS V2.** `/capabilities/operational-intelligence` (LOCKED) covers per-tenant isolation at the capability level; deeper deployment architecture (cluster topology, scaling characteristics, regional replication) lives on Phase E `/eremos-v2` product page.
- **Quantified outcome metrics.** Wait for Phase 3 customer-story registry + signed customer-story sign-offs.
- **AI / ML feature claims.** Out of scope per buyer-taxonomy §2.1 CTO/CIO vocabulary discipline and per honest-framing rules from predictive-maintenance v2 ("not 'AI predicts what will fail' — threshold-based detection on real condition signatures"). When AI/ML claims become defensible with shipped capability + customer evidence, Phase E + Phase 3 case studies will land them.

---

*`/platform` Page Spec **v2 LOCKED 2026-05-29** — FINAL Phase 2 spec. Pass 1 ChatGPT verdict: "Strongest strategic page in the entire website architecture. The reader can now understand what Elpis does, how it works, what outcomes it creates, and why Elpis exists as a vendor." 4-agent pre-lock validation workflow caught 2 HIGH-severity blocking issues (FAQ Q5 OPC UA Server misattribution + hero subhead locked-anchor capitalization drift) — both resolved before lock. Discipline-lock guard returned CLEAN across all 11 areas. v1 → v1.5 (ChatGPT refinements: Platform Principles callout + OEM/AMC symmetry + R2/R3 polish + R4 governance cue) → v2 LOCKED (workflow fixes: FAQ Q5 publish-surface split + hero anchor capitalization + FAQ Q6 parenthetical + checklist reconciliation + typo + Pillar 4 verbatim grouping). v1.5/v2 changes documented in spec preamble. Word count: ~1,810 → ~1,930 (within 1,500-2,000 target). **Tenth (FINAL) per-page spec in the Phase 2 wave** per amendment v3 §6 sequencing step 11. Vendor-worldview synthesis page — different page structure from CapabilityDeepDive (5 pillar pages) + SolutionPanel (4 solution pages). 9-section layout: Hero → Why Elpis exists → 5-pillar synthesis → Commercial engagement → Trust posture summary → Where we operate + trust anchors → Inline FAQ → Cross-lens → Final CTA. ~1,810 words within 1,500-2,000 target per §9 page-type guidance. Inline FAQ per §9 governance (/platform = YES; competitive-comparison Q explicitly EXCLUDED per proof-architecture v1 §8). §1.4 metadata block present. Commercial-engagement scope per amendment v3 §1.7 — IN: modular pillar, edge + cloud, OEM/integrator, AMC; OUT: SKU grids, per-tag pricing, subscription. Locked trust-anchor phrasings preserved verbatim from positioning v3 §4 + amendment v4 §3 + §5 (defense + space-agency anonymous framing; India + Middle East geography; AMC partner channel anonymized). Source-of-truth alignment discipline baked in from predictive-maintenance v2 + edge-connectivity v2 workflow lessons (EdgeConnect Linux roadmap, E-IDOS standalone today, mDAQ runs VAS only, per-gateway identity, beside-not-replacing, MT-LINKi side-flag carry-forward). 5 cross-spec governance side-flags carried forward (MT-LINKi publish-live gate, OPC UA Server typo on PR #73, PI/Wonderware/Aveva softening on PR #77, /solutions hub status-pill swaps when solution pages ship live, PR merge status). Cites: ALL 9 prior Phase 2 specs as cross-link targets; phase2-ia-scope-memo v2 + amendment v3 §1.7; buyer-taxonomy v1 §2.1 + §2.3 + §2.7; proof-architecture v1; design-system v3 §16 + §17 LOCKED preset for /platform line 554; positioning v3 §4 + §5 + §6 commitment #3; positioning-amendment-v4 §3 + §5; hardware-ecosystem-map v3 (5-pillar canonical source-of-truth).*
