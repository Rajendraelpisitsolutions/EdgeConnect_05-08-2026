# M.2b.6.2 — Smoke-driven wizard hardening v1 plan

**Status:** **v1 DRAFT** — awaiting user scope confirmation. ChatGPT review pass is **optional** per kickoff §5; default is to lock at v1 and proceed to implementation.
**Date:** 2026-05-20
**Form:** First-pass plan in the project's plan-trail discipline. Lighter cadence than M.2b.6.1 — v1 resolves the five open questions from the kickoff and locks file-by-file deliverables; v2/v3 are reserved for the case where v1 review surfaces architectural concerns (kickoff §5 "reserve the right to promote").

**Inputs:**
- [M.2b.6.2 kickoff](2026-05-20-mp2b62-smoke-driven-ux-hardening-kickoff.md) — scope locked at §1, deliverables sketched at §3, open questions at §4, cadence at §5, anti-silent-scope-expansion at §6
- [Platform principles](../platform-principles.md) — particularly **P6** (operational product, not developer tool) and **P2** (shared interaction primitives)
- [M.2b.5/2b.6 v3 plan](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md) — origin of **Locked N** (eager-validation composition discipline)
- [M.2b.6.1 v3 plan](2026-05-19-mp2b61-inline-enable-disable-plan-v3.md) — precedent for cadence + plan structure

---

## 0. Why this v1 plan exists (one paragraph)

The kickoff already locked scope and motivation. The v1 plan's job is to **convert kickoff §4's five open questions into Locked decisions**, refine the deliverables sketch into something an implementer can pick up cold, and lock a Definition of Done so milestone exit is unambiguous. Nothing about scope changes here. If scope expands during implementation, that's a v2 amendment per kickoff §6, not a quiet absorption.

---

## 1. Scope (re-asserted from kickoff §1, locked)

**In scope** — three independent surfaces:

- **A. Modbus wizard tag-table cross-validation** — per-row inline error when datatype byte-width and byteOrder length disagree, plus the bit-class and string-class exclusions. Save button disabled while any tag row has an error.
- **B. Studio surfaces active config path** — startup-log banner line; Config-page caption (with copy-to-clipboard); override chip when an env var is in effect. No path-edit UI.
- **C. Modbus wizard port helper-text** — one-line copy change to call out the simulator-friendly port without referencing a private artifact.

**Out of scope** — Locked deferrals from kickoff §1, do not relitigate:

| Deferral | Goes to |
|---|---|
| Cross-validating wizard fields BEYOND datatype/byteOrder | M.2d Edit-via-Wizard or its own follow-up |
| Migrating Modbus wizard to a shared tag-table primitive | M.2e Shared List Infrastructure |
| Config-path EDIT via UI | Never — the path is environment-controlled by design |
| Retrofitting cross-validation to other source wizards (FOCAS2 / S7 / MTConnect) | Each protocol's own follow-up |
| Helper-text revisions to OTHER wizard fields (host, timeouts, retries) | Out of MVP scope; revisit if operator data shows friction |

---

## 2. Position relative to existing architecture (no new architecture introduced)

M.2b.6.2 piggybacks on architectural pieces M.2b.1 / M.2b.5 / M.2b.6 / M.P2.1 already established. **No new contracts, no new lifecycle, no new pipeline behaviour.**

| Reused piece | Where it comes from | How M.2b.6.2 uses it |
|---|---|---|
| `ModbusTagValidator.Validate(tag, pathPrefix, errors)` | `src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTagValidator.cs` | Wizard validator calls it per row (see §3.A and §4 Q1) |
| `ModbusTagDefinition` record | same project | Wizard maps `ModbusTagWizardRow` → `ModbusTagDefinition` for the validator call |
| `ValidationIssue` | Core's `ElpisEdgeConnect.Core.Adapters` | Issues produced by the shared validator render in the Razor table |
| `CurrentConfigVersionDto` + `/api/v1/config/version` | `src/ElpisEdgeConnect.Management/Api/ConfigApi.cs` + `Contracts/Config/HistoryEntryDto.cs` | Extended with `ConfigPath` + `Override` fields — no new endpoint |
| `HostOptions.ResolvedDataRoot` + `ConfigurationStorageLayout` | `src/ElpisEdgeConnect.Host/HostOptions.cs` + `src/ElpisEdgeConnect.Core/Configuration/ConfigurationStorageLayout.cs` | Resolved path source-of-truth; Studio reads it via the existing `HostOptions` DI registration |
| `HostStartup.LoadConfiguration` phase | `src/ElpisEdgeConnect.Host/HostStartup.cs` line 121 | Adds one `_logger.LogInformation("Configuration loaded from: {Path} (override: {Source})", …)` line after the existing identity-resolved banner |

