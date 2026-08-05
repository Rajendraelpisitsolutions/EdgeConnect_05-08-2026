<!--
File:        docs/marketing/page-industry-automotive-spec-v1.md
Purpose:     Page spec for /industries/automotive. An INSTANCE of the LOCKED
             IndustryShell shape (page-industry-heavy-manufacturing-spec-v2.md
             §10) — the Automotive vertical fill of the fixed 8-section layout.
             Does NOT redefine the shape; §1–§9 only.
Audience:    Internal — copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), engineering (IndustryShell component implementers).
Format:      Per §9 canonical template locked in page-capabilities-hub-spec-v1.md.
Shape source: page-industry-heavy-manufacturing-spec-v2.md (LOCKED IndustryShell
                shape-setter) — its §10 defines the 8-section layout + cardinality
                (Relevant solutions 3-5, FAQ 4-6). CITED as "v2 §10" throughout.
Companion:   phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.1 IndustryShell
                skeleton, §4 proof/anonymity discipline, §5 buyer map, §6
                anti-duplication map, §9 acceptance gate)
             buyer-taxonomy-v1.md §2.2 (plant manager) + §2.3 (OT architect)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             page-capabilities-connectivity-edge-spec-v1.md (protocol wording source)
             design-system-v4.md (IndustryShell lands as v5 §25)
             LOCKED cross-link targets (do NOT duplicate):
                page-solutions-cnc-machining-spec / -precision-manufacturing /
                -predictive-maintenance / -multi-site-operations
                page-capabilities-{connectivity-edge,operational-intelligence,
                condition-monitoring,data-acquisition}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v1 — DRAFT. Awaits Pass 1 ChatGPT review before v2 lock.
Date:        2026-06-06
Status:      DRAFT. Second IndustryShell instance (after the Heavy Manufacturing
             shape-setter). Inherits the v2 §10 shape verbatim.

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7.
Word-count target: ~1,000-1,400 words page copy. Current: ~1,210 words.
-->

# `/industries/automotive` — Page Spec v1 (DRAFT)

> **⚠️ SUPERSEDED — retained as historical reference.** The LOCKED spec is **`page-industry-automotive-spec-v2.md`** (ChatGPT Pass 1 approved with no content edits).

**Vertical lens over the existing platform/solution/capability content, told through automotive vocabulary and pain. Plant manager primary; the plant's OT architect secondary. Reader self-identifies ("this is my line"), sees which solutions fit, gets honest proof posture, and is routed to the LOCKED owners of the detail. This spec is an INSTANCE of the `IndustryShell` shape locked in `page-industry-heavy-manufacturing-spec-v2.md` §10 — it fills the per-vertical slots, it does not redefine the shape.**

This is the page an automotive operations leader lands on from a vertical search ("CNC monitoring for automotive", "automotive line OEE platform", "press and robot condition monitoring") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *automotive's* reality and cross-links to the authoritative owners.

The 8-section layout, visual modes, components, and section cardinality below are inherited verbatim from the `IndustryShell` shape (v2 §10). Automotive fills the per-vertical slots: hero, reality narrative, foregrounded pillars, **4** relevant-solution cards (shape allows 3-5), proof posture, and **5** FAQ Q&A (shape allows 4-6).

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** An automotive-framed entry point. Reader leaves with *"Elpis understands a high-volume automotive line — machining cells and stamping presses and robotic weld/assembly cells, FANUC next to Siemens next to Mazak and PLC-fronted machines, takt time and OEE and traceability that can't slip — and here are the solutions that fit and where to go next."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer, competitor name, or certification claim (phase3 memo §4)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Plant / operations manager (§2.2) — wants to know Elpis grasps a high-volume mixed-vendor line where a minute of downtime has a per-minute cost, and that the consequence-heavy equipment (presses, weld/assembly robots, spindle motors) is covered. CTA preference: *"Book a scoping call."*

**Secondary:** The plant's OT architect / controls engineer (§2.3) — wants protocol-coverage honesty (including how robots and PLCs are reached), takt/OEE integrity, and condition-monitoring reach. CTA preference: *"Request an architecture review."*

