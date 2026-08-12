// ============================================================================
// File: MqttSinkAdapterTests.cs
// Purpose: Adapter tests against a real local Mosquitto broker on
//          localhost:1883. Requires: mosquitto running with anonymous access.
//          Using a real broker instead of MQTTnet's in-process server avoids
//          the silent-bind failure observed on Linux x64 and Windows Arm64.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

/// <summary>
/// Tagged <c>RequiresMqttBroker</c> so CI environments without Mosquitto
/// can exclude this class via <c>--filter "Category!=RequiresMqttBroker"</c>.
/// Every test in here connects to <c>localhost:1883</c> and will hang in
/// MQTTnet's retry path if the broker isn't up.
/// </summary>
[Trait("Category", "RequiresMqttBroker")]
public sealed class MqttSinkAdapterTests
{
    private const int BrokerPort = 1883;
    private const string BrokerHost = "127.0.0.1";

    private static MqttSinkConfiguration BatchConfig() => new()
    {
        InstanceId = "test-sink",
        ProtocolName = "mqtt",
        BrokerHost = BrokerHost,
        BrokerPort = BrokerPort,
        PublishMode = MqttPublishMode.Batch,
        TopicTemplate = "test/{gatewayId}/data",
        QosLevel = 1,
        ReconnectDelayMs = 100,
        MaxReconnectDelayMs = 500,
    };

    private static MqttSinkConfiguration PerTagConfig() => new()
    {
        InstanceId = "test-sink",
        ProtocolName = "mqtt",
        BrokerHost = BrokerHost,
        BrokerPort = BrokerPort,
        PublishMode = MqttPublishMode.PerTag,
        PerTagTopicTemplate = "eremos/{gatewayId}/cnc/{sourceId}/{tagName}",
        QosLevel = 0,
        ReconnectDelayMs = 100,
        MaxReconnectDelayMs = 500,
    };

    private static IReadOnlyList<CanonicalDataPoint> MakeBatch(int count, string sourceId = "src-1")
    {
        var now = DateTime.UtcNow;
        var pts = new CanonicalDataPoint[count];
        for (var i = 0; i < count; i++)
        {
            pts[i] = new CanonicalDataPoint
            {
                GatewayId = "gw-1",
                SourceInstanceId = sourceId,
                ProtocolName = "mock",
                DeviceId = "dev-1",
                TagName = $"tag{i}",
                TagPath = $"dev-1/tag{i}",
                Value = (double)i * 1.5,
                ValueType = CanonicalValueType.Double,
                Quality = DataQuality.Good,
                DeviceTimestamp = now,
                GatewayTimestamp = now,
                SequenceNumber = i,
            };
        }
        return pts;
    }

    /// <summary>
    /// Subscribe to a topic on the local Mosquitto broker and collect
    /// messages for a short window. Returns the collected messages.
    /// </summary>
    private static async Task<List<MqttApplicationMessage>> SubscribeAndCollect(
        string topicFilter,
        Func<Task> publishAction,
        int expectedCount = 1,
        int timeoutMs = 2000)
    {
        var received = new List<MqttApplicationMessage>();
        using var subscriber = new MqttFactory().CreateMqttClient();
        await subscriber.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(BrokerHost, BrokerPort)
            .WithClientId($"test-sub-{Guid.NewGuid():N}")
            .Build(), CancellationToken.None);

        subscriber.ApplicationMessageReceivedAsync += args =>
        {
            received.Add(args.ApplicationMessage);
            return Task.CompletedTask;
        };
        await subscriber.SubscribeAsync(topicFilter);
        await Task.Delay(100); // let subscription settle

        await publishAction();

        // Wait for messages with timeout.
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (received.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await subscriber.DisconnectAsync();
        return received;
    }

    // ----- Lifecycle -----

