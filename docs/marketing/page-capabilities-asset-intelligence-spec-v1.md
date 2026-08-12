<!--
File:        docs/marketing/page-capabilities-asset-intelligence-spec-v1.md
Purpose:     Page spec for /capabilities/asset-intelligence — Pillar 3
             deep-dive (mTracker). Content-only spec inheriting the
             CapabilityDeepDive layout locked in
             page-capabilities-condition-monitoring-spec-v1.md.
Audience:    Internal — Angular engineering team, copywriters, user +
             ChatGPT (reviewers).
Format:      Per §9 canonical template locked in
             page-capabilities-hub-spec-v1.md.
Companion:   page-capabilities-condition-monitoring-spec-v1.md (LOCKED — layout precedent)
             buyer-taxonomy-v1.md §2.2 (Plant manager / Ops VP — PRIMARY
                per amendment v3 §1.5 reverse-mapping flip; was OEM in v1
                first-draft) + §2.6 (OEM machine builder — SECONDARY)
             hardware-ecosystem-map-v3.md §4 (mTracker pillar source)
Version:     v2 — LOCKED after Pass 1 ChatGPT review (Asset Intelligence
                  is the hardest pillar to explain; v2 strengthens that)
Date:        2026-05-29 (v2 lock)
Status:      LOCKED.

Fifth per-page spec in the Phase 2 wave per amendment v3 §6
sequencing step 5. Content-only.

IMPORTANT BUYER-MAPPING CONTEXT: buyer-taxonomy v1 amendment §3
flipped /capabilities/asset-intelligence reverse mapping —
Plant manager / Ops VP is now PRIMARY (multi-site visibility,
utilization); OEM machine builder is SECONDARY (service-hours
billing, warranty fleet). This spec aligns to that flip.

Pass 1 ChatGPT review verdict (2026-05-29):
  "Approve after a focused v2 refinement pass. Structure is sound.
   Buyer alignment is correct. Trust model is excellent. The category
   explanation simply needs to be strengthened. Asset Intelligence is
   the least intuitively understood pillar in the Industrial Intelligence
   Ecosystem — the page must spend a little more effort helping readers
   understand WHY Asset Intelligence exists when they already have
   controller telemetry."

User decision on v2 refinements (2026-05-29):
  - R1 (hero softening — operational-value-first, location demoted)        APPLIED
  - R2 (§3.5 controller-vs-Asset-Intelligence differentiator callout)      APPLIED — "single most important improvement before lock" per ChatGPT
  - R3 (operational-pattern adjacencies framing for §3.4)                  DEFERRED-by-reviewer (not for v1/v2; not added to §8 deferral list per user direction)
  - R4 (CTA sharpening: "asset list" vs "fleet")                           NOT APPLIED — "Bring us your fleet" retained

