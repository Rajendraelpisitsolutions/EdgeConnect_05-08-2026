<!--
File:        docs/marketing/website-messaging-architecture-v1.md
Purpose:     Messaging architecture for the Elpis website — what each page
             says, how the messages chain across the site, and how visitors
             move from landing to conversion. NOT a visual / UX spec; the
             designer interprets visuals. NOT site code; this is the brief
             a web designer + copywriter work from.
Audience:    Web designer, marketing copywriter, the user (signing off on
             page-by-page messaging before any HTML is written).
Format:      Markdown architecture doc. Page-by-page sections with:
               - purpose
               - primary audience
               - hero messaging (above the fold)
               - section structure
               - CTAs and conversion paths
Version:     v1 (first draft)
Date:        2026-05-24

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v3.md (canonical)
  - docs/marketing/pitch-deck-outline-v1.md (parallel sales narrative)
  - docs/marketing/architecture-diagram-spec-v2.md (visual asset spec)

Locked-truth sources: see datasheet v3 header for the full list. Every
page claim traces back to those sources. Do not add claims without a
matching entry.
-->

# Elpis Website — Messaging Architecture v1

**What each page says, who it speaks to, and how visitors move from landing to conversation.**

This document defines the *messaging* layer of the Elpis website. Visuals, layout, animations, and code are the designer's and developer's interpretation. The job here is to lock down what the site **communicates** before anyone builds it.

The site evolves the Elpis online presence from "service company brochureware" into "industrial platform company positioning." Every page should reinforce that the product is the **Elpis Industrial Intelligence Platform** (EdgeConnect + EREMOS V2), not a portfolio of disconnected offerings.

---

## 1. Goals of the website

In priority order:

1. **Convert qualified visitors into sales conversations.** Every page leads to one of three CTAs: *book a scoping call*, *download the datasheet*, or *talk to sales*.
2. **Establish category identity.** Visitors should leave with a clear mental model: "Elpis runs an industrial platform that connects machines at the edge and turns the data into operational intelligence."
3. **Build technical credibility.** Industrial IT and SCADA buyers should see protocol names, architecture clarity, and OT realism — not buzzwords.
4. **Filter out the wrong-fit visitors.** "Designed for" content should let mismatched prospects qualify themselves out gracefully.
5. **Support SEO discovery.** Real protocol names, real use cases, and real outcomes serve both human readers and search.

---

## 2. Site map (information architecture)

```
/                            Homepage (platform pitch)
├── /platform                Platform overview (EdgeConnect + EREMOS V2 together)
├── /edgeconnect             Product page — EdgeConnect (edge runtime)
├── /eremos                  Product page — EREMOS V2 (intelligence layer)
├── /solutions               Solutions index
│   ├── /solutions/cnc-machining
│   ├── /solutions/automotive-parts
│   ├── /solutions/brownfield-modernization
│   ├── /solutions/oem-machine-monitoring
│   └── /solutions/multi-site-operations
├── /customers               Case studies (placeholder until real customers ship)
├── /pricing                 Pricing approach (no numbers in v1)
├── /resources               Datasheet PDF, pitch deck, blog (future)
├── /company                 About + team
└── /contact                 Demo / scoping call form
```

**Top navigation (5 items max):** *Platform · Products · Solutions · Resources · Contact*

**Footer:** secondary nav (Company, Customers, Pricing, Privacy, Terms), social, contact, copyright.

---

## 3. Voice and tone (reaffirmation)

Inherits directly from datasheet v3:

- **Confident, technical, operational.** Real protocol names. Real outcomes. Real architecture.
- **Outcomes first, architecture second.** Lead with what the buyer gets, follow with how it works.
- **No buzzwords.** Banned vocabulary: "revolutionary," "game-changing," "AI-powered," "Industry 4.0," "smart factory," "digital transformation synergy," "next-gen disruptive," "transform your operations."
- **No fabrication.** No customer names, no ROI percentages, no testimonials until the user supplies them.
- **Constrained-AI positioning.** When AI features are mentioned, lean into the constraint ("AI proposes; humans decide; local-LLM-capable") — never into the hype.

---

## 4. Homepage (`/`)

