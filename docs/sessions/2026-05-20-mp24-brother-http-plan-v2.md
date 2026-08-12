# M.P2.4 — Brother HTTP source adapter migration (v2 plan)

**Status:** v2 — LOCKED after ChatGPT review pass (2026-05-20). Reality-check (v3) is the next step before implementation.
**Date:** 2026-05-20
**Branch:** `claude/tender-edison-639a71` (worktree `tender-edison-639a71`)
**Precondition:** Bug 2 P0 fix `0845b4e` merged 2026-05-20 (sink publish path silently dead, RESOLVED).
**Predecessor plan:** [`2026-05-20-mp24-brother-http-plan.md`](2026-05-20-mp24-brother-http-plan.md) (v1).
**Estimated size:** ~2 weeks of focused work (includes demo mode, which is now in scope).
**Test baseline:** 901 → target ~1010+ after M.P2.4.

---

## 1. Goal

Promote `src/ElpisEdgeConnect/DataSources/BrotherHttpDataSource.cs` (legacy `IMachineDataSource` shape) into a new project `src/ElpisEdgeConnect.Sources.BrotherHttp/` implementing `ISourceAdapter`. Match the FOCAS2 migration's line-for-line shape so the new architecture covers both Fanuc and Brother CNCs end-to-end. Hard prerequisite for the 100-CNC customer deployment (mixed Fanuc + Brother, MQTT-only).

---

## 2. Locked architectural invariants

Carry-forward from `ARCHITECTURE_BLUEPRINT.md` Appendix A + `CLAUDE.md` §3, plus the v2-locked invariant added this round:

- **Protocol-agnostic Core.** `ElpisEdgeConnect.Sources.BrotherHttp` references Core, never vice versa.
- **Canonical data model only.** All Brother data is emitted as `CanonicalDataPoint` via `CanonicalDataPointFactory`. No sink-specific shaping in the adapter.
- **Per-adapter isolation.** One Brother adapter instance per CNC; one failing Brother adapter never affects any other adapter, route, or sink.
- **License gating.** Registration is a no-op when `source-brother-http` module is disabled.
- **Error taxonomy.** Brother-specific codes live in `BrotherErrors.cs` under the `BROTHER.*` namespace.
- **NEW LOCK (v2): no legacy DTO leaks.** Legacy `CncMachineData` / `ToolOffsetEntry` / `AlarmData` / `ToolInfoData` (and every other type in legacy `ElpisEdgeConnect.Models`) MUST NOT cross the boundary into `ElpisEdgeConnect.Sources.BrotherHttp` or any project downstream of it (Core, Routing, Sinks). The new adapter parses Brother HTTP responses directly into a private intermediate shape (or directly into `CanonicalDataPoint`s) — it never reuses the legacy DTO. The legacy DTO is permitted to appear ONLY in the test project as a parity-test oracle (see §6 catalog mapping rule). The legacy `src/ElpisEdgeConnect/` codebase itself remains untouched.

---

## 3. Milestone naming — LOCKED

**Name: M.P2.4.** The chip prompt's "M.P2.3" naming is rejected because it collides with the closed hot-reload-seam milestone at commit `545f722`.

Phase-2 platform milestone ledger after v2:

| Milestone | Topic | Status |
|---|---|---|
| M.P2.1 | Fail-soft startup | Closed |
| M.P2.2 | Runtime hot-reload | Closed |
| M.P2.3 | Coordinator cross-record recovery synthesis | Closed (ADR-0010) |
| **M.P2.4** | **Brother HTTP source adapter migration** | **This plan** |

Cross-reference updates (deferred to the v3 reality-check step so they happen in one pass):

- `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` §2 + §6: rename M.P2.3 → M.P2.4
- `docs/sessions/2026-05-20-followup-chips.md` Chip 2: title rename M.P2.3 → M.P2.4

---

## 4. Canonical Brother tag catalog (the central design artifact)

This is the v2's most important addition. Q3 / Q7 / Q8 / Q12 all depend on it, so locking it here unblocks adapter design, wizard design, validator scope, and parity-test fixture design.

### 4.1 Path convention

Hierarchical paths separated by `/`, matching the FOCAS2 `DataPoints` filter convention:

- Operators select a subset by listing leaf paths (`Status/State`) or branch prefixes (`Tools/`, `Maintenance/`) in `BrotherHttpSourceConfiguration.DataPoints`.
- Empty `DataPoints` list = collect everything in the catalog (FOCAS2 parity).
- Prefix matching: `Tools/` matches every `Tools/...` leaf.
- The catalog is fixed — operators do NOT author new paths. Selecting an unknown path emits `BROTHER.UNKNOWN_DATA_POINT` at config-validate time.

### 4.2 The catalog (fixed)

Grouped by source endpoint. Indexes `{n}`, `{slot}`, `{idx}` are emitted only when the corresponding entity is present in the protocol response.

#### From `/HTTPD_MCNINFO`

| Path | Type | Notes |
|---|---|---|
| `MachineInfo/Hostname` | string | Raw `_hostname` (e.g. `BRN68E74A6608EA`) |
| `MachineInfo/Model` | string | Raw `_model` (e.g. `SXd1`) |
| `MachineInfo/StatusCode` | int | Raw 0..5 from the protocol |
| `Status/State` | string enum | Derived: `STOP\|SUSPEND\|OPERATE\|ALARM` — same legacy mapping (0/1/4/5→STOP, 2→SUSPEND, 3→OPERATE; ALARM only when `ParseAlarms` finds a non-informational, non-maintenance alarm) |
| `Status/Running` | string enum | Derived: `Stopped\|Idle\|Hold\|Running\|Standby\|Unknown` per legacy mapping |
| `Status/Mode` | string | Always `MEM` for Brother (legacy parity) |
| `Status/AutoMode` | string | Always `MEM` |
| `Status/EmergencyStop` | bool | Always `false` (Brother HTTP does not expose this) |
| `Status/Warning` | string\|null | Current warning text (set by `ParseAlarms` informational/maintenance branches and by `ParseMaintenanceNotices`) |

#### From `/MNTP_CYCLETIME`

| Path | Type | Notes |
|---|---|---|
| `Program/Active` | string\|null | E.g. `O0926` (legacy `MainProgram`) |
| `CycleTime/Cycle` | double | Seconds (legacy `CycleTimeSeconds`) |
| `CycleTime/Cutting` | double | Seconds (legacy `CuttingTimeSeconds`) |
| `CycleTime/Operation` | double | Hours, 1-dp rounded (legacy `OperationTimeHours`) |
| `CycleTime/PowerOn` | double | Hours, 1-dp rounded (legacy `PowerOnTimeHours`) |
| `CycleTime/EndCounter` | int | Legacy `OperationEndCounter` |
| `CycleTime/CuttingRatioPercent` | int | Legacy `CuttingRatioPercent` |

#### From `/MNTP_WKCNTR`

| Path | Type | Notes |
|---|---|---|
| `Production/PartsCount` | long | First counter's count (legacy `PartsCount`) |
| `Production/Counter1/Count` | int | Emitted when non-zero in legacy |
| `Production/Counter1/Target` | int | Emitted when non-zero in legacy |
| `Production/Counter2/Count` | int | … same pattern for counters 2–4 |
| `Production/Counter2/Target` | int | |
| `Production/Counter3/Count` | int | |
| `Production/Counter3/Target` | int | |
| `Production/Counter4/Count` | int | |
| `Production/Counter4/Target` | int | |

#### From `/ATC_TOOLS`

| Path | Type | Notes |
|---|---|---|
| `Tools/ActiveNumber` | int\|null | Currently-loaded tool number (legacy `activeToolNo`, identified by `#000000`/`#ff8000` color highlight) |
| `Tools/MagazineSize` | int | Number of slot entries parsed |
| `Tools/Magazine/{slot}/Number` | int | Tool number for slot |
| `Tools/Magazine/{slot}/Length` | double | Tool length |
| `Tools/Magazine/{slot}/Radius` | double | Tool radius |
| `Tools/Magazine/{slot}/IsActive` | bool | True only for the active tool |
| `Tools/Magazine/{slot}/Name` | string | Emitted only when non-empty |
| `Tools/Magazine/{slot}/Type` | string | Emitted only when non-empty (e.g. `STD tool`) |
| `Tools/Magazine/{slot}/Life` | string | Emitted only when not `********` and non-empty |

#### From `/ALARM_CURALMLIST`

