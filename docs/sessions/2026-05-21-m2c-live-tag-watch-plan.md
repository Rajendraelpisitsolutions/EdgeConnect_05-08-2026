# M.2c — Live Tag Watch (v1 plan)

**Status:** v1 — DRAFT, OPEN QUESTIONS BELOW, pending ChatGPT review pass.
**Date:** 2026-05-21
**Branch:** `claude/tender-edison-639a71` (worktree `tender-edison-639a71`)
**Predecessor roadmap context:** [Phase 2 wrap-up roadmap v2](2026-05-21-phase2-wrapup-roadmap-v2.md) §3.6 + [v2.1](2026-05-21-phase2-wrapup-roadmap-v2.1.md) §1.2 + [v2.2](2026-05-21-phase2-wrapup-roadmap-v2.2.md) §1.3 + [v2.3](2026-05-21-phase2-wrapup-roadmap-v2.3.md) §1.2.
**Architectural authority:** [`docs/platform-principles.md`](../platform-principles.md) — **P1 Runtime Tap is strictly observational** governs every implementation choice below. M.2c does NOT propose new platform principles; it extends P1's lock with M.2c-specific implementation bounds.
**Estimated size:** ~2-3 weeks of focused work per the roadmap v2 §3.6 estimate.
**Test baseline:** 2263 passing across the solution today. Target after M.2c: ~2410 with ~+85 new tests (per v2 §4.1).
**Plan-trail discipline (per roadmap v2 §2):** v1 → ChatGPT review → v2 → reality-check → v3 → implementation handoff.

---

## 1. Goal

