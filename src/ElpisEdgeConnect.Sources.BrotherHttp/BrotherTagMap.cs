// ============================================================================
// File: BrotherTagMap.cs
// Purpose: The canonical Brother HTTP tag catalog as code. Locked structural-
//          only surface per v3.1 §5 (user's BrotherTagMap purity concern):
//          BrotherTagMapEntry exposes exactly TagName/TagPath/ValueType/
//          Unit/Description — NO LegacyFieldName, NO value transformations,
//          NO references to CncMachineData or any legacy DTO. Production
//          collectors and the test-side LegacyCanonicalMapper consume the
//          same catalog independently, so discrepancies between protocol-
//          correct behaviour and legacy quirks surface as parity-test
//          failures rather than being silently aligned.
//
// Catalog source: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §6
//                 (with §6.1 ATC slot/tool-number split and §6.4 append-only rule).
//
// Path conventions (FOCAS2 parity):
//   * TagName  — lowercase, slash-delimited (e.g., "machine_info/hostname")
//   * TagPath  — PascalCase, slash-delimited (e.g., "MachineInfo/Hostname")
//   * Templated paths use `{slot}` / `{toolNo}` / `{idx}` placeholders;
//     concrete instances are produced by the static factory methods.
//
// Reference: v3 §5, §6, §6.4; v3.1 §A
// ============================================================================

using System.Collections.Frozen;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Metadata for a single Brother HTTP tag. Structural surface is locked at
/// these five members per v3.1 §5 — any future change that adds a member
/// requires bumping the M.P2.x line per the v3 §6.4 catalog evolution rule
/// AND would fail the structural-purity contract test
/// (<c>BrotherTagMapEntry_ExposesOnlyStructuralMembers</c>).
/// </summary>
internal sealed record BrotherTagMapEntry
{
    /// <summary>Canonical tag name (e.g., <c>"status/state"</c>).</summary>
    public required string TagName { get; init; }

    /// <summary>Hierarchical tag path (e.g., <c>"Status/State"</c>).</summary>
    public required string TagPath { get; init; }

    /// <summary>Expected canonical value type.</summary>
    public required CanonicalValueType ValueType { get; init; }

    /// <summary>Engineering unit, if applicable (e.g. <c>"s"</c>, <c>"h"</c>).</summary>
    public string? Unit { get; init; }

    /// <summary>Human-readable description for Studio wizard + Browse output.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Static registry of canonical Brother HTTP tag paths. Operators select a
/// subset via <c>BrotherHttpSourceConfiguration.DataPoints</c>; empty
/// = collect everything. Prefix matching is supported (<c>"Tools/"</c>
/// matches every <c>"Tools/..."</c> leaf).
/// </summary>
/// <remarks>
/// <para>
/// Catalog is APPEND-ONLY and semantically stable within the M.P2.x line
/// per v3 §6.4. Renaming a path or repurposing its meaning is a breaking
/// change requiring an explicit milestone bump + downstream-consumer
/// migration plan.
/// </para>
/// </remarks>
internal static class BrotherTagMap
{
    // ── MachineInfo (from /HTTPD_MCNINFO) ─────────────────────────────────

    internal static readonly BrotherTagMapEntry MachineInfoHostname = new()
    {
        TagName = "machine_info/hostname", TagPath = "MachineInfo/Hostname",
        ValueType = CanonicalValueType.String,
        Description = "Brother CNC hostname (e.g. BRN68E74A6608EA)",
    };

    internal static readonly BrotherTagMapEntry MachineInfoModel = new()
    {
        TagName = "machine_info/model", TagPath = "MachineInfo/Model",
        ValueType = CanonicalValueType.String,
        Description = "Brother CNC model (e.g. SXd1, S300, R450)",
    };

    internal static readonly BrotherTagMapEntry MachineInfoStatusCode = new()
    {
        TagName = "machine_info/status_code", TagPath = "MachineInfo/StatusCode",
        ValueType = CanonicalValueType.Integer,
        Description = "Raw Brother status code (0=Off, 1=Idle, 2=Hold, 3=Running, 4=Alarm/Notice, 5=Standby)",
    };

    // ── Status (derived from /HTTPD_MCNINFO + alarm filtering) ────────────

