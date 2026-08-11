# Coverage baseline — Core

**Captured:** 2026-04-22 (Phase 2 early, under worktree `claude/gracious-yonath-05a575`)
**Tool:** `coverlet.collector` 6.0.2 via `dotnet test --collect:"XPlat Code Coverage"`
**Target:** `src/ElpisEdgeConnect.Core` (the protocol-agnostic runtime; `Phase 1` exit target was ≥80% on Core)
**Test assembly:** `ElpisEdgeConnect.Core.Tests.dll` — 806 tests, 0 failures

## Aggregate

| Metric | Value |
|---|---:|
| Line coverage | **89.56 %** (5 467 / 6 104) |
| Branch coverage | **82.90 %** (1 237 / 1 492) |

Comfortably above the Phase 1 ≥80 % target. Room to grow during Phase 2 as the routing engine's slow paths and edge cases get more stress coverage from integration tests.

## Observed gaps (low line-rate areas)

Quick scan of files below the 80 % line — these are the natural next targets if coverage becomes a Phase 2 exit gate:

| File | Line rate | Branch rate | Notes |
|---|---:|---:|---|
| `Routing/DefaultRouteBufferFactory.cs` | 0 % | — | Not exercised by Core.Tests; the factory is integration-only (covered by Integration.Tests and the leak harness). |
| `Routing/FanoutDispatcher.cs` | 82.0 % | 70.0 % | Backpressure branch + one disposal edge not hit by the current unit suite. |
| `Routing/RouteWorker.cs` (two partitions) | 72.5 / 86.4 % | 75.0 / 77.8 % | Two error-recovery branches and one graceful-stop path unreachable via the deterministic mock adapters. |

## How to re-run

```bash
dotnet test tests/ElpisEdgeConnect.Core.Tests/ElpisEdgeConnect.Core.Tests.csproj \
  --no-build --nologo \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage-raw
```

The cobertura report lands under `coverage-raw/{guid}/coverage.cobertura.xml`. The top-level `coverage` element carries the aggregate numbers (`line-rate`, `branch-rate`, `lines-covered`, `lines-valid`, `branches-covered`, `branches-valid`).

## Scope

This baseline covers only `ElpisEdgeConnect.Core` exercised by `ElpisEdgeConnect.Core.Tests`. Adapter projects (`Sources.Focas2`, `Sinks.Mqtt`) and the Host have their own test suites; a future sweep can aggregate all six projects via `coverlet.msbuild` + `reportgenerator` if a single cross-project number is needed. For Phase 2 entry, the Core number is what matters — it's the locked runtime that every adapter and sink depends on.
