<!--
File:        docs/marketing/page-resources-spec-v2.md
Purpose:     Page spec for the Resources center — defines the ResourceHub
             (/resources) AND ResourceListing (/resources/datasheets,
             /brochures, /whitepapers) layouts for design-system v5, and
             provides the instance copy. Phase 3 Group B.
Audience:    Internal — Claude (mockup build), copywriters (verbatim lift),
             user + ChatGPT (reviewers), engineering (component implementers).
Format:      Per §9 canonical template locked in page-capabilities-hub-spec-v1.md.
Companion:   phase3-ia-scope-memo-v2.md (LOCKED PARENT — §3.2 ResourceHub +
                ResourceListing + asset inventory; §4 proof/anonymity; §5 buyer
                map; §6 anti-duplication; §9 acceptance gate)
             buyer-taxonomy-v1.md (all buyers, late-stage / self-serve)
             proof-architecture-v1.md §3/§4/§8 (proof + honesty)
             design-system-v4.md (extends — ResourceHub + ResourceListing land as v5)
             LOCKED cross-link targets (do NOT duplicate spec tables):
                product pages /edgeconnect … /e-idos; /platform; /capabilities; /solutions
Version:     v2 — LOCKED after Pass 1 ChatGPT review (approved with no edits).
Date:        2026-06-06
Status:      LOCKED. ResourceHub + ResourceListing shape-setter.

v1 (page-resources-spec-v1.md) retained as historical reference.
v1 -> v2: no content changes — ChatGPT approved Resources for lock as-is
(honest card state per §2 inventory; only datasheet + deck "Download";
brochures "Request access"; whitepapers "Coming soon"/"Request access";
gating visual-only; product detail cross-linked, not duplicated).

Phase 3 Group B (Resources). Drafted alongside page-customers-spec-v1.md.
Defines TWO reusable layouts: ResourceHub (the /resources directory) and
ResourceListing (the 3 category pages share one shape). §9 acceptance gate
pre-checked in §7. The single sharpest rule here: card state must be HONEST
per the §3.2 asset inventory — a card may only say "Download" when the asset
actually exists; everything else is "Coming soon" or "Request access"
(visual state only — no functional lead-capture; that is Phase 4).
-->

# Resources center — Page Spec v2 (LOCKED) · ResourceHub + ResourceListing shape-setter

**The self-serve collateral surface. Late-stage, all-buyer: a visitor who already understands the platform comes here to grab a datasheet, brochure, or whitepaper to forward internally. This spec defines the reusable `ResourceHub` (the `/resources` directory) and `ResourceListing` (the category pages) layouts, and provides the instance copy. The hard rule: every card's state is HONEST — real assets download; everything else shows "Coming soon" or "Request access". No card implies a PDF that does not exist.**

`/resources` is **not** a product surface (product detail lives on `/edgeconnect` … `/e-idos`, LOCKED) and **not** a capability/solution explanation. It is a **library**: a directory of downloadable assets, filterable, with honest availability state. Per phase3 memo §6, it links to the product/platform owners rather than re-deriving their content.

Target length: hub ~500-700 words; each listing ~250-400 words (utility surface, lighter than a narrative page).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** A resource library. Reader leaves with *"I found the asset I needed (or requested it), and I can forward it internally."*

**IS NOT:**
- A product detail page (cross-links to `/edgeconnect` … `/e-idos`; never duplicates their spec tables)
- A capability/solution/industry explanation (cross-links the LOCKED owners)
- A gated lead-capture funnel in this wave — gating is represented as a **visual "Request access" state only**; functional email-capture is Phase 4 (memo §3.2 + §10 Q3)
- A page that lists assets which don't exist — card state is honest per the §3.2 asset inventory

### 1.2 Buyer alignment (per buyer-taxonomy v1 + memo §5)

**Primary:** all buyers, **late-stage** — someone who has already read the platform/solution/product story and now wants collateral to share with a colleague, a procurement reviewer, or an internal approver. CTA preference: *Download* (when available) / *Request access* (when gated or not-yet-published).

- Vocabulary that lands: datasheet, brochure, spec sheet, whitepaper, PDF, request access.
- Vocabulary that backfires: "gated content", "lead magnet", "fill out the form to unlock" (reads as marketing-funnel friction to a technical buyer).

### 1.4 Page metadata (SEO + HTML head)

