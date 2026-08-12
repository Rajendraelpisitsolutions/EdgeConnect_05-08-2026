<!--
File:        docs/marketing/page-industry-aerospace-spec-v2.md
Purpose:     v1 DRAFT page spec for /industries/aerospace. An INSTANCE of the
             LOCKED IndustryShell shape (page-industry-heavy-manufacturing-spec-v2
             §10). Aerospace manufacturing + MRO vertical lens. ONE OF TWO
             verticals (with Defense) permitted the reserved defense/space-agency
             trust anchor per the shape-setter §10.3 proof-posture override.
Audience:    Internal — Claude (drafts), copywriters (lifting verbatim text),
             user + ChatGPT (reviewers — defense/aerospace get extra anchor +
             regulated-buyer-tone review attention per memo §8.2 + shape §10.3),
             engineering (IndustryShell component implementers).
Format:      Per the IndustryShell shape-setter (HM v2 §2/§10).
Companion:   page-industry-heavy-manufacturing-spec-v2.md (LOCKED IndustryShell
                shape-setter — SHAPE SOURCE; this spec mirrors its §1–§9 and
                CITES its §10 for the shape + cardinality)
             phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.1 IndustryShell
                skeleton, §4 proof/anonymity discipline, §5 buyer map, §6
                anti-duplication map, §9 acceptance gate, §10.3 override)
             buyer-taxonomy-v1.md §2.3 (OT architect) + §2.7 (procurement /
                compliance reviewer)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             page-capabilities-connectivity-edge-spec-v1.md (protocol wording source)
             design-system-v4.md (extends — IndustryShell lands as v5 §25)
             LOCKED cross-link targets (do NOT duplicate):
                page-solutions-precision-manufacturing-spec / -predictive-maintenance /
                -cnc-machining / -multi-site-operations
                page-capabilities-{condition-monitoring,connectivity-edge,
                operational-intelligence,data-acquisition}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v2 — LOCKED after Pass 1 ChatGPT review (extra anchor scrutiny).
Date:        2026-06-06
Status:      LOCKED. IndustryShell INSTANCE; not a shape-setter. This spec is
             §1–§9 only — it has NO §10 of its own; the shape + cardinality live
             in the shape-setter's §10 (HM v2 §10), cited throughout.

v1 (page-industry-aerospace-spec-v1.md) retained as historical reference.
v1 -> v2 (ChatGPT Pass-1 anchor-discipline cleanup): deleted the §3.5
governance-note parenthetical that explained the space-agency anchor as "VAS
on precision rotating equipment" — the anchor must remain the entire proof
with no capability-detail elaboration.

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7.
Word-count target: ~1,000-1,400 words page copy. Current: ~1,235 words.
-->

# `/industries/aerospace` — Page Spec v2 (LOCKED)

**An INSTANCE of the LOCKED `IndustryShell` shape (HM v2 §10). Vertical lens over the existing platform/solution/capability content, told through aerospace-manufacturing + MRO vocabulary and pain. The OT architect primary; the procurement / compliance reviewer secondary. Reader self-identifies ("this is my line and my test cell"), sees which solutions fit, gets an honest — and uniquely anchored — proof posture, and is routed to the LOCKED owners of the detail.**

This is the page an aerospace manufacturing engineer or MRO reliability lead lands on from a vertical search ("turbine component machining monitoring", "engine test-cell vibration platform", "aerospace per-part traceability edge") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *aerospace reality* and cross-links to the authoritative owners.

The structure, section order, visual modes, and cardinality (Relevant solutions 3-5 cards, FAQ 4-6 Q&A) are defined by the LOCKED `IndustryShell` shape — **HM v2 §10**. This spec fills the per-vertical slots (HM v2 §10.2) and applies the **§10.3 proof-posture override** that admits the reserved anchor.

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** An aerospace-framed entry point. Reader leaves with *"Elpis understands a high-consequence aerospace line and MRO floor — precision CNCs and test cells, rotating equipment that absolutely cannot fail, traceability scrutinized down to the part — and here are the solutions that fit and where to go next."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer, named program/agency/mission, competitor name, or certification claim (phase3 memo §4)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** The aerospace OT architect / manufacturing-engineering + reliability lead (§2.3) — wants protocol-coverage honesty across precision controllers and test-cell instrumentation, condition-monitoring reach on high-value rotating equipment, and a defensible per-part data path. CTA preference: *"Request an architecture review."*

