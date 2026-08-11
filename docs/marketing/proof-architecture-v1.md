<!--
File:        docs/marketing/proof-architecture-v1.md
Purpose:     Proof placement governance for the Elpis Industrial
             Intelligence Ecosystem. Governs WHERE proof belongs (per
             surface), WHAT counts as proof (vs aspirational claim),
             WHO is named (anonymity discipline), HOW metrics are
             validated, and HOW case studies are structured.
Audience:    Internal — Claude (page-spec author for Phase 2), user +
             ChatGPT (reviewers), marketing copywriters, sales / BD
             team, future customer-story authors.
Format:      Markdown governance doc. Companion to buyer-taxonomy-v1
             as the second of two pre-spec foundation docs.
Companion:   phase2-ia-scope-memo-v2.md + amendment v3 (parent IA model
                — promotes proof architecture to first-class doc)
             industrial-intelligence-ecosystem-positioning-v3.md §4
                (credibility anchors; customer-anonymity rule)
             positioning-amendment-v4.md (customer-logo authorization
                scope and limits)
             buyer-taxonomy-v1.md §2 (per-buyer proof expectations)
             roi-calculator-spec-v2.md §7 (customer-supplied-input
                discipline rule — model for outcome-proof governance)
             design-system-v3.md §13 TrustBand, §16 trust cue pattern
                (visual surfaces where proof lands)
             design-governance-v1.md (the discipline parent)
Version:     v1
Date:        2026-05-28
Status:      DRAFT pending user + ChatGPT review.

Per Phase 2 IA memo amendment v3 §3.2 — the second of two governance
docs committed before per-page specs begin. Locks once reviewed. With
both this and buyer-taxonomy-v1 LOCKED, the Phase 2 per-page spec
wave can begin per amendment v3 §4 sequencing.
-->

# Elpis Industrial Intelligence Ecosystem — Proof Architecture v1

**Where proof belongs, what counts as proof, who gets named, how metrics get validated, how customer stories get structured. The discipline that prevents proof drift across the Phase 2 spec wave and Phase 3+ customer-story program.**

Proof is what turns a marketing claim into a buying signal. The platform has real proof — defense and space-agency deployments, AMC channel reality, mature protocol coverage, named customer logos already public on the live site, operational primitives that survive technical review. The risk is not insufficient proof. The risk is **proof drift** — the same defense anchor cited differently across three pages, an OEE metric implied in copy but unverifiable in source, a screenshot of a stylized panel mistakenly described as production UI, a customer logo paired with an unsign-off-ed deployment story.

This doc locks the discipline that prevents that drift.

Per Phase 2 IA memo amendment v3 §3.2, this is the second of two pre-spec foundation documents (the first being `buyer-taxonomy-v1.md`). Both must lock before the 10 Phase 2 per-page specs begin.

---

## 1. The proof governance principle

> **Every published proof item must be verifiable. If it cannot be verified, it must not be published as proof.**

Verifiable means one of:

1. **Architectural primitive** that a reader can confirm by reading the source (e.g., "hash-chained configuration audit log" — verifiable in the gateway code; "RSA-signed offline license" — verifiable in the license file; "FOCAS2 0i / 16i / 18i / 21i / 30i / 31i / 32i support" — verifiable by connecting to a real controller).
2. **Real customer reference** the prospect can speak to (under NDA if needed) — for Phase 3+ case studies once customer sign-off lands.
3. **Customer-supplied input** the prospect provides themselves (the ROI calculator model — the customer enters their own operating constants and the math runs on those).
4. **Publicly available evidence** that already exists outside Elpis-controlled surfaces (e.g., customer logos already on www.elpisitsolutions.com — public association is established; or sensor partner brand names appearing in third-party datasheets).

Things that are **not verifiable** and therefore **not proof**:

- Fabricated ROI percentages ("typical 30% downtime reduction")
- Customer names paired with deployment stories that haven't been sign-off-ed
- Industry-average benchmarks the prospect can't independently confirm
- Compliance framework claims (ISO 27001, SOC 2, IEC 62443, 21 CFR Part 11) Elpis hasn't formally earned
- Generic "trusted by enterprises like yours" without specific anchors
- Stock-photo testimonials or AI-generated quotes
- Internal screenshots of unreleased product features

