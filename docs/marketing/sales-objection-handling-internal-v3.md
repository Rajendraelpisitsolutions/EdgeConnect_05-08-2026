<!--
File:        docs/marketing/sales-objection-handling-internal-v3.md
Purpose:     Internal sales-enablement guide for handling common competitive
             and "build-in-house" objections in customer conversations about
             the Elpis Industrial Intelligence Platform.
Audience:    Elpis sales team — account executives, sales engineers, founders
             in sales conversations.

INTERNAL ONLY. Do not distribute to customers, prospects, or partners.

Format:      Markdown sales-enablement reference. Each objection follows the
             same 6-part structure. v3 adds a competitive maturity table and
             per-competitor battlecard companions.
Version:     v3
Date:        2026-06-05

Changes from v2 (the substantive refresh — v2 had been frozen as "final"):
  - FACTUAL CORRECTIONS (mandatory — protocol reality moved):
      * MT-LINKi is ROADMAP, not shipped. v2 Objection 6 wrongly said "we
        collect from MT-LINKi natively the same way as FOCAS2." Corrected: today
        we read the Fanuc controller DIRECTLY via FOCAS2 (no dependence on the
        OEM's MT-LINKi layer); MT-LINKi (REST) is on the roadmap.
      * Siemens S7 + OPC UA Client are SHIPPED today (v2 "when to bring in
        engineering" wrongly listed them as exotic/not-shipped). Removed.
      * Northbound today = MQTT + OPC UA Server; HTTP / TCP sinks = roadmap.
  - SHIPPED-FEATURE FOLD-IN: universal secret redaction + diagnostic bundle
    (ADR-0020), the broader operator Studio (3-way diagnostics, backup), and the
    now-operator-available hardware (Edge Gateway, mDAQ, mTracker, VAS, E-IDOS).
  - AI HONESTY: AI agents are a Phase-4.5 ARCHITECTURAL COMMITMENT, not a shipped
    feature. Framed as architecture/roadmap throughout — never as a demoable
    capability a rep can promise today.
  - CROSS-REFS refreshed: datasheet v4 → v5 (elpis-industrial-intelligence-
    platform-v5); /security copy v2 → /security spec v3; the five solution pages
    → migrated page-solutions-*-spec-v1; platform "(when built)" → page-platform
    -spec-v1; architecture-diagram v2 → v3; the 7 product pages now exist.
  - NEW second-wave objections (8/9/10): MachineMetrics / Sight Machine / Tulip;
    Unified Namespace / HiveMQ Edge / EMQX; Power BI / Grafana.
  - NEW: competitive maturity at-a-glance table (orientation) + condensed
    per-competitor battlecard companions.

ChatGPT review (2026-06-05) — "Approve with changes." Applied before lock:
  - P0 competitor fairness: Objection 9 reframed — HiveMQ Edge / EMQX Neuron are
    edge / protocol-gateway tools (OPC UA / Modbus / S7 / EtherNet/IP adapters),
    NOT just brokers; relationship → feed/coexist/compete on edge acquisition.
  - P0 OEM accuracy: Mazak SmartBox no longer called "Mazak-only" (broader
    MTConnect / sensor story); FOCAS2-direct / MT-LINKi-roadmap kept.
  - P0 own-platform: "rollback" kept (shipped config-apply pipeline) but a rep
    guardrail added so nobody oversells it as the roadmap one-click last-known-good.
  - P1: Kepware ("unique"/"raw tags" softened; "included" → "platform path,
    depending on package/scope"); Ignition (acknowledge Ignition Edge);
    Cloud IIoT (SiteWise Edge / Azure IoT Operations; cloud endpoints = tested
    deployment integrations, not assumed); MachineMetrics/Sight Machine/Tulip
    opener + characterizations corrected; maturity table + battlecards split.
  - P2: tone ("falls over at 10+", cloud-cost absolute) softened; expanded
    disqualify/engineering triggers (Sparkplug B, MT-LINKi, HTTP/TCP sinks,
    Rockwell/Mitsubishi/Omron/Beckhoff/BACnet, HighByte/Litmus, historian,
    SBOM/cert gates); v4 competitor-recognition backlog added.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v5.md (canonical product / datasheet)
  - docs/marketing/page-security-spec-v1.md (/security v3 — trust positioning)
  - docs/marketing/architecture-diagram-spec-v3.md (architecture)
  - docs/marketing/page-solutions-*-spec-v1.md (migrated solution pages — per-vertical evidence)
  - docs/marketing/page-product-*-spec-v1.md (7 product pages)
  - docs/marketing/buyer-taxonomy-v1.md (buyer framing)
  - CLAUDE.md §1, §3, §8 (locked architectural decisions + current protocol/feature state)

Own-platform honesty rule (same spirit as the /security P3 lock): never arm a
rep with a claim the platform can't back. Shipped = shipped; roadmap = roadmap;
AI = architectural commitment, not a demo.
-->

# Sales Objection-Handling Guide — Internal v3

**INTERNAL ONLY. Do not share with customers, prospects, or partners.**

This guide arms the Elpis sales team for the most common objections that come up in real customer conversations about the Elpis Industrial Intelligence Platform. Every objection has its own section with the customer's actual words, what they're really asking, the recommended response, what not to say, where we'll lose the deal, and which platform evidence to point at.

The tone is deliberately direct. Competitors are named. Loss scenarios are acknowledged. The goal is to win the deals we can win and qualify out of the deals we can't — not to pretend we win everything.

**v3 adds** three second-wave objections (MachineMetrics-class analytics, Unified Namespace / broker-edge tooling, BI dashboards), a competitive maturity at-a-glance table, and condensed per-competitor battlecards — and corrects the protocol reality (MT-LINKi is roadmap; Siemens S7 and OPC UA Client ship today).

---

## How to use this document

- **Read the whole thing once.** The principles in one objection often apply to others.
- **Don't memorize the talking points verbatim.** Internalize the framing and adapt to the conversation.
- **Take the "where you'll lose" sections seriously.** They protect your pipeline accuracy and your time.
- **Escalate when you see a pattern.** If a new objection comes up three times, tell marketing — we'll add it to v4.
- **Never quote this document to a customer.** The honesty that makes it useful internally is damaging externally.

---

## ⚠ How to use this in live calls

> **These are not scripts. Read them, internalize the category framing, then have the conversation naturally.**
>
> The single biggest failure mode for newer reps is delivering these responses verbatim. Customers can hear rehearsed answers, and the moment they do, you lose the credibility the guide was trying to build. The strongest reps use this guide to understand the customer's underlying concern — then reframe the category conversation in their own words, sized to where the customer actually is.
>
> Specifically:
>
> - **Don't dump architecture explanation too early.** The customer asked one question. Answer that question, then probe — *"is that the angle you're worried about, or is there something else underneath?"*
> - **Don't list five talking points when one will do.** The bullets here are options, not a checklist. Pick the one that fits the specific concern.
> - **Don't get into competitive comparison detail if the customer didn't ask for it.** A short reframe is almost always stronger than a long defense.
> - **Don't recite the "where you'll lose" criteria to the customer.** Those are for internal qualification.

---

## ⚠ Protocol reality — get this right (v3 correction)

Reps were previously told MT-LINKi was a live source. **It is not yet.** Say the right thing:

| | Available **today** | **Roadmap** (don't promise a date) |
|---|---|---|
| **Southbound (collect)** | FANUC **FOCAS2**, **MTConnect**, **Brother HTTP**, **Modbus TCP**, **OPC UA Client**, **Siemens S7** | FANUC **MT-LINKi (REST)** |
| **Northbound (deliver)** | **MQTT** (any broker), **OPC UA Server** | **HTTP sink**, **TCP sink** |

- For Fanuc shops, the true and strong line is: **"We read the Fanuc controller directly over FOCAS2 — we don't depend on MT-LINKi sitting in the middle."** MT-LINKi support is coming; lead with FOCAS2-direct today.
- **Siemens S7 and OPC UA Client ship today** — don't treat them as exotic. They are part of the standard southbound set.
- **AI agents are an architectural commitment, not a shipped feature.** Phase 4.5. You may describe the *architecture* ("AI proposes; humans decide; local-LLM support is mandatory; nothing in the deterministic data path") as a design principle. Do **not** promise a demoable AI feature or a ship date.
- **Named cloud endpoints, Sparkplug B, and UNS topic-model conformance require sales-engineering confirmation before you commit.** We publish over MQTT generically today; a specific AWS IoT Core / Azure IoT Hub integration (identity, TLS, topic conventions) or Sparkplug B conformance is scoped and tested per deployment, not promised in the room.
- **"Rollback" guardrail.** The shipped config flow is **draft → validate → apply → rollback** — the apply-pipeline rollback that reverts an applied change. The slicker *one-click "last-known-good" restore* is **roadmap**; don't oversell it as a current one-click feature.

---

## General principles

Five principles that apply to every competitive conversation:

1. **Acknowledge the competitor's strength first.** Customers know if you're badmouthing. Saying *"Kepware is the OPC connectivity incumbent and they earned that position"* gets you more credibility in the next sentence than dismissing them does.

2. **Reframe the question, don't answer it directly.** If the customer asks *"why not Kepware?"*, the right answer often isn't *"because we're better than Kepware at X."* It's *"Kepware ends where the data leaves the gateway; we start there."* Different category, different question.

3. **Name where we lose.** Customers respect *"if you only need OPC server functionality and nothing else, Kepware is the right answer."* They distrust vendors who claim every deal.

4. **Anchor on a specific platform property, not a marketing slogan.** "Hash-chained audit log" wins more procurement reviews than "best-in-class auditability."

5. **Tell the truth about what ships today.** A rep who oversells protocol coverage or an AI feature loses the deal at the technical review — and loses the next one too. Roadmap is roadmap; say so.

---

## Competitive maturity — at a glance

Orientation only. Read the full objection section before a real call; this table is for fast recall.

| They are… | Their real strength | Our differentiator | Default relationship |
|---|---|---|---|
| **Kepware (KEPServerEX)** | Incumbent OPC connectivity; vast driver library; rock-stable | Canonical cross-vendor vocabulary + store-and-forward + audit + EREMOS V2 analytics in one platform | **Coexist** (both publish to MQTT / OPC UA) |
| **Ignition** | Full SCADA suite; screen-builder; unlimited-licensing model | Edge-first data layer + canonical model + OEE/alarms/multi-site, not a SCADA | **Coexist** (we collect → MQTT → Ignition renders) |
| **Build in-house** | Full control; no license cost; team owns it | The 80% after the driver — resilience, audit, redaction, diagnostics — already built and maintained | **Compete** (ROI math) |
| **MQTT broker + own script** | Cheap; fine for 1–2 machines | Production-grade collection at scale: store-and-forward, quality codes, isolation, config management | **Compete** (at scale) |
| **Cloud IIoT (SiteWise / IoT Hub)** | Mature, scalable cloud analytics; SiteWise Edge / Azure IoT Operations add edge tiers | Offline-capable edge acquisition + canonical model; if their edge tier is in scope, compare directly | **Feed them** (compare if SiteWise Edge / IoT Operations in scope) |
| **OEM tools (MT-LINKi / Brother / Mazak)** | Free/bundled; deep for that vendor — note Mazak SmartBox has broader MTConnect / sensor reach | Cross-vendor canonical model + EREMOS workflows; FOCAS2-direct today (MT-LINKi roadmap) | **Compete / absorb** |
| **Existing SCADA (Wonderware / iFIX / WinCC)** | Real-time supervisory control; entrenched | Analytical layer (OEE/alarms/multi-site) beside the SCADA, not a replacement | **Coexist** |
| **MachineMetrics** | Turnkey machine monitoring / OEE; strong CNC connectivity; fast time-to-value | Offline / on-prem option, broker-agnostic ownership of the edge data layer, canonical model + EREMOS | **Compete**, sometimes coexist |
| **Sight Machine** | Enterprise industrial data / semantic-model / AI stack | Plant-floor acquisition + CNC/mixed-controller canonical stream feeding their model | **Feed / coexist** |
| **Tulip** | Frontline operations / no-code / composable-MES apps | Machine-signal acquisition + OEE/alarm data feeding operator workflows | **Feed / coexist**, sometimes compete for ops-app budget |
| **Unified Namespace / HiveMQ Edge / EMQX Neuron** | MQTT / UNS platforms with growing edge-protocol adapter capability (OPC UA / Modbus / S7 / EtherNet/IP) | CNC-aware acquisition (FOCAS2 / Brother / MTConnect) + canonical model + EREMOS; not a broker | **Feed / coexist / compete on edge acquisition** |
| **Power BI / Grafana** | Ubiquitous, flexible dashboards | OT-native collection + OEE/alarms/asset-tree (EREMOS V2); BI tools visualize, they don't collect from CNCs | **Feed them** |

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

- **Kepware is OPC-server-first; we're operational-platform-first.** Kepware delivers protocol translation. We deliver protocol translation *plus* canonical CNC vocabulary, store-and-forward buffering, three-way diagnostics, hash-chained config audit, secret redaction in backups and support bundles, a modern Connectivity Studio UI, and the EREMOS V2 intelligence layer — in one platform.
- **A shipped CNC/OEE canonical model is our differentiator.** Kepware is excellent at broad protocol connectivity; normalized manufacturing semantics usually have to be configured, modeled, or built around it. We ship the canonical CNC vocabulary and the EREMOS workflow layer as part of the product, so the same dashboard works on Fanuc, Brother, Siemens (S7), and Modbus-fronted CNCs without per-machine custom mapping.
- **The analytics layer comes with the platform path.** Kepware customers still need to build the OEE / alarms / shift-reports layer themselves (or buy a separate analytics platform). Elpis can provide the acquisition layer and the EREMOS V2 intelligence layer as one platform path, depending on package and scope. *(Don't say "included" until the commercial SKU is locked.)*
- **Modern operational discipline.** Hash-chained audit, signed offline licensing, redaction-by-construction, draft → validate → apply → rollback config — architectural to ours, not Kepware focuses.
- **Coexistence is fine.** If they want to keep Kepware where it works and add Elpis where it doesn't (CNC-specific protocols, OEE, multi-site), say so. Both can publish to MQTT or OPC UA Server. We don't have to be a rip-and-replace pitch.

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

- Datasheet v5 "Why Elpis" section
- `/security` (offline-first, signed licensing, secret redaction)
- `/solutions/cnc-machining` (canonical vocabulary specifically)
- `/edgeconnect` product page (full protocol coverage) + architecture diagram

---

## Objection 2 — "Why not Ignition (Inductive Automation)?"

### The customer's words

> *"We're considering Ignition for our SCADA stack."*
> *"Our integrator recommends Ignition."*
> *"We're already running Ignition at another plant."*

### What they're really asking

*"Do we need a full SCADA suite, or a focused integration layer? And how do you two relate?"*

### Recommended response

Acknowledge Ignition's real strength: it's a legitimate full SCADA platform with a unique unlimited-licensing model and a powerful screen-builder. That's a genuine product.

Then reframe: **Ignition is a SCADA suite. We're an industrial data platform. They're not the same product.**

Specific talking points:

- **Different categories.** Ignition is HMI + SCADA + alarming + screen-builder + scripting + database connectivity. We're protocol-agnostic data layer + canonical vocabulary + OEE + alarm tracking + multi-site aggregation. Where Ignition stops being a SCADA, we usually start. Where we stop being a data platform, Ignition often does what's next.
- **Different center of gravity — not "they can't run at the edge."** Ignition has both central Gateway and **Ignition Edge** deployment patterns, so don't claim they lack edge capability. The honest distinction: Ignition's center of gravity is SCADA / HMI / app-building, while Elpis is the CNC / mixed-controller acquisition + canonical OEE/alarms/data-model layer. EdgeConnect runs on a small control-cabinet box or the ruggedized **Edge Gateway** appliance; the difference is what each is *for*, not where it runs.
- **Lightweight, focused.** We don't build screens. We don't ship a scripting language. We don't host your operators' HMI. If you want any of that, Ignition is probably the right call. If you want the *data layer* underneath — collected from controllers, normalized to a canonical vocabulary, buffered through outages, delivered to MQTT or OPC UA Server — we're focused on exactly that.
- **Offline licensing, audit log, redaction.** Architectural commitments we've made that aren't Ignition's focus.
- **Coexistence pattern.** Many customers run both. EdgeConnect collects from CNCs and publishes to MQTT; Ignition subscribes from MQTT and renders the operator-facing HMI. Both products do what they're best at.

### What NOT to say

- ❌ "Ignition doesn't have these features" — it has many of them, just framed differently.
- ❌ "Ignition is too expensive" — their unlimited-licensing model is a real strength for some customers.
- ❌ "We're a better SCADA" — we're not a SCADA. Don't pretend.
- ❌ Anything that suggests rip-and-replace if Ignition is already deployed.

### Where you'll lose this deal

- The customer wants a **screen-builder / HMI replacement** (we don't ship one).
- The customer needs **unlimited tags / clients / screens** under one license (Ignition's model).
- The customer's existing operators are **deeply trained on Ignition** and switching has cultural cost.
- The customer needs **operator-facing real-time control** (we're observational and analytical, not control).

### Supporting evidence

- `/platform` overview — positions EdgeConnect + EREMOS V2 as a pair, not a SCADA replacement
- Datasheet v5 "Architecture at a glance" — shows we're the data layer, not the operator UI
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

- **The 80% you don't see until production.** Error handling, reconnection, store-and-forward buffering, per-tag quality codes, three-way diagnostics, draft/validate/apply/rollback config flow, hash-chained audit, **secret redaction in backups and support bundles**, role separation, fail-soft startup, per-adapter fault isolation. Each is a real engineering investment.
- **Maintenance burden is permanent.** Controllers ship firmware updates. Protocols evolve. Edge cases surface in production for years. Every protocol you maintain in-house is a permanent engineering tax.
- **Opportunity cost.** Your engineering team building plumbing isn't building differentiated product for your business. Every week spent on protocol resilience is a week not spent on what your customers actually pay you for.
- **Architectural decisions take years to design correctly.** Offline licensing without phone-home. Canonical vocabulary across vendors. Per-gateway identity that survives reorganizations. A redaction model that keeps a support bundle safe to share. You can recreate them. You probably shouldn't.
- **Use the ROI calculator.** Specifically the "Engineering driver savings" bucket: engineer-weeks per protocol, multiplied by fully-loaded engineer rate. Walk through that math with their finance team. Year-1 savings often exceed the platform cost.

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

- ROI calculator (engineering-driver-savings bucket)
- Datasheet v5 "Why Elpis" section
- Architecture diagram + spec — shows the depth of what's already built
- The migrated solution pages — demonstrate vertical applicability a from-scratch build can't easily match

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

- **For 1–2 machines: maybe DIY works.** A Python script polling FOCAS2 and publishing to Mosquitto can absolutely run.
- **For 10+ machines or production-critical: probably not.** That's where the platform-grade concerns kick in:
  - What happens when the broker is unreachable for an hour? (Store-and-forward, replay in source order)
  - How do you know which tags are stale vs current? (Per-tag quality codes)
  - How does the gateway behave when one of the 10 controllers is down? (Per-adapter isolation)
  - Where's the audit trail when someone changes a config and breaks production? (Hash-chained audit)
  - When you send a support bundle to a vendor, who guarantees it isn't leaking passwords and keys? (Redaction by construction)
  - How do you upgrade the script across 10 sites without taking each one offline? (Configuration management + draft/validate/apply/rollback)
- **The DIY path scales linearly with complexity.** Adding the second protocol means another script. Adding the second site means more deployment work. Adding alarm tracking means writing more code. The platform's architectural value is amortizing all of that.
- **OEE and alarms aren't in your script.** Once you have data in MQTT, you still need an analytics layer. Building that yourself is another engineering project (EREMOS V2 is that layer).

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

- Datasheet v5 "Edge connectivity" bullets — store-and-forward, per-adapter isolation, three-way diagnostics, safe configuration, redaction
- `/edgeconnect` product page + architecture diagram
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

Then reframe: **We usually feed cloud IoT platforms — but distinguish three things: the cloud service, the edge runtime, and the plant-floor protocol acquisition. The question is who gets normalized machine data off the floor in the first place.**

Specific talking points:

- **Cloud programs still need a plant-floor acquisition layer.** Even with a cloud destination, something must handle controller protocols, outages, and canonical modeling at the edge. We're that layer — offline-capable, with store-and-forward across outages.
- **Mind the edge tiers — don't overclaim.** AWS has **SiteWise Edge** for local collection / processing; Azure has **IoT Operations** with an on-prem MQTT broker / edge architecture. If the customer is using those, don't claim we're "the missing edge layer" — instead compare adapter coverage, offline behavior, canonical model, and CNC/OEE semantics directly. If they're using the cloud service as the enterprise *destination*, we're the edge acquisition + normalization layer feeding it.
- **EdgeConnect publishes via MQTT and OPC UA Server today.** MQTT-compatible cloud endpoints such as AWS IoT Core or Azure IoT Hub are scoped and *tested as deployment integrations* (identity, TLS, topic conventions, routing) — not assumed. We're the edge complement to their cloud stack; many deployments feed AWS or Azure with filtered, normalized plant-floor data.
- **EREMOS V2 vs SiteWise specifically.** SiteWise is cloud-vendor-locked and assumes all-in AWS. EREMOS V2 is multi-tenant, OT-native, runs on-prem or in any cloud. If your strategy is "AWS for everything," SiteWise fits. If your strategy is "optionality," EREMOS V2 fits.
- **Cost differential at scale.** Cloud IoT platforms often price per-message or per-device. Edge buffering + filtering at our layer means you only send what the cloud actually needs to see — which can reduce cloud message / storage cost when filtering and aggregation are scoped.

### What NOT to say

- ❌ "AWS is too expensive" — sometimes it is, often it isn't.
- ❌ "Cloud IoT doesn't work for OT" — it does, for the right use cases.
- ❌ Anything that attacks the customer's cloud strategy.

### Where you'll lose this deal

- The customer's **cloud strategy mandates a specific vendor's full stack** (AWS-everywhere, Azure-everywhere).
- The customer has **no on-prem appetite** and won't deploy anything outside the cloud.
- The customer's IT and OT teams are **fully merged** under one cloud-first mandate.

### Supporting evidence

- Datasheet v5 "How it deploys" — broker-agnostic publishing
- `/security` (operational trust, offline-capable)
- `/solutions/multi-site-operations` (per-site EdgeConnect feeding on-prem EREMOS V2 or cloud platforms)
- Architecture diagram — Cloud Platforms shown as a consumer tier

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

- **OEM and OEM-adjacent tools vary — don't oversimplify.** MT-LINKi is Fanuc-centric; Brother's tools are strongest inside the Brother ecosystem; **Mazak SmartBox has a broader MTConnect / sensor-connectivity story than a simple Mazak-only portal.** But the fragmentation problem still bites the moment a plant wants *one* canonical OEE / alarm / reporting model across Fanuc, Brother, Siemens, Modbus-fronted equipment, and non-CNC assets — that's where three vendor tools, three dashboards, and three OEE definitions become the pain.
- **We read the controller directly — today, over FOCAS2.** Here's the precise, honest framing on the Fanuc question: *today we read the Fanuc controller directly via FOCAS2 — we don't need MT-LINKi sitting in the middle.* **MT-LINKi support (its REST interface) is on our roadmap;** when it ships you'll be able to collect from an existing MT-LINKi deployment too. But you don't have to wait for it or keep MT-LINKi to get Fanuc data into the platform. *(Do not tell a customer we collect from MT-LINKi today — we do not yet.)*
- **We're protocol-agnostic by architecture.** EdgeConnect speaks the CNC and PLC protocols on your floor today — FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, Siemens S7 — so one platform spans the multi-vendor reality the OEM tools fragment.
- **OEM tools rarely include the analytics layer.** They show you the machine's status. They don't compute OEE Segments across your shift schedule, track tool-life across product families, aggregate across multiple sites, or produce the audit-ready data your customers' Tier-1 audit team asks for. EREMOS V2 does.
- **For OEM customers buying connected equipment:** machine builders often choose us specifically because *their* customers refuse to standardize on the OEM's tool. See `/solutions/oem-machine-monitoring`.

### What NOT to say

- ❌ **Don't say we collect from MT-LINKi today** — it's roadmap. Lead with FOCAS2-direct.
- ❌ Attack any specific OEM tool by name. Don't say "MT-LINKi is bad."
- ❌ Claim we're a Fanuc / Brother / Mazak replacement. We're not. We collect from the controllers.
- ❌ Suggest OEM tools are technically inferior. They're often well-engineered for what they do.

### Where you'll lose this deal

- **Single-vendor shop** (all Fanuc, all Brother) where the OEM tool covers their needs.
- **Customer has already standardized on an OEM tool** and doesn't have fragmentation pain yet.
- **OEM tool comes free / bundled** and the customer hasn't run into its limits.

These customers often become future opportunities when their second vendor enters the shop — flag them for follow-up.

### Supporting evidence

- Datasheet v5 "Connectivity coverage" — FOCAS2 today; MT-LINKi on the roadmap
- `/solutions/cnc-machining` (multi-vendor reality)
- `/solutions/brownfield-modernization` (mixed-generation + mixed-vendor reality)

---

## Objection 7 — "We already have a SCADA. Why do we need this?"

### The customer's words

> *"Our SCADA already collects from these machines."*
> *"We have Wonderware / GE iFIX / Siemens WinCC / etc."*
> *"Are you replacing our SCADA?"*

### What they're really asking

*"Are you going to threaten our existing SCADA investment? And if not, where exactly do you fit?"*

### Recommended response

Lead with reassurance: **We're not replacing your SCADA. We sit alongside it.**

Then reframe the relationship:

- **SCADA is operational (real-time control / supervisory). We're analytical (OEE, alarms, incidents, reports, multi-site).** Two different layers of the operational stack.
- **Common pattern: SCADA stays, we add what SCADA doesn't do.** Audit-ready OEE across multi-vendor cells. Multi-site aggregation. Tool-life trending. Persistent alarm tracking with incident grouping. Shift reports your customers' Tier-1 audit team will accept.
- **Our OPC UA Server lets your SCADA read our data; our MQTT feeds your analytics, your cloud, or whatever else needs the data downstream.** Two integration paths, two value flows.
- **For brownfield plants:** SCADA stays in place doing what it does. EdgeConnect adds the data layer to existing controllers without changing the SCADA-side workflow.

### What NOT to say

- ❌ Suggest replacing the SCADA.
- ❌ Imply that SCADA-based reporting is wrong.
- ❌ Promise to integrate with every SCADA platform on the market — confirm the specific one before agreeing.

### Where you'll lose this deal

- The customer's **SCADA already has the analytics layer they need** (some Wonderware / Ignition deployments cover OEE / alarms well enough).
- The customer's SCADA investment is **so deep that any adjacent platform feels like a threat** — emotional or political, not technical.
- The customer wants a **SCADA replacement** and we're not one — refer them to Ignition or similar.

### Supporting evidence

- Datasheet v5 "How it deploys" — coexistence framing
- Architecture diagram — OPC UA Server shown as a sink for SCADA / MES / HMI consumers
- `/solutions/cnc-machining` — *"How does this integrate with the SCADA we already have?"* answer

---

## Objection 8 — "Why not MachineMetrics / Sight Machine / Tulip?" *(second-wave)*

### The customer's words

> *"MachineMetrics already does machine monitoring out of the box."*
> *"Our corporate team is looking at Sight Machine for enterprise analytics."*
> *"We're building operator apps in Tulip."*

### What they're really asking

*"There are polished, fast-to-stand-up manufacturing-analytics products. Why you instead of (or under) them?"*

### Recommended response

These are three different products that often get lumped together — handle them separately. **MachineMetrics is the closest head-to-head because it includes machine monitoring and analytics; Sight Machine and Tulip are more often downstream or adjacent.**

- **MachineMetrics** is the closest to a head-to-head — a turnkey machine-monitoring and OEE platform with strong CNC connectivity and a cloud-first operating model. Reframe on **deployment model and neutrality**: it's cloud-first and opinionated; we're edge-first, offline-capable, broker-agnostic, on-prem-or-cloud, and vendor-neutral across protocols. If the customer needs an air-gapped or no-cloud deployment, or wants to own the data layer and choose their own analytics/cloud downstream, that's us. If they want fast turnkey time-to-value and have no on-prem or neutrality constraint, MachineMetrics is a legitimate pick — say so.
- **Sight Machine** is an enterprise industrial-data / semantic-model / AI stack, not an edge connectivity layer — and don't dismiss its data-modeling ambition. Reframe as **feed, not compete**: it still needs clean, normalized data delivered to it. EdgeConnect is the edge acquisition + canonical-model + store-and-forward layer that gets it there. We're under them, not against them.
- **Tulip** is a frontline-operations / no-code / composable-MES app platform — operator apps, work instructions, traceability, human-in-the-loop. Reframe as **different layer**: Tulip is the operator-app layer; we're the machine-data layer. They publish/consume over MQTT and standard interfaces; EdgeConnect can be the machine-signal source feeding a Tulip app. Usually a complement — occasionally competing for the operations-app budget.

The one-liner: **"MachineMetrics is a turnkey machine-monitoring & OEE platform with a cloud-first operating model; Sight Machine is an enterprise industrial-data / semantic / AI stack; Tulip is a frontline-operations / composable-MES app platform. None of them is the vendor-neutral, offline-capable edge data layer — and that's the part that decides whether the rest of your stack ever gets trustworthy data."**

### What NOT to say

- ❌ "MachineMetrics doesn't scale / is just dashboards" — it's a capable, well-funded product. Don't dismiss it.
- ❌ "Sight Machine / Tulip are competitors" — usually they're downstream of us. Calling them competitors confuses the customer.
- ❌ Claim we ship operator-app authoring (Tulip's space) or a turnkey cloud dashboard identical to MachineMetrics — we don't; we're the data layer + EREMOS V2 analytics.

### Where you'll lose this deal

- Customer wants a **turnkey cloud monitoring product with zero edge/on-prem footprint** and has no neutrality or offline constraint — MachineMetrics may simply be faster to value.
- Customer's program is **enterprise-data-ops-led** (Sight Machine already chosen at corporate) and they only need a feeder they've already sourced.
- Customer's actual need is **operator workflow apps**, not machine data — that's Tulip's job, not ours.

### Supporting evidence

- `/security` (offline / air-gapped / no-cloud option — the clearest MachineMetrics differentiator)
- `/edgeconnect` (vendor-neutral protocol coverage + canonical model)
- `/solutions/multi-site-operations` (on-prem-or-cloud optionality)
- Coexistence framing: EdgeConnect → MQTT → Sight Machine / Tulip

---

## Objection 9 — "We're building a Unified Namespace on HiveMQ Edge / EMQX. Where do you fit?" *(second-wave)*

### The customer's words

> *"We've adopted a Unified Namespace architecture."*
> *"We're standardizing on HiveMQ Edge / EMQX as our broker."*
> *"Sparkplug B is our standard — why do we need you?"*

### What they're really asking

*"We've already chosen our messaging backbone and architectural pattern. Are you redundant with it?"*

### Recommended response

This is a sophisticated buyer — respect the architecture, and **be precise: HiveMQ Edge and EMQX Neuron are legitimate edge / protocol-gateway tools, not just brokers.** Depending on edition and configuration they handle OPC UA, Modbus, Siemens S7, EtherNet/IP, HTTP and similar adapters. So our distinction is narrower than "we acquire and they don't" — get it right or a sophisticated UNS buyer will catch the overclaim instantly.

Specific talking points:

- **They have adapters; our edge is CNC-aware.** HiveMQ Edge and EMQX Neuron can collect common OT protocols (OPC UA, Modbus, S7, EtherNet/IP, HTTP) and namespace them. Where we're differentiated is the CNC / mixed-controller acquisition problem plus the application layer on top: FANUC **FOCAS2**, **Brother HTTP**, **MTConnect**, the canonical **CNC/OEE vocabulary**, per-tag quality codes, store-and-forward, and **EREMOS V2** workflows. If their UNS stack already covers their adapter needs, we feed it. If they need FOCAS2 / Brother / canonical CNC modeling / EREMOS workflows, we may still be the acquisition + application layer.
- **A UNS is only as good as the data put into it.** The hard part of a UNS isn't the broker — it's getting clean, consistently-modeled data from heterogeneous controllers into the namespace. EdgeConnect's canonical vocabulary is how the namespace stays coherent across Fanuc / Brother / Siemens / Modbus rather than carrying raw vendor tag soup.
- **We publish into your namespace.** EdgeConnect publishes to any MQTT broker — including HiveMQ Edge and EMQX — with batch or per-tag topics. If your topic structure follows your UNS / ISA-95 model, we map to it. (If they use Sparkplug B specifically, flag it for sales engineering to confirm the current state before promising it.)
- **Store-and-forward complements the broker.** Even with a robust broker, the edge-to-broker hop fails. Our per-route store-and-forward replays in source order on reconnect, so the namespace doesn't get gaps or out-of-order data during an outage.

The one-liner: **"You've built the namespace. We may be the CNC-aware on-ramp — but if HiveMQ Edge or EMQX Neuron already covers your adapters, let's compare the acquisition and modeling layer honestly."**

### What NOT to say

- ❌ "You don't need a UNS / it's overkill" — if they've adopted it, it's a deliberate, often good choice.
- ❌ "HiveMQ Edge / EMQX can't do protocol conversion" — they can; the distinction is CNC-awareness + canonical model + EREMOS, not whether adapters exist.
- ❌ "We're a better broker" — we're not a broker. Don't pretend.
- ❌ Promise Sparkplug B compliance on the spot — confirm current support with engineering first.

### Where you'll lose this deal

- The customer's **HiveMQ Edge / EMQX Neuron adapters already cover their controller mix** (e.g. all OPC UA / Modbus / S7) — less acquisition work for us to do.
- The customer already has a **working edge-acquisition layer** feeding the UNS and only needed a broker (which they have).
- The customer's controllers are **all natively MQTT / Sparkplug-capable** (rare on CNC floors, common in some greenfield process plants).
- The customer mandates **Sparkplug B** and our current support doesn't match their conformance bar (engineering confirms).

### Supporting evidence

- `/edgeconnect` (southbound protocol acquisition + canonical model + store-and-forward + MQTT publish to any broker)
- Architecture diagram — EdgeConnect as the edge acquisition layer publishing to brokers
- `/solutions/edge-connectivity` (the acquisition/normalization story)

---

## Objection 10 — "We'll just visualize the data in Power BI / Grafana." *(second-wave)*

### The customer's words

> *"We already use Power BI for reporting — can't we just point it at the data?"*
> *"Our team builds everything in Grafana."*
> *"Why do we need EREMOS V2 if we have a BI tool?"*

### What they're really asking

*"We have a dashboarding tool. Isn't your analytics layer redundant?"*

### Recommended response

Acknowledge: Power BI and Grafana are excellent, ubiquitous visualization tools, and your team's skills in them are real. **But a BI tool visualizes data it's given. It doesn't collect from a CNC, and it doesn't know what OEE means on your shift schedule.** Two different jobs.

Specific talking points:

- **They don't collect from the floor.** Power BI and Grafana sit downstream of a data source. Something has to poll the controllers, normalize the signals, buffer through outages, and deliver clean data. That's EdgeConnect — and you can absolutely point Power BI or Grafana at the result.
- **OT-native semantics aren't in a generic BI tool.** OEE Segments aligned to your shift calendar, persistent alarm tracking with incident grouping, tool-life trending across product families, the PLANT → … → SUB_EQUIPMENT asset tree, audit-ready shift reports — EREMOS V2 ships these as OT-native concepts. In Power BI / Grafana you'd rebuild all of that in DAX / queries yourself, and re-validate it every time the model changes.
- **Coexistence is the normal answer.** Most customers do both: EREMOS V2 owns the OT-native OEE/alarms/reporting and asset model; Power BI / Grafana pull from it (or from MQTT) for executive dashboards and blended business reporting. We don't fight your BI tool — we make it trustworthy by giving it correct, modeled OT data.
- **Grafana specifically** is great for real-time/time-series ops dashboards; pointing it at our data is a common and good pattern. The question is who computes the OEE math — a generic time-series tool, or an OT-native layer that already encodes it.

The one-liner: **"Power BI and Grafana are how you *show* the numbers. The question is who *computes* OEE and tracks the alarms correctly in the first place — and whether anything reliable is even collecting from the machines."**

### What NOT to say

- ❌ "Power BI / Grafana can't do this" — they can render almost anything; the issue is the collection + OT semantics, not visualization.
- ❌ "BI tools are wrong for manufacturing" — they're widely and well used.
- ❌ Position EREMOS V2 as a Power BI replacement — it isn't; they're complementary.

### Where you'll lose this deal

- Customer has **a strong BI/data team that wants to own the semantic layer themselves** and only needs clean data (sell them EdgeConnect; let them build analytics in their BI stack).
- Customer's reporting needs are **light** and a few Grafana panels off MQTT genuinely cover it.
- Customer has **already invested heavily in a BI semantic model** for manufacturing and won't duplicate it.

### Supporting evidence

- `/eremos-v2` product page (OT-native OEE/alarms/asset-tree — the differentiator vs generic BI)
- Datasheet v5 (EREMOS V2 capabilities)
- Coexistence framing: EREMOS V2 / MQTT → Power BI / Grafana downstream

---

## Battlecard companions (condensed)

One card per competitor for fast recall mid-call. Each: the reframe, two strongest points, where we lose, and whether to coexist. Use the full objection section to prepare; use the card to remember.

### Kepware
- **Reframe:** "Kepware ends where data leaves the gateway; we start there."
- **Win on:** canonical cross-vendor vocabulary; EREMOS V2 analytics included.
- **Lose if:** OPC DA legacy need; pure protocol-converter scope; standing enterprise license.
- **Relationship:** coexist (both publish to MQTT / OPC UA).

### Ignition
- **Reframe:** "Ignition is a SCADA suite; we're the data layer underneath."
- **Win on:** edge-first footprint; canonical model + multi-site analytics without building screens.
- **Lose if:** they want screen-builder / HMI / supervisory control / unlimited-tag licensing.
- **Relationship:** coexist (we collect → MQTT → Ignition renders).

### Build in-house
- **Reframe:** "The driver is 20%; the production-grade 80% is what we sell."
- **Win on:** resilience/audit/redaction/diagnostics already built; permanent maintenance tax avoided.
- **Lose if:** strong OT eng team + few protocols + high volume + long timeline.
- **Relationship:** compete (ROI math; engineering-driver-savings bucket).

### MQTT + own script
- **Reframe:** "DIY can work for 1–2 machines; around 10+ machines / multiple protocols / multiple sites, the operational burden becomes the project."
- **Win on:** store-and-forward, quality codes, per-adapter isolation, config management.
- **Lose if:** single-machine pilot; genuinely simple, low-criticality use case.
- **Relationship:** compete (at scale).

### Cloud IIoT (SiteWise / IoT Hub)
- **Reframe:** "Not AWS vs us — who gets normalized data off the floor? We feed them."
- **Win on:** offline-capable edge acquisition + canonical model; cloud-cost reduction when filtering / aggregation scoped.
- **⚠ Honesty:** SiteWise Edge / Azure IoT Operations are real edge tiers — if in scope, compare directly; don't claim we're "the missing edge layer."
- **Lose if:** all-cloud single-vendor mandate; no on-prem appetite; their edge tier already covers acquisition.
- **Relationship:** feed them (compare if SiteWise Edge / IoT Operations in scope).

### OEM tools (MT-LINKi / Brother / Mazak)
- **Reframe:** "OEM tools are often vendor-centered; one canonical model spans the whole multi-vendor floor."
- **Win on:** cross-vendor canonical model + EREMOS workflows; FOCAS2-direct acquisition today.
- **⚠ Honesty:** MT-LINKi is roadmap — lead with **FOCAS2-direct**, never claim MT-LINKi collection today. Don't call Mazak SmartBox single-vendor — it has a broader MTConnect / sensor story.
- **Lose if:** single-vendor shop; OEM tool free/bundled and limits not yet hit.
- **Relationship:** compete / absorb (we'll collect from MT-LINKi too once it ships).

### Existing SCADA (Wonderware / iFIX / WinCC)
- **Reframe:** "We don't replace SCADA; we're the analytical layer beside it."
- **Win on:** audit-ready OEE, multi-site aggregation, alarm/incident tracking; OPC UA Server feeds the SCADA.
- **Lose if:** SCADA already covers analytics; deep political attachment; they want a SCADA replacement.
- **Relationship:** coexist.

### MachineMetrics
- **Reframe:** "Closest head-to-head — turnkey monitoring/OEE; we differ on edge/offline/on-prem + neutrality."
- **Win on:** offline / on-prem option; broker-agnostic ownership of the edge data layer; canonical model + EREMOS.
- **Lose if:** turnkey cloud dashboard with no neutrality/offline constraint (faster to value).
- **Relationship:** compete, sometimes coexist.

### Sight Machine
- **Reframe:** "Enterprise data / semantic / AI stack — usually downstream of us, not against us."
- **Win on:** plant-floor acquisition + CNC/mixed-controller canonical stream feeding their model.
- **Lose if:** corporate already runs Sight Machine and only needs a feeder they've sourced.
- **Relationship:** feed / coexist.

### Tulip
- **Reframe:** "Frontline-ops / no-code / composable-MES apps — a different layer from machine data."
- **Win on:** machine-signal acquisition + OEE/alarm data feeding operator workflows.
- **Lose if:** the real need is operator apps / MES workflows first.
- **Relationship:** feed / coexist; sometimes competes for the operations-app budget.

### Unified Namespace / HiveMQ Edge / EMQX Neuron
- **Reframe:** "You built the namespace; we may be the CNC-aware on-ramp — if your adapters already cover it, we compare honestly."
- **Win on:** CNC-aware acquisition (FOCAS2 / Brother / MTConnect) + canonical model + store-and-forward + EREMOS.
- **⚠ Honesty:** HiveMQ Edge / EMQX Neuron DO have OT adapters (OPC UA / Modbus / S7) — don't claim they can't acquire. Don't promise Sparkplug B on the spot.
- **Lose if:** their adapters already cover their controller mix; they only needed a broker.
- **Relationship:** feed / coexist / compete on edge acquisition (we're not a broker).

### Power BI / Grafana
- **Reframe:** "BI tools *show* numbers; the question is who *computes* OEE and collects from the machines."
- **Win on:** OT-native OEE/alarms/asset-tree (EREMOS V2) + the collection layer BI tools don't have.
- **Lose if:** strong BI team wants to own the semantic layer (sell them EdgeConnect only); light reporting needs.
- **Relationship:** feed them (EREMOS V2 / MQTT → BI downstream).

---

## When to disqualify

The honest read across all ten objections — the customer profiles where Elpis loses fair and square:

- **Single-vendor shops** with no multi-vendor expansion plans (OEM tools are sufficient).
- **All-cloud mandates** where on-prem deployment is politically blocked.
- **Single-machine pilots** where DIY genuinely makes more sense than buying a platform.
- **SCADA-suite-only buyers** who want screen-builder + supervisory control + analytics in one product (Ignition).
- **Pure OPC-server-replacement** scenarios with no analytics need (Kepware).
- **Turnkey-cloud-dashboard buyers** with no neutrality/offline constraint (MachineMetrics may be faster to value).
- **Pure visualization needs** already covered by a few Grafana panels, with a team that wants to own the semantic layer.
- **Plants with deep existing investment in any of the above** where switching cost exceeds the value of switching.

Additional disqualify / engineering-escalation triggers (added v3 — these are shipped-state gates):

- **Sparkplug B conformance required today** — confirm with engineering; lose if strict conformance is mandatory and unsupported.
- **MT-LINKi ingestion required today** — roadmap; we read Fanuc via FOCAS2-direct today, not MT-LINKi.
- **HTTP / TCP sinks required today** — roadmap; don't workaround-promise.
- **Rockwell EtherNet/IP, Mitsubishi, Omron, Beckhoff ADS, BACnet, etc. required today** — not in the shipped southbound set; engineering confirms.
- **Customer already standardized on HighByte / Litmus / HiveMQ Edge / EMQX Neuron as the edge-acquisition layer** — we may still win CNC/OEE modeling, but not by claiming they lack acquisition.
- **Customer wants operator apps / MES workflows first** — Tulip or Ignition may lead.
- **Customer wants a historian / time-series system of record** — AVEVA PI, Canary, etc. may be the center of gravity.
- **Customer gates on a public SBOM, pen-test report, formal cert, or vulnerability-disclosure program** — the security review supports what exists; don't improvise (`/security` §6).

Qualifying out gracefully when one of these applies preserves trust for future opportunities and protects your pipeline accuracy. **A deal you should never have pursued is worth less than no deal at all.**

---

## When to bring in engineering

Escalate to an Elpis engineer (sales engineer first, then platform engineer) when:

- Customer needs a **protocol we don't ship today** — **FANUC MT-LINKi (REST)** or **HTTP / TCP sinks** (both roadmap, no committed date), or a southbound protocol outside today's set (**Rockwell EtherNet/IP, Mitsubishi, Omron, Beckhoff ADS, BACnet**, etc.). Engineering advises on timing; never promise a date in the room.
- Customer needs **Sparkplug B / a specific UNS topic-model conformance** — confirm current support before committing.
- Customer asks for an **OPC UA Server security mode beyond Sign / SignAndEncrypt + X.509** — engineering advises on what we actually do.
- Customer asks about **specific Fanuc model compatibility outside the 0i–32i range** — engineering confirms or flags a known limitation.
- Customer asks about **integration with a specific MES / ERP / historian** — sales engineering scopes it.
- Customer's IT security review asks for documentation we don't have public-facing yet (vulnerability disclosure, pen-test results, SBOM for a specific release) — engineering supplies what exists. (`/security` covers the posture; specifics go through the security review.)
- Customer asks **when AI features ship / for an AI demo** — AI is a Phase-4.5 architectural commitment, not a current feature. Engineering/product owns any timeline conversation.

---

## When to escalate to executive

Escalate to founder / exec level when:

- A deal materially shifts roadmap priorities (large customer asks for a protocol or capability that would change the roadmap — e.g. wants MT-LINKi or HTTP/TCP sinks pulled forward).
- An OEM partnership conversation reaches white-label / co-branding scope.
- A multi-site deal at enterprise scale (10+ plants, multi-year commitment) — strategic deal-shaping.
- A customer's security or compliance requirements would require a certification we haven't pursued — exec decides whether to pursue it for that customer (`/security` §6 honest-posture framing applies).

---

## Version history

- **v3, 2026-06-05 (LOCKED)** — substantive refresh + expansion. Corrected protocol reality (MT-LINKi → roadmap, lead with FOCAS2-direct; Siemens S7 + OPC UA Client ship today; HTTP/TCP sinks → roadmap). Folded in shipped features (secret redaction + diagnostic bundle, broader operator Studio, operator-available hardware). Made AI honesty explicit (architectural commitment, not a demoable feature). Refreshed all cross-refs (datasheet v5, `/security` v3, migrated solution specs, `/platform`, 7 product pages, architecture-diagram v3). Added three second-wave objections (MachineMetrics / Sight Machine / Tulip; Unified Namespace / HiveMQ Edge / EMQX; Power BI / Grafana), a competitive maturity at-a-glance table, and condensed per-competitor battlecards. **ChatGPT "Approve with changes" applied** — P0 competitor-fairness fixes (HiveMQ Edge / EMQX Neuron are edge/protocol gateways; Mazak SmartBox not vendor-locked), own-platform "rollback" guardrail, P1 accuracy edits (Ignition Edge, SiteWise Edge / Azure IoT Operations, tested cloud endpoints, MachineMetrics/Sight Machine/Tulip characterizations), and P2 tone + expanded disqualify triggers + v4 competitor-recognition backlog.
- **v2, 2026-05-24** — added "How to use this in live calls" callout to prevent over-scripting by junior reps; lightly tightened the densest bullets in §3 / §5 / §7 for live-call skimability. No structural changes; no new objections.
- **v1, 2026-05-24** — initial draft covering Kepware, Ignition, build-in-house, MQTT-DIY, cloud IoT platforms, OEM-vendor tools, and existing-SCADA objections.

Future versions (v4+) may add:
- Vertical-specific objection overlays (automotive, aerospace, energy patterns).
- Win/loss analysis from actual deals; refined "where we lose" sections with deal data.
- Print-ready 1-page battlecard exports per competitor (the cards above, formatted for a single sheet).
- **Recognize these names even before full battlecards exist** (v4 candidates): **HighByte Intelligence Hub**, **Litmus Edge** (UNS / edge normalization); **Canary Labs**, **AVEVA PI System / PI Vision** (historian-of-record); **FactoryWiz**, **Scytec DataXchange**, **Datanomix** (CNC-monitoring specialists); **PTC ThingWorx / DPM**; **Rockwell FactoryTalk**, **Siemens Industrial Edge / WinCC**; **OPC Router**, **Softing**, **Matrikon** (OPC / middleware); **Shoplogix**, **Plex**, **Sepasoft MES**. Likely v4 objection clusters: *"Why not HighByte / Litmus?"*, *"Why not our historian?"*, *"Why not a CNC-monitoring specialist?"*

---

*Sales Objection-Handling Guide — Internal v3 (LOCKED 2026-06-05, ChatGPT "Approve with changes" applied). INTERNAL USE ONLY. Do not distribute externally. Own-platform claims verified against CLAUDE.md §3/§8 current state: MT-LINKi roadmap; S7 + OPC UA Client shipped; HTTP/TCP sinks roadmap; AI = Phase-4.5 architectural commitment, not a shipped feature. Competitor characterizations corrected per the 2026-06-05 review (HiveMQ Edge / EMQX Neuron edge gateways; Mazak SmartBox MTConnect reach; Ignition Edge; SiteWise Edge / Azure IoT Operations).*
