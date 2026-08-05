<!--
File:        docs/marketing/page-industry-heavy-manufacturing-spec-v1.md
Purpose:     Page spec for /industries/heavy-manufacturing AND the
             IndustryShell SHAPE-SETTER. First Phase 3 per-page spec.
             Defines the reusable 8-section IndustryShell layout (§10,
             lands in design-system v5) that the other 5 vertical pages
             inherit, and provides the Heavy Manufacturing instance copy.
Audience:    Internal — Claude (drafts the 5 sibling vertical specs),
             copywriters (lifting verbatim text), user + ChatGPT (reviewers),
             engineering (the IndustryShell component implementers).
Format:      Per §9 canonical template locked in page-capabilities-hub-spec-v1.md.
Companion:   phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.1 IndustryShell
                skeleton, §4 proof/anonymity discipline, §5 buyer map, §6
                anti-duplication map, §9 acceptance gate, §10 Q2 shape-setter)
             buyer-taxonomy-v1.md §2.2 (plant manager) + §2.3 (OT architect)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             design-system-v4.md (extends — IndustryShell lands as v5 §25)
             LOCKED cross-link targets (do NOT duplicate):
                page-solutions-cnc-machining-spec / -brownfield-modernization /
                -predictive-maintenance / -multi-site-operations /
                -precision-manufacturing
                page-capabilities-{connectivity-edge,operational-intelligence,
                condition-monitoring,data-acquisition}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v1 — DRAFT (pre ChatGPT review). NOT locked.
Date:        2026-06-06
Status:      DRAFT — awaiting Pass 1 ChatGPT review, then v2 lock.

First Phase 3 per-page spec per phase3-ia-scope-memo-v2 §8 sequencing
step 1. Heavy Manufacturing chosen as the IndustryShell shape-setter
(memo §10 Q2): broadest, least regulatory nuance, exercises the generic
vertical pattern. Defense + Aerospace inherit this shape and add the
locked defense/space-agency anchor (memo §3.1) — reviewed AFTER this base
shape locks.

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7 of this spec.
Word-count target: ~1,000-1,400 words page copy (vertical lens; lighter
than /platform; heavier than a card hub). Current draft: ~1,180 words.
-->

# `/industries/heavy-manufacturing` — Page Spec v1 (DRAFT) + IndustryShell shape-setter

> **⚠️ SUPERSEDED — retained as historical reference.** The LOCKED spec is **`page-industry-heavy-manufacturing-spec-v2.md`**, which applies the four Pass-1 ChatGPT-review edits (geography anchor verbatim+standalone; cardinality fixed; store-and-forward softened; connectivity protocol wording tightened). Downstream sibling-vertical specs inherit the `IndustryShell` shape from **v2 §10**, not from this draft.

**Vertical lens over the existing platform/solution/capability content, told through heavy-manufacturing vocabulary and pain. Plant manager primary; the plant's OT architect secondary. Reader self-identifies ("this is my floor"), sees which solutions fit, gets honest proof posture, and is routed to the LOCKED owners of the detail. This spec also DEFINES the reusable `IndustryShell` layout (§10) the other 5 verticals inherit.**

This is the page a heavy-manufacturing operations leader lands on from a vertical search ("CNC + press monitoring platform", "heavy fabrication OEE") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *this industry's* reality and cross-links to the authoritative owners.

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** A heavy-manufacturing-framed entry point. Reader leaves with *"Elpis understands a mixed-vendor heavy floor — old presses next to new CNCs, rotating equipment that can't fail, OEE stitched by hand across bays — and here are the solutions that fit and where to go next."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer, competitor name, or certification claim (phase3 memo §4)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Plant / operations manager (§2.2) — wants to know Elpis grasps a mixed-vintage heavy floor and that the consequence-heavy equipment (presses, large motors, gearboxes) is covered. CTA preference: *"Book a scoping call."*

**Secondary:** The plant's OT architect / maintenance engineer (§2.3) — wants protocol-coverage honesty + brownfield posture + condition-monitoring reach. CTA preference: *"Request an architecture review."*

- Vocabulary that lands: mixed-vendor, brownfield, FOCAS2 / Siemens S7 / Modbus, presses, rotating equipment, gearboxes, OEE across bays, per-plant, store-and-forward, beside-not-replacing.
- Vocabulary that backfires: "Industry 4.0 transformation", "AI-powered", "single pane of glass", "turnkey digitalization", any unqualified percentage.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Heavy Manufacturing — mixed-floor intelligence · Elpis* |
| **Meta description** (140-160 chars) | *One operational view across mixed-vendor CNCs, presses, and PLCs — plus condition monitoring on the rotating equipment that can't fail. Brownfield-ready.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/heavy-manufacturing` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup (Phase 3 customer registry handles structured proof later). |