**The discipline:** if it's not verifiable, it doesn't get published as proof. It may still be true (perhaps Elpis really does reduce downtime by 30% for some customers) — but published unverifiable proof damages trust on the first technical or procurement review. The buying-signal value of restrained, verifiable proof is much higher than the lift from unverifiable claims.

---

## 2. The five categories of proof

Every proof item Phase 2 surfaces use falls into one of five categories. Each category has its own governance rules (sections 3-8 below).

| Category | What it covers | Examples |
|---|---|---|
| **Customer proof** | Named customer references and logos | Trust band logos, customer case studies, named-customer testimonials, partner references |
| **Outcome proof** | Metrics, before/after results, ROI data | OEE improvements, downtime reductions, hours saved on reporting, truck-roll cost savings |
| **Architectural proof** | Diagrams, integration patterns, deployment shapes, technical specifications | Architecture diagrams, protocol coverage tables, hardware specs, API contracts |
| **Trust proof** | Operational primitives, security posture, audit-readiness | Hash-chained audit log, RSA-signed licensing, AI constraint architecture, role separation, per-tag quality codes |
| **Operational proof** | Real protocol support, real controller models, real industrial specs | Specific Fanuc model coverage (0i-32i), Brother S700Xd1, Modbus TCP, OPC UA Server security modes |

The categories aren't watertight (a customer case study often includes outcome metrics; an architectural diagram often signals trust posture) — but the primary category determines where proof lives and how it's governed.

---

## 3. Primary-home map (proof by surface + category)

Extends the §4 anti-duplication map from Phase 2 IA memo v2 with proof-specific guidance. Each (surface, category) cell names where proof of that type lives as primary.

| Proof category | Primary home surface(s) | Referenced from (with cross-link, not duplication) |
|---|---|---|
| **Customer logos** (trust band) | Homepage §1.5 + `/platform` §4 (per positioning amendment v4) | `/customers` (Phase 3 grid variant); never on `/capabilities/*` or `/solutions/*` |
| **Named customer case studies** (Phase 3+) | `/customers` and `/customers/<story>` | `/solutions/<solution>` (one-line outcome quote teaser, links to full story); `/platform` (one-line teaser) |
| **Trust anchors** (defense / space-agency / AMC / geography) | `/platform` §4 | Homepage hero trust micro-strip + Section 7 proof band; `/security` (operational-trust context); never on capability pages directly |
| **Outcome metrics** (OEE, downtime, ROI) | `/solutions/<solution>` (in context of a specific outcome) | Homepage outcomes strip (compressed); `/capabilities/operational-intelligence` (as capability outcome); `/calculators/<calculator>` (Phase 4 interactive) |
| **Outcome benchmarks** (industry-average claims) | NOT PUBLISHED in Phase 2 | Phase 3+ once defensible benchmarks exist; until then, the ROI calculator model owns the math |
| **Architecture diagrams** | `/architecture` (interactive); `architecture-diagram-v2-*.svg` is the master | Homepage Section 3 (static embed); `/platform`; each `/solutions/<solution>` (annotated subset); each `/capabilities/<pillar>` (focused on one layer) |
| **Hardware specs** (Phase E product pages) | Phase E `/<product>` pages | `/capabilities/<pillar>` (short summary); `/architecture` (per-layer mention) |
| **Trust posture philosophy** | `/security` | `/platform`, `/architecture`, every capability page, every solution depth-example via the §16 trust cue content pattern |
| **Architectural security mechanics** (data path, audit chain, AI boundaries) | `/architecture` | `/security` cross-link |
| **Compliance framework claims** | NOT PUBLISHED until formally certified | When certifications land, they get their own `/security` sub-section |
| **Protocol coverage tables** | `/capabilities/connectivity-edge` (canonical) | Homepage connectivity strip; `/architecture` integration patterns; each `/solutions/<solution>` (filtered to relevant protocols) |
| **Product screenshots** (real EREMOS V2 / Studio UI) | NOT PUBLISHED in Phase 2 | Phase 3 introduces real screenshots once product is ready and approved — see §6 below |
| **Stylized visual signifiers** (HeroComposite dashboard panel) | Homepage hero only | NEVER on other surfaces; the signifier is locked to its home |

