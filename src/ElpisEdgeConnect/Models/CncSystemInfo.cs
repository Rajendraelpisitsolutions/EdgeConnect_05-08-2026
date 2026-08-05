// ============================================================================
// File: Models/CncSystemInfo.cs
// Purpose: Cached CNC system identity — populated once on first FOCAS2
//          connection via cnc_sysinfo + cnc_rdaxisname, then reused for all
//          subsequent polls. System info only changes if the CNC is
//          reconfigured (which requires a power cycle).
// ============================================================================

namespace ElpisEdgeConnect.Models;

/// <summary>
/// CNC controller identity and capability info.
/// Cached once per connection — does not change during normal operation.
/// </summary>
public sealed class CncSystemInfo
{
    /// <summary>CNC type: "M" = machining center, "T" = lathe, "W" = wire EDM.</summary>
    public string CncType { get; set; } = "";

    /// <summary>Machine tool type from cnc_sysinfo (MT type field).</summary>
    public string MtType { get; set; } = "";

    /// <summary>CNC series: "0iD", "0iF", "30i", "31i", "32i", etc.</summary>
    public string Series { get; set; } = "";

    /// <summary>CNC software version string.</summary>
    public string Version { get; set; } = "";

    /// <summary>Maximum number of controllable axes (from sysinfo MaxAxis field).</summary>
    public int MaxAxes { get; set; }

    /// <summary>Actual number of controlled axes on this machine.</summary>
    public int AxisCount { get; set; }

    /// <summary>Ordered axis names read from cnc_rdaxisname (e.g., ["X","Y","Z","A","B"]).</summary>
    public string[] AxisNames { get; set; } = [];
}
