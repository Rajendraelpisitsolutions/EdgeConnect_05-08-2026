# ADR-0018: Live Data Tap with Compare mode

**Status:** Accepted (2026-06-02) — Stream slice shipped; Compare / Inspect deferred (see below)
**Date:** 2026-05-30 (proposed) · 2026-06-02 (accepted)
**Framing:** Operators need a visceral "what data is actually flowing" surface that complements numeric metrics. The surface MUST show source-side and sink-side data side-by-side. The surface MUST include a Compare mode that automatically tells the operator whether the sink-side data matches the source-side data — turning manual side-by-side inspection into automated regression detection.

## Implementation status (2026-06-02)

The **Stream slice (M0–M4)** is built, tested, and live-verified against a real
gateway (MTConnect + Modbus, balanced source/sink capture over SSE). Shipped:
- `IRouteTap` demand-driven capture service (Rules 1, 4, 5: route is the unit,
  off-by-default O(1) guard, bounded per-side/per-sink rings).
- ADR-0018A value-privacy masking at capture (Rules 6, 9).
- Source (post-filter, **pre-transform**) + per-sink (pre-publish) hooks.
- `GET /api/v1/diagnostics/tap/{routeId}` SSE stream (subscribe-on-open /
  unsubscribe-on-close lifecycle).
- `Tap.razor` Stream mode at `/diagnostics/tap` (Rule 2 Stream; pause per Rule 8).
- Stable `correlationId` carried on every capture (Rule 3 join key) — so Compare
  is unblocked — using `gatewayId|routeId|sourceInstanceId|tagName|deviceTsTicks|
  sequenceNumber` (the sequenceNumber tie-breaker hardens Rule 3 against
  poll-based device-timestamp bursts).

**Deferred follow-ups** (design here remains the contract):
- **Inspect mode (Rule 2)** — v1.1.
- **Compare mode (Rules 2, 3, 7)** — v1.2; ships transform-naive first (verdict
  by observed field diff), with a banner; transform-aware ⚠ ("expected" vs
  "unexpected") needs a transform `DescribeFieldChanges` (Consequences note).
- **True rate-based reservoir sampling (Rule 5)** — at launch (poll-cadence)
  rates the ring rarely truncates; the status surface reports truncation as the
  "you're seeing a recent sample" signal until OPC-UA-scale rates need it.
- **Source/route-scoped sensitive-tag masking** — ADR-0018A defers the scoping
  qualifier; v1 masks by tag-name pattern.

## Context

The multi-protocol pilot debugging session (2026-05-30) ran for hours because every Studio surface looked healthy while zero data flowed end-to-end. The blocker turned out to be runtime bug #8 — `SourceSupervisor` never called `SubscribeAsync` for subscription-mode adapters, so notifications arrived from the OPC stack into the bounded channel and sat there with no consumer. Metrics showed `Running` state; nothing in the system rendered the actual data flowing (or not flowing).

If a Live Data Tap surface had existed, the operator would have opened it, seen "0 records captured on source side, 0 records captured on sink side", known the failure was upstream of the route, and closed the bug in roughly 30 seconds instead of several hours.

Beyond debugging, the same surface is the natural place to **verify transform behaviour** — when a route applies tag-rename, unit-conversion, deadband, or quality-passthrough transforms, the operator can see the before/after on the same screen. Today this requires reading log files line-by-line, which scales poorly.

The operator's session refinement was load-bearing:

> *"If we can add one more option as 'Compare', it should compare what was received and what was sent and tell whether it's matching or not."*

That refinement converts the surface from a visualisation aid (which is what most observation tools are) into a **regression detector**. The operator stops squinting at two columns; the system tells them when a transform is silently lossy.

This surface implements platform principle P1 (Runtime Tap is observational). Activation MUST follow ADR-0017 (demand-driven, off by default, zero-cost at idle).

## Decision

A Live Data Tap surface lands at `/diagnostics/tap` in the Studio (and at `GET /api/v1/diagnostics/tap/{routeId}` as an SSE stream for API consumers). The surface conforms to the following ten rules.

