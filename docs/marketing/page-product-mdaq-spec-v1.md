<!--
File:        docs/marketing/page-product-mdaq-spec-v1.md
Purpose:     Page spec for /mdaq — the PRODUCT detail page for mDAQ (Pillar 2,
             Data Acquisition HARDWARE — ruggedized field data-acquisition
             device). Second hardware product page; INHERITS the LOCKED §24.B
             hardware ProductDetail variant proven by /edge-gateway.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E hardware product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the LOCKED §24.B
             hardware ProductDetail layout (design-system-v4.md §24.B). Inherits the /edge-gateway shape-setter.
Companion:   design-system-v4.md §24.B (LOCKED — the
                hardware variant this inherits) + §24 / §24.A / §24.3
             page-product-edge-gateway-spec-v1.md (LOCKED — the §24.B shape-
                setter; mirror its structure + cert/IP discipline)
             hardware-ecosystem-map-v3.md §3.1 (source-of-truth for the mDAQ
                specs + BOM-elimination + "field-replacement" positioning)
             page-capabilities-data-acquisition-spec-v1.md v2 (LOCKED — the
                Pillar 2 capability story; cross-link UP)
             page-capabilities-condition-monitoring-spec-v1.md v1 (LOCKED — VAS
                is built on the mDAQ platform; cross-link)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.5 (Plant engineer — primary, greenfield /
                PLC-bypass / direct sensor acquisition) + §2.3 (OT Architect —
                secondary)
             proof-architecture-v1.md §3/§4/§8
             elpis-industrial-intelligence-platform-v5.md (datasheet)
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v2 — LOCKED 2026-06-04 after ChatGPT review (verdict "Lock after
                  revisions"; all precision-hardening edits applied). Inherits §24.B.
Date:        2026-06-04
Status:      LOCKED (Track B hardware; inherits §24.B). Confirms §24.B
                  generalizes from a PLC-bridge appliance to a direct-acquisition
                  DAQ device. mTracker / VAS / E-IDOS next.

ChatGPT review (2026-06-04) — "Lock after revisions" (strong draft; no structural
rework; cert/IP discipline + Edge-Gateway contrast + VAS handling all endorsed).
v2 applied:
  - P0: qualified "replaces the PLC" → "removes the need for a PLC when the job is
    direct sensor acquisition + publishing" (it does NOT replace PLC control /
    safety / sequencing / interlock) — §1.1, §3.2, §3.9 Q2.
  - P1: added a "DAQ details confirmed during BOM scope" mini-table after §3.4
    (analog config, sampling/reporting, digital I/O, power, mechanical,
    environment) — surfaces the deployment-specific detail a DAQ buyer expects,
    no invented numbers.
  - P1: clarified sampling → "up to 860 S/s; per-channel / aggregate + reporting
    interval confirmed during BOM scope" (§3.4 + §3.9 Q1).
  - P1: softened digital-output "control signals" → "status, counts, alarms,
    enables, and simple discrete signals" (not deterministic control).
  - P2: VAS wording → "built on the mDAQ hardware platform, configured for
    specialized vibration acquisition + analytics" (not "same hardware"/identical
    SKU).
  - P2: FAQ Q6 "IP65/IP67-class" → "IP65/IP67-compatible".
  Cert/IP discipline confirmed consistent with locked §24.B — unchanged.

INHERITS §24.B from the /edge-gateway shape-setter. Same 11-section hardware
ProductDetail composition; no new component (v3 components + §24.A spec-table).
This spec mirrors page-product-edge-gateway-spec-v1.md structure + disciplines,
swapping in the mDAQ (Data Acquisition) product content.

