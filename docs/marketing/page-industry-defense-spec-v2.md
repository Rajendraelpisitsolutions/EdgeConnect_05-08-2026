<!--
File:        docs/marketing/page-industry-defense-spec-v2.md
Purpose:     Page spec (v1 DRAFT) for /industries/defense. INSTANCE of the
             LOCKED IndustryShell shape. One of TWO verticals (with aerospace)
             permitted to use the reserved defense/space-agency trust anchor —
             anchor handling is the sharp discipline here.
Audience:    Internal — Claude (draft), copywriters (lifting verbatim text),
             user + ChatGPT (reviewers — defense + aerospace get EXTRA review
             attention for anchor handling + regulated-buyer tone), engineering
             (IndustryShell component implementers).
Format:      Per §9 canonical template; mirrors the LOCKED IndustryShell
             shape-setter EXACTLY.
Companion:   page-industry-heavy-manufacturing-spec-v2.md (LOCKED IndustryShell
                shape-setter — SHAPE SOURCE; this spec is its §1–§9 instance,
                NO §10 of its own; see v2 §10 for the reusable contract, and
                v2 §10.3 for the proof-posture OVERRIDE that permits the
                reserved anchor here)
             phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.1 IndustryShell
                skeleton, §4 proof/anonymity discipline, §5 buyer map, §6
                anti-duplication map, §9 acceptance gate)
             buyer-taxonomy-v1.md §2.2 (operations leader) + §2.3 (OT architect)
                + §2.7 (procurement / compliance reviewer)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             page-capabilities-connectivity-edge-spec-v1.md (protocol wording source)
             design-system-v4.md (IndustryShell lands as v5 §25)
             LOCKED cross-link targets (do NOT duplicate):
                page-solutions-{brownfield-modernization,predictive-maintenance,
                edge-connectivity,multi-site-operations}-spec
                page-capabilities-{connectivity-edge,operational-intelligence,
                condition-monitoring,data-acquisition}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v2 — LOCKED after Pass 1 ChatGPT review (extra anchor scrutiny).
Date:        2026-06-06
Status:      LOCKED. IndustryShell instance (defense). Shape inherited from
                page-industry-heavy-manufacturing-spec-v2 §10. This spec covers
                §1–§9 only; the shape definition is NOT re-stated here (cite v2 §10).

v1 (page-industry-defense-spec-v1.md) retained as historical reference.
v1 -> v2 (ChatGPT Pass-1 anchor-discipline cleanup): §3.3 condition-monitoring
block — removed "defense-ministry" qualifier from the E-IDOS line; now generic
"hydraulic and fluid systems" (no customer/sector specificity beyond the anchor).

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7.
Word-count target: ~1,000-1,400 words page copy. Current: ~1,210 words.
-->

# `/industries/defense` — Page Spec v2 (LOCKED)

**Vertical lens over the existing platform/solution/capability content, told through defense-manufacturing and MRO/sustainment vocabulary and pain. Defense operations / sustainment leader primary; the depot's OT architect secondary; procurement / compliance reviewer over the shoulder. Reader self-identifies ("this is my depot / my isolated network"), sees which solutions fit, gets honest category-level proof anchored to the reserved defense/space-agency anchor, and is routed to the LOCKED owners of the detail. INSTANCE of the `IndustryShell` shape defined in `page-industry-heavy-manufacturing-spec-v2` §10 — this spec fills the per-vertical slots, it does not re-define the shape.**

This is the page a defense operations or sustainment leader lands on from a vertical search ("offline edge monitoring for isolated OT networks", "air-gapped condition monitoring platform") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *this industry's* reality and cross-links to the authoritative owners.

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** A defense-framed entry point. Reader leaves with *"Elpis understands a defense floor and a sustainment depot — air-gapped and isolated OT networks, mixed legacy + modern controllers, hydraulic and rotating equipment that has to be trusted — runs offline-first with no phone-home, keeps a tamper-evident change history, and here are the solutions that fit and where to go next."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer/program/agency/branch, competitor name, or certification / security-clearance / compliance / ITAR / MIL-STD claim (phase3 memo §4 + §9)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Defense operations / sustainment leader (§2.2) — wants to know Elpis grasps an isolated-network floor and a maintenance depot, and that the consequence-heavy equipment (hydraulics, rotating equipment, test rigs) is covered without violating network isolation. CTA preference: *"Talk to us about scoping."*

