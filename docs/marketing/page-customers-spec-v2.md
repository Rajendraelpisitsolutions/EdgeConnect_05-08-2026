<!--
File:        docs/marketing/page-customers-spec-v2.md
Purpose:     Page spec for /customers — the credibility surface. Defines the
             CaseStudyShell structure AND the anonymized /customers page for
             Phase 3. The single most proof-discipline-sensitive page in the
             corpus.
Audience:    Internal — Claude (mockup build), copywriters (verbatim lift),
             user + ChatGPT (reviewers — EXTRA scrutiny here), engineering.
Format:      Per §9 canonical template locked in page-capabilities-hub-spec-v1.md.
Companion:   phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.3 CaseStudyShell +
                route rule + business dependency; §4 proof/anonymity discipline;
                §5 buyer map; §9 acceptance gate)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked anchors)
             positioning-amendment-v4.md §3 + §5 (named-customer policy + AMC anonymization)
             proof-architecture-v1.md §3/§4/§8 (proof + anonymity — the spine)
             page-industry-{defense,aerospace}-spec-v2.md (reserved-anchor handling precedent)
             design-system-v4.md (extends — CaseStudyShell lands as v5)
Version:     v2 — LOCKED after Pass 1 ChatGPT review (extra anchor + anonymity scrutiny).
Date:        2026-06-06
Status:      LOCKED. CaseStudyShell shape-setter (anonymized) + /customers.

v1 (page-customers-spec-v1.md) retained as historical reference.

v1 -> v2 changes (ChatGPT Pass-1 anchor-verbatim cleanups):
  - §1.4 meta description rewritten so it no longer paraphrases/combines
    the locked anchors (the v1 description spliced the defense anchor with a
    paraphrased AMC anchor). v2 uses a neutral description carrying no
    altered anchor fragment.
  - §3.3 Pattern C: removed the parenthetical "(Anchor: AMC providers across
    India and the Middle East.)" — a paraphrase of the locked form. The
    locked anchor lives verbatim in §3.2; Pattern C no longer cites it.
  - §3.2 Card 1: tightened so the defense/space-agency anchor remains the
    ENTIRE proof — dropped the added "high-consequence / reliability
    non-negotiable" characterization; framing is now confidentiality-only.

Phase 3 Group C (Customer proof). Drafted alongside page-resources-spec-v1.md.

THE HARD CONSTRAINT (memo §3.3 + §4): there is NO named-customer sign-off
today. So /customers ships ANONYMIZED + QUALITATIVE. The proof Elpis is
allowed to publish = the locked trust anchors (presented as proof CATEGORIES)
+ anonymized, anchor-grounded representative deployment patterns. NO named
customers/logos, NO fabricated metrics, NO invented deployment stories. The
defense/space-agency anchor appears VERBATIM and is NOT elaborated with
capability detail (per the aerospace v2 Pass-1 review precedent). Named +
quantified case studies remain blocked on written business/legal sign-off — a
non-engineering dependency, tracked outside this wave. No individual
/customers/<story> routes are created without a signed story (memo §3.3 route rule).

§9 acceptance gate pre-checked in §7. Word target ~700-1,000 (credibility
surface; concise — proof, not prose).
-->

# `/customers` — Page Spec v2 (LOCKED) · CaseStudyShell shape-setter (anonymized)

**The credibility surface. A procurement / compliance reviewer or a skeptical exec lands here asking "prove it." What we show is the proof we can actually stand behind today: the locked trust anchors as verifiable proof categories, plus anonymized, anchor-grounded representative deployment patterns. No logos. No invented numbers. No named customers. This spec also defines the reusable `CaseStudyShell` structure for the day a signed, publishable story exists.**

`/customers` is **not** a testimonial wall, **not** a logo grid, **not** a metrics showcase. Under the credibility pressure that makes this page exist, it is exactly where un-disciplined copy fabricates proof — so the proof-architecture discipline (memo §4) is the spine of the page. Per the route rule (memo §3.3), Phase 3 ships `/customers` only; no individual `/customers/<story>` detail routes are created until a signed story exists.

Target length: **~700-1,000 words** (proof, not prose).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The honest credibility surface. Reader leaves with *"Elpis is deployed in real, high-consequence environments — defense and space-agency programs, AMC channels across India and the Middle East — and the kinds of floors they work look like mine. The specifics are confidential, and they're upfront about why."*

