# Platform principles

**Status:** Active — seven commitments that shape every milestone
**Established:** 2026-05-19 (P1–P6 consolidated from ChatGPT strategic-review pass); 2026-05-30 (P7 added from diagnostic-strategy review pass)
**Location:** Top-level `docs/` deliberately — these are platform-wide, not session-specific. Lives outside `docs/sessions/` and `docs/decisions/` because it bridges both.

This document captures the principles that emerged from the post-M.2b.6 roadmap strategic review (P1–P6) and the 2026-05-30 diagnostic-strategy review (P7). It is the answer to "why are we doing it this way?" for the next several milestones. Read this before any architectural decision; if a design choice would violate one of these principles, **pause and surface the conflict** — never silently work around it.

---

## P1. Runtime Tap is strictly observational

> The Runtime Tap subsystem (see roadmap v2 §1 Locked A) is a non-intrusive observation layer over the deterministic runtime data path. **Subscribers can READ; nothing in the runtime path can READ from subscribers.**

### Why it matters

The runtime data path is deterministic, replayable, and load-bearing for store-and-forward + audit + reload semantics. The moment a tap subscriber can influence runtime behaviour (e.g. a slow Watch consumer creating backpressure that throttles the pipeline, or a debug tap interpreting itself as a "trace mode" that activates extra logging in the data path), determinism breaks. Bugs become non-reproducible. Customer-site incidents become un-replayable.

### Enforcement in practice

- Tap publication is **zero-cost when no subscribers exist** (no event allocation, no copy, no branch into the tap code path beyond a single `if (subscribers > 0)` check)
- Subscriber backpressure is **isolated**: a slow subscriber drops its own ring-buffer entries before it slows the publisher. The data path never blocks on a subscriber.
- No data-path code reads any subscriber state. Tap is **publisher-only from the runtime's perspective.**
- License gating can disable the tap entirely (sensitive deployments); the data path is unaffected by the gate state.
- Replay reproducibility test: a recorded session re-played with tap on vs tap off must produce byte-identical canonical points.

### What this rules out

- A "trace mode" that mutates pipeline behaviour when tap is active
- "Adaptive sampling" that throttles based on tap consumer count
- Any tap-emitted event whose existence affects audit-chain content
- Any sink-delivery decision (retry, drop, requeue) being influenced by tap subscriber state

---

## P2. Shared interaction primitives, not page-by-page chrome

> Every list, table, form, wizard, and modal in Studio shares a small set of reusable interaction primitives. Pages CONFIGURE these primitives; they do not reinvent them.

### Why it matters

Linear, Notion, Jira, GitHub feel cohesive not because of colour palettes but because their interaction primitives are consistent across every page. The same multi-select gesture works the same way in any list. The same search-and-filter affordance behaves identically across all entities. The same keyboard shortcuts mean the same things everywhere.

Industrial products typically fail this test. Tables in Kepware behave differently across protocols. Matrikon's filters work differently per tab. Operators relearn each page.

### Enforcement in practice

- `EntityListView<T>` (M.2e) is the only list-table component. New list pages configure it; they do not write custom tables.
- `RuntimeTap` (M.2c) is the only live-data-stream consumer pattern. Future Watch / inspector pages subscribe; they do not invent new streaming surfaces.
- Wizard sections share consistent chrome (numbered sections, sticky headers, mandatory Draft Summary panel). New wizards extend the pattern; they do not redesign it.
- POCO view-model + Razor shell + POCO unit tests is the testing pattern for every Studio component (no bUnit; matches `ReloadOutcomePanelModel` / `SourceProtocolPickerModel` / `LayoutChromeModel` precedent).
- The premium-UX implementation discipline (M.2b.5/6 v3 plan §3) applies to every Studio milestone — never degrade to plain forms to ship faster; pause and report.

### What this rules out

- Adding a new "this page's special table" widget
- Adding a new "this milestone's modal pattern" instead of reusing the shared modal
- Inconsistent multi-select / search / sort semantics per page

---

## P3. Security is spec-first; implementation follows from design, not the other way around

> Security-critical subsystems (Milestone K, future auth/RBAC, future fleet trust) start with a written operational specification. Code starts after the spec is locked, not before.

### Why it matters

Security failures destroy buyer trust disproportionately. An OPC UA security weakness, even if "the rest works," is interpreted as platform immaturity in pharma / automotive / energy / enterprise manufacturing. Iterating security designs in code is expensive — every change ripples through cert lifecycle, trust models, deployment UX, recovery flows, and fleet implications.

### Enforcement in practice

- Milestone K specification (per roadmap v2 §1 Locked C) covers 9 sections: authentication, certificate lifecycle, trust model, deployment UX, renewal, backup, role mapping, recovery, fleet — BEFORE code starts.
- Spec includes operational lifecycle (renewal, rotation, recovery) — not just academic cryptography.
- Spec locks before implementation; review pass against the spec, not the code.
- Cert lifecycle, trust model, role mapping decisions land in ADRs, not retroactively-inferred from the code.