**Secondary:** The depot's OT architect / maintenance engineer (§2.3) — wants protocol-coverage honesty, offline/air-gapped posture, condition-monitoring reach, and a defensible change history. CTA preference: *"Request an architecture review."*

**Over the shoulder:** Procurement / compliance reviewer (§2.7) — needs evidence that survives scrutiny: offline-first as an *architectural fact*, per-gateway identity, hash-chained audit. (No cert claims — those are §9-gate-banned.)

- Vocabulary that lands: air-gapped, isolated OT network, offline-first, no phone-home, per-gateway identity, hash-chained audit, chain of custody, traceability, MRO / sustainment, depot, ground-support equipment, test rig, mixed legacy + modern controllers, beside-not-replacing.
- Vocabulary that backfires: "Industry 4.0 transformation", "AI-powered", "single pane of glass", "turnkey digitalization", any unqualified percentage, any clearance / compliance / ITAR / MIL-STD claim.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Defense — offline edge intelligence · Elpis* |
| **Meta description** (140-160 chars) | *Offline-first edge intelligence for defense manufacturing and sustainment — collect from mixed controllers on isolated, air-gapped OT networks. No phone-home. Deployed in defense and space-agency programs.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/defense` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup (Phase 3 customer registry handles structured proof later; defense/space-agency customer names are off-record permanently regardless). |

---

## 2. Page structure — sections at a glance (the IndustryShell layout)

8 sections. The shape is the LOCKED `IndustryShell` layout defined in `page-industry-heavy-manufacturing-spec-v2` **§10**; this spec is an instance and does NOT re-define it. Cardinality of the variable sections is fixed at the shape level (v2 §10): **Relevant solutions = 3-5 cards; FAQ = 4-6 Q&A.** Defense uses **4** and **5** respectively.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~110 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (2-3) + pullquote | ~290 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (3-5 cards; Defense uses 4) | ~120 |
| **5** | Proof posture for this industry (anchored cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~130 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 4-6 Q&A (Defense uses 5), `FAQPage` schema | ~230 |
| **7** | Cross-lens navigation | `light-tinted` | §17 cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Defense instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · DEFENSE
>
> HEADLINE (size.3xl semibold):
> Defense floors and sustainment depots run on isolated networks. Your operational view has to live there too.
>
> SUBHEAD (size.lg, max-width 60ch):
> Precision machining beside test rigs, ground-support equipment, and platform maintenance depots — mixed legacy and modern controllers, often on air-gapped or isolated OT networks. Elpis collects from all of it over native protocols, normalizes every signal to one vocabulary, and runs offline-first with no phone-home — without replacing a single machine. Operating across India and the Middle East. Deployed in defense and space-agency programs.
>
> PRIMARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Defense%20architecture%20review`
> SECONDARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Defense%20scoping`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. BOTH locked anchors are reproduced verbatim, each opening its own sentence: "Operating across India and the Middle East." (footprint statement) AND "Deployed in defense and space-agency programs." (category descriptor — NOT a named-customer claim; defense/space-agency names are off-record permanently per the locked external-claim policy). NO program, country-of-program, platform, agency, or branch named. NO clearance / compliance / ITAR / MIL-STD claim — "offline-first / air-gapped" is an architectural fact, not a certification.

### 3.2 Section 2 — The reality in defense

> EYEBROW: THE REALITY ON A DEFENSE FLOOR
>
> SECTION TITLE: Isolated by design. Still expected to be visible.

> NARRATIVE PARA 1:
> Defense work spans two worlds that share a constraint. On the manufacturing side: precision component machining, test rigs, and ground-support equipment, on a mix of legacy and modern controllers. On the sustainment side: vehicle and platform maintenance depots keeping fielded equipment serviceable for years past its original interfaces. The constraint both share is the network. These are security-sensitive environments — OT networks are routinely air-gapped or otherwise isolated, and any tool that needs to phone home, license against a cloud, or open an outbound path is a non-starter before the first conversation.

> NARRATIVE PARA 2:
> So the data stays trapped on the floor. A depot learns a hydraulic system is degrading from a failed acceptance test, not from a trend. A rotating assembly on a test rig develops a bearing fault that nobody sees until it is loud. And every change to how the floor is monitored — every threshold, every added sensor — lives in someone's memory or a local spreadsheet, with no defensible record of who changed what and when. In an environment where traceability and chain of custody are the whole point, an unauditable monitoring layer is its own liability.

> NARRATIVE PARA 3:
> Replacing the controllers isn't the answer — they are validated for what they run, fielded for the long haul, and isolated for good reason. What needs to modernize is the data layer, inside the isolation boundary. The defense operations that get there put one protocol-agnostic runtime in front of every controller, normalize every signal at the edge, instrument the consequence-heavy hydraulic and rotating equipment, keep every reading and every config change on a tamper-evident record — and never ask the network to reach the internet to do it.

> PULLQUOTE: "In an environment where traceability is the whole point, an unauditable monitoring layer is its own liability."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES ON A DEFENSE FLOOR
> SECTION TITLE: The data layer modernizes — inside the isolation boundary.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Speak every controller you already own — on the network you already have.** EdgeConnect polls your mixed floor over native protocols — FOCAS2 for FANUC, Siemens S7 for press and line PLCs, Modbus TCP for older machines and ground-support equipment fronted by a PLC, MTConnect for open-standard machines, Brother HTTP for Brother machining centers, and OPC UA Client where a controller exposes it. It installs and runs on an isolated or air-gapped VLAN exactly as it does on a connected one — the license validates locally, with no phone-home. Canonical vocabulary at the edge means a signal means the same thing whichever machine produced it. FANUC MT-LINKi REST integration is on the roadmap. → `/capabilities/connectivity-edge`
>
> **Watch the hydraulics and the rotating equipment.** E-IDOS reads oil and fluid health — particle contamination and water saturation — on hydraulic and fluid systems (ISO 4406 / NAS 1638). VAS reads vibration signatures on rotating equipment — motors, gearboxes, fans, and the assemblies on test rigs. Both give early warning when a signature crosses a threshold your maintenance team defines — a better trigger than a calendar, not a guarantee against every failure. → `/capabilities/condition-monitoring`
>
> **One traceable operational history — on the isolated network.** EREMOS V2 computes OEE Segments and a traceable history from the edge-collected signals, so the operational picture holds the same meaning across a legacy machine and a modern cell — and it does it without the floor ever leaving its isolation boundary. → `/capabilities/operational-intelligence`
>
> **Reach the signals the controller won't give you.** Where a machine or a test rig exposes nothing useful, mDAQ acquires the sensor signal directly — temperature, pressure, flow, vibration — without waiting on a controller retrofit. → `/capabilities/data-acquisition`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT A DEFENSE FLOOR
> (4 cards → LOCKED solution pages; shape allows 3-5)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · BROWNFIELD MODERNIZATION | Modern monitoring and audit trails from the controllers you already own. | `/solutions/brownfield-modernization` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on hydraulics, rotating equipment, and test rigs — before they fail. | `/solutions/predictive-maintenance` |
| 3 | SOLUTION · EDGE CONNECTIVITY | Collect from mixed controllers on isolated, air-gapped networks. | `/solutions/edge-connectivity` |
| 4 | SOLUTION · MULTI-SITE OPERATIONS | One view across depots and sites — by aggregation, never one runtime stretched across them. | `/solutions/multi-site-operations` |

### 3.5 Section 5 — Proof posture for defense

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built for floors that can't reach the internet — and shouldn't have to.

> TRUST CUE (category-level — NO customer/program/agency/branch names, NO metrics):
> Elpis is deployed across defense manufacturing and sustainment operations — isolated floors with consequence-heavy hydraulic and rotating equipment. **Operating across India and the Middle East.** **Deployed in defense and space-agency programs.** The platform runs offline-first: the license validates locally with no phone-home, and an isolated-VLAN or air-gapped install behaves exactly as an internet-connected one. Per-route store-and-forward is built to preserve every reading through a network or broker drop — queuing locally and replaying in source order on reconnect. Each gateway carries its own identity, and every configuration change is captured in a hash-chained, tamper-evident audit trail.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Defense is ONE OF TWO verticals (with aerospace) permitted to use the reserved **"Deployed in defense and space-agency programs."** anchor (override `page-industry-heavy-manufacturing-spec-v2` §10.3), alongside the geography anchor — both VERBATIM, each opening its own sentence, here and in §3.1. Defense + space-agency **customer names are off-record PERMANENTLY** per the locked external-claim policy (memo §4) — even after any future named-customer sign-off. The category-descriptor anchor is therefore the ENTIRE proof: NO named customer, NO program name, NO country-of-program, NO platform, NO agency, NO branch, NO invented deployment story, NO capability detail beyond the anchor. "Offline-first / air-gapped" is stated as an *architectural fact*, NOT a compliance/clearance/ITAR/MIL-STD certification (those are §9-gate-banned). No metrics. Per phase3 memo §4 + §9 gate.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A (shape allows 4-6), `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What defense teams ask first.