**IS NOT:**
- A named-customer / logo page (zero named customers this wave; defense/space-agency names off-record permanently — memo §4)
- A metrics / outcomes showcase (no fabricated %, $, uptime, payback — memo §4)
- A testimonial page (no quotes attributed to named or fabricated people)
- A set of individual case-study routes (memo §3.3 route rule — `/customers` only until a signed story exists)
- An authoritative explanation of any pillar/solution (cross-links the LOCKED owners — memo §6)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** Procurement / compliance reviewer (§2.7) + skeptical exec — wants evidence that survives scrutiny, and respects honesty about what can and can't be disclosed. CTA preference: *"Talk to us about scoping"* (a reviewer validating a vendor, not a buyer chasing a demo).

- Vocabulary that lands: deployed, anonymized, representative, category, confidential, verifiable, reference (as in "we can arrange a reference under NDA").
- Vocabulary that backfires: "trusted by thousands", "industry-leading", "proven ROI", any unbacked superlative or number.

### 1.4 Page metadata

| Field | Value |
|---|---|
| **Meta title** (≤60) | *Customers — where Elpis is deployed · Elpis* |
| **Meta description** (140-160) | *The credibility surface for Elpis — the deployment categories we can publish and anonymized patterns of the floors we work, with the specifics held in confidence.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/customers` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. NO `Review`/`AggregateRating`/`Organization`-customer schema (no named/quantified proof to mark up). No trust-anchor schema at this lock. |

---

## 2. Page structure — sections at a glance

| # | Section | Mode | Component | Words |
|---|---|---|---|---|
| **1** | Hero (eyebrow + headline + sub) | `dark-deep` | `SectionShell` | ~90 |
| **2** | The proof we can publish (3 anchor categories) | `light` | Anchor-category cards | ~180 |
| **3** | Representative deployment patterns (3 anonymized `CaseStudyShell`) | `light-tinted` | CaseStudyShell cards | ~320 |
| **4** | Why these are anonymized (honesty statement + reference offer) | `light` | Narrative + note | ~150 |
| **5** | Cross-lens (Solutions · Industries · Platform) | `light-tinted` | §17 cross-lens | ~40 |
| **6** | Final CTA | `dark-deep` | `CTASection` | ~70 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: CUSTOMERS
> HEADLINE: Deployed where the cost of being wrong is high.
> SUBHEAD: Elpis runs in real production and reliability environments — including programs where the customer's name never leaves the room. Here's the proof we can publish, framed honestly: the categories we're deployed in, and the kinds of floors we work — with the specifics held in confidence.
> PRIMARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Scoping%20conversation`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

**Anti-patterns:** No "trusted by N customers" count. No logo strip. No superlatives. No metric in the headline.

### 3.2 Section 2 — The proof we can publish

> EYEBROW: THE PROOF WE CAN PUBLISH
> SECTION TITLE: Three things we can say without a single disclosure.

Three cards, each carrying a LOCKED anchor VERBATIM as a proof category. The anchors are reproduced exactly; **none is elaborated with capability detail** (per the aerospace v2 Pass-1 review — the anchor is the proof, not a springboard for specifics).

> | Card | Anchor (verbatim) | Honest framing (no specifics) |
> |---|---|---|
> | 1 | **Deployed in defense and space-agency programs.** | The customers, programs, and agencies stay off the record — permanently. That confidentiality is itself the point: we hold it the way these environments require. |
> | 2 | **Maintenance and AMC providers across India and the Middle East.** | An active service-delivery channel: AMC providers run condition monitoring and predictive maintenance on their customers' floors using Elpis hardware and EREMOS V2 incident workflows. |
> | 3 | **Operating across India and the Middle East.** | A real, current deployment footprint across both regions — plants, multi-site operators, and service partners — without overclaiming "global." |

> GOVERNANCE NOTE (not displayed): All three anchors VERBATIM, each its own card, NONE elaborated. Card-2/3 framing draws only on what positioning v3 §4 + amendment v4 §5 already establish (AMC channel reality; regional footprint). Card 1 carries the anchor + a confidentiality-as-proof framing — NO program/agency/branch/capability detail. Per memo §4 + §9 gate + the aerospace v2 anchor-discipline precedent.

### 3.3 Section 3 — Representative deployment patterns

> EYEBROW: REPRESENTATIVE DEPLOYMENT PATTERNS
> SECTION TITLE: The kinds of floors we work. Anonymized, on purpose.
> LEAD (size.base): These are representative patterns, not named accounts — the shape of real engagements with the identifying detail removed. Each maps to a solution and an industry you can read in full.

Three anonymized `CaseStudyShell` instances. **Qualitative only** — no metrics, no names, no invented specifics. Each is grounded in a LOCKED solution/industry story and the deployment shapes already described in that corpus.

