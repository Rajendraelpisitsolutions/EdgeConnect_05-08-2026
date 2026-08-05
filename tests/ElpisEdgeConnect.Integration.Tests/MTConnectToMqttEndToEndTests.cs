// ============================================================================
// File: MTConnectToMqttEndToEndTests.cs
// Purpose: End-to-end scenario for the MTConnect source adapter — fake
//          Agent → MTConnectSourceAdapter → SourceSupervisor channel →
//          RoutingEngine → MqttSinkAdapter → real MQTT broker.
//
// Prerequisites:
//   * A local MQTT broker (Mosquitto) on 127.0.0.1:1883 with anonymous
//     access. Tagged [Trait("Category", "RequiresMqttBroker")] so
//     environments without a broker can filter this out.
//
// Reference: docs/adapter-sdk/mtconnect-adapter.md; PHASE2_ENTRY.md
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sources.MTConnect;
using ElpisEdgeConnect.Sources.MTConnect.Tests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;
using static ElpisEdgeConnect.Integration.Tests.IntegrationTestData;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// End-to-end MTConnect → pipeline → MQTT scenario. Uses a fake
/// <see cref="IMTConnectClient"/> so no live Agent is required; the real
/// MQTT broker is the only external dependency.
/// </summary>
[Trait("Category", "RequiresMqttBroker")]
public sealed class MTConnectToMqttEndToEndTests
{
    private const string BrokerHost = "127.0.0.1";
    private const int BrokerPort = 1883;

    private const string TestGatewayId = "gw-mtc-e2e-fixture";
    private const string SourceInstanceId = "mtc-e2e";
    private const string SinkInstanceId = "mqtt-mtc-e2e";
    private const string RouteId = "route-mtc-mqtt";

