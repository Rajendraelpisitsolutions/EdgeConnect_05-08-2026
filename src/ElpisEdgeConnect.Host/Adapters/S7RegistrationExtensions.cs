// ============================================================================
// File: Adapters/S7RegistrationExtensions.cs
// Purpose: DI extension methods registering S7SourceAdapter instances
//          into the host. Mirrors ModbusTcpRegistrationExtensions
//          exactly, including the G.7 per-module license gate.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
//            docs/licensing/module-catalog.md (source-s7)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.S7;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods that register <see cref="S7SourceAdapter"/>
/// instances into the host. Each call appends one SourceRegistration;
/// <c>SourceSupervisor</c> consumes the cumulative list.
/// </summary>
public static class S7RegistrationExtensions
{
    /// <summary>Append one S7 source registration.</summary>
    public static IServiceCollection AddS7Source(
        this IServiceCollection services,
        S7SourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<S7SourceAdapter>();
            var identity = sp.GetService<IGatewayIdentity>();
            var adapter = new S7SourceAdapter(config.InstanceId, logger, identity);
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
    internal static bool IsS7Protocol(string protocolName)
        => string.Equals(protocolName, S7SourceConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (S7SourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsS7Protocol(src.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(S7SourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] S7 source '{src.InstanceId}' configured but " +
                $"license module '{S7SourceConfiguration.LicenseModuleKey}' " +
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
                Message = $"S7 source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = S7SourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SourceRegistration ConstructSourceRegistration(
        S7SourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<S7SourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new S7SourceAdapter(typedConfig.InstanceId, logger, identity);
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
    /// <c>protocolName = "s7"</c> and register each one. M.P2.2 phase
    /// 2.b: built on the resolve + construct helpers shared with the
    /// hot-reload factory path.
    /// </summary>
    public static IServiceCollection AddS7SourcesFromGatewayConfig(
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
            if (!IsS7Protocol(src.ProtocolName)) continue;

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
    /// Convenience overload that auto-resolves route ids from the config.
    /// </summary>
    public static IServiceCollection AddS7SourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddS7SourcesFromGatewayConfig(
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
                return null; // M.P2.1 fail-soft: main overload registers the fault.
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