GOVERNANCE LOCKS INHERITED FROM §24.B (carried verbatim in discipline):
  - CERTIFICATIONS / IP: NO formal third-party certification CLAIMS; cert / IP /
    site-compliance handled case-by-case during BOM scope; products are IP65 /
    IP67-COMPATIBLE but NOT separately certified. Allowed: "IP65 / IP67-
    compatible configurations can be scoped during BOM review." Forbidden:
    "IP65/IP67 certified", "IP-rated", "CE/UL/FCC/IEC certified", "certified
    rugged", "field certified" (unless formal evidence exists).
  - PHASE E; Plant-engineer buyer (§2.5) → CTA "Get hardware specifications" /
    "Request a BOM scope" (P-H); NO "Request an architecture review" / "Book a
    scoping call".
  - SPECS trace to hardware-ecosystem-map v3 §3.1 — VERIFY before external
    publish; no invented numbers (proof-architecture §3). (mDAQ specs are NOT
    flagged "orientation only" in the map — firmer than VAS/E-IDOS — but still
    confirm before publish.)

mDAQ-SPECIFIC positioning (hardware-ecosystem-map §3.1):
  - mDAQ is the **field-replacement** product: it captures industrial sensor
    signals (pressure, flow, level, temperature, …) **directly from the field,
    with no PLC in the loop**. This is the key contrast with the Edge Gateway
    (which BRIDGES existing PLCs). Where Edge Gateway answers "I have PLCs,"
    mDAQ answers "I have sensors and no PLC — or I want to bypass the PLC."
  - Combined with EdgeConnect + EREMOS V2, mDAQ completes an **Elpis-only
    signal-to-dashboard path** with no third-party hardware in the chain.
  - **VAS is built on the mDAQ hardware platform** (configured for specialized
    vibration acquisition + analytics — not necessarily an identical SKU) —
    cross-link to Condition Monitoring.
  - Has a small acquisition/publish protocol story (Modbus TCP/RTU acquisition;
    HTTPS / MQTT publish) — the §4 spec table carries it as an I/O + protocol
    section, not a full EdgeConnect protocol matrix.

Word-count target: 1,200-1,800 words page copy. Post-v2 draft ~1,560 words.

Note: a /mdaq static mockup can be derived from edge-gateway.html (the §24.B
hardware shape) once this spec locks.
-->

# `/mdaq` — Page Spec v1 (§24.B hardware; inherits the /edge-gateway shape-setter)

**Product detail page for mDAQ — the ruggedized field data-acquisition device (Pillar 2, Data Acquisition). The deepest factual surface for the box: what it does (direct sensor acquisition, no PLC), what it removes from your BOM, the full hardware specifications + I/O, how it deploys in the field, and how to buy. Second page on the LOCKED §24.B HARDWARE ProductDetail variant.**

This is where a **Plant engineer** lands when they want to know **what mDAQ is, physically** — analog/digital channels, ranges, sampling, connectivity, environmental, mounting — when there's no PLC in the loop, or when they want to bypass it. It is **not** the capability page (`/capabilities/data-acquisition`) and **not** a solution page; it is the **device's product truth**.

Target length: **1,200-1,800 words page copy** per §24.B (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The mDAQ product detail page. Reader leaves with *"I now know mDAQ's analog + digital channels, ranges and sampling, connectivity, environmental range, and mounting; that it acquires sensor signals directly with no PLC; what it removes from my BOM; and what to ask for to scope it into a deployment."*

**IS NOT:**
- The capability page (`/capabilities/data-acquisition`, LOCKED — the Pillar 2 *capability* story; cross-link up)
- The Edge Gateway page (`/edge-gateway`, LOCKED — that **bridges existing PLCs**; mDAQ **removes the need for a PLC when the job is direct sensor acquisition + publishing** — see §3.2)
- The VAS page (VAS is the vibration application **built on the mDAQ platform**; its own §24.B page)
- A solution / outcome page
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3 — "how to buy" mechanics, not pricing tables)
- A certifications datasheet — **no formal certifications are currently claimed; cert / ingress-protection (IP65 / IP67-*compatible*) / site-compliance are handled case-by-case during BOM scope** (§24.B.0)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.B.0)

