<!--
File:        docs/marketing/email-outreach-templates-v1.md
Purpose:     Sales-team-ready email templates for outbound outreach about
             the Elpis Industrial Intelligence Platform. Three-touch cold
             sequence + post-demo follow-up + win-back.
Audience:    Elpis sales team — account executives, business development reps,
             founders doing direct outreach.
Format:      Markdown templates with subject lines, body copy, customization
             variables, and sending guidance.
Version:     v1 (first draft)
Date:        2026-05-24

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md
  - docs/marketing/homepage-copy-v2.md
  - docs/marketing/sales-objection-handling-internal-v2.md (voice consistency)
  - All five solution pages (for vertical-specific variants)

Voice: matches the rest of the marketing system. Confident-technical,
premium-industrial, outcomes-first, no buzzwords. Industrial-OT-specific
outreach hygiene: no tracking pixels, no obvious mass-personalization
tells, no "exciting opportunity" language, plain text preferred.

Templates use {VARIABLES} for per-recipient customization. The opening
sections of the document specify which variables matter and where to
source them (LinkedIn, company website, industry filings, plant
information available on customer sites).
-->

# Email & Outreach Templates — v1

**Sales-team-ready cold outreach copy for the Elpis Industrial Intelligence Platform.**

This guide covers five template categories: a 3-touch cold sequence (for first-time outreach), a post-demo follow-up (for prospects who saw a demo and went quiet), and a win-back (for stalled or paused deals). Each template includes subject-line options, body copy, customization variables, and sending guidance.

---

## How to use these templates

- **They're starting points, not scripts.** Personalize before sending. A template that lands in 100 inboxes verbatim looks like exactly what it is — a template.
- **Industrial OT buyers detect mass outreach instantly.** Generic personalization (`{First Name}`, vague industry references) gets you flagged as spam. Specific personalization (the prospect's actual machines, their plant location, a real industry pressure they're facing) gets you read.
- **Keep them short.** No industrial buyer reads a 400-word cold email. The strongest version is 80-120 words plus a clear ask.
- **One clear ask per email.** Don't bury three CTAs. Pick the right one for the stage.
- **No tracking pixels.** OT engineers and industrial IT leads see open-tracking as a violation of trust. They'll mark your domain as spam.
- **Real signature, real role, real phone number.** No "Sent from my iPhone" hiding the rep.
- **Avoid these phrases entirely:** "Hope this finds you well," "I came across your profile," "Quick question," "Just checking in," "Exciting opportunity," "Game-changing," "Revolutionary," "AI-powered."

---

## Industrial-OT cold-outreach principles

A short reference for behaviors that matter in this market specifically:

1. **Send Tuesday–Thursday, 9 AM–11 AM local.** Industrial buyers check email between shift handovers and morning meetings. Mondays they're firefighting; Fridays they're closing out the week.
2. **Plain text or minimal HTML.** Heavy templates trigger spam filters and look like marketing automation. A plain-text email from a real person is more likely to be read.
3. **No links in the first email.** Some industrial-IT spam filters auto-quarantine cold emails with links. Send the link in the second touch after the recipient has engaged.
4. **Reply directly from the rep's address, not a marketing automation sender.** Replies should go to a human, not a noreply.
5. **No attachments in the first email.** Same spam-filter concern. Offer the datasheet in the second touch.
6. **Reference specific machines or protocols where possible.** "Your Fanuc 18i CNCs at the Indianapolis facility" beats "your manufacturing operation."

---

## Cold sequence — Touch 1

**Purpose:** First contact. Establish relevance. Earn a reply (not a meeting yet).
**Length target:** 80-120 words.
**Send timing:** Tuesday or Wednesday, 9:30-10:30 AM local time.

### Subject-line options (pick one based on the prospect's role)

For plant managers / Ops VPs:
- *Mixed-vendor CNC monitoring without per-machine scripting*
- *OEE you can defend, at {Company}*
- *{Company}'s CNC visibility, without replacing controllers*

