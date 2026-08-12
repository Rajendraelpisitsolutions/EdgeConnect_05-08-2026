// ============================================================================
// File: DataSources/MtLinkiRestDataSource.cs
// Purpose: Data source that reads CNC machine data from Fanuc MT-LINKi's
//          REST API. MT-LINKi's Collector PC acquires data from CNC
//          controllers via FOCAS and exposes it through a REST API on port 3000.
//
// MT-LINKI REST API STRUCTURE:
//   Base URL:  http://<mtlinki-server>:3000
//   API root:  /api/v1
//
//   Endpoints:
//     GET /api/v1/equipment/CNC/monitorings
//         → Returns current monitoring data for all CNC equipment
//
//     GET /api/v1/equipment/CNC/monitorings/{type}/logs
//         → Returns historical log entries for a specific monitoring type
//         → Types observed: MANUAL, AUTO, ALARM, PRODUCTION, etc.
//
//   Response format is JSON. Exact schema depends on MT-LINKi version and
//   configuration. This data source handles multiple known JSON structures.
//
// ADVANTAGES OVER DIRECT FOCAS2:
//   - No additional load on CNC controllers
//   - MT-LINKi Collector handles all FOCAS communication
//   - Supports non-Fanuc machines (via OPC-UA/MTConnect in MT-LINKi)
//   - Data already structured and timestamped
//   - Standard HTTP — no native DLLs required
//   - Can read historical logs via /logs endpoints
// ============================================================================

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Data source reading CNC data from Fanuc MT-LINKi's REST API.
/// Recommended for factories already running MT-LINKi infrastructure.
/// </summary>
public sealed class MtLinkiRestDataSource : IMachineDataSource
{
    private readonly MachineConfig _config;
    private readonly MtLinkiSettings _mtLinkiConfig;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string MachineId => _config.MachineId;
    public string MachineName => _config.MachineName;
    public DataSourceType SourceType => DataSourceType.MtLinki;

    public MtLinkiRestDataSource(MachineConfig config, ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _mtLinkiConfig = config.MtLinki ?? throw new ArgumentException(
            $"MtLinki settings required for machine {config.MachineId} with DataSourceType=MtLinki");
        _logger = logger;

        _httpClient = CreateHttpClient();

        _logger.LogInformation(
            "Machine {MachineId}: MT-LINKi REST data source initialized — " +
            "BaseUrl: {BaseUrl}, ApiVersion: {ApiVersion}, Equipment: {EquipmentType}, Identifier: {Identifier}",
            _config.MachineId,
            _mtLinkiConfig.BaseUrl,
            _mtLinkiConfig.ApiVersion,
            _mtLinkiConfig.EquipmentType,
            _mtLinkiConfig.MachineIdentifier);
    }

