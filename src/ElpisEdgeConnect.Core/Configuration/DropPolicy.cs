// ============================================================================
// File: Configuration/DropPolicy.cs
// Purpose: Overflow policy for a route's buffer when capacity is exhausted.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.8 (Backpressure)
// Milestone: B1
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// What the routing engine does when a route buffer hits its capacity ceiling.
/// Per blueprint §19.8, sources are never blocked by sinks; the buffer absorbs
/// the mismatch and the drop policy decides what happens at the ceiling.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DropPolicy>))]
public enum DropPolicy
{
    /// <summary>
    /// Evict the oldest buffered points to make room for new ones. Preserves
    /// freshness at the cost of historical completeness. Default.
    /// </summary>
    DropOldest = 0,

    /// <summary>
    /// Reject new points and preserve the existing backlog. Used rarely —
    /// when historical completeness matters more than current values.
    /// </summary>
    DropNewest = 1,

    /// <summary>
    /// Halt source acquisition until buffer drains. Use only when data loss
    /// is worse than source stalling — typically not recommended for edge.
    /// </summary>
    Block = 2,
}