### What this rules out

- Starting K implementation "to learn what's needed"
- Adding authentication features incrementally without an overall auth model
- Implementing security primitives in isolation from operator-facing UX (e.g. "we'll do the UI later")

---

## P4. Preserve the explainability data path in every milestone

> The deterministic pipeline + audit chain + diff-reload classifier already provide a unique foundation for operational explainability ("why did this happen?"). Every milestone must preserve this foundation, even when not actively building on it. **No opaque short-circuits. No swallow-the-error patterns. No undocumented decision branches.**

### Why it matters

Industrial debugging is famously terrible. Operators see disconnected logs, opaque failures, unexplained retries, invisible suppression logic. We already possess the metadata needed for first-class explainability (deterministic pipeline, audit chain, diff-reload classifier from ADR-0009/0010). The Operational Explainability milestone (roadmap v2 §1 Locked D / Tier 4) builds the Studio surface for it later — but the data path must remain non-opaque from now on.

### Enforcement in practice

- Pipeline step that suppresses a point (filter, deadband, rate-limit) emits a structured "suppressed because X" entry to the tap stream and the diagnostics ring buffer
- Reload classifier decisions (ADR-0009) emit "this entity was synthesized recovery / classified as Modified because X" trails
- Adapter retry / backoff / connect-failure paths emit structured codes (already the pattern; preserve it)
- "Catch and ignore" is never the right answer — even unrecoverable errors get a structured fault registry entry
- Code review for new pipeline-affecting code asks: "If an operator asks why this point disappeared, can we point at a specific log/audit/tap entry?"

### What this rules out

- Silent point drops without structured emission
- Try/catch blocks that consume an exception without converting to a structured fault
- Pipeline steps with side effects not visible to the tap or audit chain

---

## P5. EREMOS V2 integration is a primary market identity, not a feature

> We are not selling industrial plumbing. We are selling a vertically integrated operational transformation stack. Every cross-EREMOS integration (canonical-point schema stability, shared license modules, unified diagnostics, MQTT contract) is a high-priority moat investment, not an "if time permits" optional.

### Why it matters

Competitors require buyers to stitch together: a gateway (Kepware) + an MQTT broker (HiveMQ/Mosquitto) + a historian (InfluxDB/PI) + dashboards (Grafana) + MES integration (per-vendor). Five vendors, five UXes, five billing relationships, five integration headaches.

In our target segments — **India + Middle East mid-market manufacturing, CNC machine builders, brownfield modernization, MQTT-first plants** — this stitched-together-five-vendors model is the dominant operational pain. The integrated EdgeConnect + EREMOS story is what makes mid-market buyers pick us over the larger ecosystem players.

### Enforcement in practice

