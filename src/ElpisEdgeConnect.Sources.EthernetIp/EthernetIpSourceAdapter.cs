// ============================================================================
// File: EthernetIpSourceAdapter.cs
// Purpose: ISourceAdapter implementation for Allen-Bradley EtherNet/IP
//          controllers via libplctag. Owns an EthernetIpConnectionManager and
//          drives a per-tag-scheduled poll loop that reads each configured tag
//          on its own scan rate and emits CanonicalDataPoints.
//
//          MVP scope (multi-protocol pilot expansion plan v2.1 §5.2): manual
//          tag list, atomic + AB-STRING reads, polling only. UDT browse, COV,
//          and the ReconfigureAsync hot-swap override are deferred follow-ups.
//
// LOCKED BEHAVIOR:
//   Protocol-agnostic core — this adapter references Core, not vice versa.
//   Per-adapter isolation — one IEthernetIpClient per instance.
//   All emitted data flows through CanonicalDataPointFactory.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2;
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3, §5.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// EtherNet/IP source adapter implementing <see cref="ISourceAdapter"/> for
/// Allen-Bradley controllers (ControlLogix / CompactLogix / GuardLogix /
/// MicroLogix / Micro800) via libplctag.
/// </summary>
public sealed class EthernetIpSourceAdapter : ISourceAdapter
{
    private readonly string _instanceId;
    private readonly IEthernetIpClient _client;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;
    private readonly TimeProvider _time;

    // Lifecycle
    private EthernetIpSourceConfiguration? _config;
    private EthernetIpConnectionManager? _connectionManager;
    private CanonicalDataPointFactory? _factory;

    // Per-tag scheduling — next-due wall-clock per tag name. A tag is read when
    // UtcNow >= its entry; a missing entry means "never read" and qualifies
    // immediately. The entry is advanced BEFORE the read so a slow read does
    // not cascade into repeatedly-due behaviour on the next poll.
    private readonly Dictionary<string, DateTimeOffset> _nextDueAt = new(StringComparer.Ordinal);

    // Health tracking
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private long _tagReads;
    private long _tagReadFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;
    private DateTime? _lastPollStartedAtUtc;

