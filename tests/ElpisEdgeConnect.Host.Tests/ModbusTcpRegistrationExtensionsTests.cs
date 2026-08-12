// ============================================================================
// File: ModbusTcpRegistrationExtensionsTests.cs
// Purpose: Confirm the Modbus TCP adapter is wired into host DI the same way
//          FOCAS2 and MTConnect are, so Program.cs actually brings up a
//          Modbus source instance declared in gateway.json.
// Reference: PHASE3_EXECUTION_PLAN.md F1-F5 exit
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class ModbusTcpRegistrationExtensionsTests
{
    private static SourceInstanceConfig ModbusSource(
        string instanceId = "plc-test",
        bool enabled = true,
        string host = "127.0.0.1")
    {
        var conn = JsonSerializer.Deserialize<JsonElement>(
            $$"""{ "host": "{{host}}", "port": 502 }""");
        return new SourceInstanceConfig
        {
            InstanceId = instanceId,
            ProtocolName = ModbusTcpSourceConfiguration.ProtocolNameConstant,
            DeviceId = "dev-test",
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
                    RouteId = "route-plc-test",
                    Name = "route-plc-test",
                    SourceInstanceId = sources[0].InstanceId,
                    SinkInstanceIds = new[] { "mqtt-sink" },
                    Enabled = true,
                    Buffer = new BufferPolicyConfig { Mode = BufferMode.InMemory, MaxDepth = 100 },
                    Delivery = new DeliveryPolicyConfig { Mode = DeliveryMode.AtLeastOnce },
                },
            },
        };

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_AppendsModbusSourceAsRegistration()
    {
        // DI-time assertion only — don't build the service provider, which
        // would instantiate a real FluentModbusClient. We just verify the
        // extension enqueued a SourceRegistration descriptor for our
        // modbus-protocolled source.
        var cfg = WithSources(ModbusSource("plc-1"));

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg);

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().ContainSingle(
            "one enabled modbus source should register one SourceRegistration descriptor");
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_SkipsDisabledSources()
    {
        // Enabled=false should never make it into the DI surface at all.
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { ModbusSource("plc-off", enabled: false) },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg);

        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_SkipsNonModbusSources()
    {
        // A FOCAS2 source in the config must not be pulled into the Modbus
        // registration path.
        var focasSource = new SourceInstanceConfig
        {
            InstanceId = "cnc-1",
            ProtocolName = "focas2",
            DeviceId = "cnc-dev",
            Enabled = true,
        };
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { focasSource },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg);

        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_MissingRoute_RegistersFault_DoesNotThrow()
    {
        // M.P2.1 fail-soft: enabled source with no route is a cross-record
        // validation failure. Pre-M.P2.1 this threw and crashed the gateway;
        // now it registers a ConfigurationFault and continues so the gateway
        // boots and the operator can fix the config via Studio.
        var modbus = ModbusSource("plc-orphan");
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { modbus },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        var faultRegistry = new ElpisEdgeConnect.Core.Diagnostics.ConfigurationFaultRegistry();

        var act = () => services.AddModbusTcpSourcesFromGatewayConfig(cfg, license: null, faultRegistry: faultRegistry);

        act.Should().NotThrow(
            "M.P2.1 fail-soft: cross-record validation failures must NOT crash the gateway");

        // No source should be registered; one fault should be in the registry.
        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
        var faults = faultRegistry.GetFaults();
        faults.Should().ContainSingle(f =>
            f.InstanceId == "plc-orphan"
            && f.Kind == ElpisEdgeConnect.Core.Diagnostics.ConfigurationFaultKind.Source
            && f.ErrorCode == "CONFIG.SOURCE_WITHOUT_ROUTE");
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_InvalidTagAddress_RegistersFault_DoesNotThrow()
    {
        // Fail-soft (ADR-0003 / Locked Decision #10): a tag address that is
        // invalid for its declared address base — here address 0 under
        // One-based, whose zero-based wire address would be -1 — must NOT crash
        // the runtime on boot. Before this fix FromSourceInstance threw and the
        // whole gateway failed to start; now the bad source is skipped, a fault
        // is registered, and every other source/route boots normally.
        var conn = JsonSerializer.Deserialize<JsonElement>(
            """
            {
              "host": "127.0.0.1",
              "port": 502,
              "addressBase": "OneBased",
              "tagDefinitions": [
                { "name": "tag20", "registerClass": "HoldingRegister", "address": 0, "scanRateMs": 1000, "datatype": "uint16" }
              ]
            }
            """);
        var badSource = new SourceInstanceConfig
        {
            InstanceId = "plc-badaddr",
            ProtocolName = ModbusTcpSourceConfiguration.ProtocolNameConstant,
            DeviceId = "dev-test",
            Enabled = true,
            Connection = conn,
        };
        // WithSources gives this source a matching enabled route, so the only
        // reason to skip it is the translation failure we're pinning.
        var cfg = WithSources(badSource);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        var faultRegistry = new ElpisEdgeConnect.Core.Diagnostics.ConfigurationFaultRegistry();

        var act = () => services.AddModbusTcpSourcesFromGatewayConfig(cfg, license: null, faultRegistry: faultRegistry);

        act.Should().NotThrow(
            "fail-soft: a malformed source config must not crash the gateway on boot");

        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
        var faults = faultRegistry.GetFaults();
        faults.Should().ContainSingle(f =>
            f.InstanceId == "plc-badaddr"
            && f.Kind == ElpisEdgeConnect.Core.Diagnostics.ConfigurationFaultKind.Source
            && f.ErrorCode == "CONFIG.SOURCE_INVALID");
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_MissingRoute_NoRegistry_StillDoesNotThrow()
    {
        // When called without a faultRegistry (e.g., from tests or callers
        // that don't wire it up), the fail-soft path must still apply —
        // skip silently rather than throw. The Phase-2 invariant is "never
        // crash on cross-record validation," regardless of registry presence.
        var modbus = ModbusSource("plc-orphan");
        var cfg = new GatewayConfiguration
        {
            Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Host Test Gateway" },
            Sources = new[] { modbus },
            Routes = System.Array.Empty<RouteConfig>(),
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        var act = () => services.AddModbusTcpSourcesFromGatewayConfig(cfg);

        act.Should().NotThrow();
        services.Where(d => d.ServiceType == typeof(SourceRegistration)).Should().BeEmpty();
    }

    [Fact]
    public void AddModbusTcpSource_AppendsDescriptor_WithoutResolving()
    {
        // Multiple AddModbusTcpSource calls accumulate — same pattern FOCAS2
        // uses. Verified at descriptor level to avoid instantiating a real
        // FluentModbusClient during the test.
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));

        services.AddModbusTcpSource(
            new ModbusTcpSourceConfiguration
            {
                InstanceId = "plc-a", ProtocolName = "modbustcp",
                DeviceId = "dev-a", Host = "1.2.3.4",
            },
            routeId: "route-a");
        services.AddModbusTcpSource(
            new ModbusTcpSourceConfiguration
            {
                InstanceId = "plc-b", ProtocolName = "modbustcp",
                DeviceId = "dev-b", Host = "1.2.3.5",
            },
            routeId: "route-b");

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().HaveCount(2);
    }

    // ========================================================================
    // G.7 — License-module enforcement at adapter registration time.
    //
    // The extension method accepts an optional ILicenseManager. When supplied
    // AND the license is loaded (Current is not null), it consults
    // IsModuleEnabled(LicenseModuleKey). Disabled modules are skipped with a
    // stderr warning; other sources continue to register (per-adapter
    // isolation, Locked Decision #10). When no license is loaded — null
    // manager or null Current — enforcement is bypassed (dev / sim runs).
    // ========================================================================

    /// <summary>
    /// Build a minimally-valid LicenseInfo for tests that need
    /// <c>license.Current is not null</c> to be true. The Modules dict is
    /// empty — module enablement is driven by the NSubstitute call set up
    /// for <c>IsModuleEnabled</c> in each test.
    /// </summary>
    private static LicenseInfo MakeLoadedLicense() => new()
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
    };

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_LicenseModuleDisabled_SkipsRegistration()
    {
        var cfg = WithSources(ModbusSource("plc-licensed-out"));

        var license = Substitute.For<ILicenseManager>();
        // Loaded license, module disabled.
        license.Current.Returns(MakeLoadedLicense());
        license.IsModuleEnabled(ModbusTcpSourceConfiguration.LicenseModuleKey).Returns(false);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg, license);

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().BeEmpty(
            "module 'source-modbus-tcp' disabled in the loaded license must skip every Modbus source registration");
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_NoLicenseLoaded_RegistersAllSources()
    {
        // Per Locked Decision #7 — never cut customer data to enforce
        // licensing. When no license is loaded (dev / sim / soak runs
        // without a license file), every configured source registers.
        var cfg = WithSources(ModbusSource("plc-1"));

        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns((LicenseInfo?)null); // no license loaded
        license.IsModuleEnabled(Arg.Any<string>()).Returns(false); // intentionally hostile

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg, license);

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().ContainSingle(
            "with no license loaded, the enforcement layer must be permissive (dev / soak path)");
    }

    [Fact]
    public void AddModbusTcpSourcesFromGatewayConfig_LicenseModuleEnabled_RegistersSource()
    {
        var cfg = WithSources(ModbusSource("plc-licensed-in"));

        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(MakeLoadedLicense());
        license.IsModuleEnabled(ModbusTcpSourceConfiguration.LicenseModuleKey).Returns(true);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddModbusTcpSourcesFromGatewayConfig(cfg, license);

        var descriptors = services.Where(d => d.ServiceType == typeof(SourceRegistration)).ToList();
        descriptors.Should().ContainSingle(
            "module 'source-modbus-tcp' enabled in the loaded license must register the source normally");
    }
}