For industrial IT / SCADA engineers:
- *FOCAS2, MT-LINKi, Brother HTTP, Modbus TCP — one service*
- *Edge-first industrial data layer for {Company}*
- *Connecting {Company}'s mixed-vendor floor — without a custom integration project*

For OEM product managers:
- *Connected equipment without building the connectivity stack yourself*
- *Customer-controlled telemetry for {Company}'s installed base*

### Body template

> {First Name},
>
> Most {industry} shops I talk to are running three to seven different controller vendors — Fanuc lathes from one era, Brother machining centers from another, maybe some Modbus-fronted PLCs in front of older CNCs. The OEE numbers from each one don't reconcile, and stitching them together by hand has become a real cost.
>
> EdgeConnect runs a single service on a small box in your control cabinet that polls every controller natively — FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP — and normalizes the data into one canonical vocabulary. EREMOS V2 turns that into OEE Segments, alarm tracking, and shift reports your team will actually read.
>
> Worth a 20-minute call to see if there's a fit for {Company}?
>
> — {Rep first name}
> {Title} · Elpis IT Solutions
> {Phone} · {Email}

### Customization variables

| Variable | Source | Example |
|---|---|---|
| `{First Name}` | LinkedIn / company website | Aisha |
| `{Company}` | LinkedIn / company website | Precision Components Ltd |
| `{industry}` | Industry filings / company description | precision machining, automotive parts, OEM CNC |
| `{Rep first name}` / `{Title}` / `{Phone}` / `{Email}` | Sales-rep standard footer | — |

### Per-vertical adjustments

- **For brownfield-heavy prospects:** swap the opening from "three to seven different controller vendors" to "Fanuc 16i/18i controllers from the 2010s alongside newer machines."
- **For multi-site prospects:** swap to "{N} plants, each with its own monitoring tool, each reporting OEE differently to corporate."
- **For OEM prospects:** swap entirely — see the OEM Touch 1 below.

### OEM-specific Touch 1 body

> {First Name},
>
> Most OEMs I talk to have considered building a connected-equipment platform — embedded gateway, cloud back-end, mobile dashboard — and stalled when they got to the customer-IT conversation. Customers won't allow always-on telemetry. The connectivity story that was supposed to differentiate the equipment becomes the friction that kills the sale.
>
> EdgeConnect deploys with your machine and lets *your customer* control what flows back to your service organization. Service-relevant telemetry routes to you; operational data stays local. No always-on remote access, no data exfiltration, no customer-IT escalation.
>
> Worth a 20-minute call to see if there's a fit for {Company}'s installed base?
>
> — {Rep first name}
> {Title} · Elpis IT Solutions
> {Phone} · {Email}

---

## Cold sequence — Touch 2

**Purpose:** Follow up the silence with a different angle. Offer a specific resource.
**Length target:** 60-90 words.
**Send timing:** 4-7 days after Touch 1. Tuesday or Wednesday.

### Subject-line options

- *RE: {Touch 1 subject}* (threaded reply)
- *Two minutes on Elpis for {Company}?*
- *Sending the datasheet*

### Body template (general)

> {First Name},
>
> Following up on last week's note. I won't keep pinging you, but I wanted to share the platform datasheet in case it's useful as you think through this:
>
> {Datasheet URL or PDF attached on this touch only}
>
> Two questions that usually clarify whether there's a fit:
>
> 1. How many controller vendors does your floor currently run?
> 2. How is OEE reported today — directly from the controllers, or stitched together from spreadsheets?
>
> Either way, no pressure. Happy to be useful if there's a fit; happy to step back if there isn't.
>
> — {Rep first name}

### Notes

- The two qualifying questions do double duty: they're useful to the prospect (helps them self-diagnose) and useful to the rep (qualifies fit before a call).
- "No pressure" closing is intentional. Industrial buyers are protective of their time; explicitly disclaiming pressure earns more trust than another CTA push.

### Per-vertical adjustments

