// ============================================================================
// File: ModbusTcpSourceAdapter.cs
// Purpose: ISourceAdapter implementation for Modbus TCP devices. Owns a
//          ModbusConnectionManager, a ModbusTransactionExecutor, and a
//          ScanPlan built at InitializeAsync. PollAsync drives each group
//          on its own timer, executes FC-safe blocks, and runs the decoder
//          to produce CanonicalDataPoints.
//
// LOCKED BEHAVIOR:
//   Protocol-agnostic core — this adapter references Core, not vice versa.
//   Per-adapter isolation — one IModbusClient per instance.
//   All emitted data flows through CanonicalDataPointFactory.
//
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2, PHASE3_EXECUTION_PLAN.md §5-§6
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.ModbusTcp.Decoding;
using ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;
using ElpisEdgeConnect.Sources.ModbusTcp.Retirement;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Modbus TCP source adapter implementing <see cref="ISourceAdapter"/>.
/// F3 wires the scan-group planner and decoder into <see cref="PollAsync"/>,
/// so this adapter now emits typed <see cref="CanonicalDataPoint"/>s.
/// </summary>
public sealed class ModbusTcpSourceAdapter : ISourceAdapter, ISourceRetirement
{
    // Slice 0 commit 3.0: durable retirement attestation (inert — not yet driven
    // by the live supervisor). Cached so BeginRetirement is idempotent.
    private readonly object _retirementSync = new();
    private AdapterRetirementOperation? _retirement;

    private readonly string _instanceId;
    // Null until InitializeAsync when the production path builds the client from
    // config (transport depends on encapsulation). The test ctor injects one
    // directly, in which case the factory is null and this is set up-front.
    private IModbusClient? _client;
    private readonly FluentModbusClientFactory? _clientFactory;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;
    private readonly TimeProvider _time;

    // Lifecycle
    private ModbusTcpSourceConfiguration? _config;
    private ModbusConnectionManager? _connectionManager;
    private ModbusTransactionExecutor? _executor;
    private CanonicalDataPointFactory? _factory;
    private ScanPlan? _plan;
    private ModbusDiagnosticsCollector? _diagnostics;

    // Per-group scheduling — next-due wall-clock per group key. A group is
    // polled when UtcNow >= its entry; after a successful or failed poll we
    // bump its entry to UtcNow + group.IntervalMs. A missing entry means
    // "has never been polled" and qualifies immediately.
    private readonly Dictionary<(int IntervalMs, byte UnitId, ModbusRegisterClass Rc), DateTimeOffset> _nextDueAt = new();

    // Address-base auto-detect throttle. When a block fails with Illegal Data
    // Address (0x02) we probe the device to work out which Address base WOULD
    // make the addresses valid, then cache that answer per block start-address
    // for a short window so we don't re-probe every poll cycle. Best-effort;
    // touched only on the (single-threaded) poll path, like _nextDueAt.
    private readonly Dictionary<ushort, (DateTime Until, (ModbusAddressBase Base, ModbusRegisterClass Rc)? Result)> _baseDetectCache = new();
    private static readonly TimeSpan BaseDetectThrottle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BaseDetectRetry = TimeSpan.FromSeconds(4);

    // Health tracking
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private long _transactionsExecuted;
    private long _transactionFailures;
    private long _decodeFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;
    private DateTime? _lastPollStartedAtUtc;

    /// <summary>
    /// Production constructor — the transport client is built from the config's
    /// encapsulation in <see cref="InitializeAsync"/> via the default
    /// <see cref="FluentModbusClientFactory"/> (TCP, RTU-over-TCP, or serial RTU).
    /// </summary>
    public ModbusTcpSourceAdapter(
        string instanceId,
        ILogger<ModbusTcpSourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayIdentity = gatewayIdentity;
        _time = TimeProvider.System;
        _clientFactory = new FluentModbusClientFactory();
        _client = null; // created from config in InitializeAsync
    }

    /// <summary>
    /// Test / DI constructor — accepts a custom <see cref="IModbusClient"/>
    /// (typically <c>FakeModbusClient</c> in unit tests) and an optional
    /// <see cref="TimeProvider"/> for deterministic per-group-timer tests.
    /// The injected client wins; no factory is used.
    /// </summary>
    internal ModbusTcpSourceAdapter(
        string instanceId,
        IModbusClient client,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity = null,
        TimeProvider? time = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayIdentity = gatewayIdentity;
        _time = time ?? TimeProvider.System;
        _clientFactory = null;
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    /// <remarks>
    /// Reports the configured protocol id — <c>modbustcp</c> or <c>modbusrtu</c>
    /// (ADR-0033) — so diagnostics and the canonical data point distinguish the
    /// two. Falls back to <c>modbustcp</c> before <see cref="InitializeAsync"/>.
    /// </remarks>
    public string ProtocolName =>
        _config?.ProtocolName ?? ModbusTcpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    /// <remarks>
    /// Declares Polling + Browse. Browse returns the adapter's configured
    /// <see cref="ModbusTcpSourceConfiguration.TagDefinitions"/> as the
    /// catalog — Modbus slaves do not expose metadata over the wire, so
    /// browse is driven by config.
    /// </remarks>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.Browse;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <inheritdoc/>
    public async Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        TransitionState(AdapterState.Initializing);

        if (config is not ModbusTcpSourceConfiguration modbusConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"ModbusTcpSourceAdapter requires ModbusTcpSourceConfiguration, got {config.GetType().Name}.");
        }

        var validation = await ValidateConfigAsync(modbusConfig, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            TransitionState(AdapterState.Failed);
            var first = validation.Errors.Count > 0 ? validation.Errors[0] : null;
            throw new InvalidOperationException(
                $"ModbusTcpSourceAdapter '{_instanceId}' configuration invalid: " +
                (first?.Message ?? "unknown validation failure"));
        }

        _config = modbusConfig;

