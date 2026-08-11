<!--
File:    docs/marketing/linkedin-content-plan.md
Purpose: LinkedIn posting plan + ready-to-post captions for the Industrial
         Intelligence Blog drip. 1 post/week, Tuesdays. Link in the post body.
Status:  Launch + weeks 2-4 LOCKED (review-corrected); weeks 5-11 to follow.
Date:    2026-06-08
-->

# LinkedIn content plan — Elpis Industrial Intelligence Blog

## Cadence & tactics
- **1 post / week, every Tuesday**, ~9–11am IST.
- **Founder reshare** from a personal profile the same day (out-reaches the company page).
- **First-hour engagement:** a few teammates like/comment; reply to every comment.
- **Image:** attach the article's hero diagram (`assets/blog/<slug>-hero.png`).
- **Link:** in the post body (not first comment).
- **Honesty rules apply** (same as the site): no fabricated metrics, no competitor names, protocol status accurate (OPC UA Client = collect from external OPC UA Servers; OPC UA Server = expose outward; MT-LINKi REST = roadmap), beside-not-replacing, trust anchors verbatim. Do **not** name specific agencies unless separately cleared.

## 11-week sequence
1 Launch · 2 FANUC · 3 EdgeConnect · 4 OEE calc · 5 Condition Monitoring vs PdM · 6 Protocols · 7 Brownfield · 8 EREMOS V2 · 9 Defensible OEE · 10 Canonical Data · 11 Store-and-Forward. Then recycle best performers + product/behind-the-scenes.

---

## Week 1 — Launch announcement
*Image: homepage / OG image. Optional overlay text: "New website + Industrial Intelligence Blog now live".*

> We've launched our new website → www.elpisitsolutions.com
>
> Elpis builds an Industrial Intelligence Ecosystem — from shop-floor signal to enterprise decision.
>
> One platform to connect supported mixed-vendor machines and control systems, map supported readings into a canonical vocabulary at the edge, and turn those signals into OEE, alarms, dashboards, and reports.
>
> It runs beside your existing SCADA, historian, MES, HMI, and PLC systems — not as a rip-and-replace.
>
> Operating across India and the Middle East. Deployed in demanding defense and space-agency programs.
>
> We've also launched the Industrial Intelligence Blog — practical, engineering-led writing on edge connectivity, OEE, condition monitoring, store-and-forward, and brownfield Industry 4.0. No buzzwords.
>
> Take a look → www.elpisitsolutions.com
>
> #IndustrialAutomation #Industry40 #IIoT #SmartManufacturing #Manufacturing

**Founder reshare:**
> Months of work from our team — proud to share our new home and the start of the Industrial Intelligence Blog.
> www.elpisitsolutions.com

---

## Week 2 — FANUC CNC data collection
*Image: `fanuc-cnc-data-collection-hero.png`*

> Your FANUC machines already hold the data you want — spindle speed, run state, cycle counts, alarms.
>
> The hard part is not that the data does not exist. It is getting it out reliably, from every machine, in a form your team can actually use.
>
> Many teams start by writing a script over FOCAS2. It works in a demo, then meets the floor: connection handling matters, controller generations and enabled functions can differ, and firmware or configuration changes can create maintenance work nobody planned to own.
>
> We wrote up why home-grown FANUC collectors can become fragile — and what a maintainable edge-runtime approach looks like instead. No rip-and-replace; it runs beside your existing systems.
>
> Read it → www.elpisitsolutions.com/blog/fanuc-cnc-data-collection
>
> #IndustrialAutomation #CNC #IIoT #SmartManufacturing #Manufacturing

---

## Week 3 — EdgeConnect
*Image: `what-is-edgeconnect-hero.png`*

> A real factory floor is an archaeology of controllers — a 2009 FANUC next to a newer cell, a Siemens line, Brother machines, older equipment on Modbus, and systems that already work.
>
> The data you want is in many of them. The problem is that each speaks a different dialect.
>
> EdgeConnect is our protocol-agnostic edge runtime. It collects from supported sources using FANUC FOCAS2, MTConnect, Brother HTTP, Modbus TCP, Siemens S7, and external OPC UA Server sources through its OPC UA Client capability.
>
> It maps supported readings into a canonical vocabulary at the edge, where the required values are available, then publishes onward through MQTT or exposes mapped data through EdgeConnect's OPC UA Server.
>
> When required signals are mapped into the canonical model, downstream dashboards, historian logic, and OEE rules can often remain stable while the edge layer absorbs the protocol complexity.
>
> What EdgeConnect is, in plain terms → www.elpisitsolutions.com/blog/what-is-edgeconnect
>
> #IndustrialAutomation #IIoT #EdgeComputing #SmartManufacturing #CNC

---

## Week 4 — OEE
*Image: `how-to-calculate-oee-hero.png`*

> OEE looks simple — one percentage.
>
> But ask two supervisors how they calculate it and you may get two different numbers. Breaks counted on one line, excluded on another. "Downtime" defined three ways. Ideal rates nobody has revisited in years.
>
> The formula — Availability × Performance × Quality — is the easy part.
>
> The hard part is agreeing on the definitions behind it: running, planned stop, unplanned stop, setup, inspection, production count, and quality rules. Then applying those definitions the same way on every line, every shift.
>
> That is what makes the number trustworthy.
>
> A practical guide to calculating OEE correctly, on your definitions — not a vendor preset:
> www.elpisitsolutions.com/blog/how-to-calculate-oee
>
> #OEE #SmartManufacturing #IndustrialAutomation #Manufacturing #Industry40

---

## Weeks 5–11 — to be drafted
Condition Monitoring vs PdM · Industrial Protocols Explained · Brownfield Industry 4.0 · EREMOS V2 · Defensible OEE Reports · Canonical Data at the Edge · Store-and-Forward in IIoT.
