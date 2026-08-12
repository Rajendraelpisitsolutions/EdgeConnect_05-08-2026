<!--
File:        docs/marketing/page-product-mtracker-spec-v1.md
Purpose:     Page spec for /mtracker — the PRODUCT detail page for mTracker
             (Pillar 3, Asset Intelligence HARDWARE — miniature GSM/GPS asset-
             tracking + OEE-input telemetry device). Third hardware product
             page; INHERITS the LOCKED §24.B hardware ProductDetail variant.
Audience:    Internal — Angular engineering, copywriters, user + ChatGPT
             (reviewers), Phase E hardware product-page authors.
Format:      Per §9 canonical per-page-spec template, wrapping the LOCKED §24.B
             hardware ProductDetail layout (design-system-v4.md §24.B). Inherits the /edge-gateway shape-setter + the /mdaq
             precision-hardening lessons.
Companion:   design-system-v4.md §24.B (LOCKED)
             page-product-edge-gateway-spec-v1.md + page-product-mdaq-spec-v1.md
                (LOCKED — sibling §24.B hardware pages; mirror structure + cert/IP
                + BOM-scope-mini-table + qualified-claim discipline)
             hardware-ecosystem-map-v3.md §4.1 (source-of-truth for the mTracker
                specs + BOM-elimination + strategic adjacencies)
             page-capabilities-asset-intelligence-spec-v1.md v2 (LOCKED — the
                Pillar 3 capability story; cross-link UP)
             page-solutions-oem-machine-monitoring-spec-v1.md v3 (LOCKED — the
                lead solution that uses mTracker for service-hours / warranty /
                fleet visibility; cross-link ACROSS)
             page-architecture-spec-v1.md v2.1 (LOCKED)
             buyer-taxonomy-v1.md §2.5 (Plant engineer — primary, retrofit
                install) + §2.6 (OEM machine builder — strong secondary:
                service-hours billing / warranty / fleet)
             proof-architecture-v1.md §3/§4/§8
             elpis-industrial-intelligence-platform-v5.md (datasheet)
             2026-06-04-phase-e-solution-migration-plan.md (P-A..P-H)
