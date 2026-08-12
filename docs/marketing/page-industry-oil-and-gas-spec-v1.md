<!--
File:        docs/marketing/page-industry-oil-and-gas-spec-v1.md
Purpose:     Page spec for /industries/oil-and-gas. INSTANCE of the LOCKED
             IndustryShell shape (shape source: page-industry-heavy-
             manufacturing-spec-v2.md §10). Provides the Oil & Gas vertical
             instance copy; inherits the 8-section layout, cardinality, and
             honesty invariants from the shape-setter. Does NOT define a new
             shape — it fills the per-vertical slots (§10.2) for oil & gas.
Audience:    Internal — copywriters (lifting verbatim text), user + ChatGPT
             (reviewers), engineering (IndustryShell implementers).
Format:      Per the shape-setter's canonical template (HM v2 §1–§9 instance
             structure; the shape itself lives in HM v2 §10).
Companion:   page-industry-heavy-manufacturing-spec-v2.md (LOCKED shape-setter
                — IndustryShell §10: fixed structure §10.1, per-vertical slots
                §10.2, proof-posture override §10.3, honesty invariants §10.4)
             phase3-ia-scope-memo-v2.md (LOCKED PARENT — §4 proof/anonymity,
                §5 buyer map, §6 anti-duplication, §9 acceptance gate)
             buyer-taxonomy-v1.md §2.2 (operations leader) + §2.3 (OT architect)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             hardware-ecosystem-map §264 (hazardous-area = IP-compatibility,
                case-by-case, NO certification claim)
             page-capabilities-connectivity-edge-spec-v1.md (protocol wording source)
             LOCKED cross-link targets (do NOT duplicate):
                page-capabilities-{data-acquisition,condition-monitoring,
                asset-intelligence,connectivity-edge}-spec
                page-solutions-{predictive-maintenance,edge-connectivity,
                multi-site-operations,brownfield-modernization}-spec
                page-architecture-spec / page-platform-spec / page-security-spec
Version:     v1 — DRAFT (pre-ChatGPT review).
Date:        2026-06-06
Status:      DRAFT. Sibling vertical instance of the LOCKED IndustryShell shape.

This spec is §1–§9 (the per-vertical instance). The reusable IndustryShell
shape lives in the shape-setter at v2 §10 and is NOT re-defined here — this
page cites it as the shape source.

§9 ACCEPTANCE GATE (phase3 memo v2 §9) — pre-checked in §7.
Word-count target: ~1,000-1,400 words page copy. Current: ~1,200 words.
-->

# `/industries/oil-and-gas` — Page Spec v1 (DRAFT)

> **⚠️ SUPERSEDED — retained as historical reference.** The LOCKED spec is **`page-industry-oil-and-gas-spec-v2.md`** (Pass-1 edit: §3.6 Q3 "validated per deployment" → "assessed per deployment").

**Vertical lens over the existing platform/solution/capability content, told through oil & gas vocabulary and pain. Operations leader primary; the field's OT architect secondary. Reader self-identifies ("these are my sites"), sees which solutions fit, gets honest proof posture — with sharp discipline on hazardous-area framing — and is routed to the LOCKED owners of the detail. This page is an INSTANCE of the reusable `IndustryShell` layout defined in the shape-setter (v2 §10); it does not define its own shape.**

This is the page an oil & gas operations or reliability leader lands on from a vertical search ("remote pump-station monitoring", "pipeline condition monitoring platform", "offline edge data acquisition oil and gas") or from the `/industries/` hub. It is **not** a capability deep-dive (`/capabilities/<pillar>` ×5, LOCKED), **not** an outcome narrative (`/solutions/<solution>` ×7, LOCKED), **not** the architecture walkthrough (`/architecture`, LOCKED). It is a **vertical lens**: it re-frames what Elpis already does for *this industry's* reality — remote, unmanned, often controller-poor, intermittently connected — and cross-links to the authoritative owners.

Target length: **~1,000-1,400 words** per phase3 memo §3.1 (vertical lens).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** An oil & gas-framed entry point. Reader leaves with *"Elpis understands remote and unmanned sites — well heads and pump stations with no PLC and no reliable network — where you read the sensor directly, run on battery and 4G, operate offline, and watch the rotating equipment that strands a site when it fails. Here are the solutions that fit and where to go next."*

