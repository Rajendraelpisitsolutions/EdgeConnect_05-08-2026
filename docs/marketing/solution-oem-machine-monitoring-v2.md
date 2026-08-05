<!--
File:        docs/marketing/solution-oem-machine-monitoring-v2.md
Purpose:     Full copy for the /solutions/oem-machine-monitoring page on the
             Elpis website. The fourth vertical solution page.
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy.
Version:     v2 (post-review — two micro-refinements)
Date:        2026-05-24

Changes from v1 (2 readability trims only; no structural revision):
  - §4: tightened "Offline-capable" bullet body and "OPC UA Server
    (optional)" bullet body. ~25-30% prose trim each. Substance preserved.
  - §5: compressed the air-gap question answer from ~41 words to ~30 words.
    Improves Q&A section rhythm without losing the operational meaning.

Per ChatGPT v1 review: v2 is final — freeze and hand to design. No
structural changes, no new sections, no strategic rewrites — strategic
positioning was already correct in v1.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §8 (solution-page template)
  - docs/marketing/solution-cnc-machining-v2.md (template source)
  - docs/marketing/security-page-copy-v2.md (trust positioning highly relevant for OEM-customer dynamics)
  - shared-knowledge/contracts/eremos-per-tag-mqtt.md (per-gateway identity contract)
  - CLAUDE.md §1, §3 (locked architectural decisions — especially #5, #6, #19)
-->

# OEM Machine Monitoring — Solution Page Copy v2 (final)

**Page URL:** `/solutions/oem-machine-monitoring`
**Primary audience:** OEM product manager, OEM service operations director, OEM head of installed-base / customer success
**Secondary audience:** OEM engineering leader thinking about field-telemetry feedback into product development

---

## §1 Hero

### Copy

> ### Ship connected equipment. Diagnose remotely. Cut truck rolls.
>
> EdgeConnect deploys with your machine. EREMOS V2 aggregates your installed base. Native FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, OPC UA Server — already integrated. Your customer controls their data; you get the service visibility you need.
>
> [ Book a scoping call for your installed base ]   ·   Download the datasheet

### Visual notes

- **Full-bleed hero image:** an OEM-built machine on a customer's floor — a Brother CNC or specialized industrial machine, mid-operation, clearly in service. The visual cue is *"this is your product, working in the wild"*, not generic factory imagery.
- **Headline at display weight.** Three short imperative clauses for rhythm and immediate value.
- **Trust strip under hero:** *Connected on FOCAS2 · MT-LINKi · MTConnect · Brother HTTP · Modbus TCP · OPC UA Server. Customer-controlled. OEM-aware. Field-ready.*

### Reader-effect notes

- The OEM product manager should feel *"this is the connectivity stack we'd otherwise be building ourselves"* within 5 seconds.
- "Your customer controls their data; you get the service visibility you need" is the central trust promise — it tells OEMs they don't have to become adversaries of their own customers to deliver connected equipment.
- The protocol list signals that the platform handles whatever controllers the OEM specs — no per-machine integration project required.

---

## §2 The challenge

### Copy

> ### The OEM service reality
>
> Your service organization is blind until a customer calls. By the time the call comes, the machine has already been down. Your engineer arrives at the customer's site — sometimes hours later, sometimes overnight — to diagnose a fault that, on a connected machine, could have been identified before the customer noticed.
>
> Every dispatch costs hundreds of dollars in engineer time, travel, parts inventory, and customer goodwill. Every dispatch that turns out to be a remote-diagnosable issue is pure margin loss. Meanwhile your product engineering team is starved for field data — they're improving the next-generation machine on six-month-old anecdotes from your service team's whiteboard.
>
> The instinct is to build a connected-equipment platform yourself: an embedded gateway, a cloud back-end, a mobile-app dashboard, a service ticketing integration. Then you meet the customer's IT department. Some customers won't allow always-on connectivity. Some customers require complete data sovereignty. Some customers don't want your machine on their network at all. Your monitoring platform becomes the reason the deal stalls. The connectivity story that was supposed to differentiate your equipment becomes the friction that kills its sale.

### Visual notes

- **Three narrative paragraphs.** No bullet lists.
- **Optional pull-quote in the margin (desktop):** *"Your monitoring platform becomes the reason the deal stalls."*
- **Subdued visual treatment.** Empathy section — quiet, deliberate.

### Reader-effect notes

- The reader (OEM product manager) should feel *"this person understands the friction between our service strategy and our customers' IT departments."*
- The "build it yourself" → "meets customer IT" arc is doing real recognition work — most OEMs who've tried to build their own connected-equipment platform have hit this wall.
- The closing line is the unique OEM-specific pain that doesn't appear on any other solution page on the site.

---

## §3 The Elpis approach

### Copy

> ### How Elpis solves OEM machine monitoring
>
> **EdgeConnect ships with your machine.** One service running on a small box inside your equipment — or on a customer-supplied box adjacent to it — polls your controller over its native protocol. The tag map you author once carries across every machine you ship. Same software image, same canonical vocabulary, same deployment model — installed-base-wide.
>
> **The customer controls what flows back to you.** EdgeConnect's route-based architecture lets the customer configure which data goes where. Service-relevant signals (alarm state, run hours, fault codes, tool-life telemetry) can route to your installed-base view. Operationally sensitive data (parts programs, production volumes, customer-specific configurations) can stay local. No always-on remote-access tunnel. No data exfiltration. No customer-IT escalation.
>
> **You see your installed base; the customer sees their machine.** EREMOS V2 aggregates the service-relevant telemetry across every machine you've shipped, organized by customer site. Each customer can also run EREMOS V2 themselves if they want their own operational view — same platform, different tenant. Co-existence by design.
>
> **Per-customer fleet identity from day one.** Each shipped machine carries a stable gateway UUID. Customer/site binding is established at installation. Acquisitions, name changes, plant transfers — the identity model survives all of them. Warranty, SLA, and service-history tracking work because the identity is permanent.
>
> **Diagnose before the customer calls.** With service-relevant telemetry flowing back, your service team sees alarm patterns, fault progressions, and degraded operation across the installed base. A tool-life metric trending toward failure on three customer machines becomes a proactive service campaign, not three separate emergency dispatches.

### Visual notes

- **Five bolded-lead paragraphs.** Each lead is a commitment that resolves an OEM-specific tension.
- **The "customer controls what flows back to you" paragraph should render with slight visual prominence** — it's the single most important trust message on the page for the OEM-customer relationship.
- **Optional small inline icons** next to each bolded lead.

### Reader-effect notes

- The reader (OEM product manager) should feel *"every concern my customer would raise has been addressed architecturally."*
- The "customer controls what flows back" framing is doing enormous trust work — it tells OEMs they can deliver connected equipment without becoming adversaries of customer IT.
- The "diagnose before the customer calls" closing reframes the value proposition from "remote monitoring" (defensive) to "proactive service" (offensive — competitive differentiator).

---

## §4 What's included

### Copy

> ### What's included for OEM machine monitoring
>
> **For your machine (EdgeConnect):**
>
> - **Native protocol coverage for whatever controllers you spec** — FOCAS2 (every Fanuc generation), MT-LINKi, MTConnect, Brother HTTP, Modbus TCP. Mix and match across product lines.
> - **One tag map, deployed across your installed base** — authored once for your machine, replicated across every unit you ship. Updates to the tag map flow to existing machines via standard configuration deployment.
> - **Route-based data control** — customer configures which signals route where. Service telemetry to OEM; operational data stays local; nothing leaves the customer's network without an explicit route.
> - **Store-and-forward built in** — customer network outages don't lose machine telemetry. Buffered locally, replayed on reconnect.
> - **Per-gateway UUID and customer/site binding** — established at first start. Each shipped unit carries permanent identity for warranty, SLA, and service-history attribution.
> - **Hash-chained audit log** — tamper-evident record of every configuration change on the machine. Useful for warranty claims and regulated-industry deployments.
> - **OPC UA Server (optional)** — exposes machine data natively to the customer's SCADA / MES if they want it. Co-existence by design.
> - **Offline-capable** — no cloud, internet, or always-on connectivity required for the machine to operate.
>
> **For your service organization (EREMOS V2):**
>
> - **Installed-base view** — every shipped machine visible in one operational dashboard, organized by customer site.
> - **Per-customer fleet drill-down** — view a specific customer's machines, their alarm history, their service patterns.
> - **Persistent alarm tracking with incident grouping** — proactive identification of fault patterns across the installed base.
> - **Tool-life and consumable telemetry** — flag wear before failure, drive proactive service campaigns.
> - **Multi-tenant by design** — your installed-base view is your tenant; your customer's own view (if they run EREMOS V2 themselves) is theirs. No data leakage between tenants.
> - **Service-history reporting** — exportable per-customer or per-machine for SLA reviews, warranty claims, and contract renewals.

### Visual notes

- **Two clearly separated sub-sections** — "For your machine" and "For your service organization" — with their own headers.
- **The split mirrors the OEM-customer architectural boundary** — what ships in the machine versus what runs in the OEM's service environment.
- **Bolded leads + plain-body explanations** for each capability.
- **Higher density section** — readers scan, not read.

### Reader-effect notes

- The reader should feel *"this covers every connected-equipment capability I'd need to build myself."*
- The split between "your machine" and "your service organization" is doing important framing — it shows the OEM exactly which capabilities live where and who controls each.

---

## §5 Common OEM questions the platform answers

### Copy

> ### Questions OEM service and product teams raise
>
> If you've considered building a connected-equipment offering and stopped because the customer-relationship implications got too complicated, here's what the platform changes:
>
> - **"Can our customers say no to monitoring?"** Yes. The customer controls the route configuration; if they don't want service telemetry flowing to you, no data flows. The platform is designed to respect customer choice, not coerce it. Trust is the long-term commercial position.
> - **"Can our customer use the same platform for their own operational view?"** Yes. The customer can run EREMOS V2 themselves with their own tenant, configure their own dashboards, and route some of the same machine data to both their own view and yours. One platform, two tenants, separate data control.
> - **"What about customers with strict no-cloud or air-gap policies?"** Supported. EdgeConnect runs offline. Telemetry exports on the customer's schedule via approved channels — scheduled MQTT bursts, manual exports — instead of always-on connections. Strict data-sovereignty customers remain addressable.
> - **"How do we handle a customer who buys a machine and then refuses connectivity?"** The machine works either way. Connectivity is a layered capability, not a precondition for operation. If the customer changes their mind later, connectivity activates without a service call.
> - **"Can we white-label the operator-facing parts for our customers?"** Co-branding options are available for OEM partnerships at appropriate scale. Talk to us about your product packaging strategy.
> - **"How does pricing work for an OEM shipping hundreds or thousands of machines?"** OEM pricing is structured differently from per-plant deployments. Contact us for OEM licensing terms; we can scope it against your product economics.
> - **"What about feeding service data back to product engineering?"** EREMOS V2's installed-base view includes alarm patterns, run hours, and operational telemetry that product engineering can analyze for next-generation product decisions. The field-data feedback loop your engineering team has been asking for becomes continuous.

### Visual notes

- **Each question as a bold pull-quote**, answer in regular body weight underneath.
- **Generous line spacing.** Scan-bait, not paragraphs.
- **Optional small icon** next to each question.

### Reader-effect notes

- This section is doing the heaviest customer-relationship-concern defusion on the page.
- The "Can our customers say no?" answer is the most important single message — it tells OEMs the platform makes them better partners to their customers, not worse.
- The product-engineering feedback question is the one that opens additional internal stakeholders (R&D directors) to the conversation.

---

## §6 Customer outcomes

### Copy

> ### What OEMs see when they deploy
>
> - **Cut truck rolls on remote-diagnosable issues** — service team identifies the problem before the customer calls; some dispatches become phone-resolved
> - **Diagnose before the customer notices** — alarm patterns trigger proactive service campaigns, not reactive emergency dispatches
> - **Field data flows continuously to product engineering** — next-generation product decisions backed by real installed-base data, not six-month-old service anecdotes
> - **Warranty disputes resolve on data, not memory** — service history, run hours, and operational telemetry available per-machine
> - **Connected-equipment becomes a differentiated SKU** — your customer RFP responses can promise connectivity that doesn't violate the customer's IT policy
> - **Customer relationships strengthen, not weaken** — the platform respects customer data sovereignty; you become the OEM that didn't try to force always-on access

### Visual notes

- **Bulleted outcome list, two-column on desktop, single-column on mobile.**
- **Bolded outcome lead + light-weight supporting clause** — same pattern as homepage.
- **No icons** — typography carries the rhythm.

### Reader-effect notes

- Each outcome maps to a real OEM pain.
- The closing bullet is doing strategic work — it positions Elpis-based connectivity as a customer-relationship asset, not a customer-relationship liability.

---

## §7 What a typical OEM engagement looks like

### Copy

> ### How OEMs typically roll this out
>
> **Phase 1 — Embed in one machine model.** Pick the machine where remote service would have the biggest economic payoff (high-volume product, expensive truck rolls, or a controller you already have telemetry from internally). EdgeConnect installed alongside that model. Tag map authored for the protocol your machine speaks. Service-relevant telemetry identified together with your service organization. Engineering test units validated.
>
> **Phase 2 — Pilot at one or two customer sites.** Ship the connected model to a friendly customer or two. EREMOS V2 installed in your service environment. Real installed-base telemetry flowing. Customer onboarding playbook refined against live deployments. Customer feedback gathered on the data-control model.
>
> **Phase 3 — Roll into new shipments.** Every new unit of the connected model ships with EdgeConnect embedded. Customer onboarding includes the data-control conversation as part of installation. Service team's installed-base dashboard becomes routine.
>
> **Ongoing.** Existing units can be retrofitted as customers opt in (the controller protocols and EdgeConnect installation work on already-deployed machines). New product lines add to the OEM's installed-base view through the same architecture. Product engineering's field-data feedback loop becomes a continuous source of next-generation product decisions.

### Visual notes

- **Four-step horizontal timeline** on desktop; vertical stack on mobile.
- **Each step:** phase label, headline, 3-4 line description.
- **Subtle accent-color progress markers** between steps.
- **The "Ongoing" step** could visually loop or extend.

### Reader-effect notes

- The reader (OEM product manager planning a connected-equipment program) should feel *"this is a realistic rollout, not a vendor fantasy."*
- "Pick the machine where remote service would have the biggest economic payoff" is the right framing — OEMs already think about ROI per product line.
- The "Phase 2 — Pilot at one or two customer sites" wording is honest — pilots take time because customer IT teams are involved, not because the technology is slow.

---

## §8 Architecture for OEM machine monitoring

### Copy

> ### How it fits together for connected equipment
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, structured for OEM context: multiple customer sites, each containing one or more OEM-supplied machines (each running EdgeConnect). Customer-controlled routing splits between "stays at customer site" and "flows to OEM service environment." The OEM environment shows EREMOS V2 with installed-base view; optionally a customer-side EREMOS V2 tenant if the customer also runs the platform. ]
>
> *Each machine you ship runs EdgeConnect locally. Customer-controlled routes determine what flows back to your service organization — alarm state, run hours, tool-life telemetry — without compromising the customer's operational data. EREMOS V2 in your environment aggregates the installed base. The customer can run EREMOS V2 themselves for their own view, in a separate tenant. Trust by design.*

