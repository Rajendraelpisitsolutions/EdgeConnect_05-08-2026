// ============================================================================
// File: Focas2TagMap.cs
// Purpose: Static registry mapping FOCAS2 CNC data fields to canonical tag
//          names, paths, value types, and units. Used by collectors to emit
//          CanonicalDataPoints and by BrowseTagsAsync for tag discovery.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1
// ============================================================================

using System.Collections.Frozen;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Metadata for a single FOCAS2 tag, mapping a CNC field to its canonical
/// representation.
/// </summary>
internal sealed record TagMapEntry
{
    /// <summary>Canonical tag name (e.g., "status/run_state").</summary>
    public required string TagName { get; init; }

    /// <summary>Hierarchical tag path (e.g., "Status/RunState").</summary>
    public required string TagPath { get; init; }

    /// <summary>Expected value type.</summary>
    public required CanonicalValueType ValueType { get; init; }

    /// <summary>Engineering unit, if applicable.</summary>
    public string? Unit { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Static tag registry for all known FOCAS2 data points. Axis tags are
/// templated with <c>{axis}</c> and expanded at runtime based on the CNC's
/// configured axes.
/// </summary>
internal static class Focas2TagMap
{
    // ---- Status ----
    internal static readonly TagMapEntry RunState = new()
    {
        TagName = "status/run_state", TagPath = "Status/RunState",
        ValueType = CanonicalValueType.String, Description = "CNC run state (Reset, Stop, Hold, Start, MSTR)",
    };

    internal static readonly TagMapEntry AutoMode = new()
    {
        TagName = "status/auto_mode", TagPath = "Status/AutoMode",
        ValueType = CanonicalValueType.String, Description = "CNC auto mode (MDI, MEM, EDIT, HANDLE, JOG, TJOG, THND)",
    };

    internal static readonly TagMapEntry EmergencyStop = new()
    {
        TagName = "status/emergency_stop", TagPath = "Status/EmergencyStop",
        ValueType = CanonicalValueType.Boolean, Description = "Emergency stop active",
    };

    internal static readonly TagMapEntry MotionState = new()
    {
        TagName = "status/motion", TagPath = "Status/Motion",
        ValueType = CanonicalValueType.String, Description = "Axis motion state (None, Motion, Dwell)",
    };

    internal static readonly TagMapEntry AlarmFlag = new()
    {
        TagName = "status/alarm_flag", TagPath = "Status/AlarmFlag",
        ValueType = CanonicalValueType.Integer, Description = "Alarm state flag (0=none, 1=alarm, 2=battery)",
    };

    // ---- Program ----
    internal static readonly TagMapEntry MainProgram = new()
    {
        TagName = "program/main_program", TagPath = "Program/MainProgram",
        ValueType = CanonicalValueType.String, Description = "Main program O-number",
    };

    internal static readonly TagMapEntry RunningProgram = new()
    {
        TagName = "program/running_program", TagPath = "Program/RunningProgram",
        ValueType = CanonicalValueType.String, Description = "Currently running program O-number",
    };

    // ---- Axis templates (expanded per discovered axis) ----
    internal static TagMapEntry AxisAbsolute(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/absolute",
        TagPath = $"Axes/{axis}/Absolute",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Absolute position of {axis} axis",
    };

    internal static TagMapEntry AxisMachine(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/machine",
        TagPath = $"Axes/{axis}/Machine",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Machine coordinate of {axis} axis",
    };

    internal static TagMapEntry AxisRelative(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/relative",
        TagPath = $"Axes/{axis}/Relative",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Relative position of {axis} axis",
    };

    internal static TagMapEntry AxisDistanceToGo(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/distance_to_go",
        TagPath = $"Axes/{axis}/DistanceToGo",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Distance to go for {axis} axis",
    };

    // ---- Feed Rate ----
    internal static readonly TagMapEntry FeedRate = new()
    {
        TagName = "axes/feed_rate", TagPath = "Axes/FeedRate",
        ValueType = CanonicalValueType.Double, Unit = "mm/min",
        Description = "Actual feed rate",
    };

    // ---- Spindle ----
    internal static readonly TagMapEntry SpindleSpeed = new()
    {
        TagName = "spindle/speed", TagPath = "Spindle/Speed",
        ValueType = CanonicalValueType.Double, Unit = "rpm",
        Description = "Actual spindle speed",
    };

    internal static readonly TagMapEntry SpindleLoad = new()
    {
        TagName = "spindle/load", TagPath = "Spindle/Load",
        ValueType = CanonicalValueType.Double, Unit = "%",
        Description = "Spindle load percentage",
    };

    // ---- Alarms ----
    internal static readonly TagMapEntry AlarmsActive = new()
    {
        TagName = "alarms/active", TagPath = "Alarms/Active",
        ValueType = CanonicalValueType.Object,
        Description = "Active alarm array (alarm number, type, message, axis)",
    };

    internal static readonly TagMapEntry AlarmCount = new()
    {
        TagName = "alarms/count", TagPath = "Alarms/Count",
        ValueType = CanonicalValueType.Integer,
        Description = "Number of active alarms",
    };

    // ---- Production ----
    internal static readonly TagMapEntry CycleTime = new()
    {
        TagName = "production/cycle_time", TagPath = "Production/CycleTime",
        ValueType = CanonicalValueType.Double, Unit = "s",
        Description = "Cycle time in seconds",
    };

    internal static readonly TagMapEntry PartsCount = new()
    {
        TagName = "production/parts_count", TagPath = "Production/PartsCount",
        ValueType = CanonicalValueType.Long,
        Description = "Cumulative parts counter",
    };

    // ---- Tool ----
    // NOTE (LOCKED naming exception): tool tags deliberately break the
    // snake_case-with-slashes convention used elsewhere in this map and use
    // PascalCase-dotted names (Tool.CurrentNo, Tool.{n}.GeomRad, ToolLife.{g}.Counter).
    // This is REQUIRED by the EREMOS V2 consumer, whose ToolLifeIngestionService
    // detects tool topics by the literal "/Tool." / "/ToolLife." substring and
    // matches field names exactly (see EREMOS_API ToolLifeIngestionService.cs).
    // The MQTT sink sanitizer preserves '.' and case, so a TagName like
    // "Tool.13.GeomRad" becomes topic segment ".../{sourceId}/Tool.13.GeomRad",
    // which contains the required "/Tool." marker. Reconciled in
    // shared-knowledge/contracts/cnc-vocabulary.md (Tool section).
    internal static readonly TagMapEntry ToolCurrentNumber = new()
    {
        TagName = "Tool.CurrentNo", TagPath = "Tool/CurrentNo",
        ValueType = CanonicalValueType.Integer,
        Description = "Current tool number (T-code)",
    };

    internal static readonly TagMapEntry ToolOffsetType = new()
    {
        TagName = "Tool.OffsetType", TagPath = "Tool/OffsetType",
        ValueType = CanonicalValueType.String,
        Description = "Tool offset memory type (A, B, or C)",
    };

    internal static readonly TagMapEntry ToolOffsetCount = new()
    {
        TagName = "Tool.OffsetCount", TagPath = "Tool/OffsetCount",
        ValueType = CanonicalValueType.Integer,
        Description = "Number of available tool offset sets",
    };

    internal static readonly TagMapEntry ToolLifeEnabled = new()
    {
        TagName = "Tool.LifeEnabled", TagPath = "Tool/LifeEnabled",
        ValueType = CanonicalValueType.Boolean,
        Description = "Whether tool life management is enabled on this CNC",
    };

    // ---- Tool offset value field names (gateway short form, normalized by EREMOS) ----
    internal const string OffsetWearLength = "WearLen";
    internal const string OffsetWearRadius = "WearRad";
    internal const string OffsetGeometryLength = "GeomLen";
    internal const string OffsetGeometryRadius = "GeomRad";

    /// <summary>
    /// Per-tool offset value tag (dynamic). <paramref name="field"/> is one of
    /// the Offset* constants. Emitted only for non-zero values; not pre-enumerated
    /// in <see cref="BuildTagDefinitions"/> (browse), like individual alarms.
    /// </summary>
    internal static TagMapEntry ToolOffset(int toolNumber, string field) => new()
    {
        TagName = $"Tool.{toolNumber}.{field}",
        TagPath = $"Tool/{toolNumber}/{field}",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Tool {toolNumber} {field} offset",
    };

    /// <summary>Per-tool active flag (dynamic) — emitted for the current tool's offset.</summary>
    internal static TagMapEntry ToolOffsetActive(int toolNumber) => new()
    {
        TagName = $"Tool.{toolNumber}.IsActive",
        TagPath = $"Tool/{toolNumber}/IsActive",
        ValueType = CanonicalValueType.Boolean,
        Description = $"True if tool {toolNumber} is the active tool offset",
    };

    // ---- Tool life field names (matched exactly by EREMOS, case-sensitive) ----
    internal const string LifeToolNo = "ToolNo";
    internal const string LifeCounter = "Counter";
    internal const string LifeLimit = "Limit";
    internal const string LifeCountType = "CountType";
    internal const string LifeStatus = "Status";
    internal const string LifeRemain = "LifeRemain";

    /// <summary>Per-group tool-life tag (dynamic). <paramref name="field"/> is one of the Life* constants.</summary>
    internal static TagMapEntry ToolLifeGroup(int groupNo, string field, CanonicalValueType valueType) => new()
    {
        TagName = $"ToolLife.{groupNo}.{field}",
        TagPath = $"ToolLife/{groupNo}/{field}",
        ValueType = valueType,
        Description = $"Tool life group {groupNo} {field}",
    };

    // ---- MT-LINKi Compatible ----
    internal static readonly TagMapEntry MtLinkiCncState = new()
    {
        TagName = "mtlinki/cnc_state", TagPath = "MtLinki/CncState",
        ValueType = CanonicalValueType.String,
        Description = "MT-LINKi CNC state tag",
    };

    internal static readonly TagMapEntry MtLinkiMode = new()
    {
        TagName = "mtlinki/mode", TagPath = "MtLinki/Mode",
        ValueType = CanonicalValueType.String,
        Description = "MT-LINKi operation mode",
    };

    internal static readonly TagMapEntry MtLinkiPartsNum = new()
    {
        TagName = "mtlinki/parts_num", TagPath = "MtLinki/PartsNum",
        ValueType = CanonicalValueType.Long,
        Description = "MT-LINKi parts count",
    };

    internal static readonly TagMapEntry MtLinkiSigCut = new()
    {
        TagName = "mtlinki/sig_cut", TagPath = "MtLinki/SigCUT",
        ValueType = CanonicalValueType.Boolean,
        Description = "MT-LINKi cutting signal (PMC F4.0)",
    };

    internal static readonly TagMapEntry MtLinkiSigFeed = new()
    {
        TagName = "mtlinki/sig_feed", TagPath = "MtLinki/SigFEED",
        ValueType = CanonicalValueType.Boolean,
        Description = "MT-LINKi feed signal",
    };

    internal static readonly TagMapEntry MtLinkiSigM = new()
    {
        TagName = "mtlinki/sig_m", TagPath = "MtLinki/SigM",
        ValueType = CanonicalValueType.Boolean,
        Description = "MT-LINKi M-code signal",
    };

    internal static readonly TagMapEntry MtLinkiSigS = new()
    {
        TagName = "mtlinki/sig_s", TagPath = "MtLinki/SigS",
        ValueType = CanonicalValueType.Boolean,
        Description = "MT-LINKi S-code signal",
    };

    internal static readonly TagMapEntry MtLinkiSigT = new()
    {
        TagName = "mtlinki/sig_t", TagPath = "MtLinki/SigT",
        ValueType = CanonicalValueType.Boolean,
        Description = "MT-LINKi T-code signal",
    };

    internal static readonly TagMapEntry MtLinkiCncWarning = new()
    {
        TagName = "mtlinki/cnc_warning", TagPath = "MtLinki/CncWarning",
        ValueType = CanonicalValueType.String,
        Description = "MT-LINKi CNC operator warning message",
    };

    // ---- MT-LINKi Diagnostics: Servo temperatures ----
    internal static TagMapEntry ServoTemp(int axisIndex) => new()
    {
        TagName = $"diagnostics/servo/{axisIndex}/temperature",
        TagPath = $"Diagnostics/Servo/{axisIndex}/Temperature",
        ValueType = CanonicalValueType.Integer, Unit = "C",
        Description = $"Servo motor temperature for axis {axisIndex}",
    };

    internal static TagMapEntry ServoInsulation(int axisIndex) => new()
    {
        TagName = $"diagnostics/servo/{axisIndex}/insulation",
        TagPath = $"Diagnostics/Servo/{axisIndex}/Insulation",
        ValueType = CanonicalValueType.Integer,
        Description = $"Servo insulation resistance for axis {axisIndex}",
    };

    // ---- MT-LINKi Diagnostics: Spindle temperatures ----
    internal static readonly TagMapEntry SpindleTemp = new()
    {
        TagName = "diagnostics/spindle/temperature",
        TagPath = "Diagnostics/Spindle/Temperature",
        ValueType = CanonicalValueType.Integer, Unit = "C",
        Description = "Spindle motor temperature",
    };

    internal static readonly TagMapEntry SpindleInsulation = new()
    {
        TagName = "diagnostics/spindle/insulation",
        TagPath = "Diagnostics/Spindle/Insulation",
        ValueType = CanonicalValueType.Integer,
        Description = "Spindle insulation resistance",
    };

    // ---- MT-LINKi Diagnostics: Fan statuses ----
    internal static TagMapEntry CncFanStatus(int fanIndex) => new()
    {
        TagName = $"diagnostics/cnc_fan/{fanIndex}/status",
        TagPath = $"Diagnostics/CncFan/{fanIndex}/Status",
        ValueType = CanonicalValueType.String,
        Description = $"CNC fan {fanIndex} status (OK, Warning, Fault)",
    };

    internal static TagMapEntry SpFanStatus(int fanIndex) => new()
    {
        TagName = $"diagnostics/sp_fan/{fanIndex}/status",
        TagPath = $"Diagnostics/SpFan/{fanIndex}/Status",
        ValueType = CanonicalValueType.String,
        Description = $"Spindle amplifier fan {fanIndex} status",
    };

    internal static TagMapEntry SvFanStatus(int fanIndex) => new()
    {
        TagName = $"diagnostics/sv_fan/{fanIndex}/status",
        TagPath = $"Diagnostics/SvFan/{fanIndex}/Status",
        ValueType = CanonicalValueType.String,
        Description = $"Servo amplifier fan {fanIndex} status",
    };

    // ---- MT-LINKi Diagnostics: Battery ----
    internal static readonly TagMapEntry CncBattery = new()
    {
        TagName = "diagnostics/battery/cnc",
        TagPath = "Diagnostics/Battery/CNC",
        ValueType = CanonicalValueType.String,
        Description = "CNC battery status (OK, Warning)",
    };

    internal static readonly TagMapEntry ServoBattery = new()
    {
        TagName = "diagnostics/battery/servo",
        TagPath = "Diagnostics/Battery/Servo",
        ValueType = CanonicalValueType.String,
        Description = "Servo battery status",
    };

    // ---- Static tag list for BrowseTagsAsync (non-dynamic tags) ----

    /// <summary>All static (non-axis, non-indexed) tag entries.</summary>
    internal static readonly FrozenSet<TagMapEntry> StaticTags = new TagMapEntry[]
    {
        RunState, AutoMode, EmergencyStop, MotionState, AlarmFlag,
        MainProgram, RunningProgram,
        FeedRate,
        SpindleSpeed, SpindleLoad,
        AlarmsActive, AlarmCount,
        CycleTime, PartsCount,
        ToolCurrentNumber, ToolOffsetType, ToolOffsetCount, ToolLifeEnabled,
        MtLinkiCncState, MtLinkiMode, MtLinkiPartsNum,
        MtLinkiCncWarning,
        MtLinkiSigCut, MtLinkiSigFeed, MtLinkiSigM, MtLinkiSigS, MtLinkiSigT,
        SpindleTemp, SpindleInsulation,
        CncBattery, ServoBattery,
    }.ToFrozenSet();

    /// <summary>
    /// Build the full tag definition list including dynamic axis and diagnostic tags.
    /// </summary>
    internal static IReadOnlyList<TagDefinition> BuildTagDefinitions(IReadOnlyList<string> axisNames)
    {
        var defs = new List<TagDefinition>();

        // Static tags
        foreach (var entry in StaticTags)
        {
            defs.Add(ToDefinition(entry));
        }

        // Axis tags (4 per axis)
        foreach (var axis in axisNames)
        {
            defs.Add(ToDefinition(AxisAbsolute(axis)));
            defs.Add(ToDefinition(AxisMachine(axis)));
            defs.Add(ToDefinition(AxisRelative(axis)));
            defs.Add(ToDefinition(AxisDistanceToGo(axis)));
        }

        // Servo diagnostic tags (per axis index)
        for (int i = 0; i < axisNames.Count; i++)
        {
            defs.Add(ToDefinition(ServoTemp(i)));
            defs.Add(ToDefinition(ServoInsulation(i)));
        }

        // Fan status tags (4 CNC fans, 4 SP fans, 4 SV fans)
        for (int i = 1; i <= 4; i++)
        {
            defs.Add(ToDefinition(CncFanStatus(i)));
            defs.Add(ToDefinition(SpFanStatus(i)));
            defs.Add(ToDefinition(SvFanStatus(i)));
        }

        return defs;
    }

    private static TagDefinition ToDefinition(TagMapEntry entry) => new()
    {
        Name = entry.TagName,
        Path = entry.TagPath,
        ValueType = entry.ValueType,
        Unit = entry.Unit,
        Description = entry.Description,
        Writable = false,
    };
}