Deliver an operator-facing **Live Tag Watch** page in Studio that lets a commissioning engineer or factory-floor supervisor select a single configured source (one CNC out of N) and watch a chosen subset of its canonical tags update in real time as the runtime data path emits them. The page surfaces value, quality (Good / Bad / Uncertain), staleness (>2× the source's poll interval), and source-side timestamps. The subsystem is built on a **Runtime Tap** — a strictly observational read-only side-channel over the deterministic runtime data path, governed by P1 of `docs/platform-principles.md`. **Watch sessions** are operator-initiated, browser-bound, and transient.

The operational scenario this serves: an engineer commissioning the 100-CNC customer (per `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md`) needs immediate visual confirmation that a freshly-added Fanuc or Brother source is actually emitting the tags the wizard claimed it would, at the polling cadence configured, with the expected value ranges. They DO NOT need a historian. They DO NOT need a multi-consumer streaming surface. They DO NOT need to scroll back through yesterday's values. They need "is this CNC, at this instant, producing canonical points the way I expect" — and the Runtime Tap delivers exactly that without bending the deterministic pipeline.

---

## 2. Architectural framing — what this is / what this is NOT

### 2.1 The 5 anti-scope locks (LOCKED, verbatim from roadmap v2.2 §1.3)

These are pinned at the top of the plan because they are the single biggest execution risk for M.2c per ChatGPT's review of v2.1. Any implementation choice that drifts toward one of these is a **pause-and-surface** event, not a discretionary decision.

1. **NOT a historian.** Persistence is the Phase 5 historian milestone, not M.2c. The bounded per-tag ring is in-memory only and dies with the Watch session (or sooner, on bounded overflow). No SQLite, no disk, no resume-from-cursor.
2. **NOT a streaming platform.** Multi-consumer fanout, durable subscriptions, replay semantics — all explicitly out. A Watch session is one browser, one source, one filter, one server connection.
3. **NOT a diagnostics bus.** The existing `/diagnostics` page (M.1c.2) surfaces fault registry + audit chain + event timeline; M.2c is per-tag live values for an operator-driven session. The two surfaces share zero state.
4. **NOT an analytics feed.** No aggregation, no statistical summarisation (min/max/avg over windows), no derived signals, no rate-of-change calculations. The page shows the canonical point as the pipeline emitted it; the operator does the visual interpretation.
5. **NOT a support telemetry framework.** Operator-initiated, browser-bound, transient. Closing the browser ends the session. No background telemetry, no cloud upload, no "send to support" path.

If implementation finds itself sliding toward any of these, **stop and report** rather than silently expanding. The right answer is almost always "defer until a dedicated future milestone names that capability."

### 2.2 What this is

A protocol-agnostic, observational side-channel over the canonical pipeline + a thin Studio page that consumes it. Three layers:

- **Core contract** (`ElpisEdgeConnect.Core.Diagnostics.IRuntimeTap`): the per-source publish + subscribe seam over `CanonicalDataPoint`. Lives in Core because canonical points + diagnostics surface live in Core; the contract has no dependency on any protocol module.
- **Host wiring** (`ElpisEdgeConnect.Host.Adapters.SourceSupervisor` edit): one non-blocking `TryWrite` call per emitted point, after the point has already been committed to the routing intake channel. **Zero new responsibilities** for the supervisor — the tap publication is a side-channel emission, not a pipeline step.
- **Management layer** (Server-Sent Events endpoint + Razor page): an SSE endpoint `/api/v1/live-tags` and a `LiveTagWatch.razor` page that subscribes via the browser's native `EventSource`.

### 2.3 Locked architectural invariants (cited from CLAUDE.md §3 + ARCHITECTURE_BLUEPRINT.md Appendix A)

- **Protocol-agnostic Core.** `IRuntimeTap` lives in `ElpisEdgeConnect.Core.Diagnostics`; references no protocol module.
- **Canonical data model.** The tap publishes `CanonicalDataPoint` values, not protocol-shaped DTOs. Operators select tags by canonical tag path; the page never renders raw FOCAS2/Brother/Modbus shapes.
- **Per-adapter isolation.** A slow or stuck Watch session affects only itself; the supervisor's hot loop never blocks on a tap subscriber. The tap publish is non-blocking, dropped-oldest on overflow.
- **No AI in the data path.** Out of M.2c scope but worth restating: future AI agents that want to consume Runtime Tap connect as Watch consumers, not as pipeline taps that influence behaviour.

---

## 3. P1 as the architectural authority

This section cites `docs/platform-principles.md` **P1 — Runtime Tap is strictly observational** directly. M.2c implementation enforces P1 verbatim. It does NOT extend, parallel, or reinterpret P1. M.2c is the first concrete subsystem that exercises P1's lock; future milestones (diagnostics evolution, AI substrate, fleet tooling) will inherit the same authority via the deferred Runtime Tap contract ADR (per roadmap v2.2 §1.2 — written when the second non-M.2c consumer arrives).

### 3.1 The P1 principle (quoted)

> The Runtime Tap subsystem (see roadmap v2 §1 Locked A) is a non-intrusive observation layer over the deterministic runtime data path. **Subscribers can READ; nothing in the runtime path can READ from subscribers.**

### 3.2 P1's 5 enforcement-in-practice rules — how M.2c satisfies each

| # | P1 rule | M.2c implementation satisfies it because |
|---|---|---|
| 1 | Tap publication is **zero-cost when no subscribers exist** (single `if (subscribers > 0)` check) | `RuntimeTap.TryPublish` short-circuits before the `Channel<T>.Writer.TryWrite` call when the subscriber count for the source is zero. No allocation, no copy, no enqueue. |
| 2 | Subscriber backpressure is **isolated** — a slow subscriber drops its own ring-buffer entries before slowing the publisher | Each Watch session owns its own per-tag bounded ring (≤100 values or ≤5 min, whichever is smaller). Overflow drops the oldest entry on the subscriber's ring — never blocks the supervisor. |
| 3 | No data-path code reads any subscriber state. Tap is **publisher-only from the runtime's perspective** | `SourceSupervisor.RunPollLoopAsync` calls `_runtimeTap.TryPublish(sourceId, point)` and never reads anything back. The supervisor has no knowledge of how many subscribers exist, who they are, or what they filter on. |
| 4 | License gating can disable the tap entirely; the data path is unaffected by the gate state | When the `live-tag-watch` license module is absent, the SSE endpoint returns 404 and the supervisor's `TryPublish` is a no-op (the DI binding registers `NullRuntimeTap`). The supervisor's hot loop is byte-identical with vs without the gate. |
| 5 | Replay reproducibility — a recorded session re-played with tap on vs tap off must produce byte-identical canonical points | Deterministic-replay test (test list §11): record a 60-second canonical stream from a mock source with tap on; replay with tap off; assert byte-identical canonical-point sequences. The test fails if any tap code path can mutate a `CanonicalDataPoint`. |

### 3.3 P1's 4 explicit anti-patterns ruled out — M.2c restates them

- A "trace mode" that mutates pipeline behaviour when tap is active — **M.2c forbids any tap-on conditional in pipeline code**. The pipeline does not know whether the tap is on.
- "Adaptive sampling" that throttles based on tap consumer count — **M.2c forbids any subscriber-count read in the supervisor**.
- Any tap-emitted event whose existence affects audit-chain content — **M.2c does not emit to the audit chain. Period.**
- Any sink-delivery decision (retry, drop, requeue) being influenced by tap subscriber state — **M.2c has zero touch points to sink-side code.**

---

## 4. M.2c-specific implementation invariants (extending P1)

Per roadmap v2.1 §C, the Allowed/Forbidden table below is **M.2c implementation invariants that EXTEND P1, not parallel platform principles**. They are concrete bounds derived from P1 + the operational goal in §1. They live here (in the M.2c plan trail) because they are M.2c-specific implementation detail — not platform-wide commitments. The principle-escalation threshold (v2.2 §1.1) governs any future promotion.

### 4.1 Allowed / Forbidden invariants (locked, from roadmap v2 §3.6.2)

| Allowed | Forbidden |
|---|---|
| Observational runtime stream | NO runtime mutation — Runtime Tap is read-only side-channel |
| Bounded retention (last ≤100 values per tag OR last ≤5 min per tag, whichever is smaller) | NO historian semantics — persistence is the Phase 5 historian milestone |
| Transient Watch sessions (operator's browser open + small refresh buffer) | NO durable subscriptions — no resume-from-cursor, no offline queueing |
| Sampled diagnostics (operator-driven, server-side filtering by canonical tag path) | NO replay pipeline — no time-warp, no go-back-and-see, no scrubbing |
| Per-route + per-source introspection | NO cross-route orchestration — no joining streams, no cross-route transforms |
| Performance budget: ≤1% CPU overhead at 100-CNC scale (~540 pts/sec/gateway) | NO write-back to data path |
| Read-only side-channel via `Channel<T>` with bounded buffer, dropped-oldest on overflow | NO blocking the supervisor's hot loop |

### 4.2 Retention bounds (locked)

- **Per-tag ring depth:** ≤100 values OR ≤5 minutes of wall-clock, whichever is smaller. A tag emitting at 0.33 Hz (3s poll) saturates the 5-minute bound at ~100 samples — the two limits converge by design at the customer's polling cadence.
- **Per-session memory ceiling:** if the operator filters on 30 tags simultaneously, that's 30 × 100 = 3000 `CanonicalDataPoint` references in the session ring. At ~200 bytes per point this is ~600 KB per Watch session — well within reasonable budget.
- **Eviction policy:** dropped-oldest. The session ring is not lossy in any other sense — when a point arrives and the ring is at capacity, the oldest entry is evicted to make room. No sampling, no decimation, no aggregation.

### 4.3 Subscription scope (locked)

- **One source per Watch session.** Multi-source streams roll into a hypothetical future milestone (likely "operational intelligence layer" per roadmap v2 §4.7, working name only). M.2c v1 hard-locks single source per SSE connection.
- **Multi-tag within the source.** Operator selects N canonical tag paths (typically 5-30); the SSE stream emits only points matching one of those paths.
- **Server-side filtering.** The supervisor's `TryPublish` is unfiltered — every point flows into the tap. The tap fan-out applies the per-subscriber filter before enqueuing into the subscriber's ring. Server-side filtering is critical at 100-CNC scale (otherwise a single subscriber watching 5 tags would receive ~540 pts/sec of useless data over SSE).
- **Transient.** Session lifecycle = browser lifecycle. No persistent state. No reconnect-and-resume. If the browser disconnects, the session is gone; the operator opens a fresh session and starts over.

### 4.4 Performance budget (locked, measurement methodology in §8)

- **Target:** ≤1% CPU overhead at 100-CNC scale, where "100-CNC scale" is defined per roadmap v2 §3.6.2 as ~540 pts/sec/gateway (100 sources × 3s poll × ~16 emitted tags/poll on average).
- **Baseline reference:** measured against a no-tap (NullRuntimeTap) baseline. The "overhead" is the delta between tap-on with zero subscribers and tap-off.
- **Subscriber-active overhead:** budgeted but not capped at 1% — a Watch session with 30 tags subscribed should add measurable but bounded overhead. Acceptance bar set in §8.
- **Allocation budget:** zero-allocation per `TryPublish` call in the no-subscriber case; bounded allocation per published point in the subscriber-active case (channel write + filter check + ring enqueue).

---

## 5. Locked inputs from the wrap-up roadmap

Per roadmap v2 §3.6.3, the following design choices are **LOCKED** and are not relitigated in v1. They are restated here for traceability; reality-check during v3 confirms the wiring details but does not reopen the decisions.

| Q (v1 → v2 decision) | Locked verdict | M.2c consequence |
|---|---|---|
| Q1 — Subscription model | **Server-Sent Events (SSE).** Native browser `EventSource`, in-process, no WebSocket library. | `LiveTagsApi.cs` maps `GET /api/v1/live-tags` returning `text/event-stream`. Studio uses native JS `EventSource`. |
| Q2 — History buffer location | **Per-source supervisor + bounded ring** (last 100 values OR last 5 min per tag, whichever is smaller). | Per-subscriber bounded ring lives inside `RuntimeTap`'s subscriber registry. The supervisor itself does not retain — it publishes and forgets. |
| Q3 — Tap mechanism | **`Channel<CanonicalDataPoint>` per subscriber** with bounded buffer + dropped-oldest on overflow. Non-blocking `TryWrite`. | `RuntimeTap` constructs one `Channel.CreateBounded<CanonicalDataPoint>(...)` per Watch session with `FullMode = DropOldest`. |
| Q4 — UI scope | Single source per session; multi-tag with operator-selected filter; stale indicator (>2× poll interval); quality indicator (Good/Bad/Uncertain). | `LiveTagWatch.razor` has a source picker (single-select), tag-path multi-select, value table with two ancillary cells per row (quality, stale). |
| Q5 — Server-side filtering | **Yes** — Studio sends a list of canonical tag paths in the SSE query; supervisor only emits matching points. | SSE query string carries `?source=X&tags=path1,path2,path3`. `RuntimeTap.Subscribe(...)` takes the filter set; non-matching points are dropped before ring enqueue. |
| Q6 — Authentication | **Defer to Phase 4 auth story.** Document the localhost-only posture in M.2c plan + future ADR cross-ref. | `LiveTagsApi.cs` binds to the existing 127.0.0.1:5080 Management address. No auth header check. README + plan trail document the temporary state with explicit Phase 4 cross-ref. |
| Q7 — Historical persistence | **Out for v1.** Phase 5 historian is separate; M.2c's "last 5 minutes" is in-memory only. | No SQLite, no on-disk artifact, no audit-chain emission. Session dies with browser disconnect. |

---

## 6. Deliverables

Adapted from roadmap v2 §3.6.4 with reality-check refinements pending v3.

| File / component | Status | Notes |
|---|---|---|
| `src/ElpisEdgeConnect.Core/Diagnostics/IRuntimeTap.cs` | new | Public contract. `void TryPublish(string sourceInstanceId, CanonicalDataPoint point)` + `IRuntimeTapSubscription Subscribe(string sourceInstanceId, IReadOnlySet<string> tagPaths, ...)`. XML doc cites P1. |
| `src/ElpisEdgeConnect.Core/Diagnostics/IRuntimeTapSubscription.cs` | new | Per-subscription handle. `ChannelReader<CanonicalDataPoint> Reader { get; }` + `IAsyncDisposable` for clean detach. |
| `src/ElpisEdgeConnect.Core/Diagnostics/RuntimeTap.cs` | new | Default implementation. Per-source subscriber registry, per-subscriber bounded channel, server-side filter, no-subscriber short-circuit. Sealed. |
| `src/ElpisEdgeConnect.Core/Diagnostics/NullRuntimeTap.cs` | new | License-gated no-op binding for when `live-tag-watch` module is disabled. `TryPublish` is a literal no-op; `Subscribe` returns a zero-emission subscription. |
| `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` | edit | Inject `IRuntimeTap` via constructor. Add a single non-blocking `_runtimeTap.TryPublish(adapter.InstanceId, point)` call inside `RunPollLoopAsync`'s per-point loop, AFTER the `WriteAsync(point, ct)` to the intake channel succeeds. See §7.1 for the exact injection-point reality-check. |
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs` (or equivalent) | edit | DI registration: `RuntimeTap` when `live-tag-watch` module enabled; `NullRuntimeTap` otherwise. Singleton. |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` | edit | Add `LiveTagWatch = "live-tag-watch"` const. |
| `src/ElpisEdgeConnect.Management/Api/LiveTagsApi.cs` | new | SSE endpoint at `GET /api/v1/live-tags`. Query params: `source` (required, single), `tags` (required, comma-separated canonical paths). Subscribes to `IRuntimeTap`, streams `text/event-stream` until the HTTP response is cancelled. |
| `src/ElpisEdgeConnect.Management/Api/LiveTagEventDto.cs` | new | Wire DTO: `{ tagPath, value, valueType, quality, deviceTimestamp, ingestTimestamp, sequenceNumber }`. JSON-encoded inside each SSE `data:` line. |
| `src/ElpisEdgeConnect.Management/Components/Pages/LiveTagWatch.razor` | new | Operator-facing page. Source picker (single-select from configured sources), tag-path multi-select (driven by per-protocol canonical catalog), value table with quality + stale columns. Auto-reconnect on SSE disconnect with a banner. |
| `src/ElpisEdgeConnect.Management/Components/Pages/LiveTagWatchModel.cs` | new | POCO view-model (per platform-principles P2 — POCO view-model + Razor shell pattern). Holds session state, parses inbound SSE events, computes stale flag per tag. |
| `tests/ElpisEdgeConnect.Core.Tests/Diagnostics/RuntimeTapTests.cs` | new | Unit tests for `RuntimeTap`. See §11 for the test list. |
| `tests/ElpisEdgeConnect.Core.Tests/Diagnostics/RuntimeTapDeterministicReplayTests.cs` | new | The single most important test: tap on vs tap off produces byte-identical canonical-point sequences. Enforces P1 rule 5. |
| `tests/ElpisEdgeConnect.Host.Tests/Adapters/SourceSupervisorTapTests.cs` | new | Supervisor-level: tap is invoked once per emitted point; tap exceptions do NOT propagate (defensive try/catch around `TryPublish`); slow tap subscriber does NOT block supervisor pump. |
| `tests/ElpisEdgeConnect.Management.Tests/Api/LiveTagsApiTests.cs` | new | SSE endpoint integration: source filter, tag filter, disconnect cleanup, keep-alive cadence. |
| `tests/ElpisEdgeConnect.Management.Tests/Components/LiveTagWatchModelTests.cs` | new | POCO view-model tests: stale-flag computation, SSE event parsing, subscribe/unsubscribe lifecycle, source-list refresh. |
| `tests/ElpisEdgeConnect.Benchmarks/RuntimeTapBenchmarks.cs` | new | BenchmarkDotNet benchmark satisfying §8 budget gate. |
| `docs/licensing/module-catalog.md` | edit | Document the `live-tag-watch` module. |

**Test target:** ~+85 tests per roadmap v2 §4.1. Distribution roughly: ~25 RuntimeTap core, ~10 supervisor-level, ~15 SSE endpoint, ~25 page model, ~10 benchmark + replay-determinism gate tests.

---

## 7. SourceSupervisor injection-point reality-check

This section addresses roadmap v2 §5.2 **Q24 — Does `SourceSupervisor.RunSourceLoopAsync` have a clean injection point for `TryWrite` to RuntimeTap without restructuring the loop?**

### 7.1 The injection point (identified during v1 scan)

I read `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` at HEAD. The supervisor's poll loop (`RunPollLoopAsync`, lines 533-615) has a natural injection point at **line 601-602**, after the `foreach (var point in batch) { await sup.Channel.Writer.WriteAsync(point, ct); }` completes successfully but BEFORE `_healthSink.RecordSourceObservation(...)` at line 603.

Existing code (lines 591-608, summarised):

```csharp
foreach (var point in batch)
{
    try
    {
        await sup.Channel.Writer.WriteAsync(point, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        return;
    }
}

_healthSink.RecordSourceObservation(routeId, adapter.InstanceId, adapter.ProtocolName, batch.Count, DateTime.UtcNow);
```

Proposed edit (per-point, BEFORE the health-sink batch observation):

```csharp
foreach (var point in batch)
{
    try
    {
        await sup.Channel.Writer.WriteAsync(point, ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        return;
    }

    // Non-blocking observational publish to the Runtime Tap. P1 §enforcement #2:
    // a slow Watch subscriber drops its OWN ring entries; the publisher never
    // blocks. P1 §enforcement #1: zero-cost when no subscribers exist.
    _runtimeTap.TryPublish(adapter.InstanceId, point);
}
```

**Why this location is correct:**

1. The point has already been committed to the intake channel — the data path is unaffected by anything the tap does. P1 rule 5 (deterministic replay) is enforced by construction: the tap call is sequenced AFTER the data-path commit; it cannot mutate what flowed downstream.
2. It's inside the `try { ... } catch (AdapterException) { ... }` outer envelope, so an AdapterException raised by `PollAsync` still flows through the existing failure path — the tap doesn't see those at all.
3. `TryPublish` is non-async (per the `IRuntimeTap` contract); zero await overhead is added to the hot loop.

**What the edit does NOT do:**

- Does NOT add a try/catch around `TryPublish`. Per the `IRuntimeTap` contract, `TryPublish` MUST NOT throw — it swallows internal errors and signals via metrics, never via exceptions. This is enforced by a unit test in `RuntimeTapTests.cs` (test: "publish_when_subscriber_writer_throws_swallows_and_records_metric").
- Does NOT increment any tap-related counter inside the supervisor. The tap is publisher-only from the supervisor's perspective per P1 rule 3.
- Does NOT change the `BoundedChannelFullMode.Wait` semantics of the intake channel. The intake channel remains the routing-engine backpressure surface; the tap is independent.

**Reality-check open in v3:** confirm no in-flight branches add new responsibilities to `RunPollLoopAsync` that change the injection-point geometry before M.2c implementation lands.

### 7.2 Constructor injection

`SourceSupervisor`'s existing constructor takes `IEnumerable<SourceRegistration>`, `ISourceHealthSink`, and `ILogger<SourceSupervisor>`. Add a fourth parameter `IRuntimeTap runtimeTap`. The host composition root already constructs the supervisor explicitly (it's NOT registered as `IHostedService` per the file header); the DI binding adds `IRuntimeTap` as a singleton.

`NullRuntimeTap` is the fallback when the `live-tag-watch` license module is disabled — the supervisor's constructor parameter is non-nullable, so the no-op binding satisfies the contract without conditional logic in the supervisor.

---

## 8. Performance budget — measurement methodology

The ≤1% CPU constraint at 100-CNC scale is the headline performance lock. Without a measurement methodology it's a vibes-based claim. This section defines exactly how M.2c proves the budget.

### 8.1 Benchmark setup

`tests/ElpisEdgeConnect.Benchmarks/RuntimeTapBenchmarks.cs` (new), driven by BenchmarkDotNet (per Phase 1 W1 decision).

**Baseline benchmark (no-tap reference):**

- 100 synthetic source loops; each publishes 5.4 `CanonicalDataPoint` instances per second (matches the per-source poll rate at 100-CNC × 3s poll × ~16 tags/poll = ~540 pts/sec/gateway).
- Supervisor wired with `NullRuntimeTap`. Run for 60 seconds. Measure CPU time + allocation rate.

**Tap-on, zero-subscribers benchmark:**

- Identical setup, supervisor wired with `RuntimeTap` but zero subscribers active.
- Run for 60 seconds. Measure delta vs baseline.
- **Acceptance gate:** delta ≤1% of baseline CPU. Allocation delta MUST be zero (the zero-subscriber short-circuit is the dispatch fast path).

**Tap-on, single-subscriber benchmark:**

- Identical setup, one subscriber filtering on 30 tag paths.
- Run for 60 seconds. Measure delta vs baseline.
- **Acceptance gate:** delta ≤5% of baseline CPU. (Subscriber-active is a heavier path; 5% is the v1 target.)

### 8.2 What the budget proves

- Zero-subscriber overhead validates P1 rule 1 (zero-cost when no subscribers exist).
- Single-subscriber overhead bounds the worst-case operator-driven cost. A 30-tag Watch session is realistic for commissioning use.
- Allocation rate validates the absence of accidental boxing / closure capture in the hot path.

### 8.3 CI integration

Benchmark runs are not part of the standard `dotnet test` gate (BenchmarkDotNet is too slow for that). The Phase 1 baseline benchmarks doc (`docs/benchmarks/phase1-baseline.md`) is the pattern: M.2c adds a `docs/benchmarks/m2c-runtime-tap.md` capture with the measured numbers + the date + the machine the run was taken on. Reality-check confirms the file naming convention in v3.

---

## 9. Step-by-step implementation sequence

This is the order an implementation session should follow. Each step is small enough to test before moving on. Numbered steps; the implementation session checks each off as it lands.

1. **`IRuntimeTap` contract first.** Author `IRuntimeTap.cs` + `IRuntimeTapSubscription.cs` in `ElpisEdgeConnect.Core.Diagnostics`. XML doc cites P1 explicitly. Smallest-possible passing test: contract compiles, no implementation yet.
2. **`NullRuntimeTap` implementation.** Author the no-op binding. `TryPublish` is a literal empty method; `Subscribe` returns a subscription whose `Reader` emits nothing. Unit test: 1000 `TryPublish` calls in a tight loop allocate zero bytes (use BenchmarkDotNet `MemoryDiagnoser`).
3. **`RuntimeTap` core implementation.** Author the real implementation. Per-source subscriber registry. Per-subscriber bounded `Channel<CanonicalDataPoint>` (capacity 100, `BoundedChannelFullMode.DropOldest`, `SingleWriter = false` because the supervisor writes from one thread per source but the same subscriber may listen to multiple sources in a hypothetical future — for v1 we hard-lock single source per session, but the channel options stay flexible). Filter applied at write time. Subscriber-count read short-circuits the per-source publish when no subscribers.
4. **`RuntimeTap` unit tests.** Per §11 test list. The deterministic-replay test (per P1 rule 5) is mandatory — if any other test passes but this fails, M.2c does not ship.
5. **License gating wiring.** Add `LiveTagWatch = "live-tag-watch"` to `LicenseModuleKeys.cs`. DI registration in `CompositionRoot.cs`: `services.AddSingleton<IRuntimeTap>(sp => licenseChecker.HasModule(LicenseModuleKeys.LiveTagWatch) ? new RuntimeTap(...) : new NullRuntimeTap())`. Reality-check in v3 confirms the exact DI registration pattern matches existing license-gate sites (e.g., `Focas2RegistrationExtensions`).
6. **`SourceSupervisor.cs` injection.** Add `IRuntimeTap` constructor parameter; add the single `_runtimeTap.TryPublish(adapter.InstanceId, point)` call at the injection point identified in §7.1. Update existing supervisor tests to pass `NullRuntimeTap` to the constructor.
7. **Supervisor-level tap test.** New test file. Verify: tap is invoked exactly once per emitted point; tap exceptions (if any reach the supervisor — they shouldn't) don't kill the loop; slow tap subscriber doesn't block the supervisor pump (wire a deliberately-blocking fake subscriber; assert pump throughput is unchanged from baseline).
8. **`LiveTagEventDto` wire shape.** Define the JSON DTO. Field set: `tagPath`, `value` (polymorphic — see open question Q-V1.4), `valueType`, `quality`, `deviceTimestamp`, `ingestTimestamp`, `sequenceNumber`. Unit test: round-trip serialise/deserialise.
9. **`LiveTagsApi.cs` SSE endpoint.** Map `GET /api/v1/live-tags`. Parse query params (`source` single, `tags` comma-separated, with validation). Subscribe to `IRuntimeTap`. Stream `text/event-stream` until cancellation. Emit a heartbeat comment line every N seconds (see open question Q-V1.3 for cadence). Unit + integration tests.
10. **SSE keep-alive + disconnect cleanup.** When the HTTP response is cancelled (browser closed, connection lost, timeout), the endpoint disposes the subscription. Per-subscription `IAsyncDisposable.DisposeAsync` removes the subscriber from `RuntimeTap`'s registry — the next `TryPublish` for that source sees one fewer subscriber. Test: open and close 100 connections; assert subscriber registry returns to size zero.
11. **`LiveTagWatchModel` POCO.** Author the view-model (per platform-principles P2: POCO view-model + Razor shell + POCO unit tests; no bUnit). State: source list, selected source, tag catalog (per protocol), selected tags, live event log per tag (bounded), stale flags. Unit tests cover every state transition.
12. **`LiveTagWatch.razor` shell.** Render the picker, the multi-select, the value table. Connect to SSE via JS interop calling `new EventSource(...)`. Forward parsed events to the POCO model. Wire stale-flag computation on a timer (per-tag, recomputed on UI tick at 1 Hz).
13. **Studio navigation.** Add the Live Tag Watch link to the existing Studio navigation chrome. Reality-check in v3 confirms the exact navigation file (likely `MainLayout.razor` or equivalent — need to verify).
14. **Benchmark suite.** Author `RuntimeTapBenchmarks.cs` per §8. Run the three benchmarks; capture results in `docs/benchmarks/m2c-runtime-tap.md`.
15. **Solution-wide regression sweep.** Full `dotnet test --filter "Category!=Flaky"` clean. Zero new warnings. Coverage on `RuntimeTap.cs` ≥85%. Manual smoke through Studio: add a Brother or Mock source, open the Watch page, select a few tags, confirm values update at the expected cadence with the expected quality / stale indicators.

---

## 10. Out of scope (explicit guardrails for v1)

Beyond the 5 anti-scope locks in §2.1 (which apply to the entire subsystem), v1 explicitly excludes:

- **Multi-source per session.** One source per SSE connection. A future enhancement (post-soak, post-customer-feedback) MAY add cross-source Watch sessions; not v1.
- **Authentication.** Defer to Phase 4 auth story per roadmap v2 §3.6.3 Q6. Document the localhost-only posture; cross-ref the future ADR.
- **Recording / export.** No "save these values to a file" button. The Watch session is ephemeral by design.
- **AI integration.** Future AI advisors that want to consume Runtime Tap will subscribe via the same `IRuntimeTap` surface — but that's a Phase 4.5 concern, not M.2c. Roadmap v2 §4.7 captures the convergence note (working name "Operational Intelligence" — per terminology freeze v2.3 §1.2 NOT user-facing yet).
- **Per-tag charts / graphing.** v1 renders values as a table. Trend graphs are future polish.
- **Cross-route view.** No "watch all routes" mode. Per-source only.
- **Heatmaps / aggregation visualisations.** v1 is a table.
- **Modifying source configuration from the Watch page.** Watch is observational; configuration changes go through the existing wizards / draft-apply-rollback flow. No shortcuts.
- **Embedding the Watch in other pages.** M.2d.1 reserves `WizardWatchSlot.razor` for future composition; v1 ships the standalone page only. The wizard slot is a stub.
- **Studio "fleet view" surfaces.** Single-gateway only. The fleet management milestone (post-soak) will compose multi-gateway Watch separately.

---

## 11. Test list (target ~+85 tests)

### 11.1 `RuntimeTapTests.cs` (~25 tests)

- `Publish_WhenNoSubscribers_DoesNothingAndAllocatesZero`
- `Publish_WhenOneSubscriber_DeliversThePointToSubscriberReader`
- `Publish_WhenSubscriberFilterMatches_DeliversThePoint`
- `Publish_WhenSubscriberFilterDoesNotMatch_DropsBeforeChannel`
- `Publish_WhenSubscriberRingIsFull_DropsOldestNotNewest`
- `Publish_NeverThrows_EvenIfChannelWriterFails`
- `Subscribe_AssignsBoundedChannelWithDropOldestPolicy`
- `Subscribe_ReturnsHandleWithReaderProperty`
- `Subscribe_Dispose_RemovesSubscriberFromRegistry`
- `Subscribe_Dispose_IsIdempotent`
- `Subscribe_MultipleSubscribersToSameSource_AllReceiveMatchingPoints`
- `Subscribe_DifferentSourcesIsolated_PublishToAOnlyReachesSubscribersOfA`
- `Subscribe_FilterIsCaseSensitive` (matches canonical tag paths exactly)
- `Subscribe_EmptyFilterSet_RejectsTheSubscription` (we require an explicit filter — open question Q-V1.5 may revise)
- `Publish_DoesNotMutatePointReference` (the same `CanonicalDataPoint` reference flows to subscribers — value type semantics)
- `Publish_SubscriberCountReadIsFastSinglePass` (no per-publish allocation in zero-subscriber case)
- `Subscribe_PerSubscriptionCapacity_BoundedTo100Entries`
- `Subscribe_PerSubscriptionWallClockBound_DropsAfter5Minutes` (deferred: see Q-V1.6 — wall-clock-bounded eviction may be a v1.1 enhancement)
- `Concurrent_PublishAndSubscribe_NoTorn` (stress test, 10 publishers × 10 subscribers, no exceptions, no torn reads)
- ...plus a handful of license-gate + null-arg tests.

### 11.2 `RuntimeTapDeterministicReplayTests.cs` (1 critical test, enforces P1 rule 5)

- `Replay_TapOnVsTapOff_ProducesByteIdenticalCanonicalPoints` — record 60 seconds of canonical points from a mock source with `RuntimeTap` wired in; record 60 seconds with `NullRuntimeTap`; assert the two sequences are byte-identical (including timestamps, sequence numbers, and quality flags).

### 11.3 `SourceSupervisorTapTests.cs` (~10 tests)

- `Supervisor_PublishesEachEmittedPointToTap_ExactlyOnce`
- `Supervisor_DoesNotPublish_WhenAdapterReturnsEmptyBatch`
- `Supervisor_DoesNotPublish_WhenAdapterThrowsBeforeBatch`
- `Supervisor_DoesNotBlock_WhenTapSubscriberIsSlow` (deliberately-blocking fake subscriber)
- `Supervisor_PassesAdapterInstanceIdAsSourceArgToTap`
- `Supervisor_PublishesAfterIntakeChannelCommit` (ordering test)
- `Supervisor_TapPublishExceptionDoesNotKillPollLoop` (defensive — even though `TryPublish` shouldn't throw)
- ... plus the constructor / DI smoke tests.

### 11.4 `LiveTagsApiTests.cs` (~15 tests)

- `Sse_GET_LiveTags_Returns200WithEventStreamContentType`
- `Sse_RequiresSourceQueryParam_Returns400IfMissing`
- `Sse_RequiresTagsQueryParam_Returns400IfMissing`
- `Sse_UnknownSource_Returns404`
- `Sse_EmitsEventDataAsJson_PerPublishedPoint`
- `Sse_EmitsHeartbeatComment_AtConfiguredCadence`
- `Sse_DisconnectsCleanly_OnClientCancellation`
- `Sse_DisposesSubscription_OnDisconnect`
- `Sse_FilterMatching_OnlyEmitsSelectedTags`
- `Sse_LicenseDisabled_Returns404`
- ... plus malformed-tag-path validation tests.

### 11.5 `LiveTagWatchModelTests.cs` (~25 tests)

- `Model_LoadsSourceList_OnInit`
- `Model_FiltersTagCatalog_ByProtocol`
- `Model_AppendsValueToPerTagRing_OnInboundEvent`
- `Model_DropsOldestValue_WhenRingFull`
- `Model_ComputesStaleFlag_When2xPollIntervalExceeded`
- `Model_QualityIndicator_MapsGoodBadUncertain`
- `Model_DisconnectsSse_OnSourceChange`
- `Model_ReconnectsSse_OnTagFilterChange`
- ... plus subscribe / unsubscribe lifecycle, SSE event parsing failures, etc.

### 11.6 `RuntimeTapBenchmarks.cs` (3 benchmarks, gate per §8.1)

- `Baseline_NoTap_540PtsPerSec`
- `TapOn_ZeroSubscribers_540PtsPerSec`
- `TapOn_OneSubscriberThirtyTags_540PtsPerSec`

---

## 12. Open questions (for v2 ratification + v3 reality-check)

### 12.1 Carried from roadmap v2 §5.2 (unchanged)

- **Q24 (LIKELY RESOLVED in v1 §7.1).** Does `SourceSupervisor.RunSourceLoopAsync` have a clean injection point for `TryWrite` to RuntimeTap without restructuring the loop? **v1 verdict: yes, between line 601 (`WriteAsync` to intake channel) and line 603 (`RecordSourceObservation`). v3 reality-check confirms no in-flight branches change the geometry before M.2c lands.**
- **Q25.** What's the SSE keep-alive cadence needed for browsers to not time-out the connection on a quiet topic (operator selected a tag with low update rate)? **v1 recommendation: 15-second heartbeat comment lines (the standard Nginx default for SSE). Reality-check in v3 confirms whether Studio's Kestrel default is sufficient or whether we need an explicit comment-line heartbeat.**

### 12.2 New v1-specific open questions (for ChatGPT review pass)

- **Q-V1.1 — SSE message frame format on the wire.** Standard SSE event syntax is `event: <name>\ndata: <payload>\n\n`. Two options for our wire shape: (a) single `event: tag-value` per emission with the full JSON payload as the `data:` line — every value is one event with the same event name; (b) one event name per tag path (e.g., `event: tag:Status/RunState`) so browser handlers can subscribe per-tag client-side. **Recommendation: (a)** — simpler, lower per-event overhead, the client filters in JS. (b) would prematurely commit to a multi-handler client-side architecture we don't yet need.
- **Q-V1.2 — Per-tag filter precompile vs every-message regex.** The tag-path filter set passed at `Subscribe` time is a list of canonical paths (typically 5-30 strings). Two options: (i) `IReadOnlySet<string>` with `Contains(point.TagPath)` per publish — O(1) hash lookup; (ii) compile a regex / trie once at subscribe time. **Recommendation: (i)** — hash-set membership is fast enough, regex compilation is unnecessary complexity. Wildcards / prefix patterns are out of v1 scope per the 5 anti-scope locks. If a future milestone wants `Status/*`-style filters, that's a contract extension at that time.
- **Q-V1.3 — Heartbeat cadence (sharpens Q25).** SSE specifies that idle connections may be reaped by intermediate proxies after ~30-60 seconds. Studio is localhost-only in v1 (Q6 defers auth and binding to Phase 4), so proxy-related reaping is moot. But the browser's own `EventSource` may consider a long-quiet stream as "stuck." **Recommendation: emit a `: heartbeat` SSE comment line every 15 seconds.** Comment lines are valid SSE syntax that browsers must ignore — they keep the TCP stream warm without surfacing in event handlers. Reality-check in v3.
- **Q-V1.4 — Polymorphic value field on the wire.** `CanonicalDataPoint.Value` is typed via `CanonicalValueType` enum + a discriminated union (int / double / string / bool). The SSE wire shape needs to carry both the value AND the type. **Recommendation: emit `{ "value": <JSON-typed value>, "valueType": "Double" }`** — the value is encoded with its native JSON type (numbers as numbers, strings as strings, booleans as booleans), and the `valueType` field carries the canonical enum for client-side display. Avoid stringifying every value (e.g., "3.14" as a string) — operators expect numeric values to be numeric.
- **Q-V1.5 — Selecting a tag that doesn't exist in the canonical catalog.** Operator filters on `Status/RunState` and `Status/RunStateNoSuchTag`. The tap registry has no idea what tags a source actually emits — it just filters published points against the subscriber filter. So the typo-path never matches and the subscriber just sees fewer values than expected. Two paths: (a) **silently accept the typo path** — the subscriber sees zero events for that path, the operator infers the typo from the table being empty; (b) **validate the filter at subscribe time** by cross-referencing the source's canonical catalog (FOCAS2 / Brother / Modbus tag map) and returning an error for unknown paths. **Recommendation: (b)** — operator UX dramatically better. The Studio page already needs the per-protocol tag catalog to drive the multi-select; the API can re-use the same catalog for validation. Reality-check in v3 to confirm the per-protocol tag-map exposure pattern.
- **Q-V1.6 — Wall-clock-bounded eviction (the "≤5 minutes" half of the retention rule).** The bounded channel naturally enforces the ≤100 entry bound. The ≤5-minute bound requires evicting entries by age — which is not a native bounded-channel feature. Options: (i) wrap the channel with a per-entry timestamp + a background eviction timer; (ii) accept that "≤100 entries" is the effective bound and the ≤5 min half is enforced at the page-rendering layer (the page only displays entries whose `ingestTimestamp` is within the last 5 minutes; older entries are still in the ring but invisible). **Recommendation: (ii) for v1, defer (i) to v1.1.** The simplicity benefit is large; the operator's visible behaviour is identical; the implementation cost is much lower. The 5-minute bound becomes a Studio rendering rule rather than a tap-side invariant.
- **Q-V1.7 — Per-protocol canonical catalog exposure for filter dropdown.** The Studio page needs to render the tag-path multi-select. The FOCAS2 adapter has a `Focas2TagMap` of known paths; Brother HTTP will have `BrotherTagMap` per the M.P2.4 plan; Modbus is operator-authored. **Recommendation: introduce a small `ICanonicalCatalogProvider` per source that returns a list of known canonical paths for that source instance.** v3 reality-check confirms whether such a surface already exists in some form (e.g., via the source configuration DTO that drives the wizard).
- **Q-V1.8 — Reconnect UX.** Browser `EventSource` auto-reconnects on disconnect (default 3-second backoff). Should the Studio page surface a "Reconnecting..." banner during the gap, or silently absorb the brief outage? **Recommendation: surface a subtle banner** (consistent with the premium-UX discipline per M.2b.5/6 v3 §3). The banner clears on next event received. Reality-check in v3 confirms the shared toast/banner primitive to use.

### 12.3 v3 reality-check items (no recommendation needed yet)

- Confirm DI registration site for `IRuntimeTap` (likely `CompositionRoot.cs` near existing license-gated registrations).
- Confirm `LicenseModuleKeys.cs` exact location + naming convention.
- Confirm Studio navigation file (likely `MainLayout.razor` — need to verify).
- Confirm the benchmark docs directory pattern.
- Confirm the `LiveTagWatch.razor` page's route attribute matches the existing Studio routing convention.

---

## 13. Risks and mitigations

| # | Risk | Likelihood | Severity | Mitigation |
|---|---|---|---|---|
| 1 | M.2c quietly expands to historian / streaming / analytics during implementation | Medium | High | §2.1 5 anti-scope locks pinned at top of plan; pause-and-surface discipline |
| 2 | `RuntimeTap.TryPublish` becomes accidentally allocating (closure capture, boxing) and breaks the ≤1% CPU budget | Medium | Medium | BenchmarkDotNet `MemoryDiagnoser` gate in §8; zero-allocation requirement for zero-subscriber path is enforced by test |
| 3 | A slow Watch subscriber accidentally backpressures the supervisor (P1 rule 2 violated) | Low | High | The bounded-channel `DropOldest` policy means `TryWrite` never blocks; supervisor-level test explicitly wires a blocking fake subscriber and asserts pump throughput is unchanged |
| 4 | Replay determinism (P1 rule 5) silently broken by some future supervisor change | Low (now) / Medium (future) | Critical | `RuntimeTapDeterministicReplayTests.cs` runs in CI on every build; the test is named after the invariant so it's easy to recognise when it fails |
| 5 | SSE connection reaping by browser due to long-quiet streams (Q25 / Q-V1.3 unresolved) | Medium | Low | Heartbeat comment lines every 15 seconds; reality-check in v3 |
| 6 | Operator filters on a typo tag-path and sees an empty table with no explanation (Q-V1.5) | High if unaddressed | Medium | Q-V1.5 recommendation: validate filter at subscribe time using per-protocol catalog |
| 7 | License gate's `NullRuntimeTap` introduces a subtle divergence in supervisor behaviour vs the real `RuntimeTap` | Low | Medium | Supervisor-level tests parametrised over both implementations |
| 8 | Wall-clock-bounded eviction (Q-V1.6) deferred to rendering layer causes operator confusion about "what's still here" | Low | Low | Studio page renders only entries within the last 5 minutes; older entries silently invisible |
| 9 | Concurrent publish + subscribe race (stress) corrupts the registry | Low | High | Stress test in §11.1 (`Concurrent_PublishAndSubscribe_NoTorn`) |
| 10 | `LiveTagWatchModel` POCO drifts from the shared interaction primitives (P2) | Medium | Low | Code review against M.2b.6 view-model precedent (`ReloadOutcomePanelModel` / `SourceProtocolPickerModel`) |

---

## 14. Definition of done

- [ ] All 5 anti-scope locks (§2.1) honoured — no historian, no streaming platform, no diagnostics bus, no analytics feed, no support telemetry. Code review confirms.
- [ ] P1 rule 5 (deterministic replay) enforced by `RuntimeTapDeterministicReplayTests.cs`; test green.
- [ ] P1 rule 1 (zero-cost when no subscribers exist) enforced by allocation test; benchmark §8 acceptance gate green.
- [ ] P1 rule 2 (isolated backpressure) enforced by `SourceSupervisorTapTests`; supervisor pump throughput unchanged with a deliberately-slow subscriber.
- [ ] P1 rule 3 (publisher-only from runtime's perspective) enforced by code review — no read of subscriber state in `SourceSupervisor`.
- [ ] P1 rule 4 (license-gateable) enforced by `NullRuntimeTap` registration when `live-tag-watch` module disabled; supervisor's hot loop byte-identical with and without the gate.
- [ ] Performance budget gate green: ≤1% CPU overhead with zero subscribers; ≤5% with one subscriber on 30 tags (per §8).
- [ ] Zero new warnings; `TreatWarningsAsErrors` enforced on the Core project edits.
- [ ] Coverage ≥85% on `RuntimeTap.cs`.
- [ ] Full solution test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"`.
- [ ] Studio Live Tag Watch page end-to-end smoke: select a Mock source, pick 5 tag paths, watch values update for 60 seconds, observe stale + quality indicators.
- [ ] All 8 new v1-specific open questions resolved (Q-V1.1 through Q-V1.8) in v2 or v3.
- [ ] Q24 + Q25 from roadmap v2 §5.2 reality-checked and resolved.
- [ ] License module catalog (`docs/licensing/module-catalog.md`) documents `live-tag-watch`.
- [ ] Benchmark capture in `docs/benchmarks/m2c-runtime-tap.md` (or whatever path v3 reality-check confirms).
- [ ] Plan trail captured: this v1 → review pass → v2 → reality-check → v3 → implementation handoff. All dated files under `docs/sessions/`.
- [ ] Cross-reference: roadmap v2 §3.6 row marked complete with PR link.

---

## 15. Pause-point criteria (stop and report if any of these)

- Implementation finds itself drifting toward any of the 5 anti-scope locks (§2.1).
- Reality-check (v3) reveals `SourceSupervisor.RunPollLoopAsync` has changed in master since v1 such that the injection point in §7.1 no longer exists cleanly.
- Performance budget gate fails: zero-subscriber overhead >1% of baseline or allocation rate non-zero. **Do not ship M.2c with a degraded data path.**
- Replay determinism test fails. This is a P1 violation; code does not ship.
- Per-protocol canonical catalog exposure (Q-V1.7) reveals no existing surface — would force a separate Catalog API milestone before M.2c can land.
- Any of the 5 P1 enforcement rules cannot be enforced by an automated test — pause and design the test before implementing the affected behaviour.

---

## 16. Knock-on / next-session items

After M.2c closes:

- **M.2d.1** (shared wizard primitives) populates `WizardWatchSlot.razor` with a real reference to the M.2c page (currently a stub).
- **EREMOS V2 contract revalidation** can use the Watch page as a visual confirmation during the 7-day soak (operator confirms the gateway is emitting tags as expected without parsing JSON over MQTT).
- **Operational Intelligence layer** (working name only, per terminology freeze v2.3 §1.2) — the deferred ADR per roadmap v2.2 §1.2 gets written when the second non-M.2c consumer of Runtime Tap arrives (likely AI substrate or fleet tooling).
- **Wall-clock-bounded eviction** (Q-V1.6 deferred): if the rendering-layer fallback proves operationally confusing during the customer soak, promote to a v1.1 enhancement.
- **Per-tag charts / graphs** — future polish, not a v1 commitment. Operator feedback during soak will tell us if this is needed.
- **Filter validation surface** (Q-V1.5): if the per-protocol catalog exposure (Q-V1.7) lands cleanly in v1, the same surface can drive validation in M.2d wizards and Chip 3 provisioning template editing.

---

## 17. Cross-references

- **`docs/platform-principles.md`** — P1 Runtime Tap is strictly observational. The single architectural authority for this milestone.
- **Roadmap v2** ([2026-05-21-phase2-wrapup-roadmap-v2.md](2026-05-21-phase2-wrapup-roadmap-v2.md)) §3.6 — original M.2c scope.
- **Roadmap v2.1** ([2026-05-21-phase2-wrapup-roadmap-v2.1.md](2026-05-21-phase2-wrapup-roadmap-v2.1.md)) §1.2 — removed the redundant runtime-observability.md proposal; M.2c plan cites P1 directly.
- **Roadmap v2.2** ([2026-05-21-phase2-wrapup-roadmap-v2.2.md](2026-05-21-phase2-wrapup-roadmap-v2.2.md)) §1.1 (principle-escalation threshold), §1.2 (Runtime Tap deferred ADR), §1.3 (5 anti-scope bullets — pinned in §2.1 here).
- **Roadmap v2.3** ([2026-05-21-phase2-wrapup-roadmap-v2.3.md](2026-05-21-phase2-wrapup-roadmap-v2.3.md)) §1.2 — terminology freeze (Runtime Tap, Watch session, Operational Intelligence as working name only).
- **CLAUDE.md** §3 — locked architectural invariants (protocol-agnostic Core, canonical data model, per-adapter isolation, no AI in data path).
- **ARCHITECTURE_BLUEPRINT.md** Appendix A — locked decisions table.
- **ADR-0011** — Browse Controller management-plane separation; the architectural pattern P1 (Runtime Tap) extends.
- **M.P2.4 Brother HTTP plan** ([2026-05-20-mp24-brother-http-plan.md](2026-05-20-mp24-brother-http-plan.md)) — structural style template for this plan; Brother tag map will be one of the first per-protocol catalogs the Watch page consumes.
- **`docs/sessions/2026-05-20-100-cnc-deployment-readiness.md`** — the operational scenario in §1 (100-CNC commissioning) sources from this doc.

---

**End of v1 draft. Awaiting ChatGPT review pass. Eight new v1-specific open questions (Q-V1.1 through Q-V1.8) + two carried questions from roadmap v2 §5.2 (Q24 already provisionally answered in §7.1; Q25 still open) need verdicts before v2 locks.**