**Secondary:** Procurement / compliance reviewer in a regulated buyer (§2.7) — wants evidence that survives scrutiny: an offline-first posture, a tamper-evident audit trail, and proof that this vendor operates in the company they keep. CTA preference: *"Talk to us about scoping."*

- Vocabulary that lands: precision machining, turbine and structural components, test cells, test rigs, balancing rigs, rotating equipment, engines, turbines, spindles, extreme tolerance, traceability, per-part history, canonical signals, edge-timestamped, store-and-forward, per-site, beside-not-replacing.
- Vocabulary that backfires: "Industry 4.0 transformation", "AI-powered", "single pane of glass", "turnkey digitalization", any unqualified percentage, any compliance-certification claim.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Aerospace — precision, traceability, monitoring · Elpis* |
| **Meta description** (140-160 chars) | *Collect from precision CNCs and test cells, watch the rotating equipment that can't fail, and defend a traceable per-part history — timestamped at the edge.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/aerospace` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup (Phase 3 customer registry handles structured proof later). |

---

## 2. Page structure — sections at a glance

8 sections — the LOCKED `IndustryShell` layout. **Structure, section order, visual modes, component choices, and cardinality are fixed by the shape (HM v2 §10) and do not vary by vertical.** Per the shape's variable-section cardinality: **Relevant solutions = 3-5 cards; FAQ = 4-6 Q&A.** Aerospace uses **4** and **5** respectively.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~115 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (3) + pullquote | ~290 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (3-5 cards; aerospace uses 4) | ~120 |
| **5** | Proof posture for this industry (anchored cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~130 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 4-6 Q&A (aerospace uses 5), `FAQPage` schema | ~220 |
| **7** | Cross-lens navigation | `light-tinted` | §17 cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Aerospace instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · AEROSPACE
>
> HEADLINE (size.3xl semibold):
> In aerospace, the tolerance is on the part and the consequence is on the program. Your data layer has to be as disciplined as your floor.
>
> SUBHEAD (size.lg, max-width 60ch):
> Precision machining of turbine and structural components beside test cells and balancing rigs. FANUC, Siemens, and Mazak controllers next to direct test-cell instrumentation. Elpis collects from all of it over native protocols, normalizes every signal to one vocabulary, watches the rotating equipment that can't fail, and retains a traceable per-part history — without replacing a single machine. Operating across India and the Middle East. Deployed in defense and space-agency programs.
>
> PRIMARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Aerospace%20architecture%20review`
> SECONDARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Aerospace%20scoping`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. Both locked anchors are reproduced VERBATIM, each opening its own sentence: "Operating across India and the Middle East." and "Deployed in defense and space-agency programs." (admitted by the shape's §10.3 override for defense + aerospace). NEITHER anchor is elaborated — no program, agency, mission, or deployment detail beyond the verbatim sentence.

### 3.2 Section 2 — The reality in aerospace

> EYEBROW: THE REALITY ON AN AEROSPACE LINE
>
> SECTION TITLE: When the tolerance is microns and the record outlives the part.

> NARRATIVE PARA 1:
> An aerospace floor lives at two extremes at once. On one side, precision machining of turbine and structural components — five-axis cells holding tolerances where a single bad pass scraps a high-value blank. On the other, test cells and rigs where engines, turbines, and balancing assemblies are run, instrumented, and signed off. FANUC, Siemens, and Mazak controllers sit beside direct test-cell instrumentation that never spoke to a controller at all. Every one of them produces data that someone will eventually be asked to account for.

> NARRATIVE PARA 2:
> The cost of a gap here is not a slow line — it is a high-value rotating asset failing on a rig, a quality escape that surfaces a build later, or a traceability question that no one can answer because the signal was on the floor and never retained. Maintenance hears about a spindle or a bearing from the operator who felt the vibration change, not from a trend. And the per-part record that procurement and quality both need gets reassembled by hand, after the fact, from systems that were never asked to keep it in the same vocabulary.

> NARRATIVE PARA 3:
> Replacing the equipment is not the answer — it is qualified, depreciated, and trusted for the parts and tests it runs. What needs to modernize is the data layer. The aerospace operations that get there put one protocol-agnostic runtime in front of every controller and test cell, normalize every signal at the edge, instrument the consequence-heavy rotating equipment, and retain a timestamped per-part history the existing quality and MES systems can draw on — without taking a single machine offline.

> PULLQUOTE: "The record outlives the part — so the data layer has to be built to keep it."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES ON AN AEROSPACE LINE
> SECTION TITLE: The data layer modernizes. The qualified equipment stays.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Watch the equipment that can't fail.** VAS reads vibration signatures on the precision rotating equipment an aerospace operation lives on — engines, turbines, spindles, and balancing and test rigs — and gives early warning when a signature crosses a threshold your reliability team defines. An early-warning trigger ahead of failure, not a calendar, and not a guarantee against every failure. → `/capabilities/condition-monitoring`
>
> **Speak every controller and test cell you already own.** EdgeConnect polls your precision floor over native protocols — FOCAS2 for FANUC, Siemens S7 for Siemens controllers, Modbus TCP for instrumentation and machines fronted by a PLC, plus MTConnect for open-standard machines (including Mazak), Brother HTTP, and OPC UA Client where a controller exposes it. Canonical vocabulary at the edge means a spindle load, a stroke, or a test reading means the same thing whichever machine or cell produced it. FANUC MT-LINKi REST integration is on the roadmap. → `/capabilities/connectivity-edge`
>
> **One OEE truth and a traceable per-part history.** EREMOS V2 computes OEE Segments from the edge-collected signals against your OEE definition, and the same canonical signals — timestamped at the edge and retained — give you a per-part production history that holds the same meaning across a precision cell and a test rig. → `/capabilities/operational-intelligence`
>
> **Reach the signals the controller won't give you.** On a test cell or rig where the instrumentation exposes nothing useful to a controller, mDAQ acquires the sensor signal directly — temperature, pressure, flow, vibration — without waiting on a retrofit. → `/capabilities/data-acquisition`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT AN AEROSPACE LINE
> (4 cards → LOCKED solution pages; shape allows 3-5)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · PRECISION MANUFACTURING | Tolerance, traceability, and per-part history from the controllers you already run. | `/solutions/precision-manufacturing` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on engines, turbines, spindles, and test rigs — before they fail. | `/solutions/predictive-maintenance` |
| 3 | SOLUTION · CNC MACHINING | Mixed-vendor precision CNC floors on one operational view. | `/solutions/cnc-machining` |
| 4 | SOLUTION · MULTI-SITE OPERATIONS | One fleet view across plants and MRO sites. | `/solutions/multi-site-operations` |

### 3.5 Section 5 — Proof posture for aerospace

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built for floors where the part is high-value and the record is scrutinized.

> TRUST CUE (category-level + the reserved anchor — NO customer names, NO program/agency/mission names, NO metrics):
> Elpis is deployed across precision manufacturing and high-consequence rotating-equipment monitoring. **Operating across India and the Middle East.** **Deployed in defense and space-agency programs.** The platform runs offline-first: the license validates locally with no phone-home, and per-route store-and-forward is built to preserve every reading through a network or broker drop — queuing locally and replaying in source order on reconnect. Every configuration change is captured in a hash-chained, tamper-evident audit trail.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Aerospace is one of the TWO verticals (with Defense) admitted by the IndustryShell §10.3 proof-posture OVERRIDE to use BOTH locked anchors. Both are reproduced VERBATIM and each opens its own sentence: "Operating across India and the Middle East." + "Deployed in defense and space-agency programs." — neither elaborated. The category-descriptor anchor is the ENTIRE proof. Defense + space-agency customer, program, agency, and mission names are **off-record PERMANENTLY** per the locked external-claim policy (memo §4) — even after any future sign-off. NO named customers, NO invented deployment story, NO capability detail beyond the anchor, NO metrics — per phase3 memo §4 + §9 gate.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A (shape allows 4-6), `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What aerospace teams ask first.

**Q1. Which precision controllers and test-cell instrumentation can you actually collect from?**
> FANUC over FOCAS2, Siemens over S7, Mazak and other open-standard machines over MTConnect, machines and instrumentation fronted by a PLC over Modbus TCP, plus Brother HTTP and OPC UA Client where it's exposed — all shipping today. Where a test cell or rig exposes nothing useful to a controller, mDAQ acquires the sensor signal directly. FANUC MT-LINKi REST integration is on the roadmap. Bring the controller and instrumentation list to the architecture review and we confirm the collection path per asset.

**Q2. Can you monitor our engines, turbines, spindles, and balancing rigs for early failure?**
> Yes — that's what VAS is for. VAS reads vibration signatures on precision rotating equipment — engines, turbines, spindles, and balancing and test rigs — and gives early warning when a signature crosses a threshold your reliability team defines. It is an early-warning trigger ahead of failure, not a guarantee against every failure.

**Q3. Can we defend a traceable per-part production history?**
> Yes. EdgeConnect normalizes every machine and test-cell signal to one canonical vocabulary and timestamps it at the edge; those signals are retained, so the per-part record holds the same meaning across a precision cell and a test rig. That is an architectural fact about how the data is captured and kept — it is not a compliance certification, and we make no certification claim.

**Q4. We run more than one plant and MRO site. Does this aggregate?**
> Each site runs its own EdgeConnect with a per-gateway identity; EREMOS V2 aggregates across plants and MRO sites for a fleet view. Multi-site visibility comes from aggregation — never from one runtime stretched across sites. → `/solutions/multi-site-operations`

**Q5. Does this replace our SCADA, MES, or quality systems?**
> No. Elpis sits beside them. EdgeConnect publishes canonical signals (MQTT, OPC UA Server); EREMOS V2 exposes OEE, alarms, and the per-part history via API. Your SCADA keeps operator HMIs and control; your MES keeps scheduling and work orders; your quality system keeps the system of record. → `/architecture`

### 3.7 Section 7 — Cross-lens

Per design-system §17. Fixed `IndustryShell` preset: `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your line and your test cells.
> SUBHEAD: A controller and instrumentation list, the rotating equipment that worries you, and an OEE and traceability definition — that's enough to scope an architecture review. We run it on your real protocols against your real signals.
> PRIMARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Aerospace%20architecture%20review`
> SECONDARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Aerospace%20scoping`

---

## 4. Components used

All from design-system v4 LOCKED + the `IndustryShell` composition (HM v2 §10). No net-new primitives.

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

All copy in §3.1-§3.8. **~1,235 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~120 |
| 3.2 | Reality (3 paras + pullquote) | ~290 |
| 3.3 | What Elpis does here (4 blocks) | ~320 |
| 3.4 | Relevant solutions (4 cards) | ~120 |
| 3.5 | Proof posture | ~130 |
| 3.6 | FAQ (5 Q&A) | ~220 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4:

| Don't | Why |
|---|---|
| Quote a fabricated downtime / yield / OEE-gain / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name an aerospace customer, program, agency, mission, or logo | phase3 memo §4 — zero named customers this wave; defense/space-agency names off-record PERMANENTLY |
| Elaborate the defense/space-agency anchor (a deployment story, a program, a capability detail) | The verbatim category-descriptor anchor is the ENTIRE proof (§3.5 governance note) — no embellishment |
| Re-derive the condition-monitoring / connectivity / OEE capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| Claim AS9100 / NADCAP / MIL-STD / ITAR / any compliance certification | phase3 memo §4 + §9 gate — NO cert claims. Traceable edge-timestamped per-part history is an architectural fact; a certification is NOT |
| List MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — all three are roadmap |
| Imply Elpis replaces SCADA / MES / quality systems, or that one runtime spans sites | beside-not-replacing + per-site identity (carried in §3.6 Q4 + Q5) |
| Promise VAS "prevents all failures" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q2) |
| Promise store-and-forward "never" loses data | Use designed-to-preserve language ("built to preserve … replaying in source order") — §3.5 |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.3/§2.7 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos / programs / agencies / missions
- [x] No certification claims — explicitly NO AS9100 / NADCAP / MIL-STD / ITAR or any compliance cert; traceable edge-timestamped per-part history stated as ARCHITECTURE, not certification (§3.6 Q3, §6)
- [x] No competitor names (Mazak named as a customer's machine vendor / open-standard MTConnect machine, not a competitor)
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; EdgeConnect Linux roadmap; E-IDOS standalone (EREMOS streaming roadmap); mDAQ runs VAS only
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] BOTH locked anchors reproduced verbatim & standalone, per the §10.3 override (defense + aerospace); no paraphrase, no elaboration

---

## 8. Sign-off checklist (v2 LOCKED)

- [x] Page copy within ~1,000-1,400 words (current ~1,235)
- [x] All 8 IndustryShell sections present; structure/order/modes/cardinality inherited from the shape (HM v2 §10), not redefined here; this spec is §1–§9 with NO §10 of its own
- [x] §3.1 hero industry-framed; BOTH anchors verbatim & standalone; no anchor elaboration
- [x] §3.3 maps pillars via cross-link, never re-derives (condition monitoring foregrounded first for aerospace)
- [x] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages (4 cards; shape 3-5)
- [x] §3.5 proof posture uses the §10.3 override — category-level + BOTH anchors verbatim & standalone; cross-links `/security` + `/customers`; governance note states off-record-permanently
- [x] §3.6 FAQ vertical-calibrated, 5 Q&A (shape 4-6), `FAQPage` schema; protocol/peer/SCADA/MES/quality honesty intact; traceability stated as architecture not cert
- [x] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [x] CTA per regulated-buyer tone — PRIMARY "Request an architecture review" + SECONDARY "Talk to us about scoping" (NOT "Book a demo")
- [x] §7 Phase 3 acceptance gate all green
- [x] ChatGPT Pass 1 review (extra anchor scrutiny) applied — deleted the §3.5 governance-note parenthetical elaborating the space-agency anchor; v2 LOCKED

---

## 9. Out of scope for this page

- Named / quantified aerospace case studies (blocked on signed sign-off — memo §3.3 business dependency; defense/space-agency names off-record permanently regardless)
- Any program / agency / mission name or deployment story beyond the verbatim anchor
- Any compliance-certification claim (AS9100 / NADCAP / MIL-STD / ITAR etc. — memo §4 + §9 gate)
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)

