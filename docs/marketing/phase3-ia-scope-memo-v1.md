<!--
File:        docs/marketing/phase3-ia-scope-memo-v1.md
Purpose:     Phase 3 IA / scope memo. The plan-trail v1 that governs the
             Phase 3 page wave (Industries + Resources + Customer proof)
             BEFORE any per-page specs or mockups are written. Mirrors the
             role phase2-ia-scope-memo-v2.md played for Phase 2.
Audience:    Internal — Claude (drafts the per-page specs that follow),
             user + ChatGPT (review this memo before per-page specs),
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
Version:     v1 — DRAFT (pre ChatGPT review). NOT locked.
Date:        2026-06-06
Status:      DRAFT — awaiting Pass 1 ChatGPT review, then v2 lock.

This memo does NOT write individual page specs. Each Phase 3 page gets its
own v1 -> ChatGPT review -> v2 lock spec, citing this memo as parent — the
same cadence Phase 2 used. The memo locks scope, page shapes, buyer map,
proof/anonymity discipline, the anti-duplication map, SEO ownership, and
spec sequencing so the spec wave runs without conceptual drift.
-->

# Phase 3 IA / Scope Memo — v1 (DRAFT)

> **⚠️ SUPERSEDED — retained as historical reference.** The governing Phase 3 plan-trail artifact is **`phase3-ia-scope-memo-v2.md` (LOCKED)**, which incorporates the Pass 1 ChatGPT review edits and records the five resolved decisions. Do not cite v1 as parent in downstream specs.

**Completes the sales surface. Adds the vertical (industries), the resource center, and the credibility surface (customer proof). Locks the ~12-page Phase 3 scope, defines three new reusable layouts, and sets the proof/anonymity discipline that is sharper in Phase 3 than anywhere before it. v1 is the plan-trail draft; it goes to ChatGPT review before the per-page spec wave begins.**

Phase 1 shipped the homepage. Phase 2 shipped the platform/capability/architecture/solution/product/security surfaces — *what Elpis is, how it works, what outcomes it creates, why it exists as a vendor*. Phase 3 answers the last buyer question that the locked corpus deliberately deferred: **"prove it — in my industry, with evidence I can take to procurement."**

The roadmap (`web-platform-roadmap-v2.md` §4) frames Phase 3 as **market credibility**. That single word sets the discipline: Phase 3 is where the temptation to fabricate (logos, metrics, named customers, certifications) is strongest, and where the proof-architecture rules matter most. This memo makes those rules the spine of the phase, not a footnote.

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

> **Scope note — `/pricing`.** The roadmap places detailed pricing on a Phase 3 `/pricing` page (cited by `/platform` §3.4 and `/architecture` §3.6 as the deferral target). It is **NOT** in the roadmap §4 Phase 3 page list above. **Recommendation:** keep `/pricing` OUT of this Phase 3 wave — pricing requires a commercial baseline (SKU model, per-pillar pricing) that does not yet exist, and publishing it prematurely violates the `/platform` §6 anti-pattern. Flag for the user; default is defer. (Open question Q4.)

---

## 2. The three new conceptual surfaces and why they overlap

Phase 2 resolved the platform/capabilities/architecture/solutions overlap. Phase 3 introduces three more surfaces that each risk re-deriving locked content:

- **`/industries/<vertical>`** — the same platform story, re-told through one industry's vocabulary, regulatory frame, and pain set. Risk: it re-derives `/solutions/<solution>` and `/capabilities/<pillar>` content with vertical wording drift.
- **`/resources/*`** — a library of downloadable collateral. Risk: it becomes a second copy of product/datasheet content rather than a *listing/access* surface.
- **`/customers`** — proof. Risk: it fabricates (named logos, invented metrics) under credibility pressure, or it re-derives industry/solution narratives instead of carrying *evidence*.

The anti-duplication map (§6) assigns each a single job and forces cross-links instead of copy.

---

## 3. New reusable layouts (land in design-system v5)

Phase 2 introduced `CapabilityDeepDive` (§14), `SolutionPanel` (§15), and `ProductDetail` (§24). Phase 3 needs three more. Each is defined here at the IA level; the shape-setter spec for each locks the section list.

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
- **Gating:** roadmap §4 calls for email-capture before download on whitepapers + select datasheets. For the **mockup**, gating is represented as a visual state (a "request access" affordance on gated cards) — the actual lead-capture/form wiring is Phase 4 engineering. Flag clearly (Open question Q3).
- **Asset reality:** several assets don't exist yet (brochures per product, whitepapers). The mockup uses honest placeholder states ("Coming soon" / "Phase 3") rather than linking to nonexistent PDFs. The datasheet (`datasheet-v3-a4.pdf`) and pitch deck DO exist.

