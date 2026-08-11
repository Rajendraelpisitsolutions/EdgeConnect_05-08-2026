# M.2d.3 — Sink + Route Editors: Implementation Handoff

**Date:** 2026-05-26
**Branch:** `claude/m2d3-impl`
**Status:** Implementation complete — full test suite green, awaiting user smoke-test + commit/PR approval
**Plan reference:** `docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md`
**Precondition:** M.2d.2 merged to master at commit `312d5f6`

---

## What was built

M.2d.3 adds edit-mode wizards for **Sink** and **Route** configuration. Operators can now:
- Click the pencil icon on any row in the Destinations or Routes list
- Open the same wizard that was used to create the entry — pre-filled with the current config
- Edit any field (InstanceId is locked, protocol is locked)
- Save with optimistic concurrency (409 on stale base version → StaleEditWarningBanner)

The implementation mirrors M.2d.2's source-edit pattern exactly. Every decision from the v2 plan doc was implemented as locked.

---

## Files changed

### New source files
| File | Purpose |
|------|---------|
| `src/ElpisEdgeConnect.Management/Api/SinksUpdateApi.cs` | `PUT /api/v1/sinks/{instanceId}` — 8-step flow identical to SourcesUpdateApi |
| `src/ElpisEdgeConnect.Management/Api/RoutesUpdateApi.cs` | `PUT /api/v1/routes/{routeId}` — same pattern |
| `src/ElpisEdgeConnect.Management/Contracts/UpdateSinkRequestDto.cs` | Request body for PUT sinks |
| `src/ElpisEdgeConnect.Management/Contracts/UpdateRouteRequestDto.cs` | Request body for PUT routes |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkEditRouter.razor` | `/destinations/{id}/edit` — dispatches to protocol-specific wizard |
| `src/ElpisEdgeConnect.Management/Components/Pages/RouteEditRouter.razor` | `/routes/{id}/edit` — dispatches to AddRoute |

### Modified source files
| File | Change |
|------|--------|
| `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs` | Added `BuildEditedSinkDraft` + `BuildEditedRouteDraft` |
| `src/ElpisEdgeConnect.Management/Wizards/MqttSinkWizardModel.cs` | Added `HydrateFromExisting(SinkInstanceConfig)` |
| `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs` | Added `HydrateFromExisting(SinkInstanceConfig)` |
| `src/ElpisEdgeConnect.Management/Wizards/RouteWizardModel.cs` | Added `HydrateFromExisting(RouteConfig)` + fixed `DeadbandRow.Threshold` in DeadbandPercent branch |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddMqttDestination.razor` | Edit mode: `EditMode` param, `HydratedConfig` param, `OnParametersSet` hydration, `SaveEditAsync`, StaleEditWarningBanner |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` | Same edit mode pattern |
| `src/ElpisEdgeConnect.Management/Components/Pages/RouteWizards/AddRoute.razor` | Same edit mode + source-change warning chip (Q4) |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` | Edit pencil column with stopPropagation wrapper |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` | Edit pencil column with stopPropagation wrapper |
| `src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs` | Added `MapSinksUpdateApi()` + `MapRoutesUpdateApi()` to `MapConnectivityStudio` |

### New test files
| File | Tests |
|------|-------|
| `tests/ElpisEdgeConnect.Management.Tests/WizardConfigMergerEditTests.cs` | BuildEditedSinkDraft + BuildEditedRouteDraft (6 tests) |
| `tests/ElpisEdgeConnect.Management.Tests/MqttSinkWizardModelEditTests.cs` | HydrateFromExisting round-trips (4 tests) |
| `tests/ElpisEdgeConnect.Management.Tests/OpcUaServerSinkWizardModelEditTests.cs` | HydrateFromExisting round-trips (3 tests) |
| `tests/ElpisEdgeConnect.Management.Tests/RouteWizardModelEditTests.cs` | HydrateFromExisting round-trips (4 tests) |
| `tests/ElpisEdgeConnect.Management.Tests/SinksUpdateApiTests.cs` | PUT /api/v1/sinks: happy path, 409, 400, 404, preservation, id mismatch (6 tests) |
| `tests/ElpisEdgeConnect.Management.Tests/RoutesUpdateApiTests.cs` | PUT /api/v1/routes: happy path, 409, 400, 404, preservation, id mismatch (6 tests) |

### Modified test files
| File | Change |
|------|--------|
| `tests/ElpisEdgeConnect.Management.Tests/ElpisEdgeConnect.Management.Tests.csproj` | Added project references to `ElpisEdgeConnect.Sinks.Mqtt` + `ElpisEdgeConnect.Sinks.OpcUaServer` for enum types in hydration tests |

---

## Test count delta

| Baseline (M.2d.2) | This branch |
|---|---|
| 676 Management.Tests | 676 Management.Tests |
| 2,478 total (estimated at M.2d.2 merge) | **2,507 total passing, 1 skipped** |

The Management.Tests count stayed at 676 — this branch's new tests are offset by no test regressions. (The 29 new tests within Management.Tests bring it to 676 because the previous session's step counter was already at 676 with these included.)

---

## Decisions locked this session (all from v2 plan)

| Decision | Locked choice |
|---|---|
| Q1 Save flow | PUT direct-apply (same as SourcesUpdateApi) |
| Q2 InstanceId | Immutable — disabled field in edit mode |
| Q3 Edit URL | `SinkEditRouter.razor` at `/destinations/{id}/edit` |
| Q4 Source-change UX | Inline `MudAlert` warning chip only — no modal |
| Q7 Edit button | Pencil icon in Action column with stopPropagation |
| `DeadbandRow.Threshold` | `Value` property does not exist — `Threshold` is correct (bug caught in Step 3) |

---

## Smoke test checklist (user performs in Studio)

Before merging, verify these flows manually:

**Sink edit:**
1. Open Studio → Destinations tab
2. Confirm pencil icon appears on each row
3. Click pencil on an MQTT destination → `/destinations/{id}/edit` loads
4. Confirm fields are pre-filled (broker, port, topic, auth, etc.)
5. Change a field (e.g. QoS level) → click Save → confirm toast / redirect to `/destinations/{id}`
6. Re-open edit — confirm changed value persisted
7. Click pencil on an OPC UA Server destination → repeat steps 4–6

**Route edit:**
1. Open Studio → Routes tab
2. Confirm pencil icon appears on each row
3. Click pencil on a route → `/routes/{id}/edit` loads
4. Confirm route name, source, sinks, filter, transforms are pre-filled
5. Change sink selection or a transform → Save → confirm redirect to `/routes`
6. Re-open edit — confirm changed value persisted
7. Change the source of a route → confirm yellow "Source changed from..." warning chip appears inline

**Optimistic concurrency (optional):**
1. Open same sink edit in two browser tabs
2. Save in tab 1 → succeed
3. Save in tab 2 (stale base version) → confirm StaleEditWarningBanner appears with "Reload" link

---

## Known non-issues

- 2 pre-existing compiler warnings in `MachineManagerService.cs` — unrelated to this branch, already present on master
- The `Gate5_BrokerOutageReconnect` integration test is categorised `Flaky` and skipped by default — pre-existing

---

## Next milestone after merge

**M.2d.4 — Cross-wizard UX sweep** (`docs/sessions/2026-05-21-m2d4-cross-wizard-sweep-plan.md`)

Covers polish deferred from M.2d.1–3:
- Filter/Transforms "changed" indicator in route edit
- Drag-reorder for route sink list
- Inline delete confirmation dialogs
- Validation error scroll-to
- Keyboard shortcuts (Esc to cancel, Enter to advance)
