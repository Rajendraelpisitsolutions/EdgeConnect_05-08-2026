// ============================================================================
// File: DataSources/Focas2DllDataSource.cs
// Purpose: Data source that communicates directly with a Fanuc CNC controller
//          via the native FOCAS2 library (Fwlib64.dll / libfwlib32.so).
//
// This is the LOWEST LATENCY option — no middleware, no database, just direct
// Ethernet communication to the CNC controller's FOCAS2 service.
//
// CONNECTION MODES:
//   KeepAlive=true:  Opens connection once, reuses across polls (faster, ~5ms/poll)
//   KeepAlive=false: Opens/closes connection each poll (slower, ~20-50ms overhead)
//
// THREAD SAFETY:
//   FOCAS2 handles are NOT thread-safe. This data source is used by exactly
//   one MachinePollerService, so no locking is needed.
//
// SYSTEM INFO CACHING:
//   cnc_sysinfo and cnc_rdaxisname are called once on first successful
//   connection. The results are cached in _systemInfo and reused for all
//   subsequent polls. System info only changes if the CNC is reconfigured
//   (which requires a power cycle), so caching is safe.
// ============================================================================

using System.Diagnostics;
using System.Runtime.InteropServices;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.DataSources.Focas2;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Data source using native FOCAS2 DLL calls via P/Invoke.
/// Direct Ethernet connection to the CNC controller — no middleware.
/// </summary>
public sealed class Focas2DllDataSource : IMachineDataSource
{
    private readonly MachineConfig _config;
    private readonly Focas2Settings _focas2Config;
    private readonly ILogger _logger;

    private ushort _handle;            // FOCAS2 connection handle
    private bool _connected;           // Whether we have an active handle
    private bool _disposed;
    private CncSystemInfo? _systemInfo; // Cached system info (populated once on connect)
    private OdbStatusInfo? _cachedStatInfo; // Cached statinfo from Connect() validation (G342 quirk: only 1 statinfo call per session)
    private string? _lastRunningProgram;   // Last known running program (O-number string)
    private int _lastRunningProgramNum;    // Last known running program number (int)
    private int _consecutiveFailures;      // Track consecutive connect failures for backoff
    private DateTime _nextRetryTime = DateTime.MinValue; // Backoff: earliest time to retry

    public string MachineId => _config.MachineId;
    public string MachineName => _config.MachineName;
    public DataSourceType SourceType => DataSourceType.Focas2Dll;

    public Focas2DllDataSource(MachineConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _focas2Config = config.Focas2 ?? throw new ArgumentException(
            $"Focas2 settings required for machine {config.MachineId} with DataSourceType=Focas2Dll");
        _logger = logger;
    }

    public async Task<CncMachineData> CollectDataAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var data = new CncMachineData
        {
            MachineId = _config.MachineId,
            MachineName = _config.MachineName,
            Tags = _config.Tags,
            Timestamp = DateTimeOffset.UtcNow
        };

        try
        {
            // ALL FOCAS2 calls (Connect + data collection + Disconnect) must run on
            // the SAME thread. The G342 (0i-TF Plus) binds handles to the allocating
            // thread — calling FreeLibHandle from a different thread returns -8 and
            // the handle leaks until the CNC times it out (slot exhaustion).
            await Task.Run(() =>
            {
                try
                {
                    // Ensure we have a valid FOCAS2 connection (same thread as data collection)
                    if (!_connected || !_focas2Config.KeepAlive)
                    {
                        Connect();
                    }
                    // Always collect status first — it validates the handle is alive
                    CollectStatusData(data);

                    // Read system info once after we know the handle works.
                    // Done here (not in Connect) because some CNCs invalidate
                    // the handle if sysinfo is called before statinfo.
                    if (_systemInfo == null)
                    {
                        try
                        {
                            _systemInfo = CollectSystemInfo();
                            _logger.LogInformation(
                                "Machine {MachineId}: CNC identified — Series={Series}, Type={CncType}, Version={Version}, Axes={AxisCount} [{AxisNames}]",
                                _config.MachineId,
                                _systemInfo.Series,
                                _systemInfo.CncType,
                                _systemInfo.Version,
                                _systemInfo.AxisCount,
                                string.Join(",", _systemInfo.AxisNames));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Machine {MachineId}: Failed to read system info — will use defaults",
                                _config.MachineId);
                            _systemInfo = new CncSystemInfo
                            {
                                AxisNames = new[] { "X", "Y", "Z", "A", "B", "C" }
                            };
                        }
                    }

                    if (HasDataPoint("Program/MainProgram"))
                        CollectProgramNumber(data);

                    if (HasAnyDataPoint("Axes/"))
                        CollectAxisData(data);

                    if (HasDataPoint("Axes/FeedRate"))
                        CollectFeedRate(data);

                    if (HasAnyDataPoint("Spindle/"))
                        CollectSpindleData(data);

                    if (HasDataPoint("Alarms/Active"))
                        CollectAlarmData(data);

                    if (HasDataPoint("CycleTime"))
                        CollectCycleTime(data);

                    if (HasDataPoint("PartsCount"))
                        CollectPartsCount(data);

                    // ── MT-LINKi Compatible Tags ──
                    CollectMtLinkiCompatibleTags(data);

                    // ── Tool Information ──
                    CollectToolData(data);
                }
                catch (Focas2Exception ex) when (ex.ErrorCode == Focas2Error.EW_SOCKET)
                {
                    data.Status = MachineStatus.Offline;
                    Disconnect(); // Same thread — handle freed correctly
                    _logger.LogWarning("Machine {MachineId}: FOCAS2 connection lost (socket error). Will reconnect.",
                        _config.MachineId);
                }
                catch (Focas2Exception ex) when (ex.ErrorCode == Focas2Error.EW_HANDLE)
                {
                    data.Status = MachineStatus.Offline;
                    if (_connected)
                    {
                        Disconnect(); // Same thread — handle freed correctly
                        _logger.LogWarning("Machine {MachineId}: FOCAS2 handle went invalid mid-poll. Disconnected.",
                            _config.MachineId);
                    }
                }
                catch (Focas2Exception ex)
                {
                    data.Status = MachineStatus.Error;
                    _logger.LogWarning(ex, "Machine {MachineId}: FOCAS2 error {Code} in {Function}",
                        _config.MachineId, ex.ErrorCode, ex.FunctionName);
                }
                finally
                {
                    // Disconnect on SAME THREAD if not keeping alive.
                    // CRITICAL: must be inside Task.Run for thread affinity —
                    // FreeLibHandle only works on the thread that allocated the handle.
                    if (!_focas2Config.KeepAlive && _connected)
                    {
                        Disconnect();
                    }
                }
            }, ct);

            // Attach cached system info to snapshot (may have been read in this cycle)
            data.SystemInfo = _systemInfo;

