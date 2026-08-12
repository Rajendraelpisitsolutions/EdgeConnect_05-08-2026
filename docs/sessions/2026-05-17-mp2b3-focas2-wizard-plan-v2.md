# M.2b.3 — FOCAS2 Studio wizard (+ Browse Controller live probe)

**Status:** v2 — LOCKED (ChatGPT review folded in)
**Date:** 2026-05-17
**Branch:** worktree `happy-cartwright-465442` → `claude/happy-cartwright-465442` (from `master` at `545f722`)
**Predecessor plan:** [`2026-05-17-mp2b3-focas2-wizard-plan.md`](2026-05-17-mp2b3-focas2-wizard-plan.md) (v1, superseded)
**Successor to:** M.P2.3 (PR [#4](https://github.com/elpisitsolutions/EdgeConnect/pull/4)), inheriting the operator-pleasant hot-reload + reload-outcome surface
**Predecessor pattern:** M.2b.1 Modbus wizard (`AddModbusSource.razor`, `ModbusSourceWizardModel.cs`, `WizardConfigMerger.cs`)
**Estimated size:** ~1,350 LOC code + ~520 LOC tests, single PR
**Test baseline:** 1755 → expected ~1773 after M.2b.3 (~+18 tests: wizard model + browse service + bUnit tile coverage)

---

## 1. Goal

Add a Studio wizard for FOCAS2 source setup, mirroring the M.2b.1 Modbus wizard's shape but with:

- A **collect-all-by-default + group picker** UX for the FOCAS2 `DataPoints` hierarchical-prefix model (materially different from Modbus's per-tag rows).
- A **"Browse Controller" live probe** that drives `FromSourceInstance → ValidateConfigAsync → Initialize → Start → BrowseTagsAsync → Stop → bounded Dispose` on a throwaway `Focas2SourceAdapter` to surface real axis names + discovered tag list before the operator commits the draft.

The wizard reuses `WizardConfigMerger` and `RouteWiring` unchanged (both already protocol-agnostic from M.2b.1).

### Architectural pin (locked)

**The live probe reuses the existing `ISourceAdapter.BrowseTagsAsync` capability — no contract revision.** The `ISourceAdapter` contract file is marked LOCKED ("changes require blueprint revision") and the FOCAS2 adapter itself documents that `SourceCapabilities.TestConnect` is "intentionally NOT declared … revisit once Phase 4's management API lands the contract extension." Path A (throwaway-adapter Initialize/Start/Browse/Stop) honours that lock.

**Discovery and probe workflows are treated as management-plane ephemeral operations and are intentionally isolated from the runtime supervisor pipeline.** ADR-0011 captures this lock — it lays the foundation for future OPC UA / S7 / EtherNet/IP / MTConnect browse and "Test Connection" workflows without contaminating runtime orchestration.

---

## 2. Locked decisions

### Carry-forward from v1 (unchanged after review)

| # | Decision | Reasoning |
|---|---|---|
| A | Mirror M.2b.1 wizard shape — single Razor page, Identity / Connection / Data Points / Browse Controller / Routing / Draft Summary sections | Operator already knows the Modbus pattern. Consistency reduces cognitive load. Reuse `WizardConfigMerger` and `RouteWiring` as-is. |
| B | **Data Points UX:** collect-all default (empty `DataPoints`) + expandable group picker for hierarchical prefixes (Status/, Axes/, Spindle/, Feed/, Alarms/, Production/, Tool/, MtLinki/, Diagnostics/) | Matches how `Focas2SourceConfiguration.DataPoints` actually works. Per-tag rows would be wrong — tags are discovered, not declared. |
| C | **Live probe IN scope**, reusing `BrowseTagsAsync` via Path A (no contract change) | User-confirmed inclusion. Path A stays inside the LOCKED `ISourceAdapter` contract. |
| D | Probe button labelled **"Browse Controller"** | User-requested; more accurate — the endpoint drives `BrowseTagsAsync`. Apply consistently to button label, endpoint route, result-section heading, error messages. |
| E | **Dup IP:Port → warn but allow** | Test rigs, dual-monitor, commissioning overlap are legitimate. Warn for operator-mistake catch, don't block. |
| F | **TCP-only connection** | `Focas2SourceConfiguration` only models TCP. HSSB not supported by the adapter today. Wizard mirrors adapter capability. |
| G | The Browse endpoint takes a `SourceInstanceConfig` (canonical DTO) and runs it through `Focas2SourceConfiguration.FromSourceInstance` — same code path the supervisor uses | Exercises the same parsing logic the live system uses. Wizard projection bug or `FromSourceInstance` bug caught by Browse before commit. Defence in depth. |
| H | Probe runs a **throwaway** adapter — fresh instance, fresh native handle, no supervisor involvement | Probing must not touch the live runtime. Separate adapter lifecycle on a separate `Focas2Thread` keeps probe failures isolated. |
| I | Probe is **license-gated** by `source-focas2` (same key as registration) | A customer without the module license cannot probe. Matches registration-time enforcement. |
| J | Probe is **single-flight per `IpAddress:Port`** within the management process | FOCAS2 allocates a real handle; concurrent probes against the same controller fight. Single-flight at endpoint layer; second request returns 409 with a helpful message. |
| K | Wizard model tests follow `ModbusSourceWizardModelTests.cs` shape — POCO-only | Consistent with M.2b.1. bUnit added separately for the protocol-picker tile regression (Locked R below), not for the wizard form. |

### Added at v2 lock (ChatGPT review folded in)

| # | Decision | Reasoning |
|---|---|---|
| L | **Browse endpoint lives in `Management`, not `Host`** | `Host` owns runtime supervisor lifecycle; `Management` owns ephemeral probe workflows. Moving the endpoint into `Host` just to reuse adapter construction helpers would erode the runtime/discovery boundary. If construction logic duplication appears, extract a tiny shared helper later — don't move the endpoint. |
| M | **Browse timeout is a fixed 15s = 10s connect + 5s browse grace**, NOT derived from `config.TimeoutSeconds` | Runtime reconnect policy (`TimeoutSeconds`) and interactive UI responsiveness are different concerns. An operator setting 60s for unstable production conditions must not freeze Studio Browse for 65s. Interactive tooling stays predictably responsive. |
| N | **Probe sequence calls `ValidateConfigAsync` BEFORE `InitializeAsync`** | Schema/config validation is deterministic, fast, and local; Init/Start allocate handles and hit the network. Fast-fail cheap errors first; improves operator feedback quality. Sequence: `FromSourceInstance → ValidateConfigAsync → Initialize → Start → BrowseTags → Stop → Dispose`. |
| O | **Group picker emits PREFIXES**, not expanded explicit tag paths | `"Status/"` not `["Status/RunState", "Status/AutoMode", ...]`. Smaller config, future-proof to tag-map growth — when `Status/ProgramState` lands later, existing configs inherit it automatically. Edge case: all groups selected → collapse to empty array (Core's "empty = collect everything" semantic). |
| P | **`DisposeAsync` is bounded by a hard 5s timeout** | Native FOCAS2 DLL can hang during teardown. `await adapter.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))` in finally. On timeout: log + abandon (handle reclaimed at process exit) + surface `FOCAS2.DISPOSE_TIMEOUT` as a non-fatal warning. Probe is low-frequency; handle leak across probes is not a realistic risk vs. force-terminating a wedged `Focas2Thread`. |
| Q | **Every probe gets a correlation `ProbeId`** | `Guid.NewGuid().ToString("N")[..8]`, surfaced in the result DTO and logged at each phase boundary (FromSourceInstance / Validate / Init / Start / Browse / Stop / Dispose). Operationally invaluable for customer-issue triage. |
| R | **Protocol-picker tile regression test (bUnit)** | Cheap, high-ROI guard against the "I accidentally broke the tile" class of regression. Assertions: Modbus + FOCAS2 are clickable; S7 + MTConnect remain tooltip-disabled with "Coming in M.2b.X" chips. New file `Focas2ProtocolPickerTests.cs` (~3 tests). |

### Browse-flow precondition (full, locked)

The throwaway-adapter sequence, in order, with disposal protection:

```csharp
var probeId = Guid.NewGuid().ToString("N")[..8];
var stopwatch = Stopwatch.StartNew();
Focas2SourceAdapter? adapter = null;
try
{
    // 1. License gate (Locked I) — runs BEFORE we touch FOCAS2 at all.
    if (!license.IsModuleEnabled("source-focas2"))
        return Failure("LICENSE.MODULE_DISABLED", probeId);

    // 2. Single-flight guard (Locked J) — keyed on IpAddress:Port.
    using var leaseHandle = await TryAcquireLeaseAsync(ipAddressPort, ct);
    if (leaseHandle is null)
        return Failure("FOCAS2.BROWSE_IN_FLIGHT", probeId);

    // 3. Schema parse — fast-fail (Locked G).
    var typed = Focas2SourceConfiguration.FromSourceInstance(request);

    // 4. Construct adapter — no DI, no SourceRegistration, throwaway (Locked H).
    adapter = new Focas2SourceAdapter(typed.InstanceId, logger, gatewayIdentity);

    // 5. Bounded 15s overall (Locked M).
    using var probeCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
    probeCt.CancelAfter(TimeSpan.FromSeconds(15));

    // 6. Adapter-level validation BEFORE Init (Locked N).
    var validation = await adapter.ValidateConfigAsync(typed, probeCt.Token);
    if (!validation.IsValid)
        return Failure("FOCAS2.CONFIG_INVALID", probeId, validation.Errors);

    // 7. Init → Start → Browse → Stop.
    await adapter.InitializeAsync(typed, probeCt.Token);
    await adapter.StartAsync(probeCt.Token);
    var tags = await adapter.BrowseTagsAsync(probeCt.Token);
    await adapter.StopAsync(probeCt.Token);

    return Success(probeId, tags, axes, cncSeries, stopwatch.ElapsedMilliseconds);
}
catch (OperationCanceledException)
{
    return Failure("FOCAS2.BROWSE_TIMEOUT", probeId);
}
catch (AdapterException ex)
{
    return Failure(ex.ErrorCode, probeId, ex.Message);
}
finally
{
    if (adapter is not null)
    {
        try
        {
            // Bounded Dispose (Locked P) — surface a non-fatal warning if it
            // hangs, do NOT throw, do NOT leak the exception into the result.
            await adapter.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            logger.LogWarning("BrowseProbeId={ProbeId} Dispose hung beyond 5s — handle abandoned", probeId);
            // Result already constructed; attach FOCAS2.DISPOSE_TIMEOUT as a warning if Success.
        }
    }
}
```

This pseudocode is the **load-bearing reference** for §6.4 (`Focas2BrowseService`) implementation. Any deviation has to flag back here.

---

## 3. Out of scope (explicit guardrails)

- **No `ISourceAdapter` contract change.** No `TestConnectAsync` method. Path B explicitly rejected.
- **No generalisation of the Browse endpoint to other protocols.** Endpoint is `POST /api/v1/sources/browse/focas2` — FOCAS2-specific. Modbus/S7/MTConnect browse can land as separate milestones.
- **No HSSB / non-TCP connection modes.** Adapter doesn't support them.
- **No per-tag manual rows.** FOCAS2 tags are discovered, not declared.
- **No "wire to existing route" branch in routing.** Same safety pattern as M.2b.1 — `WizardConfigMerger` deliberately omits `AddToExisting`. New route or no route.
- **No CSV import / template library.** That's M.2c.
- **No auto-populate of the group picker from Browse results.** Browse is purely informational in v1.
- **No probe-result caching.** Each Browse click is a fresh round-trip.
- **No instance-id auto-suggest.** Modbus doesn't have it either; improve both together in a separate milestone, not here. (Resolved Q10 — see §4.)
- **No route-side dup-sink guard.** Dup-IP is the only collision we warn on. (Resolved Q8.)
- **No probe through the runtime supervisor.** Strictly throwaway adapter. The "Discovery is management-plane ephemeral" principle (Locked L + ADR-0011) is the load-bearing invariant; never route Browse through `SourceSupervisor` even if it's "easier".

---

## 4. Resolved questions (record)

The ten OPEN questions from v1 were settled by ChatGPT review on 2026-05-17. Verdicts:

| # | Question | Verdict |
|---|---|---|
| Q1 | Browse endpoint in `Management` or `Host`? | **Management.** Discovery is management-plane; Host owns runtime supervisor lifecycle. Boundary erosion is painful later. Locked **L**. |
| Q2 | Browse timeout: derived from `config.TimeoutSeconds` or fixed? | **Fixed 15s** (10s connect + 5s browse grace). Runtime reconnect policy and interactive UI responsiveness are different concerns. Locked **M**. |
| Q3 | Single-flight scope: per-IP:Port or process-wide? | **Per-IP:Port unless Step 1 reality check shows process-global state.** Step 1 is the reality check; per-IP is the default. |
| Q4 | Call `ValidateConfigAsync` before `InitializeAsync`? | **Yes.** Fast-fail cheap errors before paying connect-time cost. Locked **N**. |
| Q5 | DataPoints emission: prefixes or explicit expansion? | **Prefixes.** Future-proof to tag-map growth. All-selected → collapse to empty array. Locked **O**. |
| Q6 | Auto-populate group picker from Browse result? | **No** in v1. Avoids stale-state / overwrite UX questions. |
| Q7 | bUnit regression test for the protocol-picker tile? | **Yes.** Cheap, high ROI. New file. Locked **R**. |
| Q8 | Route-side warning for dup-sink fanout? | **No.** Different concern; not a wizard responsibility. |
| Q9 | ADR-0011: named milestone for `TestConnectAsync` deferral? | **Open-ended.** No named milestone. Deferring to "when the management API contract extension lands" is sufficient. |
| Q10 | Instance-id auto-suggest? | **Match Modbus (no auto-suggest).** Improve both together in a follow-up; not part of M.2b.3. |

**Meta-architectural observation (preserved from review):** This milestone quietly establishes the **runtime adapters vs. management-plane ephemeral probes** distinction. ADR-0011 names it explicitly so future contributors don't try to thread discovery through live supervisors.

---

## 5. Out of scope (covered in §3)

(Section number kept for parity with M.P2.3 plan structure.)

---

## 6. Deliverables

### 6.1 Management — wizard model

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs` *(new)* | POCO with two-way-bindable properties matching `Focas2SourceConfiguration` fields. `BuildSourceInstance()` projects to canonical `SourceInstanceConfig` with `protocolName = "focas2"` and the Focas2-specific block packed into `Connection`. Static `DataPointGroups` registry for the group picker (paths + display labels grouped by category, mirroring `Focas2TagMap` categories). Helper `BuildDataPointsFromSelection(groups, mode)` projects picker state back to the `dataPoints` string list — **emits PREFIXES** (Locked O), collapses all-selected to empty array. |

### 6.2 Management — Razor wizard page

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFocas2Source.razor` *(new)* | Single-page form, `@page "/sources/new/focas2"`. Sections: (1) Identity, (2) Connection, (3) Data Points (radio: "Collect all" vs "Limit to specific groups" + group checkboxes), (4) **Browse Controller** (button + result panel: `ProbeId`, axes, tag count + tag list, CNC series + type, elapsed-ms, last-error block; small caption "Browse Controller performs a temporary connection for discovery only. No configuration is saved until Draft is committed."), (5) Routing (identical to Modbus), (6) Draft summary, Save/Cancel buttons. Calls `/api/v1/config`, `/api/v1/sources/browse/focas2`, `/api/v1/config/drafts`. |

### 6.3 Management — protocol-picker tile

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/ChooseSourceProtocol.razor` | Flip the FOCAS2 tile (lines 67-82) from tooltip-disabled to clickable: replace the `MudTooltip` wrapper with an `@onclick` handler pointing at `/sources/new/focas2`, change chip text to "Available", switch chip Color to `Success` Variant `Filled`, drop the dashed-border opacity styling. |

### 6.4 Management — Browse endpoint

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseApi.cs` *(new)* | Endpoint registration class following the `SourcesApi.cs` pattern. Maps `POST /api/v1/sources/browse/focas2`. Request body: `SourceInstanceConfig`. Response: `Focas2BrowseResultDto`. License-gated at endpoint layer (Locked I). Delegates to `Focas2BrowseService`. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseResultDto.cs` *(new)* | DTO: `ProbeId`, `Success`, `AxisNames`, `Tags` (canonical `TagDefinition` list), `TagCount`, `CncSeries`, `CncType`, `ErrorCode`, `ErrorMessage`, `ValidationErrors` (when `FOCAS2.CONFIG_INVALID`), `Warnings` (e.g. `FOCAS2.DISPOSE_TIMEOUT`), `ElapsedMs`. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseService.cs` *(new)* | Service owning the throwaway-adapter sequence per the §2 pseudocode. **15s overall timeout** (Locked M). **`ValidateConfigAsync` before `InitializeAsync`** (Locked N). **Single-flight per IpAddress:Port** via `ConcurrentDictionary<string,SemaphoreSlim>` (Locked J). **Bounded `DisposeAsync`** via `.AsTask().WaitAsync(5s)` in finally (Locked P). **Correlation `ProbeId`** logged at each phase boundary (Locked Q). Catches `AdapterException` → `ErrorCode`; catches `OperationCanceledException` → `FOCAS2.BROWSE_TIMEOUT`; catches `DllNotFoundException` → `FOCAS2.NATIVE_LIBRARY_MISSING`. |
| `src/ElpisEdgeConnect.Management/ManagementServiceCollectionExtensions.cs` *(edit; or `Program.cs`)* | Register `Focas2BrowseService` as singleton; map `MapFocas2BrowseApi()` alongside `MapSourcesApi()`. |

### 6.5 Tests

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Management.Tests/Focas2SourceWizardModelTests.cs` *(new)* | ~10 tests (see §8.1). |
| `tests/ElpisEdgeConnect.Management.Tests/Focas2BrowseServiceTests.cs` *(new)* | ~5 tests (see §8.2). |
| `tests/ElpisEdgeConnect.Management.Tests/Focas2ProtocolPickerTests.cs` *(new)* | ~3 bUnit tests (see §8.3) — Locked R. |

### 6.6 Docs

| File | Change |
|---|---|
| `docs/adapter-sdk/focas2-adapter.md` | New §"Studio wizard" paragraph at the top: "The FOCAS2 source can also be added via the Studio wizard at `/sources/new/focas2`. The wizard exposes the same configuration surface this document describes, plus a Browse Controller button that does a one-shot connect + tag discovery against the configured controller." |
| `docs/sessions/2026-05-17-focas2-wizard-kickoff.md` | Append "Resolution" footer noting M.2b.3 plan v2 lives at this file. |

### 6.7 ADR

| File | Change |
|---|---|
| `docs/decisions/0011-browse-controller-reuses-browsetagsasync.md` *(new)* | Status: Accepted. Captures the locked decision to reuse `BrowseTagsAsync` rather than extend `ISourceAdapter`. Includes the verbatim load-bearing sentence: "Discovery and probe workflows are treated as management-plane ephemeral operations and are intentionally isolated from the runtime supervisor pipeline." Outline in §12. |

---

## 7. Sequence of work

| Step | What | Why / Gate |
|---|---|---|
| 1 | **Reality check.** Read `Focas2Thread`, `Focas2ConnectionManager`, `Focas2SourceAdapter.DisposeAsync` lifecycles + identify any process-wide state (statics, shared threads, shared API instance). **Also verify `Focas2SourceAdapter.ValidateConfigAsync` can be called BEFORE `InitializeAsync`** — Locked N depends on this. If Validate requires Init, surface and pause. | Pre-implementation verification. Single-flight scope (Locked J vs process-wide) and Validate-before-Init feasibility both fall out of this read. |
| 2 | Write `Focas2SourceWizardModel` + its tests (§8.1). | Smallest, most testable unit first. |
| 3 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +10 new tests green. | Lock model contract before building UI/API on top. |
| 4 | Write `Focas2BrowseService` + `Focas2BrowseResultDto` + tests (§8.2). Use `FakeFocas2Api`-backed adapter (same pattern as existing 75 Focas2 unit tests). | Probe logic next — Razor button depends on its API surface. |
| 5 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +5 new tests green. | Lock probe behaviour. |
| 6 | Write `Focas2BrowseApi.cs`, wire into Management's endpoint mapping. | API surface. |
| 7 | Write `AddFocas2Source.razor` — all six sections. | UI. |
| 8 | Flip the FOCAS2 tile on `ChooseSourceProtocol.razor`. Write `Focas2ProtocolPickerTests.cs` (§8.3). | Reachability + tile regression guard. |
| 9 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +3 bUnit tests green. | Lock tile regression. |
| 10 | **Full regression gate.** `dotnet build ElpisEdgeConnect.sln` (0 warnings, 0 errors) + `dotnet test --filter "Category!=Flaky"` across the full solution. Target: ~1773. | Final pre-doc sweep. |
| 11 | ADR-0011, docs/adapter-sdk/focas2-adapter.md edit, handoff doc footer. | Docs. |
| 12 | Optional: manual smoke against a real / faked CNC if reachable. | Operator-realism check. |
| 13 | Single commit. PR. | Phase close. |

**Steps 3, 5, 9, 10 are the regression gates.** Steps 3, 5, 9 are internal (Management.Tests only). Step 10 is the full-solution gate at ~1773.

---

## 8. Test list (preview — refined during implementation)

### 8.1 `Focas2SourceWizardModelTests` (~10)

1. **`Defaults_MatchAdapterDefaults`** — Port 8193, TimeoutSeconds 10, KeepAlive true, InitialBackoffMs 5000, MaxBackoffMs 120000, BackoffMultiplier 2.0, MaxConnectRetries 5. Cross-check against `Focas2SourceConfiguration` defaults.
2. **`BuildSourceInstance_ProtocolName_IsFocas2`** — emitted DTO has `ProtocolName = "focas2"`.
3. **`BuildSourceInstance_PackedConnection_RoundtripsViaFromSourceInstance`** — `Focas2SourceConfiguration.FromSourceInstance(model.BuildSourceInstance())` produces an equivalent typed config. **Headline parity test.**
4. **`DataPoints_CollectAllMode_EmitsEmptyArray`** — "Collect all" → emitted `dataPoints` is empty.
5. **`DataPoints_SelectiveMode_EmitsPrefixes`** — checking "Status" + "Spindle" groups → emitted `dataPoints` is `["Status/", "Spindle/"]`. Pins Locked O.
6. **`DataPoints_AllGroupsSelected_CollapsesToEmpty`** — selecting every group emits empty array (Core's "empty = collect everything"). Pins Locked O edge case.
7. **`OptionalFields_OmittedWhenBlank`** — DeviceId, DeviceName blank → DTO uses defaults (DeviceId/Name fall back to InstanceId).
8. **`Port_OutOfUShortRange_RejectedAtBuildTime`** — defensive: BuildSourceInstance refuses Port outside 0..65535.
9. **`IpAddress_BlankOrWhitespace_RejectedAtBuildTime`** — DTO can't be built with empty IpAddress (matches `FromSourceInstance` enforcement).
10. **`DeviceClass_DefaultsToCnc`** — FOCAS2 defaults to `cnc`, not Modbus's `plc`. Pin it.

### 8.2 `Focas2BrowseServiceTests` (~5)

1. **`Browse_HappyPath_ReturnsAxesAndTags_WithProbeIdAndElapsedMs`** — `FakeFocas2Api` configured with 3 axes (X/Y/Z) + standard tag map. DTO has `Success=true`, `AxisNames=[X,Y,Z]`, `Tags.Count` matches `Focas2TagMap.StaticTags` + axis-templated entries, `ProbeId` is 8-char hex, `ElapsedMs > 0`. **Headline happy-path test.**
2. **`Browse_LicenseDisabled_ReturnsLicenseModuleDisabled`** — `ILicenseManager.IsModuleEnabled("source-focas2")` false. Adapter is never constructed; DTO `Success=false`, `ErrorCode = "LICENSE.MODULE_DISABLED"`. Pins Locked I.
3. **`Browse_ConnectFailure_SurfacesAdapterErrorCode`** — `FakeFocas2Api` throws on handle allocation. DTO `Success=false`, `ErrorCode` matches the actual code the adapter emits (Step 1 reality check pins the exact code).
4. **`Browse_TimeoutAt15s_SurfacesBrowseTimeoutCode`** — `FakeFocas2Api` hangs in allocation. Service-side cancellation fires at 15s; DTO `Success=false`, `ErrorCode = "FOCAS2.BROWSE_TIMEOUT"`, `ProbeId` present. Pins Locked M.
5. **`Browse_SecondCallWhileInFlight_ReturnsInFlightCode`** — start a slow probe against `192.168.1.10:8193`; second probe to same IP:Port returns immediately, `Success=false`, `ErrorCode = "FOCAS2.BROWSE_IN_FLIGHT"`. Pins Locked J.

Optional sixth test if §7 Step 1 reveals `ValidateConfigAsync` behaviour worth pinning:

6. **`Browse_InvalidConfig_FailsAtValidateBeforeInitialize`** — service calls `ValidateConfigAsync`, fails, and never reaches `InitializeAsync`. Spy on the fake API to verify Init was NOT called. Pins Locked N. (Add if Step 1 confirms feasibility.)

### 8.3 `Focas2ProtocolPickerTests` (~3, bUnit)

1. **`ModbusTile_IsClickable_NavigatesToModbusWizard`** — render `ChooseSourceProtocol`, simulate click on the Modbus tile, assert navigation target = `/sources/new/modbus`.
2. **`Focas2Tile_IsClickable_NavigatesToFocas2Wizard`** — same shape, target = `/sources/new/focas2`. Pins Locked R.
3. **`S7AndMTConnectTiles_RemainTooltipDisabled`** — S7 + MTConnect tiles render with the disabled `MudTooltip` wrapper and the "Coming in M.2b.X" chip. Catches accidental re-activation regression.

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Step 1 reveals process-wide state preventing two simultaneous probes against different IPs | Single-flight guard becomes process-wide; test #5 reframed; ChatGPT-review Q3 contingency already approved. |
| Throwaway adapter leaks native handle on probe failure | Try/finally wraps the full lifecycle; bounded `DisposeAsync` (Locked P) keeps a wedged DLL from hanging the request thread. |
| Probe takes > 15s and Studio UX feels frozen | Razor button shows `MudProgressLinear` busy state + elapsed-ms tick; service-side cancellation token caps at 15s (Locked M). |
| `ValidateConfigAsync` requires `Initialize` to have run | Step 1 reality check verifies. If true, Locked N is unachievable as written — pause and replan (likely: skip Validate step, document why, take the cost). |
| License check is bypassed | Endpoint-layer check (rejected before service is resolved) AND inside `Focas2BrowseService.BrowseAsync` as defence in depth. Test #2 covers. |
| Browse returns tag list inconsistent with what the runtime collects | They use the same `BrowseTagsAsync` implementation. Roundtrip test (model test #3) + same `FromSourceInstance` path pin the parity. |
| FOCAS2 native library not installed → cryptic DllNotFoundException | Service catches `DllNotFoundException` → `FOCAS2.NATIVE_LIBRARY_MISSING` with install-doc reference. |
| Two operators probe the same controller from different Studio sessions | Single-flight is process-local; Studio is single-process. Cross-process out of scope. |
| Wedged `DisposeAsync` accumulates handle leaks across many probes | Locked P abandons; native handles reclaimed at process exit. Probe is low-frequency; trade-off is correct vs force-terminating a wedged `Focas2Thread`. |
| Probe accidentally trips a controller-side alarm via transient handle | Document in `docs/adapter-sdk/focas2-adapter.md`: Browse opens once + closes; the operation matches a routine FOCAS2 handle cycle. Operator-perceptible impact: low. |
| Wizard-form state lost when Browse re-renders | Razor `@code` retains state; Browse result lives in `_browseResult` field alongside `_model`. Not a real risk; smoke-test confirms. |
| `EnumerateActions`-style invisible API drift in M.2b.1's `WizardConfigMerger` | Merger reused unchanged; `WizardConfigMergerTests` already covers dup-id + dup-route invariants. Step 10 full sweep catches any miss. |

---

## 10. Definition of done

1. `dotnet build ElpisEdgeConnect.sln` — 0 warnings, 0 errors.
2. Full sweep at ~1773 (1755 + ~18 new tests across `Focas2SourceWizardModelTests`, `Focas2BrowseServiceTests`, `Focas2ProtocolPickerTests`).
3. Steps 3, 5, 9, 10 regression gates all green.
4. FOCAS2 tile on `/sources/new` is clickable and lands at `/sources/new/focas2` (test #2 in §8.3 verifies).
5. `/sources/new/focas2` produces a draft via `/api/v1/config/drafts` indistinguishable in shape from a hand-authored FOCAS2 entry.
6. Browse Controller against the FOCAS2 fake returns axes + tag count + tags + CncSeries + ProbeId + ElapsedMs; against an unreachable host returns a structured error; against a license-disabled gateway returns `LICENSE.MODULE_DISABLED`; second probe to same IP:Port returns `FOCAS2.BROWSE_IN_FLIGHT`.
7. `Focas2SourceConfiguration.FromSourceInstance(wizard.BuildSourceInstance())` reproduces an equivalent typed config (test #3 of §8.1, headline parity test).
8. ADR-0011 in place with the "management-plane ephemeral operations" load-bearing sentence verbatim; `ISourceAdapter` contract unchanged (verify by `git diff` over `src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs`).
9. `docs/adapter-sdk/focas2-adapter.md` has the new Studio paragraph.
10. Handoff doc has the resolution footer.
11. Locked H verified by code review: `Focas2BrowseService` constructs the adapter directly with `new Focas2SourceAdapter(...)`, NOT via `Focas2RegistrationExtensions.ConstructSourceRegistration`. No `SourceRegistration` or `SourceSupervisor` references in the Browse code path.
12. Locked L verified by code review: Browse endpoint lives in `src/ElpisEdgeConnect.Management/`, not in `src/ElpisEdgeConnect.Host/`. No new `Host`-side files in this PR.
13. Locked M verified: the 15s constant appears exactly once (in `Focas2BrowseService`) and is NOT computed from `config.TimeoutSeconds`.
14. Locked N verified: `ValidateConfigAsync` is invoked before `InitializeAsync` in `Focas2BrowseService.BrowseAsync`.
15. Locked O verified: `DataPoints_SelectiveMode_EmitsPrefixes` + `DataPoints_AllGroupsSelected_CollapsesToEmpty` both green.
16. Locked P verified: `DisposeAsync` invocation in `Focas2BrowseService` is wrapped in `.AsTask().WaitAsync(TimeSpan.FromSeconds(5))` inside a `finally`. Grep confirms.
17. Locked Q verified: every `Focas2BrowseService` log statement includes `ProbeId`; DTO surfaces `ProbeId`.

---

## 11. Pause-point criteria

Stop and report if:

- Step 1 reveals `Focas2SourceAdapter.ValidateConfigAsync` cannot run before `Initialize` — Locked N is unachievable as written, replan needed.
- Step 1 reveals process-wide state in the FOCAS2 stack that materially changes Locked J — adjust single-flight scope.
- Step 3 or 5 or 9 internal gate regresses any existing Management test.
- The roundtrip parity test (#3 of §8.1) fails — real bug in either side of the wizard↔FromSourceInstance bridge.
- The FOCAS2 fake adapter cannot drive `BrowseTagsAsync` end-to-end via a separately-instantiated adapter (it works fine inside the supervisor; the probe path is the first attempt outside) — may need a new test seam.
- Step 12 manual smoke trips an unexpected controller-side alarm — surface to user before merging.

---

## 12. ADR-0011 outline (preview)

Will land at `docs/decisions/0011-browse-controller-reuses-browsetagsasync.md`.

**Title:** Browse Controller reuses `BrowseTagsAsync` via throwaway adapter — discovery is management-plane ephemeral

**Status:** Accepted (2026-05-17)

**Context:** M.2b.3 introduces a Studio "Browse Controller" feature for the FOCAS2 wizard, surfacing real axis names and tag list before the operator commits a draft. The `ISourceAdapter` contract is LOCKED (file header: "changes require blueprint revision") and does not currently carry a `TestConnectAsync` method — the FOCAS2 adapter itself documents this intentional omission ("revisit once Phase 4's management API lands the contract extension"). The choice was: extend the contract (Path B) vs reuse the existing `BrowseTagsAsync` capability via a throwaway adapter lifecycle (Path A).

**Decision:**

1. The Browse endpoint is implemented as **Path A** — a throwaway `Focas2SourceAdapter` lifecycle (`FromSourceInstance → ValidateConfigAsync → Initialize → Start → BrowseTagsAsync → Stop → bounded Dispose`).
2. The endpoint lives in `ElpisEdgeConnect.Management`, NOT in `ElpisEdgeConnect.Host`. Runtime supervisor lifecycle and management-plane probe workflows are intentionally separated.
3. The throwaway adapter never enters DI, the supervisor, or the routing engine. It is constructed directly with `new Focas2SourceAdapter(...)` and disposed at the end of every probe.
4. `ISourceAdapter` is unchanged.

**Reasoning:**

- The locked `ISourceAdapter` contract should not be revised mid-phase for a wizard feature. Path B (adding `TestConnectAsync`) would touch every existing source adapter (Modbus / S7 / MTConnect / FOCAS2 / mock) — disproportionate scope.
- Reusing `BrowseTagsAsync` exercises the same code path the live system uses, including `Focas2SourceConfiguration.FromSourceInstance` and the adapter's `Initialize` / `Start` sequence. A bug in either side is caught by Browse before commit. Defence in depth.
- Locating the probe in `Management` (not `Host`) preserves the runtime/management boundary. If construction-helper duplication appears, extract a tiny shared helper later — don't move the endpoint.

**Load-bearing principle (verbatim):**

> Discovery and probe workflows are treated as management-plane ephemeral operations and are intentionally isolated from the runtime supervisor pipeline.

This principle is the foundation for future browse + test-connection workflows across OPC UA, S7, EtherNet/IP, and MTConnect. Future contributors should NOT thread discovery through `SourceSupervisor` even when it appears "easier".

**Consequences:**

- `Focas2BrowseService` carries the full throwaway-adapter sequence (15s overall timeout, single-flight per IP:Port, `ValidateConfigAsync` before `Initialize`, bounded `DisposeAsync`, correlation `ProbeId`).
- `SourceCapabilities.TestConnect` remains undeclared on the FOCAS2 adapter — consistent with the adapter's existing documentation.
- A future `TestConnectAsync` contract extension is deferred open-endedly (no named milestone). When it lands, the existing Browse endpoint can be refactored to consume it without changing the management-plane / runtime separation.

---

## 13. Scope summary

- ~150 LOC wizard model (`Focas2SourceWizardModel.cs`)
- ~520 LOC wizard Razor (`AddFocas2Source.razor`) — slightly larger than v1 estimate due to Browse result panel + caption
- ~5 LOC protocol-picker tile flip
- ~320 LOC Browse endpoint + service + DTO (15s timeout + ProbeId + ValidateConfigAsync + bounded Dispose all add a small amount)
- ~30 LOC composition wiring
- ~280 LOC wizard model tests (~10 tests)
- ~170 LOC Browse service tests (~5 tests)
- ~70 LOC bUnit tile tests (~3 tests)
- ~110 LOC ADR-0011 (slightly longer than v1's ~80 due to the management-plane sentence + context)
- ~30 lines doc edits (adapter-sdk + handoff footer)

Single PR. Test target: ~1773.

---

**End of M.2b.3 v2 plan. LOCKED 2026-05-17 after ChatGPT review. Ready for implementation per §7 sequence.**
