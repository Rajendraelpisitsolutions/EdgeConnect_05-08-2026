// ============================================================================
// File: DataSources/MTConnectDataSource.cs
// Purpose: Collects CNC machine data via the MTConnect open standard protocol.
//          MTConnect is a free, royalty-free standard supported by Brother,
//          Mazak, Okuma, DMG Mori, Haas, and many other CNC manufacturers.
//
// MTConnect PROTOCOL:
//   - HTTP/XML based (no proprietary DLLs or licenses needed)
//   - /probe    → Device description (what data items are available)
//   - /current  → Current values of all data items
//   - /sample   → Historical/streaming data with sequence numbers
//
// SUPPORTED CNC BRANDS:
//   Brother (Speedio), Mazak, Okuma, DMG Mori, Haas, Doosan, Makino,
//   and any CNC with an MTConnect adapter/agent.
//
// DATA MAPPING (MTConnect → CncMachineData):
//   Execution     → CncState (ACTIVE/INTERRUPTED/STOPPED/READY)
//   ControllerMode → Mode (AUTOMATIC/MANUAL/MANUAL_DATA_INPUT)
//   Program       → MainProgram
//   PartCount     → PartsCount
//   SpindleSpeed  → Spindle.Speed
//   SpindleLoad   → Spindle.Load
//   PathFeedrate  → FeedRate
//   Position      → Axes[].AbsolutePosition / MachinePosition
//   Condition     → ActiveAlarms
//   CycleTime     → CycleTimeSeconds
// ============================================================================

using System.Diagnostics;
using System.Net.Http;
using System.Xml.Linq;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Collects CNC machine data via the MTConnect open standard protocol.
/// Works with any CNC that has an MTConnect agent/adapter (Brother, Mazak, Okuma, etc.).
/// </summary>
public sealed class MTConnectDataSource : IMachineDataSource
{
    private readonly MachineConfig _config;
    private readonly MTConnectSettings _mtcSettings;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    // Cached device info from /probe (read once)
    private string? _deviceName;
    private string? _deviceUuid;
    private bool _probeCompleted;

    // Track consecutive failures for status
    private int _consecutiveFailures;

    public string MachineId => _config.MachineId;
    public string MachineName => _config.MachineName;
    public DataSourceType SourceType => DataSourceType.MTConnect;

