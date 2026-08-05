// ============================================================================
// File: MqttSinkAdapter.cs
// Purpose: ISinkAdapter implementation for MQTT brokers. Supports two
//          publish strategies: Batch (JSON array per batch) and PerTag
//          (raw scalar per point for EREMOS V2 compatibility).
//
//          Lifecycle: Created → Initialized → Running ↔ Degraded → Stopped
//          Reconnect: exponential backoff behind _connectLock; suppressed
//          after intentional Stop.
//
// Reference: PHASE2_ENTRY.md, MQTT sink adapter plan
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// MQTT sink adapter. Owns an <see cref="IMqttClient"/>, the connection
/// lifecycle, reconnect loop, and publish dispatch. The routing engine
/// calls <see cref="PublishAsync"/> with batches of canonical points.
/// </summary>
public sealed class MqttSinkAdapter : ISinkAdapter
{
    private readonly ILogger<MqttSinkAdapter> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private MqttSinkConfiguration _config = null!;
    private IMqttClient _mqttClient = null!;
    private MqttClientOptions _mqttClientOptions = null!;
    private CancellationTokenSource? _adapterCts;

    // Health counters (thread-safe via Interlocked).
    private long _publishAttempts;
    private long _publishSuccesses;
    private long _publishFailures;
    private long _reconnectAttempts;
    private int _consecutiveFailures;
    private DateTime? _lastSuccessAt;
    private DateTime? _lastFailureAt;
    private DateTime? _lastConnectAt;
    private AdapterError? _lastError;

    /// <summary>Construct the adapter with its stable identity.</summary>
    public MqttSinkAdapter(string instanceId, ILogger<MqttSinkAdapter> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(instanceId);
        ArgumentNullException.ThrowIfNull(logger);
        InstanceId = instanceId;
        _logger = logger;
    }

    /// <summary>
    /// Test-only constructor that accepts an injected <see cref="IMqttClient"/>
    /// so tests can connect to an in-process MQTTnet server without real TCP.
    /// </summary>
    internal MqttSinkAdapter(string instanceId, ILogger<MqttSinkAdapter> logger, IMqttClient mqttClient)
        : this(instanceId, logger)
    {
        _mqttClient = mqttClient;
    }

    /// <inheritdoc/>
    public string InstanceId { get; }

    /// <inheritdoc/>
    public string ProtocolName => "mqtt";

    /// <inheritdoc/>
    public SinkCapabilities Capabilities => SinkCapabilities.Push | SinkCapabilities.Batch;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // ----- Lifecycle -----

    /// <inheritdoc/>
    public Task InitializeAsync(SinkConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        TransitionState(AdapterState.Initializing);

        if (config is not MqttSinkConfiguration mqttConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"MqttSinkAdapter requires MqttSinkConfiguration, got {config.GetType().Name}.");
        }

