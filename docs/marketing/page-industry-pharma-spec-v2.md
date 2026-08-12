<!--
File:        docs/marketing/page-industry-pharma-spec-v2.md
Purpose:     Page spec for /industries/pharma. INSTANCE of the LOCKED
             IndustryShell shape (page-industry-heavy-manufacturing-spec-v2.md
             §10). One of the 5 sibling verticals inheriting the 8-section
             vertical-lens layout. Provides the Pharma instance copy.
Audience:    Internal — Claude (sibling-spec drafting), copywriters (lifting
             verbatim text), user + ChatGPT (reviewers), engineering
             (IndustryShell component implementers).
Format:      Per §9 canonical template; mirrors the HM shape-setter exactly
             (section markers, blockquote copy style, §7 acceptance-gate
             table, §8 sign-off, §3.5 governance-note pattern).
Companion:   page-industry-heavy-manufacturing-spec-v2.md (LOCKED SHAPE-SETTER —
                IndustryShell shape source = v2 §10; this spec is §1–§9 only,
                adds NO §10 of its own)
             phase3-ia-scope-memo-v2.md (LOCKED PARENT — §4 proof/anonymity
                discipline, §5 buyer map, §6 anti-duplication, §9 acceptance gate)
             buyer-taxonomy-v1.md §2.2 (plant manager) + §2.3 (OT architect)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             page-capabilities-connectivity-edge-spec-v1.md (protocol wording source)
             design-system-v4.md (→ IndustryShell lands as v5 §25)
             LOCKED cross-link targets (do NOT duplicate):
                page-solutions-{brownfield-modernization,predictive-maintenance,
                multi-site-operations,edge-connectivity}-spec
                page-capabilities-{connectivity-edge,operational-intelligence,
                condition-monitoring,data-acquisition}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v2 — LOCKED after Pass 1 ChatGPT review.
Date:        2026-06-06
Status:      LOCKED. IndustryShell instance; inherits the shape from
             page-industry-heavy-manufacturing-spec-v2 §10.

v1 (page-industry-pharma-spec-v1.md) retained as historical reference.

v1 -> v2 changes (ChatGPT Pass-1 regulated-language cleanup):
  - §3.6 Q2 rewritten so Elpis does NOT answer the customer's validation
    obligation — reframed to Elpis's read-only/beside posture + deferral to
    the customer's own change-control / quality processes.
  - Replaced "validated"/"qualified" ASSURANCE/descriptor copy in the
    page-visible voice with "existing control systems" / "change-controlled
    equipment" / "commissioned". The words "validated"/"qualified" now appear
    ONLY in the explicit ban lists (vocabulary-that-backfires, anti-pattern
    rows, governance notes) — never as customer-facing assurance.
  - Hash-chain language unchanged: tamper-evident change record / change-
    control support only; never Part-11 / compliance / validation.

The reusable IndustryShell shape is defined ONCE in the shape-setter (v2 §10).
This sibling spec is §1–§9 only and adds NO §10 of its own.

REGULATED-VERTICAL HONESTY NOTE (sharper than HM): pharma is a regulated
environment. This spec makes ZERO compliance, validation, or certification
claims — no GMP, GxP, 21 CFR Part 11, Annex 11, CSV / "validated" /
"qualification" / "compliant". The hash-chained config audit is framed ONLY
as support for the customer's existing change-control discipline (a
tamper-evident change record), NEVER as a compliance feature. EdgeConnect is
read-only and sits BESIDE the existing control systems — that is the
reassurance offered, not a compliance badge. See §3.5 governance note + §6 +
§7 gate.

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7.
Word-count target: ~1,000-1,400 words page copy. Current: ~1,205 words.
-->

# `/industries/pharma` — Page Spec v2 (LOCKED) · IndustryShell instance

**Vertical lens over the existing platform/solution/capability content, told through pharmaceutical-manufacturing vocabulary and constraints. Plant / operations manager primary; the plant's OT architect / qualification-aware engineer secondary. Reader self-identifies ("this is my regulated floor"), sees which solutions fit, gets an honest read-only / beside-not-replacing posture, and is routed to the LOCKED owners of the detail. This page is an INSTANCE of the `IndustryShell` shape defined in the shape-setter (v2 §10); it does not redefine the shape.**