    private const string CurrentXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MTConnectStreams xmlns="urn:mtconnect.org:MTConnectStreams:1.7">
          <Streams>
            <DeviceStream name="CNC-1" uuid="urn:test:cnc-1">
              <ComponentStream component="Controller">
                <Events>
                  <Execution dataItemId="e1" sequence="1">ACTIVE</Execution>
                  <ControllerMode dataItemId="e2" sequence="2">AUTOMATIC</ControllerMode>
                  <EmergencyStop dataItemId="e3" sequence="3">ARMED</EmergencyStop>
                  <Program dataItemId="e4" sequence="4">O9999</Program>
                  <PartCount dataItemId="e5" sequence="5">17</PartCount>
                </Events>
                <Samples>
                  <SpindleSpeed dataItemId="s1" sequence="6">1500</SpindleSpeed>
                  <PathFeedrate dataItemId="s2" sequence="7">320.5</PathFeedrate>
                </Samples>
              </ComponentStream>
              <ComponentStream component="Axes">
                <Samples>
                  <Position dataItemId="ax1" name="X" subType="ACTUAL" sequence="8">100.1</Position>
                  <Position dataItemId="ax2" name="Y" subType="ACTUAL" sequence="9">200.2</Position>
                </Samples>
              </ComponentStream>
            </DeviceStream>
          </Streams>
        </MTConnectStreams>
        """;

    [Fact]
    public async Task MTConnectSource_PublishesCurrentValuesToMqttViaPerTagTopics()
    {
        await EnsureBrokerReachableAsync();

        // --- MTConnect side ---------------------------------------------
        var fakeClient = new FakeMTConnectClient { CurrentResponse = CurrentXml };
        var mtcConfig = new MTConnectSourceConfiguration
        {
            InstanceId = SourceInstanceId,
            ProtocolName = MTConnectSourceConfiguration.ProtocolNameConstant,
            DeviceId = "cnc-e2e",
            DeviceName = "Integration CNC",
            AgentBaseUrl = "http://fake.local:5000/",
            PollIntervalMs = 0,
            TimeoutSeconds = 5,
        };
        var mtcAdapter = new MTConnectSourceAdapter(
            SourceInstanceId,
            NullLogger<MTConnectSourceAdapter>.Instance,
            new StubGatewayIdentity(TestGatewayId),
            clientFactory: _ => fakeClient);
        var sourceReg = new SourceRegistration
        {
            Adapter = mtcAdapter,
            Config = mtcConfig,
            RouteId = RouteId,
        };

        // --- MQTT side --------------------------------------------------
        var mqttConfig = new MqttSinkConfiguration
        {
            InstanceId = SinkInstanceId,
            ProtocolName = "mqtt",
            BrokerHost = BrokerHost,
            BrokerPort = BrokerPort,
            ClientId = $"edgeconnect-mtc-e2e-{Guid.NewGuid():N}",
            PublishMode = MqttPublishMode.PerTag,
            PerTagTopicTemplate = "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
            QosLevel = 0,
            ReconnectDelayMs = 200,
            MaxReconnectDelayMs = 1000,
        };
        var mqttAdapter = new MqttSinkAdapter(SinkInstanceId, NullLogger<MqttSinkAdapter>.Instance);
        var sinkReg = new SinkRegistration
        {
            Adapter = mqttAdapter,
            Config = mqttConfig,
            RouteId = RouteId,
        };

        // --- Subscribe BEFORE the host starts to avoid racing publish ---
        var topicFilter = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/+";
        var received = new ConcurrentQueue<(string Topic, string Payload)>();
        using var subscriber = new MqttFactory().CreateMqttClient();
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithClientId($"mtc-sub-{Guid.NewGuid():N}")
            .Build(), CancellationToken.None);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            received.Enqueue((args.ApplicationMessage.Topic,
                Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment)));
            return Task.CompletedTask;
        };
        await subscriber.SubscribeAsync(topicFilter);
        // Let the broker acknowledge + install the subscription before we
        // start publishing. Without this delay the first few publishes
        // can race the subscribe under CPU pressure (cross-assembly
        // parallel test runs), producing intermittent "missing tags"
        // failures. Matches the settle delay in MqttSinkAdapterTests.
        await Task.Delay(150);

        // --- Drive the pipeline -----------------------------------------
        await using var host = HostHarness.Build(
            sources: new[] { sourceReg },
            sinks: new[] { sinkReg },
            config: Config(Route(RouteId, SourceInstanceId, new[] { SinkInstanceId })));

        await host.StartAsync();

        // Wait until we've seen a reasonable set of distinct tags — the
        // MTConnect parser emits 11+ distinct canonical tags for the
        // fixture above (run_state, controller_mode, emergency_stop,
        // main_program, running_program, spindle/speed, feed_rate,
        // parts_count, alarms/count, alarms/first_fault, axes/x/absolute,
        // axes/y/absolute).
        const int ExpectedDistinctTagCount = 10;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var distinctTopics = received.Select(m => m.Topic).ToHashSet(StringComparer.Ordinal);
            if (distinctTopics.Count >= ExpectedDistinctTagCount) break;
            await Task.Delay(50);
        }

        await host.StopAsync();
        await subscriber.DisconnectAsync();

        // --- Assertions -------------------------------------------------
        var snapshot = received.ToList();
        snapshot.Should().NotBeEmpty("MTConnect points should reach the broker");

        var topicPrefix = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/";
        snapshot.Select(m => m.Topic).Should().AllSatisfy(t =>
            t.Should().StartWith(topicPrefix));

        // MqttTopicResolver turns "status/run_state" into "status_run_state"
        // (slashes inside tag names sanitized to underscores).
        var tailSegments = snapshot
            .Select(m => m.Topic[topicPrefix.Length..])
            .Distinct()
            .ToArray();
        tailSegments.Should().Contain("status_run_state");
        tailSegments.Should().Contain("status_controller_mode");
        tailSegments.Should().Contain("status_emergency_stop");
        tailSegments.Should().Contain("program_main_program");
        tailSegments.Should().Contain("spindle_speed");
        tailSegments.Should().Contain("axes_feed_rate");
        tailSegments.Should().Contain("production_parts_count");
        tailSegments.Should().Contain("alarms_count");
        tailSegments.Should().Contain("axes_x_absolute");
        tailSegments.Should().Contain("axes_y_absolute");

        mtcAdapter.State.Should().Be(AdapterState.Stopped);
        mqttAdapter.State.Should().Be(AdapterState.Stopped);

        var mtcHealth = await mtcAdapter.CheckHealthAsync(CancellationToken.None);
        mtcHealth.Metrics.Should().NotBeNull();
        var mtcMetrics = mtcHealth.Metrics!;
        ((long)mtcMetrics["pollSuccesses"]!).Should().BeGreaterThan(0);

        var mqttHealth = await mqttAdapter.CheckHealthAsync(CancellationToken.None);
        mqttHealth.Metrics.Should().NotBeNull();
        var mqttMetrics = mqttHealth.Metrics!;
        ((long)mqttMetrics["publishSuccesses"]!).Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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
                "Start Mosquitto locally before running this integration scenario. " +
                "This test is tagged [Trait(\"Category\", \"RequiresMqttBroker\")].",
                ex);
        }
    }

    /// <summary>Fixed-identity stub used so the topic filter is predictable.</summary>
    private sealed class StubGatewayIdentity : IGatewayIdentity
    {
        public StubGatewayIdentity(string gatewayId) => GatewayId = gatewayId;
        public string GatewayId { get; }
    }
}
