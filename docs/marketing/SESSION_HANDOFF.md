# Marketing & Sales Session Handoff — Elpis EdgeConnect

**Date:** 2026-05-23
**Author of this handoff:** Engineering Claude (M.2d.2 close-out session)
**Audience:** Fresh Claude session starting marketing/sales content work
**Branch this lives on:** `claude/marketing-handoff` (off `origin/master`)

This document briefs a fresh session to produce marketing and sales materials for **Elpis EdgeConnect**. Read this first; then ask the user which deliverable to start with.

---

## 0. What this session is for

**Produce marketing and sales content** that promotes Elpis EdgeConnect by explaining:

- What the product is (positioning)
- Features
- Use cases
- ROI
- Advantages over alternatives
- Buying signals + objection handling

**This session is NOT:**

- An engineering session — do not modify `src/**`, `tests/**`, or locked architecture docs
- A product-strategy session — do not change roadmap, locked decisions, or platform principles
- A vague "do everything" session — work one deliverable at a time, get feedback, iterate

---

## 1. What Elpis EdgeConnect IS (the locked truth)

Drawn from `CLAUDE.md` §1 and `docs/ARCHITECTURE_BLUEPRINT.md`. Every technical claim you make in marketing copy MUST trace back to one of these.

**Elpis EdgeConnect is a protocol-agnostic Industrial Edge Integration Platform.** It runs as a Windows service (Linux later) on the factory floor, collects data from industrial devices via multiple southbound protocols (FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP today; Siemens S7 + OPC UA Client coming), normalizes everything through a canonical data pipeline, and delivers it to one or more northbound systems (MQTT today; HTTP, TCP, OPC UA Server coming).

It is **not** a gateway with a few protocols added over time. It is a modular platform with:

- A locked protocol-agnostic core
- Pluggable source and sink adapters (compile-time assemblies, license-activated)
- A canonical internal data model (`CanonicalDataPoint`)
- Route-based configuration (one source → many sinks, independently)
- Three-layer licensing (packaging + runtime activation + UI/API enforcement)
- An admin UI called **Connectivity Studio** (default at `http://127.0.0.1:5080`)

---

## 2. Features the session can confidently promote

Every item below maps to shipped or in-flight Phase 2 work. **Do not invent.** If the user requests a claim you can't trace, ask.

### 2.1 Southbound (data sources) — protocols supported today

| Protocol | Status | What it covers |
|---|---|---|
| **FOCAS2** | Shipped | Fanuc CNCs — axes, spindle, alarms, tool, production, programs |
| **MT-LINKi** | Shipped (legacy migration to new architecture pending) | MT-LINKi-shape diagnostics on compatible Fanuc controllers |
| **MTConnect** | Shipped (legacy) | The industry-standard CNC streaming protocol |
| **Brother HTTP** | Shipped | Brother CNCs (S700Xd1 and similar) via built-in port-80 web-monitoring interface |
| **Modbus TCP** | Shipped | PLCs, drives, energy meters, any Modbus TCP device |

**Coming soon (Phase 5):**

- OPC UA Client
- Siemens S7

### 2.2 Northbound (sinks)

| Sink | Status |
|---|---|
| **MQTT** | Shipped — batch mode + per-tag mode, EREMOS V2-compatible |
| **HTTP** | Phase 3 |
| **TCP** | Phase 3 |
| **OPC UA Server** | Phase 5 |

### 2.3 Operational features (all locked architectural decisions)

