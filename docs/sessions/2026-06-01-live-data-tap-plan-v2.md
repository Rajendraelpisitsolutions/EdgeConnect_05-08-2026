# Live Data Tap — implementation plan v2 (LOCKED for build)

**Date:** 2026-06-01
**Status:** Plan v2 — incorporates the ChatGPT review of v1. **Ready to build, starting
with Live Tap v1 (Stream).**
**Supersedes:** `2026-06-01-live-data-tap-plan-v1.md`
**Governing locks:** ADR-0018 (10 rules), ADR-0017 (demand-driven), P1 (observational),
P4 (deterministic data path). New sub-decision this plan: **ADR-0018A — Tap Value
Privacy Policy** (authored as the first deliverable of M1.5; spec inline below).

## What changed from v1 (review verdicts folded in)

| Topic | v1 | v2 (locked) |
|---|---|---|
| First release | M0–M7 implied together | **Stream-only (M0–M4 + M1.5)**; Inspect (v1.1) and Compare (v1.2) follow |
| Transport | SSE (lean) | **SSE** — confirmed |
| Comparator location | Management (lean) | **Management** — confirmed. Core captures, Management compares |
| Capture-time masking | open question | **Hard prerequisite. New M1.5 + ADR-0018A. No capture hook (M2) until the value-privacy policy exists** |
| **Source tap point** | "after filter/pipeline, before enqueue" | **CORRECTED → post-filter, PRE-transform.** Capturing post-transform would gut Compare's value |
| Sink tap point | sink PublishAsync input | **Immediately before `PublishAsync`** — confirmed |
| Transform-aware Compare | open | **Deferred.** v1.2 Compare is transform-naive with a banner |
| Sampling | open | Bounded ring + evict-oldest + basic sample flag + banner for Stream; **correlated sampling deferred to Compare (v1.2)** |
| correlationId | `gw+src+tag+deviceTs` | **+ routeId + per-point sequence tie-breaker** (burst-collision fix) |
| Tests | generic | **Route-isolation + sink fan-out + hot-path-clean idle** are explicit required deliverables |
| Snapshot export | masked JSON | masked **+ redaction metadata** (`redacted`, `redactionReason`) |

## Locked decisions (the build contract)

| Topic | Decision |
|---|---|
| First release | **Stream-only** |
| Transport | SSE (SignalR only if future client→server live commands appear) |
| Comparator | Management (Core captures; Management joins/compares/exports) |
| Masking | Required sub-design (ADR-0018A) before any capture hook |
| **Source tap point** | **Post-filter, pre-transform** (route intake `scratch`, before `pipeline.Execute`) |
| Sink tap point | Immediately before `ISinkAdapter.PublishAsync` |
| Compare | Transform-naive initially, with banner |
| Sampling | Basic (ring + evict + flag + banner) for Stream; correlated sampling lands with Compare |
| correlationId | `gatewayId + routeId + sourceInstanceId + tagName + deviceTimestamp + sequenceNumber` |
| Required tests | route-isolation, sink fan-out, hot-path-clean-at-idle, masking, snapshot-redaction-metadata |

## The source tap-point correction (most important architectural decision)

`RouteWorker.RunIntakePumpAsync` today does, per batch:

```
read from source channel → apply route Filter → scratch        (A) post-filter, pre-transform
  → pipeline.Execute(scratch) → toEnqueue                       (B) post-transform
  → backpressure decide → buffer.EnqueueAsync(toEnqueue)
```

- **Source-side capture = (A) `scratch`** — post-filter, **pre-transform**. This is "what
  the route ingested from the adapter," before any rename/scale/deadband. Capturing at
  (B) would make Compare blind to the transform (source and sink would already match),
  destroying its entire purpose. **Pre-transform is mandatory for Compare to be useful.**