        // DeviceClass: Modbus is protocol-agnostic — a Modbus connection
        // can serve a PLC, meter, DAQ, tracker, etc. ValidateConfigAsync
        // requires the user to set this explicitly; no sensible default
        // exists. By the time we land here it's already non-empty.
        // GatewayId precedence:
        //   1. configured display name from gateway.json (operator-readable)
        //   2. runtime IGatewayIdentity UUID (identity-only fallback for
        //      narrow harnesses where no GatewayConfiguration is in scope)
        //   3. "gateway" sentinel (last resort for unit tests with neither)
        _factory = new CanonicalDataPointFactory(
            gatewayId: modbusConfig.GatewayId ?? _gatewayIdentity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: modbusConfig.DeviceId,
            deviceName: modbusConfig.DeviceName,
            deviceClass: modbusConfig.DeviceClass);

        // Production path: build the transport client from the encapsulation
        // (TCP / RTU-over-TCP / serial RTU). Test path: a client was injected.
        _client ??= _clientFactory!.Create(modbusConfig);
        var client = _client!;

        _connectionManager = new ModbusConnectionManager(client, modbusConfig, _instanceId, _logger);
        _executor = new ModbusTransactionExecutor(client, _connectionManager, modbusConfig, _instanceId, _logger);

        // Build the scan plan once at init; F3's poll loop walks it every
        // cycle. Because ValidateConfigAsync above rejects unknown datatypes
        // and byte-order mismatches, the planner's own validation never
        // surfaces here.
        _plan = ScanPlanner.Build(modbusConfig.TagDefinitions, modbusConfig.MaxGapRegisters);
        _nextDueAt.Clear();