            // Determine overall machine status from collected data
            data.Status = DetermineMachineStatus(data);
        }
        catch (DllNotFoundException ex)
        {
            data.Status = MachineStatus.Error;
            _logger.LogError(ex,
                "Machine {MachineId}: FOCAS2 DLL not found. Place Fwlib64.dll in app directory or set Focas2.DllPath",
                _config.MachineId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            data.Status = MachineStatus.Error;
            _logger.LogWarning(ex, "Machine {MachineId}: Unexpected error in FOCAS2 collection",
                _config.MachineId);
        }
        finally
        {
            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;
        }

        return data;
    }

    public Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            Connect();
            var ok = _connected;
            if (!_focas2Config.KeepAlive) Disconnect();
            return Task.FromResult(ok);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    // =========================================================================
    // CONNECTION MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Allocate a FOCAS2 handle and VALIDATE it with cnc_statinfo before declaring success.
    /// Retries up to 3 times with a short delay between attempts.
    /// This prevents burning through CNC handle slots on controllers (like G342)
    /// where the first handle allocation often returns an unusable handle.
    ///
    /// HANDLE LEAK PREVENTION:
    ///   Each allocated handle is immediately tested. If the test fails, the handle
    ///   is freed BEFORE allocating the next one. This ensures we never have more
    ///   than 1 outstanding handle at a time.
    /// </summary>
    private void Connect()
    {
        if (_connected) return;

        // Backoff: skip this poll if we recently had consecutive failures
        if (DateTime.UtcNow < _nextRetryTime)
        {
            throw new Focas2Exception(Focas2Error.EW_HANDLE, "connect_backoff");
        }

        const int maxRetries = 5;
        short lastError = 0;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // Step 1: Allocate handle
            var ret = Focas2Interop.AllocLibHandle(
                _focas2Config.IpAddress,
                _focas2Config.Port,
                _focas2Config.TimeoutSeconds,
                out _handle);

            if (ret != (short)Focas2Error.EW_OK)
            {
                lastError = ret;
                _logger.LogDebug("Machine {MachineId}: cnc_allclibhndl3 attempt {Attempt}/{Max} failed: {Code}",
                    _config.MachineId, attempt, maxRetries, (Focas2Error)ret);

                // Allocation failed — no handle to free, just wait and retry
                if (attempt < maxRetries)
                    Thread.Sleep(500);
                continue;
            }

            // Step 2: ALWAYS validate handle with cnc_statinfo and CACHE the result.
            // The G342 (0i-TF Plus) has TWO quirks:
            //   1) Frequently allocates handles that succeed at TCP level but are invalid
            //      at FOCAS2 protocol level — we must validate immediately.
            //   2) Only allows ONE cnc_statinfo call per handle session — calling it again
            //      in CollectStatusData invalidates the handle.
            // Solution: validate here, cache the result, CollectStatusData uses the cache.
            ret = Focas2Interop.ReadStatusInfo(_handle, out var cachedStatus);
            if (ret != (short)Focas2Error.EW_OK)
            {
                _logger.LogDebug(
                    "Machine {MachineId}: Handle {Handle} allocated but statinfo returned {Code} — freeing and retrying (attempt {Attempt}/{Max})",
                    _config.MachineId, _handle, (Focas2Error)ret, attempt, maxRetries);

                try { Focas2Interop.FreeLibHandle(_handle); } catch { }
                lastError = ret;

                if (attempt < maxRetries)
                    Thread.Sleep(1000);
                continue;
            }

            // Cache statinfo so CollectStatusData doesn't call it again
            _cachedStatInfo = cachedStatus;

            // Handle is valid — statinfo succeeded
            _connected = true;
            _consecutiveFailures = 0;
            _nextRetryTime = DateTime.MinValue;

            _logger.LogDebug(
                "Machine {MachineId}: FOCAS2 connected to {Ip}:{Port} (handle={Handle}, attempt {Attempt})",
                _config.MachineId, _focas2Config.IpAddress, _focas2Config.Port, _handle, attempt);
            return;
        }

        // All retries exhausted — apply exponential backoff for next poll
        _consecutiveFailures++;
        int backoffSeconds = Math.Min(5 * (1 << Math.Min(_consecutiveFailures - 1, 5)), 120); // 5s, 10s, 20s, 40s, 80s, max 120s
        _nextRetryTime = DateTime.UtcNow.AddSeconds(backoffSeconds);

        _logger.LogWarning(
            "Machine {MachineId}: FOCAS2 connect failed after {Max} attempts (consecutive failures: {Failures}). " +
            "Next retry in {Backoff}s to let CNC release handles.",
            _config.MachineId, maxRetries, _consecutiveFailures, backoffSeconds);

        throw new Focas2Exception((Focas2Error)lastError, "cnc_allclibhndl3");
    }

    /// <summary>
    /// Free the FOCAS2 handle and reset connection state.
    /// Always attempts to free the handle, even if we think it might be invalid.
    /// Logs but does not throw on free errors.
    /// </summary>
    private void Disconnect()
    {
        if (!_connected) return;
        try
        {
            var ret = Focas2Interop.FreeLibHandle(_handle);
            if (ret != (short)Focas2Error.EW_OK)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_freelibhndl({Handle}) returned {Code}",
                    _config.MachineId, _handle, ret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Exception freeing handle {Handle}",
                _config.MachineId, _handle);
        }
        _connected = false;
        _handle = 0; // Clear handle value to prevent accidental reuse
        _cachedStatInfo = null; // Clear cached statinfo
    }

    // =========================================================================
    // SYSTEM INFO (collected once, cached)
    // =========================================================================

    /// <summary>
    /// Reads CNC system info and axis names. Called once on first connection.
    /// Uses cnc_sysinfo for controller identity and cnc_rdaxisname for axis labels.
    /// </summary>
    private CncSystemInfo CollectSystemInfo()
    {
        var info = new CncSystemInfo();

        // Read system info (CNC series, type, version, max axes)
        var ret = Focas2Interop.ReadSystemInfo(_handle, out var sysInfo);
        if (ret == (short)Focas2Error.EW_OK)
        {
            info.CncType = sysInfo.CncType?.Trim('\0', ' ') ?? "";
            info.MtType = sysInfo.MtType?.Trim('\0', ' ') ?? "";
            info.Series = sysInfo.Series?.Trim('\0', ' ') ?? "";
            info.Version = sysInfo.Version?.Trim('\0', ' ') ?? "";

            // MaxAxis comes as a 2-char string (e.g., "32", " 8")
            if (int.TryParse(sysInfo.MaxAxis?.Trim('\0', ' '), out var maxAxes))
                info.MaxAxes = maxAxes;

            // Axes count from the Axes field
            if (int.TryParse(sysInfo.Axes?.Trim('\0', ' '), out var axisCount))
                info.AxisCount = axisCount;
        }
        else
        {
            _logger.LogDebug("Machine {MachineId}: cnc_sysinfo returned {Code}",
                _config.MachineId, (Focas2Error)ret);
        }

        // Read axis names (e.g., X, Y, Z, A, B, C)
        info.AxisNames = ReadAxisNames();

        // If sysinfo didn't give us axis count, use the names array length
        if (info.AxisCount == 0)
            info.AxisCount = info.AxisNames.Length;

        return info;
    }

    // =========================================================================
    // DATA COLLECTION METHODS
    // =========================================================================

    /// <summary>
    /// Read cnc_statinfo for run state, auto mode, emergency stop, and alarm flag.
    /// This is the primary source of machine status — collected every poll.
    /// </summary>
    private void CollectStatusData(CncMachineData data)
    {
        OdbStatusInfo status;

        // Use cached statinfo from Connect() if available.
        // G342 (0i-TF Plus) only allows ONE cnc_statinfo call per handle session —
        // calling it again invalidates the handle. The cache prevents this.
        if (_cachedStatInfo.HasValue)
        {
            status = _cachedStatInfo.Value;
            _cachedStatInfo = null; // Consume the cache — next poll will get fresh data via Connect()
        }
        else
        {
            var ret = Focas2Interop.ReadStatusInfo(_handle, out status);
            if (ret != (short)Focas2Error.EW_OK)
            {
                // Severe errors (handle invalid, socket lost) must throw so the
                // outer catch block triggers a reconnect on the next poll cycle.
                var errorCode = (Focas2Error)ret;
                if (errorCode == Focas2Error.EW_HANDLE || errorCode == Focas2Error.EW_SOCKET)
                    throw new Focas2Exception(errorCode, "cnc_statinfo");

                _logger.LogDebug("Machine {MachineId}: cnc_statinfo returned {Code}",
                    _config.MachineId, errorCode);
                return;
            }
        }

        // Run state (the most important status field)
        data.RunningStatus = status.Run switch
        {
            0 => "Reset",
            1 => "Stop",
            2 => "Hold",
            3 => "Running",
            4 => "MSTR",
            _ => $"Unknown({status.Run})"
        };

        // Auto mode (MDI, MEM, EDIT, etc.)
        data.AutoMode = status.Aut switch
        {
            0 => "MDI",
            1 => "MEM",
            3 => "EDIT",
            4 => "HANDLE",
            5 => "JOG",
            6 => "TJOG",
            7 => "THND",
            _ => $"Unknown({status.Aut})"
        };

        // Emergency stop
        data.EmergencyStop = status.Emergency == 1;

        // Override running status if emergency stop is active
        if (status.Emergency == 1)
            data.RunningStatus = "Emergency";
    }

    /// <summary>
    /// Read program number (main program O-number and running program O-number).
    /// </summary>
    private void CollectProgramNumber(CncMachineData data)
    {
        var ret = Focas2Interop.ReadProgramNumber(_handle, out var progNum);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.MainProgram = $"O{progNum.MainProgram}";

            // Running program: validate it's a reasonable O-number (0 to 99999)
            // Some CNCs return garbage in RunningProgram when no program is active
            if (progNum.RunningProgram >= 0 && progNum.RunningProgram <= 99999)
            {
                _lastRunningProgram = $"O{progNum.RunningProgram}";
                _lastRunningProgramNum = progNum.RunningProgram;
            }
            else
            {
                // Invalid — fall back to main program
                _lastRunningProgram = data.MainProgram;
                _lastRunningProgramNum = progNum.MainProgram;
                _logger.LogDebug("Machine {MachineId}: RunningProgram={RunPrg} out of range, using MainProgram",
                    _config.MachineId, progNum.RunningProgram);
            }
        }
    }

    private void CollectAxisData(CncMachineData data)
    {
        var axisNames = _systemInfo?.AxisNames ?? new[] { "X", "Y", "Z" };
        short bufLen = (short)(4 + 4 + 4 * Focas2Interop.MAX_AXIS);

        // Absolute position
        var ret = Focas2Interop.ReadAbsolutePosition(
            _handle, Focas2Interop.ALL_AXES, bufLen, out var absPos);

        if (ret == (short)Focas2Error.EW_OK && absPos.Data != null)
        {
            for (int i = 0; i < axisNames.Length; i++)
            {
                if (i >= absPos.Data.Length) break;
                var name = axisNames[i];
                if (!data.Axes.ContainsKey(name))
                    data.Axes[name] = new AxisData { AxisName = name };

                var decimalPos = absPos.Decimal != null && i < absPos.Decimal.Length
                    ? absPos.Decimal[i] : (short)3;
                data.Axes[name].AbsolutePosition = absPos.Data[i] / Math.Pow(10, decimalPos);
            }
        }

        // Machine position
        ret = Focas2Interop.ReadMachinePosition(
            _handle, Focas2Interop.ALL_AXES, bufLen, out var machPos);

        if (ret == (short)Focas2Error.EW_OK && machPos.Data != null)
        {
            for (int i = 0; i < axisNames.Length; i++)
            {
                if (i >= machPos.Data.Length) break;
                var name = axisNames[i];
                if (!data.Axes.ContainsKey(name))
                    data.Axes[name] = new AxisData { AxisName = name };

                var decimalPos = machPos.Decimal != null && i < machPos.Decimal.Length
                    ? machPos.Decimal[i] : (short)3;
                data.Axes[name].MachinePosition = machPos.Data[i] / Math.Pow(10, decimalPos);
            }
        }
    }

    private void CollectFeedRate(CncMachineData data)
    {
        var ret = Focas2Interop.ReadActualFeedRate(_handle, out var feed);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.FeedRate = feed.Data;
        }
    }

    private void CollectSpindleData(CncMachineData data)
    {
        data.Spindle = new SpindleData();

        // Actual speed
        var ret = Focas2Interop.ReadActualSpindleSpeed(_handle, out var speed);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.Spindle.Speed = speed.Data;
            data.Spindle.Direction = speed.Data > 0 ? "CW" : speed.Data < 0 ? "CCW" : "Stopped";
        }

        // Spindle load
        ret = Focas2Interop.ReadSpindleLoad(_handle, 0, out var load);
        if (ret == (short)Focas2Error.EW_OK && load.Data != null && load.Data.Length > 0)
        {
            data.Spindle.Load = load.Data[0] / 10.0; // Value is x10
        }
    }

    private void CollectAlarmData(CncMachineData data)
    {
        // Read alarm status bitmask first — if no alarms, skip the message read
        var ret = Focas2Interop.ReadAlarmStatus(_handle, out var alarmStatus);
        if (ret != (short)Focas2Error.EW_OK || alarmStatus.Data == 0)
            return; // No alarms active

        // Read alarm messages (up to 10)
        var axisNames = _systemInfo?.AxisNames;
        short num = 10;
        var alarms = new OdbAlarmMessage[10];
        ret = Focas2Interop.ReadAlarmMessages(_handle, -1, ref num, alarms);
        if (ret == (short)Focas2Error.EW_OK)
        {
            for (int i = 0; i < num; i++)
            {
                var axisLabel = "";
                if (alarms[i].Axis > 0)
                {
                    axisLabel = (axisNames != null && alarms[i].Axis - 1 < axisNames.Length)
                        ? axisNames[alarms[i].Axis - 1]
                        : $"Axis{alarms[i].Axis}";
                }

                data.ActiveAlarms.Add(new AlarmData
                {
                    AlarmNumber = alarms[i].AlarmNo,
                    AlarmType = GetAlarmTypeName(alarms[i].Type),
                    Message = alarms[i].AlarmMessage?.TrimEnd('\0') ?? "",
                    Axis = axisLabel
                });
            }
        }
    }

    private void CollectCycleTime(CncMachineData data)
    {
        // Type 0 = running time, Type 1 = cutting time, Type 2 = cycle time
        var ret = Focas2Interop.ReadTimer(_handle, 2, out var timer);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.CycleTimeSeconds = timer.Minute * 60.0 + timer.Msec / 1000.0;
        }
    }

    private void CollectPartsCount(CncMachineData data)
    {
        // Parameter 6712 = parts count on most Fanuc controllers
        var ret = Focas2Interop.ReadParameter(_handle, 6712, 0, 8, out var param);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.PartsCount = param.LData;
        }
    }

    // =========================================================================
    // MT-LINKi COMPATIBLE TAG COLLECTION
    // =========================================================================
    // These methods populate the _path1_CNC fields on CncMachineData so that
    // FOCAS2 output matches what MT-LINKi REST API produces for EREMOS V2.
    // Each tag is read from the equivalent FOCAS2 function.
    // =========================================================================

    /// <summary>
    /// Master method that populates all MT-LINKi-compatible tags from FOCAS2 data.
    /// Called after all base data is collected.
    /// </summary>
    private void CollectMtLinkiCompatibleTags(CncMachineData data)
    {
        // ── CncState: computed from cnc_statinfo fields ──
        data.CncState_path1_CNC = ComputeCncState(data);

        // ── Mode: map AutoMode to MT-LINKi mode string ──
        data.Mode_path1_CNC = data.AutoMode ?? "UNKNOWN";

        // ── Disconnect: false means we successfully read data ──
        data.Disconnect_CNC = false;

        // ── Program info: main and running program numbers + comments ──
        data.MainProgram_path1_CNC = data.MainProgram ?? "O0";
        data.ActProgram_path1_CNC = _lastRunningProgram ?? data.MainProgram ?? "O0";

        // ── PartsNum: use parameter 6711 (resettable count) for MT-LINKi compat ──
        CollectPartsNumMtLinki(data);

        // ── Program comments ──
        CollectProgramComments(data);

        // ── PMC Signals (SigCUT, SigSBK, SigDM00, SigDM01, SigMDRN) ──
        CollectPmcSignals(data);

        // ── CNC Warning (operator messages) ──
        CollectCncWarning(data);

        // ── CNC Fan Status (diagnostic 4571-4574 or maintenance check) ──
        CollectCncFanStatus(data);

        // ── Battery Status ──
        CollectBatteryStatus(data);

        // ── Servo Diagnostics (temperature, pulse coder temp, insulation) ──
        CollectServoDiagnostics(data, 0); // Servo axis 0
        CollectServoDiagnostics(data, 1); // Servo axis 1

        // ── Servo Amplifier Fan Status ──
        CollectServoFanStatus(data, 0);
        CollectServoFanStatus(data, 1);

        // ── Spindle Diagnostics (temperature, insulation, revolution count) ──
        CollectSpindleDiagnostics(data);

        // ── Spindle Fan Status ──
        CollectSpindleFanStatus(data);
    }

    /// <summary>
    /// Compute CncState_path1_CNC matching MT-LINKi's logic:
    ///   EMERGENCY → emergency stop active
    ///   ALARM     → alarm flag set
    ///   OPERATE   → running (auto cycle in progress)
    ///   SUSPEND   → hold (feed hold active)
    ///   STOP      → stopped / idle
    /// </summary>
    private static string ComputeCncState(CncMachineData data)
    {
        // Priority: Emergency > Alarm > Running state
        if (data.EmergencyStop == true)
            return "EMERGENCY";

        if (data.ActiveAlarms.Count > 0)
            return "ALARM";

        return data.RunningStatus switch
        {
            "Running" or "MSTR" => "OPERATE",
            "Hold" => "SUSPEND",
            "Stop" or "Reset" => "STOP",
            "Emergency" => "EMERGENCY",
            _ => "STOP"
        };
    }

    /// <summary>
    /// Read parts count using parameter 6711 (resettable parts count).
    /// MT-LINKi typically uses this instead of 6712 (lifetime cumulative).
    /// Falls back to 6712 value if 6711 fails.
    /// </summary>
    private void CollectPartsNumMtLinki(CncMachineData data)
    {
        // Try parameter 6711 first (resettable counter — matches MT-LINKi)
        var ret = Focas2Interop.ReadParameter(_handle, 6711, 0, 8, out var param);
        if (ret == (short)Focas2Error.EW_OK)
        {
            data.PartsNum_path1_CNC = param.LData;
            return;
        }

        // Fallback to the already-collected PartsCount (param 6712)
        if (data.PartsCount.HasValue)
        {
            data.PartsNum_path1_CNC = (int)data.PartsCount.Value;
        }
    }

    /// <summary>
    /// Read program comments for main and running programs.
    /// Uses cnc_rdprogdir3 to get the comment string for a program number.
    /// </summary>
    private void CollectProgramComments(CncMachineData data)
    {
        try
        {
            // Main program comment
            if (data.MainProgram != null && int.TryParse(
                data.MainProgram.TrimStart('O', 'o'), out var mainPrgNum))
            {
                var comment = ReadProgramComment(mainPrgNum);
                data.MainComment_path1_CNC = comment ?? "";
            }

            // Running program comment
            if (_lastRunningProgramNum > 0 && _lastRunningProgramNum != 0)
            {
                var comment = ReadProgramComment(_lastRunningProgramNum);
                data.ActComment_path1_CNC = comment ?? "";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read program comments", _config.MachineId);
        }
    }

    /// <summary>
    /// Read the comment for a specific program number using cnc_rdprogdir3.
    /// Returns null if the program is not found or the function fails.
    ///
    /// cnc_rdprogdir3 with type=1 returns:
    ///   [4 bytes top_prog] [2 bytes num_prog]
    ///   Per entry (type=1):
    ///     [4 bytes number] [4 bytes length] [52 bytes comment]
    ///   Total per entry: 60 bytes
    /// </summary>
    private string? ReadProgramComment(int programNumber)
    {
        try
        {
            if (programNumber <= 0)
                return null;

            // Buffer: 4 (top_prog) + 2 (num_prog) + 60 (one entry: 4 num + 4 len + 52 comment)
            var bufSize = 4 + 2 + 60;
            var buffer = new byte[bufSize];
            int topProg = programNumber;
            short numProg = 1;

            var ret = Focas2Interop.ReadProgramDirectory(_handle, 1, ref topProg, ref numProg, buffer);
            if (ret != (short)Focas2Error.EW_OK || numProg == 0)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdprogdir3 returned {Code} for O{Prg}",
                    _config.MachineId, (Focas2Error)ret, programNumber);
                return null;
            }

            // Verify the returned program number matches what we asked for
            int returnedPrgNo = BitConverter.ToInt32(buffer, 6); // offset 6 = after top_prog(4) + num_prog(2)
            if (returnedPrgNo != programNumber)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdprogdir3 returned O{Returned} instead of O{Asked}",
                    _config.MachineId, returnedPrgNo, programNumber);
                return null;
            }

            // Comment starts at offset 14 (4 top + 2 num + 4 number + 4 length), length 52
            int commentOffset = 14;
            int commentLen = 52;
            if (buffer.Length < commentOffset + commentLen)
                return null;

            var comment = System.Text.Encoding.ASCII.GetString(buffer, commentOffset, commentLen)
                .TrimEnd('\0', ' ', '\r', '\n');

            // Strip the parentheses if the comment is in Fanuc format: (COMMENT)
            if (comment.StartsWith('(') && comment.EndsWith(')'))
                comment = comment[1..^1].Trim();

            return string.IsNullOrEmpty(comment) ? null : comment;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Exception reading program comment for O{Prg}",
                _config.MachineId, programNumber);
            return null;
        }
    }

    /// <summary>
    /// Read PMC signals used by MT-LINKi:
    ///   SigCUT  — Cutting signal (F-table address, typically F0 bit 0 or F2 bit 0)
    ///   SigSBK  — Single block signal (F-table)
    ///   SigDM00 — DM00 signal (custom, machine-builder defined)
    ///   SigDM01 — DM01 signal (custom, machine-builder defined)
    ///   SigMDRN — M-code done signal (F-table)
    ///
    /// PMC addresses vary by machine builder. These use common Fanuc defaults.
    /// </summary>
    private void CollectPmcSignals(CncMachineData data)
    {
        try
        {
            // Read F-table bytes 0-7 (covers most common signal addresses)
            // Buffer layout for pmc_rdpmcrng: 8 header bytes + data bytes
            ushort startAddr = 0;
            ushort endAddr = 7;
            ushort dataLen = (ushort)(8 + (endAddr - startAddr + 1)); // header + data
            var buffer = new byte[dataLen];

            var ret = Focas2Interop.ReadPmcRange(
                _handle, Focas2Interop.PMC_ADDR_F,
                Focas2Interop.PMC_TYPE_BYTE,
                startAddr, endAddr, dataLen, buffer);

            if (ret == (short)Focas2Error.EW_OK)
            {
                // F-table data starts at offset 8 in the buffer
                // Common Fanuc PMC signal addresses:
                // F0 bit 2 = STL (auto start lamp) — used as SigCUT in some configs
                // F0 bit 5 = SPL (spindle rotating) — alternative cutting signal
                // F2 bit 3 = OP (auto operation, cycle in progress) — best proxy for SigCUT
                // F1 bit 6 = SBK (single block active)
                // F5 bit 0 = DM00
                // F5 bit 1 = DM01
                // F6 bit 0 = MDRN (M-code done / FIN)

                byte f0 = buffer.Length > 8 ? buffer[8] : (byte)0;
                byte f1 = buffer.Length > 9 ? buffer[9] : (byte)0;
                byte f2 = buffer.Length > 10 ? buffer[10] : (byte)0;
                byte f5 = buffer.Length > 13 ? buffer[13] : (byte)0;
                byte f6 = buffer.Length > 14 ? buffer[14] : (byte)0;

                // SigCUT: OP signal (F2 bit 3) — auto operation in progress
                data.SigCUT_path1_CNC = (f2 & 0x08) != 0;

                // SigSBK: Single block (F1 bit 6)
                data.SigSBK_path1_CNC = (f1 & 0x40) != 0;

                // SigDM00/01: Machine builder signals (F5 bits 0,1 — common convention)
                data.SigDM00_path1_CNC = (f5 & 0x01) != 0;
                data.SigDM01_path1_CNC = (f5 & 0x02) != 0;

                // SigMDRN: M-code done (F6 bit 0)
                data.SigMDRN_path1_CNC = (f6 & 0x01) != 0;
            }
            else
            {
                _logger.LogDebug("Machine {MachineId}: pmc_rdpmcrng(F,0-7) returned {Code}",
                    _config.MachineId, (Focas2Error)ret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read PMC signals", _config.MachineId);
        }
    }

    /// <summary>
    /// Read CNC warning/operator messages using cnc_rdopmsg.
    /// Maps to CncWarning_path1_CNC.
    /// </summary>
    private void CollectCncWarning(CncMachineData data)
    {
        try
        {
            // cnc_rdopmsg: type=-1 (all), buffer size = 6 + 256 per msg (max 5 msgs)
            short msgBufLen = (short)(6 + 256);
            var buffer = new byte[msgBufLen];

            var ret = Focas2Interop.ReadOperatorMessage(_handle, 0, msgBufLen, buffer);
            if (ret == (short)Focas2Error.EW_OK)
            {
                // Structure: [2 datano][2 type][2 char_num][256 data]
                short charNum = BitConverter.ToInt16(buffer, 4);
                if (charNum > 0)
                {
                    int msgLen = Math.Min((int)charNum, 256);
                    var msg = System.Text.Encoding.ASCII.GetString(buffer, 6, msgLen).TrimEnd('\0', ' ');
                    data.CncWarning_path1_CNC = string.IsNullOrEmpty(msg) ? null : msg;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read operator messages", _config.MachineId);
        }
    }

    /// <summary>
    /// Read CNC fan status using diagnostic numbers.
    /// Fanuc CNC unit fans: diag 4571-4574 (varies by controller).
    /// Values: 0=Normal, 1=Slow, 2=Stopped
    /// </summary>
    private void CollectCncFanStatus(CncMachineData data)
    {
        try
        {
            // Diagnostic 4571-4574: CNC unit fan 1-4 status
            data.CncFan1Status_path1_CNC = ReadDiagFanStatus(4571);
            data.CncFan2Status_path1_CNC = ReadDiagFanStatus(4572);
            data.CncFan3Status_path1_CNC = ReadDiagFanStatus(4573);
            data.CncFan4Status_path1_CNC = ReadDiagFanStatus(4574);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read CNC fan status", _config.MachineId);
        }
    }

    /// <summary>
    /// Read a single fan status diagnostic and return a human-readable string.
    /// </summary>
    private string? ReadDiagFanStatus(short diagNo)
    {
        var ret = Focas2Interop.ReadDiagnosticData(_handle, diagNo, 0, 8, out var diag);
        if (ret != (short)Focas2Error.EW_OK)
            return null;

        return diag.LData switch
        {
            0 => "NORMAL",
            1 => "SLOW",
            2 => "STOP",
            _ => $"UNKNOWN({diag.LData})"
        };
    }

    /// <summary>
    /// Read battery status from diagnostic data.
    /// Diag 300: CNC battery low (bit 0)
    /// Diag 301: APC (absolute position coder) battery
    /// Diag 302: Spindle position coder battery
    /// The exact diagnostic numbers vary by series; these are for 30i/31i/32i.
    /// </summary>
    private void CollectBatteryStatus(CncMachineData data)
    {
        try
        {
            // CNC battery low — diagnostic 300 (path 0 axis 0)
            var ret = Focas2Interop.ReadDiagnosticData(_handle, 300, 0, 8, out var diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                data.CncBatLow_0_path1_CNC = diag.LData != 0;
                data.CncBatLow_1_path1_CNC = diag.LData != 0; // Same value for both paths
            }

            // APC battery zero (absolute position encoder) — diag 301
            ret = Focas2Interop.ReadDiagnosticData(_handle, 301, 1, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                data.ApcBatZero_0_path1_CNC = (diag.LData & 0x01) != 0;
                data.ApcBatLow_0_path1_CNC = (diag.LData & 0x02) != 0;
            }

            ret = Focas2Interop.ReadDiagnosticData(_handle, 301, 2, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                data.ApcBatZero_1_path1_CNC = (diag.LData & 0x01) != 0;
                data.ApcBatLow_1_path1_CNC = (diag.LData & 0x02) != 0;
            }

            // Independent encoder battery zero — diag 302
            ret = Focas2Interop.ReadDiagnosticData(_handle, 302, 1, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.IndBatZero_0_path1_CNC = diag.LData != 0;

            ret = Focas2Interop.ReadDiagnosticData(_handle, 302, 2, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.IndBatZero_1_path1_CNC = diag.LData != 0;

            // Spindle position coder battery — diag 303
            ret = Focas2Interop.ReadDiagnosticData(_handle, 303, 0, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                data.SpdBatZero_0_path1_CNC = (diag.LData & 0x01) != 0;
                data.SpdBatZero_1_path1_CNC = (diag.LData & 0x02) != 0;
            }

            // Serial spindle position coder battery — diag 304
            ret = Focas2Interop.ReadDiagnosticData(_handle, 304, 0, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                data.SSpdBatZero_0_path1_CNC = (diag.LData & 0x01) != 0;
                data.SSpdBatZero_1_path1_CNC = (diag.LData & 0x02) != 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read battery status", _config.MachineId);
        }
    }

    /// <summary>
    /// Read servo diagnostics for a specific axis index (0-based).
    /// Includes: servo motor temperature, pulse coder temperature, insulation resistance.
    ///
    /// Diagnostic numbers (per axis):
    ///   308 — Servo motor temperature (°C)
    ///   309 — Pulse coder temperature (°C, if available)
    ///   413 — Insulation resistance (leakage) data (MΩ)
    /// </summary>
    private void CollectServoDiagnostics(CncMachineData data, int servoIndex)
    {
        try
        {
            short axisNum = (short)(servoIndex + 1); // FOCAS2 uses 1-based axis numbers

            // Servo motor temperature — diagnostic 308
            var ret = Focas2Interop.ReadDiagnosticData(_handle, 308, axisNum, 8, out var diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                if (servoIndex == 0) data.ServoTemp_0_path1_CNC = diag.LData;
                else data.ServoTemp_1_path1_CNC = diag.LData;
            }

            // Pulse coder temperature — diagnostic 309
            ret = Focas2Interop.ReadDiagnosticData(_handle, 309, axisNum, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                if (servoIndex == 0) data.PulseCoderTemp_0_path1_CNC = diag.LData;
                else data.PulseCoderTemp_1_path1_CNC = diag.LData;
            }

            // Insulation resistance — diagnostic 413
            ret = Focas2Interop.ReadDiagnosticData(_handle, 413, axisNum, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
            {
                if (servoIndex == 0) data.ServoLeakResistData_0_path1_CNC = diag.LData;
                else data.ServoLeakResistData_1_path1_CNC = diag.LData;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read servo diagnostics for axis {Axis}",
                _config.MachineId, servoIndex);
        }
    }

    /// <summary>
    /// Read servo amplifier fan status for a specific axis index.
    /// Diagnostic 4510-4513: servo amplifier fan status (per axis).
    /// Values: 0=Normal, 1=Slow, 2=Stopped
    /// </summary>
    private void CollectServoFanStatus(CncMachineData data, int servoIndex)
    {
        try
        {
            short axisNum = (short)(servoIndex + 1);

            // Internal fan 1 — diag 4510
            var inFan1 = ReadDiagFanStatusPerAxis(4510, axisNum);
            // Internal fan 2 — diag 4511
            var inFan2 = ReadDiagFanStatusPerAxis(4511, axisNum);
            // Radiator fan 1 — diag 4512
            var radFan1 = ReadDiagFanStatusPerAxis(4512, axisNum);
            // Radiator fan 2 — diag 4513
            var radFan2 = ReadDiagFanStatusPerAxis(4513, axisNum);

            if (servoIndex == 0)
            {
                data.InFan1SrvAmpStatus_0_path1_CNC = inFan1;
                data.InFan2SrvAmpStatus_0_path1_CNC = inFan2;
                data.RadFan1SrvAmpStatus_0_path1_CNC = radFan1;
                data.RadFan2SrvAmpStatus_0_path1_CNC = radFan2;
                // Common power supply fans (same diag with axis=0)
                data.InFan1SrvComPwStatus_0_path1_CNC = ReadDiagFanStatusPerAxis(4514, 0);
                data.InFan2SrvComPwStatus_0_path1_CNC = ReadDiagFanStatusPerAxis(4515, 0);
                data.RadFan1SrvComPwStatus_0_path1_CNC = ReadDiagFanStatusPerAxis(4516, 0);
                data.RadFan2SrvComPwStatus_0_path1_CNC = ReadDiagFanStatusPerAxis(4517, 0);
            }
            else
            {
                data.InFan1SrvAmpStatus_1_path1_CNC = inFan1;
                data.InFan2SrvAmpStatus_1_path1_CNC = inFan2;
                data.RadFan1SrvAmpStatus_1_path1_CNC = radFan1;
                data.RadFan2SrvAmpStatus_1_path1_CNC = radFan2;
                data.InFan1SrvComPwStatus_1_path1_CNC = ReadDiagFanStatusPerAxis(4514, 0);
                data.InFan2SrvComPwStatus_1_path1_CNC = ReadDiagFanStatusPerAxis(4515, 0);
                data.RadFan1SrvComPwStatus_1_path1_CNC = ReadDiagFanStatusPerAxis(4516, 0);
                data.RadFan2SrvComPwStatus_1_path1_CNC = ReadDiagFanStatusPerAxis(4517, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read servo fan status for axis {Axis}",
                _config.MachineId, servoIndex);
        }
    }

    /// <summary>
    /// Read spindle diagnostics: temperature, insulation resistance, revolution counts.
    /// Diagnostic numbers:
    ///   403 — Spindle motor temperature (°C)
    ///   410 — Spindle insulation resistance (MΩ)
    ///   417 — Spindle total revolution count (lower 32 bits)
    ///   418 — Spindle total revolution count (upper 32 bits)
    /// </summary>
    private void CollectSpindleDiagnostics(CncMachineData data)
    {
        try
        {
            // Spindle temperature — diagnostic 403
            var ret = Focas2Interop.ReadDiagnosticData(_handle, 403, 0, 8, out var diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.SpindleTemp_0_path1_CNC = diag.LData;

            // Spindle insulation resistance — diagnostic 410
            ret = Focas2Interop.ReadDiagnosticData(_handle, 410, 0, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.SpindleLeakResistData_0_path1_CNC = diag.LData;

            // Spindle total revolution count (low 32 bits) — diagnostic 417
            ret = Focas2Interop.ReadDiagnosticData(_handle, 417, 0, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.SpindleTotalRev1_0_path1_CNC = diag.LData;

            // Spindle total revolution count (high 32 bits) — diagnostic 418
            ret = Focas2Interop.ReadDiagnosticData(_handle, 418, 0, 8, out diag);
            if (ret == (short)Focas2Error.EW_OK)
                data.SpindleTotalRev2_0_path1_CNC = diag.LData;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read spindle diagnostics", _config.MachineId);
        }
    }

    /// <summary>
    /// Read spindle amplifier fan status.
    /// Diagnostic 4520-4527: spindle amp fans, common power supply fans.
    /// </summary>
    private void CollectSpindleFanStatus(CncMachineData data)
    {
        try
        {
            // Spindle common power supply fans
            data.InFan1SpdlComPwStatus_0_path1_CNC = ReadDiagFanStatus(4520);
            data.InFan2SpdlComPwStatus_0_path1_CNC = ReadDiagFanStatus(4521);
            data.RadFan1SpdlComPwStatus_0_path1_CNC = ReadDiagFanStatus(4522);
            data.RadFan2SpdlComPwStatus_0_path1_CNC = ReadDiagFanStatus(4523);

            // Spindle amplifier fans
            data.InFan1SpindleAmpStatus_0_path1_CNC = ReadDiagFanStatus(4524);
            data.InFan2SpindleAmpStatus_0_path1_CNC = ReadDiagFanStatus(4525);
            data.RadFan1SpindleAmpStatus_0_path1_CNC = ReadDiagFanStatus(4526);
            data.RadFan2SpindleAmpStatus_0_path1_CNC = ReadDiagFanStatus(4527);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to read spindle fan status", _config.MachineId);
        }
    }

    /// <summary>
    /// Read a per-axis fan diagnostic and return human-readable string.
    /// </summary>
    private string? ReadDiagFanStatusPerAxis(short diagNo, short axisNum)
    {
        var ret = Focas2Interop.ReadDiagnosticData(_handle, diagNo, axisNum, 8, out var diag);
        if (ret != (short)Focas2Error.EW_OK)
            return null;

        return diag.LData switch
        {
            0 => "NORMAL",
            1 => "SLOW",
            2 => "STOP",
            _ => $"UNKNOWN({diag.LData})"
        };
    }

    // =========================================================================
    // TOOL DATA COLLECTION
    // =========================================================================
    // Full tool information: current T-code, all offsets, tool life management.
    // This data is FREE with FOCAS2 — MT-LINKi requires an additional license.
    // =========================================================================

    /// <summary>
    /// Master method for collecting all tool-related data.
    /// </summary>
    private void CollectToolData(CncMachineData data)
    {
        try
        {
            var toolInfo = new ToolInfoData();

            // 1. Read current T-code (active tool number)
            toolInfo.CurrentToolNumber = ReadCurrentTCode();

            // 2. Read tool offset configuration and values
            ReadToolOffsets(toolInfo);

            // 3. Read tool life management data (if supported)
            ReadToolLifeData(toolInfo);

            data.ToolInfo = toolInfo;

            _logger.LogDebug(
                "Machine {MachineId}: Tool data — T{Tool}, {OffsetCount} offsets (Type {Type}), " +
                "{NonZeroCount} with data, ToolLife={LifeEnabled}",
                _config.MachineId,
                toolInfo.CurrentToolNumber,
                toolInfo.OffsetCount,
                toolInfo.OffsetMemoryType,
                toolInfo.Offsets.Count,
                toolInfo.ToolLifeEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to collect tool data", _config.MachineId);
        }
    }

    /// <summary>
    /// Read the current T-code (active tool number).
    /// Tries multiple methods in order of reliability:
    ///   1. cnc_modal type=108 (standard approach)
    ///   2. cnc_rdmacro #4120 (system variable — works on most controllers)
    ///   3. cnc_rdmacro #4120 with alternate format
    /// </summary>
    private int ReadCurrentTCode()
    {
        // Method 1: cnc_modal with type=108 (T-code)
        try
        {
            var buffer = new byte[40];
            var ret = Focas2Interop.ReadModal(_handle, 108, 40, buffer);
            if (ret == (short)Focas2Error.EW_OK)
            {
                int tCode = BitConverter.ToInt32(buffer, 4);
                if (tCode > 0 && tCode <= 99999)
                    return tCode;
            }
            else
            {
                _logger.LogDebug("Machine {MachineId}: cnc_modal(108) returned {Code}, trying macro variable",
                    _config.MachineId, (Focas2Error)ret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: cnc_modal failed", _config.MachineId);
        }

        // Method 2: Read system macro variable #4120 (current T-code, path 1)
        try
        {
            var ret = Focas2Interop.ReadMacro(_handle, 4120, 10, out var macro);
            if (ret == (short)Focas2Error.EW_OK)
            {
                // Value = McVal × 10^(-McDig)
                // For T-code, McDig is typically 0 (integer value)
                double value = macro.McVal * Math.Pow(10, -macro.McDig);
                int tCode = (int)Math.Round(value);
                if (tCode > 0 && tCode <= 99999)
                {
                    _logger.LogDebug("Machine {MachineId}: T-code from macro #4120 = T{TCode}",
                        _config.MachineId, tCode);
                    return tCode;
                }
            }
            else
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdmacro(#4120) returned {Code}",
                    _config.MachineId, (Focas2Error)ret);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Macro #4120 read failed", _config.MachineId);
        }

        return 0;
    }

    /// <summary>
    /// Read tool offset configuration (type and count) and all non-zero offset values.
    /// Uses multiple fallback strategies for CNC models that don't support all APIs.
    /// </summary>
    private void ReadToolOffsets(ToolInfoData toolInfo)
    {
        try
        {
            // Try cnc_rdtofsinfo2 first (returns both type and count)
            short ofsType = 0;
            short useNo = 0;
            var ret = Focas2Interop.ReadToolOffsetInfo2(_handle, out ofsType, out useNo);
            if (ret != (short)Focas2Error.EW_OK)
            {
                // Fallback to cnc_rdtofsinfo (count only, assume type A)
                ret = Focas2Interop.ReadToolOffsetInfo(_handle, out useNo);
                if (ret != (short)Focas2Error.EW_OK)
                {
                    _logger.LogDebug("Machine {MachineId}: cnc_rdtofsinfo returned {Code} — will probe offsets individually",
                        _config.MachineId, (Focas2Error)ret);
                }
                ofsType = 0; // Default to type A
            }

            toolInfo.OffsetMemoryType = ofsType switch
            {
                0 => "A",
                1 => "B",
                2 => "C",
                _ => $"Unknown({ofsType})"
            };

            // If API reported 0 offsets, probe individually to discover actual tools.
            // Some controllers (like G342) don't properly report count via rdtofsinfo.
            if (useNo <= 0)
            {
                _logger.LogDebug("Machine {MachineId}: Offset count=0 from API — probing offsets 1-64 individually",
                    _config.MachineId);
                useNo = ProbeToolOffsetCount(ofsType);
            }

            toolInfo.OffsetCount = useNo;

            _logger.LogDebug("Machine {MachineId}: Tool offset system — Type={Type}, Count={Count}",
                _config.MachineId, toolInfo.OffsetMemoryType, useNo);

            if (useNo <= 0)
                return;

            // Read all tool offsets based on memory type
            int maxTools = Math.Min((int)useNo, 200);

            switch (ofsType)
            {
                case 0: // Memory A — single offset per tool
                    ReadOffsetsTypeA(toolInfo, maxTools);
                    break;
                case 1: // Memory B — wear + geometry
                    ReadOffsetsTypeB(toolInfo, maxTools);
                    break;
                case 2: // Memory C — wear length/radius + geometry length/radius
                    ReadOffsetsTypeC(toolInfo, maxTools);
                    break;
                default:
                    ReadOffsetsTypeA(toolInfo, maxTools);
                    break;
            }

            // If batch read found nothing, try individual reads as final fallback
            if (toolInfo.Offsets.Count == 0 && useNo > 0)
            {
                _logger.LogDebug("Machine {MachineId}: Batch read found 0 offsets — trying individual reads",
                    _config.MachineId);
                ReadOffsetsIndividual(toolInfo, Math.Min((int)useNo, 64), ofsType);
            }

            // Mark the current tool's offset as active
            if (toolInfo.CurrentToolNumber > 0)
            {
                var activeOffset = toolInfo.Offsets.FirstOrDefault(
                    o => o.Number == toolInfo.CurrentToolNumber);
                if (activeOffset != null)
                    activeOffset.IsActive = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Error reading tool offsets", _config.MachineId);
        }
    }

    /// <summary>
    /// Probe tool offsets individually to discover how many exist.
    /// Used when cnc_rdtofsinfo returns 0 (unsupported on some CNC models).
    /// Tries reading offset #1, #2, ... up to 64 and counts successes.
    /// </summary>
    private short ProbeToolOffsetCount(short ofsType)
    {
        short maxFound = 0;
        for (short i = 1; i <= 64; i++)
        {
            var ret = Focas2Interop.ReadToolOffset(_handle, i, 0, 8, out var tofs);
            if (ret == (short)Focas2Error.EW_OK)
            {
                maxFound = i;
            }
            else if (ret == (short)Focas2Error.EW_NUMBER)
            {
                // EW_NUMBER means this offset number doesn't exist — we've found the limit
                break;
            }
            else
            {
                // Other error — stop probing
                break;
            }
        }
        return maxFound;
    }

    /// <summary>
    /// Read offsets for Memory Type A (single combined offset per tool).
    /// Uses batch read (cnc_rdtofsr) for efficiency.
    /// </summary>
    private void ReadOffsetsTypeA(ToolInfoData toolInfo, int maxTools)
    {
        // Read in chunks of 50 to keep buffer sizes manageable
        for (int start = 1; start <= maxTools; start += 50)
        {
            int end = Math.Min(start + 49, maxTools);
            int count = end - start + 1;

            // Buffer: 6 header bytes + 4 bytes per offset value
            int bufLen = 6 + 4 * count;
            var buffer = new byte[bufLen];

            var ret = Focas2Interop.ReadToolOffsetRange(
                _handle, (short)start, 0, (short)end, (short)bufLen, buffer);

            if (ret != (short)Focas2Error.EW_OK)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdtofsr(A, {Start}-{End}) returned {Code}",
                    _config.MachineId, start, end, (Focas2Error)ret);
                break;
            }

            // Parse values: header at [0-5], then 4 bytes per value
            for (int i = 0; i < count; i++)
            {
                int rawValue = BitConverter.ToInt32(buffer, 6 + i * 4);
                if (rawValue != 0)
                {
                    toolInfo.Offsets.Add(new ToolOffsetEntry
                    {
                        Number = start + i,
                        WearLength = rawValue / 1000.0 // IS-B metric default
                    });
                }
            }
        }
    }

    /// <summary>
    /// Read offsets for Memory Type B (separate wear + geometry).
    /// </summary>
    private void ReadOffsetsTypeB(ToolInfoData toolInfo, int maxTools)
    {
        // Read wear offsets (type=0) and geometry offsets (type=1) separately
        var wearValues = ReadOffsetBatch(maxTools, 0);
        var geomValues = ReadOffsetBatch(maxTools, 1);

        for (int i = 0; i < maxTools; i++)
        {
            int toolNum = i + 1;
            int wear = wearValues.TryGetValue(toolNum, out var w) ? w : 0;
            int geom = geomValues.TryGetValue(toolNum, out var g) ? g : 0;

            if (wear != 0 || geom != 0)
            {
                toolInfo.Offsets.Add(new ToolOffsetEntry
                {
                    Number = toolNum,
                    WearLength = wear / 1000.0,
                    GeometryLength = geom / 1000.0
                });
            }
        }
    }

    /// <summary>
    /// Read offsets for Memory Type C (wear length/radius + geometry length/radius).
    /// </summary>
    private void ReadOffsetsTypeC(ToolInfoData toolInfo, int maxTools)
    {
        // Type C has 4 sub-types: 0=wear length, 1=wear radius, 2=geom length, 3=geom radius
        var wearLen = ReadOffsetBatch(maxTools, 0);
        var wearRad = ReadOffsetBatch(maxTools, 1);
        var geomLen = ReadOffsetBatch(maxTools, 2);
        var geomRad = ReadOffsetBatch(maxTools, 3);

        for (int i = 0; i < maxTools; i++)
        {
            int toolNum = i + 1;
            int wl = wearLen.TryGetValue(toolNum, out var v1) ? v1 : 0;
            int wr = wearRad.TryGetValue(toolNum, out var v2) ? v2 : 0;
            int gl = geomLen.TryGetValue(toolNum, out var v3) ? v3 : 0;
            int gr = geomRad.TryGetValue(toolNum, out var v4) ? v4 : 0;

            if (wl != 0 || wr != 0 || gl != 0 || gr != 0)
            {
                toolInfo.Offsets.Add(new ToolOffsetEntry
                {
                    Number = toolNum,
                    WearLength = wl / 1000.0,
                    WearRadius = wr / 1000.0,
                    GeometryLength = gl / 1000.0,
                    GeometryRadius = gr / 1000.0
                });
            }
        }
    }

    /// <summary>
    /// Batch-read tool offset values for a given type using cnc_rdtofsr.
    /// Returns a dictionary of (toolNumber → rawValue) for non-zero entries.
    /// </summary>
    private Dictionary<int, int> ReadOffsetBatch(int maxTools, short type)
    {
        var result = new Dictionary<int, int>();

        for (int start = 1; start <= maxTools; start += 50)
        {
            int end = Math.Min(start + 49, maxTools);
            int count = end - start + 1;
            int bufLen = 6 + 4 * count;
            var buffer = new byte[bufLen];

            var ret = Focas2Interop.ReadToolOffsetRange(
                _handle, (short)start, type, (short)end, (short)bufLen, buffer);

            if (ret != (short)Focas2Error.EW_OK)
                break;

            for (int i = 0; i < count; i++)
            {
                int rawValue = BitConverter.ToInt32(buffer, 6 + i * 4);
                if (rawValue != 0)
                    result[start + i] = rawValue;
            }
        }

        return result;
    }

    /// <summary>
    /// Fallback: read tool offsets one-by-one using cnc_rdtofs.
    /// Used when batch read (cnc_rdtofsr) fails on some CNC models.
    /// </summary>
    private void ReadOffsetsIndividual(ToolInfoData toolInfo, int maxTools, short ofsType)
    {
        for (short toolNum = 1; toolNum <= maxTools; toolNum++)
        {
            try
            {
                var entry = new ToolOffsetEntry { Number = toolNum };
                bool hasData = false;

                if (ofsType == 0) // Type A: single offset
                {
                    var ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 0, 8, out var tofs);
                    if (ret == (short)Focas2Error.EW_OK && tofs.Data != 0)
                    {
                        entry.WearLength = tofs.Data / 1000.0;
                        hasData = true;
                    }
                }
                else if (ofsType == 1) // Type B: wear + geometry
                {
                    var ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 0, 8, out var wear);
                    if (ret == (short)Focas2Error.EW_OK && wear.Data != 0)
                    {
                        entry.WearLength = wear.Data / 1000.0;
                        hasData = true;
                    }
                    ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 1, 8, out var geom);
                    if (ret == (short)Focas2Error.EW_OK && geom.Data != 0)
                    {
                        entry.GeometryLength = geom.Data / 1000.0;
                        hasData = true;
                    }
                }
                else // Type C: wear len/rad + geom len/rad
                {
                    var ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 0, 8, out var wl);
                    if (ret == (short)Focas2Error.EW_OK && wl.Data != 0) { entry.WearLength = wl.Data / 1000.0; hasData = true; }
                    ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 1, 8, out var wr);
                    if (ret == (short)Focas2Error.EW_OK && wr.Data != 0) { entry.WearRadius = wr.Data / 1000.0; hasData = true; }
                    ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 2, 8, out var gl);
                    if (ret == (short)Focas2Error.EW_OK && gl.Data != 0) { entry.GeometryLength = gl.Data / 1000.0; hasData = true; }
                    ret = Focas2Interop.ReadToolOffset(_handle, toolNum, 3, 8, out var gr);
                    if (ret == (short)Focas2Error.EW_OK && gr.Data != 0) { entry.GeometryRadius = gr.Data / 1000.0; hasData = true; }
                }

                if (hasData)
                    toolInfo.Offsets.Add(entry);
            }
            catch
            {
                // Skip this tool on any error
            }
        }
    }

    /// <summary>
    /// Read tool life management data (if the feature is enabled on this CNC).
    /// Collects group info, life counters, and limits.
    /// </summary>
    private void ReadToolLifeData(ToolInfoData toolInfo)
    {
        try
        {
            // Check if tool life management is available
            var infoBuffer = new byte[6];
            var ret = Focas2Interop.ReadToolLifeInfo(_handle, infoBuffer);
            if (ret != (short)Focas2Error.EW_OK)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdtlinfo returned {Code} — tool life not available",
                    _config.MachineId, (Focas2Error)ret);
                toolInfo.ToolLifeEnabled = false;
                return;
            }

            short maxGroups = BitConverter.ToInt16(infoBuffer, 0);
            short countType = BitConverter.ToInt16(infoBuffer, 2);

            if (maxGroups <= 0)
            {
                toolInfo.ToolLifeEnabled = false;
                return;
            }

            toolInfo.ToolLifeEnabled = true;
            string countTypeStr = countType == 1 ? "Minutes" : "Count";

            // Read number of registered groups
            ret = Focas2Interop.ReadToolLifeGroupCount(_handle, out short numGroups);
            if (ret != (short)Focas2Error.EW_OK || numGroups <= 0)
            {
                _logger.LogDebug("Machine {MachineId}: cnc_rdngrp returned {Code}, numGroups={Num}",
                    _config.MachineId, (Focas2Error)ret, numGroups);
                return;
            }

            // Read the currently active tool life group
            int activeGroup = 0;
            Focas2Interop.ReadToolLifeUseGroup(_handle, out activeGroup);

            _logger.LogDebug("Machine {MachineId}: Tool life — {NumGroups} groups, active={ActiveGroup}, countType={CountType}",
                _config.MachineId, numGroups, activeGroup, countTypeStr);

            // Read each group's data
            // Limit to first 50 groups to avoid very long poll times
            int groupsToRead = Math.Min((int)numGroups, 50);
            for (int g = 1; g <= groupsToRead; g++)
            {
                try
                {
                    // cnc_rdtlgrp returns variable-size data depending on controller
                    // Buffer layout: [4 groupNo][4 nTool][4 life_count][4 life_limit][tool data...]
                    var grpBuffer = new byte[128]; // Enough for group header + a few tools
                    ret = Focas2Interop.ReadToolLifeGroup(_handle, g, grpBuffer);
                    if (ret != (short)Focas2Error.EW_OK)
                        continue;

                    int groupNo = BitConverter.ToInt32(grpBuffer, 0);
                    int nTools = BitConverter.ToInt32(grpBuffer, 4);
                    int lifeCount = BitConverter.ToInt32(grpBuffer, 8);
                    int lifeLimit = BitConverter.ToInt32(grpBuffer, 12);

                    // Determine status
                    string status;
                    if (groupNo == activeGroup)
                        status = "Active";
                    else if (lifeLimit > 0 && lifeCount >= lifeLimit)
                        status = "Expired";
                    else
                        status = "Standby";

                    toolInfo.LifeGroups.Add(new ToolLifeEntry
                    {
                        GroupNo = groupNo > 0 ? groupNo : g,
                        ToolNo = nTools, // Number of tools in this group
                        LifeCounter = lifeCount,
                        LifeLimit = lifeLimit,
                        CountType = countTypeStr,
                        Status = status
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Machine {MachineId}: Error reading tool life group {Group}",
                        _config.MachineId, g);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Error reading tool life data", _config.MachineId);
            toolInfo.ToolLifeEnabled = false;
        }
    }

    // =========================================================================
    // STATUS DETERMINATION
    // =========================================================================

    /// <summary>
    /// Determine the overall machine status from the collected data snapshot.
    /// Priority: Emergency > Alarm > Online (with run state in RunningStatus).
    /// </summary>
    private static MachineStatus DetermineMachineStatus(CncMachineData data)
    {
        // Emergency stop takes highest priority
        if (data.EmergencyStop == true)
            return MachineStatus.Alarm;

        // Active alarms
        if (data.ActiveAlarms.Count > 0)
            return MachineStatus.Alarm;

        return MachineStatus.Online;
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private string[] ReadAxisNames()
    {
        short count = Focas2Interop.MAX_AXIS;
        var names = new OdbAxisName[Focas2Interop.MAX_AXIS];
        var ret = Focas2Interop.ReadAxisNames(_handle, ref count, names);

        if (ret != (short)Focas2Error.EW_OK)
        {
            _logger.LogDebug("Machine {MachineId}: cnc_rdaxisname returned {Code}, using defaults",
                _config.MachineId, (Focas2Error)ret);
            return new[] { "X", "Y", "Z", "A", "B", "C" };
        }

        var result = names.Take(count)
            .Select(n => n.Name?.Trim('\0', ' ') ?? "?")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray();

        return result.Length > 0 ? result : new[] { "X", "Y", "Z" };
    }

    private bool HasDataPoint(string name) => _config.DataPoints.Contains(name);
    private bool HasAnyDataPoint(string prefix) => _config.DataPoints.Any(d => d.StartsWith(prefix));

    private static string GetAlarmTypeName(short type) => type switch
    {
        0 => "BG", 1 => "PS", 2 => "OH", 3 => "SB",
        4 => "SN", 5 => "SW", 6 => "OT", 7 => "PC",
        8 => "EX", 9 => "SV", 10 => "IO", 11 => "PW",
        12 => "MC", _ => $"T{type}"
    };

    private static void CheckResult(short ret, string functionName)
    {
        if (ret != (short)Focas2Error.EW_OK)
            throw new Focas2Exception((Focas2Error)ret, functionName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}

/// <summary>
/// Exception for FOCAS2 library errors.
/// </summary>
public sealed class Focas2Exception : Exception
{
    public Focas2Error ErrorCode { get; }
    public string FunctionName { get; }

    public Focas2Exception(Focas2Error errorCode, string functionName)
        : base($"FOCAS2 {functionName} failed with error {errorCode} ({(short)errorCode})")
    {
        ErrorCode = errorCode;
        FunctionName = functionName;
    }
}