**Purpose:** Land the visitor in the platform's category in 8 seconds; offer three paths to dig deeper.

**Primary audience:** First-touch — could be plant manager, industrial IT, SI partner, or executive. Multiple audience tiers funnel here.

### 4.1 Hero (above the fold)

**Headline:** *Unified industrial connectivity and operational intelligence for modern manufacturing.*

**Subhead:** *Connect CNCs, Modbus PLCs, and instrumentation into one real-time operational platform. Measure OEE on signals collected directly from the controller. From the spindle to the dashboard, on one foundation.*

**Primary CTA:** *Book a scoping call* (button, accent color)
**Secondary CTA:** *Download the datasheet* (text link)

**Trust strip below the fold:** *Built for plants running Fanuc · Brother · Mazak · Siemens · Modbus TCP*

### 4.2 Three-pillar section

Three side-by-side blocks. Each block: icon + headline + 2-line description + link to the relevant subpage.

| Pillar | Headline | Description | Link |
|---|---|---|---|
| **Edge connectivity** | *Speak every controller* | One service for FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP. No per-machine custom scripting. | `/edgeconnect` |
| **Operational intelligence** | *Trust your OEE number* | Multi-tenant analytics that turn edge data into OEE, alarms, incidents, and shift reports. | `/eremos` |
| **Edge-first by design** | *Run for years on a small box* | Offline-capable, store-and-forward, signed offline licensing. Air-gapped factories are first-class. | `/platform` |

### 4.3 Architecture at a glance

Embedded SVG (the v2-spec diagram) with the caption from the datasheet:

*One EdgeConnect deploys at each plant. One EREMOS V2 tenant aggregates many sites. Standard MQTT and OPC UA make the integration interoperable with whatever else you run.*

CTA below: *See how it fits your plant → Book a scoping call*

### 4.4 Designed-for strip

Mirror the datasheet's "Designed for" section: five customer types, one line each, each linking to the matching solution page.

### 4.5 Outcomes strip

Six outcomes from the datasheet, rendered as a scannable bullet grid (icon + bolded outcome + 1-line support):

- Cut unplanned downtime
- Trust your OEE number
- Modernize legacy controllers
- See your whole fleet
- Keep sensitive data where it belongs
- Pass your audit

### 4.6 "Replace spreadsheet operations" section

Pulled verbatim from the datasheet. This is the strongest commercial angle and earns a dedicated section.

### 4.7 Use case quick-cards

Five cards (one per solution page) — each: vertical name, one-sentence story, "Read the solution brief" link.

### 4.8 Customer logos strip (placeholder)

Grayed-out placeholder bar: *Customer logos to be added.* Mark as placeholder until the user supplies real ones.

### 4.9 Final CTA block

Centered: *Bring us a representative plant — a controller mix, a target broker, an OEE definition. We will scope a proof of value against it.* Button: *Book a scoping call*.

---

## 5. Platform overview (`/platform`)

**Purpose:** Deep dive on the joint platform for visitors who clicked "Platform" from the nav. Tells the EdgeConnect + EREMOS V2 story without forcing them to read two separate product pages.

**Primary audience:** Industrial IT, SCADA engineers, technical evaluators who want to understand how the pieces fit together.

### 5.1 Hero

**Headline:** *Two products. One platform. One operational view.*

**Subhead:** *EdgeConnect collects from every controller on your floor. EREMOS V2 turns that data into OEE, alarms, incident workflows, and shift reports. Standard MQTT and OPC UA tie them together.*

### 5.2 The two-product story

Two-column layout: left = EdgeConnect, right = EREMOS V2. Each column gets the bolded-bullet treatment from the datasheet (the edge connectivity bullets on one side, the operational intelligence bullets on the other).

### 5.3 Architecture (full SVG, captioned)

Same diagram as homepage but larger. Caption + 3-line walk-through ("edge collects · integration carries · intelligence aggregates · consumers consume").

### 5.4 Multi-site model

A dedicated section explaining the per-gateway / aggregating-tenant architecture. Visual emphasis on the "one EdgeConnect per plant, one EREMOS V2 across many" pattern.

### 5.5 Why Elpis (8 differentiators)