- Shared knowledge base (`C:\dev\shared-knowledge\`) is load-bearing infrastructure, treated as production code
- MQTT per-tag contract stability (EREMOS V2 subscribes to `eremos/+/cnc/+/+`) is a contract change, not an implementation detail
- Cross-EREMOS milestone work jumps priority queue when needed; it's not Tier-4 polish
- License module catalog (`docs/licensing/module-catalog.md`) covers both products' modules
- Pricing / bundling / GTM language treats the two products as one stack

### What this rules out

- "EREMOS-specific" branches inside EdgeConnect (the integration is via the canonical contract, not via if-statements)
- Independent roadmap drift between the two products
- Marketing copy that calls EdgeConnect "a connectivity component" without the operational-stack framing

---

## P6. Operational product, not developer tool

> Earlier-stage products optimise for runtime purity, extensibility, architecture elegance, protocol capability. Operational products optimise for onboarding, visibility, commissioning confidence, recoverability, ergonomics, explainability, trust. **EdgeConnect has crossed that line.** Optimisation targets shift accordingly.

### Why it matters

Most industrial gateway products never make this transition. They remain engineering tools that operators must learn — Win32-style admin UIs, JSON-only configuration, opaque diagnostics. They lose mid-market deals to anyone who has crossed the line.

The architectural foundations to win this transition are already in place (draft semantics, audit chain, hot reload, fail-soft, demo mode, Browse Controller, premium UX baseline). The remaining work is operational completeness — and the priority order reflects that.

### Enforcement in practice

- Tier 1 milestones in roadmap v2 are all operational completeness (M.2c Live Tag Watch, M.2d Edit-via-Wizard, M.2e Shared List Infrastructure) — NOT more protocols, NOT more configuration knobs
- Premium-UX discipline (M.2b.5/6 v3 plan §3) treats degrading the UI to plain forms as a pause-and-report event
- First-run onboarding (M.2g) is Tier 2, ahead of cloud sink expansion (M.2m, Tier 4)
- "Beautiful architecture, rough operator UX" is now the explicit anti-pattern we're closing — not a virtue
- Engineering decisions are evaluated against "does an operator with no knowledge of our codebase succeed at this task?" not "is this elegant?"

### What this rules out

- Optimising for protocol count over operator workflow (the Ignition trap)
- Hiding capability behind JSON configuration when a wizard or visualisation would serve operators better
- Treating "developer can figure it out" as a sufficient quality bar for any operator-facing surface
- Shipping features that work in code review but require docs-page reading for operator success

---

## P7. Surfaces explain outcomes, not just observations

> When a surface presents a failure, warning, state change, or operational condition, it MUST — where deterministically possible — answer four questions: **what happened, why it happened, what changed, and what action is available.** Explanations are derived from observable system state and recorded evidence; never from speculation, inference, or LLM summarisation.

### Why it matters

P4 (explainability data path) commits us to preserving the *evidence* needed to explain what happened. P7 commits us to *actually doing the explaining at the surface*. Without P7, we will preserve diagnostic data we never surface, and operators will see the same opaque "Disconnected / Error / Timeout" symptom-only reporting every other industrial gateway delivers.

The four-question framework — what happened, why, what changed, what action — is the load-bearing piece. Every new operator-facing surface (diagnostics, configuration, governance, future fleet views) is evaluated against whether it answers all four, or honestly marks which it can't.

This is what separates EdgeConnect from a protocol-driver collection. The architectural foundations are already there (deterministic pipeline, audit chain, route state, buffer state, fault registry). The remaining work is the surface-level commitment that those foundations become visible explanation, not just preserved evidence.

### Enforcement in practice

- Every new operator-facing surface that reports a failure mode walks the structured state of the system to produce a "because" chain — derived from route state, buffer state, connection state, configuration history, flight-recorder events, metrics
- Where the system cannot honestly answer one of the four questions, the surface displays **"unknown"** for that question rather than guess. A blank field is preferable to a wrong one.
- No surface uses an LLM to summarise diagnostic state. Local-LLM agents (per ADR-AI-001 and friends) operate as conversational helpers over already-explained surfaces — they never replace the deterministic explainer
- Composite scores that collapse multiple dimensions into a single number are forbidden — they violate the "why" answer by construction (you can't explain a 73/100 without showing the dimensions, at which point you're showing the structured view anyway). Use structured chips with deterministic status derivation (ADR-0027 is the worked example).
- Three operational levels guide review: **Level 1 (Observation)** — "MQTT disconnected" — most gateways stop here. **Level 2 (Explanation)** — "Route drops occurred because MQTT disconnected" — good gateways reach here. **Level 3 (Guidance)** — "Reconnect broker or wait for automatic retry; store-and-forward is buffering the data" — EdgeConnect surfaces aim for Level 3.

### What this rules out

- Free-text error summarisation by an LLM piped over diagnostic output
- A "Confidence Score" / "Route Health Score" / any single composite number as the primary health indicator
- A failure surface that displays an error code without walking the structured state to explain why
- An "explanation" that's a static lookup table from error codes to canned text — the explanation must reference *current state* (which sink, which buffer, which config version), not generic text
- Marketing copy that promises "AI explainability" when the actual mechanism is deterministic state walking; the explainability is the architectural commitment, the AI agents are conversational helpers on top of it

### What this DOES allow

- Renaming a UI label from "Root Cause Timeline" to "Route Timeline" because the system can't honestly claim causality, only correlation — that's the principle working as intended (see ADR-0026)
- Surfaces that mark a question "unknown" when the data legitimately isn't available — that's honest under P7
- The Adapter Self-Test surface generating a structured pass/fail per step with remediation hints — that's Level 3 done well

---

## Cross-references

- **Roadmap v2** ([docs/sessions/2026-05-19-post-mp2b6-product-roadmap-v2.md](sessions/2026-05-19-post-mp2b6-product-roadmap-v2.md)) — where P1–P6 emerged; tier sequencing per principle alignment.
- **M.2b.5 + M.2b.6 v3 plan §3** ([docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md](sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md)) — Premium-UX implementation discipline, the binding-contract operationalisation of P2 + P6.
- **ADR-0009, ADR-0010** — diff-reload classifier + cross-record recovery; the foundations P4 (Explainability) builds on.
- **ADR-0011** — Browse Controller management-plane separation; the architectural pattern P1 (Runtime Tap) extends.
- **ADR-0012** — FOCAS2 demo mode framing; an example of P6's "operational product" mindset in action.

---

## When to amend this document

These principles should remain stable across many milestones. Amend ONLY when:

1. A genuine strategic pivot makes one of them obsolete (e.g. we decide to build a SCADA layer after all — would conflict with P6's framing).
2. A new principle emerges from a future strategic review with the same strength as the six above.
3. An enforcement-in-practice clause needs sharpening because a milestone exposed an ambiguity.

Routine milestone work does NOT amend this document. The premium-UX discipline (M.2b.5/6 v3 §3) is the right place to capture per-milestone discipline; this document captures cross-milestone direction.

---

**End of platform principles. Six commitments locked 2026-05-19. Subsequent milestones inherit these as binding direction unless explicitly amended here.**
