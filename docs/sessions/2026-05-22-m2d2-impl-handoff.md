# M.2d.2 implementation handoff — 2026-05-22

**Status:** Steps 2-6 landed on `claude/m2d2-impl`. Steps 7-12 await a fresh session.
**Branch:** `claude/m2d2-impl` (pushed; no PR opened — preserving the "one big PR at end" plan).

This document is the cold-start brief for the next Claude session picking up M.2d.2 implementation. Read this **before** opening any wizard file.

---

## 1. What landed on `claude/m2d2-impl`

Six commits off `origin/master` (`1a1c10f` — M.2d.2 v2 plan merge):

| Commit | Step | What |
|---|---|---|
| `ed17a31` | 2 | Optimistic-concurrency primitives — `ConfigVersionMismatchDto`, `WizardConfigMerger.BuildUpdatedSourceDraft`, `EditModeContext.BaseVersionId` + factory overload, 15 tests |
| `0b37422` | 3 | Brother HTTP probe — service + endpoint + DTO + 21 tests. Closes M.P2.4 Q12. |
| `a2f8f65` | 4 | Modbus TCP probe — service + endpoint + DTO + ladder + 20 tests. `FluentModbus` 5.2.0 added to Management csproj. |
| `2ab31fe` | 5 | 4 shared panels — `LicenseModuleDisabledPanel`, `UnsupportedProtocolPanel`, `WizardNotAvailablePanel`, `StaleEditWarningBanner`. |
| `61e3a9b` | 6 | `SourceEditRouter` — single `/sources/{InstanceId}/edit` route, 4-state dispatch, hydration banner, BaseVersionId capture + 30 tests. **Render-wizard branch is a placeholder until Steps 8-10.** |

Cumulative: **+86 new tests** (537 → 623 in `ElpisEdgeConnect.Management.Tests`). 0 warnings, 0 errors. Baseline build clean.

---

## 2. What remains — Steps 7-12

### 2.1 Step 7 — Wizard model hydration helpers

Add `public static {WizardModel} HydrateFromExisting(SourceInstanceConfig)` to each:

- `src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs` (287 lines)
- `src/ElpisEdgeConnect.Management/Wizards/BrotherHttpSourceWizardModel.cs` (244 lines)
- `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` (379 lines)

Round-trip tests: `hydrate(config) → model → re-emit() → byte-equivalent SourceInstanceConfig`. **Modbus per-tag list is the tricky case** — preserve the tag order and per-tag attributes exactly.

Read the existing `BuildSourceInstanceConfig()` (or equivalent emit method) in each model to mirror its shape inversely.

### 2.2 Step 8-10 — Wizard shell adoption

Three large Razor files to refactor:

- `AddFocas2Source.razor` (748 lines) — also reroute Browse Controller through `WizardActions.TestConnectionSlot` with `"Browse Controller"` label override; preserve rich axes/tag-count panel.
- `AddBrotherHttpSource.razor` (370 lines) — add Test Connection slot wired to `/api/v1/sources/browse/brother-http` (already shipped).
- `AddModbusSource.razor` (646 lines) — add Test Connection slot wired to `/api/v1/sources/browse/modbus` (already shipped) + transient `ModbusProbeOverrides` disclosure for fallback FC / address / quantity.

For each wizard:

1. Wrap existing layout in `WizardShell` + `WizardSection`.
2. Accept new parameters: `EditModeContext? EditMode` and `SourceInstanceConfig? HydratedConfig`.
3. When `EditMode?.IsEdit == true`, hydrate the model on first render via `HydrateFromExisting`.
4. Render Save through the appropriate path:
   - Add mode → existing flow (BuildNewSourceDraft + apply)
   - Edit mode → POST to a NEW endpoint that:
     a. Verifies `EditMode.BaseVersionId == IConfigurationManager.CurrentVersionId`
     b. On match → calls `WizardConfigMerger.BuildUpdatedSourceDraft` → applies
     c. On mismatch → returns 409 + `ConfigVersionMismatchDto` → wizard renders `StaleEditWarningBanner`

**A new API endpoint is needed for Edit-mode save.** Not yet implemented. Suggested shape:

