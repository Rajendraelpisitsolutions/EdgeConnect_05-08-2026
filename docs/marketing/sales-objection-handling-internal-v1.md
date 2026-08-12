<!--
File:        docs/marketing/sales-objection-handling-internal-v1.md
Purpose:     Internal sales-enablement guide for handling common competitive
             and "build-in-house" objections in customer conversations about
             the Elpis Industrial Intelligence Platform.
Audience:    Elpis sales team — account executives, sales engineers, founders
             in sales conversations.

INTERNAL ONLY. Do not distribute to customers, prospects, or partners.
This document names competitors directly, acknowledges where the
platform is weaker, and identifies deals we will lose. That candor is
operationally useful internally and commercially damaging if leaked.

Format:      Markdown sales-enablement reference. Each objection follows the
             same 6-part structure:
               1. The customer's actual words (how they'll phrase it)
               2. What they're really asking (underlying concern)
               3. Recommended response (specific talking points)
               4. What NOT to say (anti-patterns)
               5. Where you'll lose this deal (honest disqualification)
               6. Supporting evidence (where to point the customer)
Version:     v1 (first draft)
Date:        2026-05-24

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/security-page-copy-v2.md (trust positioning)
  - docs/marketing/architecture-diagram-spec-v2.md (architecture)
  - All five solution pages (per-vertical evidence)
  - CLAUDE.md §1, §3 (locked architectural decisions)

Voice for this document is DIFFERENT from every external-facing piece:
  - Direct, frank, no marketing softening
  - Names competitors by name (Kepware, Ignition, HiveMQ, AWS, Azure)
  - Acknowledges competitive strengths where they exist
  - Honest about where Elpis loses
  - Concrete talking points, not marketing copy
  - "Do not say" warnings to prevent rep over-claims

If this document ever needs to be quoted externally, the external version
must strip every competitor reference, every "where we lose" admission,
and every "do not say" warning. Those exist only for internal use.
-->

# Sales Objection-Handling Guide — Internal v1

**INTERNAL ONLY. Do not share with customers, prospects, or partners.**

This guide arms the Elpis sales team for the seven most common objections that come up in real customer conversations about the Elpis Industrial Intelligence Platform. Every objection has its own section with the customer's actual words, what they're really asking, the recommended response, what not to say, where we'll lose the deal, and which platform evidence to point at.

The tone is deliberately direct. Competitors are named. Loss scenarios are acknowledged. The goal is to win the deals we can win and qualify out of the deals we can't — not to pretend we win everything.

---

## How to use this document

- **Read the whole thing once.** The principles in one objection often apply to others.
- **Don't memorize the talking points verbatim.** Internalize the framing and adapt to the conversation.
- **Take the "where you'll lose" sections seriously.** They protect your pipeline accuracy and your time.
- **Escalate when you see a pattern.** If a new objection comes up three times, tell marketing — we'll add it to v2.
- **Never quote this document to a customer.** The honesty that makes it useful internally is damaging externally.

---

## General principles

Before diving into specific objections, four principles that apply to every competitive conversation:

1. **Acknowledge the competitor's strength first.** Customers know if you're badmouthing. Saying *"Kepware is the OPC connectivity incumbent and they earned that position"* gets you more credibility in the next sentence than dismissing them does.

2. **Reframe the question, don't answer it directly.** If the customer asks *"why not Kepware?"*, the right answer often isn't *"because we're better than Kepware at X."* It's *"Kepware ends where the data leaves the gateway; we start there."* Different category, different question.

3. **Name where we lose.** Customers respect *"if you only need OPC server functionality and nothing else, Kepware is the right answer."* They distrust vendors who claim every deal.

4. **Anchor on a specific platform property, not a marketing slogan.** "Hash-chained audit log" wins more procurement reviews than "best-in-class auditability."

---

## Objection 1 — "Why not Kepware (KEPServerEX)?"

### The customer's words

> *"We already use Kepware for our OPC server."*
> *"We're evaluating KEPServerEX for this project."*
> *"Our IT team is standardized on Kepware."*

### What they're really asking

*"Is your protocol coverage worth switching from the industry incumbent? And is your story differentiated enough to justify the change?"*

### Recommended response

