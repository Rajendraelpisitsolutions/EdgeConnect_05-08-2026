// ============================================================================
// File: Adapters/RegistrationFactory.cs
// Purpose: Default IRegistrationFactory implementation. Stateless
//          dispatcher that consults each protocol's IsXxxProtocol helper
//          and delegates to the protocol's BuildSource / BuildSink.
//
//          The hot-reload coordinator (M.P2.2 phase 2.c) calls this to
//          construct a SourceRegistration / SinkRegistration for ONE
//          instance at apply-time, without going through DI.
//
// LOCKED design rules (per docs/sessions/2026-05-16-mp22-phase2b-plan.md):
//   * Stateless. No mutable state. No service-locator behaviour.
//   * Consults per-protocol IsXxxProtocol helpers — does NOT compare
//     protocol-name strings directly. Future alias support (e.g.,
//     "mqtt" vs "mqtt-publisher") lives in the helper, not here.
//   * Caller MUST have filtered disabled instances. The factory must
//     never observe Enabled == false. Future contributors must not add
//     such a guard here — disabled is operator intent (not a fault),
//     and gating it in the dispatcher silently masks coordinator bugs.
//   * License-disabled returns null with NO fault (license-disable is
//     intent). The per-protocol helper handles this.
//   * No-route returns null WITH a fault. The per-protocol helper
//     handles this.
//   * Unrecognised protocol → logs Warning + returns null. Defensive;
//     gateway.json schema validation catches this earlier in practice.
// Reference: docs/sessions/2026-05-16-mp22-phase2b-plan.md
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// Milestone: M.P2.2 phase 2.b
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Default <see cref="IRegistrationFactory"/> implementation. Routes
/// per-instance build requests to the protocol-specific helpers on
/// each <c>*RegistrationExtensions</c> class.
/// </summary>
public sealed class RegistrationFactory : IRegistrationFactory
{
    private readonly ILogger<RegistrationFactory> _logger;

    /// <summary>Construct the dispatcher.</summary>
    public RegistrationFactory(ILogger<RegistrationFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public SourceRegistration? BuildSource(
        SourceInstanceConfig src,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(routeIdSelector);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // Per-protocol IsXxxProtocol helpers own protocol-name comparison
        // (OrdinalIgnoreCase). Future alias support lands in the helper.
        if (ModbusTcpRegistrationExtensions.IsModbusProtocol(src.ProtocolName))
        {
            return ModbusTcpRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (Focas2RegistrationExtensions.IsFocas2Protocol(src.ProtocolName))
        {
            return Focas2RegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (MTConnectRegistrationExtensions.IsMTConnectProtocol(src.ProtocolName))
        {
            return MTConnectRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (S7RegistrationExtensions.IsS7Protocol(src.ProtocolName))
        {
            return S7RegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (BrotherHttpRegistrationExtensions.IsBrotherHttpProtocol(src.ProtocolName))
        {
            return BrotherHttpRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (OpcUaClientRegistrationExtensions.IsOpcUaClientProtocol(src.ProtocolName))
        {
            return OpcUaClientRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (EthernetIpRegistrationExtensions.IsEthernetIpProtocol(src.ProtocolName))
        {
            return EthernetIpRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (MelsecRegistrationExtensions.IsMelsecProtocol(src.ProtocolName))
        {
            return MelsecRegistrationExtensions.BuildSource(
                src, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }

        _logger.LogWarning(
            "RegistrationFactory: unrecognised source protocol '{Protocol}' for instance '{Id}'. " +
            "Validate gateway.json against the protocol catalogue.",
            src.ProtocolName, src.InstanceId);
        return null;
    }

    /// <inheritdoc/>
    public SinkRegistration? BuildSink(
        SinkInstanceConfig sink,
        GatewaySettings gateway,
        Func<string, string?> routeIdSelector,
        ILicenseManager? license,
        IConfigurationFaultRegistry? faultRegistry,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(routeIdSelector);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (MqttRegistrationExtensions.IsMqttProtocol(sink.ProtocolName))
        {
            return MqttRegistrationExtensions.BuildSink(
                sink, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }
        if (OpcUaServerRegistrationExtensions.IsOpcUaServerProtocol(sink.ProtocolName))
        {
            return OpcUaServerRegistrationExtensions.BuildSink(
                sink, gateway, routeIdSelector, license, faultRegistry, serviceProvider);
        }

        _logger.LogWarning(
            "RegistrationFactory: unrecognised sink protocol '{Protocol}' for instance '{Id}'. " +
            "Validate gateway.json against the protocol catalogue.",
            sink.ProtocolName, sink.InstanceId);
        return null;
    }
}