The single newcomer this plan considers is a **project reference from `ElpisEdgeConnect.Management` to `ElpisEdgeConnect.Sources.ModbusTcp`**. That is the one architectural call worth flagging — see §4 Q1.

---

## 3. Deliverables (file-by-file, locked)

### 3.A — Modbus wizard tag-table cross-validation

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Management/ElpisEdgeConnect.Management.csproj` | edit | Add `<ProjectReference Include="..\ElpisEdgeConnect.Sources.ModbusTcp\..." />`. Architecturally clean (Locked rule #11 forbids protocol↔protocol references, not Management→protocol). |
| `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` | edit | Add `static IReadOnlyList<ValidationIssue> ValidateTag(ModbusTagWizardRow row, int rowIndex)` — maps the wizard row to a `ModbusTagDefinition`, calls `ModbusTagValidator.Validate(...)`, returns the issues. **Composition not duplication** — the byte-width/byteorder rules live exactly once, in `ModbusTagValidator`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` | edit | (1) Compute per-row issues into a `Dictionary<int, List<ValidationIssue>>` in `_tagIssues`. (2) Apply `Error="@HasError(idx, "ByteOrder")"` + `ErrorText="@FirstError(idx, "ByteOrder")"` directly on the Datatype + ByteOrder `MudSelect` cells (and Scale field — see §4 Q4). (3) Append `!_tagIssues.Values.SelectMany(v => v).Any()` to `CanSave()`. |
| `tests/ElpisEdgeConnect.Management.Tests/ModbusSourceWizardModelTests.cs` | edit | Add ~10 new `Theory` rows covering the matrix: uint16+CDAB → invalid; float32+AB → invalid; uint32+ABCD → valid; bool+(any byteorder) → invalid; string16+(any byteorder) → invalid; HoldingRegister+bool → invalid; Coil+uint16 → invalid; Coil+bool → valid; valid happy-path control. Each test asserts the issue code (`MODBUS.CONFIG_INVALID`) and field path. |

**No changes** to `ModbusTagValidator.cs` itself. The fact that we can wire it in without modification is the strongest signal Locked N is satisfied.

### 3.B — Studio surfaces active config path

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Host/HostStartup.cs` | edit | After `_configManager.InitializeAsync(...)` (line ~122), emit `_logger.LogInformation("Configuration loaded from: {Path} (source: {Source})", currentConfigPath, configPathSource)` where `configPathSource` is `"env:EDGECONNECT_DATA_ROOT"` or `"default"`. Resolve the path locally via `new ConfigurationStorageLayout(_options.ResolvedDataRoot).CurrentConfigPath` — same construction used in `EdgeConnectComposition`. |
| `src/ElpisEdgeConnect.Management/Contracts/Config/HistoryEntryDto.cs` | edit | Extend `CurrentConfigVersionDto` with two optional fields: `string? ConfigPath` and `string? Override` (nullable — `null` means default, non-null contains the env-var name). |
| `src/ElpisEdgeConnect.Management/Api/ConfigApi.cs` | edit | In the existing `GET /api/v1/config/version` handler (line ~66), compute `host.ResolvedDataRoot` + the override-source string and include both in the response DTO. Zero new endpoints. |
| `src/ElpisEdgeConnect.Management/Components/Pages/Config.razor` | edit | Under the "Active configuration" `MudPaper` (line ~79), add a `MudText Typo="Typo.caption"` with the path in monospace, a small `MudIconButton` invoking `IJSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", _current.ConfigPath)`, and an inline `MudChip` showing `"Override: EDGECONNECT_DATA_ROOT"` when `_current.Override is not null`. |
| `tests/ElpisEdgeConnect.Management.Tests/ConfigApiConfigPathTests.cs` *(new)* | new | Two tests: (1) `GET /api/v1/config/version` with no env var → `Override` is null, `ConfigPath` matches `host.ResolvedDataRoot`. (2) With `EDGECONNECT_DATA_ROOT` set → `Override` is `"env:EDGECONNECT_DATA_ROOT"`. Uses the same `WebApplicationFactory`-style harness already used by `ConfigApiTests`. |

