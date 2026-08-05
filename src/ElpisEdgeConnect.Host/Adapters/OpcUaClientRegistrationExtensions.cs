// ============================================================================
// File: Adapters/OpcUaClientRegistrationExtensions.cs
// Purpose: DI extensions that append an OpcUaClientSourceAdapter + its
//          configuration to the host's source-registration list, so the
//          SourceSupervisor will Initialize / Start / SubscribeAsync it
//          once the locked startup sequence reaches StartSourceSupervisor.
//
//          Mirrors ModbusTcpRegistrationExtensions's three-layer pattern
//          (Resolve → Construct → composite BuildSource) so the
//          RegistrationFactory dispatcher integration is uniform across
//          protocols.
//
// LOCKED design rules (per M.P2.2 phase 2.b plan):
//   * Layer 1 (Resolve): no IServiceProvider; MAY register faults; returns
//     null on skip (license-disabled or no-route)
//   * Layer 2 (Construct): MUST NOT register faults; throws on failure
//   * Layer 3 (BuildSource): composite called by RegistrationFactory
//
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.2, §8
//            docs/sessions/2026-05-16-mp22-phase2b-plan.md
//            PR 7c-3 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.OpcUaClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="OpcUaClientSourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class OpcUaClientRegistrationExtensions
{
    /// <summary>
    /// Append a single OPC UA Client source registration with a typed config.
    /// </summary>
    public static IServiceCollection AddOpcUaClientSource(
        this IServiceCollection services,
        OpcUaClientSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<OpcUaClientSourceAdapter>();
            var adapter = new OpcUaClientSourceAdapter(config.InstanceId, logger);
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
    /// Protocol-identity helper. OrdinalIgnoreCase so future alias
    /// support lives in one place per protocol.
    /// </summary>
    internal static bool IsOpcUaClientProtocol(string protocolName)
        => string.Equals(
            protocolName,
            OpcUaClientSourceConfiguration.ProtocolNameConstant,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Layer 1: decision phase. Eager. No <c>IServiceProvider</c>
    /// needed. Validates license + route + produces the typed config.
    /// <strong>MAY register faults.</strong> Returns null on skip
    /// (license-disabled or no-route).
    /// </summary>
    internal static (OpcUaClientSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        // Defensive: protocol-name filter.
        if (!IsOpcUaClientProtocol(src.ProtocolName)) return null;

        // License-module gate. Enforced ONLY when a license is loaded
        // (`Current is not null`). Per blueprint Locked Decision #7
        // ("Never cut customer data to enforce licensing"), no license =
        // run every configured source. License-disabled is operator
        // intent — NEVER register a fault for it.
        if (license is { Current: not null }
            && !license.IsModuleEnabled(OpcUaClientSourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] OPC UA Client source '{src.InstanceId}' configured but "
                + $"license module '{OpcUaClientSourceConfiguration.LicenseModuleKey}' "
                + "is not enabled. Skipping registration.");
            return null;
        }

        var routeId = routeIdSelector(src.InstanceId);
        if (string.IsNullOrEmpty(routeId))
        {
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = src.InstanceId,
                ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
                Message = $"OPC UA Client source '{src.InstanceId}' is enabled in the config but no enabled "
                    + "route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        OpcUaClientSourceConfiguration typedConfig;
        try
        {
            typedConfig = OpcUaClientSourceConfiguration.FromSourceInstance(src) with
            {
                GatewayId = gateway?.GatewayId,
            };
        }
        catch (ArgumentException ex)
        {
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = src.InstanceId,
                ErrorCode = "OPCUA.CONFIG_INVALID",
                Message = ex.Message,
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }
        return (typedConfig, routeId);
    }

    /// <summary>
    /// Layer 2: construction phase. Needs <c>IServiceProvider</c> for
    /// the logger. <strong>MUST NEVER register faults.</strong>
    /// </summary>
    internal static SourceRegistration ConstructSourceRegistration(
        OpcUaClientSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<OpcUaClientSourceAdapter>();
        var adapter = new OpcUaClientSourceAdapter(typedConfig.InstanceId, logger);
        return new SourceRegistration
        {
            Adapter = adapter,
            Config = typedConfig,
            RouteId = routeId,
        };
    }

    /// <summary>
    /// Layer 3: composite called by the <see cref="IRegistrationFactory"/>
    /// dispatcher. Resolve + construct in one call. Returns null when
    /// the resolve layer decides to skip.
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
    /// Scan <paramref name="gatewayConfig"/> for every enabled OPC UA
    /// Client source, translate each into a typed
    /// <see cref="OpcUaClientSourceConfiguration"/>, and append a source
    /// registration for each one.
    /// </summary>
    public static IServiceCollection AddOpcUaClientSourcesFromGatewayConfig(
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
            if (!IsOpcUaClientProtocol(src.ProtocolName)) continue;

            var inputs = ResolveSourceRegistrationInputs(
                src, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry);
            if (inputs is null) continue;

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
    /// <c>SourceInstanceId</c> matches.
    /// </summary>
    public static IServiceCollection AddOpcUaClientSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddOpcUaClientSourcesFromGatewayConfig(
            gatewayConfig,
            sourceId =>
            {
                foreach (var route in gatewayConfig.Routes)
                {
                    if (route.Enabled
                        && string.Equals(route.SourceInstanceId, sourceId, StringComparison.Ordinal))
                    {
                        return route.RouteId;
                    }
                }
                return null;
            },
            license,
            faultRegistry);
    }

    /// <summary>
    /// See <see cref="ModbusTcpRegistrationExtensions"/> for the rationale
    /// — leaving the IEnumerable un-registered lets DI auto-enumerate
    /// individual descriptors and sidesteps the self-recursion deadlock.
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
