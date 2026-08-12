// ============================================================================
// File: Focas2ConnectionManager.cs
// Purpose: Manages the FOCAS2 connection lifecycle — handle allocation with
//          validation, exponential backoff on failures, system info caching,
//          and statinfo caching for the G342 single-call quirk.
//
// IMPORTANT: All methods must be called from the Focas2Thread to satisfy
//            the native library's thread-affinity requirement.
//
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2, Section 10
// ============================================================================

using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Cached CNC system information, read once per connection.
/// </summary>
internal sealed class CncSystemInfo
{
    /// <summary>CNC type: "M" = machining center, "T" = lathe.</summary>
    public string CncType { get; init; } = "";

    /// <summary>Machine tool type from cnc_sysinfo.</summary>
    public string MtType { get; init; } = "";

    /// <summary>CNC series: "0iD", "0iF", "30i", "31i", etc.</summary>
    public string Series { get; init; } = "";

    /// <summary>CNC software version string.</summary>
    public string Version { get; init; } = "";

    /// <summary>Maximum number of controllable axes.</summary>
    public int MaxAxes { get; init; }

    /// <summary>Actual number of controlled axes.</summary>
    public int AxisCount { get; init; }

    /// <summary>Ordered axis names (e.g., ["X","Y","Z","A","B"]).</summary>
    public IReadOnlyList<string> AxisNames { get; init; } = [];
}

/// <summary>
/// Manages FOCAS2 connection lifecycle: handle allocation with immediate
/// statinfo validation, exponential backoff, system info caching, and
/// orderly handle release.
/// </summary>
internal sealed class Focas2ConnectionManager
{
    private readonly IFocas2Api _api;
    private readonly Focas2SourceConfiguration _config;
    private readonly ILogger _logger;
    private readonly string _instanceId;

    // Returns a value in [0,1) used to jitter reconnect backoff (see
    // IncrementBackoff). Injectable so tests are deterministic; defaults to
    // Random.Shared so each source/instance jitters independently.
    private readonly Func<double> _jitter;

    private ushort _handle;
    private bool _connected;
    private CncSystemInfo? _systemInfo;
    private OdbStatusInfo? _cachedStatInfo;
    private int _consecutiveFailures;
    private DateTime _nextRetryTime = DateTime.MinValue;

    public Focas2ConnectionManager(
        IFocas2Api api,
        Focas2SourceConfiguration config,
        string instanceId,
        ILogger logger,
        Func<double>? jitter = null)
    {
        _api = api;
        _config = config;
        _instanceId = instanceId;
        _logger = logger;
        _jitter = jitter ?? Random.Shared.NextDouble;
    }

    /// <summary>The actual (jittered) backoff in ms applied by the most recent
    /// failure, for diagnostics and tests. Zero until the first failure.</summary>
    internal int LastBackoffMs { get; private set; }

    /// <summary>Whether a validated handle is currently held.</summary>
    public bool IsConnected => _connected;

    /// <summary>The current FOCAS2 handle. Only valid when <see cref="IsConnected"/> is true.</summary>
    public ushort Handle => _handle;

    /// <summary>Cached system info, populated on first successful connection.</summary>
    public CncSystemInfo? SystemInfo => _systemInfo;

    /// <summary>
    /// Cached statinfo from connect-time validation. Consumed (set to null)
    /// after the first StatusCollector read per connection, because the G342
    /// only allows one statinfo call per handle session.
    /// </summary>
    public OdbStatusInfo? CachedStatInfo => _cachedStatInfo;

    /// <summary>Consume the cached statinfo (returns it and sets to null).</summary>
    public OdbStatusInfo? ConsumeCachedStatInfo()
    {
        var cached = _cachedStatInfo;
        _cachedStatInfo = null;
        return cached;
    }

    /// <summary>Number of consecutive connection failures (for diagnostics).</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>
    /// Ensure a valid connection exists. If not connected and backoff allows,
    /// attempt to connect. If backoff is in effect, returns false.
    /// </summary>
    /// <returns>True if connected after the call, false if in backoff or connect failed.</returns>
    public bool EnsureConnected()
    {
        if (_connected)
        {
            return true;
        }

        if (!_config.KeepAlive)
        {
            // Non-keepalive mode: always try to connect
            return TryConnect();
        }

        // KeepAlive mode: respect backoff
        if (DateTime.UtcNow < _nextRetryTime)
        {
            return false;
        }

        return TryConnect();
    }

