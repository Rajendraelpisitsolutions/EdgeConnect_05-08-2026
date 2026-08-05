// ============================================================================
// File: Focas2SourceAdapter.cs
// Purpose: ISourceAdapter implementation for Fanuc CNC controllers via FOCAS2.
//          Orchestrates the dedicated thread, connection manager, and collectors
//          to produce CanonicalDataPoints from CNC data.
//
// LOCKED BEHAVIOR:
//   Protocol-agnostic core — this adapter references Core, not vice versa.
//   Per-adapter isolation — one Focas2Thread per instance.
//   All data emitted as CanonicalDataPoint via CanonicalDataPointFactory.
//
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2.Collectors;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// FOCAS2 source adapter implementing <see cref="ISourceAdapter"/>. Provides
/// polling-mode data collection from Fanuc CNC controllers via the native
/// FOCAS2 library over Ethernet.
/// </summary>
public sealed class Focas2SourceAdapter : ISourceAdapter, ISourceRetirement
{
    private readonly string _instanceId;
    private readonly IFocas2Api _api;
    private readonly ILogger _logger;
    private readonly IGatewayIdentity? _gatewayIdentity;

    // Lifecycle
    private Focas2SourceConfiguration? _config;
    private Focas2Thread? _thread;
    private Focas2ConnectionManager? _connectionManager;
    private CanonicalDataPointFactory? _factory;

    // Slice 0 commit 3.0: durable retirement attestation (inert — not yet driven
    // by the live supervisor). Cached so BeginRetirement is idempotent.
    private readonly object _retirementSync = new();
    private AdapterRetirementOperation? _retirement;

    // Collectors
    private StatusCollector? _statusCollector;
    private ProgramCollector? _programCollector;
    private AxisCollector? _axisCollector;
    private SpindleCollector? _spindleCollector;
    private AlarmCollector? _alarmCollector;
    private ProductionCollector? _productionCollector;
    private ToolCollector? _toolCollector;
    private MtLinkiCollector? _mtLinkiCollector;

    // Health tracking
    private long _pollAttempts;
    private long _pollSuccesses;
    private long _pollFailures;
    private DateTime? _lastSuccessAt;
    private AdapterError? _lastError;

    // Consecutive failed polls since the last success. Used to distinguish a
    // brief blip (recovers, logs RECOVERED) from a source that is persistently
    // DOWN, so the sustained-outage alert fires once when it crosses the
    // threshold. Reset to 0 on any success.
    private int _consecutiveFailures;

    /// <summary>Consecutive failed polls after which a one-shot "STILL DOWN"
    /// alert is raised (a brief blip recovers well before this).</summary>
    private const int SustainedOutageThreshold = 5;

    // Pacing — see PollAsync. Records the UTC time at which the previous
    // PollAsync invocation began, so the next call can honour the
    // configured PollIntervalMs without the supervisor having to throttle
    // itself. The source supervisor calls PollAsync in a tight loop, so
    // the adapter is the pacing authority per the supervisor's header
    // comment ("pacing is the adapter's responsibility").
    private DateTime? _lastPollStartedAtUtc;

    /// <summary>
    /// Production constructor — uses real P/Invoke via <see cref="Focas2NativeApi"/>.
    /// </summary>
    /// <param name="instanceId">Stable adapter instance id (e.g. <c>"focas-lathe-1"</c>).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="gatewayIdentity">
    /// Per-host gateway identity used to tag every emitted canonical data
    /// point's <c>GatewayId</c>. Pass <see langword="null"/> in narrow unit
    /// tests to accept the <c>"gateway"</c> fallback; production composition
    /// (the <c>AddFocas2Source</c> extension) resolves this from DI.
    /// </param>
    public Focas2SourceAdapter(
        string instanceId,
        ILogger<Focas2SourceAdapter> logger,
        IGatewayIdentity? gatewayIdentity = null)
        : this(instanceId, ChooseProductionApi(), logger, gatewayIdentity)
    {
    }