    [Fact]
    public async Task LifecycleTransitions_FollowContract()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        adapter.State.Should().Be(AdapterState.Created);

        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Initialized);

        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        await adapter.StopAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Stopped);

        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_WrongConfigType_TransitionsToFailed()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var wrongConfig = new TestWrongConfig { InstanceId = "x", ProtocolName = "wrong" };

        var act = async () => await adapter.InitializeAsync(wrongConfig, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        await adapter.DisposeAsync();
    }

    // ----- Batch publish -----

    [Fact]
    public async Task PublishBatch_HappyPath_ReturnsSuccess()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var result = await adapter.PublishAsync(MakeBatch(5), CancellationToken.None);
        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(5);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PublishBatch_EmptyBatch_ReturnsSuccessZero()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var result = await adapter.PublishAsync(Array.Empty<CanonicalDataPoint>(), CancellationToken.None);
        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(0);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PublishBatch_DeliversToBroker_WithCorrectTopicAndPayload()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var messages = await SubscribeAndCollect(
            "test/+/data",
            () => adapter.PublishAsync(MakeBatch(3), CancellationToken.None));

        messages.Should().HaveCountGreaterThanOrEqualTo(1);
        var msg = messages.First();
        msg.Topic.Should().Be("test/gw-1/data");

        var json = Encoding.UTF8.GetString(msg.PayloadSegment);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(3);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    // ----- PerTag publish -----

    [Fact]
    public async Task PublishPerTag_HappyPath_OneMessagePerPoint()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(PerTagConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        // Small delay to let the MQTT connection fully settle before
        // publishing — some brokers need a moment after CONNACK.
        await Task.Delay(100);

        var result = await adapter.PublishAsync(MakeBatch(3), CancellationToken.None);
        result.Success.Should().BeTrue(
            $"all 3 points should publish; got Accepted={result.AcceptedCount} Rejected={result.RejectedCount} Error={result.Error?.Message}");
        result.AcceptedCount.Should().Be(3);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PublishPerTag_EremosTopicPattern_MatchesSubscription()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(PerTagConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var messages = await SubscribeAndCollect(
            "eremos/+/cnc/+/+",
            () => adapter.PublishAsync(MakeBatch(1, sourceId: "AMS1CNC"), CancellationToken.None));

        messages.Should().HaveCountGreaterThanOrEqualTo(1);
        messages.First().Topic.Should().Be("eremos/gw-1/cnc/AMS1CNC/tag0");

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task PublishPerTag_BinaryValue_Rejected()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(PerTagConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var batch = new[]
        {
            new CanonicalDataPoint
            {
                GatewayId = "gw-1", SourceInstanceId = "src-1", ProtocolName = "mock",
                DeviceId = "dev-1", TagName = "bin", TagPath = "dev-1/bin",
                Value = new byte[] { 1, 2, 3 }, ValueType = CanonicalValueType.ByteArray,
                Quality = DataQuality.Good,
                DeviceTimestamp = DateTime.UtcNow, GatewayTimestamp = DateTime.UtcNow,
                SequenceNumber = 0,
            },
        };

        var result = await adapter.PublishAsync(batch, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.RejectedCount.Should().Be(1);
        result.AcceptedCount.Should().Be(0);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    // ----- Health -----

    [Fact]
    public async Task CheckHealthAsync_WhenRunning_ReportsHealthy()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.State.Should().Be(AdapterState.Running);
        health.Metrics.Should().ContainKey("publishAttempts");
        health.Metrics.Should().ContainKey("isConnected");
        health.Metrics.Should().ContainKey("publishMode");
        health.Metrics.Should().ContainKey("brokerEndpoint");

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    // ----- Validation -----

    [Fact]
    public async Task ValidateConfigAsync_ValidConfig_ReturnsSuccess()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var result = await adapter.ValidateConfigAsync(BatchConfig(), CancellationToken.None);
        result.IsValid.Should().BeTrue();
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingHost_ReturnsFailure()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var cfg = new MqttSinkConfiguration { InstanceId = "x", ProtocolName = "mqtt", BrokerHost = "" };
        var result = await adapter.ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task ValidateConfigAsync_InvalidQos_ReturnsFailure()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var cfg = new MqttSinkConfiguration
        {
            InstanceId = "x",
            ProtocolName = "mqtt",
            BrokerHost = "localhost",
            QosLevel = 2,
        };
        var result = await adapter.ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task ValidateConfigAsync_IncompleteAuth_ReturnsFailure()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var cfg = new MqttSinkConfiguration
        {
            InstanceId = "x",
            ProtocolName = "mqtt",
            BrokerHost = "localhost",
            Username = "user",
        };
        var result = await adapter.ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        await adapter.DisposeAsync();
    }

    // ----- Dispose -----

    [Fact]
    public async Task DisposeAsync_StopsIfRunning()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(BatchConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Running);

        await adapter.DisposeAsync();
        adapter.State.Should().Be(AdapterState.Stopped);
    }

    // ----- Connect failure -----

    [Fact]
    public async Task Connect_InvalidHost_FailsAfterTimeout()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        var badConfig = new MqttSinkConfiguration
        {
            InstanceId = "bad",
            ProtocolName = "mqtt",
            BrokerHost = "192.0.2.1", // RFC 5737 TEST-NET — guaranteed unreachable
            BrokerPort = 1883,
            ReconnectDelayMs = 100,
            MaxReconnectDelayMs = 200,
        };
        await adapter.InitializeAsync(badConfig, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var act = async () => await adapter.StartAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await adapter.DisposeAsync();
    }

    private sealed record TestWrongConfig : SinkConfiguration;
}
