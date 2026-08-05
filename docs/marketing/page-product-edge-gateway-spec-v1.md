<!--
File:        docs/marketing/page-product-edge-gateway-spec-v1.md
Purpose:     Page spec for /edge-gateway — the PRODUCT detail page for the Edge
             Gateway (Pillar 1, Connectivity & Edge HARDWARE appliance).
             SHAPE-SETTER for the Phase E HARDWARE product pages: the first page
             on the §24.B hardware ProductDetail variant. mDAQ / mTracker / VAS /
             E-IDOS inherit §24.B next.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E hardware product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the ProductDetail
             HARDWARE layout (design-system-v4.md §24.B).
Companion:   design-system-v4.md §24.B (the hardware
                variant this instantiates) + §24 / §24.A / §24.3
             page-product-edgeconnect-spec-v1.md (LOCKED — the software sibling;
                Edge Gateway IS the EdgeConnect appliance once Linux ships)
             hardware-ecosystem-map-v3.md §2.2 (source-of-truth for the Edge
                Gateway specs + dual identity + BOM-elimination)
             page-capabilities-connectivity-edge-spec-v1.md v2.1 (LOCKED — the
                Pillar 1 capability story; cross-link UP)
             page-solutions-edge-connectivity-spec-v1.md v2.1 (LOCKED — the
                solution that deploys the appliance)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.5 (Plant engineer — primary) + §2.3 (OT
                Architect — secondary)
             proof-architecture-v1.md §3/§4/§8 (no fabricated specs / customer
                names / competitor names)
             elpis-industrial-intelligence-platform-v5.md (datasheet — Edge
                Gateway mentioned as the optional appliance)
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v2 — LOCKED 2026-06-04 (ChatGPT review "Approve with changes" +
                  focused cert/IP re-review, both applied). §24.B HARDWARE
                  shape-setter.
Date:        2026-06-04
Status:      LOCKED 2026-06-04 (§24.B HARDWARE shape-setter). Two ChatGPT
                  passes — full review + focused cert/IP re-review — both
                  PASSED; cert/IP (no formal claims; IP65/IP67-compatible, not
                  certified/rated) + industrial-PC lifecycle wording final.
                  mDAQ / mTracker / VAS / E-IDOS inherit §24.B.

ChatGPT review (2026-06-04) — "Approve with changes." §24.B hardware adaptation
confirmed correct; Plant-engineer buyer + "Get hardware specifications" /
"Request a BOM scope" CTA kept. v2 applied:
  - CERT/IP (user + ChatGPT): no formal cert CLAIMS, but cert/IP/site-compliance
    handled case-by-case during BOM scope; products are IP65 / IP67-COMPATIBLE
    (NOT certified/rated). Added an ingress-protection spec row + operating-
    environment row; rewrote §3.9 Q6; updated §3.8, §6 anti-patterns, §24.B.0.
    Forbidden: "IP65/IP67 certified", "IP-rated", "CE/UL/FCC/IEC certified".
  - Tightened "no separate industrial PC" everywhere → "for standalone Modbus
    TCP + cellular gateway use today" (doesn't imply the full EdgeConnect runtime
    runs on the box today).
  - Softened "PLC-to-cloud" → "PLC-to-network" (meta title, hero, §3.2, §3.6).
  - Added hardware scoping rows (power/terminal, SIM/carrier/antenna, network/
    firewall, cabinet clearance, operating environment) — "confirmed during BOM
    scope", no invented numbers.
  - Added §3.4 Onboard-I/O row + §3.9 Q8 (gateway ≠ DAQ → mDAQ) and Q9 (outdoor
    / exposed mounting).
  - Added §3.6 OPTIONAL APPLIANCE 4th annotation.
  - §3.7 "How to buy" reworded to BOM/scoping (no pricing/SKU/per-unit).
  Kept (per ChatGPT): Plant-engineer buyer, the two CTAs, dual identity, today =
  Modbus TCP only, appliance optionality, /edgeconnect cross-link.

