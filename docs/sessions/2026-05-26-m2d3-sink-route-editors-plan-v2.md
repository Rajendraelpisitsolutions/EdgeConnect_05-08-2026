# M.2d.3 — Sink + Route editors (v2 plan)

**Status:** v2 — all open questions from v1 locked. Pending user ratification.
**Date:** 2026-05-26
**Supersedes:** `docs/sessions/2026-05-21-m2d3-sink-route-editors-plan.md` (v1)
**Roadmap reference:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md` §3.7.3
**Precondition satisfied:** M.2d.1 on master ✓, M.2d.2 on master ✓ (mirror-router pattern locked)

---

## 1. Q1–Q8 Locked Decisions

| # | Question | v2 Decision | Reasoning |
|---|---|---|---|
| Q1 | Draft→/config or PUT direct-apply? | **PUT direct-apply** (same as SourcesUpdateApi) | §2.1 added post-M.2d.2 explicitly prescribes `SinksUpdateApi` + `RoutesUpdateApi` mirroring `SourcesUpdateApi.cs`. Operator experience is identical to source Edit — save completes in the wizard, no separate Config-page Apply step. Validation still runs inside `ApplyDraft`. |
| Q2 | InstanceId immutability | **Confirmed locked — disabled field in Edit mode** | Renaming a sink breaks route references; renaming a route breaks buffer paths + diagnostics. Delete-and-create is the rename path. |
| Q3 | `/destinations/{id}/edit` routing | **Option A: `SinkEditRouter.razor`** (§2.1) | Single URL; router inspects `ProtocolName` and dispatches to the right wizard. Symmetric with `SourceEditRouter`. |
| Q4 | Route source-change UX | **Inline warning chip only** | Operator sees the impact at the Config page's draft diff when Validate+Apply runs. Dialog would add friction for a valid and common operation. |
| Q5 | Route sink-list editing | **Multi-select checkboxes (preserved)** | Core fanout is independent-per-sink; order has no operational meaning. Drag-reorder is M.2d.4 polish. |
| Q6 | Test Connection in Edit | **Uses edited (field-bound) values** | Natural wizard behaviour — the probe verifies what the operator is *about to save*, not what's running. |
| Q7 | Edit button placement on list rows | **Pencil icon in Action column** (same as M.2d.2 `Sources.razor` fix — `OnClick` + `stopPropagation` wrapper) | Consistent across all three list pages. |
| Q8 | Filter / Transforms in Edit | **Current expansion panels, no new section indicator** | Expansion panels are usable as-is. "Filter/Transforms changed" indicator deferred to M.2d.4. |

---

## 2. Architecture — what mirrors M.2d.2 exactly

| M.2d.2 piece | M.2d.3 equivalent |
|---|---|
| `SourceEditRouter.razor` | `SinkEditRouter.razor` + `RouteEditRouter.razor` |
| `SourcesUpdateApi.cs` (`PUT /api/v1/sources/{id}`) | `SinksUpdateApi.cs` (`PUT /api/v1/sinks/{id}`) + `RoutesUpdateApi.cs` (`PUT /api/v1/routes/{id}`) |
| `WizardConfigMerger.BuildUpdatedSourceDraft` | `BuildEditedSinkDraft` + `BuildEditedRouteDraft` (new methods) |
| `Focas2SourceWizardModel.HydrateFromExisting` | `MqttSinkWizardModel.HydrateFromExisting` + `OpcUaServerSinkWizardModel.HydrateFromExisting` + `RouteWizardModel.HydrateFromExisting` |
| `EditModeContext.Edit(instanceId, baseVersionId)` | Same — reused as-is |
| `StaleEditWarningBanner` | Same — reused as-is |
| Version load: `GET /api/v1/config/version` → `_baseVersionId` | Same pattern in both new routers |

---

## 3. New deliverables

### 3.1 WizardConfigMerger — two new methods

```csharp
// Replaces an existing sink in-place. Routes preserved byte-identically (same as BuildUpdatedSourceDraft).
// Throws ArgumentException if: sink not found, ProtocolName changed, InstanceId mismatch.
public static GatewayConfiguration BuildEditedSinkDraft(
    GatewayConfiguration current, SinkInstanceConfig editedSink)

