<!--
File:        docs/marketing/buyer-taxonomy-v1.md
Purpose:     Canonical buyer model for the Elpis Industrial Intelligence
             Ecosystem. Governs copy tone, CTA wording, proof selection,
             vocabulary discipline, and surface-routing across every
             Phase 2 page spec, future campaign, ad landing page,
             distributor enablement asset, and sales deck.
Audience:    Internal — Claude (page-spec author for Phase 2), user +
             ChatGPT (reviewers), marketing copywriters, sales / BD
             team, distributor partners, future hires.
Format:      Markdown taxonomy doc. Per-buyer profile with consistent
             fields (pain, primary surface, CTA preference, proof
             expectations, vocabulary sensitivity, common objections).
Companion:   phase2-ia-scope-memo-v2.md + amendment v3 (parent IA
                model — promotes buyer taxonomy to first-class doc)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview; §8 introduces industries; §2
                introduces capability pillars buyers map to)
             hardware-ecosystem-map-v3.md §8 (buyer-to-pillar map
                that this doc extends with surface routing, CTA,
                proof, and vocabulary discipline)
             design-system-v3.md §1 Button + §8 CTASection (the CTAs
                buyers see)
             design-governance-v1.md (the discipline parent)
Version:     v1 — LOCKED after Pass 1 user + ChatGPT review
Date:        2026-05-28
Status:      LOCKED.

Pass 1 ChatGPT review verdict (2026-05-28): "Approve with minor
additions." Two surgical changes applied at lock:
  - §2.8 NEW — System Integrator / EPC / Automation Partner reserved
    as future Buyer #8 (Phase 3+). Not active in Phase 2; the slot
    is reserved so the future addition doesn't require restructuring
    the taxonomy.
  - §3 reverse mapping — `/capabilities/asset-intelligence` flipped:
    Plant manager / Ops VP is now primary; OEM machine builder is
    now secondary. Reflects the broader reality that operations /
    multi-site visibility consume asset-intelligence content more
    often than OEM service organizations.
  - §6 — buyer-journey-stage overlay added as explicit future
    deliverable (NOT for v1).

Per Phase 2 IA memo amendment v3 §3.1 — this is the first of two
governance docs committed before per-page specs begin. v1 LOCKED
after Pass 1 review.
-->

# Elpis Industrial Intelligence Ecosystem — Buyer Taxonomy v1

**Canonical buyer model. Seven buyers, each with a profile that governs how every Phase 2 surface speaks to them.**

This is not a sales prospect list. It is the **language-and-routing contract** that every Phase 2 page spec, every CTA, every proof anchor, every ad landing page, and every distributor-handoff document inherits from. Page specs that ignore this taxonomy will sound generic; campaigns that ignore it will convert poorly.

The taxonomy extends the buyer-to-pillar map already locked in `hardware-ecosystem-map-v3.md` §8 by adding **four governance layers** the pillar map doesn't cover:

1. **Surface routing** — which Phase 2 surface(s) each buyer lands on
2. **CTA preference** — which CTA framing converts each buyer vs which framing annoys them
3. **Proof expectations** — what evidence persuades each buyer
4. **Vocabulary sensitivity** — terms that land vs terms that backfire

Per Phase 2 IA memo amendment v3 §3.1, this is one of two pre-spec foundation documents (the other being `proof-architecture-v1.md`). Both must lock before the 10 per-page specs begin.

---

## 1. The seven buyers (at-a-glance)

| # | Buyer | Lens | Primary Phase 2 surface |
|---|---|---|---|
| 1 | CTO / CIO | Enterprise architecture, vendor consolidation | `/platform` |
| 2 | Plant manager / Ops VP | Operational outcomes, OEE accountability | `/solutions/<outcome>` |
| 3 | OT Architect / SCADA engineer | Technical integration, protocol coverage | `/architecture` |
| 4 | Maintenance Manager / AMC provider | Reliability engineering, condition signals | `/solutions/predictive-maintenance` |
| 5 | Plant engineer (retrofit / greenfield) | Hands-on implementation, hardware specs | `/capabilities/data-acquisition` or `/capabilities/connectivity-edge` |
| 6 | OEM machine builder | Customer-respecting connected equipment | `/solutions/oem-machine-monitoring` *(existing v2 → v3 in Phase E)* |
| 7 | Procurement / compliance reviewer | Vendor security review, contract risk | `/security` |

