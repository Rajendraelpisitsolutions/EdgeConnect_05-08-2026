// ============================================================================
// File: BrotherHttpSourceAdapter.cs
// Purpose: ISourceAdapter implementation for Brother CNCs via the built-in
//          HTTP web-monitoring interface. Wires together the IBrotherHttpApi
//          (real or demo), the six per-endpoint collectors, the connection
//          manager, the §6.2 precedence chain, and the v3.1 §B determinism
//          + observability locks.
//
// v3.1 §B locks wired here:
//   §B.1 atomic batch          — PollAsync assembles one IReadOnlyList in-memory
//   §B.2 single timestamp      — DateTime.UtcNow captured at cycle start;
//                                shared as DeviceTimestamp + GatewayTimestamp
//   §B.3 single-flight         — Interlocked.CompareExchange gate; overrun
//                                increments _pollOverruns + returns empty
//   §B.4 no fire-and-forget    — Only async fan-out is Task.WhenAll across
//                                the six IBrotherHttpApi calls, fully awaited
//   §B.5 metrics surface       — Meter "ElpisEdgeConnect.Sources.BrotherHttp"
//                                exposes _pollDuration histogram,
//                                _endpointFailures + _pollOverruns counters
//
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §3 + §6.2,
//            docs/sessions/2026-05-21-mp24-brother-http-plan-v3.1.md §B
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.BrotherHttp.Collectors;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Brother HTTP source adapter. Polling + Browse capabilities.
/// </summary>
public sealed class BrotherHttpSourceAdapter : ISourceAdapter, Core.Adapters.Retirement.ISourceRetirement
{
    // ── v3.1 §B.5 — metric surface (process-wide Meter; instance-tagged) ──
    private static readonly Meter _meter = new("ElpisEdgeConnect.Sources.BrotherHttp");
    private static readonly Histogram<double> _pollDuration =
        _meter.CreateHistogram<double>("elpis_edgeconnect_brother_poll_duration_ms", "ms",
            "Brother HTTP poll cycle duration");
    private static readonly Counter<long> _endpointFailures =
        _meter.CreateCounter<long>("elpis_edgeconnect_brother_endpoint_failures_total", "events",
            "Per-endpoint failure count (one of mcninfo, cycletime, wkcntr, atc_tools, alarms, maintnotice)");
    private static readonly Counter<long> _pollOverruns =
        _meter.CreateCounter<long>("elpis_edgeconnect_brother_poll_overruns_total", "events",
            "Poll cycles skipped because the previous cycle was still in-flight (v3.1 §B.3)");

    private readonly string _instanceId;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _identity;

    private IBrotherHttpApi? _api;
    private BrotherHttpSourceConfiguration? _config;
    private BrotherHttpConnectionManager? _connectionManager;
    private CanonicalDataPointFactory? _factory;
    private IReadOnlyList<string>? _normalizedDataPoints;

    // v3.1 §B.3 — single-flight gate. 0 = idle, 1 = poll in-flight.
    private int _pollInFlight;

    // Slice 0 commit 3.0 retirement: supervisor-driven PULL adapter. The in-flight
    // poll (drained via this gate) is the only adapter-owned execution surface —
    // no callback ingress, reconnect coordinator, timer, or background loop;
    // SubscribeAsync is unsupported. A wedged HTTP poll keeps Completion pending.
    private readonly Core.Adapters.Retirement.PollQuiescenceGate _pollGate = new();
    private readonly object _retirementSync = new();
    private Core.Adapters.Retirement.AdapterRetirementOperation? _retirement;

    // Health bookkeeping (exposed via CheckHealthAsync metrics dictionary).
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;

    /// <summary>
    /// Production constructor. Real <see cref="IBrotherHttpApi"/> is
    /// constructed in <see cref="InitializeAsync"/> based on the resolved
    /// <see cref="BrotherHttpSourceConfiguration"/> (BaseUrl + Timeout). When
    /// <see cref="BrotherHttpDemoModeOptions.IsEnabled"/> is true, the demo
    /// backend is used instead.
    /// </summary>
    public BrotherHttpSourceAdapter(
        string instanceId,
        IHttpClientFactory httpClientFactory,
        ILogger<BrotherHttpSourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _identity = gatewayIdentity;
    }