// Replaces an existing route in-place.
// Throws ArgumentException if: route not found, RouteId changed, source not found, any sink not found.
public static GatewayConfiguration BuildEditedRouteDraft(
    GatewayConfiguration current, RouteConfig editedRoute)
```

Both are **pure** — no DI, no HTTP, no mutation. The route merger validates that referenced
source + sinks exist in `current` (defence-in-depth, same as `BuildNewRouteDraft`).

### 3.2 Hydration methods

```csharp
// Each throws ArgumentException if ProtocolName doesn't match.
public static MqttSinkWizardModel HydrateFromExisting(SinkInstanceConfig existing)
public static OpcUaServerSinkWizardModel HydrateFromExisting(SinkInstanceConfig existing)

// Route: hydrates flat properties + RouteFilterEditorModel + RouteTransformsEditorModel.
// No ProtocolName check (routes are protocol-agnostic).
public static RouteWizardModel HydrateFromExisting(RouteConfig existing)
```

Unknown JSON properties in Connection are ignored (same defensive pattern as M.2d.2 models).

### 3.3 PUT endpoints

**`SinksUpdateApi.cs`** — `PUT /api/v1/sinks/{instanceId}`

Exact same 8-step flow as `SourcesUpdateApi.DispatchAsync`:
1. Validate inputs (instanceId, body, BaseVersionId non-null)
2. Route param must match body.SinkConfig.InstanceId (400 on mismatch)
3. Load current config
4. Version check → 409 + `ConfigVersionMismatchDto { ConflictType = "VersionMismatch" }` on mismatch
5. Existence check → 404 if sink not found
6. `BuildEditedSinkDraft` → 400 on ArgumentException (ProtocolName change / invariant)
7. `CreateDraft` + `ApplyDraft` → 409 + `ValidationResultDto` on re-validation failure
8. Reload outcome → `ApplyResultDto` (200)

**`RoutesUpdateApi.cs`** — `PUT /api/v1/routes/{routeId}`

Same 8-step flow, operating on `RouteConfig` (not `SourceInstanceConfig`).

### 3.4 Mirror-router pages

**`SinkEditRouter.razor`** — `@page "/destinations/{InstanceId}/edit"`

Four-state UX matrix (same as SourceEditRouter):
- `Loading` — spinner
- `NotFound` — `NotFoundPanel` with back link
- `LoadError` — `LoadErrorPanel`
- `Loaded` — dispatch by `ProtocolName`:
  - `"mqtt"` → `<AddMqttDestination EditMode="@editCtx" HydratedConfig="@_sink" />`
  - `"opcua-server"` → `<AddOpcUaServerDestination EditMode="@editCtx" HydratedConfig="@_sink" />`
  - unknown → `<UnsupportedProtocolPanel>`

**`RouteEditRouter.razor`** — `@page "/routes/{RouteId}/edit"`

Same four-state matrix; `Loaded` dispatches to:
- `<AddRoute EditMode="@editCtx" HydratedConfig="@_route" />`

(No protocol fan-out needed for routes today — one wizard covers all.)

### 3.5 Wizard Edit-mode adoption

**`AddMqttDestination.razor`**
- Existing `@page "/destinations/new/mqtt"` kept unchanged
- Parameters: `[Parameter] EditModeContext? EditMode` + `[Parameter] SinkInstanceConfig? HydratedConfig`
- `OnParametersSet()` hydrates model from `HydratedConfig` (one-shot `_hydrated` flag, same as M.2d.2)
- **Routing section (§6)** hidden when `_isEdit` — `@if (!_isEdit)`
- `SaveAsDraftAsync` → `SaveEditAsync` split: Add path posts to `/api/v1/config/drafts` (unchanged); Edit path `PUT /api/v1/sinks/{instanceId}` with BaseVersionId
- `StaleEditWarningBanner` shown on 409 `ConflictType == "VersionMismatch"`
- InstanceId field: `Disabled="@_isEdit"` (Q2)

**`AddOpcUaServerDestination.razor`**

Identical to MQTT adoption minus the Test Connection slot.

NodeIdTemplate: in Edit mode, if the current draft value differs from `HydratedConfig`'s value, render the existing caution as `MudAlert Severity="Severity.Warning"` (§3.2 polish — low-cost, included here).

**`AddRoute.razor`**
- Parameters: `[Parameter] EditModeContext? EditMode` + `[Parameter] RouteConfig? HydratedConfig`
- `OnParametersSet()` hydrates `RouteWizardModel` (+ sub-models)
- RouteId field: `Disabled="@_isEdit"` (Q2)
- **Source-change warning chip (Q4)**: if `_isEdit` and `_model.SourceInstanceId != _originalSourceInstanceId`, render inline `MudChip Color="Color.Warning"` — "Changing the source rebinds this route. In-flight data may reflect the old source until the pipeline drains."
- **Sink delta in Draft Summary**: when `_isEdit`, show "Added: X, Y · Removed: Z" vs original sink set (nice UX, minimal cost)
- SaveEdit path: `PUT /api/v1/routes/{routeId}`

### 3.6 List page Edit entry points

**`Sinks.razor`** — Action column: pencil `MudIconButton` wrapped in `<span @onclick:stopPropagation>` (same fix as M.2d.2 Sources.razor)

**`Routes.razor`** — same

---

## 4. Tests

| File | Count | What it pins |
|---|---|---|
| `WizardConfigMergerEditTests.cs` | ~8 | `BuildEditedSinkDraft` (happy, not-found, protocol-change, route-preservation) + `BuildEditedRouteDraft` (happy, not-found, source-missing, sink-missing) |
| `MqttSinkWizardModelEditTests.cs` | ~5 | `HydrateFromExisting` round-trip fidelity (broker, auth, topic, backoff, InstanceId, Enabled) |
| `OpcUaServerSinkWizardModelEditTests.cs` | ~4 | Same for OPC UA fields |
| `RouteWizardModelEditTests.cs` | ~5 | `HydrateFromExisting` round-trip + sub-model hydration (filter globs, transform rows) |
| `SinksUpdateApiTests.cs` | ~6 | happy / 409-stale / 400-protocol-changed / 404-not-found / route-preservation / 400-id-mismatch |
| `RoutesUpdateApiTests.cs` | ~6 | same pattern |

**Total: ~34 tests.** All existing sink/route wizard tests (Add path) must stay green — no regressions.

---

## 5. Step-by-step implementation sequence

1. **Create branch** `claude/m2d3-impl` from master
2. **Merger methods (TDD)** — `WizardConfigMergerEditTests.cs` first, then `WizardConfigMerger.BuildEditedSinkDraft` + `BuildEditedRouteDraft`
3. **Hydration methods (TDD)** — tests first, then `HydrateFromExisting` on all three models
4. **PUT endpoints + tests** — `SinksUpdateApi.cs` + `RoutesUpdateApi.cs` + their test files; wire into `ManagementHostingExtensions`
5. **`SinkEditRouter.razor`** — mirror of `SourceEditRouter`
6. **`RouteEditRouter.razor`** — same
7. **`AddMqttDestination.razor`** Edit-mode adoption (WizardShell + hide Routing + Edit save path)
8. **`AddOpcUaServerDestination.razor`** same
9. **`AddRoute.razor`** Edit-mode adoption (WizardShell + source-change chip + sink delta)
10. **`Sinks.razor` + `Routes.razor`** Edit pencil entry points
11. **Full test run** — `dotnet test --filter "Category!=Flaky"`; 0 warnings, 0 errors
12. **Smoke** through Studio: create sink → edit it → save. Create route → edit it → save. Verify stale-edit banner fires on concurrent edit.
13. **Handoff doc** + PR

---

## 6. Definition of Done

- [ ] All sink/route wizards on WizardShell + WizardActions + WizardValidationBanner
- [ ] `/destinations/{id}/edit` and `/routes/{id}/edit` resolve, hydrate, and save (PUT)
- [ ] Edit button (pencil) visible on Sinks + Routes list pages, navigates correctly
- [ ] Routing section hidden in sink Edit mode
- [ ] Source-change warning chip in route Edit mode
- [ ] Stale-edit 409 banner shown on version mismatch
- [ ] `WizardConfigMergerEditTests` + per-model `HydrateFromExisting` tests green
- [ ] `SinksUpdateApiTests` + `RoutesUpdateApiTests` green
- [ ] Existing Add-path tests unchanged and green
- [ ] `dotnet test --filter "Category!=Flaky"` green
- [ ] Zero new build warnings

---

*v2 locked 2026-05-26. Pending user ratification to begin implementation.*