```
PUT /api/v1/sources/{instanceId}
Body: { sourceConfig: SourceInstanceConfig, baseVersionId: string }
Response: 200 (applied draft) | 409 (ConfigVersionMismatchDto) | 400 (validation)
```

This endpoint can live alongside Edit-mode wizard wiring — it's a step-8 sub-task, not a separate step.

### 2.3 Step 11 — Edit buttons

- `SourceDetail.razor` (274 lines) — add "Edit" button → `/sources/{instanceId}/edit`.
- `Sources.razor` (494 lines) — add inline Edit action on the row (parity with M.2b.6.1 Enable/Disable inline).

Small changes; should be quick once Steps 8-10 land.

### 2.4 Step 12 — Classifier-trust integration test

`EditMode_CosmeticOnlyChange_DoesNotRestartSource` integration test:

1. Arrange: existing focas2 source in a running test gateway
2. Act: drive the Edit flow that changes only `DeviceName` (cosmetic field — should not require restart per `RuntimeReloadClassifier`)
3. Assert: post-apply `ReloadOutcomeDto.AffectedInstances` does NOT include the source's `InstanceId`

Goes in `tests/ElpisEdgeConnect.Integration.Tests/`. Test fixture pattern: see existing `EremosV2EndToEndTests.cs` for the in-process gateway harness.

### 2.5 Steps 13-14 — Smoke + docs + PR

