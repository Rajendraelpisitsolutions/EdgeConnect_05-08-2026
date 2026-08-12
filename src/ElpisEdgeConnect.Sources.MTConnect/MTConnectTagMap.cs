// ============================================================================
// File: MTConnectTagMap.cs
// Purpose: Canonical tag-name registry for the MTConnect adapter. Tag names
//          deliberately mirror the FOCAS2 adapter's names where the semantic
//          overlaps (status/run_state, status/emergency_stop, etc.) so a
//          downstream consumer can treat a "Status/RunState" MQTT topic
//          identically regardless of which CNC protocol produced it.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.1
// ============================================================================

using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>Metadata for a single MTConnect tag.</summary>
internal sealed record MTConnectTagMapEntry
{
    public required string TagName { get; init; }
    public required string TagPath { get; init; }
    public required CanonicalValueType ValueType { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Static tag registry for MTConnect data items the adapter emits. Keys
/// are the canonical tag names downstream consumers (MQTT topics, EREMOS)
/// see; values hold the metadata needed by
/// <see cref="MTConnectSourceAdapter.BrowseTagsAsync"/>.
/// </summary>
internal static class MTConnectTagMap
{
    // ---- Status ----
    internal static readonly MTConnectTagMapEntry RunState = new()
    {
        TagName = "status/run_state", TagPath = "Status/RunState",
        ValueType = CanonicalValueType.String,
        Description = "Execution state mapped from MTConnect Execution (Active/Interrupted/Stopped/Ready/etc.)",
    };

    internal static readonly MTConnectTagMapEntry ControllerMode = new()
    {
        TagName = "status/controller_mode", TagPath = "Status/ControllerMode",
        ValueType = CanonicalValueType.String,
        Description = "Controller mode mapped from MTConnect ControllerMode (Auto/Manual/MDI/Edit/etc.)",
    };

    internal static readonly MTConnectTagMapEntry EmergencyStop = new()
    {
        TagName = "status/emergency_stop", TagPath = "Status/EmergencyStop",
        ValueType = CanonicalValueType.Boolean,
        Description = "True when MTConnect EmergencyStop reports TRIGGERED",
    };

    // ---- Program ----
    internal static readonly MTConnectTagMapEntry MainProgram = new()
    {
        TagName = "program/main_program", TagPath = "Program/MainProgram",
        ValueType = CanonicalValueType.String,
        Description = "Current main program name",
    };

    internal static readonly MTConnectTagMapEntry RunningProgram = new()
    {
        TagName = "program/running_program", TagPath = "Program/RunningProgram",
        ValueType = CanonicalValueType.String,
        Description = "Sub-program / currently executing program (falls back to main)",
    };

    // ---- Spindle ----
    internal static readonly MTConnectTagMapEntry SpindleSpeed = new()
    {
        TagName = "spindle/speed", TagPath = "Spindle/Speed",
        ValueType = CanonicalValueType.Double, Unit = "rpm",
        Description = "Spindle rotational velocity",
    };

    internal static readonly MTConnectTagMapEntry SpindleLoad = new()
    {
        TagName = "spindle/load", TagPath = "Spindle/Load",
        ValueType = CanonicalValueType.Double, Unit = "%",
        Description = "Spindle load percentage",
    };

    // ---- Feed rate ----
    internal static readonly MTConnectTagMapEntry FeedRate = new()
    {
        TagName = "axes/feed_rate", TagPath = "Axes/FeedRate",
        ValueType = CanonicalValueType.Double, Unit = "mm/min",
        Description = "Path feedrate",
    };

    // ---- Production ----
    internal static readonly MTConnectTagMapEntry PartsCount = new()
    {
        TagName = "production/parts_count", TagPath = "Production/PartsCount",
        // Long, not Integer: a parts counter is monotonic and can exceed
        // int.MaxValue over a long production run, and the stream parser reads
        // it via TryGetLong. Declaring Integer here while emitting a boxed long
        // was the source-side type mismatch that, before the BinaryWriterFormat
        // coercion fix, threw InvalidCastException at buffer-write time and
        // stranded the whole route. Long matches the emitted CLR type exactly.
        ValueType = CanonicalValueType.Long,
        Description = "Total parts produced",
    };

    internal static readonly MTConnectTagMapEntry CycleTime = new()
    {
        TagName = "production/cycle_time", TagPath = "Production/CycleTime",
        ValueType = CanonicalValueType.Double, Unit = "s",
        Description = "Cycle time / process timer in seconds",
    };

    // ---- Alarms ----
    internal static readonly MTConnectTagMapEntry AlarmCount = new()
    {
        TagName = "alarms/count", TagPath = "Alarms/Count",
        ValueType = CanonicalValueType.Integer,
        Description = "Count of active Fault conditions",
    };

    internal static readonly MTConnectTagMapEntry FirstFaultMessage = new()
    {
        TagName = "alarms/first_fault", TagPath = "Alarms/FirstFault",
        ValueType = CanonicalValueType.String,
        Description = "Text of the first active Fault condition (empty when none)",
    };

    // ---- Axis position templates ----
    // Expanded at runtime per discovered axis, producing tags like
    // "axes/x/absolute", "axes/y/machine", etc.

    internal static MTConnectTagMapEntry AxisAbsolute(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/absolute",
        TagPath = $"Axes/{axis}/Absolute",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Axis {axis} actual position (MTConnect Position subType=ACTUAL)",
    };

    internal static MTConnectTagMapEntry AxisMachine(string axis) => new()
    {
        TagName = $"axes/{axis.ToLowerInvariant()}/machine",
        TagPath = $"Axes/{axis}/Machine",
        ValueType = CanonicalValueType.Double, Unit = "mm",
        Description = $"Axis {axis} machine-coordinate position (MTConnect Position subType=MACHINE)",
    };
}