SHAPE-SETTER ROLE. First page on the §24.B HARDWARE ProductDetail variant
(design-system-v4.md §24.B, PROPOSAL). The other 4
hardware pages (mDAQ, mTracker, VAS, E-IDOS) inherit §24.B.

GOVERNANCE LOCKS (user direction 2026-06-04, recorded in §24.B.0):
  - PHASE: hardware product pages are PHASE E (reconciles design-system v3
    line 278 "Phase 3").
  - CERTIFICATIONS (nuanced — user + ChatGPT 2026-06-04): the Elpis hardware
    products carry NO formal third-party certifications currently, but Elpis IS
    open to pursuing them case-by-case during BOM scope. Products are IP65 /
    IP67-COMPATIBLE but NOT separately certified. So: no formal cert CLAIMS; an
    ingress-protection spec row is allowed using "IP65 / IP67-compatible"
    wording (NOT "certified" / "rated" / "IP-rated"); certification, ingress-
    protection, and site-compliance are handled case-by-case during BOM scope;
    certified/rated claims are published only when formal evidence exists for
    the specific product/configuration. (Resolves hardware-ecosystem-map §264.)
  - BUYER (P-H): Plant engineer (retrofit / greenfield), buyer-taxonomy §2.5 —
    the spec-sheet / BOM / field-wiring buyer. CTA = "Get hardware
    specifications" / "Request a BOM scope" (§2.5-endorsed; NOT "Request an
    architecture review", NOT "Book a scoping call"). Secondary: OT Architect
    (§2.3).
  - SPECS trace to hardware-ecosystem-map v3 §2.2 — VERIFY before external
    publish; no invented numbers (proof-architecture §3).

Source-of-truth alignment:
  - DUAL IDENTITY (honest): TODAY a standalone PLC-to-network appliance with
    built-in Modbus TCP + cellular publish; TOMORROW the canonical EdgeConnect
    appliance once EdgeConnect Linux ships (same hardware, two lifecycles).
    Never positioned as required for EdgeConnect, nor collapsed into one
    "EdgeConnect Gateway" product (connectivity-edge v2 §6).
  - Today's built-in protocol = Modbus TCP (server/client). Broader protocol
    coverage arrives with the EdgeConnect runtime (Linux roadmap) — do NOT claim
    the full EdgeConnect protocol matrix on the standalone appliance today.
  - The §4 "spec matrix" is the §24.B HARDWARE specifications table (physical),
    NOT a protocol matrix. NO certifications row.

What §24.B swaps vs §24 software (see §24.B.1): Capabilities → What it does +
what it replaces (BOM); Spec matrix → Hardware specifications; Deployment →
Deployment in the field; Editions → How to buy; Trust+security → Field-readiness
(NO cert claims).

Word-count target: 1,200-1,800 words page copy (spec tables not prose-counted).
Post-v2 draft ~1,600 words (+2 FAQ + scoping rows + ingress/IP wording).

Note: a /edge-gateway static mockup can be derived from edgeconnect.html once the
§24.B shape locks (the hardware spec-table + a device hero visual).
-->

# `/edge-gateway` — Page Spec v1 (§24.B HARDWARE shape-setter)

**Product detail page for the Edge Gateway — the ruggedized industrial appliance for Connectivity & Edge (Pillar 1, hardware). The deepest factual surface for the box: what it does, what it removes from your BOM, the full hardware specifications, how it deploys in the field, and how to buy. First page on the §24.B HARDWARE ProductDetail variant.**

This is where a **Plant engineer** lands when they want to know **what the Edge Gateway is, physically** — dimensions, power, connectivity, what it bridges, how it mounts, and how it fits the EdgeConnect story. It is **not** the capability page (`/capabilities/connectivity-edge`) and **not** the EdgeConnect software product page (`/edgeconnect`); it is the **appliance's product truth**.

