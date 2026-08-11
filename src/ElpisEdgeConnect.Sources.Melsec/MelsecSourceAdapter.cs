// ============================================================================
// File: MelsecSourceAdapter.cs
// Purpose: ISourceAdapter + ISourceRetirement for Mitsubishi MELSEC (SLMP / MC
//          3E binary over TCP, read-only). Mirrors S7SourceAdapter:
//            * InitializeAsync: validate, build the scan plan, create the
//              connection manager + CanonicalDataPointFactory.
//            * StartAsync: best-effort connect; transition to Running.
//            * PollAsync: read due blocks via the connection manager, decode,
//              emit canonical points. A failed block marks only its own tags
//              Bad — unrelated blocks are unaffected.
//            * StopAsync / DisposeAsync: tear down cleanly.
//          Retirement rides the shared ISourceRetirement lease (no generation
//          logic of its own). Runs against an injected IMelsecClient — the real
//          TCP client + Host DI arrive in step 6.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Melsec.Decoding;
using ElpisEdgeConnect.Sources.Melsec.Diagnostics;
using ElpisEdgeConnect.Sources.Melsec.Retirement;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>
/// Polling-mode source adapter for Mitsubishi MELSEC PLCs (SLMP / MC 3E binary
/// over TCP, read-only) via an injected <see cref="IMelsecClient"/>.
/// </summary>
public sealed class MelsecSourceAdapter : ISourceAdapter, ISourceRetirement, IMelsecDiagnosticsProvider
{
    private readonly string _instanceId;
    private readonly IMelsecClient _client;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;
    private readonly TimeProvider _time;

    private MelsecSourceConfiguration? _config;
    private MelsecScanPlan? _plan;
    private Profiles.MelsecProfileDefinition? _profile;
    private MelsecConnectionManager? _connection;
    private CanonicalDataPointFactory? _factory;

    private readonly Dictionary<int, DateTimeOffset> _nextDueAt = new();

    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private long _readsExecuted;
    private long _readFailures;
    private long _decodeFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;
    private readonly object _stateLock = new();

    private readonly object _retirementSync = new();
    private AdapterRetirementOperation? _retirement;

    // Observational diagnostics state (P1). Guarded by its own lock so reads never
    // contend with the poll/retirement paths and never affect adapter behavior.
    private readonly object _diagSync = new();
    private readonly Dictionary<(byte Device, int Head), BlockDiag> _blockDiag = new();
    private ushort? _lastEndCode;
    private int? _lastLatencyMs;

    private sealed record BlockDiag(MelsecBlockResult Result, ushort? EndCode, string? Message);