    /// <summary>
    /// Dispatch helper for the production constructor. When
    /// <see cref="Focas2DemoModeOptions.IsEnabled"/> is true (M.2b.3.1),
    /// returns a <see cref="Focas2DemoApi"/> instead of the real
    /// <see cref="Focas2NativeApi"/>. The choice is frozen for the
    /// process lifetime (Locked F).
    /// </summary>
    private static IFocas2Api ChooseProductionApi()
        => Focas2DemoModeOptions.IsEnabled
            ? new Focas2DemoApi()
            : new Focas2NativeApi();

    /// <summary>
    /// Test-only accessor exposing the live <see cref="IFocas2Api"/>
    /// instance. Used by the demo-dispatch tests to verify that the
    /// production constructor picks the right backend based on
    /// <see cref="Focas2DemoModeOptions"/>.
    /// </summary>
    internal IFocas2Api ApiForTesting => _api;

    /// <summary>
    /// Test constructor — accepts an injected <see cref="IFocas2Api"/> for
    /// unit testing with <c>FakeFocas2Api</c>.
    /// </summary>
    internal Focas2SourceAdapter(
        string instanceId,
        IFocas2Api api,
        ILogger logger,
        IGatewayIdentity? gatewayIdentity = null)
    {
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gatewayIdentity = gatewayIdentity;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// FOCAS2's worker is the fwlib-affine dedicated thread, so quiescence proof is
    /// the thread ACTUALLY terminating — never a Join timeout. Cleanup is initiated
    /// thread-affinity-safely and non-blockingly (enqueue the handle-free on the
    /// affine thread, then signal the thread to exit after draining); the native
    /// handle is never freed from another thread while a call may be in flight. A
    /// wedged native call leaves the durable <c>Completion</c> pending (the still-
    /// live thread is governed by the orphan/resource policy, NOT treated as proof);
    /// a late thread exit resolves <c>Proven</c>. Idempotent.
    /// </remarks>
    public AdapterRetirementOperation BeginRetirement(AdapterRetirementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_retirementSync)
        {
            return _retirement ??= Retirement.Focas2Retirement.Begin(
                initiateThreadCleanup: InitiateThreadCleanup,
                awaitThreadExit: () => _thread is null
                    ? Task.CompletedTask
                    : _thread.WaitForThreadExitAsync(),
                context);
        }
    }

    private void InitiateThreadCleanup()
    {
        var thread = _thread;
        if (thread is null)
        {
            return;
        }

        // Enqueue the handle-free ON the affine thread (thread-affinity-safe), then
        // signal the thread to exit after draining. Both are non-blocking and never
        // touch fwlib from another thread.
        var connectionManager = _connectionManager;
        Action? finalWork = connectionManager is null ? null : connectionManager.Disconnect;
        thread.BeginShutdown(finalWork);
    }

    /// <inheritdoc/>
    public string InstanceId => _instanceId;

    /// <inheritdoc/>
    public string ProtocolName => "focas2";

    /// <inheritdoc/>
    /// <remarks>
    /// Declares Polling + Browse. The <see cref="SourceCapabilities.TestConnect"/>
    /// flag is intentionally NOT declared even though a future management API
    /// could benefit from it — the <see cref="ISourceAdapter"/> contract does
    /// not yet carry a <c>TestConnectAsync</c> method. Adding the flag without
    /// the method would surface a capability the host cannot invoke. Revisit
    /// once Phase 4's management API lands the contract extension.
    /// </remarks>
    public SourceCapabilities Capabilities =>
        SourceCapabilities.Polling | SourceCapabilities.Browse;

    /// <inheritdoc/>
    public AdapterState State { get; private set; } = AdapterState.Created;

