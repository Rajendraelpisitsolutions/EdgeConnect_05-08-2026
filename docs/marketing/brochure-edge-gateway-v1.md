<!--
File:        docs/marketing/brochure-edge-gateway-v1.md
Purpose:     Content/copy for the Edge Gateway field brochure (the asset the
             /resources/brochures page currently shows as "Request access").
             SHAPE-SETTER for the 5 hardware brochures — mDAQ / mTracker /
             VAS / E-IDOS inherit this field-sheet template.
             This is the COPY (markdown); the print/PDF design is a separate
             step. Condensed verbatim from the LOCKED product spec — no new
             facts introduced.
Audience:    Plant engineer (BOM/spec buyer, buyer-taxonomy §2.5) + the AMC /
             reseller who hands it across a table.
Source-of-truth (do not exceed): page-product-edge-gateway-spec-v2 (LOCKED)
             + hardware-ecosystem-map-v3 §2.2. Specs trace to the map; verify
             before any external print run.
Version:     v2 — LOCKED 2026-06-06 (ChatGPT Pass-1 edit applied).
Date:        2026-06-06
Status:      LOCKED. Field-sheet shape-setter for the 5 hardware brochures.
v1 -> v2: "separate Linux gateway for protocol bridging" -> "...for standalone
          Modbus TCP + cellular publishing use today" (scopes the BOM claim).

DISCIPLINE (inherited by all 5 brochures):
  - Specs VERBATIM from the locked product spec; no invented numbers
    (proof-architecture §3). "Confirmed during BOM scope" where the spec says so.
  - NO formal certification CLAIMS. IP65/IP67-COMPATIBLE only (never
    "certified"/"rated"/"IP-rated"); cert/IP/site-compliance handled
    case-by-case during BOM scope (hardware-ecosystem-map §264).
  - Protocol honesty (P-G): Edge Gateway today = built-in Modbus TCP +
    cellular publish; broader EdgeConnect protocol matrix arrives with the
    EdgeConnect Linux runtime (roadmap). Dual identity honest; appliance
    optional, never required for EdgeConnect.
  - No customer names, no competitor names, no fabricated metrics.
  - Plant-engineer CTA: "Get hardware specifications" / "Request a BOM scope".

BROCHURE FIELD-SHEET TEMPLATE (the 9 blocks the other 4 inherit):
  1. Header (product · category · one-line value)
  2. What it is (1 short paragraph)
  3. What it does (3–5 bullets)
  4. What it replaces / why it matters
  5. Key specifications (honest table; no cert row)
  6. In the field (mounting / power / connectivity / offline)
  7. Where it fits (one line + platform context + cross-refs)
  8. Field-readiness (no-cert honesty note)
  9. Next step (CTA + contact)
-->

# Edge Gateway — hardware brochure (v2 LOCKED)

**Category:** Connectivity & Edge — hardware appliance (Pillar 1)
**One-line:** The ruggedized DIN-rail appliance that bridges your existing PLC fleet to the network — embedded Linux, built-in Modbus TCP, cellular publish — in one box.

---

## What it is

The Edge Gateway is a ruggedized industrial gateway running embedded Linux. It bridges existing PLC fleets to the network over Modbus TCP, publishes over cellular, and is web-configurable with USB firmware updates — on a DIN-rail box sized for a control cabinet. No separate industrial PC is required for standalone Modbus TCP + cellular gateway use today.

It has a **dual identity**: today, a standalone PLC-to-network gateway; tomorrow, once the EdgeConnect Linux runtime ships, the **canonical EdgeConnect appliance** — same hardware, two lifecycles. The appliance is an option, not a requirement: EdgeConnect also runs software-only on customer Windows hardware today.

## What it does

- **Bridges PLCs to the network.** Built-in Modbus TCP server/client connects existing PLC fleets and publishes over cellular.
- **Embedded Linux, web-configurable.** Configure sources and publishing from a browser — no separate industrial PC for standalone gateway use today.
- **Cellular + Wi-Fi + Ethernet.** Connectivity for remote sites without running new cable.
- **USB firmware updates.** Field-serviceable without a toolchain.
- **Offline-capable.** Operates without a persistent connection; cellular publish resumes when connectivity returns.