### 3.C — Modbus wizard port helper-text

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` | edit (same file as A) | Change `HelperText="Default 502"` → `HelperText="TCP port. 502 for production Modbus TCP devices; 5020 if you're using a local Modbus simulator."` (see §4 Q5). |

**Estimate:** ~120–160 LOC of implementation, ~12–14 new tests. One focused implementation session.

---

## 4. Open questions — resolved

### Q1 — Where does the byte-width metadata live, and how does the wizard compose with it?

**Discovery:** `ModbusTagValidator.Validate(ModbusTagDefinition tag, string pathPrefix, List<ValidationIssue> errors)` at `src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTagValidator.cs:39` **already implements every cross-validation rule in §1.A** — datatype/byteorder byte-count match (line 108–119), bit-class restrictions (line 87–105), byteorder-on-bit-class rejection (line 122–130), scale/offset eligibility (line 133–141). It also produces structured `ValidationIssue`s with stable error codes (`MODBUS.CONFIG_INVALID`) and field paths.

**Decision (Locked):** **The wizard maps `ModbusTagWizardRow` → `ModbusTagDefinition` and calls `ModbusTagValidator.Validate(...)` directly.** No new validation logic in `Management`. This is the textbook Locked-N composition — the same rules run in three places (adapter startup, CSV importer, wizard) with one implementation.

**Architectural ripple:** `ElpisEdgeConnect.Management.csproj` gains a `<ProjectReference>` to `ElpisEdgeConnect.Sources.ModbusTcp`.

- **Locked rule #11** (CLAUDE.md §3): "Referencing a protocol module from another protocol module" — forbidden. **Management is not a protocol module.** The locked rule constrains protocol↔protocol; Management→protocol is permitted by the architecture and is the natural place protocol-specific wizards live (the wizard model itself already encodes Modbus knowledge).
- **Assembly-load isolation test** (Management.Tests): enforces "Blazor Components consume the Management REST API only — never Core/Host services directly." The wizard model is a POCO consumed by a Razor page, but the **page does not reach across to call adapters at runtime**; it calls a pure static method. Test impact is verified in §6.
- **Alternative considered and rejected:** duplicate the byte-width/byteorder mapping inside `ModbusSourceWizardModel` (≈10 lines). Rejected because (a) it violates Locked N, (b) the next time the adapter adds a datatype the wizard would silently drift, (c) the cost is one ProjectReference line in an already-multi-project solution.

**Surface for plan review:** the project-reference is the only architectural call in this milestone. If the user prefers Option B (wizard-local helper, no project reference) for assembly-graph reasons, this becomes a v2 amendment input — say so explicitly during plan review.

### Q2 — Config-path API surface

**Discovery:** `GET /api/v1/config/version` already takes `HostOptions host` as a parameter and computes `new ConfigurationStorageLayout(host.ResolvedDataRoot)` for the size-on-disk lookup. The resolved path is already in scope at the handler.

**Decision (Locked):** **Extend `CurrentConfigVersionDto` with two optional fields, populate them from the existing handler.** No new endpoint, no new DTO, no DI changes on the Razor side. The Config page already polls `/version` every 10 seconds, so the path refreshes for free.

### Q3 — Override-chip granularity (`EDGECONNECT_DATA_ROOT` vs `EDGECONNECT_CONFIG_DIR`)

**Discovery:** `EDGECONNECT_DATA_ROOT` controls the resolved path via `hostOptions.DataRoot`. `EDGECONNECT_CONFIG_DIR` is read into `hostOptions.ConfigDirectory` (EdgeConnectComposition line ~95) but **`ConfigurationStorageLayout` is constructed from `host.ResolvedDataRoot`, which always falls back to `DataRoot` and never consults `ConfigDirectory`**. `EDGECONNECT_CONFIG_DIR` is therefore **currently inert** — it changes a field that nothing reads for path resolution.

**Decision (Locked):**
- M.2b.6.2 surfaces **only `EDGECONNECT_DATA_ROOT`** in the override chip. The `Override` field is the env-var name (e.g. `"EDGECONNECT_DATA_ROOT"`) or `null` for default.
- Spawn a separate follow-up chip (NOT in M.2b.6.2 scope) to either (a) wire `EDGECONNECT_CONFIG_DIR` through to `ConfigurationStorageLayout` so it actually does something, or (b) delete the inert env-var read from `EdgeConnectComposition` + `HostOptions.ConfigDirectory`. That decision belongs in its own focused session.

### Q4 — Tag-table inline error rendering

**Discovery:** The Modbus wizard uses `MudSimpleTable` with each cell containing a `MudTextField` / `MudSelect` / `MudNumericField`. MudBlazor's form-control components support `Error="@bool"` + `ErrorText="@string"` props natively — they render a red underline plus a small caption below the control. No row-spanning kludge required.

**Decision (Locked):**
- Add `Error` + `ErrorText` props **directly on the per-cell `MudSelect` (Datatype, ByteOrder) and `MudNumericField` (Scale, when scale-on-bool is rejected)** corresponding to the field path returned by `ModbusTagValidator`.
- One `_tagIssues[idx]` lookup populates each cell's `Error`/`ErrorText`. The first issue with a matching field-path wins (rare to have >1 per field; if it happens the others fall to the snackbar on Save).
- **No `MudAlert` blocks** inside or around the tag table — keeps the layout dense. The `CanSave()` gate is the operator's signal that something needs attention.

### Q5 — Port helper-text wording

**Decision (Locked):** Final wording is
> `"TCP port. 502 for production Modbus TCP devices; 5020 if you're using a local Modbus simulator."`

Drops "the bundled test simulator" per the kickoff's concern about referencing a private artifact. "Local Modbus simulator" reads naturally to a production operator who has never opened our integration-tests folder, and remains accurate for the developer running our `tests/ElpisEdgeConnect.Integration.Tests/ModbusSimulator/server.py`.

---

## 5. Test plan

| Surface | Tests |
|---|---|
| **3.A — cross-validation** | `ModbusSourceWizardModelTests`: extend with ~10 new theory rows. Matrix: each datatype × valid/invalid byteorder length, bit-class with/without byteorder, scale+bool rejection, happy-path control. Each test asserts `code == "MODBUS.CONFIG_INVALID"` and the field path. Composition assertion: the test calls `ModbusSourceWizardModel.ValidateTag(...)`, not the underlying `ModbusTagValidator.Validate(...)` directly — proves the wizard is wired through. |
| **3.B — config-path API** | `ConfigApiConfigPathTests` (new): no-env-var → `Override` is null + `ConfigPath` matches; env-var set → `Override == "EDGECONNECT_DATA_ROOT"`. Reuses the existing test harness style from `ConfigApiTests`. |
| **3.B — log-line banner** | One added assertion to `HostStartupTests` (existing) — the `LoadConfiguration` phase emits a log entry whose message contains `"Configuration loaded from"`. Uses the same captured-logger pattern other startup tests use. |
| **3.C — helper text** | No automated test. Visual smoke per kickoff §5. |

**Total new tests:** ~12–14. **Net delta to test count:** ~14 new, 0 modifications to existing tests.

---

## 6. Risks and mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| Assembly-load isolation test rejects the new `Sources.ModbusTcp` reference | Low | The test (`AssemblyLoadIsolationTests` or similar in `ElpisEdgeConnect.Management.Tests`) enforces _Blazor Components_ not reaching adapters at runtime, not the project graph. The wizard model is a POCO. **Verification step in implementation:** run that test first after adding the ProjectReference; if it fails we'd flip to Option B (wizard-local helper) — a clean v2 input. |
| `ModbusTagValidator.Validate(...)` produces error messages aimed at JSON / CSV consumers (path like `TagDefinitions[3].ByteOrder`) that read awkwardly in a wizard UI | Medium | The wizard renders only `issue.Message`, not `issue.Path`. The messages from `ModbusTagValidator` are operator-readable already ("Tag 'foo' byte-order CDAB is 4-byte but datatype Bool is 2-byte"). If smoke shows any message reads poorly we revise the message at the source (one place, three callers benefit). Out of scope: changing the path-prefix convention. |
| Override-chip wording on Config page reads as a warning when it's actually informational | Low | Render the `MudChip` with `Variant="Variant.Outlined"` and `Color="Color.Info"` — distinguishes "fact about your environment" from "something is wrong". |
| Adding a log line at the `LoadConfiguration` phase introduces a regression in startup-ordering tests | Low | The phase already emits `_observer.OnStartupPhase(StartupPhase.LoadConfiguration)`; we're only adding a logger call after `InitializeAsync()` completes — same point the existing `_logger.LogInformation("Gateway identity resolved: ...")` line emits its banner. Pattern-matched against precedent. |

---

## 7. Smoke verification (manual, post-implementation)

Per kickoff §5 — three independent smokes, one per surface. Run sequentially:

1. **A (cross-validation):** Open Add Modbus source wizard → enter valid identity + host → in tag table, choose datatype `uint16` then byteorder `CDAB` → expect inline error caption under ByteOrder cell + Save button disabled. Switch byteorder to `AB` → expect error to clear + Save enabled. Repeat for `float32 + AB` (invalid), `string16 + AB` (invalid), `HoldingRegister + bool` (invalid).
2. **B (config-path display):** With no env-var → restart Studio → Config page shows `<dataRoot>/config/current.json` caption, no override chip. Set `EDGECONNECT_DATA_ROOT=D:\edgeconnect-dev` → restart → Config page shows the new path + an `Override: EDGECONNECT_DATA_ROOT` chip. Click the copy-icon → clipboard contains the path string. Open the gateway's stderr log → grep for `"Configuration loaded from"` → one line per startup.
3. **C (port helper text):** Open Add Modbus source wizard → hover the Port field → tooltip / helper-text reads `"TCP port. 502 for production Modbus TCP devices; 5020 if you're using a local Modbus simulator."`.

A passes are the milestone exit gate. If any smoke fails for reasons not anticipated in §6, treat as v2 input.

---

## 8. Definition of Done

- [ ] All three surfaces (§3.A, §3.B, §3.C) merged behind a single PR
- [ ] All new tests green, all existing tests still green (`dotnet test --filter "Category!=Flaky"` clean)
- [ ] Zero analyzer warnings, zero build errors (`TreatWarningsAsErrors=true` everywhere)
- [ ] Assembly-load isolation test still passes after the ProjectReference is added (§6 row 1)
- [ ] Manual smoke pass per §7 with screenshots/log-snippets attached to the PR description
- [ ] `EDGECONNECT_CONFIG_DIR`-inertness follow-up chip spawned (§4 Q3 second decision)
- [ ] Handoff note at `docs/sessions/2026-05-21-mp2b62-handoff.md` (assuming next-day landing) captures: what merged, what smoked clean, the spawned follow-up, and any v2 amendments if applicable

---

## 9. Cadence locked

Per kickoff §5:

1. **v1 plan (this file)** → user review → lock OR amend.
2. **v2 amendment** → only if v1 review surfaces an architectural concern (e.g. user prefers Option B over Option A in Q1). Otherwise skip.
3. **Reality check** → SKIP. The three surfaces touch known files; no architectural unknowns to investigate.
4. **Implementation** → single focused session. Pause-and-report on any tradeoff not enumerated above.
5. **Smoke** → §7.
6. **Handoff** → §8 final bullet.

**ChatGPT review pass:** **optional**, default skip. Invoke only if the user wants a second opinion on the project-reference decision in §4 Q1.

---

## 10. Anti-silent-scope-expansion (carried forward from kickoff §6)

> Any tradeoff surfaced during implementation that isn't covered by this v1 plan produces a v2 amendment file, not a quiet absorption into the implementation PR.

Examples of what would be silent scope expansion (do NOT do without v2):

- "While in `ModbusSourceWizardModel`, I'll also validate UnitId range" — no. Address range, unitId range, scan-rate range are out of scope (M.2d).
- "While adding the override chip, I'll also auto-detect non-writable `dataRoot` and surface a warning" — no. Filesystem health is a separate concern (M.2k Diagnostics polish).
- "While extending `CurrentConfigVersionDto`, I'll also surface gateway-id + license info" — no. Each surface its own milestone.
- "While touching `HostStartup`, I'll fix the inert `EDGECONNECT_CONFIG_DIR` env-var" — no. That's the spawned follow-up chip per §4 Q3.

When in doubt: pause, surface, ask.

---

## 11. Acceptance signal for plan lock

User says one of:
- "v1 looks good, proceed" → plan locked, implementation can start.
- "amend X" → v2 with the specific amendment, no implementation yet.
- "back to scope" → kickoff revisit; plan paused.

**End of M.2b.6.2 v1 plan. Awaiting user review before implementation starts.**
