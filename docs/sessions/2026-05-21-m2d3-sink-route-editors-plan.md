# M.2d.3 — Sink + route editors (v1 plan)

**Status:** v1 — BRIEF, OPEN QUESTIONS BELOW, pending ChatGPT review pass
**Date:** 2026-05-21
**Roadmap reference:** `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md` §3.7.3
**Hard precondition (BLOCKING):** M.2d.1 shared primitives — plan trail
`docs/sessions/2026-05-21-m2d1-shared-primitives-plan.md`. WizardShell,
WizardActions, WizardValidationBanner, and `EditModeContext` MUST land
on master before this milestone's first edit. .2 ships first by ordering
convention only — no hard `.2 → .3` dependency.
**Estimated size:** ~3-4 days (roadmap §3.7.3).

---

## 1. Goal

Apply M.2d.1's shared primitives (WizardShell, WizardActions,
WizardValidationBanner, `EditModeContext`) to the **two existing sink
wizards** (`AddMqttDestination.razor`, `AddOpcUaServerDestination.razor`)
and the **route wizard** (`AddRoute.razor`), and introduce **Edit mode**
for all three. Edit mode lets an operator open an existing sink or route
in the same wizard surface, see its current values pre-populated, change
them, and Save — producing a draft that the existing Config page then
Validates and Applies. No new protocols, no new sections, no new
backend probes. Existing Test Connection on the MQTT wizard is preserved
verbatim — only its hosting container changes (now under
`WizardActions`).

---

## 2. Hard precondition — M.2d.1 must land first

This plan is a consumer of M.2d.1's primitives. It cites M.2d.1 for:

- `Components/Shared/WizardShell.razor` — section frame + footer
- `Components/Shared/WizardActions.razor` — Save / Cancel / (optional)
  Test Connection slot
- `Components/Shared/WizardValidationBanner.razor` — error + warning
  surface
- `Wizards/EditModeContext.cs` — discriminates Add vs Edit; loads the
  existing entity into the wizard model

If M.2d.1 is not on master at session start, **stop and surface the
branch dependency** per `feedback_handoff_branch_dependencies.md` —
don't speculatively re-build the primitives in this worktree.

### 2.1 NEW precondition (added post-M.2d.2 close-out): mirror-router pattern

M.2d.2 v2 §5.1 locked the **single-resolver-page pattern** for Edit-mode
routing — `Components/Pages/SourceEditRouter.razor` owns the
`/sources/{InstanceId}/edit` route and dispatches to the protocol-
specific wizard (FOCAS2 / Brother HTTP / Modbus TCP) or one of three
non-happy-path panels. Each wizard's `@page` keeps ONLY its
`/sources/new/<protocol>` route. This avoids the Blazor route-
ambiguity bug v1 would have hit (three wizards declaring the same
`@page "/sources/{id}/edit"`).

M.2d.3 v2 MUST adopt the same pattern for sinks and routes:

- `Components/Pages/SinkEditRouter.razor` owns
  `/destinations/{InstanceId}/edit`; dispatches by `ProtocolName`
  (MQTT / OPC UA Server / future Modbus-sink / future HTTP-sink).
  This is the right resolution of open question **Q3** below — it
  was left unanswered in v1; v2 must lock "mirror the source pattern."
- `Components/Pages/RouteEditRouter.razor` owns
  `/routes/{RouteId}/edit`; dispatches to `AddRoute.razor` with
  `EditMode + HydratedConfig`. Even though there's only one route
  wizard today (no protocol fan-out), the resolver page is still
  the right shape so the four-state UX matrix from M.2d.2 v2 §5.2
  (Loading / NotFound / LoadError / Loaded) is consistent across
  Source/Sink/Route surfaces.

The shared panels are reusable as-is:
- `LicenseModuleDisabledPanel`
- `UnsupportedProtocolPanel`
- `WizardNotAvailablePanel`
- `StaleEditWarningBanner` (Edit-mode 409 collision banner)

The optimistic-concurrency contract (`BaseVersionId` round-tripped
through the save request, 409 + `ConfigVersionMismatchDto` with
`ConflictType = "VersionMismatch"`) is the same — Sinks and Routes
need their own `SinksUpdateApi` / `RoutesUpdateApi` PUT endpoints
mirroring `SourcesUpdateApi.cs` (M.2d.2 sub-step 8.5 commit
`4c78506`). The 6-test endpoint pattern (happy / 409 stale /
400 protocol-changed / 404 not-found / route-preservation / 400
id-mismatch) carries forward.