- Vocabulary that lands: takt time, line downtime per minute, OEE, traceability, machining cells, stamping presses, robotic weld/assembly cells, FANUC / Siemens S7 / Mazak, tier-1/tier-2, multi-plant, per-plant, store-and-forward, beside-not-replacing.
- Vocabulary that backfires: "Industry 4.0 transformation", "AI-powered", "single pane of glass", "turnkey digitalization", any unqualified percentage.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Automotive — line intelligence across mixed iron · Elpis* |
| **Meta description** (140-160 chars) | *One operational view across automotive machining cells, stamping presses, and robotic weld/assembly lines — plus condition monitoring on the equipment that stops a line. OEE and traceability ready.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/automotive` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup. |

---

## 2. Page structure — sections at a glance (the IndustryShell layout)

8 sections. This table reproduces the `IndustryShell` shape locked in **v2 §10** — section order, visual modes, and component choices do not vary by vertical. Cardinality is fixed at the shape level: **Relevant solutions = 3-5 cards; FAQ = 4-6 Q&A.** Automotive uses **4** and **5** respectively.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~110 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (2-3) + pullquote | ~280 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (3-5 cards; automotive uses 4) | ~120 |
| **5** | Proof posture for this industry (anonymized cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~120 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 4-6 Q&A (automotive uses 5), `FAQPage` schema | ~220 |
| **7** | Cross-lens navigation | `light-tinted` | §17 cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Automotive instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · AUTOMOTIVE
>
> HEADLINE (size.3xl semibold):
> An automotive line runs on takt. One stalled cell costs the whole line — and you shouldn't be blind to which one.
>
> SUBHEAD (size.lg, max-width 60ch):
> Machining cells, stamping presses, and robotic weld and assembly cells — FANUC next to Siemens S7 next to Mazak, with PLC-fronted machines in between. Elpis collects from all of it over native protocols, normalizes every signal to one vocabulary, and watches the presses, robots, and motors that stop a line — without replacing a single validated machine. Operating across India and the Middle East.
>
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Automotive%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. The geography anchor ("Operating across India and the Middle East.") is reproduced verbatim — it is a footprint statement, not a customer claim. NO defense/space-agency anchor here (reserved for `/industries/defense` + `/aerospace` per memo §3.1 + v2 §10.3).

### 3.2 Section 2 — The reality on an automotive line

> EYEBROW: THE REALITY ON AN AUTOMOTIVE LINE
>
> SECTION TITLE: One takt time. A dozen vendors. Zero tolerance for a stalled cell.

> NARRATIVE PARA 1:
> An automotive line is a chain where every link runs to the same beat. A machining cell turning an engine block or a transmission case. A stamping press feeding the body shop. A row of robotic weld and assembly cells. They came from different vendors in different years — FANUC turning centers, Siemens-controlled press and line PLCs, Mazak machining centers, robots and stations fronted by PLCs — and each one reports in its own dialect, if it reports at all. The line only moves as fast as its slowest link, and the moment one cell stalls, the cost is the whole line, by the minute.

> NARRATIVE PARA 2:
> The pressure is relentless and it comes from two directions. Above, the OEM and the tier-1 contract demand traceability and an OEE number that holds up under audit. On the floor, takt time leaves no slack — a press throwing intermittent faults or a weld robot whose servo is drifting becomes a line stoppage before anyone has stitched the signals together. OEE today is reconciled across cells after the shift, describing a problem that already ate into the day's count. And the data that would have flagged the drift was on the line the whole time — just never reaching the people who could act on it in takt.

> NARRATIVE PARA 3:
> Replacing the equipment isn't the move. It's validated for the parts it runs, qualified by the customer, and tuned by the operators who run it. What needs to modernize is the data layer, not the line. The automotive plants that get there put one protocol-agnostic runtime in front of every controller and PLC, normalize every signal at the edge, instrument the presses and robots and motors that stop a line, and let the existing SCADA and MES keep doing their jobs.

> PULLQUOTE: "The line only moves as fast as its slowest link — and the data that would have flagged it was there the whole time."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES ON AN AUTOMOTIVE LINE
> SECTION TITLE: The data layer modernizes. The validated line stays.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Speak every controller and PLC on the line.** EdgeConnect polls your mixed line over native protocols — FOCAS2 for FANUC, Siemens S7 for press, line, and cell PLCs, Modbus TCP for older machines fronted by a PLC, MTConnect for open-standard machines (Mazak and others), Brother HTTP for Brother machining centers, and OPC UA Client where a controller exposes it. Robots and stations are read the same way they expose themselves — over S7, OPC UA Client, or Modbus from their controlling PLC. Canonical vocabulary at the edge means a cycle count, a fault code, or a servo reading means the same thing whichever cell produced it. FANUC MT-LINKi REST integration is on the roadmap. → `/capabilities/connectivity-edge`
>
> **One OEE and takt truth across every cell.** EREMOS V2 computes OEE Segments from the edge-collected signals against your OEE definition — so the number that took a post-shift reconciliation to assemble holds the same meaning across a stamping press and a robotic assembly cell, and availability and performance can be read against takt rather than after it. → `/capabilities/operational-intelligence`
>
> **Watch the equipment that stops a line.** VAS reads vibration signatures on the rotating and reciprocating equipment a line lives on — stamping presses, weld and assembly robots, spindle motors, and fans — to give early warning when a signature crosses a threshold your maintenance team sets. E-IDOS reads particle contamination and water saturation on hydraulic-press and lubrication systems (ISO 4406 / NAS 1638). The aim is to trend ahead of a failure and act between shifts, not to react to a stopped line. → `/capabilities/condition-monitoring`
>
> **Reach the signals the PLC won't give you.** Where a station or machine exposes nothing useful, mDAQ acquires the sensor signal directly — temperature, pressure, flow, vibration — without waiting on a controller retrofit or a robot-vendor integration. → `/capabilities/data-acquisition`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT AN AUTOMOTIVE LINE
> (4 cards → LOCKED solution pages; shape allows 3-5 per v2 §10.2)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · CNC MACHINING | Mixed-vendor machining cells — engine, transmission, driveline — on one operational view. | `/solutions/cnc-machining` |
| 2 | SOLUTION · PRECISION MANUFACTURING | OEE and traceability that hold up to OEM and tier-1 audit. | `/solutions/precision-manufacturing` |
| 3 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on presses, robots, and motors — before a stalled cell stops the line. | `/solutions/predictive-maintenance` |
| 4 | SOLUTION · MULTI-SITE OPERATIONS | One fleet view across every plant you run. | `/solutions/multi-site-operations` |

### 3.5 Section 5 — Proof posture for automotive

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built for lines where downtime is measured by the minute.

> TRUST CUE (category-level — NO customer names, NO metrics):
> Elpis is deployed across high-volume machining and assembly operations — mixed-vendor lines with consequence-heavy presses, robots, and motors. **Operating across India and the Middle East.** The platform runs offline-first: the license validates locally with no phone-home, and per-route store-and-forward is built to preserve every reading through a network or broker drop — queuing locally and replaying in source order on reconnect. Every configuration change is captured in a hash-chained, tamper-evident audit trail.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Automotive uses CATEGORY-LEVEL proof framing + the verbatim geography anchor only (v2 §10.3 — automotive is one of the four non-defense verticals). It does NOT use the defense/space-agency anchor (reserved for `/industries/defense` + `/aerospace`). No customer names, no metrics — per phase3 memo §4 + §9 gate.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A (shape allows 4-6 per v2 §10.2), `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What automotive teams ask first.