Target length: **1,200-1,800 words page copy** per §24.B (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The Edge Gateway product detail page. Reader leaves with *"I now know the box's dimensions, power, connectivity, and what it bridges; what it removes from my BOM; how it mounts and updates in the field; how it relates to EdgeConnect today vs. tomorrow; and what to ask for to scope it into a deployment."*

**IS NOT:**
- The capability page (`/capabilities/connectivity-edge`, LOCKED v2.1 — covers EdgeConnect + Edge Gateway as the Pillar 1 *capability* story; cross-link up)
- The EdgeConnect software product page (`/edgeconnect`, LOCKED — the runtime; the Edge Gateway becomes its appliance once Linux ships; cross-link across)
- A solution / outcome page (`/solutions/edge-connectivity` covers the outcome; cross-link)
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3 — this page is "how to buy" mechanics, not pricing tables)
- A heavy certifications datasheet — **no formal certifications are currently claimed; certification, ingress-protection (IP65 / IP67-*compatible*), and site-compliance are handled case-by-case during BOM scope, not as a cert section** (§24.B.0)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.B.0)

**Primary buyer:** Plant engineer (retrofit / greenfield) (§2.5) — selects hardware, designs field wiring, owns the deployment-day checklist; reads spec sheets in the morning and pulls cable in the afternoon.
- Lands here from `/capabilities/connectivity-edge` (cross-link for hardware detail), the Platform menu, or a search for *"DIN-rail Modbus TCP gateway"* / *"24V industrial PLC gateway appliance"* / *"ruggedized edge gateway cellular"*
- Wants: dimensions / power / mounting, the connectivity (cellular, Ethernet, Wi-Fi), what protocols it bridges today, firmware-update mechanics, offline behavior, and how it reduces the BOM
- CTA preference: *"Get hardware specifications"* > *"Request a BOM scope"* > *"Talk to an engineer about Connectivity & Edge"*. **NOT** *"Request an architecture review"* (OT-Architect framing) or *"Book a scoping call"* (§2.5 backfires; precedent P-H)
- Vocabulary that lands: *DIN-rail mount*, *24 V DC*, *ruggedized*, *Modbus TCP*, *4G / Wi-Fi / Ethernet*, *embedded Linux*, *USB firmware*, *PLC-fronted*, *retrofit-friendly*, *BOM*
- Vocabulary that backfires: *"platform"* / *"ecosystem"* (too abstract — they want the box), *"cloud-native"* (deal-breaker if the site has no internet), *"solution"*, marketing abstraction

