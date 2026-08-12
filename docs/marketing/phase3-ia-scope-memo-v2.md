<!--
File:        docs/marketing/phase3-ia-scope-memo-v2.md
Purpose:     LOCKED Phase 3 IA / scope memo. The governing plan-trail
             artifact for the Phase 3 page wave (Industries + Resources +
             Customer proof) — locks scope, page shapes, proof/anonymity
             discipline, the enforceable acceptance gate, and spec
             sequencing BEFORE any per-page specs or mockups are written.
             Mirrors the role phase2-ia-scope-memo-v2.md played for Phase 2.
Audience:    Internal — Claude (drafts the per-page specs that follow),
             user + ChatGPT (reviewed this memo before per-page specs),
             engineering (the IA model Phase 3 components implement).
Companion:   web-platform-roadmap-v2.md §4 (Phase 3 scope source)
             phase2-ia-scope-memo-v2.md (precedent + parent IA model this extends)
             buyer-taxonomy-v1.md (buyer alignment)
             proof-architecture-v1.md (§3 / §4 / §8 — proof + anonymity discipline)
             industrial-intelligence-ecosystem-positioning-v3.md §4 (locked trust anchors)
             positioning-amendment-v4.md §3 + §5 (named-customer unlock + AMC anonymization)
             design-system-v4.md (component library Phase 3 extends — new shapes land here as v5)
             page-platform-spec-v1.md / page-solutions-hub-spec-v1.md / page-capabilities-hub-spec-v1.md §9
                 (LOCKED Phase 2 surfaces that Phase 3 cross-links and must NOT duplicate)
Version:     v2 — LOCKED after Pass 1 ChatGPT review.
Date:        2026-06-06
Status:      LOCKED. Governing parent for all Phase 3 per-page specs.

v1 (phase3-ia-scope-memo-v1.md) retained as historical reference.

v1 -> v2 changes (5 ChatGPT-review edits + the 5 resolved decisions; no
structural rewrite):
  - §3.3 — added the /customers vs CaseStudyShell ROUTE RULE (no public
           individual case-study routes without a signed story) +
           labeled the named-case-study item as a business/legal
           dependency, not just a note (edits 1 + 4)
  - §3.2 — added the RESOURCE ASSET INVENTORY table (edit 5)
  - §3.4 — added the /industries/ HUB SHAPE note (reuses the existing
           hub/card-grid pattern; no new component) (edit 2)
  - §9  — NEW: Phase 3 ACCEPTANCE CHECKLIST — the enforceable per-spec
           gate that turns the memo from guidance into a phase contract
           (edit 3); subsequent sections renumbered
  - §10 — open questions converted to DECISIONS (RESOLVED 2026-06-06)
           recording the five confirmed defaults

Per ChatGPT Pass 1 verdict: "Directionally strong… approve with
revisions. Frames Phase 3 as market credibility, locks the 12-page scope,
introduces the needed page-shape family, and makes proof/anonymity
discipline the spine of the phase rather than a copywriting afterthought."
v2 incorporates the 5 required edits and is LOCKED as the parent IA
document for the Phase 3 per-page spec wave.
-->

# Phase 3 IA / Scope Memo — v2 (LOCKED)

**Completes the sales surface. Adds the vertical (industries), the resource center, and the credibility surface (customer proof). Locks the ~12-page Phase 3 scope, defines the new reusable layouts, sets the proof/anonymity discipline that is sharper in Phase 3 than anywhere before it, and adds an enforceable per-spec acceptance gate. LOCKED after Pass 1 ChatGPT review; the five Phase 3 decisions are resolved in §10.**

Phase 1 shipped the homepage. Phase 2 shipped the platform/capability/architecture/solution/product/security surfaces — *what Elpis is, how it works, what outcomes it creates, why it exists as a vendor*. Phase 3 answers the last buyer question that the locked corpus deliberately deferred: **"prove it — in my industry, with evidence I can take to procurement."**

The roadmap (`web-platform-roadmap-v2.md` §4) frames Phase 3 as **market credibility**. That single word sets the discipline: Phase 3 is where the temptation to fabricate (logos, metrics, named customers, certifications) is strongest, and where the proof-architecture rules matter most. This memo makes those rules the spine of the phase (§4) and an enforceable per-spec gate (§9), not a footnote.

This memo does NOT write individual page specs. Each Phase 3 page gets its own v1 -> ChatGPT review -> v2 lock spec, citing this memo as parent — the same cadence Phase 2 used.

---

## 1. Phase 3 scope — the ~12 pages, in three groups

