# M.2b.3.1 — FOCAS2 demo mode (no-CNC dev testing + sales demos)

**Status:** v1 — DRAFT, awaiting ChatGPT review pass before lock
**Date:** 2026-05-18
**Predecessor:** M.2b.3 ([PR #5](https://github.com/elpisitsolutions/EdgeConnect/pull/5), commit `4406157`) — wizard + Browse Controller probe
**Branch (target):** new branch from `claude/happy-cartwright-465442` (or from `master` post-merge)
**Estimated size:** ~350 LOC code + ~200 LOC tests, single PR
**Test baseline:** 1782 → expected ~1797 after M.2b.3.1 (~+15 tests)

---

## 1. Goal

Add a process-wide **FOCAS2 demo mode** that swaps the real `Focas2NativeApi` for a deterministic **synthetic CNC emulator** at adapter construction. When the toggle is on, every FOCAS2 source — including the Browse Controller probe — uses the emulator instead of the Fanuc DLL.

Two use cases drive this:

1. **Sales demos.** Show the Studio + dashboards + MQTT data flow against a "live" CNC without shipping hardware or coordinating customer site access.
2. **Dev testing.** Click-test the full wizard happy path locally — Browse Controller returns realistic axes + tags, the saved draft can be applied, runtime emits canonical points end-to-end.

### Architectural pin (locked)

**Demo mode is a runtime IMPLEMENTATION choice that swaps one `IFocas2Api` for another.** The `ISourceAdapter` contract is unchanged. `Focas2SourceAdapter` is unchanged in shape; only its production ctor's dispatch is modified to pick the right `IFocas2Api` implementation based on env var. ADR-0011's "discovery is management-plane ephemeral" principle still holds — demo mode applies equally to the throwaway-adapter Browse probe and the live supervisor-driven adapter.

---

## 2. Locked decisions (carried from scope confirmation 2026-05-18)

| # | Decision | Reasoning |
|---|---|---|
| A | **Toggle = process-wide env var** `EDGECONNECT_FOCAS2_FAKE_MODE=true` | User-confirmed. Cleanest mental model for sales demos ("flip switch, everything fake"). Per-source granularity adds complexity without a corresponding demo benefit and fuzzes the prod-safety story. |
| B | **Realism level = full** — animated state machine including axes, spindle, parts counter, run-state cycle, tool changes, periodic alarms, MtLinki diagnostics | User-confirmed. Demos need to look alive AND exercise the full collector surface (alarms screen, MtLinki tab, etc.). Static or minimum-viable would leave large parts of the UI untested in demo. |
| C | **Demo mode lives in `Sources.Focas2`** as a second `IFocas2Api` implementation (`Focas2DemoApi`) | Production code already abstracts over `IFocas2Api` (for `FakeFocas2Api` in tests). Adding a second production implementation keeps protocol-specific behaviour inside the protocol module — consistent with the locked "Core ← Adapters" dependency direction. |
| D | **Demo mode applies symmetrically to runtime adapter AND Browse probe** | A single dispatch in `Focas2SourceAdapter`'s production ctor covers both — the Browse probe's `Focas2BrowseService` constructs an adapter via the same production ctor, so they inherit the dispatch for free. No service-side changes needed. |
| E | **Safety: loud startup warning + Studio UI banner** | Defense against "I forgot demo mode was on in production". Startup logs ERROR-level. Studio renders a sticky banner. Prometheus gauge `edge_connect_focas2_demo_mode_active{value=1}` so monitoring can alert on it. |
| F | **`Focas2DemoModeOptions.IsEnabled` is cached at process start** | Immutable for the process lifetime. Re-reading the env var on every adapter construction enables runtime toggling but adds complexity and a race-window where some adapters are real and some are fake. Cache + restart-to-toggle is simpler and safer. |

---

## 3. Out of scope (explicit guardrails)

- **No `ISourceAdapter` contract change.**
- **No new `SourceCapabilities` flag.** `Focas2DemoApi` reports the same caps as `Focas2NativeApi`.
- **No per-source `fakeMode` config field.** Process-wide only.
- **No demo mode for other protocols.** Modbus / S7 / MTConnect remain real-only in this milestone. Each protocol's demo mode is its own follow-up if/when needed.
- **No runtime toggling.** Env var is read once at process start. Toggling requires restart.
- **No demo personas** (lathe vs machining centre, different axis count). Single canonical profile in v1 — 3-axis machining centre, series 31i-B5. Adding personas is a follow-up.
- **No deliberate-error simulation hooks** (e.g. `?simulateError=connect_failed` on Browse). The demo fake always succeeds. Failure-path UX is already exercised by Browse against an unreachable IP.
- **No demo-only license module.** `source-focas2` covers it. Sales demos run with no license (permissive) or a standard FOCAS2-licensed demo build.
- **No audit-chain "demo-emitted" annotation.** The runtime emits canonical points indistinguishably; the demo-mode signal lives at the process boundary (startup log + banner + metric).
- **No dismissable banner.** Sticky for the session — the whole point is preventing demo-mode-in-production confusion.

---

## 4. Deliverables

### 4.1 Sources.Focas2 — synthetic CNC

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2DemoApi.cs` *(new, ~250 LOC)* | Second `IFocas2Api` implementation. Internal **time-driven** state machine — every `Read*` call computes its return value from `DateTime.UtcNow - _startedAt`. Deterministic given a clock. Models: 3 axes (X/Y/Z) with sinusoidal absolute positions; run-state cycle Reset (10s) → Start (40s, cutting) → Stop (10s) over 60s; spindle speed ramps 0 → 3000 rpm during Start; parts counter increments per Start→Stop transition; tool number cycles T1/T5/T9 every 3 cycles; periodic alarm SV0432 fires for ~5s every 4 cycles then clears; MtLinki: servo temperatures ~35°C ± 3°C noise, all fans OK, batteries OK. `cnc_sysinfo` returns `Series=31i-B5`, `MtType=M` (machining centre). `cnc_statinfo` returns Reset/Start/Stop states matching the run-state cycle. |
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2DemoModeOptions.cs` *(new, ~50 LOC)* | Parses `EDGECONNECT_FOCAS2_FAKE_MODE` env var (truthy: `true`, `1`, `yes`; case-insensitive). Exposes `static bool IsEnabled { get; }` cached at first read. Exposes `static string StartupWarningMessage` for the Host startup banner. Internal test-only `Reset()` for resetting the cache between unit tests. |
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs` *(edit, ~10 LOC)* | Production constructor's `new Focas2NativeApi()` call becomes `Focas2DemoModeOptions.IsEnabled ? (IFocas2Api)new Focas2DemoApi() : new Focas2NativeApi()`. ONE LINE of dispatch. Internal test ctor accepting `IFocas2Api` is unchanged. |

### 4.2 Host — startup warning + metric

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` *(edit, ~20 LOC)* | At the end of `ConfigureRuntimeAsync`, if `Focas2DemoModeOptions.IsEnabled` is true: log an ERROR-level banner ("⚠ FOCAS2 DEMO MODE ACTIVE — all FOCAS2 sources use a synthetic controller. NEVER enable in production."), register a Prometheus gauge `edge_connect_focas2_demo_mode_active{value=1}` (matches existing metric patterns). |

### 4.3 Management — Studio banner via options

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Options/ManagementOptions.cs` *(edit, ~5 LOC)* | Add `bool Focas2DemoMode { get; init; }` — populated by `AddConnectivityStudio` from `Focas2DemoModeOptions.IsEnabled`. Keeps the Razor-to-Core isolation rule intact (the Razor reads `ManagementOptions`, not the env var directly). |
| `src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs` *(edit, ~3 LOC)* | When constructing `ManagementOptions` for DI, set `Focas2DemoMode = Focas2DemoModeOptions.IsEnabled`. |
| `src/ElpisEdgeConnect.Management/Components/Layout/MainLayout.razor` *(edit, ~15 LOC)* | When `ManagementOptions.Focas2DemoMode` is true, render a sticky top banner across all Studio pages: "🧪 **Demo mode active.** FOCAS2 sources are running against a synthetic controller — no real CNC connections. To disable: clear `EDGECONNECT_FOCAS2_FAKE_MODE` and restart EdgeConnect." Warning-amber background. Not dismissable. |

### 4.4 Tests

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2DemoApiTests.cs` *(new, ~180 LOC)* | ~12 tests covering the synthetic CNC's behaviour. See §6. |
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2SourceAdapter_DemoDispatchTests.cs` *(new, ~50 LOC)* | ~2 tests verifying the production ctor's dispatch picks the right `IFocas2Api` based on `Focas2DemoModeOptions.IsEnabled`. |
| `tests/ElpisEdgeConnect.Management.Tests/MainLayoutDemoBannerTests.cs` *(new, optional, ~30 LOC)* | Tiny test verifying the banner-rendering decision is driven by `ManagementOptions.Focas2DemoMode`. May extract a `LayoutChromeModel` POCO to make this testable without bUnit (same pattern as `SourceProtocolPickerModel`). |

### 4.5 Docs

| File | Change |
|---|---|
| `docs/adapter-sdk/focas2-adapter.md` | New "Demo mode" subsection at the top documenting the env var, what the fake emulates, and the safety story. |
| `docs/decisions/0012-focas2-demo-mode.md` *(new)* | Status: Accepted. Captures the env-var-only toggle decision, the safety mechanisms (startup log + banner + metric), and the architectural rationale for "demo is a runtime dispatch choice, not a contract change". |

---

## 5. Sequence of work

| Step | What | Gate |
|---|---|---|
| 1 | **Reality check.** Re-read `IFocas2Api` to confirm every `Read*` method can be backed by a synthetic implementation. Check whether `Focas2NativeApi`'s static-ctor `DllImportResolver` install (Focas2Interop) fires even when only `Focas2DemoApi` is used — if so, decide whether that's acceptable (no real DLL load attempt, just resolver registration) or whether we need to gate. | Verify before writing the demo fake. |
| 2 | Write `Focas2DemoModeOptions` + 2 tests for env-var parsing. | — |
| 3 | Write `Focas2DemoApi` + tests (§6.1). | Internal gate — Sources.Focas2.Tests green. |
| 4 | Edit `Focas2SourceAdapter` production ctor dispatch + dispatch tests (§6.2). | Internal gate — Sources.Focas2.Tests green. |
| 5 | Edit `EdgeConnectComposition` for startup banner + metric. | — |
| 6 | Edit `ManagementOptions` + `ManagementHostingExtensions` + `MainLayout.razor` for the Studio banner. | — |
| 7 | **Full regression gate.** `dotnet build ElpisEdgeConnect.sln` (0 warn / 0 err) + `dotnet test --filter "Category!=Flaky"` solution-wide. Target ~1797. | Final pre-doc sweep. |
| 8 | ADR-0012 + adapter-sdk doc subsection. | — |
| 9 | Manual smoke: set `EDGECONNECT_FOCAS2_FAKE_MODE=true`, run `dotnet run --project src/ElpisEdgeConnect.Management`, verify: (a) startup log fires, (b) Studio banner renders, (c) `/sources/new/focas2` → Browse Controller returns 3 axes + ~50 tags + series "31i-B5", (d) save draft → apply → source comes up Running, (e) routes emit canonical points to Mosquitto (if configured). | Demo readiness check. |
| 10 | Commit + PR. | Phase close. |

---

## 6. Test list (preview — refined during implementation)

### 6.1 `Focas2DemoApiTests` (~12)

1. **`AllocLibHandle_ReturnsSuccessAndNonZeroHandle`** — pin the connect path.
2. **`ReadSystemInfo_ReturnsCanonicalDemoIdentity`** — series 31i-B5, type M, version "1.00".
3. **`ReadAxisCount_Returns3_ForXYZProfile`**.
4. **`ReadAxisNames_ReturnsXYZInOrder`**.
5. **`RunState_CyclesThroughResetStartStop_AcrossTime`** — given an injected `Clock` advancing 70s, observe the full Reset → Start → Stop cycle.
6. **`SpindleSpeed_RampsUpDuringStart`** — speed > 0 only during Start phase.
7. **`PartsCount_IncrementsOnEachStartToStopTransition`** — after N cycles, parts == N.
8. **`ToolNumber_CyclesThroughT1T5T9`** — over 3 cycles.
9. **`AlarmStatus_FiresAndClearsPeriodically`** — every 4 cycles.
10. **`AxisPositions_AreSinusoidalAndBounded`** — `|position| < 200 mm` always.
11. **`MtLinkiTemperatures_StayWithinPlausibleRange`** — servo/spindle temps 30–40°C.
12. **`Deterministic_GivenSameClockSeed_TwoInstancesReturnSameValues`** — pins determinism for repeatable demos.

### 6.2 `Focas2SourceAdapter_DemoDispatchTests` (~2)

1. **`ProductionCtor_WhenDemoModeOn_UsesFocas2DemoApi`** — set env var, construct adapter, call `InitializeAsync` then `BrowseTagsAsync`, assert axes are X/Y/Z (the demo fake's profile, NOT the X/Y/Z fallback from a failed real connect).
2. **`ProductionCtor_WhenDemoModeOff_UsesFocas2NativeApi`** — env var unset, construct adapter, assert `AllocLibHandle` would attempt to load the real DLL (mock via `IFocas2ApiFactory` or assert via `DllNotFoundException` shape in a no-DLL environment).

These tests need careful env-var handling — likely a `[Collection("Focas2DemoMode")]` xUnit collection to serialise them and a fixture that resets `Focas2DemoModeOptions` between tests.

### 6.3 `MainLayoutDemoBannerTests` (optional, ~2)

1. **`BannerRenders_WhenManagementOptionsDemoModeTrue`**.
2. **`BannerHidden_WhenManagementOptionsDemoModeFalse`**.

(May skip if extracting a POCO for this is more ceremony than the test is worth.)

---

## 7. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Operator accidentally leaves `EDGECONNECT_FOCAS2_FAKE_MODE=true` set in production | Three independent loud signals: ERROR-level startup log, sticky Studio banner, Prometheus metric. Monitoring can alert on the metric. |
| Cached `IsEnabled` flag means env var change requires restart | Documented in the adapter-sdk note + the Studio banner text. Acceptable trade-off for simplicity. |
| Test isolation: env-var-based tests interfere with each other | xUnit `[Collection("Focas2DemoMode")]` serialises them. Each test wraps in `try / finally { Focas2DemoModeOptions.Reset(); Environment.SetEnvironmentVariable(...) }`. |
| `Focas2Interop` static DllImportResolver install fires even in demo mode | Step 1 reality-check verifies. Likely harmless — the resolver is installed but never invoked because `Focas2DemoApi` doesn't P/Invoke. If problematic, add a guard. |
| Synthetic CNC's time-driven state machine drifts across long-running demos | Cycle period is 60s; drift is bounded by `DateTime.UtcNow` precision. Demos are typically < 1 hour. Not a real concern. |
| Studio banner conflicts with existing layout chrome | MainLayout edit is small and additive. Verified visually in Step 9 manual smoke. |
| Test 6.2 #2 ("WhenDemoModeOff_UsesFocas2NativeApi") can't run on CI without the Fanuc DLL | Use `DllNotFoundException`-shape assertion (the real path will throw that without the DLL) OR introduce an `IFocas2ApiFactory` seam that's checked without invoking. Lean toward the seam for cleanliness. |

---

## 8. Definition of done

1. `dotnet build ElpisEdgeConnect.sln` — 0 warnings, 0 errors.
2. Full sweep at ~1797 (1782 + ~15 new).
3. Step 7 regression gate green.
4. Setting `EDGECONNECT_FOCAS2_FAKE_MODE=true` and running `dotnet run --project src/ElpisEdgeConnect.Management`:
   - Console shows the ERROR-level startup banner.
   - Studio renders the sticky amber banner on every page.
   - `/sources/new/focas2` → Browse Controller against ANY IP returns success with 3 axes + populated tag list + series "31i-B5".
   - Saving a draft + applying produces a Running FOCAS2 source emitting canonical points.
5. Setting `EDGECONNECT_FOCAS2_FAKE_MODE=false` (or unset) restores production behaviour byte-for-byte (verify by absence of the banner + the Prometheus gauge reads 0).
6. ADR-0012 in place; cross-links ADR-0011.
7. `docs/adapter-sdk/focas2-adapter.md` has the demo-mode subsection.
8. `ISourceAdapter` contract still unchanged (`git diff master -- src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs` is empty).
9. Locked F verified: `Focas2DemoModeOptions.IsEnabled` is read once and cached — grep shows no per-adapter env-var reads.

---

## 9. Pause-point criteria

Stop and report if:

- Step 1 reveals an unexpected coupling (e.g. `Focas2Interop` static-ctor side effects that fire even without real P/Invoke calls).
- The synthetic CNC's time-driven state machine produces values that drift outside the adapter's collector validation rules (e.g. `AlarmCount` going negative).
- The Studio banner conflicts visibly with the existing Auth or Sources page chrome.
- Manual smoke (Step 9) reveals that demo mode + a real FOCAS2 license file produces ambiguous behaviour (license module is enabled, but the fake doesn't need it — should the gate still apply?). Surface to user before merging.

---

## 10. OPEN questions for ChatGPT review

| # | Question |
|---|---|
| Q1 | **Time-driven vs call-driven state machine.** Time-driven (compute from `DateTime.UtcNow`) is more realistic for demos but harder to unit-test (need a `Clock` abstraction). Call-driven (advance state on each `Read*` call) is more testable but less realistic during periods of low polling. Recommendation: time-driven with an injectable `Func<DateTime>` clock for testability. |
| Q2 | **License gating for demo mode.** Currently `source-focas2` license module governs FOCAS2 registration. Should demo mode bypass the license check (so sales can run on unlicensed binaries) or honour it (so demo mode still requires the FOCAS2 license module to be enabled)? Bypassing makes sales-demo distribution simpler; honouring keeps the licensing model coherent. |
| Q3 | **Studio banner: warning amber or info blue?** Amber says "watch out" — appropriate for "you're not seeing real data". Blue says "informational, no problem here" — fits the dev-testing case better. Could go either way; amber is more cautious. |
| Q4 | **MainLayout test approach.** The project has no bUnit. Extract a `LayoutChromeModel` POCO mirroring the SourceProtocolPickerModel pattern? Or skip the test entirely and rely on manual smoke (Step 9)? |
| Q5 | **ADR-0012 framing.** Should it state the "demo is dispatch-level, not contract-level" principle as a load-bearing sentence (mirroring ADR-0011's "discovery is management-plane ephemeral")? This would make a future contributor think twice before adding a contract-level `IsDemo` flag. |
| Q6 | **Prometheus metric naming.** `edge_connect_focas2_demo_mode_active`? Or fold into an existing namespace like `edge_connect_runtime_mode{mode="focas2_demo"}`? The latter is more extensible if other protocols add demo modes later. |
| Q7 | **Step 9 manual smoke: include the Mosquitto path?** The full-pipeline assertion ("routes emit canonical points to Mosquitto") requires a local broker. Should the DoD include this, or split it into a "demo verified end-to-end" follow-up so the milestone can close without broker setup? |
| Q8 | **Demo-mode + the `Focas2ToMqttEndToEndTests` integration test.** That test uses its own `FakeFocas2Api`. With demo mode shipped, should the integration test be rewritten to use `Focas2DemoApi` instead? Reduces test-only code, but increases coupling between test and prod fake. Probably keep separate. |
| Q9 | **Audit-chain visibility.** Should the configuration audit chain note "demo mode active" on entries when the gateway runs in demo mode? Useful for "this customer reported X" debugging, but it's a small surface change with non-trivial implications. |
| Q10 | **Demo + dup IP:Port warning.** If demo mode is on and the operator creates two FOCAS2 sources at `192.168.1.10:8193`, both will be backed by the same `Focas2DemoApi` instance (well, two instances). Does the dup-IP warning still make sense? It does for prod-realism (operator should still see the warning so the muscle-memory transfers) — but should the warning copy be adjusted in demo mode? |

---

## 11. Scope summary

- ~250 LOC `Focas2DemoApi` (synthetic CNC)
- ~50 LOC `Focas2DemoModeOptions` (env-var parser)
- ~10 LOC `Focas2SourceAdapter` dispatch edit
- ~20 LOC Host startup banner + metric
- ~25 LOC Management options + Studio banner
- ~180 LOC `Focas2DemoApiTests`
- ~50 LOC `Focas2SourceAdapter_DemoDispatchTests`
- ~30 LOC optional `MainLayoutDemoBannerTests`
- ~80 LOC ADR-0012
- ~20 lines adapter-sdk doc subsection

Single PR. Test target: ~1797.

---

**End of M.2b.3.1 v1 plan. Awaiting ChatGPT review pass before lock. Implementation per §5 sequence after v2 commits.**