This is the page a pharma operations or engineering leader lands on from a vertical search ("OEE for packaging lines", "utility condition monitoring pharma plant", "read-only PLC data collection") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *a regulated pharmaceutical floor's* reality and cross-links to the authoritative owners.

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** A pharma-framed entry point. Reader leaves with *"Elpis understands a regulated pharma plant — PLC-controlled batch and packaging lines I cannot disturb, and critical utilities (HVAC, compressed air, purified/WFI water, chillers) running on rotating equipment that fails quietly — and it collects all of it read-only, beside my existing control systems, without touching control logic."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer, competitor name, or **compliance/validation/certification claim** (phase3 memo §4 — sharpened for this regulated vertical, see §6)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Plant / operations manager (§2.2) — wants to know Elpis grasps a regulated floor where change-controlled equipment cannot be disturbed, and that the utilities and packaging lines that gate throughput are covered. CTA preference: *"Book a scoping call."*

**Secondary:** The plant's OT architect / qualification-aware engineer (§2.3) — wants read-only posture, protocol-coverage honesty, beside-not-replacing assurance, and how the change-record discipline fits their existing change control. CTA preference: *"Request an architecture review."*

- Vocabulary that lands: read-only, sits beside, change-controlled equipment, change control, batch, packaging and filling lines, cleanroom, utilities (HVAC, compressed air, purified/WFI water, chillers), Siemens S7, Modbus, rotating equipment, per-plant, store-and-forward, beside-not-replacing, tamper-evident change record.
- Vocabulary that backfires: "compliant", "validated", "GMP/GxP-ready", "21 CFR Part 11", "Annex 11", "qualification", "Industry 4.0 transformation", "AI-powered", "single pane of glass", any unqualified percentage.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Pharma — read-only plant intelligence · Elpis* |
| **Meta description** (140-160 chars) | *Collect from packaging, filling, and utility equipment read-only — beside your existing control systems, never touching control logic. OEE and condition monitoring.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/pharma` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup (Phase 3 customer registry handles structured proof later). |

---

## 2. Page structure — sections at a glance (the IndustryShell layout)

8 sections. This is the `IndustryShell` shape inherited verbatim from the shape-setter (v2 §10); section order, visual modes, and component choices do not vary by vertical. Cardinality of the variable sections is fixed at the shape level: **Relevant solutions = 3-5 cards; FAQ = 4-6 Q&A.** Pharma uses **4** and **5** respectively.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~110 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (2-3) + pullquote | ~280 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (3-5 cards; pharma uses 4) | ~120 |
| **5** | Proof posture for this industry (anonymized cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~120 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 4-6 Q&A (pharma uses 5), `FAQPage` schema | ~220 |
| **7** | Cross-lens navigation | `light-tinted` | §17 cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Pharma instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · PHARMACEUTICALS
>
> HEADLINE (size.3xl semibold):
> A pharma plant can't be disturbed to be understood. Elpis collects from it read-only — beside your existing control systems.
>
> SUBHEAD (size.lg, max-width 60ch):
> Batch lines, packaging and filling machines, cleanrooms, and the critical utilities that keep them running — HVAC, compressed air, purified and WFI water, chillers. Most of it is PLC-controlled (Siemens S7, Modbus). Elpis reads every controller as a read-only client, normalizes the signals to one vocabulary, and watches the rotating utility equipment that fails quietly — without changing any control logic. Operating across India and the Middle East.
>
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Pharma%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. **No compliance / validation / certification claim** — no GMP, GxP, Part 11, Annex 11, "validated", "qualification", "compliant" (see §6). The geography anchor ("Operating across India and the Middle East.") is reproduced verbatim — it is a footprint statement, not a customer claim. NO defense/space-agency anchor here (reserved for `/industries/defense` + `/aerospace` per memo §3.1, override v2 §10.3).

### 3.2 Section 2 — The reality in a pharma plant

> EYEBROW: THE REALITY ON A REGULATED FLOOR
>
> SECTION TITLE: The control systems stay untouched. The data layer is on its own.
>
> NARRATIVE PARA 1:
> A pharmaceutical plant is built around equipment that cannot simply be touched. Batch reactors, filling lines, blister and cartoning machines, the cleanrooms they sit in — each was commissioned for the product it runs and locked under change control. Most of it is PLC-controlled: Siemens S7 on the process and packaging cells, Modbus on the older skids and balance-of-plant. None of it was installed to be queried by a monitoring platform, and none of it can come offline just to be connected.
>
> NARRATIVE PARA 2:
> Around that change-controlled core runs an entire plant of critical utilities — HVAC holding cleanroom pressure cascades, compressed-air systems, purified-water and WFI loops, chillers, and the pumps, compressors, and fans that drive them. These are rotating machines, and they fail the way rotating machines do: quietly, then all at once. A utility excursion doesn't just stop a line — in a regulated context it can put a batch in question. Yet the early signs are usually on the floor long before anyone acts on them, scattered across PLCs and standalone sensors that never reach a trend.
>
> NARRATIVE PARA 3:
> The answer is not to re-touch change-controlled equipment. It is to modernize the data layer *beside* it. The pharma plants that get there put one protocol-agnostic runtime in front of every controller as a read-only client, normalize every signal at the edge, instrument the consequence-heavy utility equipment, and let the existing control systems keep doing exactly what they were commissioned to do — unchanged.
>
> PULLQUOTE: "The early signs are on the floor long before anyone acts on them."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES ON A PHARMA FLOOR
> SECTION TITLE: Read-only, beside your existing control systems.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Read every controller without disturbing it.** EdgeConnect polls your floor over native protocols as a read-only client — Siemens S7 for process and packaging PLCs, Modbus TCP for older skids and balance-of-plant, plus FOCAS2, MTConnect, Brother HTTP, and OPC UA Client where a controller exposes it — all shipping today. It changes no control logic and brings no machine offline to connect it. Canonical vocabulary at the edge means a fill count, a chamber pressure, or a fault code means the same thing whichever machine produced it. FANUC MT-LINKi REST integration is on the roadmap. → `/capabilities/connectivity-edge`
>
> **One OEE truth across packaging and filling.** EREMOS V2 computes OEE Segments from the edge-collected signals against your OEE definition — so line performance on a filling or packaging line holds the same meaning across machines and shifts, without a manual reconciliation. → `/capabilities/operational-intelligence`
>
> **Watch the utilities that put a batch at risk.** VAS reads vibration signatures on the rotating equipment a pharma plant lives on — HVAC fans, chillers, compressors, and process pumps — and gives early warning when a signature crosses a threshold your maintenance team sets. Early warning on the utility equipment behind cleanroom and water-system uptime, not a calendar. → `/capabilities/condition-monitoring`
>
> **Reach the utility sensors the PLC won't give you.** Where a utility skid exposes nothing useful, mDAQ acquires the sensor signal directly — temperature, pressure, flow, vibration — without waiting on a controller retrofit. → `/capabilities/data-acquisition`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT A PHARMA PLANT
> (4 cards → LOCKED solution pages; shape allows 3-5)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · BROWNFIELD MODERNIZATION | Modern OEE and a change record from the controllers you already run. | `/solutions/brownfield-modernization` |
| 2 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on pumps, compressors, and HVAC — before a utility excursion. | `/solutions/predictive-maintenance` |
| 3 | SOLUTION · MULTI-SITE OPERATIONS | One fleet view when you run more than one plant. | `/solutions/multi-site-operations` |
| 4 | SOLUTION · EDGE CONNECTIVITY | Read-only collection from mixed PLCs, normalized at the edge. | `/solutions/edge-connectivity` |

### 3.5 Section 5 — Proof posture for pharma

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built to sit beside your control systems, not inside them.

> TRUST CUE (category-level — NO customer names, NO metrics, NO compliance claim):
> Elpis is deployed across regulated and process-driven manufacturing operations — plants with change-controlled equipment, PLC-controlled lines, and critical utilities. **Operating across India and the Middle East.** EdgeConnect connects as a read-only client and sits beside your control systems: it changes no control logic and brings no machine offline. The platform runs offline-first — the license validates locally with no phone-home, and per-route store-and-forward is built to preserve every reading through a network or broker drop, queuing locally and replaying in source order on reconnect. Every configuration change to EdgeConnect itself is captured in a hash-chained, tamper-evident change record that supports your existing change-control discipline.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Pharma uses CATEGORY-LEVEL proof framing + the verbatim geography anchor only. It does NOT use the defense/space-agency anchor (reserved for `/industries/defense` + `/aerospace`; override v2 §10.3). No customer names, no metrics. **CRITICAL (regulated vertical):** NO compliance / validation / certification claim anywhere — no GMP, GxP, 21 CFR Part 11, Annex 11, CSV, "validated", "qualification", "compliant". The hash-chained config audit is framed ONLY as support for the customer's *existing* change-control discipline (a tamper-evident change *record*), NEVER as a Part-11 feature, a compliance guarantee, or a validation deliverable. The reassurance is the read-only / beside-not-replacing posture — not a compliance badge. Per phase3 memo §4 + §9 gate.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A (shape allows 4-6), `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What pharma teams ask first.