- **Store-and-forward is mandatory.** Per-route SQLite with per-sink cursors. If the broker goes down, EdgeConnect buffers and replays — no lost data.
- **Per-adapter isolation.** One failing protocol never affects another adapter, route, or sink.
- **Fanout independence.** A failing sink never blocks a healthy sink. Each commits independently.
- **Two delivery modes** in v1: `AtMostOnce` and `AtLeastOnce`. (ExactlyOnce explicitly out — that's a feature, not a gap.)
- **Per-source ordering guaranteed.** No global ordering claim (deliberate — global ordering at the edge is a lie).
- **Three-way diagnostics.** Source / pipeline / sink — operators see exactly where the data flow broke.
- **Connectivity Studio.** Web admin UI for adding sources, sinks, routes; running Test Connection probes; viewing diagnostics. Edit-via-Wizard for existing sources just landed in M.2d.2.

### 2.4 Licensing (a major sales advantage)

- **Three-layer enforcement:** per-edition installers + signed JSON license file + UI/API enforcement.
- **Fully offline.** RSA-signed license, no phone-home. Critical for air-gapped factories.
- **License expiration never cuts customer data.** It blocks config changes only. Your data keeps flowing even if the license lapses — buyer trust.
- **Per-protocol module licensing.** Customers pay for what they use. (Per-edition packaging is the public-facing model; the underlying capability is per-module.)

### 2.5 Security and audit

- Signed configuration audit log (hash chain) — see every change to the gateway config with actor + timestamp.
- Optimistic-concurrency on config edits — two operators editing the same source can't silently overwrite each other (M.2d.2).
- Draft → validate → apply → rollback flow — never apply an untested config; rollback is one click.
- Per-gateway UUID + customer/site binding established at first start.

### 2.6 AI — deliberately constrained

This is a positioning advantage worth being explicit about:

- **AI in decision-support only, never in the data path.** The pipeline is deterministic, testable, replayable.
- **Tool-use pattern.** Agents interact via structured calls to the management API, not free-text code generation.
- **State changes always proposed, never autonomous.** User confirms in chat.
- **Local-LLM support mandatory from day one.** Cloud LLMs optional. No "secrets to OpenAI" anxiety for security-conscious manufacturers.

This is the **opposite** of vendors slapping ChatGPT into a settings dialog. The product's marketing should lean into that.

### 2.7 Cross-product compatibility

- Integrates natively with **EREMOS V2** (Elpis's other product) via per-tag MQTT topic shape
  `eremos/{gatewayId}/cnc/{sourceId}/{tagName}`. Out of the box.
- Works with **any standard MQTT broker** (Mosquitto, HiveMQ, EMQX, AWS IoT Core, Azure IoT Hub, etc.) — EREMOS V2 is not required.

---

## 3. Where to find the deeper details

When you need to back a marketing claim with technical detail, read in this order:

1. `C:\dev\EdgeConnect\CLAUDE.md` — Project overview, Phase status, locked decisions (the fastest read).
2. `C:\dev\EdgeConnect\docs\ARCHITECTURE_BLUEPRINT.md` — Especially **Appendix A** (the locked-decisions table).
3. `C:\dev\EdgeConnect\docs\platform-principles.md` — P1-P6 cross-milestone commitments. Useful for "what we won't compromise on" copy.
4. `C:\dev\EdgeConnect\docs\PHASE1_EXECUTION_PLAN.md` Section 10 — Exit criteria of the foundation phase. Tells you what's definitively shipped.
5. `C:\dev\shared-knowledge\README.md` — Cross-product context, especially EREMOS V2 integration.
6. `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md` — The MQTT topic contract.

If a question isn't answered in those documents, **ask the user**. Do not invent.

---

## 4. Use cases (sales-ready story scaffolds)

Each is a stub — the marketing session should flesh these into full case-study templates or solution briefs. The user will supply real customer names and ROI numbers when ready.

### 4.1 Multi-vendor CNC factory floor

Plant with 20-100 CNCs across Fanuc, Brother, and Mazak. Need one operational dashboard. EdgeConnect normalizes each protocol into the canonical model and publishes to MQTT (or EREMOS V2). Result: a single source of OEE truth across the plant without ripping out controllers.

### 4.2 Brownfield modernization

Plant has 15-year-old Fanuc 16i/18i CNCs lacking modern integration. EdgeConnect runs FOCAS2 against them and exposes data over MQTT to a modern analytics stack. Customer keeps the controllers; modernizes the data layer.

### 4.3 OEE / production tracking

Operator needs cycle time, parts count, alarm state, tool wear streaming to a real-time dashboard. EdgeConnect's Production + Tool collectors handle FOCAS2 + MT-LINKi out of the box; Brother HTTP's Production group covers Brother CNCs the same way.

### 4.4 Multi-site fleet

10+ plants, each with its own EdgeConnect gateway, all reporting to a corporate MQTT broker. Each plant runs offline-capable; outages buffer locally and replay on reconnect. The per-gateway UUID + customer/site binding gives clean fleet identity.

### 4.5 Hybrid edge + cloud

EdgeConnect collects everything at the edge, filters via routes, sends only what matters to AWS IoT Core / Azure IoT Hub. Sensitive data stays local; aggregate KPIs flow to the cloud. Pay-per-egress savings are real.

### 4.6 Compliance / audit trail

Regulated industries (medical device manufacturing, aerospace) need provable config-change history. EdgeConnect's hash-chained audit log + signed configs + version history is built for this — not bolted on.

### 4.7 AI-assisted operations (future-positioned, ship-when-ready)

Phase 4.5 brings four AI agents: Diagnostic, Configuration, Tag Mapping, Intelligent Alerting. All decision-support; all human-confirmed; all local-LLM-capable. Position as "AI that respects the data path, not AI in the data path."

---

## 5. ROI angles to develop

When the user asks for ROI content, these are the credible angles. Concrete numbers come from the user (or from customer interviews); the marketing session should NOT fabricate them.

| Angle | Quantifiable as |
|---|---|
| Engineering time saved vs. in-house protocol drivers | weeks per protocol × engineer day rate |
| Downtime avoided via store-and-forward | hours of disconnect tolerated × parts/hr × margin |
| Operator clarity via 3-way diagnostics | mean-time-to-diagnose drop from N hours to M minutes |
| Compliance audit speed | days of audit prep saved per inspection |
| License flexibility (per-protocol) vs. SCADA suite bundling | bundle cost − à la carte cost |
| Edge security (no phone-home, local-LLM) | reduced cyber-insurance premiums, simpler IT review |
| Multi-vendor freedom | no lock-in cost to current broker / cloud |

A first deliverable: a simple ROI calculator (markdown or spreadsheet logic) that takes inputs (# CNCs, # protocols, current SCADA cost, downtime cost/hr) and emits a payback estimate.

---

## 6. Competitive positioning (handle with care)

The marketing session should not name competitors unfavorably in customer-facing copy without the user's sign-off. For internal positioning notes, useful mental map:

| Competitor | EdgeConnect angle |
|---|---|
| **Kepware / KEPServerEX** | Modern architecture, route-based fanout, AI-assist done right, edge-first, transparent per-protocol licensing |
| **Ignition (Inductive Automation)** | Lighter weight, edge-first, simpler licensing, no client/server complexity for the data-collection layer |
| **HiveMQ Edge / Bevywise** | Industrial protocol depth (FOCAS2, MT-LINKi, Brother HTTP — not just OPC UA + Modbus) |
| **Custom-built protocol drivers** | Compliance + maintainability + edition-based licensing; offloads the hardest 30% of "make it work in production" |
| **Generic OPC UA gateways** | We do OPC UA Server (Phase 5) AND vendor-specific protocols (FOCAS2 / Brother HTTP) others won't touch |

The strongest positioning line we have: **"Multi-protocol industrial edge integration that respects the data path."** The "respects the data path" part is the AI-positioning, store-and-forward, offline-licensing, deterministic-pipeline message rolled into one phrase. Worth testing.

---

## 7. Tone, voice, and audience guidance

**Audience tiers (rough):**

- **Plant manager / operations VP** — cares about uptime, OEE, ROI, no-jargon
- **Industrial IT / SCADA engineer** — cares about protocols, security, deployment model
- **System integrator (channel)** — cares about margin, support burden, OEM-friendly licensing
- **OEM (CNC vendor) procurement** — cares about embedding terms, white-label, scale licensing
- **CISO / security review** — cares about offline operation, audit trail, no-phone-home, local-LLM

Most deliverables address one of these. Don't try to address all five in one piece.

**Tone:**

- Confident but technical
- Concrete > abstract ("FOCAS2 axis positions polled at 1 second intervals" not "real-time CNC data")
- Avoid hype words: "revolutionary," "game-changing," "AI-powered" (we ARE AI-aware but lean into the constraint, not the buzzword)
- Use real protocol names — they're trust signals to the buyer

**Avoid:**

- AI-washing (we are doing AI right by being constrained about it — don't undermine that with marketing)
- Promising things not in the locked roadmap
- Customer testimonials without user-provided text
- Specific ROI percentages without user-provided source data

---

## 8. Suggested deliverables (one at a time)

The user will pick which to start with. Common asks:

1. **One-page product datasheet** — features + benefits + protocols + pricing tier callout. PDF-ready markdown.
2. **Solution brief** (5-7 pages) per use case — problem, EdgeConnect approach, before/after, deployment shape, pricing path.
3. **ROI calculator** — markdown spec for a spreadsheet or web calc; the user supplies plug-in numbers.
4. **Competitive comparison matrix** — internal use first, then customer-sanitized.
5. **Pitch deck outline** — title, problem, solution, demo flow, pricing, ask. ~15 slides.
6. **Website landing-page copy** — hero, three-feature row, use cases, testimonials placeholder, CTA.
7. **Email / outreach templates** — 3-touch cold sequence, follow-up after demo, win-back.
8. **Blog post series** — 3-5 posts: "How EdgeConnect handles X" deep-dives.
9. **Sales objection-handling guide** — internal-only; "why not Kepware?" "why not build in-house?" "why not just MQTT broker + driver?"
10. **Case-study template** — placeholder structure for when the first real customer story lands.

**All output files go under `docs/marketing/`.** Suggested naming:

- `datasheet-edgeconnect-v1.md`
- `solution-brief-multi-cnc-factory.md`
- `roi-calculator.md`
- `pitch-deck-outline.md`
- `competitive-matrix-internal.md`
- `landing-page-hero.md`
- `objection-handling-internal.md`
- `case-study-template.md`
- `blog-01-protocol-agnostic-core.md` (etc.)

Use clear, dated revisions when iterating: `datasheet-edgeconnect-v2.md` rather than overwriting v1.

---

## 9. Questions the marketing session should ask before producing copy

The user has context the engineering codebase doesn't capture. Surface these BEFORE writing the first long piece:

1. **Target geography first?** India, US, EU, global, OEM-channel-via-Japan?
2. **Pricing model?** Per-protocol, per-CNC, per-site, per-gateway, edition tiers, freemium?
3. **Named customers to feature?** Or all anonymized for now?
4. **Brand voice references?** Existing marketing copy or website to mirror tone from?
5. **Distribution channels?** Direct sales, SI partners, OEM bundling, online self-serve?
6. **Competitive positioning preference?** Head-to-head (name competitors) or category-creation (define a new category we own)?
7. **Visual brand assets?** Logos, color palette, font choices, photo style — does Elpis have a brand book?
8. **Decision-maker mapping?** Who in the buyer org signs the PO, who blocks the deal, who champions?
9. **Length tolerance?** Some markets want one-page datasheets; others demand 20-page white papers.
10. **Localization?** English-only, or also Japanese (Fanuc is Japanese), German (Siemens), Mandarin (Brother + China market)?

Don't ask all 10 at once. Ask 2-3 most-blocking-on-the-current-deliverable.

---

## 10. What this session must NOT do

| Don't | Why |
|---|---|
| Modify any `src/**/*.cs`, `src/**/*.razor`, `tests/**` | Engineering scope — not yours |
| Modify `docs/ARCHITECTURE_BLUEPRINT.md`, `docs/PHASE*_EXECUTION_PLAN.md`, `docs/platform-principles.md`, `docs/decisions/**` | Locked-decision docs — require ADR process |
| Modify the M.2d.2 PR or any other open engineering work | Not your scope |
| Invent technical claims | Every feature claim must be traceable to §1, §2, or §3 above |
| Invent customer names, testimonials, or ROI percentages | Wait for the user to supply real ones |
| Push to master without explicit user instruction | Use a `claude/marketing-<topic>` branch and open a PR |
| Modify `CLAUDE.md` | Engineering owns it |

---

## 11. Branch + PR conventions

- Land each marketing deliverable on its own branch: `claude/marketing-datasheet`, `claude/marketing-pitch-deck`, etc.
- Open a PR for each, even drafts — keeps revisions reviewable.
- Use the same commit message conventions as engineering work: short subject, body explains the why.
- Never amend commits the user has reviewed.

---

## 12. First action when this session opens

1. Read this file end-to-end. (You're doing it now.)
2. Read `CLAUDE.md` §1 and §3 — confirm you understand the product and the locked decisions.
3. Ask the user: **"Which deliverable do you want first — datasheet, pitch deck, solution brief, ROI calc, or something else? And which audience tier — operator, IT/security, integrator partner, or OEM procurement?"**
4. Once the answer comes back, ask 2-3 of the §9 questions targeted at that deliverable.
5. Produce a draft. Get feedback. Iterate.

Don't try to produce all ten deliverables in one go. Iterate one at a time.

---

## 13. Reference: M.2d.2 close-out context (just-in-case)

The engineering session that wrote this handoff just closed Milestone M.2d.2 — Edit-via-Wizard for sources. Relevant to marketing because it's a real Connectivity Studio feature you can demo:

- Operators can now click Edit on any existing FOCAS2, Brother HTTP, or Modbus TCP source and update it through the same wizard that created it.
- Optimistic-concurrency check prevents two operators from silently overwriting each other.
- Test Connection probes on all three wizards.

PR: https://github.com/elpisitsolutions/EdgeConnect/pull/28

This is good "what's new" material for a release blog post or product update email.

---

**End of handoff. Good luck — make EdgeConnect easy to buy.**