### 3.3 `CaseStudyShell` — the customer-proof structure
Reusable structure for each story. Proposed skeleton:
1. Context — industry + plant type + scope (anonymized)
2. The problem — the operational pain (no customer name)
3. What was deployed — pillars/products + protocols (honest, shipped only)
4. The outcome — **qualitative** unless a signed, quantified story exists
5. Trust framing — the locked anchor that applies, or category-level
6. Cross-link to the relevant `/solutions/<solution>` + `/industries/<vertical>`

> **The hard constraint (see §4):** we have **no named-customer sign-off today**. So `/customers` and every `CaseStudyShell` instance in this wave is **anonymized + qualitative**. The page presents the proof we ARE allowed to publish (the locked defense/space-agency + AMC anchors, anonymized deployment patterns) — NOT named logos or invented numbers. The roadmap's "at least one customer story with explicit sign-off" exit criterion is a **business action outside this wave**; the mockup ships the anonymized structure ready to receive a signed story when one exists.

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

**This is the single biggest reason Phase 3 starts with a memo, not a page build.** The credibility surface is exactly where un-disciplined copy invents proof. Locking the discipline first protects every downstream spec.

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

Proposed build order once the memo locks:
1. **`IndustryShell` shape-setter** — draft + lock the shape on ONE vertical first. **Recommended shape-setter: Heavy Manufacturing** (broadest, least regulatory nuance, exercises the generic vertical pattern). Then parallelize the other 5.
2. **Defense + Aerospace** get extra review attention (locked-anchor handling + regulated-buyer tone).
3. **`ResourceHub` + `ResourceListing`** — simpler; can run parallel to industries.
4. **`CaseStudyShell` + `/customers`** — last; depends on the anonymized-proof discipline being locked and on confirming sign-off availability (Q1).

---

## 9. Open questions for the user (resolve before the spec wave)

1. **Named-customer sign-off — do we have ANY?** Default assumption: **no** → `/customers` ships anonymized + qualitative. Confirm, or name the story(ies) cleared for publication.
2. **Industry shape-setter** — accept **Heavy Manufacturing** as the `IndustryShell` pattern-setter, or prefer a different vertical (e.g. lead with Defense/Aerospace since the anchors live there)?
3. **Resource gating in the mockup** — represent email-gating as a visual "request access" state only (recommended), or leave all assets ungated for the mockup?
4. **`/pricing`** — confirm **defer to Phase 4** (recommended; no commercial baseline yet), or in-scope a commercial-teaser `/pricing` for Phase 3?
5. **Industry set** — accept the roadmap's 6 verticals as-is (automotive, pharma, oil & gas, defense, aerospace, heavy manufacturing), or adjust to match real deployment focus?

---

## 10. Out of scope for Phase 3 (carry to Phase 4)

- ROI calculators / interactive tools beyond the existing `/roi-calculator` mockup (roadmap §1.2 → Phase 4)
- Demo environments / embedded product UI (Phase 4)
- Documentation portal — versioned docs + full-text search (Phase 4)
- Partner portal auth + white-label workflows (Phase 4); Phase 3 only writes the AMC-partner-portal *scoping doc* per roadmap §4 exit criteria
- Lead-scoring / CRM integrations (Phase 4)
- Localization / i18n (Phase 4)
- Named-customer logos + quantified case studies (require signed sign-off; structure ships now, content lands when cleared)
- CMS selection — roadmap flags the *evaluation* begins in Phase 3; the decision itself is not a mockup deliverable

---

*Phase 3 IA / Scope Memo **v1 DRAFT** — plan-trail entry, pre ChatGPT review. Locks (pending review): the 12-page scope in 3 groups (Industries / Resources / Customer proof), three new reusable layouts (`IndustryShell`, `ResourceHub`+`ResourceListing`, `CaseStudyShell`) for design-system v5, the Phase 3 proof + anonymity discipline (§4 — the spine), buyer map, anti-duplication map, SEO intent ownership, and spec sequencing. Does not write per-page specs. Cites: web-platform-roadmap-v2 §4; phase2-ia-scope-memo-v2 (precedent); buyer-taxonomy v1; proof-architecture v1 §3/§4/§8; positioning v3 §4 + amendment v4 §3/§5; design-system v4; LOCKED Phase 2 surfaces (`/platform`, `/solutions`, `/capabilities`, `/architecture`, `/security`, product pages). Five open questions in §9 for the user before the spec wave.*