    private HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(_mtLinkiConfig.TimeoutSeconds),
            EnableMultipleHttp2Connections = true
        };

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(_mtLinkiConfig.BaseUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(_mtLinkiConfig.TimeoutSeconds)
        };

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        // Add auth header if credentials are configured
        if (!string.IsNullOrEmpty(_mtLinkiConfig.Username))
        {
            var credentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes(
                    $"{_mtLinkiConfig.Username}:{_mtLinkiConfig.Password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        return client;
    }

    /// <summary>
    /// Build the API path prefix, e.g. "/api/v1/equipment/CNC"
    /// </summary>
    private string ApiBasePath =>
        $"/api/{_mtLinkiConfig.ApiVersion}/equipment/{_mtLinkiConfig.EquipmentType}";

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
            var logTasks = new List<Task>();
            // Primary: GET /api/v1/equipment/CNC/monitorings
            var monitoringData = await FetchMonitoringDataAsync(ct);
            
            
            if (monitoringData != null)
            {
                //ParseMonitoringData(monitoringData, data);
                var parameters = monitoringData.Value.Deserialize<SignalConfig[]>();
                if (parameters != null)
                {
                    foreach (var task in parameters)
                    {
                        logTasks.Add(FetchAndParseLogsAsync(task.Name, data, ct));
                    }
                }
                
            }

            // If specific monitoring log endpoints are configured, fetch those too
           

            if (_mtLinkiConfig.FetchAlarmLogs && HasDataPoint("Alarms/Active"))
            {
                logTasks.Add(FetchAndParseLogsAsync("ALARM", data, ct));
            }

            if (_mtLinkiConfig.FetchProductionLogs &&
                (HasDataPoint("CycleTime") || HasDataPoint("PartsCount")))
            {
                logTasks.Add(FetchAndParseLogsAsync("PRODUCTION", data, ct));
                //logTasks.Add(FetchAndParseLogsAsync("CncState_path1", data, ct));
            }

            if (logTasks.Count > 0)
            {
                await Task.WhenAll(logTasks);
            }

            data.Status = data.ActiveAlarms.Count > 0 ? MachineStatus.Alarm : MachineStatus.Online;
        }
        catch (HttpRequestException ex)
        {
            data.Status = MachineStatus.Offline;
            _logger.LogWarning(ex, "Machine {MachineId}: MT-LINKi API connection failed", _config.MachineId);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            data.Status = MachineStatus.Error;
            _logger.LogWarning("Machine {MachineId}: MT-LINKi API request timeout", _config.MachineId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            data.Status = MachineStatus.Error;
            _logger.LogWarning(ex, "Machine {MachineId}: MT-LINKi data collection error", _config.MachineId);
        }
        finally
        {
            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;
        }

        return data;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            // Simple GET to the monitorings endpoint to check connectivity
            var url = $"{ApiBasePath}/monitorings";
            var response = await _httpClient.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // MT-LINKi REST API CALLS
    // =========================================================================

    /// <summary>
    /// GET /api/v1/equipment/CNC/monitorings
    /// Returns the current monitoring snapshot for all CNC equipment.
    /// </summary>
    private async Task<JsonElement?> FetchMonitoringDataAsync(CancellationToken ct)
    {
        var url = $"{ApiBasePath}/monitorings";

        _logger.LogDebug("Machine {MachineId}: GET {Url}", _config.MachineId, url);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);

        _logger.LogDebug("Machine {MachineId}: Monitoring response received ({Length} chars)",
            _config.MachineId, json.Length);

        // If the response is an array, find the entry matching our machine
        //if (doc.RootElement.ValueKind == JsonValueKind.Array)
        //{
        //    return FindMachineInArray(doc.RootElement);
        //}

        // If it's an object with a "data" array, search inside
        //if (doc.RootElement.TryGetProperty("data", out var dataArray) &&
        //    dataArray.ValueKind == JsonValueKind.Array)
        //{
        //    return FindMachineInArray(dataArray);
        //}

        // Single object — assume it's for our machine
        return doc.RootElement;
    }

    /// <summary>
    /// GET /api/v1/equipment/CNC/monitorings/{type}/logs
    /// Returns historical log entries for a specific monitoring category.
    /// </summary>
    private async Task FetchAndParseLogsAsync(string logType, CncMachineData data, CancellationToken ct)
    {
        try
        {
            var url = $"{ApiBasePath}/monitorings/{logType}/logs";

            _logger.LogDebug("Machine {MachineId}: GET {Url}", _config.MachineId, url);

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Machine {MachineId}: {LogType} logs endpoint returned {Status}",
                    _config.MachineId, logType, response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Extract array from response
            JsonElement entries;
            if (root.ValueKind == JsonValueKind.Array)
            {
                entries = root;
                
            }
            else if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
            {
                entries = dataArr;
            }
            else if (root.TryGetProperty("logs", out var logsArr) && logsArr.ValueKind == JsonValueKind.Array)
            {
                entries = logsArr;
            }
            else
            {
                return;
            }

            switch (logType.ToUpperInvariant())
            {
                case "ALARM":
                    ParseAlarmLogs(entries, data);
                    break;
                //case "PRODUCTION":
                //    ParseProductionLogs(entries, data);
                //    break;
                case "CNCSTATE_PATH1":
                    ParseProductionLogs(entries, data, logType);

                    break;
                case "PARTSNUM_PATH1":
                    ParseProductionLogs(entries, data, logType);

                    break;
                //case "MainComment_path1":
                //    ParseProductionLogs(entries, data);
                //    break;
                default:
                    _logger.LogDebug("Machine {MachineId}: Unhandled log type {LogType}",
                        _config.MachineId, logType);
                    ParseProductionLogs(entries, data,logType);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to fetch {LogType} logs",
                _config.MachineId, logType);
        }
    }

    // =========================================================================
    // JSON RESPONSE PARSING
    // =========================================================================

    /// <summary>
    /// Parse the main monitoring data response into CncMachineData.
    /// Handles multiple MT-LINKi JSON formats gracefully.
    /// </summary>
    private void ParseMonitoringData(JsonElement? element, CncMachineData data)
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Null) return;
        var root = element.Value;
        data.PartsCount ??= TryGetLong(root, "PartsNum_path1")
            ?? TryGetLong(root, "partCount")
            ?? TryGetLong(root, "count")
            ?? TryGetLong(root, "totalParts");
        // ── Program info ──
        data.MainProgram = TryGetString(root, "mainProgram")
            ?? TryGetString(root, "program")
            ?? TryGetString(root, "programName")
            ?? TryGetString(root, "programNumber")
            ?? TryGetString(root, "o_number");

        data.RunningStatus = TryGetString(root, "runStatus")
            ?? TryGetString(root, "status")
            ?? TryGetString(root, "runState")
            ?? TryGetString(root, "mode")
            ?? TryGetString(root, "operationMode");

        // ── Feed rate ──
        data.FeedRate = TryGetDouble(root, "feedRate")
            ?? TryGetDouble(root, "actualFeedRate")
            ?? TryGetDouble(root, "feed");

        // ── Axis positions ──
        ExtractAxisData(root, data);

        // ── Spindle data ──
        ExtractSpindleData(root, data);

        // ── Alarms (if inline in monitoring response) ──
        ExtractInlineAlarms(root, data);

        // ── Production data (if inline) ──
        data.CycleTimeSeconds ??= TryGetDouble(root, "cycleTime")
            ?? TryGetDouble(root, "cycleTimeSeconds")
            ?? TryGetDouble(root, "lastCycleTime");

        
    }

    /// <summary>
    /// Extract axis position data from monitoring JSON.
    /// MT-LINKi may return axes in multiple formats:
    ///   Format 1: { "axes": { "X": { "absolute": 123.4 }, "Y": { ... } } }
    ///   Format 2: { "axes": [ { "name": "X", "absolute": 123.4 } ] }
    ///   Format 3: { "absX": 123.4, "absY": 456.7 }  (flat fields)
    ///   Format 4: { "position": { "x": 123.4, "y": 456.7 } }
    /// </summary>
    private void ExtractAxisData(JsonElement root, CncMachineData data)
    {
        // Try "axes" as object: { "X": { "absolute": 123.4 } }
        if (TryGetProperty(root, "axes", out var axes) && axes.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in axes.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    data.Axes[prop.Name] = new AxisData
                    {
                        AxisName = prop.Name,
                        AbsolutePosition = TryGetDouble(prop.Value, "absolute") ?? TryGetDouble(prop.Value, "abs"),
                        MachinePosition = TryGetDouble(prop.Value, "machine") ?? TryGetDouble(prop.Value, "mach"),
                        RelativePosition = TryGetDouble(prop.Value, "relative") ?? TryGetDouble(prop.Value, "rel"),
                        DistanceToGo = TryGetDouble(prop.Value, "distanceToGo") ?? TryGetDouble(prop.Value, "dtg")
                    };
                }
            }
            return;
        }

        // Try "axes" as array: [ { "name": "X", "absolute": 123.4 } ]
        if (TryGetProperty(root, "axes", out var axesArr) && axesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in axesArr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var name = TryGetString(item, "name") ?? TryGetString(item, "axis") ?? "?";
                data.Axes[name] = new AxisData
                {
                    AxisName = name,
                    AbsolutePosition = TryGetDouble(item, "absolute") ?? TryGetDouble(item, "abs"),
                    MachinePosition = TryGetDouble(item, "machine") ?? TryGetDouble(item, "mach"),
                    RelativePosition = TryGetDouble(item, "relative") ?? TryGetDouble(item, "rel"),
                    DistanceToGo = TryGetDouble(item, "distanceToGo") ?? TryGetDouble(item, "dtg")
                };
            }
            return;
        }

        // Try "position" sub-object or flat fields
        var posRoot = TryGetProperty(root, "position", out var pos) && pos.ValueKind == JsonValueKind.Object
            ? pos : root;

        foreach (var axisName in new[] { "X", "Y", "Z", "A", "B", "C" })
        {
            var lower = axisName.ToLowerInvariant();
            var absVal = TryGetDouble(posRoot, $"abs{axisName}")
                ?? TryGetDouble(posRoot, lower)
                ?? TryGetDouble(posRoot, $"absolute{axisName}");

            if (absVal.HasValue)
            {
                data.Axes[axisName] = new AxisData
                {
                    AxisName = axisName,
                    AbsolutePosition = absVal,
                    MachinePosition = TryGetDouble(posRoot, $"mach{axisName}")
                };
            }
        }
    }

    private void ExtractSpindleData(JsonElement root, CncMachineData data)
    {
        double? speed = null, load = null;
        string? direction = null;

        // Try nested "spindle" object
        if (TryGetProperty(root, "spindle", out var sp) && sp.ValueKind == JsonValueKind.Object)
        {
            speed = TryGetDouble(sp, "speed") ?? TryGetDouble(sp, "rpm");
            load = TryGetDouble(sp, "load");
            direction = TryGetString(sp, "direction") ?? TryGetString(sp, "dir");
        }
        else
        {
            // Flat fields
            speed = TryGetDouble(root, "spindleSpeed") ?? TryGetDouble(root, "rpm");
            load = TryGetDouble(root, "spindleLoad");
            direction = TryGetString(root, "spindleDirection");
        }

        if (speed.HasValue || load.HasValue)
        {
            data.Spindle = new SpindleData
            {
                Speed = speed.HasValue ? Math.Abs(speed.Value) : null,
                Load = load,
                Direction = direction ?? (speed > 0 ? "CW" : speed < 0 ? "CCW" : "Stopped")
            };
        }
    }

    private void ExtractInlineAlarms(JsonElement root, CncMachineData data)
    {
        // Check if alarms are included inline in monitoring response
        if (!TryGetProperty(root, "alarms", out var alarms) &&
            !TryGetProperty(root, "activeAlarms", out alarms))
            return;

        if (alarms.ValueKind == JsonValueKind.Array)
        {
            foreach (var alarm in alarms.EnumerateArray())
            {
                if (alarm.ValueKind != JsonValueKind.Object) continue;
                data.ActiveAlarms.Add(new AlarmData
                {
                    AlarmNumber = TryGetInt(alarm, "alarmNo") ?? TryGetInt(alarm, "number") ?? 0,
                    AlarmType = TryGetString(alarm, "type") ?? TryGetString(alarm, "alarmType") ?? "",
                    Message = TryGetString(alarm, "message") ?? TryGetString(alarm, "alarmMessage") ?? "",
                    Axis = TryGetString(alarm, "axis") ?? ""
                });
            }
        }
    }

    private void ParseAlarmLogs(JsonElement entries, CncMachineData data)
    {
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            // Only add active alarms (not cleared/historical)
            var active = TryGetBool(entry, "active") ?? !HasProperty(entry, "clearedAt");
            if (!active) continue;

            data.ActiveAlarms.Add(new AlarmData
            {
                AlarmNumber = TryGetInt(entry, "alarmNo") ?? TryGetInt(entry, "number") ?? 0,
                AlarmType = TryGetString(entry, "type") ?? TryGetString(entry, "alarmType") ?? "",
                Message = TryGetString(entry, "message") ?? TryGetString(entry, "alarmMessage") ?? "",
                Axis = TryGetString(entry, "axis") ?? ""
            });
        }
    }
    string NormalizeSignalName(string signalName)
    {
        var withoutPrefix = signalName.Contains('.')
        ? signalName.Substring(signalName.IndexOf('.') + 1)
        : signalName;

        // Split by '_' and remove last part if it contains "CNC"
        var parts = withoutPrefix.Split('_').ToList();
        //if (parts.Last().Contains("CNC", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        return string.Join("_", parts);
    }
    private void ParseProductionLogs(JsonElement entries, CncMachineData data,string name)
    {
        // Get the most recent production log entry

        
        JsonElement? latest = null;
        DateTimeOffset latestTime = DateTimeOffset.MinValue;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)continue;

            var timeStr = TryGetString(entry, "end");
            //?? TryGetString(entry, "createdAt")
            //?? TryGetString(entry, "time");

            if (timeStr == null)
                latest = entry;
            //if (timeStr != null && DateTimeOffset.TryParse(timeStr, out var ts) && ts > latestTime)
            //{
            //    latestTime = ts;
            //    latest = entry;
            //}
            //else if (latest == null)
            //{
            //    // If no timestamp, just use the first entry
            //    latest = entry;
            //}
        }

        if (latest == null) return;

        var e = latest.Value;
        data.CycleTimeSeconds ??= TryGetDouble(e, "cycleTime")
            ?? TryGetDouble(e, "cycleTimeSeconds");

        data.PartsCount ??= TryGetLong(e, "partsCount")
            ?? TryGetLong(e, "partCount")
            ?? TryGetLong(e, "count");



        switch (NormalizeSignalName(name))
        {
            case $"CncState_path1":
                data.CncState_path1_CNC ??= TryGetString(e, "value");
                break;
            case "PartsNum_path1":
                data.PartsNum_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "Disconnect":
                data.Disconnect_CNC ??= TryGetBool(e, "value");
                break;

            case "Mode_path1":
                data.Mode_path1_CNC ??= TryGetString(e, "value");
                break;

            case "MainProgram_path1":
                data.MainProgram_path1_CNC ??= TryGetString(e, "value");
                break;

            case "ActProgram_path1":
                data.ActProgram_path1_CNC ??= TryGetString(e, "value");
                break;

            case "MainComment_path1":
                data.MainComment_path1_CNC ??= TryGetString(e, "value");
                break;

            case "ActComment_path1":
                data.ActComment_path1_CNC ??= TryGetString(e, "value");
                break;

            case "SigCUT_path1":
                data.SigCUT_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SigSBK_path1":
                data.SigSBK_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SigDM00_path1":
                data.SigDM00_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SigDM01_path1":
                data.SigDM01_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SigMDRN_path1":
                data.SigMDRN_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "CncWarning_path1":
                data.CncWarning_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncFan1Status_path1":
                data.CncFan1Status_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncFan2Status_path1":
                data.CncFan2Status_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncFan3Status_path1":
                data.CncFan3Status_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncFan4Status_path1":
                data.CncFan4Status_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncBatLow_0_path1":
                data.CncBatLow_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ApcBatZero_0_path1":
                data.ApcBatZero_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ApcBatLow_0_path1":
                data.ApcBatLow_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "IndBatZero_0_path1":
                data.IndBatZero_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SpdBatZero_0_path1":
                data.SpdBatZero_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SSpdBatZero_0_path1":
                data.SSpdBatZero_0_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ServoTemp_0_path1":
                data.ServoTemp_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "PulseCoderTemp_0_path1":
                data.PulseCoderTemp_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "ServoLeakResistData_0_path1":
                data.ServoLeakResistData_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "InFan1SrvAmpStatus_0_path1":
                data.InFan1SrvAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SrvAmpStatus_0_path1":
                data.InFan2SrvAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SrvAmpStatus_0_path1":
                data.RadFan1SrvAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SrvAmpStatus_0_path1":
                data.RadFan2SrvAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan1SrvComPwStatus_0_path1":
                data.InFan1SrvComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SrvComPwStatus_0_path1":
                data.InFan2SrvComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SrvComPwStatus_0_path1":
                data.RadFan1SrvComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SrvComPwStatus_0_path1":
                data.RadFan2SrvComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "CncBatLow_1_path1":
                data.CncBatLow_1_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ApcBatZero_1_path1":
                data.ApcBatZero_1_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ApcBatLow_1_path1":
                data.ApcBatLow_1_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "IndBatZero_1_path1":
                data.IndBatZero_1_path1_CNC ??= TryGetBool(e, "value");
                break;
            case "SpdBatZero_1_path1":
                data.SpdBatZero_1_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "SSpdBatZero_1_path1":
                data.SSpdBatZero_1_path1_CNC ??= TryGetBool(e, "value");
                break;

            case "ServoTemp_1_path1":
                data.ServoTemp_1_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "PulseCoderTemp_1_path1":
                data.PulseCoderTemp_1_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "ServoLeakResistData_1_path1":
                data.ServoLeakResistData_1_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "InFan1SrvAmpStatus_1_path1":
                data.InFan1SrvAmpStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SrvAmpStatus_1_path1":
                data.InFan2SrvAmpStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SrvAmpStatus_1_path1":
                data.RadFan1SrvAmpStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SrvAmpStatus_1_path1":
                data.RadFan2SrvAmpStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan1SrvComPwStatus_1_path1":
                data.InFan1SrvComPwStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SrvComPwStatus_1_path1":
                data.InFan2SrvComPwStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SrvComPwStatus_1_path1":
                data.RadFan1SrvComPwStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SrvComPwStatus_1_path1":
                data.RadFan2SrvComPwStatus_1_path1_CNC ??= TryGetString(e, "value");
                break;

            case "SpindleTemp_0_path1":
                data.SpindleTemp_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "SpindleLeakResistData_0_path1":
                data.SpindleLeakResistData_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "SpindleTotalRev1_0_path1":
                data.SpindleTotalRev1_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "SpindleTotalRev2_0_path1":
                data.SpindleTotalRev2_0_path1_CNC ??= TryGetInt(e, "value");
                break;

            case "InFan1SpdlComPwStatus_0_path1":
                data.InFan1SpdlComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SpdlComPwStatus_0_path1":
                data.InFan2SpdlComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SpdlComPwStatus_0_path1":
                data.RadFan1SpdlComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SpdlComPwStatus_0_path1":
                data.RadFan2SpdlComPwStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan1SpindleAmpStatus_0_path1":
                data.InFan1SpindleAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "InFan2SpindleAmpStatus_0_path1":
                data.InFan2SpindleAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan1SpindleAmpStatus_0_path1":
                data.RadFan1SpindleAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

            case "RadFan2SpindleAmpStatus_0_path1":
                data.RadFan2SpindleAmpStatus_0_path1_CNC ??= TryGetString(e, "value");
                break;

        }



    }

    // =========================================================================
    // MACHINE IDENTIFIER MATCHING
    // =========================================================================

    /// <summary>
    /// When the API returns an array of machines, find the one matching our config.
    /// Checks multiple possible identifier fields.
    /// </summary>
    private JsonElement? FindMachineInArray(JsonElement array)
    {
        var identifier = !string.IsNullOrEmpty(_mtLinkiConfig.MachineIdentifier)
            ? _mtLinkiConfig.MachineIdentifier
            : _config.MachineId;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            // Check various identifier fields
            var id = TryGetString(item, "machineId")
                ?? TryGetString(item, "machine_id")
                ?? TryGetString(item, "machineName")
                ?? TryGetString(item, "machine")
                ?? TryGetString(item, "device")
                ?? TryGetString(item, "deviceId")
                ?? TryGetString(item, "name")
                //?? TryGetString(item, "CncState_path1")
                //?? TryGetString(item, "PartsNum_path1")
                ?? TryGetString(item, "id");

            if (string.Equals(id, identifier, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        _logger.LogDebug(
            "Machine {MachineId}: Could not find identifier '{Identifier}' in API array ({Count} entries). " +
            "Using first entry as fallback.",
            _config.MachineId, identifier, array.GetArrayLength());

        // Fallback: use first entry if only one machine
        if (array.GetArrayLength() == 1)
        {
            return array[0];
        }

        return null;
    }

    // =========================================================================
    // JSON SAFE ACCESSORS (System.Text.Json)
    // =========================================================================

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // Try exact name first, then common casing variants
        if (element.TryGetProperty(name, out value)) return true;
        if (element.TryGetProperty(name.ToLowerInvariant(), out value)) return true;

        // Try PascalCase
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        if (element.TryGetProperty(pascal, out value)) return true;

        value = default;
        return false;
    }

    private static bool HasProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out _);

    private static string? TryGetString(JsonElement element, string key)
    {
        if (!TryGetProperty(element, key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.String => val.GetString(),
            JsonValueKind.Number => val.GetRawText(),
            JsonValueKind.Null => null,
            _ => null
        };
    }

    private static double? TryGetDouble(JsonElement element, string key)
    {
        if (!TryGetProperty(element, key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.Number => val.GetDouble(),
            JsonValueKind.String when double.TryParse(val.GetString(), out var d) => d,
            _ => null
        };
    }

    private static int? TryGetInt(JsonElement element, string key)
    {
        if (!TryGetProperty(element, key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.Number => val.TryGetInt32(out var i) ? i : null,
            JsonValueKind.String when int.TryParse(val.GetString(), out var i) => i,
            _ => null
        };
    }

    private static long? TryGetLong(JsonElement element, string key)
    {
        if (!TryGetProperty(element, key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.Number => val.TryGetInt64(out var l) ? l : null,
            JsonValueKind.String when long.TryParse(val.GetString(), out var l) => l,
            _ => null
        };
    }

    private static bool? TryGetBool(JsonElement element, string key)
    {
        if (!TryGetProperty(element, key, out var val)) return null;
        return val.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(val.GetString(), out var b) ? b : null,
            _ => null
        };
    }

    private bool HasDataPoint(string name) => _config.DataPoints.Contains(name);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