    /// <summary>
    /// Construct the adapter around an injected transport. Production callers pass
    /// a real <c>SlmpClient</c> (step 6); tests pass a fake.
    /// </summary>
    public MelsecSourceAdapter(
        string instanceId,
        IMelsecClient client,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity = null,
        TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _instanceId = instanceId;
        _client = client;
        _logger = logger;
        _gatewayIdentity = gatewayIdentity;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => MelsecSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    public SourceCapabilities Capabilities => SourceCapabilities.Polling | SourceCapabilities.Quality;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // Human-readable labels for the opt-in per-source diagnostic log
    // (Core SourceDataLog), mirroring the Modbus adapter's SourceTypeLabel /
    // EndpointLabel so every protocol's <source>.txt reads the same way.
    private const string SourceTypeLabel = "MELSEC SLMP/MC-3E (TCP)";

    private string EndpointLabel => _config is null
        ? "unknown"
        : $"{_config.Host}:{_config.Port}";

    /// <summary>Test accessor for the injected transport.</summary>
    internal IMelsecClient ClientForTesting => _client;

    /// <inheritdoc/>
    public AdapterRetirementOperation BeginRetirement(AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_retirementSync)
        {
            return _retirement ??= MelsecRetirement.Begin(
                initiateClose: () => _connection?.Disconnect(),
                awaitWorkerExit: () => _connection is null ? Task.CompletedTask : _connection.WaitForWireIdleAsync(),
                context);
        }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not MelsecSourceConfiguration typed)
        {
            throw new ArgumentException(
                $"Expected MelsecSourceConfiguration; got {config.GetType().FullName}", nameof(config));
        }
        if (!string.Equals(typed.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Config InstanceId '{typed.InstanceId}' does not match adapter InstanceId '{_instanceId}'.", nameof(config));
        }

        var validation = await ValidateConfigAsync(typed, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            TransitionState(AdapterState.Failed);
            var first = validation.Errors.Count > 0 ? validation.Errors[0] : null;
            throw new ArgumentException($"MELSEC configuration invalid: {first?.Code} {first?.Message}", nameof(config));
        }

        TransitionState(AdapterState.Initializing);

        _config = typed;
        // Validation guaranteed resolvability above; resolve once for planning + diagnostics.
        Profiles.MelsecProfiles.TryResolve(typed.DeviceProfile, out var resolvedProfile);
        _profile = resolvedProfile ?? Profiles.MelsecProfiles.Modern;
        _plan = MelsecScanPlanner.Build(typed.TagDefinitions, typed.MaxGapWords, typed.MaxPointsPerRequest, _profile);
        _connection = new MelsecConnectionManager(_client, typed, _logger, _time);
        _factory = new CanonicalDataPointFactory(
            gatewayId: typed.GatewayId ?? _gatewayIdentity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: typed.DeviceId,
            deviceName: typed.DeviceName,
            deviceClass: typed.DeviceClass);

        _nextDueAt.Clear();
        var now = _time.GetUtcNow();
        foreach (var block in _plan.Blocks)
        {
            _nextDueAt[block.ScanRateMs] = now;
        }

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "MELSEC source {InstanceId} initialized: host={Host}:{Port} blocks={Blocks} tags={Tags}",
            _instanceId, typed.Host, typed.Port, _plan.Blocks.Count, typed.TagDefinitions.Count);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_config is null || _connection is null)
        {
            throw new InvalidOperationException("MelsecSourceAdapter.StartAsync called before InitializeAsync.");
        }
        TransitionState(AdapterState.Starting);
        try
        {
            _ = await _connection.EnsureConnectedAsync(ct).ConfigureAwait(false);
            // Non-fatal on first start — the poll loop retries under backoff.
            TransitionState(AdapterState.Running);
            _logger.LogInformation("MELSEC source {InstanceId} started; connected={Connected}", _instanceId, _connection.IsConnected);
            SourceDataLog.Session(_instanceId,
                $"MELSEC source started — endpoint {EndpointLabel}, {_config.TagDefinitions.Count} tag(s), connected={_connection.IsConnected}.");
        }
        catch (Exception ex)
        {
            _lastError = MakeError("MELSEC.START_FAILED", ErrorCategory.Internal, ex.Message, retryable: false);
            TransitionState(AdapterState.Failed);
            SourceDataLog.Log("DEVICE", _instanceId, "start-connect",
                "DEVICE CONNECT FAILED"
                    + $" | Source name: {_instanceId}"
                    + $" | Source type: {SourceTypeLabel}"
                    + $" | Endpoint: {EndpointLabel}"
                    + " | Status: Disconnected"
                    + $" | Detail: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Created)
        {
            return;
        }
        TransitionState(AdapterState.Stopping);
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MELSEC source {InstanceId} error during stop.", _instanceId);
        }
        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("MELSEC source {InstanceId} stopped.", _instanceId);
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var level = State switch
        {
            AdapterState.Running when _connection is { IsConnected: true } => HealthLevel.Healthy,
            AdapterState.Running => HealthLevel.Degraded,
            AdapterState.Degraded => HealthLevel.Degraded,
            AdapterState.Failed => HealthLevel.Unhealthy,
            _ => HealthLevel.Unknown,
        };

        var health = new AdapterHealth
        {
            State = State,
            Level = level,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastSuccessAt,
            LastError = _lastError,
            Metrics = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["pollAttempts"] = Interlocked.Read(ref _pollAttempts),
                ["pollSuccesses"] = Interlocked.Read(ref _pollSuccesses),
                ["pollFailures"] = Interlocked.Read(ref _pollFailures),
                ["readsExecuted"] = Interlocked.Read(ref _readsExecuted),
                ["readFailures"] = Interlocked.Read(ref _readFailures),
                ["decodeFailures"] = Interlocked.Read(ref _decodeFailures),
                ["connected"] = _connection?.IsConnected ?? false,
                ["consecutiveFailures"] = _connection?.ConsecutiveFailures ?? 0,
                ["breakerState"] = _connection?.BreakerState.ToString() ?? "Unknown",
            },
        };
        return Task.FromResult(health);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        if (_config is null || _plan is null || _connection is null || _factory is null)
        {
            return Array.Empty<CanonicalDataPoint>();
        }

        Interlocked.Increment(ref _pollAttempts);

        if (!await _connection.EnsureConnectedAsync(ct).ConfigureAwait(false))
        {
            RecordPollFailure();
            return Array.Empty<CanonicalDataPoint>();
        }

        var now = _time.GetUtcNow();
        var dueBuckets = new HashSet<int>();
        foreach (var scanRate in _nextDueAt.Keys.ToList())
        {
            if (_nextDueAt[scanRate] <= now)
            {
                dueBuckets.Add(scanRate);
                _nextDueAt[scanRate] = now + TimeSpan.FromMilliseconds(scanRate);
            }
        }

        var emitted = new List<CanonicalDataPoint>();
        var anyOk = false;
        var anyFail = false;

        foreach (var block in _plan.Blocks)
        {
            if (!dueBuckets.Contains(block.ScanRateMs))
            {
                continue;
            }
            ct.ThrowIfCancellationRequested();

            var (ok, points) = await PollBlockAsync(block, ct).ConfigureAwait(false);
            emitted.AddRange(points);
            if (ok) anyOk = true; else anyFail = true;
        }

        if (anyOk && !anyFail) RecordPollSuccess();
        else if (anyFail && !anyOk) RecordPollFailure();
        else if (anyOk || anyFail) RecordPollPartial();

        return emitted;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // MELSEC is poll-only — the routing engine drives PollAsync.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        // MELSEC is browse-exempt (ADR-0015 carve-out). Return the configured
        // tag list projected to TagDefinition for informational callers.
        if (_config is null)
        {
            return Task.FromResult<IReadOnlyList<TagDefinition>>(Array.Empty<TagDefinition>());
        }
        var result = new List<TagDefinition>(_config.TagDefinitions.Count);
        foreach (var tag in _config.TagDefinitions)
        {
            var valueType = CanonicalValueType.Null;
            if (MelsecAddressParser.TryParse(tag.Address, _profile ?? Profiles.MelsecProfiles.Modern, out var address, out _))
            {
                var datatype = !string.IsNullOrWhiteSpace(tag.Datatype)
                    && MelsecDatatypeParser.TryParse(tag.Datatype, out var dt, out _)
                        ? dt
                        : address.ResolvesToBool ? MelsecDatatype.Bool : MelsecDatatype.Int16;
                valueType = CanonicalTypeFor(datatype);
            }
            result.Add(new TagDefinition
            {
                Name = tag.Name,
                Path = tag.Address,
                ValueType = valueType,
                Unit = tag.Unit,
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(result);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not MelsecSourceConfiguration typed)
        {
            return Fail("MELSEC.CONFIG_WRONG_TYPE", $"Expected MelsecSourceConfiguration; got {config.GetType().FullName}", "$");
        }
        if (string.IsNullOrWhiteSpace(typed.Host))
        {
            return Fail("MELSEC.CONFIG_MISSING_HOST", "Host is required.", "$.Host");
        }
        if (typed.Port is <= 0 or > ushort.MaxValue)
        {
            return Fail("MELSEC.CONFIG_INVALID_PORT", $"Port must be 1..65535 (got {typed.Port}).", "$.Port");
        }
        if (typed.TransportProtocol != MelsecTransportProtocol.Tcp)
        {
            return Fail("MELSEC.CONFIG_MODE_NOT_IMPLEMENTED",
                $"TransportProtocol '{typed.TransportProtocol}' is accepted in config but not implemented in Slice 1 (TCP only).", "$.TransportProtocol");
        }
        if (typed.FrameMode != MelsecFrameMode.Mc3EBinary)
        {
            return Fail("MELSEC.CONFIG_MODE_NOT_IMPLEMENTED",
                $"FrameMode '{typed.FrameMode}' is accepted in config but not implemented in Slice 1 (MC 3E binary only).", "$.FrameMode");
        }
        // A-2O acceptance rule: the profile must resolve in the registry, be
        // operator-selectable, and support the configured frame/transport. The
        // source remains MC 3E binary / TCP / read-only regardless of profile.
        if (!Profiles.MelsecProfiles.TryResolve(typed.DeviceProfile, out var profile)
            || !profile.IsOperatorSelectable)
        {
            return Fail("MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED",
                $"DeviceProfile '{typed.DeviceProfile}' is accepted in config but not operator-selectable in this release (selectable: {SelectableProfileList()}).", "$.DeviceProfile");
        }
        if (profile.FrameMode != typed.FrameMode || profile.Transport != typed.TransportProtocol)
        {
            return Fail("MELSEC.CONFIG_PROFILE_NOT_IMPLEMENTED",
                $"DeviceProfile '{typed.DeviceProfile}' does not support {typed.FrameMode}/{typed.TransportProtocol}.", "$.DeviceProfile");
        }

        try
        {
            _ = MelsecMonitoringTimer.Encode(typed.MonitoringTimerMs);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Fail("MELSEC.CONFIG_TIMER_RANGE", ex.Message, "$.MonitoringTimerMs");
        }
        if (!MelsecMonitoringTimer.TryValidateCoherence(typed.MonitoringTimerMs, typed.RequestTimeoutMs, out var timerError))
        {
            return Fail("MELSEC.CONFIG_TIMEOUT_INCOHERENT", timerError!, "$.RequestTimeoutMs");
        }
        if (typed.MaxPointsPerRequest is <= 0 or > MelsecScanPlanner.HardWordCap)
        {
            return Fail("MELSEC.CONFIG_POINTS_CAP",
                $"MaxPointsPerRequest must be 1..{MelsecScanPlanner.HardWordCap} (got {typed.MaxPointsPerRequest}).", "$.MaxPointsPerRequest");
        }
        if (typed.MaxGapWords < 0)
        {
            return Fail("MELSEC.CONFIG_INVALID_PLANNER", $"MaxGapWords must be >= 0 (got {typed.MaxGapWords}).", "$.MaxGapWords");
        }

        // Per-tag validation (address parse, datatype coherence, scan rate) via
        // the planner — one source of truth, no duplicated checks. Profile-aware:
        // iQ-F parses octal X/Y labels and rejects ZR here.
        var plan = MelsecScanPlanner.Build(typed.TagDefinitions, typed.MaxGapWords, typed.MaxPointsPerRequest, profile);
        if (plan.Errors.Count > 0)
        {
            var first = plan.Errors[0];
            return Fail(first.Code, $"Tag '{first.TagName}': {first.Message}",
                $"$.TagDefinitions[?(@.Name=='{first.TagName}')].Address");
        }

        return Task.FromResult(ValidationResult.Success());
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (State is AdapterState.Running or AdapterState.Degraded or AdapterState.Starting)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopAsync(cts.Token).ConfigureAwait(false);
            }
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        catch
        {
            // best-effort
        }
    }

    private async Task<(bool Ok, IReadOnlyList<CanonicalDataPoint>)> PollBlockAsync(MelsecScanBlock block, CancellationToken ct)
    {
        Interlocked.Increment(ref _readsExecuted);
        var stopwatch = Stopwatch.StartNew();
        var result = await _connection!.ReadWordsAsync(block.DeviceCode, block.HeadDeviceNumber, block.WordCount, ct).ConfigureAwait(false);
        stopwatch.Stop();
        var ts = DateTime.UtcNow;

        RecordBlockDiag(block, result, (int)stopwatch.ElapsedMilliseconds);

        switch (result.Status)
        {
            case MelsecClientStatus.Success:
                return (true, DecodeBlock(block, result.WordData.Span, ts));

            case MelsecClientStatus.ProtocolError:
                Interlocked.Increment(ref _readFailures);
                _lastError = MakeError("MELSEC.READ_PROTOCOL", ErrorCategory.Protocol,
                    $"end code 0x{result.EndCode:X4}: {MelsecEndCode.Describe(result.EndCode)}", retryable: true);
                return (false, BadBlock(block, ts, $"protocol: {_lastError.Message}"));

            case MelsecClientStatus.MalformedResponse:
                Interlocked.Increment(ref _readFailures);
                _lastError = MakeError("MELSEC.READ_MALFORMED", ErrorCategory.Network, result.Message ?? "malformed response", retryable: true);
                return (false, BadBlock(block, ts, $"communication: {_lastError.Message}"));

            case MelsecClientStatus.TransportError:
            default:
                Interlocked.Increment(ref _readFailures);
                _lastError = MakeError("MELSEC.READ_TRANSPORT", ErrorCategory.Network, result.Message ?? "transport error", retryable: true);
                return (false, BadBlock(block, ts, $"communication: {_lastError.Message}"));
        }
    }

    private List<CanonicalDataPoint> DecodeBlock(MelsecScanBlock block, ReadOnlySpan<byte> wordData, DateTime ts)
    {
        var points = new List<CanonicalDataPoint>(block.Entries.Count);
        foreach (var entry in block.Entries)
        {
            try
            {
                var value = MelsecDecoder.Decode(wordData, entry.ByteOffset, entry.Datatype, entry.Tag.WordOrder, entry.BitIndex);
                value = MelsecDecoder.ApplyScaleOffset(value, entry.Tag.Scale, entry.Tag.Offset);
                var point = EmitGoodPoint(entry, value, ts);
                points.Add(point);

                // Opt-in per-source data trail (throttled ~30s/tag by SourceDataLog).
                var readValue = Convert.ToString(point.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
                SourceDataLog.Log("DATA", _instanceId, $"read:{entry.TagName}",
                    "READ OK"
                        + $" | Source name: {_instanceId}"
                        + $" | Source type: {SourceTypeLabel}"
                        + $" | Endpoint: {EndpointLabel}"
                        + " | Status: Connected"
                        + $" | Tag name: {entry.TagName}"
                        + $" | Tag Address: {entry.Address}"
                        + $" | Datatype: {entry.Datatype}"
                        + (string.IsNullOrEmpty(entry.Tag.Unit) ? "" : $" | Engineering unit: {entry.Tag.Unit}")
                        + $" | Value received from source: {readValue}"
                        + $" | Value type: {point.ValueType}"
                        + $" | Quality: {point.Quality}"
                        + $" | Forwarded to destination: {readValue} (value unchanged)");
            }
            catch (MelsecDecodeException ex)
            {
                Interlocked.Increment(ref _decodeFailures);
                _logger.LogWarning(ex, "MELSEC source {InstanceId}: decode failed for tag {Tag} at {Address}.",
                    _instanceId, entry.TagName, entry.Address);
                points.Add(EmitBadPoint(entry, ts, $"config: decode failure: {ex.Message}"));
            }
        }
        return points;
    }

    private List<CanonicalDataPoint> BadBlock(MelsecScanBlock block, DateTime ts, string reason)
    {
        // Opt-in per-source device-failure line (throttled per block by SourceDataLog).
        SourceDataLog.Log("DEVICE", _instanceId, $"read-fail:{block.DeviceCode}{block.HeadDeviceNumber}",
            "DEVICE READ FAILED"
                + $" | Source name: {_instanceId}"
                + $" | Source type: {SourceTypeLabel}"
                + $" | Endpoint: {EndpointLabel}"
                + " | Status: Disconnected"
                + $" | Block: {block.DeviceCode}{block.HeadDeviceNumber} words={block.WordCount}"
                + $" | Detail: {reason}");
        var points = new List<CanonicalDataPoint>(block.Entries.Count);
        foreach (var entry in block.Entries)
        {
            points.Add(EmitBadPoint(entry, ts, reason));
        }
        return points;
    }

    private CanonicalDataPoint EmitGoodPoint(MelsecScanBlockEntry entry, object value, DateTime ts)
    {
        var quality = State == AdapterState.Degraded ? DataQuality.Uncertain : DataQuality.Good;
        return _factory!.CreatePoint(
            tagName: entry.TagName,
            tagPath: entry.Address.ToString(),
            value: value,
            valueType: CanonicalTypeForValue(value),
            quality: quality,
            deviceTimestamp: ts,
            gatewayTimestamp: ts,
            unit: entry.Tag.Unit,
            qualityReason: quality == DataQuality.Uncertain ? "adapter in Degraded state — recent transport failures" : null,
            metadata: BuildMetadata());
    }

    private CanonicalDataPoint EmitBadPoint(MelsecScanBlockEntry entry, DateTime ts, string reason) =>
        _factory!.CreatePoint(
            tagName: entry.TagName,
            tagPath: entry.Address.ToString(),
            value: null,
            valueType: CanonicalValueType.Null,
            quality: DataQuality.Bad,
            deviceTimestamp: ts,
            gatewayTimestamp: ts,
            unit: entry.Tag.Unit,
            qualityReason: reason,
            metadata: BuildMetadata());

    private Dictionary<string, object>? BuildMetadata()
    {
        if (_config is null) return null;
        var any = false;
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(_config.Line)) { dict["line"] = _config.Line!; any = true; }
        if (!string.IsNullOrWhiteSpace(_config.LineId)) { dict["lineId"] = _config.LineId!; any = true; }
        if (!string.IsNullOrWhiteSpace(_config.AssetId)) { dict["assetId"] = _config.AssetId!; any = true; }
        if (!string.IsNullOrWhiteSpace(_config.AssetClass)) { dict["assetClass"] = _config.AssetClass!; any = true; }
        return any ? dict : null;
    }

    private static CanonicalValueType CanonicalTypeFor(MelsecDatatype datatype) => datatype switch
    {
        MelsecDatatype.Bool => CanonicalValueType.Boolean,
        MelsecDatatype.Int16 or MelsecDatatype.UInt16 or MelsecDatatype.Int32 => CanonicalValueType.Integer,
        MelsecDatatype.UInt32 => CanonicalValueType.Long,
        MelsecDatatype.Float32 => CanonicalValueType.Float,
        _ => CanonicalValueType.Null,
    };

    private static CanonicalValueType CanonicalTypeForValue(object value) => value switch
    {
        bool => CanonicalValueType.Boolean,
        short or ushort or int => CanonicalValueType.Integer,
        uint or long => CanonicalValueType.Long,
        float => CanonicalValueType.Float,
        double => CanonicalValueType.Double,
        _ => CanonicalValueType.Object,
    };

    /// <inheritdoc/>
    public MelsecDiagnosticsSnapshot GetDiagnosticsSnapshot()
    {
        var config = _config;
        var plan = _plan;
        var connection = _connection;

        ushort? lastEndCode;
        int? lastLatency;
        Dictionary<(byte, int), BlockDiag> outcomes;
        lock (_diagSync)
        {
            lastEndCode = _lastEndCode;
            lastLatency = _lastLatencyMs;
            outcomes = new Dictionary<(byte, int), BlockDiag>(_blockDiag);
        }

        var route = config is null
            ? new MelsecRouteDiagnostics(0, 0xFF, 0x03FF, 0)
            : new MelsecRouteDiagnostics(config.NetworkNo, config.PcNo, config.RequestDestModuleIoNo, config.RequestDestModuleStationNo);

        var blocks = new List<MelsecScanBlockDiagnostics>();
        var tagQuality = new List<MelsecTagQuality>();
        var affected = new List<string>();

        if (plan is not null)
        {
            foreach (var block in plan.Blocks)
            {
                outcomes.TryGetValue(((byte)block.DeviceCode, block.HeadDeviceNumber), out var diag);
                var lastResult = diag?.Result ?? MelsecBlockResult.NotYetPolled;
                var isFailure = lastResult is MelsecBlockResult.ProtocolError or MelsecBlockResult.CommunicationError;

                blocks.Add(new MelsecScanBlockDiagnostics
                {
                    DeviceSymbol = block.DeviceSymbol,
                    HeadDeviceNumber = block.HeadDeviceNumber,
                    WordCount = block.WordCount,
                    ScanRateMs = block.ScanRateMs,
                    TagNames = block.Entries.Select(e => e.TagName).ToList(),
                    LastResult = lastResult,
                    LastEndCode = diag?.EndCode,
                    LastMessage = diag?.Message,
                });

                var quality = lastResult switch
                {
                    MelsecBlockResult.Good => "Good",
                    MelsecBlockResult.NotYetPolled => "Unknown",
                    _ => "Bad",
                };
                foreach (var entry in block.Entries)
                {
                    tagQuality.Add(new MelsecTagQuality
                    {
                        TagName = entry.TagName,
                        Address = entry.Address.ToString(),
                        Quality = quality,
                        Reason = isFailure ? diag?.Message : null,
                    });
                    if (isFailure)
                    {
                        affected.Add(entry.TagName);
                    }
                }
            }
        }

        return new MelsecDiagnosticsSnapshot
        {
            InstanceId = _instanceId,
            ProfileDisplayName = _profile?.DisplayName,
            Route = route,
            ScanBlocks = blocks,
            LastEndCode = lastEndCode,
            LastEndCodeDescription = lastEndCode is { } code ? MelsecEndCode.Describe(code) : null,
            AffectedTags = affected,
            TagQuality = tagQuality,
            LastRequestLatencyMs = lastLatency,
            Connected = connection?.IsConnected ?? false,
            BreakerState = connection?.BreakerState.ToString() ?? "Unknown",
            ConsecutiveFailures = connection?.ConsecutiveFailures ?? 0,
        };
    }

    /// <summary>Operator-facing list of selectable profiles for error messages.</summary>
    private static string SelectableProfileList() =>
        string.Join(", ", Profiles.MelsecProfiles.All
            .Where(p => p.IsOperatorSelectable)
            .Select(p => p.Key.ToString()));

    private void RecordBlockDiag(MelsecScanBlock block, MelsecClientResult result, int latencyMs)
    {
        MelsecBlockResult mapped;
        ushort? endCode = null;
        string? message = null;
        switch (result.Status)
        {
            case MelsecClientStatus.Success:
                mapped = MelsecBlockResult.Good;
                break;
            case MelsecClientStatus.ProtocolError:
                mapped = MelsecBlockResult.ProtocolError;
                endCode = result.EndCode;
                message = $"end code 0x{result.EndCode:X4}: {MelsecEndCode.Describe(result.EndCode)}";
                break;
            default:
                mapped = MelsecBlockResult.CommunicationError;
                message = result.Message;
                break;
        }

        lock (_diagSync)
        {
            _lastLatencyMs = latencyMs;
            if (endCode is { } ec)
            {
                _lastEndCode = ec;
            }
            _blockDiag[((byte)block.DeviceCode, block.HeadDeviceNumber)] = new BlockDiag(mapped, endCode, message);
        }
    }

    private void RecordPollSuccess()
    {
        Interlocked.Increment(ref _pollSuccesses);
        _lastSuccessAt = DateTime.UtcNow;
        if (State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Running);
        }
    }

    private void RecordPollFailure()
    {
        Interlocked.Increment(ref _pollFailures);
        if (State == AdapterState.Running)
        {
            TransitionState(AdapterState.Degraded);
        }
    }

    private void RecordPollPartial() => Interlocked.Increment(ref _pollSuccesses);

    private void TransitionState(AdapterState target)
    {
        lock (_stateLock)
        {
            State = target;
        }
    }

    private static AdapterError MakeError(string code, ErrorCategory category, string message, bool retryable) =>
        new() { Code = code, Category = category, Message = message, Retryable = retryable };

    private static Task<ValidationResult> Fail(string code, string message, string path) =>
        Task.FromResult(ValidationResult.Failure(code, message, path));
}