> **Pattern A — A mixed-vendor CNC machining floor**
> CONTEXT: A machining operation running FANUC, Brother, and other controllers side by side.
> CHALLENGE: OEE and alarm history stitched by hand across vendor-specific dashboards.
> DEPLOYED: EdgeConnect over native protocols → canonical CNC vocabulary → EREMOS V2 for OEE Segments and persistent alarms.
> OUTCOME (qualitative): One operational view across the floor; alarm patterns visible instead of isolated HMI incidents. → Read the solution: `/solutions/cnc-machining` · industry: `/industries/heavy-manufacturing`

> **Pattern B — A multi-plant operator standardizing OEE**
> CONTEXT: An operator running more than one plant on mixed controllers, wanting one OEE definition.
> CHALLENGE: Each plant reported differently; no comparable fleet view.
> DEPLOYED: A per-plant EdgeConnect runtime per site; EREMOS V2 aggregating across plants with consistent canonical vocabulary and one OEE definition.
> OUTCOME (qualitative): A fleet view that preserves per-site identity and offline operability. → Read the solution: `/solutions/multi-site-operations`

> **Pattern C — An AMC-delivered condition-monitoring engagement**
> CONTEXT: A maintenance / AMC provider delivering reliability services on a customer's rotating equipment and hydraulic systems.
> CHALLENGE: Calendar-based maintenance, no early-warning signal.
> DEPLOYED: VAS for vibration and E-IDOS for oil health, with EREMOS V2 incident workflows; customer-controlled routing of which signals reach the AMC.
> OUTCOME (qualitative): Early-warning triggers on real condition signatures — a better trigger than a calendar, not a guarantee against every failure. → Read the solution: `/solutions/predictive-maintenance`

> GOVERNANCE NOTE (not displayed): Three patterns ONLY, each anchor/corpus-grounded (CNC + multi-site map to locked solutions; AMC pattern maps to the AMC anchor + predictive-maintenance). DELIBERATELY no defense/space-agency "pattern" — that anchor stays in §3.2 verbatim and unelaborated; turning it into a deployment narrative would breach the off-record-permanently + no-elaboration rule. All outcomes qualitative; no metrics; "representative pattern" labeling is explicit so nothing reads as a named account.

### 3.4 Section 4 — Why these are anonymized

> EYEBROW: WHY ANONYMIZED
> SECTION TITLE: Confidence is part of the product.
> BODY: We don't publish customer names, logos, or quantified results — and in the case of defense and space-agency work, we never will. That isn't a gap in our proof; it's a feature of how we operate. The customers who trust Elpis with high-consequence reliability data trust us partly because we don't turn them into a marketing slide. Named, quantified case studies will appear here only with a customer's explicit, written sign-off — and only where disclosure is permitted.
> REFERENCE-OFFER NOTE (size.sm): For a serious evaluation, we can arrange a customer reference under NDA where the customer has agreed to it. Ask during scoping.

> GOVERNANCE NOTE (not displayed): Frames anonymity as principle, not absence of proof. The "named/quantified stories with written sign-off" line is the memo §3.3 business-dependency callout, surfaced to the reader honestly. The NDA-reference offer is a true, deliverable option (a 1:1 reference, not a published claim) — keep it conditional ("where the customer has agreed").

### 3.5 Section 5 — Cross-lens

> | Card | Eyebrow | Description | Destination |
> |---|---|---|---|
> | 1 | SOLUTIONS | The outcomes these deployments deliver | `/solutions` |
> | 2 | INDUSTRIES | The same proof, by sector | `/industries` |
> | 3 | PLATFORM | Why Elpis exists and how we engage | `/platform` |

### 3.6 Section 6 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Evaluate us on the things that matter.
> SUBHEAD: Bring the floor you need to prove out and the questions your procurement team will ask. We run proofs of value on real protocols and real signals — and where a reference helps and the customer agrees, we'll arrange one.
> PRIMARY CTA: Talk to us about scoping → `mailto:contact@elpisitsolutions.com?subject=Scoping%20conversation`
> SECONDARY CTA: Request an architecture review → `mailto:contact@elpisitsolutions.com?subject=Architecture%20review`

---

## 4. Components used

All design-system v4 LOCKED + the new `CaseStudyShell` (§7). No net-new primitives.

| Component | Used in |
|---|---|
| `SectionShell` (modes) | every section |
| Anchor-category cards | §3.2 |
| `CaseStudyShell` (anonymized variant) | §3.3 |
| `CapabilityCard` (cross-lens) | §3.5 |
| `CTASection` | §3.6 |

---

## 5. Verbatim copy summary

All copy §3.1–§3.6. **~760 words** (within ~700-1,000 target).

---

## 6. Anti-patterns specific to this page (the sharpest in the corpus)

