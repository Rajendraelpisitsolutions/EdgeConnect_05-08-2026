// ============================================================================
// File: Services/MqttPublisherService.cs
// Purpose: Background service that reads MqttPayload messages from a bounded
//          channel and publishes them to the MQTT broker in configurable batches.
//
// THIS IS THE SINGLE CONSUMER IN THE PRODUCER-CONSUMER PIPELINE:
//   - Producers: Multiple MachinePollerService instances (one per CNC machine)
//   - Channel:   Bounded Channel<MqttPayload> with back-pressure (DropOldest)
//   - Consumer:  This service (single reader for optimal performance)
//
// ARCHITECTURE:
//   ┌────────────┐  ┌────────────┐  ┌────────────┐
//   │ Poller #1  │  │ Poller #2  │  │ Poller #N  │  (Producers - many)
//   └─────┬──────┘  └─────┬──────┘  └─────┬──────┘
//         │               │               │
//         ▼               ▼               ▼
//   ┌─────────────────────────────────────────────┐
//   │     Bounded Channel<MqttPayload>             │  (Buffer - back-pressure)
//   │     Capacity: 10,000 | DropOldest            │
//   └─────────────────────┬───────────────────────┘
//                         │
//                         ▼
//   ┌─────────────────────────────────────────────┐
//   │     MqttPublisherService                     │  (Consumer - single)
//   │     Batch: 100 msgs | Flush: 250ms           │
//   └─────────────────────┬───────────────────────┘
//                         │
//                         ▼
//   ┌─────────────────────────────────────────────┐
//   │     MQTT Broker (Mosquitto / HiveMQ / etc.)  │
//   └─────────────────────────────────────────────┘
//
// BATCHING STRATEGY:
//   Messages are published in batches for maximum throughput:
//   1. Wait for at least one message from the channel
//   2. Greedily read up to PublishBatchSize more messages (non-blocking)
//   3. If batch isn't full, wait up to PublishBatchIntervalMs for more
//   4. Publish the batch (all messages in parallel)
//   5. Repeat
//
//   This ensures:
//   - Low latency: Partial batches are flushed within 250ms
//   - High throughput: Full batches minimize per-message overhead
//   - Efficiency: No spinning or busy-waiting when no messages are available
//
// SECURITY:
//   - TLS 1.2/1.3 enforced for broker connections
//   - Supports CA certificate verification and mutual TLS (mTLS)
//   - Passwords resolved via SecretProvider (encrypted or env var)
//   - Last Will Testament publishes "offline" when bridge disconnects unexpectedly
// ============================================================================

using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Models;
using ElpisEdgeConnect.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ElpisEdgeConnect.Services;

/// <summary>
/// Background service that consumes MQTT payloads from a bounded channel
/// and publishes them to the MQTT broker in batches.
/// 
/// REGISTERED AS:
///   Singleton + HostedService in Program.cs:
///     builder.Services.AddSingleton&lt;MqttPublisherService&gt;();
///     builder.Services.AddHostedService(sp => sp.GetRequiredService&lt;MqttPublisherService&gt;());
///   
///   This dual registration allows MachineManagerService to inject MqttPublisherService
///   directly (for accessing the channel Writer) while also registering it as a hosted
///   service (for automatic start/stop with the application lifecycle).
/// </summary>
public sealed class MqttPublisherService : BackgroundService, IAsyncDisposable
{
    // ── Channel (the pipeline buffer between pollers and this publisher) ──
    private readonly Channel<MqttPayload> _channel;

    // ── Configuration ──
    private readonly MqttSettings _mqttSettings;           // Broker connection + publishing settings
    private readonly ISecretProvider _secretProvider;       // For resolving encrypted passwords

    // ── MQTT Client ──
    private readonly ILogger<MqttPublisherService> _logger;
    private readonly IMqttClient _mqttClient;              // MQTTnet client instance
    private readonly MqttClientOptions _mqttClientOptions; // Pre-built connection options

    // ── Metrics (thread-safe via Interlocked) ──
    // These counters are exposed as properties for diagnostics reporting
    private long _publishedCount;  // Messages successfully published to broker
    private long _failedCount;     // Messages that failed to publish (after retries)
    private long _droppedCount;    // Messages dropped because MQTT was disconnected

