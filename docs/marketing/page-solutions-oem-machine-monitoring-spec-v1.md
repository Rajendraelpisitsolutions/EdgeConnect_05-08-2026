<!--
File:        docs/marketing/page-solutions-oem-machine-monitoring-spec-v1.md
Purpose:     Page spec for /solutions/oem-machine-monitoring — the
             equipment-builder solution depth-example. Phase E bulk-
             migration of solution-oem-machine-monitoring-v2.md (Phase 1
             page copy) onto the SolutionPanel §15 layout / §9 canonical
             per-page-spec format. INHERITS the locked pattern-setter
             precedents P-A..P-G from page-solutions-cnc-machining-spec-
             v1.md (page content v3).
Audience:    Internal — Angular engineering team (page implementers),
             copywriters (lifting verbatim copy), user + ChatGPT
             (reviewers), Phase E batch-migration authors.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-solutions-cnc-machining-spec-v1.md v1 (LOCKED PATTERN-
                SETTER — structural model + the four §15 ecosystem-framing
                additions + P-A..P-G locked precedents this spec inherits)
             solution-oem-machine-monitoring-v2.md (Phase 1 page copy being
                migrated — voice + structure precedent; SUPERSEDED by this
                spec at v3 lock; retained as voice reference. ALSO the
                corpus anti-overclaim "cut truck rolls" hedging precedent —
                hedged verbs preserved verbatim here)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED —
                sister SolutionPanel exemplar; IS/IS-NOT boundary: the
                protocol-agnostic cross-vendor OT-consolidation story
                across ALL controller classes — this page is the
                equipment-BUILDER cut, not the plant-operator cut)
             page-solutions-predictive-maintenance-spec-v1.md v2 (LOCKED —
                sister SolutionPanel exemplar; IS/IS-NOT boundary: the
                reliability / condition-monitoring story — this page is the
                fleet-service-economics story, not the reliability story)
             page-capabilities-hub-spec-v1.md §9 (canonical template;
                per-page-type FAQ governance — /solutions/<solution> = YES;
                metadata governance; "How this differs from…" emerging-
                pattern governance; Typical-Engagement optional-section
                guidance via §15 baseline)
             buyer-taxonomy-v1.md §2.6 (OEM machine builder — primary
                buyer; a DIFFERENT audience from the plant-operator pages.
                No secondary buyer in primary content)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics,
                no customer names, no competitor names)
             design-system-v3.md §15 (SolutionPanel — LOCKED) + §16 (trust
                cue content pattern) + §17 (cross-lens LOCKED preset for
                /solutions/<solution>) + §5.A (ArchitecturePanel.interactive
                solution-annotated variant)
             page-capabilities-asset-intelligence-spec (LEAD pillar — mTracker
                fleet visibility / service-hours billing / warranty; cross-
                lens related-pillar card per P-C)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED —
                EdgeConnect + Edge Gateway + today-protocol list; the OTHER
                pillar, cross-linked inline in §3.3)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-link target
                for "See full architecture"; per-gateway-identity / anti-
                multi-plant-EdgeConnect FAQ)
             page-solutions-hub-spec-v1.md v2 (LOCKED — /solutions hub
                directory this depth-example sits under)
             shared-knowledge/contracts/eremos-per-tag-mqtt.md (per-gateway
                identity contract — load-bearing for the fleet-identity story)
             2026-06-04-phase-e-solution-migration-plan.md (the bulk-
                migration plan-trail this spec executes; D1-D6 + §3 buyer/
                pillar map + P-A..P-G)
Version:     v1 — LOCKED 2026-06-04 (page content v3). Phase E batch member — drafted against the
                LOCKED CNC pattern-setter; carried through the batch ChatGPT
                review + pre-lock validation workflow with its 3 sibling
                migrations, then locked + shipped as one wave per the §15 Q3
                bulk-migration lock.
Date:        2026-06-04
Status:      LOCKED 2026-06-04 (page content v3). Batch ChatGPT review +
                pre-lock validation workflow PASSED (0 HIGH; 2 MED — §5
                table reconcile + §6 store-and-forward guard completeness
                — applied). Typical Engagement OMITTED stands (P-A counter-
                example). Locks + ships as one wave with the other 4.

INHERITED PATTERN-SETTER PRECEDENTS (from page-solutions-cnc-machining-
spec-v1.md; applied here):
  P-A (D4) — Typical Engagement is an optional §15 section; include ONLY
    where deployment anxiety is a real buyer objection, documenting the
    rationale + word-budget consequence. THIS PAGE OMITS IT — see the
    structural note below. This is the valuable pattern-setter data point:
    not every migrated page includes the optional section.
  P-B (D2/D5) — whatsIncluded buckets follow product-narrative groupings,
    not literal schema field names; bucket choice + omissions documented
    in §3.4.
  P-C (R4) — when a solution touches multiple pillars, the §17 cross-lens
    related-pillar card leads with the DIFFERENTIATING capability. This
    page LEADS Asset Intelligence (the fleet/service-economics differentiator
    that makes this the equipment-builder story); Connectivity & Edge is
    cross-linked inline in §3.3.
  P-D (MF2) — trust-cue placement follows the realized exemplar order
    (after Architecture, before Cross-lens + CTA), not the literal §15 prose.
  P-E (OP1) — architecture-annotation eyebrow doubles as the ≤4-word title.
  P-F (OP3) — vertical solution-page hero subhead carries the relevant
    protocol SUBSET; full list in the trust strip / §3.4.
  P-G (D5) — MT-LINKi → roadmap only; S7 + OPC UA Client → today. The two
    correctness fixes the migration surfaces vs the stale Phase 1 v2 page.

What the migration ADDS vs the Phase 1 v2 page copy (the four §15
ecosystem-framing additions + §9 governance additions):
  1. Pillar cross-references — §3.3 names the contributing capability
     pillars (Asset Intelligence + Connectivity & Edge) with inline
     /capabilities/<pillar> cross-links (NEW vs v2).
  2. Trust cue — §3.8 applies the §16 content pattern (2 cues, /security
     cross-link) (NEW vs v2).
  3. ArchitecturePanel.interactive (variant=solution-annotated) — §3.7
     replaces the v2 static SVG diagram with the §5.A interactive
     annotated subset (NEW vs v2).
  4. Cross-lens navigation — §3.9 applies the §17 LOCKED preset (NEW vs v2).
  + §1.4 metadata block (§9 metadata governance).
  + Inline FAQ reframed with FAQPage schema (§9 per-page-type FAQ
    governance; the v2 §5 "questions OEM teams raise" list is reworked into
    the FAQPage-schema inline FAQ — trimmed from 7 to 6, dropping the
    pricing-mechanics Q which routes to a scoping conversation, not a page
    answer; the white-label Q is folded into the partnership FAQ).
  + "How this differs from closed connected-equipment platforms" callout in
    §3.3 (§9 emerging-pattern governance) — emphasizing CUSTOMER-CONTROLLED
    telemetry as the differentiator.

