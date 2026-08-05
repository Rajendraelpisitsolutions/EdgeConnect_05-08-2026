# M.P2.4 — Brother HTTP source adapter migration (v1 plan)

**Status:** v1 — DRAFT, OPEN QUESTIONS BELOW, pending ChatGPT review pass
**Date:** 2026-05-20
**Branch:** `claude/tender-edison-639a71` (worktree `tender-edison-639a71`)
**Predecessor (precondition merged):** Bug 2 P0 fix `0845b4e` (2026-05-20) — sink publish path silently dead, RESOLVED. Brother work was explicitly sequenced behind Bug 2 to avoid inheriting the publish-path defect (see `2026-05-20-followup-chips.md` §sequencing).
**Estimated size:** 1–2 weeks of focused work per the chip prompt; refine after ChatGPT review folds in or trims.
**Test baseline:** 901 passing across 7 projects (Phase 2 entry point). Target after M.P2.4: ~1010+ with ~100 new Brother adapter tests + wizard model tests.

---

## 1. Goal

Promote `src/ElpisEdgeConnect/DataSources/BrotherHttpDataSource.cs` (legacy `IMachineDataSource` shape) into a new project `src/ElpisEdgeConnect.Sources.BrotherHttp/` implementing `ISourceAdapter`. Match the FOCAS2 migration's line-for-line shape so the new architecture covers both Fanuc and Brother CNCs end-to-end. Hard prerequisite for the 100-CNC customer deployment (mixed Fanuc + Brother, MQTT-only).

---

## 2. Architectural framing

**What this is.** A protocol migration — moving an existing, working data source from the legacy code path into the new adapter SDK shape. No semantic redesign of Brother behaviour. No new endpoints, no new tag categories beyond what the legacy supports.

**What this is NOT.**

- NOT a redesign of Brother HTTP semantics. The 6-endpoint surface, the status-code mapping, the "0501 standby is not an alarm" rule, the maintenance-keyword filter — all preserved.
- NOT an MT-LINKi or MTConnect migration. `src/ElpisEdgeConnect.Sources.Focas2/Collectors/MtLinkiCollector.cs` already exists inside the FOCAS2 project, so MT-LINKi is bundled in the FOCAS2 adapter; MTConnect's migration status is separate and out of scope here (flagged as scope question Q-MTC below).
- NOT a wizard/UX polish pass beyond the FOCAS2/Modbus baseline. M.2d Edit-via-Wizard will sweep wizards later.
- NOT a CSV bulk-import feature for Brother. That rolls into the parallel bulk-provision tooling chip (Chip 3).

**Locked invariants that anchor the work** (from `ARCHITECTURE_BLUEPRINT.md` Appendix A and `CLAUDE.md` §3):

- Protocol-agnostic Core — `ElpisEdgeConnect.Sources.BrotherHttp` references Core, never vice versa.
- Canonical data model — all Brother data is emitted as `CanonicalDataPoint` via `CanonicalDataPointFactory`. No sink-specific shaping in the adapter.
- Per-adapter isolation — one Brother adapter instance per CNC; one failing Brother adapter never affects any other adapter, route, or sink.
- License gating — registration is a no-op when `source-brother-http` module is disabled.
- Error taxonomy — Brother-specific codes live in `BrotherErrors.cs` under the `BROTHER.*` namespace.

---

## 3. Milestone naming question (Q1 — surface before anything else)

The chip prompt proposes the name "M.P2.3" for Brother HTTP migration. **This collides with an already-closed milestone.** Git log shows:

- `545f722` (2026-05-17) — `feat(host): M.P2.3 — coordinator synthesizes cross-record recovery`
- Plan file: `docs/sessions/2026-05-17-mp23-plan.md` (the hot-reload seam fix, v2 locked, ADR-0010)

**Recommendation:** name this milestone **M.P2.4**. Phase-2 platform milestones in order:

| Milestone | Topic | Status |
|---|---|---|
| M.P2.1 | Fail-soft startup | Closed |
| M.P2.2 | Runtime hot-reload | Closed |
| M.P2.3 | Coordinator cross-record recovery synthesis | Closed (ADR-0010) |
| **M.P2.4** | **Brother HTTP source adapter migration** | **This plan** |