    /// <summary>
    /// Free the handle and reset connection state. Always attempts to free
    /// even if the handle may be invalid. Logs but does not throw.
    /// </summary>
    public void Disconnect()
    {
        if (!_connected)
        {
            return;
        }

        try
        {
            var ret = _api.FreeLibHandle(_handle);
            if (ret != (short)Focas2ErrorCode.EW_OK)
            {
                _logger.LogDebug(
                    "Source {InstanceId}: cnc_freelibhndl({Handle}) returned {Code}",
                    _instanceId, _handle, ret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Source {InstanceId}: Exception freeing handle {Handle}",
                _instanceId, _handle);
        }

        _connected = false;
        _handle = 0;
        _cachedStatInfo = null;
    }

    /// <summary>
    /// Read system info and axis names from the CNC. Called once after the
    /// first successful connection. If already cached, does nothing.
    /// </summary>
    public void EnsureSystemInfo()
    {
        if (_systemInfo != null || !_connected)
        {
            return;
        }

        try
        {
            _systemInfo = ReadSystemInfo();
            _logger.LogInformation(
                "Source {InstanceId}: CNC identified — Series={Series}, Type={CncType}, Version={Version}, Axes={AxisCount} [{AxisNames}]",
                _instanceId,
                _systemInfo.Series,
                _systemInfo.CncType,
                _systemInfo.Version,
                _systemInfo.AxisCount,
                string.Join(",", _systemInfo.AxisNames));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Source {InstanceId}: Failed to read system info — using defaults",
                _instanceId);
            _systemInfo = new CncSystemInfo
            {
                AxisNames = ["X", "Y", "Z", "A", "B", "C"],
                AxisCount = 6,
            };
        }
    }

    /// <summary>
    /// Called when a fatal FOCAS2 error occurs during polling (EW_SOCKET or
    /// EW_HANDLE). Disconnects and increments the failure counter.
    /// </summary>
    public void HandleFatalError()
    {
        Disconnect();
        IncrementBackoff();
    }

    private bool TryConnect()
    {
        int maxRetries = _config.MaxConnectRetries;
        short lastError = 0;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // Step 1: Allocate handle
            var ret = _api.AllocLibHandle(
                _config.IpAddress,
                _config.Port,
                _config.TimeoutSeconds,
                out _handle);

            if (ret != (short)Focas2ErrorCode.EW_OK)
            {
                lastError = ret;
                _logger.LogDebug(
                    "Source {InstanceId}: cnc_allclibhndl3 attempt {Attempt}/{Max} failed: {Code}",
                    _instanceId, attempt, maxRetries, (Focas2ErrorCode)ret);

                if (attempt < maxRetries)
                {
                    Thread.Sleep(500);
                }

                continue;
            }

            // Step 1b: Bound per-handle data reads (operational mitigation for
            // the 2026-06-24 silent-stall incident). Applied immediately after
            // alloc so even the statinfo validation below is bounded. Failure
            // to set the timeout is logged but NOT fatal — a controller that
            // doesn't honour cnc_setdtimeout should still be allowed to connect;
            // 0 means "leave the library default". This bounds the library wait
            // only; it is not the slice-0 commit-3.1 supervisor deadline.
            if (_config.DataTimeoutSeconds > 0)
            {
                // Defensive: a non-EW_OK return OR a thrown P/Invoke (e.g. a
                // library that doesn't export cnc_setdtimeout) must NOT abort an
                // otherwise-valid connect — the timeout is a best-effort bound,
                // not a connect precondition. Mirrors Disconnect/EnsureSystemInfo.
                try
                {
                    var dtoRet = _api.SetDataTimeout(_handle, _config.DataTimeoutSeconds);
                    if (dtoRet != (short)Focas2ErrorCode.EW_OK)
                    {
                        _logger.LogWarning(
                            "Source {InstanceId}: cnc_setdtimeout({Seconds}s) on handle {Handle} returned {Code} — continuing; data reads may remain unbounded.",
                            _instanceId, _config.DataTimeoutSeconds, _handle, (Focas2ErrorCode)dtoRet);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Source {InstanceId}: cnc_setdtimeout({Seconds}s) on handle {Handle} threw — continuing; data reads may remain unbounded.",
                        _instanceId, _config.DataTimeoutSeconds, _handle);
                }
            }

            // Step 2: Validate handle with cnc_statinfo and cache the result.
            // G342 quirk: (1) handles can be TCP-valid but FOCAS2-invalid,
            // (2) only ONE statinfo call per handle session.
            ret = _api.ReadStatusInfo(_handle, out var cachedStatus);
            if (ret != (short)Focas2ErrorCode.EW_OK)
            {
                _logger.LogDebug(
                    "Source {InstanceId}: Handle {Handle} allocated but statinfo returned {Code} — freeing (attempt {Attempt}/{Max})",
                    _instanceId, _handle, (Focas2ErrorCode)ret, attempt, maxRetries);

                try { _api.FreeLibHandle(_handle); } catch { /* swallow */ }
                lastError = ret;

                if (attempt < maxRetries)
                {
                    Thread.Sleep(1000);
                }

                continue;
            }

            // Handle is valid
            _cachedStatInfo = cachedStatus;
            _connected = true;
            _consecutiveFailures = 0;
            _nextRetryTime = DateTime.MinValue;

            _logger.LogDebug(
                "Source {InstanceId}: Connected to {Ip}:{Port} (handle={Handle}, attempt {Attempt})",
                _instanceId, _config.IpAddress, _config.Port, _handle, attempt);
            return true;
        }