Acknowledge Kepware's strength: it's the industry-incumbent OPC connectivity layer. Most large OT environments have used it for years. That's earned.

Then reframe: **Kepware ends where the data leaves the gateway. We start there.**

Specific talking points:

- **Kepware is OPC-server-first; we're operational-platform-first.** Kepware delivers protocol translation. We deliver protocol translation *plus* canonical CNC vocabulary, store-and-forward buffering, three-way diagnostics, hash-chained config audit, AI constraint architecture, modern Connectivity Studio UI, and EREMOS V2 intelligence layer — all in one platform.
- **Canonical vocabulary across vendors is unique.** Kepware delivers raw tags as they appear in the controller. We normalize across vendors so the same dashboard works on Fanuc, Brother, Mazak, and Modbus-fronted CNCs without per-machine custom mapping.
- **EREMOS V2 is included, not a separate purchase.** Kepware customers still need to build the OEE / alarms / shift-reports layer themselves (or buy a separate analytics platform). We deliver both layers.
- **Modern operational discipline.** Hash-chained audit, signed offline licensing, AI proposes/humans decide — none of those are Kepware focuses. They're architectural to ours.
- **Coexistence is fine.** If they want to keep Kepware where it works and add Elpis where it doesn't (CNC-specific protocols, OEE, multi-site), say so. Both can publish to MQTT or OPC UA Server. You don't have to be a rip-and-replace pitch.

### What NOT to say

- ❌ "Kepware doesn't work" — it does.
- ❌ "We have more protocols than Kepware" — they have more total. They've been at this for 25 years.
- ❌ "Kepware is old-fashioned" — it's stable, which is what OT buyers want.
- ❌ "Kepware can't scale" — it scales fine for what it's designed to do.

### Where you'll lose this deal