    /// <summary>
    /// Test/internal constructor accepting an injected <see cref="IBrotherHttpApi"/>.
    /// </summary>
    internal BrotherHttpSourceAdapter(
        string instanceId,
        IBrotherHttpApi api,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _identity = gatewayIdentity;
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => BrotherHttpSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc/>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.Browse;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <inheritdoc/>
    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        TransitionState(AdapterState.Initializing);

        if (config is not BrotherHttpSourceConfiguration brotherConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"BrotherHttpSourceAdapter requires BrotherHttpSourceConfiguration, got {config.GetType().Name}.");
        }

        _config = brotherConfig;
        _connectionManager = new BrotherHttpConnectionManager(
            brotherConfig.FaultThresholdConsecutiveFailures, _instanceId, _logger);

        // Construct the API on the public-ctor path. The internal test ctor
        // injects _api directly and skips this branch.
        if (_api is null)
        {
            if (BrotherHttpDemoModeOptions.IsEnabled)
            {
                _logger.LogCritical(BrotherHttpDemoModeOptions.StartupCriticalMessage);
                _api = new BrotherHttpDemoApi();
            }
            else if (_httpClientFactory is not null)
            {
                _api = new BrotherHttpHttpApi(
                    _httpClientFactory,
                    brotherConfig.BaseUrl,
                    brotherConfig.TimeoutSeconds,
                    _instanceId,
                    _logger);
            }
            else
            {
                TransitionState(AdapterState.Failed);
                throw new InvalidOperationException(
                    "BrotherHttpSourceAdapter has no IHttpClientFactory and no injected IBrotherHttpApi.");
            }
        }

        _factory = new CanonicalDataPointFactory(
            gatewayId: brotherConfig.GatewayId ?? _identity?.GatewayId ?? "gateway",
            sourceInstanceId: _instanceId,
            protocolName: ProtocolName,
            deviceId: brotherConfig.DeviceId,
            deviceName: brotherConfig.DeviceName,
            deviceClass: brotherConfig.DeviceClass ?? "cnc");

