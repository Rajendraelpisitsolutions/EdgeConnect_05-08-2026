<!--
File:        docs/marketing/page-capabilities-data-acquisition-spec-v1.md
Purpose:     Page spec for /capabilities/data-acquisition — Pillar 2
             deep-dive (mDAQ). Content-only spec inheriting the
             CapabilityDeepDive layout locked in
             page-capabilities-condition-monitoring-spec-v1.md.
Audience:    Internal — Angular engineering team, copywriters, user +
             ChatGPT (reviewers).
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-condition-monitoring-spec-v1.md
                (LOCKED — layout precedent)
             page-capabilities-connectivity-edge-spec-v1.md
                (sister pillar deep-dive)
             buyer-taxonomy-v1.md §2.5 (Plant engineer primary) +
                §2.3 (OT Architect secondary)
             hardware-ecosystem-map-v3.md §3 (mDAQ pillar source)
Version:     v2 — LOCKED after Pass 1 ChatGPT review (Data Acquisition
                  is "one of the most disciplined capability pages in the
                  entire Phase 2 set" — minor polish refinements applied)
Date:        2026-05-29 (v2 lock)
Status:      LOCKED.

Fourth per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 5 (parallelizable). Content-only.

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve /capabilities/data-acquisition v1 and lock. This is one of the
   most disciplined capability pages in the entire Phase 2 set and may
   actually become the reference example for future hardware-oriented
   pillar pages. Final verdict across all sections: Excellent.
   Ready to lock: Yes."

User decision on v2 refinements (2026-05-29):
  - R1 (concrete instrument vocabulary in §3.2 mDAQ card)              APPLIED
        — replaces abstract signal categories with flow meters / pressure
          transmitters / level transmitters / temperature probes /
          vibration sensors / 4-20 mA loop-powered transducers
        — matches Plant Engineer vocabulary per buyer-taxonomy §2.5
  - R2 (remote-and-unmanned sites sharpening in §3.4 deployment)        APPLIED
        — adds "and unmanned" + worked examples (pipeline pump stations,
          mining outposts, well heads, off-grid water infrastructure) to
          existing deployment-footprint bullet

Both refinements were reviewer-marked "not required" / "not necessary"
polish; ChatGPT explicitly approved lock without them. User direction
was to apply both to move the page from "approve" to "reference example
for hardware-oriented pillar pages" (reviewer's words).

Sections receiving "no change" reviewer approval (preserved verbatim):
  §1.2 buyer alignment, §3.1 hero, §3.2 mDAQ card density, §3.3 BOM
  elimination, §3.4 buyers + industries columns, §3.5 PLC-vs-mDAQ
  differentiation ("may become one of the strongest pieces of copy
  in the Phase 2 capability set"), §3.6 trust cue ("no PLC in the
  trust chain"), §3.7 related solutions, §3.9 CTA ("may actually be
  the best CTA in the pillar set").

Post-draft additions (governance compliance, not content changes):
  - 2026-05-28: §1.4 Page metadata (SEO + HTML head) block added
    per /capabilities hub §9 metadata governance lock (PR #71).
  - 2026-05-29: §3.5 "How this differs from PLC-based data acquisition"
    callout added per /capabilities hub §9 emerging-pattern governance
    (PR #71 commit 782a626). Pattern was reviewer-validated on
    /capabilities/asset-intelligence v2 ("the single most important
    improvement before lock"). Applied here pre-review so the v1 draft
    that ChatGPT sees already includes the strengthening pattern.
    Callout honors anti-overclaim — actively cross-links to Connectivity
    & Edge for "you already have a PLC" cases instead of overselling mDAQ.
-->

# `/capabilities/data-acquisition` — Page Spec v1

**Capability deep-dive for the Data Acquisition pillar (mDAQ). Inherits the locked `CapabilityDeepDive` layout. Content-only spec.**

This is the page where Plant engineers (greenfield or retrofit) land when they need to capture industrial sensor signals **without a PLC in the loop** — direct sensor acquisition for plant data that doesn't exist in any existing PLC. It is **not** a product detail page (mDAQ Phase E `/mdaq`). It is **not** the architecture walkthrough (`/architecture`).

Target length: **800-1,200 words page copy** per `/capabilities` hub spec §9.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Capability deep-dive for Data Acquisition. Reader leaves with *"I now understand when I'd use mDAQ instead of (or alongside) a PLC, what it eliminates from my BOM, and which solutions it powers."*

**IS NOT:** A full mDAQ product detail page (Phase E `/mdaq`). A sensor compatibility matrix (Phase E). A solution narrative.

### 1.2 Buyer alignment (per buyer-taxonomy v1)

**Primary buyer:** Plant engineer (retrofit / greenfield) (§2.5)
- Lands here when scoping a greenfield install (no PLC infrastructure yet) or a PLC-bypass retrofit (PLC exists but doesn't expose the signals the team needs)
- Wants: hardware specs, signal-conditioning detail, real install patterns, deployment guides
- CTA preference: *"Get hardware specifications"* > *"Talk to an engineer about Data Acquisition"*
- Vocabulary that lands: 4-20 mA, 0-10 V, 24 V DC, Modbus RTU, M12 connectors, DIN-rail mount, IP65, signal conditioning, loop power
- Vocabulary that backfires: *"platform"* / *"ecosystem"* (too abstract); *"cloud-native"* (often deal-breaker if site has no internet)

**Secondary buyer:** OT Architect / SCADA engineer (§2.3)
- Lands here when evaluating mDAQ as part of the broader architecture (e.g., bypassing a PLC layer for direct sensor reads)
- Wants: integration patterns, protocol compatibility, deployment shape
- CTA preference: *"Request an architecture review"*

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Data Acquisition — mDAQ for direct sensor reads · Elpis* |
| **Meta description** (140-160 chars) | *mDAQ hardware for direct sensor acquisition without a PLC layer. Greenfield and PLC-bypass retrofit. 4-20 mA, 0-10 V, 24 V DC, Modbus RTU, DIN-rail mount.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/capabilities/data-acquisition` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. mDAQ hardware card cross-links to Phase E `/mdaq` via `Product` schema (when shipped). Page-to-page cross-link to `/architecture` uses `relatedLink`. |

---

## 2. Page structure — sections at a glance

Per design-system v3 §14 `CapabilityDeepDive` layout. Same 9 sections as the locked pillar pattern.

| # | Section | Mode | Component(s) |
|---|---|---|---|
| 1 | Hero | `dark-deep` | `SectionShell` + `Button` × 2 |
| 2 | Products in this pillar (mDAQ) | `dark` | `CapabilityCard` × 1 with pillar-2 accent |
| 3 | What this pillar eliminates from your BOM | `light` | Bulleted list |
| 4 | Strategic adjacencies | `light` | 3-column grid |
| 5 | Where this fits in the Industrial Intelligence Stack | `light-tinted` | `DiagramFrame` focused on Pillar 2 + cross-link to `/architecture` |
| 6 | Trust posture for this pillar | `light-tinted` | §16 trust cue content pattern |
| 7 | Related solutions | `light` | `CapabilityCard` × 2 (solution-card variant) |
| 8 | Cross-lens navigation | `light-tinted` | §17 cross-lens pattern (3 cards) |
| 9 | Final CTA | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: CAPABILITY · DATA ACQUISITION
>
> HEADLINE (size.3xl semibold):
> Capture industrial signals directly from sensors — when there is no PLC, or when you want to bypass the one you have.
>
> CUSTOMER QUESTION LEAD (italic):
> *"What if I don't have a PLC, or I want to bypass it and read sensors directly?"*
>
> PRIMARY CTA: Get hardware specifications
> HREF: `/contact?intent=mdaq-hardware-specs`
>
> SECONDARY CTA: Talk to an engineer about Data Acquisition
> HREF: `/contact?intent=data-acquisition-engineering`

**Anti-patterns:** no *"cloud-native"* framing (backfires with Plant engineers per buyer-taxonomy §2.5). No outcome metric in headline.

---

### 3.2 Section 2 — Products in this pillar

> EYEBROW: PRODUCTS IN THIS PILLAR

#### Card — mDAQ (pillar-2 accent)

> EYEBROW: FIELD ACQUISITION HARDWARE
> TITLE: mDAQ — General-purpose industrial data acquisition
> BODY:
> Ruggedized acquisition device that captures signals from industrial instruments — flow meters, pressure transmitters, level transmitters, temperature probes, vibration sensors, 4-20 mA loop-powered transducers — directly from the field, no PLC in the loop. 4 analog channels (0-10 V or 4-20 mA), 16-bit, 860 S/s. 8 × 24 V digital inputs + 8 × 24 V digital outputs. Modbus TCP / RTU acquisition. HTTPS / MQTT publish. 4G, Wi-Fi, GPS, optional Ethernet. −10 °C to +85 °C operating range. Optional battery backup for sites without continuous power. 180 × 150 × 60 mm rugged enclosure.
> FOOTER: Hardware appliance · 4 AI + 8 DI + 8 DO · 4G/Wi-Fi/GPS · 24 V DC
> LINK: *(Phase E product detail — coming soon)*

---

### 3.3 Section 3 — What this pillar eliminates from your BOM

> EYEBROW: WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
>
> SUBHEAD:
> Data Acquisition is the field-replacement product — combine it with EdgeConnect and EREMOS V2 for a complete Elpis-only signal-to-dashboard path with no third-party hardware in the chain.
>
> BULLETED LIST:
>
> - A standalone PLC purchased solely for sensor acquisition (when you don't actually need ladder-logic control)
> - A separate cellular modem and edge appliance (mDAQ has 4G / Wi-Fi / GPS / Ethernet built in)
> - A site-specific battery backup for power-loss continuity (optional onboard battery available)
> - A separate field-mountable signal-conditioning box for analog inputs
> - The custom wiring complexity that comes from stitching together discrete acquisition + comms + power components
> - Vendor coordination across 3-4 separate hardware suppliers for a single greenfield acquisition installation

---

### 3.4 Section 4 — Strategic adjacencies

> EYEBROW: WHO IT'S FOR · WHERE IT DEPLOYS
>
> COLUMN 1 — BUYERS:
> - **Plant engineer (greenfield)** — designing acquisition from scratch; no legacy PLC to integrate
> - **Plant engineer (retrofit / PLC-bypass)** — existing PLC doesn't expose the signals the team needs; mDAQ reads sensors directly
> - **OT Architect** — evaluating mDAQ as the field-acquisition layer of a broader Elpis-only deployment
>
> COLUMN 2 — INDUSTRIES:
> - Oil & Gas (pipeline monitoring; surface flow, pressure, temperature)
> - Water & Utilities (pump-station monitoring; flow + pressure analytics)
> - Manufacturing — process (flow / temperature / pressure on tank, reactor, distillation systems)
> - Mining & Construction (heavy-equipment hydraulic signal acquisition)
> - Power & Energy (substation auxiliary signals; instrumentation outside the main SCADA path)
>
> COLUMN 3 — DEPLOYMENT FOOTPRINT:
> - Operating across India and the Middle East
> - Offline-capable with optional battery — deployable at remote and unmanned sites (pipeline pump stations, mining outposts, well heads, off-grid water infrastructure) without continuous mains or network
> - Multi-site fleets: per-gateway identity established at first start; per-site binding clean from day one

---

### 3.5 Section 5 — Where this fits in the Industrial Intelligence Stack

> EYEBROW: WHERE IT FITS
>
> SECTION TITLE:
> Data Acquisition is the field-acquisition layer of the Industrial Intelligence Stack.
>
> CALLOUT — HOW THIS DIFFERS FROM PLC-BASED DATA ACQUISITION (size.base, single paragraph; visual treatment: light tinted card or left-rule callout):
>
> > **How this differs from PLC-based data acquisition.** If you already have a PLC and it already exposes the signals you need, read from the PLC — Connectivity & Edge handles that pillar with Modbus TCP, S7, and OPC UA Client. mDAQ exists for the cases the PLC doesn't cover: **greenfield installs with no PLC yet, PLC-bypass retrofits where the controller is locked or doesn't expose the signal you need, and direct-sensor reads where adding a PLC layer would be more expensive than acquiring the signal directly.** mDAQ is purpose-built for *"the signal exists at the sensor and the PLC can't tell you about it."*
>
> BODY:
> Pillar 2 sits at the field edge — where physical signals first become digital. mDAQ captures sensor data directly from the field, then publishes via MQTT or HTTPS to Pillar 1 (Connectivity & Edge) for integration with everything else on the floor. From there, Pillar 5 (Operational Intelligence) turns the signals into OEE / alarms / reports. mDAQ also serves as the hardware platform for Pillar 4 specialty analytics (VAS / vibration). One acquisition platform, multiple application paths.
>
> DIAGRAM FRAME (DiagramFrame focused on Pillar 2 layer)
>
> CAPTION:
> *Pillar 2 is the field-edge acquisition layer. See the full Industrial Intelligence Stack → `/architecture`*

---

### 3.6 Section 6 — Trust posture for this pillar

Per §16 trust cue content pattern. Cue focus per buyer-taxonomy v1 §2.5: direct sensor acquisition without intermediary trust assumptions.

> EYEBROW: TRUST POSTURE
>
> BODY:
> mDAQ reads sensors directly — no PLC in the trust chain, no third-party gateway interpreting your signals before they reach you. Signals are timestamped at the device with per-tag quality codes that propagate end-to-end. Offline-capable with optional battery; air-gapped sites are first-class. Configuration changes captured in the hash-chained audit log on the EdgeConnect pipeline that mDAQ feeds.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.7 Section 7 — Related solutions

> EYEBROW: RELATED SOLUTIONS

#### Card 1 — Edge Connectivity (primary)

> EYEBROW: SOLUTION · EDGE CONNECTIVITY
> TITLE: One operational view across every controller AND every direct sensor
> BODY: How mDAQ + EdgeConnect + EREMOS V2 deliver a complete Elpis-only signal-to-dashboard path — no third-party hardware in the chain.
> LINK: Read the solution → `/solutions/edge-connectivity`

#### Card 2 — Brownfield Modernization (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · BROWNFIELD MODERNIZATION
> TITLE: Modernize the data layer without replacing the controllers
> BODY: For plants with mixed-generation controllers — mDAQ bypasses the PLCs that can't expose what the team needs, while EdgeConnect integrates everything else.
> LINK: Read the solution → `/solutions/brownfield-modernization` *(existing v2; v3 in Phase E)*

---

### 3.8 Section 8 — Cross-lens navigation

Per §17 preset for `/capabilities/<pillar>`:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | ARCHITECTURE | How does this pillar fit the data flow? | `/architecture` |
| 2 | SOLUTION · EDGE CONNECTIVITY | The outcome-organised version of this pillar | `/solutions/edge-connectivity` |
| 3 | CAPABILITIES | Back to all 5 pillars | `/capabilities` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.5 Plant engineer CTA preference:

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your sensor list. We'll scope a BOM.
>
> SUBHEAD:
> Whether you're scoping a greenfield install with no existing PLC infrastructure, or a retrofit where the existing PLC can't expose the signals you need — bring us the sensor list and we'll scope the BOM. Real hardware against real signals, not slideware.
>
> PRIMARY CTA: Get hardware specifications
> HREF: `/contact?intent=mdaq-hardware-specs`
>
> SECONDARY CTA: Talk to an engineer about Data Acquisition
> HREF: `/contact?intent=data-acquisition-engineering`

---

## 4. Components used

All from design-system v3 LOCKED — no new components.

`SectionShell` (modes), `CapabilityCard` (pillar-2 + compact variants), `Button` (primary + secondary, lg), `DiagramFrame`, `CTASection`, §16 trust cue content pattern, §17 cross-lens content pattern.

---

## 5. Verbatim copy summary

Page copy collected in sections §3.1-§3.9. ~920 words total (within 800-1,200 target). Increase from v1 baseline (~880 words) reflects v2 R1 + R2 minor polish refinements applied per ChatGPT Pass 1 review.

---

## 6. Anti-patterns specific to this page

| Don't | Why |
|---|---|
| Use *"cloud-native"* / *"IoT for the edge"* framing | Backfires with Plant engineer per buyer-taxonomy §2.5 (cloud-native is often a deal-breaker for the sites they install at) |
| List specific certification claims (CE / UL / FCC / IP rating) | Per positioning v3 §11 — per-product certifications still open; Phase E `/mdaq` page will land them when ready |
| Add per-sensor compatibility matrix | Belongs on Phase E `/mdaq` page; capability page stays capability-level |
| Use *"platform"* / *"ecosystem"* as the primary descriptor for mDAQ | Plant engineers want the device, not the abstract platform (per buyer-taxonomy §2.5 vocabulary discipline) |
| Add competitor names (Advantech, Moxa, ICP DAS, HMS) | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory |
| Imply mDAQ replaces ALL PLCs everywhere | Honest framing — mDAQ is for sensor-direct acquisition; PLC-driven control loops still belong to PLCs |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 800-1,200 word target (current: ~920 words)
- [x] All 9 sections per CapabilityDeepDive layout
- [x] Customer question verbatim from hardware-ecosystem-map v3 §1
- [x] mDAQ product card uses accurate specs from hardware-ecosystem-map v3 §3.1
- [x] §3.6 trust cue focuses on direct sensor acquisition + offline-capable per buyer-taxonomy §2.5
- [x] Final CTA uses Plant-engineer-preferred framings ("Get hardware specifications" + "Talk to an engineer about Data Acquisition")
- [x] No vocabulary that backfires per buyer-taxonomy §2.5 ("cloud-native", "platform" as primary descriptor)
- [x] No certifications claimed, no fabricated metrics, no competitor names
- [x] All components from design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] §3.5 "How this differs from PLC-based data acquisition" callout present per §9 emerging-pattern governance
- [x] **v2 R1 applied** — §3.2 mDAQ card body uses concrete instrument vocabulary (flow meters / pressure transmitters / level transmitters / temperature probes / vibration sensors / 4-20 mA loop-powered transducers) per Plant Engineer vocabulary discipline
- [x] **v2 R2 applied** — §3.4 deployment footprint sharpened with "remote and unmanned sites" + worked examples (pipeline pump stations, mining outposts, well heads, off-grid water infrastructure)

---

## 8. Out of scope for v1

- **Full mDAQ product detail.** Phase E `/mdaq` covers: full sensor compatibility matrix, environmental certifications (CE / UL / FCC / IP rating), enclosure dimensions and mounting patterns, full I/O channel detail, deployment installation guides.
- **Solution narratives.** `/solutions/edge-connectivity` (Phase 2 step 10). `/solutions/brownfield-modernization` (existing v2; v3 in Phase E).
- **Industry-specific framings.** Phase 3 `/industries/<industry>`.
- **Pricing detail.** Phase 3 `/pricing` or commercial-engagement teaser on `/platform`.
- **VAS analytics that ride the mDAQ platform.** Covered on `/capabilities/condition-monitoring` (LOCKED).

---

*`/capabilities/data-acquisition` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review (verdict: "one of the most disciplined capability pages in the entire Phase 2 set… may actually become the reference example for future hardware-oriented pillar pages"). Fourth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 5. Content-only spec — inherits `CapabilityDeepDive` layout from `page-capabilities-condition-monitoring-spec-v1` (LOCKED). **v2 changes from v1:** R1 concrete instrument vocabulary in §3.2 + R2 remote-and-unmanned sites sharpening in §3.4 (both reviewer-marked optional polish; applied per user direction to move from "approve" to "reference example"). Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.5 + §2.3, proof-architecture v1, design-system v3 §14 + §16 + §17, hardware-ecosystem-map v3 §3.*
