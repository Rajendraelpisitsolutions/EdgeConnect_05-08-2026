// ============================================================================
// File: Diagnostics/SinkEventKind.cs
// Purpose: Strongly-typed discriminator for SinkEventEntry. Replaces the
//          earlier stringly-typed "degraded" / "draining" / "recovered"
//          value (C4 minor finding #1.C).
// Reference: ARCHITECTURE_BLUEPRINT.md §13, docs/PHASE2_ENTRY.md carry-forward
// ============================================================================

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Sink lifecycle event discriminator, one-to-one with the three sink
/// event records in <c>Routing/RouteEvents.cs</c>.
/// </summary>
public enum SinkEventKind
{
    /// <summary>Corresponds to <c>SinkDegradedEvent</c>.</summary>
    Degraded = 0,

    /// <summary>Corresponds to <c>SinkDrainingEvent</c>.</summary>
    Draining = 1,

    /// <summary>Corresponds to <c>SinkRecoveredEvent</c>.</summary>
    Recovered = 2,
}