    /// <inheritdoc/>
    public Task InitializeAsync(SourceConfiguration config, CancellationToken ct)
    {
        TransitionState(AdapterState.Initializing);

        if (config is not Focas2SourceConfiguration focasConfig)
        {
            TransitionState(AdapterState.Failed);
            throw new InvalidOperationException(
                $"Focas2SourceAdapter requires Focas2SourceConfiguration, got {config.GetType().Name}.");
        }

        _config = focasConfig;

        // Create the dedicated FOCAS2 thread
        _thread = new Focas2Thread(_instanceId);

        // Create the factory for canonical data points. Gateway id comes
        // from the host-level IGatewayIdentity (wired via AddFocas2Source);
        // narrow unit tests that don't supply one get the "gateway" fallback
        // so legacy test fixtures keep working.
        // DeviceClass: FOCAS2 is CNC-only. Default to "cnc" if config didn't
        // set it explicitly — keeps backward compat with pre-deviceClass
        // configs while still letting an integrator override (e.g. to
        // "cnc-lathe" if they amend the contract vocabulary later).
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

        // Create connection manager
        _connectionManager = new Focas2ConnectionManager(_api, _config, _instanceId, _logger);

        // Create collectors
        _statusCollector = new StatusCollector(_api, _logger);
        _programCollector = new ProgramCollector(_api, _logger);
        _axisCollector = new AxisCollector(_api, _logger);
        _spindleCollector = new SpindleCollector(_api, _logger);
        _alarmCollector = new AlarmCollector(_api, _logger);
        _productionCollector = new ProductionCollector(_api, _logger);
        _toolCollector = new ToolCollector(_api, _logger);
        _mtLinkiCollector = new MtLinkiCollector(_api, _logger);

        TransitionState(AdapterState.Initialized);
        _logger.LogInformation(
            "Focas2SourceAdapter '{Id}' initialized for {Ip}:{Port}.",
            _instanceId, _config.IpAddress, _config.Port);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct)
    {
        TransitionState(AdapterState.Starting);

        try
        {
            // Attempt initial connection on the dedicated thread.
            // Failure here is non-fatal — we'll retry on first PollAsync.
            await _thread!.RunAsync(() =>
            {
                var connected = _connectionManager!.EnsureConnected();
                if (connected)
                {
                    _connectionManager.EnsureSystemInfo();
                }

                return connected;
            }, ct).ConfigureAwait(false);

            TransitionState(AdapterState.Running);
            _logger.LogInformation("Focas2SourceAdapter '{Id}' started.", _instanceId);
        }
        catch (Exception ex)
        {
            // Don't fail — go to Running and let PollAsync handle reconnection.
            _logger.LogWarning(ex, "Focas2SourceAdapter '{Id}' initial connect failed — will retry on poll.", _instanceId);
            TransitionState(AdapterState.Running);
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
            if (_thread != null && _connectionManager != null)
            {
                await _thread.RunAsync(() => _connectionManager.Disconnect(), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Focas2SourceAdapter '{Id}' error during stop disconnect.", _instanceId);
        }

        if (_thread != null)
        {
            await _thread.DisposeAsync().ConfigureAwait(false);
        }

        TransitionState(AdapterState.Stopped);
        _logger.LogInformation("Focas2SourceAdapter '{Id}' stopped.", _instanceId);
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
            ["consecutiveConnectFailures"] = _connectionManager?.ConsecutiveFailures ?? 0,
            ["connected"] = _connectionManager?.IsConnected ?? false,
        };

        if (_config != null)
        {
            metrics["endpoint"] = $"{_config.IpAddress}:{_config.Port}";
        }

        if (_connectionManager?.SystemInfo is { } sysInfo)
        {
            metrics["cncSeries"] = sysInfo.Series;
            metrics["cncType"] = sysInfo.CncType;
            metrics["axisCount"] = sysInfo.AxisCount;
        }

        // M.2b.3.1: surface demo-mode at the per-source health level so
        // operators inspecting the Sources detail page can tell at a glance
        // that this adapter is synthetic. Key absence is the production
        // default; presence (value true) marks demo-backed sources.
        if (_api is Focas2DemoApi)
        {
            metrics["demoMode"] = true;
        }

        var health = new AdapterHealth
        {
            State = State,
            Level = level,
            CheckedAt = DateTime.UtcNow,
            LastSuccessAt = _lastSuccessAt,
            LastError = _lastError,
            Metrics = metrics,
        };

        return Task.FromResult(health);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct)
    {
        // Honour PollIntervalMs between successive poll starts. This is the
        // pacing contract documented on SourceSupervisor: the supervisor
        // calls PollAsync in a tight loop and expects the adapter to
        // throttle itself. First poll after start runs immediately; every
        // subsequent poll waits until PollIntervalMs has elapsed since the
        // previous poll's start. A value of 0 disables pacing.
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

        try
        {
            var points = await _thread!.RunAsync(() => CollectAll(), ct).ConfigureAwait(false);

            // An EMPTY batch while the connection is DOWN is not a successful
            // poll — CollectAll short-circuits to empty when disconnected or in
            // reconnect backoff. Treating that as success would falsely flip the
            // source back to Running/healthy during a persistent outage (the
            // connect-failure variant of the silent-stall symptom) and emit a
            // bogus RECOVERED. Count it as an ongoing failure and stay Degraded.
            // A genuine "no new data this tick" while CONNECTED still succeeds.
            if (points.Count == 0 && _connectionManager is { IsConnected: false })
            {
                RecordFailure(_lastError ?? MakeError(
                    Focas2Errors.SocketError, ErrorCategory.Network,
                    "Source disconnected; reconnect in progress.", retryable: true));
                return points;
            }

            RecordSuccess();
            return points;
        }
        catch (Focas2FatalException ex)
        {
            var error = MapFatalError(ex);
            RecordFailure(error);
            _logger.LogWarning(
                "Focas2SourceAdapter '{Id}' fatal error: {Code} — disconnected, will retry.",
                _instanceId, ex.ErrorCode);
            return [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = MakeError(Focas2Errors.CollectError, ErrorCategory.Protocol,
                $"Unexpected error during poll: {ex.Message}", retryable: true);
            RecordFailure(error);
            _logger.LogError(ex, "ALERT — FOCAS2 source '{Id}' unexpected poll error: {Message}", _instanceId, ex.Message);
            return [];
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct)
    {
        throw new NotSupportedException("FOCAS2 is a polling-only protocol.");
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct)
    {
        var axisNames = _connectionManager?.SystemInfo?.AxisNames
            ?? (IReadOnlyList<string>)["X", "Y", "Z"];

        IReadOnlyList<TagDefinition> tags = Focas2TagMap.BuildTagDefinitions(axisNames);
        return Task.FromResult(tags);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct)
    {
        if (config is not Focas2SourceConfiguration focasConfig)
        {
            return Task.FromResult(ValidationResult.Failure(
                Focas2Errors.ConfigWrongType,
                $"Expected Focas2SourceConfiguration, got {config.GetType().Name}.",
                "config"));
        }

        var errors = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(focasConfig.IpAddress))
        {
            errors.Add(new ValidationIssue
            {
                Code = Focas2Errors.ConfigInvalid,
                Message = "IpAddress is required.",
                Path = "IpAddress",
            });
        }
        else if (!IPAddress.TryParse(focasConfig.IpAddress, out _))
        {
            errors.Add(new ValidationIssue
            {
                Code = Focas2Errors.ConfigInvalid,
                Message = $"IpAddress '{focasConfig.IpAddress}' is not a valid IP address.",
                Path = "IpAddress",
            });
        }

        if (focasConfig.Port == 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = Focas2Errors.ConfigInvalid,
                Message = "Port must be non-zero.",
                Path = "Port",
            });
        }

        if (focasConfig.TimeoutSeconds <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = Focas2Errors.ConfigInvalid,
                Message = "TimeoutSeconds must be positive.",
                Path = "TimeoutSeconds",
            });
        }

        if (focasConfig.DataTimeoutSeconds < 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = Focas2Errors.ConfigInvalid,
                Message = "DataTimeoutSeconds must be zero (disabled) or positive.",
                Path = "DataTimeoutSeconds",
            });
        }

        if (errors.Count > 0)
        {
            return Task.FromResult(new ValidationResult { IsValid = false, Errors = errors });
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
                _logger.LogDebug(ex, "Focas2SourceAdapter '{Id}' error during dispose stop.", _instanceId);
            }
        }

        if (_thread != null)
        {
            await _thread.DisposeAsync().ConfigureAwait(false);
            _thread = null;
        }
    }

    // =========================================================================
    // PRIVATE — data collection orchestration
    // =========================================================================

    /// <summary>
    /// Runs on the dedicated Focas2Thread. Connects if needed, then calls
    /// each enabled collector in sequence.
    /// </summary>
    private List<CanonicalDataPoint> CollectAll()
    {
        var cm = _connectionManager!;
        var config = _config!;
        var factory = _factory!;

        // Ensure connected (respects backoff)
        if (!cm.IsConnected && !config.KeepAlive)
        {
            cm.EnsureConnected();
        }
        else if (!cm.IsConnected)
        {
            if (!cm.EnsureConnected())
            {
                return []; // In backoff
            }
        }

        if (!cm.IsConnected)
        {
            return [];
        }

        // Read system info once after first successful connection
        cm.EnsureSystemInfo();

        var handle = cm.Handle;
        var now = DateTime.UtcNow;
        var points = new List<CanonicalDataPoint>(64);

        try
        {
            // Status — always collected first (validates handle is alive)
            var cachedStatInfo = cm.ConsumeCachedStatInfo();
            _statusCollector!.Collect(handle, cachedStatInfo, factory, points, now, now);

            // Extract status values for MtLinki collector
            string? runState = null;
            string? autoMode = null;
            bool? emergencyStop = null;

            foreach (var p in points)
            {
                if (p.TagName == Focas2TagMap.RunState.TagName)
                    runState = p.Value as string;
                else if (p.TagName == Focas2TagMap.AutoMode.TagName)
                    autoMode = p.Value as string;
                else if (p.TagName == Focas2TagMap.EmergencyStop.TagName)
                    emergencyStop = p.Value as bool?;
            }

            // Program
            if (HasDataPoint("Program/MainProgram"))
                _programCollector!.Collect(handle, factory, points, now, now);

            // Find program values for MtLinki
            string? mainProgram = null;
            string? runningProgram = null;
            foreach (var p in points)
            {
                if (p.TagName == Focas2TagMap.MainProgram.TagName)
                    mainProgram = p.Value as string;
                else if (p.TagName == Focas2TagMap.RunningProgram.TagName)
                    runningProgram = p.Value as string;
            }

            // Axes
            if (HasAnyDataPoint("Axes/"))
            {
                var axisNames = cm.SystemInfo?.AxisNames ?? (IReadOnlyList<string>)["X", "Y", "Z"];
                _axisCollector!.Collect(handle, axisNames, factory, points, now, now);
            }

            // Feed Rate
            if (HasDataPoint("Axes/FeedRate"))
                _spindleCollector!.Collect(handle, factory, points, now, now);
            else if (HasAnyDataPoint("Spindle/"))
                _spindleCollector!.Collect(handle, factory, points, now, now);

            // Alarms
            int alarmCount = 0;
            if (HasDataPoint("Alarms/Active"))
            {
                var axisNames = cm.SystemInfo?.AxisNames;
                _alarmCollector!.Collect(handle, axisNames, factory, points, now, now);

                // Extract alarm count
                foreach (var p in points)
                {
                    if (p.TagName == Focas2TagMap.AlarmCount.TagName && p.Value is int ac)
                    {
                        alarmCount = ac;
                        break;
                    }
                }
            }

            // Cycle Time
            if (HasDataPoint("CycleTime") || HasDataPoint("Production/CycleTime"))
                _productionCollector!.Collect(handle, factory, points, now, now);

            // Parts Count
            if (HasDataPoint("PartsCount") || HasDataPoint("Production/PartsCount"))
            {
                // ProductionCollector handles both
                if (!HasDataPoint("CycleTime") && !HasDataPoint("Production/CycleTime"))
                    _productionCollector!.Collect(handle, factory, points, now, now);
            }

            // Find parts count for MtLinki
            long? partsCount = null;
            foreach (var p in points)
            {
                if (p.TagName == Focas2TagMap.PartsCount.TagName && p.Value is long pc)
                {
                    partsCount = pc;
                    break;
                }
            }

            // Tool (offsets + life; ToolLife paths also enable the collector)
            if (HasAnyDataPoint("Tool/") || HasAnyDataPoint("ToolLife/"))
                _toolCollector!.Collect(handle, factory, points, now, now);

            // MT-LINKi
            if (HasAnyDataPoint("MtLinki/"))
            {
                _mtLinkiCollector!.Collect(
                    handle, runState, autoMode, emergencyStop, alarmCount,
                    partsCount, mainProgram, runningProgram,
                    factory, points, now, now);
            }
        }
        catch (Focas2FatalException)
        {
            // Fatal FOCAS2 error — disconnect and let the caller handle it
            cm.HandleFatalError();
            throw;
        }

        // Disconnect if not keeping alive
        if (!config.KeepAlive && cm.IsConnected)
        {
            cm.Disconnect();
        }

        return points;
    }

    private bool HasDataPoint(string name)
    {
        var dataPoints = _config!.DataPoints;
        return dataPoints.Count == 0 || dataPoints.Contains(name);
    }

    private bool HasAnyDataPoint(string prefix)
    {
        var dataPoints = _config!.DataPoints;
        if (dataPoints.Count == 0)
        {
            return true; // Empty means collect all
        }

        foreach (var dp in dataPoints)
        {
            if (dp.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // =========================================================================
    // PRIVATE — state management & health
    // =========================================================================

    private void TransitionState(AdapterState target)
    {
        if (!AdapterStateTransitions.IsAllowed(State, target))
        {
            throw new InvalidOperationException(
                $"Focas2SourceAdapter '{_instanceId}' cannot transition from {State} to {target}.");
        }

        State = target;
    }

    private void RecordSuccess()
    {
        Interlocked.Increment(ref _pollSuccesses);
        _lastSuccessAt = DateTime.UtcNow;
        _lastError = null;
        Interlocked.Exchange(ref _consecutiveFailures, 0);

        if (State == AdapterState.Degraded)
        {
            TransitionState(AdapterState.Running);

            // Recovery counterpart to the Degraded-edge alert below, so an
            // operator/monitor watching for "STOPPED" gets a matching "RESUMED".
            _logger.LogInformation(
                "RECOVERED — FOCAS2 source '{Id}' resumed producing data.",
                _instanceId);
        }
    }

    private void RecordFailure(AdapterError error)
    {
        Interlocked.Increment(ref _pollFailures);
        _lastError = error;
        var consecutive = Interlocked.Increment(ref _consecutiveFailures);

        if (State == AdapterState.Running)
        {
            TransitionState(AdapterState.Degraded);

            // ALERT (silent-stall class, incident 2026-06-24): data WAS flowing
            // and has now stopped. Emitted once on the Running→Degraded edge —
            // NOT per failed poll — so monitoring can alert without log spam.
            // This is the operator-visible signal RC-2 was missing. NOTE: this
            // fires only when a poll SURFACES an error (e.g. the cnc_setdtimeout
            // bound turns a wedged read into a timeout). A poll that never
            // returns at all (true silent wedge) still needs the host-level
            // progress watchdog (slice-0 / diagnostic-strengthening track).
            var silentFor = _lastSuccessAt is { } last
                ? (DateTime.UtcNow - last).ToString()
                : "unknown (no prior success)";
            _logger.LogError(
                "ALERT — FOCAS2 source '{Id}' STOPPED producing data: {Code} ({Message}). " +
                "Last success {LastSuccess} (silent for {SilentFor}); source is now Degraded.",
                _instanceId, error.Code, error.Message, _lastSuccessAt, silentFor);
        }

        // Sustained-outage escalation: a brief network blip recovers (and logs
        // RECOVERED) well before this. Crossing the threshold means the source
        // is persistently DOWN, not flapping — fired ONCE (== threshold) so it
        // does not spam. Reconnect/backoff continues regardless; this is a
        // visibility signal, not a state change.
        if (consecutive == SustainedOutageThreshold)
        {
            _logger.LogError(
                "ALERT — FOCAS2 source '{Id}' STILL DOWN: {Count} consecutive poll failures. " +
                "Last error {Code} ({Message}); last success {LastSuccess}. Auto-reconnect continues.",
                _instanceId, consecutive, error.Code, error.Message, _lastSuccessAt);
        }
    }

    private static AdapterError MapFatalError(Focas2FatalException ex)
    {
        return ex.ErrorCode switch
        {
            Focas2ErrorCode.EW_SOCKET => MakeError(Focas2Errors.SocketError, ErrorCategory.Network,
                "Socket communication lost.", retryable: true),
            Focas2ErrorCode.EW_HANDLE => MakeError(Focas2Errors.HandleInvalid, ErrorCategory.Network,
                "Handle went invalid.", retryable: true),
            _ => MakeError(Focas2Errors.CollectError, ErrorCategory.Protocol,
                $"Fatal FOCAS2 error: {ex.ErrorCode}.", retryable: true),
        };
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
