# Siemens S7 source wizard (M.2b.2) — handoff (resume at M3)

**Date:** 2026-06-03
**Status:** **M0 (plan) + M1 (mockup) + M2 (backend glue) DONE. Resume at M3
(`AddS7Source.razor` + edit mode).** Goal unchanged: make S7 operator-available
(wizard + tile flip Pending→Available).

## Where it stands

| M | State | Artifact |
|---|---|---|
| Plan v1/v2 (LOCKED) | done | `2026-06-02-s7-source-wizard-plan-v2.md` — **read it + its "M2 build amendments" tail first** |
| M1 mockup | signed off; result-state card updated for the M2 compat split | `2026-05-30-ux-mockups/8-s7-source-wizard.html` |
| **M2 backend** | **DONE — built + tests green** | files below |
| **M3 razor + edit** | **DONE (2026-06-04) — built + tests green** | `AddS7Source.razor`; `SourceEditRouter` wired |
| **M4 tile flip + picker test** | **DONE (2026-06-04)** | `SourceProtocolPickerModel` s7 → Available |
| **M5 e2e verify (tiered)** | **DONE (2026-06-04) — Studio + config-apply + adapter-Running tier** | live run on 127.0.0.1:5080 |

**S7 is now operator-available.** All milestones M0–M5 closed.

### M4 (done) — tile flipped
`SourceProtocolPickerModel` s7: `Available` + `TargetHref="/sources/new/s7"`,
`PendingMilestone` dropped. `SourceProtocolPickerModelTests.S7Tile_IsAvailable_WithExpectedTargetHref`
replaces the old "remains pending" test.

### M5 (done) — tiered verification achieved
Ran the full gateway (`dotnet run` Management) on an isolated temp data root,
endpoints disabled, port 5080. **Required non-hardware bar — all green:**
- **Endpoints live:** `/validate/s7-address` → `DB1.DBW0` valid (suggests Int/Word);
  `DB1.DBZ0` → friendly "DBZ is not a valid DB width…"; `T5` → unsupported
  (`S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS`). `/test-connection` to a bogus host →
  real TCP attempt → `refused` in ~1.5s.
- **Save valid config via draft→validate→apply:** disabled S7 source (wizard's
  default "do not wire yet") — `validate isValid=true`, apply Completed, source
  persisted + round-trips (host/slot/tag intact).
- **Adapter reaches Running without schema errors:** enabled S7 source routed to a
  self-contained OPC UA Server sink — apply Completed, `faulted=[]`,
  `restarted=[s7-m5-press]`, and `/api/v1/sources/s7-m5-press` reported
  `state=Running` (best-effort connect to the bogus host, no error code, no fault).
- **Render smoke:** picker shows S7 (no stale "M.2b.2" label); `/sources/new/s7`
  prerenders clean (no unhandled-error markers), task-first header + Optimized-DB
  wording intact, empty-state CTA shown. ("Scan interval (ms)" header appears only
  once a tag is added — empty wizard correctly shows the CTA instead.)
- **Test-connection success/failure + address parser matrix + compat parity +
  probe outcomes:** green unit tests (Sources.S7.Tests 161, Management.Tests S7 52).

**Tier achieved: Studio + config-apply + adapter-Running (no hardware/simulator).**
NOT exercised: a successful READ from a real S7 PLC or a Snap7/Sharp7 simulator
(none available) — the "preferred/hardware" tier, explicitly optional in plan v2 §M5.
Temp data root used for the run was removed afterward.

### M3 delivered (2026-06-04)

- `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddS7Source.razor`
  — Add (`/sources/new/s7`) + Edit modes, mirroring `AddModbusSource.razor`.
  Source identity → PLC connection (Advanced collapsed: timeouts/retries/breaker/
  planner knobs/OptimizedDbAccess) → tag table (Name·Address·Datatype·Scan interval·
  Status·Actions + per-row More for Unit/Scale/Offset) → wire-into-route + draft
  summary (Add mode). Debounced live address validation via `/validate/s7-address`
  (preview + "Address suggests:" + auto-fill only while datatype untouched);
  per-row Status ✓/⚠/✗ from `ValidateTag`+`TagCompatibility`; planner-rejected
  datatype options **disabled** in the dropdown; clear inline datatype error;
  cancellable Test connection ("Not tested yet" default) + Test-selected-read with
  explicit row selection; real empty-state CTA; no CSV; accessibility wiring
  (aria-label, role=status/aria-live, aria-selected, text-not-colour status).
