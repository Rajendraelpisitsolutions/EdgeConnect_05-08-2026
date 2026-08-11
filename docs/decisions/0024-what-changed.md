# ADR-0024: What Changed — structured diff between last-known-good and current state

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** "It worked yesterday" is the most common industrial support trigger. This ADR specifies a structured-diff surface that answers "what changed since the last point at which this route was healthy?" The diff covers both **configuration changes** (config audit chain already provides) and **runtime state changes** (cert state, broker reachability, license, peer endpoints). Composes with the Last-Known-Good pin (ADR-0025) and Route Timeline (ADR-0026).

## Context

The 2026-05-30 review consensus elevated What Changed alongside Explain Why Data Is Missing as the two highest support-cost-reduction features. The ChatGPT framing:

> *Most support incidents are: "It worked yesterday." The next question is: "What changed?" Imagine: Last Known Good May 28 15:22. Changes since then: ✓ OPC UA certificate changed, ✓ MQTT endpoint changed, ✓ Route Line1 modified, ✓ Tag count increased by 1,200. This may actually solve more problems than the Live Data Tap.*

The foundations already exist:

- Config audit chain (ADR-0009 / 0010) records every config apply with a structured diff
- Reload classifier (ADR-0009) tracks per-entity transitions across applies
- Fault registry tracks adapter / sink fault transitions
- Route Flight Recorder (ADR-0021) records connection / cert / certificate / license events
- License manager tracks license state transitions

The missing piece is the **comparison surface**: given a Last-Known-Good (LKG) timestamp, render the structured diff of everything that changed between LKG and now.

## Decision

The What Changed surface conforms to the following five rules.

### Rule 1 — Diff scope: route, source, sink, gateway-wide

What Changed is renderable at four scopes:

- **Per-route**: changes to this route's config, plus changes to the source/sink/transforms it references
- **Per-source / per-sink**: changes to this entity's config, plus runtime state changes (cert, connection, license entitlement)
- **Gateway-wide**: every change across config + state since the LKG