        // All retries exhausted
        var backoffMs = IncrementBackoff();
        _logger.LogWarning(
            "Source {InstanceId}: Connect failed after {Max} attempts (consecutive: {Failures}). Next retry in {Backoff}ms (jittered).",
            _instanceId, maxRetries, _consecutiveFailures, backoffMs);
        return false;
    }

    private int IncrementBackoff()
    {
        _consecutiveFailures++;
        var baseMs = Math.Min(
            (int)(_config.InitialBackoffMs * Math.Pow(_config.BackoffMultiplier, Math.Min(_consecutiveFailures - 1, 10))),
            _config.MaxBackoffMs);

        // Equal jitter (AWS "Exponential Backoff And Jitter" pattern): keep at
        // least half the computed delay, then spread the upper half randomly.
        // Without this, multiple FOCAS sources recovering from the SAME outage
        // (e.g. a switch blip) reconnect in lockstep and can momentarily exceed
        // the controller's small FOCAS connection limit — itself a cause of
        // SOCKET_ERROR. De-correlating the retries avoids that reconnect storm
        // while never retrying sooner than half the intended backoff.
        var half = baseMs / 2;
        var backoffMs = half + (int)(_jitter() * half);

        LastBackoffMs = backoffMs;
        _nextRetryTime = DateTime.UtcNow.AddMilliseconds(backoffMs);
        return backoffMs;
    }

    private CncSystemInfo ReadSystemInfo()
    {
        string cncType = "", mtType = "", series = "", version = "";
        int maxAxes = 0, axisCount = 0;

        var ret = _api.ReadSystemInfo(_handle, out var sysInfo);
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            cncType = sysInfo.CncType?.Trim('\0', ' ') ?? "";
            mtType = sysInfo.MtType?.Trim('\0', ' ') ?? "";
            series = sysInfo.Series?.Trim('\0', ' ') ?? "";
            version = sysInfo.Version?.Trim('\0', ' ') ?? "";

            if (int.TryParse(sysInfo.MaxAxis?.Trim('\0', ' '), out var ma))
            {
                maxAxes = ma;
            }

            if (int.TryParse(sysInfo.Axes?.Trim('\0', ' '), out var ac))
            {
                axisCount = ac;
            }
        }
        else
        {
            _logger.LogDebug(
                "Source {InstanceId}: cnc_sysinfo returned {Code}",
                _instanceId, (Focas2ErrorCode)ret);
        }

        var axisNames = ReadAxisNames();
        if (axisCount == 0)
        {
            axisCount = axisNames.Count;
        }

        return new CncSystemInfo
        {
            CncType = cncType,
            MtType = mtType,
            Series = series,
            Version = version,
            MaxAxes = maxAxes,
            AxisCount = axisCount,
            AxisNames = axisNames,
        };
    }

    private List<string> ReadAxisNames()
    {
        var ret = _api.ReadAxisCount(_handle, out var count);
        if (ret != (short)Focas2ErrorCode.EW_OK || count <= 0)
        {
            return ["X", "Y", "Z"];
        }

        var names = new OdbAxisName[count];
        ret = _api.ReadAxisNames(_handle, ref count, names);
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            return ["X", "Y", "Z"];
        }

        var result = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            var name = names[i].Name?.Trim('\0', ' ') ?? $"Axis{i}";
            if (!string.IsNullOrEmpty(name))
            {
                result.Add(name);
            }
        }

        return result.Count > 0 ? result : ["X", "Y", "Z"];
    }
}