**The proof discipline:** if a Phase 2 page is tempted to include proof outside the primary-home cell for that surface, the right move is to **cross-link** to the primary home, not to duplicate. See `phase2-ia-scope-memo-v2.md` §4.0 (authoritative-explanation invariant) for the parent rule.

---

## 4. Customer anonymity discipline

The single most consequential proof-governance rule. Gets its own section because anonymity decisions are easy to get wrong and hard to reverse.

### 4.1 What's authorized today (per positioning amendment v4 §3)

**Customer logos:** GE, Hitachi, Toyota, Schneider Electric, BHEL, TVS, HYDAC, Filtrec (8 logos for the homepage trust band — already publicly displayed on www.elpisitsolutions.com). Plus reserved-for-future-use: Riverway, University of Agricultural Sciences (Bangalore), Software Toolbox (note: partner, not customer).

**Trust anchors (anonymous):**
- *"Deployed in defense and space-agency programs"* — covers ISRO (VAS, satellite radar antenna vibration monitoring) and MoD (E-IDOS via third-party supplier integration). The specific customer names stay confidential. The category descriptor is defensible.
- *"Operating across India and the Middle East"* — current deployment footprint. Establishes international relevance without overclaiming "global."

**Sensor ecosystem partners (already in positioning v3 §3.4):** HYDAC, Parker, MP Filter, Argo-hytos — supported sensor brands for E-IDOS. Public association is established via the contamination-sensor compatibility statement.

### 4.2 What stays anonymous (Phase 1-2)

**Specific deployment stories** stay anonymized regardless of whether the customer's logo is authorized. Per positioning amendment v4 §2:

| Allowed | Still locked (until Phase 3) |
|---|---|
| "GE" / "Hitachi" / "Toyota" as a logo in the trust band | "Toyota's plant in Bidadi uses Elpis VAS to monitor..." (deployment story tied to a specific named customer) |
| "Trusted by industrial leaders across automotive, energy, heavy manufacturing, and defense" | "Toyota improved OEE by N% using EdgeConnect" (outcome-paired-with-name) |

**Defense / space-agency customer names** stay anonymous even after the Phase 3 customer-story program activates, per the original positioning v3 §4 lock. The category descriptor is the proof; the names are not for external publication regardless of phase.

**AMC channel partners** stay anonymous at the partner level until Phase 4 partner portal formalization. *"Maintenance and AMC providers across India and the Middle East"* is the caption pattern.

### 4.3 The Phase 3 customer-story activation (when it ships)

Three preconditions, all required:

1. **Explicit per-customer sign-off** for both the name and the specific deployment claims being made. Sign-off is written, traceable, and dated.
2. **Verifiable claims only** — every metric the story references must be defensible by the customer's own data (not Elpis's projection of what the customer might be experiencing).
3. **Sign-off scope** — the customer signs off on specific approved phrasings, not a general "you can mention us." Phrasings that drift beyond approved scope are governance breaches.

The case-study structure (§7 below) is the template all Phase 3+ customer stories follow once sign-off lands.

### 4.4 Authorization workflow for net-new customer logos

If a new customer wants their logo on Elpis surfaces (or Elpis wants to add a new logo):