This plan file is named `2026-05-20-mp24-brother-http-plan.md` reflecting the proposed M.P2.4 rename. If you'd rather rename to a different scheme (e.g., `M.P2.3a Brother`), say so before v2.

Knock-on doc edits if M.P2.4 is accepted:

- `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §2 and §6: rename M.P2.3 → M.P2.4
- `docs/sessions/2026-05-20-followup-chips.md` Chip 2: title rename M.P2.3 → M.P2.4

---

## 4. Open questions (for ChatGPT review pass)

Each question has my recommendation, but is not locked.

### Q2 — Where are the §7 customer answers actually locked?

**Conflict.** The chip prompt (Chip 2 §"Dependencies / ordering") asserts: *"The customer's §7 locked answers in the deployment-readiness doc (now on master after PR #16) — specifically the polling cadence (3s), tag count (~65-75), and Fanuc/Brother split (80/20) — reflect reality."*

But: `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` on master still presents §7 as **open questions**, not locked answers. `git log -20` shows no PR #16 (most recent PRs are #15 M.2b.6.2, #18 Bug 2 fix).

**Recommendation:** Treat the chip's numbers (3s polling / ~65–75 tags / 80/20 split) as the **working assumption** for the test profile and Fanuc-template defaults, AND flag in the kickoff handoff that §7 needs explicit lock with the customer before the 7-day in-house soak runs. Resolve in v2 by either:
- Reading the actual PR #16 if it exists somewhere I'm missing, OR
- Promoting the chip's numbers into §7 of the deployment-readiness doc as part of this work.

### Q3 — Tag model: hierarchical paths vs explicit tag list vs hybrid?

The legacy adapter parses 6 endpoints into a flat `CncMachineData` DTO with named fields (`MainProgram`, `PartsCount`, `CycleTimeSeconds`, `ToolInfo.Offsets[...]`, `ActiveAlarms[...]`) plus an `AdditionalData` dictionary for everything else (`MaintenanceWarning`, `Counter1.Count`, `Tool.{n}.Name`, `Maintenance.{n}.DuePercent`, etc.).

Three options for the new shape:

- **(a) Hierarchical `DataPoints` filter (FOCAS2 pattern).** Operators pick from a known-catalog of paths: `MachineInfo/Hostname`, `MachineInfo/Model`, `Status/RunState`, `CycleTime/Cycle`, `CycleTime/Cutting`, `WorkCounters/Counter1.Count`, `WorkCounters/Counter1.Target`, `Tools/Magazine.{n}.Length`, `Alarms/Active`, `Maintenance/Notices`. Empty list = collect all. Same operator UX as FOCAS2.
- **(b) Explicit tag list (Modbus pattern).** Operators define every emitted tag with name + endpoint mapping. Heavier authoring. Doesn't match the fixed-response nature of Brother HTTP.
- **(c) Hybrid — fixed catalog + DataPoints filter (= FOCAS2 pattern exactly).** Brother emits from a code-defined canonical catalog; operators filter via hierarchical paths. Same as (a).

**Recommendation: (a)/(c).** Brother HTTP is a fixed-response protocol like FOCAS2, not a per-register/per-coil protocol like Modbus. The Modbus tag-list shape would be operator-hostile here — operators don't author Brother tags, the protocol defines them. Adopt FOCAS2's `DataPoints` filter exactly.

Sub-question Q3.a: should we expose the AdditionalData "ToolN.Name" / "MaintenanceN.DuePercent" / "CounterN.Target" entries as hierarchical paths too (collectible by operators) or always-on? Recommend always-on for parity with legacy, no operator filtering at that depth.

### Q4 — Connection lifecycle for a connectionless protocol

HTTP is connectionless. FOCAS2 has a `Focas2ConnectionManager` that owns handle allocation + keep-alive. For Brother, "connected" maps to "first successful `HTTPD_MCNINFO` call returned within timeout."

Three sub-questions:

- **Q4.a — What triggers `Connecting → Connected`?** Recommend: first successful `HTTPD_MCNINFO` round-trip during `StartAsync`. Until then, state is `Connecting`. Mirror FOCAS2's connection manager pattern with a `BrotherHttpConnectionManager` even though there's no socket handle — it owns the "have we ever talked to this CNC successfully" flag and the consecutive-failure counter that drives state degradation.
- **Q4.b — What triggers `Connected → Faulted`?** Legacy: 3 consecutive failures → `MachineStatus.Offline`. Recommend keeping the same threshold (configurable via `FaultThresholdConsecutiveFailures`, default 3) and transitioning to `Faulted` with `BROTHER.HTTP_UNREACHABLE` after that many failures.
- **Q4.c — What about partial-endpoint failures within a single poll cycle?** Legacy: if `HTTPD_MCNINFO` returns null (unreachable), the whole cycle is treated as offline; other endpoint failures are debug-logged but don't fail the cycle. Recommend preserving that behaviour: `HTTPD_MCNINFO` is the keep-alive endpoint; other endpoint failures degrade data fidelity but don't degrade adapter state.

### Q5 — Demo mode (for no-CNC dev / sales): yes or defer?

FOCAS2 has demo mode (`IFocas2Api` abstraction with `Focas2DemoApi`, gated by `Focas2DemoModeOptions.IsEnabled`) so sales and dev work without real CNCs. Brother CNCs are equally inaccessible from a dev box.

**Recommendation: yes, ship Brother demo mode in M.P2.4 scope.** Same `IBrotherHttpApi` abstraction with `BrotherHttpHttpApi` (real) and `BrotherHttpDemoApi` (fake). Costs ~2–3 days of extra work but eliminates a major commissioning-time pain point (you can't smoke-test a wizard against a Brother CNC you don't have access to). Sales/demo parity with FOCAS2 is also valuable. Tradeoff: extends the estimate from 1–2 weeks to ~2 weeks.

If schedule pressure surfaces, this is the most natural defer-to-fast-follow. Flag as: "M.P2.4.1 Brother demo mode" as the fallback.

### Q6 — Collector decomposition

FOCAS2 has 8 collectors (one per data category). Brother has 6 endpoints. Two options:

- **(a) 1 collector per endpoint** — `MachineInfoCollector`, `CycleTimeCollector`, `WorkCounterCollector`, `AtcToolsCollector`, `AlarmCollector`, `MaintenanceCollector`. Natural mapping to the protocol.
- **(b) Fewer collectors grouped by canonical data category** — `StatusCollector` (machine info + alarms), `ProductionCollector` (cycle time + work counters), `ToolCollector`, `MaintenanceCollector`. Less mechanical mapping but cleaner canonical-domain semantics.

**Recommendation: (a) — one collector per endpoint.** Matches FOCAS2 (where collectors are mostly per-call, e.g. `StatusCollector` reads FOCAS2 status, `SpindleCollector` reads spindle). Easier to test in isolation. Easier to add a new endpoint later without refactoring.

### Q7 — Tag validator: does Brother need one?

Modbus has rich tag-level validation (datatype, byte-order, register class, scale/offset compatibility) because operators author per-register tags. Brother tags are NOT operator-authored — they're a fixed protocol catalog. The only operator-authored thing is the `DataPoints` filter list.

**Recommendation: no `BrotherHttpTagValidator` class.** Validation is "does each `DataPoints` entry resolve to a known path in the Brother catalog?" That can live inline in `BrotherHttpSourceConfiguration.ValidateConfigAsync` (or wherever the FOCAS2 adapter does its DataPoints validation today — flag for reality-check in v3). If we discover Brother needs per-tag fields (e.g., per-tag scaling on cycle-time interpretations), revisit.

### Q8 — Side-by-side comparison test fixture: what's the input?

Acceptance criterion #4 in the chip prompt: *"Side-by-side comparison test — same input data, legacy `BrotherHttpDataSource` vs new `BrotherHttpSourceAdapter`, identical canonical-point output. Land this as part of the test suite."*

Two complications:

- Legacy emits `CncMachineData`, new emits `CanonicalDataPoint`. The "identical canonical-point output" comparison only makes sense if we first define the legacy-`CncMachineData` → `CanonicalDataPoint` mapping as the test's ground truth. That mapping IS the canonical catalog from Q3.
- The "same input data" needs captured Brother HTTP response samples. Recommend authoring `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Samples/` with real-shape responses from each of the 6 endpoints (from the legacy code's example formats — `HTTPD_MCNINFO: BRN68E74A6608EA,SXd1,3,01,0,1`, etc.) and use those as the test input.

**Recommendation:** lock the canonical catalog in v2 (Q3 resolution), then write the parity test in two phases: (a) run legacy parser against the samples to capture the `CncMachineData` it produces, (b) run new adapter against the samples and assert canonical-point output matches the v2-locked canonical mapping. The legacy code becomes test-only oracle; it doesn't ship in the new project.

### Q9 — Disposal semantics

Legacy `BrotherHttpDataSource` is `IDisposable` and disposes its `HttpClient`. New adapter needs to match `ISourceAdapter`'s disposal contract — check whether that's `IAsyncDisposable` (likely, per FOCAS2). HttpClient lifetime is also worth thinking about — should the new adapter own its own `HttpClient` (legacy pattern) or use `IHttpClientFactory` (modern pattern, better socket pool reuse at 100-CNC scale)?

**Recommendation:** Use `IHttpClientFactory` via DI registration. At 100 CNCs polling every 3 seconds, socket-pool reuse matters. Legacy's per-source HttpClient pattern is fine at single-digit-machine scale but burns sockets at our target deployment.

### Q10 — Polling cadence default and clamps

Legacy implicitly polls at whatever cadence the supervisor calls `CollectDataAsync`. The new adapter respects `SourceConfiguration.PollIntervalMs` (visible in `Focas2SourceAdapter`). Q2 (customer §7) says 3s. Should we have a minimum-clamp safety floor (e.g., refuse `PollIntervalMs < 500ms` for Brother since it's HTTP not socket-keep-alive) and a default if unspecified?

**Recommendation:** default `PollIntervalMs = 3000` for Brother (matches customer §7 working assumption). Minimum clamp: 500ms; warn at validation time below 1000ms. Surface as `BROTHER.POLL_TOO_FAST` validation issue at 500ms < x < 1000ms.

### Q11 — License module catalog file location

The chip prompt says "Add `source-brother-http` to the module catalog" with target `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs (or wherever the catalog lives)`. Need to verify exact location — `Focas2SourceConfiguration.cs` references `LicenseModuleKeys.SourceFocas2` but I haven't yet read the catalog file. Reality-check in v3.

Also: `docs/licensing/module-catalog.md` documentation update — required per chip; will follow whatever shape exists today.

### Q12 — Studio wizard scope: full-featured or minimum-viable?

The chip says "Studio wizard. Compose tag validation via `BrotherHttpTagValidator` if Brother has per-tag validation needs (decide based on legacy code shape)." Per Q7 above, no `BrotherHttpTagValidator` needed. So the wizard is the simpler shape:

- Connection fields (BaseUrl / Host / Port / TimeoutSeconds)
- DataPoints selector (multi-select from the known catalog from Q3 — could be a categorized checkbox grid)
- Polling cadence
- Backoff (initial / max / multiplier — match FOCAS2 wizard's "advanced" section if it has one)
- Optional "Test Connection" button that fires `HTTPD_MCNINFO` against the configured base URL (mirrors M.2b.6 destination wizards which have Test Connection)

**Recommendation:** match FOCAS2 wizard scope minus FOCAS2-specific fields. Test Connection is in scope (M.2b.6 baseline). DataPoints selector design depends on Q3 resolution.

### Q-MTC — MTConnect migration status (out-of-scope but flag)

`CLAUDE.md` §1 lists MTConnect as a supported protocol. The legacy `src/ElpisEdgeConnect/DataSources/` likely contains an `MTConnectDataSource.cs` too. Is MTConnect already migrated, queued, or unscheduled? Not relevant to the 100-CNC customer (Fanuc + Brother only) but worth knowing for Phase 2 sequencing. **Out of scope for M.P2.4; flag in handoff.**

---

## 5. Scope / deliverables (working list, pending Q resolution)

Adapted from chip prompt; concrete file plan to be refined in v2 after Q answers.

| File | Status | Notes |
|---|---|---|
| `src/ElpisEdgeConnect.Sources.BrotherHttp/ElpisEdgeConnect.Sources.BrotherHttp.csproj` | new | Mirror Focas2.csproj — net8.0, nullable, TreatWarningsAsErrors |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs` | new | Typed config record + `FromSourceInstance(SourceInstanceConfig)` factory; `LicenseModuleKey = "source-brother-http"` const; `ProtocolNameConstant = "brother-http"` |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceAdapter.cs` | new | `ISourceAdapter` implementation. Connecting → Connected → Faulted lifecycle. Pacing-authority responsibility per FOCAS2 pattern. |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpConnectionManager.cs` | new | Owns "have we ever connected" + consecutive-failure counter + degradation policy (Q4) |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/IBrotherHttpApi.cs` + `BrotherHttpHttpApi.cs` | new | Abstraction over the 6 endpoint calls. Allows demo-mode (Q5) |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoApi.cs` | new | If Q5 = yes — synthetic response generator. Drop if Q5 = defer. |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoModeOptions.cs` | new | If Q5 = yes — env-var / static flag matching FOCAS2's pattern |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/MachineInfoCollector.cs` | new | One per endpoint per Q6 |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/CycleTimeCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/WorkCounterCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/AtcToolsCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/AlarmCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/MaintenanceCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherErrors.cs` | new | `BROTHER.*` error catalog (HTTP_UNREACHABLE, ENDPOINT_PARSE_FAILED, POLL_TOO_FAST, etc.) |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs` | new | Canonical catalog of known paths per Q3 |
| `src/ElpisEdgeConnect.Host/Adapters/BrotherHttpRegistrationExtensions.cs` | new | DI registration extension matching Focas2RegistrationExtensions; license-gated |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` | edit | Add `services.AddBrotherHttpSourcesFromGatewayConfig(...)` |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` (verify in v3) | edit | Add `SourceBrotherHttp = "source-brother-http"` |
| `docs/licensing/module-catalog.md` | edit | Document new module |
| `src/ElpisEdgeConnect.Management/Wizards/BrotherHttpSourceWizardModel.cs` | new | Mirror FOCAS2/Modbus wizard models |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddBrotherHttpSource.razor` | new | Studio wizard; Test Connection per Q12 |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddSource.razor` | edit | Add Brother HTTP picker card |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/` | new project | Target ≥100 tests, ≥80% coverage. Adapter lifecycle, config round-trip, per-endpoint parser tests, Connecting→Connected→Faulted state machine, side-by-side parity vs legacy (Q8). |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Samples/*.txt` | new | Captured Brother HTTP response shapes (6 endpoints × multiple states) |
| `tests/ElpisEdgeConnect.Management.Tests/BrotherHttpSourceWizardModelTests.cs` | new | Wizard model FromSourceInstance round-trip + validation |
| `ElpisEdgeConnect.sln` | edit | Add two new projects |
| `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` | edit | Q1 rename M.P2.3 → M.P2.4 if accepted; promote chip's Q2 numbers into §7 if confirmed |
| `docs/sessions/2026-05-20-followup-chips.md` | edit | Chip 2 title rename if Q1 accepted |