    internal static readonly BrotherTagMapEntry StatusState = new()
    {
        TagName = "status/state", TagPath = "Status/State",
        ValueType = CanonicalValueType.String,
        Description = "Derived CNC state (STOP|SUSPEND|OPERATE|ALARM) per the v3 §6.2 precedence chain",
    };

    internal static readonly BrotherTagMapEntry StatusRunning = new()
    {
        TagName = "status/running", TagPath = "Status/Running",
        ValueType = CanonicalValueType.String,
        Description = "Running indicator (Stopped|Idle|Hold|Running|Standby|Unknown)",
    };

    internal static readonly BrotherTagMapEntry StatusMode = new()
    {
        TagName = "status/mode", TagPath = "Status/Mode",
        ValueType = CanonicalValueType.String,
        Description = "CNC mode (Brother always reports MEM)",
    };

    internal static readonly BrotherTagMapEntry StatusAutoMode = new()
    {
        TagName = "status/auto_mode", TagPath = "Status/AutoMode",
        ValueType = CanonicalValueType.String,
        Description = "Auto-mode indicator (Brother always reports MEM)",
    };

    internal static readonly BrotherTagMapEntry StatusEmergencyStop = new()
    {
        TagName = "status/emergency_stop", TagPath = "Status/EmergencyStop",
        ValueType = CanonicalValueType.Boolean,
        Description = "Emergency-stop flag (Brother HTTP doesn't expose this; always false)",
    };

    internal static readonly BrotherTagMapEntry StatusWarning = new()
    {
        TagName = "status/warning", TagPath = "Status/Warning",
        ValueType = CanonicalValueType.String,
        Description = "Operator-visible warning text per v3 §6.2 precedence (maintenance, informational, alarm)",
    };

    // ── Program (from /MNTP_CYCLETIME) ────────────────────────────────────

    internal static readonly BrotherTagMapEntry ProgramActive = new()
    {
        TagName = "program/active", TagPath = "Program/Active",
        ValueType = CanonicalValueType.String,
        Description = "Active program O-number (e.g. O0926)",
    };

    // ── CycleTime (from /MNTP_CYCLETIME) ──────────────────────────────────

    internal static readonly BrotherTagMapEntry CycleTimeCycle = new()
    {
        TagName = "cycle_time/cycle", TagPath = "CycleTime/Cycle",
        ValueType = CanonicalValueType.Double, Unit = "s",
        Description = "Cycle time in seconds",
    };

    internal static readonly BrotherTagMapEntry CycleTimeCutting = new()
    {
        TagName = "cycle_time/cutting", TagPath = "CycleTime/Cutting",
        ValueType = CanonicalValueType.Double, Unit = "s",
        Description = "Cutting time in seconds",
    };

    internal static readonly BrotherTagMapEntry CycleTimeOperation = new()
    {
        TagName = "cycle_time/operation", TagPath = "CycleTime/Operation",
        ValueType = CanonicalValueType.Double, Unit = "h",
        Description = "Operation time in hours (1-dp rounded)",
    };

    internal static readonly BrotherTagMapEntry CycleTimePowerOn = new()
    {
        TagName = "cycle_time/power_on", TagPath = "CycleTime/PowerOn",
        ValueType = CanonicalValueType.Double, Unit = "h",
        Description = "Power-on time in hours (1-dp rounded)",
    };

    internal static readonly BrotherTagMapEntry CycleTimeEndCounter = new()
    {
        TagName = "cycle_time/end_counter", TagPath = "CycleTime/EndCounter",
        ValueType = CanonicalValueType.Integer,
        Description = "Operation end counter",
    };

    internal static readonly BrotherTagMapEntry CycleTimeCuttingRatioPercent = new()
    {
        TagName = "cycle_time/cutting_ratio_percent", TagPath = "CycleTime/CuttingRatioPercent",
        ValueType = CanonicalValueType.Integer, Unit = "%",
        Description = "Cutting time / cycle time as a percentage",
    };

    // ── Production (from /MNTP_WKCNTR) ────────────────────────────────────

    internal static readonly BrotherTagMapEntry ProductionPartsCount = new()
    {
        TagName = "production/parts_count", TagPath = "Production/PartsCount",
        ValueType = CanonicalValueType.Long,
        Description = "First counter's count (legacy PartsCount alias)",
    };