Per roadmap v2 §4. Grouped by conceptual surface:

### Group A — Industries (vertical SEO + buyer self-identification)
| Page | Route |
|---|---|
| Industries hub | `/industries/` |
| Automotive | `/industries/automotive` |
| Pharma | `/industries/pharma` |
| Oil & Gas | `/industries/oil-and-gas` |
| Defense | `/industries/defense` |
| Aerospace | `/industries/aerospace` |
| Heavy Manufacturing | `/industries/heavy-manufacturing` |

### Group B — Resources (the resource center)
| Page | Route |
|---|---|
| Resources hub | `/resources/` |
| Datasheets | `/resources/datasheets` |
| Brochures (mDAQ, mTracker, VAS, E-IDOS, Edge Gateway) | `/resources/brochures` |
| Whitepapers | `/resources/whitepapers` |

### Group C — Customer proof (credibility, anonymized)
| Page | Route |
|---|---|
| Customers | `/customers` |

**Total: 12 pages.** (Hub + 6 industries = 7; resources hub + 3 = 4; customers = 1.)

> **Scope note — `/pricing`.** The roadmap places detailed pricing on a Phase 3 `/pricing` page (cited by `/platform` §3.4 and `/architecture` §3.6 as the deferral target). It is **NOT** in the roadmap §4 Phase 3 page list above. **DECISION (§10 Q4): `/pricing` is DEFERRED to Phase 4** — pricing requires a commercial baseline (SKU model, per-pillar pricing) that does not yet exist, and publishing it prematurely violates the `/platform` §6 anti-pattern.

---

## 2. The three new conceptual surfaces and why they overlap

Phase 2 resolved the platform/capabilities/architecture/solutions overlap. Phase 3 introduces three more surfaces that each risk re-deriving locked content:

- **`/industries/<vertical>`** — the same platform story, re-told through one industry's vocabulary, regulatory frame, and pain set. Risk: it re-derives `/solutions/<solution>` and `/capabilities/<pillar>` content with vertical wording drift.
- **`/resources/*`** — a library of downloadable collateral. Risk: it becomes a second copy of product/datasheet content rather than a *listing/access* surface.
- **`/customers`** — proof. Risk: it fabricates (named logos, invented metrics) under credibility pressure, or it re-derives industry/solution narratives instead of carrying *evidence*.

The anti-duplication map (§6) assigns each a single job and forces cross-links instead of copy.

---

## 3. New reusable layouts (land in design-system v5)

Phase 2 introduced `CapabilityDeepDive` (§14), `SolutionPanel` (§15), and `ProductDetail` (§24). Phase 3 needs new ones. Each is defined here at the IA level; the shape-setter spec for each locks the section list.

### 3.1 `IndustryShell` — the 6 vertical pages
A vertical lens over the *existing* platform/solution/capability content. Proposed section skeleton (shape-setter spec confirms):
1. Hero — industry-framed headline + sub + CTA
2. The reality in this industry — vertical pain narrative (2–3 paras)
3. What Elpis does here — maps the 5 pillars / relevant solutions to this vertical's needs (cross-links, does NOT re-derive)
4. Relevant solutions — cards linking to the `/solutions/<solution>` pages that fit this vertical
5. Proof posture for this industry — anonymized trust cue (defense/aerospace use the locked anchors; others use category-level framing)
6. Common questions — vertical-calibrated inline FAQ (per `/capabilities` hub §9 FAQ governance)
7. Cross-lens — `/solutions` + `/architecture` + `/platform`
8. Final CTA

> **Honesty rule baked into the shape:** `IndustryShell` pulls protocol/outcome claims from the LOCKED parent specs verbatim. No vertical gets a capability the platform doesn't ship. No vertical gets a fabricated metric. Defense + Aerospace get the locked trust anchors (and ONLY those); the other four verticals get category-level proof framing, not invented deployments.

### 3.2 `ResourceHub` + `ResourceListing` — the resource center
- `ResourceHub` (`/resources/`) — directory of the resource categories (datasheets / brochures / whitepapers) + featured items.
- `ResourceListing` (`/resources/datasheets`, `/brochures`, `/whitepapers`) — a filterable card grid of downloadable assets (type / product / industry / audience facets per roadmap §4).
- **Gating:** roadmap §4 calls for email-capture before download on whitepapers + select datasheets. **DECISION (§10 Q3):** for the **mockup**, gating is represented as a visual "request access" state on gated cards only — the actual lead-capture/form wiring is Phase 4 engineering. **Amended 2026-06-06 (§10 Q3):** scope widened to **gate every download** — a contact-capture form (`gate.js`) now precedes any real asset download. Still a visual mockup (no backend); functional capture is Phase 4.
- **Asset reality:** resource cards must show real/coming-soon/request-access state **honestly** — never link to or imply a PDF that does not exist. The asset inventory below is the source of truth for which is which.