| Path | Type | Notes |
|---|---|---|
| `Alarms/ActiveCount` | int | Count after filtering out informational + maintenance entries |
| `Alarms/Active/{idx}/Number` | int | Alarm number |
| `Alarms/Active/{idx}/Type` | string | Always `Brother` |
| `Alarms/Active/{idx}/Message` | string | Alarm message |
| `Maintenance/Warning` | string\|null | `;`-joined maintenance-keyword alarms (legacy `MaintenanceWarning`) |
| `Maintenance/WarningCount` | int | Count of maintenance-keyword alarms |

**Filter semantics (preserved from legacy, locked):**

- Informational alarm code 501 (Standby) → does NOT appear in `Alarms/Active/{idx}/*`; sets `Status/Warning` to the message and forces `Status/State=STOP`, `Status/Running=Idle`.
- Maintenance keywords (`GREASING`, `GREASE`, `MAINTENANCE`, `FILTER`, `LUBRICATION`, `LUBRICANT`) → do NOT appear in `Alarms/Active/{idx}/*`; flow into `Maintenance/Warning` and `Maintenance/WarningCount`.
- Otherwise non-empty `Alarms/Active/{idx}/*` forces `Status/State=ALARM` and sets `Status/Warning` to the first alarm message.

#### From `/MNTP_MAINTNOTICE`

| Path | Type | Notes |
|---|---|---|
| `Maintenance/NoticeCount` | int | Count of parsed notices |
| `Maintenance/DueSummary` | string | `;`-joined human-readable summary (e.g. `GREASING XYZ AXIS (Notified)`) |
| `Maintenance/Notice/{idx}/Description` | string | E.g. `GREASING XYZ AXIS` |
| `Maintenance/Notice/{idx}/Condition` | string | E.g. `Z-axis travel distance` |
| `Maintenance/Notice/{idx}/Status` | string | Valid / Invalid |
| `Maintenance/Notice/{idx}/Current` | string | Raw counter value |
| `Maintenance/Notice/{idx}/Limit` | string | Raw limit value |
| `Maintenance/Notice/{idx}/State` | string | Normal / Warning / Overdue |
| `Maintenance/Notice/{idx}/DuePercent` | int | Computed `current * 100 / limit` when limit > 0 |

### 4.3 Catalog totals

- **From `/HTTPD_MCNINFO`:** 9 leaf paths (3 fixed + 6 derived)
- **From `/MNTP_CYCLETIME`:** 7 leaf paths
- **From `/MNTP_WKCNTR`:** 1 fixed + 8 indexed (4 counters × 2 fields, sparse — only emitted when non-zero)
- **From `/ATC_TOOLS`:** 2 fixed + up to 7 × `MagazineSize` indexed paths
- **From `/ALARM_CURALMLIST`:** 2 maintenance-derived + up to 3 × `Alarms/ActiveCount` indexed paths
- **From `/MNTP_MAINTNOTICE`:** 2 fixed + 8 × `NoticeCount` indexed paths

For a typical Brother SXd1 with one program running, no alarms, 3 maintenance notices, 1 tool: ~ 9 + 7 + 1 + 9 (1 tool × 7 fields, sparse) + 2 + (2 + 24) ≈ **~50–55 tags emitted per poll cycle**. For the 100-CNC customer at 80/20 Fanuc/Brother split (~20 Brother CNCs), Brother contributes ~1100 tag emissions per cycle to the soak profile.

### 4.4 Catalog evolution rule

The catalog is fixed within M.P2.4. New fields discovered during reality-check (v3) that map cleanly are folded in; novel categories that don't exist in legacy are deferred to follow-ups. Customer-specific extensions are NOT a v1-customer concern (all 100 CNCs use the same tag set per §0 of the deployment-readiness doc).

---

## 5. Locked Q answers (carry-forward from review verdicts)