---

## 2. Page structure — sections at a glance (the IndustryShell layout)

8 sections. This table IS the `IndustryShell` shape (formalized in §10).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~110 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (2-3) + pullquote | ~280 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (4-5 solution cards) | ~120 |
| **5** | Proof posture for this industry (anonymized cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~120 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 5 Q&A with `FAQPage` schema | ~220 |
| **7** | Cross-lens navigation | `light-tinted` | §17 cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Heavy Manufacturing instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · HEAVY MANUFACTURING
>
> HEADLINE (size.3xl semibold):
> Heavy manufacturing runs on mixed iron. Your operational view shouldn't fragment because of it.
>
> SUBHEAD (size.lg, max-width 60ch):
> Decades-old presses beside last year's CNCs. FANUC next to Siemens S7 next to Modbus PLCs. Elpis collects from all of it over native protocols, normalizes every signal to one vocabulary, and watches the rotating equipment that can't fail — without replacing a single machine. Operating across India and the Middle East.
>
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Heavy%20manufacturing%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. The geography anchor ("Operating across India and the Middle East") is reproduced verbatim — it is a footprint statement, not a customer claim. NO defense/space-agency anchor here (reserved for `/industries/defense` + `/aerospace` per memo §3.1).

### 3.2 Section 2 — The reality in heavy manufacturing

> EYEBROW: THE REALITY ON A HEAVY FLOOR
>
> SECTION TITLE: Five generations of iron, one production target.
>
> NARRATIVE PARA 1:
> A heavy-manufacturing floor is an archaeology of capital decisions. A hydraulic press commissioned in 1998 still stamping good parts. A FANUC turning center from the 2010s. A Siemens-controlled line cell added last year. A row of older machines fronted by Modbus PLCs because their original interfaces never survived a network-security review. Every one of them is making parts. None of them speaks the same language.
>
> NARRATIVE PARA 2:
> The cost of that fragmentation is concentrated and expensive. A large press or a flagship CNC going down doesn't slow a line — it stops one. Maintenance hears about a bearing or a hydraulic fault from the operator who noticed the noise, not from a trend. OEE gets reconciled across bays in a spreadsheet on Monday, describing a problem that already cost the weekend. And the data that would have caught it early was on the floor the whole time — just never reaching the people who could act.
>
> NARRATIVE PARA 3:
> Replacing the iron isn't the answer. It's depreciated, validated for the parts it runs, and the operators know it by feel. What needs to modernize is the data layer, not the machines. The heavy shops that get there put one protocol-agnostic runtime in front of every controller, normalize every signal at the edge, instrument the consequence-heavy rotating equipment, and let the existing systems keep doing their jobs.
>
> PULLQUOTE: "The data that would have caught it early was on the floor the whole time."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES ON A HEAVY FLOOR
> SECTION TITLE: The data layer modernizes. The iron stays.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Speak every controller you already own.** EdgeConnect polls your mixed floor over native protocols — FOCAS2 for FANUC, Siemens S7 for press and line PLCs, Modbus TCP for older machines behind a PLC, MTConnect and Brother HTTP for the rest, OPC UA Client where it's exposed. Canonical vocabulary at the edge means a spindle, a stroke count, or a fault code means the same thing whichever machine produced it. FANUC MT-LINKi REST integration is on the roadmap. → `/capabilities/connectivity-edge`
>
> **One OEE truth across every bay.** EREMOS V2 computes OEE Segments from the edge-collected signals against your OEE definition — so the number that took a Monday spreadsheet to assemble holds the same meaning across a 1998 press and a 2024 cell. → `/capabilities/operational-intelligence`
>
> **Watch the equipment that can't fail.** VAS reads vibration signatures on the rotating equipment a heavy floor lives on — large motors, gearboxes, fans, and presses — and E-IDOS reads particle contamination and water saturation on hydraulic-press and lubrication systems (ISO 4406 / NAS 1638). Early warning on a signature your maintenance team sets, not a calendar. → `/capabilities/condition-monitoring`
>
> **Reach the signals the PLC won't give you.** Where a machine exposes nothing useful, mDAQ acquires the sensor signal directly — temperature, pressure, flow, vibration — without waiting on a controller retrofit. → `/capabilities/data-acquisition`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT A HEAVY FLOOR
> (4 cards → LOCKED solution pages)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · BROWNFIELD MODERNIZATION | Modern OEE and audit trails from the controllers you already own. | `/solutions/brownfield-modernization` |
| 2 | SOLUTION · CNC MACHINING | Mixed-vendor CNC floors on one operational view. | `/solutions/cnc-machining` |
| 3 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on presses, motors, and gearboxes — before they fail. | `/solutions/predictive-maintenance` |
| 4 | SOLUTION · MULTI-SITE OPERATIONS | One fleet view when you run more than one plant. | `/solutions/multi-site-operations` |

### 3.5 Section 5 — Proof posture for heavy manufacturing

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built for floors where downtime is measured in stopped lines.

> TRUST CUE (category-level — NO customer names, NO metrics):
> Elpis is deployed across heavy fabrication and machining operations — mixed-vendor floors with consequence-heavy rotating equipment — **operating across India and the Middle East.** The platform runs offline-first: the license validates locally with no phone-home, and per-route store-and-forward means a network or broker drop never costs you a cycle or a parts count. Every configuration change is captured in a hash-chained, tamper-evident audit trail.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Heavy Manufacturing uses CATEGORY-LEVEL proof framing + the verbatim geography anchor only. It does NOT use the defense/space-agency anchor (reserved for `/industries/defense` + `/aerospace`). No customer names, no metrics — per phase3 memo §4 + §9 gate.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A, `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What heavy-manufacturing teams ask first.

**Q1. Which of our machines can you actually collect from?**
> FANUC over FOCAS2, Siemens controllers over S7, older machines fronted by a PLC over Modbus TCP, plus MTConnect, Brother HTTP, and OPC UA Client where it's exposed — all shipping today. FANUC MT-LINKi REST integration is on the roadmap. Bring the controller list to the scoping call and we confirm the collection path per machine.

**Q2. Do we have to replace our older presses and CNCs?**
> No. EdgeConnect reads each controller as a read-only client — it never changes control logic, and no machine comes offline to connect it. The iron stays; the data layer modernizes.

**Q3. Can you monitor our presses and large motors for failure?**
> Yes — that's what VAS and E-IDOS are for. VAS reads vibration signatures on rotating equipment (motors, gearboxes, fans, presses); E-IDOS reads hydraulic and lubrication oil health. They give early warning when a signature crosses a threshold your maintenance team defines — a better trigger than a calendar, not a guarantee against every failure.

**Q4. We run more than one plant. Does this aggregate?**
> Each plant runs its own EdgeConnect with a per-gateway identity; EREMOS V2 aggregates across plants for a fleet view. Multi-site visibility comes from aggregation — never from one runtime stretched across plants. → `/solutions/multi-site-operations`

**Q5. Does this replace our SCADA or MES?**
> No. Elpis sits beside them. EdgeConnect publishes canonical signals (MQTT, OPC UA Server); EREMOS V2 exposes OEE, alarms, and reports via API. Your SCADA keeps operator HMIs and control; your MES keeps scheduling and work orders. → `/architecture`

### 3.7 Section 7 — Cross-lens

Per design-system §17. Preset for `IndustryShell`: `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your floor — every vintage of it.
> SUBHEAD: A controller list, the rotating equipment that worries you, and an OEE definition — that's enough to scope a proof of value. We run it on your real protocols against your real signals.
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Heavy%20manufacturing%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

---

## 4. Components used

All from design-system v4 LOCKED + the new `IndustryShell` composition (§10). No net-new primitives.

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

All copy in §3.1-§3.8. **~1,180 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~110 |
| 3.2 | Reality (3 paras + pullquote) | ~280 |
| 3.3 | What Elpis does here (4 blocks) | ~320 |
| 3.4 | Relevant solutions (4 cards) | ~120 |
| 3.5 | Proof posture | ~120 |
| 3.6 | FAQ (5 Q&A) | ~220 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4:

| Don't | Why |
|---|---|
| Quote a fabricated downtime-reduction / OEE-gain / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name a heavy-manufacturing customer or use a logo | phase3 memo §4 — zero named customers this wave |
| Use the defense/space-agency anchor here | Reserved for `/industries/defense` + `/aerospace` (memo §3.1). Heavy mfg = category-level + geography anchor only |
| Re-derive the OEE / condition-monitoring / connectivity capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| List MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — all three are roadmap |
| Imply Elpis replaces SCADA / MES, or that one runtime spans plants | beside-not-replacing + per-plant identity (carried in §3.6 Q4 + Q5) |
| Promise VAS/E-IDOS "prevents all failures" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q3) |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.2/§2.3 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos
- [x] No certification claims (ISO 4406 / NAS 1638 are oil-cleanliness *report codes*, not Elpis certifications)
- [x] No competitor names
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] Locked geography anchor reproduced verbatim; defense/space-agency anchor NOT used (reserved)

