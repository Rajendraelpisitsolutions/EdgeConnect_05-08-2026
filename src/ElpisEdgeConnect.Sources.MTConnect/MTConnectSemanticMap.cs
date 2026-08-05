// ============================================================================
// File: MTConnectSemanticMap.cs
// Purpose: THE single source of truth linking each canonical CNC tag to the
//          MTConnect dataItem it is sourced from — in BOTH representations:
//            * StreamElementNames — the PascalCase element local names seen in
//              a /current|/sample stream (what MTConnectStreamParser reads).
//            * ProbeDataItemTypes — the UPPER_SNAKE `type` tokens declared in a
//              /probe document (what the wizard's availability check reads).
//          The stream parser and the /probe availability checker MUST consume
//          this one table so they can never drift — i.e. the wizard can never
//          claim a tag is available that the runtime would not emit
//          (M.2b.4 plan v2 §2, the load-bearing guardrail).
//
//          Scalar/event tags are uniform "is the type present?" lookups and
//          live in Scalar. Alarms (Condition) and axis Positions need bespoke
//          handling and are documented here but discovered separately.
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md §2.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>One canonical tag and the MTConnect dataItem identities it maps from.</summary>
internal sealed record MTConnectSemanticMapping
{
    /// <summary>The canonical tag this mapping produces.</summary>
    public required MTConnectTagMapEntry Tag { get; init; }

    /// <summary>
    /// PascalCase element local names in a /current|/sample stream that source
    /// this tag (e.g. <c>["SpindleSpeed","RotaryVelocity"]</c>). Used by the
    /// stream parser.
    /// </summary>
    public required IReadOnlyList<string> StreamElementNames { get; init; }

    /// <summary>
    /// UPPER_SNAKE dataItem <c>type</c> tokens declared in a /probe document that
    /// source this tag (e.g. <c>["SPINDLE_SPEED","ROTARY_VELOCITY"]</c>). Used by
    /// the wizard's availability checker.
    /// </summary>
    public required IReadOnlyList<string> ProbeDataItemTypes { get; init; }
}

/// <summary>Shared semantic mapping table — consumed by both the stream parser and the probe checker.</summary>
internal static class MTConnectSemanticMap
{
    public static readonly MTConnectSemanticMapping RunState = new()
    { Tag = MTConnectTagMap.RunState, StreamElementNames = ["Execution"], ProbeDataItemTypes = ["EXECUTION"] };

    public static readonly MTConnectSemanticMapping ControllerMode = new()
    { Tag = MTConnectTagMap.ControllerMode, StreamElementNames = ["ControllerMode"], ProbeDataItemTypes = ["CONTROLLER_MODE"] };

    public static readonly MTConnectSemanticMapping EmergencyStop = new()
    { Tag = MTConnectTagMap.EmergencyStop, StreamElementNames = ["EmergencyStop"], ProbeDataItemTypes = ["EMERGENCY_STOP"] };

    public static readonly MTConnectSemanticMapping MainProgram = new()
    { Tag = MTConnectTagMap.MainProgram, StreamElementNames = ["Program"], ProbeDataItemTypes = ["PROGRAM"] };

    // RunningProgram is derived from the same Program dataItem (with an optional
    // SubProgram override), so it is available whenever PROGRAM is present.
    public static readonly MTConnectSemanticMapping RunningProgram = new()
    { Tag = MTConnectTagMap.RunningProgram, StreamElementNames = ["Program", "SubProgram"], ProbeDataItemTypes = ["PROGRAM", "SUB_PROGRAM"] };

    public static readonly MTConnectSemanticMapping SpindleSpeed = new()
    { Tag = MTConnectTagMap.SpindleSpeed, StreamElementNames = ["SpindleSpeed", "RotaryVelocity"], ProbeDataItemTypes = ["SPINDLE_SPEED", "ROTARY_VELOCITY"] };

    public static readonly MTConnectSemanticMapping SpindleLoad = new()
    { Tag = MTConnectTagMap.SpindleLoad, StreamElementNames = ["SpindleLoad", "Load"], ProbeDataItemTypes = ["SPINDLE_LOAD", "LOAD"] };

    public static readonly MTConnectSemanticMapping FeedRate = new()
    { Tag = MTConnectTagMap.FeedRate, StreamElementNames = ["PathFeedrate"], ProbeDataItemTypes = ["PATH_FEEDRATE"] };

    public static readonly MTConnectSemanticMapping PartsCount = new()
    { Tag = MTConnectTagMap.PartsCount, StreamElementNames = ["PartCount"], ProbeDataItemTypes = ["PART_COUNT"] };

    public static readonly MTConnectSemanticMapping CycleTime = new()
    { Tag = MTConnectTagMap.CycleTime, StreamElementNames = ["CycleTime", "ProcessTimer"], ProbeDataItemTypes = ["CYCLE_TIME", "PROCESS_TIMER"] };

    /// <summary>
    /// The uniform scalar/event semantic tags: availability is a simple
    /// "does /probe declare a dataItem whose type is in ProbeDataItemTypes?".
    /// (Alarms and axis Positions are special — see below.)
    /// </summary>
    public static readonly IReadOnlyList<MTConnectSemanticMapping> Scalar =
    [
        RunState, ControllerMode, EmergencyStop, MainProgram, RunningProgram,
        SpindleSpeed, SpindleLoad, FeedRate, PartsCount, CycleTime,
    ];

    /// <summary>
    /// Alarm tags (<c>alarms/count</c>, <c>alarms/first_fault</c>) are emitted
    /// whenever the device declares any <c>category="CONDITION"</c> dataItem —
    /// they aggregate Fault conditions and are not type-specific.
    /// </summary>
    public static readonly IReadOnlyList<MTConnectTagMapEntry> AlarmTags =
    [
        MTConnectTagMap.AlarmCount, MTConnectTagMap.FirstFaultMessage,
    ];

    /// <summary>The /probe dataItem type that sources axis position tags (per Linear/Rotary axis).</summary>
    public const string PositionType = "POSITION";

    /// <summary>The /probe category marking a Condition (alarm-source) dataItem.</summary>
    public const string ConditionCategory = "CONDITION";
}