Each buyer has a primary surface (where they land first) and 1-2 secondary surfaces (where they go next). The full mapping is in §3.

---

## 2. Per-buyer profiles

### 2.1 CTO / CIO

**Role context.** Enterprise technology leader. Often signs the contract; rarely the first reader. Lands here after a peer recommendation, an analyst report, an RFP shortlist, or a board-deck reference.

**Primary pain.** Vendor sprawl. OT/IT integration debt accumulating across years of point-tool acquisitions. Compliance load growing faster than security headcount. Pressure to consolidate technology spend without breaking what works on the floor.

**What they're trying to achieve.** Defensible architecture for the next 5 years. One vendor relationship instead of seven. Predictable cost. Audit-ready posture. A platform their board narrative can survive.

**Primary surface:** `/platform`
**Secondary surfaces:** `/security`, `/architecture`
**Tertiary:** `/capabilities` (rarely; usually delegates to OT Architect)

**CTA preference.**
- ✓ "Talk to engineering" — feels collegial, low-pressure
- ✓ "Request an architecture review" — signals seriousness without commitment
- ✗ "Book a demo" — too consumer-SaaS; CTOs don't sit through demos
- ✗ "Start your free trial" — N/A for industrial platform

**Proof expectations.**
- Architecture diagrams that a peer-tier CTO would respect
- Customer logos from peer-tier industrial enterprises (the homepage trust band lands hardest with this buyer)
- Security posture documentation (not certifications they can't verify — concrete operational primitives)
- Strategic narrative coherence (vendor identity, roadmap discipline, governance maturity)
- Honest framing on compliance — *"we have these primitives; we'll formally certify when a customer requires it"* outperforms certification theater

**Vocabulary that lands:**
- *"OT-native architecture"*
- *"Vendor-agnostic at the protocol layer"*
- *"Edge-first by design"*
- *"Auditable from day one"*
- *"Industrial Intelligence Ecosystem"*

**Vocabulary that backfires:**
- *"Digital transformation"* (consultant-flavored, low-substance)
- *"Industry 4.0"* (dated, vague)
- *"AI-powered"* (consumer SaaS overuse; CTOs are skeptical)
- *"Synergy"* (corporate-speak; immediately discredits)
- *"Revolutionary"* (anything-revolutionary signals novice marketing)

**Common objections (cross-link to sales objection guide):**
- *"We already have an internal IoT initiative"* → build-in-house
- *"We're standardizing on AWS/Azure"* → cloud IoT platform
- *"We have Ignition / Wonderware / Kepware"* → SCADA / OPC-server incumbents

---

### 2.2 Plant manager / Ops VP

**Role context.** Operational owner of one or more plants. Reports to a COO or VP of Manufacturing. Lives in cycle-time variance, OEE numbers, shift handover reports, and customer-quality audits.

**Primary pain.** Mixed-vendor controllers producing OEE numbers that don't reconcile. Stitching reports together by hand. Downtime detected in retrospect. Manual shift reports drifting. Quality audits asking for evidence the current systems can't produce.

**What they're trying to achieve.** Less downtime. Defensible OEE. Less manual reporting. One operational view that survives a customer audit. Time back for actual production management instead of report-stitching.

**Primary surface:** `/solutions/<outcome>` (predictive-maintenance, edge-connectivity, or one of the existing 5 v2 pages depending on which outcome dominates)
**Secondary surfaces:** `/capabilities/operational-intelligence`, `/platform`

**CTA preference.**
- ✓ "Book a scoping call" — concrete next step, low pressure
- ✓ "Bring us your most demanding cell" / "your oldest CNC" / "your fleet" — consultative, respects their skepticism
- ✗ "Talk to engineering" — too technical-flavored; they want commercial conversation
- ✗ "Request an architecture review" — wrong scope for ops audience

**Proof expectations.**
- Outcome-specific metrics (downtime reduction, OEE improvement, reporting time saved)
- Customer stories (anonymized OK in Phase 1-2 per positioning v3 §4)
- Operational realism in the language — *"three shifts a day, mixed Fanuc and Brother CNCs"* outperforms *"24/7 operations across heterogeneous controller fleets"*
- Tool-life / cycle-time / parts-count specifics — proves the platform speaks their floor's language
- "Replace spreadsheet operations" framing — the strongest single commercial moment for this buyer

**Vocabulary that lands:**
- *"OEE you can defend"*
- *"Cycle-time variance"*
- *"Shift handover"*
- *"Mixed-vendor cells"*
- *"Audit-ready"*
- *"Replace spreadsheet operations"*
- Real protocol names (FOCAS2, Brother HTTP) — trust signals

**Vocabulary that backfires:**
- *"Digital transformation"* (consultant-speak; ops VPs are too pragmatic)
- *"Smart factory"* (overused; means nothing operationally)
- *"AI insights"* (skeptical until proven; "AI" can imply autonomous changes they don't want)
- *"Single source of truth"* (overused; sounds like cliché)

**Common objections:**
- *"We already have SCADA"* → existing-SCADA coexistence
- *"Won't this disrupt production?"* → deploy-incrementally framing
- *"How long does this take?"* → typical-engagement timeline (week 1 / weeks 2-4 / weeks 5-8)

---

### 2.3 OT Architect / SCADA engineer

**Role context.** Technical implementer. Designs the data path. Owns the protocol decisions. Reports to plant IT or a corporate OT architecture function. Reads documentation carefully; tests claims; remembers vendors who oversold.

**Primary pain.** Protocol fragmentation. Per-machine custom mapping that drifts when controllers change. Vendor lock-in built into existing tools. Security review cycles that delay every new deployment.

**What they're trying to achieve.** Architectural clarity. Protocol coverage that doesn't quietly drop their oldest controllers. Integration patterns that survive an audit. A platform that doesn't fight their existing SCADA / MES / historian.

**Primary surface:** `/architecture`
**Secondary surfaces:** `/capabilities/connectivity-edge`, `/security`

**CTA preference.**
- ✓ "Talk to an engineer about <pillar>" — direct technical conversation
- ✓ "Request an architecture review" — signals serious technical engagement
- ✗ "Book a scoping call" — feels too sales-flavored
- ✗ "Talk to sales" — they'll qualify out

**Proof expectations.**
- Full protocol coverage list with specific models (FOCAS2 0i / 16i / 18i / 21i / 30i / 31i / 32i specifically; not "Fanuc CNCs")
- Architectural depth — three-way diagnostics, per-tag quality codes, canonical vocabulary, store-and-forward mechanics
- OPC UA Server security modes named explicitly
- Hash-chained audit log existence (a single sentence is enough — they verify, not believe)
- Real integration patterns (MQTT publish modes, OPC UA Server browse paths)

**Vocabulary that lands:**
- *"Protocol-agnostic by architecture"*
- *"Edge runtime"*
- *"OPC UA Server"*, *"Modbus TCP"*, *"FOCAS2"*, *"MTConnect"* — real protocol names always
- *"Three-way diagnostics"*
- *"Canonical CNC vocabulary"*
- *"Per-tag quality codes"*
- *"Hash-chained configuration audit"*

**Vocabulary that backfires:**
- *"Intuitive"* (vague; OT architects don't trust the word)
- *"Easy"* (industrial integration is never easy; the word destroys credibility)
- *"No-code"* (defensive instinct; they want to see the code)
- *"Future-proof"* (overpromise; they know nothing is)
- *"Seamless integration"* (cliché; they'll roll their eyes)

**Common objections:**
- *"Why not just build it?"* → build-in-house objection (engineering driver savings argument lands here specifically)
- *"How does it differ from Kepware / Ignition?"* → competitive reframing
- *"Will it work with our existing SCADA?"* → coexistence patterns

---

### 2.4 Maintenance Manager / AMC provider

**Role context.** Reliability-engineering buyer. Sometimes in-house (Maintenance Manager); sometimes contracted (AMC = Annual Maintenance Contract provider). Both run on condition data and predictive insight. Lives with the constant tension between break-fix culture and predict-and-prevent ambition.

**Primary pain.** Discovering failures at the moment they happen. No visibility into condition signals (vibration patterns, oil contamination, tool wear). Manual handheld spot-checks that miss everything between visits. Vendor monitoring tools that lock data to one machine vendor.

**What they're trying to achieve.** Move from break-fix to predict-and-prevent on rotating machinery and hydraulic systems. Catch failures three weeks early instead of three hours late. Document the diagnostic for the customer (AMC scenario) or for the corporate maintenance review (in-house scenario).

**Primary surface:** `/solutions/predictive-maintenance`
**Secondary surfaces:** `/capabilities/condition-monitoring`, `/capabilities/asset-intelligence`

**CTA preference.**
- ✓ "Bring us your most-watched machine" — respects their skepticism
- ✓ "Talk to a reliability engineer" — peer-to-peer language
- ✓ "Book a scoping call" — works if framed around a specific machine
- ✗ "Book a demo" — they want to see real signals from real machines, not slideware

**Proof expectations.**
- Defense / space-agency anchor (anonymized — already authorized per positioning v3 §4)
- AMC channel acknowledgment (the existing-buyer reality matters; doesn't get hidden)
- VAS + E-IDOS analytical depth (FFT, ISO/NAS cleanliness, time-domain + frequency-domain)
- Real sensor partner names (HYDAC, Filtrec, Parker, MP Filter, Argo-Hytos for E-IDOS; sensor-agnostic design specifically)
- Tool-life tracking patterns

**Vocabulary that lands:**
- *"Vibration analysis"*, *"FFT"*, *"order analysis"*, *"bearing fault detection"*
- *"Oil health intelligence"*, *"ISO 4406 / NAS 1638 cleanliness"*
- *"Predictive maintenance"*, *"condition monitoring"*
- *"AMC provider channel"* — explicit acknowledgment lands when the buyer IS one
- *"Reliability engineering"*
- *"Hydraulic and lubrication systems"*

**Vocabulary that backfires:**
- *"Real-time analytics"* (vague; they want specific analyses named)
- *"Big data"* (irrelevant; they care about right data, not big data)
- *"ML/AI predictions"* without architectural detail (alarms them — they need to know what the model does, not that it exists)
- *"Self-healing"* (overpromise — maintenance buyers know nothing self-heals)

**Common objections:**
- *"We have an OEM monitoring tool"* → OEM-tool fragmentation reframing
- *"We have a different vibration analyser"* → sensor-agnostic design (E-IDOS specifically supports multi-vendor sensors)
- *"How does this integrate with our CMMS?"* → not currently in Phase 2 scope; honest framing required

---

### 2.5 Plant engineer (retrofit / greenfield)

**Role context.** Hands-on implementation engineer. Selects hardware, designs the field wiring, owns the deployment-day checklist. Often the person who installs EdgeConnect at the customer site. Reads spec sheets in the morning and pulls cable in the afternoon.

**Primary pain.** Deployment complexity. Hardware compatibility surprises. Integration test patterns that don't survive contact with real plant wiring. Vendor documentation that assumes ideal conditions.

**What they're trying to achieve.** Clear deployment guides. Hardware specs that match real industrial environments (24V DC, DIN-rail mount, ruggedized I/O ratings). Integration test patterns they can run on a Monday. Technical support that picks up the phone.

**Primary surface:** depends on use case —
- `/capabilities/data-acquisition` (when bypassing PLC — direct sensor acquisition with mDAQ)
- `/capabilities/connectivity-edge` (when integrating with existing PLC infrastructure — EdgeConnect + Edge Gateway)

**Secondary surfaces:** `/architecture`, `/capabilities/operational-intelligence`

**CTA preference.**
- ✓ "Talk to an engineer about <specific pillar>" — they want technical conversation
- ✓ "Request a BOM scope" — concrete next step
- ✓ "Get hardware specifications" — practical
- ✗ "Book a scoping call" — sales-flavored
- ✗ "Request an architecture review" — wrong scope (architecture is for the OT Architect; plant engineers execute)

**Proof expectations.**
- Hardware certifications when available (CE / UL / FCC / IEC / IP rating — open question per positioning v3 §11)
- Specific I/O channel counts, voltage ranges, environmental specs
- Form-factor specs (size in mm, mounting type, connector standards)
- Real install photos (Phase 3+ when available)
- Deployment patterns that survive real plant constraints

**Vocabulary that lands:**
- *"PLC-fronted"*, *"Modbus RTU"*, *"4-20 mA"*, *"24 V DC"*
- *"DIN-rail mount"*, *"M12 connectors"*, *"IP65"*
- *"Per-channel acquisition rate"*, *"signal conditioning"*
- *"Cabinet integration"*, *"retrofit-friendly"*
- *"Field wiring"*, *"loop power"*

**Vocabulary that backfires:**
- *"Platform"* / *"Ecosystem"* (too abstract — they want to know about the box, not the strategy)
- *"Solution"* (consultant-flavored)
- *"Enterprise architecture"* (wrong audience scope)
- *"Cloud-native"* (often a deal-breaker if their site has no internet)
- Marketing-flavored abstractions in general — plant engineers want specifics

**Common objections:**
- *"What's the BOM cost?"* → modular pricing teaser on `/platform`; full pricing Phase 3
- *"How does it handle X PLC family?"* → protocol coverage detail
- *"What if there's no PLC at all?"* → mDAQ (direct sensor acquisition)
- *"What about power loss?"* → store-and-forward, optional battery backup

---

### 2.6 OEM machine builder

**Role context.** Equipment vendor — builds machines, ships them to industrial customers, supports the installed base. Either an OEM product manager (deciding the connectivity strategy for next-generation equipment) or an OEM service director (running the existing service organization). Often the same person at smaller OEMs.

**Primary pain.** Customer-IT pushback on telemetry. Service truck rolls that should have been remote diagnostics. No fleet visibility on shipped equipment. Building a connected-equipment platform in-house and stalling at the customer-IT conversation.

**What they're trying to achieve.** Ship connected equipment that customers actually accept. Remote diagnostics that cut truck-roll cost. Fleet visibility for warranty / service-hours billing. A connectivity story that differentiates equipment instead of slowing the sale.

**Primary surface:** `/solutions/oem-machine-monitoring` *(existing v2; will get v3 bump in Phase E with the new ecosystem framing)*
**Secondary surfaces:** `/capabilities/asset-intelligence`, `/platform`

**CTA preference.**
- ✓ "Bring us your installed base" — consultative, scoped
- ✓ "Talk to OEM partnerships" — peer-to-peer with whoever handles channel
- ✓ "Request an OEM scoping call" — qualifies them as OEM specifically
- ✗ Generic "Book a demo" — wrong context

**Proof expectations.**
- OEM partnership references where available (Phase 3+ when AMC / OEM customer stories are sign-off-ready)
- Service-economics math (truck-roll cost × dispatches × remote-diagnosis fraction)
- Customer-data-control governance (the platform respects customer choice — proven via route-based architecture)
- White-label / co-branding option acknowledgment (kept high-level in Phase 1-2; detailed terms in partner conversation)
- mTracker for service-hours billing / warranty / fleet visibility

**Vocabulary that lands:**
- *"Service-hours billing"*, *"truck rolls"*
- *"Warranty fleet"*, *"installed base"*
- *"Remote diagnostics"*, *"diagnose before the customer calls"*
- *"Customer-controlled telemetry"* — the differentiator
- *"White-label"*, *"OEM partnership"*
- *"Service organization"*, *"field service"*

**Vocabulary that backfires:**
- *"Smart machine"* (consumer-marketing flavored)
- *"AI-enabled equipment"* (skeptical; customers will ask hard questions)
- *"IoT for machines"* (vague; the specific service economics matter)
- *"Digital twin"* (overloaded term; means different things to different OEMs)

**Common objections:**
- *"How do we white-label this?"* → partner-program conversation
- *"Will our customers accept the telemetry?"* → customer-controlled-data architecture
- *"Can we differentiate by offering this vs not offering it?"* → yes, but framing matters

---

### 2.7 Procurement / compliance reviewer

**Role context.** Risk / contract gatekeeper. Reviews vendor security posture before procurement signs the PO. Reads vendor questionnaires, runs security review meetings, owns the no-go decision when documentation is thin. Rarely the first reader; always the last reviewer.

**Primary pain.** Vendor security review backlog. Marketing material that overclaims compliance. Vendors who can't produce concrete operational documentation. Contracts with too much "trust us" and too little verifiable architecture.

**What they're trying to achieve.** Clear security posture they can document. No-overclaim language they can quote in their own internal review. Predictable contract terms. Audit-defensible architecture they can show to their CISO without flinching.

**Primary surface:** `/security`
**Secondary surfaces:** `/platform`, `/architecture`

**CTA preference.**
- ✓ "Request a security review" — exact match for their role
- ✓ "Download our security posture documentation" — they want to circulate it internally
- ✓ "Talk to our security lead" — peer-to-peer
- ✗ "Book a demo" — wrong audience
- ✗ "Talk to sales" — they'll re-route internally

**Proof expectations.**
- Hash-chained audit log existence (concrete operational primitive)
- Signed offline licensing (RSA-signed, no phone-home — verifiable)
- AI constraint architecture (proposes, never alters — verifiable in the runtime contract)
- Role separation supported (concrete capability)
- OPC UA Server security modes (Sign / SignAndEncrypt / X.509 — verifiable in the spec)
- **Honest compliance posture** — *"we have these primitives; ISO 27001 / SOC 2 / IEC 62443 / 21 CFR Part 11 are not currently certified; we'll pursue them when a customer's deployment requires it"* — outperforms overclaim every time
- The "we will not stage a certification mid-sales-cycle to win a deal" line specifically

**Vocabulary that lands:**
- *"Audit trail"*, *"hash-chained"*, *"tamper-evident"*
- *"Offline-capable"*, *"no phone-home"*, *"air-gapped"*
- *"Role separation"*, *"least privilege"*
- *"Compliance-aware"*, *"operational primitives"*
- *"Honest compliance posture"* — the meta-vocabulary that signals maturity

**Vocabulary that backfires:**
- *"Military-grade security"* (red flag — almost always overclaim)
- *"Zero trust"* (overused; without architectural evidence it's marketing)
- *"AI-driven security"* (worse than nothing — implies opacity)
- *"Bank-grade encryption"* (cliché; security reviewers ignore it)
- Any specific compliance framework claim (ISO, SOC, IEC, FDA) without the actual certification

**Common objections:**
- *"Where's your SOC 2 / ISO 27001 / IEC 62443?"* → honest compliance posture (`/security` §6)
- *"Send your security questionnaire response"* → architectural-primitives documentation
- *"What's your incident response process?"* → currently roadmap, not feature (honest framing)

---

### 2.8 Reserved future buyer: System Integrator / EPC / Automation Partner *(Phase 3+, NOT active in v1)*

**Why reserved, not active.** In India and the Middle East specifically (two of Elpis's primary geographies), a large percentage of industrial projects are influenced by:
- Automation contractors who scope and deliver plant projects on behalf of end customers
- System integrators who select the platform stack as part of an integration engagement
- EPC (Engineering, Procurement, Construction) partners who specify connectivity as part of large capital projects
- Panel builders who include EdgeConnect / Edge Gateway in panel designs
- Industrial solution providers who package the ecosystem for vertical-specific deployments

This buyer is **distinct from**:
- The OEM machine builder (§2.6) — OEMs build the equipment; SIs / EPCs integrate equipment from multiple OEMs
- The AMC provider (subset of §2.4) — AMCs run service contracts; SIs / EPCs deliver project work
- The end-customer plant manager (§2.2) — SIs / EPCs sell *into* plants, not from them

**Why not in Phase 2:** the SI / EPC buyer requires distinct content (channel-partner enablement collateral, project-economics framing, deal-registration patterns, distributor handoffs) that Phase 2 doesn't ship. Adding the buyer prematurely would either bloat Phase 2 surfaces or require Phase 2 surfaces to serve a buyer they're not designed for.

**Phase 3+ scope** when this buyer activates: dedicated partner enablement surface (`/partners/<region>` per web-platform-roadmap v2 §5), channel-partner-specific CTAs, project-economics proof patterns, deal-registration flow, and a refresh of `/platform` to surface partner-channel content. The Phase 3+ effort that activates this buyer also resolves the AMC-channel formalization noted in positioning v3 §5.

**Governance for now:** if a Phase 2 conversation surfaces an SI / EPC engagement (real prospect, named project, ad campaign), it gets handled as a sales conversation rather than a Phase 2 surface addition. The taxonomy slot is reserved so the addition lands cleanly when Phase 3+ work formalizes the partner channel.

---

## 3. Surface-to-buyer reverse mapping

Reading the taxonomy from the other direction — for each Phase 2 surface, which buyers primarily land there:

| Surface | Primary buyer(s) | Secondary buyer(s) |
|---|---|---|
| `/platform` | CTO / CIO | Plant manager / Ops VP, Procurement |
| `/capabilities` (hub) | OT Architect (directory entry) | Plant engineer (browsing) |
| `/capabilities/connectivity-edge` | OT Architect, Plant engineer | — |
| `/capabilities/data-acquisition` | Plant engineer (greenfield / PLC-bypass) | OT Architect |
| `/capabilities/asset-intelligence` | Plant manager / Ops VP (multi-site visibility, utilization) | OEM machine builder (service-hours billing, warranty fleet) |
| `/capabilities/condition-monitoring` | Maintenance Manager / AMC provider | Plant manager (reliability-pressured) |
| `/capabilities/operational-intelligence` | Plant manager / Ops VP | CTO / CIO |
| `/architecture` | OT Architect / SCADA engineer | CTO / CIO, Procurement |
| `/solutions` (hub) | Plant manager (outcome-browsing) | All others as entry point |
| `/solutions/predictive-maintenance` | Maintenance Manager / AMC provider | Plant manager (reliability-pressured) |
| `/solutions/edge-connectivity` | OT Architect, Plant engineer | Plant manager |
| `/security` | Procurement / compliance reviewer | CTO / CIO, OT Architect |

**Use this table when authoring page specs:** the per-page tone, CTA, proof selection, and vocabulary should be optimized for the **primary** buyer. Secondary buyers are accommodated via cross-lens navigation (per memo v2 §5.2), not by trying to serve everyone in the page itself.

---

## 4. Cross-cutting patterns

Things that are true across all 7 buyers (or notable contrasts):

- **Specificity beats abstraction across every buyer.** Real protocol names, real machine models, real industry terms, real numbers. The 7 buyers have radically different vocabularies but they all distrust marketing-flavored abstraction equally.
- **Coexistence framing wins.** Every buyer rejects rip-and-replace language. The Industrial Intelligence Ecosystem positions as "we sit alongside what you already have" — the framing works for CTO consolidation (we add, don't replace), Ops VP risk (we don't disrupt production), OT Architect respect (we don't fight your SCADA), and OEM partnership (we layer into your existing service organization).
- **Honest restraint outperforms overclaim with every buyer.** "We have these primitives; we'll certify when needed" outperforms "compliant with everything." "Our AI proposes; humans decide" outperforms "AI-powered everything." Maturity is a buying signal in industrial.
- **CTOs and Procurement read carefully; Plant managers and Maintenance buyers act on first impression; OT Architects and Plant engineers verify everything.** This shapes how dense vs scannable each surface should be.
- **No buyer responds well to consumer-SaaS vocabulary.** "Demo", "trial", "subscribe today", "transformation", "synergy", "easy" — these immediately discredit across all 7. The industrial market remembers vendors who oversold.
- **Defense / space-agency credibility anchor lands hardest with CTOs and Maintenance buyers** (different reasons — strategic legitimacy for CTOs, technical credibility for Maintenance). It lands less with Plant engineers (they care about the specific install, not the prestige) and Procurement (they want documentation, not anecdote).

---

## 5. How to use this in page-spec authoring

For each Phase 2 page spec:

1. **Identify the primary buyer** from the §3 reverse mapping. Optimize the page for that buyer.
2. **Pull the primary buyer's profile (§2.x)** and apply:
   - The CTA preference (which Button label, which CTAGroup framing)
   - The proof expectations (which evidence to surface and where)
   - The vocabulary discipline (which terms to use; which to ban)
3. **Cross-lens to secondary buyers via the §17 navigation pattern** (cross-lens content pattern in design-system v3 §17). Don't try to serve secondary buyers inside the page primary content.
4. **Validate the page against the common objections** for the primary buyer (per their §2.x profile, cross-link to the sales objection guide).

**Sanity test:** read the page spec aloud as the primary buyer. If a sentence makes you wince ("would a Maintenance Manager actually say this?"), rewrite. The taxonomy is the language test.

---

## 6. Out of scope for v1

- **Specific named accounts.** This is a buyer-role taxonomy, not a customer list. Named-account targeting is sales-track work, not marketing-content governance.
- **Geographic variations.** Buyer vocabulary differs slightly across India / Middle East / US / EU. Phase 4+ may introduce regional adaptations; v1 covers the dominant English-language vocabulary.
- **Industry-specific overlays.** Phase 3 may introduce industry-vocabulary overlays (e.g., "pump-station monitoring" for Water & Utilities, "test stands" for Aerospace). v1 covers cross-industry buyer roles.
- **Buyer journey stage modeling.** This doc covers the buyer at the *evaluation* stage — which Phase 2 surfaces they land on, how each page should speak to them, what evidence they need. Awareness-stage content (ad copy, social, content marketing) and post-deployment content (success program, expansion) are out of scope for v1. **Reserved future deliverable:** `buyer-journey-overlay-v1.md` will add a WHEN dimension (Awareness → Evaluation → Validation → Procurement → Expansion) on top of this WHO dimension. Per ChatGPT v1 review, the overlay is *"not required now but eventually valuable."*
- **Sales-team-internal nuance.** This is marketing-content governance. Sales-side buyer-targeting nuance (champion mapping, decision-maker analysis, buying-committee strategy) is a separate sales-enablement asset.

---

## 7. Sign-off checklist

Before v1 lock:

- [ ] All 7 buyers have all 6 governance fields filled (pain, primary surface, CTA preference, proof expectations, vocabulary lands, vocabulary backfires, objections)
- [ ] Reverse mapping table (§3) covers every Phase 2 surface
- [ ] No buyer profile references a Phase 3+ capability as primary (Phase 2 page-spec relevance must be the focus)
- [ ] Vocabulary discipline aligns with positioning v3 + amendment v4 (no fabricated customer references; no certification claims; no consumer-SaaS language)
- [ ] CTA preferences align with design-system v3 §1 Button variants and §8 CTASection patterns
- [ ] Objection-handling cross-references match the sales objection guide v2
- [ ] User reviews and approves the 7-buyer set + 4 governance layers
- [ ] ChatGPT review pass + take-list applied → v2 if needed

---

*Buyer Taxonomy v1 — LOCKED 2026-05-28 after Pass 1 user + ChatGPT review. Extends hardware-ecosystem-map v3 §8 buyer-to-pillar map with four governance layers (surface routing, CTA preference, proof expectations, vocabulary sensitivity). Pass 1 lock additions: §2.8 reserved future Buyer #8 (System Integrator / EPC, Phase 3+); §3 asset-intelligence reverse-mapping flip (Plant manager primary, OEM secondary); §6 buyer-journey-overlay reserved as future deliverable. First of two pre-spec foundation docs per Phase 2 IA memo amendment v3 §3.1; companion `proof-architecture-v1.md` follows immediately.*
