// ============================================================================
// File: Adapters/Focas2RegistrationExtensions.cs
// Purpose: DI extensions that append a Focas2SourceAdapter + its configuration
//          to the host's source-registration list, so the SourceSupervisor
//          will initialize/start/poll it once the locked startup sequence
//          reaches the StartSourceSupervisor phase.
//
//          Keeps all adapter construction in ONE place — the Host's
//          composition layer — honoring the "compile-time assemblies,
//          activated by license at DI registration time" rule from the
//          blueprint (Section 3, Locked Decision #4).
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
using ElpisEdgeConnect.Sources.Focas2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="Focas2SourceAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SourceSupervisor"/> consumes the cumulative list.
/// </summary>
public static class Focas2RegistrationExtensions
{
    /// <summary>
    /// Append a FOCAS2 source registration. The adapter is constructed at
    /// registration time so it participates in DI disposal; the route id
    /// correlates it with a <c>RouteDefinition</c> that must reference the
    /// same source instance id.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="config">The FOCAS2 connection and data-selection config.</param>
    /// <param name="routeId">The route id whose intake receives this source's points.</param>
    public static IServiceCollection AddFocas2Source(
        this IServiceCollection services,
        Focas2SourceConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SourceRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<Focas2SourceAdapter>();
            // Optional — a narrow harness may skip AddElpisEdgeConnectHost
            // and therefore not register an IGatewayIdentity; fall back to
            // the adapter's "gateway" sentinel in that case.
            var identity = sp.GetService<IGatewayIdentity>();
            var adapter = new Focas2SourceAdapter(config.InstanceId, logger, identity);
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
    internal static bool IsFocas2Protocol(string protocolName)
        => string.Equals(protocolName, Focas2SourceConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>A CNC endpoint targeted by two or more enabled FOCAS2 sources.</summary>
    public sealed record SharedCncEndpoint(string Endpoint, IReadOnlyList<string> SourceIds);

    /// <summary>
    /// Find CNC endpoints (<c>ip:port</c>) that more than one enabled FOCAS2
    /// source targets. FANUC controllers permit only a few simultaneous FOCAS
    /// clients, so co-located sources can exhaust that limit and cause
    /// <c>SOCKET_ERROR</c> drops. This is advisory only — the sources still come
    /// up — so the host logs a warning rather than registering a fault. Sources
    /// whose connection block cannot be parsed are skipped (any real problem
    /// there surfaces as its own configuration fault during registration).
    /// </summary>
    public static IReadOnlyList<SharedCncEndpoint> FindSharedCncEndpoints(GatewayConfiguration gatewayConfig)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);

        var byEndpoint = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in gatewayConfig.Sources)
        {
            if (!src.Enabled) continue;
            if (!IsFocas2Protocol(src.ProtocolName)) continue;

            Focas2SourceConfiguration typed;
            try
            {
                typed = Focas2SourceConfiguration.FromSourceInstance(src);
            }
            catch
            {
                continue; // malformed/missing IP — surfaced elsewhere as a fault
            }

            var endpoint = $"{typed.IpAddress}:{typed.Port}";
            if (!byEndpoint.TryGetValue(endpoint, out var list))
            {
                list = [];
                byEndpoint[endpoint] = list;
            }
            list.Add(src.InstanceId);
        }

        return byEndpoint
            .Where(kvp => kvp.Value.Count >= 2)
            .Select(kvp => new SharedCncEndpoint(kvp.Key, kvp.Value))
            .ToList();
    }

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (Focas2SourceConfiguration TypedConfig, string RouteId)? ResolveSourceRegistrationInputs(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsFocas2Protocol(src.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(Focas2SourceConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] FOCAS2 source '{src.InstanceId}' configured but " +
                $"license module '{Focas2SourceConfiguration.LicenseModuleKey}' " +
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
                Message = $"FOCAS2 source '{src.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the source.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = Focas2SourceConfiguration.FromSourceInstance(src) with
        {
            GatewayId = gateway?.GatewayId,
        };
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SourceRegistration ConstructSourceRegistration(
        Focas2SourceConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<Focas2SourceAdapter>();
        var identity = sp.GetService<IGatewayIdentity>();
        var adapter = new Focas2SourceAdapter(typedConfig.InstanceId, logger, identity);
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
    /// <c>protocolName = "focas2"</c> and append a source registration for
    /// each. M.P2.2 phase 2.b: now built on top of
    /// <c>ResolveSourceRegistrationInputs</c> (eager preflight) and
    /// <c>ConstructSourceRegistration</c> (deferred adapter construction)
    /// so the boot path and the hot-reload factory path share the same
    /// decision + construction helpers.
    /// </summary>
    public static IServiceCollection AddFocas2SourcesFromGatewayConfig(
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
            if (!IsFocas2Protocol(src.ProtocolName)) continue;

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
    /// <c>SourceInstanceId</c> matches. Throws if a FOCAS2 source has no
    /// route — that would be a dead source at runtime.
    /// </summary>
    public static IServiceCollection AddFocas2SourcesFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddFocas2SourcesFromGatewayConfig(
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
    /// M.E.DI build it from the individual descriptors at resolve time.
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
