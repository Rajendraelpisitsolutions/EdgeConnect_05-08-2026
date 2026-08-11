// ============================================================================
// File: MTConnectSourceAdapter.cs
// Purpose: ISourceAdapter implementation for the open MTConnect standard.
//          Polls the Agent's /current endpoint on every PollAsync, parses
//          the XML via MTConnectStreamParser, and emits canonical data
//          points. No native DLL required — plain HTTP + XML.
//
// LOCKED behavior:
//   * Protocol-agnostic Core stays pristine — this adapter references Core,
//     not vice versa.
//   * Per-adapter isolation — one HttpClient per instance, owned by the
//     adapter and disposed on DisposeAsync.
//   * All data emitted as CanonicalDataPoint via CanonicalDataPointFactory,
//     identity sourced from the injected IGatewayIdentity (falls back to
//     "gateway" sentinel only when tests omit it).
//   * Poll pacing via PollIntervalMs — same contract as the FOCAS2 adapter.
//
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2; PHASE2_ENTRY.md Phase 2 adapter list
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// MTConnect source adapter. Polls the configured Agent's <c>/current</c>
/// endpoint every <see cref="SourceConfiguration.PollIntervalMs"/> and
/// emits canonical points for execution state, program, spindle, feedrate,
/// production counters, axis positions, and active alarms.
/// </summary>
public sealed class MTConnectSourceAdapter : ISourceAdapter, Core.Adapters.Retirement.ISourceRetirement
{
    private readonly string _instanceId;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;
    private readonly Func<MTConnectSourceConfiguration, IMTConnectClient> _clientFactory;

    // Slice 0 commit 3.0 retirement: this adapter is a supervisor-driven PULL
    // adapter — its only adapter-owned execution surface is the in-flight poll
    // (no callback ingress, reconnect coordinator, timer, or background loop;
    // SubscribeAsync is unsupported). The gate makes that poll a durable Worker
    // proof; a wedged HTTP poll keeps retirement Completion pending.
    private readonly Core.Adapters.Retirement.PollQuiescenceGate _pollGate = new();
    private readonly object _retirementSync = new();
    private Core.Adapters.Retirement.AdapterRetirementOperation? _retirement;

    // Lifecycle state populated on InitializeAsync.
    private MTConnectSourceConfiguration? _config;
    private IMTConnectClient? _client;
    private CanonicalDataPointFactory? _factory;

    // Pacing — last PollAsync start. Mirrors the Focas2 contract so the
    // source supervisor can call us in a tight loop.
    private DateTime? _lastPollStartedAtUtc;

    // Health counters.
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private int _consecutiveFailures;
    private DateTime? _lastSuccessAt;
    private DateTime? _lastFailureAt;
    private AdapterError? _lastError;
    private DateTime _nextRetryAtUtc = DateTime.MinValue;
    private int _currentBackoffMs;

    // Probe caching — done once after first successful /current.
    private bool _probeCompleted;
    private string? _deviceUuid;
    private string? _deviceManufacturer;

    /// <summary>
    /// Production constructor — constructs an <see cref="MTConnectHttpClient"/>
    /// at initialization time based on the supplied configuration.
    /// </summary>
    public MTConnectSourceAdapter(
        string instanceId,
        ILogger<MTConnectSourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
        : this(instanceId, logger, gatewayIdentity,
               clientFactory: cfg => new MTConnectHttpClient(cfg))
    {
    }

    /// <summary>
    /// Test constructor — accepts a client factory so unit tests can wire
    /// a fake <see cref="IMTConnectClient"/> that returns fixed XML bodies.
    /// </summary>
    internal MTConnectSourceAdapter(
        string instanceId,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity,
        Func<MTConnectSourceConfiguration, IMTConnectClient> clientFactory)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayIdentity = gatewayIdentity;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => MTConnectSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    /// <remarks>
    /// Declares Polling + Browse. Subscription via the Agent's <c>/sample</c>
    /// long-poll endpoint would be a worthwhile follow-up but is out of
    /// scope for Phase 2.
    /// </remarks>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.Browse;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <inheritdoc/>
    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        TransitionState(AdapterState.Initializing);

        if (config is not MTConnectSourceConfiguration mtcConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"MTConnectSourceAdapter requires MTConnectSourceConfiguration, got {config.GetType().Name}.");
        }

