// ============================================================================
// File: Adapters/BrotherHttpRegistrationExtensions.cs
// Purpose: DI extensions that append a BrotherHttpSourceAdapter + its
//          configuration to the host's source-registration list, so the
//          SourceSupervisor will initialize/start/poll it once the locked
//          startup sequence reaches the StartSourceSupervisor phase.
//          Mirrors Focas2RegistrationExtensions's three-layer pattern
//          (M.P2.2 phase 2.b) so hot-reload + boot share the same helpers.
//
//          BrotherHttp is the FIRST source adapter to use IHttpClientFactory
//          (v3 §4 Pattern C). This extension registers the factory once
//          via services.AddHttpClient() if not already present.
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.2, §8;
//            docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §3, §4
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net.Http;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.BrotherHttp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="BrotherHttpSourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class BrotherHttpRegistrationExtensions
{
    /// <summary>
    /// Append a Brother HTTP source registration. Adapter is constructed at
    /// registration time so it participates in DI disposal.
    /// </summary>
    public static IServiceCollection AddBrotherHttpSource(
        this IServiceCollection services,
        BrotherHttpSourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        // Brother HTTP is the first source adapter to need IHttpClientFactory
        // (v3 §4 Pattern C). AddHttpClient is idempotent — safe to call
        // multiple times if multiple Brother sources are registered.
        services.AddHttpClient();

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<BrotherHttpSourceAdapter>();
            var identity = sp.GetService<IGatewayIdentity>();
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var adapter = new BrotherHttpSourceAdapter(config.InstanceId, factory, logger, identity);
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
    internal static bool IsBrotherHttpProtocol(string protocolName)
        => string.Equals(protocolName, BrotherHttpSourceConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (BrotherHttpSourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsBrotherHttpProtocol(src.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(BrotherHttpSourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] Brother HTTP source '{src.InstanceId}' configured but " +
                $"license module '{BrotherHttpSourceConfiguration.LicenseModuleKey}' " +
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
                Message = $"Brother HTTP source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = BrotherHttpSourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SourceRegistration ConstructSourceRegistration(
        BrotherHttpSourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<BrotherHttpSourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var adapter = new BrotherHttpSourceAdapter(typedConfig.InstanceId, factory, logger, identity);
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
    /// <c>protocolName = "brother-http"</c> and append a source registration
    /// for each. Idempotent registration of <c>IHttpClientFactory</c> via
    /// <c>services.AddHttpClient()</c>.
    /// </summary>
    public static IServiceCollection AddBrotherHttpSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        ArgumentNullException.ThrowIfNull(routeIdSelector);

        // Always register IHttpClientFactory (idempotent), even if no Brother
        // sources are in this config — keeps future hot-reload additions safe.
        services.AddHttpClient();

        foreach (var src in gatewayConfig.Sources)
        {
            if (!src.Enabled) continue;
            if (!IsBrotherHttpProtocol(src.ProtocolName)) continue;

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
    public static IServiceCollection AddBrotherHttpSourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddBrotherHttpSourcesFromGatewayConfig(
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

    /// <summary>
    /// Drop any pre-existing explicit <c>IEnumerable&lt;SourceRegistration&gt;</c>
    /// registration so DI auto-enumerates from the individual
    /// <c>SourceRegistration</c> singletons we just added.
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