Version:     v2 — LOCKED 2026-06-04 after ChatGPT review (verdict "Lock after
                  revisions"; spec-table hardening applied). Inherits §24.B.
Date:        2026-06-04
Status:      LOCKED (Track B hardware; inherits §24.B). Fourth hardware page
                  (Edge Gateway + mDAQ + mTracker). VAS + E-IDOS next.

ChatGPT review (2026-06-04) — "Lock after revisions" (positioning right —
plant-engineer primary, OEM secondary, no OEE-compute overclaim, no analog-DAQ
confusion, cert/IP clean; spec table needed hardening). v2 applied:
  - P0: expanded the §3.4 main spec table — Inputs row (channel count, voltage/
    contact, counter rate, debounce, sink/source, protection — BOM-scoped),
    Connectivity row ("4G/LTE class … SIM/carrier/region/bands BOM-scoped + GPS/
    GNSS"), Power+battery row (backup-vs-primary, charging, runtime by reporting
    interval, low-power), + new Environmental + Mechanical rows (BOM-scoped).
  - P0: FAQ Q3 OEE precision — mTracker supplies the *equipment-side* inputs;
    EREMOS V2 combines them with configured context (planned time, ideal cycle,
    SKU, quality) to compute OEE.
  - P1: §3.4 mini-table + Geo-fence-behavior, Data-handling, Device-management
    rows; trust strip "GSM/GPS/4G" → "4G/LTE cellular · GPS/GNSS".
  - P2: FAQ Q2 mDAQ-distinction hardened (both touch digital → split by purpose:
    sensor values vs run-state/counts/location).
  Cert/IP discipline + OEM-secondary handling confirmed — unchanged.

INHERITS §24.B + the /mdaq precision-hardening lessons (qualified claims, a
"confirmed during BOM scope" mini-table, no overclaim). Same 11-section hardware
composition; no new component.

GOVERNANCE LOCKS INHERITED FROM §24.B:
  - CERTIFICATIONS / IP: NO formal cert CLAIMS; cert / IP / site-compliance
    case-by-case during BOM scope; IP65 / IP67-COMPATIBLE (not certified/rated).
    Forbidden cert wording per §24.B.0.
  - PHASE E; specs trace to hardware-ecosystem-map v3 §4.1 (verify before
    external publish; NOT flagged "orientation only" — firmer than VAS/E-IDOS).

BUYER (per §24.B.0 + P-H): **Plant engineer (§2.5) PRIMARY** — installs the
retrofit tracker, owns the field attachment + wiring to equipment signals. CTA =
"Get hardware specifications" / "Request a BOM scope". **OEM machine builder
(§2.6) STRONG SECONDARY** — service-hours billing, warranty triggers, fleet
visibility on shipped equipment (the lead /solutions/oem-machine-monitoring
outcome). Served via the §3.7 how-to-buy + cross-lens, not by re-targeting the
whole page.

mTracker-SPECIFIC positioning (hardware-ecosystem-map §4.1) + honesty:
  - mTracker is a **miniature GSM/GPS asset-tracking + OEE-input telemetry**
    device. It tracks utilization of industrial assets (fixed and mobile) and
    reports **OEE *inputs*** — production time, downtime, idle time, output
    quantity — derived from **equipment-level digital signals**. PRECISION:
    mTracker provides the OEE *inputs*; **EREMOS V2 computes the OEE** — do not
    claim mTracker "computes OEE."
  - DISTINGUISH from mDAQ: mDAQ = analog/digital *sensor* DAQ (4-20 mA / 0-10 V).
    mTracker = miniature *asset-tracking + digital-signal telemetry* (no analog
    channels) with GSM/GPS + geo-fence + battery, designed for **retrofit
    attachment** to equipment.
  - Strategic adjacencies: OEM machine builders (service-hours billing /
    warranty), multi-site operators tracking idle assets, geo-fenced compliance.

Word-count target: 1,200-1,800 words page copy. Post-v2 draft ~1,540 words.

Note: a /mtracker static mockup can be derived from edge-gateway.html (the §24.B
hardware shape) once this spec locks.
-->

# `/mtracker` — Page Spec v1 (§24.B hardware; inherits the /edge-gateway shape-setter)

**Product detail page for mTracker — the miniature GSM/GPS asset-tracking + OEE-input telemetry device (Pillar 3, Asset Intelligence). The deepest factual surface for the device: what it tracks, what it removes from your BOM, the full hardware specifications, how it retrofits onto equipment, and how to buy. Third page on the LOCKED §24.B HARDWARE ProductDetail variant.**

This is where a **Plant engineer** lands when they want to know **what mTracker is, physically** — connectivity, equipment-signal inputs, battery, geo-fence, retrofit attachment — to put utilization + OEE inputs on assets that don't report them today. It is **not** the capability page (`/capabilities/asset-intelligence`) and **not** a solution page; it is the **device's product truth**.

Target length: **1,200-1,800 words page copy** per §24.B (spec tables not prose-counted).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The mTracker product detail page. Reader leaves with *"I now know mTracker's connectivity (GSM/GPS), the equipment signals it reads, its battery and geo-fence, and that it retrofits onto fixed or mobile assets; what it removes from my BOM; that it feeds OEE *inputs* to EREMOS V2; and what to ask for to scope it."*

**IS NOT:**
- The capability page (`/capabilities/asset-intelligence`, LOCKED — the Pillar 3 *capability* story; cross-link up)
- The mDAQ page (`/mdaq`, LOCKED — analog/digital **sensor DAQ**; mTracker is **asset tracking + digital-signal telemetry**, no analog channels — see §3.2 / §3.9 Q2)
- An OEE-analytics product — **mTracker provides the OEE *inputs*; EREMOS V2 computes the OEE** (§3.2)
- A solution / outcome page (`/solutions/oem-machine-monitoring` covers the service-hours / warranty / fleet *outcome* — cross-link)
- The architecture walkthrough (`/architecture` v2.1)
- A pricing page (`/pricing`, Phase 3 — "how to buy" mechanics, not pricing tables)
- A certifications datasheet — **no formal certifications are currently claimed; cert / ingress-protection (IP65 / IP67-*compatible*) / site-compliance handled case-by-case during BOM scope** (§24.B.0)

### 1.2 Buyer alignment (per buyer-taxonomy v1 + §24.B.0)

**Primary buyer:** Plant engineer (retrofit / greenfield) (§2.5) — installs the retrofit tracker, wires it to equipment-level signals, owns the field attachment.
- Lands here from `/capabilities/asset-intelligence` (cross-link for hardware detail), the Platform menu, or a search for *"GSM GPS asset tracker OEE"* / *"retrofit machine utilization tracker"* / *"equipment run-hours telemetry"*
- Wants: connectivity (GSM/GPS/4G), the equipment signals it reads (digital inputs), battery backup + runtime, geo-fence behavior, retrofit attachment + mounting, environmental
- CTA preference: *"Get hardware specifications"* > *"Request a BOM scope"* > *"Talk to an engineer about Asset Intelligence"*. **NOT** *"Request an architecture review"* / *"Book a scoping call"* (§2.5 backfires; P-H)
- Vocabulary that lands: *GSM / GPS / 4G*, *equipment-level digital inputs*, *run hours*, *idle time*, *geo-fence*, *retrofit attachment*, *battery backup*, *24 V*, *BOM*
- Vocabulary that backfires: *"platform"* / *"ecosystem"* (too abstract), *"cloud-native"*, *"solution"*, marketing abstraction

**Strong secondary buyer:** OEM machine builder (§2.6) — service-hours billing, warranty triggers, fleet visibility on shipped equipment.
- Wants: equipment-level run-hours/utilization for service-hours billing + warranty; geo-fence + location for installed-base fleet
- Served via the §3.7 how-to-buy + cross-lens to `/solutions/oem-machine-monitoring` (the outcome surface), per buyer-taxonomy §5 step 3 — not by re-targeting the whole page

### 1.4 Page metadata (SEO + HTML head)

Per §9 metadata governance. Hardware-product-page pattern (inherits `/edge-gateway` §1.4).

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *mTracker — GSM/GPS Asset + OEE-Input Telemetry · Elpis* |
| **Meta description** (140-160 chars) | *Miniature GSM/GPS tracker that reports asset utilization and OEE inputs from equipment-level signals. Geo-fence, battery backup, retrofit attachment.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/mtracker` |
| **Schema intent** | `schema.org/Product` + `BreadcrumbList`. §3.9 inline FAQ uses `FAQPage`. Cross-links to `/capabilities/asset-intelligence` + `/solutions/oem-machine-monitoring` + `/architecture` use `relatedLink`. |

---

## 2. Page structure — sections at a glance

`ProductDetail` HARDWARE layout per §24.B (LOCKED). **11 sections** (inherits `/edge-gateway` / `/mdaq`).

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — product name + one-line + CTAs + hardware hero visual | `dark-deep` | `SectionShell` + `Button` ×2 + trust strip + `hero__composite` | ~90 |
| **2** | What it is — device definition + OEE-inputs precision + pillar cross-link | `light` | Narrative + `/capabilities/<pillar>` cross-link | ~150 |
| **3** | What it does + what it replaces (BOM) | `dark` | `CapabilityCard` grid + BOM-elimination list | ~200 |
| **4** | Hardware specifications | `light` | §24.A spec-table (Category · Value; grouped) + BOM-scope mini-table | spec (not prose) |
| **5** | Deployment in the field (retrofit) | `light-tinted` | spec-table + narrative | ~130 |
| **6** | Architecture — where it fits | `light` | `ArchitecturePanel.interactive` (product-annotated) + caption | ~90 |
| **7** | How to buy | `dark` | narrative (unit + asset-intelligence path; OEM angle; mechanics, not pricing) | ~120 |
| **8** | Field-readiness (no cert claims) | `light-tinted` | trust-cue content pattern (§16), reframed | ~100 |
| **9** | Common questions (inline FAQ) — 8 Q&A | `light` | inline FAQ + `FAQPage` schema | ~420 |
| **10** | Related — cross-lens | `light-tinted` | cross-lens content pattern (§17) | ~50 |
| **11** | Final CTA | `dark-deep` | `CTASection` | ~80 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: PRODUCT · ASSET INTELLIGENCE — HARDWARE
> HEADLINE: mTracker
> SUBHEAD (max-width 64ch):
> The miniature GSM/GPS device that puts utilization and OEE inputs on the assets that don't report them today — fixed or mobile. It reads equipment-level signals, tracks location, and publishes over cellular, on a retrofit attachment with battery backup.
>
> PRIMARY CTA (`Button.primary.lg`): Get hardware specifications → HREF `/contact?intent=mtracker-specs`
> SECONDARY CTA (`Button.secondary.lg`): Request a BOM scope → HREF `/contact?intent=mtracker-bom`
>
> TRUST STRIP (size.sm):
> 4G / LTE cellular · GPS / GNSS · equipment-level digital inputs · geo-fence alerts · battery backup · retrofit attachment · IP65 / IP67-compatible (BOM-scoped).
>
> HERO VISUAL (right column, §24 hero-visual slot): a hardware-relevant SVG — a miniature device line-art with a spec-highlight panel (GSM/GPS · digital inputs · geo-fence · battery · retrofit). Decorative (`aria-hidden`), token-only, "illustrative" caption.

**Anti-patterns:** Product name + value headline. No "Request an architecture review" / "Book a scoping call" (§2.5 → "Get hardware specifications", P-H). No formal certification claim (IP65/IP67 stated as *compatible* only). Don't claim mTracker *computes* OEE (it feeds the inputs; EREMOS V2 computes), nor that it does analog sensor acquisition (that's mDAQ).

### 3.2 Section 2 — What it is

> EYEBROW: WHAT IT IS
> SECTION TITLE: Utilization and OEE inputs on assets that never reported them.
>
> BODY:
> mTracker is a miniature GSM/GPS asset-tracking and telemetry device. It reads **equipment-level digital signals** — run / stop, cycle, output counts — and reports **OEE *inputs*** (production time, downtime, idle time, output quantity) along with location, over cellular, with battery backup. It is designed for **retrofit attachment** to fixed or mobile assets.
>
> BODY ¶2 (muted):
> It is the **Asset Intelligence** pillar — see the capability story → `/capabilities/asset-intelligence`. mTracker provides the **inputs**; **EREMOS V2 computes the OEE** and the utilization analytics. (For direct analog *sensor* acquisition — pressure, flow, temperature — see **mDAQ**; mTracker is asset tracking + digital-signal telemetry, not an analog DAQ.)

### 3.3 Section 3 — What it does + what it replaces

> EYEBROW: WHAT IT DOES
> SECTION TITLE: What it does — and what it removes from your BOM.

Feature cards (what it does):

> - **Reads equipment-level signals.** Digital inputs capture run / stop, cycle, and output-count signals straight from the asset — no manual logs.
> - **Tracks location + utilization.** GSM/GPS reports where an asset is and whether it's running, idle, or stopped — fixed plants or mobile fleets.
> - **Geo-fence alerts.** Boundary entry/exit alerts for location and compliance.
> - **Retrofit + remote.** A miniature attachment with battery backup and cellular publish — fits assets that ship without telemetry.

What it replaces (BOM-elimination, per hardware-ecosystem-map §4.1):

> One mTracker removes from the customer BOM:
> - **manual production-hour spreadsheets**;
> - a separate **GPS / geo-fence tracker**;
> - a **service-hours odometer** for warranty triggers;
> - **asset-presence audits**.

### 3.4 Section 4 — Hardware specifications (§24.A, hardware)

> EYEBROW: HARDWARE SPECIFICATIONS
> SECTION TITLE: The device, in numbers.

Spec-table per §24.A (hardware variant) — `Category | Value`, grouped. Values trace to hardware-ecosystem-map v3 §4.1 (verify before external publish). **No formal certification *claims*** — IP65 / IP67-*compatible* wording only (a design characteristic, not a certified rating — §24.B.0).

| Category | Value |
|---|---|
| **Connectivity** | Cellular publish — 4G / LTE class (SIM / carrier / region / bands confirmed during BOM scope) — plus GPS / GNSS positioning |
| **Inputs** | Equipment-level digital inputs for run / stop, cycle, and output-count signals. Channel count, voltage / contact type, counter rate, debounce, sink / source behavior, and input protection confirmed during BOM scope. |
| **Location** | GPS / GNSS positioning + **geo-fence** entry / exit alerts |
| **Power + battery** | Battery backup for assets without continuous power. Supply range, backup-vs-primary role, charging / replacement, runtime by reporting interval, and low-power behavior confirmed during BOM scope. |
| **Form factor** | Miniature, designed for **retrofit attachment** (fixed or mobile assets) |
| **Environmental** | Operating temperature, humidity / condensation, shock / vibration (mobile assets), and UV / outdoor exposure confirmed during BOM scope |
| **Mechanical** | Dimensions, weight, enclosure material, connector / harness, and cable ingress confirmed during BOM scope |
| **Ingress protection** | IP65 / IP67-**compatible** configurations can be scoped where a site requires it; final protection level, enclosure approach, and any certification requirements confirmed during BOM scope. *(Compatibility, not a certified rating — no formal IP certification is currently claimed.)* |
| **Mounting** | Retrofit attachment; mounting method, exposure, and antenna placement confirmed during BOM scope |

**Telemetry + deployment details confirmed during BOM scope** (the deployment-specific detail an asset-tracking buyer expects — surfaced here, no invented numbers):

| Item | Confirmed during BOM scope |
|---|---|
| **Digital inputs** | Channel count, voltage/threshold, dry/wet contact, debounce, sink/source behavior, protection |
| **OEE-input mapping** | Which equipment signals map to run / stop / cycle / output-count; how output quantity is counted |
| **Reporting** | Report interval, timestamping, buffering / offline retention, publish path (to EREMOS V2) |
| **Power + battery** | Supply, current draw, battery runtime, charging / replacement, low-power mode |
| **Mechanical + mounting** | Mounting method (screw / adhesive / magnetic / bracket), tamper resistance, antenna placement, cable ingress for mobile assets |
| **Geo-fence behavior** | Boundary source, update interval, alert latency, indoor / poor-signal behavior, and device-side vs platform-side evaluation |
| **Data handling** | Offline buffer capacity, timestamp source, store-and-forward + duplicate handling, event-triggered vs periodic publish |
| **Device management** | Device identity / TLS, provisioning, remote config, OTA firmware, geo-fence config management |

> CAPTION (size.sm): Connectivity, inputs, geo-fence, and form factor trace to the hardware ecosystem map and are confirmed at quoting time. Signal mapping and reporting are confirmed during BOM scope.

### 3.5 Section 5 — Deployment in the field (retrofit)

> EYEBROW: IN THE FIELD
> SECTION TITLE: How it retrofits.

| | |
|---|---|
| **Retrofit attachment** | Designed to attach to fixed or mobile assets without a redesign. Mounting method, location, and antenna placement confirmed during BOM scope. |
| **Equipment-signal wiring** | Digital inputs wire to existing run / stop / cycle / output signals. Signal voltage/threshold, contact type, and OEE-input mapping confirmed during BOM scope. |
| **Power** | Battery backup for assets without continuous power; supply + runtime confirmed during BOM scope. |
| **Connectivity** | GSM / GPS / 4G cellular publish. SIM / carrier / antenna and network assumptions confirmed during BOM scope. |
| **Geo-fence** | Boundary entry/exit alerts configured per deployment. |
| **Offline** | Buffers locally and publishes when connectivity returns. |
| **Environment** | Exposure, humidity, and enclosure approach confirmed during BOM scope; IP65 / IP67-compatible configurations can be scoped where the placement (incl. mobile/outdoor assets) requires it (no certified rating claimed). |

### 3.6 Section 6 — Architecture (where it fits)

> EYEBROW: WHERE IT FITS
> SECTION TITLE: The device in the stack.

`ArchitecturePanel.interactive` (product-annotated, §5.A): equipment signals → **mTracker** (highlighted; GSM/GPS telemetry) → cellular → EREMOS V2 (utilization + OEE). Annotation eyebrow-as-title (§24 P-E). Annotations:

| Annotated region | Eyebrow | Body |
|---|---|---|
| Equipment → mTracker | EQUIPMENT-LEVEL SIGNALS | Digital inputs read run / stop, cycle, and output-count signals straight from the asset. |
| mTracker core | RETROFIT TELEMETRY | A miniature GSM/GPS attachment with battery backup — fits assets that ship without telemetry. |
| mTracker → EREMOS V2 | OEE INPUTS | mTracker reports the OEE *inputs* + location; **EREMOS V2 computes the OEE** and utilization analytics. |
| mTracker (geo) | GEO-FENCE | GPS + geo-fence entry/exit alerts for location and compliance. |

> CAPTION: For direct analog *sensor* acquisition, see **mDAQ**; for service-hours / warranty / fleet outcomes on shipped equipment, see `/solutions/oem-machine-monitoring`. The full stack → `/architecture`.

### 3.7 Section 7 — How to buy

> EYEBROW: HOW TO BUY
> SECTION TITLE: One device, plus the utilization picture.
>
> *Packaging labels are illustrative until commercial packaging is approved; this section describes how the unit is bought + what it pairs with, not pricing.*
>
> BODY:
> mTracker is a hardware unit, **scoped against the assets to instrument, the equipment signals available, connectivity, power/battery, and the mounting environment**. It feeds utilization + OEE inputs to EREMOS V2. For **OEM machine builders**, the same equipment-level run-hours drive service-hours billing, warranty triggers, and fleet visibility on shipped equipment — see `/solutions/oem-machine-monitoring`. Bring the asset list, the signals available on each, the connectivity, and the mounting; we'll scope the BOM. Contact Elpis for unit availability and BOM scoping; detailed pricing follows the BOM review. No pricing tables, SKU grids, or per-unit pricing on this page.

### 3.8 Section 8 — Field-readiness

Trust-cue content pattern (§16), reframed for hardware (cert/IP per §24.B.0):

> EYEBROW: FIELD-READINESS
>
> CUE 1 — **Built to retrofit, fixed or mobile.** A miniature attachment with battery backup, designed to go onto assets that ship without telemetry — including mobile equipment in the field.
>
> CUE 2 — **Remote-ready, offline-capable.** GSM / GPS / 4G publishes from anywhere with cellular; buffers locally and publishes on reconnect. Geo-fence alerts for location and compliance.
>
> *(Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, but certified/rated claims are published only when formal evidence exists for the specific product/configuration.)*

### 3.9 Section 9 — Common questions (inline FAQ)

Per §9 (product pages = YES). `FAQPage` schema. 8 questions.

> #### Q1. What does it actually track?
> Asset utilization and location: it reads equipment-level digital signals (run / stop, cycle, output count) and reports the OEE *inputs* — production time, downtime, idle time, output quantity — plus GPS location, over cellular. Which signals are available and how they map are confirmed during BOM scope.
>
> #### Q2. How is mTracker different from mDAQ?
> mDAQ is the field **sensor DAQ**: analog 4–20 mA / 0–10 V sensor acquisition plus I/O. mTracker is the **asset telemetry tracker**: equipment-state / count inputs plus GPS, geo-fence, cellular, battery, and retrofit attachment — no analog channels. (Both touch digital signals, so the real split is by purpose:) mDAQ is for sensor *values*; mTracker is for run-state, counts, utilization inputs, and *location*.
>
> #### Q3. Does mTracker compute OEE?
> No — mTracker provides the **equipment-side** OEE inputs from digital signals: run / stop, cycle / output count, and time-state signals. **EREMOS V2 computes OEE** and utilization analytics, combining those inputs with the configured production context — planned time, ideal cycle, product / SKU, and quality data where required. The device captures and reports the signals; the platform does the math.
>
> #### Q4. Can it track mobile equipment, not just fixed machines?
> Yes. GSM/GPS + battery backup are designed for fixed plants and mobile assets alike; geo-fence alerts cover location and compliance. Mounting and antenna placement for mobile assets are confirmed during BOM scope.
>
> #### Q5. What does it need from the equipment to read run-hours?
> Access to equipment-level digital signals (run / stop, cycle, or output-count). The available signals, their voltage/contact type, and the OEE-input mapping are confirmed during BOM scope; where a signal isn't exposed, we scope how to derive it.
>
> #### Q6. Is it certified? What about IP65 / IP67?
> No formal third-party certifications are currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope. Where an asset (including mobile/outdoor) requires IP65 / IP67-compatible protection, Elpis can scope a compatible configuration or enclosure approach; formal certification or rating claims are published only when the specific product/configuration has the required certification or test evidence.
>
> #### Q7. For OEMs — can it drive service-hours billing and warranty?
> Yes — that's a core use. Equipment-level run-hours and utilization feed service-hours billing, warranty triggers, and fleet visibility on shipped equipment. See the OEM outcome → `/solutions/oem-machine-monitoring`.
>
> #### Q8. How long does the battery last, and how is it powered?
> Battery backup covers assets without continuous power; supply, current draw, runtime, and charging/replacement are confirmed during BOM scope against the duty cycle and reporting interval.

### 3.10 Section 10 — Related (cross-lens)

Per §24.3 (adapted for a hardware product — Asset Intelligence capability + the OEM solution that uses mTracker + architecture):

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | CAPABILITY · ASSET INTELLIGENCE | The Pillar 3 capability story | `/capabilities/asset-intelligence` |
| 2 | SOLUTION · OEM MACHINE MONITORING | Service-hours, warranty + fleet visibility on shipped equipment | `/solutions/oem-machine-monitoring` |
| 3 | ARCHITECTURE | How the building blocks connect into one stack | `/architecture` |

> Looking at this from another angle?

### 3.11 Section 11 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Bring us the assets you can't see.
> SUBHEAD: A list of the assets to instrument, the signals available on each, and where they sit (fixed or mobile) — that's what we scope a BOM against. We confirm the inputs, power, and mounting for your assets, not for a brochure.
> PRIMARY CTA: Get hardware specifications → `/contact?intent=mtracker-specs`
> SECONDARY CTA: Request a BOM scope → `/contact?intent=mtracker-bom`

---

## 4. Components used

All design-system v3 LOCKED + the §24.A spec-table content pattern. **No new visual primitive** (inherits §24.B).

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.11 |
| `CapabilityCard` (compact) | §3.3 |
| `ArchitecturePanel.interactive` (product-annotated) | §3.6 |
| §24.A spec-table content pattern (hardware) | §3.4 hardware specs + BOM-scope mini-table; §3.5 field table |
| Trust-cue content pattern (§16, reframed as field-readiness) | §3.8 |
| Cross-lens content pattern (§17) | §3.10 |
| `CTASection` | §3.11 |
| Inline FAQ (`FAQPage` schema) | §3.9 |
| Hero visual (`hero__composite`, §24 slot) | §3.1 |

---

## 5. Verbatim copy summary

All page copy in §3.1-§3.11. **~1,540 words page copy** (within the §24.B 1,200-1,800 target; post-ChatGPT-review). Spec-table cell text (§3.4 incl. the BOM-scope mini-table, §3.5) + §3.6 annotations are NOT prose-counted.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + the §24.B.3 hardware anti-patterns:

| Don't | Why |
|---|---|
| Claim CE / UL / FCC / IEC / certified IP65 / certified IP67 (or "IP-rated", "certified rugged", "field certified") unless formal evidence exists | Inherited §24.B.0. Allowed: "IP65 / IP67-compatible configurations can be scoped during BOM review". |
| Claim mTracker **computes** OEE | mTracker feeds the OEE *inputs*; **EREMOS V2 computes** (§3.2, §3.6, §3.9 Q3). |
| Imply mTracker does analog sensor acquisition | That's **mDAQ**. mTracker = asset tracking + digital-signal telemetry, no analog channels (§3.2, §3.9 Q2). |
| Publish fabricated specs (battery runtime, channel counts, intervals) | Trace to hardware-ecosystem-map v3 §4.1; the rest is "confirmed during BOM scope". |
| Use "Request an architecture review" / "Book a scoping call" as the primary CTA | §2.5 backfires; P-H — "Get hardware specifications" / "Request a BOM scope". |
| Over-target the OEM buyer in primary page content | OEM (§2.6) is the strong secondary — served via §3.7 + the `/solutions/oem-machine-monitoring` cross-lens, not by re-targeting the whole page. |
| Customer / competitor names, fabricated metrics | proof-architecture §3/§4/§8. |
| Introduce a new visual primitive | §24.B composes from v3 components + §24.A. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,200-1,800 words (current ~1,500); spec tables not prose-counted
- [x] All 11 §24.B sections present (hardware variant; inherits /edge-gateway / mDAQ)
- [x] **NO formal cert claims; cert / IP / site-compliance case-by-case during BOM scope; IP65 / IP67 *compatible* only; §3.9 Q6 + §3.5 use approved wording; §3.4 ingress row present**
- [x] §3.1 hero product-led; CTA "Get hardware specifications" / "Request a BOM scope" (§2.5, P-H)
- [x] **mTracker provides OEE *inputs*; EREMOS V2 computes OEE — stated in §3.2, §3.6, §3.9 Q3 (no "computes OEE" claim)**
- [x] **mTracker-vs-mDAQ distinction explicit (telemetry/tracking vs analog DAQ)** — §3.2, §3.9 Q2
- [x] §3.3 BOM-elimination list present (spreadsheets / GPS tracker / service-hours odometer / presence audits)
- [x] §3.4 specs trace to hardware-ecosystem-map v3 §4.1 (GSM/GPS/4G, digital inputs, geo-fence, battery, retrofit); BOM-scope mini-table present
- [x] §3.7 + §3.10 carry the OEM service-hours/warranty angle via `/solutions/oem-machine-monitoring` (secondary buyer via cross-lens)
- [x] §3.10 cross-lens: asset-intelligence + oem-machine-monitoring + architecture (documented §24.3 adaptation)
- [x] Plant-engineer vocabulary (GSM/GPS, digital inputs, run hours, geo-fence, retrofit); no abstract lead
- [x] No new component beyond v3 + §24.A; §1.4 metadata (`Product` schema)
- [x] Specs VERIFIED against hardware-ecosystem-map v3 §4.1 before external publish
- [x] **Inherited §24.B + mDAQ lessons** (cert/IP discipline, BOM-scope mini-table, qualified claims)
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Certifications.** None currently claimed; cert/IP case-by-case during BOM scope (§24.B.0).
- **OEE computation + utilization analytics.** That's **EREMOS V2** (`/eremos-v2`, LOCKED) — mTracker feeds the inputs; cross-link.
- **Analog sensor acquisition.** That's **mDAQ** (LOCKED) — cross-link.
- **Capability + solution narratives.** `/capabilities/asset-intelligence` + `/solutions/oem-machine-monitoring` (LOCKED) — cross-link.
- **Architecture walkthrough.** `/architecture` (LOCKED v2.1).
- **Pricing / commercial packaging.** `/pricing` (Phase 3). "How to buy" mechanics only here.
- **Per-signal mapping / battery-runtime matrices.** Confirmed at BOM scope; not published until verified.

---

*`/mtracker` Page Spec **v2 LOCKED 2026-06-04** (§24.B HARDWARE ProductDetail; inherits the LOCKED §24.B variant + the /mdaq precision-hardening lessons) after ChatGPT review ("Lock after revisions"; spec-table hardening + OEE-input precision applied). Third hardware product page — confirms §24.B generalizes to an asset-tracking / OEE-input telemetry device with no new component. mTracker positioned precisely: it reports OEE *inputs* from equipment-level digital signals + location; **EREMOS V2 computes the OEE** (no "computes OEE" claim). Distinguished from mDAQ (analog sensor DAQ) — mTracker is asset tracking + digital-signal telemetry, no analog channels. Specs trace to hardware-ecosystem-map v3 §4.1 (GSM/GPS/4G + battery backup; equipment-level digital inputs; geo-fence; retrofit attachment) — verify before external publish. Cert/IP discipline inherited from §24.B (no formal claims; IP65/IP67-compatible, not certified/rated; case-by-case during BOM scope), plus a BOM-scope mini-table (the /mdaq lesson). Plant-engineer buyer (§2.5) primary → "Get hardware specifications" / "Request a BOM scope" CTA (P-H); OEM machine builder (§2.6) strong secondary via the /solutions/oem-machine-monitoring cross-lens (service-hours / warranty / fleet). Next: user + ChatGPT review → lock → VAS + E-IDOS inherit §24.B (flip to Maintenance Manager §2.4; their map specs are "orientation only" → provisional). Cites: design-system-v4 §24.B/§24.A/§24.3, page-product-edge-gateway-spec-v1 + page-product-mdaq-spec-v1 (§24.B siblings), hardware-ecosystem-map-v3 §4.1, page-capabilities-hub-spec-v1 §9, buyer-taxonomy-v1 §2.5/§2.6, proof-architecture-v1 §3/§4/§8, page-capabilities-asset-intelligence-spec-v1 v2, page-solutions-oem-machine-monitoring-spec-v1 v3, page-architecture-spec-v1 v2.1, elpis-industrial-intelligence-platform-v5, 2026-06-04-phase-e-solution-migration-plan (P-A..P-H).*
