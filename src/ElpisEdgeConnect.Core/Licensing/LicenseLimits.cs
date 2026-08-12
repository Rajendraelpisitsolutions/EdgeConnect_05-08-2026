// ============================================================================
// File: Licensing/LicenseLimits.cs
// Purpose: Global instance limits from the license payload.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Global ceilings on the number of source instances, sink instances, and
/// routes a single gateway may run regardless of per-module limits.
/// </summary>
public sealed record LicenseLimits
{
    /// <summary>Maximum total source instances allowed.</summary>
    public required int MaxSourceInstances { get; init; }

    /// <summary>Maximum total sink instances allowed.</summary>
    public required int MaxSinkInstances { get; init; }

    /// <summary>Maximum total routes allowed.</summary>
    public required int MaxRoutes { get; init; }
}
