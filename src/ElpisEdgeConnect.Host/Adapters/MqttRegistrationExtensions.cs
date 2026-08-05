// ============================================================================
// File: Adapters/MqttRegistrationExtensions.cs
// Purpose: DI extensions that append an MqttSinkAdapter + its configuration
//          to the host's sink-registration list, so the SinkSupervisor will
//          initialize/start it once the locked startup sequence reaches the
//          StartSourceSupervisor phase (which also brings sinks up first).
//
//          Keeps all adapter construction in ONE place — the Host's
//          composition layer — honoring the "compile-time assemblies,
//          activated by license at DI registration time" rule from the
//          blueprint (Section 3, Locked Decision #4).
// Reference: ARCHITECTURE_BLUEPRINT.md §3, §4.3, §8; PHASE2_ENTRY.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.Mqtt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods for registering <see cref="MqttSinkAdapter"/>
/// instances into the host. Each call appends one registration; the
/// <see cref="SinkSupervisor"/> consumes the cumulative list.
/// </summary>
public static class MqttRegistrationExtensions
{
    /// <summary>
    /// Append an MQTT sink registration. The adapter is constructed at
    /// registration time so it participates in DI disposal; the route id
    /// correlates it with a <c>RouteDefinition</c> that must reference the
    /// same sink instance id.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="config">The MQTT broker, topic and publish-mode config.</param>
    /// <param name="routeId">The route id this sink belongs to.</param>
    public static IServiceCollection AddMqttSink(
        this IServiceCollection services,
        MqttSinkConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SinkRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<MqttSinkAdapter>();
            var adapter = new MqttSinkAdapter(config.InstanceId, logger);
            return new SinkRegistration
            {
                Adapter = adapter,
                Config = config,
                RouteId = routeId,
            };
        });

        ReplaceSinkRegistrationEnumerable(services);
        return services;
    }

    /// <summary>M.P2.2 phase 2.b: protocol-identity helper, OrdinalIgnoreCase.</summary>
    internal static bool IsMqttProtocol(string protocolName)
        => string.Equals(protocolName, MqttSinkConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (MqttSinkConfiguration TypedConfig, string RouteId)? ResolveSinkRegistrationInputs(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsMqttProtocol(sink.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(MqttSinkConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] MQTT sink '{sink.InstanceId}' configured but " +
                $"license module '{MqttSinkConfiguration.LicenseModuleKey}' " +
                "is not enabled. Skipping registration.");
            return null;
        }

        var routeId = routeIdSelector(sink.InstanceId);
        if (string.IsNullOrEmpty(routeId))
        {
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Sink,
                InstanceId = sink.InstanceId,
                ErrorCode = "CONFIG.SINK_WITHOUT_ROUTE",
                Message = $"MQTT destination '{sink.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the destination.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = MqttSinkConfiguration.FromSinkInstance(sink);
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SinkRegistration ConstructSinkRegistration(
        MqttSinkConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MqttSinkAdapter>();
        var adapter = new MqttSinkAdapter(typedConfig.InstanceId, logger);
        return new SinkRegistration { Adapter = adapter, Config = typedConfig, RouteId = routeId };
    }

    /// <summary>M.P2.2 phase 2.b Layer 3 — composite for the dispatcher.</summary>
    internal static SinkRegistration? BuildSink(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider sp)
    {
        var inputs = ResolveSinkRegistrationInputs(sink, gateway, routeIdSelector, license, faultRegistry);
        if (inputs is null) return null;
        return ConstructSinkRegistration(inputs.Value.TypedConfig, inputs.Value.RouteId, sp);
    }

    /// <summary>
    /// Scan <paramref name="gatewayConfig"/> for every enabled sink with
    /// <c>protocolName = "mqtt"</c> and append a sink registration for
    /// each. M.P2.2 phase 2.b: built on the resolve + construct helpers
    /// shared with the hot-reload factory path.
    /// </summary>
    public static IServiceCollection AddMqttSinksFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        ArgumentNullException.ThrowIfNull(routeIdSelector);

        foreach (var sink in gatewayConfig.Sinks)
        {
            if (!sink.Enabled) continue;
            if (!IsMqttProtocol(sink.ProtocolName)) continue;

            var inputs = ResolveSinkRegistrationInputs(
                sink, gatewayConfig.Gateway, routeIdSelector, license, faultRegistry);
            if (inputs is null) continue;

            var typedConfig = inputs.Value.TypedConfig;
            var routeId = inputs.Value.RouteId;
            services.AddSingleton<SinkRegistration>(sp =>
                ConstructSinkRegistration(typedConfig, routeId, sp));
            ReplaceSinkRegistrationEnumerable(services);
        }

        return services;
    }

    /// <summary>
    /// Convenience overload that resolves the route id by scanning the
    /// config's own <c>Routes</c> list for the first enabled route whose
    /// <c>SinkInstanceIds</c> contains the sink. Throws if an MQTT sink
    /// has no route — that would be a dead sink at runtime.
    /// </summary>
    public static IServiceCollection AddMqttSinksFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddMqttSinksFromGatewayConfig(
            gatewayConfig,
            sinkId =>
            {
                foreach (var route in gatewayConfig.Routes)
                {
                    if (route.Enabled && route.SinkInstanceIds.Contains(sinkId, StringComparer.Ordinal))
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
    /// Drop any pre-existing explicit <c>IEnumerable&lt;SinkRegistration&gt;</c>
    /// registration so DI auto-enumerates from the individual
    /// <c>SinkRegistration</c> singletons we just added.
    /// <para>
    /// Earlier revisions re-registered an explicit factory of the form
    /// <c>sp =&gt; sp.GetServices&lt;SinkRegistration&gt;().ToList()</c>,
    /// but that self-recurses: <c>GetServices&lt;T&gt;</c> is implemented as
    /// <c>GetRequiredService&lt;IEnumerable&lt;T&gt;&gt;</c>, which re-enters
    /// the very singleton factory currently being resolved and deadlocks on
    /// M.E.DI's singleton lock. Leaving the IEnumerable un-registered lets
    /// M.E.DI build it from the individual descriptors at resolve time.
    /// </para>
    /// </summary>
    private static void ReplaceSinkRegistrationEnumerable(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IEnumerable<SinkRegistration>))
            {
                services.RemoveAt(i);
            }
        }
    }
}
