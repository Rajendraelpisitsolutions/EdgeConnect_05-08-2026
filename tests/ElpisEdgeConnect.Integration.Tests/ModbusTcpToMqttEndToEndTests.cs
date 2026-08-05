// ============================================================================
// File: ModbusTcpToMqttEndToEndTests.cs
// Purpose: F3 exit gate — Modbus source (pymodbus sim via Docker) →
//          canonical pipeline → MQTT sink (real Mosquitto) → EREMOS V2
//          PerTag topic shape. Verifies that typed values decoded by the
//          F3 decoder reach the broker under their expected tag names.
//
// Prerequisites (both independently skip-gated):
//   * RequiresDocker:      docker CLI + daemon reachable for the pymodbus sim.
//   * RequiresMqttBroker:  Mosquitto on 127.0.0.1:1883 anonymous.
//
// Reference: PHASE3_EXECUTION_PLAN.md §11 Definition of Done
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;
using Xunit.Abstractions;
using static ElpisEdgeConnect.Integration.Tests.IntegrationTestData;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// End-to-end Modbus → pipeline → MQTT scenario. Exercises every datatype
/// and byte-order the F3 decoder supports against the pymodbus simulator.
/// </summary>
[Trait("Category", "RequiresDocker")]
[Trait("Category", "RequiresMqttBroker")]
public sealed class ModbusTcpToMqttEndToEndTests : IClassFixture<ModbusTcpSimulatorFixture>
{
    private const string BrokerHost = "127.0.0.1";
    private const int BrokerPort = 1883;

    private const string TestGatewayId = "gw-modbus-e2e";
    private const string SourceInstanceId = "modbus-e2e";
    private const string SinkInstanceId = "mqtt-e2e-modbus";
    private const string RouteId = "route-modbus-mqtt";

    private readonly ModbusTcpSimulatorFixture _sim;
    private readonly ITestOutputHelper _output;

    public ModbusTcpToMqttEndToEndTests(ModbusTcpSimulatorFixture sim, ITestOutputHelper output)
    {
        _sim = sim;
        _output = output;
    }