### Visual notes

- **OEM-specific variant of the master architecture diagram.** The most important visual distinction from other solution-page diagrams: explicit "customer site boundary" lines, with the OEM-side environment visually distinct from customer-side.
- **Customer-controlled route arrows** should be visually emphasized — the diagram should make clear that the customer's decisions, not the OEM's, gate the data flow.
- **Caption in italic** beneath the diagram.

### Reader-effect notes

- The diagram should communicate *"the OEM-customer architectural boundary is respected"* — that's the unique trust message of the OEM page.
- This is the slide an OEM product manager will use internally to explain the connected-equipment strategy to their service org and to customer IT departments.

---

## §9 Final CTA

### Copy

> ### Bring us your installed base.
>
> Tell us about your equipment — how many machines you've shipped, what controllers you spec, what your service organization needs to see. We will scope an embedded deployment for your next product release and a path to retrofitting existing units. No multi-year platform commitment required to prove the connectivity stack works.
>
> [ Book a scoping call for your installed base ]   ·   Or download the datasheet

### Visual notes

- **Centered, generous whitespace.** Same pacing as homepage and other solution-page final CTAs.
- **Primary CTA button in accent color.** Secondary as text link.
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- "Bring us your installed base" localizes the homepage CTA pattern to the OEM audience.
- *"No multi-year platform commitment required to prove the connectivity stack works"* is doing pre-emptive objection-handling against OEM caution about new infrastructure investments.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 60 | Headline + subhead + CTAs |
| §2 The challenge | 260 | Three narrative paragraphs |
| §3 Elpis approach | 350 | Five bolded-lead paragraphs |
| §4 What's included | 320 | Two sub-sections, tightened bullet prose |
| §5 Common questions | 270 | Seven Q&A pairs, tighter air-gap answer |
| §6 Customer outcomes | 110 | Six bullets |
| §7 Typical engagement | 250 | Four-phase timeline |
| §8 Architecture | 100 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,790 words** | Plus diagram + visual elements |

