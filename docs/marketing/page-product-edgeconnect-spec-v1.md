<!--
File:        docs/marketing/page-product-edgeconnect-spec-v1.md
Purpose:     Page spec for /edgeconnect — the PRODUCT detail page for
             EdgeConnect (Pillar 1, Connectivity & Edge software product).
             SHAPE-SETTER for the Phase E Track B product pages: the first
             page built on the new ProductDetail shape (design-system
             addendum §24). /eremos-v2 inherits §24 next.
Audience:    Internal — Angular engineering (implementers), copywriters
             (verbatim copy), user + ChatGPT (reviewers), Phase E product-
             page authors.
Format:      Per §9 canonical per-page-spec template (page-capabilities-hub
             -spec-v1.md §9), wrapping the ProductDetail page layout
             (design-system-v4.md §24, LOCKED).
Companion:   design-system-v4.md §24 + §24.A + §24.3
                (LOCKED — the page shape this instantiates)
             docs/marketing/web/edgeconnect.html (the ChatGPT-reviewed static
                mockup — visual ground truth; lift copy from §3 below)
             elpis-industrial-intelligence-platform-v5.md (datasheet —
                source-of-truth for the product facts + protocol state)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED —
                the capability story this product page provides the spec
                depth for; cross-link UP)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED — the
                lead solution that uses EdgeConnect; cross-link ACROSS)
             page-architecture-spec-v1.md v2.1 (LOCKED — cross-pillar stack)
             buyer-taxonomy-v1.md §2.3 (OT Architect — primary) + §2.5 (Plant
                engineer — secondary)
             proof-architecture-v1.md §3/§4/§8 (no fabricated metrics /
                customer names / competitor names)
             CLAUDE.md §3 (locked architectural decisions) + §8 (authoritative
                operator-available protocol state — the today-list)
             2026-06-04-phase-e-solution-migration-plan.md (precedents P-A..P-H)
Version:     v1 — LOCKED 2026-06-04 after ChatGPT review (verdict "Approve
                with changes"; all must-fix + should-fix items applied — see
                review note below). ProductDetail shape-setter; /eremos-v2
                inherits §24.
Date:        2026-06-04
Status:      LOCKED (Track B shape-setter).

ChatGPT review (2026-06-04) — verdict "Approve with changes" (ProductDetail
confirmed a genuinely distinct, necessary shape; product-led opening, protocol
state, CTA, per-gateway discipline all endorsed). Applied:
  - §3.4 caption clarified (public product-level matrix vs deployment-specific
    conformance/sizing at architecture review).
  - §3.5 added a "Network + host requirements" row (OS class, firewall/VLAN,
    broker endpoints, OPC UA certificate trust, time-sync, Studio access, buffer
    sizing — confirmed at review, no fabricated figures) + adapter-lifecycle
    sentence (adapters versioned WITH the runtime) + config→audit detail.
  - §3.6 added the 4-annotation table (was generic description).
  - §3.7 editions title "Pay for…" → "Activate the connectivity you license";
    illustrative-labels note moved BEFORE the cards; "deployment-scale pricing"
    → "scoping; detailed pricing scoped after architecture review".
  - §3.9 added Q6 (certificate/credential/secret handling) + Q7 (sizing).
  - §3.3 fan-out card: "Exactly-once is not offered" added (precision).
  Already resolved before this review (the review saw the pre-lock copy): the
  ProductDetail addendum is LOCKED (not PROPOSAL). Mockup updated in lockstep:
  protocol table gained a Direction column; editions wording aligned; Q6/Q7 +
  network row added (static architecture SVG, bundled-Inter note, token-only
  styling were already in from the prior pass).

SHAPE-SETTER ROLE. First page on the LOCKED §24 ProductDetail shape. The other
software product page (/eremos-v2) inherits this structure; hardware product
pages await the §24.B variant. Decisions made here that the next product page
inherits are flagged inline.

