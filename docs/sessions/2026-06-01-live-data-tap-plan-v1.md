# Live Data Tap — implementation plan v1

**Date:** 2026-06-01
**Status:** Plan v1 — DRAFT for ChatGPT review pass (→ v2). Not yet started.
**Governing locks:** ADR-0018 (Live Data Tap with Compare, 10 rules), ADR-0017
(diagnostic surfaces are demand-driven), platform principle P1 (Runtime Tap is
strictly observational), P4 (preserve the deterministic data path).

## Why now / why it drifted

This is the surface the operator went looking for during the 2026-06-01 incident to
answer "what data comes from the source, what goes to the sink?" — and couldn't
find. It was fully **designed** on 2026-05-30 (ADR-0018 + ADR-0017 + three signed-off
mockups: `docs/sessions/2026-05-30-ux-mockups/{1-tap-stream,2-tap-compare,3-tap-inspect}.html`)
but never built — the MTConnect wizard, diagnostic bundle, and data-delivery fixes
took priority. The design is locked; this plan is purely *how to build it*.

Had it existed, the incident's "MTConnect data not reaching MQTT" question would have
been a 30-second answer ("source captured 19/poll, sink captured 0") instead of a
multi-hour root-cause hunt.

## The design is already locked — what this plan must honour

From ADR-0018 (do not relitigate; these are the build contract):
- **Route is the unit of tap** — capture both ends of one route: source-intake points
  and the points handed to each sink's `PublishAsync` (per-sink on fan-out).
