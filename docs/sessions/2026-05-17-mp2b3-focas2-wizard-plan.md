# M.2b.3 — FOCAS2 Studio wizard (+ Browse Controller live probe)

**Status:** v1 — DRAFT, awaiting ChatGPT review pass before lock
**Date:** 2026-05-17
**Branch:** worktree `happy-cartwright-465442` → `claude/happy-cartwright-465442` (from `master` at `545f722`)
**Successor to:** M.P2.3 (PR [#4](https://github.com/elpisitsolutions/EdgeConnect/pull/4)), inheriting the operator-pleasant hot-reload + reload-outcome surface
**Predecessor pattern:** M.2b.1 Modbus wizard (`AddModbusSource.razor`, `ModbusSourceWizardModel.cs`, `WizardConfigMerger.cs`)
**Estimated size:** ~1,300 LOC code + ~450 LOC tests, single PR
**Test baseline:** 1755 → expected ~1770 after M.2b.3 (~+15 tests; wizard model + browse endpoint coverage)

---

## 1. Goal

Add a Studio wizard for FOCAS2 source setup, mirroring the M.2b.1 Modbus wizard's shape but with:

- A **collect-all-by-default + group picker** UX for the FOCAS2 `DataPoints` hierarchical-prefix model (which is materially different from Modbus's per-tag rows).
- A **"Browse Controller" live probe** that drives `Initialize → Start → BrowseTagsAsync → Stop → Dispose` on a throwaway `Focas2SourceAdapter` to surface real axis names + discovered tag list before the operator commits the draft.

The wizard reuses `WizardConfigMerger` and `RouteWiring` unchanged (both already protocol-agnostic from M.2b.1).

### Architectural pin (locked)

**The live probe reuses the existing `ISourceAdapter.BrowseTagsAsync` capability — no contract revision.** The `ISourceAdapter` contract file is marked LOCKED ("changes require blueprint revision") and the FOCAS2 adapter itself documents that `SourceCapabilities.TestConnect` is "intentionally NOT declared … revisit once Phase 4's management API lands the contract extension." Path A (throwaway-adapter Initialize/Start/Browse/Stop) honours that lock. A future milestone may add `TestConnectAsync` to the contract; M.2b.3 will not.

---

## 2. Locked decisions (carried from scope-confirmation 2026-05-17)

| # | Decision | Reasoning |
|---|---|---|
| A | Mirror M.2b.1 wizard shape — single Razor page, Identity / Connection / Data Points / Browse Controller / Routing / Draft Summary sections | Operator already knows the Modbus pattern. Consistency reduces cognitive load. Reuse `WizardConfigMerger` and `RouteWiring` as-is. |
| B | **Data Points UX:** collect-all default (empty `DataPoints`) + expandable group picker for hierarchical prefixes (Status/, Axes/, Spindle/, Feed/, Alarms/, Production/, Tool/, MtLinki/, Diagnostics/) | Matches how `Focas2SourceConfiguration.DataPoints` actually works (empty = collect everything; otherwise prefix-match). Per-tag rows would be wrong for FOCAS2 — tags are discovered, not declared. |
| C | **Live probe IN scope**, reusing `BrowseTagsAsync` via Path A (no contract change) | User-confirmed inclusion. Path A stays inside the LOCKED `ISourceAdapter` contract. |
| D | Probe button labelled **"Browse Controller"** (not "Probe controller") | User-requested. More accurate — the endpoint actually drives `BrowseTagsAsync`. Apply consistently to button label, endpoint route, result-section heading, error messages. |
| E | **Dup IP:Port → warn but allow** | Two FOCAS2 sources on the same controller is technically allowed by Core and there are legitimate edge cases (test rig, dual-monitor). Warn so the operator-mistake case is caught, but don't block. |
| F | **TCP-only connection** | `Focas2SourceConfiguration` only models TCP (IpAddress + Port). HSSB is not supported by the adapter today. Wizard surface mirrors adapter capability. |
| G | The new endpoint takes a `SourceInstanceConfig` (canonical DTO) and runs it through `Focas2SourceConfiguration.FromSourceInstance` — same code path the supervisor uses | Exercises the same parsing logic the live system uses. A bug in the wizard's projection or in `FromSourceInstance` is caught by Browse before the operator commits. Defence in depth. |
| H | Probe runs a **throwaway** adapter — fresh instance, fresh native handle, no supervisor involvement | Probing must not touch the live runtime. A separate adapter lifecycle on a separate `Focas2Thread` keeps probe failures isolated from the live data path. |
| I | Probe is **license-gated** by `source-focas2` (same key as registration) | A customer without the FOCAS2 module license cannot probe. Matches the registration-time enforcement; consistent surface. |
| J | Probe is **single-flight per `IpAddress:Port`** within the management process | FOCAS2 allocates a real handle. Concurrent probes against the same controller can fight (handle exhaustion). Single-flight at the endpoint layer; if a probe is already in flight, second request returns 409 with a helpful message. |
| K | Wizard model tests follow `ModbusSourceWizardModelTests.cs` shape — POCO-only, no bUnit | `ReloadOutcomePanelModelTests.cs` shows bUnit IS available but the existing wizard tests deliberately stay model-level. Stay consistent. |

---

## 3. Out of scope (explicit guardrails)

- **No `ISourceAdapter` contract change.** No `TestConnectAsync` method. Path B was explicitly rejected in scope-confirmation as out-of-scope for a wizard milestone.
- **No generalisation of the Browse endpoint to other protocols.** The endpoint is `POST /api/v1/sources/browse/focas2` — FOCAS2-specific. Modbus/S7/MTConnect browse can land as separate milestones once their own wizards need it. (Decision validated against the alternative "Path A but FOCAS2-only endpoint" option — same result, less speculative scaffolding.)
- **No HSSB / non-TCP connection modes.** Adapter doesn't support them.
- **No per-tag manual rows.** FOCAS2 tags are discovered, not declared.
- **No "wire to existing route" branch in routing.** Same safety pattern as M.2b.1 — `WizardConfigMerger` deliberately omits `AddToExisting`. New route or no route.
- **No CSV import / template library.** That's M.2c.
- **No auto-populate of the group picker from Browse results.** Browse is purely informational in v1. Operator still picks groups manually. (OPEN Q6 — see §10.)
- **No probe-result caching.** Each Browse click is a fresh round-trip. Caching opens stale-data questions; not worth solving in v1.

---

## 4. Deliverables

### 4.1 Management — wizard model

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs` *(new)* | POCO with two-way-bindable properties matching `Focas2SourceConfiguration` fields. `BuildSourceInstance()` projects to canonical `SourceInstanceConfig` with `protocolName = "focas2"` and the Focas2-specific block packed into `Connection`. Static `DataPointGroups` registry for the group picker (paths + display labels grouped by category, mirroring `Focas2TagMap` categories). Helper `BuildDataPointsFromSelection(groups, mode)` projects the picker state back to the `dataPoints` string list. |

### 4.2 Management — Razor wizard page

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFocas2Source.razor` *(new)* | Single-page form, `@page "/sources/new/focas2"`. Sections: (1) Identity, (2) Connection, (3) Data Points (radio: "Collect all" vs "Limit to specific groups" + group checkboxes), (4) **Browse Controller** (button + result panel: discovered axes, tag list, last-error block), (5) Routing (identical structure to Modbus), (6) Draft summary, Save/Cancel buttons. Calls `/api/v1/config` to load current config (dup-id / dup-IP warnings), `/api/v1/sources/browse/focas2` for the probe, and `/api/v1/config/drafts` to save. |

### 4.3 Management — protocol-picker tile

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/ChooseSourceProtocol.razor` | Flip the FOCAS2 tile (lines 67-82) from tooltip-disabled to clickable: replace the `MudTooltip` wrapper with an `@onclick` handler pointing at `/sources/new/focas2`, change the chip text from "Coming in M.2b.3" → "Available", switch chip Color to `Color.Success` Variant `Filled`, drop the dashed-border opacity styling. |

### 4.4 Management — Browse endpoint

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseApi.cs` *(new)* | Endpoint registration class following the `SourcesApi.cs` pattern. Maps `POST /api/v1/sources/browse/focas2`. Request body: `SourceInstanceConfig`. Response: `Focas2BrowseResultDto` (success: discovered axes, tag definitions, system info; failure: error code + message). License-gated via `ILicenseManager.IsModuleEnabled("source-focas2")`. Single-flight per `IpAddress:Port` via a process-wide `ConcurrentDictionary<string,SemaphoreSlim>`. Bounded timeout = `config.TimeoutSeconds * 1000 + 5000 ms` grace. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseResultDto.cs` *(new)* | DTO for the response: `Success` bool, `AxisNames`, `Tags` (canonical `TagDefinition` list), `CncSeries`, `CncType`, `ErrorCode`, `ErrorMessage`, `ElapsedMs`. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseService.cs` *(new)* | Service that owns probe execution: build `Focas2SourceConfiguration` via `FromSourceInstance`, construct a throwaway `Focas2SourceAdapter`, run `InitializeAsync → StartAsync → BrowseTagsAsync → StopAsync → DisposeAsync` under a cancellation-bound timeout. Returns the result DTO. Catches `AdapterException` and surfaces the error code + message; catches `OperationCanceledException` (timeout) and surfaces `FOCAS2.BROWSE_TIMEOUT`. |
| `src/ElpisEdgeConnect.Management/ManagementServiceCollectionExtensions.cs` *(edit if exists; else `Program.cs` wiring location)* | Register `Focas2BrowseService` as a singleton; the endpoint resolves it from DI. Map `MapFocas2BrowseApi()` alongside the existing `MapSourcesApi()` call. |

### 4.5 Tests

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Management.Tests/Focas2SourceWizardModelTests.cs` *(new)* | ~10 tests covering: default-values match adapter defaults, `BuildSourceInstance` produces a canonical config with correct protocolName, group-picker projection to `dataPoints` (collect-all → empty array; group selection → expected prefixes), optional fields omitted when blank, roundtrip via `Focas2SourceConfiguration.FromSourceInstance` reproduces the typed config, ushort port validation, IP-address blank-rejection at `BuildSourceInstance` time. |
| `tests/ElpisEdgeConnect.Management.Tests/Focas2BrowseServiceTests.cs` *(new)* | ~5 tests against a `FakeFocas2Api`-backed adapter: success returns axes + tags + system info; license-disabled returns 403-shaped DTO; connect-failure surfaces `FOCAS2.CONNECT_FAILED`; timeout surfaces `FOCAS2.BROWSE_TIMEOUT`; single-flight returns 409-shaped result when a probe for the same IP:Port is in flight. |

### 4.6 Docs

| File | Change |
|---|---|
| `docs/adapter-sdk/focas2-adapter.md` | New §"Studio wizard" paragraph at the top: "The FOCAS2 source can also be added via the Studio wizard at `/sources/new/focas2`. The wizard exposes the same configuration surface this document describes, plus a Browse Controller button that does a one-shot connect + tag discovery against the configured controller." |
| `docs/sessions/2026-05-17-focas2-wizard-kickoff.md` | Append a "Resolution" footer noting M.2b.3 plan v1 lives at `docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan.md`. |

### 4.7 ADR

| File | Change |
|---|---|
| `docs/decisions/0011-browse-controller-reuses-browsetagsasync.md` *(new)* | Status: Accepted. Captures the locked decision to reuse `BrowseTagsAsync` for the wizard probe rather than extend `ISourceAdapter` with `TestConnectAsync`. Context: locked contract, future milestone deferral, Path A vs Path B trade-off. References the wizard milestone. Short — likely ~80 lines. |

---

## 5. Sequence of work

| Step | What | Why / Gate |
|---|---|---|
| 1 | **Reality check.** Confirm `Focas2SourceAdapter` can be safely instantiated outside the supervisor: read `Focas2Thread`, `Focas2ConnectionManager` lifecycles, and `DisposeAsync` behaviour. Identify any process-wide state (singletons, statics) that would make two simultaneous probes against different IPs collide. | Pre-implementation verification. If a static prevents two probes (e.g. shared thread, shared API instance), the single-flight guard needs to be PROCESS-WIDE not per-IP. Lock the guard scope against reality. |
| 2 | Write `Focas2SourceWizardModel` + its tests. Pure POCO work — no UI, no API. | Smallest, most testable unit first. |
| 3 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — must stay green, +10 new tests pass. | Lock the model contract before building UI/API on top of it. |
| 4 | Write `Focas2BrowseService` + `Focas2BrowseResultDto` + tests (with `FakeFocas2Api`-backed adapter, similar to how the Focas2 unit tests already wire fake API). | Probe logic next, since the Razor button depends on its API surface. |
| 5 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — must stay green, +5 new tests pass. | Lock the probe behaviour. |
| 6 | Write `Focas2BrowseApi.cs`, wire into Management's endpoint mapping. | API surface. |
| 7 | Write `AddFocas2Source.razor`. Calls `/api/v1/config`, `/api/v1/sources/browse/focas2`, `/api/v1/config/drafts`. | UI. |
| 8 | Flip the FOCAS2 tile on `ChooseSourceProtocol.razor`. | Reachability. |
| 9 | **Full regression gate.** `dotnet build ElpisEdgeConnect.sln` (0 warnings, 0 errors) + `dotnet test --filter "Category!=Flaky"` across the full solution. Target: ~1770. | Final pre-doc sweep. |
| 10 | ADR-0011, docs/adapter-sdk/focas2-adapter.md edit, handoff doc footer. | Docs. |
| 11 | Optional: manual smoke against a real / faked CNC if reachable. | Operator-realism check. |
| 12 | Single commit. PR. | Phase close. |

**Steps 3, 5, 9 are the regression gates.** Steps 3 and 5 are internal (Management.Tests project only). Step 9 is the full-solution gate.

---

## 6. Test list (preview — refined during implementation)

### 6.1 `Focas2SourceWizardModelTests` (~10)

1. **`Defaults_MatchAdapterDefaults`** — Port 8193, TimeoutSeconds 10, KeepAlive true, InitialBackoffMs 5000, MaxBackoffMs 120000, BackoffMultiplier 2.0, MaxConnectRetries 5. (Cross-check against `Focas2SourceConfiguration` defaults to catch drift.)
2. **`BuildSourceInstance_ProtocolName_IsFocas2`** — emitted DTO has `ProtocolName = "focas2"`.
3. **`BuildSourceInstance_PackedConnection_RoundtripsViaFromSourceInstance`** — `Focas2SourceConfiguration.FromSourceInstance(model.BuildSourceInstance())` produces an equivalent typed config. Headline parity test.
4. **`DataPoints_CollectAllMode_EmitsEmptyArray`** — group picker in "Collect all" mode → emitted `dataPoints` is empty.
5. **`DataPoints_SelectiveMode_EmitsPrefixes`** — checking "Status" + "Spindle/Speed" → emitted `dataPoints` is `["Status/", "Spindle/Speed"]` (or equivalent exact set).
6. **`DataPoints_AllGroupsSelected_EquivalentToCollectAll`** — selecting every group is documented as equivalent to empty; implementation choice (always emit groups vs. collapse-to-empty). Test pins the decision.
7. **`OptionalFields_OmittedWhenBlank`** — DeviceId, DeviceName, DeviceClass defaults / blanks don't pollute the DTO.
8. **`Port_OutOfUShortRange_RejectedAtBuildTime`** — defensive: BuildSourceInstance throws (or returns clamped) if Port is set outside 0..65535.
9. **`IpAddress_BlankOrWhitespace_RejectedAtBuildTime`** — DTO can't be built with an empty IpAddress (matches `FromSourceInstance` enforcement).
10. **`DeviceClass_DefaultsToCnc`** — Modbus defaults to `plc`; FOCAS2 defaults to `cnc`. Pin it.

### 6.2 `Focas2BrowseServiceTests` (~5)

1. **`Browse_HappyPath_ReturnsAxesAndTags`** — `FakeFocas2Api` configured with 3 axes (X/Y/Z) and standard tag map. Service returns DTO with `Success=true`, `AxisNames=[X,Y,Z]`, `Tags` contains every `Focas2TagMap.StaticTags` entry + axis-templated entries.
2. **`Browse_LicenseDisabled_ReturnsLicensedDeniedResult`** — `ILicenseManager.IsModuleEnabled("source-focas2")` returns false. Service skips adapter construction; DTO has `Success=false`, `ErrorCode = "LICENSE.MODULE_DISABLED"`.
3. **`Browse_ConnectFailure_SurfacesAdapterErrorCode`** — `FakeFocas2Api.AllocateHandleAsync` throws `Focas2FatalException(EW_SOCKET)`. Service catches; DTO has `Success=false`, `ErrorCode = "FOCAS2.CONNECT_FAILED"` (or whatever the adapter actually emits — pin to reality).
4. **`Browse_TimeoutExceeded_SurfacesBrowseTimeoutCode`** — `FakeFocas2Api.AllocateHandleAsync` hangs; cancellation token fires after `TimeoutSeconds*1000 + 5000ms` grace; DTO has `Success=false`, `ErrorCode = "FOCAS2.BROWSE_TIMEOUT"`.
5. **`Browse_SecondCallWhileInFlight_ReturnsBusyResult`** — start a probe against `192.168.1.10:8193` that's intentionally slow; second probe to the same IP:Port returns immediately with `Success=false`, `ErrorCode = "FOCAS2.BROWSE_IN_FLIGHT"`.

(Razor component tests deliberately deferred — Modbus precedent is POCO-model-only.)

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `Focas2SourceAdapter` has process-wide state that prevents two simultaneous probes (e.g. shared `Focas2Thread` or static DLL load) | Step 1 reality check reads the lifecycle code to verify. If true, single-flight guard becomes process-wide not per-IP, and the test for concurrent-different-IP gets reframed. |
| Throwaway adapter leaks native handle on probe failure | `Focas2BrowseService` wraps the entire flow in `try/finally` with `DisposeAsync`. Probe-timeout test (#4) specifically verifies disposal fires even when cancelled. |
| Probe takes > 10s and Studio UX feels frozen | Razor button enters busy state with `MudProgressLinear` and shows elapsed time. Service-side cancellation token caps at `TimeoutSeconds*1000 + 5000ms`. |
| License check is bypassed by a clever request | License check runs at the endpoint layer (rejected before service is even resolved) AND inside `Focas2BrowseService.BrowseAsync` as defence in depth. Tests cover both. |
| Browse returns tag list inconsistent with what the actual adapter collects at runtime | They use the same `BrowseTagsAsync` implementation. Roundtrip test (Wizard test #3) + the fact that the probe uses the same `FromSourceInstance` path pins the parity. |
| FOCAS2 native library not installed → probe fails with cryptic DllNotFoundException | `Focas2BrowseService` catches `DllNotFoundException` and emits `FOCAS2.NATIVE_LIBRARY_MISSING` with the install-doc reference. |
| Two operators probe the same controller from different Studio sessions | Single-flight is process-local. A second operator in the same process sees the BUSY result. Cross-process is out of scope (Studio is single-process). |
| Wizard-form state lost when the operator clicks Browse and navigates the result | Razor `@code` block retains state across re-renders; Browse result lives in `_browseResult` field alongside `_model`. Not a real risk; flag for smoke. |
| Probe accidentally probes the live route's controller and trips an alarm | Reality check (Step 1): does FOCAS2 raise any controller-side signal when a transient handle is opened? README says probe is ~5ms when keep-alive is on. Browse opens once and closes — likely below operator-perceptible threshold. Document in adapter-sdk note. |

---

## 8. Definition of done

1. `dotnet build ElpisEdgeConnect.sln` — 0 warnings, 0 errors.
2. Full sweep at ~1770 (1755 + ~15 new tests across `Focas2SourceWizardModelTests` and `Focas2BrowseServiceTests`).
3. Steps 3, 5, 9 regression gates all green.
4. FOCAS2 tile on `/sources/new` is clickable and lands at `/sources/new/focas2`.
5. `/sources/new/focas2` produces a draft via `/api/v1/config/drafts` indistinguishable in shape from a hand-authored FOCAS2 entry.
6. Browse Controller button against the FOCAS2 fake returns axes + tags; against an unreachable host returns a structured error; against a license-disabled gateway returns LICENSE.MODULE_DISABLED.
7. `Focas2SourceConfiguration.FromSourceInstance(wizard.BuildSourceInstance())` produces an equivalent typed config (test #3, the headline parity test).
8. ADR-0011 in place; the `ISourceAdapter` contract is unchanged (verify by `git diff` over `src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs`).
9. `docs/adapter-sdk/focas2-adapter.md` has the new Studio paragraph.
10. Handoff doc has the resolution footer.

---

## 9. Pause-point criteria

Stop and report if:

- Step 1 reality check shows the FOCAS2 adapter has process-wide state that materially changes the single-flight design.
- Step 3 or 5 internal gate regresses any existing Management test.
- The roundtrip parity test (#3 of wizard model) fails because `Focas2SourceConfiguration.FromSourceInstance` rejects a payload the wizard produces — a real bug in either side of the bridge.
- The FOCAS2 fake adapter doesn't actually exercise `BrowseTagsAsync` end-to-end (the existing 75 tests use it, but never via a separately-instantiated adapter outside the supervisor) — the probe path may need a new test seam.
- A probe against the real-CNC environment (optional Step 11) trips an unexpected controller-side alarm — surface to user before merging.

---

## 10. OPEN questions for ChatGPT review

| # | Question |
|---|---|
| Q1 | Should the Browse endpoint live in `Management` (current plan) or in `Host`? The adapter instantiation logic lives in `Host` (via `Focas2RegistrationExtensions.ConstructSourceRegistration`); reusing that helper would mean putting the endpoint behind a thin `Host`-side service. Trade-off: Management ownership keeps the API surface together; Host ownership keeps adapter construction in one place. |
| Q2 | Browse timeout policy: `config.TimeoutSeconds * 1000 + 5000ms` grace, OR a fixed Browse-specific timeout (e.g. 15s) independent of the wizard's connect-timeout field? Operator may want to test rough timing with a short timeout; using config's value couples Browse to wizard state. |
| Q3 | Single-flight scope: per-IP:Port (current plan) vs process-wide. Step 1 reality check will inform, but pre-review: which would the reviewer prefer if `Focas2Thread` turns out to be process-shared? |
| Q4 | Should the probe also call `ValidateConfigAsync` before `Initialize`, to surface config-schema-level errors before paying the connect-time cost? Or strictly Initialize+Start+Browse? |
| Q5 | DataPoints UX detail: when the operator picks the "Status" group, should it emit `"Status/"` (prefix, all sub-points) or expand to the individual `"Status/RunState"`, `"Status/AutoMode"`, etc. paths? Prefix is shorter on the wire; explicit paths are auditable. The adapter accepts both. |
| Q6 | Should Browse auto-populate the group picker from the discovered tags? (Operator clicks Browse → returned tag categories pre-tick the matching group boxes.) Smoother UX but couples picker state to probe results. Current plan says no. |
| Q7 | Should `ChooseSourceProtocol.razor` get a regression test (bUnit) verifying both Modbus AND FOCAS2 tiles are clickable + S7/MTConnect remain tooltip-disabled? Modbus shipped without one. Catches accidental "broke the tile" regressions. |
| Q8 | Should the warn-but-allow dup-IP guard (Locked E) also cover the **route side** — warn if the new source's selected sinks already feed another FOCAS2 source's route? Probably no (different concern), but pin the answer. |
| Q9 | Should ADR-0011 explicitly defer the `TestConnectAsync` contract extension to a named future milestone (e.g. "revisit in M.2c or M.2d") or leave the deferral open-ended? |
| Q10 | Razor component: should the wizard auto-fill the InstanceId field with `focas-1`, `focas-2`, ... based on the highest existing FOCAS2 instance number, the way good wizards do? Modbus does NOT — the field starts blank. Match Modbus for consistency, or improve here and back-port to Modbus later? |

---

## 11. Scope summary

- ~150 LOC wizard model (`Focas2SourceWizardModel.cs`)
- ~500 LOC wizard Razor (`AddFocas2Source.razor`)
- ~5 LOC protocol-picker tile flip
- ~300 LOC Browse endpoint + service + DTO
- ~30 LOC composition wiring (DI + endpoint mapping)
- ~280 LOC wizard model tests (~10 tests)
- ~170 LOC Browse service tests (~5 tests)
- ~80 LOC ADR-0011
- ~30 lines doc edits (adapter-sdk + handoff footer)

Single PR. Test target: ~1770.

---

**End of M.2b.3 v1 plan. Awaiting ChatGPT review pass before lock. Implementation per §5 sequence after v2 commits.**