**Estimate revision:** chip's 1–2 weeks assumes no demo mode (Q5 = defer). If Q5 = yes, ~2 weeks. If we discover MTConnect needs migration alongside, that's a separate milestone (M.P2.5).

---

## 6. Out of scope (explicit guardrails)

- No semantic change to Brother behaviour vs legacy.
- No MT-LINKi or MTConnect migration (separate milestones / scope).
- No CSV bulk-import for Brother (rolled into Chip 3 bulk-provision tooling).
- No Studio Live Tag Watch integration (that's M.2c).
- No wizard polish beyond FOCAS2/Modbus baseline (M.2d Edit-via-Wizard will sweep).
- No new architectural decisions about adapter SDK shape — strict mirror of FOCAS2 + Modbus precedents.
- No changes to Core, Routing, Buffer, or Sink behaviour. Bug 2 fix is the last touch on those for this customer's gating bugs.
- No changes to license signing or activation flow. Just adding a new module key.

---

## 7. Risks and mitigations

| # | Risk | Likelihood | Severity | Mitigation |
|---|---|---|---|---|
| 1 | Q1 naming collision goes unnoticed → existing M.P2.3 references get clobbered | Already mitigated | High | Surfaced as Q1 above; v2 locks rename. |
| 2 | Q2 customer §7 numbers turn out wrong → in-house soak profile is misaligned | Medium | Medium | Resolve Q2 in v2; promote into §7 or refuse to proceed until customer locks. |
| 3 | Side-by-side parity test (Q8) reveals legacy emitted fields we missed → tag catalog gap | Medium | Medium | Capture endpoint samples + run legacy parser against them in v3 reality-check, BEFORE writing canonical catalog. |
| 4 | Demo mode (Q5) scope creep eats into the 2-week envelope | Low if Q5 deferred / Medium if Q5 = yes | Medium | If demo mode goes in scope, lock its scope tightly (synthetic responses for the 6 endpoints only — no per-CNC modelling). |
| 5 | HttpClient lifetime at 100-CNC scale exhausts socket pool | Medium | High | Q9 — use `IHttpClientFactory` from day one, not per-instance new HttpClient(). Verify in soak. |
| 6 | Brother-specific quirks not visible in legacy code surface only against a real Brother CNC at install time | Medium | High | Reality-check (v3) reads ALL of legacy `src/ElpisEdgeConnect/` Brother-touching code, not just `BrotherHttpDataSource.cs`. Verify `BrotherHttpSettings`, `DataSourceFactory`, `MachineConfig`, registration paths. |
| 7 | License module catalog file is not where Q11 assumes → registration extension won't compile | Low | Low | v3 reality-check reads the catalog file explicitly; v2 will not yet commit to a path. |
| 8 | M.2d Edit-via-Wizard later refactors all wizards and our Brother wizard needs rework | Medium | Low | Accepted. Brother wizard ships at FOCAS2/Modbus baseline. |
| 9 | Side-by-side parity test fails because legacy `CncMachineData.AdditionalData` dictionary has key-order non-determinism | Low | Low | Test compares sets, not lists. Order-independent assertion. |

---

## 8. Sequence of work (placeholder — locks in v2)

This is a draft for shape, not the final order. v2 will lock after Q resolution.

1. **Q resolution (this v1 → ChatGPT review → v2)** — close Q1–Q12 + Q-MTC; lock canonical Brother catalog (Q3) and demo-mode decision (Q5).
2. **Reality check (v3)** — read all legacy Brother-touching code (DataSources, Config models, MachineConfig, DataSourceFactory, registration). Confirm Q11 (license catalog location), Q9 (ISourceAdapter disposal contract), and that no fields are missing from the canonical catalog.
3. **Project skeleton.** csproj + sln registration + smallest-possible passing test (just the namespace).
4. **Config + protocol abstraction.** `BrotherHttpSourceConfiguration` + `IBrotherHttpApi` + `BrotherHttpHttpApi`. Tests: config round-trip via `FromSourceInstance`, license module key wiring.
5. **Collectors (per Q6).** Six collectors, one per endpoint. Tests: per-endpoint parser against captured samples (Q8 fixture).
6. **Adapter lifecycle.** `BrotherHttpSourceAdapter` + `BrotherHttpConnectionManager`. Tests: Created → Initializing → Initialized → Connecting → Connected → Faulted state machine; pacing; per-adapter isolation.
7. **Side-by-side parity test (Q8).** Legacy oracle vs new canonical output. Likely the most valuable test in the suite.
8. **Errors taxonomy + tag map.** `BrotherErrors.cs` + `BrotherTagMap.cs`. Tests: error code stability, tag map round-trip.
9. **DI registration + Host wiring.** `BrotherHttpRegistrationExtensions` + `EdgeConnectComposition` edit. Tests: license-gate no-op when disabled, instance materialization from gateway.json.
10. **Demo mode (if Q5 = yes).** `BrotherHttpDemoApi` + `BrotherHttpDemoModeOptions`. Tests: demo dispatch chosen when flag set; synthetic data shape validity.
11. **Studio wizard.** `BrotherHttpSourceWizardModel` + `AddBrotherHttpSource.razor` + `AddSource.razor` picker. Test Connection button. Tests: wizard model round-trip, validation messages.
12. **License catalog + docs.** `LicenseModuleKeys.cs` + `docs/licensing/module-catalog.md`.
13. **Solution-wide regression sweep.** All-projects test pass; zero warnings; coverage ≥80% on the new project.
14. **Manual end-to-end smoke** through Studio: add a Brother source via wizard (demo mode if available, else a captured-response harness), wire to MQTT sink, verify canonical-point flow ends at `mosquitto_sub`.
15. **Commit + handoff doc.**

Estimate: 10 working days (~2 weeks) if Q5 demo-mode is in scope; 7 working days (~1.5 weeks) if deferred.

---

## 9. Definition of done

- [ ] M.P2.4 naming locked (Q1 resolved); cross-references updated in deployment-readiness doc and chips doc.
- [ ] §7 customer answers status clarified (Q2); chip's numbers either confirmed locked in §7 or working-assumption status flagged for customer conversation.
- [ ] All new tests green; ≥80% coverage on `src/ElpisEdgeConnect.Sources.BrotherHttp/`.
- [ ] Zero new warnings (TreatWarningsAsErrors enforced).
- [ ] Full solution test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"`.
- [ ] Side-by-side parity test (Q8) passes: legacy oracle and new adapter produce identical canonical-point output against captured endpoint samples.
- [ ] License gate verified — registration is a no-op when `source-brother-http` module is disabled (registration-extension test).
- [ ] Brother source can be added through Studio wizard end-to-end (manual smoke against demo-mode or captured-response harness).
- [ ] Plan trail captured: this v1 → review pass → v2 → reality-check → v3 → implementation handoff. All dated files under `docs/sessions/`.
- [ ] Cross-reference: deployment-readiness §2 (Brother HTTP migration row) marked complete with PR link; chips doc Chip 2 marked closed.

---

## 10. Pause-point criteria (stop and report if any of these)

- Q1 (naming) goes unanswered — every other artifact references the wrong milestone name.
- Q2 (customer §7 answers) reveals the customer profile assumptions are materially different from the chip's stated numbers — e.g., polling cadence is 100ms not 3s, which would change the topology recommendation and the demo-mode design.
- Reality-check (v3) reveals legacy Brother has dependencies I didn't anticipate — e.g., shared `DataSourceFactory` patterns that would force a Core change to migrate cleanly.
- Side-by-side parity test (Q8) reveals legacy emits fields that don't have a clean canonical mapping — would force a canonical-catalog redesign mid-implementation.
- M.2d Edit-via-Wizard or another milestone has landed wizard-API changes that invalidate the FOCAS2 wizard pattern I'm copying from.
- License catalog file (Q11) turns out to live in a path that contradicts the Phase 2 conventions I've inferred.
- ISourceAdapter contract has changed since FOCAS2 migrated (e.g., new required method) that I'd miss by copying FOCAS2's shape.

---

## 11. Knock-on / next-session items

After M.P2.4 closes:

- Chip 3 (bulk-provision tooling) can include a `template-brother.json` (Brother stub becomes real).
- Chip 1 (Bug 2 P0) — already closed; no action.
- Chip 4 + Chip 5 (Bug 1 buffer path + EDGECONNECT_CONFIG_DIR) — independent; can proceed in parallel.
- 7-day in-house soak (§5 of deployment-readiness) — unblocked.
- §7 customer answers — should be locked before soak in any case (regardless of Q2 resolution).

---

**End of v1 draft. Awaiting ChatGPT review pass. Twelve open questions + one out-of-scope flag (Q-MTC) need verdicts before v2 locks.**