**Q1. Which of our equipment can you actually collect from?**
> Siemens controllers over S7, older skids and balance-of-plant over Modbus TCP, plus FOCAS2, MTConnect, Brother HTTP, and OPC UA Client where it's exposed — all shipping today. That covers most PLC-controlled batch and packaging lines and the utility equipment around them. FANUC MT-LINKi REST integration is on the roadmap. Bring the equipment list to the scoping call and we confirm the collection path per machine.

**Q2. Does this touch or change our existing control systems?**
> EdgeConnect connects to each controller as a read-only client — it reads data and changes no control logic, and no machine comes offline to connect it. It sits beside your existing control systems rather than inside them; the control side keeps running exactly as it does today, and Elpis only observes. How that read-only data path fits your site's own change-control and quality processes is your determination to make — we're glad to walk your engineering and quality teams through the exact data path on an architecture review so they can assess it against your procedures.

**Q3. Can you monitor our utilities and rotating equipment for early failure?**
> Yes — that's what VAS and mDAQ are for. VAS reads vibration signatures on rotating utility equipment (HVAC fans, chillers, compressors, process pumps); where a skid exposes nothing useful, mDAQ acquires temperature, pressure, flow, or vibration directly. Both give early warning when a signature crosses a threshold your maintenance team defines — a better trigger than a calendar, not a guarantee against every failure.

