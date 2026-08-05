// ============================================================================
// File: Models/CncMachineData.cs
// Purpose: Domain model representing a single data collection snapshot from
//          a Fanuc CNC machine. This is the INTERNAL model passed through
//          the processing pipeline:
//
//          FanucApiClient → CncMachineData → (serialized to JSON) → MqttPayload
//
// DATA FLOW:
//   1. FanucApiClient.CollectDataAsync() populates this model from FOCAS API responses
//   2. MachinePollerService.ConvertToMqttPayload() serializes it to JSON bytes
//   3. The JSON bytes are wrapped in an MqttPayload and written to the channel
//   4. MqttPublisherService reads from the channel and publishes to the broker
//
// WIRE FORMAT:
//   The JSON representation of this model is what MQTT subscribers receive.
//   See README.md "Sample Payload" for the exact JSON structure.
//
// EXTENSIBILITY:
//   The AdditionalData dictionary allows collecting custom data points
//   without modifying this model. It's a catch-all for machine-specific
//   or future data points.
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Models;

/// <summary>
/// Represents a single data collection snapshot from a Fanuc CNC machine.
/// 
/// This is the internal domain model passed through the processing pipeline.
/// Each poll cycle produces one instance of this class per machine.
/// 
/// THREAD SAFETY: Instances are created by a single poller thread and then
/// serialized — no concurrent mutation, so no locks needed.
/// </summary>
public sealed class CncMachineData
{
    /// <summary>
    /// Unique machine identifier matching the config's MachineId.
    /// Used as the primary key in MQTT topic: {prefix}/{MachineId}/data
    /// The 'required' keyword ensures this is always set during construction.
    /// </summary>
    public required string MachineId { get; init; }

    /// <summary>
    /// Human-readable machine name from config — included in every payload
    /// so downstream consumers can display names without a lookup table.
    /// </summary>
    public required string MachineName { get; init; }

    /// <summary>
    /// UTC timestamp when this data was collected from the machine.
    /// 
    /// IMPORTANT: This is the COLLECTION time (when we read the data),
    /// not the time the machine produced the data. There may be a small
    /// latency delta (typically <50ms on local networks).
    /// 
    /// Always UTC to avoid timezone confusion in multi-location deployments.
    /// DateTimeOffset preserves timezone info in JSON serialization.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Machine connectivity and operational status.
    /// 
    /// Set by FanucApiClient.CollectDataAsync() based on API responses:
    ///   - Online:  All data points collected successfully
    ///   - Offline: Circuit breaker is open (machine unreachable)
    ///   - Error:   HTTP request failed (timeout, network error)
    ///   - Alarm:   Machine online but has active alarms
    ///   - Unknown: Initial state before first poll completes
    /// 
    /// Serialized as a string in JSON (via JsonStringEnumConverter).
    /// </summary>
    public MachineStatus Status { get; set; } = MachineStatus.Unknown;

    /// <summary>
    /// Currently loaded/running CNC program name or number.
    /// 
    /// Fanuc FOCAS returns this as a string like "O1234" or "PART_A.NC".
    /// Null if the Program/MainProgram data point was not collected
    /// or the API call failed.
    /// </summary>
    public string? MainProgram { get; set; }

    /// <summary>
    /// Machine running state as reported by the CNC controller.
    /// 
    /// Typical values: "Running", "Idle", "Hold", "Stopped", "Alarm"
    /// The exact strings depend on the Fanuc controller model and firmware.
    /// Null if not collected.
    /// </summary>
    public string? RunningStatus { get; set; }

    /// <summary>
    /// CNC auto mode: "MDI", "MEM", "EDIT", "HANDLE", "JOG", "TJOG", "THND".
    /// Read from cnc_statinfo.Aut on each poll. Null if not collected.
    /// </summary>
    public string? AutoMode { get; set; }

    /// <summary>
    /// Whether the emergency stop button is active on the controller.
    /// Read from cnc_statinfo.Emergency on each poll. Null if not collected.
    /// </summary>
    public bool? EmergencyStop { get; set; }

    /// <summary>
    /// Cached CNC system identity (series, type, version, axis names).
    /// Populated once on first FOCAS2 connection, then attached to every snapshot.
    /// Null for non-FOCAS2 data sources or if sysinfo read failed.
    /// </summary>
    public CncSystemInfo? SystemInfo { get; set; }

    /// <summary>
    /// Tool information: current tool, offsets, and tool life data.
    /// Populated from FOCAS2 cnc_modal, cnc_rdtofs, cnc_rdtlgrp calls.
    /// Null for non-FOCAS2 data sources or if tool reads are not configured.
    ///
    /// FOCAS2 ADVANTAGE: MT-LINKi requires a separate license for tool data.
    /// FOCAS2 provides this at no additional cost.
    /// </summary>
    public ToolInfoData? ToolInfo { get; set; }

