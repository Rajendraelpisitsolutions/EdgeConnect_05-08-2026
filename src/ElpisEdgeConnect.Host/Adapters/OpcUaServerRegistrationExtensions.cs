// ============================================================================
// File: Adapters/OpcUaServerRegistrationExtensions.cs
// Purpose: DI extensions that append an OpcUaServerSinkAdapter + its
//          configuration to the host's sink-registration list. Mirrors
//          the MQTT registration pattern (G.7) including license-gated
//          enforcement and the IEnumerable<SinkRegistration> deadlock fix.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.1
//            docs/licensing/module-catalog.md (sink-opc-ua-server)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// DI extension methods that register
/// <see cref="OpcUaServerSinkAdapter"/> instances into the host. Each
/// call appends one registration; <c>SinkSupervisor</c> consumes the
/// cumulative list.
/// </summary>
public static class OpcUaServerRegistrationExtensions
{
    /// <summary>
    /// Append an OPC UA Server sink registration.
    /// </summary>
    public static IServiceCollection AddOpcUaServerSink(
        this IServiceCollection services,
        OpcUaServerConfiguration config,
        string routeId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        services.AddSingleton<SinkRegistration>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger<OpcUaServerSinkAdapter>();
            var adapter = new OpcUaServerSinkAdapter(config.InstanceId, logger);
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
    internal static bool IsOpcUaServerProtocol(string protocolName)
        => string.Equals(protocolName, OpcUaServerConfiguration.ProtocolNameConstant, StringComparison.OrdinalIgnoreCase);

    /// <summary>M.P2.2 phase 2.b Layer 1 — decision phase. MAY register faults.</summary>
    internal static (OpcUaServerConfiguration TypedConfig, string RouteId)? ResolveSinkRegistrationInputs(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry)
    {
        if (!IsOpcUaServerProtocol(sink.ProtocolName)) return null;

        if (license is { Current: not null }
            && !license.IsModuleEnabled(OpcUaServerConfiguration.LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] OPC UA Server sink '{sink.InstanceId}' configured but " +
                $"license module '{OpcUaServerConfiguration.LicenseModuleKey}' " +
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
                Message = $"OPC UA Server destination '{sink.InstanceId}' is enabled in the config but no enabled route references it. Either add a route or disable the destination.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var typedConfig = OpcUaServerConfiguration.FromSinkInstance(sink);
        return (typedConfig, routeId);
    }

    /// <summary>M.P2.2 phase 2.b Layer 2 — construction phase. MUST NOT register faults.</summary>
    internal static SinkRegistration ConstructSinkRegistration(
        OpcUaServerConfiguration typedConfig,
        string routeId,
        IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<OpcUaServerSinkAdapter>();
        var adapter = new OpcUaServerSinkAdapter(typedConfig.InstanceId, logger);
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
    /// <c>protocolName = "opcua-server"</c> and append a registration.
    /// M.P2.2 phase 2.b: built on the resolve + construct helpers shared
    /// with the hot-reload factory path.
    /// </summary>
    public static IServiceCollection AddOpcUaServerSinksFromGatewayConfig(
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
            if (!IsOpcUaServerProtocol(sink.ProtocolName)) continue;

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
    /// config's <c>Routes</c> list.
    /// </summary>
    public static IServiceCollection AddOpcUaServerSinksFromGatewayConfig(
        this IServiceCollection services,
        GatewayConfiguration gatewayConfig,
        ILicenseManager? license = null,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayConfig);
        return services.AddOpcUaServerSinksFromGatewayConfig(
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
    /// Drop any pre-existing explicit
    /// <c>IEnumerable&lt;SinkRegistration&gt;</c> registration so DI
    /// auto-enumerates from the individual <c>SinkRegistration</c>
    /// singletons. See <c>MqttRegistrationExtensions</c> for the
    /// detailed rationale (singleton-resolver deadlock fix).
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
