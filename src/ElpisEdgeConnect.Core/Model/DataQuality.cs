// ============================================================================
// File: Model/DataQuality.cs
// Purpose: Quality classification for a CanonicalDataPoint value.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.1
// ============================================================================

namespace ElpisEdgeConnect.Core.Model;

/// <summary>
/// Quality classification for a data point value. Follows OPC UA semantics
/// so that OPC UA sinks can map directly without additional translation.
/// </summary>
public enum DataQuality
{
    /// <summary>Quality is not yet determined. Default for partial construction.</summary>
    Unknown = 0,

    /// <summary>Value is known-good and fresh.</summary>
    Good = 1,

    /// <summary>Value is reported but its accuracy cannot be confirmed.</summary>
    Uncertain = 2,

    /// <summary>Value is known-bad (e.g., read failure, device in alarm).</summary>
    Bad = 3,

    /// <summary>Value is valid but older than the staleness threshold.</summary>
    Stale = 4,
}