    /// <summary>
    /// Axis position data keyed by axis name (e.g., "X", "Y", "Z", "A", "B").
    /// 
    /// A typical 3-axis mill has X, Y, Z entries.
    /// A 5-axis machine adds A and B (or C) rotary axes.
    /// A lathe typically has X and Z.
    /// 
    /// The dictionary key is the axis name from the FOCAS response.
    /// An empty dictionary means axis data was not collected or unavailable.
    /// </summary>
    public Dictionary<string, AxisData> Axes { get; set; } = new();

    /// <summary>
    /// Spindle data (speed, load, direction).
    /// 
    /// Null if spindle data was not collected. For multi-spindle machines,
    /// this represents the primary spindle — extend to a Dictionary&lt;string, SpindleData&gt;
    /// if you need to support multiple spindles.
    /// </summary>
    public SpindleData? Spindle { get; set; }

    /// <summary>
    /// List of currently active alarms on the CNC controller.
    /// 
    /// An empty list means "no active alarms" (which is the happy path).
    /// When alarms are present, the Status is automatically set to MachineStatus.Alarm.
    /// 
    /// IMPORTANT: This only reflects alarms ACTIVE at the time of collection.
    /// Historical alarms that have been cleared won't appear here.
    /// For alarm history, query the Fanuc alarm history API separately.
    /// </summary>
    public List<AlarmData> ActiveAlarms { get; set; } = [];

    /// <summary>
    /// Current or last cycle time in seconds.
    /// 
    /// Represents how long the current (or most recently completed) machining
    /// cycle took. Useful for OEE (Overall Equipment Effectiveness) calculations.
    /// Null if not collected.
    /// </summary>
    public double? CycleTimeSeconds { get; set; }

    /// <summary>
    /// Parts produced counter — cumulative count from the CNC controller.
    /// 
    /// This is the machine's internal parts counter, which typically
    /// increments each time a machining cycle completes. It may reset
    /// when the operator resets the counter or loads a new program.
    /// 
    /// Downstream systems should calculate deltas (PartsCount[now] - PartsCount[previous])
    /// to determine production rate, rather than using the absolute value.
    /// </summary>
    public long? PartsCount { get; set; }
    public string? CncState_path1_CNC { get; set; }
    public int? PartsNum_path1_CNC { get; set; }
    public bool? Disconnect_CNC    { get; set; }
    public string? Mode_path1_CNC { get; set; }
    public string? MainProgram_path1_CNC { get; set; }
    public string? ActProgram_path1_CNC { get; set; }
    public string? MainComment_path1_CNC { get; set; }
    public string? ActComment_path1_CNC  { get; set; }
    public bool? SigCUT_path1_CNC    { get; set; }
    public bool? SigSBK_path1_CNC { get; set; }
    public bool? SigDM00_path1_CNC    { get; set; }
    public bool? SigDM01_path1_CNC { get; set; }
    public bool? SigMDRN_path1_CNC { get; set; }
    public string? CncWarning_path1_CNC { get; set; }
    public string? CncFan1Status_path1_CNC { get; set; }
    public string? CncFan2Status_path1_CNC { get; set; }
    public string? CncFan3Status_path1_CNC { get; set; }
    public string? CncFan4Status_path1_CNC { get; set; }
    public bool? CncBatLow_0_path1_CNC { get; set; }
    public bool? ApcBatZero_0_path1_CNC { get; set; }
    public bool? ApcBatLow_0_path1_CNC { get; set; }
    public bool? IndBatZero_0_path1_CNC { get; set; }
    public bool? SpdBatZero_0_path1_CNC { get; set; }
    public bool? SSpdBatZero_0_path1_CNC { get; set; }
    public int? ServoTemp_0_path1_CNC { get; set; }
    public int? PulseCoderTemp_0_path1_CNC { get; set; }
    public int? ServoLeakResistData_0_path1_CNC { get; set; }
    public string? InFan1SrvAmpStatus_0_path1_CNC { get; set; }
    public string? InFan2SrvAmpStatus_0_path1_CNC { get; set; }
    public string? RadFan1SrvAmpStatus_0_path1_CNC { get; set; }
    public string? RadFan2SrvAmpStatus_0_path1_CNC { get; set; }
    public string? InFan1SrvComPwStatus_0_path1_CNC { get; set; }
    public string? InFan2SrvComPwStatus_0_path1_CNC { get; set; }
    public string? RadFan1SrvComPwStatus_0_path1_CNC { get; set; }
    public string? RadFan2SrvComPwStatus_0_path1_CNC { get; set; }
    public bool? CncBatLow_1_path1_CNC { get; set; }
    public bool? ApcBatZero_1_path1_CNC { get; set; }
    public bool? ApcBatLow_1_path1_CNC { get; set; }
    public bool? IndBatZero_1_path1_CNC { get; set; }
    public bool? SpdBatZero_1_path1_CNC { get; set; }
    public bool? SSpdBatZero_1_path1_CNC { get; set; }
    public int? ServoTemp_1_path1_CNC { get; set; }
    public int? PulseCoderTemp_1_path1_CNC { get; set; }
    public int? ServoLeakResistData_1_path1_CNC { get; set; }
    public string? InFan1SrvAmpStatus_1_path1_CNC { get; set; }
    public string? InFan2SrvAmpStatus_1_path1_CNC { get; set; }
    public string? RadFan1SrvAmpStatus_1_path1_CNC { get; set; }
    public string? RadFan2SrvAmpStatus_1_path1_CNC { get; set; }
    public string? InFan1SrvComPwStatus_1_path1_CNC { get; set; }
    public string? InFan2SrvComPwStatus_1_path1_CNC { get; set; }
    public string? RadFan1SrvComPwStatus_1_path1_CNC { get; set; }
    public string? RadFan2SrvComPwStatus_1_path1_CNC { get; set; }
    public int? SpindleTemp_0_path1_CNC { get; set; }
    public int? SpindleLeakResistData_0_path1_CNC { get; set; }
    public int? SpindleTotalRev1_0_path1_CNC { get; set; }
    public int? SpindleTotalRev2_0_path1_CNC { get; set; }
    public string? InFan1SpdlComPwStatus_0_path1_CNC { get; set; }
    public string? InFan2SpdlComPwStatus_0_path1_CNC { get; set; }
    public string? RadFan1SpdlComPwStatus_0_path1_CNC { get; set; }
    public string? RadFan2SpdlComPwStatus_0_path1_CNC    { get; set; }
    public string? InFan1SpindleAmpStatus_0_path1_CNC { get; set; }
    public string? InFan2SpindleAmpStatus_0_path1_CNC { get; set; }
    public string? RadFan1SpindleAmpStatus_0_path1_CNC { get; set; }
    public string? RadFan2SpindleAmpStatus_0_path1_CNC { get; set; }
    