Source-of-truth alignment baked into this v1 draft (migration plan D5 + P-G):
  - MT-LINKi → ROADMAP mention, removed from the today-list. The Phase 1 v2
    page listed MT-LINKi as a connected-today protocol across §1/§4; this
    migration corrects it per side-flag #1 resolution (2026-06-04) +
    /platform v2.1 §6 re-add governance.
  - S7 + OPC UA Client are NOW operator-available (CLAUDE.md §8 + locked
    connectivity-edge v2). The Phase 1 v2 protocol enumeration omitted both;
    this migration adds them to the today-list. (S7 + OPC UA Client are the
    two near-universal controller classes an OEM specs alongside Fanuc;
    surfacing them is a real coverage correction for THIS buyer.)
  - OEM today-protocol subset (this page surfaces the controller classes an
    OEM commonly specs): FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA
    Client, S7 — plus the OPC UA SERVER sink for customer-side SCADA/MES
    exposure; FANUC MT-LINKi REST on the roadmap. Full protocol matrix lives
    on Phase E /edgeconnect; this page stays at solution-level vocabulary.
  - EdgeConnect = Windows service today; Linux near-term roadmap (on Edge
    Gateway). For OEMs this is load-bearing: the embedded-box framing must
    NOT imply a Linux/DIN-rail appliance is shipping today — §3.4 carries the
    honest "Windows today, embed on a small box you supply or a customer box
    adjacent to the machine; Linux + Edge Gateway appliance near-term
    roadmap" framing.
  - Per-gateway identity / anti-multi-plant-EdgeConnect: each shipped machine
    carries its own per-gateway UUID established at installation; the
    OEM's installed-base view comes from EREMOS V2 AGGREGATION across those
    per-machine runtimes — NOT one OEM-side runtime reaching into customer
    plants. (eremos-per-tag-mqtt.md contract + /architecture v2.1.)
  - "Beside, not replacing": EdgeConnect ships beside the customer's own
    SCADA/MES/historian; the OPC UA Server sink exposes machine data to them
    rather than supplanting them.
  - Anti-overclaim: "cut" / "reduce" verbs only — NEVER "eliminate" / "no" /
    "zero". This page is the corpus precedent for the hedged "cut truck
    rolls" verb (Phase 1 v2 §1/§6); preserved verbatim.

Voice preservation (migration plan D6): the Phase 1 v2 page is built on
three signature lines this migration preserves —
  - the hero triad "Ship connected equipment. Diagnose remotely. Cut truck
    rolls." (§1, the hedged-verb precedent line);
  - "Your monitoring platform becomes the reason the deal stalls." (§2, the
    OEM-specific pain that appears on no other solution page);
  - "Your customer controls their data; you get the service visibility you
    need." (the central trust promise / differentiator).
Plus "diagnose before the customer calls" (the reframe from defensive
remote-monitoring to offensive proactive-service) and "Co-existence by
design."

