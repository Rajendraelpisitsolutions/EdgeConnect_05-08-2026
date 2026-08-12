// ============================================================================
// File: Adapters/MelsecRegistrationExtensions.cs
// Purpose: DI extension methods registering MelsecSourceAdapter instances into
//          the host. Mirrors S7RegistrationExtensions, including the G.7
//          per-module license gate. The one MELSEC difference: the adapter takes
//          an IMelsecClient, so construction news up a real SlmpClient from the
//          typed config (route header, monitoring timer, timeouts).
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
//            docs/licensing/module-catalog.md (source-melsec)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.Melsec;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods that register <see cref="MelsecSourceAdapter"/> instances
/// into the host. Each call appends one SourceRegistration; the SourceSupervisor
/// consumes the cumulative list.
/// </summary>
public static class MelsecRegistrationExtensions
{
    /// <summary>Append one MELSEC source registration.</summary>
    public static IServiceCollection AddMelsecSource(
        this IServiceCollection services,
        MelsecSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp => ConstructSourceRegistration(config, routeId, sp));
        ReplaceSourceRegistrationEnumerable(services);
        return services;
    }

    /// <summary>Protocol-identity helper (OrdinalIgnoreCase).</summary>
    internal static bool IsMelsecProtocol(string protocolName)
        => string.Equals(protocolName, MelsecSourceConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>Decision phase — MAY register faults.</summary>
    internal static (MelsecSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsMelsecProtocol(src.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(MelsecSourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] MELSEC source '{src.InstanceId}' configured but " +
                $"license module '{MelsecSourceConfiguration.LicenseModuleKey}' " +
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
                Message = $"MELSEC source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = MelsecSourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>Construction phase — MUST NOT register faults. News up the real SlmpClient transport.</summary>
    internal static SourceRegistration ConstructSourceRegistration(
        MelsecSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MelsecSourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new MelsecSourceAdapter(typedConfig.InstanceId, new SlmpClient(typedConfig), logger, identity);
        return new SourceRegistration { Adapter = adapter, Config = typedConfig, RouteId = routeId };
    }

    /// <summary>Composite for the dispatcher.</summary>
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

    /// <summary>Scan the gateway config for enabled <c>protocolName = "melsec"</c> sources and register each.</summary>
    public static IServiceCollection AddMelsecSourcesFromGatewayConfig(
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
            if (!IsMelsecProtocol(src.ProtocolName)) continue;

            var inputs = ResolveSourceRegistrationInputs(
                src, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry);
            if (inputs is null) continue;

            var typedConfig = inputs.Value.TypedConfig;
            var routeId = inputs.Value.RouteId;
            services.AddSingleton<SourceRegistration>(sp => ConstructSourceRegistration(typedConfig, routeId, sp));
            ReplaceSourceRegistrationEnumerable(services);
        }

        return services;
    }

    /// <summary>Convenience overload that auto-resolves route ids from the config.</summary>
    public static IServiceCollection AddMelsecSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddMelsecSourcesFromGatewayConfig(
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
                return null; // fail-soft: main overload registers the fault.
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