**Primary buyer:** Plant engineer (retrofit / greenfield) (§2.5) — selects hardware, designs field wiring, owns the deployment-day checklist; here specifically the **direct-sensor-acquisition / PLC-bypass** case.
- Lands here from `/capabilities/data-acquisition` (cross-link for hardware detail), the Platform menu, or a search for *"4-20 mA cellular data logger"* / *"field DAQ no PLC"* / *"ruggedized IoT acquisition 24V"*
- Wants: analog channel count + ranges (0–10 V / 4–20 mA), resolution + sampling, digital I/O, acquisition protocols (Modbus TCP/RTU), publish (HTTPS / MQTT), connectivity (cellular / Wi-Fi / GPS), environmental range, optional battery, mounting
- CTA preference: *"Get hardware specifications"* > *"Request a BOM scope"* > *"Talk to an engineer about Data Acquisition"*. **NOT** *"Request an architecture review"* / *"Book a scoping call"* (§2.5 backfires; P-H)
- Vocabulary that lands: *4–20 mA*, *0–10 V*, *16-bit*, *digital I/O*, *24 V*, *Modbus RTU*, *signal conditioning*, *loop power*, *field wiring*, *retrofit*, *no PLC*, *BOM*
- Vocabulary that backfires: *"platform"* / *"ecosystem"* (too abstract), *"cloud-native"*, *"solution"*, marketing abstraction

