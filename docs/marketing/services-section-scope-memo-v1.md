<!--
File:    docs/marketing/services-section-scope-memo-v1.md
Purpose: Scope memo (plan-trail v1) for adding a Services section to
         www.elpisitsolutions.com, led by Elpis's AVEVA System Integrator
         credential. For review before drafting copy / building the mockup.
Status:  v1 DRAFT — awaiting copy review + the open inputs in §7.
Date:    2026-06-08
-->

# Scope memo — Services section (AVEVA SI + engineering services)

## 1. Why
The new site is **product-only** (EdgeConnect/EREMOS/hardware) and dropped Elpis's **services identity** entirely — the old site's `/our-services/*` pages 301 to the homepage. Elpis is also an **authorised AVEVA Member System Integrator** and an engineering-services company. This memo adds a Services section, led by the AVEVA SI credential.

## 2. Decisions locked (user, 2026-06-08)
- Build a **full Services section** (not just AVEVA).
- Add a **"Services" nav dropdown** (new top-level).
- **Fold Architecture + Security under the Platform dropdown** to make nav room.
- **6 service pages.** Copy **drafted from the old-site offerings**, then user-reviewed.

## 3. The AVEVA credential (verbatim from the certificate)
- "Elpis IT Solutions Pvt. Ltd is an **authorised AVEVA Member System Integrator**."
- **SI Number 516322** · Enrolled 29 May 2025 · Issued 9 June 2025.
- **Use the tier exactly** — "Authorised AVEVA Member System Integrator." Do NOT inflate to Select/Premier/Endorsed.
- **Display:** an HTML credential card (title + SI number + dates) + a "View certificate (PDF)" download of the supplied PDF. **No fabricated AVEVA logo** — the official AVEVA partner badge image can be added later per AVEVA brand guidelines.

## 4. Positioning — the dual identity (must stay consistent)
The site/blog say "EdgeConnect/EREMOS run *beside* your SCADA — not a replacement." Adding SCADA system-integration is complementary, framed as:
> Elpis is **both** an AVEVA SCADA System Integrator (services) **and** an industrial-intelligence product company. As an SI we design, deploy, and integrate AVEVA SCADA; our products are vendor-neutral and complement any SCADA, including AVEVA. One partner can deliver the SCADA layer *and* the edge/OEE layer.

## 5. Information architecture
**`/services` hub** + 6 pages:
| Slug | Page |
|---|---|
| `/services/aveva-scada-integration` | **AVEVA SCADA Integration** (flagship; hosts the SI credential card + cert PDF) |
| `/services/data-analytics-ai` | Data Analytics & AI |
| `/services/embedded-design` | Embedded Design & Development |
| `/services/hardware-design` | Hardware Design & Development |
| `/services/web-app-development` | Web App Development |
| `/services/mobile-app-development` | Mobile App Design & Development |

**Nav restructure (net 7 top-level, was 8):**
- **Platform** (dropdown) → EdgeConnect · EREMOS V2 · **Architecture** · **Security** *(Architecture + Security move here; removed as standalone items)*
- Capabilities · Solutions · Industries · **Hardware** (dd) · **Services** (dd) · Blog
- Services dropdown lists the 6 pages.

## 6. Honesty boundaries (binding)
- **AI-as-a-service ≠ product AI.** "Data Analytics & AI" is a *services* capability (we build analytics/AI solutions for clients). It must NOT blur the product rule "AI is decision-support, never in the data path."
- No fabricated metrics, client names, or certifications beyond the real AVEVA SI. SI tier verbatim.
- Engineering-services copy describes capabilities at a credible, general level — no invented specifics; user supplies/corrects real detail on review.

## 7. Open inputs needed
1. **AVEVA official partner badge image** (optional, per AVEVA brand guidelines) — else text credential card only.
2. **OK to publish SI Number 516322** on a public page? (verifiable credential ID; confirm.)
3. **Per-service specifics** — review the drafted copy and correct with real offerings / tech / industries served.

## 8. Build sequence (plan-trail)
1. This memo (review).
2. Draft copy for all 6 (text-first) → review.
3. Static HTML mockup: **/services hub + AVEVA flagship** as pattern-setters → sign-off.
4. Build all 6 + nav restructure (Services dd; fold Architecture/Security into Platform) + sitemap + **repoint old `/our-services/*` 301s** to the new pages (currently → home) + add the cert PDF to assets.
5. Deploy (cPanel) + re-submit sitemap.

*v1 DRAFT — decisions §2 locked; AVEVA tier §3 verbatim. Awaiting copy review + §7 inputs, then mockup.*
