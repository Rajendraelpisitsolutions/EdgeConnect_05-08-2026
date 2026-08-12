// ============================================================================
// File: Eremos/EremosV2MockSubscriber.cs
// Purpose: Contract-driven mock subscriber for the EREMOS V2 revalidation
//          mock-fallback path. Subscribes to eremos/+/cnc/+/+ (Phase 0
//          contract per shared-knowledge/contracts/eremos-per-tag-mqtt.md)
//          and records every received message in-memory for the
//          contract / resilience gates' measurement methodology.
//
//          IMPORTANT: this subscriber does NOT test EREMOS V2 internals.
//          Gate 6 under the mock path is explicitly renamed "contract
//          subscriber receive parity" — it tests
//          mock_subscriber.ReceiveCount == gateway.EmitCount, not
//          EREMOS V2's ingest pipeline. Gates 6 and 7 (real-EREMOS-only)
//          are SKIPPED with explicit reasons on this code path.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §4.3 + §6
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// Mock EREMOS V2 subscriber. Connects to a Mosquitto broker, subscribes
/// to the Phase 0 contract pattern <c>eremos/+/cnc/+/+</c>, and records
/// every received PerTag message for downstream gate validators.
/// </summary>
public sealed class EremosV2MockSubscriber : IAsyncDisposable
{
    private const string Phase0Subscription = "eremos/+/cnc/+/+";

    private readonly string _brokerUrl;
    private readonly string _subscriberClientId;
    private readonly IMqttClient _client;
    private readonly ConcurrentBag<ReceivedMessage> _received = new();
    private readonly ConcurrentDictionary<string, long> _perTopicCounts = new();
    private MqttClientOptions? _options;
    private bool _connected;
    private bool _disposed;

    public EremosV2MockSubscriber(string brokerUrl, string subscriberClientId)
    {
        _brokerUrl = brokerUrl;
        _subscriberClientId = subscriberClientId;
        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        // Auto-reconnect: needed for Gate 5 broker-outage scenarios. When
        // the broker process dies, MQTTnet fires Disconnected; we wait
        // briefly then attempt to reconnect (and re-subscribe on the
        // Phase 0 pattern). Bounded retry loop — gives up if the broker
        // stays down longer than the test's recovery deadline.
        _client.DisconnectedAsync += OnDisconnectedAsync;
    }

    /// <summary>Connect to the broker and subscribe to <c>eremos/+/cnc/+/+</c>.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var uri = new Uri(_brokerUrl);
        _options = new MqttClientOptionsBuilder()
            .WithClientId(_subscriberClientId)
            .WithTcpServer(uri.Host, uri.Port)
            .WithCleanSession(true)
            .Build();

        await _client.ConnectAsync(_options, ct).ConfigureAwait(false);
        await _client.SubscribeAsync(Phase0Subscription, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce, ct)
            .ConfigureAwait(false);
        _connected = true;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        // No-op: the test fixture explicitly calls ReconnectAsync() after
        // injecting a broker outage. Auto-reconnect inside the event
        // handler had timing issues with MQTTnet 4.x's lifecycle. Doing
        // it explicitly from the test gives deterministic control over
        // the reconnect moment, which Gate 5's recoveryStart measurement
        // depends on.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Force a reconnect + re-subscribe. Used by the Gate 5 outage test
    /// after broker.StartAsync() to deterministically restore the
    /// subscriber side of the pipeline. Production EREMOS V2 instances
    /// would handle reconnect via their own client semantics; the mock
    /// subscriber is a test fixture, not a production client.
    /// </summary>
    public async Task ReconnectAsync(CancellationToken ct = default)
    {
        if (_disposed || _options is null)
        {
            throw new InvalidOperationException(
                "ReconnectAsync called on a disposed or unconnected subscriber.");
        }

        await _client.ConnectAsync(_options, ct).ConfigureAwait(false);
        await _client.SubscribeAsync(
            Phase0Subscription,
            MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Total number of messages received over the subscriber's lifetime
    /// (across all topics matching <c>eremos/+/cnc/+/+</c>).
    /// </summary>
    public long ReceiveCount => _received.Count;

    /// <summary>
    /// Per-topic receive counter — used by Gate 2 (emit/receive count
    /// parity per topic).
    /// </summary>
    public IReadOnlyDictionary<string, long> PerTopicReceiveCounts =>
        _perTopicCounts.ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// All received messages in receive order. Used by Gate 3 (schema
    /// stability) and Gate 4 (topic determinism).
    /// </summary>
    public IReadOnlyList<ReceivedMessage> ReceivedMessages =>
        _received.OrderBy(m => m.ReceivedAt).ToList();

    public async ValueTask DisposeAsync()
    {
        _disposed = true; // suppress auto-reconnect on clean disconnect
        if (_connected)
        {
            try
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }
        _client.Dispose();
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payloadBytes = e.ApplicationMessage.PayloadSegment.ToArray();
        var payload = Encoding.UTF8.GetString(payloadBytes);

        _received.Add(new ReceivedMessage(topic, payload, payloadBytes, DateTime.UtcNow));
        _perTopicCounts.AddOrUpdate(topic, 1, (_, count) => count + 1);
        return Task.CompletedTask;
    }
}

/// <summary>
/// One received MQTT message captured by the mock subscriber. Carries the
/// topic + payload (UTF-8 string + raw bytes) + receive timestamp.
/// </summary>
/// <param name="Topic">The MQTT topic the message arrived on.</param>
/// <param name="Payload">The payload decoded as UTF-8.</param>
/// <param name="PayloadBytes">Raw payload bytes (for non-UTF-8 detection in Gate 3).</param>
/// <param name="ReceivedAt">UTC wall-clock time the message was received.</param>
public sealed record ReceivedMessage(
    string Topic,
    string Payload,
    byte[] PayloadBytes,
    DateTime ReceivedAt);