    /// <summary>
    /// Counter slot (1..4) count. Sparse — emitted only when non-zero,
    /// matching legacy <c>AdditionalData["Counter{n}.Count"]</c>.
    /// </summary>
    internal static BrotherTagMapEntry ProductionCounterCount(int counter) => new()
    {
        TagName = $"production/counter{counter}/count",
        TagPath = $"Production/Counter{counter}/Count",
        ValueType = CanonicalValueType.Integer,
        Description = $"Work counter {counter} count (sparse — emitted only when non-zero)",
    };

    /// <summary>
    /// Counter slot (1..4) target. Sparse — emitted only when non-zero,
    /// matching legacy <c>AdditionalData["Counter{n}.Target"]</c>.
    /// </summary>
    internal static BrotherTagMapEntry ProductionCounterTarget(int counter) => new()
    {
        TagName = $"production/counter{counter}/target",
        TagPath = $"Production/Counter{counter}/Target",
        ValueType = CanonicalValueType.Integer,
        Description = $"Work counter {counter} target (sparse — emitted only when non-zero)",
    };

    // ── Tools (from /ATC_TOOLS, split per v3 §6.1) ────────────────────────

    internal static readonly BrotherTagMapEntry ToolsActiveNumber = new()
    {
        TagName = "tools/active_number", TagPath = "Tools/ActiveNumber",
        ValueType = CanonicalValueType.Integer,
        Description = "Currently-loaded tool number (the slot highlighted with #ff8000 in legacy)",
    };

    internal static readonly BrotherTagMapEntry ToolsMagazineSize = new()
    {
        TagName = "tools/magazine_size", TagPath = "Tools/MagazineSize",
        ValueType = CanonicalValueType.Integer,
        Description = "Number of parsed magazine slot entries",
    };

    /// <summary>Magazine slot — tool number in this slot.</summary>
    internal static BrotherTagMapEntry ToolsMagazineNumber(int slot) => new()
    {
        TagName = $"tools/magazine/{slot}/number",
        TagPath = $"Tools/Magazine/{slot}/Number",
        ValueType = CanonicalValueType.Integer,
        Description = $"Tool number occupying magazine slot {slot}",
    };

    /// <summary>Magazine slot — geometry length of the tool.</summary>
    internal static BrotherTagMapEntry ToolsMagazineLength(int slot) => new()
    {
        TagName = $"tools/magazine/{slot}/length",
        TagPath = $"Tools/Magazine/{slot}/Length",
        ValueType = CanonicalValueType.Double,
        Description = $"Geometry length of the tool in magazine slot {slot}",
    };

    /// <summary>Magazine slot — geometry radius of the tool.</summary>
    internal static BrotherTagMapEntry ToolsMagazineRadius(int slot) => new()
    {
        TagName = $"tools/magazine/{slot}/radius",
        TagPath = $"Tools/Magazine/{slot}/Radius",
        ValueType = CanonicalValueType.Double,
        Description = $"Geometry radius of the tool in magazine slot {slot}",
    };

    /// <summary>Magazine slot — whether this slot's tool is the active one.</summary>
    internal static BrotherTagMapEntry ToolsMagazineIsActive(int slot) => new()
    {
        TagName = $"tools/magazine/{slot}/is_active",
        TagPath = $"Tools/Magazine/{slot}/IsActive",
        ValueType = CanonicalValueType.Boolean,
        Description = $"True iff the tool in slot {slot} is currently active",
    };

    /// <summary>Per-tool name (legacy <c>Tool.{toolNo}.Name</c>; sparse).</summary>
    internal static BrotherTagMapEntry ToolsToolName(int toolNo) => new()
    {
        TagName = $"tools/tool/{toolNo}/name",
        TagPath = $"Tools/Tool/{toolNo}/Name",
        ValueType = CanonicalValueType.String,
        Description = $"Tool {toolNo} name (sparse — emitted only when non-empty)",
    };