The same renderer + diff engine drives all four scopes; scope is a filter. Default scope is per-route (the operator's typical entry point is "this specific route stopped working").

### Rule 2 — Diff dimensions

The diff covers six dimensions:

| Dimension | Source | Example diff line |
|---|---|---|
| **Configuration** | Config audit chain | `Route OPCCli2OPCServ.transforms[2].kind: 'passthrough' → 'rename'` |
| **Reload classifier outcome** | ADR-0009/0010 classifier | `Source OPC-UA-Cli: synthesized-recovery (reason: endpoint URL changed)` |
| **Certificate** | Trust Center state (ADR-0022) | `Trusted peer cert with thumbprint AB...EF: added 2026-05-31 14:22:01` |
| **License** | License manager | `License module MQTT-Sink: entitled → unentitled (license expired)` |
| **Connection / sink reachability** | Flight Recorder (ADR-0021) events | `MQTT broker mqtt://prod-01:1883: 3 disconnect/reconnect cycles since LKG` |
| **Throughput envelope** | Metrics counters | `Configured tag count: 487 → 1,704 (+1,217)` |

Each diff line is structured: `{dimension, scope, beforeValue, afterValue, occurredAtUtc, sourceOfTruth}`. Free-text fields are forbidden (mirrors ADR-0021 Rule 3 discipline).

### Rule 3 — Last-Known-Good anchor selection

The diff requires an anchor — the "last point at which this route was healthy." Three anchor sources, in priority order:

1. **Operator-pinned LKG** (per ADR-0025): the operator explicitly marked a config version as "this was working." Default if present.
2. **Last successful Adapter Self-Test** (per Phase C): if Self-Test passed within the last 7 days for this route's source + sink, use that timestamp.
3. **Last contiguous-healthy window**: walk the Flight Recorder backwards from now; find the start of the most recent contiguous window during which all route chips (per ADR-0027) were green. Use that timestamp.

If no anchor can be determined ("no LKG pinned, no Self-Test ever passed, no historically-healthy window in the Flight Recorder ring"), the surface honestly shows: *"No Last-Known-Good anchor available. Showing all changes since gateway startup."* Per P7, an honest blank wins over a wrong answer.

### Rule 4 — Operator-language summary above raw diff

Above the raw diff list, the surface renders an operator-language summary:

```
Since 2026-05-28 15:22 (last known good):

⚠ 3 changes look suspicious for this route:
  • MQTT broker endpoint changed
  • Tag count increased by 1,217 (subscription may exceed limits)
  • Route transform 'rename' added (downstream subscribers may need to re-subscribe)

✓ 2 changes do not affect this route:
  • Brother-HTTP source added (new source; doesn't touch this route)
  • Operator role updated (governance change; no runtime impact)
```

The "suspicious" classification is deterministic — a static rule per dimension says whether a change to that dimension affects the scoped target. Endpoint changes are always suspicious for routes that reference the endpoint. Throughput-envelope expansions are always suspicious. Operator role changes never affect runtime behaviour. The rules are reviewable; no inference.

### Rule 5 — Composability with Timeline and Explain-Why

What Changed produces a flat diff list. Route Timeline (ADR-0026) renders the same dimensions as a chronological timeline. Explain Why (ADR-0023) consumes the same data to walk causality for a specific verdict.

The three surfaces are **three renderings of the same underlying state-change dataset**. ADR-0024 defines the dataset shape; ADR-0023 and ADR-0026 are downstream renderings. The data model is `StateChangeRecord`:

```csharp
public sealed record StateChangeRecord
{
    public required Guid ChangeId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required StateChangeDimension Dimension { get; init; }
    public required StateChangeScope Scope { get; init; }   // route / source / sink / gateway
    public required string EntityId { get; init; }
    public required JsonElement BeforeValue { get; init; }
    public required JsonElement AfterValue { get; init; }
    public required string SourceOfTruth { get; init; }     // "config-audit-chain" | "flight-recorder" | "trust-center" | "license-manager" | "metrics"
}
```

`StateChangeRecord` is the join point. Single source of truth, three surfaces.

## Consequences

**Positive:**

- "It worked yesterday" answers itself — the operator opens What Changed and sees the structured diff before calling support
- Bundle (ADR-0020) includes the What Changed output at bundle-generation time, so support engineers receive a structured "here's what changed" alongside the customer's incident description
- Composes with Last-Known-Good pin (ADR-0025) operationally — the pin is the anchor; What Changed is the rendering
- Timeline (ADR-0026) and Explain Why (ADR-0023) inherit the same `StateChangeRecord` dataset, eliminating divergence between the three explainability surfaces

**Negative:**

- The "suspicious classification" rule table needs to be maintained as new dimensions are added. Reviewable; tractable.
- Backfilling the per-dimension SourceOfTruth uniformity requires touching the Trust Center, License Manager, and Flight Recorder to emit `StateChangeRecord` events. Mechanical, but cross-cutting.
- For long-ago anchors, the Flight Recorder ring may not extend back that far. The surface honestly notes: *"Flight Recorder ring covers 14 days; anchor is 23 days ago. Showing what we can."*

**Forbidden patterns:**

- A free-text "summary" generated by an LLM
- A "smart" diff that hides changes the system thinks are "irrelevant" — every change is shown; suspicious-classification only sorts / highlights, never filters out
- A diff that includes data values (tag-value changes, throughput specifics) — those are per-point and belong to the Live Data Tap; What Changed shows config + state changes only
- Anchors derived from "last time the route had non-zero throughput" — that's not a healthy anchor (a route can throughput-but-be-degraded). Only the three structured anchors in Rule 3 qualify.

## Reference

- ADR-0009 / 0010 — config audit chain + reload classifier (the existing config-diff foundation)
- ADR-0020 — Diagnostic Bundle (includes What Changed output)
- ADR-0021 — Route Flight Recorder (emits `StateChangeRecord` events)
- ADR-0022 — Certificate Trust Center (source of cert-dimension changes)
- ADR-0023 — Explain Why Data Is Missing (downstream rendering of `StateChangeRecord`)
- ADR-0025 — Last-Known-Good Config Pin (anchor source #1)
- ADR-0026 — Route Timeline (downstream rendering of `StateChangeRecord`)
- ADR-0027 — Route Health Surface (anchor source #3 derivation)
- Platform principle P4 — preserve the explainability data path
- Platform principle P7 — surfaces explain outcomes
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md`