Slightly tighter than v1 (~1,820 words). The §4 bullet trims and §5 air-gap answer compression account for the difference.

---

## Visual / pacing guidance summary

- **Pacing matches other solution pages:** hero anchored → challenge intimate → approach explanatory → features dense → questions scannable → outcomes scannable → engagement phased → architecture visual → CTA confident
- **Imagery:** OEM-product-aware. Real OEM equipment in customer environments — Brother CNCs, specialized industrial machines, OEM service engineers on-site. Avoid generic "Industry 4.0 connected factory" imagery.
- **Palette:** same dark premium industrial as homepage and other solution pages.
- **Mobile:** every section tested at 375px wide. The phase timeline in §7 stacks vertically; the §4 "your machine / your service organization" split sub-sections collapse cleanly.

---

## What's out of scope for v2

- **Real OEM customer case studies** — placeholder language only until OEM customers go public
- **Specific truck-roll cost-savings percentages claimed as "typical"** — never; the calculator handles that
- **Direct comparisons to OEM connected-equipment vendor platforms** (Sight Machine, MachineMetrics, etc.) — that's the objection-handling guide's job
- **OEM pricing detail** — handled separately in OEM partner conversations; page routes to scoping call
- **White-label / co-branding contractual terms** — surfaced as a benefit; specifics belong in OEM partner agreements
- **Specific product-engineering feedback loop integration with PLM / CAD systems** — future deliverable if demand emerges

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4, homepage copy v2, CNC solution page v2, brownfield page v2, security page v2, and multi-site page v2 for voice consistency
- [ ] No fabricated OEM customer names or ROI claims
- [ ] Customer-data-control language reviewed against the security page v2 trust framing
- [ ] Per-gateway identity language traces to `shared-knowledge/contracts/eremos-per-tag-mqtt.md` and CLAUDE.md §3 lock #19
- [ ] Architecture diagram OEM variant approved (designer brief from `architecture-diagram-spec-v2.md`) — the customer/OEM boundary visual treatment is unique to this page
- [ ] §5 OEM questions reviewed by Elpis sales lead — language must reflect actual OEM-product-manager conversations
- [ ] §7 engagement timeline reviewed against realistic OEM-pilot pace
- [ ] CTA destinations confirmed (scoping-call form + datasheet download)
- [ ] Page tested at 375px mobile width
- [ ] OEM pricing-conversation routing confirmed (page does not expose pricing detail)

---

*Solution page — OEM Machine Monitoring, v2 (final), 2026-05-24. Per ChatGPT v1 review, no v3 planned. One solution page remaining: precision manufacturing.*
