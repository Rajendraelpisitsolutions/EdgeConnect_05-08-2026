// ============================================================================
// File: Models/ToolInfoData.cs
// Purpose: Tool information model — current tool, offsets, and tool life data.
//          Populated from FOCAS2 cnc_modal, cnc_rdtofs, cnc_rdtlgrp calls.
//
// FOCAS2 ADVANTAGE:
//   MT-LINKi requires a separate (paid) license for tool data.
//   FOCAS2 provides full tool information at no additional cost — this gives
//   customers direct access to tool offsets, life counters, and status
//   that would otherwise require upgrading their MT-LINKi license.
//
// TOOL OFFSET MEMORY TYPES (Fanuc CNC):
//   Type A: Single offset per tool (combined geometry+wear)
//   Type B: Separate geometry and wear offsets per tool
//   Type C: Separate geometry/wear with length and radius split
//
// TOOL LIFE MANAGEMENT:
//   Optional CNC feature — tracks usage (count or minutes) per tool group.
//   Each group can hold multiple tools; when one expires, the next is selected.
//   Not all CNC configurations enable this feature.
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Models;

/// <summary>
/// Complete tool information from the CNC controller.
/// Published as structured MQTT tags for EREMOS V2 integration.
/// </summary>
public sealed class ToolInfoData
{
    /// <summary>Currently active tool number (T-code).</summary>
    public int CurrentToolNumber { get; set; }

    /// <summary>Total number of tool offset sets available on this CNC.</summary>
    public int OffsetCount { get; set; }

    /// <summary>
    /// Tool offset memory type: "A", "B", or "C".
    ///   A = Single offset per tool
    ///   B = Separate geometry and wear offsets
    ///   C = Separate geometry/wear with length/radius split
    /// </summary>
    public string OffsetMemoryType { get; set; } = "A";

    /// <summary>
    /// All tool offsets with non-zero values.
    /// Only tools that have at least one non-zero offset are included
    /// to keep the payload compact.
    /// </summary>
    public List<ToolOffsetEntry> Offsets { get; set; } = new();

    /// <summary>Whether tool life management is enabled on this CNC.</summary>
    public bool ToolLifeEnabled { get; set; }

    /// <summary>
    /// Tool life group data (only populated if ToolLifeEnabled = true).
    /// Each group tracks usage of one or more tools.
    /// </summary>
    public List<ToolLifeEntry> LifeGroups { get; set; } = new();
}

/// <summary>
/// Offset data for a single tool.
/// Values are in machine units (mm or inches) depending on CNC configuration.
///
/// For Memory Type A: only WearLength is populated (single combined offset).
/// For Memory Type B: WearLength and GeometryLength are populated.
/// For Memory Type C: All four fields (WearLength, WearRadius, GeometryLength, GeometryRadius).
/// </summary>
public sealed class ToolOffsetEntry
{
    /// <summary>Tool offset number (1-based).</summary>
    public int Number { get; set; }

    /// <summary>Wear compensation value (or combined offset for Type A). In machine units.</summary>
    public double WearLength { get; set; }

    /// <summary>Wear radius compensation. Only for Memory Type C. In machine units.</summary>
    public double WearRadius { get; set; }

    /// <summary>Geometry (shape) compensation value. Only for Memory Type B/C. In machine units.</summary>
    public double GeometryLength { get; set; }

    /// <summary>Geometry radius compensation. Only for Memory Type C. In machine units.</summary>
    public double GeometryRadius { get; set; }

    /// <summary>True if this is the currently active tool offset.</summary>
    public bool IsActive { get; set; }

    /// <summary>Returns true if any offset value is non-zero.</summary>
    [JsonIgnore]
    public bool HasData => WearLength != 0 || WearRadius != 0 ||
                           GeometryLength != 0 || GeometryRadius != 0;
}

/// <summary>
/// Tool life management data for a single tool group.
///
/// Tool life management allows automatic tool changes when a tool's life
/// (measured in usage count or minutes) reaches the preset limit.
/// Each group can contain multiple tools — when the active tool expires,
/// the CNC automatically switches to the next tool in the group.
/// </summary>
public sealed class ToolLifeEntry
{
    /// <summary>Tool life group number.</summary>
    public int GroupNo { get; set; }

    /// <summary>Tool number within the group.</summary>
    public int ToolNo { get; set; }

    /// <summary>Current life counter value (count or minutes used).</summary>
    public int LifeCounter { get; set; }

    /// <summary>Life limit — tool is considered expired when counter reaches this.</summary>
    public int LifeLimit { get; set; }

    /// <summary>Counter type: "Count" (number of uses) or "Minutes" (cutting time).</summary>
    public string CountType { get; set; } = "Count";

    /// <summary>
    /// Tool status within the group.
    ///   "Active"  — Currently in use
    ///   "Standby" — Ready to be used (next in line)
    ///   "Expired" — Life counter reached limit
    ///   "Skipped" — Manually skipped by operator
    /// </summary>
    public string Status { get; set; } = "Standby";

    /// <summary>Life remaining as percentage (0-100).</summary>
    [JsonIgnore]
    public double LifeRemainingPercent => LifeLimit > 0
        ? Math.Max(0, (1.0 - (double)LifeCounter / LifeLimit) * 100.0)
        : 100.0;
}
