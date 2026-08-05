// ============================================================================
// File: Adapters/MTConnectRegistrationExtensions.cs
// Purpose: DI extensions that append an MTConnectSourceAdapter + its typed
//          configuration to the host's source-registration list. Mirrors
//          Focas2RegistrationExtensions — any new protocol module follows
//          the same pattern.
//
//          Per locked decision #4 (compile-time assemblies, activated by
//          license at DI registration time). The license gate itself is
//          phase-3 work; these extensions just register the adapter.
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.2, §8; PHASE2_ENTRY.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.MTConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="MTConnectSourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class MTConnectRegistrationExtensions
{
    /// <summary>
    /// Append one MTConnect source registration. The adapter is constructed
    /// at registration time so it participates in DI disposal.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="config">Typed MTConnect connection + polling config.</param>
    /// <param name="routeId">The route id whose intake receives this source's points.</param>
    public static IServiceCollection AddMTConnectSource(
        this IServiceCollection services,
        MTConnectSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<MTConnectSourceAdapter>();
            var identity = sp.GetService<IGatewayIdentity>();
            var adapter = new MTConnectSourceAdapter(config.InstanceId, logger, identity);
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

    /// <summary>M.P2.2 phase 2.b: protocol-identity helper, OrdinalIgnoreCase.</summary>
    internal static bool IsMTConnectProtocol(string protocolName)
        => string.Equals(protocolName, MTConnectSourceConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (MTConnectSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsMTConnectProtocol(src.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(MTConnectSourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] MTConnect source '{src.InstanceId}' configured but " +
                $"license module '{MTConnectSourceConfiguration.LicenseModuleKey}' " +
                "is not enabled. Skipping registration.");
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
                Message = $"MTConnect source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = MTConnectSourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SourceRegistration ConstructSourceRegistration(
        MTConnectSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MTConnectSourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new MTConnectSourceAdapter(typedConfig.InstanceId, logger, identity);
        return new SourceRegistration { Adapter = adapter, Config = typedConfig, RouteId = routeId };
    }

    /// <summary>M.P2.2 phase 2.b Layer 3 — composite for the dispatcher.</summary>
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
    /// <c>protocolName = "mtconnect"</c> and append a source registration
    /// for each. M.P2.2 phase 2.b: built on the resolve + construct
    /// helpers shared with the hot-reload factory path.
    /// </summary>
    public static IServiceCollection AddMTConnectSourcesFromGatewayConfig(
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
            if (!IsMTConnectProtocol(src.ProtocolName)) continue;

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
    /// Convenience overload: resolves route ids by scanning the config's own
    /// <c>Routes</c> list for the first enabled route whose
    /// <c>SourceInstanceId</c> matches.
    /// </summary>
    public static IServiceCollection AddMTConnectSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddMTConnectSourcesFromGatewayConfig(
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
                return null; // M.P2.1 fail-soft: main overload registers the fault.
            },
            license,
            faultRegistry);
    }

    // Drop any pre-existing explicit IEnumerable<SourceRegistration>
    // registration so DI auto-enumerates from the individual
    // SourceRegistration singletons. See the equivalent comment in
    // Focas2RegistrationExtensions — registering our own factory of the
    // form sp => sp.GetServices<T>().ToList() self-recurses and deadlocks
    // on M.E.DI's singleton lock.
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
