# M.2b.3 — FOCAS2 Studio wizard (+ Browse Controller live probe)

**Status:** v3 — LOCKED (Step 1 reality-check folded in)
**Date:** 2026-05-17
**Branch:** worktree `happy-cartwright-465442` → `claude/happy-cartwright-465442` (from `master` at `545f722`)
**Predecessor plans:**
- [`v1`](2026-05-17-mp2b3-focas2-wizard-plan.md) — initial draft
- [`v2`](2026-05-17-mp2b3-focas2-wizard-plan-v2.md) — ChatGPT review folded in
**This version:** v3 — Step 1 reality-check on the FOCAS2 adapter lifecycle exposed two issues with Locked M (15s cap is unenforceable as written) and Locked P (bounded Dispose targets the wrong call). Both amendments folded in below.
**Estimated size:** ~1,400 LOC code + ~540 LOC tests, single PR
**Test baseline:** 1755 → expected ~1773 after M.2b.3 (~+18 tests)

---

## 0. What changed v2 → v3

**Issue α (Locked M was unenforceable):** `Focas2ConnectionManager.TryConnect` runs an uncancellable retry loop with default `MaxConnectRetries=5` × `TimeoutSeconds=10s` per attempt + blocking `Thread.Sleep` between attempts (~57s worst case). `CancellationToken` only kills the AWAIT — the queued work continues on the Focas2Thread. v2's 15s overall budget caps the HTTP response, but the underlying thread keeps doing useless work, accumulating orphan threads across repeated probes against dead IPs.

**Issue β (Locked P bounded the wrong call):** `Focas2Thread.DisposeAsync` already has its own 10s `Thread.Join(10s)` bound — it cannot hang indefinitely. The actual wedge vector is `Focas2SourceAdapter.StopAsync`, which awaits a queued Disconnect work item behind any in-flight native call. The plan's `WaitAsync(5s)` on `DisposeAsync` (which is essentially a no-op after StopAsync) protected nothing useful.

**Resolutions:**
- **New Locked S** — the probe constructs an override config with `MaxConnectRetries = 1` and `TimeoutSeconds = min(8, request.TimeoutSeconds)`. Browse is for *discovery*, not production-timing simulation. Caps native connect work at ~8s; eliminates orphan-thread accumulation.
- **Revised Locked P** — bound the **combined Stop+Dispose phase** at 12s (slightly above the internal Thread.Join ceiling of 10s), not Dispose alone at 5s. Wrap in `Task.Run(...).WaitAsync(TimeSpan.FromSeconds(12))` so abandonment is clean if the thread is wedged in native code.

Everything else from v2 carries forward unchanged.

---

## 1. Goal

Add a Studio wizard for FOCAS2 source setup, mirroring the M.2b.1 Modbus wizard's shape but with:

- A **collect-all-by-default + group picker** UX for the FOCAS2 `DataPoints` hierarchical-prefix model.
- A **"Browse Controller" live probe** that drives `FromSourceInstance → ValidateConfigAsync → Initialize → Start → BrowseTagsAsync → bounded(Stop + Dispose)` on a throwaway `Focas2SourceAdapter` with **a probe-only override config** (single-attempt, ≤8s timeout) to surface real axis names + tag list before the operator commits the draft.

The wizard reuses `WizardConfigMerger` and `RouteWiring` unchanged.

### Architectural pin (locked)

**The live probe reuses the existing `ISourceAdapter.BrowseTagsAsync` capability — no contract revision.** The `ISourceAdapter` contract file is marked LOCKED. Path A (throwaway-adapter Initialize/Start/Browse/Stop) honours that lock.

**Discovery and probe workflows are treated as management-plane ephemeral operations and are intentionally isolated from the runtime supervisor pipeline.** ADR-0011 captures this lock.

---

## 2. Locked decisions

### Carry-forward from v2 (unchanged)

