// ============================================================================
// File: DataSources/BrotherHttpDataSource.cs
// Purpose: Collects CNC machine data from Brother CNC machines via their
//          built-in HTTP web monitoring interface. No licenses required.
//
// BROTHER BUILT-IN HTTP API:
//   Brother Speedio (and other Brother CNC models) expose a web monitoring
//   interface on port 80 with CSV/text endpoints for machine data.
//
// ENDPOINTS USED:
//   /HTTPD_MCNINFO       -> Machine info: hostname, model, status code
//   /HTTPD_DATE          -> Current CNC datetime (YYYYMMDDHHmmss)
//   /MNTP_WKCNTR         -> Work counters (4 counters with count/target/signal)
//   /MNTP_CYCLETIME      -> Program, cycle/cutting times, operation hours
//   /ATC_TOOLS           -> ATC magazine: tool positions, lengths, radii, names
//   /ALARM_CURALMLIST     -> Current active alarms
//   /MNTP_MAINTNOTICE    -> Maintenance notices (greasing, etc.)
//
// BROTHER STATUS CODES (from HTTPD_MCNINFO):
//   0 = Power Off / Unknown
//   1 = Idle / Ready
//   2 = Stopped / Hold
//   3 = Running (cycle active)
//   4 = Alarm
//
// SUPPORTED MODELS:
//   Brother Speedio S300, S500, S700, S1000, R450, R650
//   Brother Speedio SXd1, M140, M200, W1000
//   Any Brother CNC with the built-in web monitoring interface
// ============================================================================

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Collects CNC machine data from Brother CNC machines via their built-in
/// HTTP web monitoring interface (port 80). No licenses or proprietary DLLs needed.
/// </summary>
public sealed class BrotherHttpDataSource : IMachineDataSource
{
    private readonly MachineConfig _config;
    private readonly BrotherHttpSettings _brotherSettings;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    // Cached machine info (read once)
    private string? _hostname;
    private string? _model;
    private bool _firstReadDone;

    // Track consecutive failures for status determination
    private int _consecutiveFailures;

    public string MachineId => _config.MachineId;
    public string MachineName => _config.MachineName;
    public DataSourceType SourceType => DataSourceType.BrotherHttp;