    /// <summary>
    /// Current feed rate in mm/min (metric) or in/min (imperial).
    /// 
    /// The unit depends on the machine's configured measurement system.
    /// This is the ACTUAL feed rate (not the programmed rate), which may
    /// differ due to feed rate override knob on the controller.
    /// Null if not collected.
    /// </summary>
    public double? FeedRate { get; set; }

    /// <summary>
    /// Catch-all dictionary for additional/custom data points.
    /// 
    /// USE CASES:
    ///   - Machine-specific data not covered by the typed properties above
    ///   - Future data points added without modifying this model
    ///   - Custom Fanuc PMC (Programmable Machine Controller) data
    ///   - Macro variables or custom FOCAS data
    /// 
    /// Keys should use dot-notation for namespacing: "coolant.level", "tool.current"
    /// Values can be any JSON-serializable type (string, number, bool, etc.)
    /// </summary>
    public Dictionary<string, object?> AdditionalData { get; set; } = new();

    /// <summary>
    /// Tags copied from the machine's configuration (MachineConfig.Tags).
    /// 
    /// Included in every payload so MQTT subscribers can filter/group data
    /// by tag without maintaining a separate configuration lookup.
    /// Example: ["lathe", "bay-1", "critical"]
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Time taken to collect all data points from this machine (milliseconds).
    /// 
    /// Measured with Stopwatch in FanucApiClient.CollectDataAsync().
    /// Useful for performance monitoring:
    ///   - Normal: 20-100ms on a local network
    ///   - Slow:   100-500ms may indicate network issues or overloaded controller
    ///   - Timeout: >TimeoutMs indicates a serious connectivity problem
    /// 
    /// Dashboard operators can use this to identify machines with degrading
    /// network performance before they start causing timeouts.
    /// </summary>
    public long CollectionDurationMs { get; set; }
}

/// <summary>
/// Position data for a single CNC axis (e.g., X, Y, Z, A, B, C).
/// 
/// Fanuc CNC machines report multiple coordinate systems simultaneously:
///   - Absolute:  Position in the workpiece coordinate system (G54-G59)
///   - Machine:   Position in the machine's home coordinate system
///   - Relative:  Position relative to a programmer-set reference point
///   - DistToGo:  Remaining distance in the current move command
/// 
/// All values are in millimeters (metric) or inches (imperial) depending
/// on the machine's G20/G21 setting.
/// </summary>
public sealed class AxisData
{
    /// <summary>Axis identifier (e.g., "X", "Y", "Z", "A", "B", "C").</summary>
    public string AxisName { get; set; } = string.Empty;

    /// <summary>
    /// Position in the active workpiece coordinate system (G54-G59).
    /// This is the most commonly used position for monitoring — it tells
    /// you where the tool is relative to the workpiece.
    /// </summary>
    public double? AbsolutePosition { get; set; }