## What it replaces

One Edge Gateway removes from the customer BOM:
- a separate **industrial PC** for standalone Modbus TCP + cellular gateway use today;
- a separate **Linux gateway** for standalone Modbus TCP + cellular publishing use today;
- a separate **cellular modem** for remote sites;
- the need to **host EdgeConnect on customer-owned Windows infrastructure** once the EdgeConnect Linux runtime ships (until then, the full EdgeConnect runtime still runs software-only on Windows).

## Key specifications

| Category | Value |
|---|---|
| Compute | Embedded Linux · 256 MB RAM · 2 GB Flash |
| Power | 24 V DC (current draw, terminal type, circuit-protection confirmed during BOM scope) |
| Enclosure | Ruggedized · 200 × 150 × 75 mm |
| Ingress protection | IP65 / IP67-**compatible** configurations can be scoped where a site requires it — *compatibility, not a certified rating; no formal IP certification is currently claimed* |
| Mounting | DIN-rail (control cabinet); clearance, cable routing, antenna placement confirmed during BOM scope |
| Connectivity | 4G (cellular) · Wi-Fi · Ethernet (SIM / carrier / antenna + network assumptions confirmed during BOM scope) |
| Built-in protocol | Modbus TCP (server/client) |
| Onboard I/O | PLC / network gateway — **not a DAQ module**. Direct analog/digital sensor acquisition is handled by mDAQ. |
| Management | Web-configurable · USB firmware updates |
| Cellular publish | Built-in (standalone mode today) |

*Physical specifications trace to the Elpis hardware ecosystem map and are confirmed at quoting time. Broader protocol coverage (FOCAS2, MTConnect, OPC UA Client, Siemens S7, …) arrives when the EdgeConnect Linux runtime ships on this hardware.*

## In the field

DIN-rail mount in the control cabinet on 24 V DC industrial power. Cellular for remote sites; Wi-Fi or Ethernet where available. Built-in Modbus TCP bridges the PLC fleet today. Firmware updates over USB — field-serviceable. Operates offline and resumes cellular publish on reconnect. Operating environment, cabinet clearance, and any ingress-protection requirement are confirmed during the BOM scope against your cabinet and site constraints.

## Where it fits

PLC fleet → **Edge Gateway** (built-in Modbus TCP + cellular publish today) → network / MQTT → EREMOS V2. It is the appliance form of the Connectivity & Edge pillar, and the future EdgeConnect appliance footprint — but EdgeConnect also runs software-only, so the box is an option, not a prerequisite.

## Field-readiness

Built for the cabinet, not the office: ruggedized enclosure, 24 V DC industrial power, DIN-rail mount, embedded Linux. Offline-first and remote-ready — built-in cellular publishes from remote sites and resumes on reconnect, with no dependency on customer Windows infrastructure in appliance mode.

*Formal third-party certifications are not currently claimed. Certification, ingress-protection, and site-compliance requirements are handled case-by-case during BOM scope; IP65 / IP67-compatible configurations can be scoped where required, and certified/rated claims are published only when formal evidence exists for the specific product/configuration.*

## Next step

Bring your PLC list, your site connectivity (cellular vs. wired), and your cabinet constraints — that's what we scope a BOM against.
**Get hardware specifications · Request a BOM scope** — contact@elpisitsolutions.com

---

*Edge Gateway brochure **v2 LOCKED 2026-06-06** (ChatGPT Pass-1: Linux-gateway BOM line scoped to "standalone Modbus TCP + cellular publishing use today") — field-sheet shape-setter for the 5 hardware brochures. Copy condensed verbatim from the LOCKED `page-product-edge-gateway-spec-v2` + `hardware-ecosystem-map-v3 §2.2`; no new facts. No formal cert claims (IP65/IP67-compatible only); protocol honesty (Modbus TCP today, broader matrix with EdgeConnect Linux); dual identity; appliance optional. ChatGPT Pass 1 applied; v2 LOCKED. The other four (mDAQ / mTracker / VAS / E-IDOS) inherit the 9-block template above.*