### Rule 1 — Route is the unit of tap, not source or sink alone

A tap captures **both ends of a single route** simultaneously: the points emitted by the source intake, and the points handed to each sink's `PublishAsync`. Per-source or per-sink-alone tap is technically simpler but operationally useless — you can't compare, and you can't verify the pipeline transforms in between.

If a route fans out to multiple sinks, the tap captures the input to each sink independently. The render shows source-side once and sink-side once per sink (small chips in the sink column header).

### Rule 2 — Three render modes

Operators choose between three modes via a toggle group:

| Mode | Render | Use case |
|---|---|---|
| **Stream** | Two columns side-by-side: source-side captures, sink-side captures. Latest at top. Auto-scrolls unless operator pinned. | Live debugging — see the data flow happen in real time |
| **Compare** | Single column per pair: source point, sink point, **match verdict** (✓ exact / ⚠ transform-altered / ⛔ missing on sink / ⛔ extra on sink) | Regression detection — the verdict is the operator-facing value |
| **Inspect** | Click any single captured point to expand the full canonical record (all fields, including masked-as-sensitive shown as `***`) | Field-level investigation when an issue is narrowed |

The mode is operator-selected per visit; it doesn't persist beyond the page session.

### Rule 3 — Compare verdicts are deterministic per transform pair

The Compare mode's verdict mechanism is the load-bearing engineering decision. Each captured source-side point gets a stable `correlationId` (typically derived from `gatewayId + sourceInstanceId + tagName + deviceTimestamp` — stable across the pipeline, doesn't depend on assignment order). The sink-side capture inherits the same `correlationId`. The verdict comparator joins source and sink captures by `correlationId` and asserts:

| Field | Expected after transform |
|---|---|
| `tagName` | May differ if route has tag-rename — verdict ⚠ Transform-altered with diff annotation |
| `value` | May differ if route has unit-conversion / scale / offset — verdict ⚠ Transform-altered with `before → after` |
| `quality` | Should match unless route has quality-overlay transform — ⚠ otherwise |
| `deviceTimestamp` | MUST match — ⛔ otherwise (would indicate a serious pipeline bug) |
| `gatewayTimestamp` | Expected to differ (set later in pipeline) — not compared |
| `metadata` keys | Should match unless route has metadata-augment transform — ⚠ otherwise |

A point present on source but not on sink within the comparison window is ⛔ **missing-on-sink** — almost always a routing/buffer/sink failure.

A point present on sink but not on source within the comparison window is ⛔ **extra-on-sink** — exotic but possible (sink retry replay during pipeline reconfigure, etc.).

The comparison window is bounded — points older than the window are evicted from the join state. Default window: 30 seconds (covers worst-case buffer drain + sink retry).

### Rule 4 — Activation is demand-driven per ADR-0017

The tap surface adds zero per-point overhead when no operator is watching. Specifically:

- The runtime does NOT capture points into ring buffers
- The runtime does NOT compute `correlationId` for points
- The runtime does NOT serialise points for capture transport

When the first subscriber opens the Studio page for a given route, the runtime sets the route's tap flag. Source intake + sink publish hot paths check the flag once per batch (O(1) volatile read). When set, points are captured into bounded ring buffers (source-side + sink-side, per Rule 5).

When the last subscriber disconnects, the runtime clears the tap flag after a 60-second cooldown. Capture stops; ring buffers are released.

### Rule 5 — Bounded capture with sampling at high rate

Each ring buffer holds **at most 1,000 points per side per sink** (4,000 in a fan-out-3 case). When a buffer fills, the oldest point is silently evicted on insert. No log line, no fault.

At rates above 1,000 points/second, the runtime SHOULD sample rather than capture every point. Sampling strategy:

- For Stream / Inspect modes — **reservoir sampling** of N points over the window; the render shows the sample rate as a banner ("showing ~10% — random sample at 30K/sec capture rate")
- For Compare mode — **correlated sampling**: when source-side is sampled, sink-side captures the matching `correlationId` regardless of sampling-policy timing. Without correlated sampling, Compare mode would produce false ⛔ missing-on-sink verdicts due to sampling mismatch.