Pulled from the datasheet's "Why Elpis" section, outcome-first phrasing.

### 5.6 Deploy incrementally

Pulled from the datasheet. Defuses the disruption fear.

### 5.7 CTA

*Talk to an engineer · Download the datasheet · See the pitch deck*

---

## 6. Product page — EdgeConnect (`/edgeconnect`)

**Purpose:** Technical deep dive on the edge runtime. The page a SCADA engineer or industrial IT lead lands on after searching "FOCAS2 gateway" or "MT-LINKi MQTT."

**Primary audience:** Industrial IT, SCADA engineer, system integrator evaluator. Plant managers should still find it readable but the depth is for technical buyers.

### 6.1 Hero

**Headline:** *EdgeConnect — Protocol-agnostic edge runtime for industrial data*

**Subhead:** *FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP — one service speaks them all. Normalizes to a canonical model. Delivers to MQTT or OPC UA Server. Runs offline. License-activated per protocol.*

### 6.2 Connectivity coverage

The connectivity table from the datasheet (CNC controllers / PLC + instrumentation / Messaging / Enterprise integration), split by category.

### 6.3 Capabilities

Bolded-bullet list from the datasheet's "Edge connectivity" section:

- Native protocol coverage
- No lost production data (store-and-forward)
- Faults isolated, not contagious (per-adapter isolation)
- Three-way diagnostics
- Connectivity Studio
- Safe configuration (draft → validate → apply → rollback)
- Auditable changes (hash-chained config history)

### 6.4 Architecture detail

Embedded SVG (the architecture diagram) zoomed into the edge portion. Or a dedicated edge-layer diagram if the designer prefers.

### 6.5 Canonical CNC vocabulary

A callout explaining that every source delivers tags using the shared canonical vocabulary — `running`, `spindle_rpm`, `feed_rate`, `parts_count`, `cycle_time`, axis positions. The same dashboard layout works across vendors.

### 6.6 What runs where

Brief technical note: Windows service today, Linux on the roadmap, runs offline at the edge.

### 6.7 Licensing approach

Brief: Starter / Professional / Enterprise editions with optional protocol modules. Link to `/pricing`.

### 6.8 CTA

*Book a scoping call · Talk to an engineer*

---

## 7. Product page — EREMOS V2 (`/eremos`)

**Purpose:** Technical deep dive on the intelligence platform. The page an Ops VP or analytics lead lands on after searching "OEE platform multi-tenant" or "industrial dashboard MQTT."

**Primary audience:** Ops VP, analytics lead, plant manager evaluating an analytics layer. Less protocol-heavy than the EdgeConnect page; more outcomes-heavy.

### 7.1 Hero

**Headline:** *EREMOS V2 — Multi-tenant analytics for industrial operations*

**Subhead:** *Turn machine data into OEE, alarms, incident workflows, reports, and dashboards. Per-tenant isolation. Edge-collected signals. From any data provider, not just Elpis.*

### 7.2 Capabilities

Bolded-bullet list from the datasheet's "Operational intelligence" section:

- Real industrial asset model (PLANT → AREA → LINE → EQUIPMENT → SUB_EQUIPMENT)
- OEE you can audit (Segment-based)
- Alarms and incidents tracked
- Alerts on your channels
- Reports the team will actually read (shift reports, OEE summaries, downtime breakdowns, tool-life trends — PDF and Excel)
- Tool life tracking
- Multi-tenant by design
- Dashboards that scale to mixed fleets (panes split by device class)

### 7.3 How EREMOS V2 receives data

Brief: subscribes to MQTT. EdgeConnect is the reference data provider; third-party providers also supported. Each tenant's provider is registered as a Device.

### 7.4 Where it runs

Brief: customer data center, private cloud, or as a managed service.

### 7.5 Licensing approach

Brief: same edition tiers as EdgeConnect, linked to platform tier. Link to `/pricing`.

### 7.6 CTA

*Book a demo · Talk to an analyst*

---

## 8. Solutions pages — template + five stubs

**Purpose:** SEO-and-conversion landing pages for specific buyer profiles. Each page is the datasheet's narrative re-pointed at a single vertical or use case.