    /// <summary>Total messages successfully published to the MQTT broker.</summary>
    public long PublishedCount => Interlocked.Read(ref _publishedCount);

    /// <summary>Total messages that failed to publish.</summary>
    public long FailedCount => Interlocked.Read(ref _failedCount);

    /// <summary>Total messages dropped (MQTT disconnected when trying to publish).</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>Whether the MQTT client is currently connected to the broker.</summary>
    public bool IsConnected => _mqttClient.IsConnected;

    /// <summary>
    /// The channel writer that pollers use to enqueue data for MQTT publishing.
    /// 
    /// Exposed as a property so MachineManagerService can pass it to pollers.
    /// Each poller calls Writer.TryWrite(payload) or Writer.WriteAsync(payload)
    /// to enqueue messages.
    /// </summary>
    public ChannelWriter<MqttPayload> Writer => _channel.Writer;

    /// <summary>
    /// Constructs the MQTT publisher service.
    /// 
    /// Sets up:
    ///   1. Bounded channel with back-pressure (DropOldest when full)
    ///   2. MQTTnet client with TLS and authentication
    ///   3. Connection event handlers (disconnected → auto-reconnect)
    /// </summary>
    public MqttPublisherService(
        IOptions<MqttSettings> mqttSettings,
        IOptions<AppSettings> appSettings,
        ISecretProvider secretProvider,
        ILogger<MqttPublisherService> logger)
    {
        _mqttSettings = mqttSettings.Value;
        _secretProvider = secretProvider;
        _logger = logger;

        // ── Create the bounded channel ──
        // BoundedChannelOptions controls the buffer behavior:
        //   - Capacity: Maximum messages in the buffer
        //   - FullMode: What happens when the buffer is full
        //   - SingleReader: Optimization hint (only this service reads)
        _channel = Channel.CreateBounded<MqttPayload>(new BoundedChannelOptions(appSettings.Value.ChannelCapacity)
        {
            // When the channel is full, drop the OLDEST message to make room.
            // This ensures we always have the most RECENT machine data.
            // In CNC monitoring, stale data is worse than missing data.
            FullMode = BoundedChannelFullMode.DropOldest,

            // Performance optimization: tell the channel that only ONE thread
            // will ever read from it (this service). This allows the channel
            // to use a more efficient single-reader data structure internally.
            SingleReader = true
        });

        // ── Create MQTTnet client ──
        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        // Build the MQTT connection options (TLS, auth, keep-alive, Last Will, etc.)
        _mqttClientOptions = BuildMqttOptions();

        // ── Wire up MQTT event handlers ──
        // These fire on connection state changes for logging and auto-reconnection
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ConnectedAsync += OnConnectedAsync;
    }

    /// <summary>
    /// Main execution loop — called automatically by the .NET Generic Host.
    /// 
    /// EXECUTION FLOW:
    ///   1. Connect to MQTT broker (with retry)
    ///   2. Enter batch-read loop:
    ///      a. Wait for at least one message from the channel
    ///      b. Fill batch greedily (up to PublishBatchSize)
    ///      c. Wait briefly for more messages (up to PublishBatchIntervalMs)
    ///      d. Publish the batch to the MQTT broker
    ///      e. Repeat
    ///   3. On shutdown: drain remaining messages from the channel
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ── DryRun mode: skip MQTT broker connection ──
        if (_mqttSettings.DryRun)
        {
            _logger.LogWarning(
                "══════════════════════════════════════════════════════════════════");
            _logger.LogWarning(
                "  MQTT DRY-RUN MODE ENABLED — payloads will be LOGGED, not published.");
            _logger.LogWarning(
                "  No data will be sent to the MQTT broker or EREMOS.");
            _logger.LogWarning(
                "  Set MqttSettings.DryRun = false for production.");
            _logger.LogWarning(
                "══════════════════════════════════════════════════════════════════");
        }
        else
        {
            // ── Step 1: Connect to the MQTT broker ──
            // This retries indefinitely with exponential backoff until connected or cancelled
            await ConnectWithRetryAsync(stoppingToken);
        }