**IS NOT:**
- A capability deep-dive (cross-links to `/capabilities/<pillar>`; never re-derives)
- An outcome narrative (cross-links to `/solutions/<solution>`; never re-tells in full)
- The architecture walkthrough (`/architecture`) or vendor worldview (`/platform`)
- A customer-story page (`/customers` carries anonymized proof; this page carries a proof *cue* + link)
- A source of any fabricated metric, named customer, competitor name, or **hazardous-area certification claim** (phase3 memo §4)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Operations / reliability manager (§2.2) — runs upstream/midstream/downstream assets across dispersed sites, wants to know Elpis can reach remote and unmanned equipment without a controller or a steady network, and that the consequence-heavy rotating equipment (pumps, compressors, turbines) is covered. CTA preference: *"Book a scoping call."*

**Secondary:** The field's OT architect / control engineer (§2.3) — wants honesty on direct-sensor acquisition vs. PLC reads, offline store-and-forward + backhaul behavior, hazardous-area framing, and that nothing here displaces SCADA. CTA preference: *"Request an architecture review."*

- Vocabulary that lands: remote, unmanned, well head, pump station, compressor, turbine, pipeline, direct sensor read, battery, 4G backhaul, offline / store-and-forward, rotating equipment, hydraulic / lubrication oil health, beside-SCADA, per-site.
- Vocabulary that backfires: "Industry 4.0 transformation", "AI-powered", "single pane of glass", "intrinsically safe" (unless framed as IP-compatibility per §3.6), any unqualified percentage, any cert logo.

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub §9 metadata governance.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Oil & Gas — remote, offline-first monitoring · Elpis* |
| **Meta description** (140-160 chars) | *Monitor remote and unmanned oil & gas sites where there's no PLC and no reliable network — direct sensor reads on battery and 4G, offline, beside your SCADA.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/industries/oil-and-gas` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §3.6 inline FAQ uses `FAQPage` schema. Cross-links to `/solutions/<solution>`, `/capabilities/<pillar>`, `/architecture`, `/platform`, `/security` use `relatedLink`. No trust-anchor schema markup (Phase 3 customer registry handles structured proof later). |

---

## 2. Page structure — sections at a glance (the IndustryShell layout)

8 sections. The shape is the LOCKED `IndustryShell` layout (shape source: shape-setter **v2 §10**). Cardinality is fixed at the shape level: **Relevant solutions = 3-5 cards; FAQ = 4-6 Q&A.** Oil & Gas uses **4** and **5** respectively.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub + 2 CTAs) | `dark-deep` | `SectionShell` + `Button` ×2 | ~110 |
| **2** | The reality in this industry (vertical pain narrative) | `light` | Narrative paragraphs (2-3) + pullquote | ~280 |
| **3** | What Elpis does here (pillar/solution mapping, cross-linked) | `light-tinted` | Lead-paragraph blocks, each cross-linking a LOCKED owner | ~320 |
| **4** | Relevant solutions (cards → `/solutions/<solution>`) | `light` | Card grid (3-5 cards; O&G uses 4) | ~120 |
| **5** | Proof posture for this industry (anonymized cue) | `light-tinted` | Trust-cue block + `/security` + `/customers` cross-link | ~120 |
| **6** | Common questions (vertical-calibrated inline FAQ) | `light` | 4-6 Q&A (O&G uses 5), `FAQPage` schema | ~220 |
| **7** | Cross-lens navigation | `light-tinted` | Cross-lens (3 cards: `/solutions` + `/architecture` + `/platform`) | ~50 |
| **8** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail (Oil & Gas instance)

### 3.1 Section 1 — Hero

> EYEBROW: INDUSTRY · OIL & GAS
>
> HEADLINE (size.3xl semibold):
> Your assets are remote, unmanned, and offline half the time. Your monitoring shouldn't quit when the network does.
>
> SUBHEAD (size.lg, max-width 60ch):
> Well heads, pump stations, compressors, and turbines — spread across fields, often with no PLC to talk to and no reliable connection home. Elpis reads the sensor directly where there's no controller, runs on battery and 4G, keeps working offline, and watches the rotating equipment that strands a site when it fails. Operating across India and the Middle East.
>
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Oil%20and%20gas%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