        _config = mqttConfig;
        _adapterCts = new CancellationTokenSource();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_config.BrokerHost, _config.BrokerPort)
            .WithClientId(_config.ClientId ?? $"edgeconnect-{InstanceId}")
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_config.KeepAliveSeconds))
            // Bound the CONNECT/CONNACK handshake — MQTTnet 4.x defaults to
            // no timeout on this round-trip, so a non-responsive broker
            // hangs the host's startup forever. 15s is generous for a LAN
            // and still fast enough to fail before the user gives up.
            .WithTimeout(TimeSpan.FromSeconds(15));

        if (!string.IsNullOrEmpty(_config.Username))
        {
            builder.WithCredentials(_config.Username, _config.Password);
        }

        if (_config.UseTls)
        {
            builder.WithTlsOptions(o => o.UseTls());
        }

        _mqttClientOptions = builder.Build();

        if (_mqttClient is null)
        {
            _mqttClient = new MqttFactory().CreateMqttClient();
        }

        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;

        TransitionState(AdapterState.Initialized);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Startup performs a SINGLE bounded connect attempt — it must never block.
    /// <para>
    /// <c>HostStartup</c> awaits the sink supervisor BEFORE the source supervisor,
    /// and <c>SinkSupervisor</c>'s per-adapter isolation is exception-based: a
    /// method that <i>hangs</i> rather than throws bypasses it entirely. An
    /// unbounded connect-retry here therefore stalled every source on the
    /// gateway whenever the broker was unreachable — a breach of locked decision
    /// #10 (per-adapter isolation).
    /// </para>
    /// <para>
    /// So a failed initial connect degrades THIS sink only and hands off to the
    /// background reconnect loop; sources start and buffer normally.
    /// </para>
    /// </remarks>
    public async Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);
        Interlocked.Increment(ref _reconnectAttempts);
        try
        {
            await _mqttClient.ConnectAsync(_mqttClientOptions, ct).ConfigureAwait(false);
            _lastConnectAt = DateTime.UtcNow;
            TransitionState(AdapterState.Running);
            _logger.LogInformation("MqttSinkAdapter '{Id}' connected to {Host}:{Port}.",
                InstanceId, _config.BrokerHost, _config.BrokerPort);
        }
        catch (OperationCanceledException)
        {
            // Shutdown raced the connect — a genuine start failure.
            TransitionState(AdapterState.Failed);
            throw;
        }
        catch (Exception ex)
        {
            // Broker unreachable at start: degrade and keep retrying in the
            // background instead of failing (and blocking) host startup.
            // Starting -> Degraded is not a legal transition, so pass through
            // Running — see AdapterStateTransitions.
            TransitionState(AdapterState.Running);
            TransitionState(AdapterState.Degraded);
            _lastError = MakeError(
                "MQTT.CONNECT_FAILED",
                ErrorCategory.Network,
                $"Initial connect to {_config.BrokerHost}:{_config.BrokerPort} failed: {ex.Message}. "
                    + "Retrying in the background; sources are unaffected.",
                retryable: true);
            _lastFailureAt = DateTime.UtcNow;
            _logger.LogWarning(ex,
                "MqttSinkAdapter '{Id}' could not connect to {Host}:{Port} at start — "
                    + "starting Degraded and retrying in the background.",
                InstanceId, _config.BrokerHost, _config.BrokerPort);
            StartBackgroundReconnect();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Stopping)
        {
            return;
        }
        TransitionState(AdapterState.Stopping);
        try
        {
            _adapterCts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        if (_mqttClient.IsConnected)
        {
            try
            {
                await _mqttClient.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder().Build(), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MqttSinkAdapter '{Id}' disconnect threw.", InstanceId);
            }
        }

        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("MqttSinkAdapter '{Id}' stopped.", InstanceId);
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var level = State switch
        {
            AdapterState.Running when _lastError is null => HealthLevel.Healthy,
            AdapterState.Running => HealthLevel.Degraded,
            AdapterState.Degraded => HealthLevel.Degraded,
            AdapterState.Failed => HealthLevel.Unhealthy,
            _ => HealthLevel.Unknown,
        };

        var metrics = new Dictionary<string, object>
        {
            ["publishAttempts"] = Interlocked.Read(ref _publishAttempts),
            ["publishSuccesses"] = Interlocked.Read(ref _publishSuccesses),
            ["publishFailures"] = Interlocked.Read(ref _publishFailures),
            ["consecutiveFailures"] = _consecutiveFailures,
            ["isConnected"] = _mqttClient?.IsConnected ?? false,
            ["reconnectAttempts"] = Interlocked.Read(ref _reconnectAttempts),
            ["publishMode"] = _config?.PublishMode.ToString() ?? "unknown",
            ["brokerEndpoint"] = _config is not null ? $"{_config.BrokerHost}:{_config.BrokerPort}" : "unknown",
        };
        if (_lastConnectAt.HasValue)
        {
            metrics["lastConnectAtUtc"] = _lastConnectAt.Value.ToString("O");
        }
        if (_lastSuccessAt.HasValue)
        {
            metrics["lastSuccessfulPublishAtUtc"] = _lastSuccessAt.Value.ToString("O");
        }
        if (_lastFailureAt.HasValue)
        {
            metrics["lastFailureAtUtc"] = _lastFailureAt.Value.ToString("O");
        }

        return Task.FromResult(new AdapterHealth
        {
            State = State,
            Level = level,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastSuccessAt,
            LastError = _lastError,
            Metrics = metrics,
        });
    }

    // ----- Publish -----

    /// <inheritdoc/>
    public async Task<PublishResult> PublishAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return PublishResult.Successful(0, TimeSpan.Zero);
        }

        Interlocked.Increment(ref _publishAttempts);
        var sw = Stopwatch.StartNew();

        if (!_mqttClient.IsConnected)
        {
            var err = MakeError("MQTT.NOT_CONNECTED", ErrorCategory.Network,
                "MQTT client is not connected to the broker.", retryable: true);
            RecordFailure(err);
            return PublishResult.Failed(err, sw.Elapsed);
        }

        try
        {
            return _config.PublishMode == MqttPublishMode.PerTag
                ? await PublishPerTagAsync(points, sw, ct).ConfigureAwait(false)
                : await PublishBatchAsync(points, sw, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Caller cancellation propagates.
        }
        catch (Exception ex)
        {
            var err = MakeError("MQTT.PUBLISH_FAILED", ErrorCategory.Network,
                $"Publish failed: {ex.Message}", retryable: true);
            RecordFailure(err);
            return PublishResult.Failed(err, sw.Elapsed);
        }
    }

    private async Task<PublishResult> PublishBatchAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        Stopwatch sw,
        CancellationToken ct)
    {
        var topic = MqttTopicResolver.Resolve(
            _config.TopicTemplate,
            gatewayId: points[0].GatewayId,
            sourceId: points[0].SourceInstanceId,
            deviceClass: ExtractDeviceClass(points[0]));

        var payload = MqttPayloadSerializer.SerializeBatch(points);

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MapQos(_config.QosLevel))
            .WithRetainFlag(_config.RetainDefault)
            .Build();

        await _mqttClient.PublishAsync(message, ct).ConfigureAwait(false);
        RecordSuccess();
        return PublishResult.Successful(points.Count, sw.Elapsed);
    }

    private async Task<PublishResult> PublishPerTagAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        Stopwatch sw,
        CancellationToken ct)
    {
        var accepted = 0;
        var rejected = 0;
        AdapterError? lastPointError = null;

        foreach (var point in points)
        {
            // Format the raw value. Null return = unsupported type (byte[]).
            var rawValue = RawValueFormatter.Format(point.Value, point.ValueType);
            if (rawValue is null)
            {
                rejected++;
                _logger.LogWarning(
                    "PerTag mode does not support binary values; point '{Tag}' rejected.",
                    point.TagName);
                continue;
            }

            var topic = MqttTopicResolver.Resolve(
                _config.PerTagTopicTemplate,
                gatewayId: point.GatewayId,
                sourceId: point.SourceInstanceId,
                tagName: point.TagName,
                deviceClass: ExtractDeviceClass(point));

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(rawValue))
                .WithQualityOfServiceLevel(MapQos(_config.QosLevel))
                .WithRetainFlag(_config.RetainDefault)
                .Build();

            try
            {
                await _mqttClient.PublishAsync(message, ct).ConfigureAwait(false);
                accepted++;
            }
            catch (OperationCanceledException)
            {
                throw; // Caller cancellation always propagates.
            }
            catch (Exception ex)
            {
                rejected++;
                lastPointError = MakeError("MQTT.PERTAG_PUBLISH_FAILED", ErrorCategory.Network,
                    $"PerTag publish failed for tag '{point.TagName}': {ex.Message}", retryable: true);
                _logger.LogWarning(ex,
                    "PerTag publish failed for tag '{Tag}' on topic '{Topic}'.",
                    point.TagName, topic);
                // First failure does NOT abort remaining points.
            }
        }

        // PublishResult contract (Adapters/PublishResult.cs):
        //   "Success = true if the ENTIRE batch was accepted by the sink."
        // Therefore Success is true ONLY when rejected == 0. Partial-success
        // (some accepted, some rejected) returns Success=false so the routing
        // engine's SinkPublisher does NOT advance the buffer cursor — the
        // batch is held and retried on the next dequeue. Idempotent PerTag
        // topics (one tag per topic, latest-wins) make the resulting
        // duplicate publishes for the previously-accepted points harmless.
        //
        // This closes the data-loss window observed during the 5-min S&F
        // pilot smoke against 20.197.8.189:1883 — a 30s wifi blip caused
        // 9 publishes to hit the wire AFTER the broker dropped the TCP
        // connection but BEFORE MQTTnet declared the client offline. With
        // partial-success returning Success=true, those 9 points were
        // silently lost; with Success=false, they sit on the buffer until
        // the sink reconnects and the batch retries.
        if (rejected == 0 && accepted > 0)
        {
            RecordSuccess();
            return new PublishResult
            {
                Success = true,
                AcceptedCount = accepted,
                RejectedCount = 0,
                Latency = sw.Elapsed,
            };
        }

        var error = lastPointError ?? MakeError("MQTT.PUBLISH_FAILED", ErrorCategory.Network,
            "All points in the batch failed to publish.", retryable: true);
        RecordFailure(error);
        // Do NOT use PublishResult.Failed() — it hardcodes RejectedCount=0.
        // We need to carry the actual rejected count from the per-point loop.
        return new PublishResult
        {
            Success = false,
            AcceptedCount = accepted,   // some may have made the wire — surface for observability
            RejectedCount = rejected,
            Error = error,
            Latency = sw.Elapsed,
        };
    }

    /// <inheritdoc/>
    public Task UpdateCurrentValuesAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken ct)
        => throw new NotSupportedException("MQTT sink is push-only; UpdateCurrentValues is not supported.");

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(
        SinkConfiguration config,
        CancellationToken ct)
    {
        if (config is not MqttSinkConfiguration mqttConfig)
        {
            return Task.FromResult(ValidationResult.Failure(
                "MQTT.CONFIG_WRONG_TYPE",
                $"Expected MqttSinkConfiguration, got {config?.GetType().Name ?? "null"}.",
                "config"));
        }

        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(mqttConfig.BrokerHost))
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_MISSING_HOST",
                Message = "BrokerHost is required.",
                Path = "BrokerHost",
            });
        }

        if (mqttConfig.BrokerPort < 1 || mqttConfig.BrokerPort > 65535)
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_INVALID_PORT",
                Message = $"BrokerPort must be 1..65535, got {mqttConfig.BrokerPort}.",
                Path = "BrokerPort",
            });
        }

        if (mqttConfig.QosLevel is not (0 or 1))
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_INVALID_QOS",
                Message = $"QosLevel must be 0 or 1, got {mqttConfig.QosLevel}.",
                Path = "QosLevel",
            });
        }

        var activeTemplate = mqttConfig.PublishMode == MqttPublishMode.PerTag
            ? mqttConfig.PerTagTopicTemplate
            : mqttConfig.TopicTemplate;
        if (string.IsNullOrWhiteSpace(activeTemplate))
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_MISSING_TOPIC",
                Message = "TopicTemplate (or PerTagTopicTemplate for PerTag mode) is required.",
                Path = mqttConfig.PublishMode == MqttPublishMode.PerTag
                    ? "PerTagTopicTemplate" : "TopicTemplate",
            });
        }

        if (mqttConfig.KeepAliveSeconds < 1)
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_INVALID_KEEPALIVE",
                Message = $"KeepAliveSeconds must be >= 1, got {mqttConfig.KeepAliveSeconds}.",
                Path = "KeepAliveSeconds",
            });
        }

        if (mqttConfig.ReconnectDelayMs < 100)
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_INVALID_RECONNECT",
                Message = $"ReconnectDelayMs must be >= 100, got {mqttConfig.ReconnectDelayMs}.",
                Path = "ReconnectDelayMs",
            });
        }

        // Incomplete auth: username without password or vice versa.
        var hasUser = !string.IsNullOrEmpty(mqttConfig.Username);
        var hasPass = !string.IsNullOrEmpty(mqttConfig.Password);
        if (hasUser != hasPass)
        {
            issues.Add(new ValidationIssue
            {
                Code = "MQTT.CONFIG_AUTH_INCOMPLETE",
                Message = "Username and Password must both be set or both be empty.",
                Path = hasUser ? "Password" : "Username",
            });
        }

        if (issues.Count == 0)
        {
            return Task.FromResult(ValidationResult.Success());
        }
        // Return the first validation issue. The ValidationResult API
        // supports a single (code, message, path) triple per call; if
        // multiple issues exist they're all collected above but only the
        // first is surfaced to the caller.
        var first = issues[0];
        return Task.FromResult(ValidationResult.Failure(first.Code, first.Message, first.Path));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Cancel the adapter CTS FIRST so any background reconnect loop
        // observes the cancellation before we try to stop/disconnect.
        try { _adapterCts?.Cancel(); } catch (ObjectDisposedException) { }

        if (State is AdapterState.Running or AdapterState.Degraded or AdapterState.Starting)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopAsync(cts.Token).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }

        if (_mqttClient is not null)
        {
            _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
            _mqttClient.Dispose();
        }
        _connectLock.Dispose();
        _adapterCts?.Dispose();
    }

    // ----- Connect / reconnect -----


    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        // Guard: do NOT reconnect after intentional stop.
        if (State is AdapterState.Stopping or AdapterState.Stopped)
        {
            return Task.CompletedTask;
        }

        if (args.ClientWasConnected && State is AdapterState.Running or AdapterState.Degraded)
        {
            if (State == AdapterState.Running)
            {
                TransitionState(AdapterState.Degraded);
            }

            _logger.LogWarning(
                "MqttSinkAdapter '{Id}' disconnected from broker. Starting background reconnect.",
                InstanceId);

            StartBackgroundReconnect();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Fire-and-forget background reconnect loop, serialized behind
    /// <see cref="_connectLock"/> and backing off up to
    /// <c>MaxReconnectDelayMs</c>. Shared by the disconnect handler and by a
    /// failed initial connect in <see cref="StartAsync"/>, so a broker that is
    /// down at startup recovers by exactly the same path as one that drops later.
    /// </summary>
    private void StartBackgroundReconnect()
    {
        _ = Task.Run(async () =>
        {
            await _connectLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Re-check state after acquiring the lock — stop may have
                // been called while we were waiting.
                if (State is AdapterState.Stopping or AdapterState.Stopped)
                {
                    return;
                }

                var delay = _config.ReconnectDelayMs;
                while (State is AdapterState.Degraded or AdapterState.Running)
                {
                    Interlocked.Increment(ref _reconnectAttempts);
                    try
                    {
                        await _mqttClient.ConnectAsync(_mqttClientOptions, _adapterCts?.Token ?? CancellationToken.None)
                            .ConfigureAwait(false);
                        _lastConnectAt = DateTime.UtcNow;
                        _logger.LogInformation("MqttSinkAdapter '{Id}' reconnected.", InstanceId);

                        // A restored connection IS recovery. Previously the
                        // adapter only left Degraded once a publish succeeded,
                        // because _consecutiveFailures is reset in RecordSuccess
                        // — every failed publish during the outage had already
                        // incremented it. The operator was left staring at a
                        // stale error on a healthy connection.
                        _consecutiveFailures = 0;
                        _lastError = null;
                        if (State == AdapterState.Degraded)
                        {
                            TransitionState(AdapterState.Running);
                        }
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        return; // Stop was called.
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "MqttSinkAdapter '{Id}' reconnect failed; retrying in {Delay}ms.",
                            InstanceId, delay);
                        try
                        {
                            await Task.Delay(delay, _adapterCts?.Token ?? CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        delay = (int)Math.Min(delay * _config.ReconnectMultiplier, _config.MaxReconnectDelayMs);
                    }
                }
            }
            finally
            {
                _connectLock.Release();
            }
        });
    }

    // ----- Helpers -----

    private void TransitionState(AdapterState target)
    {
        if (!AdapterStateTransitions.IsAllowed(State, target))
        {
            throw new InvalidOperationException(
                $"MqttSinkAdapter '{InstanceId}' cannot transition from {State} to {target}.");
        }
        State = target;
    }

    private void RecordSuccess()
    {
        Interlocked.Increment(ref _publishSuccesses);
        _consecutiveFailures = 0;
        _lastSuccessAt = DateTime.UtcNow;
        _lastError = null;
        if (State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Running);
        }
    }

    private void RecordFailure(AdapterError error)
    {
        Interlocked.Increment(ref _publishFailures);
        _consecutiveFailures++;
        _lastError = error;
        _lastFailureAt = DateTime.UtcNow;
        if (State == AdapterState.Running)
        {
            TransitionState(AdapterState.Degraded);
        }
    }

    private static AdapterError MakeError(string code, ErrorCategory category, string message, bool retryable)
        => new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };

    private static MqttQualityOfServiceLevel MapQos(int level) => level switch
    {
        0 => MqttQualityOfServiceLevel.AtMostOnce,
        1 => MqttQualityOfServiceLevel.AtLeastOnce,
        _ => MqttQualityOfServiceLevel.AtLeastOnce,
    };

    /// <summary>
    /// Pull the deviceClass off a point's metadata. Source adapters that
    /// went through <see cref="CanonicalDataPointFactory"/> with a
    /// deviceClass set will have it under
    /// <see cref="CanonicalDataPointFactory.DeviceClassMetadataKey"/>.
    /// Returns <see langword="null"/> for v1 publishers — the topic resolver
    /// then falls back to <see cref="MqttTopicResolver.DefaultDeviceClass"/>
    /// (currently <c>"cnc"</c>) for backward compat.
    /// </summary>
    private static string? ExtractDeviceClass(CanonicalDataPoint point)
    {
        if (point.Metadata is null) return null;
        if (!point.Metadata.TryGetValue(CanonicalDataPointFactory.DeviceClassMetadataKey, out var value))
        {
            return null;
        }
        return value as string;
    }
}
