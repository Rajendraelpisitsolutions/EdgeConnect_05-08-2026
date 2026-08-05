# ADR-0026: Route Timeline — unified rendering of Flight Recorder + Explain Why + What Changed (no causality claim)

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** The Route Timeline is a single visual surface that renders state events (ADR-0021), state changes (ADR-0024), and Explain-Why verdicts (ADR-0023) along a unified time axis. **The UI never uses the words "root cause."** Events are shown in time order; correlation is visible; causality is for the operator to infer from the chain. This naming + scoping discipline preserves P7's honesty clause.

## Context

The 2026-05-30 strategic review (ChatGPT proposal #11, then surfaced as ChatGPT's "Root Cause Timeline" addition) identified the unified timeline as a signature surface. The instinct is correct — Flight Recorder events, state changes, and explain-why verdicts share a temporal dimension; rendering them on one canvas lets the operator see "MQTT disconnected at 10:34, buffer growth started at 10:34, first drop at 10:39" as a coherent picture rather than three separate pages.

The trap is the naming. "Root Cause Timeline" claims causality the system cannot prove. The system can prove temporal ordering (event A happened at T₁; event B happened at T₂ > T₁). It can prove *correlation* (the verdict shape suggests the missing-on-sink starts shortly after the SinkDisconnected). It cannot prove that A *caused* B without a causal model — and that model would either be a heuristic (rejected by P7) or an LLM inference (rejected by P7).

If we ship "Root Cause" and customers find the system pointing at the wrong root cause once, the explainability promise collapses harder than if we'd never used the word. The naming discipline is the load-bearing piece.

## Decision

The Route Timeline conforms to the following five rules.

### Rule 1 — UI label is "Route Timeline," never "Root Cause"

The surface is labelled "Route Timeline" in nav, page titles, breadcrumbs, screenshots, marketing copy, ADR cross-references, and bundle manifests. The string "root cause" does not appear in the UI under any circumstance. Internal class names may use `Timeline`; never `RootCause`.

Rationale: the system displays evidence in time order with structured per-event detail; the operator infers causality. Implying the system has determined the cause violates P7.

### Rule 2 — Data source is the union of three existing datasets

Timeline renders the union of:

- **Flight Recorder events** (ADR-0021) — state transitions (connect, disconnect, subscription created, queue threshold crossed, etc.)
- **`StateChangeRecord` entries** (ADR-0024) — config, cert, license, throughput-envelope changes
- **Verdict events** (ADR-0018) — Compare-mode verdicts (✓ exact, ⚠ transform-altered, ⛔ missing-on-sink, ⛔ extra-on-sink) at their `correlationId.deviceTimestamp`

Timeline does NOT generate any new dataset. It is purely a rendering layer over existing data sources. This eliminates divergence between the surfaces — change Flight Recorder's event schema, Timeline reflects it; change Compare's verdict shape, Timeline reflects it.

### Rule 3 — Visual representation: vertical time-ordered list, expandable per row

The Timeline renders as a vertical scrollable list, newest-on-top. Each row is one event with:

- Timestamp (precise to millisecond when available)
- Event kind icon (per category)
- Operator-language label (one line)
- Severity colour (matches Flight Recorder severity palette)
- Expand-to-detail affordance: clicking expands the row to show the full structured detail and, for verdict events, the Explain-Why because-chain inline (ADR-0023's `ExplainStep` list)

Filtering: top toolbar offers per-category filters (Lifecycle / Connection / Subscription / Throughput / Verdicts / Configuration / Faults / External). Default: all categories visible.

Time window selector: 5 min / 1 hour / 24 hours / 7 days / since Last-Known-Good (per ADR-0025) / custom range. Default: 1 hour.

### Rule 4 — Correlation indicators, not causality claims

Rows that the system can deterministically associate (e.g., a `MissingOnSink` verdict whose correlation id matches the time window of a `SinkDisconnected` event 12s earlier) get a **correlation indicator**: a subtle visual link (left-rail bracket) between rows and an indicator label such as *"correlates with"* or *"observed near"* — never *"caused by"* or *"resulted from."*

The correlation rule is documented per pairing:

| Event pair | Correlation indicator | Rationale |
|---|---|---|
| `SinkDisconnected` + later `MissingOnSink` verdicts within window | "Verdicts observed during sink disconnect window" | Temporal correlation, not causal proof — sink could have disconnected because of unrelated network issue |
| `ConfigApplied` + change in throughput envelope within 60s | "Throughput change observed near config apply" | Apply may not be the cause; coincidence possible |
| `CertificateNearingExpiry` + later `SourceDisconnected` | "Disconnect observed; cert was approaching expiry" | Cert may or may not be the cause; many disconnect causes exist |

The correlation indicators are deterministic (rule-table based). They surface plausibility for the operator to verify; they don't claim closure.

### Rule 5 — Time window honours ADR-0017 demand-driven activation

Loading the Timeline page activates the underlying data feeds (Flight Recorder query, StateChangeRecord query, Verdict event subscription) per ADR-0017 Rule 2 (subscriber-presence activation). Closing the page deactivates after the 60s cooldown. Timeline itself does not maintain a persistent subscriber state beyond what the underlying surfaces require.

## Consequences

**Positive:**

- The operator sees the route's recent history in one scrollable surface — no need to cross-reference three pages
- Verdicts and state events render adjacent — "the missing-on-sink verdicts cluster right after the sink disconnect" becomes visually obvious without claiming the system inferred the cause
- The naming discipline protects the long-term explainability promise — by deliberately under-claiming the system's certainty, the surface remains trustworthy when it's right (most of the time) and not embarrassing when correlation isn't causality
- Premium-feel surface that doesn't require months of engineering — it's a rendering layer over data we already collect

**Negative:**

- The deliberate naming restraint may feel like underselling to marketing copy. The mitigation is that Level-3 guidance (P7) inside the Explain-Why inline expand still answers "what action you can take" — the surface remains directive, just not over-claiming.
- Correlation indicators (Rule 4) are a rule table that needs maintenance as new event kinds are added. Mechanical.
- Visual density at high event rates may overwhelm. The category filters and time window selector (Rule 3) are the mitigation; default to 1-hour window.

**Forbidden patterns:**

- The string "root cause" anywhere in the UI, the routes, the breadcrumbs, the screenshots, the bundle manifest, the marketing copy
- A "smart" filter that hides events the system deems irrelevant — operator scopes via the explicit filters, never implicit suppression
- A causal-claim label on any correlation indicator
- A free-text "summary" of the timeline generated by anything (LLM or heuristic) — operator reads the timeline; system doesn't summarise

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (Timeline honours the activation model)
- ADR-0018 — Live Data Tap Compare mode (verdict events feed Timeline)
- ADR-0021 — Route Flight Recorder (state events feed Timeline)
- ADR-0023 — Explain Why Data Is Missing (renders inline per verdict row)
- ADR-0024 — What Changed (state-change records feed Timeline)
- Platform principle P4 — preserve the explainability data path
- Platform principle P7 — surfaces explain outcomes — *this ADR's naming discipline is P7's honesty clause in worked form*
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md` — captures the naming-discipline decision