Structural note — Typical Engagement section OMITTED (migration plan D4 /
P-A; this page's documented decision). D4 marked OEM Typical-Engagement as
TBD, to decide at draft. DECISION: OMIT.

  Rationale. P-A admits the optional §15 Typical Engagement section ONLY
  where DEPLOYMENT anxiety is a real buyer objection. For the Plant-manager
  buyer (CNC) that objection is explicit ("How long does this take?" →
  buyer-taxonomy §2.2 typical-engagement timeline), so CNC restored it. The
  OEM machine builder (buyer-taxonomy §2.6) is a different audience: their
  decisive anxiety is NOT a deployment clock but CUSTOMER-IT ACCEPTANCE and
  FLEET/SERVICE ECONOMICS — "will my customers accept the telemetry?", "can
  they say no?", "what about air-gapped customers?", "how does this survive
  acquisitions and plant transfers?". §2.6 lists none of CNC's deployment-
  clock objections; it lists customer-data-control and service-economics
  ones. Those are answered structurally in §3.3 (customer-controlled routing,
  per-customer fleet identity) and in the §3.5 FAQ — not by a 4-step rollout
  timeline. The Phase 1 v2 §7 "typical OEM engagement" was a PROGRAM-ADOPTION
  arc (embed in one model → pilot at customer sites → roll into shipments),
  not a deployment-anxiety timeline; its load-bearing content (retrofit /
  opt-in, "no multi-year commitment to prove the stack works") is folded into
  §3.5 Q5 + the §3.10 final CTA rather than given its own section.

  Word-budget consequence (the P-A documented consequence, inverted): because
  the optional section is OMITTED, this is a 10-section page and the entire
  page sits WITHIN the 1,500-1,800 band with no documented over-ceiling — the
  opposite of CNC's case. There is no over-ceiling reason to document because
  there is no optional section.

  Pattern-setter data point: this is the first Phase E migration to OMIT the
  optional Typical Engagement section. It validates P-A as a genuine per-page
  decision (not a default-on), and records the discriminator: include the
  section when the buyer's anxiety is a deployment CLOCK; omit it when the
  buyer's anxiety is RELATIONSHIP / ECONOMICS and is better answered in the
  approach narrative + FAQ. The other migrations decide per D4 on this basis.

IS / IS-NOT vs the two LOCKED sister depth-examples (so this page does not
overlap them):
  - /solutions/edge-connectivity v2.1 = the protocol-agnostic cross-vendor
    OT-CONSOLIDATION story across ALL controller classes, for the
    plant-side OT Architect / plant engineer. THIS page is the
    equipment-BUILDER cut: the OEM ships connected equipment INTO customer
    plants; the buyer is the vendor, not the operator; the differentiator is
    fleet/service economics + customer-controlled telemetry, not floor-wide
    vendor consolidation.
  - /solutions/predictive-maintenance v2 = the RELIABILITY / condition-
    monitoring story (vibration / oil / bearing-fault) for the Maintenance
    Manager / AMC provider. THIS page borrows tool-life-telemetry vocabulary
    but its outcome is SERVICE ECONOMICS (cut truck rolls, warranty, fleet
    visibility, service-hours billing), not condition-monitoring depth.

Word-count target: 1,500-1,800 words page copy per /capabilities hub §9
page-type guidance for /solutions/<solution>. 10-section SolutionPanel core,
no optional Typical Engagement section → reconciled ~1,640 words, in-band.

Carry-forward side-flag (publish-orchestration, not a spec blocker): when
this page ships live, /solutions hub v2 Card (OEM) "Coming soon" status pill
+ pre-live link swap per the /solutions hub pre-live link policy. Ships as
part of the 5-page bulk wave (no solution page goes live in v3 while a
sibling is still v2).
-->

# `/solutions/oem-machine-monitoring` — Page Spec v1 (page content v3)

**Solution depth-example for the OEM machine builder — the equipment vendor shipping connected machines into customer plants. Uses `SolutionPanel` layout from design-system v3 §15. Reader lands here when they want the outcome view of running one service-visibility picture across an installed base — without becoming an adversary of their own customers' IT departments.**

This is the page where OEM product managers, service operations directors, and heads of installed-base land when they want the **outcome view** of connected equipment: ship machines customers actually accept, diagnose remotely, cut truck rolls, and see the whole fleet — while the customer keeps control of their data. It is **not** the capability page (`/capabilities/asset-intelligence` covers mTracker fleet visibility as a pillar; `/capabilities/connectivity-edge` covers EdgeConnect). It is **not** the architecture walkthrough (`/architecture`). It is the **equipment-builder solution narrative**.

Target length: **1,500-1,800 words page copy** per `/capabilities` hub spec §9 page-type guidance for `/solutions/<solution>`.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Solution depth-example narrative for the OEM machine builder. Reader leaves with *"I now understand what shipping connected equipment on this platform gets me, why my customers will accept it instead of stalling the deal, which controllers it collects from today, how my installed base stays identifiable through acquisitions and transfers, and what service-economics outcomes I can hold Elpis to."*

**IS NOT:**
- The capability page (`/capabilities/asset-intelligence` covers mTracker fleet visibility / service-hours / warranty as a pillar; `/capabilities/connectivity-edge` covers EdgeConnect as Pillar 1; this page cross-links rather than duplicating)
- A product detail page (Phase E `/edgeconnect` covers the full protocol matrix, semantic modes, OPC UA Server security profiles, route-config mechanics)
- The architecture walkthrough (`/architecture` covers cross-pillar composition + per-gateway identity; LOCKED v2.1)
- The protocol-agnostic / OT-consolidation depth-example (`/solutions/edge-connectivity` v2.1 is the **plant-operator** cross-vendor consolidation story; this page is the **equipment-builder** cut — the OEM ships *into* customer plants, it does not operate them)
- The reliability story (`/solutions/predictive-maintenance` v2 is the condition-monitoring depth-example; this page borrows tool-life vocabulary but its outcome is **service economics**, not vibration/oil analysis)
- A pricing or commercial page (OEM licensing is scoped in a partner conversation; the page routes to a scoping call, never exposes pricing detail)

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** OEM machine builder (§2.6) — the equipment vendor, either an OEM product manager (deciding the connectivity strategy for next-generation equipment) or an OEM service operations director (running the existing service organization); often the same person at a smaller OEM. **A different audience from the plant-operator pages** — this buyer ships equipment, they don't run a floor.
- Lands here from `/solutions` hub, from the homepage, from an OEM-partnership referral, or from a search for *"connected equipment platform OEM"* / *"remote machine diagnostics OEM"* / *"installed-base monitoring"* / *"reduce service truck rolls"*
- Wants: ship connected equipment customers actually accept, remote diagnostics that cut truck-roll cost, fleet visibility for warranty and service-hours billing, a connectivity story that differentiates equipment instead of slowing the sale, field data flowing back to product engineering
- CTA preference: *"Request an OEM scoping call"* / *"Bring us your installed base"* > *"Talk to OEM partnerships"* > datasheet download (§2.6 — generic *"Book a demo"* is the wrong context)
- Vocabulary that lands: *truck rolls*, *installed base*, *warranty fleet*, *service-hours billing*, *remote diagnostics*, *diagnose before the customer calls*, *customer-controlled telemetry* (the differentiator), *white-label*, *field service*, and real protocol/model names (FOCAS2, every Fanuc generation) as trust signals
- Vocabulary that backfires: *"smart machine"*, *"AI-enabled equipment"*, *"IoT for machines"*, *"digital twin"*

**Secondary buyer:** none carried in primary content. §2.6 lists no secondary buyer for this page; the OEM engineering leader interested in field-data feedback is accommodated via the §3.5 FAQ (Q6) and the cross-lens to `/capabilities/asset-intelligence`, not by widening the primary narrative.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/solutions/cnc-machining` v1 (page content v3) §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *OEM Machine Monitoring — Connected Equipment · Elpis* |
| **Meta description** (140-160 chars) | *Ship connected equipment customers accept. Remote diagnostics, fleet visibility, customer-controlled telemetry. Native FOCAS2, MTConnect, Modbus TCP and more.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/solutions/oem-machine-monitoring` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. §3.5 inline FAQ uses `FAQPage` schema. Cross-links to `/capabilities/asset-intelligence` + `/capabilities/connectivity-edge` + `/architecture` + `/security` use `relatedLink`. Product cards for EdgeConnect + EREMOS V2 (when Phase E product pages ship) via `SoftwareApplication` schema. |

---

## 2. Page structure — sections at a glance

`SolutionPanel` layout per design-system v3 §15 (LOCKED). **10 sections** — the standard SolutionPanel shape (same as `/solutions/edge-connectivity` v2.1 and `/solutions/predictive-maintenance` v2). The **optional Typical Engagement** section is **OMITTED** per migration plan D4 / P-A (rationale documented in the header structural note and §3.6).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — outcome headline + subhead + CTAs | `dark-deep` | `SectionShell` + `Button` × 2 | ~75 |
| **2** | The OEM Service Reality (customer pain) — narrative empathy, 3 paragraphs | `light` | Narrative copy + optional margin pull-quote | ~210 |
| **3** | How Elpis Solves OEM Machine Monitoring — 5 bolded-lead paragraphs + pillar cross-refs + "How this differs from closed connected-equipment platforms" callout | `light-tinted` | Bolded-lead paragraphs with `/capabilities/<pillar>` cross-links + callout block | ~400 |
| **4** | What's Included — For Your Machine (EdgeConnect) + For Your Service Organization (EREMOS V2) | `light` | Bulleted feature lists with bolded leads, 2 sub-sections | ~280 |
| **5** | Common Questions (inline FAQ) — 6 Q&A pairs | `light` | Bold pull-quote questions + answers + `FAQPage` schema | ~340 |
| **6** | Outcomes You Can Hold Us To — bulleted, 2-column desktop | `dark` | Bolded outcome leads + supporting clauses | ~120 |
| **7** | Architecture For This Solution — solution-annotated diagram | `light-tinted` | `ArchitecturePanel.interactive` variant=`solution-annotated` + caption + "See full architecture →" | ~80 |
| **8** | Trust Cue — 2 cues + `/security` cross-link | `light-tinted` | Trust cue content pattern (design-system v3 §16) | ~80 |
| **9** | Cross-lens navigation — LOCKED preset per §17 | `light-tinted` | Cross-lens content pattern (3 cards) | ~50 |
| **10** | Final CTA — "Bring us your installed base" | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> SOLUTION · OEM MACHINE MONITORING
>
> HEADLINE (size.3xl semibold):
> Ship connected equipment. Diagnose remotely. Cut truck rolls.
>
> SUBHEAD (size.lg, max-width 60ch):
> EdgeConnect deploys with your machine; EREMOS V2 aggregates your installed base. Native FOCAS2, MTConnect, Brother HTTP, and Modbus TCP — plus OPC UA Client and Siemens S7 for whatever else you spec. Your customer controls their data; you get the service visibility you need.
>
> PRIMARY CTA (`Button.primary.lg`):
> Request an OEM scoping call
> HREF: `/contact?intent=oem-scoping`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Download the datasheet
> HREF: `/resources/datasheet`

> TRUST STRIP (under hero, size.sm):
> *Connected on FOCAS2 · MTConnect · Brother HTTP · Modbus TCP · OPC UA Client · Siemens S7 — with an OPC UA Server sink for customer-side SCADA. FANUC MT-LINKi REST on the roadmap. Customer-controlled. OEM-aware. Field-ready.*

**Anti-patterns:** No *"smart machine"* / *"AI-enabled equipment"* / *"IoT for machines"* / *"digital twin"* framing (buyer-taxonomy §2.6 vocabulary discipline). No fabricated truck-roll-savings metric in the headline. The hero triad **"Ship connected equipment. Diagnose remotely. Cut truck rolls."** is preserved **verbatim** from Phase 1 v2 §1 (voice + the corpus anti-overclaim hedged-verb precedent — *"cut"*, never *"eliminate"* / *"no"* truck rolls). The central trust promise *"Your customer controls their data; you get the service visibility you need"* is preserved verbatim (voice). Hero leads with the **outcome** (connected equipment / remote diagnostics / cut truck rolls), not the products (EdgeConnect + EREMOS V2) — per §15 anti-pattern.

> **Pattern-setter note (inherited P-F) — solution-page hero subhead protocol enumeration.** The hero subhead names the **OEM-relevant protocol subset** with the full list carried in the trust strip directly beneath, per P-F. **Migration correction surfaced here (P-G):** the Phase 1 v2 §1 subhead listed MT-LINKi as a connected-today protocol and omitted OPC UA Client + S7; this migration drops MT-LINKi to a roadmap mention in the trust strip and adds OPC UA Client + Siemens S7 to the today-list (CLAUDE.md §8 + locked connectivity-edge v2). The OPC UA *Server* sink is named separately — it is how the customer's own SCADA/MES consumes machine data, not a source protocol.

---

### 3.2 Section 2 — The OEM Service Reality

> EYEBROW: THE OEM SERVICE REALITY
>
> NARRATIVE PARAGRAPH 1 (size.base):
> Your service organization is blind until a customer calls. By the time the call comes, the machine has already been down. Your engineer arrives at the customer's site — sometimes hours later, sometimes overnight — to diagnose a fault that, on a connected machine, could have been identified before the customer noticed.
>
> NARRATIVE PARAGRAPH 2 (size.base):
> Every dispatch costs in engineer time, travel, parts inventory, and customer goodwill. Every dispatch that turns out to be a remote-diagnosable issue is pure margin loss. Meanwhile your product engineering team is starved for field data — they're improving the next-generation machine on six-month-old anecdotes from the service team's whiteboard.
>
> NARRATIVE PARAGRAPH 3 (size.base):
> The instinct is to build a connected-equipment platform yourself: an embedded gateway, a cloud back-end, a dashboard, a ticketing integration. Then you meet the customer's IT department. Some customers won't allow always-on connectivity. Some require complete data sovereignty. Some don't want your machine on their network at all. The connectivity story that was supposed to differentiate your equipment becomes the friction that kills its sale.

> OPTIONAL MARGIN PULL-QUOTE (desktop, size.lg italic):
> *"Your monitoring platform becomes the reason the deal stalls."*

**Note (voice):** the *"build it yourself → then you meet the customer's IT department"* recognition arc and the pull-quote *"Your monitoring platform becomes the reason the deal stalls"* are preserved from Phase 1 v2 §2 per migration plan D6 — this is the OEM-specific pain that appears on no other solution page. Paragraph 2 drops the v2's bare *"hundreds of dollars"* phrasing to a hedged *"costs in engineer time, travel…"* (proof-architecture §3 — no fabricated per-dispatch dollar figure); the service-economics math itself is scoped in the conversation, not asserted on the page. No bullet lists — subdued empathy treatment.

---

### 3.3 Section 3 — How Elpis Solves OEM Machine Monitoring

> EYEBROW: HOW ELPIS SOLVES OEM MACHINE MONITORING

> CALLOUT — HOW THIS DIFFERS FROM CLOSED CONNECTED-EQUIPMENT PLATFORMS (size.base, single paragraph; bordered card or left-rule callout, sits before the bolded-lead paragraphs):
>
> > **How this differs from closed connected-equipment platforms.** The usual connected-equipment model is a closed loop: the machine phones home to the OEM's own cloud, the OEM holds the telemetry, and the customer is asked to trust a black box on their network. That model is exactly what stalls at the customer's IT department. Elpis inverts it. EdgeConnect runs locally on the equipment, and the customer configures — by route — which signals flow back to you and which stay on their floor. **Customer-controlled telemetry is the differentiator.** You get the service visibility you need without asking the customer to surrender data sovereignty, and the connectivity story becomes a reason to buy the machine instead of a reason to hesitate. The customer's own systems stay where they are; Elpis layers into the relationship instead of overriding it.

#### Bolded-lead paragraphs (5 paragraphs):

> **EdgeConnect ships with your machine.** One service running on a small box inside your equipment — or on a customer-supplied box adjacent to it — polls your controller over its native protocol: FOCAS2 for any Fanuc generation, MTConnect for the open-standard machines, Brother HTTP, Modbus TCP for PLC-fronted designs, plus OPC UA Client and Siemens S7 for whatever else you spec; FANUC MT-LINKi REST is on the roadmap. The tag map you author once carries across every machine you ship — same software image, same canonical vocabulary, same deployment model, installed-base-wide. This is the **Connectivity & Edge** capability applied to shipped equipment → `/capabilities/connectivity-edge`.

> **The customer controls what flows back to you.** EdgeConnect's route-based architecture lets the customer configure which data goes where. Service-relevant signals — alarm state, run hours, fault codes, tool-life telemetry — can route to your installed-base view. Operationally sensitive data — parts programs, production volumes, customer-specific configurations — can stay local. No always-on remote-access tunnel, no data exfiltration, no customer-IT escalation.
>
> *(Render this paragraph with slight visual prominence — it is the single most important trust message on the page for the OEM-customer relationship.)*

> **You see your installed base; the customer sees their machine.** EREMOS V2 aggregates the service-relevant telemetry across every machine you've shipped, organized by customer site. Each customer can also run EREMOS V2 themselves if they want their own operational view — same platform, a separate tenant, no data leakage between them. Co-existence by design.

> **Per-customer fleet identity from day one.** Each shipped machine carries a stable per-gateway UUID, with customer/site binding established at installation. Acquisitions, name changes, plant transfers — the identity model survives all of them, so warranty, SLA, and service-hours tracking hold up because the identity is permanent. This is the **Asset Intelligence** capability — mTracker turns that permanent fleet identity into service-hours billing, warranty attribution, and installed-base visibility → `/capabilities/asset-intelligence`.

> **Diagnose before the customer calls.** With service-relevant telemetry flowing back, your service team sees alarm patterns, fault progressions, and degraded operation across the installed base. A tool-life metric trending toward failure on three customer machines becomes one proactive service campaign instead of three separate emergency dispatches — which is how remote diagnostics turns from a defensive cost-control measure into an offensive service differentiator.

**Note (voice + pillars):** the five bolded leads are preserved from Phase 1 v2 §3 (each lead resolves an OEM-specific tension); *"diagnose before the customer calls"* and *"Co-existence by design"* retained per D6. Pillar cross-refs are the NEW §15 ecosystem-framing addition vs v2. **Pillar-balance note (inherited P-C reasoning):** the lead pillar is **Asset Intelligence** (per migration plan §3 — mTracker fleet visibility / service-hours / warranty is the *differentiating* capability that makes this the equipment-builder story); **Connectivity & Edge** is the architectural enabler, cross-linked inline in paragraph 1. The cross-lens card (§3.9) therefore leads with Asset Intelligence per P-C. **Migration correction (P-G):** paragraph 1's protocol enumeration adds OPC UA Client + S7 to the today-list and moves MT-LINKi REST to the roadmap (vs the Phase 1 v2 §3, which had no protocol list, and §4, which listed MT-LINKi as today).

---

### 3.4 Section 4 — What's Included

> EYEBROW: WHAT'S INCLUDED

Per design-system v3 §15 `whatsIncluded` schema: 2 buckets — `edgeConnect` (relabeled **For Your Machine**) + `eremosV2` (relabeled **For Your Service Organization**). The bucket *labels* follow the OEM-customer architectural boundary (what ships *in the machine* vs what runs in the *OEM's service environment*) — a product-narrative grouping, not the literal schema field names, per P-B. The standalone `hardwareProducts` bucket is **omitted** — OEMs embed EdgeConnect on a box they supply inside their own equipment; the Edge Gateway appliance is a near-term-roadmap deployment option mentioned inline, not a lead bucket on this page. (Bucket-narrative governance per P-B + migration plan D2/D5.)

#### For Your Machine (EdgeConnect, embedded with your equipment)

> - **Native protocol coverage for whatever controllers you spec** — FOCAS2 (every Fanuc generation), MTConnect, Brother HTTP, Modbus TCP, plus OPC UA Client and Siemens S7. Mix and match across product lines. **On the roadmap:** FANUC MT-LINKi REST. For the full protocol matrix with semantic modes, see Phase E `/edgeconnect` (coming soon).
> - **One tag map, deployed across your installed base** — authored once for your machine, replicated across every unit you ship; tag-map updates flow to existing machines via standard configuration deployment.
> - **Route-based data control** — the customer configures which signals route where: service telemetry to the OEM, operational data stays local, nothing leaves the customer's network without an explicit route.
> - **OPC UA Server sink (optional)** — exposes machine data natively to the customer's own SCADA / MES if they want it. Beside, not replacing. Co-existence by design.
> - **Per-route store-and-forward** — a customer-network outage doesn't lose machine telemetry; signals buffer locally and replay in source order on reconnect.
> - **Per-gateway UUID + customer/site binding** — established at first start. Each shipped unit carries permanent identity for warranty, SLA, and service-history attribution.
> - **Hash-chained audit log** — a tamper-evident record of every configuration change on the machine; useful for warranty claims and regulated-industry deployments.
> - **Offline-capable** — no cloud, internet, or always-on connectivity required for the machine to operate.

> > *Deployment note — EdgeConnect ships as a Windows service today; embed it on a small box inside your equipment, or run it on a customer-supplied box adjacent to the machine. A Linux runtime is near-term roadmap, arriving on the Edge Gateway appliance for OEMs who prefer a turnkey embedded module. The appliance is an option, not a requirement.*

#### For Your Service Organization (EREMOS V2, aggregating the installed base)

> - **Installed-base view** — every shipped machine in one operational dashboard, organized by customer site.
> - **Per-customer fleet drill-down** — a specific customer's machines, their alarm history, their service patterns.
> - **Persistent alarm tracking with incident grouping** — proactive identification of fault patterns across the installed base, not isolated tickets.
> - **Tool-life and consumable telemetry** — flag wear ahead of failure to drive proactive service campaigns.
> - **Multi-tenant by design** — your installed-base view is your tenant; your customer's own view (if they run EREMOS V2 themselves) is theirs. No data leakage between tenants.
> - **Service-history reporting** — exportable per-customer or per-machine for SLA reviews, warranty claims, and contract renewals.

---

### 3.5 Section 5 — Common Questions

Per `/capabilities` hub spec §9 per-page-type FAQ governance: `/solutions/<solution>` includes an inline FAQ with `FAQPage` schema markup. 6 questions calibrated to OEM product-manager / service-director scoping concerns (the Phase 1 v2 §5 seven-question list is reworked here: the pricing-mechanics Q is dropped — it routes to a scoping conversation, not a page answer — and the white-label Q is folded into the partnership answer Q5).

> EYEBROW: COMMON QUESTIONS
>
> SECTION TITLE:
> What OEM service and product teams ask before scoping.

#### Q1. Can our customers say no to monitoring?

> Yes — and that's the point. The customer controls the route configuration; if they don't want service telemetry flowing to you, none does. The platform is designed to respect customer choice, not coerce it, because trust is the durable commercial position. An OEM that respects data sovereignty becomes the vendor the customer's IT department signs off on, not the one they block.

#### Q2. Can our customer use the same platform for their own operational view?

> Yes. The customer can run EREMOS V2 themselves in their own tenant, build their own dashboards, and route some of the same machine signals to both their view and yours. One platform, two tenants, separate data control — co-existence by design. It also sits beside their existing SCADA, MES, or historian; the optional OPC UA Server sink exposes machine data to those systems rather than replacing them.

#### Q3. What about customers with strict no-cloud or air-gap policies?

> Supported. EdgeConnect runs offline. Telemetry exports on the customer's schedule via approved channels — scheduled or manual exports — instead of always-on connections, so strict data-sovereignty customers stay addressable. The machine itself never needs connectivity to operate.

#### Q4. How do we handle a customer who buys the machine and then refuses connectivity?

> The machine works either way. Connectivity is a layered capability, not a precondition for operation. If the customer changes their mind later, connectivity activates through a configuration change — no service call required.

#### Q5. Can we white-label the operator-facing parts, and how does an OEM partnership work?

> Co-branding options are available for OEM partnerships at appropriate scale, and OEM licensing is structured differently from per-plant deployments — we scope it against your product economics in a partner conversation rather than publishing it here. Bring your installed-base size, the controllers you spec, and your packaging strategy to an OEM scoping call and we'll scope the licensing and any white-label path together.

#### Q6. What about feeding service data back to product engineering?

> The installed-base view includes alarm patterns, run hours, and operational telemetry your product-engineering team can analyze for next-generation decisions — so the field-data feedback loop runs on continuous installed-base data instead of six-month-old service anecdotes. The same per-gateway identity that supports warranty and service-hours billing keeps that field data attributable per machine and per customer.

---

### 3.6 Section 6 — Outcomes You Can Hold Us To

> EYEBROW: OUTCOMES YOU CAN HOLD US TO
>
> SECTION TITLE:
> What changes when OEMs deploy.

**Bulleted outcomes, 2-column on desktop, single column on mobile. Bolded outcome lead + light-weight supporting clause.**

> - **Cut truck rolls on remote-diagnosable issues** — the service team identifies the problem before the customer calls; some dispatches become phone-resolved
> - **Diagnose before the customer notices** — alarm patterns trigger proactive service campaigns instead of reactive emergency dispatches
> - **Field data flows continuously to product engineering** — next-generation decisions backed by real installed-base data, not six-month-old service anecdotes
> - **Warranty disputes resolve on data, not memory** — service history, run hours, and operational telemetry available per machine
> - **Connected equipment becomes a differentiated SKU** — your RFP responses can promise connectivity that doesn't violate the customer's IT policy
> - **Customer relationships strengthen, not weaken** — the platform respects customer data sovereignty; you become the OEM that didn't try to force always-on access

*Note on quantified outcomes:* per `proof-architecture-v1` §3 + §4, this page does not assert specific truck-roll-savings percentages or per-dispatch dollar figures. The service-economics math is scoped per OEM in the conversation; quantified outcomes wait for the Phase 3 customer-story registry. **Outcome verbs use "cut" / "reduce" framing, never "eliminate" / "no" / "zero"** — this page is the corpus anti-overclaim precedent for the hedged *"cut truck rolls"* verb (Phase 1 v2 §6), preserved here. *(Typical Engagement section deliberately OMITTED — see header structural note: the OEM buyer's decisive anxiety is customer-IT acceptance + service economics, answered in §3.3 + the FAQ, not a deployment-clock timeline.)*

---

### 3.7 Section 7 — Architecture For This Solution

> EYEBROW: ARCHITECTURE FOR THIS SOLUTION
>
> CAPTION (above diagram, size.base):
> How it fits together for connected equipment.

**Diagram structure** (per `ArchitecturePanel.interactive` variant=`solution-annotated`, design-system v3 §5.A + §15). Replaces the Phase 1 v2 static SVG (NEW §15 ecosystem-framing addition):

Solution-annotated subset of the Industrial Intelligence Stack, restructured for the OEM-customer boundary. The defining visual distinction from the plant-operator solution diagrams: explicit **customer-site boundary lines** separating customer-side from OEM-side, with customer-controlled route arrows visually emphasized. Highlights:
- **Customer sites (multiple, highlighted):** each contains one or more OEM-supplied machines, each running its own EdgeConnect with a per-gateway UUID
- **Customer-controlled route split (the emphasized element):** the customer's route configuration gates what flows — "stays at customer site" vs "flows to the OEM service environment"
- **OEM service environment (highlighted):** EREMOS V2 with the installed-base view, aggregating across the per-machine runtimes
- **Optional customer-side EREMOS V2 tenant:** shown as a separate tenant if the customer also runs the platform — co-existence, no cross-tenant leakage

**Annotations (4 specific to this solution, per §5.A: the eyebrow doubles as the ≤4-word annotation title — inherited P-E — followed by a 1-2 sentence body; max 8 annotations per zoom level):**

| Annotated region | Eyebrow | Annotation body |
|---|---|---|
| Machine → EdgeConnect (per site) | NATIVE PROTOCOLS | EdgeConnect embeds with each shipped machine and polls its controller — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, plus OPC UA Client + S7. *MT-LINKi REST on roadmap.* |
| Customer route split | CUSTOMER-CONTROLLED ROUTES | The customer's routes — not the OEM's — decide which signals flow back. Service telemetry can route to the OEM; operational data can stay local. Per-route store-and-forward survives connectivity gaps. |
| Per-machine identity | PER-CUSTOMER FLEET IDENTITY | Each shipped machine carries a permanent per-gateway UUID with customer/site binding, so warranty, SLA, and service-hours tracking survive acquisitions and plant transfers. |
| EdgeConnect → customer SCADA/MES | BESIDE, NOT REPLACING | The optional OPC UA Server sink exposes machine data to the customer's own SCADA / MES / historian; those systems stay where they are and consume the data instead of being supplanted. |

> CAPTION (below diagram, size.sm italic):
> *Each machine you ship runs its own EdgeConnect locally; EREMOS V2 in your environment aggregates the installed base across those per-machine runtimes. The customer can run EREMOS V2 themselves in a separate tenant. Trust by design. See the full peer-architecture story → `/architecture`.*

---

### 3.8 Section 8 — Trust Cue

Per design-system v3 §16 trust cue content pattern. 2 cues, both linking to `/security` (NEW §15 ecosystem-framing addition vs v2). **Placement (inherited P-D):** after Architecture, immediately before Cross-lens + Final CTA — following the realized order in the LOCKED sister exemplars, which supersedes the literal §15 prose. For the OEM buyer the trust cues are load-bearing — they are the architectural proof of the customer-data-control promise the whole page rests on.

> EYEBROW: TRUST POSTURE
>
> CUE 1 (size.base):
> **Customer-controlled, offline by default.** EdgeConnect runs offline — the license validates locally, with no phone-home — and the customer's route configuration, not the OEM's, gates what telemetry flows back. If the customer network drops, per-route store-and-forward buffers locally and replays in source order on reconnect. Cloud connectivity is opt-in, never required for the machine to run.
>
> CUE 2 (size.base):
> **Per-machine identity + hash-chained configuration audit.** Each shipped machine runs its own EdgeConnect with a per-gateway UUID and customer/site binding established at installation — there is no single OEM-side runtime reaching into customer plants. Every configuration change is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain, which is what makes warranty claims and regulated-industry deployments defensible on data rather than memory.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.9 Section 9 — Cross-lens Navigation

Per design-system v3 §17 cross-lens content pattern. **LOCKED preset for `/solutions/<solution>` pages**: `/capabilities/<related-pillar>` + `/architecture` + `/solutions` (back to hub). (NEW §15 ecosystem-framing addition vs v2.)

> Pattern-setter decision (inherited P-C): this solution touches two pillars; the related-pillar card leads with **Asset Intelligence** (the mTracker fleet-visibility / service-hours / warranty capability that *makes this the equipment-builder story* and is the differentiator per migration plan §3), with Connectivity & Edge cross-linked inline in §3.3 paragraph 1. Rationale (per P-C): the cross-lens card points to what makes the solution *unique*, not what enables it — protocol collection enables every solution, but fleet/service economics is what distinguishes the OEM cut. This applies the CNC pattern-setter's P-C rule with the differentiating pillar flipped to Asset Intelligence for this buyer.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · ASSET INTELLIGENCE | The underlying capability — mTracker fleet visibility, service-hours, warranty | `/capabilities/asset-intelligence` |
| 2 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |
| 3 | SOLUTIONS | Back to the full solutions directory | `/solutions` |

> Looking for the same thing from another angle?

---

### 3.10 Section 10 — Final CTA

Per buyer-taxonomy v1 §2.6 OEM-builder CTA preference. Vertical-localized per design-system v3 §15 anti-pattern (final CTA on solution pages must be solution-specific, not generic). Voice preserved from Phase 1 v2 §9.

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your installed base.
>
> SUBHEAD:
> Tell us about your equipment — how many machines you've shipped, what controllers you spec, what your service organization needs to see. We'll scope an embedded deployment for your next product release and a path to retrofitting existing units as customers opt in. No multi-year platform commitment required to prove the connectivity stack works.
>
> PRIMARY CTA: Request an OEM scoping call
> HREF: `/contact?intent=oem-scoping`
>
> SECONDARY CTA: Download the datasheet
> HREF: `/resources/datasheet`

**Note (voice):** *"Bring us your installed base"* localizes the homepage CTA pattern to the OEM audience (preserved from Phase 1 v2 §9). *"No multi-year platform commitment required to prove the connectivity stack works"* is preserved — it does the pre-emptive objection-handling against OEM caution about new infrastructure investment, and absorbs the load-bearing intent of the omitted Typical Engagement section (retrofit / opt-in / prove-it-first) into the CTA.

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.10 final CTA |
| `ArchitecturePanel.interactive` (variant=`solution-annotated` per §5.A + §15) | §3.7 architecture-for-this-solution diagram |
| Trust cue content pattern (design-system v3 §16) | §3.8 trust cues |
| Cross-lens content pattern (design-system v3 §17 — LOCKED preset for /solutions/<solution>) | §3.9 cross-lens |
| `CTASection` | §3.10 final CTA |
| Inline FAQ pattern (`FAQPage` schema markup) | §3.5 common questions |

Page composition follows `SolutionPanel` layout from design-system v3 §15 (LOCKED 10-section structure; optional Typical Engagement section omitted per P-A — see §5).

---

## 5. Verbatim copy summary

All page copy collected in §3.1-§3.10. **~1,640 words total.** The 10-section SolutionPanel core is **within** the 1,500-1,800 target for `/solutions/<solution>` per `/capabilities` hub §9, with **no documented over-ceiling** — because the optional Typical Engagement section is omitted (P-A; the inverse of CNC's over-ceiling case). Architecture-diagram annotation bodies (§3.7) are counted as diagram content, not prose, per the locked exemplar convention. The per-section figures below are **approximate targets** (they sum to ~1,705 ≈ the stated ~1,640 — estimate granularity; mirrors the CNC §5 reconciling clause so the table and the stated total don't read as two different numbers).

Section-by-section word distribution:

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~75 |
| 3.2 | The OEM Service Reality (3 paragraphs) | ~210 |
| 3.3 | How Elpis Solves OEM Machine Monitoring (callout + 5 bolded-lead paragraphs) | ~400 |
| 3.4 | What's Included (2 buckets) | ~280 |
| 3.5 | Common Questions (6 Q&A) | ~340 |
| 3.6 | Outcomes You Can Hold Us To (6 outcomes) | ~120 |
| 3.7 | Architecture For This Solution (caption + 4 annotations) | ~80 |
| 3.8 | Trust Cue (2 cues + cross-link) | ~80 |
| 3.9 | Cross-lens | ~50 |
| 3.10 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and §15 SolutionPanel anti-patterns:

| Don't | Why |
|---|---|
| List MT-LINKi as connected-today | Per side-flag #1 resolution (2026-06-04) + `/platform` v2.1 §6 re-add governance — MT-LINKi has no Studio wizard / modular adapter today. The Phase 1 v2 page listed it as today across §1/§4; this migration corrects it to a roadmap mention. Future edits must NOT re-add MT-LINKi to the today-list until the engineering milestone ships. |
| Omit S7 / OPC UA Client from the today-list, or list them as roadmap | They are operator-available today (CLAUDE.md §8 + locked connectivity-edge v2). The Phase 1 v2 enumeration omitted both; this migration adds them. |
| Imply the OEM holds the telemetry or the machine phones home to an OEM cloud | The entire differentiator is **customer-controlled** routing (§3.3 callout + ¶2, §3.5 Q1/Q3, §3.8 Cue 1). Any drift toward an OEM-owns-the-data model regresses the core promise and re-creates the customer-IT friction the page resolves. |
| Imply one OEM-side EdgeConnect runtime reaches into customer plants | Per locked `/architecture` v2.1 + eremos-per-tag-mqtt.md — each shipped machine runs its own runtime with a per-gateway UUID; the OEM's installed-base view comes from EREMOS V2 aggregation. §3.8 Cue 2 + the §3.7 annotation carry the guard. |
| Imply EdgeConnect Linux / an embedded appliance is current behavior | EdgeConnect is Windows today; Linux is near-term roadmap on the Edge Gateway appliance. The §3.4 deployment note carries the honest framing — embed on a box the OEM supplies or a customer box adjacent to the machine; don't drop it. |
| Use absolute outcome claims ("eliminate truck rolls", "no dispatches", "zero downtime") | Anti-overclaim discipline — this page is the corpus precedent for the hedged *"cut truck rolls"* verb. Outcome verbs use "cut" / "reduce". Note: "no data leakage" / "nothing leaves without an explicit route" appears as a route-architecture *mechanism* description (§3.3, §3.4, §3.5 Q1), and the store-and-forward "doesn't lose telemetry" / "survives connectivity gaps" phrasing (§3.4, §3.7 annotation, §3.8 Cue 1) is likewise a *mechanism* description — both stay tied to the mechanism, never framed as an absolute guarantee. |
| Assert specific truck-roll-savings percentages, per-dispatch dollar figures, or warranty-cost reductions | Per `proof-architecture-v1` §3 + §4 — no fabricated outcome metrics. The Phase 1 v2 §2 "hundreds of dollars per dispatch" is hedged here; service-economics math is scoped per OEM in conversation. |
| Add competitor / connected-equipment-vendor names (Sight Machine, MachineMetrics, ThingWorx, etc.) | Per `proof-architecture-v1` §8 — competitive framing is sales-objection-guide territory. The §3.3 "How this differs" callout names the CATEGORY (closed connected-equipment platforms) without naming products. |
| Add customer logos, OEM customer names, or named deployment stories | Per `proof-architecture-v1` §4 + positioning v3 §4 + amendment v4 — Phase 2/E has no customer-logo authorization; named stories wait for Phase 3 sign-off. |
| Use *"smart machine"* / *"AI-enabled equipment"* / *"IoT for machines"* / *"digital twin"* / *"seamless"* / *"future-proof"* | Per buyer-taxonomy §2.6 vocabulary discipline — OEM buyers read these as consumer-marketing flavored or overloaded; the specific service economics matter, not the buzzword. |
| Lead the hero with products instead of the outcome | Per §15 SolutionPanel anti-pattern — the hero leads with "Ship connected equipment. Diagnose remotely. Cut truck rolls.", not "EdgeConnect + EREMOS V2". |
| Replace `ArchitecturePanel.interactive` (variant=`solution-annotated`) with a static image | Per §15 anti-pattern — solution pages need annotated subsets, not generic diagrams. This is precisely what the migration upgrades vs the Phase 1 v2 static SVG; the customer-site-boundary treatment is unique to this page. |
| Add a Typical Engagement timeline section | OMITTED for this buyer per P-A (see §5 + header structural note) — the OEM's decisive anxiety is customer-IT acceptance + service economics, not a deployment clock. Re-adding it would re-introduce the documented over-ceiling for no buyer benefit. |
| Sand off the Phase 1 voice character | Per migration plan D6 — the hero triad, "Your monitoring platform becomes the reason the deal stalls", "Your customer controls their data; you get the service visibility you need", "diagnose before the customer calls", and "Co-existence by design" are retained voice choices. |

---

## 7. Sign-off checklist (v3 lock)

- [x] Page copy word count reconciled: **~1,640 total**; 10-section SolutionPanel core within the 1,500-1,800 band; NO over-ceiling (optional Typical Engagement omitted per P-A). Header / §5 / this line agree.
- [x] All 10 sections present per SolutionPanel layout; optional Typical Engagement section OMITTED with documented rationale (P-A; deployment anxiety is NOT this buyer's decisive objection)
- [x] §3.1 hero leads with outcome ("Ship connected equipment. Diagnose remotely. Cut truck rolls."), not products; hedged "cut" verb preserved verbatim
- [x] §3.1 subhead + trust strip drop MT-LINKi from the today-list (roadmap mention only) and add OPC UA Client + Siemens S7 (P-G corrections)
- [x] §3.3 "How this differs from closed connected-equipment platforms" callout present, emphasizing customer-controlled telemetry as the differentiator (§9 emerging-pattern governance)
- [x] §3.3 names the contributing pillars (Asset Intelligence lead + Connectivity & Edge inline) with `/capabilities/<pillar>` cross-links (NEW §15 ecosystem-framing addition)
- [x] §3.3 "customer controls what flows back to you" paragraph rendered with slight visual prominence (the central trust message); voice leads preserved
- [x] §3.4 What's Included follows §15 schema (2 buckets relabeled For Your Machine / For Your Service Organization; `hardwareProducts` omitted — bucket-narrative rationale documented per P-B)
- [x] §3.4 EdgeConnect deployment note honest (Windows today, embed on OEM/customer-supplied box, Linux + Edge Gateway appliance near-term roadmap, appliance optional)
- [x] §3.4 + §3.5 Q-context protocol lists: FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 today; OPC UA Server sink for customer SCADA; MT-LINKi REST roadmap
- [x] §3.5 inline FAQ uses `FAQPage` schema markup per §9 governance; reframed from v2's 7 Qs to 6 (pricing-mechanics Q dropped → scoping call; white-label folded into Q5)
- [x] §3.5 Q1 (customers say no) anchors customer-controlled routing as trust position; Q2 says "beside, not replacing" re SCADA/MES
- [x] §3.5 Q3 (air-gap / no-cloud) describes offline + scheduled export; Q4 (refuse-then-accept) says machine works either way
- [x] §3.6 outcomes use "cut" / "reduce" framing, NOT "eliminate" / "no" / "zero"; corpus hedged-verb precedent preserved
- [x] §3.6 omits truck-roll-savings percentages and per-dispatch dollar figures (proof-architecture v1 §3 + §4)
- [x] §3.7 architecture uses `ArchitecturePanel.interactive` variant=`solution-annotated`, NOT a static image; customer-site-boundary + customer-controlled-route arrows emphasized; annotations honor §5.A + P-E (eyebrow = ≤4-word title)
- [x] §3.8 trust cues cover customer-controlled/offline-first AND per-machine identity + hash-chained audit; cross-link `/security`; placement after Architecture per P-D
- [x] §3.9 cross-lens cards match the LOCKED §17 preset; related-pillar card leads with Asset Intelligence per P-C (differentiating pillar)
- [x] §3.10 final CTA uses OEM-preferred framing ("Request an OEM scoping call" / "Bring us your installed base") and is vertical-localized; "no multi-year commitment" line preserved
- [x] EdgeConnect + EREMOS V2 + mTracker positioning matches the LOCKED `/capabilities/connectivity-edge` + `/capabilities/asset-intelligence` specs
- [x] No vocabulary that backfires per buyer-taxonomy §2.6 (no "smart machine" / "AI-enabled equipment" / "IoT for machines" / "digital twin")
- [x] No customer logos, no customer names, no fabricated metrics, no competitor/connected-equipment-vendor names
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 metadata block present per §9 metadata governance
- [x] Inline FAQ present per §9 per-page-type FAQ governance (`/solutions/<solution>` is YES)
- [x] Phase 1 v2 voice character preserved (D6)
- [x] Per-gateway identity / customer-data-control language traces to `shared-knowledge/contracts/eremos-per-tag-mqtt.md` + CLAUDE.md §3 lock #19
- [x] Inherited pattern-setter precedents applied (P-A omit-decision documented; P-B bucket relabel; P-C Asset-Intelligence lead; P-D trust-cue placement; P-E annotation eyebrow; P-F hero subset; P-G MT-LINKi/S7/OPC-UA-Client corrections)
- [x] Batch ChatGPT review pass applied
- [x] Pre-lock validation workflow PASSED (cross-spec drift + §15/§9 coverage + discipline-lock guard) across the batch of 4

---

## 8. Out of scope for v1 (v3 content)

- **Full EdgeConnect protocol coverage table.** Phase E `/edgeconnect` covers the full matrix with semantic modes (FOCAS2 polled vs subscription, OPC UA Server security profiles), per-protocol integration test patterns, MT-LINKi REST detail.
- **Full Asset Intelligence / mTracker capability detail.** `/capabilities/asset-intelligence` covers fleet visibility / service-hours / warranty as a pillar; this page cross-links rather than duplicating.
- **Full EREMOS V2 capability detail.** `/capabilities/operational-intelligence` covers alarms / multi-tenant as a pillar; cross-link, don't duplicate.
- **Per-pillar capability detail.** `/capabilities/connectivity-edge` (LOCKED v2.1) covers EdgeConnect + Edge Gateway; cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1) covers the cross-pillar Industrial Intelligence Stack + per-gateway identity; cross-link for the full stack story.
- **OEM pricing / licensing detail.** Handled in the OEM partner conversation; the page routes to a scoping call and does not expose pricing.
- **White-label / co-branding contractual terms.** Surfaced as a benefit (§3.5 Q5); specifics belong in OEM partner agreements.
- **Service-economics calculator (truck-roll cost × dispatches × remote-diagnosis fraction).** A separate commercial asset; the page does not assert the math.
- **Product-engineering feedback-loop integration with PLM / CAD systems.** Future deliverable if demand emerges (Phase 1 v2 §"out of scope").
- **Plant-operator / CNC / brownfield / multi-site framings.** The sibling solution pages (their own v3 migrations in this Phase E wave) cover those outcomes; this page is the equipment-builder cut.
- **Security walkthrough.** `/security` covers the full operational trust posture; this page cross-links from §3.8.
- **Real OEM customer case studies / named deployment stories.** Phase 3 customer-story sign-off process.

---

*`/solutions/oem-machine-monitoring` Page Spec **v1 LOCKED 2026-06-04 (page content v3 — SolutionPanel migration of the Phase 1 v1→v2 page copy)**. Phase E batch member, drafted against the LOCKED CNC pattern-setter (`page-solutions-cnc-machining-spec-v1.md`); LOCKED after the batch ChatGPT review + pre-lock validation workflow (run wf_e86046ac-cdb — 0 HIGH; 2 MED applied); ships as one wave with its 3 sibling migrations (design-system v3 §15 Q3 bulk-migration lock). Migrates `solution-oem-machine-monitoring-v2.md` into the §9 canonical per-page-spec format + SolutionPanel §15 layout, adding the four §15 ecosystem-framing additions (pillar cross-refs, trust cue, ArchitecturePanel.interactive, cross-lens), §1.4 metadata, inline FAQ with FAQPage schema, and the "How this differs from closed connected-equipment platforms" callout (customer-controlled telemetry as differentiator). Inherited precedents applied: P-A — Typical Engagement OMITTED (first Phase E migration to do so; the buyer's anxiety is customer-IT acceptance + service economics, not a deployment clock — page stays in-band with no over-ceiling); P-B — whatsIncluded buckets relabeled For Your Machine / For Your Service Organization; P-C — cross-lens related-pillar card leads with Asset Intelligence (the differentiating pillar for this buyer); P-D — trust-cue after Architecture; P-E — annotation eyebrow as ≤4-word title; P-F — hero subhead carries the OEM protocol subset; P-G — MT-LINKi → roadmap, S7 + OPC UA Client → today (the correctness fixes this migration surfaces). Source-of-truth alignment baked in: customer-controlled routing; per-machine / per-gateway identity (anti-multi-plant-EdgeConnect); EdgeConnect Windows-today / Linux-roadmap; beside-not-replacing via OPC UA Server sink; anti-overclaim "cut truck rolls" hedged verb preserved verbatim (the corpus precedent). Phase 1 voice character preserved (D6). Primary buyer: OEM machine builder (buyer-taxonomy §2.6). Cites: page-capabilities-hub-spec-v1 §9, design-system-v3 §15/§16/§17/§5.A, buyer-taxonomy-v1 §2.6, proof-architecture-v1 §3/§4/§8, page-capabilities-asset-intelligence-spec (lead pillar), page-capabilities-connectivity-edge-spec-v1 v2.1, page-architecture-spec-v1 v2.1, page-solutions-cnc-machining-spec-v1 v1 (pattern-setter), page-solutions-edge-connectivity-spec-v1 v2.1 + page-solutions-predictive-maintenance-spec-v1 v2 (sister IS/IS-NOT boundaries), solution-oem-machine-monitoring-v2 (migrated source + anti-overclaim precedent), shared-knowledge/contracts/eremos-per-tag-mqtt.md, 2026-06-04-phase-e-solution-migration-plan.md.*