        // F5: diagnostics collector is rebuilt alongside the plan so every
        // configured block starts with clean per-block metrics. History
        // across config reloads is deliberately dropped — the audit log
        // owns that story, not the adapter.
        _diagnostics = new ModbusDiagnosticsCollector(_plan);

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "ModbusTcpSourceAdapter '{Id}' initialized for {Host}:{Port} ({Encapsulation}) — {Groups} scan group(s), {Blocks} block(s), {Tags} tag(s).",
            _instanceId, modbusConfig.Host, modbusConfig.Port, modbusConfig.Encapsulation,
            _plan.Groups.Count, _plan.TotalBlockCount, _plan.TotalTagCount);
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);

        try
        {
            // Best-effort initial connect. Failure here is non-fatal — the
            // supervisor calls PollAsync next and the connection manager
            // retries on its own cadence. Mirrors the FOCAS2 contract.
            await _connectionManager!.EnsureConnectedAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ModbusTcpSourceAdapter '{Id}' initial connect threw — will retry on poll.",
                _instanceId);
        }

        TransitionState(AdapterState.Running);
        _logger.LogInformation("ModbusTcpSourceAdapter '{Id}' started.", _instanceId);

        // Create/seed this source's own log file up front so it exists for reading
        // even before any issue occurs. Subsequent DEVICE/CODE lines append here.
        DataIssueLog.Session(_instanceId,
            $"source STARTED — endpoint={_config?.Host}:{_config?.Port} ({_config?.Encapsulation}); "
                + $"deviceClass={_config?.DeviceClass}; defaultUnitId={_config?.DefaultUnitId}; "
                + $"scanGroups={_plan?.Groups.Count ?? 0} blocks={_plan?.TotalBlockCount ?? 0} "
                + $"tags={_plan?.TotalTagCount ?? 0}; pollIntervalMs={_config?.PollIntervalMs}; "
                + $"connectTimeoutMs={_config?.ConnectTimeoutMs} requestTimeoutMs={_config?.RequestTimeoutMs}. "
                + "This file logs DEVICE issues (connectivity/slave/transport) and CODE issues (decode/internal).");
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        if (State is AdapterState.Stopped or AdapterState.Stopping)
        {
            return;
        }

        TransitionState(AdapterState.Stopping);

        if (_connectionManager is not null)
        {
            _connectionManager.Disconnect();
        }

        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("ModbusTcpSourceAdapter '{Id}' stopped.", _instanceId);

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
            ["transactionsExecuted"] = _transactionsExecuted,
            ["transactionFailures"] = _transactionFailures,
            ["decodeFailures"] = _decodeFailures,
            ["connected"] = _connectionManager?.IsConnected ?? false,
            ["consecutiveConnectFailures"] = _connectionManager?.ConsecutiveFailures ?? 0,
            ["circuitBreakerState"] = _connectionManager?.BreakerState.ToString() ?? "Unknown",
        };

        if (_config is not null)
        {
            metrics["endpoint"] = $"{_config.Host}:{_config.Port}";
            metrics["encapsulation"] = _config.Encapsulation.ToString();
        }

        if (_plan is not null)
        {
            metrics["scanGroups"] = _plan.Groups.Count;
            metrics["scanBlocks"] = _plan.TotalBlockCount;
            metrics["scanTags"] = _plan.TotalTagCount;
        }

        // F5: flatten the per-block diagnostics snapshot into JSON-friendly
        // primitives so Prometheus / management-API consumers don't need
        // to know about the Diagnostics namespace.
        if (_diagnostics is not null)
        {
            var snapshot = _diagnostics.Snapshot();
            metrics["blockMetrics"] = FlattenBlockMetrics(snapshot.Blocks);
            metrics["slaveExceptionsByCode"] = snapshot.SlaveExceptionsByCode;
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

    private static List<Dictionary<string, object>> FlattenBlockMetrics(
        IReadOnlyList<ModbusBlockMetricsSnapshot> blocks)
    {
        var list = new List<Dictionary<string, object>>(blocks.Count);
        foreach (var b in blocks)
        {
            var entry = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["unitId"] = b.Key.UnitId,
                ["registerClass"] = b.Key.RegisterClass.ToString(),
                ["startAddress"] = b.Key.StartAddress,
                ["count"] = b.Key.Count,
                ["txs"] = b.Transactions,
                ["ok"] = b.Successes,
                ["fail"] = b.Failures,
                ["retries"] = b.Retries,
                ["transportErrors"] = b.TransportErrors,
                ["slaveExceptions"] = b.SlaveExceptions,
                ["decodeErrors"] = b.DecodeErrors,
            };
            if (b.RttMeanMs is { } mean) entry["rttMeanMs"] = mean;
            if (b.RttMinMs is { } rmin) entry["rttMinMs"] = rmin;
            if (b.RttMaxMs is { } rmax) entry["rttMaxMs"] = rmax;
            if (b.RttP95Ms is { } p95) entry["rttP95Ms"] = p95;
            if (b.RttLatestMs is { } latest) entry["rttLatestMs"] = latest;
            if (b.LastSuccessAt is { } ok) entry["lastSuccessAt"] = ok.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (b.LastFailureAt is { } nok) entry["lastFailureAt"] = nok.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            if (b.LastErrorCode is not null) entry["lastErrorCode"] = b.LastErrorCode;
            if (b.LastErrorCategory is not null) entry["lastErrorCategory"] = b.LastErrorCategory;
            list.Add(entry);
        }
        return list;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// F3 contract: walks the <see cref="ScanPlan"/> built at
    /// <see cref="InitializeAsync"/>, polling only the groups whose per-group
    /// interval timer has elapsed. For each due group, executes every
    /// <see cref="ScanBlock"/> via the transaction executor and decodes
    /// each <see cref="ScanBlockEntry"/> into a <see cref="CanonicalDataPoint"/>.
    /// </para>
    /// <para>
    /// Failure handling mirrors the agreed F3 scope:
    /// <list type="bullet">
    ///   <item>Block-level transaction failure (transport or slave exception)
    ///   emits no points for that block's tags. Counted in <c>transactionFailures</c>.</item>
    ///   <item>Per-tag decode exception emits no point for that tag. Counted
    ///   in <c>decodeFailures</c>.</item>
    ///   <item>Never emit "last-known" or synthetic values — source adapters
    ///   are pipeline-deterministic per blueprint LOCK #14.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        // Self-pacing floor — the supervisor calls PollAsync in a tight loop
        // and expects the adapter to throttle. Users set PollIntervalMs to
        // match the fastest group they want to hit.
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

        if (_plan is null || _connectionManager is null || _executor is null || _factory is null)
        {
            return [];
        }

        try
        {
            if (!await _connectionManager.EnsureConnectedAsync(ct).ConfigureAwait(false))
            {
                // Device unreachable / in backoff / circuit-breaker open. Record
                // it as a failure so the adapter goes Degraded and the connect
                // error is surfaced to diagnostics — otherwise the source would
                // sit in "Running" with no data and no explanation of why.
                RecordFailure(MakeError(
                    ModbusErrors.ConnectFailed,
                    ErrorCategory.Network,
                    $"Modbus device {_config?.Host}:{_config?.Port} is not reachable — no connection "
                        + $"(consecutive connect failures: {_connectionManager.ConsecutiveFailures}, "
                        + $"circuit breaker: {_connectionManager.BreakerState}).",
                    retryable: true));
                DataIssueLog.Log("DEVICE", _instanceId,
                    $"connect:{_config?.Host}:{_config?.Port}",
                    $"CONNECT FAILED — endpoint={_config?.Host}:{_config?.Port} ({_config?.Encapsulation}); "
                        + $"connectTimeoutMs={_config?.ConnectTimeoutMs}; "
                        + $"consecutiveConnectFailures={_connectionManager.ConsecutiveFailures}; "
                        + $"circuitBreaker={_connectionManager.BreakerState}; "
                        + $"lastSuccessAt={(_connectionManager.LastSuccessAt?.ToString("O") ?? "never")}; "
                        + "probableCause: device not reachable at this IP/subnet, TCP port closed/filtered, "
                        + "the edge PC NIC is on a different subnet, or the endpoint is not a Modbus server. "
                        + "Verify with ping + Test-NetConnection <host> -Port <port>; "
                        + "action: no data emitted this poll cycle");
                return [];
            }

            var points = new List<CanonicalDataPoint>();
            var now = _time.GetUtcNow();

            foreach (var group in _plan.Groups)
            {
                var key = (group.IntervalMs, group.UnitId, group.RegisterClass);
                if (_nextDueAt.TryGetValue(key, out var dueAt) && now < dueAt)
                {
                    continue;
                }

                // Mark due-time BEFORE executing so a slow transaction does
                // not cascade into repeatedly-due behaviour on the next poll.
                _nextDueAt[key] = now.AddMilliseconds(group.IntervalMs);

                foreach (var block in group.Blocks)
                {
                    await PollBlockAsync(group, block, points, ct).ConfigureAwait(false);
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
            var error = MakeError(ModbusErrors.TransactionFailed, ErrorCategory.Protocol,
                $"Unexpected error during Modbus poll: {ex.Message}", retryable: true);
            RecordFailure(error);
            _logger.LogWarning(ex, "ModbusTcpSourceAdapter '{Id}' unexpected poll error.", _instanceId);
            DataIssueLog.Log("CODE", _instanceId, "poll-exception",
                $"POLL ERROR — endpoint={_config?.Host}:{_config?.Port} ({_config?.Encapsulation}); "
                    + $"unexpected {ex.GetType().Name}: {ex.Message}; "
                    + $"scanGroups={_plan?.Groups.Count ?? 0} blocks={_plan?.TotalBlockCount ?? 0} "
                    + $"tags={_plan?.TotalTagCount ?? 0}; "
                    + "probableCause: internal adapter fault (not a device problem); "
                    + "action: no data emitted this poll cycle");
            return [];
        }
    }

    private async Task PollBlockAsync(
        ScanGroup group, ScanBlock block, List<CanonicalDataPoint> points, CancellationToken ct)
    {
        Interlocked.Increment(ref _transactionsExecuted);
        var request = block.ToRequest(group.UnitId, group.RegisterClass);
        var result = await _executor!.ExecuteAsync(request, ct).ConfigureAwait(false);

        var blockKey = ScanBlockKey.From(group, block);
        _diagnostics?.RecordTransaction(blockKey, result);

        if (!result.IsSuccess)
        {
            Interlocked.Increment(ref _transactionFailures);
            if (result.Error is { } err)
            {
                _lastError = err;
            }
            _logger.LogDebug(
                "ModbusTcpSourceAdapter '{Id}': block FC0{Fc} unit={Unit} addr={Addr} qty={Qty} failed ({Code}).",
                _instanceId, group.FunctionCode, group.UnitId,
                block.StartAddress, block.Count, result.Error?.Code ?? "unknown");

            // G.5: emit one Quality=Bad point per tag in the failed block so
            // downstream OPC UA / MQTT clients observe the outage instead of
            // continuing to see the last successful value's timestamp drift.
            // Skipping the emission would leave clients with stale Good-quality
            // values — incorrect per OPC UA spec and confusing on dashboards.
            // Value = null is acceptable when Quality is non-Good per the
            // canonical-data-model contract (see docs/core/canonical-data-model.md).
            var badTimestamp = _time.GetUtcNow().UtcDateTime;

            // Illegal Data Address (0x02) almost always means the wrong "Address
            // base" is selected. Probe the device (throttled) to find which base
            // WOULD make these addresses valid, so we can tell the operator the
            // exact base to pick rather than a cryptic device string.
            var isIllegalAddress =
                result.SlaveExceptionCode == 0x02 && _config is not null && block.Entries.Count > 0;
            (ModbusAddressBase Base, ModbusRegisterClass Rc)? detected = isIllegalAddress
                ? await DetectWorkingBaseThrottledAsync(block, group, ct).ConfigureAwait(false)
                : null;

            // Generic operator-facing reason — used for the log line and for any
            // non-0x02 slave exception. The per-tag 0x02 message (built in the
            // emit loop below) is more specific.
            var qualityReason = result.SlaveExceptionCode != 0
                ? DescribeOperatorReason(result.SlaveExceptionCode)
                : (result.Error?.Message
                    ?? result.Error?.Code
                    ?? "Modbus read failed (no error detail)");

            // Persist the data issue to the source's own log file under
            // %ProgramData%\EdgeConnect\logs\. A read that fails after the socket
            // is open is a DEVICE-side problem (slave exception such as Illegal
            // Data Address, wrong unit id, or a transport timeout) — never our
            // code. Throttled per block+code. One line carries everything needed
            // to diagnose without cross-referencing other tools.
            var tagDetails = new List<string>(block.Entries.Count);
            foreach (var entry in block.Entries)
            {
                var t = entry.Tag;
                tagDetails.Add(
                    $"{t.Name}@{group.RegisterClass}/{t.Address} (unit={t.UnitId}, dt={t.Datatype ?? "uint16"}, "
                        + $"offset={entry.Offset}, width={entry.Width})");
            }
            var slaveCode = result.SlaveExceptionCode != 0
                ? $"0x{result.SlaveExceptionCode:X2} ({DescribeSlaveExceptionCode(result.SlaveExceptionCode)})"
                : "n/a";
            DataIssueLog.Log("DEVICE", _instanceId,
                $"block:{group.FunctionCode}:{group.UnitId}:{block.StartAddress}:{result.Error?.Code}",
                $"READ FAILED — endpoint={_config?.Host}:{_config?.Port} ({_config?.Encapsulation}); "
                    + $"request: FC0{group.FunctionCode} ({group.RegisterClass}) unit={group.UnitId} "
                    + $"startAddr={block.StartAddress} qty={block.Count}; "
                    + $"error: code={result.Error?.Code} category={result.Error?.Category} "
                    + $"retryable={result.Error?.Retryable} slaveExceptionCode={slaveCode} "
                    + $"retries={result.RetryCount} elapsedMs={result.Elapsed.TotalMilliseconds:F0}; "
                    + $"reason=\"{qualityReason}\"; "
                    + $"affectedTags({block.Entries.Count})=[{string.Join(" | ", tagDetails)}]; "
                    + $"probableCause: {DescribeProbableCause(result.Error, result.SlaveExceptionCode)}; "
                    + $"action: emitted Quality=Bad / Value=null for each tag above");

            foreach (var entry in block.Entries)
            {
                var reason = qualityReason;
                if (isIllegalAddress)
                {
                    var entered = ReconstructEnteredAddress(
                        entry.Tag.Address, _config!.AddressBase, group.RegisterClass);
                    reason = AddressBaseReason(entered, _config!.AddressBase, group.RegisterClass, detected);
                }
                points.Add(EmitBadPoint(entry, group.RegisterClass, badTimestamp, reason));
            }
            return;
        }

        // DeviceTimestamp = GatewayTimestamp = now at completion. Modbus has
        // no on-wire time, and we don't want to introduce a null here.
        var txTimestamp = _time.GetUtcNow().UtcDateTime;

        foreach (var entry in block.Entries)
        {
            try
            {
                var point = DecodeEntry(entry, group.RegisterClass, result, txTimestamp);
                points.Add(point);

                // Log the successful reading for this tag too (throttled per tag
                // to ~30s so the file stays readable — the poll loop reads far
                // faster than that). Category DATA distinguishes it from issues.
                DataIssueLog.Log("DATA", _instanceId,
                    $"read:{point.TagName}",
                    $"READ OK — tag={point.TagName} {group.RegisterClass}/{entry.Tag.Address} "
                        + $"(unit={entry.Tag.UnitId}); "
                        + $"value={Convert.ToString(point.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null"}; "
                        + $"type={point.ValueType}; quality={point.Quality}"
                        + (string.IsNullOrEmpty(entry.Tag.Unit) ? "" : $"; eu={entry.Tag.Unit}"));
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _decodeFailures);
                _diagnostics?.RecordDecodeError(blockKey,
                    errorCode: "MODBUS.DECODE_FAILED",
                    errorCategory: ErrorCategory.Protocol);
                _logger.LogWarning(ex,
                    "ModbusTcpSourceAdapter '{Id}': decode failed for tag '{Tag}' (addr={Addr}, datatype={Datatype}).",
                    _instanceId, entry.Tag.Name, entry.Tag.Address, entry.Tag.Datatype);

                // A decode failure on a successful read is a CODE-side problem
                // (the raw registers arrived but interpreting them threw — usually
                // a datatype/byte-order/width mismatch in the tag definition).
                DataIssueLog.Log("CODE", _instanceId,
                    $"decode:{entry.Tag.Name}",
                    $"DECODE FAILED — endpoint={_config?.Host}:{_config?.Port}; "
                        + $"tag={entry.Tag.Name} {group.RegisterClass}/{entry.Tag.Address} "
                        + $"(unit={entry.Tag.UnitId}, datatype={entry.Tag.Datatype ?? "uint16"}, "
                        + $"byteOrder={(entry.Tag.ByteOrder?.ToString() ?? "default")}, "
                        + $"offset={entry.Offset}, width={entry.Width}, "
                        + $"scale={entry.Tag.Scale?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, "
                        + $"offsetVal={entry.Tag.Offset?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}); "
                        + $"error: {ex.GetType().Name}: {ex.Message}; "
                        + "probableCause: the tag's datatype / byte-order / width does not match the device's "
                        + "actual register layout (e.g. int16 declared for a 32-bit value, or wrong word order); "
                        + "action: no point emitted for this tag this cycle");
            }
        }
    }

    private CanonicalDataPoint DecodeEntry(
        ScanBlockEntry entry,
        ModbusRegisterClass registerClass,
        ModbusTransactionResult result,
        DateTime txTimestamp)
    {
        var tag = entry.Tag;
        object value;
        CanonicalValueType valueType;

        if (registerClass.IsBitRead())
        {
            var bits = result.BitPayload
                ?? throw new InvalidOperationException(
                    $"FC0{(byte)registerClass + 1} transaction returned no bit payload for tag '{tag.Name}'.");
            if (entry.Offset >= bits.Length)
            {
                throw new InvalidOperationException(
                    $"Bit payload for tag '{tag.Name}' is shorter than expected (offset {entry.Offset} >= length {bits.Length}).");
            }
            value = ModbusDecoder.DecodeBit(bits[entry.Offset]);
            valueType = CanonicalValueType.Boolean;
        }
        else
        {
            var registers = result.RegisterPayload
                ?? throw new InvalidOperationException(
                    $"FC0{(byte)registerClass + 1} transaction returned no register payload for tag '{tag.Name}'.");

            var defaultSpec = new ModbusDatatypeSpec(ModbusDatatype.UInt16);
            var spec = ModbusDatatypeParser.Parse(tag.Datatype, defaultSpec);
            var byteOrder = tag.ByteOrder ?? ModbusByteOrderExtensions.DefaultFor(spec.ByteCount);

            var raw = ModbusDecoder.DecodeRegisters(
                registers, entry.Offset, entry.Width, spec, byteOrder);

            value = spec.SupportsScaleOffset
                ? ModbusScaleOffset.Apply(raw, tag.Scale, tag.Offset)
                : raw;
            valueType = value is double
                ? CanonicalValueType.Double
                : spec.CanonicalType;
        }

        // G.5: when the adapter is currently in Degraded state (an outer-
        // level poll exception lowered it; per-block failures emit Bad
        // points but do not change adapter State), downgrade successful
        // emissions to Uncertain so OPC UA clients see
        // UncertainSubstituteValue rather than Good. Returns to Good once
        // RecordSuccess() runs at the end of PollAsync and lifts the
        // adapter back to Running.
        var quality = State == AdapterState.Degraded
            ? DataQuality.Uncertain
            : DataQuality.Good;

        return _factory!.CreatePoint(
            tagName: tag.Name,
            tagPath: $"{registerClass}/{tag.Address}",
            value: value,
            valueType: valueType,
            quality: quality,
            deviceTimestamp: txTimestamp,
            gatewayTimestamp: txTimestamp,
            unit: tag.Unit,
            qualityReason: quality == DataQuality.Uncertain
                ? "adapter in Degraded state — recent transaction failures"
                : null);
    }

    /// <summary>
    /// G.5: build a Quality=Bad canonical point for a tag whose block-read
    /// failed. Uses <see cref="CanonicalValueType.Null"/> + null Value per
    /// the canonical-data-model contract: "`Null` typically paired with
    /// `Bad` or `Uncertain` quality" (docs/core/canonical-data-model.md
    /// §`CanonicalValueType` catalog).
    /// <para>
    /// Downstream sinks distinguish Bad from Good by the Quality field, not
    /// the value. OPC UA Server (Milestone H) will translate Quality=Bad to
    /// the appropriate StatusCode while preserving the node's declared
    /// DataType — node typing is established at address-space build time
    /// from the configured tag definition, not per-update.
    /// </para>
    /// </summary>
    private CanonicalDataPoint EmitBadPoint(
        ScanBlockEntry entry,
        ModbusRegisterClass registerClass,
        DateTime txTimestamp,
        string qualityReason)
    {
        var tag = entry.Tag;
        return _factory!.CreatePoint(
            tagName: tag.Name,
            tagPath: $"{registerClass}/{tag.Address}",
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
        throw new NotSupportedException("Modbus TCP is a polling-only protocol.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        // Modbus has no on-wire metadata — browse returns what the user
        // defined in the config. F4 populates TagDefinitions via CSV import.
        var tagDefs = _config?.TagDefinitions ?? [];
        var list = new List<TagDefinition>(tagDefs.Count);
        foreach (var td in tagDefs)
        {
            var spec = ModbusDatatypeParser.Parse(td.Datatype,
                td.RegisterClass.IsBitRead()
                    ? new ModbusDatatypeSpec(ModbusDatatype.Bool)
                    : new ModbusDatatypeSpec(ModbusDatatype.UInt16));

            list.Add(new TagDefinition
            {
                Name = td.Name,
                Path = $"{td.RegisterClass}/{td.Address}",
                ValueType = spec.CanonicalType,
                Unit = td.Unit,
                Description = null,
                Writable = false,
                ProtocolMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["unitId"] = td.UnitId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["registerClass"] = td.RegisterClass.ToString(),
                    ["address"] = td.Address.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["scanRateMs"] = td.ScanRateMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["datatype"] = spec.Datatype.ToString(),
                },
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(list);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        if (config is not ModbusTcpSourceConfiguration modbusConfig)
        {
            return Task.FromResult(ValidationResult.Failure(
                ModbusErrors.ConfigWrongType,
                $"Expected ModbusTcpSourceConfiguration, got {config.GetType().Name}.",
                "config"));
        }

        var errors = new List<ValidationIssue>();

        // ADR-0033: the protocol id and encapsulation must agree. `modbustcp` is
        // native TCP only; `modbusrtu` is RTU framing (serial or over TCP).
        var isRtuProtocol = string.Equals(
            modbusConfig.ProtocolName, ModbusTcpSourceConfiguration.RtuProtocolNameConstant, StringComparison.OrdinalIgnoreCase);
        if (isRtuProtocol && modbusConfig.Encapsulation == ModbusEncapsulation.Tcp)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigInvalid,
                Message = "A 'modbusrtu' source must use SerialRtu or RtuOverTcp encapsulation. " +
                          "For native Modbus TCP, use a 'modbustcp' source.",
                Path = "Encapsulation",
            });
        }
        else if (!isRtuProtocol && modbusConfig.Encapsulation != ModbusEncapsulation.Tcp)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigInvalid,
                Message = $"A 'modbustcp' source must use Tcp encapsulation, not {modbusConfig.Encapsulation}. " +
                          "For RTU framing (serial or RTU-over-TCP), use a 'modbusrtu' source.",
                Path = "Encapsulation",
            });
        }

        if (modbusConfig.Encapsulation == ModbusEncapsulation.SerialRtu)
        {
            // Serial RTU addresses the slave by serial port, not host/port.
            if (string.IsNullOrWhiteSpace(modbusConfig.SerialPort))
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigMissingField,
                    Message = "SerialPort is required for SerialRtu encapsulation (e.g. \"COM3\" or \"/dev/ttyUSB0\").",
                    Path = "SerialPort",
                });
            }

            if (modbusConfig.BaudRate <= 0)
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigOutOfRange,
                    Message = "BaudRate must be > 0 (e.g. 9600, 19200, 38400).",
                    Path = "BaudRate",
                });
            }
        }
        else
        {
            // TCP / RTU-over-TCP address the slave by host:port.
            if (string.IsNullOrWhiteSpace(modbusConfig.Host))
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigMissingField,
                    Message = "Host is required.",
                    Path = "Host",
                });
            }

            if (modbusConfig.Port == 0)
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigOutOfRange,
                    Message = "Port must be > 0 (typical value: 502).",
                    Path = "Port",
                });
            }
        }

        if (modbusConfig.DefaultUnitId > 247 && modbusConfig.DefaultUnitId != 255)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = $"DefaultUnitId {modbusConfig.DefaultUnitId} is outside the Modbus range 0..247 (255 is the TCP 'any' sentinel).",
                Path = "DefaultUnitId",
            });
        }

        if (modbusConfig.ConnectTimeoutMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "ConnectTimeoutMs must be > 0.",
                Path = "ConnectTimeoutMs",
            });
        }

        if (modbusConfig.RequestTimeoutMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "RequestTimeoutMs must be > 0.",
                Path = "RequestTimeoutMs",
            });
        }

        if (modbusConfig.MaxTransactionRetries < 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "MaxTransactionRetries must be >= 0.",
                Path = "MaxTransactionRetries",
            });
        }

        if (modbusConfig.MaxBackoffMs < modbusConfig.InitialBackoffMs)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "MaxBackoffMs must be >= InitialBackoffMs.",
                Path = "MaxBackoffMs",
            });
        }

        if (modbusConfig.BackoffMultiplier < 1.0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "BackoffMultiplier must be >= 1.0.",
                Path = "BackoffMultiplier",
            });
        }

        if (modbusConfig.CircuitBreakerThreshold < 1)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "CircuitBreakerThreshold must be >= 1.",
                Path = "CircuitBreakerThreshold",
            });
        }

        if (modbusConfig.MaxGapRegisters < 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "MaxGapRegisters must be >= 0 (0 disables coalescing).",
                Path = "MaxGapRegisters",
            });
        }

        // DeviceClass: Modbus is protocol-agnostic — the user MUST declare
        // what kind of device this Modbus connection fronts so the per-tag
        // MQTT topic is correct. CNC adapters (FOCAS2, MTConnect) default
        // to "cnc" because the protocol implies the role; Modbus has no
        // such implicit role.
        // Vocabulary + regex documented in
        // shared-knowledge/contracts/eremos-per-tag-mqtt.md.
        if (string.IsNullOrWhiteSpace(modbusConfig.DeviceClass))
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigMissingField,
                Message = "DeviceClass is required for Modbus sources (e.g. \"plc\", \"meter\", \"daq\"). " +
                          "See shared-knowledge/contracts/eremos-per-tag-mqtt.md for the vocabulary.",
                Path = "DeviceClass",
            });
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(
            modbusConfig.DeviceClass, "^[a-z0-9-]+$"))
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigInvalid,
                Message = $"DeviceClass '{modbusConfig.DeviceClass}' is invalid. " +
                          "Must match ^[a-z0-9-]+$ (lowercase ASCII alphanumeric and hyphens).",
                Path = "DeviceClass",
            });
        }

        // Per-tag validation delegated to ModbusTagValidator so the F4 CSV
        // importer applies the same rules at import time.
        for (var i = 0; i < modbusConfig.TagDefinitions.Count; i++)
        {
            ModbusTagValidator.Validate(
                modbusConfig.TagDefinitions[i],
                pathPrefix: $"TagDefinitions[{i}]",
                errors: errors,
                addressBase: modbusConfig.AddressBase);
        }

        if (errors.Count > 0)
        {
            return Task.FromResult(new ValidationResult { IsValid = false, Errors = errors });
        }

        return Task.FromResult(ValidationResult.Success());
    }

    /// <summary>
    /// Execute a single read transaction directly (no planner). Kept for
    /// F1-era tests and for the F3 integration suite's explicit FC-dispatch
    /// assertions; production code path flows through <see cref="PollAsync"/>.
    /// </summary>
    internal async Task<ModbusTransactionResult> ExecuteAsyncInternal(
        ModbusReadRequest request, CancellationToken ct)
    {
        if (_executor is null || _connectionManager is null)
        {
            throw new InvalidOperationException(
                $"ModbusTcpSourceAdapter '{_instanceId}' is not initialized.");
        }

        Interlocked.Increment(ref _transactionsExecuted);
        var result = await _executor.ExecuteAsync(request, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            Interlocked.Increment(ref _transactionFailures);
            if (result.Error is { } err)
            {
                RecordFailure(err);
            }
        }
        else
        {
            RecordSuccess();
        }
        return result;
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
                _logger.LogDebug(ex,
                    "ModbusTcpSourceAdapter '{Id}' error during dispose stop.",
                    _instanceId);
            }
        }

        if (_connectionManager is not null)
        {
            await _connectionManager.DisposeAsync().ConfigureAwait(false);
        }
        else if (_client is not null)
        {
            // ConnectionManager usually owns the client; if init failed we
            // still need to tear the client down ourselves. (_client is null
            // when a production adapter is disposed before InitializeAsync.)
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Initiates a lock-free transport close (so a wedged read holding the wire
    /// lock can be interrupted without first acquiring it), then resolves the
    /// durable <c>Completion</c> when the wire is idle (the read worker exited).
    /// Idempotent: repeated calls return the same operation.
    /// </remarks>
    public AdapterRetirementOperation BeginRetirement(AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_retirementSync)
        {
            return _retirement ??= ModbusRetirement.Begin(
                initiateClose: () => _connectionManager?.Disconnect(),
                awaitWorkerExit: () => _connectionManager is null
                    ? Task.CompletedTask
                    : _connectionManager.WaitForWireIdleAsync(),
                context);
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
                $"ModbusTcpSourceAdapter '{_instanceId}' cannot transition from {State} to {target}.");
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

    /// <summary>Human-readable name for a raw Modbus slave exception code.</summary>
    private static string DescribeSlaveExceptionCode(byte code) => code switch
    {
        0x01 => "Illegal Function",
        0x02 => "Illegal Data Address",
        0x03 => "Illegal Data Value",
        0x04 => "Slave Device Failure",
        0x05 => "Acknowledge",
        0x06 => "Slave Device Busy",
        0x08 => "Memory Parity Error",
        0x0A => "Gateway Path Unavailable",
        0x0B => "Gateway Target Device Failed To Respond",
        _ => "Unknown",
    };

    /// <summary>
    /// Plain-English, operator-actionable Quality=Bad reason for a slave
    /// exception — shown in the Live Data Tap / diagnostics. The raw Modbus
    /// device text is meaningless to a non-Modbus operator; this tells them
    /// exactly what to change (most often the source's Address base).
    /// </summary>
    private static string DescribeOperatorReason(byte slaveExceptionCode) => slaveExceptionCode switch
    {
        0x02 => "The device has no register at this address (Illegal Data Address). "
              + "This usually means the wrong \"Address base\" is selected on the source. "
              + "Pick One-based if you entered addresses like 33 or 37; Modicon (4xxxx) if you "
              + "entered 40001 or 40033; or Zero-based if you entered the exact register number. "
              + "Also check the register class (Holding vs Input) matches the device.",
        0x01 => "The device does not support this request (Illegal Function). "
              + "Try the other register class — Holding vs Input register, or Coil vs Discrete input.",
        0x03 => "The device rejected the requested value/length (Illegal Data Value) — "
              + "check the tag's datatype/width for this address.",
        0x04 => "The device reported an internal failure (Slave Device Failure).",
        0x06 => "The device is busy — reduce the poll rate or allow more retries.",
        0x0A or 0x0B => "The gateway could not reach the target device (Gateway Target Failed) — "
              + "check the Unit/Slave id and the gateway routing.",
        _ => $"The device rejected the request (Modbus exception 0x{slaveExceptionCode:X2} — "
           + $"{DescribeSlaveExceptionCode(slaveExceptionCode)}).",
    };

    /// <summary>
    /// Probe the device to find which Address base would make this block's
    /// addresses valid. Recovers the operator-entered address from the first
    /// tag's (already-normalised) wire address, then tries each OTHER base's
    /// wire mapping with a single-register read — the base the device accepts is
    /// the correct one. Cached per block start-address for
    /// <see cref="BaseDetectThrottle"/>. Returns null when no base works (the
    /// register is genuinely absent on the device).
    /// </summary>
    private async Task<(ModbusAddressBase Base, ModbusRegisterClass Rc)?> DetectWorkingBaseThrottledAsync(
        ScanBlock block, ScanGroup group, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        if (_baseDetectCache.TryGetValue(block.StartAddress, out var cached) && now < cached.Until)
        {
            return cached.Result;
        }

        var currentBase = _config!.AddressBase;
        var currentRc = group.RegisterClass;
        var entered = ReconstructEnteredAddress(block.Entries[0].Tag.Address, currentBase, currentRc);

        (ModbusAddressBase Base, ModbusRegisterClass Rc)? found = null;
        // Try the current register class first, then its natural alternate, and
        // every address base under each — the first combination the device
        // accepts is the correct one to recommend.
        foreach (var rc in RegisterClassCandidates(currentRc))
        {
            foreach (var candidate in new[]
                     {
                         ModbusAddressBase.ZeroBased,
                         ModbusAddressBase.OneBased,
                         ModbusAddressBase.Modicon,
                     })
            {
                if ((candidate == currentBase && rc == currentRc)
                    || !candidate.TryToZeroBased(entered, rc, out var wire))
                {
                    continue;
                }

                ModbusTransactionResult probe;
                try
                {
                    probe = await _executor!
                        .ExecuteAsync(new ModbusReadRequest(group.UnitId, rc, (ushort)wire, 1), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // a failed probe must never break the poll cycle
                catch
                {
                    continue;
                }
#pragma warning restore CA1031
                if (probe.IsSuccess)
                {
                    found = (candidate, rc);
                    break;
                }
            }

            if (found is not null)
            {
                break;
            }
        }

        // Cache a positive answer for a while; a null (nothing worked yet) only
        // briefly, so a transient first-probe miss right after a reload doesn't
        // stick — the next poll re-probes and finds the real base.
        _baseDetectCache[block.StartAddress] = (now + (found is not null ? BaseDetectThrottle : BaseDetectRetry), found);
        return found;
    }

    /// <summary>
    /// Register classes to probe when detecting a mismatch: the current one
    /// first (a base-only mismatch is the common case), then its natural
    /// alternate (Holding↔Input, Coil↔DiscreteInput).
    /// </summary>
    private static IEnumerable<ModbusRegisterClass> RegisterClassCandidates(ModbusRegisterClass current)
    {
        yield return current;
        var alt = current switch
        {
            ModbusRegisterClass.HoldingRegister => ModbusRegisterClass.InputRegister,
            ModbusRegisterClass.InputRegister => ModbusRegisterClass.HoldingRegister,
            ModbusRegisterClass.Coil => ModbusRegisterClass.DiscreteInput,
            ModbusRegisterClass.DiscreteInput => ModbusRegisterClass.Coil,
            _ => current,
        };
        if (alt != current)
        {
            yield return alt;
        }
    }

    /// <summary>Plain-language name for a register class (matches the wizard's dropdown).</summary>
    private static string RegisterClassDisplayName(ModbusRegisterClass rc) => rc switch
    {
        ModbusRegisterClass.HoldingRegister => "Holding register",
        ModbusRegisterClass.InputRegister => "Input register",
        ModbusRegisterClass.Coil => "Coil",
        ModbusRegisterClass.DiscreteInput => "Discrete input",
        _ => rc.ToString(),
    };

    /// <summary>Reverse the address-base conversion to recover the operator-entered address.</summary>
    private static int ReconstructEnteredAddress(
        ushort wireAddress, ModbusAddressBase currentBase, ModbusRegisterClass rc) => currentBase switch
        {
            ModbusAddressBase.OneBased => wireAddress + 1,
            ModbusAddressBase.Modicon => wireAddress + ModbusAddressBaseExtensions.ModiconOffset(rc),
            _ => wireAddress,
        };

    /// <summary>Friendly display name for an address base.</summary>
    private static string BaseDisplayName(ModbusAddressBase b) => b switch
    {
        ModbusAddressBase.OneBased => "One-based",
        ModbusAddressBase.Modicon => "Modicon (4xxxx)",
        _ => "Zero-based",
    };

    /// <summary>
    /// Per-tag Quality=Bad reason for an Illegal Data Address failure: name the
    /// exact Address base the device accepts, or say the register is genuinely
    /// absent when no base works.
    /// </summary>
    private static string AddressBaseReason(
        int enteredAddress, ModbusAddressBase currentBase, ModbusRegisterClass currentRc,
        (ModbusAddressBase Base, ModbusRegisterClass Rc)? detected)
    {
        if (detected is { } d)
        {
            var baseChanged = d.Base != currentBase;
            var rcChanged = d.Rc != currentRc;
            var baseName = BaseDisplayName(d.Base);
            var rcName = RegisterClassDisplayName(d.Rc);

            if (baseChanged && rcChanged)
            {
                return $"Wrong settings. Your address {enteredAddress} works as a {rcName} with "
                     + $"\"{baseName}\" addressing — on this source set Register class to \"{rcName}\" "
                     + $"and Address base to \"{baseName}\".";
            }
            if (rcChanged)
            {
                return $"Wrong register type. Your address {enteredAddress} is a {rcName}, not a "
                     + $"{RegisterClassDisplayName(currentRc)} — change this source's Register class to \"{rcName}\".";
            }
            return $"Address base is wrong. Your address {enteredAddress} works as {baseName} — change "
                 + $"this source's Address base to \"{baseName}\". (Currently set to {BaseDisplayName(currentBase)}.)";
        }

        // Nothing worked under any address base OR register type — the address
        // itself is wrong (or points at a different unit). Keep this plain.
        return $"This device has no data at address {enteredAddress}. It was rejected with every address "
             + $"setting we tried, so please check that {enteredAddress} is the correct address in the "
             + $"device's manual (and that the Unit/Slave id is right).";
    }

    /// <summary>
    /// Best-guess remediation text for a failed read, keyed first off the raw
    /// slave-exception code (most specific) then the error category.
    /// </summary>
    private static string DescribeProbableCause(AdapterError? error, byte slaveExceptionCode)
    {
        switch (slaveExceptionCode)
        {
            case 0x02:
                return "device returned Illegal Data Address — the configured register addresses do not exist "
                     + "on this device. Modbus 4xxxx holding registers are ZERO-BASED here: enter 40001 as 0, "
                     + "40033 as 32, etc. Also confirm the register class (holding FC03 vs input FC04) matches.";
            case 0x01:
                return "device returned Illegal Function — it does not support this function code / register class.";
            case 0x0A:
            case 0x0B:
                return "gateway could not reach the target unit — verify the unit/slave id and the gateway routing.";
            case 0x06:
                return "slave reported busy — reduce the poll rate or allow more retries.";
        }

        if (error is null)
        {
            return "unknown.";
        }
        return error.Category switch
        {
            ErrorCategory.Network =>
                "no or broken response over the socket (timeout/reset) — check reachability, the unit id, and that "
                    + "the endpoint is a real Modbus server (not just an open TCP port).",
            ErrorCategory.DeviceState =>
                "device/gateway cannot serve this unit id — verify the unit/slave id.",
            ErrorCategory.ResourceExhausted =>
                "slave reported busy — reduce poll rate / allow retries.",
            ErrorCategory.Configuration =>
                "request rejected locally before send (quantity/range) — check the tag address/width.",
            ErrorCategory.Protocol =>
                "device rejected the request — verify register addresses are zero-based (4xxxx -> subtract 40001) "
                    + "and the register class (holding vs input) matches the device.",
            _ => error.Category.ToString(),
        };
    }
}