**Resource asset inventory (source of truth for card state):**

| Asset | State | Phase 3 treatment |
|---|---|---|
| Datasheet (`datasheet-v3-a4.pdf`) | Exists | Link / download |
| Pitch deck (`pitch-deck-v7.pptx`) | Exists | Link / download or featured resource |
| Product brochures (Edge Gateway, mDAQ, mTracker, VAS, E-IDOS) | Missing / partial | "Coming soon" or "request access" — no dead PDF links |
| Whitepapers | Missing | "Coming soon" / "request access" |

> Any future asset added to `/resources` must be added to this inventory with its true state. A card may only present "download" when the asset actually exists in `assets/`.

### 3.3 `CaseStudyShell` — the customer-proof structure
Reusable structure for each story. Proposed skeleton:
1. Context — industry + plant type + scope (anonymized)
2. The problem — the operational pain (no customer name)
3. What was deployed — pillars/products + protocols (honest, shipped only)
4. The outcome — **qualitative** unless a signed, quantified story exists
5. Trust framing — the locked anchor that applies, or category-level
6. Cross-link to the relevant `/solutions/<solution>` + `/industries/<vertical>`

> **ROUTE RULE (edit 1).** `CaseStudyShell` is a reusable *future* structure and may inform the anonymized proof blocks **inside `/customers`**, but Phase 3 does **NOT** create public individual `/customers/<story>` routes unless a signed, publishable customer story exists. The Phase 3 scope (§1) is `/customers` only. No detail pages are built on spec.

> **BUSINESS / LEGAL DEPENDENCY (edit 4).** Phase 3 ships the customer-proof *structure* and the *anonymized credibility surface*. **Named and/or quantified case studies remain blocked on written, publishable business/legal sign-off** and are tracked as a separate **non-engineering dependency** — not a deliverable of this wave. We have **no named-customer sign-off today** (§10 Q1), so `/customers` and every `CaseStudyShell` instance in this wave is **anonymized + qualitative**: the locked defense/space-agency + AMC anchors and anonymized deployment patterns — NOT named logos or invented numbers. The structure is ready to receive a signed story the moment one is cleared; that clearance is a business action, not a mockup task. The roadmap's "at least one customer story with explicit sign-off" exit criterion is therefore satisfied **outside** this wave.

### 3.4 `/industries/` hub — shape note (edit 2)
The Industries hub route is in scope but is **not** a new design-system component. **`/industries/` reuses the existing hub / card-grid pattern** already established by `/capabilities` and `/solutions` (a hero + a card grid of the 6 verticals + cross-lens + CTA). No new reusable shape is required for the hub itself; only the 6 vertical pages use `IndustryShell`. The hub route is thereby shape-owned.

---

## 4. Proof + anonymity discipline (the spine of Phase 3)

This is the section every Phase 3 spec must cite. Sourced from `proof-architecture-v1` §3/§4/§8 + positioning v3 §4 + positioning-amendment-v4 §3/§5.

| Rule | Phase 3 application |
|---|---|
| **No fabricated metrics** | No %, $, uptime, OEE-gain, or payback numbers on industries/customers pages unless they come from a signed customer story (none exist yet). Outcomes stay qualitative ("cut", "reduce", "trend ahead of failure") — never "eliminate"/"zero"/invented figures. |
| **No customer names / logos** | Zero named customers in this wave. Defense + space-agency customer names stay off-record **permanently** per the locked external-claim policy (even after Phase 3 sign-off). AMC partner names wait for the Phase 4 partner portal. |
| **Locked trust anchors — verbatim only** | "Operating across India and the Middle East." · "Deployed in defense and space-agency programs." · "Maintenance and AMC providers across India and the Middle East." No paraphrase. Defense + Aerospace industry pages and `/customers` are the natural homes for these. |
| **No competitor names** | Public surfaces never name competitors (sales-objection guide territory only, per proof-arch §8). |
| **No certification claims** | No formal cert logos/claims (ISO/IEC/IP ratings as *compatibility*, case-by-case, per hardware-ecosystem-map §264). Procurement-grade compliance proof waits for real certification. |
| **Protocol honesty (P-G)** | Industries pages inherit the shipped-protocol list verbatim: FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 shipped; MT-LINKi REST roadmap; EdgeConnect Linux roadmap; E-IDOS standalone (EREMOS streaming roadmap); mDAQ runs VAS only. |
| **Authoritative-explanation invariant** (phase2 memo §4.0) | Industries/customers pages do NOT become the authoritative explanation of any pillar/solution/architecture — they cross-link to the LOCKED owner. |

