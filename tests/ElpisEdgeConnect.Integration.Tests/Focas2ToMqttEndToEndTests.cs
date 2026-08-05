// ============================================================================
// File: Focas2ToMqttEndToEndTests.cs
// Purpose: End-to-end scenario that closes the loop for the first real CNC
//          data path — FakeFocas2Api → Focas2SourceAdapter → SourceSupervisor
//          channel → RoutingEngine → MqttSinkAdapter → real MQTT broker.
//
//          Verifies that points produced by the FOCAS2 adapter reach the
//          broker with the correct PerTag topic shape and payload. This is
//          the "finishing FOCAS2" exit gate for Phase 2: unit tests stay
//          green AND the wiring works end-to-end against a real sink.
//
// Prerequisites:
//   * A local MQTT broker (Mosquitto) running on 127.0.0.1:1883 with
//     anonymous access. Matches the existing MQTT sink adapter tests.
//
// Reference: PHASE2_ENTRY.md; ARCHITECTURE_BLUEPRINT.md §4.2, §4.3, §8
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
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.Mqtt;
using ElpisEdgeConnect.Sources.Focas2;
using ElpisEdgeConnect.Sources.Focas2.Tests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;
using static ElpisEdgeConnect.Integration.Tests.IntegrationTestData;

namespace ElpisEdgeConnect.Integration.Tests;

/// <summary>
/// End-to-end FOCAS2 → pipeline → MQTT scenario. Requires a local Mosquitto
/// broker on 127.0.0.1:1883 (matches existing MQTT sink adapter tests).
/// </summary>
[Trait("Category", "RequiresMqttBroker")]
public sealed class Focas2ToMqttEndToEndTests
{
    private const string BrokerHost = "127.0.0.1";
    private const int BrokerPort = 1883;

    // Pinned gateway id used in this test. The adapter resolves its
    // gateway id via IGatewayIdentity; we inject a stub returning this
    // literal so the MQTT topic filter is predictable. In production the
    // host's FileSystemGatewayIdentity emits a persistent UUID.
    private const string TestGatewayId = "gw-e2e-fixture";
    private const string SourceInstanceId = "focas-e2e";
    private const string SinkInstanceId = "mqtt-e2e";
    private const string RouteId = "route-focas-mqtt";

