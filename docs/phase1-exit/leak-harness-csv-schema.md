# Leak Harness — CSV Schema & Usage

**Project:** `tests/ElpisEdgeConnect.LeakHarness/`
**Milestone:** D — phase 8
**Reference:** `PHASE1_EXECUTION_PLAN.md` Milestone D5

## Purpose

Long-running soak-test driver that boots a real EdgeConnect host via the production composition root, drives two mock sources at ~1 000 pts/sec each (a realistic CNC rate), and samples memory / GC / diagnostics counters on a fixed interval into a CSV file.

Two runtime modes:

| Mode | Duration | Purpose |
|---|---|---|
| **4-hour kickoff** | 14 400 s | Phase 1 exit gate (approved split) |
| **7-day continuous** | 604 800 s | v1 ship gate |

Smoke runs (60–120 s) are used during development to prove the harness itself works end-to-end; they do not produce authoritative Phase 1 evidence.

## Usage

```sh
dotnet run --project tests/ElpisEdgeConnect.LeakHarness --configuration Release -- \
  --duration-seconds 14400 \
  --sample-interval-seconds 60 \
  --output docs/phase1-exit/leak-harness-4h.csv
```

CLI arguments (all optional):

| Argument | Default | Notes |
|---|---|---|
| `--duration-seconds` | `120` | Total run length |
| `--sample-interval-seconds` | `10` | Cadence at which a row is written |
| `--output` | `leak-harness.csv` | CSV file path (overwritten) |

Developer defaults (2 min, 10 s interval) are intentionally short so a wrong invocation doesn't accidentally burn hours.

## Runtime topology

- **Composition root:** the real `CompositionRoot.AddElpisEdgeConnectHost(...)` wires every Phase 1 service.
- **Config:** a `current.json` is seeded in a temp directory at startup. Two sources, two sinks, two routes (`src-1→sink-1`, `src-2→sink-2`).
- **License:** file is absent → `HostStartup.LoadLicense` is a no-op (`File.Exists` check in `HostStartup.cs`).
- **Adapters:** `MockSourceAdapter` × 2 (each with `PollGate` + `PointsPerPoll = 100`); `MockSinkAdapter` × 2 (free-pass).
- **Load:** two background loader tasks call `gate.Release()` every 100 ms → 10 polls/sec × 100 points = 1 000 pts/sec per source → 2 000 pts/sec total.
- **Readiness:** `EnableEndpointsServer = false` — the HTTP surface is not bound. Readiness is observable via the collector snapshot.

## CSV schema — v1

Fixed column order. New columns must be appended, not inserted, to preserve downstream compatibility.

| # | Column | Type | Source |
|---:|---|---|---|
| 1 | `timestamp_utc` | ISO-8601 UTC | `DateTime.UtcNow` at sample time |
| 2 | `elapsed_sec` | decimal | seconds since run start |
| 3 | `private_bytes` | int64 | `Process.PrivateMemorySize64` after `proc.Refresh()` |
| 4 | `managed_heap_bytes` | int64 | `GC.GetTotalMemory(forceFullCollection: false)` |
| 5 | `gen0` | int32 | `GC.CollectionCount(0)` |
| 6 | `gen1` | int32 | `GC.CollectionCount(1)` |
| 7 | `gen2` | int32 | `GC.CollectionCount(2)` |
| 8 | `total_points_delivered` | int64 | `sink1.PublishedCount + sink2.PublishedCount` |
| 9 | `route_event_log_live` | int32 | `GetRouteStateEvents("route-1").LiveCount` |
| 10 | `route_event_log_dropped` | int64 | `GetRouteStateEvents("route-1").TotalDropped` |
| 11 | `bp_event_log_live` | int32 | `GetBackpressureEvents("route-1").LiveCount` |
| 12 | `bp_event_log_dropped` | int64 | `GetBackpressureEvents("route-1").TotalDropped` |
| 13 | `sink_event_log_live` | int32 | `GetSinkEvents("route-1", "sink-1").LiveCount` |
| 14 | `sink_event_log_dropped` | int64 | `GetSinkEvents("route-1", "sink-1").TotalDropped` |

Invariants that any valid run must satisfy:

- `managed_heap_bytes` at end ≤ 1.10 × value at start (10 % growth ceiling — plan §10)
- `private_bytes` at end ≤ 1.10 × value at start
- `gen2` is monotonic and grows *slowly* — a steep upward slope indicates a leak
- `route_event_log_live` ≤ `DiagnosticsConstants.DefaultRouteEventRetention` (256)
- `bp_event_log_live` ≤ `DefaultBackpressureEventRetention` (128)
- `sink_event_log_live` ≤ `DefaultSinkEventRetention` (128)
- `total_points_delivered` is monotonically increasing across samples

## Phase 1 exit kickoff criteria (4-hour run)

The kickoff is **PASS** iff **all** of the following hold:

1. **Completion:** the harness ran for the full 14 400 seconds without crashing, hanging, or throwing an unhandled exception.
2. **Memory ceiling:** `managed_heap_bytes[last] ≤ 1.10 × managed_heap_bytes[first]` AND `private_bytes[last] ≤ 1.10 × private_bytes[first]`.
3. **Gen2 trend:** the Gen2 collection count grows sublinearly relative to throughput — rate of increase over the second half of the run is ≤ rate over the first half (no accelerating GC pressure).
4. **Bounded retention:** every `*_event_log_live` column stays ≤ its `DiagnosticsConstants` ceiling for the entire run.
5. **Data-flow progress:** `total_points_delivered` ≥ 14 400 × 2 000 × 0.95 = 27 360 000 (95 % of the scheduled load).
   **D10 revision note:** The 2 000 pts/sec baseline assumes `BufferMode.InMemory`. The harness's default config uses the `BufferPolicyConfig` default (`Mode = StoreAndForward` → `SqliteBuffer` with `synchronous=FULL`), whose writer-mutex contention under the full pipeline (enqueue + ack + stats + reclaim, all sharing one `SemaphoreSlim(1,1)`) limits sustained throughput to ~1 400 pts/sec across two routes. When running against SqliteBuffer, the corrected threshold is: `14 400 × 1 400 × 0.95 = 19 152 000`. Both thresholds are documented; the pass/fail vote in `leak-harness-kickoff-summary.md` evaluates against both.
6. **Event-log drop counters** grow only as expected by the retention math (they are allowed to grow; the invariant is that `LiveCount` stays bounded).

If any criterion fails, the run is **FAIL**, the raw CSV is committed anyway, and the Phase 1 exit review documents the gap before the final gate.

## Artifact layout

- **Raw CSV:** `docs/phase1-exit/leak-harness-4h.csv` (4-hour kickoff) and `docs/phase1-exit/leak-harness-7d.csv` (v1 ship gate). Smoke run artifacts go under `docs/phase1-exit/leak-harness-smoke-*.csv`.
- **Summary:** `docs/phase1-exit/leak-harness-kickoff-summary.md` — written by hand after each kickoff, recording start/end values and the pass/fail vote.

## Explicit non-goals

- **No mini monitoring platform.** The harness writes a flat CSV — no dashboards, no metrics aggregation, no live graphs. Analysis is a spreadsheet task.
- **No host feature changes.** The harness exercises the production composition root exactly as the integration scenarios do.
- **No performance optimization** unless the kickoff run reveals a real leak or unbounded growth. The D phase 7 benchmark gate has already validated CPU throughput.