    /// <summary>
    /// Production constructor — uses the real <see cref="LibPlcTagClient"/>, or
    /// the synthetic <see cref="EthernetIpDemoClient"/> when
    /// <see cref="EthernetIpDemoModeOptions.IsEnabled"/> is true (the
    /// EDGECONNECT_ETHERNETIP_FAKE_MODE env var). Mirrors the S7 demo seam.
    /// </summary>
    public EthernetIpSourceAdapter(
        string instanceId,
        ILogger<EthernetIpSourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
        : this(instanceId, ChooseProductionClient(), logger, gatewayIdentity, time: null)
    {
    }

    /// <summary>
    /// Selects the transport client for the production constructor: the
    /// synthetic demo client when EtherNet/IP demo mode is active, otherwise the
    /// real libplctag-backed client.
    /// </summary>
    private static IEthernetIpClient ChooseProductionClient()
        => EthernetIpDemoModeOptions.IsEnabled
            ? new EthernetIpDemoClient()
            : new LibPlcTagClient();

    /// <summary>
    /// Test / DI constructor — accepts a custom <see cref="IEthernetIpClient"/>
    /// (typically a fake in unit tests) and an optional <see cref="TimeProvider"/>
    /// for deterministic per-tag-timer tests.
    /// </summary>
    internal EthernetIpSourceAdapter(
        string instanceId,
        IEthernetIpClient client,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity = null,
        TimeProvider? time = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayIdentity = gatewayIdentity;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => EthernetIpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    /// <remarks>
    /// Declares Polling + Browse. Browse returns the configured tag list as the
    /// catalog — the MVP does not yet walk the controller's UDT tree (deferred
    /// to the UDT-walker milestone), so browse is config-driven like Modbus.
    /// </remarks>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.Browse;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // Human-readable labels for the opt-in per-source diagnostic log
    // (Core SourceDataLog), mirroring the Modbus adapter's SourceTypeLabel /
    // EndpointLabel so every protocol's <source>.txt reads the same way.
    private const string SourceTypeLabel = "EtherNet/IP (CIP)";

    private string EndpointLabel => _config is null
        ? "unknown"
        : $"{_config.Host} (path '{_config.Path}', {_config.CpuFamily})";

    /// <inheritdoc/>
    public async Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        TransitionState(AdapterState.Initializing);

        if (config is not EthernetIpSourceConfiguration eipConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"EthernetIpSourceAdapter requires EthernetIpSourceConfiguration, got {config.GetType().Name}.");
        }

        var validation = await ValidateConfigAsync(eipConfig, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            TransitionState(AdapterState.Failed);
            var first = validation.Errors.Count > 0 ? validation.Errors[0] : null;
            throw new InvalidOperationException(
                $"EthernetIpSourceAdapter '{_instanceId}' configuration invalid: " +
                (first?.Message ?? "unknown validation failure"));
        }

        // Non-blocking findings (e.g. whitespace around a tag address) would
        // otherwise only be visible to whoever ran validation in the Studio.
        // Surface them on startup too — they are exactly the class of problem
        // that is invisible in the UI and fatal at read time.
        foreach (var warning in validation.Warnings)
        {
            _logger.LogWarning(
                "EthernetIpSourceAdapter '{Id}' configuration warning [{Code}] at {Path}: {Message}",
                _instanceId, warning.Code, warning.Path, warning.Message);
        }

        _config = eipConfig;
        _factory = new CanonicalDataPointFactory(
            gatewayId: eipConfig.GatewayId ?? _gatewayIdentity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: eipConfig.DeviceId,
            deviceName: eipConfig.DeviceName,
            deviceClass: eipConfig.DeviceClass);

        _connectionManager = new EthernetIpConnectionManager(_client, eipConfig, _instanceId, _logger, _time);
        _nextDueAt.Clear();

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "EthernetIpSourceAdapter '{Id}' initialized for {Host} (path '{Path}', {Family}) — {Tags} tag(s).",
            _instanceId, eipConfig.Host, eipConfig.Path, eipConfig.CpuFamily, eipConfig.TagDefinitions.Count);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// No initial connect — PollAsync calls EnsureConnectedAsync itself. The
    /// eager attempt's result was discarded (both paths ended at Running), but
    /// it was awaited all the way up through SourceSupervisor.AddAsync into the
    /// hot-reload coordinator, so a config apply against an unreachable device
    /// stalled the operator's Save for the connect budget.
    /// </remarks>
    public Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);
        TransitionState(AdapterState.Running);
        _logger.LogInformation(
            "EthernetIpSourceAdapter '{Id}' started; connecting on first poll.", _instanceId);
        SourceDataLog.Session(_instanceId,
            $"EtherNet/IP source started — endpoint {EndpointLabel}, {_config?.TagDefinitions.Count ?? 0} tag(s).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Stopping)
        {
            return;
        }

        TransitionState(AdapterState.Stopping);
        _connectionManager?.Disconnect();
        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("EthernetIpSourceAdapter '{Id}' stopped.", _instanceId);
        await Task.CompletedTask.ConfigureAwait(false);
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
            ["pollAttempts"] = _pollAttempts,
            ["pollSuccesses"] = _pollSuccesses,
            ["pollFailures"] = _pollFailures,
            ["tagReads"] = _tagReads,
            ["tagReadFailures"] = _tagReadFailures,
            ["connected"] = _connectionManager?.IsConnected ?? false,
            ["consecutiveConnectFailures"] = _connectionManager?.ConsecutiveFailures ?? 0,
            ["circuitBreakerState"] = _connectionManager?.BreakerState.ToString() ?? "Unknown",
        };

        if (_config is not null)
        {
            metrics["endpoint"] = _config.Host;
            metrics["path"] = _config.Path;
            metrics["cpuFamily"] = _config.CpuFamily.ToString();
            metrics["tagCount"] = _config.TagDefinitions.Count;
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

    /// <inheritdoc/>
    /// <remarks>
    /// Reads every tag whose per-tag scan timer has elapsed. A non-fatal read
    /// failure (tag not found, type mismatch) emits one Quality=Bad point for
    /// that tag and continues; a fatal transport failure drops the session via
    /// the connection manager and ends the cycle (remaining tags retry next
    /// poll). Never emits synthetic / last-known values — the pipeline stays
    /// deterministic per blueprint LOCK #14.
    /// </remarks>
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
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

        Interlocked.Increment(ref _pollAttempts);

        if (_config is null || _connectionManager is null || _factory is null)
        {
            return [];
        }

        try
        {
            if (!await _connectionManager.EnsureConnectedAsync(ct).ConfigureAwait(false))
            {
                return [];
            }

            var points = new List<CanonicalDataPoint>();
            var now = _time.GetUtcNow();

            using (await _connectionManager.AcquireWireLockAsync(ct).ConfigureAwait(false))
            {
                foreach (var tag in _config.TagDefinitions)
                {
                    if (_nextDueAt.TryGetValue(tag.Name, out var dueAt) && now < dueAt)
                    {
                        continue;
                    }
                    _nextDueAt[tag.Name] = now.AddMilliseconds(tag.ScanRateMs);

                    var sessionAlive = await PollTagAsync(tag, points, ct).ConfigureAwait(false);
                    if (!sessionAlive)
                    {
                        // Session dropped — stop reading the rest this cycle.
                        break;
                    }
                }
            }

            RecordSuccess();
            return points;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = MakeError(EthernetIpErrors.ReadFailed, ErrorCategory.Protocol,
                $"Unexpected error during EtherNet/IP poll: {ex.Message}", retryable: true);
            RecordFailure(error);
            _logger.LogWarning(ex, "EthernetIpSourceAdapter '{Id}' unexpected poll error.", _instanceId);
            return [];
        }
    }

    /// <summary>
    /// Read one tag and append the resulting point. Returns false if the read
    /// hit a fatal transport error and the session was dropped.
    /// </summary>
    private async Task<bool> PollTagAsync(
        EthernetIpTagDefinition tag, List<CanonicalDataPoint> points, CancellationToken ct)
    {
        Interlocked.Increment(ref _tagReads);
        var elementType = EthernetIpElementTypeExtensions.Parse(tag.Datatype);
        var txTimestamp = _time.GetUtcNow().UtcDateTime;

        EthernetIpReadResult result;
        try
        {
            result = await _client.ReadTagAsync(tag.Address, elementType, ct).ConfigureAwait(false);
        }
        catch (EthernetIpFatalException ex)
        {
            Interlocked.Increment(ref _tagReadFailures);
            var error = MakeError(ex.ErrorCode, ErrorCategory.Network, ex.Message, retryable: true);
            _lastError = error;
            _connectionManager!.HandleFatalTransportError(ex);
            points.Add(EmitBadPoint(tag, txTimestamp, ex.Message));
            _logger.LogWarning(ex,
                "EthernetIpSourceAdapter '{Id}': fatal read of tag '{Tag}' ({Addr}).",
                _instanceId, tag.Name, tag.Address);
            SourceDataLog.Log("DEVICE", _instanceId, "fatal-read",
                "DEVICE READ FAILED (session dropped)"
                    + $" | Source name: {_instanceId}"
                    + $" | Source type: {SourceTypeLabel}"
                    + $" | Endpoint: {EndpointLabel}"
                    + " | Status: Disconnected"
                    + $" | Tag name: {tag.Name}"
                    + $" | Tag Address: {tag.Address}"
                    + $" | Detail: {ex.ErrorCode}: {ex.Message}");
            return false;
        }

        if (!result.Success)
        {
            Interlocked.Increment(ref _tagReadFailures);
            _lastError = MakeError(result.ErrorCode ?? EthernetIpErrors.ReadFailed,
                ErrorCategory.Protocol, result.ErrorText ?? "read failed", retryable: false);
            points.Add(EmitBadPoint(tag, txTimestamp, result.ErrorText ?? result.ErrorCode ?? "EtherNet/IP read failed"));
            _logger.LogDebug(
                "EthernetIpSourceAdapter '{Id}': tag '{Tag}' ({Addr}) read failed ({Code}).",
                _instanceId, tag.Name, tag.Address, result.ErrorCode);

            // A tag-level failure leaves the session up, so it never reached the
            // device log — a permanently dead tag stayed invisible to anyone
            // reading the log instead of watching the live UI. Record it here as
            // well; SourceDataLog throttles to one line per tag per 30s, so a
            // tag failing every poll cannot flood the file.
            SourceDataLog.Log("DEVICE", _instanceId, $"tag-read-failed:{tag.Name}",
                "TAG READ FAILED (session healthy)"
                    + $" | Source name: {_instanceId}"
                    + $" | Source type: {SourceTypeLabel}"
                    + $" | Endpoint: {EndpointLabel}"
                    + " | Status: Connected"
                    + $" | Tag name: {tag.Name}"
                    + $" | Tag Address: {tag.Address}"
                    + $" | Detail: {result.ErrorCode ?? EthernetIpErrors.ReadFailed}: "
                    + $"{result.ErrorText ?? "read failed"}");
            return true;
        }

        var point = DecodePoint(tag, elementType, result, txTimestamp);
        points.Add(point);

        // Opt-in per-source data trail (throttled ~30s/tag by SourceDataLog).
        var readValue = Convert.ToString(point.Value, CultureInfo.InvariantCulture) ?? "null";
        SourceDataLog.Log("DATA", _instanceId, $"read:{tag.Name}",
            "READ OK"
                + $" | Source name: {_instanceId}"
                + $" | Source type: {SourceTypeLabel}"
                + $" | Endpoint: {EndpointLabel}"
                + " | Status: Connected"
                + $" | Tag name: {tag.Name}"
                + $" | Tag Address: {tag.Address}"
                + $" | Datatype: {elementType}"
                + (string.IsNullOrEmpty(tag.Unit) ? "" : $" | Engineering unit: {tag.Unit}")
                + $" | Value received from source: {readValue}"
                + $" | Value type: {point.ValueType}"
                + $" | Quality: {point.Quality}"
                + $" | Forwarded to destination: {readValue} (value unchanged)");
        return true;
    }

    private CanonicalDataPoint DecodePoint(
        EthernetIpTagDefinition tag,
        EthernetIpElementType elementType,
        EthernetIpReadResult result,
        DateTime txTimestamp)
    {
        object value = result.Value!;
        CanonicalValueType valueType = result.ValueType;

        // Apply linear scale/offset for numeric types. Produces a double, like
        // the Modbus decoder, so downstream typing stays consistent.
        if (elementType.SupportsScaleOffset() && (tag.Scale is not null || tag.Offset is not null))
        {
            var raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            value = (raw * (tag.Scale ?? 1.0)) + (tag.Offset ?? 0.0);
            valueType = CanonicalValueType.Double;
        }

        var quality = State == AdapterState.Degraded ? DataQuality.Uncertain : DataQuality.Good;

        return _factory!.CreatePoint(
            tagName: tag.Name,
            tagPath: tag.Address,
            value: value,
            valueType: valueType,
            quality: quality,
            deviceTimestamp: txTimestamp,
            gatewayTimestamp: txTimestamp,
            unit: tag.Unit,
            qualityReason: quality == DataQuality.Uncertain
                ? "adapter in Degraded state — recent read failures"
                : null);
    }

    private CanonicalDataPoint EmitBadPoint(
        EthernetIpTagDefinition tag, DateTime txTimestamp, string qualityReason)
    {
        return _factory!.CreatePoint(
            tagName: tag.Name,
            tagPath: tag.Address,
            value: null,
            valueType: CanonicalValueType.Null,
            quality: DataQuality.Bad,
            deviceTimestamp: txTimestamp,
            gatewayTimestamp: txTimestamp,
            unit: tag.Unit,
            qualityReason: qualityReason);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct)
    {
        throw new NotSupportedException("EtherNet/IP is a polling-only protocol in the MVP adapter.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        // MVP: browse returns the configured tag list. UDT-tree discovery lands
        // with the UDT walker in a later milestone.
        var tagDefs = _config?.TagDefinitions ?? [];
        var list = new List<TagDefinition>(tagDefs.Count);
        foreach (var td in tagDefs)
        {
            var elementType = EthernetIpElementTypeExtensions.ParseOrNull(td.Datatype)
                ?? EthernetIpElementType.Dint;
            list.Add(new TagDefinition
            {
                Name = td.Name,
                Path = td.Address,
                ValueType = elementType.CanonicalType(),
                Unit = td.Unit,
                Description = null,
                Writable = false,
                ProtocolMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["address"] = td.Address,
                    ["datatype"] = elementType.ToString(),
                    ["scanRateMs"] = td.ScanRateMs.ToString(CultureInfo.InvariantCulture),
                },
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(list);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        if (config is not EthernetIpSourceConfiguration eipConfig)
        {
            return Task.FromResult(ValidationResult.Failure(
                EthernetIpErrors.ConfigWrongType,
                $"Expected EthernetIpSourceConfiguration, got {config.GetType().Name}.",
                "config"));
        }

        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(eipConfig.Host))
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigMissingField,
                Message = "Host is required.",
                Path = "Host",
            });
        }

        if (eipConfig.ConnectTimeoutMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "ConnectTimeoutMs must be > 0.",
                Path = "ConnectTimeoutMs",
            });
        }

        if (eipConfig.RequestTimeoutMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "RequestTimeoutMs must be > 0.",
                Path = "RequestTimeoutMs",
            });
        }

        if (eipConfig.MaxTransactionRetries < 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "MaxTransactionRetries must be >= 0.",
                Path = "MaxTransactionRetries",
            });
        }

        if (eipConfig.MaxBackoffMs < eipConfig.InitialBackoffMs)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "MaxBackoffMs must be >= InitialBackoffMs.",
                Path = "MaxBackoffMs",
            });
        }

        if (eipConfig.BackoffMultiplier < 1.0)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "BackoffMultiplier must be >= 1.0.",
                Path = "BackoffMultiplier",
            });
        }

        if (eipConfig.CircuitBreakerThreshold < 1)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = "CircuitBreakerThreshold must be >= 1.",
                Path = "CircuitBreakerThreshold",
            });
        }

        // DeviceClass: EtherNet/IP fronts PLCs and other device roles — operator
        // must declare it for the per-tag MQTT topic. Mirrors the Modbus rule.
        if (string.IsNullOrWhiteSpace(eipConfig.DeviceClass))
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigMissingField,
                Message = "DeviceClass is required for EtherNet/IP sources (e.g. \"plc\"). " +
                          "See shared-knowledge/contracts/eremos-per-tag-mqtt.md for the vocabulary.",
                Path = "DeviceClass",
            });
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(eipConfig.DeviceClass, "^[a-z0-9-]+$"))
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigInvalid,
                Message = $"DeviceClass '{eipConfig.DeviceClass}' is invalid. " +
                          "Must match ^[a-z0-9-]+$ (lowercase ASCII alphanumeric and hyphens).",
                Path = "DeviceClass",
            });
        }

        var warnings = new List<ValidationIssue>();
        for (var i = 0; i < eipConfig.TagDefinitions.Count; i++)
        {
            EthernetIpTagValidator.Validate(
                eipConfig.TagDefinitions[i],
                pathPrefix: $"TagDefinitions[{i}]",
                errors: errors,
                warnings: warnings);
        }

        return Task.FromResult(errors.Count > 0
            ? new ValidationResult { IsValid = false, Errors = errors, Warnings = warnings }
            : new ValidationResult { IsValid = true, Warnings = warnings });
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
                _logger.LogDebug(ex, "EthernetIpSourceAdapter '{Id}' error during dispose stop.", _instanceId);
            }
        }

        if (_connectionManager is not null)
        {
            await _connectionManager.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    // =========================================================================
    // PRIVATE — state / health helpers
    // =========================================================================

    private void TransitionState(AdapterState target)
    {
        if (!AdapterStateTransitions.IsAllowed(State, target))
        {
            throw new InvalidOperationException(
                $"EthernetIpSourceAdapter '{_instanceId}' cannot transition from {State} to {target}.");
        }
        State = target;
    }

    private void RecordSuccess()
    {
        Interlocked.Increment(ref _pollSuccesses);
        _lastSuccessAt = DateTime.UtcNow;
        _lastError = null;
        if (State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Running);
        }
    }

    private void RecordFailure(AdapterError error)
    {
        Interlocked.Increment(ref _pollFailures);
        _lastError = error;
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
}