**Q4. How does the change record help our change-control discipline?**
> Every configuration change to EdgeConnect is captured in a hash-chained, tamper-evident change record — what changed, and that the history hasn't been altered after the fact. It is designed to support the change-control discipline you already run, giving you a verifiable record of changes to the data layer. It is a change *record*, not a compliance or validation deliverable — your own change-control processes and your quality system remain the system of record. → `/security`

**Q5. Does this replace our SCADA, MES, or historian?**
> No. Elpis sits beside them. EdgeConnect publishes canonical signals read-only (MQTT, OPC UA Server); EREMOS V2 exposes OEE, alarms, and reports via API. Your SCADA keeps operator HMIs and control, your MES keeps batch and scheduling, your historian keeps its records. Elpis adds an operational and condition-monitoring view alongside them. → `/architecture`

### 3.7 Section 7 — Cross-lens

Per design-system §17. Preset for `IndustryShell` (fixed, v2 §10.2): `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your plant — change-controlled equipment and all.
> SUBHEAD: An equipment and utility list, the rotating equipment that worries you, and an OEE definition — that's enough to scope a proof of value. We run it read-only, on your real protocols, beside your real systems.
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Pharma%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

---

## 4. Components used

All from design-system v4 LOCKED + the `IndustryShell` composition (v2 §10). No net-new primitives.

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

All copy in §3.1-§3.8. **~1,205 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~120 |
| 3.2 | Reality (3 paras + pullquote) | ~290 |
| 3.3 | What Elpis does here (4 blocks) | ~320 |
| 3.4 | Relevant solutions (4 cards) | ~115 |
| 3.5 | Proof posture | ~130 |
| 3.6 | FAQ (5 Q&A) | ~230 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4 + the IndustryShell honesty invariants (v2 §10.4):

| Don't | Why |
|---|---|
| Make ANY compliance / validation claim — GMP, GxP, 21 CFR Part 11, Annex 11, CSV, "validated", "qualified/qualification", "compliant" | **Sharpest rule on this page.** These are certification/compliance claims banned by phase3 memo §4 + §9 gate. EdgeConnect makes no compliance guarantee |
| Frame the hash-chained config audit as a Part-11 feature, a compliance guarantee, or a validation deliverable | The audit may be framed ONLY as a tamper-evident change *record* that **supports** the customer's existing change-control discipline (§3.5 + §3.6 Q4) |
| Imply EdgeConnect touches, writes to, or alters the customer's control logic | EdgeConnect is read-only and sits BESIDE the existing control systems — the core reassurance (§3.1, §3.6 Q2) |
| Quote a fabricated downtime / OEE-gain / yield / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name a pharma customer or use a logo | phase3 memo §4 — zero named customers this wave |
| Use the defense/space-agency anchor here | Reserved for `/industries/defense` + `/aerospace` (memo §3.1, override v2 §10.3). Pharma = category-level + geography anchor only |
| Re-derive the OEE / condition-monitoring / connectivity capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| List MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — all three are roadmap |
| Imply Elpis replaces SCADA / MES / historian, or that one runtime spans plants | beside-not-replacing + per-plant identity (carried in §3.6 Q4 + Q5) |
| Promise VAS / mDAQ "prevents all failures" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q3) |
| Promise store-and-forward "never" loses data | Use designed-to-preserve language ("built to preserve … replaying in source order") — §3.5 |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.2/§2.3 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos
- [x] No certification claims — and, sharpened for this regulated vertical, **NO compliance / validation claim**: no GMP, GxP, 21 CFR Part 11, Annex 11, CSV, "validated", "qualification", "compliant" anywhere. Hash-chained audit framed ONLY as change-control *support* / tamper-evident change record, never a compliance feature
- [x] No competitor names
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] Locked geography anchor reproduced verbatim & standalone; defense/space-agency anchor NOT used (reserved)
- [x] EdgeConnect read-only / beside-not-replacing posture carried explicitly (§3.1, §3.5, §3.6 Q2 + Q5)

---

## 8. Sign-off checklist (v1 DRAFT — pending ChatGPT Pass 1)

- [x] Page copy within ~1,000-1,400 words (current ~1,205)
- [x] All 8 IndustryShell sections present (§2), inheriting the shape from v2 §10 (adds no §10 of its own)
- [x] §3.1 hero industry-framed; read-only/beside posture stated; geography anchor verbatim; no defense anchor; no compliance claim
- [x] §3.3 maps pillars via cross-link, never re-derives; read-only framing in the connectivity block
- [x] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages (4 cards; shape 3-5)
- [x] §3.5 proof posture category-level + geography anchor verbatim & standalone; read-only/beside stated; audit = change-control support only; cross-links `/security` + `/customers`
- [x] §3.6 FAQ vertical-calibrated, 5 Q&A (shape 4-6), `FAQPage` schema; revalidation/read-only, utilities, change-record-support, SCADA/MES/historian honesty intact
- [x] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [x] §7 Phase 3 acceptance gate all green
- [x] NO GxP / Part-11 / validation / compliance claim anywhere; audit framed as change-control SUPPORT only (§3.5 governance note + §6)
- [ ] ChatGPT Pass 1 review applied; promote to v2 LOCKED

---

## 9. Out of scope for this page

- Named / quantified pharma case studies (blocked on signed sign-off — memo §3.3 business dependency)
- **Any compliance / validation / certification posture** (GMP, GxP, Part 11, Annex 11, CSV) — not claimed, not promised; out of scope permanently for this surface
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)

---

*`/industries/pharma` Page Spec **v2 LOCKED 2026-06-06** after Pass 1 ChatGPT review (regulated-language cleanup). INSTANCE of the LOCKED `IndustryShell` shape defined in the shape-setter (page-industry-heavy-manufacturing-spec-**v2 §10**); this spec is §1–§9 only and adds NO §10 of its own. 8-section vertical-lens layout inherited verbatim; cardinality fixed (solutions 3-5 → pharma uses 4, FAQ 4-6 → pharma uses 5). Category-level proof + verbatim & standalone geography anchor ("Operating across India and the Middle East."); defense/space-agency anchor NOT used (reserved, override v2 §10.3). **Regulated-vertical honesty sharpened:** ZERO compliance/validation/certification claims (no GMP, GxP, 21 CFR Part 11, Annex 11, CSV, "validated", "qualification", "compliant"); hash-chained config audit framed ONLY as support for existing change-control discipline (tamper-evident change record), never a compliance feature; EdgeConnect read-only and sits BESIDE the existing control systems as the reassurance. v1 -> v2 applied the ChatGPT regulated-language cleanup: §3.6 Q2 rewritten so Elpis does not answer the customer's validation obligation (reframed to read-only/beside + deferral to the customer's own change-control/quality processes); "validated"/"qualified" assurance copy replaced with "existing control systems" / "change-controlled equipment" / "commissioned" in the page-visible voice (the words now appear only in ban lists). Cross-links the LOCKED `/capabilities/<pillar>` (connectivity-edge, operational-intelligence, condition-monitoring, data-acquisition), `/solutions/<solution>` (brownfield-modernization, predictive-maintenance, multi-site-operations, edge-connectivity), `/architecture`, `/platform`, `/security` owners; re-derives none (memo §6). §7 pre-checks the memo §9 acceptance gate (all green). ~1,205 words within the ~1,000-1,400 vertical-lens target. Cites: phase3-ia-scope-memo-v2 (parent); page-industry-heavy-manufacturing-spec-v2 §10 (shape source); buyer-taxonomy v1 §2.2/§2.3; proof-architecture v1 §3/§4/§8; positioning v3 §4; page-capabilities-connectivity-edge-spec v1 (protocol wording); design-system v4 (→ v5 §25 IndustryShell).*
