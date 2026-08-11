// ============================================================================
// File: MqttSinkAdapter.cs
// Purpose: ISinkAdapter implementation for MQTT brokers. Supports two
//          publish strategies: Batch (JSON array per batch) and PerTag
//          (raw scalar per point for EREMOS V2 compatibility).
//
//          Lifecycle: Created → Initialized → Running ↔ Degraded → Stopped
//          Reconnect: a fast phase (attempts 2-6, 250ms apart) followed by the
//          configured exponential backoff, serialized behind _connectLock and
//          an in-flight guard; suppressed after intentional Stop.
//          Publish: a bounded grace window lets a publish wait out an in-flight
//          reconnect instead of failing the instant IsConnected is false.
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
    // ----- Reconnect / publish-grace tuning -----
    //
    // These are CONSTANTS on purpose, not configuration keys.
    //
    //  * There is no operator decision here. Nobody can usefully answer "how
    //    long should a publish wait for a reconnect?" — the only right answer is
    //    "longer than a reconnect normally takes, and short enough that a real
    //    outage is still reported promptly." That is an engineering constant.
    //  * Every existing destination benefits without being edited. A config key
    //    would only help deployments that were re-opened in the wizard and saved.
    //  * Adding keys would break the ADR-0020 redaction drift guard, which
    //    asserts MqttConnectionKeys.All == MqttBundleRedactionRules.KnownKeys.
    //
    // THE ONE INVARIANT THAT TIES THEM TOGETHER:
    //   the fast reconnect phase (~1.25s) MUST stay SHORTER than the publish
    //   grace (2s).
    // That is what makes a brief drop — broker restart, wifi blip, NAT timeout —
    // invisible to the route: the reconnect lands inside the window the publish
    // is already waiting out, so the route never sees an error at all. Widening
    // the fast phase past the grace, or shrinking the grace below the fast
    // phase, silently reintroduces the failure this pair exists to remove, and
    // nothing else in the code will complain. Change them together.

    /// <summary>
    /// How long a publish waits for an in-flight reconnect before reporting
    /// <c>MQTT.NOT_CONNECTED</c>. Bounded: after this window the SAME retryable
    /// error is raised, so a genuine outage still surfaces on the route and the
    /// buffer still holds the batch.
    /// </summary>
    private static readonly TimeSpan PublishConnectGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Poll granularity while waiting out <see cref="PublishConnectGrace"/>.
    /// Fine enough that a 250ms fast-phase reconnect is picked up almost
    /// immediately rather than being rounded up to the next coarse tick.
    /// </summary>
    private const int ConnectGracePollMs = 25;

    /// <summary>
    /// Number of reconnect attempts in the fast phase, counting the immediate
    /// first one. Attempt 1 fires with no delay; attempts 2-6 follow 250ms
    /// apart, so the phase spans (6-1) x 250ms = 1.25s before the configured
    /// backoff takes over.
    /// </summary>
    private const int FastReconnectPhaseAttempts = 6;

    /// <summary>
    /// Delay between fast-phase attempts. The shipped configured default is
    /// 5000ms, which is right for a broker that is genuinely down and far too
    /// slow for what actually dominates in the field: an endpoint that is back
    /// within a second. Paying five seconds for a 200ms outage is what operators
    /// experience as "reconnecting takes for ever".
    /// </summary>
    private const int FastReconnectDelayMs = 250;

    private readonly ILogger<MqttSinkAdapter> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    /// <summary>
    /// 0 = no reconnect loop running, 1 = one is in flight. Both the disconnect
    /// handler and the publish grace can ask for a reconnect, so without this
    /// guard several loops would queue on <see cref="_connectLock"/> and then
    /// run one after another — the later ones calling ConnectAsync on a client
    /// the first had already reconnected.
    /// </summary>
    private int _reconnectLoopRunning;

    private MqttSinkConfiguration _config = null!;
    private IMqttClient _mqttClient = null!;
    private MqttClientOptions _mqttClientOptions = null!;
    private CancellationTokenSource? _adapterCts;

    // Health counters (thread-safe via Interlocked).
    private long _publishAttempts;
    private long _publishSuccesses;
    private long _publishFailures;
    private long _reconnectAttempts;

    // Drop counters, used to tell a flapping client id apart from an ordinary
    // outage. See DescribeFlapping.
    private long _disconnectCount;
    private DateTime? _firstDisconnectAt;

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
            .WithClientId(_config.ClientId ?? DefaultClientId(InstanceId))
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
            // Surfaced so the diagnostic bundle carries the flap evidence even
            // when the connection happens to be up at the moment it is taken.
            ["disconnectCount"] = Interlocked.Read(ref _disconnectCount),
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

        // One grace budget per call, shared with both publish paths below, so a
        // genuine outage can never multiply the 2s wait by the batch size.
        var grace = new ConnectGraceBudget();

        // A config apply that touches a route's source cascades a route restart.
        // The new route worker dequeues the persisted backlog and publishes at
        // once — measured at 0.1ms after the worker started. This sink is
        // deliberately NOT restarted by that reload, so if its client happened to
        // be mid-reconnect the very first publish used to lose the race and
        // degrade the whole route over a connection that was about to come back.
        // Wait the reconnect out; if it is a real outage nothing changes.
        if (!await WaitForConnectionAsync(grace, ct).ConfigureAwait(false))
        {
            var err = MakeError("MQTT.NOT_CONNECTED", ErrorCategory.Network,
                "MQTT client is not connected to the broker." + DescribeFlapping(), retryable: true);
            RecordFailure(err);
            return PublishResult.Failed(err, sw.Elapsed);
        }

        try
        {
            return _config.PublishMode == MqttPublishMode.PerTag
                ? await PublishPerTagAsync(points, grace, sw, ct).ConfigureAwait(false)
                : await PublishBatchAsync(points, grace, sw, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Caller cancellation propagates.
        }
        catch (Exception ex)
        {
            var err = MakeError("MQTT.PUBLISH_FAILED", ErrorCategory.Network,
                $"Publish failed: {ex.Message}{DescribeFlapping()}", retryable: true);
            RecordFailure(err);
            return PublishResult.Failed(err, sw.Elapsed);
        }
    }

    private async Task<PublishResult> PublishBatchAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        ConnectGraceBudget grace,
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

        // The entry check can pass and the link still drop while the batch is
        // being serialized, which on a large batch is not instant. Spend the
        // grace here too if the budget is untouched. Best-effort by design: if
        // it expires we still call PublishAsync and let the real failure decide
        // the error code, so a mid-flight drop keeps reporting MQTT.PUBLISH_FAILED
        // exactly as it did before.
        await WaitForConnectionAsync(grace, ct).ConfigureAwait(false);

        await _mqttClient.PublishAsync(message, ct).ConfigureAwait(false);
        RecordSuccess();
        return PublishResult.Successful(points.Count, sw.Elapsed);
    }

    private async Task<PublishResult> PublishPerTagAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        ConnectGraceBudget grace,
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

            // PerTag walks the batch one message at a time, so a drop part-way
            // through would otherwise fail every remaining point instantly — the
            // same lost race as the entry check, just later in the loop. The
            // shared budget means the wait is spent at most once per call, not
            // once per point: 200 points during a real outage still cost one 2s
            // window, not 200 of them.
            await WaitForConnectionAsync(grace, ct).ConfigureAwait(false);

            try
            {
                await _mqttClient.PublishAsync(message, ct).ConfigureAwait(false);
                accepted++;
                // Per-source flow log (opt-in): value posted to this destination.
                Diagnostics.SinkFlowLog.Post(
                    point.SourceInstanceId, InstanceId, point.TagName, topic, rawValue, "OK");
            }
            catch (OperationCanceledException)
            {
                throw; // Caller cancellation always propagates.
            }
            catch (Exception ex)
            {
                rejected++;
                lastPointError = MakeError("MQTT.PERTAG_PUBLISH_FAILED", ErrorCategory.Network,
                    $"PerTag publish failed for tag '{point.TagName}': {ex.Message}{DescribeFlapping()}",
                    retryable: true);
                _logger.LogWarning(ex,
                    "PerTag publish failed for tag '{Tag}' on topic '{Topic}'.",
                    point.TagName, topic);
                Diagnostics.SinkFlowLog.Post(
                    point.SourceInstanceId, InstanceId, point.TagName, topic, rawValue, "FAILED: " + ex.Message);
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

            Interlocked.Increment(ref _disconnectCount);
            _firstDisconnectAt ??= DateTime.UtcNow;

            _logger.LogWarning(
                "MqttSinkAdapter '{Id}' disconnected from broker ({Count} time(s) so far). "
                    + "Starting background reconnect.",
                InstanceId, Interlocked.Read(ref _disconnectCount));

            StartBackgroundReconnect();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Fire-and-forget background reconnect loop. Shared by the disconnect
    /// handler, by a failed initial connect in <see cref="StartAsync"/>, and by
    /// the publish grace, so a broker that is down at startup recovers by
    /// exactly the same path as one that drops later.
    /// <para>
    /// Schedule: attempt 1 immediate, attempts 2-6 at
    /// <see cref="FastReconnectDelayMs"/> apart (~1.25s of fast phase), then the
    /// configured ramp — <c>ReconnectDelayMs</c> x <c>ReconnectMultiplier</c>,
    /// capped at <c>MaxReconnectDelayMs</c>. The fast phase covers what actually
    /// happens in the field (a broker restart or a wifi blip, back within a
    /// second); the configured ramp still governs a broker that is genuinely
    /// down, so nothing hammers an endpoint that is not coming back.
    /// </para>
    /// <para>
    /// At most one loop runs at a time — see <see cref="_reconnectLoopRunning"/>.
    /// </para>
    /// </summary>
    private void StartBackgroundReconnect()
    {
        // Already reconnecting: nothing to do. Cheap enough that the publish
        // path can call this unconditionally on every disconnected publish.
        if (Interlocked.CompareExchange(ref _reconnectLoopRunning, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _connectLock.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Adapter was disposed while this loop was being scheduled. The
                // guard still has to come back down — a stuck guard would block
                // every later reconnect silently.
                Interlocked.Exchange(ref _reconnectLoopRunning, 0);
                return;
            }

            try
            {
                // Re-check state after acquiring the lock — stop may have
                // been called while we were waiting.
                if (State is AdapterState.Stopping or AdapterState.Stopped)
                {
                    return;
                }

                var attempt = 0;
                var rampDelay = _config.ReconnectDelayMs;
                while (State is AdapterState.Degraded or AdapterState.Running)
                {
                    attempt++;
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
                        int delay;
                        if (attempt < FastReconnectPhaseAttempts)
                        {
                            // Fast phase. Deliberately shorter in total than
                            // PublishConnectGrace so a brief drop is invisible to
                            // the route — see the constants at the top of this
                            // class before changing either number.
                            delay = FastReconnectDelayMs;
                        }
                        else
                        {
                            // Fast phase spent: the endpoint is not coming
                            // straight back, so fall through to the operator's
                            // configured ramp — unchanged behaviour from here on.
                            delay = rampDelay;
                            rampDelay = (int)Math.Min(
                                rampDelay * _config.ReconnectMultiplier, _config.MaxReconnectDelayMs);
                        }

                        _logger.LogWarning(ex,
                            "MqttSinkAdapter '{Id}' reconnect attempt {Attempt} failed; retrying in {Delay}ms.",
                            InstanceId, attempt, delay);
                        try
                        {
                            await Task.Delay(delay, _adapterCts?.Token ?? CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                    }
                }
            }
            finally
            {
                // Guard down BEFORE the lock: Release() throws
                // ObjectDisposedException if the adapter was disposed under us,
                // and a statement after a throwing one in a finally never runs —
                // the guard would stick at 1 and block every later reconnect.
                // A loop started in this instant simply queues on _connectLock,
                // which this one is about to release.
                Interlocked.Exchange(ref _reconnectLoopRunning, 0);
                _connectLock.Release();
            }
        });
    }

    /// <summary>
    /// Waits out an in-flight reconnect, up to <see cref="PublishConnectGrace"/>,
    /// and returns whether the client is connected at the end of it.
    /// <para>
    /// A genuine outage is unaffected: the window is bounded, and when it expires
    /// the caller raises the same retryable error it always did, the buffer holds
    /// the batch, and the route reports the real problem. All this removes is the
    /// case where the connection was milliseconds away from being back.
    /// </para>
    /// <para>
    /// The budget is per publish call, so the cost of an outage is one window per
    /// batch — never one per point.
    /// </para>
    /// </summary>
    private async Task<bool> WaitForConnectionAsync(ConnectGraceBudget grace, CancellationToken ct)
    {
        if (_mqttClient.IsConnected)
        {
            return true;
        }

        // Window already spent on an earlier point in this same batch, or the
        // adapter is not in a state where reconnecting is meaningful (stopped,
        // or never started). Fail fast rather than idling for 2s.
        if (grace.Spent || State is not (AdapterState.Running or AdapterState.Degraded))
        {
            return false;
        }

        grace.Spent = true;

        // If nothing is reconnecting, start it. A publish that discovers a dead
        // connection and then merely reports it would leave recovery waiting on
        // the next disconnect event, which for an already-dropped client never
        // comes. No-op when a loop is already in flight.
        StartBackgroundReconnect();

        var waited = TimeSpan.Zero;
        var poll = TimeSpan.FromMilliseconds(ConnectGracePollMs);
        while (waited < PublishConnectGrace)
        {
            // Caller cancellation propagates out of PublishAsync as it always has.
            await Task.Delay(poll, ct).ConfigureAwait(false);
            waited += poll;

            if (_mqttClient.IsConnected)
            {
                return true;
            }

            // Shutdown while we were waiting — stop holding the route up.
            if (State is AdapterState.Stopping or AdapterState.Stopped)
            {
                return false;
            }
        }

        return _mqttClient.IsConnected;
    }

    /// <summary>
    /// One-shot allowance for <see cref="WaitForConnectionAsync"/>, created per
    /// <see cref="PublishAsync"/> call and threaded through both publish paths.
    /// A class rather than a struct because it is mutated across await points.
    /// </summary>
    private sealed class ConnectGraceBudget
    {
        /// <summary>True once the grace window has been used by this publish call.</summary>
        public bool Spent { get; set; }
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

    /// <summary>
    /// Names the cause when the broker connection is not merely down but
    /// CYCLING, and returns an empty string otherwise.
    ///
    /// "The MQTT client is not connected" is a symptom, and it reads identically
    /// whether the broker is switched off, the network is out, or another client
    /// is using this client id. Only the last of those repeats at speed: brokers
    /// enforce client-id uniqueness, so two clients sharing an id evict each other
    /// in a loop, and publishes fail in the sub-second windows where this one has
    /// just been kicked. Dozens of drops a minute is not an outage — an outage
    /// disconnects once and stays down — so the rate is the tell, and it is the
    /// one thing the operator cannot infer from the message they are given.
    ///
    /// Deliberately conservative: at least 5 drops AND a rate above roughly one a
    /// minute before saying anything, so a flaky radio link that drops twice an
    /// hour is never accused of a configuration fault.
    /// </summary>
    private string DescribeFlapping()
    {
        var drops = Interlocked.Read(ref _disconnectCount);
        if (drops < 5 || _firstDisconnectAt is not { } since)
        {
            return string.Empty;
        }

        var elapsed = DateTime.UtcNow - since;
        var minutes = Math.Max(elapsed.TotalMinutes, 1d);
        if (drops / minutes < 1d)
        {
            return string.Empty;
        }

        var window = elapsed.TotalMinutes < 1.5
            ? "the last minute"
            : $"the last {(int)Math.Round(elapsed.TotalMinutes)} minutes";

        return $" The connection has dropped {drops} times in {window}, which usually means "
            + "another client is connected to this broker with the same client id — they take "
            + "turns evicting each other. Give this destination its own Client id, or leave it "
            + "blank and EdgeConnect will build a unique one.";
    }

    /// <summary>
    /// Client id used when the operator leaves <c>ClientId</c> blank.
    ///
    /// MQTT brokers enforce client-id uniqueness: a second client connecting with
    /// an id already in use evicts the first, which reconnects and evicts it back.
    /// That flap loop surfaces to the operator as
    /// <c>MQTT.PERTAG_PUBLISH_FAILED — the MQTT client is disconnected</c> with the
    /// buffer filling and no hint of the real cause, because each publish lands in
    /// one of the sub-second windows where this client is the one just kicked off.
    ///
    /// A bare <c>edgeconnect-{InstanceId}</c> collides whenever two gateways have a
    /// destination of the same name — and since the wizard suggests names like
    /// "MQTT", that is the ordinary case, not an exotic one. Including the host
    /// name makes the default unique per machine while staying STABLE across
    /// restarts, so broker-side ACLs and dashboards keyed on the id keep working.
    ///
    /// This restores the behaviour the pre-migration code already had:
    /// <c>MqttSettings.ClientId</c> defaulted to <c>edgeconnect-{hostname}</c>
    /// "for automatic uniqueness"; the port to this adapter dropped the hostname.
    ///
    /// Safe for existing deployments: the client connects with
    /// <c>WithCleanSession(true)</c>, so a longer id orphans no queued session
    /// state — the broker simply sees a new, and now non-colliding, client.
    /// </summary>
    internal static string DefaultClientId(string instanceId)
    {
        var host = SanitizeClientIdPart(Environment.MachineName);
        return string.IsNullOrEmpty(host)
            ? $"edgeconnect-{instanceId}"
            : $"edgeconnect-{host}-{instanceId}";
    }

    /// <summary>
    /// Keeps only characters brokers reliably accept in a client id, and caps the
    /// length so a long corporate hostname cannot push the id past limits some
    /// brokers still enforce.
    /// </summary>
    private static string SanitizeClientIdPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            {
                sb.Append(c);
            }

            if (sb.Length == 24)
            {
                break;
            }
        }

        return sb.ToString();
    }
}