1. **Confirm public association already exists** (logo on customer's public references, joint press release, public case study, public partnership announcement). If not — pause; do not add unilaterally.
2. **Confirm legal authority** to use the logo per any signed agreements with that customer.
3. **Update `positioning-amendment-v5.md`** (or next sequential amendment) with the new logo authorization and any scope limits (e.g., logo only — no story until separate sign-off).
4. **Update `TrustBand` props** and any other surfaces explicitly authorized in the amendment.

The discipline is: amendments document the authorization. Logos don't quietly appear in commits without an amendment trail.

---

## 5. Metrics, benchmarks, and screenshots — validation rules

### 5.1 Metric validation

**Outcome metrics published on customer-facing surfaces must be validated against one of:**

- **A specific customer's data**, with that customer's sign-off (Phase 3+ case studies)
- **Customer-supplied input** through an interactive surface (the ROI calculator model — see `roi-calculator-spec-v2.md` §7 discipline rules)
- **Architectural primitives that produce the outcome** as direct consequence (e.g., "store-and-forward buffering eliminates lost cycles during broker outages" — verifiable architecturally, not as a metric)

**Forbidden:**

- *"Typical 30% downtime reduction"* — fabricated "typical" without source
- *"Customers report up to 50% faster diagnosis"* — vague "up to" with no specific source
- *"Industry-leading OEE improvements"* — unfalsifiable, content-free
- Specific percentages presented without the customer who experienced them

**The discipline (locked from ROI calc spec §7):** the calculator never fabricates value; every output is a function of customer-supplied inputs. The same rule governs every other surface — outcome metrics either come from a specific named customer (Phase 3+) or from customer-supplied input in an interactive tool. They never appear as floating "typical" claims.

### 5.2 Benchmark discipline

**A defensible benchmark requires:**

- **A documented source** — a customer study, an industry report Elpis can cite, public deployment data the buyer can verify
- **A compared-to-what frame** — *"Cuts truck rolls by 40% compared to dispatch-on-customer-call"* is defensible if the source supports it; *"40% better truck-roll cost"* without a baseline is content-free
- **A scope** — what kind of customer, what kind of deployment, what kind of measurement window

**Benchmarks Phase 2 surfaces should NOT use:**

- *"Best in class"* — unfalsifiable
- *"Industry-leading"* — content-free
- *"#1 in [category]"* — unprovable without third-party ranking
- *"Faster than [competitor]"* — competitive overclaim that backfires on technical review
- *"Up to N% improvement"* — the "up to" weasel word destroys credibility

**Defensible benchmarks in Phase 2:** focus on **architectural property benchmarks** ("Supports every Fanuc CNC generation that exposes FOCAS2 — 0i through 32i," "Works with any compliant MQTT broker"). These are verifiable. Outcome benchmarks defer to Phase 3+ customer stories.

### 5.3 Screenshot governance

**Phase 2 surfaces use:**

- **Stylized visual signifiers** for the intelligence layer (the `HeroComposite` dashboard panel per `homepage-spec-v3.md §3.1` — stylized, not real screenshot). Locked rule: *"NO real EREMOS V2 screenshot in HeroComposite until approved."*
- **Architecture diagrams** — abstract, no UI fidelity claims
- **Real hardware product photography** — for mDAQ in `HeroComposite`, hardware ecosystem section, Phase E product pages

**Phase 2 surfaces do NOT use:**

- Real EREMOS V2 product screenshots (Phase 3 once product ships and screenshots are approved)
- Real Connectivity Studio screenshots (Phase 3 when an approved screenshot set is available — until then, Studio is referenced via written description)
- AI-generated images of any kind
- Stock photography (handshakes, smiling operators, aerial assembly lines, generic factory shots)
- Competitor screenshots (never, regardless of phase)

**Phase 3 product-screenshot activation:**

1. Real EREMOS V2 product screenshots approved through a designated product-marketing approval workflow
2. Each screenshot pinned to a specific platform version (so future UI evolution doesn't silently invalidate the proof)
3. Screenshots NEVER substitute for the stylized `HeroComposite` panel — the stylized signifier remains the homepage hero pattern; product screenshots land in dedicated product pages and case studies

---

## 6. Trust anchor reuse

The defense / space-agency / AMC / geography anchors are powerful but easily over-reused. Discipline:

| Anchor | Primary home | Permitted re-use surfaces | Phrasing pattern (locked) |
|---|---|---|---|
| Defense / space-agency deployment | `/platform` §4 | Homepage hero trust micro-strip, Homepage Section 7 proof band, `/security` page (operational-trust context) | *"Deployed in defense and space-agency programs"* — exact phrasing; never elaborated beyond this without sign-off |
| AMC channel | `/platform` (positioning frame), `/capabilities/condition-monitoring` (buyer-context), `/solutions/predictive-maintenance` (use-case) | Sales objection guide internal (acknowledgment that AMC is real buyer reality) | *"Maintenance and AMC providers across India and the Middle East"* — exact phrasing |
| Geography (India + Middle East) | `/platform` §4 | Homepage hero trust micro-strip | *"Operating across India and the Middle East"* — exact phrasing |
| Sensor ecosystem partners (HYDAC, Filtrec, etc.) | `/capabilities/condition-monitoring` (E-IDOS sensor-agnostic context), homepage `TrustBand` | `/security` if relevant to supply-chain trust narrative (Phase 3+) | Per positioning v3 §3.4 — already named publicly |

**Discipline rules:**

- The anchor phrasings above are **locked exact wording**. Variations ("Defense / space programs," "Defense industry deployments," etc.) drift the proof and must be avoided.
- Anchors **never** pair with specific customer names in Phase 1-2 (per positioning v3 §4 + amendment v4).
- Trust anchors **never** appear as a sole credibility claim without architectural / operational backup. "We're deployed in defense" without trust posture documentation is overclaim.
- New trust anchors require positioning-amendment governance, not in-commit additions.

---

## 7. Case-study structure (locked for Phase 3+ customer stories)

When Phase 3 customer-story program activates and a named customer signs off, every case study follows this locked pattern. Specifying the structure now prevents Phase 3 from inventing per-story formats.

**The 8 locked sections:**

1. **Hero** — customer name + logo + one-line outcome (e.g., *"How [Customer] cut unplanned downtime across 40 mixed-vendor CNCs"*)
2. **Customer context** — what kind of customer, what they make, what their operational scale is (1-2 paragraphs)
3. **The problem** — operational pain in their words, with specifics (no abstraction)
4. **The Elpis approach** — which pillars / products contributed, how the deployment was scoped
5. **Outcomes** — specific metrics with customer sign-off, presented with the "compared to what" frame (e.g., *"Cut unplanned downtime by N hours per month vs the previous quarter baseline"* — not *"reduced downtime"*)
6. **Architecture for this customer** — annotated diagram subset showing the deployed pattern (uses `ArchitecturePanel.interactive` with deployment-specific annotations)
7. **What's verifiable / what's not** — explicit framing: which claims the customer signed off on, which remain anonymous (this section is the trust-building moment for the next prospect reading the story)
8. **Call to action** — *"Talk to [contact]"* — connects the prospect to the right Elpis person for a similar engagement

**Anonymization fallback** — if a customer wants the story published but not their name attached:

- Hero: descriptive role (e.g., *"How a Tier-2 automotive supplier in Pune cut downtime..."*)
- Customer context: industry + scale + geography, no name
- All other sections proceed as written, with the customer's sign-off on the anonymous version
- The story still lives in `/customers/<story-slug>` and contributes to the proof anchor pool

**Forbidden in case studies:**

- Fabricated quotes
- Outcome metrics not signed-off by the customer
- Screenshots that include customer-specific data without redaction
- Claims that contradict the customer's own public statements

---

## 8. Anti-patterns — system-wide

Things that destroy proof discipline if allowed:

| Anti-pattern | Why forbidden |
|---|---|
| *"Trusted by enterprises like yours"* (without anchor) | Unfalsifiable. Adds zero buying signal. Cluttered. |
| *"Up to N% improvement"* | The "up to" weasel word makes the metric useless. Forbidden across all surfaces. |
| *"Best in class"* / *"Industry-leading"* | Content-free. Discredits everything else on the page. |
| Customer logo + deployment story pairing in Phase 1-2 | Governance breach per positioning amendment v4 §2. |
| Real EREMOS V2 screenshot in `HeroComposite` | Phase 2 lock per homepage-spec v3 §3.1. Stylized signifier only. |
| Stock photography (handshakes, factories, operators) | Design governance §2.3 — already forbidden. Photography is real shop-floor or none. |
| Compliance framework claim without certification | Procurement detects this immediately. Use honest compliance posture instead. |
| AI-generated customer quotes / testimonials / "case studies" | Fabrication. Permanent ban. |
| Generic "typical customer sees..." | Unverifiable; substitute customer-specific data (Phase 3+) or omit. |
| Reusing trust anchor phrasing with variations | The phrasings are locked exact (§6 above). Variations drift the proof. |
| Citing competitor benchmarks as if they're Elpis's | Competitive proof must be Elpis's own architectural primitives or Elpis customers' outcomes, never re-attributed. |
| Outcome metrics on capability pages | Outcomes live on `/solutions/<solution>` (in context). Capabilities describe what the platform does, not what customers achieve. |

---

## 9. Sign-off checklist (v1 lock)

Before v1 lock:

- [ ] User reviews and approves the verifiability principle (§1)
- [ ] User reviews and approves the 5-category proof model (§2)
- [ ] User reviews and approves the primary-home map (§3) — especially the cells marked NOT PUBLISHED in Phase 2
- [ ] Customer anonymity discipline (§4) cross-references positioning v3 §4 + amendment v4 correctly
- [ ] Metric / benchmark / screenshot validation rules (§5) align with ROI calculator spec v2 §7 discipline
- [ ] Trust anchor reuse rules (§6) preserve the locked anchor phrasings exactly
- [ ] Case-study structure (§7) is the locked pattern for Phase 3+ customer stories
- [ ] Anti-patterns (§8) catch the highest-frequency proof-drift modes
- [ ] ChatGPT review pass + take-list applied → v2 if needed
- [ ] With this doc LOCKED + `buyer-taxonomy-v1` LOCKED, the Phase 2 per-page spec wave can begin per amendment v3 §4 sequencing

---

## 10. Out of scope for v1

- **Phase 3 customer-story production workflow.** Who solicits sign-off, who drafts the story, who reviews, how legal sign-off is captured — that's operational program work for Phase 3 launch, not v1 governance.
- **Phase 4 calculator outputs as proof.** Once Phase 4 calculators ship (`/calculators/oee-uplift`, `/calculators/downtime-cost`, `/calculators/brownfield-tco` per web-platform-roadmap v2 §5), they become a new proof category — interactive customer-supplied-input proof. Their governance gets folded into v2 or a separate `calculator-proof-discipline-v1.md`.
- **Industry-specific proof variations.** Phase 3 industries pages may need industry-tailored proof anchors (e.g., "deployed in NASA satellite radar antenna programs" might land in `/industries/aerospace`). v1 covers cross-industry proof; per-industry tailoring is Phase 3+ work.
- **Sales-deck-specific proof.** Pitch deck v6 (Phase E per positioning v3 §10 cascade) re-derives proof from this doc. Internal sales-deck-only proof patterns (slides that never become public) are sales-track work, not marketing-content governance.
- **Localization of proof.** Regional / language variations of proof phrasing (Japanese, German, Mandarin, Arabic) are deferred to Phase 4+ localization scope.
- **Buyer-journey-stage proof variation.** What proof a buyer needs at Awareness vs Evaluation vs Procurement is governed by the future `buyer-journey-overlay-v1.md` (reserved per buyer-taxonomy v1 §6).

---

*Proof Architecture v1, 2026-05-28. DRAFT pending user + ChatGPT review. Locks the verifiability principle, 5-category proof model, primary-home map, customer-anonymity discipline, metric/benchmark/screenshot validation rules, trust-anchor reuse rules, and case-study structure for Phase 3+. Companion to `buyer-taxonomy-v1.md`; together they complete the pre-spec foundation per Phase 2 IA memo amendment v3 §3. Once both lock, the 10 Phase 2 per-page specs begin per amendment v3 §4 sequencing.*