    [Fact]
    public async Task ModbusSource_PublishesAllTagDatatypesToMqtt()
    {
        if (!_sim.IsAvailable)
        {
            _output.WriteLine($"[SKIPPED] Modbus simulator not available: {_sim.UnavailableReason}");
            return;
        }
        await EnsureBrokerReachableAsync();

        // --- Modbus side -------------------------------------------------
        var modbusConfig = new ModbusTcpSourceConfiguration
        {
            InstanceId = SourceInstanceId,
            ProtocolName = "modbustcp",
            DeviceId = "plc-e2e",
            DeviceName = "E2E Demo PLC",
            Host = _sim.Host,
            Port = (ushort)_sim.Port,
            DefaultUnitId = 1,
            PollIntervalMs = 100,
            ConnectTimeoutMs = 3000,
            RequestTimeoutMs = 2000,
            KeepAlive = true,
            MaxTransactionRetries = 1,
            InitialBackoffMs = 50,
            MaxBackoffMs = 500,
            CircuitBreakerThreshold = 100,
            CircuitBreakerResetMs = 1000,
            MaxGapRegisters = 8,
            TagDefinitions = BuildDemoTagSet(),
        };
        var modbusAdapter = new ModbusTcpSourceAdapter(
            SourceInstanceId,
            new FluentModbusClient(),
            NullLogger<ModbusTcpSourceAdapter>.Instance,
            new StubGatewayIdentity(TestGatewayId));
        var sourceReg = new SourceRegistration
        {
            Adapter = modbusAdapter,
            Config = modbusConfig,
            RouteId = RouteId,
        };

        // --- MQTT side ---------------------------------------------------
        var mqttConfig = new MqttSinkConfiguration
        {
            InstanceId = SinkInstanceId,
            ProtocolName = "mqtt",
            BrokerHost = BrokerHost,
            BrokerPort = BrokerPort,
            ClientId = $"edgeconnect-modbus-e2e-{Guid.NewGuid():N}",
            PublishMode = MqttPublishMode.PerTag,
            PerTagTopicTemplate = "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
            QosLevel = 0,
            ReconnectDelayMs = 200,
            MaxReconnectDelayMs = 1000,
        };
        var mqttAdapter = new MqttSinkAdapter(
            SinkInstanceId,
            NullLogger<MqttSinkAdapter>.Instance);
        var sinkReg = new SinkRegistration
        {
            Adapter = mqttAdapter,
            Config = mqttConfig,
            RouteId = RouteId,
        };

        // --- Subscribe BEFORE starting the host -------------------------
        var topicFilter = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/+";
        var received = new ConcurrentQueue<(string Topic, string Payload)>();
        using var subscriber = new MqttFactory().CreateMqttClient();
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithClientId($"e2e-modbus-sub-{Guid.NewGuid():N}")
            .Build(), CancellationToken.None);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            received.Enqueue((
                args.ApplicationMessage.Topic,
                Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment)));
            return Task.CompletedTask;
        };
        await subscriber.SubscribeAsync(topicFilter);
        // Settle — matches the FOCAS2 E2E test's guard against racing the
        // first publish against subscription installation.
        await Task.Delay(200);

        // --- Drive the pipeline -----------------------------------------
        await using var host = HostHarness.Build(
            sources: new[] { sourceReg },
            sinks: new[] { sinkReg },
            config: Config(Route(RouteId, SourceInstanceId, new[] { SinkInstanceId })));

        await host.StartAsync();

        // Demo has 14 tags (see BuildDemoTagSet). Wait until at least one
        // message has landed per tag, or the 20s deadline elapses.
        var expectedTagNames = BuildDemoTagSet().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var topicsSeen = received
                .Select(m => m.Topic.Split('/').Last())
                .ToHashSet(StringComparer.Ordinal);
            if (topicsSeen.IsSupersetOf(expectedTagNames)) break;
            await Task.Delay(50);
        }

        await host.StopAsync();
        await subscriber.DisconnectAsync();

        // --- Assertions --------------------------------------------------
        var snapshot = received.ToList();
        _output.WriteLine($"Received {snapshot.Count} MQTT message(s) across " +
            $"{snapshot.Select(m => m.Topic).Distinct().Count()} distinct topic(s).");

        snapshot.Should().NotBeEmpty("the adapter must publish at least one decoded tag");

        var topicPrefix = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/";
        snapshot.Select(m => m.Topic).Should().AllSatisfy(t =>
            t.Should().StartWith(topicPrefix));

        // Collapse to the most-recent payload per tag (the randomizer
        // thread updates numeric values every second, so we can't assert
        // exact numbers — only that the decoded shape is right).
        var latestByTag = snapshot
            .GroupBy(m => m.Topic[topicPrefix.Length..], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Payload, StringComparer.Ordinal);

        // Every configured tag should have been published at least once.
        foreach (var name in expectedTagNames)
        {
            latestByTag.Should().ContainKey(name,
                $"tag '{name}' was configured but never reached the broker");
        }

        // Spot-check decoded representations for each datatype:
        //  - bool tag payload is the lowercase "true" / "false" literal
        //  - numeric tags parse as numbers
        //  - string tags land unquoted (PerTag mode ships raw scalars)
        latestByTag["running"].Should().BeOneOf("true", "false");
        latestByTag["alarm_active"].Should().BeOneOf("true", "false");
        latestByTag["door_closed"].Should().BeOneOf("true", "false");

        double.Parse(latestByTag["spindle_rpm"], CultureInfo.InvariantCulture)
            .Should().BeInRange(1200, 1600);

        double.Parse(latestByTag["spindle_load"], CultureInfo.InvariantCulture)
            .Should().BeInRange(-40, 40);

        double.Parse(latestByTag["feed_rate"], CultureInfo.InvariantCulture)
            .Should().BeInRange(150, 350);

        long.Parse(latestByTag["parts_count"], CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(1_000_000L, "seeded counter starts at 1,234,567 and only climbs");

        double.Parse(latestByTag["temperature"], CultureInfo.InvariantCulture)
            .Should().BeInRange(30, 50, "raw 380..460 with scale 0.1 → 38.0..46.0 °C");

        latestByTag["part_name"].Should().Be("SHAFT-7X");
        latestByTag["mode"].Should().Be("AUTO");
        latestByTag["alarm_code"].Should().Be("0");

        double.Parse(latestByTag["cycle_time"], CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(0);

        double.Parse(latestByTag["energy_kwh"], CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(128.0, "seeded at 128.4 and only climbs");

        modbusAdapter.State.Should().Be(AdapterState.Stopped);
        mqttAdapter.State.Should().Be(AdapterState.Stopped);

        var health = await modbusAdapter.CheckHealthAsync(CancellationToken.None);
        ((long)health.Metrics!["pollSuccesses"]!).Should().BeGreaterThan(0);
        ((long)health.Metrics!["decodeFailures"]!).Should().Be(0,
            "every tag in the demo set must decode cleanly");
    }

    private static IReadOnlyList<ModbusTagDefinition> BuildDemoTagSet() => new ModbusTagDefinition[]
    {
        // Bits
        new() { Name = "running",         RegisterClass = ModbusRegisterClass.Coil,            Address = 0,   ScanRateMs = 250, Datatype = "bool" },
        new() { Name = "alarm_active",    RegisterClass = ModbusRegisterClass.Coil,            Address = 1,   ScanRateMs = 250, Datatype = "bool" },
        new() { Name = "door_closed",     RegisterClass = ModbusRegisterClass.DiscreteInput,   Address = 0,   ScanRateMs = 250, Datatype = "bool" },
        new() { Name = "tool_in_spindle", RegisterClass = ModbusRegisterClass.DiscreteInput,   Address = 1,   ScanRateMs = 250, Datatype = "bool" },

        // 16-bit numerics
        new() { Name = "spindle_rpm",     RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 0,   ScanRateMs = 200, Datatype = "uint16", Unit = "rpm" },
        new() { Name = "spindle_load",    RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 1,   ScanRateMs = 200, Datatype = "int16",  Unit = "%" },

        // Floats
        new() { Name = "feed_rate",       RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 10,  ScanRateMs = 200, Datatype = "float32", ByteOrder = ModbusByteOrder.ABCD, Unit = "mm/min" },
        new() { Name = "cycle_time",      RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 30,  ScanRateMs = 500, Datatype = "float32", ByteOrder = ModbusByteOrder.ABCD, Unit = "s" },
        new() { Name = "energy_kwh",      RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 40,  ScanRateMs = 500, Datatype = "float32", ByteOrder = ModbusByteOrder.ABCD, Unit = "kWh" },

        // Word-swapped 32-bit counter
        new() { Name = "parts_count",     RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 20,  ScanRateMs = 500, Datatype = "uint32",  ByteOrder = ModbusByteOrder.CDAB },

        // Int16 alarm code
        new() { Name = "alarm_code",      RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 50,  ScanRateMs = 500, Datatype = "int16" },

        // Strings — two separate lengths exercise the stringN width resolver
        new() { Name = "mode",            RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 60,  ScanRateMs = 1000, Datatype = "string8" },
        new() { Name = "part_name",       RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 100, ScanRateMs = 1000, Datatype = "string8" },

        // Scaled temperature on Input Registers
        new() { Name = "temperature",     RegisterClass = ModbusRegisterClass.InputRegister,   Address = 0,   ScanRateMs = 500, Datatype = "int16", Scale = 0.1, Unit = "C" },
    };

    private static async Task EnsureBrokerReachableAsync()
    {
        using var probe = new TcpClient();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await probe.ConnectAsync(BrokerHost, BrokerPort, cts.Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"MQTT broker not reachable at {BrokerHost}:{BrokerPort}. " +
                "Start Mosquitto locally before running this integration scenario.", ex);
        }
    }

    private sealed class StubGatewayIdentity : IGatewayIdentity
    {
        public StubGatewayIdentity(string gatewayId) => GatewayId = gatewayId;
        public string GatewayId { get; }
    }
}