- **For brownfield prospects:** replace question 1 with *"What's the oldest CNC on your floor — and is it still in active production?"*
- **For multi-site prospects:** replace questions with *"How many plants currently report OEE separately? And how consistent are the OEE definitions across them?"*
- **For OEM prospects:** replace questions with *"How many machines have you shipped that you'd want service-visibility on? And what's your truck-roll cost per dispatch?"*

---

## Cold sequence — Touch 3

**Purpose:** Last touch. Honest, brief, plants a memory. Doesn't try to convert.
**Length target:** 50-80 words.
**Send timing:** 5-10 days after Touch 2. End of week is fine (Thursday or Friday morning) since this isn't asking for action.

### Subject-line options

- *Closing the loop*
- *Last note from me on this*
- *Stepping back — file this if useful*

### Body template

> {First Name},
>
> I don't want to clutter your inbox, so this is the last note from me on this thread. If now isn't the right time for {Company} to look at multi-vendor industrial data infrastructure, that's completely fine.
>
> If the topic does come up later — whether for a new line, a brownfield modernization, a new customer audit requirement, or just a quieter quarter — happy to pick this up then. The datasheet from last week is yours to file.
>
> — {Rep first name}
> {Phone} · {Email}

### Notes

- The "if now isn't the right time" framing respects the prospect's autonomy. Industrial buyers reward respect; they punish persistence.
- Naming specific future triggers (new line, brownfield modernization, customer audit) plants useful associations for when those events actually happen.
- Don't ask for a reply. The point is to leave the door open, not to wedge it.
- After Touch 3, stop. If the prospect responds later, that's the strongest possible re-engagement signal.

---

## Post-demo follow-up

**Purpose:** Re-engage a prospect who saw a demo and went quiet. Provide a specific next-step option.
**Length target:** 100-150 words.
**Send timing:** 2-4 days after the demo. If they were enthusiastic in the demo, lean toward 2 days. If they were skeptical, lean toward 4.

### Subject-line options

- *Following up on the EdgeConnect demo*
- *Next step on the {Company} scoping*
- *Recap from {date} — and what's next*

### Body template

> {First Name},
>
> Thanks again for the time on {date}. A few things from the demo that I wanted to flag for your reference:
>
> - {Specific moment 1 from the demo — e.g., "the Fanuc FOCAS2 source you ran reads from your 18i controllers without modification"}
> - {Specific moment 2 — e.g., "the EREMOS V2 OEE Segments view you saw is what would land in your shift reports"}
> - {One concern they raised, with a follow-up answer or commitment}
>
> Based on what we discussed, I think the highest-leverage next step is {one of}:
>
> 1. **A scoping call with our engineering lead** — to confirm the platform works against your specific controller mix before any further commitment.
> 2. **A proof-of-value engagement on one cell** — week-one deployment, real signals, real OEE. We've scoped this for similar shops in 5-8 weeks.
> 3. **An architecture review with your IT/security team** — to surface any procurement-side concerns early.
>
> What feels right for you?
>
> — {Rep first name}

### Notes

- The "specific moments from the demo" section is mandatory — it proves you remember the conversation. Templated post-demos that skip this read as automated.
- The "one concern they raised" line is critical. If the demo had a skeptical moment, address it explicitly. Pretending it didn't happen loses the deal.
- Three options for the next step lets the prospect self-select their comfort level — scoping call (lower commitment), proof-of-value (moderate), architecture review (higher process commitment, lower technical risk).
- "What feels right for you?" closes with their agency, not yours.

---

## Win-back

**Purpose:** Re-open a stalled or paused deal 3+ months after the last contact. Provide a credible reason to re-engage.
**Length target:** 100-130 words.
**Send timing:** Any reasonable trigger — a platform milestone (e.g., OPC UA Server shipped), a customer-side trigger (new fiscal year, recent acquisition, new plant), or a relevant industry event.

### Subject-line options

- *Coming back around — has anything changed at {Company}?*
- *Updates since we last talked*
- *Following up after {previous-conversation context}*

### Body template