| # | Decision | Reasoning |
|---|---|---|
| A | Mirror M.2b.1 wizard shape | Operator already knows the Modbus pattern. |
| B | **Data Points UX:** collect-all default + group picker over hierarchical prefixes | Matches `Focas2SourceConfiguration.DataPoints` semantic. |
| C | **Live probe IN scope**, reusing `BrowseTagsAsync` via Path A | No contract change. |
| D | Probe button labelled **"Browse Controller"** | User-requested. |
| E | **Dup IP:Port → warn but allow** | Test rigs, dual-monitor, commissioning overlap are legitimate. |
| F | **TCP-only connection** | Adapter doesn't support HSSB. |
| G | Browse endpoint takes a `SourceInstanceConfig`; runs through `Focas2SourceConfiguration.FromSourceInstance` | Same code path the supervisor uses. Defence in depth. |
| H | Probe runs a **throwaway adapter** — no supervisor involvement | Probe failures isolated from live data path. |
| I | Probe is **license-gated** by `source-focas2` | Consistent with registration-time enforcement. |
| J | Probe is **single-flight per `IpAddress:Port`** | Per-handle thread-affinity rule supports concurrent handles on different threads. Confirmed by Step 1. |
| K | Wizard model tests follow `ModbusSourceWizardModelTests.cs` shape | Consistent with M.2b.1. |
| L | **Browse endpoint lives in `Management`, not `Host`** | Runtime/management boundary preservation. |
| M | **Browse timeout is a fixed 15s = 10s connect + 5s browse grace**, NOT derived from `config.TimeoutSeconds` | Interactive UI responsiveness separated from runtime reconnect policy. **Enforceable now that Locked S caps native connect work to ≤8s** (v2 alone could not enforce this — see §0). |
| N | **Probe sequence calls `ValidateConfigAsync` BEFORE `InitializeAsync`** | Confirmed pure/config-only by Step 1. Fast-fail cheap errors. |
| O | **Group picker emits PREFIXES**, not expanded explicit tag paths | Future-proof to tag-map growth. |
| Q | Every probe gets a correlation `ProbeId` | Triage-critical. |
| R | Protocol-picker tile regression test (bUnit) | Cheap, high-ROI. |

### Added at v3 lock (Step 1 reality-check folded in)

| # | Decision | Reasoning |
|---|---|---|
| **S** | **The probe forces `MaxConnectRetries = 1` and `TimeoutSeconds = min(8, request.TimeoutSeconds)` on the throwaway adapter, regardless of what the wizard form supplied** | `Focas2ConnectionManager.TryConnect` has an uncancellable retry loop (default 5 attempts × 10s + Thread.Sleep between) that can run ~57s of native work — `CancellationToken` only kills the await, not the queued work on Focas2Thread. Without this override, Locked M's 15s budget caps the HTTP response but doesn't stop orphan threads from doing useless work afterward. Browse is for discovery, not production-timing simulation; the wizard's `TimeoutSeconds` field describes runtime behaviour, not probe behaviour. |
| **P** *(revised)* | **The combined Stop + Dispose cleanup phase is bounded at 12s** via `Task.Run(stopThenDispose).WaitAsync(TimeSpan.FromSeconds(12))`. Slightly above `Focas2Thread.DisposeAsync`'s internal 10s `Thread.Join(10s)` ceiling so a wedged native call doesn't extend us beyond that. On timeout: log + abandon, attach `FOCAS2.DISPOSE_TIMEOUT` as a non-fatal warning to the result. | v2's `WaitAsync(5s)` on `DisposeAsync` alone bounded the wrong call — `DisposeAsync` is a no-op after `StopAsync` runs (the underlying `Focas2Thread` is already disposed via the `_disposed` short-circuit). The wedge risk lives in `StopAsync` awaiting a queued Disconnect work item. The 12s combined bound captures both. |

### Browse-flow precondition (v3, locked)