What ProductDetail owns (vs the capability + solution pages — §24.1):
  - This page opens with "what it is", NOT a customer-pain narrative (that is
    SolutionPanel's move).
  - This page OWNS the full spec depth: the protocol matrix with direction +
    status + semantic modes, deployment + system requirements + versioning/
    support lifecycle, editions + licensing mechanics. The capability page
    (/capabilities/connectivity-edge) and the solution pages explicitly DEFER
    this depth here.
  - Cross-links UP to /capabilities/connectivity-edge and ACROSS to
    /solutions/edge-connectivity; never re-tells their narrative.

Source-of-truth alignment baked in (P-G + the locked decisions):
  - Protocol state per CLAUDE.md §8: FOCAS2, MTConnect, Brother HTTP, Modbus
    TCP, OPC UA Client, S7 = AVAILABLE today (southbound); MQTT, OPC UA Server =
    AVAILABLE (northbound). MT-LINKi (REST), HTTP sink, TCP sink, Linux host =
    ROADMAP. Do NOT re-add MT-LINKi to the today-list; do NOT list S7/OPC UA
    Client as roadmap.
  - Delivery modes: AtMostOnce + AtLeastOnce only (per route) — CLAUDE.md §3
    lock #12; ExactlyOnce rejected. (This is a verifiable locked claim, not an
    overclaim.)
  - EdgeConnect = Windows service today (.NET 8); Linux near-term roadmap on the
    Edge Gateway appliance. Edge Gateway = optional (software-only fully
    supported).
  - Per-gateway identity / anti-multi-plant-EdgeConnect (CLAUDE.md §3 #19 +
    /architecture v2.1 FAQ Q6).
  - "Beside, not replacing" SCADA/MES/historian.
  - Offline-first; RSA-signed offline license; expiration blocks config not data
    (CLAUDE.md §3 #6/#7).

Buyer (§24.0 + buyer-taxonomy §2.3): OT Architect / SCADA engineer primary —
the technical evaluator who reads docs carefully and tests claims. Plant
engineer (§2.5) secondary (deployment/sizing). CTA = "Request an architecture
review" (§2.3-endorsed; NOT "Book a scoping call" — §2.3 backfire, precedent
P-H). This is the canonical product-page buyer; /eremos-v2 re-derives its own
(also §2.3-leaning, plus Plant manager §2.2 for the analytics outcome).

Editions discipline (§24 anti-pattern, ChatGPT A2): this page describes edition
STRUCTURE + licensing MECHANICS only — NO pricing tables; edition labels
(Starter / Professional / Enterprise) are illustrative until commercial
packaging is approved. Pricing detail waits for Pricing governance (Phase 3
/pricing).

No new component (§24 governance): composes from design-system v3 LOCKED
components + the §24.A spec-table content pattern. Protocol matrix carries a
Direction column (§24.A recommendation).

Word-count target: 1,200-1,800 words page copy (spec-table cell text + diagram
annotations are not prose-counted, per §24 + the §3.7/§3.8 convention). Post-
review draft ~1,600 words (+2 FAQ + editions note).
-->

# `/edgeconnect` — Page Spec v1 (ProductDetail shape-setter)

**Product detail page for EdgeConnect — the protocol-agnostic edge runtime (Pillar 1, Connectivity & Edge). The deepest factual EdgeConnect surface: the full protocol matrix, deployment model + system requirements, and editions + licensing mechanics that the capability and solution pages defer here. First page on the LOCKED `ProductDetail` shape (design-system addendum §24).**

This is where an OT Architect / SCADA engineer lands when they want to know **what EdgeConnect is, exactly** — which protocols it speaks today vs. roadmap, how it deploys, what the system requirements and lifecycle are, and how it's licensed. It is **not** the capability story (`/capabilities/connectivity-edge`) and **not** the outcome story (`/solutions/edge-connectivity`); it is the **product truth**.

Target length: **1,200-1,800 words page copy** per design-system addendum §24 (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The EdgeConnect product detail page. Reader leaves with *"I now know exactly which protocols EdgeConnect speaks today vs. roadmap, how it deploys and what it requires, how it's versioned and supported, how it's licensed, and how it fits beside my existing systems — enough to take it into an architecture review."*

**IS NOT:**
- The capability page (`/capabilities/connectivity-edge`, LOCKED v2.1 — covers EdgeConnect + Edge Gateway as the Pillar 1 *capability* story; this page provides its *spec depth* and cross-links up)
- A solution / outcome page (`/solutions/edge-connectivity` v2.1 + `/solutions/cnc-machining` v3 cover the *outcomes*; this page cross-links across)
- The EREMOS V2 product page (`/eremos-v2`, the sibling software ProductDetail page — Pillar 5 intelligence layer)
- A hardware product page (`/edge-gateway` and the other hardware pages are the deferred §24.B variant)
- The architecture walkthrough (`/architecture` v2.1 — cross-pillar stack)
- A pricing page (`/pricing`, Phase 3 — this page describes edition *structure* + licensing *mechanics*, never pricing tables)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.0)

