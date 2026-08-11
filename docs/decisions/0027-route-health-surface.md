# ADR-0027: Route Health Surface — structured chips with deterministic status, no composite score

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** This ADR locks how route health is rendered to operators: as structured chips across independent dimensions, with a deterministic overall route status (Healthy / Warning / Degraded / Failed) derived by rule from the chips. **No composite scoring math is permitted anywhere in the UI.** Composite scores collapse multi-dimensional health into a single number that hides the underlying signal, violates P7, and creates well-known industrial-observability pathologies. This is the worked example of P7's "forbidden composite scores" clause.

## Context

The 2026-05-30 strategic review surfaced a temptation: render a "Confidence Score" or "Route Health Score" — a single 0–100 number summarising route health. Operators ostensibly love simple signals.

ChatGPT initially proposed the score; the operator (in the review pass) elevated structured chips to a product-philosophy decision over the score. The argument is fully captured in the strategic discussion and codified here.

The three pathologies of composite health scores in industrial observability:

1. **The weights lie.** A 98/100 that weights drops at 30% and cert expiry at 5% masks a 20-day-out cert problem behind clean drops. The operator learns to trust the number, then the number betrays them.
2. **Operators manage to the score, not the system.** Production deployments routinely show this — actions get done to keep the score green that don't actually keep the system healthy. SRE literature documents the same pathology.
3. **You can't explain a composite number.** "Why is it 98 and not 99?" forces the surface to expose the weights, at which point you're showing the structured view anyway. The score adds opacity without adding information.

Conclusion: structured chips win on every axis (honesty, explainability, operator behaviour). Composite scores are forbidden by ADR.

## Decision

The Route Health Surface conforms to the following six rules.

### Rule 1 — Six chip dimensions, three groups

Each route renders six chips grouped by concern:

**Connectivity (always green-green-green is the baseline)**
- 🟢/🟡/🔴 **Connection** — source + sink connection state
- 🟢/🟡/🔴 **Configuration** — config apply state, hot-reconfigure result
- 🟢/🟡/🔴 **Certificate** — expiry status of any cert this route depends on (per ADR-0022)

**Throughput**
- 🟢/🟡/🔴 **Latency** — observed pipeline latency vs. configured envelope
- 🟢/🟡/🔴 **Throughput** — observed point rate vs. configured envelope

**Reliability**
- 🟢/🟡/🔴 **Drops** — observed drop rate vs. zero baseline

Each chip's status is deterministic from observable state — rule documented in code, reviewable, not a function of weights.

### Rule 2 — Each chip is independent

The six chips are computed independently. The Drops chip's amber state does NOT influence the Latency chip's state. The Certificate chip's red state does NOT influence the Connection chip's state. Each is a single-dimension status with its own threshold rules.

Independence is the load-bearing property. The operator scanning the row sees which dimension is degraded; the surface never collapses dimensions into each other.

### Rule 3 — Per-chip drill-down on click

Clicking any chip opens a drill-down panel for that dimension showing:

- Current value(s) and threshold(s)
- Trend over the last hour (sparkline)
- Relevant Flight Recorder events (ADR-0021) in the window
- Relevant What Changed entries (ADR-0024) in the window
- Remediation hint (P7 Level 3) — *"Drops > 0 indicates buffer overflow during the sink disconnect window 10:34–10:43. Options: increase buffer capacity, add a redundant sink, investigate broker stability."*

The drill-down composes with the existing diagnostic surfaces — it's a focused view, not new data.

### Rule 4 — Deterministic overall route status

The route surfaces an overall status derived from the chips by a strict rule:

| Status | Derivation rule |
|---|---|
| **Failed** | Any chip is red AND the route is not delivering data (e.g., source disconnect + sink disconnect simultaneously) |
| **Degraded** | Any chip is red OR (multiple chips amber AND data delivery affected) |
| **Warning** | Any chip is amber AND data delivery is unaffected |
| **Healthy** | All six chips green |

