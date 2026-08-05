// ============================================================================
// File: Session/SparkplugMqttTransport.cs
// Purpose: The concrete MQTTnet implementation of the actor-owned transport seam
//          (slice 4, review r1 B3). Pins MQTT 3.1.1; validates the CONNACK and the
//          SUBACK grant (exact NCMD must be granted QoS 1); QoS-0/retain=false DATA;
//          QoS-1/retain=false NDEATH Will. Auto-reconnect is DISABLED. A fresh client
//          is created per ConnectAsync with the actor-supplied generation captured in
//          its disconnect handler. TWO teardown semantics: DisconnectAsync is a
//          GRACEFUL clean DISCONNECT (broker DISCARDS the Will — used by slice 6 after
//          an explicit NDEATH); DisposeAsync ABORTS (dispose without a clean DISCONNECT,
//          so the broker PUBLISHES the Will) — the correct retirement for a suspect /
//          uncertain attempt. Intentional teardown suppresses the Disconnected event.
//          Option/message construction is extracted to pure internal factories so the
//          MQTT profile is unit-testable without a broker (the team rejects the
//          in-process server; real-broker interop is K6).
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.2, §4, §9.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>MQTTnet-backed <see cref="ISparkplugMqttTransport"/>; no automatic reconnect.</summary>
internal sealed class SparkplugMqttTransport : ISparkplugMqttTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<IMqttClient> _clientFactory;
    private IMqttClient? _client;
    private volatile bool _suppressDisconnectedEvent;
    private bool _disposed;

    /// <summary>Construct with the default MQTTnet client factory.</summary>
    public SparkplugMqttTransport()
        : this(() => new MqttFactory().CreateMqttClient())
    {
    }

    /// <summary>Construct with an injected client factory (tests).</summary>
    /// <param name="clientFactory">Creates a fresh <see cref="IMqttClient"/> per CONNECT.</param>
    internal SparkplugMqttTransport(Func<IMqttClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        _clientFactory = clientFactory;
    }

    /// <inheritdoc/>
    public event Func<long, Task>? Disconnected;

    /// <inheritdoc/>
    public event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;

    /// <inheritdoc/>
    public bool IsConnected => _client?.IsConnected ?? false;

    /// <inheritdoc/>
    public async Task ConnectAsync(
        SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await RetireClientAsync().ConfigureAwait(false); // abort any prior client before a new session
        _suppressDisconnectedEvent = false;

        var client = _clientFactory();
        _client = client;

        // Capture THIS attempt's generation so a retired client's delayed callback carries its
        // own generation. A callback fired for an actor-requested teardown is suppressed.
        client.DisconnectedAsync += _ =>
            _suppressDisconnectedEvent ? Task.CompletedTask : RaiseDisconnectedAsync(connectionGeneration);

        // Forward inbound NCMD messages tagged with this client's generation; the actor filters stale
        // generations and classifies the payload (it never publishes from the callback).
        client.ApplicationMessageReceivedAsync += args =>
        {
            var handler = NodeCommandReceived;
            if (handler is null)
            {
                return Task.CompletedTask;
            }

            var segment = args.ApplicationMessage.PayloadSegment;
            var payload = segment.Array is null
                ? ReadOnlyMemory<byte>.Empty
                : new ReadOnlyMemory<byte>(segment.Array, segment.Offset, segment.Count);
            return handler.Invoke(connectionGeneration, payload);
        };

        MqttClientConnectResult result;
        try
        {
            result = await client.ConnectAsync(BuildConnectOptions(request), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation stays cancellation — never normalized to a transport failure
        }
        catch (Exception ex)
        {
            // Normalize a framework/MQTTnet CONNECT throw into a stable, secret-free typed error
            // (type name only — never the exception message, which could echo endpoint/credentials).
            throw TransportFailure(
                SparkplugErrors.TransportConnectFailed, $"CONNECT failed ({ex.GetType().Name}).");
        }

        RequireConnectSuccess(result.ResultCode == MqttClientConnectResultCode.Success, result.ResultCode.ToString());
    }

    /// <inheritdoc/>
    public async Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(topicFilter);
        var client = RequireClient();
        MqttClientSubscribeResult result;
        try
        {
            result = await client.SubscribeAsync(BuildSubscribeOptions(topicFilter), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation stays cancellation
        }
        catch (Exception ex)
        {
            throw TransportFailure(
                SparkplugErrors.TransportSubscribeFailed, $"SUBSCRIBE failed ({ex.GetType().Name}).");
        }

        RequireExactNcmdGrant(
            result.Items.Select(i => new KeyValuePair<string, int>(i.TopicFilter.Topic, MapGrantedQos(i.ResultCode))).ToList(),
            topicFilter);
    }

    /// <inheritdoc/>
    public async Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(topic);
        var client = RequireClient();
        try
        {
            var result = await client.PublishAsync(BuildPublishMessage(topic, payload), cancellationToken).ConfigureAwait(false);
            return result.IsSuccess; // local transport boundary — never broker receipt
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false; // observable/uncertain send failure
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        // GRACEFUL: a clean MQTT DISCONNECT tells the broker to DISCARD the Will (the caller has
        // already published an explicit NDEATH). Suppress the Disconnected event (intentional).
        var client = _client;
        if (client is null || !client.IsConnected)
        {
            return;
        }

        _suppressDisconnectedEvent = true;
        try
        {
            await client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort graceful disconnect.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await RetireClientAsync().ConfigureAwait(false);
    }

    // ABORT: dispose the client WITHOUT a clean DISCONNECT, so the broker publishes the Will
    // (NDEATH) for a suspect/uncertain attempt. Suppress the Disconnected event (intentional).
    private async Task RetireClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        _suppressDisconnectedEvent = true;
        try
        {
            // Deliberately NO DisconnectAsync — a clean DISCONNECT would suppress the Will.
            client.Dispose();
        }
        catch
        {
            // Best-effort — the client is being retired.
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private Task RaiseDisconnectedAsync(long generation)
    {
        var handler = Disconnected;
        return handler is null ? Task.CompletedTask : handler.Invoke(generation);
    }

    private IMqttClient RequireClient() =>
        _client ?? throw new InvalidOperationException("The Sparkplug transport is not connected.");

    // ----- Pure factories / validators (unit-testable without a broker) -----

    /// <summary>Build the pinned MQTT 3.1.1 client options (incl. the QoS-1, non-retained NDEATH Will).</summary>
    internal static MqttClientOptions BuildConnectOptions(SparkplugMqttConnectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new MqttClientOptionsBuilder()
            .WithProtocolVersion(MqttProtocolVersion.V311) // pinned wire contract, not a library default
            .WithTcpServer(request.Endpoint.Host, request.Endpoint.Port)
            .WithClientId(request.ClientId)
            .WithCleanSession(request.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(request.KeepAliveSeconds))
            .WithTimeout(ConnectTimeout)
            .WithWillTopic(request.WillTopic)
            .WithWillPayload(request.WillPayload.ToArray())
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce) // QoS 1
            .WithWillRetain(false);

        if (!string.IsNullOrEmpty(request.Username))
        {
            builder.WithCredentials(request.Username, request.Password);
        }

        if (request.Endpoint.Tls)
        {
            builder.WithTlsOptions(o => o.UseTls());
        }

        return builder.Build();
    }

    /// <summary>Build the exact NCMD subscribe options at QoS 1.</summary>
    internal static MqttClientSubscribeOptions BuildSubscribeOptions(string topicFilter) =>
        new MqttFactory().CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

    /// <summary>Build a QoS-0, non-retained publish message.</summary>
    internal static MqttApplicationMessage BuildPublishMessage(string topic, ReadOnlyMemory<byte> payload) =>
        new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.ToArray())
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce) // QoS 0
            .WithRetainFlag(false)
            .Build();

    /// <summary>Fail closed unless CONNECT returned a success CONNACK.</summary>
    internal static void RequireConnectSuccess(bool isSuccess, string resultCode)
    {
        if (!isSuccess)
        {
            throw TransportFailure(
                SparkplugErrors.TransportConnectFailed, $"CONNECT was refused (CONNACK '{resultCode}').");
        }
    }

    /// <summary>
    /// Fail closed unless the SUBACK is exactly one entry for the requested NCMD topic granted at
    /// QoS 1 (a downgrade to QoS 0 or a failure result must prevent NBIRTH).
    /// </summary>
    internal static void RequireExactNcmdGrant(IReadOnlyList<KeyValuePair<string, int>> grants, string expectedTopic)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Count != 1
            || !string.Equals(grants[0].Key, expectedTopic, StringComparison.Ordinal)
            || grants[0].Value != 1)
        {
            var detail = grants.Count == 0 ? "no grant" : $"'{grants[0].Key}' -> QoS {grants[0].Value}";
            throw TransportFailure(
                SparkplugErrors.TransportSubscribeFailed,
                $"the exact NCMD SUBSCRIBE ('{expectedTopic}') must be granted QoS 1 (was {detail}).");
        }
    }

    /// <summary>Build a stable, secret-free network <see cref="AdapterException"/> for a transport failure.</summary>
    private static AdapterException TransportFailure(string code, string message) =>
        new(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Network,
            Message = message,
            Retryable = false,
        });

    private static int MapGrantedQos(MqttClientSubscribeResultCode code) => code switch
    {
        MqttClientSubscribeResultCode.GrantedQoS0 => 0,
        MqttClientSubscribeResultCode.GrantedQoS1 => 1,
        MqttClientSubscribeResultCode.GrantedQoS2 => 2,
        _ => -1, // any failure/unspecified result
    };
}