    /// <summary>Per-tool type (legacy <c>Tool.{toolNo}.Type</c>; sparse).</summary>
    internal static BrotherTagMapEntry ToolsToolType(int toolNo) => new()
    {
        TagName = $"tools/tool/{toolNo}/type",
        TagPath = $"Tools/Tool/{toolNo}/Type",
        ValueType = CanonicalValueType.String,
        Description = $"Tool {toolNo} type (e.g. STD tool; sparse)",
    };

    /// <summary>Per-tool life display (legacy <c>Tool.{toolNo}.Life</c>; sparse).</summary>
    internal static BrotherTagMapEntry ToolsToolLife(int toolNo) => new()
    {
        TagName = $"tools/tool/{toolNo}/life",
        TagPath = $"Tools/Tool/{toolNo}/Life",
        ValueType = CanonicalValueType.String,
        Description = $"Tool {toolNo} life display (sparse — emitted only when set and not '********')",
    };

    // ── Alarms (from /ALARM_CURALMLIST, after informational + maintenance filtering) ──

    internal static readonly BrotherTagMapEntry AlarmsActiveCount = new()
    {
        TagName = "alarms/active_count", TagPath = "Alarms/ActiveCount",
        ValueType = CanonicalValueType.Integer,
        Description = "Count of active alarms after informational + maintenance filtering",
    };

    /// <summary>Active alarm at index — alarm number.</summary>
    internal static BrotherTagMapEntry AlarmsActiveNumber(int idx) => new()
    {
        TagName = $"alarms/active/{idx}/number",
        TagPath = $"Alarms/Active/{idx}/Number",
        ValueType = CanonicalValueType.Integer,
        Description = $"Active alarm at index {idx}: alarm number",
    };

    /// <summary>Active alarm at index — alarm type (always "Brother").</summary>
    internal static BrotherTagMapEntry AlarmsActiveType(int idx) => new()
    {
        TagName = $"alarms/active/{idx}/type",
        TagPath = $"Alarms/Active/{idx}/Type",
        ValueType = CanonicalValueType.String,
        Description = $"Active alarm at index {idx}: type (always 'Brother')",
    };

    /// <summary>Active alarm at index — message text.</summary>
    internal static BrotherTagMapEntry AlarmsActiveMessage(int idx) => new()
    {
        TagName = $"alarms/active/{idx}/message",
        TagPath = $"Alarms/Active/{idx}/Message",
        ValueType = CanonicalValueType.String,
        Description = $"Active alarm at index {idx}: human-readable message",
    };

    // ── Maintenance (from /ALARM_CURALMLIST keyword filter + /MNTP_MAINTNOTICE) ──

    internal static readonly BrotherTagMapEntry MaintenanceWarning = new()
    {
        TagName = "maintenance/warning", TagPath = "Maintenance/Warning",
        ValueType = CanonicalValueType.String,
        Description = "';'-joined maintenance-keyword alarm messages",
    };

    internal static readonly BrotherTagMapEntry MaintenanceWarningCount = new()
    {
        TagName = "maintenance/warning_count", TagPath = "Maintenance/WarningCount",
        ValueType = CanonicalValueType.Integer,
        Description = "Count of maintenance-keyword alarm messages",
    };

    internal static readonly BrotherTagMapEntry MaintenanceNoticeCount = new()
    {
        TagName = "maintenance/notice_count", TagPath = "Maintenance/NoticeCount",
        ValueType = CanonicalValueType.Integer,
        Description = "Count of parsed maintenance notices",
    };

    internal static readonly BrotherTagMapEntry MaintenanceDueSummary = new()
    {
        TagName = "maintenance/due_summary", TagPath = "Maintenance/DueSummary",
        ValueType = CanonicalValueType.String,
        Description = "';'-joined human-readable maintenance summary",
    };

    /// <summary>Maintenance notice at index — description.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeDescription(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/description",
        TagPath = $"Maintenance/Notice/{idx}/Description",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: description (e.g. GREASING XYZ AXIS)",
    };