---

## 8. Sign-off checklist (v1 → v2 lock)

- [ ] Page copy within ~1,000-1,400 words (current ~1,180)
- [ ] All 8 IndustryShell sections present (§2)
- [ ] §3.1 hero industry-framed; geography anchor verbatim; no defense anchor
- [ ] §3.3 maps pillars via cross-link, never re-derives
- [ ] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages
- [ ] §3.5 proof posture category-level + geography anchor only; cross-links `/security` + `/customers`
- [ ] §3.6 FAQ vertical-calibrated, 5 Q&A, `FAQPage` schema; protocol/peer/SCADA honesty intact
- [ ] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [ ] §7 Phase 3 acceptance gate all green
- [ ] §10 IndustryShell shape definition complete for design-system v5 hand-off
- [ ] ChatGPT Pass 1 review applied; v2 locked

---

## 9. Out of scope for this page

- Named / quantified heavy-manufacturing case studies (blocked on signed sign-off — memo §3.3 business dependency)
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)

---

## 10. `IndustryShell` shape definition (for design-system v5)

**This is the reusable contract the other 5 vertical pages inherit.** Heavy Manufacturing (above) is the canonical instance.

### 10.1 Fixed structure (all 6 verticals)
The 8-section layout in §2 is fixed. Section order, visual modes, and component choices do not vary by vertical.

