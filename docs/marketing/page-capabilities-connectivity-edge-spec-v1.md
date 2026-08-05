<!--
File:        docs/marketing/page-capabilities-connectivity-edge-spec-v1.md
Purpose:     Page spec for /capabilities/connectivity-edge — Pillar 1
             deep-dive (EdgeConnect + Edge Gateway). Content-only spec
             inheriting the CapabilityDeepDive layout locked in
             page-capabilities-condition-monitoring-spec-v1.md.
Audience:    Internal — Angular engineering team, copywriters, user +
             ChatGPT (reviewers), authors of the remaining 3 pillar
             specs.
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-condition-monitoring-spec-v1.md
                (LOCKED — layout precedent for this spec)
             page-capabilities-hub-spec-v1.md §9 (canonical template)
             phase2-ia-scope-memo-v2.md + amendment v3 (IA parent)
             buyer-taxonomy-v1.md §2.3 + §2.5 (OT Architect +
                Plant engineer profiles — primary + secondary buyers)
             proof-architecture-v1.md (proof discipline)
             design-system-v3.md §14 (CapabilityDeepDive), §16
                (trust cue), §17 (cross-lens)
             hardware-ecosystem-map-v3.md §2 (Connectivity & Edge
                pillar — verbatim source for EdgeConnect + Edge
                Gateway detail)
             industrial-intelligence-ecosystem-positioning-v3.md
                (parent worldview)
Version:     v2.2 — LOCKED. Original v2 locked 2026-05-29 (dual-product
                  framing + appliance-mandatory anti-pattern + IoT-gateway
                  callout sharpened). v2.1 amendment 2026-05-29 fixed
                  OPC UA Server → OPC UA Client typo. v2.2 amendment
                  2026-06-04 dropped MT-LINKi from §3.2 Card 1 today-list
                  per platform-team direction (no customer demand;
                  engineering deferred to low priority); MT-LINKi REST
                  integration moved to roadmap mention.
Date:        2026-05-29 (v2 lock) / 2026-05-29 (v2.1) / 2026-06-04 (v2.2)
Status:      LOCKED.

v2.1 amendment (2026-05-29): §3.2 Card 1 EdgeConnect protocol list
typo correction. Original v2 listed "OPC UA Server" in the source-
polling slot — wrong, OPC UA Server is a sink/expose mechanism on
EdgeConnect's northbound surface, not a source-polling protocol.
Corrected to "OPC UA Client" (the correct source-polling protocol).
Surfaced by the /solutions/edge-connectivity v2 pre-lock workflow's
cross-spec drift validator; corrected upstream rather than reverting
downstream (the edge-connectivity solution spec correctly uses
"OPC UA Client" in all 6 locations and matches architecture v2 +
CLAUDE.md §8). Same line correctly retains "Publishes to MQTT, OPC UA
Server, or HTTP/TCP" — OPC UA Server is the publish surface, OPC UA
Client is the source-polling protocol. No other content changes.

Third per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 5 (parallelizable pillar deep-dives). Content-only
spec — inherits CapabilityDeepDive layout from condition-monitoring
spec without re-derivation.

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve after a small v2 refinement pass. This is a strong page and
   the overall structure works very well. The OT Architect targeting is
   accurate, the proof discipline is intact, the trust posture is one of
   the strongest in the ecosystem, and the EdgeConnect vs Edge Gateway
   story is mostly successful. The only area that needs additional work
   before lock is the dual-product framing — Connectivity & Edge is the
   first pillar that genuinely contains software runtime + hardware
   appliance, and the page needs to work slightly harder to explain why
   those belong together."