### 8.1 Solutions page template

Every solution page follows this structure:

1. **Hero** — *<Vertical name>: <one-line outcome>* + *<2-line subhead positioning Elpis for this buyer>*
2. **The challenge** — 2–3 paragraphs describing the problem this vertical faces
3. **The Elpis approach** — how the platform addresses it, with specific protocol / capability calls
4. **What's included** — feature/capability bullets relevant to this vertical
5. **Customer outcomes** — outcome bullets reframed for this vertical (e.g., for OEM monitoring: "remote diagnostics," "no truck rolls for tag changes")
6. **Architecture (vertical-relevant subset)** — embedded SVG or simplified version
7. **CTA** — *Book a scoping call for your <vertical>*

### 8.2 The five solution stubs (v1 = stubs, full copy in a follow-up deliverable)

| URL | Vertical | Hero outcome |
|---|---|---|
| `/solutions/cnc-machining` | Multi-vendor CNC machining | *One operational view across every Fanuc, Brother, and Mazak on your floor.* |
| `/solutions/automotive-parts` | Automotive parts and precision machining | *OEE accountability across mixed-vendor production cells.* |
| `/solutions/brownfield-modernization` | Brownfield modernization | *Modernize the data layer without replacing the controllers.* |
| `/solutions/oem-machine-monitoring` | OEM machine monitoring | *Ship connected equipment. Diagnose remotely. No truck rolls for tag changes.* |
| `/solutions/multi-site-operations` | Multi-site industrial operations | *One platform, many sites, consistent KPIs.* |

Full copy for each is a future deliverable (see §17 — out of scope for v1).

---

## 9. Customers (`/customers`)

**Purpose:** Case studies and customer logos when they exist. Placeholder content until then.

**v1 state:** A single placeholder page with the message:

> *We work with manufacturing plants and OEMs across [geography]. Customer case studies are in production — contact us to be considered for an early-customer feature, or speak to a reference customer under NDA.*

CTA: *Talk to a reference customer*

**Do NOT fabricate customer logos, names, or testimonials.** Wait for the user to supply real ones.

---

## 10. Pricing (`/pricing`)

**Purpose:** Communicate the licensing model without exposing numbers.

**Approach:**

Headline: *Licensed for the way industrial buyers actually consume software.*

Three columns for the editions:

- **Starter** — *Small machine monitoring deployments*
- **Professional** — *Multi-line / plant operations*
- **Enterprise** — *Multi-site industrial intelligence platform*

Each column lists what's included (high-level capability tiers, no module-level detail) with a checkmark or dot pattern.

Below the columns: *Available with optional industrial connectivity modules — FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, MQTT, OPC UA Server.*

Bottom of page: *Contact us for licensing details, edition feature lists, and deployment-scale pricing tailored to your fleet.* + form.

**No numeric prices in v1.** When the user is ready to publish prices, this page evolves.

---

## 11. Resources (`/resources`)

**Purpose:** Downloads and content marketing destination.

**v1 contents:**

- **Datasheet PDF** — print-ready version derived from datasheet v3
- **Pitch deck PDF** — derived from the pitch deck outline once designed
- **Architecture diagram** — downloadable SVG/PNG
- **Blog** — placeholder section, empty in v1, intended to receive deep-dive technical posts later (e.g., "How EdgeConnect handles FOCAS2 alarm semantics")

Each downloadable asset is gated behind a lightweight form (email + company) — optional in v1 if Elpis prefers no friction.

---

## 12. Company (`/company`)

**Purpose:** Trust signals about who's behind the product.

Sections:

- **About Elpis** — 2-paragraph company story (user supplies)
- **Team** — leadership bios (user supplies; placeholder if not ready)
- **Engineering principles** — pull a tight version of the platform principles document if user approves making it public-facing (P1 runtime tap, P3 security spec-first, P4 explainability, etc., reframed for a public audience)
- **Open positions** — if Elpis is hiring

**v1 state:** placeholder for About and Team until the user supplies content.

---

## 13. Contact (`/contact`)

**Purpose:** Conversion endpoint.

**Form fields (minimum):**