> {First Name},
>
> We talked back in {month} about whether EdgeConnect made sense for {Company}'s {specific scenario from previous conversation}. At the time you mentioned {specific reason for the pause — e.g., "you wanted to wait until after the Q1 budget cycle," "you were evaluating an internal-build option first," "the timing wasn't right with the plant expansion"}.
>
> Two reasons I'm circling back now:
>
> 1. {Specific change at {Company} — new fiscal year, recent acquisition, new line announced, etc. — that might change the timing}
> 2. {Specific Elpis update — OPC UA Server shipped, new vertical-specific deployment, new customer story you can share}
>
> If the timing is right now, happy to pick up the conversation. If not, no pressure — just wanted to make sure I didn't disappear on you.
>
> — {Rep first name}

### Notes

- Naming the specific past pause-reason is mandatory. Generic win-back emails get ignored; ones that reference the actual previous conversation get read.
- The two-reason format frames the re-engagement as informed and respectful, not pushy.
- "Just wanted to make sure I didn't disappear on you" is intentionally human — it acknowledges the previous silence without apologizing for it.
- If the previous deal stalled because of a competitive loss, the win-back is different — see the Competitive-loss win-back below.

### Competitive-loss win-back

If the deal was lost to a specific competitor (Kepware, Ignition, in-house build, cloud IoT, etc.), use this variant:

> {First Name},
>
> You went with {competitor} back in {month} for {Company}'s {specific scenario}. Hope the deployment has gone well — and I genuinely mean that, no sour grapes here.
>
> Two reasons I'm following up:
>
> 1. Most multi-vendor industrial deployments end up needing more than the original tool covers. If {Company}'s scope has expanded — more controllers, more sites, OEE accountability, audit-readiness — we sometimes fit alongside the platform you've already deployed, rather than instead of it.
> 2. {Specific Elpis update worth knowing about — OPC UA Server, a new vertical, etc.}
>
> No pressure to reply. Just wanted to leave the door open.
>
> — {Rep first name}

### Notes

- "Hope the deployment has gone well" is genuine. Wishing the customer well even after losing earns respect.
- "Sometimes fit alongside the platform you've already deployed, rather than instead of it" is the coexistence framing from the objection guide — even on a competitive-loss win-back, don't position as a replacement.
- After this email, don't re-engage for another 6+ months unless the customer responds.

---

## Subject-line library (quick reference)

A consolidated list of all subject-line variants used above, organized by audience and intent:

**For plant managers / Ops VPs (first touch):**
- *Mixed-vendor CNC monitoring without per-machine scripting*
- *OEE you can defend, at {Company}*
- *{Company}'s CNC visibility, without replacing controllers*

**For industrial IT / SCADA engineers (first touch):**
- *FOCAS2, MT-LINKi, Brother HTTP, Modbus TCP — one service*
- *Edge-first industrial data layer for {Company}*
- *Connecting {Company}'s mixed-vendor floor — without a custom integration project*

**For OEM product managers (first touch):**
- *Connected equipment without building the connectivity stack yourself*
- *Customer-controlled telemetry for {Company}'s installed base*

**Follow-ups:**
- *RE: {previous subject}* (threaded reply, second touch)
- *Two minutes on Elpis for {Company}?*
- *Sending the datasheet*

**Closing:**
- *Closing the loop*
- *Last note from me on this*
- *Stepping back — file this if useful*

**Post-demo:**
- *Following up on the EdgeConnect demo*
- *Next step on the {Company} scoping*
- *Recap from {date} — and what's next*

**Win-back:**
- *Coming back around — has anything changed at {Company}?*
- *Updates since we last talked*
- *Following up after {previous-conversation context}*

---

## Anti-patterns — do not send