**Anti-patterns:** No "Industry 4.0 transformation". No unqualified metric. No hazardous-area certification claim (no ATEX / IECEx / Ex / Class-Div / "intrinsically safe"). The geography anchor ("Operating across India and the Middle East.") is reproduced verbatim — it is a footprint statement, not a customer claim. NO defense/space-agency anchor here (reserved for `/industries/defense` + `/aerospace` per memo §3.1 / shape-setter §10.3).

### 3.2 Section 2 — The reality in oil & gas

> EYEBROW: THE REALITY IN THE FIELD
>
> SECTION TITLE: The asset is in the field. The data has to come to you.
>
> NARRATIVE PARA 1:
> Oil & gas assets don't sit on a tidy factory floor. A well head on a desert lease. A pump station an hour off the nearest road. A compressor skid on a pipeline right-of-way. A turbine in a remote facility no one staffs full-time. Many were never built around a programmable controller, and the ones that have one often expose nothing useful — a locked, proprietary box bought for control, not for data. The reading you need is right there at the equipment. Nothing is carrying it anywhere.
>
> NARRATIVE PARA 2:
> Then there's the network. Connectivity at these sites is intermittent by nature — a 4G signal that comes and goes, a satellite link that's expensive and slow, hours or days between backhaul windows. A monitoring approach that assumes a steady connection home simply stops working the moment the link drops, and the data that would have flagged a failing pump or a contaminated lubrication system is lost on the spot — exactly when an unmanned site can least afford a surprise. Rotating equipment is where the consequence concentrates: a pump, a compressor, or a turbine going down doesn't slow production, it strands a site.
>
> NARRATIVE PARA 3:
> The answer isn't running fiber to every well head. It's putting the intelligence at the edge: read the sensor directly when there's no controller to ask, run on battery and 4G so the site doesn't depend on infrastructure it doesn't have, work offline and queue everything locally, and backhaul in order when the link returns. Watch the rotating equipment and the oil that keeps it alive, and let the SCADA system keep doing its job.
>
> PULLQUOTE: "A monitoring approach that assumes a steady connection home stops working the moment the link drops."

### 3.3 Section 3 — What Elpis does here

> EYEBROW: WHAT ELPIS DOES IN THE FIELD
> SECTION TITLE: Read it at the edge. Hold it through the outage. Send it when you can.

(Each block cross-links the LOCKED capability owner; copy summarizes, never re-derives — memo §6.)

> **Read the sensor directly — no controller required.** At remote and unmanned sites with no PLC, or a locked controller that exposes nothing, mDAQ acquires the signal straight from the sensor — temperature, pressure, flow, vibration — running on battery and 4G. It operates offline by design: readings queue locally and replay in source order when the link returns, so a backhaul gap doesn't become a data gap. → `/capabilities/data-acquisition`
>
> **Watch the equipment that strands a site.** VAS reads vibration signatures on the rotating equipment a field lives on — pumps, compressors, turbines, fans — and E-IDOS reads particle contamination and water saturation on hydraulic and lubrication oil (ISO 4406 / NAS 1638). Early warning on a signature your reliability team sets, so a failing bearing or a degrading lube system shows up before it pulls an unmanned asset down. → `/capabilities/condition-monitoring`
>
> **Know where the asset is and how hard it's working.** mTracker reports utilization and location on remote and mobile assets — so a compressor on a lease or a pump skid in the field stays accountable even when no one is standing next to it. → `/capabilities/asset-intelligence`
>
> **Speak the controller where one exists.** Where a site does have a PLC or controller worth reading, EdgeConnect polls it over native protocols — Modbus TCP, OPC UA Client where it's exposed, plus the wider shipped protocol set — and normalizes every signal to one canonical vocabulary at the edge. → `/capabilities/connectivity-edge`

### 3.4 Section 4 — Relevant solutions

> EYEBROW: SOLUTIONS THAT FIT A REMOTE FIELD
> (4 cards → LOCKED solution pages; shape allows 3-5)