- Name
- Company
- Role (dropdown: plant manager / industrial IT / system integrator / OEM / other)
- Plant location / geography (optional)
- Brief description of your environment (free text)
- Message

**Form CTAs:**

- *Book a scoping call* (primary)
- *Just send a question* (secondary)

**Email destinations:** to be configured by the user.

Below the form: direct contact details (sales email, support email, phone).

---

## 14. Footer (global)

**Footer columns:**

1. **Product** — Platform · EdgeConnect · EREMOS V2 · Pricing
2. **Solutions** — CNC machining · Automotive parts · Brownfield · OEM · Multi-site
3. **Company** — About · Team · Careers · Contact
4. **Resources** — Datasheet · Pitch deck · Architecture diagram · Blog
5. **Legal** — Privacy · Terms · Security

**Footer bottom:** wordmark, social links, copyright, country/jurisdiction.

---

## 15. Navigation (global)

**Top nav (max 5):** *Platform · Products · Solutions · Resources · Contact*

*Products* and *Solutions* are dropdown menus:

- **Products:** EdgeConnect / EREMOS V2 / Pricing
- **Solutions:** the 5 vertical pages

A persistent **Book a scoping call** button sits in the top-right of the nav on every page.

---

## 16. SEO patterns

**Page title pattern:** *<Page focus> · Elpis Industrial Intelligence Platform*

Example: *EdgeConnect — Protocol-agnostic edge runtime · Elpis Industrial Intelligence Platform*

**Meta description pattern:** 1–2 sentences from the page's hero subhead, tuned for ~150 characters.

**Primary keywords (initial set, refine after analytics data):**

- Industrial edge integration platform
- Multi-vendor CNC data collection
- FOCAS2 MQTT gateway
- MT-LINKi data collection
- Brother HTTP CNC integration
- Modbus TCP industrial gateway
- OPC UA server CNC
- OEE platform multi-tenant
- Industrial alarm management
- Edge-first industrial intelligence

**URL pattern:** lowercase, hyphenated, descriptive. No query strings in canonical URLs.

**Structured data:** schema.org `SoftwareApplication` on product pages; `Organization` on company page; `FAQPage` on relevant solutions pages once FAQ content exists.

**OG / social cards:** branded preview images for every page (designer task).

---

## 17. Out of scope for v1

- **Full copy for solution pages** (§8 has stubs only — full per-vertical copy is a follow-up deliverable)
- **Real customer case studies** — placeholder only until customers ship
- **Pricing numbers** — model only, no prices
- **Blog content** — section structure in place, posts to follow
- **Localization** (Japanese, German, Mandarin) — English-only in v1
- **Investor / press section** — defer until needed
- **Documentation / developer portal** — separate subdomain, not part of this messaging architecture
- **Component-level design system** — designer's job
- **HTML / CSS / framework choice** — developer's job

---

## 18. Conversion path (how messaging chains across pages)

The site should make this path easy:

1. **Visitor lands** on homepage, a solution page, or a product page (depending on entry point — search, ad, referral)
2. **Visitor identifies fit** via "Designed for" content on whatever page they entered
3. **Visitor explores** — typically clicks into one of: relevant solution page, product page, architecture diagram
4. **Visitor converts** via one of three CTAs: *book a scoping call*, *download the datasheet*, *talk to sales*

Every page must:

- Contain at least one primary CTA above the fold
- Contain at least one secondary CTA below the fold
- Link to at least one related page (avoid dead-end leaves)
- Reinforce the "Industrial Intelligence Platform" category positioning

---

## 19. Sign-off checklist

Before any page is built:

- [ ] Homepage hero approved by user
- [ ] All page titles and URLs approved
- [ ] Voice and tone reaffirmed against the locked-truth sources
- [ ] No fabricated customer names, ROI percentages, or testimonials
- [ ] Every feature claim traces back to the locked-truth sources listed in the file header
- [ ] Designer briefed on the architecture diagram (use the SVG from `architecture-diagram-spec-v2.md`)
- [ ] SEO keywords reviewed by user and refined for actual Elpis priority verticals
- [ ] Contact form destinations and pricing-page approach approved by user

---

*Website Messaging Architecture — v1, 2026-05-24. Derived from datasheet v3.*