- `SourceEditRouter.razor` — added `"s7"` to `ProtocolsWithEditWizard` + the
  `case "s7"` render branch. `SourceEditRouterTests` updated (s7 now RenderWizard).
- Build: full solution 0 warnings / 0 errors. Management.Tests 929 pass.
- **Not yet done in M3:** live render smoke of the Blazor page — deferred to M5
  (needs the gateway running). Test surface follows repo convention: POCO model
  tests (M2) + router `ResolveUxState` tests; no per-wizard bUnit (matches
  Modbus/Focas2/MTConnect).

### M4 (next) — flip tile + picker test

In `SourceProtocolPickerModel.cs`, change the `s7` tile to
`Status = Available` + `TargetHref = "/sources/new/s7"` (drop `PendingMilestone`);
update `SourceProtocolPickerModelTests` accordingly. This is the operator-visible
"ship it" switch — gated on M3 tests passing (✓) and the M5 non-hardware path
being defined (✓ in plan v2 §M5).

## What M2 delivered (all green; full solution builds 0 errors)

New files:
- `src/ElpisEdgeConnect.Sources.S7/S7TagCompatibility.cs` — pure shared checker
  for the two planner-rejected datatype/address rules + the narrower-width
  warning. **This is the single source of truth M3 must use** for per-row
  status, Save gating, and Test-read enablement.
- `src/ElpisEdgeConnect.Management/Wizards/S7SourceWizardModel.cs`
  (`+ S7TagWizardRow`) — model, `BuildSourceInstance()`, `HydrateFromExisting()`,
  `ValidateTag()`, `TagCompatibility()`, `Validate()`, static `Datatypes` (14),
  `ConnectionTypes`, `SuggestDatatypes(widthHint)`.
- `src/ElpisEdgeConnect.Management/Api/S7AddressValidationService.cs` (+ request/
  result records) and `S7AddressValidationApi.cs` —
  `POST /api/v1/sources/validate/s7-address` (parser-only, debounce from UI).
- `src/ElpisEdgeConnect.Management/Api/S7ProbeService.cs` (+ DTOs) and
  `S7ProbeApi.cs` — `POST /api/v1/sources/browse/s7/test-connection` and
  `…/test-read` (read-only, license-gated `source-s7`, per host:port:rack:slot
  single-flight).
- DI + endpoints registered in `ManagementHostingExtensions.cs`.
- `Management.Tests` now references `Sources.S7` (for the fake `IS7Client` +
  `FromSourceInstance` parity).

Tests: `S7TagCompatibilityTests` (+planner parity), extended `S7AddressParserTests`
(plan-v2 invalid/valid matrix), `S7SourceWizardModelTests`,
`S7AddressValidationServiceTests`, `S7ProbeServiceTests` (fake `IS7Client`).
Sources.S7.Tests = 161 pass; Management.Tests = 930 pass.

## Locked M2 decisions (codified in plan v2 "M2 build amendments")

1. **Planner-rejected combos BLOCK Save** (bit-form requires Bool; datatype not
   wider than address width). Narrower-than-width only **warns**. Wizard reuses
   `S7TagCompatibility` so a wizard-accepted config never fails Initialize.
2. **Timer/Counter unsupported → blocks Save** (`S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS`);
   parser stays permissive, wizard support layer rejects.
3. **OptimizedDbAccess** confirmed informational (Advanced/neutral/off).
   **Duplicate tag names block; duplicate addresses warn.**
4. **"Datatype mismatch" is not a Test-read outcome** — S7 reads are raw bytes;
   test-read = read-ok / read-failed / DB-access-denied (+ connect failures).

## M3 deliverables (task #56) — bound by plan v2 "M1 mockup review — UI punch list"

Build `Components/Pages/SourceWizards/AddS7Source.razor` mirroring
`AddModbusSource.razor`:
- Source identity (name/description; protocol fixed) → Connection (Host/Rack/Slot/
  Port/ConnectionType; **OptimizedDbAccess + timeouts under Advanced**) → tag
  table (Name·Address·Datatype·Scan·**Status**·Actions; Unit/Scale/Offset behind
  a per-row "More").
- **Debounced** live address validation → call the `/validate/s7-address`
  endpoint; render NormalizedAddress + "Address suggests: …" (auto-fill datatype
  only while `Datatype == null`/untouched; never overwrite a manual choice — the
  model already nulls Datatype for this).
