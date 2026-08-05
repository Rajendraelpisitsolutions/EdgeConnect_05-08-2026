// ============================================================================
// File: Adapters/EthernetIpRegistrationExtensions.cs
// Purpose: DI extensions that append an EthernetIpSourceAdapter + its
//          configuration to the host's source-registration list, so the
//          SourceSupervisor initializes / starts / polls it once the locked
//          startup sequence reaches StartSourceSupervisor.
//
//          Mirrors ModbusTcpRegistrationExtensions — compile-time assemblies
//          activated at DI registration time per blueprint Locked Decision #4.
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.2, §8;
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §5.2
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.EthernetIp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="EthernetIpSourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class EthernetIpRegistrationExtensions
{
    /// <summary>Append a single EtherNet/IP source registration with a typed config.</summary>
    public static IServiceCollection AddEthernetIpSource(
        this IServiceCollection services,
        EthernetIpSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<EthernetIpSourceAdapter>();
            var identity = sp.GetService<IGatewayIdentity>();
            var adapter = new EthernetIpSourceAdapter(config.InstanceId, logger, identity);
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

    /// <summary>Protocol-identity helper. OrdinalIgnoreCase so future aliases live in one place.</summary>
    internal static bool IsEthernetIpProtocol(string protocolName)
        => string.Equals(
            protocolName,
            EthernetIpSourceConfiguration.ProtocolNameConstant,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Layer 1 — decision phase. Eager, no <c>IServiceProvider</c>. Validates
    /// license + route + produces the typed config. MAY register faults.
    /// Returns null on skip (license-disabled or no-route).
    /// </summary>
    internal static (EthernetIpSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsEthernetIpProtocol(src.ProtocolName)) return null;

        // G.7: license-module gate. Enforced ONLY when a license is loaded.
        // No license = run every configured source (Locked Decision #7).
        if (license is { Current: not null }
            && !license.IsModuleEnabled(EthernetIpSourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] EtherNet/IP source '{src.InstanceId}' configured but " +
                $"license module '{EthernetIpSourceConfiguration.LicenseModuleKey}' " +
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
                Message = $"EtherNet/IP source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = EthernetIpSourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>
    /// Layer 2 — construction phase. Needs <c>IServiceProvider</c> for the
    /// logger + identity. MUST NEVER register faults.
    /// </summary>
    internal static SourceRegistration ConstructSourceRegistration(
        EthernetIpSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger<EthernetIpSourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new EthernetIpSourceAdapter(typedConfig.InstanceId, logger, identity);
        return new SourceRegistration
        {
            Adapter = adapter,
            Config = typedConfig,
            RouteId = routeId,
        };
    }

    /// <summary>
    /// Layer 3 — composite. Used by the <c>IRegistrationFactory</c> dispatcher.
    /// Resolve + construct in one call. Returns null when resolve decides to skip.
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
    /// <c>protocolName = "ethernetip"</c>, translate each into a typed
    /// <see cref="EthernetIpSourceConfiguration"/>, and append a registration.
    /// </summary>
    public static IServiceCollection AddEthernetIpSourcesFromGatewayConfig(
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
            if (!IsEthernetIpProtocol(src.ProtocolName)) continue;

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
    /// Convenience overload that resolves the route id by scanning the config's
    /// own <c>Routes</c> list for the first enabled route whose
    /// <c>SourceInstanceId</c> matches.
    /// </summary>
    public static IServiceCollection AddEthernetIpSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddEthernetIpSourcesFromGatewayConfig(
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
                return null;
            },
            license,
            faultRegistry);
    }

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