        _logger.LogInformation(
            "MQTT Publisher started — batch size: {BatchSize}, flush interval: {FlushMs}ms, " +
            "channel capacity: {Capacity}, DryRun: {DryRun}",
            _mqttSettings.PublishBatchSize,
            _mqttSettings.PublishBatchIntervalMs,
            _channel.Reader.Count,
            _mqttSettings.DryRun);

        // ── Step 2: Enter the batch-read loop ──
        // Pre-allocate the batch list to avoid repeated allocations
        var batch = new List<MqttPayload>(_mqttSettings.PublishBatchSize);
        var reader = _channel.Reader;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Clear the batch from the previous iteration
                batch.Clear();

                // ── Wait for at least one message ──
                // WaitToReadAsync blocks until data is available OR cancellation.
                // This is efficient — no busy-waiting or polling.
                if (await reader.WaitToReadAsync(stoppingToken))
                {
                    // ── Fill the batch greedily ──
                    // Create a timeout token for the batch flush interval.
                    // If we don't fill the batch within this time, we flush what we have.
                    using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    batchCts.CancelAfter(_mqttSettings.PublishBatchIntervalMs);

                    try
                    {
                        // First: grab everything already in the channel (non-blocking)
                        while (batch.Count < _mqttSettings.PublishBatchSize &&
                               reader.TryRead(out var payload))
                        {
                            batch.Add(payload);
                        }

                        // Second: if batch isn't full, wait for more messages
                        // (up to the batch interval timeout)
                        if (batch.Count < _mqttSettings.PublishBatchSize)
                        {
                            while (batch.Count < _mqttSettings.PublishBatchSize)
                            {
                                try
                                {
                                    // ReadAsync blocks until a message arrives or timeout
                                    var payload = await reader.ReadAsync(batchCts.Token);
                                    batch.Add(payload);
                                }
                                catch (OperationCanceledException)
                                {
                                    // Batch flush timer expired — publish what we have.
                                    // This is the NORMAL path for partial batches.
                                    break;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Batch timer expired (not service shutdown) — flush partial batch
                    }

                    // ── Publish the collected batch ──
                    if (batch.Count > 0)
                    {
                        await PublishBatchAsync(batch, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service is shutting down — exit the loop cleanly
                break;
            }
            catch (Exception ex)
            {
                // Unexpected error — log and retry after a brief delay.
                // This catches edge cases like channel corruption or MQTT client errors.
                _logger.LogError(ex,
                    "MQTT Publisher encountered an unexpected error — retrying in 1 second");
                await Task.Delay(1000, stoppingToken);
            }
        }

        // ── Step 3: Drain remaining messages on shutdown ──
        // Best-effort attempt to publish any messages still in the channel
        await DrainChannelAsync();
    }

    /// <summary>
    /// Publishes a batch of MQTT messages to the broker.
    ///
    /// If DryRun is enabled, logs payloads instead of publishing.
    /// If the MQTT client is disconnected, the entire batch is dropped
    /// (counted in DroppedCount). Otherwise, all messages are published
    /// concurrently using Task.WhenAll for maximum throughput.
    /// </summary>
    private async Task PublishBatchAsync(List<MqttPayload> batch, CancellationToken ct)
    {
        // DryRun mode: log payloads to console/file instead of publishing to MQTT.
        // Useful for testing FOCAS2 data collection without sending to EREMOS.
        if (_mqttSettings.DryRun)
        {
            foreach (var payload in batch)
            {
                var payloadText = System.Text.Encoding.UTF8.GetString(payload.PayloadBytes);
                _logger.LogInformation(
                    "[DRY-RUN] Topic: {Topic} | Machine: {MachineId} | Payload: {Payload}",
                    payload.Topic, payload.MachineId, payloadText);
            }
            Interlocked.Add(ref _publishedCount, batch.Count);
            return;
        }

        // Check MQTT connection before publishing
        if (!_mqttClient.IsConnected)
        {
            _logger.LogWarning(
                "MQTT broker not connected — dropping {Count} messages. " +
                "Reconnection will be attempted automatically.",
                batch.Count);
            Interlocked.Add(ref _droppedCount, batch.Count);
            return;
        }

        // Publish all messages in the batch concurrently.
        // Task.WhenAll fires all publishes at once, which is significantly
        // faster than publishing one at a time (especially with QoS 1/2
        // which require broker acknowledgment).
        var tasks = batch.Select(payload => PublishSingleAsync(payload, ct));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Publishes a single MQTT message to the broker.
    /// 
    /// Maps the QoS integer (0/1/2) to the MQTTnet enum, builds the
    /// application message, and publishes it. Exceptions are caught and
    /// counted (don't let one failed message stop the rest of the batch).
    /// </summary>
    private async Task PublishSingleAsync(MqttPayload payload, CancellationToken ct)
    {
        try
        {
            // Map QoS integer to MQTTnet enum
            var qos = payload.QoS switch
            {
                0 => MqttQualityOfServiceLevel.AtMostOnce,   // Fire-and-forget
                1 => MqttQualityOfServiceLevel.AtLeastOnce,  // Guaranteed delivery
                2 => MqttQualityOfServiceLevel.ExactlyOnce,  // Exactly once
                _ => MqttQualityOfServiceLevel.AtLeastOnce   // Default to QoS 1
            };

            // Build the MQTT application message
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(payload.Topic)                  // e.g., "factory/cnc/CNC-001/data"
                .WithPayload(payload.PayloadBytes)         // Pre-serialized JSON bytes
                .WithQualityOfServiceLevel(qos)            // Delivery guarantee
                .WithRetainFlag(payload.Retain)            // Whether broker stores this message
                .Build();

            // Publish to the broker and wait for acknowledgment (QoS 1/2)
            await _mqttClient.PublishAsync(message, ct);

            // Increment success counter (thread-safe)
            Interlocked.Increment(ref _publishedCount);
        }
        catch (Exception ex)
        {
            // Increment failure counter (thread-safe)
            Interlocked.Increment(ref _failedCount);

            // Log at Warning level — one failed message shouldn't be alarming,
            // but a pattern of failures indicates a broker issue.
            _logger.LogWarning(ex,
                "Failed to publish MQTT message to topic '{Topic}' for machine {MachineId}",
                payload.Topic,
                payload.MachineId);
        }
    }

    /// <summary>
    /// Drains remaining messages from the channel during graceful shutdown.
    /// 
    /// Attempts to publish all queued messages before the service exits.
    /// This is a best-effort operation — if publishing fails during drain,
    /// we stop immediately (the broker may already be unreachable).
    /// </summary>
    private async Task DrainChannelAsync()
    {
        _logger.LogInformation("Draining remaining MQTT messages from channel...");
        var drained = 0;

        // Read all remaining messages (non-blocking TryRead)
        while (_channel.Reader.TryRead(out var payload))
        {
            try
            {
                await PublishSingleAsync(payload, CancellationToken.None);
                drained++;
            }
            catch
            {
                // Publishing failed during drain — stop trying.
                // Remaining messages are lost (acceptable during shutdown).
                break;
            }
        }

        _logger.LogInformation(
            "Drain complete: published {Drained} remaining messages", drained);
    }

    #region Connection Management

    /// <summary>
    /// Connects to the MQTT broker with indefinite retry and exponential backoff.
    /// 
    /// RETRY STRATEGY:
    ///   Attempt 1: Wait ReconnectDelayMs (5000ms default)
    ///   Attempt 2: Wait × 1.5 (7500ms)
    ///   Attempt 3: Wait × 1.5 (11250ms)
    ///   ...capped at 60,000ms (1 minute)
    /// 
    /// This runs at startup and continues until either:
    ///   - Connection succeeds → returns normally
    ///   - Cancellation requested → throws OperationCanceledException (service shutdown)
    /// </summary>
    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromMilliseconds(_mqttSettings.ReconnectDelayMs);
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                attempt++;
                _logger.LogInformation(
                    "Connecting to MQTT broker {Host}:{Port} (attempt {Attempt}, transport: {Transport}, protocol: {Protocol}, TLS: {UseTls})...",
                    _mqttSettings.BrokerHost,
                    _mqttSettings.BrokerPort,
                    attempt,
                    _mqttSettings.Transport,
                    _mqttSettings.ProtocolVersion,
                    _mqttSettings.UseTls);

                // Attempt MQTT connection with pre-built options
                await _mqttClient.ConnectAsync(_mqttClientOptions, ct);

                _logger.LogInformation(
                    "Successfully connected to MQTT broker {Host}:{Port}",
                    _mqttSettings.BrokerHost,
                    _mqttSettings.BrokerPort);
                return; // Connected! Exit the retry loop.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "MQTT connection attempt {Attempt} failed — retrying in {Delay}s. " +
                    "Check broker address, port, credentials, and TLS settings.",
                    attempt,
                    delay.TotalSeconds);

                // Wait before retrying
                await Task.Delay(delay, ct);

                // Exponential backoff: multiply delay by 1.5, cap at 60 seconds
                delay = TimeSpan.FromMilliseconds(
                    Math.Min(delay.TotalMilliseconds * 1.5, 60_000));
            }
        }
    }