| Card | Eyebrow | One-line | Destination |
|---|---|---|---|
| 1 | SOLUTION · PREDICTIVE MAINTENANCE | Early warning on pumps, compressors, and turbines — before a failure strands a site. | `/solutions/predictive-maintenance` |
| 2 | SOLUTION · EDGE CONNECTIVITY | Read it at the edge and hold it through the outage — battery, 4G, offline. | `/solutions/edge-connectivity` |
| 3 | SOLUTION · MULTI-SITE OPERATIONS | One view across every field, lease, and station you run. | `/solutions/multi-site-operations` |
| 4 | SOLUTION · BROWNFIELD MODERNIZATION | Modern monitoring from assets that were never built for it. | `/solutions/brownfield-modernization` |

### 3.5 Section 5 — Proof posture for oil & gas

> EYEBROW: PROOF POSTURE
> SECTION TITLE: Built for sites no one is standing next to.

> TRUST CUE (category-level — NO customer names, NO metrics):
> Elpis is deployed across remote and distributed industrial operations — dispersed sites with consequence-heavy rotating equipment and intermittent connectivity. **Operating across India and the Middle East.** The platform runs offline-first: the license validates locally with no phone-home, and per-route store-and-forward is built to preserve every reading through a network drop — queuing locally and replaying in source order on reconnect or scheduled backhaul. Every configuration change is captured in a hash-chained, tamper-evident audit trail.

> CROSS-LINKS: Full operational trust posture → `/security`  ·  Anonymized deployment patterns → `/customers`

> **Governance note (not displayed):** Oil & Gas uses CATEGORY-LEVEL proof framing + the verbatim geography anchor only. It does NOT use the defense/space-agency anchor (reserved for `/industries/defense` + `/aerospace`, per shape-setter §10.3). No customer names, no metrics, **no hazardous-area certification claim** — per phase3 memo §4 + §9 gate and hardware-ecosystem-map §264.

### 3.6 Section 6 — Common questions

Per `/capabilities` hub §9 FAQ governance. 5 vertical-calibrated Q&A (shape allows 4-6), `FAQPage` schema.

> EYEBROW: COMMON QUESTIONS
> SECTION TITLE: What oil & gas teams ask first.

**Q1. Our remote sites have no PLC and no reliable network. Can you still monitor them?**
> Yes — that's the design point. Where there's no controller to ask, mDAQ reads the sensor directly (temperature, pressure, flow, vibration) on battery and 4G. It operates offline: readings queue locally in per-route store-and-forward and replay in source order when the link returns or the next backhaul window opens. A connectivity gap doesn't have to become a data gap.

**Q2. Can you monitor our pumps, compressors, and turbines — plus the hydraulics — for early failure?**
> Yes. VAS reads vibration signatures on rotating equipment (pumps, compressors, turbines, fans); E-IDOS reads hydraulic and lubrication oil health — particle contamination and water saturation (ISO 4406 / NAS 1638). They give early warning when a signature crosses a threshold your reliability team defines — a better trigger than a calendar, not a guarantee against every failure.

**Q3. Our sites are in hazardous areas. Is your hardware certified for that?**
> We make no hazardous-area certification claim — no ATEX, IECEx, Ex, or Class/Division rating, and we do not describe the hardware as "intrinsically safe." What we do is evaluate enclosure IP-rating compatibility for your specific environment, case-by-case, during the hardware BOM and scoping work — validated per deployment, not asserted as a blanket certification. Bring your area classification to the scoping call and we assess fit honestly.

**Q4. We operate multiple fields and stations. Does this aggregate across sites?**
> Each site runs its own edge deployment with a per-site identity; EREMOS V2 aggregates across sites for a fleet view. Multi-site visibility comes from aggregation — never from one runtime stretched across fields, which would defeat the offline-first design. → `/solutions/multi-site-operations`

**Q5. Does this replace our SCADA?**
> No. Elpis sits beside it. EdgeConnect and mDAQ publish canonical signals (MQTT, OPC UA Server); EREMOS V2 exposes condition status, alarms, and reports via API. Your SCADA keeps operational control and the operator picture; Elpis adds the edge acquisition, condition monitoring, and asset intelligence layer on top. → `/architecture`

### 3.7 Section 7 — Cross-lens