    // ------------------------------------------------------------------
    // HappyPath: a FOCAS2 fake that returns a known status snapshot flows
    // canonical points end-to-end to the broker on the EREMOS V2 PerTag
    // topic shape. We only inspect the status tags — Status runs on every
    // poll regardless of DataPoints filter, so they're the most stable
    // assertion target.
    // ------------------------------------------------------------------
    [Fact]
    public async Task Focas2Source_PublishesStatusTagsToMqttViaPerTagTopics()
    {
        await EnsureBrokerReachableAsync();

        // --- FOCAS2 side -------------------------------------------------
        var fakeApi = new FakeFocas2Api
        {
            // Run=3 (Editing/Running), Aut=1 (MDI), Emergency=0 (not stopped)
            StatusInfo = new OdbStatusInfo { Run = 3, Aut = 1, Emergency = 0 },
        };
        var focasConfig = new Focas2SourceConfiguration
        {
            InstanceId = SourceInstanceId,
            ProtocolName = "focas2",
            DeviceId = "lathe-01",
            DeviceName = "Integration Lathe",
            IpAddress = "127.0.0.1",
            Port = 8193,
            KeepAlive = true,
            MaxConnectRetries = 1,
            // Restrict collection to Status only so each poll yields a
            // compact batch (run_state, auto_mode, emergency_stop).
            DataPoints = new[] { "Status/RunState" },
        };
        var focasAdapter = new Focas2SourceAdapter(
            SourceInstanceId,
            fakeApi,
            NullLogger<Focas2SourceAdapter>.Instance,
            new StubGatewayIdentity(TestGatewayId));
        var sourceReg = new SourceRegistration
        {
            Adapter = focasAdapter,
            Config = focasConfig,
            RouteId = RouteId,
        };

        // --- MQTT side ---------------------------------------------------
        var mqttConfig = new MqttSinkConfiguration
        {
            InstanceId = SinkInstanceId,
            ProtocolName = "mqtt",
            BrokerHost = BrokerHost,
            BrokerPort = BrokerPort,
            ClientId = $"edgeconnect-e2e-{Guid.NewGuid():N}",
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

        // --- Subscribe BEFORE starting the host so we don't race the
        //     first publish. Wildcard matches any status_* tag emitted
        //     for this gateway+source. ConcurrentQueue preserves order
        //     without an explicit lock on a collection instance.
        var topicFilter = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/+";
        var received = new ConcurrentQueue<(string Topic, string Payload)>();
        using var subscriber = new MqttFactory().CreateMqttClient();
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithClientId($"e2e-sub-{Guid.NewGuid():N}")
            .Build(), CancellationToken.None);
        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            received.Enqueue((
                args.ApplicationMessage.Topic,
                Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment)));
            return Task.CompletedTask;
        };
        await subscriber.SubscribeAsync(topicFilter);
        // Let the broker ack + install the subscription before starting
        // the host. Without this delay the first few publishes can race
        // the subscribe under CPU pressure (cross-assembly parallel test
        // runs), producing intermittent "missing tags" failures.
        await Task.Delay(150);

        // --- Drive the pipeline -----------------------------------------
        await using var host = HostHarness.Build(
            sources: new[] { sourceReg },
            sinks: new[] { sinkReg },
            config: Config(Route(RouteId, SourceInstanceId, new[] { SinkInstanceId })));

        await host.StartAsync();

        // FOCAS2 status collector emits five tags per poll: run_state,
        // auto_mode, emergency_stop, motion, alarm_flag. The supervisor
        // polls in a tight loop so waiting for the full set should be
        // quick against a local broker.
        const int ExpectedStatusTagCount = 5;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var distinctTopics = received.Select(m => m.Topic).ToHashSet(StringComparer.Ordinal);
            if (distinctTopics.Count >= ExpectedStatusTagCount) break;
            await Task.Delay(50);
        }

        await host.StopAsync();
        await subscriber.DisconnectAsync();

        // --- Assertions --------------------------------------------------
        var snapshot = received.ToList();

        snapshot.Should().NotBeEmpty("at least one status tag should reach the broker");

        var topicPrefix = $"eremos/{TestGatewayId}/cnc/{SourceInstanceId}/";
        snapshot.Select(m => m.Topic).Should().AllSatisfy(t =>
            t.Should().StartWith(topicPrefix));

        // The FOCAS2 status tags carry slashes like "status/run_state"; the
        // MQTT topic resolver sanitizes them to underscores, so they land
        // on topics ending in "status_run_state" etc. Status collector emits
        // all five on every poll regardless of the DataPoints filter.
        var tailSegments = snapshot.Select(m => m.Topic[topicPrefix.Length..]).Distinct().ToArray();
        tailSegments.Should().Contain("status_run_state");
        tailSegments.Should().Contain("status_auto_mode");
        tailSegments.Should().Contain("status_emergency_stop");
        tailSegments.Should().Contain("status_motion");
        tailSegments.Should().Contain("status_alarm_flag");

        // PerTag payloads are raw scalar strings. Run=3 maps to "Running" in
        // the status collector, Aut=1 maps to "MDI". We don't hard-code the
        // string representation here — just verify a non-empty payload.
        snapshot.Should().AllSatisfy(m =>
            m.Payload.Should().NotBeNullOrEmpty("PerTag payloads must carry a scalar"));

        // The adapter should have observed poll successes and ended in
        // Stopped after the host-level StopAsync() drained the supervisors.
        focasAdapter.State.Should().Be(AdapterState.Stopped);
        mqttAdapter.State.Should().Be(AdapterState.Stopped);
        var focasHealth = await focasAdapter.CheckHealthAsync(CancellationToken.None);
        focasHealth.Metrics.Should().NotBeNull();
        var focasMetrics = focasHealth.Metrics!;
        focasMetrics.Should().ContainKey("pollSuccesses");
        ((long)focasMetrics["pollSuccesses"]!).Should().BeGreaterThan(0,
            "at least one poll should have succeeded end-to-end");

        var mqttHealth = await mqttAdapter.CheckHealthAsync(CancellationToken.None);
        mqttHealth.Metrics.Should().NotBeNull();
        var mqttMetrics = mqttHealth.Metrics!;
        mqttMetrics.Should().ContainKey("publishSuccesses");
        ((long)mqttMetrics["publishSuccesses"]!).Should().BeGreaterThan(0,
            "MQTT sink should have reported at least one successful publish");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Fast TCP probe that fails the test with a clear message when the
    /// local MQTT broker is not running, instead of hanging in the MQTT
    /// client's retry loop. Matches how MQTT sink tests are expected to
    /// be run (see CLAUDE.md: "MQTT tests require Mosquitto").
    /// </summary>
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
                "On Linux: `sudo systemctl start mosquitto`. " +
                "On Windows: install Mosquitto and ensure the service is running. " +
                "This test is tagged [Trait(\"Category\", \"RequiresMqttBroker\")] " +
                "so it can be filtered out of environments without a broker.",
                ex);
        }
    }

    /// <summary>
    /// Minimal <see cref="IGatewayIdentity"/> stub that returns a fixed
    /// gateway id. Used so the test knows exactly which topic prefix the
    /// FOCAS2 adapter will emit under.
    /// </summary>
    private sealed class StubGatewayIdentity : IGatewayIdentity
    {
        public StubGatewayIdentity(string gatewayId) => GatewayId = gatewayId;
        public string GatewayId { get; }
    }
}