```csharp
var probeId = Guid.NewGuid().ToString("N")[..8];
var stopwatch = Stopwatch.StartNew();
Focas2SourceAdapter? adapter = null;
var warnings = new List<string>();
try
{
    // 1. License gate (Locked I) — before we touch FOCAS2 at all.
    if (!license.IsModuleEnabled("source-focas2"))
        return Failure("LICENSE.MODULE_DISABLED", probeId);

    // 2. Single-flight guard (Locked J) — keyed on IpAddress:Port. Wait(0).
    using var leaseHandle = TryAcquireLease(ipAddressPort);
    if (leaseHandle is null)
        return Failure("FOCAS2.BROWSE_IN_FLIGHT", probeId);

    // 3. Schema parse (Locked G).
    var requested = Focas2SourceConfiguration.FromSourceInstance(request);

    // 4. *** Locked S: build the probe override config ***
    //     Browse always runs single-attempt with a tight timeout, regardless
    //     of what the operator typed in the wizard. The runtime adapter
    //     honours those fields; the probe does not.
    var probeConfig = requested with
    {
        MaxConnectRetries = 1,
        TimeoutSeconds = Math.Min(8, Math.Max(1, requested.TimeoutSeconds)),
    };

    // 5. Construct adapter — no DI, no SourceRegistration, throwaway (Locked H).
    adapter = new Focas2SourceAdapter(probeConfig.InstanceId, logger, gatewayIdentity);

    // 6. Bounded 15s overall (Locked M, now enforceable thanks to Locked S).
    using var probeCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
    probeCt.CancelAfter(TimeSpan.FromSeconds(15));

    // 7. Adapter-level validation BEFORE Init (Locked N — confirmed pure).
    var validation = await adapter.ValidateConfigAsync(probeConfig, probeCt.Token);
    if (!validation.IsValid)
        return Failure("FOCAS2.CONFIG_INVALID", probeId, validation.Errors);

    // 8. Init → Start → Browse.
    await adapter.InitializeAsync(probeConfig, probeCt.Token);
    await adapter.StartAsync(probeCt.Token);
    var tags = await adapter.BrowseTagsAsync(probeCt.Token);

    return Success(probeId, tags, axes, cncSeries, stopwatch.ElapsedMilliseconds);
}
catch (OperationCanceledException)
{
    return Failure("FOCAS2.BROWSE_TIMEOUT", probeId);
}
catch (AdapterException ex)
{
    return Failure(ex.ErrorCode, probeId, ex.Message);
}
catch (DllNotFoundException)
{
    return Failure("FOCAS2.NATIVE_LIBRARY_MISSING", probeId);
}
finally
{
    // *** Locked P (revised): bounded combined Stop + Dispose ***
    if (adapter is not null)
    {
        try
        {
            await Task.Run(async () =>
            {
                try { await adapter.StopAsync(CancellationToken.None); } catch { }
                try { await adapter.DisposeAsync(); } catch { }
            }).WaitAsync(TimeSpan.FromSeconds(12));
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "ProbeId={ProbeId} Stop+Dispose hung beyond 12s — adapter abandoned, native handle reclaimed at process exit",
                probeId);
            warnings.Add("FOCAS2.DISPOSE_TIMEOUT");
            // result object (already returned via success/failure path) is mutable for warnings.
        }
    }
}
```

This pseudocode is the **load-bearing reference** for the `Focas2BrowseService` implementation in §6.4. Any deviation has to flag back here.

---

## 3. Out of scope (explicit guardrails)

(Unchanged from v2 — copied verbatim.)

- No `ISourceAdapter` contract change.
- No generalisation of the Browse endpoint to other protocols.
- No HSSB / non-TCP connection modes.
- No per-tag manual rows.
- No "wire to existing route" branch.
- No CSV import / template library (M.2c).
- No auto-populate of the group picker from Browse results.
- No probe-result caching.
- No instance-id auto-suggest.
- No route-side dup-sink guard.
- No probe through the runtime supervisor.

---

## 4. Resolved questions (record)

