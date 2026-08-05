# Marketing Design Execution — Session Handoff

**Date:** 2026-05-24
**Author of this handoff:** Marketing Content Session (closed 2026-05-24, PR #29 merged)
**Audience:** Fresh Claude session producing visual, presentation, and web deliverables from the marketing content already shipped.
**Branch this lives on:** `claude/marketing-design-handoff` — merge before starting the next session, or branch from it explicitly.

This document briefs a fresh session to turn the marketing **content** that just shipped into the marketing **assets** that customers actually see: branded PowerPoint, branded SVG diagrams, designed-and-built website pages, print-ready PDFs, and a working ROI calculator.

Read this first. Then read the marketing closeout (`docs/sessions/2026-05-24-marketing-handoff-closeout.md`) for the full content inventory and decision log. Then ask the user which deliverable to start with.

---

## 0. What this session is for

**Produce visual, presentation, and interactive assets** from the marketing content that already exists:

- Branded SVG architecture diagrams (master + per-vertical variants)
- Branded executive pitch deck (PowerPoint or Keynote)
- Designed website pages (wireframes → designs → built pages)
- Print-ready datasheet PDF
- Working ROI calculator (Excel + web variant + PDF worksheet)
- Designer-ready solution-brief PDFs (derived from solution pages)
- Email-rendering and outreach-tool configuration (if those are being launched)

**This session is NOT:**

- A marketing-content writing session. Copy is locked in v2/v4 final files; edits to copy go back to a marketing-content session, not the design session.
- A strategic positioning session. The seven-audience segmentation and the platform positioning are locked.
- A net-new-deliverables session. The 14 deliverables shipped in the prior session are the source materials; this session executes them visually.

---

## 1. What's already produced (source materials)

Every visual deliverable in this session derives from one or more of these source files. All are on `master` after PR #29 merge.

| Source file | What it feeds |
|---|---|
| `docs/marketing/elpis-industrial-intelligence-platform-v4.md` | Datasheet PDF, homepage hero, every solution page hero, pitch deck content |
| `docs/marketing/pitch-deck-outline-v1.md` | Branded executive pitch deck (12 slides) |
| `docs/marketing/architecture-diagram-spec-v2.md` | Branded SVG architecture diagram + all per-vertical variants |
| `docs/marketing/website-messaging-architecture-v2.md` | Full site IA + per-page messaging for designer + developer |
| `docs/marketing/homepage-copy-v2.md` | Homepage production copy + visual/pacing notes |
| `docs/marketing/solution-cnc-machining-v2.md` | `/solutions/cnc-machining` page + canonical template for the other 4 |
| `docs/marketing/solution-brownfield-modernization-v2.md` | `/solutions/brownfield-modernization` page |
| `docs/marketing/solution-multi-site-operations-v2.md` | `/solutions/multi-site-operations` page |
| `docs/marketing/solution-oem-machine-monitoring-v2.md` | `/solutions/oem-machine-monitoring` page |
| `docs/marketing/solution-precision-manufacturing-v2.md` | `/solutions/precision-manufacturing` page |
| `docs/marketing/security-page-copy-v2.md` | `/security` page + trust-annotated architecture diagram variant |
| `docs/marketing/roi-calculator-spec-v2.md` | Excel template + web calculator + PDF worksheet |
| `docs/marketing/sales-objection-handling-internal-v2.md` | Internal sales doc — minimal design treatment beyond clean formatting |
| `docs/marketing/email-outreach-templates-v1.md` | Sales-rep outbound tooling configuration |
| `docs/sessions/2026-05-24-marketing-handoff-closeout.md` | Full content inventory, locked decisions, lessons learned |

The full content inventory with brief descriptions is in §2 of the closeout doc.

---

## 2. Visual identity — what's been specified

Multiple source files specify the visual direction. The shortest synthesis:

### Palette (suggested defaults — defer to Elpis brand book if it exists)

- **Background:** deep navy or near-black, e.g. `#1A1F26`, `#0F1419`, `#1E2329`
- **Container fills (hero blocks like EdgeConnect, EREMOS V2):** elevated steel grey, e.g. `#2A2F36` with subtle gradient
- **Container fills (secondary tier):** mid steel grey, e.g. `#3A4049`
- **Tertiary boxes:** lighter grey or transparent with thin border
- **Borders / dividers:** cool grey, low contrast, e.g. `#4A5560`
- **Body text:** off-white, e.g. `#E8ECF1`
- **Accent (sparingly, primary CTAs and emphasized arrows):** ONE brand-accent color. Suggested amber/orange if Elpis brand uses warm accent, or cyan/teal if cool. **Only one.**

### Typography

- Sans-serif, geometric or humanist — Inter, IBM Plex Sans, Manrope, or the Elpis brand face if defined
- Three weights only: regular for labels, semibold for box titles, bold for headlines
- All-caps reserved for short single-word labels (`MQTT`, `OPC UA`)
- Generous letter spacing on small text for projection legibility

### Imagery rules

- **Real shop-floor photography only.** No stock-photo handshakes. No "happy worker with tablet." No aerial assembly-line shots.
- **Specific over generic.** A real Fanuc 18i controller close-up beats a generic factory shot.
- **For OEM contexts:** OEM-built machine on a customer's floor, mid-operation.
- **For brownfield contexts:** older Fanuc controller, slightly worn, operator's hand on MPG.
- **For security contexts:** closed control cabinet, dim lighting, no people. Calm. NOT cybersecurity clichés (no padlocks, hooded hackers, glowing networks).
- **Geometric illustration acceptable** if photography isn't available — single coherent icon set (Tabler, Lucide, Phosphor, or custom).

### Performance budgets (developer-side, but designer affects)

- First contentful paint < 1.5s on a 4 Mbps connection
- Largest contentful paint < 2.5s
- Total page weight < 1 MB for content pages, < 2 MB for homepage (including architecture diagram)
- JavaScript bundle < 200 KB compressed
- Web fonts: subset to Latin where possible, max 2 weights per page
- Images: WebP or AVIF, lazy-loaded below fold, no autoplay video

### Accessibility (mandatory)

- WCAG AA equivalent: 4.5:1 contrast for body text, 3:1 for large headings (validate against both dark and light variants)
- Keyboard navigation: every interactive element reachable, visible focus states
- Mobile-first responsive: 16 px minimum body text, 44×44 px tap targets, no horizontal scroll
- Color is never the only carrier of meaning

### Anti-patterns (do NOT do)

- No padlocks, hooded hackers, shields, glowing networks (cybersecurity clichés)
- No "AI brain with circuits" abstract imagery
- No spinning gears, "Industry 4.0" parallax effects
- No autoplay videos, hero animations that delay first paint
- No gradient overload (one subtle gradient per hero block max)
- No more than one accent color
- No AWS / Azure / GCP logos in the architecture diagram (vendor-neutral)
- No emoji in marketing copy or subject lines
- No competitor logos or names on customer-facing pages

---

## 3. Per-deliverable design scope

### 3.1 Branded SVG architecture diagram

**Source:** `docs/marketing/architecture-diagram-spec-v2.md` — the most rigorous designer brief in the system. Includes sign-off checklist, palette, typography, iconography, multi-site emphasis, output variants, anti-patterns.

**Output:**
- `architecture-diagram-v1-dark.svg` — master (full version, dark palette)
- `architecture-diagram-v1-light.svg` — light variant
- `architecture-diagram-v1-slide.svg` — 16:9 pitch-deck variant
- `architecture-diagram-v1-simple.svg` — 3-box executive variant
- `@2x` PNG fallbacks for each
- **Per-vertical variants** for solution pages (CNC, brownfield, multi-site, OEM, precision, security trust-annotated)

**Tool suggestions:** Figma or Illustrator. Mermaid-rendered placeholder lives in the source files as structural reference.

**Why this comes first:** every other visual asset (datasheet, pitch deck slide 8, every solution page, security page) embeds or references this diagram. Unblocks the whole pipeline.

**Place final assets in:** `docs/marketing/assets/` (create the directory).

---

### 3.2 Executive pitch deck (PowerPoint or Keynote)

**Source:** `docs/marketing/pitch-deck-outline-v1.md` — 12 slides, each with key message + content + visual notes + speaker notes.

**Output:** branded `.pptx` (most compatible; deliver `.key` too if Elpis uses Keynote) + PDF export for distribution.

**Slide order:**
1. Title
2. The problem
3. Designed for
4. The solution
5. Outcomes
6. Replace spreadsheets
7. Connectivity coverage
8. Architecture at a glance (uses branded SVG from §3.1)
9. Why Elpis
10. Deploy incrementally
11. Editions and roadmap
12. Next step

**Designer briefing notes** are in the outline file's closing section (palette, typography, imagery rules, animation budget).

**Critical:** respect the per-slide visual notes. The outline is opinionated for a reason — visual hierarchy and pacing are part of the design, not just the text.

---

### 3.3 Website pages

**Source:** `docs/marketing/website-messaging-architecture-v2.md` (site IA, per-page messaging) + `docs/marketing/homepage-copy-v2.md` (homepage production copy) + 5 solution page copies + `docs/marketing/security-page-copy-v2.md`.

**Output:** wireframes → designs → built pages for:
- `/` (homepage — full production copy ready)
- `/platform` (platform overview — messaging architecture has the structure)
- `/edgeconnect` (product page)
- `/eremos` (product page)
- `/solutions/cnc-machining` (full copy ready)
- `/solutions/precision-manufacturing` (full copy ready)
- `/solutions/brownfield-modernization` (full copy ready)
- `/solutions/oem-machine-monitoring` (full copy ready)
- `/solutions/multi-site-operations` (full copy ready)
- `/security` (full copy ready)
- `/pricing` (messaging architecture has the structure)
- `/resources` (downloads page — links to datasheet PDF, pitch deck PDF, architecture SVG)
- `/contact` (form spec in messaging architecture §14)
- `/company` (placeholder — needs user-supplied content)
- `/customers` (placeholder until real customer logos)

**Tool suggestions:**
- **Wireframes / designs:** Figma
- **Built site:** framework choice TBD — Webflow / Next.js / Astro / WordPress all plausible

**Performance and accessibility** mandatory per §18, §19 of the messaging architecture.

**Order:** homepage first (sets the brand pattern), then solution pages, then product pages, then security, then pricing/resources/contact, then placeholders (company, customers).

---

### 3.4 Datasheet PDF (print-ready)

**Source:** `docs/marketing/elpis-industrial-intelligence-platform-v4.md` — full content. Currently formatted for long-form web one-pager.

**Output:** print-ready branded PDF, suitable for trade-show handout, sales-packet inclusion, customer email attachment.

**Tool suggestions:** InDesign for print-quality typography and bleeds; Figma → PDF export acceptable for a faster path.

**Layout decisions:** reflow the long-form content into print page geometry. Architecture diagram embedded as branded SVG (from §3.1). Avoid the dense web-style tables — print typography allows more whitespace.

**Format:** consider both A4 and US Letter variants if Elpis sells internationally. Watch for fold-line legibility if it's distributed as a tri-fold brochure.

---

### 3.5 ROI calculator

**Source:** `docs/marketing/roi-calculator-spec-v2.md` — math, inputs, outputs, UX guidance, discipline rules.

**Output (three deliverables, priority order):**
1. **Excel / Google Sheets template** (canonical) — math lives here, every formula visible, no hidden VBA
2. **Web calculator** (lead magnet) — interactive form, live recalculation, gated download of the Excel
3. **PDF worksheet** (sales-meeting handout) — two pages: blank input grid + worked example

**Tool suggestions:** Excel for the spreadsheet, Figma + framework for the web, design tool for the PDF.

**Critical discipline (per spec §7):** the calculator must NEVER fabricate value. Every output traces to a customer-supplied input. No "typical 30% downtime reduction" baked in. The downtime formula caveat (recovered downtime ≠ productive throughput in capacity-constrained plants) must be surfaced prominently, not buried in footnotes.

---

### 3.6 Solution-brief PDFs (per vertical)

**Source:** the 5 solution-page copy files. Each is ~1,500-1,800 words structured for a vertical buyer.

**Output:** 4-8 page branded PDFs per vertical, suitable for sales packets and emailed pre-meeting briefings.

**Order:** start with CNC machining (the template-source page); the other four inherit the pattern.

**Designer choice:** the solution pages each include section-level visual notes and reader-effect notes. Use them.

---

### 3.7 Sales-team and internal assets (minimal design)

**Source:** `docs/marketing/sales-objection-handling-internal-v2.md` and `docs/marketing/email-outreach-templates-v1.md`.

**Output:** clean PDF or web-formatted internal references for the sales team. Minimal branding (Elpis wordmark + clean typography). These are working documents, not customer-facing assets.

**Critical:** the objection guide is INTERNAL ONLY (the header explicitly warns against external distribution). The PDF should carry a header / footer flag making that obvious — *"INTERNAL ONLY — DO NOT DISTRIBUTE"*.

---

## 4. Things this session must NOT do

| Don't | Why |
|---|---|
| **Modify the source copy** | Content is locked in v2/v4 files. Copy edits go back to a marketing-content session, not the design session. |
| **Change strategic positioning** | The seven-audience segmentation, the "Industrial Intelligence Platform" category framing, the locked decisions in the closeout doc §4 — all locked. |
| **Invent customer logos, names, testimonials** | Wait for the user to supply real ones. Every page has a placeholder strip — keep it as a placeholder. |
| **Claim certifications** (ISO 27001, SOC 2, IEC 62443, IATF 16949, AS9100, 21 CFR Part 11) | The platform hasn't been formally certified against these frameworks. The honest "what you can verify today" framing on the security page is the right level. |
| **Display competitor logos or names** on customer-facing assets | The objection guide names competitors internally; nothing external should. |
| **Ship without the user's sign-off checklist completion** | Every deliverable has a sign-off checklist in its source file. Use it. |
| **Use stock photography or AI-generated imagery** | Real shop-floor only. If no real photos exist, use clean geometric illustration. |
| **Add tracking pixels or analytics that violate the privacy posture** in messaging-architecture §19, §20 | Industrial buyers detect and resent these. |

---

## 5. Visual identity confirmation questions for the user (ask at session start)

The marketing-content session assumed defaults for several visual choices. Confirm or correct these before any production work begins:

1. **Does Elpis have a brand book / brand guidelines document?** If yes, defer to it on palette, typography, logo treatment. The defaults in §2 above become advisory only.
2. **What's the accent color?** Suggested amber/orange (warm) or cyan/teal (cool). One color, used sparingly.
3. **Where's the Elpis logo file?** Need vector (.svg or .ai) for SVG/PDF embedding and PNG for web.
4. **What's the preferred design tool?** Figma vs Illustrator vs Sketch.
5. **What's the preferred presentation tool?** PowerPoint vs Keynote vs Google Slides. (Deliver `.pptx` regardless for compatibility.)
6. **What's the web framework / CMS?** Webflow vs Next.js vs Astro vs WordPress vs other. Affects build-vs-handoff scoping.
7. **Is design work happening in-session, or being handed to an external designer?** Affects what this session produces (deep specs vs actual asset files).
8. **Timeline and sequence priority?** Which deliverable does the user need first?

---

## 6. Recommended deliverable order

In priority order, based on unblocking dependencies:

1. **Visual identity confirmation** (the 8 questions above) — 30 minutes with the user
2. **Branded SVG architecture diagram** (§3.1) — unblocks pitch deck, datasheet, every solution page
3. **Pitch deck** (§3.2) — highest-leverage visual asset per the prior session's ChatGPT verdict
4. **Homepage wireframe → design → build** (§3.3, homepage first) — sets the brand pattern for everything else on the site
5. **Datasheet PDF** (§3.4) — needed for sales packets, trade shows, prospect emails
6. **Solution page wireframes → designs → builds** (§3.3, five pages) — derive pattern from homepage; CNC page first since it's the template source
7. **ROI calculator** (§3.5) — Excel first, then web variant, then PDF worksheet
8. **Security page** (§3.3) — important for enterprise sales but lower volume than the homepage
9. **Solution-brief PDFs** (§3.6) — derived from solution pages
10. **Internal sales assets** (§3.7) — minimal design treatment, last priority

---

## 7. First-action checklist for the next session

1. **Read this handoff doc end-to-end** (you're doing it now)
2. **Read `docs/sessions/2026-05-24-marketing-handoff-closeout.md`** — full content inventory, locked decisions, lessons learned
3. **Read `docs/marketing/architecture-diagram-spec-v2.md`** — the most rigorous designer brief in the system, sets the tone for visual execution
4. **Read `docs/marketing/website-messaging-architecture-v2.md` §18, §19, §20** — performance budget, accessibility, analytics constraints that affect every web asset
5. **Ask the user the 8 visual-identity confirmation questions in §5 above**
6. **Ask the user which deliverable from §6 to start with** (default recommendation: branded SVG architecture diagram)
7. **Verify current branch before every commit** per `feedback_branch_verification.md`
8. **Apply the v1 → user-review → v2 cadence** per `feedback_planning_cadence.md`. For design work, "review" often means user looking at the rendered asset, not ChatGPT reviewing markdown — adjust the cadence accordingly.

---

## 8. How this session relates to the marketing-content session (just closed)

| Dimension | Marketing content (closed) | Design execution (this session) |
|---|---|---|
| **What gets produced** | Copy, structure, narrative, voice | Visual assets, presentations, interactive tools, built code |
| **Sources of truth** | CLAUDE.md, ARCHITECTURE_BLUEPRINT.md, shared-knowledge contracts | The 14 marketing-content deliverables (v2/v4 final) |
| **Editing rights** | Free to write/revise copy | Free to interpret visually; copy edits go BACK to a marketing-content session |
| **Approval mechanism** | User sign-off per deliverable, with ChatGPT review pass | User sign-off per visual asset (designer-by-eye review, not markdown review) |
| **Lives in git as** | Markdown files under `docs/marketing/` | Asset files under `docs/marketing/assets/` (create dir) + designed/built website code in its own location |
| **Cadence** | v1 → ChatGPT review → v2, freeze at v2 | v1 → user review of rendered asset → v2, freeze at user OK |
| **Branch convention** | `claude/marketing-<topic>` | `claude/marketing-design-<asset>` |

---

## 9. Where everything in the marketing content stack lives

After PR #29 merge, all source files are on `master`:

```
docs/marketing/
├── SESSION_HANDOFF.md                              # original session brief
├── DESIGN_EXECUTION_HANDOFF.md                     # ← this file
├── elpis-industrial-intelligence-platform-v4.md    # datasheet (canonical)
├── pitch-deck-outline-v1.md                        # pitch deck outline
├── architecture-diagram-spec-v2.md                 # designer brief (canonical)
├── website-messaging-architecture-v2.md            # web IA (canonical)
├── homepage-copy-v2.md                             # homepage copy (canonical)
├── security-page-copy-v2.md                        # /security copy (canonical)
├── solution-cnc-machining-v2.md                    # CNC vertical (canonical, template source)
├── solution-brownfield-modernization-v2.md         # brownfield (canonical)
├── solution-multi-site-operations-v2.md            # multi-site (canonical)
├── solution-oem-machine-monitoring-v2.md           # OEM (canonical)
├── solution-precision-manufacturing-v2.md          # precision (canonical)
├── roi-calculator-spec-v2.md                       # ROI calc (canonical)
├── sales-objection-handling-internal-v2.md         # internal objection guide (canonical)
├── email-outreach-templates-v1.md                  # outbound templates (final)
└── [v1/v2/v3 historical versions retained for cadence audit]

docs/sessions/
└── 2026-05-24-marketing-handoff-closeout.md        # marketing-content session closeout

docs/marketing/assets/                              # ← create this dir for design output
└── [SVG, PNG, PPTX, PDF assets land here]
```

---

*End of design execution handoff. Next session opens cold and is up to speed in ~20 minutes of reading (this doc + the closeout + the architecture diagram spec).*
