// ============================================================================
// File: OpcUaClientRegistrationExtensionsTests.cs
// Purpose: Confirm the OPC UA Client adapter is wired into host DI the
//          same way Modbus / FOCAS2 / MTConnect / S7 / Brother HTTP are,
//          so Program.cs actually brings up an opcua-client source
//          instance declared in gateway.json.
//
// Reference: PR 7c-3 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class OpcUaClientRegistrationExtensionsTests
{
    private const string TestEndpoint = "opc.tcp://factorytalk.local:4840";

    // ─── Add-from-gateway-config ─────────────────────────────────────

    [Fact]
    public void AddOpcUaClientSourcesFromGatewayConfig_AppendsOpcUaClientSourceAsRegistration()
    {
        var cfg = WithSources(OpcUaClientSource("opcua-1"));

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddOpcUaClientSourcesFromGatewayConfig(cfg);

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().ContainSingle(
            "one enabled opcua-client source should register one SourceRegistration descriptor");
    }

    [Fact]
    public void AddOpcUaClientSourcesFromGatewayConfig_SkipsDisabledSources()
    {
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { OpcUaClientSource("opcua-off", enabled: false) },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddOpcUaClientSourcesFromGatewayConfig(cfg);

        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
    }

    [Fact]
    public void AddOpcUaClientSourcesFromGatewayConfig_SkipsNonOpcUaClientSources()
    {
        var modbusSource = new SourceInstanceConfig
        {
            InstanceId = "modbus-1",
            ProtocolName = "modbustcp",
            DeviceId = "plc-1",
            Enabled = true,
        };
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { modbusSource },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddOpcUaClientSourcesFromGatewayConfig(cfg);

        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
    }

    // ─── Layer 1 — ResolveSourceRegistrationInputs ────────────────────

    [Fact]
    public void ResolveInputs_LicenseDisabled_ReturnsNull_AndRegistersNoFault()
    {
        // Per blueprint Locked Decision #7 — license-disable is operator
        // intent, NOT a fault. Resolve must skip silently when the
        // license module is disabled.
        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(new LicenseInfo
        {
            LicenseId = "TEST-LICENSE",
            Customer = "Test Customer",
            GatewayId = "gw-test",
            Edition = LicenseEdition.Professional,
            IssuedAt = System.DateTime.UtcNow.AddDays(-1),
            ExpiresAt = System.DateTime.UtcNow.AddDays(30),
            Limits = new LicenseLimits
            {
                MaxSourceInstances = 100,
                MaxSinkInstances = 100,
                MaxRoutes = 100,
            },
            Modules = System.Collections.Frozen.FrozenDictionary<string, LicenseModule>.Empty,
        });
        license.IsModuleEnabled(OpcUaClientSourceConfiguration.LicenseModuleKey).Returns(false);

        var src = OpcUaClientSource("opcua-1");
        var faults = new ConfigurationFaultRegistry();

        var result = OpcUaClientRegistrationExtensions.ResolveSourceRegistrationInputs(
            src,
            new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            _ => "route-opcua",
            license,
            faults);

        result.Should().BeNull();
        faults.GetFaults().Should().BeEmpty(
            "license-disabled is operator intent, not a fault — blueprint Locked Decision #7");
    }

    [Fact]
    public void ResolveInputs_NoRouteForSource_RegistersFault_AndReturnsNull()
    {
        var src = OpcUaClientSource("opcua-orphan");
        var faults = new ConfigurationFaultRegistry();

        var result = OpcUaClientRegistrationExtensions.ResolveSourceRegistrationInputs(
            src,
            new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            _ => null,    // route resolver returns "no route"
            license: null,
            faults);

        result.Should().BeNull();
        var registered = faults.GetFaults();
        registered.Should().ContainSingle();
        registered[0].ErrorCode.Should().Be("CONFIG.SOURCE_WITHOUT_ROUTE");
        registered[0].InstanceId.Should().Be("opcua-orphan");
    }

    [Fact]
    public void ResolveInputs_MalformedConnection_RegistersFault_AndReturnsNull()
    {
        // PR 7c-3 — FromSourceInstance throws ArgumentException when the
        // Connection object is missing endpointUrl. The resolve layer
        // must convert that into a fault rather than letting it bubble
        // up and crash the registration enumeration.
        var src = new SourceInstanceConfig
        {
            InstanceId = "opcua-malformed",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "dev",
            Enabled = true,
            Connection = JsonSerializer.Deserialize<JsonElement>("""{ "applicationUri": "urn:test" }"""),
        };
        var faults = new ConfigurationFaultRegistry();

        var result = OpcUaClientRegistrationExtensions.ResolveSourceRegistrationInputs(
            src,
            new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            _ => "route-opcua",
            license: null,
            faults);

        result.Should().BeNull();
        var registered = faults.GetFaults();
        registered.Should().ContainSingle();
        registered[0].ErrorCode.Should().Be("OPCUA.CONFIG_INVALID");
    }

    // ─── Protocol-identity helper ─────────────────────────────────────

    [Theory]
    [InlineData("opcua-client", true)]
    [InlineData("OPCUA-CLIENT", true)]   // OrdinalIgnoreCase
    [InlineData("modbustcp", false)]
    [InlineData("focas2", false)]
    public void IsOpcUaClientProtocol_OrdinalIgnoreCaseMatchesProtocolName(string protocolName, bool expected)
    {
        OpcUaClientRegistrationExtensions.IsOpcUaClientProtocol(protocolName).Should().Be(expected);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static SourceInstanceConfig OpcUaClientSource(
        string instanceId = "opcua-test",
        bool enabled = true,
        string endpoint = TestEndpoint)
    {
        var conn = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "endpointUrl": "{{endpoint}}" }""");
        return new SourceInstanceConfig
        {
            InstanceId = instanceId,
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "scada-test",
            Enabled = enabled,
            Connection = conn,
        };
    }

    private static GatewayConfiguration WithSources(params SourceInstanceConfig[] sources)
        => new()
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = sources,
            Routes = new[]
            {
                new RouteConfig
                {
                    RouteId = "route-opcua-test",
                    Name = "route-opcua-test",
                    SourceInstanceId = sources[0].InstanceId,
                    SinkInstanceIds = new[] { "mqtt-sink" },
                    Enabled = true,
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.InMemory, MaxDepth = 100 },
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtLeastOnce },
                },
            },
        };
}