(Unchanged from v2. The Q-by-Q verdict table from ChatGPT review remains the authoritative record. See [v2 §4](2026-05-17-mp2b3-focas2-wizard-plan-v2.md#4-resolved-questions-record).)

---

## 5. Out of scope (covered in §3)

(Section number kept for parity with M.P2.3 plan structure.)

---

## 6. Deliverables

### 6.1 Management — wizard model

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/Focas2SourceWizardModel.cs` *(new)* | POCO matching `Focas2SourceConfiguration` fields. `BuildSourceInstance()` projects to canonical `SourceInstanceConfig`. Static `DataPointGroups` registry. Helper `BuildDataPointsFromSelection(groups, mode)` emits PREFIXES (Locked O), collapses all-selected to empty. |

### 6.2 Management — Razor wizard page

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddFocas2Source.razor` *(new)* | Single-page form. Six sections: Identity, Connection, Data Points, **Browse Controller** (result panel: `ProbeId`, axes, tag count + tag list, CNC series + type, elapsed-ms, last-error, **warnings** list — to surface `FOCAS2.DISPOSE_TIMEOUT` when it lands), Routing, Draft Summary. Caption beneath the Browse button: **"Browse Controller performs a temporary single-attempt connection (max 8s) for discovery only. No configuration is saved until Draft is committed. The wizard's Timeout setting describes runtime behaviour and does not affect Browse."** This caption pins Locked S in the UI so operators don't expect Browse to honour their typed timeout. |

### 6.3 Management — protocol-picker tile

(Unchanged from v2.)

### 6.4 Management — Browse endpoint

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseApi.cs` *(new)* | Maps `POST /api/v1/sources/browse/focas2`. License-gated at endpoint layer. Delegates to `Focas2BrowseService`. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseResultDto.cs` *(new)* | DTO: `ProbeId`, `Success`, `AxisNames`, `Tags`, `TagCount`, `CncSeries`, `CncType`, `ErrorCode`, `ErrorMessage`, `ValidationErrors`, `Warnings`, `ElapsedMs`. `Warnings` is the list `FOCAS2.DISPOSE_TIMEOUT` flows through. |
| `src/ElpisEdgeConnect.Management/Api/Focas2BrowseService.cs` *(new)* | Implements the §2 pseudocode verbatim. **Locked S override** at step 4 (`probeConfig = requested with { MaxConnectRetries = 1, TimeoutSeconds = min(8, max(1, requested.TimeoutSeconds)) }`). **Locked P combined Stop+Dispose bound at 12s** in `finally`. Single-flight via `ConcurrentDictionary<string,SemaphoreSlim>` with `Wait(0)`. Correlation `ProbeId` logged at each phase boundary. |
| `src/ElpisEdgeConnect.Management/ManagementServiceCollectionExtensions.cs` *(edit; or `Program.cs`)* | Register `Focas2BrowseService` as singleton; map endpoint. |

### 6.5 Tests

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Management.Tests/Focas2SourceWizardModelTests.cs` *(new)* | ~10 tests (see §8.1). |
| `tests/ElpisEdgeConnect.Management.Tests/Focas2BrowseServiceTests.cs` *(new)* | ~6 tests (see §8.2). v3 adds **test #4 specifically pins Locked S**. |
| `tests/ElpisEdgeConnect.Management.Tests/Focas2ProtocolPickerTests.cs` *(new)* | ~3 bUnit tests (see §8.3). |

### 6.6 Docs

(Unchanged from v2.)

### 6.7 ADR

(Unchanged from v2. ADR-0011 outline still §12. The load-bearing sentence and Path A reasoning carry over.)

---

## 7. Sequence of work

(Steps unchanged in shape; Step 1 is now *complete* — its findings produced this v3.)

| Step | What | Why / Gate |
|---|---|---|
| 1 | **Reality check.** ✅ **COMPLETE 2026-05-17.** Findings recorded in §14 below. Yielded Locked S + revised Locked P. Locked J + Locked N confirmed unchanged. | Done. |
| 2 | Write `Focas2SourceWizardModel` + its tests (§8.1). | Smallest, most testable unit first. |
| 3 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +10 new tests green. | Lock model contract before UI/API. |
| 4 | Write `Focas2BrowseService` + `Focas2BrowseResultDto` + tests (§8.2). | Probe logic. |
| 5 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +6 new tests green. | Lock probe behaviour, including Locked S override. |
| 6 | Write `Focas2BrowseApi.cs`, wire into Management's endpoint mapping. | API surface. |
| 7 | Write `AddFocas2Source.razor` — all six sections including the Locked S caption. | UI. |
| 8 | Flip the FOCAS2 tile on `ChooseSourceProtocol.razor`. Write `Focas2ProtocolPickerTests.cs` (§8.3). | Reachability + regression guard. |
| 9 | **Internal gate.** `dotnet test tests/ElpisEdgeConnect.Management.Tests` — +3 bUnit tests green. | Lock tile regression. |
| 10 | **Full regression gate.** `dotnet build ElpisEdgeConnect.sln` (0 warnings, 0 errors) + `dotnet test --filter "Category!=Flaky"`. Target ~1773. | Final pre-doc sweep. |
| 11 | ADR-0011, docs/adapter-sdk/focas2-adapter.md edit, handoff doc footer. | Docs. |
| 12 | Optional: manual smoke against a real / faked CNC. | Operator-realism check. |
| 13 | Single commit. PR. | Phase close. |

---

## 8. Test list (preview — refined during implementation)

### 8.1 `Focas2SourceWizardModelTests` (~10)

(Unchanged from v2.)

### 8.2 `Focas2BrowseServiceTests` (~6 — v3 adds test #4 specifically for Locked S)

1. **`Browse_HappyPath_ReturnsAxesAndTags_WithProbeIdAndElapsedMs`** — `FakeFocas2Api` with 3 axes. DTO `Success=true`, `ProbeId` is 8-char hex, `ElapsedMs > 0`.
2. **`Browse_LicenseDisabled_ReturnsLicenseModuleDisabled`** — pins Locked I.
3. **`Browse_ConnectFailure_SurfacesAdapterErrorCode`** — fake throws on handle alloc; DTO `ErrorCode` matches Step-1-verified code.
4. **`Browse_RequestWithHighRetriesAndTimeout_OverriddenByProbeConfig`** *(new in v3, pins Locked S)* — request has `MaxConnectRetries=10` and `TimeoutSeconds=60`. Spy on `FakeFocas2Api.AllocLibHandle` to assert: called **exactly once**, with `timeout` parameter **≤ 8**. Even when the fake returns success, the override must be applied. **This is the headline Locked S test.**
5. **`Browse_TimeoutAt15s_SurfacesBrowseTimeoutCode`** — fake hangs in alloc; service-side cancel fires within 15s ± 1s; DTO `ErrorCode = "FOCAS2.BROWSE_TIMEOUT"`, `ProbeId` present.
6. **`Browse_SecondCallWhileInFlight_ReturnsInFlightCode`** — pins Locked J.

Optional seventh test if Stop/Dispose behaviour worth pinning:

7. **`Browse_StopDisposeHang_AbandonsAdapterWithWarning`** — fake delays Stop's queued Disconnect indefinitely. Service Task.Run cleanup hits 12s, throws TimeoutException, attaches `FOCAS2.DISPOSE_TIMEOUT` to `Warnings`. **Pins revised Locked P.** Add if Step 1's seam allows clean fake-side hang injection.

### 8.3 `Focas2ProtocolPickerTests` (~3, bUnit)

(Unchanged from v2.)

---

## 9. Risks & mitigations

(v2 risks carried forward; orphan-thread risk now resolved by Locked S.)

| Risk | Mitigation |
|---|---|
| ~~Probe's retry loop accumulates orphan threads against dead IPs~~ | **Resolved by Locked S** — single attempt, ≤8s. |
| ~~`WaitAsync(5s)` on DisposeAsync doesn't actually bound the wedge risk~~ | **Resolved by revised Locked P** — bound combined Stop+Dispose at 12s. |
| Native FOCAS2 DLL hang during teardown | Locked P combined bound abandons the adapter at 12s; surfaces `FOCAS2.DISPOSE_TIMEOUT` warning. Probe is low-frequency; native handle reclaimed at process exit. |
| Operator confused by "Browse uses 8s but I set 60s" | UI caption beneath the Browse button (§6.2) explains the runtime/probe split explicitly. |
| Browse takes > 15s and UX feels frozen | Razor shows `MudProgressLinear` busy state + elapsed-ms tick; service-side cancel at 15s; with Locked S the practical max is ~9s before cancel fires anyway. |
| License check bypassed | Endpoint-layer check + service-internal check (defence in depth). Test #2 covers. |
| Browse tag list inconsistent with runtime | Same `BrowseTagsAsync` implementation + same `FromSourceInstance` path. Roundtrip test #3 of §8.1 pins parity. |
| FOCAS2 native library not installed | Service catches `DllNotFoundException` → `FOCAS2.NATIVE_LIBRARY_MISSING`. |
| Two operators probe the same controller cross-session | Single-flight is process-local; Studio is single-process. Out of scope. |
| Probe trips a controller-side alarm via transient handle | Brief handle cycle (≤8s + close); matches FOCAS2 norms. Documented in adapter-sdk note. |

---

## 10. Definition of done

(v2 clauses 1–10 carried forward; clauses 11–17 updated for Locked S and revised P.)

1. `dotnet build ElpisEdgeConnect.sln` — 0 warnings, 0 errors.
2. Full sweep at ~1773 (1755 + ~18 new tests across the three test files).
3. Steps 3, 5, 9, 10 regression gates all green.
4. FOCAS2 tile on `/sources/new` clickable, lands at `/sources/new/focas2`.
5. `/sources/new/focas2` produces a draft via `/api/v1/config/drafts` indistinguishable in shape from a hand-authored entry.
6. Browse Controller against FOCAS2 fake returns axes + tag count + tags + CncSeries + ProbeId + ElapsedMs; unreachable host → structured error; license-disabled → `LICENSE.MODULE_DISABLED`; second probe same IP:Port → `FOCAS2.BROWSE_IN_FLIGHT`.
7. `Focas2SourceConfiguration.FromSourceInstance(wizard.BuildSourceInstance())` reproduces an equivalent typed config (headline parity test #3 of §8.1).
8. ADR-0011 in place with the management-plane sentence verbatim; `ISourceAdapter` contract unchanged.
9. `docs/adapter-sdk/focas2-adapter.md` has the new Studio paragraph.
10. Handoff doc has the resolution footer.
11. Locked H verified: `Focas2BrowseService` constructs the adapter directly; no `SourceRegistration` / `SourceSupervisor` references in the Browse code path.
12. Locked L verified: Browse endpoint lives in `src/ElpisEdgeConnect.Management/`, not `src/ElpisEdgeConnect.Host/`.
13. Locked M verified: 15s constant appears once in `Focas2BrowseService`, NOT computed from `config.TimeoutSeconds`. **Locked S verified separately at clause 16.**
14. Locked N verified: `ValidateConfigAsync` invoked before `InitializeAsync`.
15. Locked O verified: `DataPoints_SelectiveMode_EmitsPrefixes` + `DataPoints_AllGroupsSelected_CollapsesToEmpty` both green.
16. **Locked S verified:** `Focas2BrowseService` builds a `probeConfig = requested with { MaxConnectRetries = 1, TimeoutSeconds = min(8, max(1, requested.TimeoutSeconds)) }`. Test #4 of §8.2 (the "high retries + 60s timeout overridden" test) is green. UI caption present beneath the Browse button.
17. **Locked P (revised) verified:** the cleanup `finally` wraps Stop+Dispose in `Task.Run(...).WaitAsync(TimeSpan.FromSeconds(12))` (not 5s, and not on `DisposeAsync` alone). The `FOCAS2.DISPOSE_TIMEOUT` warning channel exists in the DTO and is exercised by test #7 of §8.2 if present.
18. Locked Q verified: every `Focas2BrowseService` log includes `ProbeId`; DTO surfaces `ProbeId`.

---

## 11. Pause-point criteria

Updated for v3:

- ~~Step 1 reveals `Focas2SourceAdapter.ValidateConfigAsync` cannot run before Init~~ — confirmed passes; Locked N holds.
- ~~Step 1 reveals process-wide state in the FOCAS2 stack~~ — confirmed none beyond benign static DllImportResolver install; Locked J holds.
- Step 3 / 5 / 9 internal gate regresses any existing Management test.
- Roundtrip parity test (#3 of §8.1) fails — real bug in either side of the wizard↔FromSourceInstance bridge.
- The Locked S override test (#4 of §8.2) shows the override didn't apply — implementation bug in `Focas2BrowseService`.
- The Step 12 manual smoke trips an unexpected controller-side alarm — surface before merging.

---

## 12. ADR-0011 outline

(Unchanged from v2 — see [v2 §12](2026-05-17-mp2b3-focas2-wizard-plan-v2.md#12-adr-0011-outline-preview).)

ADR-0011 does NOT mention Locked S or P — those are implementation-level decisions about the throwaway adapter's behaviour, not load-bearing architectural lockings. The ADR's job is to commit to "discovery is management-plane ephemeral"; the timeout/retry numbers are tactical, not architectural.

---

## 13. Scope summary

- ~150 LOC wizard model
- ~520 LOC wizard Razor (Locked S caption + warnings rendering add ~10 LOC over v2)
- ~5 LOC protocol-picker tile flip
- ~340 LOC Browse endpoint + service + DTO (probeConfig override + Locked P combined cleanup add ~20 LOC over v2)
- ~30 LOC composition wiring
- ~280 LOC wizard model tests (~10 tests)
- ~200 LOC Browse service tests (~6 tests, +1 for Locked S over v2)
- ~70 LOC bUnit tile tests (~3 tests)
- ~110 LOC ADR-0011
- ~30 lines doc edits

Single PR. Test target: ~1773.

---

## 14. Step 1 reality-check record (2026-05-17)

Read against `src/ElpisEdgeConnect.Sources.Focas2/` lifecycle code. Findings:

### Priority check 1 — `ValidateConfigAsync` is pure/config-only

**PASS.** [`Focas2SourceAdapter.cs:360-417`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs#L360) validates `IpAddress` / `Port` / `TimeoutSeconds` on the `config` parameter only. No reference to `_thread`, `_connectionManager`, `_config`, or any post-Init state. Locked N holds.

### Priority check 2 — No process-wide serialization

**PASS.** Each adapter owns its own thread ([`Focas2Thread.cs:32-40`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2Thread.cs#L32)). Only process-wide state is `Focas2Interop`'s static-ctor `DllImportResolver` install ([`Focas2Interop.cs:45-50`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2Interop.cs#L45)) — one-time idempotent, no serialization. File header documents per-handle (not per-process) thread safety. Locked J holds.

### Priority check 3 — Adapter disposal reliable enough for bounded dispose

**PASS WITH AMENDMENT.** `Focas2Thread.DisposeAsync` internally bounds at 10s via `Thread.Join(10s)` ([`Focas2Thread.cs:131`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2Thread.cs#L131)) — cannot hang indefinitely. But v2's `WaitAsync(5s)` on `DisposeAsync` alone bounded the wrong call: after `StopAsync` runs, `DisposeAsync` is a no-op (the underlying thread is already disposed via `_disposed` short-circuit). The wedge risk lives in `StopAsync` awaiting a queued Disconnect work item. **Revised to: bound combined Stop+Dispose phase at 12s.** Locked P updated.

### Priority check 4 — Different IP:Port probes safely concurrent

**PASS WITH AMENDMENT.** Architecturally yes — per-adapter `Focas2Thread`, per-handle FOCAS2 thread-affinity. But the **connection manager's retry loop runs uncancellable work**: `TryConnect` ([`Focas2ConnectionManager.cs:208-278`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2ConnectionManager.cs#L208)) runs up to `MaxConnectRetries=5` × `TimeoutSeconds=10s` native + blocking `Thread.Sleep(500)/Thread.Sleep(1000)` — ~57s worst case. `CancellationToken` passed to `_thread.RunAsync` ([`Focas2Thread.cs:60-65`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2Thread.cs#L60)) only cancels the awaiting Task, not the queued work item. Result: v2's Locked M (15s HTTP cap) holds for the response but allows ~40s of orphan-thread work afterward. **Amendment: Locked S — probe forces `MaxConnectRetries=1` + `TimeoutSeconds ≤ 8`.** With this, the worst-case work on Focas2Thread fits inside the 15s budget. Locked M now genuinely enforceable.

### Priority check 5 — Same IP:Port returns busy, not queue

**PASS.** Implementation is `SemaphoreSlim.Wait(0)` (immediate try-acquire). Locked J holds.

### Adjacent observations (informational, no plan change)

- `BrowseTagsAsync` ([`Focas2SourceAdapter.cs:350-357`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs#L350)) is a synchronous read of `_connectionManager.SystemInfo.AxisNames`, populated by `EnsureSystemInfo` in StartAsync. If Start's connect fails, SystemInfo is null and Browse returns the `["X", "Y", "Z"]` fallback. Probe failures with `FOCAS2.CONNECT_FAILED` will skip Browse entirely (catches before Browse is reached).
- `StartAsync` ([`Focas2SourceAdapter.cs:184-212`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2SourceAdapter.cs#L184)) is "fail-soft": connect failure is logged but state transitions to Running anyway. For the probe, this means a failed connect won't throw out of StartAsync — Browse runs and returns the fallback axes. We need to detect connect failure explicitly via `adapter.CheckHealthAsync` or by inspecting connection state, OR accept that "Browse succeeded but with fallback axes" is the surface for a soft connect failure. **Surfacing this for the implementation step**: the Browse service should check `_connectionManager.IsConnected` (via `CheckHealthAsync().Metrics["connected"]`) after StartAsync and emit `FOCAS2.CONNECT_FAILED` if false, rather than relying on StartAsync to throw. *(Not a Locked decision; tactical implementation note.)*
- The 75 existing FOCAS2 unit tests all use `FakeFocas2Api`. The "different handles concurrent on different real threads" claim is the architectural commitment, not empirically validated against the real Fanuc DLL. Step 12 optional smoke (if reachable) is the only place this could be tested live.

---

**End of M.2b.3 v3 plan. LOCKED 2026-05-17 after Step 1 reality-check. Ready for implementation per §7 sequence starting at Step 2.**
