# Session handoff — M.P2.1 Phase 3b

**Date:** 2026-05-15
**Previous session ended at commit:** `383f1a9`
**Branch:** `claude/p2-fail-soft-startup` (in worktree `C:\dev\EdgeConnect\.claude\worktrees\modbus-f1\`)
**Next milestone:** M.P2.1 Phase 3b (UI layer)

## What landed in the previous session

```
383f1a9  docs(decisions): backfill ADRs 0002-0008
114b93f  docs(decisions): ADR-0001 Device Inspector / M.5
a720767  docs(claude-md): codify docs/decisions/ + docs/sessions/
2636105  M.P2.1 phase 3a — Faulted-state surfaces (data layer)
7b5f3bc  M.P2.1 phase 2 — wire fail-soft into the startup path
6d85f38  M.P2.1 phase 1 — IConfigurationFaultRegistry + audit-chain
```

Gateway no longer crashes on cross-record configuration faults
(Phase 2). API layer fully exposes faults (Phase 3a). Studio UI is
the remaining work.

**Test baseline:** 1598/1598 passing. After Phase 3b: expect ~1610-
1625 (new SinkInventoryBuilder tests + a few page-binding changes
won't add many tests).

## What's pending in Phase 3b

UI surfaces that make the data from Phase 3a visible to operators:

1. **`SinkInventoryBuilder`** (new pure builder) mirroring
   `SourceInventoryBuilder` and `RouteInventoryBuilder`. Walks
   `config.Sinks`, joins with snapshots + faults. **Different
   from sources**: sinks have a 1-to-many relationship with
   routes (one sink can be referenced by multiple routes).

2. **`SinksApi` rewire** to config-driven via `SinkInventoryBuilder`
   (mirrors the M.2b.1.1 SourcesApi rewire).

3. **`SinkListItemDto` wire shape change**: `RouteId: string`
   → `RouteIds: IReadOnlyList<string>`. Only consumers are the
   in-process Sinks/SinkDetail Razor pages (no external HTTP
   consumers). Document the contract break in the file header.

4. **Razor page updates:**
   - `Sources.razor`: add tooltip on Faulted chip showing
     `"{ErrorCode}: {Message}"`
   - `Sinks.razor` (Destinations page): config-driven null-aware
     route column (now plural — render as chips for multi-route),
     Faulted-state colors + tooltip, refreshed empty-state copy
   - `SinkDetail.razor`: handle null/empty routes; refresh 404
     wording to "not in current configuration"
   - `Routes.razor`: Faulted-state colors + tooltip on the
     existing `StateColor` switch
   - `RouteDetail.razor`: Faulted state chip + reason banner
     when route is faulted

5. **New `/diagnostics` Configuration-faults panel**: top-of-page
   `MudPaper` section, `@if (_faults.Count > 0)` conditional,
   warning-amber accent. Lists each fault as a row with Kind
   badge / InstanceId / ErrorCode / Message / relative-time.
   Refreshes on existing /diagnostics poll cadence.

6. **`Overview.razor` faults strip**: small inline strip near
   page header, only when N > 0:
   `⚠ N configuration faults — see Diagnostics`
   (link to /diagnostics).

## Locked decisions (do NOT relitigate)

All architectural decisions for this milestone are written down as
ADRs. **Before making any decision, scan `docs/decisions/`.**

Most-relevant for Phase 3b:

- **ADR-0002** Configuration = inventory truth (every list page
  walks config first, enriches with diagnostics)
- **ADR-0005** Faults are runtime state (registry is in-memory,
  read-only from Studio)
- **ADR-0007** Display precedence: **Disabled > Faulted > live
  state > Configured/Not running** (locked by ChatGPT review pass)
- **ADR-0008** "Destinations" not "Sinks" in operator-facing UI

Locked from the AskUserQuestion turns in the previous session:

- Faulted tooltip = `"{ErrorCode}: {Message}"` (no
  "Configuration fault" vs "Runtime fault" kind prefix)
- `/diagnostics` panel: top-of-page section, hidden when no
  faults exist
- Overview indicator: small inline strip (NOT a full alert
  banner)
- `SinkListItemDto.RouteId` → `RouteIds: List<string>`

## In-flight state on disk (poison config primed)

`C:\Users\Sudhakar C\AppData\Local\edgeconnect-uademo\config\current.json`
is primed for a two-fault-path smoke test:

| Source | Setup | Fault path |
|---|---|---|
| `modbus-demo` | Enabled=true, valid route | Healthy (control) |
| `modbus-line-2` | Enabled=false, no route | Disabled (control) |
| `modbus-line-3` | Enabled=true + route `route-line-3` + unreachable `127.0.0.1:502` | Runtime fault (adapter init fails) |
| `Modbus-4` | Enabled=true, **no route** | Cross-record config fault (registry path) |

After Phase 3b ships, smoke-test plan:
1. `dotnet run --project src\ElpisEdgeConnect.Management`
2. Gateway should boot (Phase 2 work)
3. Studio `/sources` should show:
   - modbus-demo: Running (green)
   - modbus-line-2: Disabled (dark)
   - modbus-line-3: Faulted (red) with `MODBUS.SOCKET_ERROR`-style
     code in tooltip
   - Modbus-4: Faulted (red) with `CONFIG.SOURCE_WITHOUT_ROUTE`
     in tooltip
4. `/diagnostics` should show the Configuration-faults panel at top
   with one row (Modbus-4)
5. Overview should show "⚠ 1 configuration fault" strip

## Process / cadence expectations

- **Plan → ChatGPT review → implement** for architectural work.
  Phase 3b is mostly mechanical UI/API mirroring of established
  patterns; a review pass is NOT needed unless something novel
  emerges.
- **Commits with detailed bodies** explaining *why*, not just
  *what*. Co-Authored-By trailer on every commit.
- **Merge with `--no-ff`** when M.P2.1 phase 3b is verified — the
  user does this manually after smoke-test passes.
- **Studio (`Management.exe`) holds binary locks** during dotnet
  builds. If a rebuild fails with "file in use," kill Studio first.

## Plan structure for Phase 3b

Recommended order in the new session:

1. `SinkInventoryBuilder` + tests (~250 LOC + ~280 LOC tests)
2. `SinksApi` rewire (~30 LOC)
3. `SinkListItemDto` contract change (~20 LOC + Razor consumers)
4. Razor page tooltip updates (~50 LOC across 5 files)
5. `/diagnostics` Configuration-faults panel (~80 LOC)
6. `Overview.razor` faults strip (~20 LOC)
7. Build + run all tests
8. Commit Phase 3b as a single commit
9. Smoke test against the poison config
10. Merge `claude/p2-fail-soft-startup` into master with `--no-ff`

Estimated session length: 1-2 hours.

## After Phase 3b merges

Next milestone candidates, in order:

1. **M.P2.2** — Runtime hot-reload (add/remove instances at
   apply-time, no restart). ~800-1200 LOC, ~3-4 cycles. Reuses
   M.P2.1's fault plumbing for hot-reload init failures.
2. **M.5a** — Device Inspector (FOCAS2 implementation +
   `/sources/{id}/probe` page). Customer A unblocker. Schedule
   AFTER M.P2.2 so wizards ship into healthy substrate.
3. **M.2b.2** (S7 wizard, Customer B unblocker) and **M.2b.3**
   (FOCAS2 wizard, Customer A unblocker) — bundle M.5a/c for
   probe-based auto-discovery.

Carry-forward followups (lower priority, captured in commit messages
and ADRs):
- `.gitignore` entry for
  `tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/.venv/`
- M.2a history "initial-0000-..." sentinel visual cleanup
- DiagnosticsEventCodes.Sink* rename (audit-chain migration —
  needs a separate ADR before doing)