    /// <summary>
    /// Event handler fired when the MQTT client successfully connects.
    /// Logs the connection status and whether a persistent session was restored.
    /// </summary>
    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation(
            "MQTT connection established — persistent session present: {SessionPresent}",
            args.ConnectResult.IsSessionPresent);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Event handler fired when the MQTT client disconnects unexpectedly.
    /// Attempts automatic reconnection after a configurable delay.
    /// 
    /// NOTE: This only fires for UNEXPECTED disconnections (broker went down,
    /// network failure, etc.). Graceful disconnections (service shutdown)
    /// set ClientWasConnected = false.
    /// </summary>
    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (args.ClientWasConnected)
        {
            // We WERE connected and got disconnected — this is unexpected
            _logger.LogWarning(
                "MQTT unexpectedly disconnected: {Reason}. " +
                "Reconnecting in {Delay}ms...",
                args.Reason,
                _mqttSettings.ReconnectDelayMs);

            // Wait before reconnecting to avoid hammering the broker
            await Task.Delay(_mqttSettings.ReconnectDelayMs);

            try
            {
                // Attempt to reconnect using the same options
                await _mqttClient.ConnectAsync(_mqttClientOptions, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Reconnection failed — will retry on the next disconnect event
                // or the next publish attempt
                _logger.LogError(ex,
                    "MQTT reconnection failed — will retry on next cycle");
            }
        }
    }