- Per-row Status ✓/⚠/✗ from `ValidateTag` (errors) + `TagCompatibility` (warning).
- **Cancellable** Test connection + Test-selected-read (enabled only for a valid
  selected row; "reads once, does not save"). Failure **warns, never blocks Save**.
- Save bar copy operator-facing ("N tags · M invalid…"); no draft→validate→apply
  wording. **No CSV affordance in prod v1.** No T/C examples.
- **Edit mode** via `SourceEditRouter` + s7 → `HydrateFromExisting`; round-trip
  all fields + tag rows; removing a tag persists; invalid edit blocks apply.

**M3 must also apply the 2026-06-03 mockup-review tweaks** — see plan v2
"M3 UI refinements (2026-06-03 mockup review)": Test connection defaults to
"Not tested yet"; Advanced collapsed by default; "Scan interval (ms)"; explicit
row-selection for Test-selected-read; Timer+Counter unsupported state; disable
planner-rejected datatype options in the dropdown; duplicate tag-name state; real
empty-state CTA ("+ Add your first tag"); no CSV affordance in v1; full
accessibility wiring (labels, aria-invalid/describedby, role=status/aria-live on
result chips, text-not-colour status, aria-selected row). Mockup
`8-s7-source-wizard.html` was revised to match.

## M4 / M5 (unchanged from plan v2)

M4: flip `SourceProtocolPickerModel` s7 Pending→**Available** + picker test (only
after M3 green). M5: the **required non-hardware bar** in plan v2 §M5 (config-apply
+ reload/edit + adapter Running + invalid-blocks-Save + mocked `IS7Client` + parser
matrix); Snap7/Sharp7 simulator preferred; record the tier achieved.

## Build / environment notes (unchanged)

- Building **Management** needs the gateway exe **stopped** (bin DLL lock); it was
  not running this session.
- Always `dotnet test` **with build** after editing a referenced project.
- S7 adds no new `GatewaySettings` field, so the bundle-schema / ConfigurationDiffer
  snapshot concerns did not recur (full Management suite stayed green).

## Deferred follow-ups status (2026-06-04)

Picked up the three deferred items. Decisions + state:

- **Demo mode — DONE.** Mirrors FOCAS2 (ADR-0012); recorded in **ADR-0029**.
  `EDGECONNECT_S7_FAKE_MODE` → `S7DemoModeOptions` → `S7DemoClient` (synthetic
  sine-driven bytes, injectable clock) via the production-ctor dispatch;
  `S7FakeModeMeter` gauge `edgeconnect_s7_fake_mode_enabled`; composition
  stderr + `s7.fake-mode.activated` startup event; Studio amber banner
  (`ManagementOptions.S7FakeMode` → `LayoutChromeModel`); per-adapter
  `demoMode` health metric. Tests: `S7DemoModeTests`, `S7DemoClientTests`,
  `LayoutChromeModelTests` (S7 banner).
- **CSV import — backend DONE; wizard UI pending sign-off.** `S7TagCsvImporter`
  + result types (`Sources.S7/Import/`), schema
  `Name,Address,Datatype,ScanRateMs,Unit,Scale,Offset` (Name+Address required;
  datatype derived when blank; scan defaults 1000), strict all-errors-at-once,
  reuses `S7AddressParser`/`S7DatatypeParser`/`S7TagCompatibility`, T/C rejected,
  dup-name blocks / dup-address warns. Tests: `Import/S7TagCsvImporterTests`.
  **Operator chose "importer + in-wizard upload UI."** Per the static-mockup-first
  rule, the affordance mockup `2026-05-30-ux-mockups/9-s7-csv-import.html` is
  **awaiting sign-off** before wiring into `AddS7Source.razor` (in-process import,
  map `S7TagDefinition` → `S7TagWizardRow`, Add-vs-Replace, surface issues).
- **Optimized DB — DEFERRED (operator decision).** Stays the documented
  informational flag (shipped state). True symbol reads need symbolic access
  that Sharp7 (S7comm, absolute-offset) can't do — tracked as **Phase 4.5
  Milestone N** (TIA symbol/offset export import OR S7comm-plus/PUT-GET upgrade;
  ~2–5 wks, needs real hardware). No code change this round.

## Reference

- Plan v2 (+ M2 amendments) — `2026-06-02-s7-source-wizard-plan-v2.md`
- Mockup — `2026-05-30-ux-mockups/8-s7-source-wizard.html`
- ADR-0015 (wizard contract); `AddModbusSource.razor` / `ModbusSourceWizardModel` (templates)