User decision on v2 refinements (2026-05-29):
  - R1 (unifying framing sentence before §3.2 cards — "One capability,
       two deployment surfaces")                                       APPLIED
        — ChatGPT: "the most important refinement before lock"
        — solves the "Why are these two separate things on the same
          page?" first-read risk
  - R2 (sharpen §3.5 IoT-gateway callout to lead with the differentiator
       instead of leading with the contrast)                           APPLIED
        — reviewer-flagged optional; applied per user direction
        — "every signal arrives at every sink in the same shape" now
          opens the callout, gateway contrast moved to second position
  - R3 (new §6 anti-pattern: don't present Edge Gateway as the required
       deployment path for EdgeConnect)                                APPLIED
        — pure governance lock against future "buy the box" copy drift

Sections receiving "no change" reviewer approval (preserved verbatim):
  §1.2 buyer targeting ("Excellent — perfectly aligned"), §3.1 hero,
  §3.2 EdgeConnect Linux roadmap framing ("Excellent — keep exactly as
  written"), §3.3 BOM elimination, §3.4 strategic adjacencies, §3.6
  trust cue ("one of the strongest trust cues in the entire ecosystem"),
  §3.7 related solutions, §3.9 CTA ("Excellent — no changes required"),
  proof-architecture compliance ("Strong governance discipline").

Post-draft additions (governance compliance, not content changes):
  - 2026-05-28: §1.4 Page metadata (SEO + HTML head) block added
    per /capabilities hub §9 metadata governance lock (PR #71).
  - 2026-05-29: §3.5 "How this differs from a general-purpose IoT
    gateway" callout added per /capabilities hub §9 emerging-pattern
    governance (PR #71 commit 782a626). Pattern was reviewer-validated
    on /capabilities/asset-intelligence v2 ("the single most important
    improvement before lock"). Applied here pre-review so the v1 draft
    that ChatGPT sees already includes the strengthening pattern.
    (v2 R2 above further sharpened this callout's opening.)
-->

# `/capabilities/connectivity-edge` — Page Spec v1

**Capability deep-dive for the Connectivity & Edge pillar (EdgeConnect + Edge Gateway). Inherits the `CapabilityDeepDive` layout confirmed for the 5 pillar pages in PR #72. Content-only spec — same 9-section structure, content adapted to this pillar.**

This is the page where OT Architects and Plant engineers land when they want to evaluate Elpis for industrial data acquisition and edge connectivity. It is **not** a product detail page (EdgeConnect and Edge Gateway each get Phase E product pages). It is **not** the architecture walkthrough (`/architecture`). It is the **capability** view: what this pillar covers, what it eliminates from your BOM, where it sits architecturally, what trust posture applies, what solutions build on it.

Target length: **800-1,200 words page copy** per `/capabilities` hub spec §9 page-type guidance.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Capability deep-dive for Connectivity & Edge. Reader leaves with *"I now understand what this pillar covers, what it replaces in my current setup, where it sits architecturally, and which solutions it powers."*

**IS NOT:** A full product detail page (EdgeConnect Phase E `/edgeconnect`; Edge Gateway Phase E `/edge-gateway`). A protocol reference table (lives on `/architecture`). A solution narrative (that's `/solutions/edge-connectivity`).

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** OT Architect / SCADA engineer (§2.3)
- Lands here from a Google search for *"FOCAS2 MQTT gateway"*, *"industrial edge runtime"*, *"protocol-agnostic OT data layer"*, or capability-first navigation
- Wants: architectural clarity, full protocol coverage, integration patterns
- CTA preference: *"Talk to an engineer about Connectivity & Edge"* > *"Request an architecture review"*
- Vocabulary that lands: protocol-agnostic, edge runtime, OPC UA Server, MQTT, FOCAS2, MTConnect, Modbus TCP, store-and-forward, three-way diagnostics, canonical CNC vocabulary, hash-chained audit
- Vocabulary that backfires: *"intuitive"*, *"easy"*, *"seamless integration"*, *"future-proof"*

**Secondary buyer:** Plant engineer (retrofit / greenfield) (§2.5)
- Lands here when they need to specify the BOM for a retrofit or greenfield install with existing PLC infrastructure
- Wants: hardware specs (Edge Gateway specifically), deployment patterns
- CTA preference: *"Get hardware specifications"* > *"Talk to an engineer"*

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Connectivity & Edge — protocols + edge runtime · Elpis* |
| **Meta description** (140-160 chars) | *Protocol-agnostic edge runtime for FOCAS2, OPC UA, MQTT, Modbus, MTConnect, S7, Brother HTTP. Store-and-forward, three-way diagnostics, signed audit chain.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/capabilities/connectivity-edge` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. Edge Gateway hardware card cross-links to Phase E `/edge-gateway` via `Product` schema. EdgeConnect runtime references link to Phase E `/edgeconnect` (when shipped) via `SoftwareApplication`. Page-to-page cross-link to `/architecture` uses `relatedLink`. |

---

## 2. Page structure — sections at a glance

Per design-system v3 §14 `CapabilityDeepDive` layout (locked in PR #72 for all 5 pillars):

| # | Section | Visual mode | Primary component(s) |
|---|---|---|---|
| **1** | Hero (eyebrow + customer question + CTAs) | `dark-deep` | `SectionShell` + `Button` × 2 |
| **2** | Products in this pillar (EdgeConnect + Edge Gateway) | `dark` | `CapabilityCard` × 2 with pillar-1 accent |
| **3** | What this pillar eliminates from your BOM | `light` | Bulleted list |
| **4** | Strategic adjacencies | `light` | 3-column grid |
| **5** | Where this fits in the Industrial Intelligence Stack | `light-tinted` | `DiagramFrame` focused on Pillar 1 + cross-link to `/architecture` |
| **6** | Trust posture for this pillar | `light-tinted` | §16 trust cue content pattern |
| **7** | Related solutions | `light` | `CapabilityCard` × 2 (solution-card variant) |
| **8** | Cross-lens navigation | `light-tinted` | §17 cross-lens pattern (3 cards) |
| **9** | Final CTA | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW (small-caps brand-teal):
> CAPABILITY · CONNECTIVITY & EDGE
>
> HEADLINE (size.3xl semibold):
> One operational data layer for every controller on your floor — without ripping out what you already have.
>
> CUSTOMER QUESTION LEAD (size.lg italic):
> *"How do I get my controllers' data into one operational view, on-premise and offline-capable?"*
>
> PRIMARY CTA (`Button.primary.lg`):
> Talk to an engineer about Connectivity & Edge
> HREF: `/contact?intent=connectivity-edge-engineering`
>
> SECONDARY CTA (`Button.secondary.lg`):
> Request an architecture review
> HREF: `/contact?intent=architecture-review`

**Anti-patterns:** no *"seamless integration"* / *"intuitive"* / *"easy"* framing (backfires per buyer-taxonomy §2.3). No outcome metric in headline.

---

### 3.2 Section 2 — Products in this pillar

> EYEBROW: PRODUCTS IN THIS PILLAR
>
> SECTION INTRO (size.lg, between eyebrow and cards):
> This pillar combines the software runtime that understands industrial protocols (EdgeConnect) with the appliance that deploys and hosts industrial connectivity in the field (Edge Gateway). **One capability, two deployment surfaces.**

#### Card 1 — EdgeConnect (pillar-1 accent)

> EYEBROW: EDGE RUNTIME
> TITLE: EdgeConnect — Protocol-agnostic industrial data layer
> BODY:
> One service that polls every controller on your floor over its native protocol — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7. Normalizes signals into a canonical vocabulary. Publishes to MQTT, OPC UA Server, or HTTP/TCP. Store-and-forward buffering means no lost cycles during broker outages. Three-way diagnostics show source / pipeline / sink health. Hash-chained configuration audit. Per-tag quality codes. Runs Windows today; Linux on the roadmap (deploys then on Edge Gateway). FANUC MT-LINKi REST integration is on the roadmap.
> FOOTER: Software · Windows service today · Linux roadmap
> LINK: *(Phase E product detail — coming soon)*

#### Card 2 — Edge Gateway (pillar-1 accent)

> EYEBROW: INDUSTRIAL APPLIANCE
> TITLE: Edge Gateway — Ruggedized PLC-to-cloud appliance
> BODY:
> Ruggedized industrial gateway running embedded Linux. Built-in Modbus TCP server/client, 4G/Wi-Fi/Ethernet, web-configurable, USB firmware updates. 256 MB RAM / 2 GB Flash, 24 V DC, 200 × 150 × 75 mm rugged enclosure. **Dual strategic identity:** today it ships as a standalone PLC-to-cloud gateway with built-in Modbus TCP and cellular publish. Tomorrow — once EdgeConnect Linux ships — it becomes the canonical EdgeConnect appliance. Buy it today for what it does today; it grows into the broader platform path on the same hardware.
> FOOTER: Hardware appliance · embedded Linux · 24 V DC · ruggedized
> LINK: *(Phase E product detail — coming soon)*

**Anti-patterns:** No omission of the dual-identity story for Edge Gateway (it's the central commercial signal — same box, two lifecycles). No omission of the *"Linux roadmap"* honest framing for EdgeConnect (today Windows, Linux on the roadmap).

---

### 3.3 Section 3 — What this pillar eliminates from your BOM

> EYEBROW: WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
>
> SUBHEAD:
> Connectivity & Edge consolidates what would otherwise be a stack of separate products.
>
> BULLETED LIST:
>
> - Per-vendor monitoring tools (one for Fanuc, one for Brother, one for Mazak — each with its own dashboard and reporting cadence)
> - A separate industrial PC for hosting edge software
> - A separate Linux gateway for protocol bridging
> - A separate cellular modem for remote sites without wired connectivity
> - The need to host EdgeConnect on customer-owned Windows infrastructure (Edge Gateway, when EdgeConnect Linux ships, eliminates this)
> - In-house engineering effort to maintain custom protocol drivers across firmware updates
> - The per-machine custom-mapping tax that drifts when controllers change

---

### 3.4 Section 4 — Strategic adjacencies

> EYEBROW: WHO IT'S FOR · WHERE IT DEPLOYS
>
> COLUMN 1 — BUYERS:
> - **OT Architect / SCADA engineer** — designs the integration; owns protocol decisions
> - **Plant engineer (retrofit / greenfield)** — sizes the deployment; selects Edge Gateway hardware
> - **Industrial IT lead** — evaluates the edge runtime against existing SCADA / historian / MES infrastructure
>
> COLUMN 2 — INDUSTRIES:
> - Manufacturing — discrete (CNC, machining, automotive parts)
> - Manufacturing — process (flow / temperature / pressure analytics)
> - Oil & Gas (pipeline monitoring, surface and downhole hydraulic systems)
> - Power & Energy (substation telemetry, generation-equipment monitoring)
> - Water & Utilities (pump-station monitoring, flow + pressure analytics)
> - OEM machine monitoring (service-hours billing, warranty, remote fleet)
>
> COLUMN 3 — DEPLOYMENT FOOTPRINT:
> - Operating across India and the Middle East
> - Offline-capable; air-gapped factories are first-class
> - Multi-site fleets: one Edge Gateway per plant + one Edge runtime per plant; per-gateway identity from first start

---

### 3.5 Section 5 — Where this fits in the Industrial Intelligence Stack

> EYEBROW: WHERE IT FITS
>
> SECTION TITLE:
> Connectivity & Edge is the connectivity layer of the Industrial Intelligence Stack.
>
> CALLOUT — HOW THIS DIFFERS FROM A GENERAL-PURPOSE IOT GATEWAY (size.base, single paragraph; visual treatment: light tinted card or left-rule callout):
>
> > **How this differs from a general-purpose IoT gateway.** EdgeConnect + Edge Gateway **normalize signals to canonical CNC vocabulary at the edge** — every signal arrives at every sink in the same shape, regardless of which controller produced it. Most IoT gateways pass protocol bytes through to the cloud, which forces every downstream sink to re-interpret the same vendor encoding. Per-route store-and-forward survives connectivity gaps without losing source ordering. Three-way diagnostics (source / pipeline / sink) tell you immediately where the data path broke.
>
> BODY:
> Pillars 2-4 (Data Acquisition, Asset Intelligence, Condition Monitoring) capture field signals from sensors, mobile assets, and condition-monitoring instruments. Pillar 1 (Connectivity & Edge) integrates those signals — plus everything coming from existing PLCs and controllers — into one canonical data layer. From there, signals flow to Pillar 5 (Operational Intelligence) for OEE / alarms / reports, to external SCADA / MES / cloud platforms via OPC UA Server / MQTT / HTTP, or stay local for offline operation.
>
> DIAGRAM FRAME (DiagramFrame focused on Pillar 1 layer of architecture-diagram-v2)
>
> CAPTION:
> *Pillar 1 is the data-layer foundation. See the full Industrial Intelligence Stack → `/architecture`*

---

### 3.6 Section 6 — Trust posture for this pillar

Per §16 trust cue content pattern (design-system v3 §16). Cue focus per buyer-taxonomy v1 §2.3 + §2.5: offline-first operation, no forced cloud dependency.

> EYEBROW: TRUST POSTURE
>
> BODY:
> EdgeConnect and Edge Gateway both run offline by default. License validates locally — no phone-home. Cloud connectivity is opt-in, not required. Plants on isolated OT VLANs install and run the platform the same way as plants with internet access. Hash-chained configuration audit captures every change to the gateway with actor identity and timestamp — tamper-evident, replay-ready. Per-tag quality codes propagate end-to-end so downstream consumers can distinguish a real signal from a stale one.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.7 Section 7 — Related solutions

> EYEBROW: RELATED SOLUTIONS

#### Card 1 — Edge Connectivity (primary)

> EYEBROW: SOLUTION · EDGE CONNECTIVITY
> TITLE: One operational view across every controller on your floor
> BODY: How EdgeConnect + Edge Gateway + EREMOS V2 work together to consolidate mixed-vendor controllers into one operational data layer.
> LINK: Read the solution → `/solutions/edge-connectivity`

#### Card 2 — CNC Machining (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · CNC MACHINING
> TITLE: Mixed-vendor CNC floors on one operational view
> BODY: For shops running mixed Fanuc / Brother / Mazak controllers — one canonical CNC vocabulary across every vendor.
> LINK: Read the solution → `/solutions/cnc-machining` *(existing v2; v3 in Phase E)*

---

### 3.8 Section 8 — Cross-lens navigation

Per §17 preset for `/capabilities/<pillar>` (design-system v3 §17 + memo v2 §5.2):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | ARCHITECTURE | How does this pillar fit the data flow? | `/architecture` |
| 2 | SOLUTION · EDGE CONNECTIVITY | The outcome-organised version of this pillar | `/solutions/edge-connectivity` |
| 3 | CAPABILITIES | Back to all 5 pillars | `/capabilities` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.3 OT Architect + §2.5 Plant engineer CTA preferences:

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Talk to an engineer about your specific controller mix.
>
> SUBHEAD:
> Whether you're scoping a retrofit, a greenfield install, or a multi-site standardization — bring us the controller mix and we'll scope the deployment shape. Demos run on real protocols against real signals, not slideware.
>
> PRIMARY CTA: Talk to an engineer about Connectivity & Edge
> HREF: `/contact?intent=connectivity-edge-engineering`
>
> SECONDARY CTA: Request an architecture review
> HREF: `/contact?intent=architecture-review`

---

## 4. Components used

All from design-system v3 LOCKED — no new components introduced.

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `CapabilityCard` (pillar-1 accent + compact variants) | §3.2 products; §3.7 related solutions; §3.8 cross-lens |
| `Button` (primary + secondary, size lg) | §3.1 hero; §3.9 final CTA |
| `DiagramFrame` | §3.5 stack diagram |
| `CTASection` | §3.9 final CTA |
| §16 trust cue content pattern | §3.6 trust posture |
| §17 cross-lens content pattern | §3.8 cross-lens |

---

## 5. Verbatim copy summary

For the engineering team and copywriters, all page copy in one place — same structure as the condition-monitoring spec §5 (collected per-section). See sections §3.1-§3.9 above for the verbatim content; the copy is structured for direct lift into the Angular `CapabilityDeepDive` component instance.

**Total page copy:** ~1,005 words (within 800-1,200 target). Increase from v1 baseline (~950 words) reflects v2 R1 §3.2 unifying intro + R3 new §6 anti-pattern. R2 callout sharpening was a same-word-count swap.

---

## 6. Anti-patterns specific to this page

In addition to system-wide anti-patterns from design-system v3 §21 and the §6 anti-patterns precedent locked in the condition-monitoring spec:

| Don't | Why |
|---|---|
| Omit the Edge Gateway dual-identity story (today standalone + tomorrow EdgeConnect appliance) | The dual-identity IS the commercial signal for this pillar — eliminates the perception of "two separate purchases vs one growth path" |
| Imply EdgeConnect Linux is current behavior | Per positioning v3 — today Windows, Linux on roadmap. Honest framing required. |
| Use *"seamless"* / *"intuitive"* / *"easy"* | Vocabulary backfires per buyer-taxonomy v1 §2.3 — destroys credibility with OT Architect on first read |
| List specific Fanuc / Brother / Mazak models in card bodies | Protocol-level coverage stays at the protocol level; specific controller model details belong on Phase E product pages (`/edgeconnect`) and on `/solutions/cnc-machining` |
| Add per-protocol detail tables | Protocol coverage table lives on `/edgeconnect` (Phase E) and is referenced from `/architecture`; capability page stays capability-level |
| Add cloud-vendor logos (AWS / Azure / GCP) | Per design-governance §2.3 + proof-architecture §3 — no vendor logos on capability pages |
| Add competitor names (Kepware, Ignition, etc.) | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory, not capability page |
| Present Edge Gateway as the required deployment path for EdgeConnect | Connectivity & Edge must remain deployable both as software-only and appliance-based. The appliance is an option, not a requirement — protects against accidental "buy the box" framing drift in future copy revisions |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 800-1,200 word target (current: ~1,005 words)
- [x] All 9 sections present per CapabilityDeepDive layout
- [x] Hero customer question uses verbatim text from hardware-ecosystem-map v3 §1
- [x] EdgeConnect + Edge Gateway product cards use accurate descriptions from hardware-ecosystem-map v3 §2.1 + §2.2
- [x] Edge Gateway dual-identity story explicit in §3.2 card body (today standalone + tomorrow EdgeConnect appliance)
- [x] EdgeConnect Linux roadmap framing honest (Windows today)
- [x] §3.6 trust cue focuses on offline-first + no-phone-home per buyer-taxonomy §2.3
- [x] §3.5 diagram is DiagramFrame focused on Pillar 1 (NOT full architecture diagram)
- [x] Final CTA uses OT-Architect-preferred framings
- [x] No vocabulary that backfires per buyer-taxonomy v1 §2.3
- [x] No customer logos, no fabricated metrics, no competitor names, no cloud-vendor logos
- [x] All components are design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] §3.5 "How this differs from a general-purpose IoT gateway" callout present per §9 emerging-pattern governance
- [x] **v2 R1 applied** — §3.2 section intro frames "One capability, two deployment surfaces" (ChatGPT: "the most important refinement before lock")
- [x] **v2 R2 applied** — §3.5 callout opens with the canonical-normalization differentiator instead of the IoT-gateway contrast
- [x] **v2 R3 applied** — §6 anti-patterns now explicitly guard against "Edge Gateway as required deployment path for EdgeConnect" copy drift

---

## 8. Out of scope for v1

- **Full EdgeConnect product detail.** Phase E `/edgeconnect` page covers: full protocol coverage table, Connectivity Studio UI screenshots, OPC UA Server security mode details, integration test patterns.
- **Full Edge Gateway product detail.** Phase E `/edge-gateway` page covers: full hardware specs, environmental certifications (CE / UL / FCC / IP rating — open question per positioning v3 §11), enclosure dimensions, mounting details.
- **Solution narratives.** `/solutions/edge-connectivity` (Phase 2 step 10) covers the outcome-organised story. `/solutions/cnc-machining` (existing v2; v3 in Phase E) covers the CNC-specific outcome.
- **Industries-specific framings.** Phase 3 `/industries/<industry>`.
- **Pricing detail.** Phase 3 `/pricing` or commercial-engagement teaser on `/platform`.
- **SCADA / MES / historian coexistence patterns.** Lives on `/architecture` (integration patterns) and `/solutions/<solution>` (deployment narratives).

---

*`/capabilities/connectivity-edge` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review (verdict: "Approve after a small v2 refinement pass — the only area that needs additional work before lock is the dual-product framing"). Third per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 5. Content-only spec — inherits the `CapabilityDeepDive` layout confirmed by `page-capabilities-condition-monitoring-spec-v1` (LOCKED). Same 9-section structure, content adapted to Pillar 1 (EdgeConnect + Edge Gateway). Inherits §1-§8 canonical structure from `page-capabilities-hub-spec-v1.md §9`. **v2 changes from v1:** R1 §3.2 section intro ("One capability, two deployment surfaces") — ChatGPT's "single most important refinement before lock"; R2 §3.5 IoT-gateway callout sharpened (leads with the canonical-normalization differentiator); R3 new §6 anti-pattern guarding against Edge-Gateway-as-required-deployment-path drift. Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.3 + §2.5, proof-architecture v1, design-system v3 §14 + §16 + §17, hardware-ecosystem-map v3 §2, positioning v3.*