        _normalizedDataPoints = brotherConfig.DataPoints.Count == 0
            ? null
            : BrotherHttpSourceConfiguration.NormalizeDataPoints(brotherConfig.DataPoints);

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "BrotherHttpSourceAdapter '{InstanceId}' initialized for {BaseUrl}.",
            _instanceId, brotherConfig.BaseUrl);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        if (_config is null || _api is null) throw new InvalidOperationException("Adapter not initialised.");
        TransitionState(AdapterState.Starting);

        var probe = await _api.GetMachineInfoAsync(ct).ConfigureAwait(false);
        if (probe is null)
        {
            // ADR-0003 fail-soft: initial connect failure must not crash the host.
            // Log a warning, transition to Running (same as FOCAS2), and let the
            // polling loop retry — the connection manager will fault after
            // FaultThresholdConsecutiveFailures consecutive failures.
            _connectionManager!.RecordHealthFailure();
            _lastError = new AdapterError
            {
                Code = BrotherErrors.HttpUnreachable,
                Category = ErrorCategory.Network,
                Message = "Brother CNC unreachable on first HTTPD_MCNINFO probe.",
                Retryable = true,
            };
            _logger.LogWarning(
                "BrotherHttpSourceAdapter '{InstanceId}' initial probe failed at {BaseUrl} — will retry on poll.",
                _instanceId, _config.BaseUrl);
            TransitionState(AdapterState.Running);
            return;
        }

        _connectionManager!.RecordHealthSuccess();
        _lastSuccessAt = DateTime.UtcNow;
        TransitionState(AdapterState.Running);
        _logger.LogInformation(
            "BrotherHttpSourceAdapter '{InstanceId}' connected to {BaseUrl}.",
            _instanceId, _config.BaseUrl);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct)
    {
        if (State == AdapterState.Running || State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Stopping);
        }
        TransitionState(AdapterState.Stopped);
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
                Retirement.BrotherRetirementDetailCodes.Initiated,
                Retirement.BrotherRetirementDetailCodes.WorkerIdleProven,
                context);
        }
    }

    /// <inheritdoc/>
    public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct)
    {
        var level = State switch
        {
            AdapterState.Running => HealthLevel.Healthy,
            AdapterState.Degraded => HealthLevel.Degraded,
            AdapterState.Failed => HealthLevel.Unhealthy,
            _ => HealthLevel.Unknown,
        };
        var metrics = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["pollAttempts"] = _pollAttempts,
            ["pollSuccesses"] = _pollSuccesses,
            ["pollFailures"] = _pollFailures,
            ["consecutiveFailures"] = _connectionManager?.ConsecutiveFailures ?? 0,
            ["everConnected"] = _connectionManager?.EverConnected ?? false,
        };
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
        if (_config is null || _factory is null || _connectionManager is null || _api is null)
        {
            throw new InvalidOperationException("Adapter not initialised.");
        }

        // v3.1 §B.3 — single-flight gate. Skip overlap, increment metric, return empty.
        if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
        {
            _pollOverruns.Add(1, new KeyValuePair<string, object?>("source", _instanceId));
            _logger.LogWarning(
                "Brother poll cycle skipped — previous cycle still in-flight (source '{InstanceId}').",
                _instanceId);
            return Array.Empty<CanonicalDataPoint>();
        }

        try
        {
            // v3.1 §B.2 — single timestamp captured at cycle start.
            var pollStartedAtUtc = DateTime.UtcNow;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _pollAttempts++;

            // v3.1 §B.4 — no fire-and-forget; v3.1 §B.1 — atomic batch via WhenAll.
            var machineInfoTask = _api.GetMachineInfoAsync(ct);
            var cycleTimeTask = _api.GetCycleTimeAsync(ct);
            var workCountersTask = _api.GetWorkCountersAsync(ct);
            var atcToolsTask = _api.GetAtcToolsAsync(ct);
            var alarmsTask = _api.GetAlarmsAsync(ct);
            var maintenanceTask = _api.GetMaintenanceNoticesAsync(ct);

            await Task.WhenAll(
                machineInfoTask, cycleTimeTask, workCountersTask,
                atcToolsTask, alarmsTask, maintenanceTask).ConfigureAwait(false);

            var session = new BrotherPollSession();
            MachineInfoCollector.Collect(machineInfoTask.Result, session);
            CycleTimeCollector.Collect(cycleTimeTask.Result, session);
            WorkCounterCollector.Collect(workCountersTask.Result, session);
            AtcToolsCollector.Collect(atcToolsTask.Result, session);
            AlarmCollector.Collect(alarmsTask.Result, session);
            MaintenanceCollector.Collect(maintenanceTask.Result, session);

            if (session.MachineInfoEndpointFailed)
            {
                _connectionManager.RecordHealthFailure();
                if (_connectionManager.ShouldFault && State != AdapterState.Failed)
                {
                    _lastError = new AdapterError
                    {
                        Code = BrotherErrors.HttpUnreachable,
                        Category = ErrorCategory.Network,
                        Message = $"Health endpoint failed {_connectionManager.ConsecutiveFailures} consecutive times.",
                        Retryable = true,
                    };
                    TransitionState(AdapterState.Failed);
                }
            }
            else
            {
                _connectionManager.RecordHealthSuccess();
                _lastSuccessAt = pollStartedAtUtc;
            }

            // v3.1 §B.5 — per-endpoint failure metrics.
            RecordEndpointMetric(session.MachineInfoEndpointFailed, "mcninfo");
            RecordEndpointMetric(session.CycleTimeEndpointFailed, "cycletime");
            RecordEndpointMetric(session.WorkCountersEndpointFailed, "wkcntr");
            RecordEndpointMetric(session.AtcToolsEndpointFailed, "atc_tools");
            RecordEndpointMetric(session.AlarmsEndpointFailed, "alarms");
            RecordEndpointMetric(session.MaintenanceNoticesEndpointFailed, "maintnotice");

            // v3.1 §B.1 atomic batch — build the canonical points in-memory.
            var points = BuildPoints(session, pollStartedAtUtc);

            var outcome = ClassifyOutcome(session);
            _pollDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("source", _instanceId),
                new KeyValuePair<string, object?>("outcome", outcome));

            if (outcome == "failed") _pollFailures++;
            else _pollSuccesses++;

            return points;
        }
        finally
        {
            Interlocked.Exchange(ref _pollInFlight, 0);
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct)
    {
        throw new InvalidOperationException(
            "Brother HTTP does not support Subscription capability — use PollAsync.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        var defs = new List<TagDefinition>(BrotherTagMap.StaticTags.Count);
        foreach (var entry in BrotherTagMap.StaticTags)
        {
            defs.Add(new TagDefinition
            {
                Name = entry.TagName,
                Path = entry.TagPath,
                ValueType = entry.ValueType,
                Unit = entry.Unit,
                Description = entry.Description,
                Writable = false,
            });
        }
        return Task.FromResult<IReadOnlyList<TagDefinition>>(defs);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        var errors = new List<ValidationIssue>();
        var warnings = new List<ValidationIssue>();

        if (config is not BrotherHttpSourceConfiguration brotherConfig)
        {
            errors.Add(new ValidationIssue
            {
                Code = BrotherErrors.ConfigMissingField,
                Message = $"Expected BrotherHttpSourceConfiguration, got {config.GetType().Name}.",
                Path = "Configuration",
            });
            return Task.FromResult(new ValidationResult { IsValid = false, Errors = errors });
        }

        if (string.IsNullOrWhiteSpace(brotherConfig.BaseUrl))
        {
            errors.Add(new ValidationIssue
            {
                Code = BrotherErrors.ConfigMissingField,
                Message = "BaseUrl is required.",
                Path = nameof(brotherConfig.BaseUrl),
            });
        }

        if (brotherConfig.TimeoutSeconds <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = BrotherErrors.ConfigOutOfRange,
                Message = $"TimeoutSeconds must be > 0; got {brotherConfig.TimeoutSeconds}.",
                Path = nameof(brotherConfig.TimeoutSeconds),
            });
        }

        if (brotherConfig.FaultThresholdConsecutiveFailures <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = BrotherErrors.ConfigOutOfRange,
                Message = $"FaultThresholdConsecutiveFailures must be >= 1; got {brotherConfig.FaultThresholdConsecutiveFailures}.",
                Path = nameof(brotherConfig.FaultThresholdConsecutiveFailures),
            });
        }

        var pollClass = BrotherHttpSourceConfiguration.ClassifyPollInterval(brotherConfig.PollIntervalMs);
        if (pollClass == BrotherHttpSourceConfiguration.PollIntervalClassification.TooFast)
        {
            errors.Add(new ValidationIssue
            {
                Code = BrotherErrors.PollTooFast,
                Message = $"PollIntervalMs={brotherConfig.PollIntervalMs} below hard minimum " +
                          $"{BrotherHttpSourceConfiguration.PollIntervalHardMinimumMs} ms (Q10).",
                Path = nameof(brotherConfig.PollIntervalMs),
            });
        }
        else if (pollClass == BrotherHttpSourceConfiguration.PollIntervalClassification.Warning)
        {
            warnings.Add(new ValidationIssue
            {
                Code = BrotherErrors.PollTooFastWarning,
                Message = $"PollIntervalMs={brotherConfig.PollIntervalMs} below the recommended " +
                          $"{BrotherHttpSourceConfiguration.PollIntervalSoftWarningMs} ms threshold.",
                Path = nameof(brotherConfig.PollIntervalMs),
            });
        }

        var normalized = BrotherHttpSourceConfiguration.NormalizeDataPoints(brotherConfig.DataPoints);
        foreach (var entry in normalized)
        {
            if (!BrotherHttpSourceConfiguration.IsCatalogMember(entry))
            {
                errors.Add(new ValidationIssue
                {
                    Code = BrotherErrors.UnknownDataPoint,
                    Message = $"DataPoints entry '{entry}' does not match any catalog leaf or prefix.",
                    Path = nameof(brotherConfig.DataPoints),
                });
            }
        }

        return Task.FromResult(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
        });
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // HttpClient is owned by IHttpClientFactory's lifecycle (Pattern C).
        // Nothing per-instance to dispose synchronously.
        return ValueTask.CompletedTask;
    }

    // ── §6.2 precedence chain + canonical-point emission ─────────────────

    private List<CanonicalDataPoint> BuildPoints(BrotherPollSession session, DateTime stamp)
    {
        var points = new List<CanonicalDataPoint>(64);

        bool ShouldEmit(string tagPath)
        {
            if (_normalizedDataPoints is null) return true;
            foreach (var filter in _normalizedDataPoints)
            {
                if (string.Equals(filter, tagPath, StringComparison.OrdinalIgnoreCase)) return true;
                if (tagPath.StartsWith(filter + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void Emit(BrotherTagMapEntry entry, object value)
        {
            if (!ShouldEmit(entry.TagPath)) return;
            points.Add(_factory!.CreatePoint(
                tagName: entry.TagName, tagPath: entry.TagPath,
                value: value, valueType: entry.ValueType,
                quality: DataQuality.Good,
                deviceTimestamp: stamp, gatewayTimestamp: stamp,
                unit: entry.Unit));
        }

        if (session.Hostname is { } host) Emit(BrotherTagMap.MachineInfoHostname, host);
        if (session.Model is { } model) Emit(BrotherTagMap.MachineInfoModel, model);
        if (session.StatusCode is { } sc) Emit(BrotherTagMap.MachineInfoStatusCode, sc);

        // §6.2 Status/State precedence
        var (state, running) = MapStatusCode(session.StatusCode);
        if (session.InformationalAlarmMessage is not null)
        {
            state = "STOP";
            running = "Idle";
        }
        if (session.ActiveAlarms.Count > 0)
        {
            state = "ALARM";
        }
        Emit(BrotherTagMap.StatusState, state);
        Emit(BrotherTagMap.StatusRunning, running);
        Emit(BrotherTagMap.StatusMode, "MEM");
        Emit(BrotherTagMap.StatusAutoMode, "MEM");
        Emit(BrotherTagMap.StatusEmergencyStop, false);

        // §6.2 Status/Warning precedence
        string? warning = null;
        var notifiedNotice = FirstNotifiedNotice(session);
        if (notifiedNotice is not null)
        {
            warning = $"Maintenance ({notifiedNotice.Description})";
        }
        if (session.MaintenanceAlarmMessages.Count > 0)
        {
            warning = $"Maintenance ({session.MaintenanceAlarmMessages[0]})";
        }
        if (session.InformationalAlarmMessage is not null)
        {
            warning = session.InformationalAlarmMessage;
        }
        if (session.ActiveAlarms.Count > 0)
        {
            warning = session.ActiveAlarms[0].Message;
        }
        if (warning is not null)
        {
            Emit(BrotherTagMap.StatusWarning, warning);
        }

        if (session.Program is { } prog) Emit(BrotherTagMap.ProgramActive, prog);

        if (session.CycleSeconds is { } cs) Emit(BrotherTagMap.CycleTimeCycle, cs);
        if (session.CuttingSeconds is { } cut) Emit(BrotherTagMap.CycleTimeCutting, cut);
        if (session.OperationHours is { } op) Emit(BrotherTagMap.CycleTimeOperation, op);
        if (session.PowerOnHours is { } po) Emit(BrotherTagMap.CycleTimePowerOn, po);
        if (session.EndCounter is { } ec) Emit(BrotherTagMap.CycleTimeEndCounter, ec);
        if (session.CuttingRatioPercent is { } ratio) Emit(BrotherTagMap.CycleTimeCuttingRatioPercent, ratio);

        if (session.PartsCount is { } parts) Emit(BrotherTagMap.ProductionPartsCount, parts);
        foreach (var pair in session.Counters)
        {
            var slotIdx = pair.Key;
            var counter = pair.Value;
            if (counter.Count is { } cnt) Emit(BrotherTagMap.ProductionCounterCount(slotIdx), cnt);
            if (counter.Target is { } tgt) Emit(BrotherTagMap.ProductionCounterTarget(slotIdx), tgt);
        }

        if (session.ActiveToolNumber is { } activeNo) Emit(BrotherTagMap.ToolsActiveNumber, activeNo);
        Emit(BrotherTagMap.ToolsMagazineSize, session.Magazine.Count);
        foreach (var slot in session.Magazine)
        {
            Emit(BrotherTagMap.ToolsMagazineNumber(slot.Slot), slot.ToolNumber);
            Emit(BrotherTagMap.ToolsMagazineLength(slot.Slot), slot.Length);
            Emit(BrotherTagMap.ToolsMagazineRadius(slot.Slot), slot.Radius);
            Emit(BrotherTagMap.ToolsMagazineIsActive(slot.Slot), slot.IsActive);
            if (slot.Name is { } n) Emit(BrotherTagMap.ToolsToolName(slot.ToolNumber), n);
            if (slot.Type is { } t) Emit(BrotherTagMap.ToolsToolType(slot.ToolNumber), t);
            if (slot.Life is { } l) Emit(BrotherTagMap.ToolsToolLife(slot.ToolNumber), l);
        }

        Emit(BrotherTagMap.AlarmsActiveCount, session.ActiveAlarms.Count);
        for (var i = 0; i < session.ActiveAlarms.Count; i++)
        {
            var a = session.ActiveAlarms[i];
            Emit(BrotherTagMap.AlarmsActiveNumber(i), a.Number);
            Emit(BrotherTagMap.AlarmsActiveType(i), "Brother");
            Emit(BrotherTagMap.AlarmsActiveMessage(i), a.Message);
        }

        if (session.MaintenanceAlarmMessages.Count > 0)
        {
            Emit(BrotherTagMap.MaintenanceWarning, string.Join("; ", session.MaintenanceAlarmMessages));
            Emit(BrotherTagMap.MaintenanceWarningCount, session.MaintenanceAlarmMessages.Count);
        }
        Emit(BrotherTagMap.MaintenanceNoticeCount, session.Notices.Count);
        if (session.Notices.Count > 0)
        {
            var summary = new List<string>(session.Notices.Count);
            for (var i = 0; i < session.Notices.Count; i++)
            {
                var n = session.Notices[i];
                var idx = i + 1;
                Emit(BrotherTagMap.MaintenanceNoticeDescription(idx), n.Description);
                Emit(BrotherTagMap.MaintenanceNoticeCondition(idx), n.Condition);
                Emit(BrotherTagMap.MaintenanceNoticeStatus(idx), n.Status);
                Emit(BrotherTagMap.MaintenanceNoticeCurrent(idx), n.Current);
                Emit(BrotherTagMap.MaintenanceNoticeLimit(idx), n.Limit);
                Emit(BrotherTagMap.MaintenanceNoticeState(idx), n.State);
                if (n.DuePercent is { } d) Emit(BrotherTagMap.MaintenanceNoticeDuePercent(idx), d);

                var summaryEntry = n.Description;
                if (!string.IsNullOrEmpty(n.Notified) && n.Notified != "None")
                {
                    summaryEntry += $" ({n.Notified})";
                }
                summary.Add(summaryEntry);
            }
            Emit(BrotherTagMap.MaintenanceDueSummary, string.Join("; ", summary));
        }

        return points;
    }

    private static MaintenanceNoticeEntry? FirstNotifiedNotice(BrotherPollSession session)
    {
        foreach (var n in session.Notices)
        {
            if (string.Equals(n.Notified, "Notified", StringComparison.OrdinalIgnoreCase))
            {
                return n;
            }
        }
        return null;
    }

    private static (string State, string Running) MapStatusCode(int? code) => code switch
    {
        0 => ("STOP", "Stopped"),
        1 => ("STOP", "Idle"),
        2 => ("SUSPEND", "Hold"),
        3 => ("OPERATE", "Running"),
        4 => ("STOP", "Idle"),
        5 => ("STOP", "Standby"),
        _ => ("STOP", "Unknown"),
    };

    private static string ClassifyOutcome(BrotherPollSession session)
    {
        if (session.MachineInfoEndpointFailed) return "failed";
        if (session.CycleTimeEndpointFailed ||
            session.WorkCountersEndpointFailed ||
            session.AtcToolsEndpointFailed ||
            session.AlarmsEndpointFailed ||
            session.MaintenanceNoticesEndpointFailed)
        {
            return "degraded";
        }
        return "success";
    }

    private void RecordEndpointMetric(bool failed, string endpoint)
    {
        if (!failed) return;
        _endpointFailures.Add(1,
            new KeyValuePair<string, object?>("source", _instanceId),
            new KeyValuePair<string, object?>("endpoint", endpoint));
    }

    private void TransitionState(AdapterState next)
    {
        if (!AdapterStateTransitions.IsAllowed(State, next))
        {
            _logger.LogWarning(
                "BrotherHttpSourceAdapter '{InstanceId}' rejected state transition {From} → {To}.",
                _instanceId, State, next);
            return;
        }
        State = next;
    }
}