**This is the single biggest reason Phase 3 starts with a memo, not a page build.** The credibility surface is exactly where un-disciplined copy invents proof. Locking the discipline first protects every downstream spec; §9 makes it enforceable.

---

## 5. Buyer alignment (per buyer-taxonomy v1)

| Surface | Primary buyer | What they want | CTA preference |
|---|---|---|---|
| `/industries/<vertical>` | Vertical operations leader / plant manager (§2.2) + the vertical's OT architect (§2.3) | "Do you understand MY industry's constraints?" — self-identification + relevant proof | "Book a scoping call" / "Request an architecture review" (vertical-appropriate) |
| `/industries/defense` + `/aerospace` | Program / procurement lead in regulated verticals | Credibility + anonymized proof + compliance posture | "Request an architecture review" |
| `/resources/*` | All buyers, late-stage | Self-serve collateral to forward internally | Download / request access |
| `/customers` | Procurement / compliance reviewer (§2.7) + skeptical exec | Evidence that survives scrutiny | "Talk to us about scoping" |

---

## 6. Anti-duplication map

| Content | Authoritative owner (LOCKED) | Phase 3 pages may… |
|---|---|---|
| The 5 pillars | `/capabilities/<pillar>` ×5 | reference + cross-link; never re-derive |
| Outcome narratives | `/solutions/<solution>` ×7 | link as "relevant solutions"; never re-tell in full |
| The stack / deployment shapes | `/architecture` | link; never re-draw the diagram |
| Vendor worldview + commercial model | `/platform` | link; never restate the principles |
| Trust posture detail | `/security` | link; industries/customers carry a trust *cue* only |
| Product detail | `/edgeconnect` … `/e-idos` | resources/brochures link to product pages; never duplicate spec tables |
| Proof / evidence in context | **`/customers`** (this wave) | industries pages carry a proof *cue* + link to `/customers` |

Industries pages are a **vertical lens**, resources are a **library**, customers are **evidence**. None is an authoritative explanation of platform mechanics.

---

## 7. SEO intent-cluster ownership

- `/industries/<vertical>` — owns vertical-qualified queries ("CNC monitoring for automotive", "oil & gas condition monitoring platform"). This is the primary new SEO surface of Phase 3.
- `/resources/*` — owns asset-intent queries ("Elpis datasheet", "mDAQ brochure", "industrial OEE whitepaper").
- `/customers` — owns credibility queries ("Elpis customers", "industrial intelligence case study"); thin until signed stories land.
- Industries pages must NOT compete with `/solutions` for outcome queries — they target *vertical + outcome*, solutions target *outcome*.

---

## 8. Recommended sequencing + cadence

Same proven cadence as Phase 2: **scope memo (this doc) → ChatGPT review → lock → page-shape shape-setter specs → per-page specs (v1 → review → lock) → mockups (pattern-setter by hand, siblings in parallel) → wire → PR.**

Build order once the memo locks:
1. **`IndustryShell` shape-setter** — draft + lock the shape on ONE vertical first. **Shape-setter: Heavy Manufacturing** (§10 Q2 — broadest, least regulatory nuance, exercises the generic vertical pattern). Then parallelize the other 5.
2. **Defense + Aerospace** get extra review attention (locked-anchor handling + regulated-buyer tone) AFTER the base shape is locked.
3. **`ResourceHub` + `ResourceListing`** — simpler; can run parallel to industries.
4. **`CaseStudyShell` + `/customers`** — last; depends on the anonymized-proof discipline being locked (§4) and the route rule (§3.3).

---

## 9. Phase 3 acceptance checklist (enforceable per-spec gate)

**Every Phase 3 per-page spec AND every mockup must pass these gates before lock/merge.** This turns the memo from guidance into a phase contract.