    /// <summary>
    /// Position in the machine's home coordinate system.
    /// This is the "raw" position relative to the machine's reference point (home).
    /// Useful for diagnostics and collision detection.
    /// </summary>
    public double? MachinePosition { get; set; }

    /// <summary>
    /// Position relative to a user-set reference point.
    /// Operators can reset this to zero at any position for convenience.
    /// Less commonly used in automated monitoring.
    /// </summary>
    public double? RelativePosition { get; set; }

    /// <summary>
    /// Remaining distance in the current motion command.
    /// Shows how far the axis still needs to travel to complete the current
    /// G-code block. Reaches zero when the move is complete.
    /// Useful for progress estimation within a machining operation.
    /// </summary>
    public double? DistanceToGo { get; set; }
}

/// <summary>
/// Spindle data from the CNC controller.
/// 
/// The spindle is the rotating component that holds the cutting tool (mills)
/// or the workpiece (lathes). Monitoring spindle speed and load is critical
/// for detecting:
///   - Tool wear (gradually increasing load)
///   - Tool breakage (sudden load spike or drop to zero)
///   - Programming errors (wrong speed for the material)
/// </summary>
public sealed class SpindleData
{
    /// <summary>
    /// Spindle speed in RPM (revolutions per minute).
    /// This is the ACTUAL speed, which may differ from the programmed speed
    /// due to the spindle override knob on the controller.
    /// </summary>
    public double? Speed { get; set; }

    /// <summary>
    /// Spindle load as a percentage of rated capacity (0-100+).
    /// 
    /// Values above 100% indicate the spindle is overloaded and may trigger
    /// an alarm on the controller. Normal machining typically runs at 20-60%.
    /// 
    /// MONITORING TIPS:
    ///   - Gradually increasing load → tool wear (schedule tool change)
    ///   - Sudden spike → possible tool breakage or hard material
    ///   - Near-zero during cutting → tool may have broken
    /// </summary>
    public double? Load { get; set; }

    /// <summary>
    /// Spindle rotation direction: "CW" (clockwise), "CCW" (counter-clockwise),
    /// or "Stopped". The direction is determined by M03 (CW) and M04 (CCW) G-codes.
    /// </summary>
    public string? Direction { get; set; }
}

/// <summary>
/// Represents a single active alarm on the CNC controller.
/// 
/// Fanuc CNC machines have multiple alarm categories:
///   - PS (Program/Syntax):  G-code programming errors
///   - OT (Overtravel):      Axis exceeded soft/hard travel limits
///   - SV (Servo):           Servo motor or encoder errors
///   - OH (Overheat):        Temperature exceeded limits
///   - SR (System/Reset):    Requires system reset
///   - MC (Machine):         PMC-generated alarms (machine builder specific)
///   - EX (External):        External device alarms
/// </summary>
public sealed class AlarmData
{
    /// <summary>
    /// Fanuc alarm number (e.g., 10, 300, 5530).
    /// Each alarm number has a specific meaning documented in the
    /// Fanuc operator's manual for the specific controller model.
    /// </summary>
    public int AlarmNumber { get; set; }

    /// <summary>
    /// Alarm type/category (e.g., "PS", "OT", "SV", "OH", "MC").
    /// See class documentation for the meaning of each type.
    /// </summary>
    public string AlarmType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable alarm message.
    /// This is the description text from the Fanuc controller, e.g.,
    /// "SERVO ALARM: EXCESS ERROR" or "OVERTRAVEL: +X".
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Axis associated with this alarm (e.g., "X", "Y", "Z", "SP" for spindle).
    /// Empty string if the alarm is not axis-specific.
    /// </summary>
    public string Axis { get; set; } = string.Empty;
}

/// <summary>
/// Machine connectivity and operational status enum.
/// 
/// Serialized as a string in JSON payloads using JsonStringEnumConverter,
/// making it human-readable for downstream consumers:
///   { "status": "Online" }  instead of  { "status": 1 }
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MachineStatus
{
    /// <summary>Initial state before first poll completes.</summary>
    Unknown,

    /// <summary>Machine is reachable and all data points collected successfully.</summary>
    Online,

    /// <summary>Machine is unreachable (circuit breaker open, typically after 5 consecutive failures).</summary>
    Offline,

    /// <summary>Machine had an HTTP error (timeout, network failure) but circuit breaker hasn't tripped yet.</summary>
    Error,

    /// <summary>Machine is online but has active alarms requiring operator attention.</summary>
    Alarm
}

public sealed class SignalConfig
{
    

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;
    [JsonPropertyName("formula")]
    public string Formula { get; set; } = string.Empty;
    [JsonPropertyName("dataType")]
    public string Datatype { get; set; } = string.Empty;
}