    public BrotherHttpDataSource(MachineConfig config, ILogger logger)
    {
        _config = config;
        _brotherSettings = config.BrotherHttp
            ?? throw new ArgumentNullException(nameof(config), "BrotherHttp settings are required");
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_brotherSettings.BaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(_brotherSettings.TimeoutSeconds)
        };
    }

    public async Task<CncMachineData> CollectDataAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var data = new CncMachineData
        {
            MachineId = _config.MachineId,
            MachineName = _config.MachineName,
            Tags = _config.Tags
        };

        try
        {
            // Fetch all endpoints concurrently for speed
            var mcnInfoTask = FetchTextAsync("/HTTPD_MCNINFO", ct);
            var cycleTimeTask = FetchTextAsync("/MNTP_CYCLETIME", ct);
            var workCounterTask = FetchTextAsync("/MNTP_WKCNTR", ct);
            var atcToolsTask = FetchTextAsync("/ATC_TOOLS", ct);
            var alarmsTask = FetchTextAsync("/ALARM_CURALMLIST", ct);
            var maintTask = FetchTextAsync("/MNTP_MAINTNOTICE", ct);

            await Task.WhenAll(mcnInfoTask, cycleTimeTask, workCounterTask,
                               atcToolsTask, alarmsTask, maintTask);

            var mcnInfo = mcnInfoTask.Result;
            var cycleTime = cycleTimeTask.Result;
            var workCounter = workCounterTask.Result;
            var atcTools = atcToolsTask.Result;
            var alarms = alarmsTask.Result;
            var maintenance = maintTask.Result;

            // If machine info fails, machine is unreachable
            if (mcnInfo == null)
            {
                data.Status = MachineStatus.Offline;
                data.Disconnect_CNC = true;
                sw.Stop();
                data.CollectionDurationMs = sw.ElapsedMilliseconds;
                return data;
            }

            // Parse all responses
            ParseMachineInfo(mcnInfo, data);
            ParseCycleTime(cycleTime, data);
            ParseWorkCounters(workCounter, data);
            ParseAtcTools(atcTools, data);
            ParseAlarms(alarms, data);
            ParseMaintenanceNotices(maintenance, data);

            _consecutiveFailures = 0;
            data.Disconnect_CNC = false;

            // Determine status
            if (data.ActiveAlarms.Count > 0)
                data.Status = MachineStatus.Alarm;
            else
                data.Status = MachineStatus.Online;

            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;

            if (!_firstReadDone)
            {
                _logger.LogInformation(
                    "Machine {MachineId}: Brother {Model} ({Hostname}) — first read OK in {Duration}ms. " +
                    "State={State}, Program={Program}, PartsCount={Parts}, Tools={ToolCount}",
                    _config.MachineId, _model, _hostname, sw.ElapsedMilliseconds,
                    data.CncState_path1_CNC, data.MainProgram,
                    data.PartsCount, data.ToolInfo?.Offsets?.Count ?? 0);
                _firstReadDone = true;
            }
            else
            {
                _logger.LogDebug(
                    "Machine {MachineId}: Brother data collected in {Duration}ms — " +
                    "State={State}, Program={Program}, Parts={Parts}",
                    _config.MachineId, sw.ElapsedMilliseconds,
                    data.CncState_path1_CNC, data.MainProgram, data.PartsCount);
            }

            return data;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogWarning(ex,
                "Machine {MachineId}: Brother HTTP collection failed (consecutive: {Failures})",
                _config.MachineId, _consecutiveFailures);

            data.Status = _consecutiveFailures >= 3 ? MachineStatus.Offline : MachineStatus.Error;
            data.Disconnect_CNC = true;
            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;
            return data;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("/HTTPD_MCNINFO", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ========================================================================
    // HTTP Fetch
    // ========================================================================

    private async Task<string?> FetchTextAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(endpoint, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(
                "Machine {MachineId}: Brother {Endpoint} request failed — {Message}",
                _config.MachineId, endpoint, ex.Message);
            return null;
        }
    }

    // ========================================================================
    // CSV/Text Parsing — Brother HTTP Responses
    // ========================================================================

    /// <summary>
    /// Parse HTTPD_MCNINFO response.
    /// Format: hostname,model,statusCode,?,?,language
    /// Example: BRN68E74A6608EA,SXd1,3,01,0,1
    ///
    /// Status codes:
    ///   0 = Unknown/Off, 1 = Idle/Ready, 2 = Stopped/Hold, 3 = Running, 4 = Alarm
    /// </summary>
    private void ParseMachineInfo(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        var parts = response.Trim().Split(',');
        if (parts.Length < 3) return;

        _hostname = parts[0].Trim();
        _model = parts[1].Trim();
        var statusCode = parts[2].Trim();

        // Store model info
        data.SystemInfo = new CncSystemInfo
        {
            Series = $"Brother {_model}",
            CncType = "Brother",
            Version = _hostname
        };

        // Map Brother status code to CncState
        // Known codes from SXd1: 0=Off, 1=Idle, 2=Hold, 3=Running, 4=Notification, 5=Standby
        // NOTE: Status 4 means "notification/alarm present" but could be informational
        // (e.g., standby 0501). Don't set ALARM here — let ParseAlarms decide based
        // on the actual alarm list. Default to STOP for status 4.
        data.CncState_path1_CNC = statusCode switch
        {
            "0" => "STOP",         // Power off / unknown
            "1" => "STOP",         // Idle / ready
            "2" => "SUSPEND",      // Stopped / hold
            "3" => "OPERATE",      // Running (cycle active)
            "4" => "STOP",         // Notification present — ParseAlarms will upgrade to ALARM if real
            "5" => "STOP",         // Standby / sleep mode
            _ => "STOP"
        };

        // Map to running status
        data.RunningStatus = statusCode switch
        {
            "0" => "Stopped",
            "1" => "Idle",
            "2" => "Hold",
            "3" => "Running",
            "4" => "Idle",         // Notification present, not necessarily running
            "5" => "Standby",
            _ => "Unknown"
        };

        // Mode — Brother doesn't expose mode separately via this endpoint
        data.Mode_path1_CNC = statusCode == "3" ? "MEM" : "MEM";
        data.AutoMode = "MEM";
        data.EmergencyStop = false;
    }

    /// <summary>
    /// Parse MNTP_CYCLETIME response.
    /// Format: key-value pairs, comma-separated within lines.
    /// Example:
    ///   Program,(,0926,)
    ///   Cycle time,0000:04:56.2
    ///   Cutting time,0000:04:43.5
    ///   Non cutting time,0000:00:12.7
    ///   Others,0.0
    ///   Cutting time / cycle time, 95 %
    ///   (Other times are not included in the cycle time)
    ///   Operation time,3462:45:28
    ///   Power on time,2496:17:52
    ///   Total operation time,3465:37:02
    ///   Date,20260322115936
    ///   Operation end counter,0994
    /// </summary>
    private void ParseCycleTime(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrEmpty(line)) continue;

            // Program line: "Program,(,0926,)"
            if (line.StartsWith("Program,", StringComparison.OrdinalIgnoreCase))
            {
                var programPart = line.Substring("Program,".Length).Trim();
                // Extract program number from "(,0926,)" format
                var progNum = programPart.Replace("(", "").Replace(")", "").Replace(",", "").Trim();
                if (!string.IsNullOrEmpty(progNum))
                {
                    data.MainProgram = $"O{progNum}";
                    data.MainProgram_path1_CNC = data.MainProgram;
                    data.ActProgram_path1_CNC = data.MainProgram;
                }
                continue;
            }

            // Cycle time: "Cycle time,0000:04:56.2"
            if (line.StartsWith("Cycle time,", StringComparison.OrdinalIgnoreCase))
            {
                var timeStr = line.Substring("Cycle time,".Length).Trim();
                data.CycleTimeSeconds = ParseBrotherTimeToSeconds(timeStr);
                continue;
            }

            // Cutting time: "Cutting time,0000:04:43.5"
            if (line.StartsWith("Cutting time,", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("Cutting time /", StringComparison.OrdinalIgnoreCase))
            {
                var timeStr = line.Substring("Cutting time,".Length).Trim();
                var cuttingSec = ParseBrotherTimeToSeconds(timeStr);
                if (cuttingSec.HasValue)
                    data.AdditionalData["CuttingTimeSeconds"] = cuttingSec.Value;
                continue;
            }

            // Operation time: "Operation time,3462:45:28"
            if (line.StartsWith("Operation time,", StringComparison.OrdinalIgnoreCase))
            {
                var timeStr = line.Substring("Operation time,".Length).Trim();
                var opSec = ParseBrotherTimeToSeconds(timeStr);
                if (opSec.HasValue)
                    data.AdditionalData["OperationTimeHours"] = Math.Round(opSec.Value / 3600.0, 1);
                continue;
            }

            // Power on time: "Power on time,2496:17:52"
            if (line.StartsWith("Power on time,", StringComparison.OrdinalIgnoreCase))
            {
                var timeStr = line.Substring("Power on time,".Length).Trim();
                var poSec = ParseBrotherTimeToSeconds(timeStr);
                if (poSec.HasValue)
                    data.AdditionalData["PowerOnTimeHours"] = Math.Round(poSec.Value / 3600.0, 1);
                continue;
            }

            // Operation end counter: "Operation end counter,0994"
            if (line.StartsWith("Operation end counter,", StringComparison.OrdinalIgnoreCase))
            {
                var countStr = line.Substring("Operation end counter,".Length).Trim();
                if (int.TryParse(countStr, out var endCount))
                {
                    data.AdditionalData["OperationEndCounter"] = endCount;
                }
                continue;
            }

            // Cutting ratio: "Cutting time / cycle time, 95 %"
            if (line.StartsWith("Cutting time / cycle time", StringComparison.OrdinalIgnoreCase))
            {
                var ratioPart = line.Split(',').LastOrDefault()?.Trim().Replace("%", "").Trim();
                if (int.TryParse(ratioPart, out var ratio))
                    data.AdditionalData["CuttingRatioPercent"] = ratio;
                continue;
            }
        }
    }

    /// <summary>
    /// Parse MNTP_WKCNTR response.
    /// Format: 4 lines (one per counter), each: count, currentValue, targetValue, endSignalValue, ?, ?
    /// Example:
    ///   999,     0,     0,     0,,0
    ///     0,     0,     0,     0,,0
    /// </summary>
    private void ParseWorkCounters(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;

        // First counter is the primary parts count
        var firstLine = lines[0].Trim().TrimEnd('\r');
        var parts = firstLine.Split(',');
        if (parts.Length >= 1)
        {
            var countStr = parts[0].Trim();
            if (long.TryParse(countStr, out var count))
            {
                data.PartsCount = count;
                data.PartsNum_path1_CNC = (int)count;
            }
        }

        // Store all 4 counters in AdditionalData
        for (int i = 0; i < Math.Min(lines.Length, 4); i++)
        {
            var line = lines[i].Trim().TrimEnd('\r');
            var counterParts = line.Split(',');
            if (counterParts.Length >= 1)
            {
                var cStr = counterParts[0].Trim();
                if (int.TryParse(cStr, out var cVal) && cVal != 0)
                {
                    data.AdditionalData[$"Counter{i + 1}.Count"] = cVal;
                }
                if (counterParts.Length >= 3)
                {
                    var targetStr = counterParts[2].Trim();
                    if (int.TryParse(targetStr, out var target) && target != 0)
                        data.AdditionalData[$"Counter{i + 1}.Target"] = target;
                }
            }
        }
    }

    /// <summary>
    /// Parse ATC_TOOLS response.
    /// First line: "Program(0926)" — indicates current program and optionally active tool
    /// Subsequent lines: ATC magazine slots with tool data.
    /// Format per tool line:
    ///   slotNo, [bgColor], [fgColor], toolNo, ?, ?, toolName, ?, ?, lengthXradius, ?, ?, ?, ?, ?, lifeDisplay, ?, ?, toolType, ?, ?, ?, color1, color2, ?
    ///
    /// Example:
    ///   01,,,001,,,              ,,, 448.254x0.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0
    ///   11,#000000,#ff8000,011,,,              ,,, 537.900x10.100,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0
    ///
    /// Tool 11 has orange highlight (#ff8000) = currently active tool.
    /// </summary>
    private void ParseAtcTools(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;

        var toolInfo = new ToolInfoData
        {
            OffsetMemoryType = "Brother",
            ToolLifeEnabled = false
        };

        int? activeToolNo = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrEmpty(line)) continue;

            // Header line: "Program(0926)"
            if (line.StartsWith("Program(", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Already parsed from MNTP_CYCLETIME
            }

            var parts = line.Split(',');
            if (parts.Length < 10) continue;

            // Parse slot number
            var slotStr = parts[0].Trim();
            if (!int.TryParse(slotStr, out var slotNo)) continue;

            // Check if this tool is active (has highlight colors in fields 1-2)
            // Active tool example: "11,#000000,#ff8000,011,..."  (black bg, orange fg)
            // Inactive tool:       "01,,,001,..."                (no colors)
            var color1 = parts.Length > 1 ? parts[1].Trim() : "";
            var color2 = parts.Length > 2 ? parts[2].Trim() : "";
            bool isActive = !string.IsNullOrEmpty(color1) && color1.StartsWith("#") &&
                            !string.IsNullOrEmpty(color2) && color2.StartsWith("#");

            // Tool number (field index 3)
            var toolNoStr = parts[3].Trim();
            int.TryParse(toolNoStr, out var toolNo);

            if (isActive)
                activeToolNo = toolNo;

            // Tool name (field index 6) — may be blank or contain name like "DRILL6.35"
            var toolName = parts.Length > 6 ? parts[6].Trim() : "";

            // Length x Radius (field index 9) — format: "448.254x0.000"
            double toolLength = 0, toolRadius = 0;
            if (parts.Length > 9)
            {
                var dimStr = parts[9].Trim();
                var dims = dimStr.Split('x', StringSplitOptions.RemoveEmptyEntries);
                if (dims.Length >= 1)
                    double.TryParse(dims[0].Trim(), CultureInfo.InvariantCulture, out toolLength);
                if (dims.Length >= 2)
                    double.TryParse(dims[1].Trim(), CultureInfo.InvariantCulture, out toolRadius);
            }

            // Tool type (field index 18) — "STD tool", etc.
            var toolType = "";
            if (parts.Length > 18)
                toolType = parts[18].Trim();

            // Tool life display (field index 15) — "********" means unlimited
            var lifeDisplay = "";
            if (parts.Length > 15)
                lifeDisplay = parts[15].Trim();

            var entry = new ToolOffsetEntry
            {
                Number = toolNo,
                GeometryLength = toolLength,
                GeometryRadius = toolRadius,
                IsActive = isActive
            };

            toolInfo.Offsets.Add(entry);

            // Store tool name and type in AdditionalData if non-empty
            if (!string.IsNullOrWhiteSpace(toolName))
                data.AdditionalData[$"Tool.{toolNo}.Name"] = toolName;
            if (!string.IsNullOrWhiteSpace(toolType))
                data.AdditionalData[$"Tool.{toolNo}.Type"] = toolType;
            if (lifeDisplay != "********" && !string.IsNullOrEmpty(lifeDisplay))
                data.AdditionalData[$"Tool.{toolNo}.Life"] = lifeDisplay;
        }

        toolInfo.OffsetCount = toolInfo.Offsets.Count;
        if (activeToolNo.HasValue)
            toolInfo.CurrentToolNumber = activeToolNo.Value;

        data.ToolInfo = toolInfo;
    }

    /// <summary>
    /// Parse ALARM_CURALMLIST response.
    /// Empty response = no active alarms.
    /// Non-empty = CSV lines with alarm data.
    /// Format: alarmNo, message, program, ?, statusCode
    /// Example: 0501, *Standby mode,0926,001186,3
    ///
    /// Brother "informational" alarms (not real alarms):
    ///   0501 = Standby mode (machine is idle)
    ///   These are filtered out — they indicate machine state, not faults.
    ///
    /// Brother "maintenance" alarms (greasing, filter change, etc.):
    ///   These appear in ALARM_CURALMLIST but are maintenance reminders,
    ///   not real faults. They are published as MaintenanceWarning tags
    ///   for the maintenance department, but don't trigger ALARM state.
    /// </summary>
    private void ParseAlarms(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        // Brother informational alarm numbers that are NOT real alarms
        var informationalAlarms = new HashSet<int> { 501 };

        // Keywords that indicate maintenance reminders, not real faults
        var maintenanceKeywords = new[] { "GREASING", "GREASE", "MAINTENANCE", "FILTER", "LUBRICATION", "LUBRICANT" };

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var maintenanceWarnings = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrEmpty(line)) continue;

            // Parse: "   0501, *Standby mode,0926,001186,3"
            var parts = line.Split(',');
            var alarmNo = 0;
            var message = line;

            if (parts.Length >= 2)
            {
                int.TryParse(parts[0].Trim(), out alarmNo);
                message = parts[1].Trim();
            }

            // Check if this is an informational/status message, not a real alarm
            if (informationalAlarms.Contains(alarmNo))
            {
                // Standby mode (0501) → update state to STOP/idle, not ALARM
                if (message.Contains("Standby", StringComparison.OrdinalIgnoreCase))
                {
                    data.CncState_path1_CNC = "STOP";
                    data.RunningStatus = "Idle";
                }
                data.CncWarning_path1_CNC = message;
                continue; // Don't add to active alarms
            }

            // Check if this is a maintenance reminder, not a real fault
            bool isMaintenanceAlarm = maintenanceKeywords.Any(kw =>
                message.Contains(kw, StringComparison.OrdinalIgnoreCase));

            if (isMaintenanceAlarm)
            {
                // Store as maintenance warning for the maintenance department
                maintenanceWarnings.Add(message);
                data.CncWarning_path1_CNC = $"Maintenance ({message})";
                continue; // Don't trigger ALARM state
            }

            data.ActiveAlarms.Add(new AlarmData
            {
                AlarmNumber = alarmNo,
                AlarmType = "Brother",
                Message = message
            });
        }

        // Publish maintenance warnings as dedicated tags for the maintenance department
        if (maintenanceWarnings.Count > 0)
        {
            data.AdditionalData["MaintenanceWarning"] = string.Join("; ", maintenanceWarnings);
            data.AdditionalData["MaintenanceWarningCount"] = maintenanceWarnings.Count;
        }

        if (data.ActiveAlarms.Count > 0)
        {
            data.CncState_path1_CNC = "ALARM";
            data.CncWarning_path1_CNC = data.ActiveAlarms[0].Message;
        }
    }

    /// <summary>
    /// Parse MNTP_MAINTNOTICE response.
    /// Format per line: condition, description, status, current, limit, notified, state, color
    /// Example: Z-axis travel distance),GREASING XYZ AXIS   ,Invalid,        0,   100000,Notified,Normal,#00ff00
    /// Lines starting with "None" = no maintenance notice.
    ///
    /// Publishes detailed maintenance data as MQTT tags for the maintenance department:
    ///   Maintenance.{n}.Description  — What needs to be done (e.g., "GREASING XYZ AXIS")
    ///   Maintenance.{n}.Condition    — Trigger condition (e.g., "Z-axis travel distance")
    ///   Maintenance.{n}.Status       — Valid/Invalid
    ///   Maintenance.{n}.Current      — Current counter value
    ///   Maintenance.{n}.Limit        — Limit that triggers the notice
    ///   Maintenance.{n}.State        — Normal/Warning/Overdue
    ///   Maintenance.{n}.DuePercent   — How close to the limit (0-100+%)
    ///   MaintenanceNoticeCount       — Total number of active notices
    ///   MaintenanceDueSummary        — Human-readable summary for dashboards
    /// </summary>
    private void ParseMaintenanceNotices(string? response, CncMachineData data)
    {
        if (string.IsNullOrWhiteSpace(response)) return;

        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int noticeIndex = 0;
        var dueSummaries = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (string.IsNullOrEmpty(line) || line.StartsWith("None", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 7) continue;

            var condition = parts[0].Trim();
            var description = parts[1].Trim();
            var status = parts[2].Trim();
            var currentVal = parts[3].Trim();
            var limitVal = parts[4].Trim();
            var notified = parts[5].Trim();
            var state = parts[6].Trim();

            noticeIndex++;
            data.AdditionalData[$"Maintenance.{noticeIndex}.Description"] = description;
            data.AdditionalData[$"Maintenance.{noticeIndex}.Condition"] = condition;
            data.AdditionalData[$"Maintenance.{noticeIndex}.Status"] = status;
            data.AdditionalData[$"Maintenance.{noticeIndex}.Current"] = currentVal;
            data.AdditionalData[$"Maintenance.{noticeIndex}.Limit"] = limitVal;
            data.AdditionalData[$"Maintenance.{noticeIndex}.State"] = state;

            // Calculate due percentage for maintenance department dashboards
            if (long.TryParse(currentVal.Trim(), out var current) &&
                long.TryParse(limitVal.Trim(), out var limit) && limit > 0)
            {
                var duePercent = (int)Math.Round(current * 100.0 / limit);
                data.AdditionalData[$"Maintenance.{noticeIndex}.DuePercent"] = duePercent;
            }

            // Build summary: "GREASING XYZ AXIS (Notified)" or "GREASING ALL GRP (Overdue)"
            var summary = description;
            if (!string.IsNullOrEmpty(notified) && notified != "None")
                summary += $" ({notified})";
            dueSummaries.Add(summary);

            // If maintenance notice also sets a warning (and no real alarm is active)
            if (notified.Equals("Notified", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(data.CncWarning_path1_CNC))
            {
                data.CncWarning_path1_CNC = $"Maintenance ({description})";
            }
        }

        if (noticeIndex > 0)
        {
            data.AdditionalData["MaintenanceNoticeCount"] = noticeIndex;
            data.AdditionalData["MaintenanceDueSummary"] = string.Join("; ", dueSummaries);
        }
    }

    // ========================================================================
    // Time Parsing Helpers
    // ========================================================================

    /// <summary>
    /// Parse Brother time format to total seconds.
    /// Brother uses "HHHH:MM:SS.D" format (hours can exceed 24).
    /// Examples:
    ///   "0000:04:56.2"  → 296.2 seconds
    ///   "3462:45:28"    → 12465928 seconds
    /// </summary>
    private static double? ParseBrotherTimeToSeconds(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return null;

        var colonParts = timeStr.Split(':');
        if (colonParts.Length < 2) return null;

        try
        {
            double hours = 0, minutes = 0, seconds = 0;

            if (colonParts.Length >= 1)
                double.TryParse(colonParts[0], CultureInfo.InvariantCulture, out hours);
            if (colonParts.Length >= 2)
                double.TryParse(colonParts[1], CultureInfo.InvariantCulture, out minutes);
            if (colonParts.Length >= 3)
                double.TryParse(colonParts[2], CultureInfo.InvariantCulture, out seconds);

            return hours * 3600 + minutes * 60 + seconds;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