### 10.2 Per-vertical slots (what each vertical fills)
- **§3.1 Hero** — industry-framed headline + sub. Geography anchor verbatim optional (true for all today).
- **§3.2 Reality** — the vertical's specific pain narrative (2-3 paras + pullquote).
- **§3.3 What Elpis does here** — selects which of the 5 pillars to foreground for this vertical (always cross-links the LOCKED owner; never re-derives).
- **§3.4 Relevant solutions** — selects the 3-5 `/solutions/<solution>` cards that fit the vertical.
- **§3.5 Proof posture** — see override rule §10.3.
- **§3.6 FAQ** — 4-6 vertical-calibrated Q&A.
- **§3.7 Cross-lens** — fixed preset `/solutions` + `/architecture` + `/platform`.
- **CTA** — per buyer (§2.2 plant manager → "Book a scoping call"; regulated verticals lead "Request an architecture review").

### 10.3 Proof-posture override rule (the one hard variant)
- **Defense + Aerospace** (`/industries/defense`, `/industries/aerospace`): §3.5 MAY use the locked **"Deployed in defense and space-agency programs."** anchor verbatim, plus the geography anchor. These two verticals get extra ChatGPT review attention for anchor handling + regulated-buyer tone.
- **All four other verticals** (heavy manufacturing, automotive, pharma, oil & gas): §3.5 uses **category-level framing + the geography anchor only**. NO defense/space-agency anchor. NO invented deployments.
- **Every vertical**, without exception: no named customers, no fabricated metrics, no cert claims, no competitor names, protocol status verbatim (phase3 memo §9 gate).

### 10.4 Honesty invariants (non-negotiable across all 6)
1. Cross-link, never re-derive, the LOCKED capability/solution/architecture owners (memo §6).
2. Every protocol/outcome claim traces to a LOCKED parent spec — no vertical invents a capability.
3. The §9 acceptance gate is re-run per vertical spec before lock.

### 10.5 Hub note
`/industries/` reuses the existing hub/card-grid pattern (memo §3.4) — NOT `IndustryShell`. Only the 6 vertical pages use this shape.

---

*`/industries/heavy-manufacturing` Page Spec **v1 DRAFT** — first Phase 3 per-page spec + the `IndustryShell` shape-setter. 8-section vertical-lens layout (§2/§10) the other 5 verticals inherit. Heavy Manufacturing chosen as shape-setter per phase3 memo §10 Q2. Category-level proof + verbatim geography anchor (defense/space-agency anchor reserved for defense/aerospace per memo §3.1). Cross-links the LOCKED `/capabilities/<pillar>`, `/solutions/<solution>`, `/architecture`, `/platform`, `/security` owners; re-derives none (memo §6). §7 pre-checks the memo §9 acceptance gate (all green). ~1,180 words within the ~1,000-1,400 vertical-lens target. Awaiting Pass 1 ChatGPT review → v2 lock, then 5 sibling vertical specs parallelize against §10. Cites: phase3-ia-scope-memo-v2 (parent); buyer-taxonomy v1 §2.2/§2.3; proof-architecture v1 §3/§4/§8; positioning v3 §4; design-system v4 (→ v5 §25 IndustryShell).*