---

*`/industries/aerospace` Page Spec **v2 LOCKED 2026-06-06** (ChatGPT Pass 1 applied — deleted the §3.5 governance-note parenthetical elaborating the space-agency anchor; the anchor remains the entire proof, unelaborated). An INSTANCE of the LOCKED `IndustryShell` shape — structure, section order, visual modes, and cardinality (solutions 3-5, FAQ 4-6) are owned by the shape-setter **HM v2 §10**, cited not redefined; this spec is §1–§9 with NO §10 of its own. One of the TWO verticals (with Defense) admitted by the **§10.3 proof-posture override** to use BOTH locked anchors: "Operating across India and the Middle East." + "Deployed in defense and space-agency programs." — both VERBATIM and standalone in §3.1 hero and §3.5 proof posture, neither elaborated. NO named customers / programs / agencies / missions (off-record PERMANENTLY per the locked external-claim policy — the category-descriptor anchor is the entire proof; §3.5 governance note states this). NO AS9100 / NADCAP / MIL-STD / ITAR or any compliance-cert claim; traceable edge-timestamped per-part history stated as architecture, not certification. Cross-links the LOCKED `/capabilities/<pillar>` (condition monitoring foregrounded), `/solutions/<solution>`, `/architecture`, `/platform`, `/security` owners; re-derives none (memo §6). Protocol status verbatim. Regulated-buyer CTA: "Request an architecture review" + "Talk to us about scoping". §7 pre-checks the memo §9 acceptance gate (all green). ~1,235 words within the ~1,000-1,400 vertical-lens target. Cites: page-industry-heavy-manufacturing-spec-v2 §10 (shape source); phase3-ia-scope-memo-v2 (parent); buyer-taxonomy v1 §2.3/§2.7; proof-architecture v1 §3/§4/§8; positioning v3 §4; page-capabilities-connectivity-edge-spec v1 (protocol wording); design-system v4 (→ v5 §25 IndustryShell).*