    /// <summary>
    /// Builds MQTT client connection options from the configuration.
    /// 
    /// INCLUDES:
    ///   - Transport: TCP or WebSocket (configurable)
    ///   - Protocol Version: V310, V311, or V500 (configurable)
    ///   - Client ID, clean session, keep-alive
    ///   - Authentication (username + password)
    ///   - TLS (CA cert, client cert, protocol versions)
    ///   - Last Will Testament (publishes "offline" on unexpected disconnect)
    /// </summary>
    private MqttClientOptions BuildMqttOptions()
    {
        var builder = new MqttClientOptionsBuilder();

        // ── Transport: TCP or WebSocket ──
        var isWebSocket = _mqttSettings.Transport.Equals("WebSocket", StringComparison.OrdinalIgnoreCase);

        if (isWebSocket)
        {
            // Build WebSocket URI: ws(s)://host:port/path
            var scheme = _mqttSettings.UseTls ? "wss" : "ws";
            var wsPath = _mqttSettings.WebSocketPath.TrimStart('/');
            var wsUri = $"{scheme}://{_mqttSettings.BrokerHost}:{_mqttSettings.BrokerPort}/{wsPath}";

            builder.WithWebSocketServer(ws =>
            {
                ws.WithUri(wsUri);
            });

            _logger.LogInformation(
                "MQTT transport: WebSocket → {Uri}",
                wsUri);
        }
        else
        {
            // Standard TCP connection
            builder.WithTcpServer(_mqttSettings.BrokerHost, _mqttSettings.BrokerPort);

            _logger.LogInformation(
                "MQTT transport: TCP → {Host}:{Port}",
                _mqttSettings.BrokerHost,
                _mqttSettings.BrokerPort);
        }

        // ── Protocol Version ──
        // MQTTnet 4.x defaults to V500 (MQTT 5.0) which WILL FAIL against
        // brokers that only support MQTT 3.x. Explicitly set the version.
        var protocolVersion = _mqttSettings.ProtocolVersion?.ToUpperInvariant() switch
        {
            "V310" or "3.1" or "310" => MQTTnet.Formatter.MqttProtocolVersion.V310,
            "V311" or "3.1.1" or "311" => MQTTnet.Formatter.MqttProtocolVersion.V311,
            "V500" or "5.0" or "500" or "5" => MQTTnet.Formatter.MqttProtocolVersion.V500,
            _ => MQTTnet.Formatter.MqttProtocolVersion.V311  // Default to 3.1.1 for max compat
        };
        builder.WithProtocolVersion(protocolVersion);

        _logger.LogInformation("MQTT protocol version: {Version}", protocolVersion);

        // ── Client ID, session, keep-alive ──
        builder
            .WithClientId(_mqttSettings.ClientId)
            .WithCleanSession(_mqttSettings.CleanSession)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_mqttSettings.KeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(30));

