<!--
File:    docs/sessions/2026-06-13-linkedin-posting-handoff.md
Purpose: Self-contained handoff to run the Elpis LinkedIn posting program in
         its own session. Everything a fresh session needs: status, cadence,
         the locked captions, what's left to draft, the rules, and the assets.
Status:  Handoff. Read this first when continuing LinkedIn posting.
Date:    2026-06-13
-->

# Handoff — LinkedIn posting program

## TL;DR
The website + a **10-article blog** are live at www.elpisitsolutions.com. The job in *this* track is to run a **weekly LinkedIn drip** of those articles (1/week, Tuesdays) and keep the cadence going.

**Source of truth for captions:** `docs/marketing/linkedin-content-plan.md`
**Related (LinkedIn outbound, not posting):** `docs/marketing/customer-outbound-kit.md`

## Status
- ✅ **Plan + cadence** locked (`linkedin-content-plan.md`).
- ✅ **Captions LOCKED & ready to post:** Week 1 (launch announcement) + Weeks 2–4 (FANUC, EdgeConnect, OEE-calc), review-corrected. Link in the post body; hero image attached.
- ⬜ **TO DRAFT: Weeks 5–11** captions (7 posts) — stubbed at the bottom of `linkedin-content-plan.md`.

## Cadence & tactics (locked)
- **1 post / week, every Tuesday**, ~9–11am IST.
- **Founder reshare** from a personal profile the same day (out-reaches the company page).
- **First hour:** a few teammates like/comment; reply to every comment.
- **Image:** attach the article's hero diagram, `assets/blog/<slug>-hero.png`.
- **Link:** in the post body (not first comment).

## The 11-week sequence (posting order)
| Wk | Post | Blog URL (`www.elpisitsolutions.com` + ) | Hero image (`assets/blog/`) | Caption |
|----|------|------------------------------------------|------------------------------|---------|
| 1 | Launch announcement | / | og-default.png (or homepage) | ✅ LOCKED |
| 2 | How to Collect Data from FANUC CNCs | /blog/fanuc-cnc-data-collection | fanuc-cnc-data-collection-hero.png | ✅ LOCKED |
| 3 | What Is EdgeConnect? | /blog/what-is-edgeconnect | what-is-edgeconnect-hero.png | ✅ LOCKED |
| 4 | How to Calculate OEE Correctly | /blog/how-to-calculate-oee | how-to-calculate-oee-hero.png | ✅ LOCKED |
| 5 | Condition Monitoring vs Predictive Maintenance | /blog/condition-monitoring-vs-predictive-maintenance | condition-monitoring-vs-predictive-maintenance-hero.png | ⬜ draft |
| 6 | Industrial Protocols Explained | /blog/industrial-protocols-explained | industrial-protocols-explained-hero.png | ⬜ draft |
| 7 | What "Brownfield" Industry 4.0 Really Means | /blog/brownfield-industry-4-0 | brownfield-industry-4-0-hero.png | ⬜ draft |
| 8 | What Is EREMOS V2? | /blog/what-is-eremos-v2 | what-is-eremos-v2-hero.png | ⬜ draft |
| 9 | How to Build Defensible OEE Reports | /blog/defensible-oee-reports | defensible-oee-reports-hero.png | ⬜ draft |
| 10 | Canonical Data at the Edge | /blog/canonical-data-at-the-edge | canonical-data-at-the-edge-hero.png | ⬜ draft |
| 11 | Store-and-Forward in IIoT | /blog/store-and-forward-iiot | store-and-forward-iiot-hero.png | ⬜ draft |

After week 11: recycle the best performers + product/behind-the-scenes posts.

## How to run a posting session
- **To post a ready week (1–4):** copy the caption from `linkedin-content-plan.md`, attach the hero image, post Tuesday, founder reshares, team engages.
- **To draft weeks 5–11:** same cadence as the blog — **draft caption → ChatGPT review pass → apply corrections → lock into the doc.** Then post.

## Caption rules (binding — same honesty discipline as the site)
- **No fabricated metrics** (%/$/OEE-gain/uptime/latency); no customer or competitor names.
- **Protocol status accurate:** collection = FANUC FOCAS2, MTConnect, Brother HTTP, Modbus TCP, Siemens S7, and OPC UA Client (reads from external OPC UA Servers); output = MQTT publishing + EdgeConnect's own OPC UA Server. **MT-LINKi REST = roadmap.**
- **Beside-not-replacing** SCADA / historian / MES / HMI / PLC.
- **Condition monitoring / PdM = early-warning aid, not a guarantee.** Store-and-forward: never "never loses data."
- **Trust anchors verbatim**, no agency names: "Operating across India and the Middle East." / **"Deployed in demanding defense and space-agency programs."**
- **AVEVA:** "Authorised AVEVA Member System Integrator" exactly (no inflation); no AVEVA logos/marks.
- **AI** = decision-support, never in the data path.

## Where to point the ask (NEW since the plan was written)
The site now has a **conversion layer**: a contact form (`/contact`) and a **free-assessment landing page (`/assessment`)** wired to the Elpis CRM (+ email backup). For any post or comment with a call-to-action, **point it at `www.elpisitsolutions.com/assessment`** ("get a free assessment of your floor") rather than a generic contact. Add UTM tags (e.g. `?utm_source=linkedin&utm_campaign=blog-drip`) so Plausible attributes the leads.

## Companion: LinkedIn *outbound* (separate from posting)
`docs/marketing/customer-outbound-kit.md` has the LinkedIn **outbound** sequence (connection note → first message → follow-up), targeting plant managers / OT leads in India + the Middle East, also routing to `/assessment`. Posting (this doc) and outbound (that doc) compound — run both.

## Cross-references
- `docs/marketing/linkedin-content-plan.md` — captions (source of truth).
- `docs/marketing/customer-outbound-kit.md` — LinkedIn outbound + Microsoft co-sell + AVEVA directory.
- `docs/sessions/2026-06-08-website-launch-and-blog-handoff.md` — site + blog launch context.