    /// <summary>Maintenance notice at index — condition.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeCondition(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/condition",
        TagPath = $"Maintenance/Notice/{idx}/Condition",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: condition",
    };

    /// <summary>Maintenance notice at index — status.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeStatus(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/status",
        TagPath = $"Maintenance/Notice/{idx}/Status",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: status (Valid|Invalid)",
    };

    /// <summary>Maintenance notice at index — current value.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeCurrent(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/current",
        TagPath = $"Maintenance/Notice/{idx}/Current",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: current value",
    };

    /// <summary>Maintenance notice at index — limit value.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeLimit(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/limit",
        TagPath = $"Maintenance/Notice/{idx}/Limit",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: limit value",
    };

    /// <summary>Maintenance notice at index — state.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeState(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/state",
        TagPath = $"Maintenance/Notice/{idx}/State",
        ValueType = CanonicalValueType.String,
        Description = $"Maintenance notice {idx}: state (Normal|Warning|Overdue)",
    };

    /// <summary>Maintenance notice at index — due percent.</summary>
    internal static BrotherTagMapEntry MaintenanceNoticeDuePercent(int idx) => new()
    {
        TagName = $"maintenance/notice/{idx}/due_percent",
        TagPath = $"Maintenance/Notice/{idx}/DuePercent",
        ValueType = CanonicalValueType.Integer, Unit = "%",
        Description = $"Maintenance notice {idx}: percentage of limit consumed",
    };

    // ── Static catalog surface ────────────────────────────────────────────

    /// <summary>
    /// Non-templated entries (every <see cref="BrotherTagMapEntry"/> that
    /// is a <c>static readonly</c> field, not a templated factory method).
    /// </summary>
    internal static readonly FrozenSet<BrotherTagMapEntry> StaticTags = new BrotherTagMapEntry[]
    {
        MachineInfoHostname, MachineInfoModel, MachineInfoStatusCode,
        StatusState, StatusRunning, StatusMode, StatusAutoMode, StatusEmergencyStop, StatusWarning,
        ProgramActive,
        CycleTimeCycle, CycleTimeCutting, CycleTimeOperation, CycleTimePowerOn,
        CycleTimeEndCounter, CycleTimeCuttingRatioPercent,
        ProductionPartsCount,
        ToolsActiveNumber, ToolsMagazineSize,
        AlarmsActiveCount,
        MaintenanceWarning, MaintenanceWarningCount, MaintenanceNoticeCount, MaintenanceDueSummary,
    }.ToFrozenSet();

    /// <summary>
    /// Prefix segments under which templated paths live. Operators selecting
    /// any of these as a <c>BrotherHttpSourceConfiguration.DataPoints</c>
    /// entry collect every path that starts with the prefix.
    /// </summary>
    internal static readonly FrozenSet<string> Prefixes = new[]
    {
        "MachineInfo/", "Status/", "Program/", "CycleTime/", "Production/",
        "Tools/", "Tools/Magazine/", "Tools/Tool/",
        "Alarms/", "Alarms/Active/",
        "Maintenance/", "Maintenance/Notice/",
    }.ToFrozenSet();

    /// <summary>
    /// Set of static (non-templated) <see cref="BrotherTagMapEntry.TagPath"/>
    /// values for fast catalog-membership checks by the config validator.
    /// </summary>
    internal static readonly FrozenSet<string> StaticTagPaths =
        BuildStaticTagPaths().ToFrozenSet();

    private static IEnumerable<string> BuildStaticTagPaths()
    {
        foreach (var entry in StaticTags)
        {
            yield return entry.TagPath;
        }
    }

    /// <summary>
    /// Catalog-membership predicate used by the
    /// <c>BrotherHttpSourceConfiguration</c> DataPoints validator.
    /// A path is "known" if it either matches a static catalog entry or
    /// starts with one of the registered prefixes.
    /// </summary>
    /// <param name="path">A normalized DataPoints path entry to check.</param>
    /// <returns><see langword="true"/> if the path is in the catalog.</returns>
    internal static bool IsKnownPathOrPrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Match against static leaf paths (case-insensitive — DataPoints
        // normalization in v3.1 §B.6 lowercases before storing, but the
        // operator may pass the raw form to a one-off API call).
        foreach (var leaf in StaticTagPaths)
        {
            if (string.Equals(leaf, path, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Match against registered prefixes (operator selected a
        // collection — e.g., "Tools/" matches every templated tool path).
        foreach (var prefix in Prefixes)
        {
            if (path.EndsWith('/') &&
                string.Equals(path, prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!path.EndsWith('/') &&
                string.Equals(path + "/", prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