The rule is documented in code, reviewable, and produces the same status from the same chip state. It is NOT a weighted sum.

The overall status surfaces in the routes list (column), the Route detail page header, the fleet view (Phase 5), and the route's chip in any cross-reference. The overall status is a deterministic categorical, never a number.

### Rule 5 — No composite scores anywhere — not even hidden, not even "for trending"

The system never computes a composite Health Score, Reliability Index, Quality Number, or any single-number aggregate of the six dimensions. Not visible by default. Not visible in an Advanced panel. Not exposed via API. Not stored as a metric.

Per-dimension metrics are independently trended (Prometheus already does this). Fleet-level aggregation (Phase 5) aggregates each dimension independently — "12 of 47 routes have drops chip amber" — never "fleet score 73."

This is the strictest rule in the ADR because the slippery slope is well-documented. Even one hidden score becomes the score everyone optimises to.

### Rule 6 — Composability with Timeline, Explain-Why, What Changed

The chip drill-downs (Rule 3) link to:

- Route Timeline (ADR-0026) filtered to the chip's dimension
- Explain-Why (ADR-0023) for any Drops chip non-green (drops are verdicts; the walker explains them)
- What Changed (ADR-0024) anchored at the last point this chip was green

This makes the Health Surface the **entry point** to the diagnostic suite — operator sees a chip turn amber, clicks, follows the trail. The chips are the first-class navigation primitive for the diagnostic surfaces.

## Consequences

**Positive:**

- The six chips give the same one-glance affordance a composite score would — "are all chips green?" is as fast as "is the score above 95?"
- The chips never lie. A red Certificate chip cannot be masked by clean Drops.
- The architecture is composable — adding a new dimension (e.g., "Subscription staleness") in a future ADR is a new chip, not a rebalance of score weights
- The "deterministic overall status" (Rule 4) gives management the aggregate they want ("how many routes Healthy?") without scoring math
- The surface is honest under P7 — each chip is explainable, the overall status is derivable by rule, the operator can always trace from chip to evidence

**Negative:**

- Six chips per route + overall status is more pixels than a single number. Mitigation: chips are compact (icon + one-word label); a row of six fits in a typical routes-table row.
- Adding a new dimension requires UI work (new chip slot) plus a new rule. Tractable.
- The "no composite score, ever" discipline will be relitigated periodically as someone proposes "a quick health index for the dashboard." The discipline is in the ADR explicitly so the answer is documented.

**Forbidden patterns:**

- Any UI element showing a number that summarises multiple dimensions of route health
- A "fleet score" / "site score" / "gateway score" at any aggregation level
- A weighted-sum status badge ("Route Score: 87")
- A "smart" overall status that weights some chips more than others — the overall status is a strict rule per Rule 4
- Removing a chip because its dimension is "noisy" — instead, refine the per-chip threshold rule; don't hide the dimension

## Consequences for ADR-AI agents

Future AI agents (Diagnostic Agent, Configuration Agent per Phase 4.5) operate over the structured chips and their drill-downs — they don't introduce composite scores either. An AI agent's summary may say "two routes have drops chip red and one route has cert chip amber" — that's reporting structured observations, not summarising into a score.

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (chip data is collected always; chip rendering is demand-driven per the page-view trigger)
- ADR-0021 — Route Flight Recorder (chip drill-downs link to relevant events)
- ADR-0022 — Certificate Trust Center (Certificate chip is sourced from Trust Center state)
- ADR-0023 — Explain Why Data Is Missing (Drops chip drill-down invokes the walker)
- ADR-0024 — What Changed (chip drill-down anchors at last-green-window)
- ADR-0026 — Route Timeline (chip drill-down filters Timeline to the chip's dimension)
- Platform principle P6 — operational product (one-glance health affordance is the operator gesture)
- Platform principle P7 — surfaces explain outcomes — *this ADR is the canonical worked example of P7's forbidden-composite-scores clause*
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md` — captures the structured-chips vs single-score decision pass