Reference:
- `docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md` §5.1-§5.7
- `docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md` §0.1, §0.2
- `src/ElpisEdgeConnect.Management/Components/Pages/SourceEditRouter.razor`
  (working reference implementation)
- `src/ElpisEdgeConnect.Management/Api/SourcesUpdateApi.cs`
  (working reference endpoint)

---

## 3. Per-wizard work breakdown

### 3.1 MQTT destination — `AddMqttDestination.razor`

| Aspect | What changes |
|---|---|
| Shell | Outer `<MudStack>` + `<MudPaper>` sections wrapped by `WizardShell` (header, numbered sections, footer slot). |
| Footer | `WizardActions` replaces today's inline Save/Cancel `MudStack`. The existing **Test Connection** button (section 4) is unchanged in behaviour — it still POSTs to `/api/v1/sinks/test-connection/mqtt` — but its placement is standardised inside the Test Connection slot of `WizardActions` (open question Q1). |
| Edit mode | `@page "/destinations/{instanceId}/edit"` added alongside today's `@page "/destinations/new/mqtt"`. On Edit, `EditModeContext.LoadAsync(instanceId)` resolves the `SinkInstanceConfig` from `/api/v1/config`, hydrates `MqttSinkWizardModel`, sets `_isEditMode = true`, and disables the InstanceId field (Locked invariant — ids are immutable post-create; see open question Q2). |
| Routing section (§6 of the existing file) | **HIDDEN in Edit mode.** Routing decisions for an existing sink are managed in the Route wizard, not in the sink editor. Surfacing it in Edit would invite "switch this sink's route" semantics that don't exist in Core. |
| Existing Test Connection | Unchanged. Per M.2b.6, the MQTT probe at `/api/v1/sinks/test-connection/mqtt` already exists with the canonical `MqttTestConnectionResultDto` shape (ProbeId + ElapsedMs + ErrorCode + RemediationHint). No backend change. |

### 3.2 OPC UA Server destination — `AddOpcUaServerDestination.razor`