- [ ] Cites this memo (`phase3-ia-scope-memo-v2`) as parent
- [ ] Cites §4 proof / anonymity rules explicitly
- [ ] No fabricated metrics (%, $, uptime, OEE-gain, payback) — outcomes qualitative
- [ ] No named customers / logos (anywhere, any vertical, incl. defense/space-agency)
- [ ] No certification claims (IP/ISO/IEC as compatibility only, case-by-case)
- [ ] No competitor names
- [ ] Shipped vs roadmap protocol status preserved verbatim (P-G): FOCAS2/MTConnect/Brother HTTP/Modbus TCP/OPC UA Client/S7 shipped; MT-LINKi REST + EdgeConnect Linux + E-IDOS→EREMOS streaming = roadmap; mDAQ runs VAS only
- [ ] No `/pricing` page or pricing detail (deferred to Phase 4)
- [ ] No individual `/customers/<story>` routes without signed, publishable approval
- [ ] Resource cards show real / coming-soon / request-access state honestly per the §3.2 asset inventory (no dead or implied PDFs)
- [ ] Does not re-derive an authoritative explanation owned by a LOCKED Phase 2 surface (§6) — cross-links instead
- [ ] Locked trust anchors (if used) reproduced verbatim, no paraphrase

---

## 10. Decisions (resolved 2026-06-06)

The five open questions from v1 §9, confirmed by the user (matching the recommended defaults):

| # | Question | Decision |
|---|---|---|
| 1 | Named-customer story cleared for publication? | **No.** `/customers` ships anonymized + qualitative unless/until written, publishable approval exists (tracked as a business/legal dependency — §3.3). |
| 2 | Industry shape-setter? | **Heavy Manufacturing** is the `IndustryShell` pattern-setter. Defense / Aerospace reviewed *after* the base shape is locked. |
| 3 | Resource gating in the mockup? | **Visual "request access" state only.** No functional gating / lead-capture wiring in Phase 3. **Amended 2026-06-06:** user elected to **gate EVERY download** — the mockup now shows a contact-capture form (`gate.js`) before any asset download (datasheet, overview deck, datasheet/brochure CTAs). Still **visual-only** (no data stored/sent); functional capture / CRM remains a Phase 4 deliverable. |
| 4 | `/pricing` in Phase 3? | **Deferred to Phase 4.** No commercial baseline yet. |
| 5 | Keep the six roadmap verticals? | **Yes** — automotive, pharma, oil & gas, defense, aerospace, heavy manufacturing, as-is. |

---

## 11. Out of scope for Phase 3 (carry to Phase 4)

- ROI calculators / interactive tools beyond the existing `/roi-calculator` mockup (roadmap §1.2 → Phase 4)
- Demo environments / embedded product UI (Phase 4)
- Documentation portal — versioned docs + full-text search (Phase 4)
- Partner portal auth + white-label workflows (Phase 4); Phase 3 only writes the AMC-partner-portal *scoping doc* per roadmap §4 exit criteria
- Lead-scoring / CRM integrations + functional email-capture (Phase 4)
- Localization / i18n (Phase 4)
- `/pricing` page (Phase 4 — §10 Q4)
- **Named-customer logos + quantified case studies** — blocked on signed, publishable business/legal sign-off (§3.3 business dependency); structure ships now, content lands when cleared
- Individual `/customers/<story>` routes (none until a signed story exists — §3.3 route rule)
- CMS selection — roadmap flags the *evaluation* begins in Phase 3; the decision itself is not a mockup deliverable

---

*Phase 3 IA / Scope Memo **v2 LOCKED 2026-06-06** after Pass 1 ChatGPT review ("directionally strong… approve with revisions… makes proof/anonymity discipline the spine of the phase"). Governing parent for all Phase 3 per-page specs. Locks: the 12-page scope in 3 groups (Industries / Resources / Customer proof); the new reusable layouts (`IndustryShell`, `ResourceHub`+`ResourceListing`, `CaseStudyShell`) for design-system v5, plus the `/industries/` hub reusing the existing card-grid pattern; the Phase 3 proof + anonymity discipline (§4 — the spine); the enforceable per-spec acceptance gate (§9); buyer map; anti-duplication map; SEO intent ownership; spec sequencing (Heavy Manufacturing shape-setter first). Five decisions resolved (§10). v1 -> v2 applied the 5 ChatGPT-review edits: /customers-vs-CaseStudyShell route rule + named-case-study business dependency; resource asset inventory table; /industries/ hub-shape note; the §9 acceptance checklist; open-questions → resolved-decisions. Does not write per-page specs. Cites: web-platform-roadmap-v2 §4; phase2-ia-scope-memo-v2 (precedent); buyer-taxonomy v1; proof-architecture v1 §3/§4/§8; positioning v3 §4 + amendment v4 §3/§5; design-system v4; LOCKED Phase 2 surfaces.*