- **Three modes** — Stream (two columns, live), Compare (verdict per source/sink pair),
  Inspect (expand one point's full canonical record).
- **Compare verdicts** join by a stable `correlationId` (`gatewayId + sourceInstanceId
  + tagName + deviceTimestamp`); verdicts ✓ exact / ⚠ transform-altered / ⛔ missing /
  ⛔ extra; 30s join window.
- **Verdict counters** (`tap.compare*`), header rollup line, reset on deactivate.
- **Pause / scroll-back / snapshot-to-JSON** affect render only, never the runtime.

From ADR-0017 (the activation contract — the load-bearing engineering constraint):
- **Off by default, zero per-point cost when off.** No capture, no `correlationId`, no
  serialisation, no ring writes unless someone is watching.
- **Activated by subscriber presence**, reference-counted, O(1) `volatile` hot-path
  check, **bounded in scope** (tapping route A must not capture route B or any sink not
  on A), **bounded lifetime** (deactivate after last subscriber + 60s cooldown),
  **bounded volume** (≤1,000 points/side/sink ring, silent evict-on-full), **sampling**
  above 1,000 pts/sec (reservoir for Stream/Inspect; *correlated* sampling for Compare
  so it doesn't produce false ⛔-missing).
- **Privacy masking at capture time** (Rule 6/7) — sensitive values become `***` before
  they enter the ring; cleartext never lives in a capture buffer or a snapshot export.

## Architecture (proposed — for review)

Two layers, matching where the hot path lives (Core) vs where the operator surface
lives (Management).

### Core — `IRouteTap` capture service (new, `src/ElpisEdgeConnect.Core/Diagnostics/`)

The single owner of activation state + ring buffers, injected into the routing engine.

- **Activation:** `bool IsTapActive(routeId)` = `O(1)` volatile read of a per-route
  subscriber count. `Subscribe(routeId) / Unsubscribe(routeId)` (ref-counted, with the
  60s cooldown timer). Per-route, per-side, per-sink ring buffers materialise on first
  subscribe, release on cooldown-expiry.
- **Capture entry points (the two hot-path hooks):**
  - **Source side** — in `RouteWorker.RunIntakePumpAsync`, after the filter/pipeline,
    *before* enqueue: `if (_tap.IsTapActive(routeId)) _tap.CaptureSource(routeId, batch)`.
  - **Sink side** — in `RouteWorker.RunSinkLoopAsync` (or `SinkPublisher`), at the batch
    handed to `PublishAsync`: `if (_tap.IsTapActive(routeId)) _tap.CaptureSink(routeId,
    sinkId, batch)`.
  - The `IsTapActive` guard is the *only* cost when off (one volatile read per batch, not
    per point). **A hot-path-clean test asserts zero capture work at idle (ADR-0017
    Rule 1 + its enforceable-lint clause).**
- **Capture is fire-and-forget into a bounded ring; never awaits a subscriber, never
  back-pressures** (P1). Evict-on-full is silent.
- **Masking at capture time** (Rule 6): the captured copy has sensitive values replaced
  with `***`. Needs a sensitive-tag policy readable from Core — see Open Question Q3.
- `correlationId` computed only at capture, only when active.

### Management — SSE transport + comparator + Studio page

- **`GET /api/v1/diagnostics/tap/{routeId}`** — SSE stream (`text/event-stream`),
  reusing the existing SSE pattern (BackupApi/ConfigApi/DiagnosticsApi already stream).
  Opening the stream calls `IRouteTap.Subscribe`; closing it (or the Blazor circuit
  `OnDispose`) calls `Unsubscribe`. The stream pushes newly-captured source/sink points
  (already masked) as they land in the ring.
- **Compare comparator** (Management or Core — see Q2): joins source+sink captures by
  `correlationId` over the 30s window, emits verdicts + maintains the `tap.compare*`
  counters. Transform-awareness (⚠ vs ⛔) needs the route's transform definitions — see
  Q4.
- **`Tap.razor`** at `/diagnostics/tap` — mode toggle (Stream / Compare / Inspect),
  two-column / verdict / expand renders per the three mockups, pause / scroll / snapshot,
  the header rollup line, the sampling banner. Mockup-faithful.

## Milestones (mockup-first, plan-trail cadence)

> **M0 is a gate.** Per the standing "any UI gets a static mockup sign-off first" rule —
> the mockups exist but are 1 month old and predate the quarantine/health-surface work.
> M0 re-confirms them before any wiring.

| M | Deliverable | Gate |
|---|---|---|
| **M0** | Re-review the three existing mockups against current Studio; refresh if drifted; operator sign-off. Confirm the header rollup, sampling banner, and snapshot affordance match ADR-0018 Rules 7–9. | **Sign-off** |
| **M1** | Core `IRouteTap` service: activation (ref-count + cooldown), per-route/side/sink bounded rings, `IsTapActive` O(1) guard. Unit tests incl. **hot-path-clean idle test**. No hooks wired yet. | Tests green |
| **M2** | Wire the two hot-path hooks (source-intake + sink-publish) behind `IsTapActive`. `correlationId` at capture. Capture-time masking (Q3). Bounded-volume + reservoir-sampling tests. | Tests green |
| **M3** | SSE endpoint `GET /diagnostics/tap/{routeId}` + subscribe/unsubscribe lifecycle tied to stream open/close. Endpoint tests (active vs idle, multi-subscriber ref-count, cooldown). | Tests green |
| **M4** | `Tap.razor` — **Stream mode** (the MVP that answers "is data flowing?"). Mode toggle scaffold, pause/scroll, sampling banner. | Live verify |
| **M5** | **Inspect mode** — expand one captured point to the full canonical record (masked). Click-through to SourceDetail/SinkDetail (Rule 10). | Live verify |
| **M6** | **Compare mode** — comparator + verdicts + counters + header rollup + snapshot-to-JSON export (masked, Rule 9). The regression-detector payload. | Live verify |
| **M7** | End-to-end live verification (MTConnect/Modbus → MQTT, with a deliberately mis-mapped tag to exercise ⚠ and a stopped sink to exercise ⛔), full test pass, handoff + ADR-0018 status → Accepted. | Done |

**Sequencing rationale:** Stream (M4) is the smallest slice that closes the original
operator pain and de-risks the whole hot-path-hook + SSE spine. Compare (M6) is the
highest-value but heaviest piece (correlator + transform-awareness) and rides on a
proven spine. Inspect (M5) is cheap and slots between.

## Open questions for the review pass (v1 → v2)

- **Q1 — Activation transport.** SSE (matches existing endpoints, simplest) vs SignalR
  (ADR-0017 names both). Lean: **SSE**, since the project already streams SSE and the
  tap is one-directional (runtime → operator). Confirm.
- **Q2 — Where does the comparator live?** Core (closer to capture, reusable by an API
  consumer) vs Management (closer to render, keeps Core lean). Lean: **Management** — the
  comparator is a render/diagnostic concern; Core just captures. Confirm against P1 (Core
  stays a pure data path).
- **Q3 — Capture-time masking source-of-truth.** ADR-0018 Rule 6 cites `gateway.sensitiveTags`
  config + per-tag `IsSensitive` metadata. **Neither appears to exist yet** — the existing
  `ConfigRedactionEngine`/`SecretShapeDetector` mask *config*, not data-point values. Does
  this plan also introduce the sensitive-tag-value policy, or is that a prerequisite
  sub-ADR? Scope decision needed.
- **Q4 — Transform-aware Compare.** ADR-0018 §Consequences flags that ⚠-vs-⛔ accuracy
  needs the route's transforms to describe their field changes (`DescribeFieldChanges`, a
  noted ADR-0015 amendment). For v1, is Compare allowed to ship **transform-naive**
  (any field diff = ⚠, only correlationId presence/absence drives ⛔) and add
  transform-awareness later, or is `DescribeFieldChanges` in-scope now?
- **Q5 — Sampling threshold realism.** ADR-0017 cites 30K/sec (OPC UA). For the launch
  protocols (Modbus/MTConnect/FOCAS at poll cadence) per-route rates are far lower. Ship
  reservoir sampling but is the 1,000 pts/sec trigger the right default, or defer the
  high-rate path until an OPC UA pilot needs it?
- **Q6 — Effort.** ADR-0018 estimates ~1 week. Does the operator want the full 3-mode
  surface, or ship **Stream-only (M0–M4)** first as a fast win and treat Compare/Inspect
  as a follow-up milestone?

## Risks

- **Hot-path safety (highest).** The capture hooks live on the Core data path. The
  `IsTapActive` guard, fire-and-forget capture, and evict-on-full must be provably
  zero-cost at idle and non-blocking when active. Mitigation: the ADR-0017 enforceable
  hot-path-clean test is a first-class deliverable in M1/M2, not an afterthought.
- **Masking correctness (privacy).** A capture path that leaks cleartext sensitive data
  is worse than no tap. Q3 must be resolved before M2.
- **Compare false verdicts** if transforms aren't described (Q4) — manage operator
  expectations via the mode banner ("transform-naive compare") if shipping naive.

## Reference

- ADR-0018 — Live Data Tap with Compare (the 10-rule build contract)
- ADR-0017 — demand-driven diagnostic surfaces (the activation contract)
- Platform principles P1 (observational), P4 (deterministic data path), P6/P7 (operational + explainable)
- Mockups — `docs/sessions/2026-05-30-ux-mockups/{1-tap-stream,2-tap-compare,3-tap-inspect}.html`
- Hooks — `RouteWorker.RunIntakePumpAsync` (source), `SinkPublisher.PublishWithRetryAsync` (sink)
- `docs/sessions/2026-06-01-data-delivery-fixes-handoff.md` — the incident that re-surfaced the need
