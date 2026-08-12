// ============================================================================
// File: Adapters/RegistrationFactoryTests.cs
// Purpose: Pin the M.P2.2 phase 2.b IRegistrationFactory dispatcher
//          contract. Tests cover the full §6.2 matrix:
//             * 4 protocol happy paths for BuildSource (Modbus, Focas2,
//               MTConnect, S7)
//             * 2 protocol happy paths for BuildSink (MQTT, OPC UA Server)
//             * unknown protocol returns null + logs warning
//             * license-disabled returns null with NO fault registered
//             * no-route returns null WITH fault registered
//             * argument-null guards
//             * sentinel IServiceProvider that throws on any GetService —
//               proves the resolve layer is fully lazy w.r.t. DI
//               (license/route skip paths never reach the SP)
// Reference: docs/sessions/2026-05-16-mp22-phase2b-plan.md §8
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using System.Collections.Frozen;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using ElpisEdgeConnect.Sources.Focas2;
using ElpisEdgeConnect.Sources.MTConnect;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Adapters;

public sealed class RegistrationFactoryTests
{
    // ── Test fixtures ──────────────────────────────────────────────

    private static RegistrationFactory MakeFactory()
        => new(NullLogger<RegistrationFactory>.Instance);

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }

    private static GatewaySettings Gateway() => new()
    {
        GatewayId = "gw-test",
        GatewayName = "Test Gateway",
    };

    private static SourceInstanceConfig SourceInstance(
        string instanceId,
        string protocolName,
        string connectionJson,
        bool enabled = true)
        => new()
        {
            InstanceId = instanceId,
            ProtocolName = protocolName,
            DeviceId = "dev-" + instanceId,
            Enabled = enabled,
            Connection = JsonDocument.Parse(connectionJson).RootElement,
        };

    private static SinkInstanceConfig SinkInstance(
        string instanceId,
        string protocolName,
        string connectionJson,
        bool enabled = true)
        => new()
        {
            InstanceId = instanceId,
            ProtocolName = protocolName,
            Enabled = enabled,
            Connection = JsonDocument.Parse(connectionJson).RootElement,
        };

    /// <summary>Always returns the given route id.</summary>
    private static Func<string, string?> ConstantRouteId(string routeId)
        => _ => routeId;

    /// <summary>Always returns null — simulates the no-route case.</summary>
    private static Func<string, string?> NoRoute()
        => _ => null;

    /// <summary>
    /// Sentinel <see cref="IServiceProvider"/> that throws on ANY
    /// <c>GetService</c> call. Proves the resolve / dispatcher layers
    /// never touch DI on skip paths.
    /// </summary>
    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => throw new InvalidOperationException(
                $"Sentinel: eager DI use detected for {serviceType.Name}. " +
                "The resolve layer must NEVER touch the IServiceProvider on a skip path.");
    }

    /// <summary>Minimal LicenseInfo for "license is loaded" scenarios.</summary>
    private static LicenseInfo MakeLoadedLicense() => new()
    {
        LicenseId = "TEST-LICENSE",
        Customer = "Test Customer",
        GatewayId = "gw-test",
        Edition = LicenseEdition.Professional,
        IssuedAt = DateTime.UtcNow.AddDays(-1),
        ExpiresAt = DateTime.UtcNow.AddDays(30),
        Limits = new LicenseLimits
        {
            MaxSourceInstances = 100,
            MaxSinkInstances = 100,
            MaxRoutes = 100,
        },
        Modules = FrozenDictionary<string, LicenseModule>.Empty,
    };

    // ── Source happy paths ─────────────────────────────────────────

    [Fact]
    public void BuildSource_Modbus_ReturnsValidRegistration()
    {
        var src = SourceInstance("plc-modbus",
            ModbusTcpSourceConfiguration.ProtocolNameConstant,
            """{ "host": "127.0.0.1", "port": 502 }""");
        var faults = new ConfigurationFaultRegistry();

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-modbus"),
            license: null, faults, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.InstanceId.Should().Be("plc-modbus");
        reg.Adapter.ProtocolName.Should().Be(ModbusTcpSourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<ModbusTcpSourceConfiguration>();
        reg.RouteId.Should().Be("route-modbus");
        faults.GetFaults().Should().BeEmpty();
    }

    [Fact]
    public void BuildSource_Focas2_ReturnsValidRegistration()
    {
        var src = SourceInstance("focas-1",
            Focas2SourceConfiguration.ProtocolNameConstant,
            """{ "ipAddress": "192.168.1.101" }""");

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-focas"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(Focas2SourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<Focas2SourceConfiguration>();
        reg.RouteId.Should().Be("route-focas");
    }

    [Fact]
    public void BuildSource_MTConnect_ReturnsValidRegistration()
    {
        var src = SourceInstance("mtc-1",
            MTConnectSourceConfiguration.ProtocolNameConstant,
            """{ "agentBaseUrl": "http://192.168.1.50:5000/" }""");

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-mtc"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(MTConnectSourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<MTConnectSourceConfiguration>();
    }

    [Fact]
    public void BuildSource_S7_ReturnsValidRegistration()
    {
        var src = SourceInstance("s7-1",
            S7SourceConfiguration.ProtocolNameConstant,
            """{ "host": "192.168.1.40" }""");

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-s7"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(S7SourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<S7SourceConfiguration>();
    }

    [Fact]
    public void BuildSource_OpcUaClient_ReturnsValidRegistration()
    {
        // PR 7c-3 — RegistrationFactory must dispatch to
        // OpcUaClientRegistrationExtensions for protocolName="opcua-client".
        var src = SourceInstance("opcua-1",
            ElpisEdgeConnect.Sources.OpcUaClient.OpcUaClientSourceConfiguration.ProtocolNameConstant,
            """{ "endpointUrl": "opc.tcp://192.168.1.30:4840" }""");

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-opcua"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(
            ElpisEdgeConnect.Sources.OpcUaClient.OpcUaClientSourceConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<ElpisEdgeConnect.Sources.OpcUaClient.OpcUaClientSourceConfiguration>();
        reg.RouteId.Should().Be("route-opcua");
    }

    // ── Sink happy paths ───────────────────────────────────────────

    [Fact]
    public void BuildSink_Mqtt_ReturnsValidRegistration()
    {
        var sink = SinkInstance("mqtt-1",
            MqttSinkConfiguration.ProtocolNameConstant,
            """{ "brokerHost": "broker.example.com", "brokerPort": 1883 }""");

        var reg = MakeFactory().BuildSink(
            sink, Gateway(), ConstantRouteId("route-mqtt"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(MqttSinkConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<MqttSinkConfiguration>();
        reg.RouteId.Should().Be("route-mqtt");
    }

    [Fact]
    public void BuildSink_OpcUaServer_ReturnsValidRegistration()
    {
        var sink = SinkInstance("opcua-1",
            OpcUaServerConfiguration.ProtocolNameConstant,
            """{ "endpointUrl": "opc.tcp://localhost:4840/test" }""");

        var reg = MakeFactory().BuildSink(
            sink, Gateway(), ConstantRouteId("route-opcua"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().NotBeNull();
        reg!.Adapter.ProtocolName.Should().Be(OpcUaServerConfiguration.ProtocolNameConstant);
        reg.Config.Should().BeOfType<OpcUaServerConfiguration>();
    }

    // ── Unknown protocol ───────────────────────────────────────────

    [Fact]
    public void BuildSource_UnrecognisedProtocol_ReturnsNull_AndLogsWarning()
    {
        // Use a captured logger to verify the Warning is emitted.
        var logged = new List<string>();
        var capturingLogger = Substitute.For<ILogger<RegistrationFactory>>();
        capturingLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        capturingLogger
            .When(l => l.Log(
                Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()))
            .Do(c =>
            {
                var level = (LogLevel)c[0];
                var state = c[2];
                if (level == LogLevel.Warning)
                {
                    logged.Add(state?.ToString() ?? string.Empty);
                }
            });
        var factory = new RegistrationFactory(capturingLogger);

        var src = SourceInstance("ghost-1", "ghost-protocol", "{}");

        var reg = factory.BuildSource(
            src, Gateway(), ConstantRouteId("route-x"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().BeNull("no Is{Protocol}Protocol helper matched 'ghost-protocol'");
        logged.Should().NotBeEmpty("the dispatcher must log a warning on unrecognised protocols");
        logged[0].Should().Contain("ghost-protocol").And.Contain("ghost-1");
    }

    [Fact]
    public void BuildSink_UnrecognisedProtocol_ReturnsNull_AndLogsWarning()
    {
        var logged = new List<string>();
        var capturingLogger = Substitute.For<ILogger<RegistrationFactory>>();
        capturingLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        capturingLogger
            .When(l => l.Log(
                Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()))
            .Do(c =>
            {
                var level = (LogLevel)c[0];
                var state = c[2];
                if (level == LogLevel.Warning)
                {
                    logged.Add(state?.ToString() ?? string.Empty);
                }
            });
        var factory = new RegistrationFactory(capturingLogger);

        var sink = SinkInstance("ghost-sink", "ghost-protocol", "{}");

        var reg = factory.BuildSink(
            sink, Gateway(), ConstantRouteId("route-x"),
            license: null, faultRegistry: null, BuildServiceProvider());

        reg.Should().BeNull();
        logged.Should().NotBeEmpty();
        logged[0].Should().Contain("ghost-protocol").And.Contain("ghost-sink");
    }

    // ── License-disabled ───────────────────────────────────────────

    [Fact]
    public void BuildSource_LicenseDisabledModule_ReturnsNull_AndNoFaultRegistered()
    {
        // License is loaded and reports the Modbus module disabled.
        // License-disable is operator INTENT, not a fault — null return
        // with NO fault registered.
        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(MakeLoadedLicense());
        license.IsModuleEnabled(ModbusTcpSourceConfiguration.LicenseModuleKey).Returns(false);

        var src = SourceInstance("plc-licensed-off",
            ModbusTcpSourceConfiguration.ProtocolNameConstant,
            """{ "host": "127.0.0.1", "port": 502 }""");
        var faults = new ConfigurationFaultRegistry();

        var reg = MakeFactory().BuildSource(
            src, Gateway(), ConstantRouteId("route-x"),
            license, faults, BuildServiceProvider());

        reg.Should().BeNull("license-disabled module produces null");
        faults.GetFaults().Should().BeEmpty(
            "license-disable is operator intent — NEVER register a fault for it");
    }

    // ── No-route fault path ────────────────────────────────────────

    [Fact]
    public void BuildSource_NoRouteForSource_RegistersFault_AndReturnsNull()
    {
        var src = SourceInstance("plc-orphan",
            ModbusTcpSourceConfiguration.ProtocolNameConstant,
            """{ "host": "127.0.0.1", "port": 502 }""");
        var faults = new ConfigurationFaultRegistry();

        var reg = MakeFactory().BuildSource(
            src, Gateway(), NoRoute(),
            license: null, faults, BuildServiceProvider());

        reg.Should().BeNull("no enabled route references this source");
        var registered = faults.GetFaults();
        registered.Should().ContainSingle();
        registered[0].Kind.Should().Be(ConfigurationFaultKind.Source);
        registered[0].InstanceId.Should().Be("plc-orphan");
        registered[0].ErrorCode.Should().Be("CONFIG.SOURCE_WITHOUT_ROUTE");
    }

    // ── Argument-null guards ───────────────────────────────────────

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var factory = MakeFactory();
        var src = SourceInstance("any", ModbusTcpSourceConfiguration.ProtocolNameConstant,
            """{ "host": "127.0.0.1", "port": 502 }""");
        var sink = SinkInstance("any-sink", MqttSinkConfiguration.ProtocolNameConstant,
            """{ "brokerHost": "x" }""");
        var sp = BuildServiceProvider();

        ((Action)(() => factory.BuildSource(null!, Gateway(), ConstantRouteId("r"), null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSource(src, null!, ConstantRouteId("r"), null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSource(src, Gateway(), null!, null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSource(src, Gateway(), ConstantRouteId("r"), null, null, null!)))
            .Should().Throw<ArgumentNullException>();

        ((Action)(() => factory.BuildSink(null!, Gateway(), ConstantRouteId("r"), null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSink(sink, null!, ConstantRouteId("r"), null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSink(sink, Gateway(), null!, null, null, sp)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => factory.BuildSink(sink, Gateway(), ConstantRouteId("r"), null, null, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Sentinel IServiceProvider lazy-resolution proof ────────────

    [Fact]
    public void BuildSource_FilteredViaPreflight_DoesNotResolveAnyServices()
    {
        // The strong lazy-resolution proof: a sentinel IServiceProvider
        // that THROWS on any GetService call. The factory invokes
        // BuildSource on:
        //   * an unrecognised protocol (dispatcher fallthrough, no SP used)
        //   * a license-disabled source (resolve-layer skip, no SP used)
        //   * a no-route source (resolve-layer skip, no SP used)
        // None of these should reach the construction layer — proving the
        // resolve/decision layer is fully lazy w.r.t. DI.
        var sentinel = new ThrowingServiceProvider();
        var factory = MakeFactory();

        // (a) Unknown protocol — dispatcher rejects before any helper runs.
        var ghost = SourceInstance("ghost", "no-such-protocol", "{}");
        ((Action)(() => factory.BuildSource(
                ghost, Gateway(), ConstantRouteId("r"),
                license: null, faultRegistry: null, sentinel)))
            .Should().NotThrow(
                "dispatcher must reject unrecognised protocols WITHOUT touching the SP");

        // (b) License-disabled — resolve-layer returns null before SP use.
        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(MakeLoadedLicense());
        license.IsModuleEnabled(Arg.Any<string>()).Returns(false);

        var modbusSrc = SourceInstance("plc-l",
            ModbusTcpSourceConfiguration.ProtocolNameConstant,
            """{ "host": "127.0.0.1", "port": 502 }""");
        ((Action)(() => factory.BuildSource(
                modbusSrc, Gateway(), ConstantRouteId("r"),
                license, faultRegistry: null, sentinel)))
            .Should().NotThrow(
                "license-disable path must NOT reach the construction layer / SP");

        // (c) No-route — resolve-layer registers fault and returns null
        // before SP use.
        var faults = new ConfigurationFaultRegistry();
        ((Action)(() => factory.BuildSource(
                modbusSrc, Gateway(), NoRoute(),
                license: null, faults, sentinel)))
            .Should().NotThrow(
                "no-route path must NOT reach the construction layer / SP");
        faults.GetFaults().Should().ContainSingle();
    }
}