**Secondary buyer:** OT Architect / SCADA engineer (§2.3) — validating the appliance against the broader EdgeConnect architecture; served via the §3.6 architecture section + cross-lens.

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Hardware-product-page pattern (first instance; mDAQ / mTracker / VAS / E-IDOS inherit).

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Edge Gateway — Ruggedized PLC-to-Network Appliance · Elpis* |
| **Meta description** (140-160 chars) | *Ruggedized DIN-rail edge gateway: embedded Linux, built-in Modbus TCP, 4G / Wi-Fi / Ethernet, 24 V DC. The canonical EdgeConnect appliance once Linux ships.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/edge-gateway` |
| **Schema intent** | `schema.org/Product` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/edgeconnect` + `/capabilities/connectivity-edge` + `/architecture` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` HARDWARE layout per §24.B (PROPOSAL). **11 sections.**

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line + CTAs + hardware hero visual | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + `hero__composite` (device/spec visual) | ~90 |
| **2** | What it is — appliance definition + dual identity + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~150 |
| **3** | What it does + what it replaces (BOM) | `dark` | `CapabilityCard` grid + BOM-elimination list | ~200 |
| **4** | Hardware specifications | `light` | §24.A spec-table (Category · Value; grouped) — **no cert row** | spec (not prose) |
| **5** | Deployment in the field | `light-tinted` | spec-table + narrative | ~130 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~90 |
| **7** | How to buy | `dark` | narrative (unit + paired software; mechanics, not pricing) | ~120 |
| **8** | Field-readiness (no certs) | `light-tinted` | trust-cue content pattern (§16), reframed | ~100 |
| **9** | Common questions (inline FAQ) — 9 Q&A | `light` | inline FAQ + `FAQPage` schema | ~470 |
| **10** | Related — cross-lens | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · CONNECTIVITY & EDGE — HARDWARE
> HEADLINE: Edge Gateway
> SUBHEAD (max-width 64ch):
> The ruggedized industrial appliance that bridges your existing PLC fleet to the network — embedded Linux, built-in Modbus TCP, cellular publish — on a DIN-rail box, no separate industrial PC for standalone gateway use today. A standalone PLC-to-network gateway today; the canonical EdgeConnect appliance once Linux ships.
>
> PRIMARY CTA (`Button.primary.lg`): Get hardware specifications → HREF `/contact?intent=edge-gateway-specs`
> SECONDARY CTA (`Button.secondary.lg`): Request a BOM scope → HREF `/contact?intent=edge-gateway-bom`
>
> TRUST STRIP (size.sm):
> DIN-rail · 24 V DC · embedded Linux · 4G / Wi-Fi / Ethernet · built-in Modbus TCP · 200 × 150 × 75 mm rugged enclosure · offline-capable.
>
> HERO VISUAL (right column, §24 hero-visual slot): a hardware-relevant SVG — a device line-art of the DIN-rail enclosure with a spec-highlight callout panel (24 V DC · Modbus TCP · 4G/Wi-Fi/Ethernet · 256 MB / 2 GB). Decorative (`aria-hidden`), token-only, "illustrative" caption.

**Anti-patterns:** Headline is the product name + value. No "Request an architecture review" / "Book a scoping call" (§2.5 → "Get hardware specifications", P-H). No formal certification claim anywhere (IP65 / IP67 stated as *compatible* only, never "certified" / "rated"; certs handled case-by-case during BOM scope). Honor the dual identity (standalone today / EdgeConnect appliance tomorrow); don't position the appliance as required for EdgeConnect.

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: One ruggedized box where the industrial PC used to be.
>
> BODY:
> The Edge Gateway is a ruggedized industrial gateway running embedded Linux. It bridges existing PLC fleets to the network over Modbus TCP, publishes over cellular, and is web-configurable with USB firmware updates — on a DIN-rail box sized for a control cabinet. No separate industrial PC is required for standalone Modbus TCP + cellular gateway use today.
>
> BODY ¶2 (muted):
> It is the appliance form of the **Connectivity & Edge** pillar — see the capability story → `/capabilities/connectivity-edge`. It has a **dual identity**: today a standalone PLC-to-network gateway (built-in Modbus TCP + cellular publish); tomorrow, once the EdgeConnect Linux runtime ships, the **canonical EdgeConnect appliance** — same hardware, two lifecycles. For the EdgeConnect runtime itself, see → `/edgeconnect`.

### 3.3 Section 3 — What it does + what it replaces

> EYEBROW: WHAT IT DOES
> SECTION TITLE: What it does — and what it removes from your BOM.

Feature cards (what it does):

> - **Bridges PLCs to the network.** Built-in Modbus TCP server/client connects existing PLC fleets; publishes over cellular.
> - **Embedded Linux, web-configurable.** Configure sources and publishing from a browser; no separate industrial PC for standalone gateway use today.
> - **Cellular + Wi-Fi + Ethernet.** Connectivity for remote sites without running new cable.
> - **USB firmware updates.** Field-serviceable without a toolchain.

What it replaces (BOM-elimination, per hardware-ecosystem-map §2.2):

> One Edge Gateway removes from the customer BOM:
> - a separate **industrial PC** for standalone Modbus TCP + cellular gateway use today;
> - a separate **Linux gateway** for protocol bridging;
> - a separate **cellular modem** for remote sites;
> - the need to **host EdgeConnect on customer-owned Windows infrastructure** once the EdgeConnect Linux runtime ships (until then, the full EdgeConnect runtime still runs software-only on Windows).

### 3.4 Section 4 — Hardware specifications (§24.A, hardware)

> EYEBROW: HARDWARE SPECIFICATIONS
> SECTION TITLE: The box, in numbers.

Spec-table per §24.A (hardware variant) — `Category | Value`, grouped. Values trace to hardware-ecosystem-map v3 §2.2 (verify before external publish). **No formal certification *claims*** — the products carry no formal third-party certifications currently (§24.B.0); the ingress-protection row uses the IP65 / IP67-*compatible* wording (a design characteristic, NOT a certified rating).

| Category | Value |
|---|---|
| **Compute** | Embedded Linux; 256 MB RAM; 2 GB Flash |
| **Power** | 24 V DC. Current draw, terminal type, and circuit-protection recommendation confirmed during BOM scope. |
| **Enclosure** | Ruggedized, 200 × 150 × 75 mm |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where a site requires it; final protection level, enclosure approach, and any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification is currently claimed.)* |
| **Mounting** | DIN-rail (control cabinet). Cabinet clearance, cable routing, and antenna placement confirmed during BOM scope. |
| **Connectivity** | 4G (cellular) · Wi-Fi · Ethernet. SIM / carrier / antenna and static-IP / DHCP / firewall / local-admin assumptions confirmed during BOM scope. |
| **Built-in protocol** | Modbus TCP (server/client) |
| **Onboard I/O** | A PLC / network gateway, **not a DAQ module** — direct analog/digital sensor acquisition is handled by **mDAQ**. If a deployment needs direct sensor acquisition, scope mDAQ alongside or instead. |
| **Management** | Web-configurable; USB firmware updates |
| **Cellular publish** | Built-in (standalone mode today) |

> CAPTION (size.sm): Physical specifications trace to the hardware ecosystem map and are confirmed at quoting time. Broader protocol coverage (FOCAS2, MTConnect, OPC UA Client, S7, …) arrives when the EdgeConnect Linux runtime ships on this hardware — see `/edgeconnect` for the runtime's full protocol matrix.

### 3.5 Section 5 — Deployment in the field

> EYEBROW: IN THE FIELD
> SECTION TITLE: How it installs.

| | |
|---|---|
| **Mounting + power** | DIN-rail mount in the control cabinet; 24 V DC industrial power. No separate industrial PC for standalone Modbus TCP + cellular gateway use today. |
| **Connectivity** | Cellular for remote sites; Wi-Fi or Ethernet where available. Publishes over cellular in standalone mode. SIM / carrier / antenna and static-IP / DHCP / firewall / local-admin assumptions confirmed during BOM scope. |
| **Protocols today** | Built-in Modbus TCP (server/client) for PLC bridging. Additional protocols arrive with the EdgeConnect Linux runtime. |
| **Firmware** | USB firmware updates — field-serviceable. |
| **Offline** | Operates without a persistent connection; cellular publish resumes when connectivity returns. |
| **Operating environment** | Temperature, humidity, exposure, and enclosure approach confirmed during BOM scope; IP65 / IP67-compatible configurations can be scoped where the placement requires it (no certified rating claimed). |
| **Field-wiring + site fit** | Enclosure dimensions, power, mounting, cabinet clearance, and antenna/cabling are confirmed during the BOM scope against the cabinet + site constraints. |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: The appliance in the stack.

`ArchitecturePanel.interactive` (product-annotated, §5.A): PLC fleet → **Edge Gateway** (highlighted; today built-in Modbus TCP + cellular publish) → cloud / MQTT → EREMOS V2. Annotation eyebrow-as-title (§24 P-E). Annotations:

| Annotated region | Eyebrow | Body |
|---|---|---|
| PLC → Edge Gateway | PLC BRIDGE | Built-in Modbus TCP connects the existing PLC fleet; no separate industrial PC or Linux gateway for standalone gateway use. |
| Edge Gateway core | DUAL IDENTITY | Standalone PLC-to-network appliance today (built-in Modbus TCP + cellular publish); the canonical EdgeConnect appliance once the Linux runtime ships — same hardware. |
| Edge Gateway → network | CELLULAR PUBLISH | Built-in cellular (4G) publishes from remote sites without new cable; offline-capable, resumes on reconnect. |
| Edge Gateway (vs. software-only) | OPTIONAL APPLIANCE | EdgeConnect also runs software-only on customer Windows hardware today. Edge Gateway is selected when the site wants a ruggedized field appliance now, and becomes the EdgeConnect appliance footprint once the Linux runtime ships. |

> CAPTION: The Edge Gateway is an **option**, not a requirement — EdgeConnect also runs software-only on customer hardware. See the runtime → `/edgeconnect`; the full stack → `/architecture`.

### 3.7 Section 7 — How to buy

> EYEBROW: HOW TO BUY
> SECTION TITLE: One unit, plus the software it grows into.
>
> *Packaging labels are illustrative until commercial packaging is approved; this section describes how the unit is bought + what it pairs with, not pricing.*
>
> BODY:
> The Edge Gateway is a hardware unit, **scoped against controller count, site connectivity, cabinet constraints, field environment, and whether the appliance is used standalone today or as the future EdgeConnect appliance footprint**. Today it ships in **standalone mode** (built-in Modbus TCP + cellular publish). When the EdgeConnect Linux runtime ships, the same hardware becomes the **canonical EdgeConnect appliance** — the appliance grows into the broader platform path without new hardware. Bring the PLC list, the site connectivity (cellular vs. wired), and the cabinet constraints; we'll scope the BOM. Contact Elpis for unit availability and BOM scoping; detailed pricing follows the BOM review. No pricing tables, SKU grids, or per-unit pricing on this page.

### 3.8 Section 8 — Field-readiness

Trust-cue content pattern (§16), reframed for hardware (NO certifications — §24.B.0):

> EYEBROW: FIELD-READINESS
>
> CUE 1 — **Built for the cabinet, not the office.** Ruggedized enclosure, 24 V DC industrial power, DIN-rail mount, embedded Linux. Sized (200 × 150 × 75 mm) to drop into a control cabinet beside the PLCs it bridges.
>
> CUE 2 — **Offline-first, remote-ready.** Operates without a persistent connection; built-in cellular publishes from remote sites and resumes on reconnect. No dependency on customer Windows infrastructure in appliance mode.
>
> *(Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, but certified/rated claims are published only when formal evidence exists for the specific product/configuration.)*

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 9 Plant-engineer questions.

> #### Q1. What are the dimensions and power?
> 200 × 150 × 75 mm ruggedized enclosure, 24 V DC, DIN-rail mount. Embedded Linux, 256 MB RAM, 2 GB Flash. See the specifications table above; exact figures are confirmed at quoting time.
>
> #### Q2. What does it bridge today vs. later?
> Today: built-in Modbus TCP (server/client) for PLC fleets, with cellular publish — standalone. The broader protocol set (FOCAS2, MTConnect, OPC UA Client, Siemens S7, …) arrives when the EdgeConnect Linux runtime ships on this hardware; see `/edgeconnect` for that matrix.
>
> #### Q3. Do I still need a separate industrial PC or a Linux gateway?
> For standalone Modbus TCP + cellular gateway use today, no — Edge Gateway combines the gateway, Linux appliance, and cellular modem in one DIN-rail box. For the full EdgeConnect runtime today, EdgeConnect still runs software-only on customer Windows hardware. Once the EdgeConnect Linux runtime ships, this same Edge Gateway hardware becomes the canonical EdgeConnect appliance footprint.
>
> #### Q4. How are firmware updates handled?
> Via USB — field-serviceable without a toolchain. Configuration is web-based.
>
> #### Q5. Does it work where there's no cable / no internet?
> Yes. Built-in cellular (4G) publishes from remote sites; Wi-Fi and Ethernet are available where present. It operates offline and resumes publishing on reconnect.
>
> #### Q6. Is Edge Gateway certified? What about IP65 / IP67?
> No formal third-party certifications are currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope. Where a site requires IP65 / IP67-class protection, Elpis can scope an IP65 / IP67-compatible configuration or enclosure approach; formal certification or rating claims are published only when the specific product/configuration has the required certification or test evidence.
>
> #### Q7. Is the appliance required to run EdgeConnect?
> No. EdgeConnect runs software-only on customer hardware (Windows today). The Edge Gateway is an **option** — a turnkey box for sites that prefer one — and becomes the canonical EdgeConnect appliance once the Linux runtime ships.
>
> #### Q8. Does it have onboard sensor I/O? How is it different from mDAQ?
> Edge Gateway is a PLC / network gateway, not a DAQ module. Direct analog/digital sensor acquisition (4–20 mA, 0–10 V, digital I/O) is handled by **mDAQ**. If your deployment needs direct sensor acquisition, scope mDAQ alongside or instead of Edge Gateway — confirmed during BOM scope.
>
> #### Q9. Can it be mounted outside a control cabinet?
> Cabinet placement, exposure, temperature, humidity, antenna placement, and ingress-protection requirements are confirmed during BOM scope. IP65 / IP67-compatible configurations can be scoped where required, but no formal certified IP rating is claimed unless evidence exists for the specific configuration.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 (adapted for a hardware product — the most useful "across" link is the paired software product, `/edgeconnect`, rather than a solution):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · CONNECTIVITY & EDGE | The Pillar 1 capability story | `/capabilities/connectivity-edge` |
| 2 | PRODUCT · EDGECONNECT | The runtime this appliance hosts (Linux roadmap) | `/edgeconnect` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your PLC fleet and your cabinet constraints.
> SUBHEAD: A PLC list, your site connectivity (cellular vs. wired), and the cabinet space you've got — that's what we scope a BOM against. We confirm the dimensions, power, mounting, and connectivity for your site, not for a brochure.
> PRIMARY CTA: Get hardware specifications → `/contact?intent=edge-gateway-specs`
> SECONDARY CTA: Request a BOM scope → `/contact?intent=edge-gateway-bom`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (§24.B composes from §24).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3 |
| `ArchitecturePanel.interactive` (product-annotated) | §3.6 |
| §24.A spec-table content pattern (hardware) | §3.4 hardware specs; §3.5 field table |
| Trust-cue content pattern (§16, reframed as field-readiness) | §3.8 |
| Cross-lens content pattern (§17) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |
| Hero visual (`hero__composite`, §24 slot) | §3.1 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,600 words page copy** (within the §24.B 1,200-1,800 target; post-v2 with +2 FAQ + the cert/IP + scoping wording). Spec-table cell text (§3.4, §3.5) + §3.6 annotations are NOT prose-counted.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.B.3 hardware anti-patterns:

| Don't | Why |
|---|---|
| Claim CE / UL / FCC / IEC / certified IP65 / certified IP67 (or "IP-rated", "certified rugged", "field certified") unless formal evidence exists for that exact product/configuration | The products carry no formal certifications currently (§24.B.0). **Allowed** wording: "IP65 / IP67-compatible configurations can be scoped during BOM review." **Forbidden:** "IP65 certified" / "IP67 certified" / "IP-rated" / "CE/UL/FCC/IEC certified" without formal documentation. Certs + IP + site-compliance are handled case-by-case during BOM scope. |
| Publish fabricated or unverified physical specs | Trace to hardware-ecosystem-map v3 §2.2; "confirmed at quoting time". |
| Claim the full EdgeConnect protocol matrix on the standalone appliance today | Today's built-in protocol is Modbus TCP; broader coverage arrives with the EdgeConnect Linux runtime (§3.4 caption, §3.9 Q2). |
| Position the Edge Gateway as required for EdgeConnect, or collapse them into one product | Dual identity + appliance-is-optional (connectivity-edge v2 §6); §3.6 caption + §3.9 Q7 guard it. |
| Use "Request an architecture review" / "Book a scoping call" as the primary CTA | §2.5 backfires; P-H — hardware page uses "Get hardware specifications" / "Request a BOM scope". |
| Use abstract platform/ecosystem language as the lead | §2.5 Plant engineer wants the box, not the strategy; lead with specs + BOM. |
| Imply the appliance phones home / needs the cloud | Offline-first; cellular publish is opt-in and resumes on reconnect (§3.8 Cue 2). |
| Customer / competitor names, fabricated metrics | proof-architecture §3/§4/§8. |
| Introduce a new visual primitive | §24.B composes from v3 components + §24.A. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,400); spec tables not prose-counted
- [x] All 11 §24.B sections present (hardware variant)
- [x] **NO formal cert claims anywhere; cert / IP / site-compliance handled case-by-case during BOM scope; IP65 / IP67 stated as *compatible* only (not "certified" / "rated"); §3.9 Q6 + Q9 use the approved wording; §3.4 ingress row + §3.5 operating-environment row present**
- [x] §3.1 hero product-led; CTA "Get hardware specifications" / "Request a BOM scope" (§2.5, P-H)
- [x] §3.2 opens with "what it is"; dual identity stated; cross-links UP to `/capabilities/connectivity-edge` + ACROSS to `/edgeconnect`
- [x] §3.3 BOM-elimination list present (industrial PC / Linux gateway / cellular modem / Windows hosting)
- [x] §3.4 hardware specs table traces to hardware-ecosystem-map v3 §2.2; "confirmed at quoting time"; NO cert row
- [x] §3.4 + §3.9 Q2: today = built-in Modbus TCP; broader protocols arrive with EdgeConnect Linux (no full-matrix overclaim on the standalone box)
- [x] §3.6 dual identity + appliance-is-optional in the architecture caption; §3.9 Q7 confirms not-required
- [x] §3.7 "how to buy" = mechanics, not pricing; labels illustrative
- [x] §3.8 field-readiness (ruggedization, offline) with NO cert claims
- [x] §3.10 cross-lens: capability + `/edgeconnect` (paired product) + `/architecture` (documented §24.3 adaptation for hardware)
- [x] Plant-engineer vocabulary (DIN-rail, 24 V DC, BOM, Modbus TCP); no "cloud-native" / abstract platform lead
- [x] No new component beyond v3 + §24.A
- [x] §1.4 metadata (`Product` schema)
- [x] **§24.B shape-setter decisions documented** for mDAQ / mTracker / VAS / E-IDOS to inherit (no-cert discipline, hardware spec-table, BOM-elimination, §2.5/§2.4 buyer + CTA)
- [x] Specs VERIFIED against hardware-ecosystem-map v3 before external publish
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Certifications.** None currently; no section (§24.B.0).
- **Full EdgeConnect protocol matrix.** `/edgeconnect` (LOCKED) — the runtime; cross-link.
- **The other hardware products.** mDAQ / mTracker / VAS / E-IDOS — each its own §24.B page.
- **Capability + solution narratives.** `/capabilities/connectivity-edge` + `/solutions/edge-connectivity` (LOCKED) — cross-link.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3). "How to buy" mechanics only here.
- **Exact electrical / environmental tolerances beyond the map anchors.** Confirmed at quoting time; not published until verified.

---

*`/edge-gateway` Page Spec **v2 LOCKED 2026-06-04** (§24.B HARDWARE ProductDetail shape-setter), after ChatGPT review ("Approve with changes") + a focused cert/IP re-review (both passed; cert/IP + industrial-PC lifecycle wording final). First page on the hardware variant of the ProductDetail shape (design-system-v4.md §24.B). 11-section hardware composition; no new visual primitive (v3 components + §24.A spec-table). Governance locks (user direction 2026-06-04): hardware pages = PHASE E (reconciles design-system v3 line 278); NO formal certification CLAIMS — cert / IP / site-compliance handled case-by-case during BOM scope, products IP65 / IP67-compatible (NOT certified/rated); resolves §264; Plant-engineer buyer (§2.5) → "Get hardware specifications" / "Request a BOM scope" CTA (P-H). Specs trace to hardware-ecosystem-map v3 §2.2 (verify before external publish). Dual identity honest (standalone PLC-to-cloud today; canonical EdgeConnect appliance once Linux ships); appliance is optional, not required; today's built-in protocol = Modbus TCP (no full-EdgeConnect-matrix overclaim on the standalone box). Next: user + ChatGPT review → lock → mDAQ / mTracker / VAS / E-IDOS inherit §24.B. Cites: design-system-v4 §24.B/§24.A/§24.3, hardware-ecosystem-map-v3 §2.2, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.5/§2.3, proof-architecture-v1 §3/§4/§8, page-product-edgeconnect-spec-v1 (software sibling), page-capabilities-connectivity-edge-spec-v1 v2.1, page-solutions-edge-connectivity-spec-v1 v2.1, page-architecture-spec-v1 v2.1, elpis-industrial-intelligence-platform-v5, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