- **No "Hope this finds you well"** — instant template-recognition tell.
- **No emoji in subject lines** — flags as marketing automation.
- **No `[ACTION REQUIRED]` or `[URGENT]` in subject lines** — manipulative and reads as spam.
- **No tracking pixels** — industrial IT detects and flags them.
- **No more than one link in the first email** — many spam filters auto-quarantine cold emails with multiple links.
- **No PowerPoint or PDF attachments in Touch 1** — wait until Touch 2 after engagement.
- **No "I noticed you / your company"** language that's clearly automated — be specific about what you noticed (a specific machine, a specific industry pressure, a specific recent announcement) or don't reference noticing at all.
- **No CTA stacking** ("book a call OR download the datasheet OR check out our website OR follow us on LinkedIn"). One ask per email.
- **No "circling back" or "checking in"** unless the prior thread genuinely had something to circle back to.
- **No corporate boilerplate sign-offs** ("At Elpis IT Solutions, we are committed to..."). Just the rep's name, role, phone, email.
- **No mass-CC** anyone except the prospect's direct address.

---

## Cadence and sequencing

Standard 3-touch cadence for cold outreach:

| Touch | Timing | Purpose | Length |
|---|---|---|---|
| Touch 1 | Day 0 (Tue/Wed AM) | Establish relevance | 80-120 words |
| Touch 2 | Day +5 to +7 (Tue/Wed AM) | Different angle, offer datasheet | 60-90 words |
| Touch 3 | Day +12 to +17 (Thu/Fri AM) | Honest close-out | 50-80 words |

Post-demo:

| Touch | Timing | Purpose |
|---|---|---|
| Post-demo follow-up | 2-4 days after demo | Recap + next-step options |
| If no response | +7 days | Single short follow-up referencing the post-demo email |
| If still no response | +14 days | One last note, then move to win-back queue |

Win-back:

| Touch | Timing | Purpose |
|---|---|---|
| Win-back | 3+ months after last contact | Re-open with specific reason |
| If response | Normal sales cadence | — |
| If no response | +6 months | Single short follow-up |
| If still no response | Stop | Leave the door open without further outreach |

---

## Customization variables — sourcing checklist

Before sending any template, verify you have:

- [ ] **First name** — from LinkedIn or company website
- [ ] **Company** — exactly as the prospect writes it (some have legal-name-vs-trade-name differences that matter)
- [ ] **Industry** — specific enough to use ("precision machining" not "manufacturing")
- [ ] **Specific machine or controller reference** — sourced from public information (case studies, job postings, plant tours, industry filings)
- [ ] **Recent company event** (for win-back) — acquisition, new line announcement, fiscal-year change, exec hire
- [ ] **Previous conversation context** (for win-back) — your CRM should have this; if it doesn't, the rep needs to reconstruct from email history before sending

---

## Sign-off checklist

Before any cold sequence goes out:

- [ ] Subject line picked from the library or written fresh — confirm it does NOT trigger spam-filter keywords
- [ ] Body customized with at least 2 variables (not just `{First Name}`)
- [ ] No tracking pixels
- [ ] No links in Touch 1
- [ ] No attachments in Touch 1
- [ ] Plain text or minimal HTML
- [ ] Real rep signature with phone number
- [ ] Send time confirmed (Tue/Wed AM local for Touch 1 and Touch 2; Thu/Fri AM for Touch 3)
- [ ] Sender reputation: avoid bulk-sending the same template to many recipients on the same day from the same domain

---

## What's out of scope for v1

- **Industry-specific deep variants** — automotive parts, aerospace, energy each have their own outreach patterns; v1 covers the core vertical agnostically
- **A/B test framework** — pick one subject line per send for v1; build A/B testing infrastructure later once volume justifies it
- **Marketing-automation integration** — these are rep-direct templates; if marketing automation is added later, the templates need re-checking for sender-reputation impact
- **LinkedIn message templates** — LinkedIn outreach is a different format and a separate deliverable
- **Event-driven sequences** — post-trade-show, post-webinar, post-content-download all merit their own templates later
- **OEM-partner sequences** — distinct from end-customer outreach; separate deliverable
- **Localized templates** (Japanese, German, Mandarin) — English-only in v1

---

*Email & Outreach Templates — v1, 2026-05-24. Pair with sales-objection-handling-internal-v2 for in-call follow-through. Standard rep practice: read both before running an outbound campaign.*