- **Why post-FILTER, not pre-filter** (a refinement the review left open, surfaced here
  to be precise): the route Filter is the route declaring "I don't want these tags."
  Capturing *pre*-filter would show points the route intentionally excluded and produce
  confusing false ⛔-missing verdicts in Compare (a filtered point legitimately never
  reaches the sink). Post-filter = "every captured source point *should* reach the sink
  (modulo drop/quarantine/buffer lag)," which is exactly the invariant Compare checks.
  *Deferred:* a future "filtered-out count" indicator can satisfy the separate "is my
  filter dropping data?" debugging need without polluting the source/sink Compare.
- **Sink-side capture = the batch handed to `PublishAsync`** — per sink, on fan-out.

## correlationId (burst-collision fix)

```
correlationId = gatewayId + routeId + sourceInstanceId + tagName
              + deviceTimestamp + sequenceNumber
```

The review correctly flagged that poll-based adapters can emit multiple points with the
**same `deviceTimestamp`**, so `gw+src+tag+deviceTs` alone collides under bursts and
Compare would pair the wrong points. `CanonicalDataPoint.SequenceNumber` (assigned by
`CanonicalDataPointFactory`, monotonic per source instance, and **preserved through the
pipeline and buffer**) is the natural tie-breaker — it already rides on the point and
survives to the sink. `routeId` is added so two routes off the same source don't cross-
correlate. (If a future transform is found to rewrite `SequenceNumber`, fall back to a
capture-assigned monotonic id — noted, not expected.)

## ADR-0018A — Tap Value Privacy Policy (M1.5 deliverable; spec)

**Blocker per review: no capture hook ships until this exists.** The config redaction
engine masks *configuration*; it must NOT be reused for live values. Minimal v1 model:

**Config shape** (`gateway.sensitiveTags`, new):
```
sensitiveTags:
  - exact tag name            e.g.  "recipe/secret_setpoint"
  - glob pattern              e.g.  "recipe/*"
  - routeId/sourceId/tag scope (optional qualifier)
```

**Capture rule (applied at capture time, in Core, before the point enters any ring):**
```
if tag matches sensitive policy:
    value     -> "***"
    valueType -> unchanged
    quality   -> unchanged
    timestamps-> unchanged
    metadata  -> sensitive metadata keys masked the same way (policy-scoped)
```

**No value heuristics for live process data in v1.** OT-value sensitivity is domain-
specific; entropy/shape guessing creates false positives and false trust. Explicit
allowlist only. (The `SecretShapeDetector` heuristic stays a *config-only* tool.)

**Snapshot export** carries redaction metadata so support can interpret it safely:
```json
{ "value": "***", "redacted": true, "redactionReason": "sensitiveTagPolicy" }
```

**Tests:** policy match (exact/glob/scoped), capture-time masking (value `***`, type/
quality/timestamps intact), snapshot-export masking + metadata, and a negative test
(non-sensitive tag passes through clear).

## Release slices & milestones

### Live Tap v1 — Stream (the immediate operator need)

