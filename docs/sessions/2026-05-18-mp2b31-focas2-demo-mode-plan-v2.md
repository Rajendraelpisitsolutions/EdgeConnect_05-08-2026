# M.2b.3.1 — FOCAS2 demo mode (no-CNC dev testing + sales demos)

**Status:** v2 — LOCKED (ChatGPT review pass folded in)
**Date:** 2026-05-18
**Predecessor plans:** [`v1`](2026-05-18-mp2b31-focas2-demo-mode-plan.md) (initial draft, superseded by this file)
**Predecessor milestone:** M.2b.3 ([PR #5](https://github.com/elpisitsolutions/EdgeConnect/pull/5), commit `4406157`)
**Estimated size:** ~380 LOC code + ~250 LOC tests, single PR
**Test baseline:** 1782 → expected ~1800 after M.2b.3.1 (~+18 tests)

---

## 0. What changed v1 → v2

The ChatGPT review pass produced four substantive amendments. All are tightenings, not relaxations.

1. **New Locked G — injectable monotonic clock.** v1 had this as Q1 OPEN. The fake's state machine must drive off an injectable `Func<DateTime>` (or `IClock`) so unit tests can advance state deterministically without `Thread.Sleep`. Production wires `() => DateTime.UtcNow`.

2. **New Locked H — env var is the ONLY activation path.** Demo mode cannot be enabled from `gateway.json` or any other persisted config. Closing the "rogue config enables fake mode in production" attack/footgun.

3. **New Locked I — `Focas2DemoApi` must never P/Invoke or load `fwlib*.dll`.** Pure managed code, no references to `Focas2Interop`. The fake must work on a clean laptop with no Fanuc native library installed — that's the explicit dev-laptop scenario.

4. **New Locked J — license gate is NOT bypassed.** Demo mode is registered the same way as a real FOCAS2 source via `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs`, which checks `IsModuleEnabled("source-focas2")`. Demo mode must NOT become a hidden license escape hatch.

Other refinements: metric renamed to `edgeconnect_focas2_fake_mode_enabled` (snake_case, namespaced); banner colour locked to **warning amber** (loud but not "system failure red"); startup log uses `LogCritical` (single emission, deliberate); audit/diagnostics visibility added at source/runtime level (Q9); dup-IP warning copy adjusted for demo (Q10); Mosquitto smoke explicitly out of scope (Q7).

---

## 1. Goal

Add a process-wide **FOCAS2 demo mode** that swaps the real `Focas2NativeApi` for a deterministic synthetic CNC emulator at adapter construction. Two driving use cases:

1. **Sales demos** — show the Studio + dashboards + MQTT data flow against a "live" CNC without shipping hardware or coordinating customer-site access.
2. **Dev testing** — click-test the full wizard happy path locally on a clean laptop without the Fanuc native library installed.

### Architectural pin (locked)

**Demo mode is a runtime IMPLEMENTATION choice that swaps one `IFocas2Api` for another.** The `ISourceAdapter` contract is unchanged. `Focas2SourceAdapter` is unchanged in shape; only its production ctor's dispatch is modified to pick the right `IFocas2Api` implementation based on env var. ADR-0011's "discovery is management-plane ephemeral" principle still holds — demo mode applies equally to the throwaway-adapter Browse probe and the live supervisor-driven adapter.

ADR-0012 frames demo mode narrowly: **a simulation backend for operator demos and CI** — not a protocol-abstraction concept. Future contributors who want to add a `Modbus` demo mode should follow the same pattern (per-protocol `IXxxApi` second implementation), NOT generalise demo mode to the Core layer.

---

## 2. Locked decisions

### Carried from v1 (unchanged)

| # | Decision | Reasoning |
|---|---|---|
| A | **Toggle = process-wide env var** `EDGECONNECT_FOCAS2_FAKE_MODE=true` | User-confirmed. Cleanest mental model. |
| B | **Realism level = full** — animated axes, run-state cycle, parts counter, tool changes, periodic alarms, MtLinki diagnostics | User-confirmed. Demos need to look alive AND exercise the full collector surface. |
| C | **`Focas2DemoApi` lives in `Sources.Focas2`** as a second `IFocas2Api` implementation | Production code already abstracts over `IFocas2Api`; protocol-specific behaviour stays inside the protocol module. |
| D | **Demo mode applies symmetrically to runtime adapter AND Browse probe** | Single dispatch in `Focas2SourceAdapter` covers both. |
| E | **Safety: loud startup log + Studio amber banner + Prometheus gauge** | Three independent signals. |
| F | **`Focas2DemoModeOptions.IsEnabled` cached at process start** | Immutable for the process lifetime. Restart-to-toggle. |

### Added at v2 lock (ChatGPT review folded in)

| # | Decision | Reasoning |
|---|---|---|
| **G** | **`Focas2DemoApi` accepts an injectable `Func<DateTime>` clock.** Production wires `() => DateTime.UtcNow`; tests inject a controlled clock | Time-driven state machine (more realistic for demos than call-driven) WITHOUT making tests depend on `Thread.Sleep`. Determinism + realism. |
| **H** | **Env var is the ONLY activation path.** Saved configuration (`gateway.json`, draft API, any persisted store) cannot enable demo mode | Closes the rogue-config attack/footgun. Demo activation requires explicit operator action at process start. Pinned by a code-review DoD clause: grep for any `gatewayConfig`-driven path setting `Focas2DemoModeOptions.IsEnabled` — must find none. |
| **I** | **`Focas2DemoApi` must NEVER P/Invoke or load `fwlib*.dll`.** No `using Focas2Interop`; no `DllImport`; pure managed C# | The dev-laptop scenario requires demo mode to work without the Fanuc native library installed. Pinned by a test: construct `Focas2DemoApi`, call every `IFocas2Api` method, run on a system without `Fwlib64.dll`/`libfwlib32.so`, assert no `DllNotFoundException` is thrown. |
| **J** | **License gate is NOT bypassed.** A FOCAS2 source registered in demo mode still requires `source-focas2` module to be enabled in the loaded license (per the existing `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` check) | Demo mode must NOT become a hidden license escape hatch. Sales-demo distributions can use a permissive (no license file loaded) build OR ship a demo license that has the module enabled. Either path is consistent with the existing licensing model. |

### Demo-fake state machine (locked behaviour, time-driven)

`Focas2DemoApi` exposes a deterministic state machine driven by the injected clock. Given `elapsed = clock() - _startedAt`:

| Aspect | Behaviour |
|---|---|
| Cycle period | 60s (Reset 10s → Start 40s → Stop 10s) |
| CNC identity | series `31i-B5`, type `M` (machining centre), version `1.00` |
| Axes | X, Y, Z (3 axes) |
| Axis positions | `position(t) = 100 * sin(2π * t / 30)` (mm) per axis with axis-specific phase offset |
| Spindle speed | 0 during Reset/Stop; ramps 0 → 3000 rpm linearly across Start phase |
| Parts counter | increments at each Start → Stop transition; starts at 0 |
| Tool number | cycles T1 → T5 → T9 every 3 cycles |
| Alarm | SV0432 raises for ~5s every 4 cycles, then clears |
| MtLinki servo temps | `35 + 3 * sin(2π * t / 90)` (°C) per axis |
| MtLinki fans | all OK |
| MtLinki batteries | all OK |

---

## 3. Resolved questions (record)

All ten OPEN questions from v1 settled by ChatGPT review on 2026-05-18:

| # | Verdict |
|---|---|
| Q1 | **Time-driven** state machine. Drives off `Func<DateTime>` clock (Locked G). Tests inject a controlled clock; production uses `() => DateTime.UtcNow`. |
| Q2 | **License gate NOT bypassed** (Locked J). `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` keeps its existing `IsModuleEnabled("source-focas2")` check unchanged. |
| Q3 | **Warning amber** for the Studio banner. Startup log uses `LogCritical` (single deliberate emission). |
| Q4 | **Unit tests with injectable clock**, no `Thread.Sleep` anywhere. Tests advance the fake by setting clock-output ahead of time. |
| Q5 | **ADR-0012 framing:** "simulation backend for operator demos and CI" — narrow scope. NOT a general protocol-abstraction concept. Future demo modes per protocol follow the same pattern; do NOT generalise to Core. |
| Q6 | **Metric name:** `edgeconnect_focas2_fake_mode_enabled`. Gauge type, value 0 or 1, no labels needed (single global flag). |
| Q7 | **No Mosquitto smoke required.** Existing `Focas2ToMqttEndToEndTests` already covers the MQTT path against a `FakeFocas2Api`. The demo-mode smoke is "Studio Browse Controller returns canonical demo identity" — does NOT require a broker. |
| Q8 | **Do NOT rewrite `Focas2ToMqttEndToEndTests`** to use `Focas2DemoApi`. Add demo-mode-specific tests separately. The existing integration test's `FakeFocas2Api` is test-scoped and serves a different purpose (per-test scenario knobs); blurring them is a future refactor, not part of this milestone. |
| Q9 | **Audit + diagnostics expose fake mode at source/runtime status level**, not per-data-point. Implementations: (a) one diagnostics event emitted at startup when demo mode active, (b) `Focas2SourceAdapter.CheckHealthAsync().Metrics["demoMode"] = true` when backed by `Focas2DemoApi`. No per-canonical-point annotation — the global banner + per-source metric is sufficient. |
| Q10 | **Dup-IP warning copy adjusted** in demo mode to: "Two FOCAS2 sources point at `{IpAddress}:{Port}`. In demo mode, both are backed by the same simulated controller (this is harmless). In production, this would mean two real handles to the same controller." |

---

## 4. Out of scope (explicit guardrails)

- **No `ISourceAdapter` contract change.**
- **No new `SourceCapabilities` flag.**
- **No per-source `fakeMode` config field.** Process-wide via env var only (Locked H).
- **No config-driven activation.** Saved `gateway.json` cannot enable demo mode. Code review will verify (DoD §10).
- **No demo mode for other protocols** in this milestone. Each protocol's demo mode is its own follow-up if needed.
- **No runtime toggling.** Env var read once at process start (Locked F).
- **No demo personas** (lathe vs machining centre, multiple axis counts). Single canonical profile in v1.
- **No deliberate-error simulation hooks.** Demo fake always succeeds. Failure-path UX is exercised by Browse against an unreachable IP (already verified in M.2b.3).
- **No demo-only license module.** Locked J: standard `source-focas2` gate applies.
- **No per-data-point demo annotation in the audit chain.** Only the source/runtime-level signal (Q9).
- **No dismissable banner.** Sticky for the session.
- **No P/Invoke or native-DLL reference from `Focas2DemoApi`** (Locked I). Verified by test on a clean machine.
- **No rewrite of `Focas2ToMqttEndToEndTests`** in this milestone (Q8). The existing `FakeFocas2Api` test scoping stays.
- **No Mosquitto smoke in the DoD** (Q7). Demo-mode smoke is Studio-only.

---

## 5. Deliverables

### 5.1 Sources.Focas2 — synthetic CNC

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2DemoApi.cs` *(new, ~270 LOC)* | Second `IFocas2Api` implementation. Time-driven state machine per §2 table. Constructor accepts `Func<DateTime>? clock = null` — production passes `() => DateTime.UtcNow`. NO references to `Focas2Interop`. Pure managed C#. Implements every method on `IFocas2Api` deterministically from the elapsed-time function. Per Locked I, `git grep "Focas2Interop\|DllImport\|fwlib"` in this file should find zero matches. |
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2DemoModeOptions.cs` *(new, ~60 LOC)* | Parses `EDGECONNECT_FOCAS2_FAKE_MODE` env var (truthy: `true`, `1`, `yes`; case-insensitive). Exposes `static bool IsEnabled { get; }` cached at first read. Exposes `static string StartupCriticalMessage` for the Host startup banner. Per Locked H, the parser reads ONLY `Environment.GetEnvironmentVariable`; no config injection. Internal test-only `Reset()` helper for inter-test cleanup. |
| `src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs` *(edit, ~10 LOC)* | Production constructor dispatches: `_api = Focas2DemoModeOptions.IsEnabled ? new Focas2DemoApi() : new Focas2NativeApi()`. ONE LINE. Health-metrics edit (~5 LOC): when the adapter is backed by `Focas2DemoApi`, `CheckHealthAsync` adds `metrics["demoMode"] = true` (Q9). |

### 5.2 Host — startup banner + metric + diagnostics event

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` *(edit, ~25 LOC)* | If `Focas2DemoModeOptions.IsEnabled`: emit ONE `LogCritical` line at startup ("⚠ FOCAS2 FAKE MODE ACTIVE — all FOCAS2 sources use a synthetic controller. Set EDGECONNECT_FOCAS2_FAKE_MODE=false and restart for production behaviour."); register Prometheus gauge `edgeconnect_focas2_fake_mode_enabled = 1` (or 0 when disabled, for diff-friendly scraping); emit ONE diagnostics event ("focas2.fakeMode.startup-activated") so the audit/diagnostics surface (Q9) makes the activation visible. |

### 5.3 Management — Studio banner via options

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Options/ManagementOptions.cs` *(edit, ~5 LOC)* | Add `bool Focas2FakeMode { get; init; }`. Populated by `AddConnectivityStudio` from `Focas2DemoModeOptions.IsEnabled`. Keeps Razor-to-Core isolation intact. |
| `src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs` *(edit, ~5 LOC)* | When constructing `ManagementOptions`, set `Focas2FakeMode = Focas2DemoModeOptions.IsEnabled`. |
| `src/ElpisEdgeConnect.Management/Components/Layout/MainLayout.razor` *(edit, ~15 LOC)* | When `ManagementOptions.Focas2FakeMode` is true, render a sticky top **warning-amber** banner: "🧪 **Demo mode active.** FOCAS2 sources are running against a synthetic controller — no real CNC connections. To disable: clear `EDGECONNECT_FOCAS2_FAKE_MODE` and restart EdgeConnect." Not dismissable. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFocas2Source.razor` *(edit, ~10 LOC)* | Q10 dup-IP warning copy update: when `ManagementOptions.Focas2FakeMode` is true AND the user-typed IP:Port collides with an existing FOCAS2 source, change the existing warning message to the demo-mode-aware copy specified in Q10. |

### 5.4 Tests

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2DemoApiTests.cs` *(new, ~200 LOC)* | ~13 tests covering the synthetic CNC's deterministic behaviour with an injectable clock. See §7.1. |
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2DemoModeOptionsTests.cs` *(new, ~50 LOC)* | ~4 tests for env-var parsing (truthy variants, falsy values, missing var, cache behavior). |
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2SourceAdapter_DemoDispatchTests.cs` *(new, ~80 LOC)* | ~3 tests: production ctor dispatches to demo API when env var set; ctor dispatches to real API when unset; **the no-DLL test** (Locked I, constructs and uses `Focas2DemoApi` end-to-end, asserts no `DllNotFoundException` even if Fwlib64.dll is absent). |
| `tests/ElpisEdgeConnect.Sources.Focas2.Tests/Focas2DemoApi_NoNativeReferenceTests.cs` *(new, ~30 LOC)* | Reflection-based static-analysis test: load the `Focas2DemoApi` type's declared methods + fields; assert NONE have `[DllImport]`; assert NO reference to `Focas2Interop`. Pins Locked I. |
| `tests/ElpisEdgeConnect.Management.Tests/MainLayoutDemoBannerTests.cs` *(new, ~40 LOC)* | Extract a `LayoutChromeModel` POCO (mirrors `SourceProtocolPickerModel` pattern); test the model decides banner visibility from `Focas2FakeMode` flag. |

### 5.5 Docs

| File | Change |
|---|---|
| `docs/adapter-sdk/focas2-adapter.md` | New "Demo mode" subsection (top of file) documenting the env var, what the fake emulates, the safety story (license gate still applies, no native library required, env-var-only activation). |
| `docs/decisions/0012-focas2-demo-mode.md` *(new)* | Status: Accepted. Captures Locked A–J. Framing per Q5: "simulation backend for operator demos and CI." Cross-links ADR-0011 (Browse Controller). |

---

## 6. Sequence of work

| Step | What | Gate |
|---|---|---|
| 1 | **Reality check.** Re-read `IFocas2Api` (confirm full coverage by synthetic). Verify `Focas2Interop`'s static-ctor `DllImportResolver` install does NOT fire when only `Focas2DemoApi` is constructed (else Locked I is at risk — need a guard or static-import isolation). Locate the existing diagnostics-event surface for the startup "fakeMode.activated" entry (Q9). | Verify before writing. |
| 2 | Write `Focas2DemoModeOptions` + 4 env-var parser tests. | — |
| 3 | Write `Focas2DemoApi` (time-driven, injectable clock) + 13 deterministic tests + the no-native-reference reflection test. | Internal gate — Sources.Focas2.Tests green; +18 new. |
| 4 | Edit `Focas2SourceAdapter` production ctor dispatch + health-metric `demoMode` flag + 3 dispatch tests. | Internal gate. |
| 5 | Edit `EdgeConnectComposition` for `LogCritical` banner + metric + diagnostics event. | — |
| 6 | Edit `ManagementOptions` + hosting wiring + MainLayout amber banner + AddFocas2Source dup-IP copy adjustment + `MainLayoutDemoBannerTests`. | — |
| 7 | **Full regression gate.** Solution build (0 warn / 0 err) + test sweep `Category!=Flaky`. Target ~1800. | — |
| 8 | ADR-0012 + adapter-sdk doc subsection. | — |
| 9 | **Manual smoke.** Set `EDGECONNECT_FOCAS2_FAKE_MODE=true`; run `dotnet run --project src/ElpisEdgeConnect.Management`. Verify: (a) `LogCritical` banner in console; (b) amber Studio banner sticky on every page; (c) `/sources/new/focas2` → Browse Controller returns 3 axes + ~50 tags + series "31i-B5"; (d) save draft → apply → source comes up Running emitting canonical points (verified via Diagnostics page or `/api/v1/diagnostics`); (e) Prometheus gauge reads 1 at `/metrics`; (f) clearing env var + restart → banner gone, gauge reads 0. **No Mosquitto required** (Q7). | Demo-readiness check. |
| 10 | Commit + PR. | Phase close. |

---

## 7. Test list (preview — refined during implementation)

### 7.1 `Focas2DemoApiTests` (~13)

All tests inject a controlled clock (no `Thread.Sleep` anywhere — Locked G).

1. **`AllocLibHandle_ReturnsSuccessAndNonZeroHandle`** — connect path works without DLL.
2. **`ReadSystemInfo_ReturnsCanonicalDemoIdentity`** — series `31i-B5`, type `M`, version `1.00`.
3. **`ReadAxisCount_Returns3`**.
4. **`ReadAxisNames_ReturnsXYZInOrder`**.
5. **`RunState_AtT5_IsReset_AtT30_IsStart_AtT55_IsStop`** — pins the 10/40/10 cycle phases.
6. **`SpindleSpeed_AtT5_Is0_AtT30_IsBetween1000And2500`** — ramps during Start.
7. **`PartsCount_AfterTwoFullCycles_IsTwo`** — Start→Stop transitions counted.
8. **`ToolNumber_CyclesT1T5T9_AcrossThreeCycles`**.
9. **`AlarmStatus_FiresEvery4thCycle_ClearsAfter5Seconds`**.
10. **`AxisPositions_AtMultipleClockOffsets_AreBounded_PlusMinus100mm`**.
11. **`MtLinkiServoTemperature_StaysWithin32To38Celsius`**.
12. **`Deterministic_TwoInstancesSameClock_ReturnIdenticalValues`** — pins repeatability for sales demos.
13. **`AllReadMethods_NeverThrow_OnAFreshClockSeed`** — quick safety pass to ensure no `NullRef` / `IndexOutOfRange` from edge phases.

### 7.2 `Focas2DemoModeOptionsTests` (~4)

1. **`Truthy_Variants_AreRecognised`** — `true`, `1`, `yes`, `TRUE` all enable.
2. **`FalsyAndMissing_DisableMode`** — `false`, `0`, empty, unset all disable.
3. **`Cache_IsImmutableAfterFirstRead`** — set env var, read `IsEnabled` (true), unset env var, read again, still true. Locked F.
4. **`Reset_ResumesEnvVarReads`** — test-only helper restores fresh-read state.

### 7.3 `Focas2SourceAdapter_DemoDispatchTests` (~3)

xUnit `[Collection("Focas2DemoMode")]` to serialise env-var manipulation.

1. **`ProductionCtor_WhenDemoModeOn_UsesFocas2DemoApi`** — set env var, construct, Initialize, BrowseTags, assert axes = `[X, Y, Z]` and series = `31i-B5` (the demo fake's profile).
2. **`ProductionCtor_WhenDemoModeOff_UsesFocas2NativeApi_OnSystemsWithDll`** *(conditional)* — env var unset, construct, attempt connect → expect either real-DLL behaviour or `FOCAS2.NATIVE_LIBRARY_MISSING`. The exact assertion depends on CI environment; this test is `[Fact(Skip = "requires Fanuc DLL")]` on CI, runnable manually on dev boxes with the DLL.
3. **`ProductionCtor_DemoMode_OnSystemWithoutDll_ConstructsAndUsesFakeWithoutThrowing`** — the dev-laptop scenario. Validates Locked I.

### 7.4 `Focas2DemoApi_NoNativeReferenceTests` (~3)

Pure reflection tests; no runtime CNC behaviour.

1. **`DemoApiType_HasNoDllImportAttributes`** — walk methods, assert none have `[DllImport]`.
2. **`DemoApiType_HasNoReferenceToFocas2Interop`** — inspect the type's IL / metadata for any reference to `Focas2Interop`. If reflection-walk is too brittle, fall back to a source-file `git grep` test (read the file, assert it doesn't contain "Focas2Interop").
3. **`DemoApiAssembly_DoesNotForceFwlibLoad`** — load the assembly metadata; assert no native-library reference forces an early Fwlib*.dll load.

### 7.5 `MainLayoutDemoBannerTests` (~2)

Test the extracted `LayoutChromeModel` POCO.

1. **`Banner_Visible_WhenFocas2FakeModeTrue`**.
2. **`Banner_Hidden_WhenFocas2FakeModeFalse`**.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Operator forgets `EDGECONNECT_FOCAS2_FAKE_MODE=true` is set in production | Three independent signals (Locked E): `LogCritical` startup log, sticky amber Studio banner, Prometheus gauge. Monitoring alerts on the gauge. The audit/diagnostics event (Q9) makes activation visible in the gateway's own diagnostics surface. |
| Saved config or REST API enables demo mode unintentionally | Locked H closes this. DoD §10 includes a code-review grep for any persisted-config path that touches `Focas2DemoModeOptions.IsEnabled`. |
| `Focas2DemoApi` accidentally triggers native-DLL load | Locked I + tests 7.4. The reflection test fails loudly if anyone adds a P/Invoke or `Focas2Interop` reference. |
| Demo mode shipped without `source-focas2` license module gating | Locked J keeps the existing `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` license check unchanged. A test in 7.3 or 7.5 could add a quick assertion (FOCAS2 source skipped when license module disabled and demo mode on); not load-bearing because the existing infrastructure already enforces it. |
| `LogCritical` at startup looks like a failed boot in automated log aggregation | The single deliberate emission has a distinctive message string (contains "FOCAS2 FAKE MODE ACTIVE"). Document in adapter-sdk that log monitoring should NOT treat this specific Critical line as a failure. Prometheus gauge is the canonical machine-readable signal. |
| Test isolation: env-var tests interfere with each other | xUnit `[Collection("Focas2DemoMode")]` serialises them; per-test `try / finally { Focas2DemoModeOptions.Reset(); Environment.SetEnvironmentVariable(...) }`. |
| `Focas2Interop` static-ctor `DllImportResolver` install fires when `Focas2DemoApi` is constructed | Step 1 reality-check verifies. The resolver registration is harmless (no DLL load; just callback registration). Locked I prohibits `Focas2DemoApi` from REFERENCING `Focas2Interop` — that's what prevents the static ctor from firing in demo-only paths. |
| Studio amber banner conflicts visibly with existing layout chrome | Edit is small and additive. Step 9 manual smoke verifies. |
| Dup-IP warning copy update breaks the `Focas2SourceWizardModelTests` | Those tests don't assert on Razor copy. Verified during Step 7 full sweep. |

---

## 9. Definition of done

1. `dotnet build ElpisEdgeConnect.sln` — 0 warnings, 0 errors.
2. Full sweep at ~1800 (1782 + ~18 new).
3. Step 7 regression gate green.
4. Setting `EDGECONNECT_FOCAS2_FAKE_MODE=true` and running `dotnet run --project src/ElpisEdgeConnect.Management` on the worktree path:
   - Console emits the single `LogCritical` "FOCAS2 FAKE MODE ACTIVE" line.
   - Studio renders the sticky amber banner on every page.
   - `/sources/new/focas2` → Browse Controller returns success with 3 axes + populated tag list + series `31i-B5`.
   - Save → apply → FOCAS2 source comes up Running emitting canonical points.
   - `/metrics` shows `edgeconnect_focas2_fake_mode_enabled 1`.
   - Diagnostics page shows the "fakeMode.startup-activated" event.
5. Clearing the env var + restart restores production behaviour byte-for-byte (banner gone, gauge reads 0).
6. ADR-0012 in place; cross-links ADR-0011.
7. `docs/adapter-sdk/focas2-adapter.md` has the demo-mode subsection.
8. `ISourceAdapter` contract unchanged (`git diff master -- src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs` empty).
9. **Locked G verified:** `Focas2DemoApi` constructor accepts `Func<DateTime>? clock`. All tests in 7.1 inject clocks; grep finds zero `Thread.Sleep` in `Focas2DemoApiTests.cs`.
10. **Locked H verified by code review:** `git grep "Focas2DemoModeOptions\\.IsEnabled"` shows only one writer (the env-var parser); no persisted-config path sets it.
11. **Locked I verified:** test 7.4.1 + 7.4.2 green; test 7.3.3 green on a system without `Fwlib64.dll`.
12. **Locked J verified:** the existing `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` license check is unchanged (`git diff` empty). A FOCAS2 source registered with `IsModuleEnabled("source-focas2") == false` is still skipped at registration time even when demo mode is on.
13. Health metrics expose `demoMode: true` when adapter is `Focas2DemoApi`-backed (Q9).
14. Dup-IP warning copy in `AddFocas2Source.razor` adjusted for demo case (Q10).

---

## 10. Pause-point criteria

Stop and report if:

- Step 1 reveals that `Focas2Interop`'s static-ctor fires merely from loading the assembly (e.g. via a module initializer), threatening Locked I. Will require a more careful isolation strategy.
- The synthetic CNC's state machine produces values that drift outside the adapter's collector validation rules (e.g. `AlarmCount` going negative; spindle speed > 8000 rpm).
- The Studio amber banner conflicts visibly with the auth/login chrome.
- Step 9 manual smoke reveals demo mode + a real `source-focas2`-licensed file produces ambiguous behaviour (gauge active but license also active — operator can't tell which is informing the runtime).
- A reflection test (7.4) fails to find a clean way to prove "no `Focas2Interop` reference" because the import is dynamic / late-bound. Fall back to a file-source grep test in that case.

---

## 11. ADR-0012 outline (preview)

Will land at `docs/decisions/0012-focas2-demo-mode.md`.

**Title:** FOCAS2 demo mode — env-var-toggled simulation backend for operator demos and CI

**Status:** Accepted (2026-05-18)

**Context:** M.2b.3 shipped the FOCAS2 Studio wizard with a Browse Controller probe. Without a real Fanuc CNC or the FOCAS2 native library, the wizard's happy path (Browse-returns-axes-and-tags, save-draft-and-source-comes-up-Running) cannot be demonstrated. This blocks two recurring workflows: sales demos (no hardware to ship) and dev testing on engineer laptops (no Fanuc DLL installed). M.2b.3.1 adds a process-wide demo-mode toggle.

**Decision:**

1. A new `IFocas2Api` implementation, `Focas2DemoApi`, lives alongside `Focas2NativeApi` in `Sources.Focas2`. It's pure managed C# with NO P/Invoke or `Focas2Interop` reference.
2. `Focas2SourceAdapter`'s production constructor dispatches: when `EDGECONNECT_FOCAS2_FAKE_MODE` is truthy, it constructs the demo API; otherwise the native API. Same dispatch applies to runtime adapters and the Browse Controller throwaway adapter.
3. The toggle is **process-wide and env-var-only** (Locked A + H). Saved configuration cannot enable it.
4. The toggle **does not bypass license gating** (Locked J). FOCAS2 sources still require the `source-focas2` license module to be enabled.
5. Demo mode is loudly visible: `LogCritical` at startup, sticky amber Studio banner, Prometheus gauge `edgeconnect_focas2_fake_mode_enabled`, and a single diagnostics event "fakeMode.startup-activated".
6. The synthetic CNC drives off an **injectable monotonic clock** (Locked G) so tests can advance state deterministically without `Thread.Sleep`. Production uses `() => DateTime.UtcNow`.
7. `Focas2DemoApi` must work on a clean laptop with no Fanuc native library installed (Locked I). Verified by reflection + behaviour tests.

**Load-bearing framing (verbatim):**

> Demo mode is a simulation backend for operator demos and CI. It is NOT a protocol-abstraction concept. Future contributors who want a Modbus or S7 demo mode should follow the same pattern (per-protocol `IXxxApi` second implementation, env-var toggle, license-gated) — NOT generalise demo mode to the Core layer.

**Consequences:**

- `ISourceAdapter` and the Core layer are unchanged.
- `Focas2SourceAdapter` has a one-line dispatch in its production ctor + a one-line health-metric addition (`demoMode: true`).
- A new env-var-only safety surface exists: a code-review invariant that `Focas2DemoModeOptions.IsEnabled` has exactly one writer (the env-var parser).
- A new test invariant exists: `Focas2DemoApi` cannot acquire a P/Invoke reference (reflection test 7.4).
- License gating (`source-focas2`) governs demo-mode FOCAS2 sources identically to real ones. Sales-demo distributions either ship with no license loaded (permissive dev path) or with a demo license that has the module enabled.

**References:**

- ADR-0011 (Browse Controller reuses BrowseTagsAsync) — demo mode applies symmetrically to the Browse probe and the runtime adapter without disturbing ADR-0011's "discovery is management-plane ephemeral" principle.
- M.2b.3.1 plan v2 (this file).
- ChatGPT review pass on M.2b.3.1 plan v1, 2026-05-18 — added Locked G/H/I/J; locked banner colour, metric name, and license-gating policy.

---

## 12. Scope summary

- ~270 LOC `Focas2DemoApi` (synthetic CNC)
- ~60 LOC `Focas2DemoModeOptions` (env-var parser + cache)
- ~15 LOC `Focas2SourceAdapter` dispatch + health-metric edit
- ~25 LOC Host startup banner + metric + diagnostics event
- ~30 LOC Management options + Studio banner + Razor dup-IP copy update
- ~200 LOC `Focas2DemoApiTests` (13 tests, all clock-driven)
- ~50 LOC `Focas2DemoModeOptionsTests` (4 tests)
- ~80 LOC `Focas2SourceAdapter_DemoDispatchTests` (3 tests)
- ~30 LOC `Focas2DemoApi_NoNativeReferenceTests` (3 reflection tests, pins Locked I)
- ~40 LOC `MainLayoutDemoBannerTests` (2 tests, requires `LayoutChromeModel` POCO extraction)
- ~120 LOC ADR-0012
- ~30 lines adapter-sdk doc subsection

Single PR. Test target: ~1800.

---

**End of M.2b.3.1 v2 plan. LOCKED 2026-05-18 after ChatGPT review pass. Ready for implementation per §6 sequence starting at Step 1 (reality-check).**
