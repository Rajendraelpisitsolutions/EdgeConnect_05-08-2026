# M.P2.4 — Brother HTTP source adapter migration (v3 reality-check)

**Status:** v3 — IMPLEMENTATION-READY after reality-check pass (2026-05-21). All v2 open items closed; minor catalog refinements applied; one non-blocking flag carried into implementation as a cross-check, not a gate.
**Date:** 2026-05-21
**Branch:** `claude/tender-edison-639a71`
**Predecessor plans:** [v1](2026-05-20-mp24-brother-http-plan.md) → [v2 (locked)](2026-05-20-mp24-brother-http-plan-v2.md) → this v3 (reality-check).
**Test baseline at v3 close:** 901 (Phase 2 entry). Target after M.P2.4: ~1010+.

---

## 1. What v3 did

v3 is a **read-only reality-check** against the working codebase to confirm v2's locks survive contact with the actual code, fold any discoveries back into the §4 catalog, and pin every file path / DI pattern that v2 left TBD. No production code changes.

Specifically, v3 closed:

- Q11 (license catalog file location).
- `ISourceAdapter` contract surface (no required members added since FOCAS2 migrated).
- `IHttpClientFactory` DI pattern (no existing precedent; Brother is the first; locked Pattern C).
- `BrotherTagMap` purity (user's concern from 2026-05-21 review pass).
- §4 catalog refinements (ATC slot-vs-tool keying, Status precedence chain).
- Parity-test mechanics (no injection seam in legacy `BrotherHttpDataSource`; locked `HttpListener` test server pattern).
- Legacy code surface (full Brother touchpoint list — narrower than expected; no Core changes required).

v3 also carries ONE non-blocking flag into implementation (the MQTT topic-shape-with-slashes question) for cross-check during step 14 (manual smoke), not as a gate.

---

## 2. Q11 LOCKED — license catalog file location

**File:** [`src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs`](../../src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs)

Existing source-side constants (verified):

```csharp
public const string SourceModbusTcp = "source-modbus-tcp";
public const string SourceFocas2    = "source-focas2";
public const string SourceMtconnect = "source-mtconnect";   // [pre-allocated; adapter not yet migrated]
public const string SourceS7        = "source-s7";
public const string SourceOpcUaClient = "source-opc-ua-client";
```

**Edit for M.P2.4:** add to the `// ----- Sources -----` block:

```csharp
/// <summary>Brother HTTP web-monitoring source adapter (built-in port 80 interface). No proprietary licenses required from Brother — this gates the module within the EdgeConnect license.</summary>
public const string SourceBrotherHttp = "source-brother-http";
```

Reference doc to update at the same step: `docs/licensing/module-catalog.md` (file location TBD — confirm during step 12; v3 read of `LicenseModuleKeys.cs` line 11 confirms the catalog doc path is `docs/licensing/module-catalog.md`).

**Bonus observation:** `SourceMtconnect = "source-mtconnect"` is already pre-allocated. So an eventual MTConnect adapter migration (Q-MTC, out of scope) would only need adapter code + a wizard, not a new license module key. Worth noting in the M.P2.4 handoff.

---

## 3. ISourceAdapter contract — confirmed surface

[`src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs`](../../src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs) inherits from `System.IAsyncDisposable`. Required members:

| Member | Brother implementation |
|---|---|
| `string InstanceId` | trivial pass-through |
| `string ProtocolName` | `"brother-http"` (lowercase, matches FOCAS2's `"focas2"` convention) |
| `SourceCapabilities Capabilities` | `Polling \| Browse` (mirrors FOCAS2; Brother is polling-only; Browse for wizard) |
| `AdapterState State` | from `Focas2SourceAdapter` precedent — backed by a private field updated via `AdapterStateTransitions` |
| `Task InitializeAsync(SourceConfiguration, CT)` | type-check + cast to `BrotherHttpSourceConfiguration`; construct factory + collectors + connection manager + API |
| `Task StartAsync(CT)` | establish first `HTTPD_MCNINFO` round-trip (per Q4 lock) → Running |
| `Task StopAsync(CT)` | graceful, ≤10 s — drain in-flight HTTP requests + cancel collectors |
| `Task<AdapterHealth> CheckHealthAsync(CT)` | snapshot (poll attempts/successes/failures, last success at, last error) |
| `Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CT)` | one fan-out across 6 endpoints → collect → priority-resolve → emit canonical points |
| `IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CT)` | throw `InvalidOperationException("Brother HTTP does not support Subscription")` — we don't declare the capability |
| `Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CT)` | return `BrotherTagMap.BuildTagDefinitions()` |
| `Task<ValidationResult> ValidateConfigAsync(SourceConfiguration, CT)` | cast + validate: required fields, polling clamps (Q10), DataPoints catalog membership (Q7) |
| `ValueTask DisposeAsync()` | dispose HttpClient (via factory pattern §4) + cancel any background tasks |

**Verified:** no new required members since FOCAS2 migrated. Pattern transfer is line-for-line.

---

## 4. IHttpClientFactory DI pattern — LOCKED (Pattern C)

`Focas2RegistrationExtensions.cs` constructs adapters with `new Focas2SourceAdapter(instanceId, logger, identity)` — no `IHttpClientFactory` involvement (FOCAS2 uses native DLL P/Invoke). Modbus is similar (TCP socket, not HTTP). **Brother HTTP is the first source adapter to need `IHttpClientFactory`.**

Three patterns were considered:

- (A) `factory.CreateClient("brother-http-{instanceId}")` named per-instance — verbose registration, fragile naming.
- (B) Typed client (`services.AddHttpClient<IBrotherHttpApi, BrotherHttpHttpApi>()`) — doesn't fit multi-instance with different BaseUrls.
- (C) Inject `IHttpClientFactory` into `BrotherHttpHttpApi`, create a default client per call (or once at construction) and set BaseAddress + Timeout from the typed config.

**LOCKED: Pattern C.** Microsoft's recommended pattern for multi-instance HTTP clients with different configs. Each `BrotherHttpHttpApi` instance:

```csharp
public BrotherHttpHttpApi(IHttpClientFactory factory, BrotherHttpSourceConfiguration config)
{
    _factory = factory;
    _baseUrl = config.BaseUrl.TrimEnd('/');
    _timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
}

private HttpClient CreateClient()
{
    var client = _factory.CreateClient();
    client.BaseAddress = new Uri(_baseUrl);
    client.Timeout = _timeout;
    return client;
}
```

Sockets pool inside `HttpMessageHandler` (managed by `IHttpClientFactory`'s default lifecycle). `HttpClient` is cheap to recreate per call; the underlying handler is reused. Correct at 100-CNC scale.

**Registration extension addition:** at the top of `BrotherHttpRegistrationExtensions.AddBrotherHttpSourcesFromGatewayConfig`, idempotently register:

```csharp
services.AddHttpClient();   // adds IHttpClientFactory if not already present
```

Then construct each adapter passing `IHttpClientFactory` resolved from DI:

```csharp
var factory = sp.GetRequiredService<IHttpClientFactory>();
var api = new BrotherHttpHttpApi(factory, typedConfig);  // or BrotherHttpDemoApi() per Q5
var adapter = new BrotherHttpSourceAdapter(typedConfig.InstanceId, api, logger, identity);
```

---

## 5. BrotherTagMap purity — LOCKED (user's specific concern)

[`Focas2TagMap.cs`](../../src/ElpisEdgeConnect.Sources.Focas2/Focas2TagMap.cs) verified as structural-only:

```csharp
internal sealed record TagMapEntry
{
    public required string TagName { get; init; }
    public required string TagPath { get; init; }
    public required CanonicalValueType ValueType { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
}
```

Zero references to FOCAS2 protocol response bytes, zero references to legacy DTOs. Collectors compute values from FOCAS2 responses independently. This is exactly the shape that lets the same map be safely shared between production and a test-side parity oracle.

**BrotherTagMap surface — LOCKED to the same five members.** No `LegacyFieldName`, no `DefaultValue`, no method that takes a `CncMachineData` reference. The locked surface in code form:

```csharp
internal sealed record BrotherTagMapEntry
{
    public required string TagName { get; init; }
    public required string TagPath { get; init; }
    public required CanonicalValueType ValueType { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
}
```

**Contract-shape purity test (new test added to step 4 of §10 sequence):**

```csharp
[Fact]
public void BrotherTagMapEntry_ExposesOnlyStructuralMembers()
{
    var publicMembers = typeof(BrotherTagMapEntry)
        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field)
        .Select(m => m.Name)
        .ToHashSet();

    publicMembers.Should().BeEquivalentTo(new[]
    {
        nameof(BrotherTagMapEntry.TagName),
        nameof(BrotherTagMapEntry.TagPath),
        nameof(BrotherTagMapEntry.ValueType),
        nameof(BrotherTagMapEntry.Unit),
        nameof(BrotherTagMapEntry.Description),
    });
}
```

Any future PR that adds a "LegacyFieldName" property, a value-transform method, or anything else that could couple `BrotherTagMap` to legacy parser assumptions will fail this test loudly. The test is the structural-purity gate.

**No production-test ref cycle.** The test project references the production project's `BrotherTagMap` type. The production project does NOT reference the test project. `LegacyCanonicalMapper` (test-only) reads `BrotherTagMap.AllEntries` for the catalog and looks up entries by path; it computes values from `CncMachineData` field references that live in the test project's own code. No production rule can be polluted by legacy field references because `BrotherTagMap` has no such surface.

---

## 6. §4 catalog refinements

Two refinements surfaced from line-by-line re-walk of `BrotherHttpDataSource.cs` (§ETag /HTTPD_MCNINFO through /MNTP_MAINTNOTICE) against the v2 §4 catalog.

### 6.1 ATC tools — split slot-keyed vs tool-number-keyed

Legacy `ParseAtcTools` (lines 442–539 of `BrotherHttpDataSource.cs`) uses **hybrid keying**:

- `ToolInfo.Offsets[i]` is **slot-indexed** (list order = magazine slot order). Each entry carries the **tool number** in `entry.Number`, plus geometry length/radius and IsActive.
- `AdditionalData["Tool.{toolNo}.Name"]`, `Tool.{toolNo}.Type`, `Tool.{toolNo}.Life` are **tool-number-keyed** (using the value from `entry.Number`).

v2 §4 collapsed everything under `Tools/Magazine/{slot}/...` which loses the tool-number-keyed semantics for name/type/life. **v3 refinement: split into two sub-namespaces:**

| Path | Type | Notes |
|---|---|---|
| `Tools/ActiveNumber` | int\|null | Tool number of the currently-loaded tool (`activeToolNo`) — unchanged from v2 |
| `Tools/MagazineSize` | int | Count of magazine slot entries — unchanged from v2 |
| `Tools/Magazine/{slot}/Number` | int | Tool number currently in slot — unchanged from v2 |
| `Tools/Magazine/{slot}/Length` | double | Geometry length for tool in slot (legacy `entry.GeometryLength`; `WearLength` is always 0 for Brother and skipped) |
| `Tools/Magazine/{slot}/Radius` | double | Geometry radius for tool in slot (legacy `entry.GeometryRadius`) |
| `Tools/Magazine/{slot}/IsActive` | bool | True only for slot containing the active tool |
| `Tools/Tool/{toolNo}/Name` | string | **NEW** — tool name (legacy `AdditionalData["Tool.{toolNo}.Name"]`; sparse — only when non-empty) |
| `Tools/Tool/{toolNo}/Type` | string | **NEW** — tool type, e.g. `STD tool` (legacy `AdditionalData["Tool.{toolNo}.Type"]`; sparse) |
| `Tools/Tool/{toolNo}/Life` | string | **NEW** — tool life display (legacy `AdditionalData["Tool.{toolNo}.Life"]`; sparse — only when not `********` and non-empty) |

**Assumption preserved from legacy:** tool numbers are unique within a magazine. Two slots with the same tool number would collide on the `Tools/Tool/{toolNo}/...` keys; legacy already silently overwrites in that case. Brother CNCs don't typically support duplicate tool numbers in a magazine, so this is a benign assumption to inherit.

**Field-not-applicable note:** `ToolOffsetEntry.WearLength` and `WearRadius` are always 0 for Brother (legacy `entry = new ToolOffsetEntry { Number, GeometryLength, GeometryRadius, IsActive }` — Wear fields default to 0). The catalog deliberately omits them. The parity oracle (`LegacyCanonicalMapper`) skips Wear* fields when mapping; documented in `LegacyCanonicalMapper.cs` header.

### 6.2 Status/State + Status/Warning precedence chain — LOCKED

Multiple collectors can write `Status/State` and `Status/Warning`. Legacy resolves precedence implicitly via the parser invocation order in `CollectDataAsync` (line 119–124: `ParseMachineInfo → ParseCycleTime → ParseWorkCounters → ParseAtcTools → ParseAlarms → ParseMaintenanceNotices`). The new collectors run logically in parallel (per Q6 lock — one collector per endpoint). To preserve protocol-correct semantics without depending on call order, **lock the precedence as an explicit rule applied by the adapter after collectors finish.**

**`Status/State` precedence (lowest to highest):**

1. **Base:** mapped from `MachineInfo/StatusCode` (0/1/4/5 → `STOP`, 2 → `SUSPEND`, 3 → `OPERATE`).
2. **Informational alarm 501 override:** if Brother emits the standby info alarm, force `Status/State = STOP`, `Status/Running = Idle`. (Legacy `ParseAlarms` lines 587–598.)
3. **Active-alarm override:** if `Alarms/ActiveCount > 0` (after informational + maintenance filtering), force `Status/State = ALARM`. (Legacy `ParseAlarms` lines 626–630.)

**`Status/Warning` precedence (lowest to highest):**

1. **None** (omitted from emission).
2. **Maintenance notice (notified):** if any `Maintenance/Notice/{idx}/State == Notified`, set `Status/Warning = "Maintenance ({description})"`. (Legacy `ParseMaintenanceNotices` lines 698–702.)
3. **Maintenance alarm:** if any alarm message matches maintenance keywords, set `Status/Warning = "Maintenance ({message})"`. (Legacy `ParseAlarms` lines 600–608.)
4. **Informational alarm 501:** set `Status/Warning = message`. (Legacy `ParseAlarms` line 595.)
5. **Active alarm:** set `Status/Warning = first ActiveAlarm.Message`. (Legacy `ParseAlarms` lines 627–629.)

**Implementation in adapter:** the per-poll buffer (`BrotherPollSession`) accumulates raw signal from each collector — initial state code from `MachineInfoCollector`, informational/maintenance/active alarm sets from `AlarmCollector`, notified-maintenance-notice list from `MaintenanceCollector`. After all collectors run, `BrotherHttpSourceAdapter.PollAsync` applies the precedence rules in one place and emits the final `Status/State` and `Status/Warning` canonical points.

**Why this is protocol-correct and not legacy-coupled:** the precedence reflects "real alarms trump informational status; informational standby trumps the status-code lookup; maintenance alarms degrade the warning surface differently from real alarms." These are Brother-protocol semantics, not legacy-parser idiosyncrasies. The parity oracle (`LegacyCanonicalMapper`) applies the same precedence to the legacy `CncMachineData` (which has already had the order-dependent writes baked in) — so both sides agree, but for protocol reasons, not legacy reasons.

### 6.3 Fields deliberately omitted from catalog (documented for the no-leak audit)

The following `CncMachineData` fields are intentionally NOT in the canonical catalog:

| Legacy field | Reason for omission |
|---|---|
| `Status` (`MachineStatus` enum) | Adapter lifecycle state, not a canonical tag. Supervisor knows from `AdapterState`. |
| `Disconnect_CNC` | Same — adapter lifecycle, not a tag. |
| `CollectionDurationMs` | Diagnostic, not a tag. Exposed via `CheckHealthAsync` if needed. |
| `Tags` | Carried from `MachineConfig`, identical per source instance; not protocol-derived data. |
| `Timestamp` | Already in canonical point header as `DeviceTimestamp` / `GatewayTimestamp`. |
| `SystemInfo.Series` (`"Brother {model}"`) | Derived from `MachineInfo/Model`. Redundant. |
| `SystemInfo.CncType` (always `"Brother"`) | Constant. Not protocol-derived. |
| `SystemInfo.Version` (= hostname) | Redundant with `MachineInfo/Hostname`. |
| `MainProgram_path1_CNC`, `ActProgram_path1_CNC` | Aliases for `MainProgram`. Already covered by `Program/Active`. |
| `PartsNum_path1_CNC` | Int-cast alias for `PartsCount`. Already covered by `Production/PartsCount`. |
| `ToolInfo.OffsetMemoryType` (always `"Brother"`) | Constant. |
| `ToolInfo.ToolLifeEnabled` (always `false`) | Constant; Brother doesn't expose tool life management. |
| `ToolOffsetEntry.WearLength`, `WearRadius` | Always 0 for Brother. |
| All FOCAS2-only `*_path1_CNC` fields (60+ fields including `SigCUT_path1_CNC`, `CncFan1Status_path1_CNC`, `ServoTemp_*`, `SpindleTemp_*`, battery flags, etc.) | Set by Focas2 collectors, never by Brother. Out of scope for the Brother catalog. |
| `Axes` (`Dictionary<string, AxisData>`) | Brother HTTP doesn't expose axis positions. |
| `Spindle` (`SpindleData`) | Brother HTTP doesn't expose spindle data. |
| `FeedRate` | Brother HTTP doesn't expose feed rate. |

The `LegacyCanonicalMapper` explicitly enumerates the fields it WILL map (positive list) and asserts at test-build-time that every other field in `CncMachineData` is in the deliberately-omitted set (the negative list above). This catches the case where someone adds a new field to legacy `CncMachineData` without updating the catalog.

### 6.4 Catalog evolution rule (v3.1 lock)

Existing canonical paths in `BrotherTagMap` are **append-only and semantically stable** within the M.P2.x line. Renaming a path or repurposing its meaning is a breaking change requiring an explicit version-bump milestone (e.g., M.P3.x) and a downstream-consumer migration plan. EREMOS V2 dashboards / OEE consumers / historian schemas will start depending on the exact tag paths post-soak; silent repurposing would break them without warning.

---

## 7. Parity test infrastructure — LOCKED

Legacy `BrotherHttpDataSource.cs:71` constructs its own `HttpClient`:

```csharp
_httpClient = new HttpClient
{
    BaseAddress = new Uri(_brotherSettings.BaseUrl.TrimEnd('/')),
    Timeout = TimeSpan.FromSeconds(_brotherSettings.TimeoutSeconds)
};
```

No injection seam. Options considered:

- (a) Modify legacy to accept an injected `HttpMessageHandler` — **rejected**, scope creep into legacy code (chip prompt says "promote… into a new project"; modifying legacy is out of scope).
- (b) Reflection-based `_httpClient` field swap — **rejected**, fragile, hidden.
- (c) `Microsoft.AspNetCore.TestHost` — **rejected**, adds an ASP.NET Core dependency to the test project for a small need.
- (d) `System.Net.HttpListener` test server in the test project, serving canned bytes from `Samples/{scenario}/` — **LOCKED**.

**`BrotherHttpTestServer` (test-only)** lives at `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/BrotherHttpTestServer.cs`. ~50 LOC. Constructor takes a `string samplesDir`, picks an ephemeral local port via `HttpListener.Prefixes.Add("http://localhost:{port}/")`, and serves files matching the request path (`/HTTPD_MCNINFO` → `samplesDir/HTTPD_MCNINFO.txt`). Returns 404 for unknown paths. `Dispose` stops the listener.

**Parity test flow (per fixture scenario):**

```csharp
[Theory]
[InlineData("running")]
[InlineData("idle")]
[InlineData("alarm")]
[InlineData("standby")]
[InlineData("maintenance-overdue")]
[InlineData("offline")]
public async Task LegacyOracle_AndNewAdapter_ProduceEquivalentCanonicalPoints(string scenario)
{
    using var server = new BrotherHttpTestServer($"Samples/{scenario}");
    var baseUrl = server.BaseUrl;

    // Legacy oracle: real HttpClient against test server
    var legacyConfig = new MachineConfig { … BrotherHttp = new BrotherHttpSettings { BaseUrl = baseUrl, TimeoutSeconds = 5 } };
    using var legacy = new BrotherHttpDataSource(legacyConfig, NullLogger.Instance);
    var legacyDto = await legacy.CollectDataAsync(CancellationToken.None);
    var oraclePoints = LegacyCanonicalMapper.Map(legacyDto, factoryFixture);

    // New adapter: same test server, same factory fixture
    var newConfig = BrotherHttpSourceConfiguration.FromSourceInstance(/* equivalent SourceInstanceConfig */);
    var api = new BrotherHttpHttpApi(httpClientFactoryFixture, newConfig);
    var newAdapter = new BrotherHttpSourceAdapter(newConfig.InstanceId, api, NullLogger.Instance, identityFixture);
    await newAdapter.InitializeAsync(newConfig, default);
    await newAdapter.StartAsync(default);
    var adapterPoints = await newAdapter.PollAsync(default);

    // Compare as sets (tag-path keyed)
    adapterPoints.ToDictionary(p => p.TagPath).Should().BeEquivalentTo(
        oraclePoints.ToDictionary(p => p.TagPath),
        opts => opts.Excluding(p => p.DeviceTimestamp)
                    .Excluding(p => p.GatewayTimestamp)
                    .Excluding(p => p.SequenceNumber));
}
```

**Sample fixture corpus** (lives at `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Samples/`):

- `running/` — machine in cycle, has program, parts counted, no alarms
- `idle/` — machine idle, no program running
- `alarm/` — real alarm active (non-informational, non-maintenance)
- `standby/` — informational alarm 501 (Standby mode) active
- `maintenance-overdue/` — maintenance notice in Notified state
- `offline/` — only `HTTPD_MCNINFO` returns success; other endpoints return 404 (tests degraded-but-not-faulted state per Q4)

Each scenario folder contains 6 `.txt` files: `HTTPD_MCNINFO.txt`, `MNTP_CYCLETIME.txt`, `MNTP_WKCNTR.txt`, `ATC_TOOLS.txt`, `ALARM_CURALMLIST.txt`, `MNTP_MAINTNOTICE.txt`. Bytes are taken from the example formats documented in `BrotherHttpDataSource.cs` comments (e.g. line 213 `"BRN68E74A6608EA,SXd1,3,01,0,1"` for HTTPD_MCNINFO).

**Test project reference graph:**

```
ElpisEdgeConnect.Sources.BrotherHttp.Tests
   ├─► ElpisEdgeConnect.Sources.BrotherHttp     (new project under test)
   ├─► ElpisEdgeConnect.Core                    (canonical types)
   └─► ElpisEdgeConnect                          (legacy — for the parity oracle only)
```

The new production project (`ElpisEdgeConnect.Sources.BrotherHttp`) does NOT reference `ElpisEdgeConnect` — preserving the §2 no-leak lock.

---

## 8. Legacy code surface — full inventory

Walked `src/ElpisEdgeConnect/` for every file Brother-touching. Findings:

| File | Touchpoint | Migration action |
|---|---|---|
| `DataSources/BrotherHttpDataSource.cs` | The adapter (legacy) | Read-only — kept as parity oracle. |
| `DataSources/IMachineDataSource.cs` | Legacy contract | Read-only — never imported by new project. |
| `DataSources/DataSourceFactory.cs` | Legacy registration | NOT migrated — new arch uses `BrotherHttpRegistrationExtensions`. Untouched. |
| `DataSources/DataSourceType.cs` (enum value `BrotherHttp`) | Legacy registration switch | NOT migrated — new arch uses `protocolName="brother-http"` string. Untouched. |
| `Configuration/MachineConfig.cs` | Holds `BrotherHttpSettings? BrotherHttp` and `DataSourceType` | NOT migrated — new arch uses `SourceInstanceConfig`. Untouched. |
| `Configuration/MachineConfig.cs` → `BrotherHttpSettings` | Two fields only: `BaseUrl` (string), `TimeoutSeconds` (int) | Mapped to new `BrotherHttpSourceConfiguration` (richer surface — adds Port, DataPoints, FaultThresholdConsecutiveFailures, backoff). |
| `Models/CncMachineData.cs` | Legacy DTO; oracle for parity test | Read-only — referenced by test project only. |
| `Models/ToolInfoData.cs` (ToolOffsetEntry, ToolLifeEntry) | Legacy DTO | Read-only — referenced by test project only. |
| `Models/CncSystemInfo.cs` | Legacy DTO | Read-only — referenced by test project only. |
| `Models/MachineStatus.cs` (enum inside CncMachineData.cs) | Legacy status enum | Read-only — not in canonical catalog (see §6.3). |
| Legacy `MachinePollerService` (referenced from header comment) | Legacy data acquisition orchestrator | Not touched — new arch uses `SourceSupervisor` + `RouteWorker`. The legacy poller and the new supervisor never run side-by-side for the same instance. |

**No Core changes required.** The new arch's `ISourceAdapter` contract, `SourceConfiguration` base, and DI plumbing already accept Brother's needs.

---

## 9. Non-blocking flag — MQTT topic shape with slashes

`Focas2TagMap.cs` declares `TagName = "axes/x/absolute"` with slashes. The MQTT sink's PerTag topic is documented (deployment-readiness §4 line 164) as `eremos/{gatewayId}/cnc/{sourceId}/{tagName}`, and EREMOS V2 subscribes to `eremos/+/cnc/+/+`.

If `{tagName}` substitutes literally and contains slashes, the topic becomes `eremos/gw/cnc/src/axes/x/absolute` — 7 segments — which does NOT match `eremos/+/cnc/+/+` (5 segments, single-level wildcard).

Three possibilities:

- (a) MQTT sink transforms slashes in tagName before substitution.
- (b) EREMOS V2 actually subscribes to `eremos/+/cnc/+/#` (multi-level), and the doc is imprecise.
- (c) FOCAS2 collectors emit a slash-flattened tagName at runtime that's different from `Focas2TagMap.X.TagName`.

**This is NOT a Brother-specific issue.** Brother mirrors FOCAS2's pattern, so whatever works for FOCAS2 works for Brother. v3 carries this as a **cross-check during step 14 manual smoke** (verify Brother PerTag topics arrive at EREMOS V2 as expected), not as a gate.

If the cross-check fails (i.e., the slash mismatch is real and Brother's topics don't reach EREMOS), the fix lives in the MQTT sink or the EREMOS subscription — not in `BrotherTagMap`. The catalog itself is contract-correct.

Resolution carried to handoff doc; not blocking.

---

## 10. Implementation sequence — locked from v2 §10, with v3 refinements

Updated steps (changes from v2 in **bold**):

| Step | What | Gate |
|---|---|---|
| 1 | Cross-reference doc edits: M.P2.3 → M.P2.4 in deployment-readiness §2/§6 + chips doc Chip 2 title | Mechanical; no review |
| 2 | Project skeleton + sln registration + namespace placeholder test | Build clean; 901 → 902 |
| 3 | `IBrotherHttpApi` + `BrotherHttpHttpApi` (real, via `IHttpClientFactory` per §4) + `BrotherHttpDemoApi` (synthetic, 5 scenarios cycling + state evolution per v3.1 §C.2) + `BrotherHttpDemoModeOptions` | Demo-real dispatch test green |
| 4 | `BrotherTagMap.cs` (catalog as code per §6) **+ structural-purity contract test (§5)** | Tests assert catalog completeness against v2 §4 + v3 §6 refinements + purity test green |
| 5 | Six collectors (one per endpoint, Q6) **— emit raw signal into `BrotherPollSession`, no precedence logic** | Per-endpoint parser tests against sample fixtures green |
| 6 | `BrotherHttpSourceConfiguration` + `FromSourceInstance` factory + DataPoints validator (Q7 inline, catalog membership) + DataPoints normalization per v3.1 §B.6 (three new tests: prefix-and-leaf-collapse, trailing-slash-dedup, unknown-entry-rejection) | Config round-trip tests + validation tests + normalization tests green |
| 7 | `BrotherHttpConnectionManager` + `BrotherHttpSourceAdapter` lifecycle **+ precedence-chain post-processor (§6.2) emitting `Status/State` and `Status/Warning` after collectors finish + single-flight guard (v3.1 §B.3) + atomic-batch assembly (v3.1 §B.1) + single timestamp authority captured at cycle start (v3.1 §B.2) + no fire-and-forget Tasks (v3.1 §B.4)** | State machine tests + per-adapter isolation tests + precedence tests + atomicity test + single-flight test green |
| 8 | **`BrotherHttpTestServer` (HttpListener, §7) + sample fixture corpus (6 scenarios × 6 endpoint files) + `LegacyCanonicalMapper.cs` (test-only) + parity test (§7)** | Parity test green across all 6 sample scenarios |
| 9 | `BrotherErrors.cs` + finalize error taxonomy from steps 3–8 surfaced codes | Error code stability test green |
| 10 | `BrotherHttpRegistrationExtensions` + `EdgeConnectComposition` edit + license module key (§2 — `LicenseModuleKeys.SourceBrotherHttp`) **+ `services.AddHttpClient()` registration** | License-gate no-op test green; instance materialization test green |
| 11 | Studio wizard (Q12 scope) + `AddSource.razor` picker + Test Connection button | Wizard model tests green; manual Studio smoke against demo mode |
| 12 | `docs/licensing/module-catalog.md` update | Build clean |
| 13 | Full solution regression sweep + Brother-specific metrics verified at `/metrics` per v3.1 §B.5 | All-projects test pass; zero warnings; coverage ≥80% on new project; three Brother metrics emit with bounded cardinality |
| 14 | Manual end-to-end Studio smoke: add Brother source via wizard (demo mode), wire to MQTT sink, verify canonical-point flow → `mosquitto_sub` **+ cross-check §9 (slash-in-tagName topic shape)** | Confirms invariant from Bug 2 holds for the new adapter; §9 either confirmed working or escalated as follow-up |
| 15 | Commit + handoff doc + plan-trail finalization | PR opens; deployment-readiness §2 marks Brother HTTP migration row complete |

**Estimated effort:** ~10 working days. Step 5 (collectors) and step 8 (parity infrastructure) are the highest-variance.

---

## 11. Definition of done — confirmed from v2 §11, with v3 additions

Carrying v2 DoD verbatim with two additions (marked **NEW**):

- [ ] M.P2.4 naming applied throughout (deployment-readiness doc + chips doc + this plan trail).
- [ ] All new tests green; ≥80% coverage on `src/ElpisEdgeConnect.Sources.BrotherHttp/`.
- [ ] Zero new warnings (TreatWarningsAsErrors enforced).
- [ ] Full solution test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"`.
- [ ] Parity test (§7) passes across all 6 sample scenarios.
- [ ] No production code in `src/ElpisEdgeConnect.Sources.BrotherHttp/` references legacy types from `ElpisEdgeConnect.Models`.
- [ ] **NEW: `BrotherTagMapEntry` structural-purity test (§5) green** — surface limited to TagName/TagPath/ValueType/Unit/Description.
- [ ] **NEW: `LegacyCanonicalMapper` deliberate-omission audit** — every `CncMachineData` field is either mapped (in catalog) or explicitly in the §6.3 omission list; test fails if a new legacy field is added without classification.
- [ ] License gate verified: registration is a no-op when `source-brother-http` module is disabled.
- [ ] Brother source can be added through Studio wizard end-to-end (manual smoke against demo mode).
- [ ] Demo mode dispatch verified (`BrotherHttpDemoModeOptions` toggle).
- [ ] Polling cadence clamps verified: validation rejects `<500ms` with `BROTHER.POLL_TOO_FAST`, warns `500..1000ms`, accepts ≥`1000ms`.
- [ ] Plan trail captured: v1 → v2 → v3 (this) → implementation handoff.
- [ ] Cross-reference: deployment-readiness §2 marked complete; chips doc Chip 2 marked closed.
- [ ] §9 topic-shape cross-check resolved either green (Brother PerTag topics reach EREMOS V2 in the soak) or escalated as a known follow-up not blocking M.P2.4.
- [ ] **v3.1: poll-cycle atomicity + single timestamp pinned by tests** — a test seeds a slow endpoint and asserts all points emitted by that cycle share an identical `DeviceTimestamp` and `GatewayTimestamp` captured before the slow endpoint started.
- [ ] **v3.1: single-flight no-overlap pinned by tests** — a test schedules two `PollAsync` calls concurrently and asserts the second returns an empty list immediately + `poll_overruns_total` increments by 1, no `poll_duration_ms` recorded for the skipped tick.
- [ ] **v3.1: no fire-and-forget audit clean** — grep + manual review of all `async` invocations in the new project confirms zero unobserved Tasks.
- [ ] **v3.1: metrics surface verified at /metrics** — demo-mode smoke run produces all three Brother-specific metrics with bounded cardinality (one `source` × six `endpoint` values for `endpoint_failures_total`).

---

## 12. Items v3 did NOT need to change from v2

Confirming v2's locks survived contact with code:

- M.P2.4 naming — still locked.
- §4 canonical catalog (with §6 refinements applied) — still the central design artifact.
- §2 no-legacy-DTO-leak invariant — verified achievable; production project will not reference `ElpisEdgeConnect.Models`.
- Demo mode in scope — verified, fits the `IFocas2Api`/`Focas2DemoApi` precedent perfectly.
- Parity oracle is test-only — verified, lives only in the test project.
- Q1, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q12, Q-MTC verdicts — all still hold after reality-check.
- 1-collector-per-endpoint (Q6) — collectors emit raw signal; precedence-chain post-processor (new in §6.2) handles cross-collector resolution.

---

## 13. Pause-point criteria during implementation

Stop and report if:

- Step 3 reveals an `IHttpClientFactory` composition conflict with existing host DI lifetimes that the pattern in §4 doesn't address.
- Step 4's purity test (`BrotherTagMapEntry_ExposesOnlyStructuralMembers`) is impossible to satisfy because the catalog needs a method that takes a `CncMachineData` reference for some reason I missed (would invalidate §5 lock).
- Step 5's collectors discover a legacy parsing branch that emits a `CncMachineData` field not in the v3 §6 catalog or §6.3 omission list (would mean v3 missed a tag — add to catalog).
- Step 7's precedence-chain post-processor produces canonical points that disagree with `LegacyCanonicalMapper` even after applying §6.2 rules — that means either §6.2 has a bug or legacy has order-dependent behavior I missed.
- Step 8 (parity test) reveals a sample-fixture vs legacy mismatch — i.e., my synthesized sample bytes don't actually parse the way the legacy comments imply. Need to capture real Brother responses if available, or refine sample fixtures.
- Step 14's §9 topic-shape cross-check fails AND blocks the soak — escalate to MQTT sink team / EREMOS V2 contract.

---

## 14. v3 sign-off

All v2 open items closed:

- Q11 → **§2** (LicenseModuleKeys.cs file confirmed; line to add specified).
- v2 §7 license catalog row (TBD) → resolved to LicenseModuleKeys.cs.
- v2 §12 risk #6 (license catalog) → mitigated by §2.
- v2 §12 risk #5 (`IHttpClientFactory` interaction) → mitigated by §4 (Pattern C locked; no conflict found).
- v2 §12 risk #2 (`BrotherTagMap` purity drift) → mitigated by §5 contract-shape test.
- v2 §12 risk #3 (parity oracle ↔ catalog drift) → mitigated by §6.3 deliberate-omission audit test.
- v2 §12 risk #9 (catalog incompleteness) → mitigated by §6.1 ATC keying refinement + §6.2 precedence chain.
- ISourceAdapter contract surface → confirmed (§3); no new required members.
- Parity test infrastructure → §7 HttpListener-based test server pattern locked.
- §9 non-blocking flag → carried to step 14 manual smoke.

**v3 LOCKED. Implementation may start at §10 step 1.**

User's specific v3 instruction ("verify whether BrotherTagMap.cs can be safely referenced by test-side LegacyCanonicalMapper without accidentally making production mapping rules depend on legacy parser assumptions") answered in §5: yes, BrotherTagMap will be structural-only mirroring `Focas2TagMap` exactly, and the contract-shape purity test prevents future drift. The parity oracle's value-transformation logic stays inside the test project; the catalog itself carries zero legacy-parser-coupled members.

---

**End of v3 reality-check. Next step: begin step 1 of §10 (cross-reference doc renames) when user gives the implementation go-ahead.**
