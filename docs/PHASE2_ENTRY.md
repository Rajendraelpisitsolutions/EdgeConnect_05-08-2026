# Phase 2 Entry Document

**Baseline:** `v0.1.0-phase1` (`342c6bb`)
**Phase 1 exit record:** `docs/phase1-exit/phase1-exit-checklist.md`
**Date:** 2026-04-09

---

## What Phase 1 delivered

- **ElpisEdgeConnect.Core** — protocol-agnostic runtime: canonical model, adapter contracts, error taxonomy, config manager (draft/validate/apply/rollback), license manager (RSA-signed offline), transform pipeline (4 steps), InMemory + SQLite store-and-forward buffers, routing engine (fanout, retry, replay, lifecycle, backpressure), diagnostics collector (single state store, 5 typed seams, observable Prometheus-shaped metrics)
- **ElpisEdgeConnect.Host** — Windows service skeleton with locked startup/shutdown ordering, source + sink supervisors, RouteDefinitionFactory, health/readiness/metrics HTTP endpoints (HttpListener, zero NuGet deps)
- **ElpisEdgeConnect.MockAdapters** — production-shaped ISourceAdapter/ISinkAdapter with deterministic failure injection (TCS gates, SemaphoreSlim throttles)
- **13 integration scenarios** — plan's verbatim D3 names, real host composition, mock adapters
- **Benchmark baseline** — 15/17 measured, 2 deferred to production hardware
- **Leak harness** — 4-hour kickoff verified post-D10 fix
- **858 tests**, 0 warnings, 0 errors, `TreatWarningsAsErrors=true`

---

## Carry-forward from Phase 1

### Must-fix (inherited defects)

| # | Item | Source | Severity | Status |
|---:|---|---|---|---|
| 1 | **Disposal-order NRE** — `SqliteConnection.RemoveCommand` throws NRE during `DisposeAsync` when sink loops race with connection teardown | D8 post-fix kickoff | Minor (teardown only) | ✅ **CLOSED** — fixed in `3d32fbc` (three-layer fix across `SqliteBuffer.DequeueBatchAsync` + `RouteWorker` + `RoutingEngine`); regression tests `SqliteBufferDisposalRaceTests` pin both racing and post-dispose paths. |
| 2 | **Harness throughput-target clarification** — criterion 5 threshold was corrected from 27.36M (InMemory) to 19.15M (SqliteBuffer); future soak runs should specify the buffer mode explicitly | D10 kickoff summary | Config (harness) | ✅ **CLOSED** — leak harness now takes an explicit `--buffer-mode` flag (defaults to `store-and-forward`), prints the per-mode sustained-throughput ceiling on start-up, and wires the mode into the route config so it's never inherited from the `RouteConfig.Buffer` default. |

### Deferred benchmarks (2)

| Benchmark | Why deferred | Evidence location |
|---|---|---|
| `SqliteBuffer_Replay_MultiSinkCursors` | Production-target measurement (90s multi-sink replay with p50/p95/p99) | `docs/benchmarks/phase1-baseline.md` §"Remaining benchmark debt" |
| `SqliteBuffer_RecoveryTime_1HourBacklog` | Production-target measurement (18M-point cold-start recovery) | same |

### C4 minor findings (8)

| # | Finding | Disposition | Status |
|---:|---|---|---|
| 1.A | `DiagnosticsCollectorOptions.Validate()` paramName misleading | Polish | ✅ **CLOSED** — validator now throws with the exact failing property as `ParamName`; regression tests in `DiagnosticsCollectorOptionsTests`. |
| 1.B | `SourceHealthSnapshot.State` non-nullable default | Doc or nullable | ✅ **CLOSED** — type-level remarks document the "snapshot never materializes until the supervisor pushes" invariant, so `State` is never defaulted in practice. |
| 1.C | `SinkEventEntry.Kind` stringly-typed | Could be enum | ✅ **CLOSED** — new `SinkEventKind` enum; producers + tests updated. |
| 2.A | `RecordStep` first/last-step rollup assumes declaration order | Document | ✅ **CLOSED** — `RecordStep` XML remarks spell out the "first-invoked wins its slot forever" contract. |
| 2.B | `suppressedCount` parameter silently dropped | Add to `TransformStepStats` | ✅ **CLOSED** — plumbed through to `TransformStepStats.PointsSuppressed` (default 0, backward-compatible). |
| 2.C | `EnsureSource` silently replaces state on id change | Acceptable | ⚪ Accepted as documented — no action. |
| 2.D | `RecordSourceState` doesn't clear prior `LastError` | Acceptable | ⚪ Accepted as documented — no action. |
| 3.A | N×M snapshot allocation per Prometheus scrape | Perf optimization | 🟡 **Deferred** — exercise only under Prometheus-scrape load; revisit after Phase 3 if scrapes become a hotspot. |