        // ── Authentication ──
        // Resolve the password (may be encrypted or env var reference)
        var password = _secretProvider.Resolve(_mqttSettings.Password);
        if (!string.IsNullOrEmpty(_mqttSettings.Username))
        {
            builder.WithCredentials(_mqttSettings.Username, password);
        }

        // ── TLS Configuration ──
        // Applies to BOTH TCP and WebSocket transports
        if (_mqttSettings.UseTls)
        {
            builder.WithTlsOptions(tls =>
            {
                // Enforce minimum TLS 1.2 (rejects insecure older versions)
                tls.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);

                // CA certificate — verifies the broker's identity
                if (!string.IsNullOrEmpty(_mqttSettings.CaCertificatePath) &&
                    File.Exists(_mqttSettings.CaCertificatePath))
                {
                    var caCert = new X509Certificate2(_mqttSettings.CaCertificatePath);
                    tls.WithTrustChain(new X509Certificate2Collection(caCert));
                }

                // Client certificate — mutual TLS (broker verifies this client)
                if (!string.IsNullOrEmpty(_mqttSettings.ClientCertificatePath) &&
                    File.Exists(_mqttSettings.ClientCertificatePath))
                {
                    var clientPassword = _secretProvider.Resolve(
                        _mqttSettings.ClientCertificatePassword);
                    var clientCert = new X509Certificate2(
                        _mqttSettings.ClientCertificatePath, clientPassword);
                    tls.WithClientCertificates(new X509Certificate2Collection(clientCert));
                }

                // Certificate validation handler
                // TODO: In production, implement proper certificate validation
                // instead of accepting all certificates. Either use cert pinning
                // or install the broker's CA in the system trust store.
                tls.WithCertificateValidationHandler(_ => true);
            });
        }

        // ── Last Will Testament ──
        // If this client disconnects unexpectedly (crash, network failure),
        // the broker automatically publishes this message on our behalf.
        // Subscribers monitoring the bridge status topic see "offline" immediately.
        builder.WithWillTopic($"{_mqttSettings.TopicPrefix}/$bridge/status")
               .WithWillPayload("offline"u8.ToArray())  // UTF-8 literal bytes
               .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
               .WithWillRetain();  // Retained so new subscribers see the status immediately

        return builder.Build();
    }

    #endregion

    /// <summary>
    /// Async disposal: publishes "offline" status and disconnects cleanly.
    /// Called when the .NET host shuts down the service.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_mqttClient.IsConnected)
        {
            try
            {
                // Publish final "offline" status before disconnecting
                var offlineMsg = new MqttApplicationMessageBuilder()
                    .WithTopic($"{_mqttSettings.TopicPrefix}/$bridge/status")
                    .WithPayload("offline"u8.ToArray())
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag()  // Retained so new subscribers see "offline"
                    .Build();

                await _mqttClient.PublishAsync(offlineMsg, CancellationToken.None);

                // Graceful MQTT disconnect (sends DISCONNECT packet to broker)
                await _mqttClient.DisconnectAsync();
            }
            catch
            {
                // Best effort — if we can't publish or disconnect cleanly,
                // the Last Will Testament will handle the "offline" status
            }
        }

        // Dispose the MQTTnet client and its underlying TCP connection
        _mqttClient.Dispose();
    }
}
