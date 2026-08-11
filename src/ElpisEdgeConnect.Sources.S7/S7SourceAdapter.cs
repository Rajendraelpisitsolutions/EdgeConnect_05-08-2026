// ============================================================================
// File: S7SourceAdapter.cs
// Purpose: ISourceAdapter for Siemens S7 PLCs via Sharp7. Mirrors
//          ModbusTcpSourceAdapter's structure:
//             * InitializeAsync: validate config, build ScanPlan,
//               create CanonicalDataPointFactory + ConnectionManager.
//             * StartAsync: initial best-effort connect, transition to
//               Running.
//             * PollAsync: walk due groups + blocks, ReadArea via the
//               connection manager, decode each block entry, emit
//               canonical points (Good / Uncertain / Bad).
//             * StopAsync / DisposeAsync: tear down cleanly.
//
//          Per-tag quality follows the canonical model state machine:
//             - Block read succeeds: per-tag Good (or Uncertain if the
//               adapter is in the Degraded outer state).
//             - Block read fails: per-tag Bad with Value=null and
//               QualityReason carrying the Sharp7 error text.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
//            docs/core/canonical-data-model.md (Quality state machine)
// ============================================================================

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.S7.Decoding;
using ElpisEdgeConnect.Sources.S7.Retirement;
using ElpisEdgeConnect.Sources.S7.Scanning;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Polling-mode source adapter for Siemens S7 PLCs (S7-300 / S7-400 /
/// S7-1200 / S7-1500) via the Sharp7 ISO-on-TCP client.
/// </summary>
public sealed class S7SourceAdapter : ISourceAdapter, ISourceRetirement
{
    private readonly string _instanceId;
    private readonly IS7Client _client;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;
    private readonly TimeProvider _time;

    private S7SourceConfiguration? _config;
    private S7ScanPlan? _plan;
    private S7ConnectionManager? _connection;
    private CanonicalDataPointFactory? _factory;

    private readonly Dictionary<(int IntervalMs, S7MemoryArea Area, int Db), DateTimeOffset> _nextDueAt = new();

    // Metrics — Interlocked-friendly long counters.
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private long _readsExecuted;
    private long _readFailures;
    private long _decodeFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;
    private DateTime? _lastPollStartedAtUtc;
    private readonly object _stateLock = new();

    // Slice 0 commit 3.0: durable retirement attestation (inert — not yet driven
    // by the live supervisor). Cached so BeginRetirement is idempotent.
    private readonly object _retirementSync = new();
    private AdapterRetirementOperation? _retirement;

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => S7SourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling
        | SourceCapabilities.Browse
        | SourceCapabilities.Quality;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    // Human-readable labels for the opt-in per-source diagnostic log
    // (Core SourceDataLog), mirroring the Modbus adapter's SourceTypeLabel /
    // EndpointLabel so every protocol's <source>.txt reads the same way.
    private const string SourceTypeLabel = "S7 ISO-on-TCP";

    private string EndpointLabel => _config is null
        ? "unknown"
        : $"{_config.Host}:{_config.Port} rack={_config.Rack} slot={_config.Slot}";

    /// <summary>
    /// Production constructor — builds a fresh <see cref="Sharp7Client"/>
    /// for the wire layer. Most production callers use this overload via
    /// <c>S7RegistrationExtensions.AddS7Source</c>.
    /// </summary>
    public S7SourceAdapter(
        string instanceId,
        ILogger<S7SourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
        : this(instanceId, ChooseProductionClient(), logger, gatewayIdentity, time: null)
    {
    }

    /// <summary>
    /// Dispatch helper for the production constructor. When
    /// <see cref="S7DemoModeOptions.IsEnabled"/> is true, returns a synthetic
    /// <see cref="S7DemoClient"/> instead of the real <see cref="Sharp7Client"/>.
    /// The choice is frozen for the process lifetime (demo mode is read once).
    /// </summary>
    private static IS7Client ChooseProductionClient()
        => S7DemoModeOptions.IsEnabled
            ? new S7DemoClient()
            : new Sharp7Client();

    /// <summary>
    /// Test-only accessor exposing the live <see cref="IS7Client"/> instance,
    /// so the demo-dispatch tests can verify the production constructor picks
    /// the right backend based on <see cref="S7DemoModeOptions"/>.
    /// </summary>
    internal IS7Client ClientForTesting => _client;

    /// <summary>
    /// Test/DI constructor — accepts a custom <see cref="IS7Client"/>
    /// and <see cref="TimeProvider"/>. Used by unit tests against a
    /// fake transport.
    /// </summary>
    internal S7SourceAdapter(
        string instanceId,
        IS7Client client,
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
    /// <remarks>
    /// Initiates a lock-free transport close (so a wedged Sharp7 read holding the
    /// wire lock can be interrupted without first acquiring it), then resolves the
    /// durable <c>Completion</c> when the wire is idle (the read worker exited).
    /// Idempotent: repeated calls return the same operation.
    /// </remarks>
    public AdapterRetirementOperation BeginRetirement(AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_retirementSync)
        {
            return _retirement ??= S7Retirement.Begin(
                initiateClose: () => _connection?.Disconnect(),
                awaitWorkerExit: () => _connection is null
                    ? Task.CompletedTask
                    : _connection.WaitForWireIdleAsync(),
                context);
        }
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not S7SourceConfiguration typed)
        {
            throw new ArgumentException(
                $"Expected S7SourceConfiguration; got {config.GetType().FullName}",
                nameof(config));
        }
        if (!string.Equals(typed.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Config InstanceId '{typed.InstanceId}' does not match adapter InstanceId '{_instanceId}'.",
                nameof(config));
        }

        var validation = await ValidateConfigAsync(typed, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            TransitionState(AdapterState.Failed);
            var first = validation.Errors.Count > 0 ? validation.Errors[0] : null;
            throw new ArgumentException(
                $"S7 configuration invalid: {first?.Code} {first?.Message}",
                nameof(config));
        }

        TransitionState(AdapterState.Initializing);

        _config = typed;
        _plan = S7ScanPlanner.Build(typed.TagDefinitions, typed.MaxGapBytes, typed.MaxReadBytes);
        _connection = new S7ConnectionManager(_client, typed, _logger, _time);
        _factory = new CanonicalDataPointFactory(
            gatewayId: typed.GatewayId ?? _gatewayIdentity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: typed.DeviceId,
            deviceName: typed.DeviceName,
            deviceClass: typed.DeviceClass);

        _nextDueAt.Clear();
        var now = _time.GetUtcNow();
        foreach (var g in _plan.Groups)
        {
            _nextDueAt[(g.IntervalMs, g.Area, g.DbNumber)] = now;
        }

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "S7 source {InstanceId} initialized: host={Host}:{Port} rack={Rack} slot={Slot} groups={Groups} tags={Tags}",
            _instanceId, typed.Host, typed.Port, typed.Rack, typed.Slot,
            _plan.Groups.Count, typed.TagDefinitions.Count);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_config is null || _connection is null)
        {
            throw new InvalidOperationException(
                "S7SourceAdapter.StartAsync called before InitializeAsync.");
        }
        TransitionState(AdapterState.Starting);
        try
        {
            _ = await _connection.EnsureConnectedAsync(ct).ConfigureAwait(false);
            // Non-fatal on first start — the poll loop retries on backoff.
            TransitionState(AdapterState.Running);
            _logger.LogInformation(
                "S7 source {InstanceId} started; connected={Connected}",
                _instanceId, _connection.IsConnected);
            SourceDataLog.Session(_instanceId,
                $"S7 source started — endpoint {EndpointLabel}, {_config.TagDefinitions.Count} tag(s), connected={_connection.IsConnected}.");
        }
        catch (Exception ex)
        {
            _lastError = MakeError("S7.START_FAILED", ErrorCategory.Internal, ex.Message, retryable: false);
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
            _logger.LogWarning(ex, "S7 source {InstanceId} encountered an error during stop.", _instanceId);
        }
        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("S7 source {InstanceId} stopped.", _instanceId);
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
                ["demoMode"] = _client is S7DemoClient,
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
        _lastPollStartedAtUtc = DateTime.UtcNow;

        if (!await _connection.EnsureConnectedAsync(ct).ConfigureAwait(false))
        {
            RecordPollFailure();
            return Array.Empty<CanonicalDataPoint>();
        }

        var emitted = new List<CanonicalDataPoint>();
        var nowOffset = _time.GetUtcNow();
        var anyBlockSucceeded = false;
        var anyBlockFailed = false;

        foreach (var group in _plan.Groups)
        {
            var key = (group.IntervalMs, group.Area, group.DbNumber);
            if (_nextDueAt.TryGetValue(key, out var dueAt) && dueAt > nowOffset)
            {
                continue;
            }
            _nextDueAt[key] = nowOffset + TimeSpan.FromMilliseconds(group.IntervalMs);

            foreach (var block in group.Blocks)
            {
                var (ok, points) = await PollBlockAsync(group, block, ct).ConfigureAwait(false);
                emitted.AddRange(points);
                if (ok) anyBlockSucceeded = true;
                else anyBlockFailed = true;
            }
        }

        if (anyBlockSucceeded && !anyBlockFailed)
        {
            RecordPollSuccess();
        }
        else if (anyBlockFailed && !anyBlockSucceeded)
        {
            RecordPollFailure();
        }
        else if (anyBlockSucceeded || anyBlockFailed)
        {
            // Partial success — stay in current outer state but track as degraded.
            RecordPollPartial();
        }

        return emitted;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // S7 is poll-only — the routing engine drives PollAsync. Provide
        // a trivial pass-through so the contract is honored.
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        if (_config is null)
        {
            return Task.FromResult<IReadOnlyList<TagDefinition>>(Array.Empty<TagDefinition>());
        }
        // MVP browse returns the configured tag list — symbol discovery
        // (Optimized DB walk) is a future Milestone N concern per the
        // plan-of-record.
        var result = new List<TagDefinition>(_config.TagDefinitions.Count);
        foreach (var t in _config.TagDefinitions)
        {
            var addr = S7AddressParser.Parse(t.Address);
            var spec = string.IsNullOrWhiteSpace(t.Datatype)
                ? new S7DatatypeSpec(addr.WidthHint switch
                {
                    S7AddressWidthHint.Bit => S7Datatype.Bool,
                    S7AddressWidthHint.Byte => S7Datatype.Byte,
                    S7AddressWidthHint.Word => S7Datatype.Word,
                    S7AddressWidthHint.DWord => S7Datatype.DWord,
                    _ => S7Datatype.Word,
                })
                : S7DatatypeParser.Parse(t.Datatype, default);
            result.Add(new TagDefinition
            {
                Name = t.Name,
                Path = t.Address,
                ValueType = spec.CanonicalType,
                Unit = t.Unit,
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(result);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config is not S7SourceConfiguration typed)
        {
            return Task.FromResult(ValidationResult.Failure(
                "S7.CONFIG_WRONG_TYPE",
                $"Expected S7SourceConfiguration; got {config.GetType().FullName}",
                "$"));
        }
        if (string.IsNullOrWhiteSpace(typed.Host))
        {
            return Task.FromResult(ValidationResult.Failure(
                "S7.CONFIG_MISSING_HOST", "Host is required.", "$.Host"));
        }
        if (typed.Port <= 0 || typed.Port > ushort.MaxValue)
        {
            return Task.FromResult(ValidationResult.Failure(
                "S7.CONFIG_INVALID_PORT", $"Port must be 1..65535 (got {typed.Port}).", "$.Port"));
        }
        if (typed.Rack < 0 || typed.Slot < 0)
        {
            return Task.FromResult(ValidationResult.Failure(
                "S7.CONFIG_INVALID_RACK_SLOT", "Rack and Slot must be non-negative.", "$"));
        }
        if (typed.MaxReadBytes <= 0 || typed.MaxGapBytes < 0)
        {
            return Task.FromResult(ValidationResult.Failure(
                "S7.CONFIG_INVALID_PLANNER", "MaxReadBytes>0 and MaxGapBytes>=0 are required.", "$"));
        }

        // Pre-parse every tag address — operators see config errors at
        // load time, not at first poll.
        foreach (var tag in typed.TagDefinitions)
        {
            try
            {
                S7AddressParser.Parse(tag.Address);
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(ValidationResult.Failure(
                    "S7.CONFIG_INVALID_ADDRESS",
                    $"Tag '{tag.Name}': {ex.Message}",
                    $"$.TagDefinitions[?(@.Name=='{tag.Name}')].Address"));
            }
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

    private async Task<(bool Ok, IReadOnlyList<CanonicalDataPoint> Points)> PollBlockAsync(
        S7ScanGroup group,
        S7ScanBlock block,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _readsExecuted);
        var stopwatch = Stopwatch.StartNew();
        var buffer = ArrayPool<byte>.Shared.Rent(block.ByteCount);
        try
        {
            var read = await _connection!.ReadAreaAsync(
                group.Area, group.DbNumber, block.StartByte, block.ByteCount, buffer, ct)
                .ConfigureAwait(false);
            stopwatch.Stop();

            var ts = DateTime.UtcNow;
            if (!read.Success)
            {
                Interlocked.Increment(ref _readFailures);
                _lastError = MakeError(
                    "S7.READ_FAILED",
                    ClassifyErrorCategory(read.ErrorCode),
                    $"Sharp7 code {read.ErrorCode}: {read.ErrorText}",
                    retryable: true);

                SourceDataLog.Log("DEVICE", _instanceId, $"read-fail:{group.Area}{group.DbNumber}",
                    "DEVICE READ FAILED"
                        + $" | Source name: {_instanceId}"
                        + $" | Source type: {SourceTypeLabel}"
                        + $" | Endpoint: {EndpointLabel}"
                        + " | Status: Disconnected"
                        + $" | Block: {group.Area} DB{group.DbNumber} start={block.StartByte} bytes={block.ByteCount}"
                        + $" | Detail: Sharp7 code {read.ErrorCode}: {read.ErrorText}");

                var badPoints = new List<CanonicalDataPoint>(block.Entries.Count);
                foreach (var entry in block.Entries)
                {
                    badPoints.Add(EmitBadPoint(entry, ts, _lastError.Message));
                }
                return (false, badPoints);
            }

            // Decode synchronously — Spans can't cross await boundaries.
            var goodPoints = DecodeBlock(block, buffer, ts);
            return (true, goodPoints);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private List<CanonicalDataPoint> DecodeBlock(S7ScanBlock block, byte[] buffer, DateTime ts)
    {
        var bufferSpan = buffer.AsSpan(0, block.ByteCount);
        var points = new List<CanonicalDataPoint>(block.Entries.Count);
        foreach (var entry in block.Entries)
        {
            try
            {
                var value = S7Decoder.Decode(
                    bufferSpan, entry.BlockRelativeByteOffset, entry.ParsedAddress.BitOffset, entry.Spec);
                value = ApplyScaleOffset(value, entry);
                var point = EmitGoodPoint(entry, value, ts);
                points.Add(point);

                // Opt-in per-source data trail (throttled ~30s/tag by SourceDataLog).
                var readValue = Convert.ToString(point.Value, CultureInfo.InvariantCulture) ?? "null";
                SourceDataLog.Log("DATA", _instanceId, $"read:{entry.Tag.Name}",
                    "READ OK"
                        + $" | Source name: {_instanceId}"
                        + $" | Source type: {SourceTypeLabel}"
                        + $" | Endpoint: {EndpointLabel}"
                        + " | Status: Connected"
                        + $" | Tag name: {entry.Tag.Name}"
                        + $" | Tag Address: {entry.ParsedAddress}"
                        + $" | Datatype: {entry.Spec.CanonicalType}"
                        + (string.IsNullOrEmpty(entry.Tag.Unit) ? "" : $" | Engineering unit: {entry.Tag.Unit}")
                        + $" | Value received from source: {readValue}"
                        + $" | Value type: {point.ValueType}"
                        + $" | Quality: {point.Quality}"
                        + $" | Forwarded to destination: {readValue} (value unchanged)");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _decodeFailures);
                _logger.LogWarning(ex,
                    "S7 source {InstanceId}: decode failed for tag {Tag} at {Address}.",
                    _instanceId, entry.Tag.Name, entry.ParsedAddress);
                points.Add(EmitBadPoint(entry, ts, $"decode failure: {ex.Message}"));
            }
        }
        return points;
    }

    private CanonicalDataPoint EmitGoodPoint(S7ScanBlockEntry entry, object value, DateTime ts)
    {
        var canonicalType = entry.Spec.CanonicalType;
        var quality = State == AdapterState.Degraded ? DataQuality.Uncertain : DataQuality.Good;
        return _factory!.CreatePoint(
            tagName: entry.Tag.Name,
            tagPath: entry.ParsedAddress.ToString(),
            value: value,
            valueType: canonicalType,
            quality: quality,
            deviceTimestamp: ts,
            gatewayTimestamp: ts,
            unit: entry.Tag.Unit,
            qualityReason: quality == DataQuality.Uncertain ? "adapter in Degraded state — recent transport failures" : null,
            metadata: BuildMetadata());
    }

    private CanonicalDataPoint EmitBadPoint(S7ScanBlockEntry entry, DateTime ts, string reason) =>
        _factory!.CreatePoint(
            tagName: entry.Tag.Name,
            tagPath: entry.ParsedAddress.ToString(),
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

    private static object ApplyScaleOffset(object value, S7ScanBlockEntry entry)
    {
        if (entry.Spec.SupportsScaleOffset && (entry.Tag.Scale is not null || entry.Tag.Offset is not null))
        {
            var raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var scale = entry.Tag.Scale ?? 1.0;
            var offset = entry.Tag.Offset ?? 0.0;
            return raw * scale + offset;
        }
        return value;
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

    private void RecordPollPartial()
    {
        // Partial success — keep the outer state where it is, but ensure
        // we don't slip back to Running from Degraded just from one good
        // block, and don't slip into Degraded just from one bad block.
        Interlocked.Increment(ref _pollSuccesses);
    }

    private void TransitionState(AdapterState target)
    {
        lock (_stateLock)
        {
            State = target;
        }
    }

    private static AdapterError MakeError(string code, ErrorCategory category, string message, bool retryable) =>
        new() { Code = code, Category = category, Message = message, Retryable = retryable };

    private static ErrorCategory ClassifyErrorCategory(int sharp7Code)
    {
        // Sharp7's error code namespace:
        //   - Negative: TCP / transport (closed socket, timeout)
        //   - Low positive: ISO connect errors
        //   - High positive (0x00xxxxxx): PDU / S7 protocol errors
        // For MVP we map them all to Network / Protocol; future polish can
        // separate fatal-from-transient on specific codes.
        return sharp7Code < 0 ? ErrorCategory.Network : ErrorCategory.Protocol;
    }
}