### Other carry-forward

- **7-day continuous soak** — v1 ship gate, not Phase 1. Schedule early in Phase 2. Harness now has an explicit `--buffer-mode` flag so the mode under test is pinned at start-up.
- **`coverlet` coverage measurement** — ✅ **baseline captured** at `docs/benchmarks/coverage-baseline.md` (89.56 % line / 82.90 % branch on Core).
- **Full external code review** for smaller milestones (A1–B3, D2–D8) — self-reviewed only
- **3 Phase 2 docs** — adapter SDK guide, configuration authoring guide, ops runbook
- **Blueprint §15 open questions** — final sweep not done
- **1 quarantined test** — `RoutingEngine_EndToEnd_5kPtsPerSec_30sOutage_ZeroLossAndOrdered` (wall-clock throughput); deterministic companion covers the same invariants
- **MQTT tests category tag** — ✅ `MqttRawSmokeTest` and `MqttSinkAdapterTests` now carry `[Trait("Category", "RequiresMqttBroker")]` so environments without Mosquitto can filter them out cleanly via `dotnet test --filter "Category!=RequiresMqttBroker"`.

### Flaky-test policy (inherited, documented)

- Gate filter: `dotnet test --filter "Category!=Flaky"`
- Integration test classes serialized via `RoutingIntegrationCollection`
- Disposition doc: `docs/phase1-exit/flaky-tests-disposition.md`

---

## Phase 2 scope (from `CLAUDE.md` §8)

> Phase 2: Migrate existing FOCAS2, MT-LINKi, MTConnect, Brother HTTP, MQTT into new architecture

This means taking the legacy code in `src/ElpisEdgeConnect/` (the original FanucCncDataBridge migration) and refactoring each protocol module into the new `ISourceAdapter` / `ISinkAdapter` shape against the Phase 1 Core contracts.

---

## Recommended Phase 2 startup sequence

### Step 0 — consolidation (this document + immediate cleanup)

- [x] Tag `v0.1.0-phase1` — done
- [x] Freeze Phase 1 docs — this document
- [x] Create Phase 2 entry doc — this document
- [ ] Fix the disposal-order NRE (known, reproducible, 1–2 hours)
- [ ] Schedule the 7-day soak run (harness is ready; just needs wall-clock time)

### Step 1 — first adapter track

Pick the commercially closest adapter. Likely candidates:

| Adapter | Type | Complexity | Commercial urgency | Status |
|---|---|---|---|---|
| **MQTT sink** | Sink (push) | Low — well-understood protocol, existing code to migrate | High — every deployment needs MQTT | ✅ **DONE** |
| **FOCAS2 source** | Source (polling) | Medium — Fanuc library binding, CNC-specific tag mapping | High — core CNC connectivity | ✅ **DONE** (pending real-CNC pilot) |
| **MTConnect source** | Source (polling) | Medium — HTTP + XML parse, multi-vendor | Medium | ✅ **DONE** (pending real-Agent pilot) |
| **MT-LINKi source** | Source (polling) | Low — HTTP-based, simpler than FOCAS2 | Medium | ⚪ Deferred |
| **Brother HTTP source** | Source (polling) | Low — HTTP JSON, narrow device family | Lower | ⚪ Deferred |

**Recommendation:** MQTT sink first (it's the simplest adapter to migrate and every deployment needs it), then FOCAS2 source (the core CNC use case).

### Step 2 — parallel tracks

While adapters are being migrated:
- Run the 7-day soak in background
- Capture the 2 deferred benchmarks on production hardware
- Write the adapter SDK guide (needed before Phase 3 opens adapter development to others)

---

## Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-04-09 | Phase 1 closed at `v0.1.0-phase1` | Exit checklist passed; 4-hour kickoff green post-D10 fix |
| — | Phase 2 first adapter: TBD | Awaiting commercial priority input |
| — | 7-day soak: scheduled for TBD | Harness ready; wall-clock commitment needed |