- The customer needs **OPC DA legacy support** (we don't ship it).
- The customer has **decade-deep Kepware investment** with custom drivers and trained operators.
- The customer needs a **pure protocol-converter** with no analytics layer (Kepware is leaner and probably cheaper for that scope).
- The customer's IT team has a **standing Kepware enterprise license** that costs them nothing per additional gateway.

Qualify out gracefully if any of those apply.

### Supporting evidence

- Datasheet v4 "Why Elpis" section
- Security page (offline-first, signed licensing)
- CNC machining solution page (canonical vocabulary specifically)
- Architecture diagram

---

## Objection 2 — "Why not Ignition (Inductive Automation)?"

### The customer's words

> *"We're considering Ignition for our SCADA stack."*
> *"Our integrator recommends Ignition."*
> *"We're already running Ignition at another plant."*

### What they're really asking

*"Do we need a full SCADA suite, or a focused integration layer? And how do you two relate?"*

### Recommended response

Acknowledge Ignition's real strength: it's a legitimate full SCADA platform with unique unlimited-licensing model and a powerful screen-builder. That's a genuine product.

Then reframe: **Ignition is a SCADA suite. We're an industrial data platform. They're not the same product.**

Specific talking points:

- **Different categories.** Ignition is HMI + SCADA + alarming + screen-builder + scripting + database connectivity. We're protocol-agnostic data layer + canonical vocabulary + OEE + alarm tracking + multi-site aggregation. Where Ignition stops being a SCADA, we usually start. Where we stop being a data platform, Ignition often does what's next.
- **Edge-first vs server-first.** EdgeConnect runs on a small box in the control cabinet. Ignition Gateway runs on a server (or several). Different deployment footprint, different operational economics.
- **Lightweight, focused.** We don't build screens. We don't ship a scripting language. We don't host your operators' HMI. If you want any of that, Ignition is probably the right call. If you want the *data layer* underneath — collected from controllers, normalized to a canonical vocabulary, buffered through outages, delivered to MQTT or OPC UA — we're focused on exactly that.
- **AI constraint, offline licensing, audit log.** Architectural commitments we've made that aren't Ignition's focus.
- **Coexistence pattern.** Many customers run both. EdgeConnect collects from CNCs and publishes to MQTT; Ignition subscribes from MQTT and renders the operator-facing HMI. Both products do what they're best at.

### What NOT to say

- ❌ "Ignition doesn't have these features" — it has many of them, just framed differently.
- ❌ "Ignition is too expensive" — their unlimited-licensing model is a real strength for some customers.
- ❌ "We're a better SCADA" — we're not a SCADA. Don't pretend.
- ❌ Anything that suggests rip-and-replace if Ignition is already deployed.

### Where you'll lose this deal

- The customer wants a **screen-builder / HMI replacement** (we don't ship one).
- The customer needs **unlimited tags / clients / screens** under one license (Ignition's licensing model).
- The customer's existing operators are **deeply trained on Ignition** and switching has cultural cost.
- The customer needs **operator-facing real-time control** (we're observational and analytical, not control).

### Supporting evidence

- Datasheet v4 "Architecture at a glance" — shows we're the data layer, not the operator UI
- Platform overview page (when built) — emphasizes EdgeConnect + EREMOS V2 as a pair, not a SCADA replacement
- Coexistence story — both products publishing to and consuming from MQTT

---

## Objection 3 — "Why not build in-house?"

### The customer's words

> *"Our IT team thinks we can build this ourselves."*
> *"We have an internal IoT initiative."*
> *"We've already started writing the FOCAS2 driver."*
> *"How is this different from just hiring two engineers for a year?"*

### What they're really asking

*"Is your platform worth the licensing cost compared to our own engineering time? And what are we actually buying — the protocols or something else?"*

### Recommended response

Acknowledge the build option honestly: protocol drivers can be written. The FOCAS2 SDK is published. MTConnect is open. Modbus TCP is documented. A capable engineering team can absolutely write these.

Then reframe: **The driver is the first 20% of the work. The other 80% is production-readiness and architectural decisions you'd have to make over years that we've already made.**

Specific talking points:

- **The 80% you don't see until production.** Error handling, exponential reconnection, store-and-forward buffering, replay on reconnect, per-tag quality codes, three-way diagnostics, configuration draft/validate/apply/rollback, hash-chained audit log, role separation, license-gated DI registration, fail-soft startup. Each of those is a real engineering investment.
- **Maintenance burden is permanent.** Controllers ship firmware updates. Protocols evolve. Edge cases surface in production for years. Every protocol you maintain in-house is a permanent engineering tax.
- **Opportunity cost.** Your engineering team building plumbing isn't building differentiated product for your business. Every week spent on protocol resilience is a week not spent on what your customers actually pay you for.
- **Architectural decisions we've already made cost years to design correctly.** Offline licensing without phone-home. Canonical vocabulary across vendors. AI in decision-support only. Per-gateway identity model that survives reorganizations. These aren't features — they're commitments that took years to think through. You can recreate them. You probably shouldn't.
- **Use the ROI calculator.** Specifically the "Engineering driver savings" bucket: 4-8 engineer-weeks per protocol, multiplied by fully-loaded engineer rate. Walk through that math with their finance team. The Year-1 savings often exceed the platform cost by 2-3x before any operational benefit kicks in.

### What NOT to say

- ❌ "Your team can't build this" — they probably can.
- ❌ "Open-source drivers are buggy" — many are excellent.
- ❌ "You'll never finish" — they might. The question is whether they should.
- ❌ Anything that dismisses internal engineering competence.

### Where you'll lose this deal

- The customer has a **very capable internal OT engineering team** with deep experience and a long product roadmap.
- The customer's **volume justifies the build** (very large fleet, very few protocols).
- The customer **culturally rejects external dependencies** (defense contractor, regulated industry with strict supply-chain controls, etc.).
- The customer is **mid-build already** and changing direction has political cost.

### Supporting evidence

- ROI calculator (specifically the engineering-driver-savings bucket)
- Datasheet v4 "Why Elpis" section
- Architecture diagram + spec — shows the depth of what's already built
- All five solution pages — demonstrate the platform's vertical applicability that a from-scratch build can't easily match

---

## Objection 4 — "Why not just buy an MQTT broker and write our own driver?"

### The customer's words

> *"We just need to get data from these machines to MQTT — why is this so complicated?"*
> *"We're going to use Mosquitto and write a Python script."*
> *"Can't we just call FOCAS2 directly and publish?"*

### What they're really asking

*"Can we skip the platform layer and assemble parts ourselves? What are we missing if we DIY?"*

### Recommended response

Acknowledge: yes, technically you can. The first version of EdgeConnect was probably exactly that script you're imagining.

Then reframe: **A driver is the first 20% of the work. Production readiness is the other 80% — and the third time you wake up at 3 AM to a crashed gateway, you'll wish you'd bought the platform.**

Specific talking points:

- **For 1-2 machines: maybe DIY works.** A Python script polling FOCAS2 and publishing to Mosquitto can absolutely run.
- **For 10+ machines or production-critical: probably not.** That's where the platform-grade concerns kick in:
  - What happens when the broker is unreachable for an hour? (Store-and-forward)
  - How do you know which tags are stale vs current? (Quality codes)
  - How does the gateway behave when one of the 10 controllers is down? (Per-adapter isolation)
  - Where's the audit trail when someone changes a config and breaks production? (Hash-chained audit)
  - How do you upgrade the Python script across 10 sites without taking each one offline? (Configuration management)
- **The DIY path scales linearly with complexity.** Adding the second protocol means another script. Adding the second site means more deployment work. Adding alarm tracking means writing more code. The platform's architectural value is amortizing all of that.
- **OEE and alarms aren't in your script.** Once you have data in MQTT, you still need an analytics layer. Building that yourself is another engineering project.

### What NOT to say

- ❌ "MQTT brokers are unreliable" — they're not. Mosquitto is excellent.
- ❌ "Python isn't enterprise-grade" — it absolutely is for this kind of work.
- ❌ Anything that dismisses the DIY approach as obviously wrong.

### Where you'll lose this deal

- **Single-machine pilot.** DIY genuinely is fine for that.
- **Customer has internal engineering time, tolerance for operational quirks, and a long timeline.**
- **Customer's use case is genuinely simple** — one protocol, one broker, one consumer, low criticality.
- **Customer values learning the protocols themselves** (sometimes a legitimate organizational goal).

### Supporting evidence

- Datasheet v4 "Edge connectivity" bullets — specifically store-and-forward, per-adapter isolation, three-way diagnostics, safe configuration
- Architecture diagram + spec
- ROI calculator — quantifies the engineering-time cost of DIY at scale

---

## Objection 5 — "Why not a cloud IIoT platform (AWS IoT SiteWise, Azure IoT Hub)?"

### The customer's words

> *"We're standardizing on AWS / Azure for IoT."*
> *"Our cloud team prefers SiteWise."*
> *"We already pay for Azure IoT Hub."*

### What they're really asking

*"Why not the cloud IoT platform our IT team already chose? And how do you fit with our cloud strategy?"*

### Recommended response

Acknowledge: cloud IoT platforms are real, mature, scalable, and well-funded. AWS IoT SiteWise and Azure IoT Hub are legitimate products.

Then reframe: **We don't compete with cloud IoT platforms. We feed them. The question isn't AWS vs Elpis — it's how does the data get to AWS in the first place.**

Specific talking points:

- **Cloud IoT platforms assume always-on cloud connectivity.** That's fine for some OT deployments and a deal-breaker for others. We're the layer that handles the edge regardless of how the cloud is configured.
- **Cloud IoT platforms assume the edge runtime is someone else's problem.** SiteWise expects something to publish to it. Azure IoT Hub expects an SDK-using gateway to send messages. That something is what we are.
- **EdgeConnect publishes to AWS IoT Core, Azure IoT Hub, or any MQTT broker.** We're not a competitor to their cloud stack; we're the edge complement. Many of our deployments end up feeding AWS or Azure with filtered, normalized data from the plant floor.
- **EREMOS V2 vs SiteWise specifically.** SiteWise is cloud-vendor-locked and assumes you're all-in on AWS. EREMOS V2 is multi-tenant, OT-native, can run on-prem or in the cloud of your choice, doesn't lock you to any specific cloud vendor. If your strategy is "AWS for everything," SiteWise might be a fit. If your strategy is "we want optionality," EREMOS V2 is.
- **Cost differential at scale.** Cloud IoT platforms often price per-message or per-device. Edge buffering + filtering at our layer means you only send to the cloud what the cloud actually needs to see — which usually cuts cloud-platform costs significantly.

### What NOT to say

- ❌ "AWS is too expensive" — sometimes it is, often it isn't.
- ❌ "Cloud IoT doesn't work for OT" — it does, for the right use cases.
- ❌ Anything that attacks the customer's cloud strategy.

### Where you'll lose this deal

- The customer's **cloud strategy mandates a specific vendor's full stack** (AWS-everywhere policy, Azure-everywhere policy, etc.).
- The customer has **no on-prem appetite** and won't deploy anything outside the cloud.
- The customer's IT and OT teams are **fully merged** and operate under one cloud-first mandate.

### Supporting evidence

- Datasheet v4 "How it deploys" section — emphasizes broker-agnostic publishing
- Security page (operational trust, offline-capable)
- Multi-site solution page (per-site EdgeConnect feeding either on-prem EREMOS V2 or cloud platforms)
- The Mermaid architecture diagram explicitly shows Cloud Platforms as a consumer tier

---

## Objection 6 — "What about the OEM vendor's own monitoring software?"

### The customer's words

> *"Fanuc has MT-LINKi — why not just use that?"*
> *"Brother has its own monitoring portal."*
> *"Mazak Smartbox does all of this."*

### What they're really asking

*"Why a third party when the OEM provides their own tool?"*

### Recommended response

Acknowledge: OEM monitoring tools exist, often work well for that one vendor's equipment, and are sometimes free or bundled with the controller.

Then reframe: **OEM tools are single-vendor. Most plants are multi-vendor. The OEM-tool-per-vendor pattern is exactly the fragmentation problem you're trying to solve.**

Specific talking points:

- **OEM tools are vendor-locked.** MT-LINKi reads Fanuc. Brother's portal reads Brother. Mazak Smartbox reads Mazak. If your plant runs all three (and most do), you end up with three vendor tools, three dashboards, three OEE definitions, three reporting cadences — the exact fragmentation that drove you to look for a platform in the first place.
- **We're protocol-agnostic by architecture.** EdgeConnect speaks every CNC protocol relevant to your floor — including MT-LINKi natively. You don't have to abandon MT-LINKi; we collect from it the same way we collect from FOCAS2 or Brother HTTP.
- **OEM tools rarely include the analytics layer.** They show you the machine's status. They don't compute OEE Segments across your shift schedule, track tool-life across product families, aggregate across multiple sites, or produce the audit-ready data your customers' Tier-1 audit team asks for.
- **For OEM customers buying connected equipment:** OEMs themselves often choose us specifically because their customers refuse to standardize on the OEM's tool. See `/solutions/oem-machine-monitoring`.

### What NOT to say

- ❌ Attack any specific OEM tool by name. Don't say "MT-LINKi is bad."
- ❌ Claim we're a Fanuc/Brother/Mazak replacement. We're not. We collect from them.
- ❌ Suggest that OEM tools are technically inferior. They're often well-engineered for what they're designed to do.

### Where you'll lose this deal

- **Single-vendor shop** (all Fanuc, all Brother, etc.) where the OEM tool covers their needs.
- **Customer has already standardized on an OEM tool** and doesn't have fragmentation pain yet.
- **OEM tool comes free / bundled** and the customer hasn't run into its limits yet.

These customers often become future opportunities when their second vendor enters the shop — flag them for follow-up.

### Supporting evidence

- Datasheet v4 "Connectivity coverage" — shows MT-LINKi is a supported source, not a competitor
- CNC machining solution page (multi-vendor reality)
- Brownfield solution page (mixed-generation reality, includes mixed-vendor)

---

## Objection 7 — "We already have a SCADA. Why do we need this?"

### The customer's words

> *"Our SCADA already collects from these machines."*
> *"We have Wonderware / GE iFix / Siemens WinCC / etc."*
> *"Are you replacing our SCADA?"*

### What they're really asking

*"Are you going to threaten our existing SCADA investment? And if not, where exactly do you fit?"*

### Recommended response

Lead with reassurance: **We're not replacing your SCADA. We sit alongside it.**

Then reframe the relationship:

- **SCADA is operational (real-time control / supervisory). We're analytical (OEE, alarms, incidents, reports, multi-site).** Two different layers of the operational stack.
- **Common pattern: SCADA stays, we add what SCADA doesn't do.** Audit-ready OEE across multi-vendor cells. Multi-site aggregation. Tool-life trending. Persistent alarm tracking with incident grouping. Shift reports your customers' Tier-1 audit team will accept.
- **Our OPC UA Server lets your SCADA read our data.** If your SCADA wants normalized, canonical-vocabulary data feeds from our platform, that's how. If your SCADA has reliable real-time tag access already, that's how it stays.
- **Our MQTT lets us feed your analytics, your cloud, your EREMOS V2 tenant, or whatever else needs the data downstream.**
- **For brownfield plants:** SCADA stays in place doing what it does. EdgeConnect adds the data layer to existing controllers without changing the SCADA-side workflow.

### What NOT to say

- ❌ Suggest replacing the SCADA.
- ❌ Imply that SCADA-based reporting is wrong.
- ❌ Promise to integrate with every SCADA platform on the market — confirm the specific one before agreeing.

### Where you'll lose this deal

- The customer's **SCADA already has the analytics layer they need** (some Wonderware / Ignition deployments cover OEE / alarms well enough).
- The customer's SCADA investment is **so deep that any adjacent platform feels like a threat** — emotional or political objection, not technical.
- The customer wants a **SCADA replacement** and we're not one — refer them to Ignition or similar.

### Supporting evidence

- Datasheet v4 "How it deploys" — coexistence framing
- Architecture diagram — OPC UA Server explicitly shown as a sink for SCADA/MES/HMI consumers
- CNC solution page §3 — *"How does this integrate with the SCADA we already have?"* answer

---

## When to disqualify

The honest read across all seven objections — the customer profiles where Elpis loses fair and square:

- **Single-vendor shops** with no multi-vendor expansion plans (OEM tools are sufficient).
- **All-cloud mandates** where on-prem deployment is politically blocked.
- **Single-machine pilots** where DIY genuinely makes more sense than buying a platform.
- **SCADA-suite-only buyers** who want screen-builder + supervisory control + analytics in one product (Ignition).
- **Pure OPC-server-replacement** scenarios with no analytics need (Kepware).
- **Plants with deep existing investment in any of the above** where switching cost exceeds the value of switching.

Qualifying out gracefully when one of these applies preserves trust for future opportunities and protects your pipeline accuracy. **A deal you should never have pursued is worth less than no deal at all.**

---

## When to bring in engineering

Escalate to an Elpis engineer (sales engineer first, then platform engineer) when:

- Customer asks for a protocol we don't ship (rare — but if Siemens S7, OPC UA Client, or something exotic comes up, engineering needs to weigh in on timing).
- Customer asks for OPC UA Server security mode beyond Sign / SignAndEncrypt + X.509 — engineering should advise on what we actually do.
- Customer asks about specific Fanuc model compatibility outside the 0i–32i range — engineering can confirm or flag a known limitation.
- Customer asks about integration with a specific MES / ERP / historian — sales engineering scopes it.
- Customer's IT security review asks for documentation we don't have public-facing yet (vulnerability disclosure, pen-test results, etc.) — engineering supplies what exists.

---

## When to escalate to executive

Escalate to founder / exec level when:

- A deal materially shifts roadmap priorities (large customer asks for a protocol or capability that would change Phase 5).
- An OEM partnership conversation reaches white-label / co-branding scope.
- A multi-site deal at enterprise scale (10+ plants, multi-year commitment) — strategic deal-shaping.
- A customer's security or compliance requirements would require a certification we haven't pursued — exec needs to decide whether to pursue it for that customer.

---

## Version history

- **v1, 2026-05-24** — initial draft covering Kepware, Ignition, build-in-house, MQTT-DIY, cloud IoT platforms, OEM-vendor tools, and existing-SCADA objections.

Future versions will add:
- Specific competitor patterns as they surface in real customer conversations.
- Win/loss analysis from actual deals.
- Refined "where we lose" sections with deal data.
- Industry-specific competitive patterns (automotive, aerospace, energy).

---

*Sales Objection-Handling Guide — Internal v1, 2026-05-24. INTERNAL USE ONLY. Do not distribute externally.*
