<!-- ============================================================
     ⛔ SUPERSEDED 2026-06-05 — promoted into design-system-v4.md as §24.
     This addendum's provisional "§18" numbering collided with design-system
     v3's §18–§23 globals; ProductDetail was renumbered §18→§24 and folded into
     Design System v4 (additive over v3). The canonical ProductDetail spec now
     lives at docs/marketing/design-system-v4.md (§24 software / §24.A spec-table
     / §24.B hardware / §24.3 cross-lens). All 7 product specs reference §24.
     This file is retained as history only — do not edit or cite it; cite
     design-system-v4.md §24.x instead.
     ============================================================ -->
<!--
File:        docs/marketing/design-system-productdetail-addendum-v1.md
Purpose:     Defines the NEW ProductDetail page shape for Phase E software
             product pages (EdgeConnect, EREMOS V2). design-system v3 §15/§25
             explicitly deferred "full product detail" to Phase E and ships no
             product-detail composition; this addendum supplies it. Proposed as
             a design-system §18 (+ a §18.A spec-table content pattern), to fold
             into design-system v4 at governance.
Audience:    User + ChatGPT (reviewers), Angular engineering, Phase E product-
             page spec authors (EdgeConnect is the shape-setter; the other
             software product page — EREMOS V2 — inherits this shape).
Format:      Design-system addendum. Pairs with the static HTML mockup at
             docs/marketing/web/edgeconnect.html for visual sign-off (per the
             "any UI gets a static HTML mockup first" discipline).
Companion:   design-system-v3.md §14 (CapabilityDeepDive) + §15 (SolutionPanel)
                + §16 (trust cue) + §17 (cross-lens) + §5.A (ArchitecturePanel
                .interactive) — ProductDetail reuses these; introduces NO new
                visual primitive.
             page-capabilities-hub-spec-v1.md §9 (the per-page-spec template the
                product-page specs will follow)
             buyer-taxonomy-v1.md §2.3 (OT Architect — product-page primary
                buyer) + §2.5 (Plant engineer — secondary)
             hardware-ecosystem-map-v3.md (the 7 products; software vs hardware
                split; the per-product certification open question that gates
                HARDWARE product pages)
             elpis-industrial-intelligence-platform-v5.md (datasheet — source of
                truth for the EdgeConnect + EREMOS V2 product facts)
             2026-06-04-phase-e-solution-migration-plan.md (precedents P-A..P-H,
                inherited where applicable)
Version:     v1 — ChatGPT review APPLIED (verdict "Approve with changes";
             ProductDetail confirmed as a genuinely distinct, necessary 4th
             shape). 5 addendum refinements applied: (A1) Versioning + support
             lifecycle subsection added to §5; (A2) editions/licensing scoped to
             structure+mechanics, NOT pricing tables (anti-pattern added; labels
             illustrative until packaging approved); (A3) hardware variant stays
             deferred — confirmed; (A4) cross-lens lead solution card locked to
             /solutions/edge-connectivity; (A5) §18.A spec-table variants + a
             recommended Direction column added. Companion mockup got 5 fixes
             (hex→token, bundled-Inter note, static architecture SVG, delivery-
             mode precision, editions-illustrative caveat).
Date:        2026-06-04
Status:      §18 (software) LOCKED 2026-06-04 (user go-ahead after ChatGPT
             review) — GOVERNING ProductDetail shape until folded into a
             design-system v4 consolidation. /edgeconnect = shape-setter;
             /eremos-v2 inherits §18. §18.B (hardware variant) LOCKED 2026-06-04
             (validated by the /edge-gateway shape-setter + two ChatGPT passes).
             §18.B locks the cert/IP + Phase-E resolutions (see §18.B.0);
             mDAQ / mTracker / VAS / E-IDOS inherit it.