**Q1. Does this run fully offline, on an air-gapped network?**
> Yes. Elpis is offline-first by design. The license validates locally — there is no phone-home, no cloud dependency, and no outbound path required to operate. Installing on an isolated VLAN or a fully air-gapped network behaves exactly the same as an internet-connected one; the network isolation is something we install inside, not something we ask you to relax.

**Q2. Which controllers and equipment can you actually collect from?**
> FANUC over FOCAS2, Siemens controllers over S7, older machines and ground-support equipment fronted by a PLC over Modbus TCP, plus MTConnect, Brother HTTP, and OPC UA Client where it's exposed — all shipping today. FANUC MT-LINKi REST integration is on the roadmap. For hydraulic, fluid, and rotating equipment, E-IDOS and VAS read condition directly. Bring the controller and equipment list to the scoping conversation and we confirm the collection path per asset.

**Q3. Can you monitor our hydraulic and fluid systems and rotating equipment for early failure?**
> Yes — that's what E-IDOS and VAS are for. E-IDOS reads oil and fluid health (particle contamination, water saturation) on hydraulic and fluid systems; VAS reads vibration signatures on rotating equipment. They give early warning when a signature crosses a threshold your maintenance team defines — a better trigger than a calendar, not a guarantee against every failure. (E-IDOS runs standalone today; streaming into EREMOS V2 is on the roadmap. mDAQ runs VAS.)

