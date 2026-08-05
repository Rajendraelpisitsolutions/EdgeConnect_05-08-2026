// ============================================================================
// File: Adapters/ModbusTcpRegistrationExtensions.cs
// Purpose: DI extensions that append a ModbusTcpSourceAdapter + its
//          configuration to the host's source-registration list, so the
//          SourceSupervisor will initialize/start/poll it once the locked
//          startup sequence reaches StartSourceSupervisor.
//
//          Mirrors Focas2RegistrationExtensions — compile-time assemblies
//          activated at DI registration time per blueprint Locked Decision #4.
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.2, §8; PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.ModbusTcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="ModbusTcpSourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class ModbusTcpRegistrationExtensions
{
    /// <summary>
    /// Append a single Modbus TCP source registration with a typed config.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="config">The Modbus connection + tag-definition config.</param>
    /// <param name="routeId">The route id whose intake receives this source's points.</param>
    public static IServiceCollection AddModbusTcpSource(
        this IServiceCollection services,
        ModbusTcpSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<ModbusTcpSourceAdapter>();
            var identity = sp.GetService<IGatewayIdentity>();
            var adapter = new ModbusTcpSourceAdapter(config.InstanceId, logger, identity);
            return new SourceRegistration
            {
                Adapter = adapter,
                Config = config,
                RouteId = routeId,
            };
        });

        ReplaceSourceRegistrationEnumerable(services);
        return services;
    }

    /// <summary>
    /// M.P2.2 phase 2.b: protocol-identity helper. Matches BOTH Modbus protocol
    /// ids — <c>modbustcp</c> and <c>modbusrtu</c> (ADR-0033) — which share this
    /// adapter; the per-protocol encapsulation rule is enforced by the adapter's
    /// ValidateConfigAsync. OrdinalIgnoreCase so aliasing lives in one place.
    /// </summary>
    internal static bool IsModbusProtocol(string protocolName)
        => ModbusTcpSourceConfiguration.IsModbusProtocolName(protocolName);

    /// <summary>
    /// M.P2.2 phase 2.b — Layer 1: decision phase. Eager. No
    /// <c>IServiceProvider</c> needed. Validates license + route +
    /// produces the typed config. <strong>MAY register faults.</strong>
    /// Returns null on skip (license-disabled or no-route); the caller
    /// continues with the next instance.
    /// </summary>
    internal static (ModbusTcpSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        // Defensive: protocol-name filter. The dispatcher / DI extension
        // should have filtered already, but resolve is internal-public
        // surface and must guard.
        if (!IsModbusProtocol(src.ProtocolName)) return null;

        // G.7: license-module gate. Enforced ONLY when a license is loaded
        // (`Current is not null`). Per blueprint Locked Decision #7
        // ("Never cut customer data to enforce licensing"), no license =
        // run every configured source. License-disabled is operator
        // intent — NEVER register a fault for it.
        if (license is { Current: not null }
            && !license.IsModuleEnabled(ModbusTcpSourceConfiguration.LicenseModuleKey))
        {
            // Per-adapter isolation (Locked Decision #10): one disabled
            // module never stops other sources. Log to stderr — at this
            // point the host's ILoggerFactory may not be available.
            Console.Error.WriteLine(
                $"[license] Modbus TCP source '{src.InstanceId}' configured but " +
                $"license module '{ModbusTcpSourceConfiguration.LicenseModuleKey}' " +
                "is not enabled. Skipping registration.");
            return null;
        }

        var routeId = routeIdSelector(src.InstanceId);
        if (string.IsNullOrEmpty(routeId))
        {
            // M.P2.1 fail-soft: cross-record validation failure. Register
            // the fault, skip this source, continue with the others.
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = src.InstanceId,
                ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
                Message = $"Modbus source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        // Carry the human-readable gateway display name from the
        // top-level gateway block onto every source config we hand to
        // the adapter — that becomes CanonicalDataPoint.GatewayId and
        // the {gatewayId} segment in MQTT topics.
        //
        // Fail-soft (ADR-0003 / Locked Decision #10): translation can throw
        // on a malformed source — e.g. a tag address that is invalid for its
        // declared address base. That must NEVER crash the whole runtime on
        // boot. Register the fault, skip this one source, and let every other
        // source and route start normally. The operator sees the bad source
        // in the config-fault surface instead of a dead gateway.
        ModbusTcpSourceConfiguration typedConfig;
        try
        {
            typedConfig = ModbusTcpSourceConfiguration.FromSourceInstance(src) with
            {
                GatewayId = gateway?.GatewayId,
            };
        }
        catch (Exception ex)
        {
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = src.InstanceId,
                ErrorCode = "CONFIG.SOURCE_INVALID",
                Message = $"Modbus source '{src.InstanceId}' could not be loaded and was skipped: {ex.Message}",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }
        return (typedConfig, routeId);
    }

    /// <summary>
    /// M.P2.2 phase 2.b — Layer 2: construction phase. Needs
    /// <c>IServiceProvider</c> for the logger + identity. <strong>MUST
    /// NEVER register faults.</strong> Either succeeds or throws — the
    /// caller handles construction failures (DI lambda surfaces them at
    /// resolve time; the coordinator's TryWithFaultAsync catches them).
    /// </summary>
    internal static SourceRegistration ConstructSourceRegistration(
        ModbusTcpSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<ModbusTcpSourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new ModbusTcpSourceAdapter(typedConfig.InstanceId, logger, identity);
        return new SourceRegistration
        {
            Adapter = adapter,
            Config = typedConfig,
            RouteId = routeId,
        };
    }

    /// <summary>
    /// M.P2.2 phase 2.b — Layer 3: composite. Used by the
    /// <c>IRegistrationFactory</c> dispatcher. Resolve + construct in
    /// one call. Returns null when the resolve layer decides to skip.
    /// </summary>
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

    /// <summary>
    /// Scan <paramref name="gatewayConfig"/> for every enabled source with
    /// <c>protocolName = "modbustcp"</c>, translate each into a typed
    /// <see cref="ModbusTcpSourceConfiguration"/>, and append a source
    /// registration for each one. The <paramref name="routeIdSelector"/>
    /// maps a source instance id to the route id whose intake it feeds.
    /// </summary>
    public static IServiceCollection AddModbusTcpSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        ArgumentNullException.ThrowIfNull(routeIdSelector);

        foreach (var src in gatewayConfig.Sources)
        {
            if (!src.Enabled) continue;
            if (!IsModbusProtocol(src.ProtocolName)) continue;

            // Eager preflight: license check, route resolution, typed
            // config translation. Returns null on skip (license-disabled
            // logs to stderr; no-route registers a fault). Either way,
            // the boot path continues to the next source — no throws.
            var inputs = ResolveSourceRegistrationInputs(
                src, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry);
            if (inputs is null) continue;

            // Adapter construction is deferred via the DI lambda because
            // ILoggerFactory + IGatewayIdentity aren't built yet at this
            // point in composition. Capture the typed config + route id
            // for the closure.
            var typedConfig = inputs.Value.TypedConfig;
            var routeId = inputs.Value.RouteId;
            services.AddSingleton<SourceRegistration>(sp =>
                ConstructSourceRegistration(typedConfig, routeId, sp));
            ReplaceSourceRegistrationEnumerable(services);
        }

        return services;
    }

    /// <summary>
    /// Convenience overload that resolves the route id by scanning the
    /// config's own <c>Routes</c> list for the first enabled route whose
    /// <c>SourceInstanceId</c> matches. Throws if a Modbus source has no
    /// route — that would be a dead source at runtime.
    /// </summary>
    public static IServiceCollection AddModbusTcpSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddModbusTcpSourcesFromGatewayConfig(
            gatewayConfig,
            sourceId =>
            {
                foreach (var route in gatewayConfig.Routes)
                {
                    if (route.Enabled &&
                        string.Equals(route.SourceInstanceId, sourceId, StringComparison.Ordinal))
                    {
                        return route.RouteId;
                    }
                }
                // M.P2.1 fail-soft: return null to signal "no route." The
                // main overload registers the fault via faultRegistry and
                // continues; the gateway no longer crashes on this config.
                return null;
            },
            license,
            faultRegistry);
    }

    /// <summary>
    /// Drop any pre-existing explicit <c>IEnumerable&lt;SourceRegistration&gt;</c>
    /// registration so DI auto-enumerates from the individual
    /// <c>SourceRegistration</c> singletons we just added.
    /// <para>
    /// Earlier revisions re-registered an explicit factory of the form
    /// <c>sp =&gt; sp.GetServices&lt;SourceRegistration&gt;().ToList()</c>,
    /// but that self-recurses: <c>GetServices&lt;T&gt;</c> is implemented as
    /// <c>GetRequiredService&lt;IEnumerable&lt;T&gt;&gt;</c>, which re-enters
    /// the very singleton factory currently being resolved and deadlocks on
    /// M.E.DI's singleton lock. Leaving the IEnumerable un-registered lets
    /// M.E.DI build it from the individual descriptors at resolve time,
    /// which is exactly what we want and sidesteps the recursion entirely.
    /// </para>
    /// </summary>
    private static void ReplaceSourceRegistrationEnumerable(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IEnumerable<SourceRegistration>))
            {
                services.RemoveAt(i);
            }
        }
    }
}