**Primary buyer:** OT Architect / SCADA engineer (§2.3)
- Lands here from `/capabilities/connectivity-edge` (cross-link for spec depth), the homepage/nav Platform menu, or a search for *"FOCAS2 MQTT gateway"* / *"protocol-agnostic edge runtime"* / *"OPC UA Server industrial gateway"* / *"S7 + FOCAS2 one runtime"*
- Wants: the exact protocol coverage with direction + status + semantic modes, OPC UA Server security modes, store-and-forward + diagnostics mechanics, deployment + system requirements, versioning/support, licensing mechanics — verifiable, not hand-wavy
- CTA preference: *"Request an architecture review"* > *"Talk to an engineer about Connectivity & Edge"* > datasheet download. **NOT** *"Book a scoping call"* (a §2.3 backfire — precedent P-H)
- Vocabulary that lands: *protocol-agnostic*, *edge runtime*, *FOCAS2 / MTConnect / Brother HTTP / Modbus TCP / OPC UA Client / S7 / OPC UA Server*, *canonical vocabulary*, *store-and-forward*, *three-way diagnostics*, *per-tag quality codes*, *hash-chained audit*, *per-gateway identity*, *draft → validate → apply → rollback*
- Vocabulary that backfires: *"intuitive"*, *"easy"*, *"no-code"*, *"seamless integration"*, *"future-proof"*, *"single pane of glass"*