| M | Deliverable | Gate |
|---|---|---|
| **M0** | Re-review the three mockups vs current Studio; refresh drift; operator sign-off. Confirm header rollup, sampling banner, snapshot affordance. | **Sign-off** |
| **M1** | Core `IRouteTap`: ref-counted activation + 60s cooldown, `IsTapActive(routeId)` O(1) volatile read, per-route/side/**per-sink** bounded rings (≤1,000/side/sink, silent evict). Unit tests incl. **hot-path-clean-at-idle** + **route-isolation (A active ⇒ B not captured)**. No hooks wired. | Tests green |
| **M1.5** | **ADR-0018A value-privacy policy** + `gateway.sensitiveTags` config + capture-time masking + masking tests. **Prerequisite — blocks M2.** | Tests green |
| **M2** | Wire the two hooks behind `IsTapActive`: source = `scratch` (post-filter, pre-transform); sink = before `PublishAsync` (per sink). `correlationId` at capture (with masking applied). Bounded-volume, basic-sampling-flag, and **sink fan-out (per-sink rings)** tests. | Tests green |
| **M3** | SSE `GET /api/v1/diagnostics/tap/{routeId}`. Stream open → `Subscribe`; close / Blazor circuit `OnDispose` → `Unsubscribe`. Endpoint tests: idle-vs-active, multi-subscriber ref-count, cooldown deactivation. | Tests green |
| **M4** | `Tap.razor` @ `/diagnostics/tap` — **Stream mode**: two-column source/sink, latest-on-top, auto-scroll-unless-pinned, pause, scroll-back, sampling banner, route picker, mode-toggle scaffold (Inspect/Compare disabled/"coming soon"). | **Live verify** |

**Live verify M4:** MTConnect/Modbus → MQTT; open the tap; see source captures climb and
sink captures climb; stop the sink and watch sink-side go quiet while source-side
continues (the exact incident signature). Confirm **zero capture when the page is
closed** (hot-path-clean assertion holds live).

### Live Tap v1.1 — Inspect

| M | Deliverable | Gate |
|---|---|---|
| **M5** | Inspect mode: click a captured point → expand full `CanonicalDataPoint` (sensitive fields `***`). Click-through to SourceDetail/SinkDetail filtered to the tag (ADR-0018 Rule 10). | Live verify |

### Live Tap v1.2 — Compare

| M | Deliverable | Gate |
|---|---|---|
| **M6** | Management comparator: join by `correlationId` over 30s window; verdicts ✓/⚠/⛔-missing/⛔-extra (**transform-naive**); `tap.compare*` counters + header rollup; **correlated sampling**; snapshot-to-JSON export (masked + redaction metadata). Transform-naive banner. | Live verify |
| **M7** | Full end-to-end verify (deliberate mis-mapped tag → ⚠; stopped sink → ⛔-missing), full test pass, handoff, ADR-0018 status → Accepted. | Done |

**Transform-naive banner (M6):**
> *Compare shows observed differences. It does not yet verify whether a configured
> transform intentionally caused the difference.*

## Required tests (called out so they aren't an afterthought)

1. **Hot-path-clean at idle** (ADR-0017 Rule 1 lint) — with no subscriber, capture does
   zero per-point work; only the `IsTapActive` volatile read runs.
2. **Route-isolation** — tap route A active, route B running → only A captures.
3. **Sink fan-out** — route with sinks X+Y → one source ring, separate sink ring per X and
   Y; a source point correlates to one capture per sink.
4. **Masking** — sensitive tag value `***` at capture; type/quality/timestamps intact;
   snapshot carries `redacted/redactionReason`; non-sensitive passes clear.
5. **Activation lifecycle** — multi-subscriber ref-count; deactivate after last + 60s
   cooldown; bounded ring evicts oldest silently.

## Risks (unchanged priority order)

1. **Hot-path safety** — the capture hooks are on the Core data path. `IsTapActive` guard
   + fire-and-forget + evict-on-full must be provably zero-cost at idle and non-blocking
   when active. The hot-path-clean test is a first-class M1 deliverable.
2. **Masking correctness** — a leak is worse than no tap. M1.5 gates M2 by construction.
3. **Compare false verdicts** — managed by the transform-naive banner until
   `DescribeFieldChanges` lands.

## Reference

- ADR-0018 (build contract), ADR-0017 (activation contract), ADR-0018A (M1.5, this plan)
- P1 / P4 / P6 / P7
- Mockups — `docs/sessions/2026-05-30-ux-mockups/{1-tap-stream,2-tap-compare,3-tap-inspect}.html`
- Hooks — `RouteWorker.RunIntakePumpAsync` (`scratch`, post-filter pre-transform) /
  `SinkPublisher` before `PublishAsync`
- Plan v1 + this review — `2026-06-01-live-data-tap-plan-v1.md`
- Incident — `2026-06-01-data-delivery-fixes-handoff.md`