        _config = mtcConfig;
        _client = _clientFactory(mtcConfig);
        // MTConnect is a CNC-only standard. Default deviceClass to "cnc"
        // for backward compat with pre-deviceClass configs.
        // Prefer configured human-readable gatewayId from gateway.json over
        // the runtime IGatewayIdentity UUID for the topic-purpose gateway
        // segment. See SourceConfiguration.GatewayId XML doc for rationale.
        _factory = new CanonicalDataPointFactory(
            gatewayId: _config.GatewayId ?? _gatewayIdentity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: _config.DeviceId,
            deviceName: _config.DeviceName,
            deviceClass: _config.DeviceClass ?? "cnc");

        _currentBackoffMs = _config.InitialBackoffMs;

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "MTConnectSourceAdapter '{Id}' initialized against Agent {BaseUrl} (deviceName='{Device}').",
            _instanceId, _config.AgentBaseUrl, _config.AgentDeviceName ?? "");

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);
        TransitionState(AdapterState.Running);
        _logger.LogInformation("MTConnectSourceAdapter '{Id}' started.", _instanceId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Stopping)
        {
            return Task.CompletedTask;
        }
        TransitionState(AdapterState.Stopping);
        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("MTConnectSourceAdapter '{Id}' stopped.", _instanceId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Slice 0 commit 3.0 (inert) — begin a durable quiescence attestation. This
    /// is a supervisor-driven pull adapter: the in-flight poll (drained via the
    /// gate) is the only adapter-owned surface (Worker); there is no callback
    /// ingress or background work creator (verified — no timer/loop/coordinator,
    /// SubscribeAsync unsupported), so those surfaces are NotApplicable. Idempotent.
    /// </summary>
    public Core.Adapters.Retirement.AdapterRetirementOperation BeginRetirement(
        Core.Adapters.Retirement.AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_retirementSync)
        {
            return _retirement ??= Core.Adapters.Retirement.PullAdapterRetirement.Begin(
                _pollGate,
                Retirement.MTConnectRetirementDetailCodes.Initiated,
                Retirement.MTConnectRetirementDetailCodes.WorkerIdleProven,
                context);
        }
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
            ["pollAttempts"] = Interlocked.Read(ref _pollAttempts),
            ["pollSuccesses"] = Interlocked.Read(ref _pollSuccesses),
            ["pollFailures"] = Interlocked.Read(ref _pollFailures),
            ["consecutiveFailures"] = _consecutiveFailures,
            ["probeCompleted"] = _probeCompleted,
        };
        if (_config is not null)
        {
            metrics["agentBaseUrl"] = _config.AgentBaseUrl;
            metrics["agentDeviceName"] = _config.AgentDeviceName ?? string.Empty;
        }
        if (_deviceUuid is not null) metrics["deviceUuid"] = _deviceUuid;
        if (_deviceManufacturer is not null) metrics["deviceManufacturer"] = _deviceManufacturer;
        if (_lastFailureAt.HasValue) metrics["lastFailureAtUtc"] = _lastFailureAt.Value.ToString("O");

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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        // Retirement gate: refuse new polls once retirement has begun so the
        // in-flight poll can drain to a clean Worker quiescence proof.
        if (!_pollGate.TryEnterPoll())
        {
            return Array.Empty<CanonicalDataPoint>();
        }

        try
        {
            return await PollCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _pollGate.ExitPoll();
        }
    }

    private async Task<IReadOnlyList<CanonicalDataPoint>> PollCoreAsync(CancellationToken ct)
    {
        // Honour PollIntervalMs between successive poll starts. Matches
        // the Focas2 adapter's pacing contract.
        var pollIntervalMs = _config?.PollIntervalMs ?? 0;
        if (pollIntervalMs > 0 && _lastPollStartedAtUtc is { } lastStart)
        {
            var elapsed = DateTime.UtcNow - lastStart;
            var target = TimeSpan.FromMilliseconds(pollIntervalMs);
            if (elapsed < target)
            {
                await Task.Delay(target - elapsed, ct).ConfigureAwait(false);
            }
        }
        _lastPollStartedAtUtc = DateTime.UtcNow;

        // Respect backoff when consecutive failures are piling up so we
        // don't hammer a down Agent.
        if (DateTime.UtcNow < _nextRetryAtUtc)
        {
            return [];
        }

        Interlocked.Increment(ref _pollAttempts);

        try
        {
            // Probe once on the first successful cycle. A probe failure is
            // non-fatal — the adapter can still emit /current points even
            // without device metadata.
            if (!_probeCompleted)
            {
                await TryProbeAsync(ct).ConfigureAwait(false);
            }

            var xml = await _client!.GetCurrentAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var points = new List<CanonicalDataPoint>(24);
            var parsed = MTConnectStreamParser.ParseCurrent(xml, _factory!, points, now, now);
            if (!parsed)
            {
                var err = MakeError(MTConnectErrors.NoDeviceStream, ErrorCategory.Protocol,
                    "Agent response contained no DeviceStream with readable values.", retryable: true);
                RecordFailure(err);
                return [];
            }

            RecordSuccess();
            return points;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-driven cancellation propagates. HttpClient.Timeout
            // surfaces as TaskCanceledException with an UNCANCELLED caller
            // token — that falls through to the handler below so we treat
            // it as a retryable network failure.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            var err = MakeError(MTConnectErrors.HttpRequestFailed, ErrorCategory.Network,
                $"HTTP request timed out: {ex.Message}", retryable: true);
            RecordFailure(err);
            return [];
        }
        catch (HttpRequestException ex)
        {
            var err = MakeError(MTConnectErrors.HttpRequestFailed, ErrorCategory.Network,
                $"HTTP request to Agent failed: {ex.Message}", retryable: true);
            RecordFailure(err);
            _logger.LogWarning(ex, "MTConnectSourceAdapter '{Id}' /current request failed.", _instanceId);
            return [];
        }
        catch (XmlException ex)
        {
            var err = MakeError(MTConnectErrors.XmlParseFailed, ErrorCategory.Protocol,
                $"Agent returned malformed XML: {ex.Message}", retryable: true);
            RecordFailure(err);
            _logger.LogWarning(ex, "MTConnectSourceAdapter '{Id}' XML parse failed.", _instanceId);
            return [];
        }
        catch (Exception ex)
        {
            var err = MakeError(MTConnectErrors.CollectError, ErrorCategory.Protocol,
                $"Unexpected error during poll: {ex.Message}", retryable: true);
            RecordFailure(err);
            _logger.LogWarning(ex, "MTConnectSourceAdapter '{Id}' unexpected poll error.", _instanceId);
            return [];
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct)
    {
        throw new NotSupportedException(
            "MTConnect adapter is polling-only for now. /sample long-poll subscription is a future enhancement.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        // Static set — every tag the parser could emit. Per-device actual
        // availability depends on the Agent's /probe response; operators
        // should cross-reference with the Agent's own /probe output.
        var tags = new List<TagDefinition>
        {
            ToTagDefinition(MTConnectTagMap.RunState),
            ToTagDefinition(MTConnectTagMap.ControllerMode),
            ToTagDefinition(MTConnectTagMap.EmergencyStop),
            ToTagDefinition(MTConnectTagMap.MainProgram),
            ToTagDefinition(MTConnectTagMap.RunningProgram),
            ToTagDefinition(MTConnectTagMap.SpindleSpeed),
            ToTagDefinition(MTConnectTagMap.SpindleLoad),
            ToTagDefinition(MTConnectTagMap.FeedRate),
            ToTagDefinition(MTConnectTagMap.PartsCount),
            ToTagDefinition(MTConnectTagMap.CycleTime),
            ToTagDefinition(MTConnectTagMap.AlarmCount),
            ToTagDefinition(MTConnectTagMap.FirstFaultMessage),
        };
        // Include a representative 3-axis set; actual axis names are
        // discovered via /probe but the standard X/Y/Z covers most CNCs.
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            tags.Add(ToTagDefinition(MTConnectTagMap.AxisAbsolute(axis)));
            tags.Add(ToTagDefinition(MTConnectTagMap.AxisMachine(axis)));
        }
        IReadOnlyList<TagDefinition> result = tags;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        if (config is not MTConnectSourceConfiguration mtcConfig)
        {
            return Task.FromResult(ValidationResult.Failure(
                MTConnectErrors.ConfigWrongType,
                $"Expected MTConnectSourceConfiguration, got {config.GetType().Name}.",
                "config"));
        }
        if (string.IsNullOrWhiteSpace(mtcConfig.AgentBaseUrl))
        {
            return Task.FromResult(ValidationResult.Failure(
                MTConnectErrors.ConfigInvalid,
                "AgentBaseUrl is required.", "AgentBaseUrl"));
        }
        // Normalize-then-check rather than demanding an absolute URL outright:
        // a bare host or host:port is a valid way to name an agent, and the
        // normalizer is what decides whether the value can be reached at all.
        if (MTConnectSourceConfiguration.TryNormalizeAgentBaseUrl(mtcConfig.AgentBaseUrl) is null)
        {
            return Task.FromResult(ValidationResult.Failure(
                MTConnectErrors.ConfigInvalid,
                MTConnectSourceConfiguration.InvalidAgentBaseUrlMessage(mtcConfig.AgentBaseUrl),
                "AgentBaseUrl"));
        }
        if (mtcConfig.TimeoutSeconds <= 0)
        {
            return Task.FromResult(ValidationResult.Failure(
                MTConnectErrors.ConfigInvalid,
                "TimeoutSeconds must be positive.", "TimeoutSeconds"));
        }
        return Task.FromResult(ValidationResult.Success());
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (State is not (AdapterState.Stopped or AdapterState.Created or AdapterState.Failed))
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MTConnectSourceAdapter '{Id}' error during dispose stop.", _instanceId);
            }
        }

        if (_client is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { /* dispose best-effort */ }
        }
    }

    // ---- Private helpers -------------------------------------------------

    private async Task TryProbeAsync(CancellationToken ct)
    {
        try
        {
            var xml = await _client!.GetProbeAsync(ct).ConfigureAwait(false);
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var ns = doc.Root?.GetDefaultNamespace() ?? System.Xml.Linq.XNamespace.None;
            var device = System.Linq.Enumerable.FirstOrDefault(doc.Descendants(ns + "Device"));
            if (device is not null)
            {
                _deviceUuid = device.Attribute("uuid")?.Value;
                _deviceManufacturer = System.Linq.Enumerable.FirstOrDefault(
                    device.Descendants(ns + "Description"))?.Attribute("manufacturer")?.Value;
                _logger.LogInformation(
                    "MTConnectSourceAdapter '{Id}' probed Agent: uuid={Uuid}, manufacturer={Manufacturer}",
                    _instanceId, _deviceUuid, _deviceManufacturer);
            }
            _probeCompleted = true;
        }
        catch (Exception ex)
        {
            // Probe failure is non-fatal — proceed to /current anyway.
            _logger.LogDebug(ex,
                "MTConnectSourceAdapter '{Id}' probe failed; will retry on next poll.",
                _instanceId);
        }
    }

    private static TagDefinition ToTagDefinition(MTConnectTagMapEntry entry) => new()
    {
        Name = entry.TagName,
        Path = entry.TagPath,
        ValueType = entry.ValueType,
        Unit = entry.Unit,
        Description = entry.Description,
    };

    private void TransitionState(AdapterState target)
    {
        if (!AdapterStateTransitions.IsAllowed(State, target))
        {
            throw new InvalidOperationException(
                $"MTConnectSourceAdapter '{_instanceId}' cannot transition from {State} to {target}.");
        }
        State = target;
    }

    private void RecordSuccess()
    {
        Interlocked.Increment(ref _pollSuccesses);
        _lastSuccessAt = DateTime.UtcNow;
        _lastError = null;
        _consecutiveFailures = 0;
        _nextRetryAtUtc = DateTime.MinValue;
        _currentBackoffMs = _config?.InitialBackoffMs ?? 2000;

        if (State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Running);
        }
    }

    private void RecordFailure(AdapterError error)
    {
        Interlocked.Increment(ref _pollFailures);
        _consecutiveFailures++;
        _lastError = error;
        _lastFailureAt = DateTime.UtcNow;

        // Back off so the next poll waits at least _currentBackoffMs.
        _nextRetryAtUtc = DateTime.UtcNow.AddMilliseconds(_currentBackoffMs);
        var max = _config?.MaxBackoffMs ?? 60_000;
        var mult = _config?.BackoffMultiplier ?? 2.0;
        _currentBackoffMs = (int)Math.Min(_currentBackoffMs * mult, max);

        var degradeAt = _config?.DegradeAfterConsecutiveFailures ?? 3;
        if (State == AdapterState.Running && _consecutiveFailures >= degradeAt)
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
}