Sections receiving "no change" reviewer approval:
  §1.2 buyer alignment, §3.2 mTracker card ("very well calibrated"),
  §3.3 BOM elimination ("permanent CapabilityDeepDive element"),
  §3.6 trust cue ("one of the strongest trust cues in the entire
  ecosystem"), §3.7 related solutions, §3.8 cross-lens.

Post-draft additions (governance compliance, not content changes):
  - 2026-05-28: §1.4 Page metadata (SEO + HTML head) block added
    per /capabilities hub §9 metadata governance lock (PR #71).
-->

# `/capabilities/asset-intelligence` — Page Spec v1

**Capability deep-dive for the Asset Intelligence pillar (mTracker). Inherits the locked `CapabilityDeepDive` layout. Content-only spec.**

This is the page where Plant managers / Ops VPs land when they need utilization, location, and OEE visibility on equipment they operate across multiple sites — and where OEM machine builders land when they want fleet visibility on machines they've shipped. It is **not** a product detail page (mTracker Phase E `/mtracker`). It is **not** the OEM solution narrative (`/solutions/oem-machine-monitoring`).

Target length: **800-1,200 words page copy** per `/capabilities` hub spec §9.

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** Capability deep-dive for Asset Intelligence. Reader leaves with *"I now understand when I'd use mTracker for utilization and OEE telemetry — whether for the assets I run, or the assets I've shipped to customers."*

**IS NOT:** A full mTracker product detail page (Phase E `/mtracker`). A solution narrative (that's `/solutions/oem-machine-monitoring` for the OEM angle, and `/solutions/multi-site-operations` for the operations angle).

### 1.2 Buyer alignment (per buyer-taxonomy v1 — POST-amendment §3 mapping flip)

**Primary buyer:** Plant manager / Ops VP (§2.2) — *flipped from first-draft v1 per buyer-taxonomy amendment*
- Lands here when scoping multi-site asset visibility, utilization tracking, or OEE telemetry across plants
- Wants: outcome-specific framing (downtime visibility, OEE accountability), multi-site capability
- CTA preference: *"Bring us your fleet"* / *"Book a scoping call"* > *"Talk to engineering"*
- Vocabulary that lands: utilization, OEE inputs, multi-site visibility, fleet, geo-fence, downtime
- Vocabulary that backfires: *"AI insights"*, *"smart factory"*, *"digital transformation"*

**Secondary buyer:** OEM machine builder (§2.6)
- Lands here when scoping connected-equipment programs: service-hours billing, warranty triggers, fleet visibility on shipped machines
- Wants: customer-respecting telemetry posture (per buyer-taxonomy §2.6), service-economics math
- CTA preference: *"Bring us your installed base"*

### 1.4 Page metadata (SEO + HTML head)

Per `/capabilities` hub spec v1 §9 "Per-page metadata governance" (LOCKED 2026-05-28). Pattern reference: `/capabilities/operational-intelligence` spec v1 §1.4.

| Field | Value |
|---|---|
| **Meta title** (50-60 chars) | *Asset Intelligence — fleet visibility & utilization · Elpis* |
| **Meta description** (140-160 chars) | *mTracker for multi-site asset visibility, utilization tracking, and OEE telemetry. Plant-manager and OEM fleet-program use cases. Customer-respecting telemetry.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/capabilities/asset-intelligence` |
| **Schema intent** | `schema.org/WebPage` with `BreadcrumbList`. mTracker hardware/service card cross-links to Phase E `/mtracker` via `Product` schema (when shipped). Page-to-page cross-link to `/architecture` uses `relatedLink`. |

---

## 2. Page structure — sections at a glance

Per design-system v3 §14 `CapabilityDeepDive` layout. Same 9 sections.

| # | Section | Mode | Component(s) |
|---|---|---|---|
| 1 | Hero | `dark-deep` | `SectionShell` + `Button` × 2 |
| 2 | Products in this pillar (mTracker) | `dark` | `CapabilityCard` × 1 with pillar-3 accent |
| 3 | What this pillar eliminates from your BOM | `light` | Bulleted list |
| 4 | Strategic adjacencies | `light` | 3-column grid |
| 5 | Where this fits in the Industrial Intelligence Stack | `light-tinted` | `DiagramFrame` focused on Pillar 3 + cross-link to `/architecture` |
| 6 | Trust posture for this pillar | `light-tinted` | §16 trust cue content pattern |
| 7 | Related solutions | `light` | `CapabilityCard` × 2 (solution-card variant) |
| 8 | Cross-lens navigation | `light-tinted` | §17 cross-lens pattern (3 cards) |
| 9 | Final CTA | `dark-deep` | `CTASection` |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: CAPABILITY · ASSET INTELLIGENCE
>
> HEADLINE (size.3xl semibold):
> Know which assets are productive, which are idle, and where they're deployed — across every site and shift.
>
> CUSTOMER QUESTION LEAD (italic):
> *"How do I track utilization, location, and OEE on equipment I've shipped or deployed across multiple sites?"*
>
> PRIMARY CTA: Bring us your fleet
> HREF: `/contact?intent=asset-intelligence-scoping`
>
> SECONDARY CTA: Talk to an engineer about Asset Intelligence
> HREF: `/contact?intent=asset-intelligence-engineering`

---

### 3.2 Section 2 — Products in this pillar

> EYEBROW: PRODUCTS IN THIS PILLAR

#### Card — mTracker (pillar-3 accent)

> EYEBROW: ASSET UTILIZATION + OEE TELEMETRY
> TITLE: mTracker — Miniature GSM/GPS asset-tracking and OEE telemetry
> BODY:
> Tracks utilization of industrial assets — fixed and mobile — and reports OEE inputs (production time, downtime, idle time, output quantity) directly from equipment-level digital signals. Built-in GSM/GPS 4G with battery backup. Geo-fence alerts for asset-presence and movement. Equipment-level digital inputs for runtime / cycle counts. Designed for retrofit attachment to existing equipment without re-engineering the host.
> FOOTER: Hardware appliance · GSM/GPS 4G + battery · geo-fence alerts · retrofit-friendly
> LINK: *(Phase E product detail — coming soon)*

---

### 3.3 Section 3 — What this pillar eliminates from your BOM

> EYEBROW: WHAT THIS PILLAR ELIMINATES FROM YOUR BOM
>
> SUBHEAD:
> Asset Intelligence consolidates utilization, location, and OEE telemetry into one device — instead of three or four overlapping point tools.
>
> BULLETED LIST:
>
> - Manual production-hour spreadsheets (operator fills in start-of-shift; supervisor stitches at end-of-week)
> - Separate GPS / geo-fence trackers (a different device + a different subscription for the same asset)
> - Service-hours odometers wired in just to trigger warranty / maintenance reminders
> - Annual asset-presence audits to confirm equipment is still where the records say it is
> - The reporting effort to reconcile "what the spreadsheet says" with "what the GPS tracker says" with "what the operator remembers"

---

### 3.4 Section 4 — Strategic adjacencies

> EYEBROW: WHO IT'S FOR · WHERE IT DEPLOYS
>
> COLUMN 1 — BUYERS:
> - **Plant manager / Ops VP** — multi-site visibility, utilization patterns, OEE accountability
> - **OEM machine builder** — service-hours billing, warranty triggers, fleet visibility on shipped equipment
> - **Plant engineer (retrofit)** — sizing mTracker installs onto existing equipment without re-engineering the host
>
> COLUMN 2 — INDUSTRIES:
> - Manufacturing — multi-site operators tracking idle assets across plants
> - OEM machine monitoring (machine builders with installed-base service programs)
> - Mining & Construction (mobile heavy equipment, fleet visibility)
> - Logistics-adjacent industrial (geo-fenced asset compliance)
> - Aerospace ground-support equipment (utilization tracking on tow tractors, pushback rigs, ground-power units)
>
> COLUMN 3 — DEPLOYMENT FOOTPRINT:
> - Operating across India and the Middle East
> - Per-gateway identity + customer/site binding established at first start — clean fleet identity from day one
> - Retrofit-attachable: no re-engineering of the host equipment required
> - Battery-backed: deployable on equipment without continuous mains power

---

### 3.5 Section 5 — Where this fits in the Industrial Intelligence Stack

> EYEBROW: WHERE IT FITS
>
> SECTION TITLE:
> Asset Intelligence is the equipment-level telemetry layer of the Industrial Intelligence Stack.
>
> CALLOUT — HOW THIS DIFFERS FROM CONTROLLER MONITORING (size.base, single paragraph; visual treatment: light tinted card or left-rule callout):
>
> > **How this differs from controller monitoring.** Controller monitoring tells you what a machine is doing right now — spindle load, axis position, alarm state. Asset Intelligence tells you **where each asset is, how often it's used, whether it's productive, and how it performs across sites** — even when the equipment has no PLC, when the controller is locked behind a vendor's proprietary boundary, or when the question isn't *"what is this machine doing"* but *"is this asset earning its keep."*
>
> BODY:
> Pillar 3 captures equipment-level utilization, location, and OEE inputs — the signals that don't come from controllers (because the equipment doesn't have a PLC) or from sensors (because the question is "is this asset running" not "what's the pressure"). mTracker publishes via MQTT through Pillar 1 (Connectivity & Edge) into Pillar 5 (Operational Intelligence) where utilization rolls into OEE Segments, geo-fence events become incident records, and service-hours feed warranty / maintenance reporting.
>
> DIAGRAM FRAME (DiagramFrame focused on Pillar 3 layer)
>
> CAPTION:
> *Pillar 3 captures equipment-level intelligence — utilization, location, OEE inputs. See the full Industrial Intelligence Stack → `/architecture`*

---

### 3.6 Section 6 — Trust posture for this pillar

Per §16 trust cue content pattern. Cue focus per buyer-taxonomy v1: per-gateway identity + customer/site binding.

> EYEBROW: TRUST POSTURE
>
> BODY:
> Each mTracker carries a stable gateway UUID and customer/site binding established at first start. Fleet identity is unambiguous from day one. Acquisitions, divestitures, plant transfers, name changes — the identity model survives them all. For OEMs shipping equipment with mTracker embedded: customer-controlled routing means the customer decides which utilization / location / OEE signals route back to the OEM service organization, which stay local to the customer's operations, and which are exposed to no one.
>
> CROSS-LINK: Read the full operational trust posture → `/security`

---

### 3.7 Section 7 — Related solutions

> EYEBROW: RELATED SOLUTIONS

#### Card 1 — Multi-site Operations (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · MULTI-SITE OPERATIONS
> TITLE: One operational view across every plant
> BODY: For multi-plant operators consolidating utilization, OEE, and asset visibility — mTracker captures the equipment-level signals; EREMOS V2 aggregates across sites.
> LINK: Read the solution → `/solutions/multi-site-operations` *(existing v2; v3 in Phase E)*

#### Card 2 — OEM Machine Monitoring (existing v2; v3 in Phase E)

> EYEBROW: SOLUTION · OEM MACHINE MONITORING
> TITLE: Ship connected equipment that customers actually accept
> BODY: For OEMs embedding mTracker in shipped equipment — service-hours billing, warranty triggers, fleet visibility, without compromising customer-IT trust.
> LINK: Read the solution → `/solutions/oem-machine-monitoring` *(existing v2; v3 in Phase E)*

---

### 3.8 Section 8 — Cross-lens navigation

Per §17 preset for `/capabilities/<pillar>`:

| Card | Eyebrow | Description | Destination |
|---|---|---|---|
| 1 | ARCHITECTURE | How does this pillar fit the data flow? | `/architecture` |
| 2 | SOLUTION · MULTI-SITE OPERATIONS | The outcome-organised version for plant operators | `/solutions/multi-site-operations` |
| 3 | CAPABILITIES | Back to all 5 pillars | `/capabilities` |

> Looking for the same thing from another angle?

---

### 3.9 Section 9 — Final CTA

Per buyer-taxonomy v1 §2.2 Plant manager / Ops VP CTA preference (primary buyer post-flip):

> EYEBROW: NEXT STEP
>
> HEADLINE:
> Bring us your fleet. We'll scope visibility.
>
> SUBHEAD:
> Whether you're a plant operator wanting utilization visibility across sites, or an OEM scoping fleet visibility on shipped equipment — bring us the asset list and we'll scope what mTracker delivers and where it fits with the rest of your operational stack.
>
> PRIMARY CTA: Bring us your fleet
> HREF: `/contact?intent=asset-intelligence-scoping`
>
> SECONDARY CTA: Talk to an engineer about Asset Intelligence
> HREF: `/contact?intent=asset-intelligence-engineering`

---

## 4. Components used

All from design-system v3 LOCKED — no new components.

`SectionShell` (modes), `CapabilityCard` (pillar-3 + compact), `Button` (primary + secondary, lg), `DiagramFrame`, `CTASection`, §16 trust cue pattern, §17 cross-lens pattern.

---

## 5. Verbatim copy summary

Page copy collected in sections §3.1-§3.9. ~950 words total (within 800-1,200 target). Increase from v1 baseline (~870 words) reflects the v2 §3.5 "How this differs from controller monitoring" callout block.

---

## 6. Anti-patterns specific to this page

| Don't | Why |
|---|---|
| Position mTracker primarily for OEMs in the hero / §1 framing | Per buyer-taxonomy amendment §3 — Plant manager / Ops VP is PRIMARY (multi-site visibility); OEM is SECONDARY. Hero must lead with the primary buyer's lens. |
| Use *"AI insights"* / *"smart factory"* / *"digital transformation"* | Backfires with both Plant manager and OEM audiences per buyer-taxonomy §2.2 + §2.6 |
| Omit OEM angle entirely | OEM is real secondary buyer — covered in §3.4 strategic adjacencies, §3.6 trust posture (customer-controlled routing), §3.7 related solutions |
| Add specific service-hours pricing models | Service-hours-billing detail belongs on `/solutions/oem-machine-monitoring` (existing v2; v3 in Phase E) |
| Add competitor names (Samsara, Verizon Connect, Trackunit, etc.) | Per proof-architecture v1 §8 — competitive framing is sales-objection-guide territory |

---

## 7. Sign-off checklist (v2 lock)

- [x] Page copy fits 800-1,200 word target (current: ~950 words)
- [x] All 9 sections per CapabilityDeepDive layout
- [x] Customer question verbatim from hardware-ecosystem-map v3 §1
- [x] mTracker card uses accurate descriptors from hardware-ecosystem-map v3 §4.1
- [x] PRIMARY BUYER ALIGNMENT IS PLANT MANAGER / OPS VP per buyer-taxonomy amendment §3 (NOT OEM, which is now secondary)
- [x] §3.6 trust cue covers BOTH per-gateway identity (Plant manager angle) AND customer-controlled routing (OEM angle)
- [x] §3.7 related solutions include both Multi-Site Operations (Plant manager) and OEM Machine Monitoring (secondary OEM)
- [x] Final CTA uses Plant-manager-preferred framing ("Bring us your fleet")
- [x] No vocabulary that backfires per §2.2 / §2.6
- [x] No fabricated metrics, no competitor names
- [x] All components from design-system v3 LOCKED
- [x] Page-spec structure follows §9 canonical template
- [x] §1.4 Page metadata block present per §9 metadata governance
- [x] **v2 R1 applied** — §3.1 hero leads with operational-value framing (productive / idle), location demoted to third clause
- [x] **v2 R2 applied** — §3.5 includes explicit "How this differs from controller monitoring" callout addressing the *"my PLC already tells me runtime"* buyer question (per ChatGPT v1 review: "the single most important improvement before lock")

---

## 8. Out of scope for v1

- **Full mTracker product detail.** Phase E `/mtracker` covers: full hardware specs, environmental certifications, geo-fence configuration patterns, integration with existing CMMS / warranty systems, deployment patterns.
- **Service-hours billing detail / warranty integration patterns.** Lives on `/solutions/oem-machine-monitoring`.
- **Multi-site operations narrative.** Lives on `/solutions/multi-site-operations`.
- **Industry-specific framings.** Phase 3 `/industries/<industry>`.
- **Pricing detail.** Phase 3 `/pricing` or commercial-engagement teaser on `/platform`.

---

*`/capabilities/asset-intelligence` Page Spec **v2 LOCKED 2026-05-29** after Pass 1 ChatGPT review. Fifth per-page spec in the Phase 2 wave per amendment v3 §6 sequencing step 5. Content-only — inherits `CapabilityDeepDive` layout from `page-capabilities-condition-monitoring-spec-v1` (LOCKED). **POST-buyer-taxonomy-amendment alignment** — Plant manager / Ops VP is PRIMARY, OEM is SECONDARY (per buyer-taxonomy v1 amendment §3 reverse-mapping flip). **v2 changes from v1:** R1 hero softening (operational-value-first, location demoted) + R2 §3.5 explicit "How this differs from controller monitoring" callout (per ChatGPT "single most important improvement before lock"). Cites: phase2-ia-scope-memo v2 + amendment v3, buyer-taxonomy v1 §2.2 + §2.6 + §3, proof-architecture v1, design-system v3 §14 + §16 + §17, hardware-ecosystem-map v3 §4.*