**Q4. How is our change history protected?**
> Each gateway carries its own per-gateway identity, and every configuration change is captured in a hash-chained, tamper-evident audit trail. The intent is a defensible record of who changed what and when — supporting the traceability and chain-of-custody expectations these environments carry. → `/security`

**Q5. Does this replace our existing control system or SCADA?**
> No. Elpis sits beside them. EdgeConnect reads each controller as a read-only client — it never changes control logic, and no machine comes offline to connect it. Your control system and SCADA keep operator HMIs and control; Elpis modernizes the data layer beside them. → `/architecture`

### 3.7 Section 7 — Cross-lens

Per design-system §17. Preset for `IndustryShell`: `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your floor — even the part that can't reach the internet.
> SUBHEAD: A controller and equipment list, the hydraulic and rotating equipment that worries you, and your network constraints — that's enough to scope an architecture review. We design for your isolation boundary, not around it.
> PRIMARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Defense%20architecture%20review`
> SECONDARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Defense%20scoping`

---

## 4. Components used

All from design-system v4 LOCKED + the `IndustryShell` composition (shape defined in `page-industry-heavy-manufacturing-spec-v2` §10; lands in design-system v5). No net-new primitives.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1 hero; §3.8 CTA |
| Card grid (solution cards) | §3.4 |
| `CapabilityCard` (cross-lens variant) | §3.7 |
| Trust-cue block | §3.5 |
| Inline FAQ (`FAQPage` schema) | §3.6 |
| `CTASection` | §3.8 |

---

## 5. Verbatim copy summary

All copy in §3.1-§3.8. **~1,210 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~115 |
| 3.2 | Reality (3 paras + pullquote) | ~290 |
| 3.3 | What Elpis does here (4 blocks) | ~320 |
| 3.4 | Relevant solutions (4 cards) | ~120 |
| 3.5 | Proof posture | ~130 |
| 3.6 | FAQ (5 Q&A) | ~230 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4:

| Don't | Why |
|---|---|
| Quote a fabricated downtime-reduction / availability-gain / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name a defense customer, program, country-of-program, platform, agency, or branch | phase3 memo §4 — zero named customers; defense/space-agency names off-record **permanently**. The category anchor is the entire proof |
| Invent a deployment story or a capability detail beyond the anchor | The reserved anchor is a category descriptor, NOT a narrative license (memo §4 + override v2 §10.3) |
| Claim a security clearance, compliance certification, ITAR registration, or MIL-STD conformance | §9-gate-banned cert claims. Offline / air-gapped is an architectural fact we CAN state; a certification is NOT |
| Paraphrase either locked anchor | Both reproduced VERBATIM, each opening its own sentence (override v2 §10.3) |
| Re-derive the connectivity / condition-monitoring / operational-intelligence capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| List MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — all three are roadmap; E-IDOS standalone today; mDAQ runs VAS only |
| Imply Elpis replaces the control system / SCADA, or that one runtime spans depots | beside-not-replacing + per-gateway identity (carried in §3.6 Q4 + Q5; §3.4 card 4) |
| Promise VAS/E-IDOS "prevents all failures" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q3) |
| Promise store-and-forward "never" loses data | Use designed-to-preserve language ("built to preserve … replaying in source order") — §3.5 |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.2/§2.3 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos / programs / agencies / branches (the reserved anchor is the only proof; off-record permanently — §3.5 governance note)
- [x] No certification claims (no security-clearance / compliance / ITAR / MIL-STD; offline/air-gapped stated as architecture, not certification — §3.5 + §6. ISO 4406 / NAS 1638 are oil-cleanliness *report codes*, not Elpis certifications)
- [x] No competitor names
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; EdgeConnect Linux roadmap; E-IDOS standalone (EREMOS streaming roadmap); mDAQ runs VAS only
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] BOTH locked anchors reproduced VERBATIM + standalone (per override v2 §10.3): geography + defense/space-agency, in §3.1 + §3.5

---

## 8. Sign-off checklist (v2 LOCKED)

- [x] Page copy within ~1,000-1,400 words (current ~1,210)
- [x] All 8 IndustryShell sections present (§2); shape cited to v2 §10 (no §10 of its own)
- [x] §3.1 hero industry-framed; BOTH anchors verbatim + standalone (override v2 §10.3); no program/agency/branch named; no cert claim
- [x] §3.3 maps pillars via cross-link, never re-derives
- [x] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages (4 cards; shape 3-5)
- [x] §3.5 proof posture category-level + BOTH anchors verbatim & standalone; governance note states defense/space-agency names off-record permanently + anchor is the entire proof; cross-links `/security` + `/customers`
- [x] §3.6 FAQ vertical-calibrated, 5 Q&A (shape 4-6), `FAQPage` schema; offline/air-gapped, protocol, condition-monitoring, audit, and beside-not-replacing honesty intact
- [x] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [x] §7 Phase 3 acceptance gate all green
- [x] Shape inherited from `page-industry-heavy-manufacturing-spec-v2` §10 (no shape re-definition here)
- [x] ChatGPT Pass 1 review (extra anchor scrutiny) applied — removed "defense-ministry" qualifier from §3.3 E-IDOS line; v2 LOCKED

---

## 9. Out of scope for this page

- Named / quantified defense case studies (off-record permanently for defense/space-agency per memo §4; structure-only via `CaseStudyShell` → `/customers`)
- Any security-clearance / compliance / ITAR / MIL-STD certification claim (no real certification to cite; offline/air-gapped is architecture, not certification)
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)

---

*`/industries/defense` Page Spec **v2 LOCKED 2026-06-06** (ChatGPT Pass 1 applied — removed "defense-ministry" qualifier from the §3.3 E-IDOS line; anchor remains the entire proof). INSTANCE of the LOCKED `IndustryShell` shape (shape defined in `page-industry-heavy-manufacturing-spec-v2` §10; this spec is §1–§9 only, no §10 of its own). 8-section vertical-lens layout; cardinality per v2 §10 (solutions 3-5 — Defense uses 4; FAQ 4-6 — Defense uses 5). Defense is ONE OF TWO verticals (with aerospace) permitted the reserved defense/space-agency anchor: BOTH locked anchors reproduced VERBATIM + standalone in §3.1 hero and §3.5 proof posture per override v2 §10.3 ("Operating across India and the Middle East." + "Deployed in defense and space-agency programs."). Proof is category-level ONLY — NO named customers/programs/agencies/branches (off-record permanently per memo §4; the anchor is the entire proof), NO invented deployment, NO metrics, NO security-clearance/compliance/ITAR/MIL-STD certification (offline/air-gapped stated as architecture, not certification). Cross-links the LOCKED `/capabilities/<pillar>`, `/solutions/<solution>`, `/architecture`, `/platform`, `/security` owners; re-derives none (memo §6). Protocol status verbatim (FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST + EdgeConnect Linux + E-IDOS→EREMOS streaming roadmap; mDAQ VAS only). §7 pre-checks the memo §9 acceptance gate (all green). ~1,210 words within target. Cites: phase3-ia-scope-memo-v2 (parent); page-industry-heavy-manufacturing-spec-v2 §10 (shape source) + §10.3 (anchor override); buyer-taxonomy v1 §2.2/§2.3/§2.7; proof-architecture v1 §3/§4/§8; positioning v3 §4; page-capabilities-connectivity-edge-spec v1 (protocol wording); design-system v4 (→ v5 §25 IndustryShell).*