Cross-lens preset for `IndustryShell` (shape-setter §3.7 / §10.2): `/solutions` + `/architecture` + `/platform`.

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | SOLUTIONS | Every outcome, organized by the problem it solves | `/solutions` |
| 2 | ARCHITECTURE | How the pieces connect into one stack | `/architecture` |
| 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.8 Section 8 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your hardest site — the one no one wants to drive to.
> SUBHEAD: The asset list, the rotating equipment that worries you, what (if anything) exposes data today, and your connectivity reality — that's enough to scope a proof of value. We run it on direct sensor reads or your real protocols, offline-first, against your real signals.
> PRIMARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Oil%20and%20gas%20scoping`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

---

## 4. Components used

All from design-system v4 LOCKED + the `IndustryShell` composition (shape-setter §10 / design-system v5). No net-new primitives.

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

All copy in §3.1-§3.8. **~1,200 words** (within the ~1,000-1,400 vertical-lens target).

| § | Section | Words |
|---|---|---|
| 3.1 | Hero | ~115 |
| 3.2 | Reality (3 paras + pullquote) | ~290 |
| 3.3 | What Elpis does here (4 blocks) | ~315 |
| 3.4 | Relevant solutions (4 cards) | ~120 |
| 3.5 | Proof posture | ~120 |
| 3.6 | FAQ (5 Q&A) | ~225 |
| 3.7 | Cross-lens | ~50 |
| 3.8 | Final CTA | ~70 |

---

## 6. Anti-patterns specific to this page

In addition to design-system §21 system-wide + phase3 memo §4:

| Don't | Why |
|---|---|
| Claim ATEX / IECEx / Ex / Class-Div certification, or call hardware "intrinsically safe" | hardware-ecosystem-map §264 — hazardous-area framing is IP-rating *compatibility*, case-by-case during BOM/scoping; NO formal cert claim. Carried in §3.6 Q3 |
| Quote a fabricated downtime / availability / payback metric | phase3 memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Name an oil & gas customer or use a logo | phase3 memo §4 — zero named customers this wave |
| Use the defense/space-agency anchor here | Reserved for `/industries/defense` + `/aerospace` (shape-setter §10.3). Oil & gas = category-level + geography anchor only |
| Re-derive the data-acquisition / condition-monitoring / asset-intelligence / connectivity capability story in full | memo §6 anti-duplication — summarize + cross-link to the LOCKED `/capabilities/<pillar>` owner |
| Imply CNC protocols (FOCAS2 / S7 / Brother) dominate here, or list MT-LINKi as shipped, or imply EdgeConnect Linux / E-IDOS→EREMOS streaming is current | P-G protocol honesty — oil & gas leans on mDAQ / Modbus / OPC UA; MT-LINKi REST + Linux + E-IDOS→EREMOS streaming are roadmap; mDAQ runs VAS only |
| Imply Elpis replaces SCADA, or that one runtime spans sites | beside-not-replacing + per-site identity (carried in §3.6 Q4 + Q5) |
| Promise VAS/E-IDOS "prevents all failures" | Early-warning framing only — "trigger, not guarantee" (§3.6 Q2) |
| Promise store-and-forward "never" loses data | Use designed-to-preserve language ("built to preserve … replaying in source order on reconnect or backhaul") — §3.5 |
| "Industry 4.0 transformation" / "AI-powered" / "single pane of glass" | buyer-taxonomy §2.2/§2.3 vocabulary discipline |

---

## 7. Phase 3 acceptance gate (phase3 memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent (header + intro)
- [x] Cites §4 proof / anonymity rules (§3.5 + §6)
- [x] No fabricated metrics (outcomes qualitative throughout)
- [x] No named customers / logos
- [x] No certification claims — **hazardous-area framing is IP-rating compatibility, case-by-case during scoping; NO ATEX/IECEx/Ex/Class-Div/intrinsically-safe claim** (§3.6 Q3 + §6); ISO 4406 / NAS 1638 are oil-cleanliness *report codes*, not Elpis certifications
- [x] No competitor names
- [x] Protocol status verbatim: FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST roadmap; Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only (oil & gas leans on mDAQ/Modbus/OPC UA — CNC protocols not implied to dominate)
- [x] No `/pricing` or pricing detail
- [x] No individual `/customers/<story>` route (links to `/customers` hub only)
- [x] No resource-asset claims on this page (N/A — resource cards live on `/resources`)
- [x] Does not re-derive a LOCKED Phase 2 authoritative explanation (§3.3 + §6 cross-link instead)
- [x] Locked geography anchor reproduced verbatim & standalone; defense/space-agency anchor NOT used (reserved)

---

## 8. Sign-off checklist (v1 DRAFT)

- [x] Page copy within ~1,000-1,400 words (current ~1,200)
- [x] All 8 IndustryShell sections present (§2); shape source cited as v2 §10
- [x] §3.1 hero industry-framed; geography anchor verbatim; no defense anchor; no hazardous-area cert claim
- [x] §3.3 maps pillars via cross-link, never re-derives; leads with Data Acquisition (mDAQ direct-sensor) + offline/remote
- [x] §3.4 solution cards point to LOCKED `/solutions/<solution>` pages (4 cards; shape 3-5)
- [x] §3.5 proof posture category-level + geography anchor verbatim & standalone; cross-links `/security` + `/customers`
- [x] §3.6 FAQ vertical-calibrated, 5 Q&A (shape 4-6), `FAQPage` schema; hazardous-area Q3 = IP-compatibility case-by-case during scoping, NO cert claim; protocol/peer/SCADA honesty intact
- [x] §3.7 cross-lens = `/solutions` + `/architecture` + `/platform`
- [x] §7 Phase 3 acceptance gate all green
- [ ] ChatGPT Pass 1 review (pending — v1 DRAFT; promote to v2 LOCKED after review)

---

## 9. Out of scope for this page

- Named / quantified oil & gas case studies (blocked on signed sign-off — memo §3.3 business dependency)
- Hazardous-area certification (ATEX / IECEx / Ex / Class-Div / intrinsically-safe) — not claimed; IP-compatibility assessed per deployment during scoping (hardware-ecosystem-map §264)
- Full capability detail (lives on `/capabilities/<pillar>`)
- Full solution narratives (live on `/solutions/<solution>`)
- The architecture walkthrough (`/architecture`) and vendor worldview (`/platform`)
- Pricing (Phase 4 `/pricing`)
- Resource downloads (live on `/resources/*`)

---

*`/industries/oil-and-gas` Page Spec **v1 DRAFT 2026-06-06**. Sibling vertical INSTANCE of the LOCKED `IndustryShell` shape (shape source: page-industry-heavy-manufacturing-spec **v2 §10**); this spec is §1–§9 and defines no shape of its own. 8-section vertical-lens layout; cardinality fixed at the shape level (solutions 3-5 → uses 4, FAQ 4-6 → uses 5). Leads with Data Acquisition (mDAQ direct-sensor reads at remote/unmanned sites, battery + 4G, offline) + Condition Monitoring + Asset Intelligence + Connectivity-where-PLCs-exist — NOT EdgeConnect-on-CNCs. Category-level proof + verbatim standalone geography anchor ("Operating across India and the Middle East."); NO defense/space-agency anchor (reserved, override §10.3). **Hazardous-area honesty (sharp for this vertical): NO ATEX/IECEx/Ex/Class-Div/intrinsically-safe certification claim — only enclosure IP-rating compatibility, evaluated case-by-case during the hardware BOM/scoping (hardware-ecosystem-map §264), carried verbatim in §3.6 Q3.** Protocol status verbatim (mDAQ/Modbus/OPC UA lean; MT-LINKi REST + Linux + E-IDOS→EREMOS streaming roadmap; mDAQ runs VAS only). Cross-links the LOCKED `/capabilities/<pillar>`, `/solutions/<solution>`, `/architecture`, `/platform`, `/security` owners; re-derives none (memo §6). §7 pre-checks the memo §9 acceptance gate (all green). ~1,200 words within target. Next: ChatGPT Pass 1 review → v2 LOCK. Cites: phase3-ia-scope-memo-v2 (parent); shape-setter v2 §10 (shape source); buyer-taxonomy v1 §2.2/§2.3; proof-architecture v1 §3/§4/§8; positioning v3 §4; hardware-ecosystem-map §264 (hazardous-area IP-compatibility); page-capabilities-connectivity-edge-spec v1 (protocol wording).*