    public MTConnectDataSource(MachineConfig config, ILogger logger)
    {
        _config = config;
        _mtcSettings = config.MTConnect
            ?? throw new ArgumentNullException(nameof(config), "MTConnect settings are required");
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mtcSettings.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_mtcSettings.TimeoutSeconds)
        };

        // MTConnect agents typically don't require authentication
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/xml");
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
            // First call: probe to discover device structure
            if (!_probeCompleted)
            {
                await ProbeDeviceAsync(ct);
            }

            // Fetch current values from the MTConnect agent
            var currentXml = await FetchCurrentAsync(ct);
            if (currentXml == null)
            {
                data.Status = MachineStatus.Offline;
                data.Disconnect_CNC = true;
                sw.Stop();
                data.CollectionDurationMs = sw.ElapsedMilliseconds;
                return data;
            }

            // Parse all data items from the XML response
            ParseMTConnectCurrent(currentXml, data);

            _consecutiveFailures = 0;
            data.Disconnect_CNC = false;

            // Determine status
            if (data.ActiveAlarms.Count > 0)
                data.Status = MachineStatus.Alarm;
            else
                data.Status = MachineStatus.Online;

            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;

            _logger.LogDebug(
                "Machine {MachineId}: MTConnect data collected in {Duration}ms — " +
                "State={State}, Program={Program}, Spindle={Speed}rpm, Feed={Feed}",
                _config.MachineId, sw.ElapsedMilliseconds,
                data.CncState_path1_CNC, data.MainProgram,
                data.Spindle?.Speed, data.FeedRate);

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
                "Machine {MachineId}: MTConnect collection failed (consecutive: {Failures})",
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
            var response = await _httpClient.GetAsync(BuildUrl("probe"), ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ========================================================================
    // MTConnect HTTP Requests
    // ========================================================================

    /// <summary>
    /// Probe the MTConnect agent to discover device structure and capabilities.
    /// Called once on first data collection.
    /// </summary>
    private async Task ProbeDeviceAsync(CancellationToken ct)
    {
        try
        {
            var url = BuildUrl("probe");
            _logger.LogInformation("Machine {MachineId}: Probing MTConnect agent at {Url}", _config.MachineId, url);

            var response = await _httpClient.GetStringAsync(url, ct);
            var doc = XDocument.Parse(response);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            // Find the device element
            var device = doc.Descendants(ns + "Device").FirstOrDefault();
            if (device != null)
            {
                _deviceName = device.Attribute("name")?.Value;
                _deviceUuid = device.Attribute("uuid")?.Value;
                var manufacturer = device.Descendants(ns + "Description").FirstOrDefault()
                    ?.Attribute("manufacturer")?.Value;

                _logger.LogInformation(
                    "Machine {MachineId}: MTConnect device discovered — Name={DeviceName}, " +
                    "UUID={UUID}, Manufacturer={Manufacturer}",
                    _config.MachineId, _deviceName, _deviceUuid, manufacturer);

                // Log available data item categories
                var dataItems = doc.Descendants(ns + "DataItem").ToList();
                var categories = dataItems
                    .GroupBy(d => d.Attribute("category")?.Value ?? "unknown")
                    .Select(g => $"{g.Key}:{g.Count()}")
                    .ToList();
                _logger.LogInformation(
                    "Machine {MachineId}: MTConnect data items — {Categories} (total: {Total})",
                    _config.MachineId, string.Join(", ", categories), dataItems.Count);
            }

            _probeCompleted = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Machine {MachineId}: MTConnect probe failed — will retry on next poll",
                _config.MachineId);
            // Don't throw — we can still try /current without probe info
        }
    }

    /// <summary>
    /// Fetch current values from the MTConnect agent.
    /// Returns the parsed XML document, or null if the request failed.
    /// </summary>
    private async Task<XDocument?> FetchCurrentAsync(CancellationToken ct)
    {
        try
        {
            var url = BuildUrl("current");
            var response = await _httpClient.GetStringAsync(url, ct);
            return XDocument.Parse(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                "Machine {MachineId}: MTConnect /current request failed — {Message}",
                _config.MachineId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Build the full URL for an MTConnect request, including device filter if configured.
    /// </summary>
    private string BuildUrl(string endpoint)
    {
        if (!string.IsNullOrEmpty(_mtcSettings.DeviceName))
            return $"{_mtcSettings.DeviceName}/{endpoint}";
        return endpoint;
    }

    // ========================================================================
    // MTConnect XML Parsing → CncMachineData Mapping
    // ========================================================================

    /// <summary>
    /// Parse MTConnect /current XML response and populate CncMachineData.
    ///
    /// MTConnect XML structure:
    ///   <MTConnectStreams>
    ///     <Streams>
    ///       <DeviceStream name="..." uuid="...">
    ///         <ComponentStream component="Controller" ...>
    ///           <Events>
    ///             <Execution ...>ACTIVE</Execution>
    ///             <ControllerMode ...>AUTOMATIC</ControllerMode>
    ///             <Program ...>O1234</Program>
    ///           </Events>
    ///           <Samples>
    ///             <PathFeedrate ...>500.0</PathFeedrate>
    ///           </Samples>
    ///           <Condition>
    ///             <Fault ...>ALARM TEXT</Fault>
    ///           </Condition>
    ///         </ComponentStream>
    ///       </DeviceStream>
    ///     </Streams>
    ///   </MTConnectStreams>
    /// </summary>
    private void ParseMTConnectCurrent(XDocument doc, CncMachineData data)
    {
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Collect all data items into a flat lookup for easy access
        var dataItems = new Dictionary<string, (string Value, string? SubType, string? Name)>(
            StringComparer.OrdinalIgnoreCase);

        // MTConnect uses various element names for data items. Collect them all.
        var streams = doc.Descendants(ns + "DeviceStream").ToList();
        if (streams.Count == 0)
        {
            _logger.LogWarning("Machine {MachineId}: No DeviceStream found in MTConnect response",
                _config.MachineId);
            return;
        }

        // Process all elements within Events, Samples, and Condition sections
        foreach (var stream in streams)
        {
            CollectDataItems(stream, ns, dataItems);
        }

        // Log discovered data items on first successful read
        if (_consecutiveFailures == 0 && dataItems.Count > 0)
        {
            _logger.LogDebug(
                "Machine {MachineId}: MTConnect data items received: {Items}",
                _config.MachineId,
                string.Join(", ", dataItems.Keys.Take(30)));
        }

        // ── Map MTConnect data items to CncMachineData ──

        // Execution state → CncState
        if (TryGetValue(dataItems, "Execution", out var execution))
        {
            data.CncState_path1_CNC = MapExecutionToCncState(execution);
            data.RunningStatus = execution;
        }

        // Controller mode → Mode
        if (TryGetValue(dataItems, "ControllerMode", out var mode))
        {
            data.Mode_path1_CNC = MapControllerMode(mode);
            data.AutoMode = mode;
        }

        // Emergency stop
        if (TryGetValue(dataItems, "EmergencyStop", out var estop))
        {
            data.EmergencyStop = string.Equals(estop, "TRIGGERED", StringComparison.OrdinalIgnoreCase);
            if (data.EmergencyStop == true)
                data.CncState_path1_CNC = "EMERGENCY";
        }

        // Program name
        if (TryGetValue(dataItems, "Program", out var program))
        {
            data.MainProgram = program;
            data.MainProgram_path1_CNC = program;
        }

        // Sub-program / active program
        if (TryGetValue(dataItems, "SubProgram", out var subProgram))
        {
            data.ActProgram_path1_CNC = subProgram;
        }
        else
        {
            data.ActProgram_path1_CNC = data.MainProgram_path1_CNC;
        }

        // Program comment
        if (TryGetValue(dataItems, "ProgramComment", out var comment))
        {
            data.MainComment_path1_CNC = comment;
        }

        // Parts count
        if (TryGetValue(dataItems, "PartCount", out var partCount))
        {
            if (long.TryParse(partCount, out var pc))
            {
                data.PartsCount = pc;
                data.PartsNum_path1_CNC = (int)pc;
            }
        }

        // Feed rate
        if (TryGetValue(dataItems, "PathFeedrate", out var feedRate))
        {
            if (double.TryParse(feedRate, System.Globalization.CultureInfo.InvariantCulture, out var fr))
            {
                data.FeedRate = fr;
            }
        }

        // Cycle time
        if (TryGetValue(dataItems, "CycleTime", out var cycleTime) ||
            TryGetValue(dataItems, "ProcessTimer", out cycleTime))
        {
            if (double.TryParse(cycleTime, System.Globalization.CultureInfo.InvariantCulture, out var ct2))
            {
                data.CycleTimeSeconds = ct2;
            }
        }

        // ── Spindle data ──
        var spindle = new SpindleData();
        bool hasSpindleData = false;

        if (TryGetValue(dataItems, "SpindleSpeed", out var spindleSpeed) ||
            TryGetValue(dataItems, "RotaryVelocity", out spindleSpeed))
        {
            if (double.TryParse(spindleSpeed, System.Globalization.CultureInfo.InvariantCulture, out var ss))
            {
                spindle.Speed = ss;
                hasSpindleData = true;
            }
        }

        if (TryGetValue(dataItems, "SpindleLoad", out var spindleLoad) ||
            TryGetValue(dataItems, "Load", out spindleLoad))
        {
            if (double.TryParse(spindleLoad, System.Globalization.CultureInfo.InvariantCulture, out var sl))
            {
                spindle.Load = sl;
                hasSpindleData = true;
            }
        }

        if (TryGetValue(dataItems, "RotaryMode", out var rotaryMode))
        {
            spindle.Direction = rotaryMode;
            hasSpindleData = true;
        }

        if (hasSpindleData)
            data.Spindle = spindle;

        // ── Axis positions ──
        ParseAxisPositions(dataItems, data);

        // ── Alarms / Conditions ──
        ParseConditions(doc, ns, data);

        // ── Tool data (if available) ──
        if (TryGetValue(dataItems, "ToolId", out var toolId) ||
            TryGetValue(dataItems, "ToolNumber", out toolId))
        {
            if (int.TryParse(toolId, out var tn))
            {
                data.ToolInfo = new ToolInfoData { CurrentToolNumber = tn };
            }
        }

        // ── Signals (if available from PMC/machine-specific items) ──
        // These may not be available on all MTConnect agents
        if (TryGetValue(dataItems, "BlockCount", out _))
        {
            data.SigSBK_path1_CNC = false; // Default; override if specific signal exists
        }
    }

    /// <summary>
    /// Collect all data item elements from a DeviceStream into a flat dictionary.
    /// MTConnect data items can be Events, Samples, or Conditions with various element names.
    /// </summary>
    private void CollectDataItems(XElement stream, XNamespace ns,
        Dictionary<string, (string Value, string? SubType, string? Name)> items)
    {
        // Get all descendant elements within Events and Samples sections
        var sections = stream.Descendants(ns + "Events")
            .Concat(stream.Descendants(ns + "Samples"));

        foreach (var section in sections)
        {
            foreach (var element in section.Elements())
            {
                var localName = element.Name.LocalName;
                var value = element.Value?.Trim() ?? string.Empty;
                var subType = element.Attribute("subType")?.Value;
                var name = element.Attribute("name")?.Value;

                // Skip UNAVAILABLE values — means the data item exists but has no current value
                if (string.Equals(value, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use name attribute if available (more specific), otherwise use element type
                var key = !string.IsNullOrEmpty(name) ? name : localName;

                // Store by element type (e.g., "Execution", "SpindleSpeed")
                if (!items.ContainsKey(localName))
                    items[localName] = (value, subType, name);

                // Also store by name attribute if different from element type
                if (!string.IsNullOrEmpty(name) && !items.ContainsKey(name))
                    items[name] = (value, subType, name);

                // Store with subType qualifier (e.g., "Position_ACTUAL", "Position_MACHINE")
                if (!string.IsNullOrEmpty(subType))
                {
                    var qualifiedKey = $"{localName}_{subType}";
                    if (!items.ContainsKey(qualifiedKey))
                        items[qualifiedKey] = (value, subType, name);
                }
            }
        }
    }

    /// <summary>
    /// Parse axis position data items from MTConnect.
    /// MTConnect uses Position with subType ACTUAL, MACHINE, COMMANDED.
    /// Axis is identified by the name attribute (e.g., "Xact", "Yact") or
    /// the component name in the ComponentStream.
    /// </summary>
    private void ParseAxisPositions(
        Dictionary<string, (string Value, string? SubType, string? Name)> dataItems,
        CncMachineData data)
    {
        // Common axis name patterns in MTConnect
        string[] axisNames = ["X", "Y", "Z", "A", "B", "C"];

        foreach (var axisName in axisNames)
        {
            var axis = new AxisData { AxisName = axisName };
            bool hasData = false;

            // Try various naming conventions used by different MTConnect agents
            // Brother: Xact, Xload, etc.
            // Standard: Position with name="X" and subType="ACTUAL"
            foreach (var (key, item) in dataItems)
            {
                var keyUpper = key.ToUpperInvariant();

                // Pattern: "Xact", "Yact", "Zact" (absolute position)
                if (keyUpper == $"{axisName}ACT" || keyUpper == $"{axisName}ABS" ||
                    keyUpper == $"{axisName}POS")
                {
                    if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out var pos))
                    {
                        axis.AbsolutePosition = pos;
                        hasData = true;
                    }
                }

                // Pattern: "Xmachine", machine position
                if (keyUpper == $"{axisName}MACHINE" || keyUpper == $"{axisName}MACH")
                {
                    if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out var pos))
                    {
                        axis.MachinePosition = pos;
                        hasData = true;
                    }
                }
            }

            // Also check for standard MTConnect Position elements
            if (!hasData)
            {
                // Look for Position_ACTUAL where name contains the axis letter
                foreach (var (key, item) in dataItems)
                {
                    if (item.Name?.StartsWith(axisName, StringComparison.OrdinalIgnoreCase) == true ||
                        item.Name?.EndsWith(axisName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var localName = key.Split('_')[0];
                        if (string.Equals(localName, "Position", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(item.Value, System.Globalization.CultureInfo.InvariantCulture, out var pos))
                            {
                                if (item.SubType?.Equals("ACTUAL", StringComparison.OrdinalIgnoreCase) == true)
                                    axis.AbsolutePosition = pos;
                                else if (item.SubType?.Equals("MACHINE", StringComparison.OrdinalIgnoreCase) == true)
                                    axis.MachinePosition = pos;
                                hasData = true;
                            }
                        }
                    }
                }
            }

            if (hasData)
                data.Axes[axisName] = axis;
        }
    }

    /// <summary>
    /// Parse Condition elements for alarms/faults.
    /// MTConnect conditions: Normal, Warning, Fault, Unavailable.
    /// Fault = alarm, Warning = advisory.
    /// </summary>
    private void ParseConditions(XDocument doc, XNamespace ns, CncMachineData data)
    {
        var conditions = doc.Descendants(ns + "Condition").ToList();

        foreach (var condition in conditions)
        {
            foreach (var element in condition.Elements())
            {
                var localName = element.Name.LocalName;

                if (string.Equals(localName, "Fault", StringComparison.OrdinalIgnoreCase))
                {
                    var nativeCode = element.Attribute("nativeCode")?.Value ?? "0";
                    var message = element.Value?.Trim() ?? string.Empty;
                    var type = element.Attribute("type")?.Value ?? "SYSTEM";

                    int.TryParse(nativeCode, out var alarmNo);

                    data.ActiveAlarms.Add(new AlarmData
                    {
                        AlarmNumber = alarmNo,
                        AlarmType = type,
                        Message = message
                    });
                }
                else if (string.Equals(localName, "Warning", StringComparison.OrdinalIgnoreCase))
                {
                    var message = element.Value?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(message) && string.IsNullOrEmpty(data.CncWarning_path1_CNC))
                    {
                        data.CncWarning_path1_CNC = message;
                    }
                }
            }
        }
    }

    // ========================================================================
    // MTConnect → EREMOS/MT-LINKi Value Mapping
    // ========================================================================

    /// <summary>
    /// Map MTConnect Execution state to EREMOS CncState values.
    /// MTConnect: ACTIVE, INTERRUPTED, STOPPED, READY, FEED_HOLD, OPTIONAL_STOP, PROGRAM_STOPPED
    /// EREMOS:    OPERATE, SUSPEND, STOP, ALARM, EMERGENCY
    /// </summary>
    private static string MapExecutionToCncState(string execution)
    {
        return execution.ToUpperInvariant() switch
        {
            "ACTIVE" => "OPERATE",
            "INTERRUPTED" => "SUSPEND",
            "FEED_HOLD" => "SUSPEND",
            "OPTIONAL_STOP" => "SUSPEND",
            "PROGRAM_STOPPED" => "STOP",
            "STOPPED" => "STOP",
            "READY" => "STOP",
            _ => execution
        };
    }

    /// <summary>
    /// Map MTConnect ControllerMode to EREMOS Mode values.
    /// MTConnect: AUTOMATIC, MANUAL, MANUAL_DATA_INPUT, SEMI_AUTOMATIC, EDIT
    /// EREMOS:    MEM, JOG, MDI, EDIT, HANDLE
    /// </summary>
    private static string MapControllerMode(string mode)
    {
        return mode.ToUpperInvariant() switch
        {
            "AUTOMATIC" => "MEM",
            "MANUAL" => "JOG",
            "MANUAL_DATA_INPUT" => "MDI",
            "SEMI_AUTOMATIC" => "HANDLE",
            "EDIT" => "EDIT",
            _ => mode
        };
    }

    /// <summary>
    /// Try to get a value from the data items dictionary.
    /// Checks both exact match and common variations.
    /// </summary>
    private static bool TryGetValue(
        Dictionary<string, (string Value, string? SubType, string? Name)> items,
        string key, out string value)
    {
        if (items.TryGetValue(key, out var item))
        {
            value = item.Value;
            return !string.IsNullOrEmpty(value);
        }
        value = string.Empty;
        return false;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