- Manual end-to-end smoke verification in Studio (user-driven; can't be automated here).
- Update `docs/sessions/2026-05-21-mp24-handoff.md` §6 — close Q12 Test Connection deferral (already implemented in Step 3 above; needs the doc-update commit).
- Update `docs/sessions/2026-05-21-m2d3-sink-route-editors-plan.md` — add cross-reference to mirror-router pattern from §5.1 of the M.2d.2 v2 plan (M.2d.3 v2 precondition).
- Open the PR.

---

## 3. Critical contract points the next session must respect

### 3.1 What Step 6's router already established

`SourceEditRouter.razor` is the entry point. It:
- Loads `GatewayConfiguration` and finds the source by `InstanceId`.
- Captures `_baseVersionId` from `/api/v1/config/version`.
- Resolves the UX state and renders one of four branches.
- For the `RenderWizard` branch, it has a **placeholder** awaiting Steps 8-10.

**Where Steps 8-10 must hook in:** In `SourceEditRouter.razor`, the `UxState.RenderWizard` switch case currently renders a `MudPaper` placeholder. Replace each protocol's case with the actual wizard component invocation:

```razor
case UxState.RenderWizard:
    @switch (_source.ProtocolName)
    {
        case "focas2":
            <AddFocas2Source EditMode="@EditModeContext.Edit(_source.InstanceId, _baseVersionId!)"
                             HydratedConfig="@_source" />
            break;
        case "brother-http":
            <AddBrotherHttpSource EditMode="..." HydratedConfig="@_source" />
            break;
        case "modbustcp":
            <AddModbusSource EditMode="..." HydratedConfig="@_source" />
            break;
    }
    break;
```

The `ProtocolsWithEditWizard` set in the router (currently `{ "focas2", "brother-http", "modbustcp" }`) is asserted-against in tests — if you add a 4th protocol's wizard, update the set AND the test atomically.

### 3.2 The §5.5 route-preservation invariant

`WizardConfigMerger.BuildUpdatedSourceDraft` (Step 2) has a locked invariant: **the returned config's `Routes` reference is the same as the input's** (byte-identical). Tests pin this:

- `BuildUpdatedSourceDraft_PreservesRoutesByteIdentical`
- `BuildUpdatedSourceDraft_PreservesRoutes_EvenWhenSourceDisabled`

The Edit-mode save endpoint MUST use `BuildUpdatedSourceDraft` for source replacement. Do NOT route through `BuildNewSourceDraft` + "delete-then-add" — that would touch routes.

### 3.3 The §5.4 field mutability table

`SourceInstanceConfig.InstanceId` and `ProtocolName` are **immutable in Edit**. Wizards must:
- Surface InstanceId and ProtocolName as **disabled inputs with tooltip** (not hidden — operators need to see them).
- Refuse to submit a save where either field differs from the hydrated source.

`BuildUpdatedSourceDraft` already enforces this server-side; tests pin:
- `BuildUpdatedSourceDraft_MissingInstanceId_Throws`
- `BuildUpdatedSourceDraft_ChangedProtocolName_Throws`

### 3.4 Optimistic concurrency wire pattern

The Edit-mode save endpoint shape (suggested):

```csharp
// Pseudocode
group.MapPut("/{instanceId}", async (
    string instanceId,
    UpdateSourceRequest request,  // { SourceConfig, BaseVersionId }
    IConfigurationManager configMgr,
    ...) =>
{
    var currentVersion = configMgr.CurrentVersionId;
    if (request.BaseVersionId != currentVersion)
    {
        return Results.Conflict(new ConfigVersionMismatchDto
        {
            BaseVersionId = request.BaseVersionId,
            CurrentVersionId = currentVersion,
            ChangedSinceUtc = configMgr.LastAppliedAtUtc,
        });
    }
    var current = await configMgr.GetCurrentAsync();
    var draft = WizardConfigMerger.BuildUpdatedSourceDraft(current, request.SourceConfig);
    // ... apply draft through existing flow ...
});
```

Wizard fetches the 409 body, populates `StaleEditWarningBanner`, single Reload button → reloads the page (which re-runs `SourceEditRouter.LoadAsync` capturing the new `BaseVersionId`).

### 3.5 Read these BEFORE editing wizards

1. `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` — the most complex wizard; understanding its tag list + Save flow makes the other two easy.
2. `src/ElpisEdgeConnect.Management/Components/Shared/WizardShell.razor` — slot contract.
3. `src/ElpisEdgeConnect.Management/Components/Shared/WizardActions.razor` — Save / Cancel / TestConnection slot contract.
4. `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` — to understand the per-tag list shape before writing `HydrateFromExisting`.

---

## 4. Test count trajectory

| State | Tests |
|---|---|
| Master before M.2d.2 impl | 537 |
| After Step 2 | 552 (+15) |
| After Step 3 | 573 (+21) |
| After Step 4 | 593 (+20) |
| After Step 5 | 593 (presentational, indirect coverage) |
| After Step 6 | 623 (+30) |
| **Step 7-12 target (per v2 §6.5)** | **~85-100 cumulative — ~12-37 more to add** |

Remaining test budget: 3 hydration round-trip tests (one per protocol model) + 3 wizard page-model tests (Add+Edit per protocol) + 1 integration test = ~7-10 tests + whatever the Edit-mode endpoint needs (~5). Roughly 12-15 more tests gets us to the lower end of the §6.5 target.

---

## 5. Recommended fresh-session opening

```
# Read first
cat docs/sessions/2026-05-22-m2d2-impl-handoff.md  # THIS doc
cat docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md  # the locked v2 plan

# Sanity-check state
git fetch origin
git log origin/claude/m2d2-impl --oneline   # should show 6 commits
git checkout -b claude/m2d2-impl-step7 origin/claude/m2d2-impl

# Confirm green baseline
dotnet build ElpisEdgeConnect.sln --nologo 2>&1 | tail -5
dotnet test tests/ElpisEdgeConnect.Management.Tests/ --no-build --nologo 2>&1 | tail -3
# Expect: 623 tests passing

# Begin step 7
cat src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs
# ... etc.
```

When all 12 steps complete and tests are green: rebase / squash as appropriate, push, open the M.2d.2 implementation PR against master.

---

## 6. What I'd do differently on fresh start

Two things slowed me down in this session that the next session can skip:

1. **Reading the MQTT probe service for too long** — Brother followed its pattern closely, but I should have skim-read it for structure rather than line-by-line.
2. **`ModbusException` constructor issue** — FluentModbus's `ModbusException` has restricted constructors. The fix was to introduce a probe-owned `ModbusProbeSlaveRejectedException` for the catch surface. **Next session: don't construct `FluentModbus.ModbusException` directly anywhere.** Use `ModbusProbeSlaveRejectedException` for the slave-rejection signal.

---

**End of handoff. Master + 6 commits = solid foundation. Steps 7-12 are well-scoped from here.**