| Aspect | What changes |
|---|---|
| Shell | Same WizardShell adoption as MQTT. |
| Footer | `WizardActions` — **no Test Connection slot** (OPC UA Server is an acceptor; the existing "no probe" caption in the file moves into `WizardActions`' tooltip or a captioned `MudAlert`). |
| Edit mode | `@page "/destinations/{instanceId}/edit"` shares the destination-edit route with MQTT — the Edit endpoint resolves the sink's `ProtocolName` first, then dispatches to the right wizard (open question Q3). |
| Namespace section caution | The existing "do NOT change after deployment" caption on `NodeIdTemplate` becomes **more prominent in Edit mode** — render as `MudAlert Severity="Severity.Warning"` when `EditModeContext.IsEdit` and the operator's draft differs from the applied value. (Polish detail; pinned in M.2d.4 sweep if not done here.) |
| Routing section | **HIDDEN in Edit mode**, same rationale as MQTT. |

### 3.3 Route wizard — `AddRoute.razor`

| Aspect | What changes |
|---|---|
| Shell | Same WizardShell adoption. The wizard's 7 sections (Identity, Source, Destinations, Buffer, Filter, Transforms, Delivery) become `WizardShell` numbered sections. The `MudExpansionPanels` for Filter / Transforms / Delivery are preserved within the section frame. |
| Footer | `WizardActions` — **no Test Connection slot** (a route is a logical fanout, not a connection; there's nothing to probe). |
| Edit mode | `@page "/routes/{routeId}/edit"`. `EditModeContext.LoadAsync(routeId)` hydrates `RouteWizardModel` + `RouteFilterEditorModel` + `RouteTransformsEditorModel`. RouteId disabled in Edit. |
| Source picker behaviour in Edit | Source picker still renders the full cards list, but the currently-bound source is pre-selected and an inline warning chip ("Changing the source disconnects this route from `<old-source>` and rebinds to `<new-source>`") appears when the operator selects a different card. Open question Q4. |
| Sink multi-select in Edit | The current checkbox model is preserved (no drag-reorder, no priority). Open question Q5 covers whether sink-list order matters operationally. |
| Filter + Transforms editors | UX polish (banner unification, validation copy parity) is deferred to **M.2d.4 cross-wizard sweep** per roadmap §3.7.4 — this milestone only changes the framing, not the editor internals. |

---

## 4. Edit-mode mechanics for sinks — "in use" semantics

A sink is **in use** when at least one `RouteConfig` in the current applied
configuration references it via `SinkInstanceIds`. Sinks can be edited
while in use, but the semantics must be explicit:

- **InstanceId** — immutable, full stop. Disabled field in Edit mode.
- **ProtocolName** — immutable. (Implicit — the wizard type is bound to
  the protocol.)
- **Enabled flag** — flipping Enabled in Edit is fine; the wizard
  presents a confirmation step in the Draft Summary when toggling
  Enabled=true → false while routes still reference the sink ("Routes
  `<r1>, <r2>` reference this sink and will be unable to deliver until
  it is re-enabled"). The toggle is allowed; the Apply-side validator
  enforces fanout-integrity rules.
- **Broker host / port / TLS / auth (MQTT)** — editable. Test Connection
  uses the **edited** values, not the applied ones. The operator can
  point the sink at a different broker; routes referencing the sink
  continue to fanout, but their target endpoint changes on Apply.
- **Endpoint URL / ApplicationUri / NamespaceUri / NodeIdTemplate
  (OPC UA Server)** — editable, but `NodeIdTemplate` carries a louder
  warning per §3.2 because external clients pin against it.

**Validate-before-save:** the wizard's `CanSave()` runs the same model
validators in Edit as in Add. Cross-record invariants (sink-id
referenced by enabled-but-disabled route, etc.) are caught at
`/api/v1/config/drafts` POST time by Core's `CrossRecordValidator` —
same gate as the existing Add path. No new validator surface.

---

## 5. Edit-mode mechanics for routes — cascading-impact semantics

A route's identity is `RouteId`; its primary references are
`SourceInstanceId` (single) and `SinkInstanceIds` (multi). Editing these
has cascade implications:

- **RouteId** — immutable. Disabled in Edit. (Renaming a route would
  break diagnostics history, buffer file paths, and operator muscle
  memory; out of scope for v1.)
- **SourceInstanceId** — editable but **warns**. Switching the source
  rebinds the route's pull-loop; existing per-route buffers persist
  (they're keyed by RouteId, not SourceId) but in-flight data may
  reflect the old source until the pipeline drains. The warning chip
  in §3.3 surfaces this. Open question Q4 — do we require a
  confirmation dialog or is the warning chip sufficient?
- **SinkInstanceIds** — editable. Adding a sink starts fanout to it
  on Apply; removing a sink stops fanout (buffered messages for that
  sink-cursor are retained per blueprint §6 store-and-forward — they
  don't get drained, they age out per `BufferMaxAgeDays`). The Draft
  Summary explicitly enumerates added vs removed sinks.
- **Buffer policy, Filter, Transforms, Delivery** — editable freely.
  These are runtime parameters; Apply pushes the new policy and the
  next pipeline tick uses it.

**Validate-before-save:** identical to §4. The draft round-trips through
`/api/v1/config/drafts` which invokes the same `CrossRecordValidator`
the Add path uses.

---

## 6. Deliverables

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddMqttDestination.razor` | Adopt WizardShell + WizardActions + WizardValidationBanner. Add `@page "/destinations/{instanceId}/edit"`. Honour `EditModeContext`. Hide Routing section in Edit. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SinkWizards/AddOpcUaServerDestination.razor` | Same as MQTT, minus Test Connection (no probe). |
| `src/ElpisEdgeConnect.Management/Components/Pages/RouteWizards/AddRoute.razor` | Same shell adoption + `@page "/routes/{routeId}/edit"`. SinkInstanceIds delta enumeration in Draft Summary. Source-change warning chip. |
| `src/ElpisEdgeConnect.Management/Wizards/MqttSinkWizardModel.cs` | `LoadFromExisting(SinkInstanceConfig existing)` method (hydrates from applied config). |
| `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs` | Same. |
| `src/ElpisEdgeConnect.Management/Wizards/RouteWizardModel.cs` | Same; also needs hydration into `RouteFilterEditorModel` and `RouteTransformsEditorModel`. |
| `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs` | **NEW methods**: `BuildEditedSinkDraft(currentConfig, editedSink)` + `BuildEditedRouteDraft(currentConfig, editedRoute)`. Pure, deterministic, mirror the existing `BuildNewSinkDraft` / `BuildNewRouteDraft` shape. Replace-by-instance-id semantics. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Sinks.razor` | Add an "Edit" action column entry (links to `/destinations/{id}/edit`). |
| `src/ElpisEdgeConnect.Management/Components/Pages/Routes.razor` | Add an "Edit" action column entry (links to `/routes/{id}/edit`). |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/WizardConfigMergerEditTests.cs` | New: BuildEditedSinkDraft + BuildEditedRouteDraft unit tests covering happy paths + invariants (instance-id not changed, route references resolve, fanout integrity). |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/MqttSinkWizardModelEditTests.cs` | LoadFromExisting round-trips fidelity. |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/OpcUaServerSinkWizardModelEditTests.cs` | Same. |
| `tests/ElpisEdgeConnect.Management.Tests/Wizards/RouteWizardModelEditTests.cs` | Same. |

**Test budget:** ~20-25 tests. Roadmap §4.1 allocates ~60 tests across
all 4 M.2d sub-milestones — M.2d.3 takes the largest slice because of
the merger work.

---

## 7. Definition of Done (roadmap §3.7.3)

- [ ] All sink/route wizards on shared shell (WizardShell + WizardActions + WizardValidationBanner).
- [ ] Edit mode green: `/destinations/{id}/edit` and `/routes/{id}/edit` routes resolve, hydrate, save through the same draft endpoint as Add.
- [ ] Edit button visible on `/sinks` and `/routes` list pages.
- [ ] Edit-mode tests green — `WizardConfigMergerEditTests` + per-model `LoadFromExisting` round-trip tests.
- [ ] No regression on existing Add paths — the existing wizard tests (`AddMqttDestination` integration, etc.) keep passing unchanged.
- [ ] `dotnet test --filter "Category!=Flaky"` green.
- [ ] Zero new build warnings.

---

## 8. Step-by-step implementation sequence

1. **Verify precondition.** Confirm M.2d.1 primitives are on master. If not, stop and report.
2. **Add merger methods.** Implement `WizardConfigMerger.BuildEditedSinkDraft` and `BuildEditedRouteDraft` with the same defence-in-depth invariants the new-* variants enforce. Tests first (TDD).
3. **Hydrate wizard models.** Add `LoadFromExisting` to `MqttSinkWizardModel`, `OpcUaServerSinkWizardModel`, `RouteWizardModel` (plus filter + transforms editor models). Round-trip tests.
4. **Adopt WizardShell in `AddMqttDestination.razor`.** No Edit mode yet — just the shell migration. Verify Add path is intact.
5. **Add Edit route to MQTT wizard.** `@page "/destinations/{instanceId}/edit"`, `EditModeContext` wiring, hidden Routing section.
6. **Repeat for `AddOpcUaServerDestination.razor`.** Single PR or split — operator preference; the OPC UA piece is structurally identical to MQTT minus the Test Connection slot.
7. **Adopt WizardShell in `AddRoute.razor`.** Add path stays intact.
8. **Add Edit route to Route wizard.** Source-change warning chip + sink delta in Draft Summary.
9. **Add Edit action to `Sinks.razor` and `Routes.razor` list pages.** Verify navigation.
10. **End-to-end smoke** through Studio (`127.0.0.1:5080`): create a sink, edit it, save draft, validate, apply. Same for a route. Capture into the handoff doc.

---

## 9. Open questions for v2 ratification

### Q1 — Draft → Validate → Apply lifecycle integrity (CRITICAL)

The existing Add wizards POST to `/api/v1/config/drafts` and navigate to
`/config?new=<draftId>` — the operator then clicks Validate, then Apply.
This **already honours** the locked draft → validate → apply → rollback
flow (CLAUDE.md §9 anti-pattern #10). **Edit-via-Wizard should do the
exact same thing:** build the edited entity, call
`WizardConfigMerger.BuildEditedSinkDraft` / `BuildEditedRouteDraft`
(pure, in-memory transform), POST the resulting full
`GatewayConfiguration` to `/api/v1/config/drafts`, navigate to
`/config`. Apply happens via the explicit Apply button on the Config
page, **never silently from the wizard**.

**Confirm in v2:** is this the intended behaviour? It's the
architecturally correct one — no Apply-in-place. The wizard never
mutates the applied config directly; it always produces a draft. This
preserves rollback and matches the locked lifecycle.

### Q2 — InstanceId immutability in Edit mode

Locked in §4 and §5 as immutable. **Confirm.** Renaming a sink or route
would invalidate per-instance diagnostics history, per-route buffer
file paths (`<dataRoot>/buffers/<routeId>.db`), and any external
references (operator runbooks, EREMOS V2 topic subscriptions for
OPC UA later). The architecturally correct rename path is "delete +
create" — a separate, explicit operator action, not a silent wizard
side effect.

### Q3 — Destination-edit URL routing

Two options:

- **Option A:** single `/destinations/{id}/edit` page that loads the
  sink, inspects `ProtocolName`, and dispatches to the right wizard
  component. One URL, polymorphic dispatch.
- **Option B:** protocol-specific edit URLs
  (`/destinations/{id}/edit/mqtt`, `/destinations/{id}/edit/opcua`).
  Symmetric with the Add paths (`/destinations/new/mqtt`,
  `/destinations/new/opcua`).

**Recommendation:** Option A. The operator clicks Edit on a row; the
URL shouldn't require knowing the protocol. Symmetric with the
single Edit button on the list. v2 to confirm.

### Q4 — Route source-change UX

Three options when an operator switches `SourceInstanceId` in Edit:

- Inline warning chip only (today's plan).
- Inline warning chip + confirmation dialog at Save time.
- Disallow — force the operator to delete + recreate.

**Recommendation:** Option 1 (chip only) for v1; the warning is plain
text and the operator still goes through Validate + Apply on the Config
page where the impact is visible in the diff. v2 to ratify or escalate
to Option 2.

### Q5 — Route sink-list editing UX

Routes have multiple sinks (fanout). Is `SinkInstanceIds` editing in
the Edit mode:

- **Multi-select checkboxes** (today's Add behaviour, preserved) — v1 default.
- **Drag-and-drop reorder** — Core's fanout semantics are independent-per-sink, so order **should not matter operationally**. If order is purely cosmetic, drag-and-drop adds polish but no semantic value. Defer to M.2d.4 polish sweep.
- **Add / remove with explicit confirmation** when removing a sink that's actively delivering — out for v1 (the draft Diff on the Config page already shows removed sinks).

**Recommendation:** v1 keeps multi-select checkboxes. Confirm in v2.

### Q6 — Test Connection in Edit mode

When an MQTT sink is edited and the operator clicks Test Connection,
should the probe use the **edited** values (broker host changed in
the field but draft not saved) or the **applied** values? Today's
wizard uses the field-bound values for Test Connection in Add mode,
which means Edit will naturally use edited values too — but this
deserves a v2 confirmation as it's the right semantic ("verify what
I'm about to save", not "verify what's running").

### Q7 — Edit button placement on list rows

M.2b.6.1 added inline Enable/Disable buttons to the Sources / Sinks /
Routes lists. Where does Edit slot in — same column as
Enable/Disable, separate Action column, or contextual menu on the
row? v2 to align with M.2b.6.1's pattern.

### Q8 — Route Filter / Transforms in Edit mode

The Filter + Transforms editors are unmodified by this milestone (per
roadmap §3.7.4). But operators editing a route will inevitably want to
tweak filters and deadbands. Are the current expansion panels usable
enough in Edit, or do we need a "Filter / Transforms changed" indicator
on the WizardShell section header? v2 to scope.

---

## 10. Cross-references

- Roadmap: `docs/sessions/2026-05-21-phase2-wrapup-roadmap-v2.md` §3.7.3
- Predecessor sub-milestone (HARD): `docs/sessions/2026-05-21-m2d1-shared-primitives-plan.md`
- Sibling sub-milestone (ordering only): `docs/sessions/2026-05-21-m2d2-source-wizards-plan.md`
- Successor sub-milestone: `docs/sessions/2026-05-21-m2d4-cross-wizard-sweep-plan.md`
- Existing M.2b.6 wizard plan: `docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md`
- WizardConfigMerger (current `BuildNewSinkDraft` / `BuildNewRouteDraft`): `src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs`
- CLAUDE.md §9 anti-pattern #10 (draft → validate → apply → rollback flow) — the architectural lock that Q1 confirms is honoured.
- ARCHITECTURE_BLUEPRINT.md §6 (store-and-forward per-route buffer keyed by RouteId) — basis for §5's "buffer cursor retention on sink removal" claim.

---

**End of v1 brief plan. Ready for ChatGPT review pass.**