| Page | Meta title (≤60) | Meta description (140-160) |
|---|---|---|
| `/resources` | *Resources — datasheets, brochures, whitepapers · Elpis* | *Datasheets, hardware brochures, and whitepapers for the Elpis Industrial Intelligence Ecosystem. Download what's available; request what's coming.* |
| `/resources/datasheets` | *Datasheets · Elpis Resources* | *Platform and product datasheets for EdgeConnect, EREMOS V2, and the Elpis acquisition hardware. Download the current sheet; request product-specific sheets.* |
| `/resources/brochures` | *Brochures · Elpis Resources* | *Hardware brochures for Edge Gateway, mDAQ, mTracker, VAS, and E-IDOS. Request access while the brochure set is being finalized.* |
| `/resources/whitepapers` | *Whitepapers · Elpis Resources* | *Technical whitepapers on edge connectivity, canonical data, OEE, and condition monitoring. Request access — the whitepaper series is in preparation.* |

Schema intent: `schema.org/CollectionPage` + `BreadcrumbList`. No `FAQPage`. Asset links use real file URLs only where the asset exists; placeholder cards carry no `download` URL.

---

## 2. The §3.2 asset inventory (card-state source of truth)

Reproduced from phase3 memo v2 §3.2 — this table governs every card's state. **A card may present "Download" ONLY if the asset row says Exists.**

| Asset | State | Card state | Link |
|---|---|---|---|
| Platform datasheet (`assets/datasheet-v3-a4.pdf`) | Exists | **Download** | the real PDF |
| Pitch deck (`assets/pitch-deck-v7.pptx`) | Exists | **Download** (or featured) | the real file |
| Product brochures — Edge Gateway, mDAQ, mTracker, VAS, E-IDOS | Exists (content LOCKED v2; A4 PDFs in `web/assets/brochure-*-a4.pdf`) | **Download** (gated) | the real PDFs — gated via gate.js |
| Whitepapers (edge connectivity, canonical data, OEE, condition monitoring) | Exists (content LOCKED v2; A4 PDFs in `web/assets/whitepaper-*-a4.pdf`) | **Download** (gated) | the real PDFs — gated via gate.js |

> Any asset added later must be added to this table with its true state before its card may show "Download". Engineering must not wire a `download` attribute to a non-existent file.

