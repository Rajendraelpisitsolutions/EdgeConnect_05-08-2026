# M.P2.2 Phase 2.b — Tactical implementation plan v2

**Date:** 2026-05-16
**Branch:** `claude/m-p2-2-hot-reload` (tip: `5857634` after Phase 2.a)
**Status:** **Locked.** Implementation may proceed.
**Related docs:**
- `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` (ADR-0009)
- `docs/sessions/2026-05-16-mp22-kickoff.md`
- `docs/sessions/2026-05-16-mp22-phase2-design.md` §4 (design baseline)
- `docs/sessions/2026-05-16-mp22-phase2a-plan.md` (Phase 2.a — shipped)

This is the locked tactical plan for Phase 2.b only. It supersedes the
inline v1 plan from the chat session — v1 was reviewed and 10 items
were folded into v2; v2 was reviewed and 3 final adjustments were
folded into this doc.

---

## 0. Review resolution log

Two review passes; all 13 items dispositioned. The most important
architectural correction came from review pass #1 item #1 (DI lambda
throw was a behavior change) and item #2 (eager-vs-deferred
construction); the design now uses a three-layer split that
preserves boot semantics bit-identically while sharing decision +
construction logic across boot and hot-reload paths.

| Pass | # | Item | Outcome |
|---|---|---|---|
| 1 | 1 | DI lambda throw on null | Accepted. Replaced with `continue` (skip silently). |
| 1 | 2 | Hidden double-construction; eager registration | Accepted partially. Preflight eager; adapter construction stays deferred for the boot path (because `ILoggerFactory` / `IGatewayIdentity` are DI-resolved). Three-layer split converges boot + hot-reload paths. |
| 1 | 3 | `internal` not `public` on per-protocol `Build*` | Accepted. Tests use `InternalsVisibleTo`. |
| 1 | 4 | Protocol-name `OrdinalIgnoreCase` | Accepted. Plus centralized `Is{Protocol}Protocol` helpers (review pass #2 item #4). |
| 1 | 5 | Factory must not register faults for disabled instances | Accepted. Pinned in §2 non-goals. |
| 1 | 6 | Route selector abstraction | No change. |
| 1 | 7 | Defer `RegistrationBuildResult` | No change. |
| 1 | 8 | Non-goals section | No change. |
| 1 | 9 | Add lazy-resolution test | Accepted as test #12. |
| 1 | 10 | `ObservedAtUtc` timestamp ownership | Acknowledge only. Pinned as future-note for 2.c — coordinator MUST NOT overwrite the factory-set timestamp. |
| 2 | 1 | Rename `ResolvePreflight` | Accepted. Becomes `ResolveSourceRegistrationInputs` / `ResolveSinkRegistrationInputs`. |
| 2 | 2 | `ConstructRegistration` never registers faults | Accepted. Locked in §6 + non-goals. |
| 2 | 4 | Centralized `Is{Protocol}Protocol` helper | Accepted. Each extension gets one; dispatcher consults them. |

---

## 1. Scope

Phase 2.b extracts a per-instance registration builder from each of
the six protocol `*RegistrationExtensions.cs` files. The coordinator
(Phase 2.c) needs to construct a `SourceRegistration` /
`SinkRegistration` for ONE instance at a time, without going through
DI (the container is sealed after boot). Today, that logic is fused
with the boot-time DI loop.

Three new files + six modified files. **Boot-time external behavior
unchanged.** Production code does not call the new factory yet — that
hookup lands in Phase 2.c.

**Test target:** 1681 baseline → **1693** (12 new tests).

---

## 2. Out of scope + non-goals locked

### Out of scope

- `RuntimeReloadCoordinator` — Phase 2.c.
- `IConfigurationManager.CurrentChanged` subscription — Phase 2.c.
- `HostStartup` / `CompositionRoot` / `EdgeConnectComposition` changes — Phase 2.c.
- `ApplyResultDto.Reload` block / Razor changes — Phase 3.
- Renaming `SinkRegistration.RouteId` — deferred past Phase 2 entirely.
- Removing the existing DI-time extension methods — they stay, just delegate to shared helpers.
- Full-eager registration construction — would require composition
  restructuring to make `ILoggerFactory` / `IGatewayIdentity`
  available before `BuildServiceProvider()`. Documented future work.

### Non-goals locked

> `IRegistrationFactory` is a stateless dispatcher. It does not own
> configuration, identity, logging, or license state. All of those
> flow in via constructor / method parameters. Future contributors
> must not turn it into a service-locator-style "ambient context" —
> adapters get their dependencies from the `IServiceProvider`
> parameter at construction time, exactly as the DI factories do
> today.

> The factory's `BuildSource` / `BuildSink` and the per-protocol
> `Resolve{Source,Sink}RegistrationInputs` helpers ASSUME the caller
> has filtered out disabled instances. Disabled is operator intent,
> not a fault. **The factory MUST NEVER observe `Enabled == false`.**
> Future contributors must not add `if (!src.Enabled) ...` to the
> factory — that's the coordinator's responsibility (Phase 2.c plan).

> The `Construct{Source,Sink}Registration` helpers MUST NEVER
> register configuration faults. Fault registration is the
> responsibility of the resolve helpers ONLY. Construct either
> succeeds or throws — clean separation makes future preview/dry-run
> modes possible without surprise side effects.

---

## 3. Files (new + modified)

### New

| File | Purpose | LOC est. |
|---|---|---|
| `src/ElpisEdgeConnect.Host/Adapters/IRegistrationFactory.cs` | Dispatcher contract (interface) | ~35 |
| `src/ElpisEdgeConnect.Host/Adapters/RegistrationFactory.cs` | Default implementation — switch on `ProtocolName`, normalized + via `Is{Protocol}Protocol` helpers | ~140 |
| `tests/ElpisEdgeConnect.Host.Tests/Adapters/RegistrationFactoryTests.cs` | 12 tests | ~310 |

### Modified

| File | Change | LOC delta |
|---|---|---|
| `src/ElpisEdgeConnect.Host/Adapters/ModbusTcpRegistrationExtensions.cs` | Add three internal statics (`ResolveSourceRegistrationInputs`, `ConstructSourceRegistration`, `BuildSource`); rewrite `AddModbusTcpSourcesFromGatewayConfig` to call resolve + deferred construct | +~70 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/Focas2RegistrationExtensions.cs` | Same shape | +~70 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/MTConnectRegistrationExtensions.cs` | Same shape | +~70 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/S7RegistrationExtensions.cs` | Same shape | +~70 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/MqttRegistrationExtensions.cs` | Same shape (sink side) | +~70 / -~30 |
| `src/ElpisEdgeConnect.Host/Adapters/OpcUaServerRegistrationExtensions.cs` | Same shape (sink side) | +~70 / -~30 |
| `tests/ElpisEdgeConnect.Host.Tests/ElpisEdgeConnect.Host.Tests.csproj` | None — `InternalsVisibleTo` already in `src/.../ElpisEdgeConnect.Host.csproj` |  — |
| `src/ElpisEdgeConnect.Host/ElpisEdgeConnect.Host.csproj` | Add `<InternalsVisibleTo Include="ElpisEdgeConnect.Host.Tests" />` if not already present | +1 |

**Total budget:** ~485 production + ~310 tests. Modest growth over v1 (~430+280) from the three-layer split — pays off in clarity.

---

## 4. `IRegistrationFactory` contract

```csharp
namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Stateless per-instance registration builder. The hot-reload
/// coordinator (M.P2.2 phase 2.c) calls this to construct a
/// SourceRegistration / SinkRegistration for ONE configuration
/// instance at apply-time, without going through DI.
/// </summary>
public interface IRegistrationFactory
{
    /// <summary>
    /// Build a SourceRegistration for the given source instance.
    /// </summary>
    /// <param name="src">
    /// Enabled source instance config. The factory ASSUMES the caller
    /// has filtered disabled instances — disabled is intent, not a
    /// fault. Violating this assumption is a contract bug.
    /// </param>
    /// <param name="gateway">Gateway-level settings.</param>
    /// <param name="routeIdSelector">
    /// Resolves a source instance id to its referencing route id.
    /// Returns null when no enabled route references the source —
    /// in which case the factory registers
    /// CONFIG.SOURCE_WITHOUT_ROUTE in <paramref name="faultRegistry"/>
    /// and returns null.
    /// </param>
    /// <param name="license">License manager (optional in dev/test).</param>
    /// <param name="faultRegistry">Where to register cross-record validation faults.</param>
    /// <param name="serviceProvider">For resolving ILoggerFactory + IGatewayIdentity.</param>
    /// <returns>
    /// A SourceRegistration ready to hand to SourceSupervisor.AddAsync,
    /// or null when the protocol is unrecognised, the license module
    /// is disabled, OR a cross-record fault was registered.
    /// </returns>
    SourceRegistration? BuildSource(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider);

    /// <summary>Mirror for sinks.</summary>
    SinkRegistration? BuildSink(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider);
}
```

### Return-null semantics

`null` is the unified "this instance will not be supervised" signal:

1. **License module disabled** — no fault. Log to stderr (matches boot path today).
2. **Cross-record validation failure** (no route references this source) — fault registered; null returned.
3. **Unrecognised `ProtocolName`** — no protocol's helper claimed it. Dispatcher logs at Warning. Defensive null return.

The coordinator's `TryWithFaultAsync` wraps the call site; any
thrown exception from construction becomes `HOST.RECONCILE_FAILED` —
not a null.

---

## 5. The three-layer split per protocol

Each `*RegistrationExtensions` class gets three new `internal static`
methods. Same shape across all six protocols.

### Layer 1: `ResolveSourceRegistrationInputs` (decision phase)

**Pure-ish.** Eager. No `IServiceProvider` needed. Validates + decides
skip. **MAY register faults.**

```csharp
internal static (ModbusTcpSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
    SourceInstanceConfig src,
    GatewaySettings gateway,
    Func<string, string?> routeIdSelector,
    ILicenseManager? license,
    IConfigurationFaultRegistry? faultRegistry)
{
    // Defensive: protocol-name filter. Caller should have done this
    // but the dispatcher relies on uniform behavior.
    if (!IsModbusProtocol(src.ProtocolName)) return null;

    // License check — no fault registered (license-disabled is intent).
    if (license is { Current: not null }
        && !license.IsModuleEnabled(ModbusTcpSourceConfiguration.LicenseModuleKey))
    {
        Console.Error.WriteLine(
            $"[license] Modbus TCP source '{src.InstanceId}' configured but " +
            $"license module '{ModbusTcpSourceConfiguration.LicenseModuleKey}' " +
            "is not enabled. Skipping registration.");
        return null;
    }

    // Route resolution — register fault on miss.
    var routeId = routeIdSelector(src.InstanceId);
    if (string.IsNullOrEmpty(routeId))
    {
        faultRegistry?.Register(new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Source,
            InstanceId = src.InstanceId,
            ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
            Message = $"Modbus source '{src.InstanceId}' is enabled but no enabled route references it.",
            ObservedAtUtc = DateTime.UtcNow,
        });
        return null;
    }

    // Typed-config translation.
    var typedConfig = ModbusTcpSourceConfiguration.FromSourceInstance(src) with
    {
        GatewayId = gateway.GatewayId,
    };
    return (typedConfig, routeId);
}
```

### Layer 2: `ConstructSourceRegistration` (construction phase)

**Pure.** Needs `IServiceProvider` for logger + identity. **MUST NOT
register faults.** Throws on adapter constructor failure.

```csharp
internal static SourceRegistration ConstructSourceRegistration(
    ModbusTcpSourceConfiguration typedConfig,
    string routeId,
    IServiceProvider sp)
{
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<ModbusTcpSourceAdapter>();
    var identity = sp.GetService<IGatewayIdentity>();
    var adapter = new ModbusTcpSourceAdapter(typedConfig.InstanceId, logger, identity);
    return new SourceRegistration
    {
        Adapter = adapter,
        Config = typedConfig,
        RouteId = routeId,
    };
}
```

### Layer 3: `BuildSource` (composite for the factory)

Thin orchestration. Resolve + construct.

```csharp
internal static SourceRegistration? BuildSource(
    SourceInstanceConfig src,
    GatewaySettings gateway,
    Func<string, string?> routeIdSelector,
    ILicenseManager? license,
    IConfigurationFaultRegistry? faultRegistry,
    IServiceProvider sp)
{
    var inputs = ResolveSourceRegistrationInputs(src, gateway, routeIdSelector, license, faultRegistry);
    if (inputs is null) return null;
    return ConstructSourceRegistration(inputs.Value.TypedConfig, inputs.Value.RouteId, sp);
}
```

### Protocol-identity helper

```csharp
internal static bool IsModbusProtocol(string protocolName)
    => string.Equals(
        protocolName,
        ModbusTcpSourceConfiguration.ProtocolNameConstant,
        StringComparison.OrdinalIgnoreCase);
```

Each extension exposes its own `Is{Protocol}Protocol` helper. The
dispatcher consults them. Future alias support (e.g., `"mqtt"` ==
`"mqtt-publisher"`) lands in one place per protocol.

### DI extension rewrite (boot path, semantics preserved)

```csharp
public static IServiceCollection AddModbusTcpSourcesFromGatewayConfig(
    this IServiceCollection services,
    GatewayConfiguration gatewayConfig,
    Func<string, string?> routeIdSelector,
    ILicenseManager? license = null,
    IConfigurationFaultRegistry? faultRegistry = null)
{
    foreach (var src in gatewayConfig.Sources)
    {
        if (!src.Enabled) continue;
        if (!IsModbusProtocol(src.ProtocolName)) continue;

        // Eager preflight: license check, route resolution, typed
        // config. Returns null on skip (license-disabled or no route).
        var inputs = ResolveSourceRegistrationInputs(
            src, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry);
        if (inputs is null) continue;  // ← skip silently, NO THROW

        // Adapter construction is deferred via the DI lambda because
        // ILoggerFactory + IGatewayIdentity aren't built yet at this
        // point in composition.
        var typedConfig = inputs.Value.TypedConfig;
        var routeId = inputs.Value.RouteId;
        services.AddSingleton<SourceRegistration>(sp =>
            ConstructSourceRegistration(typedConfig, routeId, sp));
        ReplaceSourceRegistrationEnumerable(services);
    }
    return services;
}
```

**Boot semantics:** identical to today. License-disabled, no-route,
wrong-protocol all `continue`. No throws inside the lambda. The
lambda is now a one-liner that only does the construction step.

---

## 6. `RegistrationFactory` dispatcher

```csharp
public sealed class RegistrationFactory : IRegistrationFactory
{
    private readonly ILogger<RegistrationFactory> _logger;

    public RegistrationFactory(ILogger<RegistrationFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public SourceRegistration? BuildSource(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(routeIdSelector);
        ArgumentNullException.ThrowIfNull(sp);

        // Each protocol's IsXxxProtocol helper handles its own
        // canonicalisation (OrdinalIgnoreCase). Centralised so future
        // alias support lives in the protocol module.
        if (ModbusTcpRegistrationExtensions.IsModbusProtocol(src.ProtocolName))
            return ModbusTcpRegistrationExtensions.BuildSource(src, gateway, routeIdSelector, license, faultRegistry, sp);
        if (Focas2RegistrationExtensions.IsFocas2Protocol(src.ProtocolName))
            return Focas2RegistrationExtensions.BuildSource(src, gateway, routeIdSelector, license, faultRegistry, sp);
        if (MTConnectRegistrationExtensions.IsMTConnectProtocol(src.ProtocolName))
            return MTConnectRegistrationExtensions.BuildSource(src, gateway, routeIdSelector, license, faultRegistry, sp);
        if (S7RegistrationExtensions.IsS7Protocol(src.ProtocolName))
            return S7RegistrationExtensions.BuildSource(src, gateway, routeIdSelector, license, faultRegistry, sp);

        _logger.LogWarning(
            "RegistrationFactory: unrecognised source protocol '{Protocol}' for instance '{Id}'. " +
            "Validate gateway.json against the protocol catalogue.",
            src.ProtocolName, src.InstanceId);
        return null;
    }

    public SinkRegistration? BuildSink(...)
    {
        // Mirror for MQTT + OPC UA Server.
    }
}
```

---

## 7. Implementation order (with regression gates)

Same cadence as Phase 2.a. Single commit at the end.

| Step | Files touched | Why this order |
|---|---|---|
| 1 | `IRegistrationFactory.cs` (new). Contract only. |  |
| 2 | `ModbusTcpRegistrationExtensions.cs` — add `IsModbusProtocol`, three internal statics, rewrite `AddModbusTcpSourcesFromGatewayConfig`. Add `<InternalsVisibleTo>` to Host.csproj if missing. | Pilot extraction — most-exercised protocol; surfaces pattern issues early. |
| 3 | **Full test sweep — must still be 1681.** | First regression gate. |
| 4 | Remaining 5 extensions — `Focas2`, `MTConnect`, `S7` (sources), `Mqtt`, `OpcUaServer` (sinks). Same mechanical pattern. | Bulk work. |
| 5 | **Full test sweep — must still be 1681.** | Second regression gate. |
| 6 | `RegistrationFactory.cs` (new) — dispatcher with the helper-consulting switch. | All per-protocol methods exist. |
| 7 | `RegistrationFactoryTests.cs` — 12 tests. | Per §8. |
| 8 | **Final full sweep — expect 1693 (1681 + 12).** | Final gate. |
| 9 | Single commit. |  |

---

## 8. Test list (12 tests, named)

`RegistrationFactoryTests`:

| # | Test name | What it pins |
|---|---|---|
| 1 | `BuildSource_Modbus_ReturnsValidRegistration` | Happy path |
| 2 | `BuildSource_Focas2_ReturnsValidRegistration` | Happy path |
| 3 | `BuildSource_MTConnect_ReturnsValidRegistration` | Happy path |
| 4 | `BuildSource_S7_ReturnsValidRegistration` | Happy path |
| 5 | `BuildSource_UnrecognisedProtocol_ReturnsNull_AndLogsWarning` | Dispatcher fallthrough |
| 6 | `BuildSource_LicenseDisabledModule_ReturnsNull_AndNoFaultRegistered` | License-disabled = intent, not fault |
| 7 | `BuildSource_NoRouteForSource_RegistersFault_AndReturnsNull` | `CONFIG.SOURCE_WITHOUT_ROUTE` |
| 8 | `BuildSink_Mqtt_ReturnsValidRegistration` | Sink happy path |
| 9 | `BuildSink_OpcUaServer_ReturnsValidRegistration` | Sink happy path |
| 10 | `BuildSink_UnrecognisedProtocol_ReturnsNull_AndLogsWarning` | Sink fallthrough |
| 11 | `Build_NullArguments_Throw` | Argument-null guards on both methods |
| 12 | **`BuildSource_FilteredViaPreflight_DoesNotResolveAnyServices`** | **Sentinel `IServiceProvider` that throws on any `GetService` call. Proves the license/route skip paths never reach `Construct*` (which is the only layer that touches the SP). Locks the three-layer separation.** |

Test #12 uses a custom `IServiceProvider` that throws
`InvalidOperationException("eager DI use detected")` on `GetService`.
The factory is invoked with this sentinel and a license-disabled
source. The assertion is: `BuildSource` returns `null` AND no
exception was thrown — proving the resolve layer is fully lazy w.r.t.
DI.

---

## 9. Definition of done

1. `dotnet build ElpisEdgeConnect.sln --nologo` is 0 warnings, 0 errors.
2. `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky" --no-build --nologo` passes — total **1693/1693**.
3. The 12 named tests in §8 exist by exact name and pass.
4. The 6 protocol extensions each have:
   - A new internal `Is{Protocol}Protocol(string) → bool` (OrdinalIgnoreCase).
   - A new internal `Resolve{Source,Sink}RegistrationInputs(...)`.
   - A new internal `Construct{Source,Sink}Registration(...)`.
   - A new internal `Build{Source,Sink}(...)` composite.
   - The pre-existing DI extension method body now calls the resolve helper eagerly + the construct helper inside a deferred lambda. No throws inside the lambda.
5. `IRegistrationFactory` + `RegistrationFactory` exist; the dispatcher uses the `IsXxxProtocol` helpers.
6. **No file outside `src/ElpisEdgeConnect.Host/Adapters/` and `tests/ElpisEdgeConnect.Host.Tests/Adapters/` is modified** (except the Host.csproj's `<InternalsVisibleTo>` if needed).

---

## 10. Pause-point criteria before continuing to 2.c

Stop after the 2.b commit lands and report back if:

- Step 3 regression gate failed (Modbus extraction broke something) — the pattern needs rethinking before applying to the other 5.
- A protocol's adapter constructor takes dependencies not resolvable from `IServiceProvider` alone (would require contract extension).
- The license-check semantics diverge between extensions in ways the unified resolve helper can't express.
- Anything novel surfaces in the dispatcher tests (e.g., the lazy-resolution test reveals an accidental eager DI use).

Otherwise: continue straight to Phase 2.c on the same branch.

---

## 11. Future notes (out of scope; for 2.c plan)

- **`ConfigurationFault.ObservedAtUtc` ownership.** The factory sets the timestamp at fault-creation time. The coordinator (Phase 2.c) MUST NOT overwrite the timestamp on subsequent observations — the registry's existing `Register` semantics (REPLACE on same (Kind, InstanceId) key) handle this correctly. Pin this when writing the coordinator plan.

- **Full-eager construction.** If a future composition refactor moves `ILoggerFactory` / `IGatewayIdentity` to before `BuildServiceProvider()`, the DI extension's lambda can collapse into a direct `services.AddSingleton(constructed)` call — no behavior change, just simpler. Track as a follow-up if composition is touched for other reasons.

- **Richer return type (`RegistrationBuildResult`).** Today `null`
  conflates several outcomes (skipped vs invalid vs disabled vs
  unknown protocol). If the coordinator's diagnostics surface needs
  to distinguish them, introduce a discriminated `record` return
  type. Not needed for 2.c.

---

**End of Phase 2.b v2 plan. Locked. Implementation may proceed.**