| Q | Lock |
|---|---|
| Q1 — Milestone naming | **M.P2.4.** Per §3. |
| Q2 — Customer §7 numbers | Treat 3000 ms polling / 65–75 tags / 80/20 split as **working assumptions**, not locked customer facts. Adapter defaults and in-house soak profile use these numbers; customer lock happens before the 48-hour acceptance test, not before implementation. |
| Q3 — Tag model | **Hybrid fixed catalog + `DataPoints` filter.** Catalog locked in §4. FOCAS2 path-prefix semantics. |
| Q4 — Connection lifecycle | **`HTTPD_MCNINFO` is the health authority.** First successful round-trip during `StartAsync` → `Connected`. 3 consecutive failures (where failure = `HTTPD_MCNINFO` returned null/exception) → `Faulted` with `BROTHER.HTTP_UNREACHABLE`. Other endpoint failures within a poll cycle degrade data fidelity (emit fewer canonical points) but do NOT degrade adapter state. |
| Q5 — Demo mode | **In scope.** Brother needs FOCAS2 parity for dev/sales smoke tests. `IBrotherHttpApi` abstraction with `BrotherHttpHttpApi` (real) + `BrotherHttpDemoApi` (synthetic) + `BrotherHttpDemoModeOptions` (env-var flag), mirroring FOCAS2's pattern. |
| Q6 — Collectors | **One collector per endpoint.** Six collectors: `MachineInfoCollector`, `CycleTimeCollector`, `WorkCounterCollector`, `AtcToolsCollector`, `AlarmCollector`, `MaintenanceCollector`. |
| Q7 — Tag validator | **No `BrotherHttpTagValidator` class.** Validation is "does each `DataPoints` entry resolve to a known leaf or prefix in §4 catalog?" — lives inline in `BrotherHttpSourceConfiguration` validation (mirrors how FOCAS2 validates `DataPoints`). |
| Q8 — Parity test | **Mediated through the §4 canonical mapping.** Wording locked: "legacy parsed DTO mapped through the v2 canonical catalog equals new adapter canonical output against the same input bytes." No raw legacy-DTO equality assertions. Test mapping function lives in the test project only — not in the adapter (per the §2 no-leak lock). |
| Q9 — HttpClient lifetime | **`IHttpClientFactory` via DI registration.** 100 CNCs × 3 s poll = ~33 HTTP req/s sustained per gateway, with bursts during reconnect storms — pooled sockets are mandatory. |
| Q10 — Polling cadence | Default `PollIntervalMs = 3000`. Hard minimum `500` (validation rejection `BROTHER.POLL_TOO_FAST`). Soft warning at `< 1000` (validation issue at Warning severity, accepts but flags). |
| Q11 — License catalog file location | **Deferred to v3 reality-check.** v2 does NOT commit to a file path; v3 reads the catalog and updates this plan inline before implementation starts. |
| Q12 — Wizard scope | **Minimum-viable but complete.** Connection (BaseUrl/Host/Port/Timeout), polling cadence, `DataPoints` selector (multi-select against §4 catalog), Test Connection button (fires `HTTPD_MCNINFO`). Match M.2b.6 destination-wizard ergonomics; no extra fields beyond FOCAS2 wizard scope. |
| Q-MTC — MTConnect | **Out of scope.** Tracked separately as a follow-up Phase 2 milestone (no number assigned yet). Flagged in the M.P2.4 handoff. |

---

## 6. Parity-test framing — LOCKED (replaces v1 wording)

The chip prompt's acceptance criterion "same input data, legacy `BrotherHttpDataSource` vs new `BrotherHttpSourceAdapter`, identical canonical-point output" is preserved in spirit but **the comparison flows through the §4 canonical mapping**, not via raw legacy-DTO equality.

```
            Brother HTTP response bytes
                 (fixture file)
                 │
        ┌────────┴─────────┐
        ▼                  ▼
   Legacy parser       New collectors
   → CncMachineData    → CanonicalDataPoint[]
        │
        ▼
   §4 canonical mapping  (test-only function)
   → CanonicalDataPoint[]
        │
        └─────► assertEquals (set)  ◄──── new adapter output
```

**Locked rule:** the §4 canonical mapping function lives in `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/LegacyCanonicalMapper.cs`. It MUST NOT appear in the production project. The legacy `CncMachineData` type itself appears only in test code (the test project transitively references `src/ElpisEdgeConnect/` for the parser + DTO; the production `Sources.BrotherHttp` project does not).

