# 01 — Clone, build, run unit tests

## Clone

```pwsh
cd C:\dev
git clone <repo-url> EdgeConnect
cd EdgeConnect
```

You can clone anywhere, but several scripts and absolute-path conventions assume `C:\dev\EdgeConnect`. Following that convention saves friction.

## Shared knowledge

EdgeConnect shares a contract with **EREMOS V2** via files under `C:\dev\shared-knowledge\`. Clone that too if you'll be touching anything that crosses the boundary (MQTT contract, glossary, decisions affecting both products):

```pwsh
cd C:\dev
git clone <shared-knowledge-url> shared-knowledge
```

`CLAUDE.md` lists the absolute paths you'll be referring to.

## Bootstrap (one-liner)

From the repo root:

```pwsh
.\scripts\dev\bootstrap.ps1
```

This script:

1. Verifies prerequisites (`dotnet --version`, `pwsh -Version`).
2. Runs `dotnet restore` on the solution.
3. Runs `dotnet build ElpisEdgeConnect.sln`.
4. Runs `dotnet test --filter "Category!=Flaky"` against the full suite.

Expected at the end:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Total tests: ~2,500     Passed: ~2,500     Failed: 0     Skipped: small number
```

If the test run reports a small number of failures in `ConfigSchemaModelTests`, that's a known pre-existing issue tied to a count assertion (see `task_b3eda035` in the project tracker). Doesn't block onboarding.

## Manual equivalents

If you'd rather run the steps individually:

```pwsh
dotnet restore ElpisEdgeConnect.sln
dotnet build   ElpisEdgeConnect.sln --no-restore
dotnet test    ElpisEdgeConnect.sln --no-build --filter "Category!=Flaky"
```

## What you just built

The solution has ~30 projects. The shape:

- `src/ElpisEdgeConnect.Core/` — protocol-agnostic runtime (canonical model, pipeline, routing, store-and-forward, licensing).
- `src/ElpisEdgeConnect.Sources.*` — protocol adapter modules (Focas2, BrotherHttp, ModbusTcp, MTConnect, OpcUaClient, S7).
- `src/ElpisEdgeConnect.Sinks.*` — northbound modules (Mqtt, OpcUaServer).
- `src/ElpisEdgeConnect.Host/` — headless runtime entry point.
- `src/ElpisEdgeConnect.Management/` — Connectivity Studio (Blazor Server + REST API).
- `src/ElpisEdgeConnect/` — legacy migrated FanucCncDataBridge code (Phase 2 refactor target; do not add new code here).
- `tools/` — CLI utilities (LicenseGen, ValidateConfig, ValidateSidecar, bulk-provision generator, etc.).
- `tests/` — xUnit + FluentAssertions test projects per layer.

The [codebase tour](06-codebase-tour.md) goes deeper.

## Build expectations (don't ship work that violates these)

- `TreatWarningsAsErrors=true` on Core. Zero analyzer warnings at Error level.
- Nullable reference types enabled project-wide.
- Every public Core API has XML doc comments.
- Tests are deterministic. No `Thread.Sleep`. Use `TaskCompletionSource` or time abstractions.

## Done?

Next: set up the [dev license](02-dev-license.md), then the [dev gateway config](03-dev-config.md).
