<!--
File:    docs/marketing/services-section-scope-memo-v2.md
Purpose: Scope memo (plan-trail v2, LOCKED) for the Services section.
         Supersedes v1. Embeds the user-confirmed copy + guardrails.
Status:  v2 LOCKED 2026-06-08. UPDATE 2026-06-08: Web App + Mobile App
         dropped — 4 services (AVEVA, Embedded, Hardware, Data Analytics & AI).
Date:    2026-06-08
-->

# Scope memo v2 (LOCKED) — Services section

Supersedes `services-section-scope-memo-v1.md`. All confirmations from the 2026-06-08 review are folded in below.

## 1. Decisions (locked)
- Full **Services** section; **new "Services" nav dropdown**; keep Products and Services **separate** in nav.
- **Fold Architecture + Security into the Platform dropdown** (remove as standalone nav items).
- **6 pages**, `/services` hub + 6. Copy below is **approved**.
- Build order (§11): hub → AVEVA → Embedded first; then Hardware, Data Analytics & AI, Web, Mobile.

## 2. AVEVA credential — display rules (strict)
- Public credential card shows **only**:
  > **Authorised AVEVA Member System Integrator**
  > SI Number 516322
  > [View certificate (PDF)](/assets/aveva-si-certificate.pdf)
- **Do NOT display** enrolled/issued dates on the page (the PDF keeps them; do not edit the PDF).
- **Exact wording only:** "Authorised AVEVA Member System Integrator." Forbidden: "Certified AVEVA Partner", "Premier", "Select", "Strategic Partner", "Approved Integrator".
- **No AVEVA logos / partner badges / screenshots / official marks** (no permission). Text card + PDF only.
- **Trademark note** (footer of the AVEVA page): "AVEVA and AVEVA product names are trademarks of AVEVA Group Limited or its subsidiaries. Elpis displays its system-integrator credential based on the issued certificate."

## 3. Services hub intro (approved)
> Engineering services + industrial intelligence products, from one partner.
>
> Elpis combines engineering services with its own Industrial Intelligence products. As an Authorised AVEVA Member System Integrator, we design and integrate SCADA, embedded, hardware, software, and analytics systems for industrial operations — and connect them with edge data collection, OEE, alarms, dashboards, and reports.
>
> One accountable partner for the SCADA layer, the edge layer, and the operational-intelligence layer.

## 4. AVEVA SCADA Integration page (approved)
- **H1:** AVEVA SCADA Integration · **Subtitle:** Authorised AVEVA Member System Integrator
- **Credential card:** as §2.
- **What we do:**
  > Elpis designs, deploys, and integrates SCADA and operations-control solutions on AVEVA platforms, including AVEVA System Platform, AVEVA Plant SCADA, and AVEVA Historian. As an Authorised AVEVA Member System Integrator, we support projects from architecture and specification through configuration, commissioning, integration, and support.
  >
  > Project scope is defined around the customer's installed AVEVA products, site architecture, control systems, historian requirements, reporting needs, and integration points.
- **Positioning (no competitor comparison):**
  > Many SCADA projects focus only on the supervisory-control layer. Elpis can also connect that layer with edge connectivity and operational intelligence through EdgeConnect and EREMOS V2.
  >
  > EdgeConnect collects data from supported industrial sources and maps supported readings into a canonical model at the edge. EREMOS V2 turns clean machine signals into OEE, alarms, dashboards, and reports. These products sit beside existing SCADA, historian, MES, HMI, and PLC systems; they do not replace control logic, operator workflows, alarm acknowledgement, historians, or MES processes.

## 5. Embedded Design & Development page (approved)
- **H1:** Embedded Design & Development · **Subtitle:** Embedded firmware and device software for industrial and defense systems.
- **Intro:**
  > Elpis develops embedded firmware and device-level software for industrial and defense applications — from microcontroller-based systems and edge devices to sensor interfaces, communications, diagnostics, and field-ready embedded code.
- **What we do:** Firmware for industrial & defense devices · MCU & edge-device software · sensor/peripheral integration · industrial communication interfaces · device diagnostics, logging, maintainability · board bring-up + HW/SW integration · embedded support for rugged field-deployable systems · **early-stage ASIC-related engineering support, where project scope requires it**.
- **ASIC wording:** only "early-stage ASIC-related engineering support". Forbidden: "full ASIC design house", "end-to-end ASIC delivery", "silicon-proven", "defense-certified ASIC design".
- **Where it applies:** industrial monitoring devices, edge gateways, sensor systems, defense electronics, embedded control modules, connected equipment.
- **Honesty boundary (on page):** "Certification, qualification, and compliance scope are defined project by project. Elpis does not claim military-grade, aerospace-grade, or certified rugged compliance unless explicitly documented for the project."

## 6. Hardware Design & Development page (approved)
- **H1:** Hardware Design & Development · **Subtitle:** Industrial electronics from concept to prototype and production support.
- **Intro:**
  > Elpis supports industrial and defense hardware development across schematic design, PCB layout, prototyping, sensor/interface integration, board bring-up, and hardware/software integration.
  >
  > Where appropriate, projects can include ASIC-adjacent or ASIC-support activities. Elpis is not presented as a full ASIC design house unless that capability is separately confirmed.
- **Avoid:** CE/UL/ATEX/IP-rated, military-grade, aerospace-grade, production-volume claims — unless documented.

## 7. Data Analytics & AI page
- Services capability (bespoke analytics/ML for clients). **Boundary on page:** "AI is used as decision support where appropriate. Deterministic data pipelines, control logic, and safety-critical workflows remain deterministic and governed." Distinct from product data path.

## 8. Web App / Mobile App pages — DROPPED (2026-06-08)
Not built. May revisit later.

### (original notes, retained for history)
- Web apps/portals/dashboards; native + cross-platform mobile (operator/field tools, monitoring, companion apps). Generic-but-credible; user to add stacks/examples. No fabricated specifics.

## 9. IA + nav
- `/services` hub + `/services/<slug>`: aveva-scada-integration, embedded-design, hardware-design, data-analytics-ai, web-app-development, mobile-app-development.
- Pages live in a real `/services/` folder, root-relative links, `web.config` pretty-URL rules (mirrors `/blog`).
- **Nav (net 7 top-level):** Platform (dd: EdgeConnect · EREMOS V2 · Architecture · Security) · Capabilities · Solutions · Industries · Hardware (dd) · Services (dd: the 6) · Blog.
- **Repoint old 301s:** `/our-services/*` currently → `/`; map to the new `/services/*` equivalents in `web.config`.

## 10. Honesty guardrails (binding)
AVEVA tier verbatim, no inflation, no logos/marks, trademark note. AI-as-a-service ≠ product AI-in-data-path. No fabricated certs/metrics/clients; defense/industrial domain claims kept general; compliance "project by project". ASIC "early-stage" only.

## 11. Build sequence
1. v2 memo (this).
2. **Batch 1:** nav restructure + `/services` hub + AVEVA + Embedded + web.config (`/services` rules + `/our-services` 301 repoint) + sitemap.
3. **Batch 2:** Hardware, Data Analytics & AI, Web, Mobile.
4. Deploy (cPanel) + re-submit sitemap.

*v2 LOCKED 2026-06-08.*