**Secondary buyer:** Plant engineer (retrofit / greenfield) (§2.5) — deployment/sizing/BOM
- Wants: host platform + system requirements, footprint/sizing per controller count, software-only vs. Edge Gateway appliance trade-off
- Served via the §5 deployment section + cross-lens (per buyer-taxonomy §5 step 3 — secondary buyers via the relevant section + cross-lens, not by re-targeting the whole page)

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 metadata governance. Product-page pattern (first instance; `/eremos-v2` inherits).

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *EdgeConnect — Protocol-Agnostic Edge Runtime · Elpis* |
| **Meta description** (140-160 chars) | *EdgeConnect speaks FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 — normalized to one canonical vocabulary, offline-first.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/edgeconnect` |
| **Schema intent** | `schema.org/SoftwareApplication` (product) + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/connectivity-edge` + `/solutions/edge-connectivity` + `/architecture` + `/security` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` layout per design-system addendum §24 (LOCKED). **11 sections** (software variant).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line definition + CTAs + trust strip | `dark-deep` | `SectionShell` + `Button` ×2 | ~90 |
| **2** | What it is — product definition + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~140 |
| **3** | Capabilities — the runtime's feature set | `dark` | `CapabilityCard` grid (compact) | ~220 |
| **4** | Protocol coverage — the full matrix | `light` | §24.A spec-table (Direction · Status · Coverage) | spec (not prose) |
| **5** | Deployment + system requirements (incl. versioning/support lifecycle) | `light-tinted` | §24.A spec-table + narrative | ~120 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~80 |
| **7** | Editions + licensing (structure + mechanics, not pricing) | `dark` | editions cards + narrative | ~150 |
| **8** | Trust + security posture | `light-tinted` | trust-cue content pattern (§16) | ~110 |
| **9** | Common questions (inline FAQ) — 7 Q&A | `light` | inline FAQ + `FAQPage` schema | ~430 |
| **10** | Related — cross-lens (§24.3 preset) | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

Verbatim copy ground-truthed against the reviewed mockup (`docs/marketing/web/edgeconnect.html`). The mockup is the visual reference; this section is the authoritative copy + structure.

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal): PRODUCT · CONNECTIVITY & EDGE
>
> HEADLINE (size.3xl semibold): EdgeConnect
>
> SUBHEAD (size.lg, max-width 64ch):
> The protocol-agnostic edge runtime at the core of the Elpis platform. One service polls every controller on your floor over its native protocol, normalizes every signal to one canonical vocabulary, and delivers it once to every system that needs it — with store-and-forward and three-way diagnostics built in.
>
> PRIMARY CTA (`Button.primary.lg`): Request an architecture review → HREF `/contact?intent=edgeconnect-architecture-review`
> SECONDARY CTA (`Button.secondary.lg`): Download the datasheet → HREF `/resources/datasheet`
>
> TRUST STRIP (size.sm):
> FOCAS2 · MTConnect · Brother HTTP · Modbus TCP · OPC UA Client · Siemens S7 — normalized to one canonical vocabulary. FANUC MT-LINKi (REST) on the roadmap. Windows today; Linux on the roadmap. Offline by default.
>
> HERO VISUAL (right column, per §24 hero-visual slot): two-column hero (`hero__inner`) — copy left, a product-relevant `HeroComposite`-style SVG right (matches the homepage hero; no blank right half). For EdgeConnect: a **protocol-fan-in → canonical-stream** graphic (six native protocols converging into the EdgeConnect runtime, one canonical stream out to MQTT / OPC UA Server). Decorative (`aria-hidden`), token-only, "illustrative" caption. See the mockup `web/edgeconnect.html`.

**Anti-patterns:** Headline is the product name + value, not a feature list. Hero is product-led (it IS a product page) but the headline value is the *outcome of the runtime*, not a spec dump. No "Book a scoping call" (P-H). No "seamless" / "intuitive" / "single pane of glass".

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: One runtime in front of every controller on the floor.
>
> BODY (size.md):
> EdgeConnect is a Windows service that runs on a small box in your control cabinet. It connects to the controllers you already own — CNCs, PLCs, and instrumentation — over their native protocols, normalizes every reading to a shared canonical vocabulary, and publishes that normalized stream once for every downstream consumer: an MQTT broker, an OPC UA Server, EREMOS V2, your SCADA, your historian.
>
> BODY ¶2 (size.base, muted):
> It is the **Connectivity & Edge** pillar of the Industrial Intelligence Ecosystem. For the capability story, see → `/capabilities/connectivity-edge`. This page is the product detail — the full protocol coverage, deployment model, and licensing.

**Note (§24.1):** opens with "what it is", not a pain narrative. Cross-links UP to the capability pillar.

### 3.3 Section 3 — Capabilities

> EYEBROW: CAPABILITIES
> SECTION TITLE: What the runtime does.

Feature grid (compact `CapabilityCard`s; bolded lead + 1-2 sentence body):

> - **Protocol-agnostic core.** The core runtime references no protocol. Adapters plug in and are activated by license at startup — new protocols ship without touching the old ones.
> - **Canonical normalization.** Every signal becomes a canonical data point — `spindle_rpm`, `cycle_time`, alarm state — before routing, regardless of which vendor produced it.
> - **Per-route store-and-forward.** SQLite-backed buffering with per-sink cursors. Connectivity gaps replay in source order on reconnect — queued data is preserved, not dropped.
> - **Three-way diagnostics.** Source, pipeline, and sink health surfaced separately, so the OT team sees exactly which leg of the data path broke.
> - **Per-adapter isolation.** A failing adapter never affects another adapter, route, or sink. A misbehaving sink never blocks a healthy one.
> - **Connectivity Studio.** Web admin for sources, routes, and sinks, with Test Connection probes and a draft → validate → apply → rollback flow before any change reaches the data path.
> - **Hash-chained audit.** Every configuration change captured with actor identity and timestamp in a tamper-evident, replay-ready chain.
> - **Fan-out routing.** One source fans out to many sinks independently — a failing sink never blocks a healthy one. Delivery mode is configured per route: at-most-once or at-least-once. Exactly-once is not offered.

**Note:** "at-most-once / at-least-once" is the LOCKED delivery-mode set (CLAUDE.md §3 #12 — ExactlyOnce rejected); accurate, not an overclaim. Store-and-forward phrasing ("preserved, not dropped") is mechanism-tethered, not an absolute guarantee.

### 3.4 Section 4 — Protocol coverage (§24.A spec-table)

> EYEBROW: PROTOCOL COVERAGE
> SECTION TITLE: Every protocol the runtime speaks.

Spec-table per §24.A. **Carries Direction (southbound/northbound) + Status (Available/Roadmap) columns** so a reader never infers source-vs-sink. Status reflects CLAUDE.md §8 (P-G).

| Protocol | Direction | Status | Coverage / modes |
|---|---|---|---|
| **FOCAS2** | Southbound | Available | Fanuc CNCs (0i / 16i / 18i / 21i / 30i / 31i / 32i) — axes, spindle, alarms, tool, production counters, programs. Polled. |
| **MTConnect** | Southbound | Available | The open-standard CNC streaming protocol; probe-document driven. |
| **Brother HTTP** | Southbound | Available | Brother CNCs (S700Xd1 and similar) via the built-in web-monitoring interface. |
| **Modbus TCP** | Southbound | Available | PLCs, drives, energy meters — any Modbus TCP device, including PLCs fronting older CNCs. |
| **OPC UA Client** | Southbound | Available | OPC UA-native controllers, gateways, and servers across broad industrial equipment. |
| **Siemens S7** | Southbound | Available | Native S7 driver for Siemens PLC fleets; manual tag-address mapping. |
| **MQTT** | Northbound | Available | Any compliant broker (Mosquitto, HiveMQ, EMQX, AWS IoT Core, Azure IoT Hub). Batch or per-tag publish modes. |
| **OPC UA Server** | Northbound | Available | Exposes signals to SCADA / MES / HMI. Security modes: Sign, SignAndEncrypt, X.509. ISA-95-style browse paths configurable. |
| **FANUC MT-LINKi (REST)** | Southbound | Roadmap | Fanuc's REST-based machine-data product, via its REST API. |
| **HTTP sink** | Northbound | Roadmap | Direct delivery to REST endpoints. |
| **TCP sink** | Northbound | Roadmap | Delivery to legacy TCP listeners. |

> CAPTION (size.sm): This page carries the **product-level** protocol matrix. Deployment-specific semantic modes, connection-pool sizing, and conformance checks are confirmed during the architecture review against your real controller mix.

### 3.5 Section 5 — Deployment + system requirements

> EYEBROW: DEPLOYMENT & REQUIREMENTS
> SECTION TITLE: How it runs.

Spec-table (§24.A) + the **Versioning + support lifecycle** subsection (ChatGPT A1):

| | |
|---|---|
| **Host platform** | Windows service today (.NET 8). Linux is near-term roadmap — it ships on the Edge Gateway appliance as the canonical EdgeConnect appliance. Cross-platform by design (Windows-only APIs guarded). |
| **Deployment shape** | Software-only on customer hardware (a small control-cabinet box), or the ruggedized **Edge Gateway** appliance (DIN-rail, embedded Linux) — an option, not a requirement. |
| **Footprint** | One service per plant or per cell, sized to the controller count. Runs offline; no cloud dependency. |
| **Identity** | Per-gateway UUID + customer/site binding established at first start. Each plant runs its own runtime; multi-site visibility comes from EREMOS V2 aggregating across per-plant runtimes — never one runtime spanning plants. |
| **Resilience** | Per-route store-and-forward (SQLite) survives broker/network outages and replays in source order. Faults isolated per adapter and per sink. |
| **Network + host requirements** | Host OS class, firewall / VLAN assumptions, broker endpoints, OPC UA Server exposure + certificate trust, time-sync (NTP / plant source), Connectivity Studio admin access, and SQLite buffer-retention sizing are confirmed during architecture review against the controller count + site policy. No fixed figures published — sizing depends on tag count, publish frequency, and the expected outage window. |
| **Versioning + support** | Released as a versioned Windows service; protocol adapters are versioned **with the runtime** and validated during architecture review before production activation (not free-floating plugins). Configuration changes go through draft → validate → apply → rollback, each entering the hash-chained audit log. Support boundary spans the EdgeConnect runtime, the Edge Gateway appliance (when used), and the EREMOS V2 integration. |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: EdgeConnect in the stack.

`ArchitecturePanel.interactive` (product-annotated subset, design-system §5.A): Floor controllers → **EdgeConnect** (highlighted) → MQTT / OPC UA Server (publish once, fan out) → EREMOS V2 + SCADA / MES / historian (*beside, not replacing*). The mockup ships a static SVG stand-in; the page uses the interactive variant.

**Annotations (4, per §5.A — eyebrow doubles as the ≤4-word title per §24 P-E):**

| Annotated region | Eyebrow | Body |
|---|---|---|
| Floor → EdgeConnect | NATIVE PROTOCOLS | EdgeConnect polls controllers over FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and S7. |
| EdgeConnect core | CANONICAL RUNTIME | Signals normalize to one canonical shape before routing — every sink receives the same shape. |
| EdgeConnect → sinks | FAN-OUT ROUTING | MQTT and OPC UA Server expose the same canonical stream to EREMOS V2 and existing systems, independently per sink. |
| Identity / deployment | PER-PLANT RUNTIME | Each plant or cell runs its own runtime; multi-site visibility comes from EREMOS V2 aggregation — never one runtime spanning plants. |

> CAPTION: EdgeConnect sits beside your existing SCADA / historian / MES, not in place of them — they consume canonical signals instead of vendor-specific ones. See the full cross-pillar story → `/architecture`.

### 3.7 Section 7 — Editions + licensing

> EYEBROW: EDITIONS & LICENSING
> SECTION TITLE: Activate the connectivity you license.
>
> *Edition labels are illustrative until commercial packaging is approved; this section describes packaging + licensing mechanics, not pricing.*

Editions cards (illustrative labels):

> - **Starter** — single-protocol, single-site connectivity for a first proof of value.
> - **Professional** — multi-protocol, multi-route connectivity for a production line or cell.
> - **Enterprise** — full protocol set, fleet-scale routing, and the operability surfaces for multi-site operations.

> BODY:
> Connectivity modules — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, Siemens S7, MQTT, OPC UA Server — activate per protocol against your edition. Licensing is an **RSA-signed offline license file**: fully offline, no phone-home. A lapsed license blocks configuration changes only — your machines keep talking. Contact Elpis for edition feature lists and deployment-scale scoping; detailed pricing is scoped after architecture review.

**Note (§24 anti-pattern, A2):** edition structure + licensing mechanics only; NO pricing tables; labels illustrative until packaging approved.

### 3.8 Section 8 — Trust + security posture

> EYEBROW: TRUST POSTURE
> SECTION TITLE: Built for OT review.

Trust-cue content pattern (§16), 2 cues, cross-link `/security`:

> CUE 1 — **Offline-first, air-gap-ready.** EdgeConnect runs offline by default. The license validates locally — no phone-home. Plants on isolated OT VLANs install and run it the same way as connected plants; cloud connectivity is opt-in, not required.
>
> CUE 2 — **Per-gateway identity + hash-chained audit.** Each plant runs its own runtime with a per-gateway UUID. Every configuration change is captured with actor identity and timestamp in a tamper-evident, replay-ready audit chain.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 per-page-type FAQ governance (product pages = YES). `FAQPage` schema. 7 technical-evaluation Q&A.

> #### Q1. Which protocols ship today vs. roadmap?
> Today: FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, and Siemens S7 (southbound); MQTT and OPC UA Server (northbound). On the roadmap: FANUC MT-LINKi (REST), HTTP and TCP sinks, and Linux host support. See the coverage table above.
>
> #### Q2. Can it run on Linux today?
> Today EdgeConnect ships as a Windows service. Linux is near-term roadmap and arrives on the Edge Gateway appliance as the canonical EdgeConnect appliance. The codebase is cross-platform by design; Windows-only APIs are guarded.
>
> #### Q3. Does it replace our SCADA / historian / MES?
> No. EdgeConnect sits beside them and publishes canonical signals via MQTT and OPC UA Server. Your existing systems keep their jobs and consume consistent signals instead of vendor-specific ones.
>
> #### Q4. What happens when the broker or network drops?
> Per-route store-and-forward queues every signal at the source with its quality code preserved and replays in source order on reconnect. Three-way diagnostics surface which leg was affected during the outage.
>
> #### Q5. Can one EdgeConnect serve multiple plants?
> No — that's an anti-pattern. Each plant runs its own EdgeConnect with a per-gateway UUID; multi-site visibility comes from EREMOS V2 aggregating across the per-plant runtimes.
>
> #### Q6. How are certificates, credentials, and secrets handled?
> OPC UA certificates, broker credentials, and route secrets are deployment-specific configuration, confirmed during architecture review. The production deployment defines certificate trust, credential storage, rotation responsibility, and access boundaries before the runtime is enabled. (OPC UA Server supports Sign, SignAndEncrypt, and X.509 security modes.)
>
> #### Q7. How is EdgeConnect sized for controller count and tag volume?
> Sizing depends on controller count, poll frequency, tag count, route count, and the expected outage-buffer window. The architecture review scopes the host and buffering requirements against your real controller mix; no fixed figures are published.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 product-page cross-lens preset:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONNECTIVITY & EDGE | The Pillar 1 capability story | `/capabilities/connectivity-edge` |
| 2 | SOLUTION · EDGE CONNECTIVITY | The OT-consolidation outcome built on EdgeConnect | `/solutions/edge-connectivity` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your controller mix.
> SUBHEAD: A controller list, a target broker, an existing-systems boundary — that's what we scope an architecture review against. Demos run on real protocols against real signals, not slideware.
> PRIMARY CTA: Request an architecture review → `/contact?intent=edgeconnect-architecture-review`
> SECONDARY CTA: Download the datasheet → `/resources/datasheet`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive.**

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1 hero; §3.11 final CTA |
| `CapabilityCard` (compact) | §3.3 capabilities; §3.7 editions cards |
| `ArchitecturePanel.interactive` (product-annotated per §5.A) | §3.6 |
| §24.A spec-table content pattern | §3.4 protocol matrix; §3.5 deployment table |
| Trust-cue content pattern (§16) | §3.8 |
| Cross-lens content pattern (§17, §24.3 preset) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,600 words page copy** (within the §24 1,200-1,800 target; post-ChatGPT-review with +2 FAQ + editions note). Spec-table cell text (§3.4, §3.5) and the §3.6 diagram annotations are NOT prose-counted, per §24 + the §3.7/§3.8 convention. The reviewed mockup (`web/edgeconnect.html`) is the visual ground truth; copy here is authoritative.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.4 ProductDetail anti-patterns:

| Don't | Why |
|---|---|
| Re-tell the capability or solution narrative as primary content | §24.1 — cross-link UP (capability) + ACROSS (solution); ProductDetail owns the spec, not the narrative. |
| Open with a "customer pain" empathy narrative | That's SolutionPanel's move (§24 anti-pattern). ProductDetail opens with "what it is" (§3.2). |
| List MT-LINKi as Available, or S7 / OPC UA Client as Roadmap | Per CLAUDE.md §8 + P-G. MT-LINKi is Roadmap; S7 + OPC UA Client are Available. The §3.4 table Status column is the guard. |
| Publish pricing tables or treat edition labels as locked packaging | §24 anti-pattern + ChatGPT A2 — structure + mechanics only; labels illustrative until Pricing governance (Phase 3). |
| Drop the protocol matrix's Direction or Status column | §24.A — a reader must never infer source-vs-sink or today-vs-roadmap. |
| Use "Book a scoping call" as the primary CTA | §2.3 backfire; precedent P-H — product pages use "Request an architecture review". |
| Imply EdgeConnect Linux is current, or the Edge Gateway is required | Windows today / Linux roadmap; appliance optional (§3.5 + carry-forward from connectivity-edge v2 §6). |
| Imply one EdgeConnect serves multiple plants | Per-gateway identity / anti-multi-plant (CLAUDE.md §3 #19 + /architecture v2.1 Q6). §3.5 Identity row + §3.8 Cue 2 + §3.9 Q5 guard it. |
| Frame store-and-forward / delivery as an absolute guarantee | Mechanism-tethered phrasing only ("preserved, not dropped"; "configured per route"). The delivery-mode claim is the LOCKED #12 set — accurate, but keep it tied to the route-config mechanism. |
| Fabricated throughput/latency/uptime numbers, customer names, or competitor names | proof-architecture §3/§4/§8. Controller-vendor names (Fanuc/Brother/Siemens) are coverage facts, not competitive comparison. |
| Introduce a new visual primitive | §24 governance — compose from v3 components + §24.A only. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,450); spec tables not prose-counted
- [x] All 11 ProductDetail sections present per §24.2
- [x] §3.1 hero is product-led with the runtime's value; CTA "Request an architecture review" (P-H), NOT "Book a scoping call"
- [x] §3.2 opens with "what it is" (no pain narrative); cross-links UP to `/capabilities/connectivity-edge`
- [x] §3.4 protocol matrix carries Direction + Status columns; MT-LINKi = Roadmap, S7 + OPC UA Client = Available (P-G / CLAUDE.md §8)
- [x] §3.5 deployment table honest (Windows today / Linux roadmap on Edge Gateway; appliance optional) + Versioning/support subsection present (A1)
- [x] §3.5 + §3.8 + §3.9 Q5 carry per-gateway identity / anti-multi-plant discipline
- [x] §3.6 uses `ArchitecturePanel.interactive` (product-annotated), not a static image; "beside, not replacing"
- [x] §3.7 editions = structure + mechanics only; labels illustrative; NO pricing (A2)
- [x] §3.8 trust cues cover offline-first + per-gateway/hash-chained audit; cross-link `/security`
- [x] §3.9 inline FAQ uses `FAQPage` schema; Q1 protocol split correct; Q5 denies multi-plant runtime
- [x] §3.10 cross-lens matches §24.3 preset (connectivity-edge + edge-connectivity + architecture)
- [x] Delivery-mode claim (at-most-once / at-least-once) tied to per-route config (CLAUDE.md §3 #12)
- [x] No new component beyond design-system v3 + §24.A
- [x] §1.4 metadata present (SoftwareApplication schema); no banned vocabulary (§2.3)
- [x] No fabricated metrics / customer names / competitor names
- [x] Visual matches the reviewed mockup (`web/edgeconnect.html`)
- [x] **Shape-setter decisions documented** for `/eremos-v2` to inherit (§24 instantiation, spec-table Direction column, editions-not-pricing, CTA P-H, versioning/support subsection)
- [x] ChatGPT review pass applied (verdict "Approve with changes"); v2 items applied — §3.4 caption clarity; §3.5 Network+host row + adapter lifecycle; §3.6 4-annotation table; §3.7 editions title→"Activate the connectivity you license" + illustrative-note-before-cards + pricing→scoping; §3.9 Q6 certs + Q7 sizing; §3.3 exactly-once; mockup aligned (Direction column + editions + Q6/Q7 + network row)

---

## 8. Out of scope for v1

- **Full per-protocol semantic-mode / conformance tables.** Scoped at architecture-review time; the §3.4 matrix stays at coverage + direction + status + one-line modes.
- **EREMOS V2 product detail.** `/eremos-v2` (sibling ProductDetail page) covers the Pillar 5 intelligence layer; this page cross-links.
- **Edge Gateway hardware detail.** The §24.B hardware variant (`/edge-gateway`) covers dimensions, power, enclosure, I/O, environmental, certifications — deferred pending the cert open question.
- **Capability + solution narratives.** `/capabilities/connectivity-edge` + `/solutions/edge-connectivity` (LOCKED) — cross-link, don't duplicate.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3). This page is structure + mechanics only.
- **Security walkthrough.** `/security` — cross-link from §3.8.

---

*`/edgeconnect` Page Spec **v1 LOCKED 2026-06-04** (ProductDetail shape-setter) after ChatGPT review (verdict "Approve with changes"; all must-fix + should-fix applied). First page on the LOCKED §24 ProductDetail shape (design-system-v4.md). Instantiates the 11-section software-variant composition with no new visual primitive (v3 components + §24.A spec-table). Source-of-truth aligned: protocol state P-G-correct (MT-LINKi roadmap; S7 + OPC UA Client available, per CLAUDE.md §8); delivery modes per the locked #12 set; Windows-today/Linux-roadmap; per-gateway identity; beside-not-replacing; offline-first. OT-Architect buyer (§2.3) → "Request an architecture review" CTA (P-H). Editions = structure + mechanics, NOT pricing. Visual ground truth = the ChatGPT-reviewed mockup at web/edgeconnect.html. Next: user + ChatGPT review → lock → /eremos-v2 inherits §24. Cites: design-system-v4 §24/§24.A/§24.3, page-capabilities-hub-spec-v1 §9, design-system-v3 §5.A/§16/§17, buyer-taxonomy-v1 §2.3/§2.5, proof-architecture-v1 §3/§4/§8, CLAUDE.md §3/§8, elpis-industrial-intelligence-platform-v5 (datasheet), page-capabilities-connectivity-edge-spec-v1 v2.1, page-solutions-edge-connectivity-spec-v1 v2.1, page-architecture-spec-v1 v2.1, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