| Don't | Why |
|---|---|
| Name any customer, program, agency, branch, or mission | memo §4 — zero named customers; defense/space-agency off-record permanently |
| Show a logo grid or "trusted by" count | No named/aggregate proof exists; fabricating one breaches §4 |
| Quote any metric (%, $, uptime, payback, OEE gain) | memo §4 — no fabricated metrics; outcomes qualitative until a signed story exists |
| Elaborate the defense/space-agency anchor with capability/program detail | aerospace v2 Pass-1 precedent — the anchor is the proof, verbatim + unelaborated |
| Turn the defense/space-agency anchor into a "deployment pattern" narrative | §3.3 governance note — that anchor stays in §3.2 only; no narrative |
| Invent a customer quote or testimonial | No attributable source exists; fabrication breaches §4 |
| Create individual `/customers/<story>` routes | memo §3.3 route rule — none without a signed story |
| Imply the anonymized patterns are specific named accounts | §3.3 labels them "representative patterns" explicitly |
| Promise a reference unconditionally | Conditional only — "where the customer has agreed", under NDA |

---

## 7. `CaseStudyShell` shape definition (for design-system v5)

**Anonymized variant (this wave):** Context → Challenge → Deployed (pillars/products, honest) → Outcome (qualitative) → cross-link to the mapped solution + industry. No name, no metric, no logo. Labeled "representative pattern".

**Signed variant (future, when sign-off exists):** the same structure may carry a named customer, quantified outcomes, and a quote — ONLY with written, publishable sign-off, and ONLY where disclosure is permitted (never for defense/space-agency names). A signed story unlocks an individual `/customers/<story>` route (memo §3.3 route rule); until then, `/customers` is the only route.

**Invariant:** every CaseStudyShell instance traces to a LOCKED solution/industry story; it cross-links rather than re-derives (memo §6); it passes the §9 gate.

---

## 8. Phase 3 acceptance gate (memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent
- [x] Cites §4 proof/anonymity rules (the spine — §3.2 + §3.3 + §3.4 + §6)
- [x] No fabricated metrics (all outcomes qualitative)
- [x] No named customers / logos / counts
- [x] No certification claims
- [x] No competitor names
- [x] Protocol status verbatim where referenced (Pattern A native protocols / canonical vocabulary; mDAQ runs VAS; no roadmap item claimed as shipped)
- [x] No `/pricing` / pricing detail
- [x] No individual `/customers/<story>` routes (route rule honored; §7 defines when one unlocks)
- [x] Resource-asset state N/A
- [x] Does not re-derive a LOCKED owner (§6) — every pattern cross-links its solution/industry
- [x] Locked anchors VERBATIM and UNELABORATED (§3.2); defense/space-agency anchor not turned into a narrative (§3.3)

---

## 9. Out of scope

- Named / quantified case studies (blocked on written, publishable business/legal sign-off — memo §3.3 business dependency; defense/space-agency names off-record permanently)
- Individual `/customers/<story>` detail routes (none until a signed story exists)
- Logo walls, testimonial carousels, star ratings, "trusted by N" counters
- A published, named customer-reference list (NDA references are arranged 1:1, not published)
- Quantified ROI proof (Phase 3 customer-story registry, once signed stories exist)

---

*`/customers` Page Spec **v2 LOCKED 2026-06-06** (ChatGPT Pass 1 applied — meta description de-paraphrased; Pattern C anchor parenthetical removed; Card 1 tightened to confidentiality-only so the anchor is the entire proof) — the credibility surface, ANONYMIZED + QUALITATIVE per the hard constraint (no named-customer sign-off; memo §3.3 + §4). Publishes the three locked trust anchors VERBATIM as proof categories (§3.2, none elaborated — aerospace v2 anchor-discipline precedent) + three anonymized, anchor/corpus-grounded representative deployment patterns (§3.3 — CNC, multi-site, AMC; deliberately NO defense/space-agency narrative) + an honesty statement framing anonymity as principle with a conditional NDA-reference offer (§3.4). Defines `CaseStudyShell` (§7) in anonymized + future-signed variants; no individual `/customers/<story>` routes until a signed story exists (memo §3.3 route rule). NO named customers/logos/counts, NO fabricated metrics, NO testimonials, NO competitor names. §8 pre-checks the memo §9 acceptance gate (all green). ~760 words. Cites: phase3-ia-scope-memo-v2 (parent §3.3/§4/§9); positioning v3 §4 + amendment v4 §3/§5 (anchors + named-customer policy); proof-architecture v1 §3/§4/§8; page-industry-{defense,aerospace}-spec-v2 (reserved-anchor precedent); design-system v4 (→ v5 CaseStudyShell).*