### Rule 6 — Privacy masking applies at capture time

Per ADR-0017 Rule 7. A point passing through the pipeline that contains a value marked sensitive (`gateway.sensitiveTags` config OR per-tag `IsSensitive` metadata) is captured with the value field replaced by `***`. The diagnostic surface never holds the cleartext.

Credentials in connection blocks (e.g., OPC UA `Credentials.Password`) are never tappable — they don't cross the data path; they live in adapter state.

### Rule 7 — Compare mode emits verdict counters

While Compare mode is active, the runtime increments per-route counters:

- `tap.comparePairsCount` — total source/sink pairs compared
- `tap.compareExactCount` — verdict ✓
- `tap.compareTransformAlteredCount` — verdict ⚠
- `tap.compareMissingOnSinkCount` — verdict ⛔ missing
- `tap.compareExtraOnSinkCount` — verdict ⛔ extra

The render surfaces a header line: **"Last 60s: 18,000 pairs / 17,950 ✓ / 50 ⚠ / 0 ⛔"** so the operator can spot deterioration at a glance without scrolling captures.

The counters reset when the tap is deactivated (per Rule 4). They are not persisted to long-term metrics — Compare mode is a debugging surface, not an SLO surface.

### Rule 8 — Operators can pause / replay within the window

While viewing, an operator can:
- **Pause** capture (stops the stream render; ring buffers continue filling so the operator returns to the latest data on Resume)
- **Scroll back** through the captures already in the buffer
- **Snapshot** the current Compare verdict table as a downloadable JSON file (for filing tickets or attaching to bug reports)

These operations DO NOT alter the runtime — they affect only the render. Per ADR-0017 the runtime doesn't know whether the operator scrolled.

### Rule 9 — Tap export does not bypass privacy

The Snapshot download in Rule 8 inherits the same masking the live render uses. Sensitive fields are written as `***` in the JSON. There is no operator-facing "show real values" toggle; cleartext sensitive data simply isn't in the capture buffers.

### Rule 10 — The surface composes with other diagnostics

Tap captures carry the same canonical `CanonicalDataPoint` shape that pipeline transforms operate on. Operators can:
- Click a captured point → opens the SourceDetail or SinkDetail page filtered to that point's tag
- Click a Compare ⚠ verdict → opens the route's transform-step diagnostics filtered to that transform

The tap is not a closed surface; it's a launchpad into the existing diagnostic plumbing.

## Consequences

**Positive:**

- Operator debugging shortens from hours to seconds for the most common "is data flowing?" question
- Transform regressions become visible by automatic verdict, not manual inspection
- Customer-demo value — visually compelling, immediately understandable
- Architecturally clean — composes with ADR-0017 (demand-driven) + P1 (observational)
- Single surface serves three modes (Stream / Compare / Inspect) without combinatorial code

**Negative:**

- Implementation cost is non-trivial (~1 week realistic) — ring buffers + SSE plumbing + masking + comparator + render
- Compare mode's correlator adds a small per-point hot-path cost while active — minimised by Rule 4 (off by default) and Rule 5 (sampled at high rate)
- Transform-aware Compare requires the route's transform definitions to be inspectable from the comparator — a future route-transform addition that doesn't surface its semantics will produce false ⚠ verdicts. Mitigation: the transform contract gets a `DescribeFieldChanges` method (small ADR-0015 amendment if it lands).

**Out of scope (deliberately):**

- Long-term capture or replay across hours. The tap is bounded to the active window. Long-term capture is a different decision (compliance / audit) and would need a different ADR.
- Programmatic regression alerts based on Compare verdicts. The verdict counters in Rule 7 enable this in a future ADR, but the surface itself doesn't fire alarms.
- Multi-gateway tap aggregation. The tap is per-gateway; a fleet-aware tap is a future decision.

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (this surface conforms)
- Platform principle P1 — observational-only
- Multi-protocol pilot session — `docs/sessions/2026-05-30-opcua-client-wizard-debugging-followups.md`
- ADR-0019 — capability coverage (different surface; this ADR composes with it)