**Fixture corpus:** captured Brother HTTP responses under `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Samples/` — one folder per machine-state scenario (running, idle, alarm, standby, maintenance-overdue, offline-HTTPD_MCNINFO-only). Each scenario contains six `.txt` files, one per endpoint. v3 reality-check captures these samples by running the legacy parser against representative responses (the legacy code's source comments document the exact wire shapes).

---

## 7. Scope / deliverables — v2 locked list

Mostly unchanged from v1; demo mode confirmed in scope; license catalog file location flagged for v3.

| File | Status | Notes |
|---|---|---|
| `src/ElpisEdgeConnect.Sources.BrotherHttp/ElpisEdgeConnect.Sources.BrotherHttp.csproj` | new | Mirror Focas2.csproj |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceConfiguration.cs` | new | Typed config + `FromSourceInstance` factory; `LicenseModuleKey = "source-brother-http"` const; `ProtocolNameConstant = "brother-http"` |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpSourceAdapter.cs` | new | `ISourceAdapter` implementation; lifecycle per Q4 |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpConnectionManager.cs` | new | Owns "ever connected" flag + consecutive-failure counter |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/IBrotherHttpApi.cs` | new | Abstraction over the 6 endpoint calls |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpHttpApi.cs` | new | Real implementation via `IHttpClientFactory` per Q9 |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoApi.cs` | new | Per Q5 — synthetic responses for the 6 endpoints |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoModeOptions.cs` | new | Env-var + static flag, FOCAS2 pattern |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/MachineInfoCollector.cs` | new | One per endpoint per Q6 |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/CycleTimeCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/WorkCounterCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/AtcToolsCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/AlarmCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/Collectors/MaintenanceCollector.cs` | new | |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherErrors.cs` | new | `BROTHER.HTTP_UNREACHABLE`, `BROTHER.ENDPOINT_PARSE_FAILED`, `BROTHER.POLL_TOO_FAST`, `BROTHER.UNKNOWN_DATA_POINT`, plus others surfaced in v3 |
| `src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs` | new | §4 catalog as code (path list + emit predicates) |
| `src/ElpisEdgeConnect.Host/Adapters/BrotherHttpRegistrationExtensions.cs` | new | DI registration matching `Focas2RegistrationExtensions`; license-gated; `IHttpClientFactory` registration |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` | edit | Add `services.AddBrotherHttpSourcesFromGatewayConfig(...)` |
| `<license catalog file>` | edit | Add `SourceBrotherHttp = "source-brother-http"` — exact file deferred to v3 reality-check (Q11) |
| `docs/licensing/module-catalog.md` | edit | Document new module |
| `src/ElpisEdgeConnect.Management/Wizards/BrotherHttpSourceWizardModel.cs` | new | Q12 scope; mirror FOCAS2 wizard model |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddBrotherHttpSource.razor` | new | Connection + polling + DataPoints selector + Test Connection |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddSource.razor` | edit | Add Brother HTTP picker card |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/` | new project | ≥100 tests, ≥80% coverage; lifecycle, config round-trip, per-endpoint parsers, DataPoints validator, Connecting→Connected→Faulted state machine, parity-via-canonical-mapping (§6) |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Samples/<scenario>/*.txt` | new | Captured Brother HTTP responses (running/idle/alarm/standby/maintenance/offline) |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/LegacyCanonicalMapper.cs` | new | Test-only `CncMachineData → IReadOnlyList<CanonicalDataPoint>` per §4 catalog; the parity oracle |
| `tests/ElpisEdgeConnect.Management.Tests/BrotherHttpSourceWizardModelTests.cs` | new | Wizard model FromSourceInstance round-trip + validation |
| `ElpisEdgeConnect.sln` | edit | Add two new projects |
| `docs/sessions/2026-05-20-100-cnc-deployment-readiness.md` | edit | M.P2.3 → M.P2.4 rename; promote chip's Q2 numbers into §7 as working assumptions with `[customer-lock-pending]` annotation |
| `docs/sessions/2026-05-20-followup-chips.md` | edit | Chip 2 title rename |

---

## 8. Out of scope (explicit guardrails — unchanged from v1)

- No semantic change to Brother behaviour vs legacy.
- No MT-LINKi or MTConnect migration.
- No CSV bulk-import for Brother.
- No Studio Live Tag Watch integration (M.2c).
- No wizard polish beyond FOCAS2/Modbus baseline (M.2d will sweep).
- No new architectural decisions about adapter SDK shape — strict mirror of FOCAS2 + Modbus precedents.
- No changes to Core, Routing, Buffer, or Sink behaviour.
- No changes to license signing or activation flow. Just adding a new module key.

---

## 9. Risks and mitigations

| # | Risk | Mitigation |
|---|---|---|
| 1 | Q2 customer §7 numbers turn out wrong → in-house soak profile misaligned | Numbers documented as `[customer-lock-pending]` in §7 of deployment-readiness doc; locked before 48-hour acceptance test |
| 2 | Parity oracle (§6 `LegacyCanonicalMapper`) drifts from production §4 catalog | Single source of truth: `BrotherTagMap.cs` defines the catalog; both the production collectors AND the parity mapper consume it. Mismatch = compilation fail. Reality-check v3 confirms this is achievable without circular refs. |
| 3 | Brother quirks visible only against real hardware surface at install time | v3 reality-check reads ALL legacy Brother-touching code, not just `BrotherHttpDataSource.cs`. Verify `BrotherHttpSettings`, `DataSourceFactory`, `MachineConfig`, registration paths, and any per-model quirks (`SXd1` vs `R450` etc.). |
| 4 | Demo mode synthetic responses don't represent enough real-world variation | v3 captures the same fixture corpus used for parity testing AND for demo cycling; demo cycles through 5–6 scenarios (running / idle / alarm / standby / maintenance-overdue / offline) to give wizard testers realistic state transitions |
| 5 | `IHttpClientFactory` registration interaction with existing host wiring | v3 reality-check reads the existing DI bootstrap; if there's a per-source `HttpClient` lifetime convention I missed, surface before code lands |
| 6 | License catalog file location (Q11) not where I assumed → registration extension breaks build | v3 reads the file explicitly; deliverables row marked TBD in §7 stays TBD until then |
| 7 | M.2d Edit-via-Wizard later refactors all wizards | Accepted. Brother wizard ships at FOCAS2/Modbus baseline. |
| 8 | Side-by-side parity fails because of legacy non-determinism (dictionary ordering, async race) | Test asserts on sets, not lists. Parity mapper canonicalizes ordering by canonical path before comparison. |
| 9 | §4 catalog is incomplete — legacy emits a field I missed | v3 reality-check re-walks `BrotherHttpDataSource.cs` line-by-line cross-referenced against the catalog table; any field touched in legacy that doesn't map to a catalog path is escalated to "fold into v2 catalog" before implementation |
| 10 | 100-CNC soak surfaces a Brother-specific resource leak (sockets, file handles, async tasks) not seen at single-machine scale | Bug 2 hardening already added `TaskScheduler.UnobservedTaskException` handler at composition root; verify Brother adapter is not introducing fire-and-forget Tasks that could escape it |

---

## 10. Sequence of work — v2 locked outline (v3 may refine after reality-check)

| Step | What | Gate |
|---|---|---|
| 0 | v3 reality-check (pre-impl): read all legacy Brother-touching code + license catalog (Q11) + ISourceAdapter contract surface; fold any gaps into §4 catalog or §7 deliverables | Pause-point if §4 catalog needs amendment |
| 1 | Cross-reference doc edits: M.P2.3 → M.P2.4 in deployment-readiness §2/§6 + chips doc Chip 2 title | Mechanical; no review needed |
| 2 | Project skeleton + sln registration + namespace placeholder test | Build clean; baseline 901 → 902 |
| 3 | `IBrotherHttpApi` + `BrotherHttpHttpApi` (real, via `IHttpClientFactory`) + `BrotherHttpDemoApi` (synthetic) + `BrotherHttpDemoModeOptions` | Demo-real dispatch test green |
| 4 | `BrotherTagMap.cs` (§4 catalog as code) | Tests assert catalog completeness against §4 table |
| 5 | Six collectors (one per endpoint, Q6) | Per-endpoint parser tests against §6 sample fixtures green |
| 6 | `BrotherHttpSourceConfiguration` + `FromSourceInstance` factory + DataPoints validator (Q7 inline) | Config round-trip tests + validation tests green |
| 7 | `BrotherHttpConnectionManager` + `BrotherHttpSourceAdapter` lifecycle (Q4) | State machine tests + per-adapter isolation tests green |
| 8 | Parity oracle (`LegacyCanonicalMapper`) + parity test (§6) | Parity test green against all sample scenarios |
| 9 | `BrotherErrors.cs` + finalize error taxonomy from steps 3–8 surfaced codes | Error code stability test green |
| 10 | DI registration (`BrotherHttpRegistrationExtensions`) + `EdgeConnectComposition` edit + license module key | License-gate no-op test green; instance materialization test green |
| 11 | Studio wizard (`BrotherHttpSourceWizardModel` + `AddBrotherHttpSource.razor` + `AddSource.razor` picker + Test Connection) | Wizard model tests green; manual Studio smoke against demo mode |
| 12 | License catalog file edit (Q11 location confirmed in step 0) + `docs/licensing/module-catalog.md` update | Build clean |
| 13 | Full solution regression sweep | All-projects test pass; zero warnings; coverage ≥80% on new project |
| 14 | Manual end-to-end Studio smoke: add Brother source via wizard (demo mode), wire to MQTT sink, verify canonical-point flow → `mosquitto_sub` | Confirms invariant from Bug 2 holds for the new adapter |
| 15 | Commit + handoff doc + plan-trail finalization (v3 reality-check log + v2 amendments captured) | PR opens; deployment-readiness §2 marks Brother HTTP migration row complete |

Estimate: ~10 working days. Demo mode (step 3) and parity test (step 8) are the two slots most likely to expand.

---

## 11. Definition of done

- [ ] M.P2.4 naming applied throughout (deployment-readiness doc + chips doc + this plan trail).
- [ ] All new tests green; ≥80% coverage on `src/ElpisEdgeConnect.Sources.BrotherHttp/`.
- [ ] Zero new warnings (TreatWarningsAsErrors enforced).
- [ ] Full solution test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"`.
- [ ] **Parity test (§6) passes:** legacy parser → `LegacyCanonicalMapper` (test-only) → canonical-point set ≡ new adapter canonical-point set against every sample scenario fixture.
- [ ] No production code in `src/ElpisEdgeConnect.Sources.BrotherHttp/` references legacy types from `ElpisEdgeConnect.Models` (no-leak invariant verified by `using` audit + namespace check).
- [ ] License gate verified: registration is a no-op when `source-brother-http` module is disabled.
- [ ] Brother source can be added through Studio wizard end-to-end (manual smoke against demo mode).
- [ ] Demo mode dispatch verified: `BrotherHttpDemoModeOptions.IsEnabled=true` causes adapter to use `BrotherHttpDemoApi`; off causes `BrotherHttpHttpApi`.
- [ ] Polling cadence clamps verified: validation rejects `<500ms` with `BROTHER.POLL_TOO_FAST`, warns `500..1000ms`, accepts ≥`1000ms`.
- [ ] Plan trail captured: v1 → v2 (this) → v3 reality-check → implementation handoff. All dated files under `docs/sessions/`.
- [ ] Cross-reference: deployment-readiness §2 (Brother HTTP migration row) marked complete with PR link; chips doc Chip 2 marked closed.
- [ ] No legacy DTO leaks: spot-check the new project's compiled assembly does not reference `ElpisEdgeConnect.Models.dll`.

---

## 12. Pause-point criteria

Stop and report if:

- v3 reality-check reveals §4 catalog is incomplete in a way that materially changes the wizard or the parity test design (not just adding a few more leaf paths).
- v3 reality-check reveals legacy Brother has cross-cutting dependencies that would force a Core change to migrate cleanly.
- License catalog (Q11) lives in a file/structure that doesn't match the Phase 2 conventions inferred from FOCAS2.
- `IHttpClientFactory` doesn't compose cleanly with the existing per-source DI lifetime (e.g., adapter instances are transient but `IHttpClientFactory` requires scoped).
- ISourceAdapter contract has acquired new required members since FOCAS2 migrated that I'd miss by mirroring it.
- The parity oracle (`LegacyCanonicalMapper`) can't be built without circular references between test and production projects.
- The customer §7 numbers (Q2) get explicit customer answers that materially diverge from the working assumptions (e.g., polling cadence is 100 ms not 3 s, changing the topology + soak design).

---

## 13. Knock-on / next-session items

After M.P2.4 closes:

- Chip 3 (bulk-provision tooling) — `template-brother.json` becomes real (was stub).
- Chip 4 + Chip 5 (Bug 1 buffer path + EDGECONNECT_CONFIG_DIR) — independent; can proceed in parallel.
- 7-day in-house soak (§5 of deployment-readiness) — unblocked once Brother demo mode + parity test green.
- §7 customer answers — should be locked with the customer before the in-house soak begins (regardless of any §7 update done during this milestone).
- MTConnect migration (Q-MTC) — next Phase 2 milestone if a customer needs it; flag in handoff.

---

**v2 LOCKED. Next step: v3 reality-check pass — read all legacy Brother-touching code, confirm license catalog location, confirm ISourceAdapter contract surface, confirm Q4 connection-manager pattern is compatible with the no-socket nature of HTTP, fold any §4 catalog gaps surfaced by line-by-line legacy walk. Implementation does NOT start until v3 closes.**