> **Amendment 2026-06-06 (gate every download — memo §10 Q3 amended):** "Download" no longer means *open access*. Per the user decision, **every** real-asset download (platform datasheet, overview deck, and the datasheet CTAs across the site) is now preceded by a **contact-capture form** (`gate.js`). "Download" in this table therefore means *the asset exists and is gated by the contact form*; "Request access" / "Coming soon" are unchanged (those assets don't exist yet). The form is a **visual mockup only** — no data is stored or sent; functional capture / CRM is a Phase 4 deliverable.

---

## 3. ResourceHub — `/resources` (the directory)

### 3.1 Section structure (ResourceHub shape)

| # | Section | Mode | Component |
|---|---|---|---|
| 1 | Hero (eyebrow + headline + sub) | `dark-deep` | `SectionShell` |
| 2 | Resource categories (3 cards → listing pages) | `light` | Card grid |
| 3 | Available now (the assets that actually exist) | `light-tinted` | Asset cards (Download) |
| 4 | Cross-lens (Platform · Capabilities · Solutions) | `light-tinted` | §17 cross-lens |
| 5 | CTA | `dark-deep` | `CTASection` |

### 3.2 Copy

> **§1 Hero**
> EYEBROW: RESOURCES
> HEADLINE: Everything you need to make the internal case.
> SUBHEAD: Datasheets, hardware brochures, and technical whitepapers for the Elpis Industrial Intelligence Ecosystem. Download what's published today; request what's in preparation — we'll send it the moment it's ready.

> **§2 Resource categories** (3 cards → `/resources/<category>`)
> | Card | Title | Blurb | Destination |
> |---|---|---|---|
> | 1 | Datasheets | Platform and product spec sheets — the technical one-pager for an internal reviewer. | `/resources/datasheets` |
> | 2 | Brochures | Hardware brochures for the acquisition product line. | `/resources/brochures` |
> | 3 | Whitepapers | Deeper technical reads on connectivity, canonical data, OEE, and condition monitoring. | `/resources/whitepapers` |

> **§3 Available now** (only the real assets — honest)
> SECTION TITLE: Available to download today.
> - **Platform datasheet (PDF)** — the one-page overview of the Industrial Intelligence Ecosystem. **Download →** (`assets/datasheet-v3-a4.pdf`)
> - **Platform overview deck** — the ecosystem and 5-pillar story in slide form. **Download →** (`assets/pitch-deck-v7.pptx`)
> NOTE (size.sm italic): Hardware brochures are now available to download (A4 PDF, gated). The whitepaper series is in preparation — request access on its page and we'll send it as it publishes.
>
> **Amendment 2026-06-06 (brochures produced):** the 5 hardware brochures (`brochure-*-v1.md`, content LOCKED v2) were rendered to A4 PDFs in `web/assets/brochure-*-a4.pdf`; their `/resources/brochures` cards moved from "Request access" → **Download** (gated via gate.js). §2 inventory updated. Whitepapers remained "Coming soon" until produced.
>
> **Amendment 2026-06-06 (whitepapers produced):** the 4 whitepapers (`whitepaper-*-v1.md`, content LOCKED v2) were rendered to A4 PDFs in `web/assets/whitepaper-*-a4.pdf`; their `/resources/whitepapers` cards moved from "Coming soon" → **Download** (gated via gate.js). §2 inventory updated. **Every resource card is now a real, gated download** — the resource center is complete.

> **§4 Cross-lens** — CAPABILITIES → `/capabilities` · PLATFORM → `/platform` · SOLUTIONS → `/solutions`

> **§5 CTA**
> EYEBROW: NEXT STEP
> HEADLINE: Need something you don't see here?
> SUBHEAD: Tell us what you're evaluating and who needs to see it — we'll point you to the right asset or prepare what you need.
> PRIMARY CTA: Request a resource → `mailto:contact@elpisitsolutions.com?subject=Resource%20request`
> SECONDARY CTA: Book a scoping call → `mailto:contact@elpisitsolutions.com?subject=Scoping%20call`

---

## 4. ResourceListing — the category pages (shared shape)

### 4.1 Section structure (ResourceListing shape — all 3 category pages)

| # | Section | Mode | Component |
|---|---|---|---|
| 1 | Hero (category-framed) | `dark-deep` | `SectionShell` |
| 2 | Filter bar (type / product / industry / audience) | `light` | Facet chips — **visual only in the mockup** |
| 3 | Asset card grid (honest state per §2 inventory) | `light` | Resource cards |
| 4 | Cross-lens + CTA | `light-tinted` / `dark-deep` | §17 + `CTASection` |

**Card anatomy:** title · one-line description · type/product tags · **state action** — one of:
- **Download** (asset exists) — links to the real file
- **Request access** (gated or not-yet-published) — opens the visual request affordance (mockup: a `mailto:` with the asset name in the subject; Phase 4 wires real capture)
- **Coming soon** (in preparation, not yet requestable) — non-interactive pill

> **Gating note (governance, not displayed):** Per memo §10 Q3, the mockup represents email-gating as a "Request access" state ONLY. No form, no lead-capture, no "unlock". Functional gating is Phase 4.

### 4.2 Per-category content

#### `/resources/datasheets`
> HERO: Datasheets — the technical one-pager. / The spec sheet a reviewer can read in two minutes.
> Cards:
> - **Platform datasheet** — Industrial Intelligence Ecosystem overview. Tags: Platform. **Download** (`assets/datasheet-v3-a4.pdf`).
> - **EdgeConnect datasheet** — protocol coverage, deployment, diagnostics. Tags: Software · EdgeConnect. **Request access** (link context: `/edgeconnect`).
> - **EREMOS V2 datasheet** — OEE, alarms, multi-tenant analytics. Tags: Software · EREMOS V2. **Request access** (`/eremos-v2`).
> - **Hardware spec sheets** (Edge Gateway / mDAQ / mTracker / VAS / E-IDOS) — **Request access** (link context: the product pages).

#### `/resources/brochures`
> HERO: Brochures — the hardware line at a glance. / Field-ready overviews of the acquisition products.
> Cards (all **Request access** per §2 inventory — brochures missing/partial; never "Download"):
> - Edge Gateway brochure → context `/edge-gateway`
> - mDAQ brochure → `/mdaq`
> - mTracker brochure → `/mtracker`
> - VAS brochure → `/vas`
> - E-IDOS brochure → `/e-idos`
> NOTE (italic): The brochure set is being finalized. Request access and we'll send the current version as it publishes; in the meantime each product page carries the full detail.

#### `/resources/whitepapers`
> HERO: Whitepapers — the deeper technical reads. / For the architect who wants the reasoning, not just the spec.
> Cards (all **Coming soon** / **Request access** — whitepapers missing per §2 inventory):
> - "Protocol-agnostic edge: canonical data at the source" — **Coming soon**
> - "Store-and-forward and three-way diagnostics on the edge" — **Coming soon**
> - "OEE you can defend: computing segments on canonical signals" — **Coming soon**
> - "Condition monitoring as an early-warning trigger, not a guarantee" — **Coming soon**
> NOTE (italic): The whitepaper series is in preparation. Request access to be notified as each publishes.

---

## 5. Components used

All design-system v4 LOCKED + the new `ResourceHub` / `ResourceListing` compositions (§7 shape definitions). No net-new primitives beyond the resource card states.

| Component | Used in |
|---|---|
| `SectionShell` (modes) | every section |
| Card grid | hub categories, listing asset grid |
| Resource card (Download / Request access / Coming soon states) | listings + hub §3 |
| Facet chips (visual only) | listing filter bar |
| `CapabilityCard` (cross-lens) | hub + listing cross-lens |
| `CTASection` | hub + listing CTA |

---

## 6. Anti-patterns specific to this surface

| Don't | Why |
|---|---|
| Show "Download" on a card whose asset doesn't exist | The single sharpest rule — §2 inventory governs. Dead/implied PDFs break trust (memo §3.2 + §9 gate) |
| Build a functional email-capture / "unlock" form | Gating is a visual state only this wave; functional capture is Phase 4 (memo §10 Q3) |
| Duplicate product spec tables on a resource card | memo §6 — link to the product page; the card is a pointer, not a copy |
| Invent whitepaper findings, brochure metrics, or datasheet claims | proof-architecture §3/§4 — no fabricated content; coming-soon items carry titles only, no fabricated abstracts with data |
| Use "lead magnet" / "fill the form to unlock" language | buyer-taxonomy — reads as funnel friction to a technical buyer |
| Name customers or quote metrics in resource blurbs | memo §4 — no named customers, no fabricated metrics |

---

## 7. ResourceHub + ResourceListing shape definitions (for design-system v5)

**ResourceHub** (§3.1) — hub directory: hero → category cards → available-now (real assets) → cross-lens → CTA. One instance: `/resources`.

**ResourceListing** (§4.1) — category listing: hero → filter bar (visual facets) → honest-state asset card grid → cross-lens + CTA. Three instances: `/resources/datasheets`, `/resources/brochures`, `/resources/whitepapers`. The three differ only in hero copy + which cards/states they carry (per §2 inventory).

**Invariant across both:** card state is honest per the §2 asset inventory; gating is visual-only; no re-derivation of product/capability content (cross-link instead).

---

## 8. Phase 3 acceptance gate (memo v2 §9) — pre-checked

- [x] Cites `phase3-ia-scope-memo-v2` as parent
- [x] Cites §4 proof/anonymity + honest card-state rule (§2 inventory)
- [x] No fabricated metrics; coming-soon items carry titles only
- [x] No named customers / logos
- [x] No certification claims
- [x] No competitor names
- [x] Protocol status N/A on this surface (no protocol claims made); product pages own protocol detail
- [x] No `/pricing` / pricing detail
- [x] No individual `/customers/<story>` routes (N/A)
- [x] Resource cards show real / coming-soon / request-access state honestly per §2 — the central rule
- [x] Does not re-derive a LOCKED owner (§6) — cross-links instead
- [x] Gating is visual-only (no functional capture) per memo §10 Q3

---

## 9. Out of scope

- Functional email-capture / lead-gen / CRM (Phase 4)
- Resource taxonomy back-end / CMS (memo §10 — evaluation only)
- Actually producing the missing brochures + whitepapers (content production is a separate business task; the page ships the honest "request access"/"coming soon" states now)
- Versioned-docs / documentation portal (Phase 4)
- Pricing sheets (Phase 4 `/pricing`)

---

*Resources center Page Spec **v2 LOCKED 2026-06-06** (ChatGPT Pass 1 approved with no edits) — defines `ResourceHub` (`/resources`) + `ResourceListing` (`/resources/datasheets`, `/brochures`, `/whitepapers`) for design-system v5, with instance copy. Central discipline: honest card state per the memo §3.2 asset inventory (only the platform datasheet + overview deck exist → "Download"; brochures + whitepapers → "Request access" / "Coming soon"); gating is visual-only (Phase 4 wires capture). Cross-links the LOCKED product/platform/capability/solution owners; re-derives none (memo §6). §8 pre-checks the memo §9 acceptance gate (all green). Cites: phase3-ia-scope-memo-v2 (parent §3.2/§4/§6/§9/§10 Q3); buyer-taxonomy v1; proof-architecture v1 §3/§4/§8; design-system v4 (→ v5).*