**Secondary buyer:** OT Architect / SCADA engineer (§2.3) — validating the Elpis-only signal-to-dashboard path; served via the §3.6 architecture section + cross-lens.

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Hardware-product-page pattern (inherits `/edge-gateway` §1.4).

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *mDAQ — Ruggedized Field Data Acquisition · Elpis* |
| **Meta description** (140-160 chars) | *Acquire sensor signals directly from the field — no PLC. 4 analog (0–10 V / 4–20 mA), digital I/O, Modbus TCP/RTU, cellular publish, 24 V, −10 to +85 °C.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/mdaq` |
| **Schema intent** | `schema.org/Product` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/data-acquisition` + `/edge-gateway` + `/architecture` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` HARDWARE layout per §24.B (LOCKED). **11 sections** (inherits `/edge-gateway`).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line + CTAs + hardware hero visual | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + `hero__composite` | ~90 |
| **2** | What it is — device definition + "no PLC" contrast + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~150 |
| **3** | What it does + what it replaces (BOM) | `dark` | `CapabilityCard` grid + BOM-elimination list | ~200 |
| **4** | Hardware specifications + I/O | `light` | §24.A spec-table (Category · Value; grouped, incl. analog/digital I/O) | spec (not prose) |
| **5** | Deployment in the field | `light-tinted` | spec-table + narrative | ~130 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~90 |
| **7** | How to buy | `dark` | narrative (unit + signal-to-dashboard path; mechanics, not pricing) | ~120 |
| **8** | Field-readiness (no cert claims) | `light-tinted` | trust-cue content pattern (§16), reframed | ~100 |
| **9** | Common questions (inline FAQ) — 8 Q&A | `light` | inline FAQ + `FAQPage` schema | ~420 |
| **10** | Related — cross-lens | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · DATA ACQUISITION — HARDWARE
> HEADLINE: mDAQ
> SUBHEAD (max-width 64ch):
> The ruggedized field data-acquisition device that captures industrial sensor signals — pressure, flow, level, temperature — **directly from the field, with no PLC in the loop**. Four analog channels, digital I/O, cellular publish, on a 24 V box sized for the field.
>
> PRIMARY CTA (`Button.primary.lg`): Get hardware specifications → HREF `/contact?intent=mdaq-specs`
> SECONDARY CTA (`Button.secondary.lg`): Request a BOM scope → HREF `/contact?intent=mdaq-bom`
>
> TRUST STRIP (size.sm):
> 4 analog (0–10 V / 4–20 mA, 16-bit) · 8 + 8 digital I/O (24 V) · Modbus TCP/RTU acquisition · HTTPS / MQTT publish · 4G / Wi-Fi / GPS · −10 to +85 °C · optional battery · IP65 / IP67-compatible (BOM-scoped).
>
> HERO VISUAL (right column, §24 hero-visual slot): a hardware-relevant SVG — a device line-art with a spec-highlight panel (4×AI · 8+8 DI/DO · 24 V · 4G/Wi-Fi/GPS · −10…+85 °C). Decorative (`aria-hidden`), token-only, "illustrative" caption.

**Anti-patterns:** Product name + value headline. No "Request an architecture review" / "Book a scoping call" (§2.5 → "Get hardware specifications", P-H). No formal certification claim (IP65/IP67 stated as *compatible* only). Don't claim mDAQ runs EdgeConnect or bridges PLCs (it's a direct-acquisition device).

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: Sensor signals, straight from the field — no PLC required.
>
> BODY:
> mDAQ is a ruggedized field data-acquisition device. It captures industrial sensor signals — pressure, flow, level, temperature, and more — directly from the field over its analog and digital inputs, with no PLC in the loop, and publishes them over cellular or Wi-Fi. It is the **field-replacement** product: where the Edge Gateway *bridges* an existing PLC fleet, mDAQ *removes the need for a PLC when the job is direct sensor acquisition and publishing*. (It is an acquisition device — it does not replace a PLC's control, safety, sequencing, or interlock functions.)
>
> BODY ¶2 (muted):
> It is the **Data Acquisition** pillar — see the capability story → `/capabilities/data-acquisition`. Combined with EdgeConnect and EREMOS V2, mDAQ completes an Elpis-only path from the sensor to the dashboard, with no third-party hardware in the chain. (The **VAS** vibration analyser is built on the mDAQ hardware platform, configured for specialized vibration acquisition and analytics — see Condition Monitoring.)

### 3.3 Section 3 — What it does + what it replaces

> EYEBROW: WHAT IT DOES
> SECTION TITLE: What it does — and what it removes from your BOM.

Feature cards (what it does):

> - **Direct sensor acquisition.** Four analog channels (0–10 V or 4–20 mA, 16-bit) capture pressure, flow, level, temperature, and similar signals straight from the field.
> - **Digital I/O.** Eight 24 V digital inputs and eight 24 V digital outputs for status, counts, alarms, enables, and simple discrete signals.
> - **Acquires and publishes.** Modbus TCP/RTU acquisition where a device speaks it; HTTPS / MQTT publish to your broker or to EREMOS V2.
> - **Remote-ready.** 4G, Wi-Fi, GPS, and an optional battery for sites without power or cable.

What it replaces (BOM-elimination, per hardware-ecosystem-map §3.1):

> One mDAQ removes from the customer BOM:
> - a **standalone PLC** for sensor acquisition;
> - a separate **cellular modem and edge appliance**;
> - a **site-specific battery backup** (available as an mDAQ option).

### 3.4 Section 4 — Hardware specifications + I/O (§24.A, hardware)

> EYEBROW: HARDWARE SPECIFICATIONS
> SECTION TITLE: The device, in numbers.

Spec-table per §24.A (hardware variant) — `Category | Value`, grouped. Values trace to hardware-ecosystem-map v3 §3.1 (verify before external publish). **No formal certification *claims*** — IP65 / IP67-*compatible* wording only (a design characteristic, not a certified rating — §24.B.0).

| Category | Value |
|---|---|
| **Analog inputs** | 4 channels, 0–10 V or 4–20 mA, 16-bit, up to 860 S/s (per-channel / aggregate behavior + reporting interval confirmed during BOM scope) |
| **Digital I/O** | 8 × 24 V digital inputs · 8 × 24 V digital outputs |
| **Acquisition protocols** | Modbus TCP / Modbus RTU |
| **Publish** | HTTPS · MQTT |
| **Connectivity** | 4G (cellular) · Wi-Fi · GPS · optional Ethernet |
| **Power** | 24 V; **optional battery** for sites without power. Current draw, terminal type, and circuit-protection recommendation confirmed during BOM scope. |
| **Environmental** | −10 °C to +85 °C operating range |
| **Enclosure** | Ruggedized, 180 × 150 × 60 mm |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where a site requires it; final protection level, enclosure approach, and any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification is currently claimed.)* |
| **Mounting** | Field mount; cabinet clearance, cable routing, and antenna placement confirmed during BOM scope |

> CAPTION (size.sm): Channel counts, ranges, sampling, and environmental figures trace to the hardware ecosystem map and are confirmed at quoting time. Sensor type, loop power, and signal-conditioning per channel are confirmed during BOM scope.

**DAQ details confirmed during BOM scope** (the deployment-specific electrical/mechanical detail a DAQ buyer expects — surfaced here in one place, not buried in captions; no invented numbers):

| Item | Confirmed during BOM scope |
|---|---|
| **Analog input configuration** | Per-channel voltage / current mode, loop power / excitation, signal conditioning, input protection, accuracy assumptions |
| **Sampling + reporting** | Whether 860 S/s is per-channel or aggregate, report interval, timestamping, buffering / offline retention; GPS as geo-context and/or time source |
| **Digital I/O** | Input thresholds, output type / current rating, sink / source behavior, protection |
| **Power** | Supply tolerance, current draw, battery runtime / charging, circuit protection |
| **Mechanical** | Mounting hardware, terminals / connectors, cable glands, antenna placement, enclosure approach, weight |
| **Environment** | Humidity / condensation, exposure, ingress-protection approach (IP65 / IP67-compatible), site compliance |

### 3.5 Section 5 — Deployment in the field

> EYEBROW: IN THE FIELD
> SECTION TITLE: How it installs.

| | |
|---|---|
| **Field wiring** | Sensors wire directly to the analog/digital inputs — no PLC in between. Per-channel sensor type, loop power, and signal conditioning confirmed during BOM scope. |
| **Power** | 24 V; optional battery for sites without mains power. Current draw / terminal / protection confirmed during BOM scope. |
| **Connectivity** | 4G cellular for remote sites; Wi-Fi where available; GPS for location/geo-context. SIM / carrier / antenna and network/firewall assumptions confirmed during BOM scope. |
| **Environment** | −10 °C to +85 °C operating range. Exposure, humidity, and enclosure approach confirmed during BOM scope; IP65 / IP67-compatible configurations can be scoped where the placement requires it (no certified rating claimed). |
| **Offline** | Acquires locally and publishes when connectivity returns. |
| **Mounting + site fit** | Enclosure dimensions, mounting, cabinet clearance, and antenna/cabling confirmed during BOM scope. |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: The device in the stack.

`ArchitecturePanel.interactive` (product-annotated, §5.A): field sensors → **mDAQ** (highlighted; direct acquisition, no PLC) → HTTPS / MQTT → EREMOS V2 (with EdgeConnect optional in the path). Annotation eyebrow-as-title (§24 P-E). Annotations:

| Annotated region | Eyebrow | Body |
|---|---|---|
| Sensors → mDAQ | DIRECT ACQUISITION | Sensors wire directly to mDAQ's analog/digital inputs — no PLC in the loop. |
| mDAQ core | FIELD-REPLACEMENT | mDAQ replaces a standalone PLC + modem + edge appliance with one ruggedized field device. |
| mDAQ → EREMOS V2 | PUBLISH | HTTPS / MQTT publish to your broker or EREMOS V2 — an Elpis-only sensor-to-dashboard path with EdgeConnect + EREMOS V2. |
| mDAQ (remote) | REMOTE-READY | 4G + GPS + optional battery for sites without power or cable; acquires offline, publishes on reconnect. |

> CAPTION: For PLC-fronted floors, the **Edge Gateway** bridges existing PLCs instead — see `/edge-gateway`. The full stack → `/architecture`.

### 3.7 Section 7 — How to buy

> EYEBROW: HOW TO BUY
> SECTION TITLE: One device, plus the path to the dashboard.
>
> *Packaging labels are illustrative until commercial packaging is approved; this section describes how the unit is bought + what it pairs with, not pricing.*
>
> BODY:
> mDAQ is a hardware unit, **scoped against channel count + sensor types, site connectivity, power/battery needs, field environment, and mounting**. On its own it acquires and publishes; combined with EdgeConnect and EREMOS V2 it completes an Elpis-only sensor-to-dashboard path. Bring the sensor list (signal types + ranges), the site connectivity (cellular vs. wired), power availability, and the environment; we'll scope the BOM. Contact Elpis for unit availability and BOM scoping; detailed pricing follows the BOM review. No pricing tables, SKU grids, or per-unit pricing on this page.

### 3.8 Section 8 — Field-readiness

Trust-cue content pattern (§16), reframed for hardware (cert/IP per §24.B.0):

> EYEBROW: FIELD-READINESS
>
> CUE 1 — **Built for the field, not the office.** Ruggedized 180 × 150 × 60 mm enclosure, 24 V power with an optional battery, −10 °C to +85 °C operating range. Wires straight to the sensors it reads.
>
> CUE 2 — **Remote-ready, offline-capable.** 4G + Wi-Fi + GPS for sites without cable; acquires locally and publishes on reconnect. No PLC or industrial PC in the loop.
>
> *(Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, but certified/rated claims are published only when formal evidence exists for the specific product/configuration.)*

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 8 Plant-engineer questions.

> #### Q1. What sensors can it read?
> Four analog channels accept 0–10 V or 4–20 mA signals (16-bit, up to 860 S/s; per-channel / aggregate behavior + reporting interval confirmed during BOM scope) — pressure, flow, level, temperature, and similar. Eight 24 V digital inputs and eight 24 V digital outputs handle status, counts, alarms, enables, and simple discrete signals. Per-channel sensor type, loop power, and signal conditioning are confirmed during BOM scope.
>
> #### Q2. How is mDAQ different from the Edge Gateway?
> Edge Gateway **bridges existing PLCs** (Modbus TCP) to the network. mDAQ **reads sensors directly with no PLC in the acquisition path** — it removes the need for a PLC when the job is acquisition and publishing (it does not replace PLC control, safety, sequencing, or interlock functions). If you already have PLCs controlling the process, scope Edge Gateway; if you have sensors and no PLC — or want a separate acquisition path that bypasses the PLC — scope mDAQ. They can also be used together.
>
> #### Q3. How does the data reach a dashboard?
> mDAQ publishes over HTTPS or MQTT to your broker or to EREMOS V2. With EdgeConnect + EREMOS V2 it forms an Elpis-only sensor-to-dashboard path with no third-party hardware in the chain.
>
> #### Q4. Can it run without mains power or a network?
> Yes. An optional battery covers sites without power; 4G cellular publishes from remote sites. It acquires locally and publishes when connectivity returns.
>
> #### Q5. What's the operating temperature and enclosure?
> −10 °C to +85 °C, in a ruggedized 180 × 150 × 60 mm enclosure. Exposure and ingress-protection requirements are confirmed during BOM scope.
>
> #### Q6. Is it certified? What about IP65 / IP67?
> No formal third-party certifications are currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope. Where a site requires IP65 / IP67-compatible protection, Elpis can scope an IP65 / IP67-compatible configuration or enclosure approach; formal certification or rating claims are published only when the specific product/configuration has the required certification or test evidence.
>
> #### Q7. How does it relate to VAS?
> VAS — the Vibration Analyser System — is built on the mDAQ hardware platform, configured for specialized vibration acquisition and analytics. If you need rotating-machinery vibration monitoring, see Condition Monitoring (VAS).
>
> #### Q8. Can it be mounted outside an enclosure / exposed?
> Cabinet placement, exposure, temperature, humidity, antenna placement, and ingress-protection requirements are confirmed during BOM scope. IP65 / IP67-compatible configurations can be scoped where required, but no formal certified IP rating is claimed unless evidence exists for the specific configuration.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 (adapted for a hardware product — Data Acquisition capability + the Condition-Monitoring pillar that VAS extends from mDAQ + architecture):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · DATA ACQUISITION | The Pillar 2 capability story | `/capabilities/data-acquisition` |
| 2 | CAPABILITY · CONDITION MONITORING | VAS is built on the mDAQ platform | `/capabilities/condition-monitoring` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us your sensors and your site.
> SUBHEAD: A sensor list (signal types + ranges), your site connectivity and power, and the environment it'll sit in — that's what we scope a BOM against. We confirm the channels, power, and mounting for your site, not for a brochure.
> PRIMARY CTA: Get hardware specifications → `/contact?intent=mdaq-specs`
> SECONDARY CTA: Request a BOM scope → `/contact?intent=mdaq-bom`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (inherits §24.B).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3 |
| `ArchitecturePanel.interactive` (product-annotated) | §3.6 |
| §24.A spec-table content pattern (hardware) | §3.4 hardware specs + I/O; §3.5 field table |
| Trust-cue content pattern (§16, reframed as field-readiness) | §3.8 |
| Cross-lens content pattern (§17) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |
| Hero visual (`hero__composite`, §24 slot) | §3.1 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,560 words page copy** (within the §24.B 1,200-1,800 target; post-ChatGPT-review). Spec-table cell text (§3.4 incl. the BOM-scope mini-table, §3.5) + §3.6 annotations are NOT prose-counted.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.B.3 hardware anti-patterns:

| Don't | Why |
|---|---|
| Claim CE / UL / FCC / IEC / certified IP65 / certified IP67 (or "IP-rated", "certified rugged", "field certified") unless formal evidence exists | Inherited §24.B.0. Allowed: "IP65 / IP67-compatible configurations can be scoped during BOM review"; certs/IP handled case-by-case during BOM scope. |
| Publish fabricated or unverified specs | Trace to hardware-ecosystem-map v3 §3.1; "confirmed at quoting time" / "during BOM scope". |
| Imply mDAQ bridges PLCs or runs EdgeConnect | mDAQ is a **direct-acquisition** device (no PLC). PLC bridging is the **Edge Gateway** (§3.2, §3.9 Q2). |
| Over-promise the sensor list as universal | Four analog (0–10 V / 4–20 mA) + digital 24 V I/O; per-channel sensor type + conditioning confirmed during BOM scope. |
| Use "Request an architecture review" / "Book a scoping call" as the primary CTA | §2.5 backfires; P-H — "Get hardware specifications" / "Request a BOM scope". |
| Use abstract platform/ecosystem language as the lead | §2.5 Plant engineer wants the box + the channels, not the strategy. |
| Customer / competitor names, fabricated metrics | proof-architecture §3/§4/§8. |
| Introduce a new visual primitive | §24.B composes from v3 components + §24.A. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,500); spec tables not prose-counted
- [x] All 11 §24.B sections present (hardware variant; inherits /edge-gateway)
- [x] **NO formal cert claims; cert / IP / site-compliance handled case-by-case during BOM scope; IP65 / IP67 stated as *compatible* only (not "certified" / "rated"); §3.9 Q6 + Q8 use the approved wording; §3.4 ingress row present**
- [x] §3.1 hero product-led; CTA "Get hardware specifications" / "Request a BOM scope" (§2.5, P-H)
- [x] §3.2 opens with "what it is"; **"no PLC / field-replacement" contrast with Edge Gateway explicit**; cross-links UP to `/capabilities/data-acquisition`
- [x] §3.3 BOM-elimination list present (standalone PLC / modem + edge appliance / battery)
- [x] §3.4 hardware + I/O specs trace to hardware-ecosystem-map v3 §3.1; analog (0–10 V / 4–20 mA, 16-bit, 860 S/s) + digital (8+8, 24 V) + protocols (Modbus TCP/RTU; HTTPS/MQTT) + −10…+85 °C + 180×150×60 mm; ingress row IP65/67-compatible
- [x] §3.6 mDAQ ≠ PLC-bridge; Edge Gateway cross-link for PLC-fronted floors; Elpis-only sensor-to-dashboard path
- [x] §3.7 "how to buy" = mechanics, not pricing; labels illustrative
- [x] §3.8 field-readiness (ruggedized, environmental, remote) with cert/IP wording per §24.B
- [x] §3.9 Q2 (vs Edge Gateway) + Q7 (VAS on mDAQ) present; Q6/Q8 cert wording approved
- [x] §3.10 cross-lens: data-acquisition + condition-monitoring (VAS) + architecture (documented §24.3 adaptation)
- [x] Plant-engineer vocabulary (4–20 mA, 0–10 V, 24 V, Modbus RTU, loop power); no "cloud-native" / abstract lead
- [x] No new component beyond v3 + §24.A; §1.4 metadata (`Product` schema)
- [x] Specs VERIFIED against hardware-ecosystem-map v3 §3.1 before external publish
- [x] **Inherited §24.B consistently** with /edge-gateway (cert/IP discipline, hardware spec-table, BOM-elimination, §2.5 buyer/CTA)
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Certifications.** None currently claimed; cert/IP handled case-by-case during BOM scope (§24.B.0).
- **PLC bridging.** That's the **Edge Gateway** (LOCKED) — cross-link.
- **VAS vibration analytics.** VAS is the application built on the mDAQ platform — its own §24.B page.
- **Capability + solution narratives.** `/capabilities/data-acquisition` + `/capabilities/condition-monitoring` (LOCKED) — cross-link.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3). "How to buy" mechanics only here.
- **Per-channel signal-conditioning / sensor-compatibility matrices.** Confirmed at BOM scope; not published until verified.

---

*`/mdaq` Page Spec **v2 LOCKED 2026-06-04** (§24.B HARDWARE ProductDetail; inherits the LOCKED §24.B variant from the /edge-gateway shape-setter) after ChatGPT review ("Lock after revisions"; precision-hardening edits applied — qualified PLC-replacement, BOM-scope mini-table, sampling clarity, softened digital-output wording, VAS/IP tightening). Second hardware product page — confirms §24.B generalizes from a PLC-bridge appliance to a direct-acquisition field device with no new component (v3 components + §24.A spec-table). mDAQ positioned as the **field-replacement** product (direct sensor acquisition, no PLC; contrast with Edge Gateway's PLC bridging); VAS is built on the mDAQ platform (cross-link to Condition Monitoring). Specs trace to hardware-ecosystem-map v3 §3.1 (4 analog 0–10 V/4–20 mA 16-bit 860 S/s; 8+8 24 V digital I/O; Modbus TCP/RTU acquisition; HTTPS/MQTT publish; 4G/Wi-Fi/GPS/optional Ethernet; −10…+85 °C; optional battery; 180×150×60 mm) — verify before external publish. Cert/IP discipline inherited from §24.B (no formal claims; IP65/IP67-compatible, not certified/rated; case-by-case during BOM scope). Plant-engineer buyer (§2.5) → "Get hardware specifications" / "Request a BOM scope" CTA (P-H). Next: user + ChatGPT review → lock → mTracker / VAS / E-IDOS inherit §24.B (VAS/E-IDOS flip to Maintenance Manager §2.4; their map specs are "orientation only" → provisional). Cites: design-system-v4 §24.B/§24.A/§24.3, page-product-edge-gateway-spec-v1 (§24.B shape-setter), hardware-ecosystem-map-v3 §3.1, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.5/§2.3, proof-architecture-v1 §3/§4/§8, page-capabilities-data-acquisition-spec-v1 v2, page-capabilities-condition-monitoring-spec-v1 v1, page-architecture-spec-v1 v2.1, elpis-industrial-intelligence-platform-v5, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