**Q1. Which controllers and robots can you actually collect from?**
> FANUC over FOCAS2, Siemens controllers over S7, older machines fronted by a PLC over Modbus TCP, plus MTConnect (Mazak and other open-standard machines), Brother HTTP, and OPC UA Client where it's exposed — all shipping today. Robots and stations are read however they expose themselves — typically over S7, OPC UA Client, or Modbus from their controlling PLC; there is no separate robot-only protocol. FANUC MT-LINKi REST integration is on the roadmap. Bring the controller and cell list to the scoping call and we confirm the collection path per machine.

**Q2. Do we have to take validated line equipment offline to connect it?**
> No. EdgeConnect reads each controller and PLC as a read-only client — it never changes control logic, and no cell comes offline to connect it. The validated line stays as qualified; the data layer modernizes around it.

**Q3. Can you catch a press, robot, or motor failure before it stalls the line?**
> That's what VAS and E-IDOS are for. VAS reads vibration signatures on presses, robots, and motors; E-IDOS reads hydraulic and lubrication oil health. They give early warning when a signature crosses a threshold your maintenance team defines — a trigger to act between shifts, not a guarantee against every failure.

**Q4. We run more than one plant. Does this aggregate?**
> Each plant runs its own EdgeConnect with a per-gateway identity; EREMOS V2 aggregates across plants for a fleet view. Multi-site visibility comes from aggregation — never from one runtime stretched across plants. → `/solutions/multi-site-operations`

**Q5. Does this replace our SCADA or MES?**
> No. Elpis sits beside them. EdgeConnect publishes canonical signals (MQTT, OPC UA Server); EREMOS V2 exposes OEE, alarms, and reports via API. Your SCADA keeps operator HMIs and control; your MES keeps scheduling, work orders, and traceability records. → `/architecture`

### 3.7 Section 7 — Cross-lens

Per design-system §17. Preset for `IndustryShell` (v2 §10.2): `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your line — every cell and every vendor.
> SUBHEAD: A controller and cell list, the presses and robots that worry you, and an OEE definition — that's enough to scope a proof of value. We run it on your real protocols against your real signals, in takt.
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Automotive%20scoping`
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

All copy in §3.1-§3.8. **~1,210 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~115 |
| 3.2 | Reality (3 paras + pullquote) | ~290 |
| 3.3 | What Elpis does here (4 blocks) | ~330 |
| 3.4 | Relevant solutions (4 cards) | ~115 |
| 3.5 | Proof posture | ~120 |
| 3.6 | FAQ (5 Q&A) | ~225 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4:

| Don't | Why |
|---|---|
| Quote a fabricated downtime-cost / OEE-gain / takt-improvement / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name an automotive customer, OEM, or tier-1, or use a logo | phase3 memo §4 — zero named customers this wave |
| Use the defense/space-agency anchor here | Reserved for `/industries/defense` + `/aerospace` (memo §3.1; v2 §10.3). Automotive = category-level + geography anchor only |
| Invent a robot-specific protocol | Robots/PLCs are read over S7 / OPC UA Client / Modbus — no robot-only protocol exists (§3.3 + §3.6 Q1) |
| Re-derive the OEE / condition-monitoring / connectivity capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| List MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — all three are roadmap |
| Imply Elpis replaces SCADA / MES, or that one runtime spans plants | beside-not-replacing + per-plant identity (carried in §3.6 Q4 + Q5) |
| Promise VAS/E-IDOS "prevents all failures" or "eliminates downtime" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q3) |
| Promise store-and-forward "never" loses data | Use designed-to-preserve language ("built to preserve … replaying in source order") — §3.5 |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.2/§2.3 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos (no OEM / tier-1 names either)
- [x] No certification claims (ISO 4406 / NAS 1638 are oil-cleanliness *report codes*, not Elpis certifications)
- [x] No competitor names (FANUC / Siemens / Mazak / Brother named as the customer's own equipment, not as competitors)
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only; robots/PLCs read via S7/OPC UA Client/Modbus (no invented robot protocol)
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] Locked geography anchor reproduced verbatim & standalone; defense/space-agency anchor NOT used (reserved)

---

## 8. Sign-off checklist (v1 DRAFT — awaits ChatGPT review)

- [x] Page copy within ~1,000-1,400 words (current ~1,210)
- [x] All 8 IndustryShell sections present (§2); shape cited to v2 §10
- [x] §3.1 hero industry-framed; geography anchor verbatim & standalone; no defense anchor
- [x] §3.3 maps pillars via cross-link, never re-derives (the 4 memo-specified foreground pillars)
- [x] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages (4 cards; shape 3-5)
- [x] §3.5 proof posture category-level + geography anchor verbatim & standalone; cross-links `/security` + `/customers`
- [x] §3.6 FAQ vertical-calibrated, 5 Q&A (shape 4-6), `FAQPage` schema; protocol/peer/SCADA honesty intact
- [x] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [x] §7 Phase 3 acceptance gate all green
- [x] Shape inherited from v2 §10 verbatim; this spec adds NO §10 of its own
- [ ] ChatGPT Pass 1 review applied → promote to v2 LOCKED

---

## 9. Out of scope for this page

- Named / quantified automotive case studies (blocked on signed sign-off — memo §3.3 business dependency)
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)
- The `IndustryShell` shape definition itself (owned by `page-industry-heavy-manufacturing-spec-v2.md` §10 — this spec is an instance, not a shape-setter)

---

*`/industries/automotive` Page Spec **v1 DRAFT 2026-06-06**. Second `IndustryShell` instance after the Heavy Manufacturing shape-setter; inherits the 8-section vertical-lens layout + cardinality (solutions 3-5, FAQ 4-6) from `page-industry-heavy-manufacturing-spec-v2.md` **§10** verbatim — this spec is §1–§9 only and defines no shape of its own. Automotive lens: high-volume mixed-vendor lines (machining cells, stamping presses, robotic weld/assembly), takt time, per-minute line-downtime cost, OEE + traceability, tier-1/tier-2 pressure, multi-plant. Foregrounds 4 pillars (connectivity-edge, operational-intelligence, condition-monitoring, data-acquisition) and 4 solutions (cnc-machining, precision-manufacturing, predictive-maintenance, multi-site-operations), all via cross-link — re-derives none (memo §6). Category-level proof + verbatim standalone geography anchor ("Operating across India and the Middle East."); defense/space-agency anchor NOT used (reserved, v2 §10.3). Protocol status verbatim (FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST + Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only; robots/PLCs via S7/OPC UA Client/Modbus, no invented robot protocol). §7 pre-checks the memo §9 acceptance gate (all green). ~1,210 words within target. Next: ChatGPT Pass 1 review → v2 lock. Cites: phase3-ia-scope-memo-v2 (parent); page-industry-heavy-manufacturing-spec-v2 §10 (shape source); buyer-taxonomy v1 §2.2/§2.3; proof-architecture v1 §3/§4/§8; positioning v3 §4; page-capabilities-connectivity-edge-spec v1 (protocol wording); design-system v4 (→ v5 §25 IndustryShell).*