WHY A NEW SHAPE (not a reuse). design-system v3 ships three content-page shapes:
CapabilityDeepDive (§14, /capabilities/<pillar>), SolutionPanel (§15,
/solutions/<solution>), and the /architecture composition (§5.A). None fits a
PRODUCT page: the product page is the DEEPEST factual surface — full protocol /
spec matrices, semantic modes, security profiles, editions + licensing, system
requirements — that the capability and solution pages intentionally DEFER to it
(see /solutions/* §8 "out of scope → Phase E /edgeconnect" + connectivity-edge
§8). ProductDetail is that deferral's destination.

SCOPE OF THIS ADDENDUM: the SOFTWARE product variant only (EdgeConnect, EREMOS
V2). The HARDWARE product variant (Edge Gateway, mDAQ, mTracker, VAS, E-IDOS)
adds physical-spec sections (dimensions, power, enclosure, I/O, environmental,
certifications) and is DEFERRED pending the per-product certification open
question (hardware-ecosystem-map v3 §264 — CE/UL/FCC/IEC/IP unresolved) and the
Phase 3-vs-E scoping ambiguity (design-system v3 line 278). When those resolve,
a §18.B hardware variant extends this shape.

NO NEW VISUAL PRIMITIVE. ProductDetail is a page COMPOSITION over existing
design-system v3 components (SectionShell, Button, CapabilityCard, Architecture
Panel.interactive, trust-cue §16, cross-lens §17, CTASection, inline FAQ) plus
ONE new content pattern — the §18.A spec-table — which is a styled `<table>`, not
a new primitive (same governance class as the §16/§17 content patterns). This
keeps the addition additive and design-governance-clean; it does not reopen the
v3 component contract.
-->

# design-system addendum — §18 `ProductDetail` page shape (software variant)

**The reusable page composition for Phase E software product pages (`/edgeconnect`, `/eremos-v2`). The deepest factual product surface — where the protocol/spec depth, semantic modes, security profiles, editions, and system requirements that the capability and solution pages defer actually live.**

---

## 18.0 Where used + buyer

- **Where used:** `/edgeconnect` (shape-setter) and `/eremos-v2` (software products). Hardware products defer to a future §18.B variant.
- **Primary buyer:** OT Architect / SCADA engineer (buyer-taxonomy §2.3) — the technical evaluator who reads documentation carefully and tests claims. **Secondary:** Plant engineer (§2.5 — deployment/sizing/BOM).
- **CTA framing (P-H inherited):** the primary CTA is the §2.3-endorsed **"Request an architecture review"** / **"Talk to an engineer"** — NOT "Book a scoping call" (a §2.3 backfire). Product pages are never §2.2-Plant-manager-primary, so they re-derive their CTA from §2.3 per precedent P-H.

## 18.1 How ProductDetail differs from CapabilityDeepDive and SolutionPanel

| | CapabilityDeepDive (§14) | SolutionPanel (§15) | **ProductDetail (§18)** |
|---|---|---|---|
| **Centered on** | A capability pillar (what it does, why it matters) | A buyer outcome (the pain → how Elpis solves it) | **A product (what it is, exactly — full spec depth)** |
| **Primary buyer** | OT Architect / Plant engineer | The solution's buyer (Plant mgr, OEM, …) | **OT Architect / SCADA engineer** |
| **Depth ceiling** | Capability-level; defers product spec | Solution-level vocabulary; defers product spec | **Full spec: protocol matrix + semantic modes + security profiles + editions + system requirements** |
| **Has a "Customer Pain" narrative** | No | Yes (§15 §2) | **No** — opens with "What it is", not empathy |
| **Has a full spec/protocol matrix** | No (cross-links here) | No (cross-links here) | **Yes — this is its defining section** |
| **Has editions + licensing detail** | No | No | **Yes** |

The discipline: **CapabilityDeepDive and SolutionPanel cross-link DOWN to ProductDetail for spec depth; ProductDetail cross-links UP to the capability pillar and ACROSS to the solutions that use the product.** No duplication — the product page owns the spec; the others own the capability/outcome framing.

## 18.2 Section structure (software variant) — 11 sections

| # | Section | Mode | Primary component(s) | Notes |
|---|---|---|---|---|
| 1 | **Hero** — product name + one-line definition + CTAs + **right-side hero visual** | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + **`hero__composite` visual** | Eyebrow `PRODUCT · <PILLAR>`. Headline = product name + value, not a feature list. **Two-column (`hero__inner`): copy left, a product-relevant `HeroComposite`-style visual right** — matches the homepage hero (no blank right half). The visual is a lightweight on-brand SVG specific to the product (e.g. EdgeConnect = protocol-fan-in → canonical stream; EREMOS V2 = an OEE/incident dashboard panel); decorative (`aria-hidden`), token-only, with an "illustrative" caption. |
| 2 | **What it is** — 2-3 sentence product definition + the pillar it belongs to | `light` | Narrative + `/capabilities/<pillar>` cross-link | The "for what / for whom" frame. No pain narrative. |
| 3 | **Capabilities** — the product's feature set | `dark` | `CapabilityCard` grid (compact) | Deeper than the capability page's overview; still scannable. |
| 4 | **Spec matrix** — the full protocol/coverage matrix | `light` | **§18.A spec-table** | THE defining section. Status column (Available / Roadmap), semantic-mode + security-profile detail. Honors the locked protocol state (P-G). |
| 5 | **How it deploys + system requirements** | `light-tinted` | spec-table + narrative | Host platform (honest Windows-today / Linux-roadmap), sizing, offline, per-gateway identity, appliance option, **and a `Versioning + support lifecycle` subsection** (runtime/platform generation, supported host OS, adapter activation + update model, draft→apply→rollback, support boundary across EdgeConnect / Edge Gateway / EREMOS V2). Not a new section — lives inside §5 (or §7). |
| 6 | **Architecture — where it fits** | `light` | `ArchitecturePanel.interactive` (product-annotated) + "See full architecture →" | Product-scoped annotation subset; cross-link `/architecture`. |
| 7 | **Editions + licensing** | `dark` | editions cards (CapabilityCard variant) + spec-table | Starter / Professional / Enterprise + modular per-protocol activation; three-layer licensing. **Describes edition *structure* + licensing *mechanics* only — NOT detailed pricing tables** (those stay out of ProductDetail until Pricing governance is locked; edition labels are illustrative until commercial packaging is approved). |
| 8 | **Trust + security posture** | `light-tinted` | trust-cue content pattern (§16) | 2 cues; cross-link `/security`. |
| 9 | **Common questions** — inline FAQ | `light` | inline FAQ + `FAQPage` schema | Technical-evaluation Q&A (per §9 per-page-type FAQ governance; product pages = YES). |
| 10 | **Related** — cross-lens | `light-tinted` | cross-lens content pattern (§17) | Cards: the capability pillar + the solutions that use the product + `/architecture`. (Product-page preset — see §18.3.) |
| 11 | **Final CTA** | `dark-deep` | `CTASection` | §2.3-framed ("Request an architecture review" / "Bring us your controller mix"). |

Word-count target: **1,200-1,800 words page copy** (spec tables are not prose-counted, mirroring the §3.7/§3.8 diagram-annotation convention).

## 18.A Spec-table content pattern (NEW — content pattern, not a primitive)

A styled `<table>` for protocol/coverage/spec matrices. Columns vary by table (e.g., Protocol · Status · Semantic modes · Notes; or Spec · Value). Discipline rules:
- **Status column is mandatory** on any capability/protocol table — `Available` or `Roadmap`, never ambiguous. The locked protocol state (MT-LINKi = roadmap; S7 + OPC UA Client = available, per CLAUDE.md §8) is enforced here.
- **No fabricated performance numbers** (proof-architecture §3). Spec values trace to a locked source (CLAUDE.md, ARCHITECTURE_BLUEPRINT, the contracts, or the v5 datasheet).
- Visual: `mode`-aware borders + zebra rows via the existing surface tokens; teal accent on the header row. No new color tokens.
- A11y: real `<th scope>` headers; caption where the table needs context.
- **Spec-table variants** (same pattern, different content): protocol matrix · system-requirements table · licensing matrix · security-modes table. Authors reuse the pattern rather than inventing new structures.
- **Protocol matrices SHOULD carry a `Direction` column** (southbound / northbound) — or make direction unmistakable via group rows + caption — so a reader never has to infer whether an entry is a source or a sink.

## 18.3 Cross-lens preset for `/product` pages (proposed §17 addition)

Per design-system v3 §17 (LOCKED per-surface presets), add:

| From surface | Cards rendered |
|---|---|
| `/<product>` (ProductDetail) | `/capabilities/<pillar>` (the product's pillar) · the lead `/solutions/<solution>` that uses it · `/architecture` |

For `/edgeconnect`: `/capabilities/connectivity-edge` · **`/solutions/edge-connectivity`** (the lead solution card — the most direct EdgeConnect outcome) · `/architecture`. (`/solutions/cnc-machining` is the narrower vertical proof — secondary only if a 4th related card is ever allowed.)

## 18.4 Anti-patterns (ProductDetail-specific)

- ❌ Re-telling the capability or solution narrative as primary content — cross-link, don't duplicate (mirrors §15 memo v2 §4.0 authoritative-explanation invariant).
- ❌ Opening with a "customer pain" empathy narrative — that's SolutionPanel's move; ProductDetail opens with "what it is."
- ❌ A spec/protocol table without a Status column, or one that contradicts CLAUDE.md §8 (MT-LINKi must be Roadmap; S7 + OPC UA Client Available).
- ❌ Fabricated throughput/latency/uptime numbers, or customer/competitor names (proof-architecture §3/§4/§8).
- ❌ "Book a scoping call" as the primary CTA (a §2.3 backfire — use "Request an architecture review", precedent P-H).
- ❌ Introducing a new visual primitive — ProductDetail composes from v3 components + the §18.A spec-table content pattern only.
- ❌ Building the hardware variant before the certification open question + Phase 3/E ambiguity resolve.
- ❌ Publishing detailed pricing tables, or treating edition labels as locked commercial packaging — ProductDetail describes edition *structure* + licensing *mechanics*; pricing detail waits for Pricing governance (edition names illustrative until packaging is approved).

## 18.5 Governance

This addendum is a **proposal**. On sign-off (user + ChatGPT review + the `/edgeconnect` static mockup), §18 + §18.A + the §18.3 cross-lens preset fold into **design-system v4** (additive over v3; no v3 component changed). The `/edgeconnect` page spec is the shape-setter; `/eremos-v2` inherits §18. Hardware product pages use **§18.B (below)**.

---

## 18.B `ProductDetail` hardware variant

**LOCKED 2026-06-04** (user go-ahead; validated by the `/edge-gateway` shape-setter + two ChatGPT passes — full review + cert/IP re-review). §18 (software) and §18.B (hardware) are both LOCKED. Covers the 5 hardware products: **Edge Gateway** (Pillar 1), **mDAQ** (Pillar 2), **mTracker** (Pillar 3), **VAS** + **E-IDOS** (Pillar 4). `/edge-gateway` is the §18.B shape-setter.

### 18.B.0 Scope, buyer, certifications, phase

- **Phase (RESOLVED, user direction 2026-06-04):** hardware product pages are **Phase E** — this reconciles design-system v3 line 278's "reserved for Phase 3 product pages" reference, which is superseded. (Flag for a design-system v3.x note to align line 278.)
- **Certifications + ingress protection (RESOLVED, user + ChatGPT direction 2026-06-04):** the Elpis hardware products carry **no formal third-party certifications currently** (CE / UL / FCC / IEC), but Elpis **is open to pursuing them case-by-case** depending on customer / site / BOM requirements. Products are **IP65 / IP67-compatible** but **NOT separately certified**. Governance for §18.B hardware pages:
  - **No heavy certifications section, and NO formal cert *claims*.** Certification, ingress-protection, and site-compliance requirements are **handled case-by-case during BOM scope**.
  - Hardware pages **may include compliance/readiness discussion** (e.g. an ingress-protection spec row, an operating-environment row, a field-readiness section) **without claiming formal certification**.
  - **Allowed wording:** "IP65 / IP67-compatible configurations can be scoped during BOM review" / "designed for IP65 / IP67-compatible deployments, with final rating/certification confirmed during BOM scope."
  - **Forbidden** (unless formal evidence exists for the specific product/configuration): "IP65 certified", "IP67 certified", "IP-rated", "CE certified", "UL certified", "FCC certified", "IEC certified", "certified rugged", "field certified".
  - **No certification logos/marks** unless formally approved. Certified/rated claims are published **only when formal evidence exists** for that exact product/configuration.
  - This resolves the hardware-ecosystem-map v3 §264 open question (no formal certs currently; pursued case-by-case). (Flag for a §264 amendment recording the resolution.)
- **Primary buyer (per P-H — re-derive from the page's actual buyer):** the *hardware-relevant* buyer, which differs by product:
  - **Edge Gateway / mDAQ / mTracker** → **Plant engineer (retrofit / greenfield), buyer-taxonomy §2.5** — selects hardware, designs field wiring, owns the deployment-day checklist. CTA: **"Get hardware specifications"** / **"Request a BOM scope"** (§2.5-endorsed; NOT "Request an architecture review" or "Book a scoping call").
  - **VAS / E-IDOS** → **Maintenance Manager / AMC provider, buyer-taxonomy §2.4** (condition-monitoring instruments). CTA: **"Bring us your most-watched machine"** / **"Talk to a reliability engineer."**
- **Spec values trace to a locked source** (hardware-ecosystem-map v3 §2-§6) — and must be **verified before external publish** (some map anchors are flagged "orientation only," e.g. VAS / E-IDOS). No invented specs (proof-architecture §3).

### 18.B.1 How §18.B differs from §18 (software)

§18.B keeps the 11-section ProductDetail arc but swaps the software-specific sections for hardware ones, and **removes the certifications/security-mode depth** (no certs; the trust section becomes field-readiness):

| §18 (software) section | §18.B (hardware) section |
|---|---|
| Capabilities | **What it does + what it replaces** (the BOM-elimination story — hardware-ecosystem-map "eliminates from a customer BOM" content) |
| Spec matrix (protocol/integration) | **Hardware specifications** (the §18.A spec-table: dimensions, power, enclosure, environmental, connectivity, I/O, mounting) — the defining section |
| Deployment + system requirements | **Deployment in the field** (mounting, power, field-wiring, firmware update, ruggedization) |
| Editions + licensing | **How to buy** (the unit + the software it pairs with; mechanics, not pricing; labels illustrative) |
| Trust + security posture (cert/security modes) | **Field-readiness** (ruggedization, environmental range, offline operation — **NO cert claims**) |

Hero, What-it-is, Architecture, FAQ, Cross-lens, Final CTA carry over from §18 unchanged (hero gets a hardware-relevant visual — a device line-art or a spec-highlight panel, per the §18 hero-visual slot).

### 18.B.2 The hardware spec-table (§18.A variant)

A `Category | Value` (or `Spec | Value`) table for physical specifications. Same §18.A discipline: real `<th scope>`, token-only styling, **no fabricated numbers**, values trace to hardware-ecosystem-map v3. Group rows (Power · Enclosure · Connectivity · I/O · Environmental · Mounting) are encouraged for scannability. **No certifications row.**

### 18.B.3 Anti-patterns (hardware-specific)

- ❌ **Any formal certification claim** (CE / UL / FCC / IEC / "IP65 certified" / "IP67 certified" / "IP-rated" / "certified rugged" / "field certified") unless formal evidence exists for that exact product/configuration. **Allowed:** the IP65 / IP67-*compatible* wording (see §18.B.0). Cert / IP / site-compliance are handled case-by-case during BOM scope; no certification logos/marks unless formally approved.
- ❌ Fabricated or unverified physical specs — trace to hardware-ecosystem-map v3; flag "orientation only" anchors for verification before publish.
- ❌ Positioning the Edge Gateway as required for EdgeConnect, or collapsing the two into one product (carry-forward from connectivity-edge v2 §6) — honor the dual identity (standalone PLC-to-cloud today; canonical EdgeConnect appliance once Linux ships).
- ❌ Implying E-IDOS streams into EREMOS V2 today (it's a standalone instrument; streaming is near-term roadmap — hardware-ecosystem-map §5.2).
- ❌ "Book a scoping call" / "Request an architecture review" as the primary CTA — use the §2.5 (or §2.4) hardware CTA per P-H.
- ❌ A new visual primitive — §18.B composes from v3 components + the §18.A spec-table.

---

*ProductDetail page-shape addendum v1 — **LOCKED 2026-06-04** after ChatGPT review (verdict "Approve with changes"; all refinements applied) + user go-ahead. Defines the software-variant product-detail composition deferred by design-system v3 §15/§25. Reuses v3 components + one new content pattern (§18.A spec-table); introduces no new primitive. Pairs with the /edgeconnect static HTML mockup for visual sign-off. Folds into design-system v4 on sign-off. Cites: design-system-v3 §14/§15/§16/§17/§5.A/§25, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.3/§2.5, hardware-ecosystem-map-v3 (§264 cert open question), elpis-industrial-intelligence-platform-v5 (product facts), 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
