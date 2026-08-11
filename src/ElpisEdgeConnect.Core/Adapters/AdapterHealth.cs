// ============================================================================
// File: Adapters/AdapterHealth.cs
// Purpose: Point-in-time health snapshot returned by an adapter.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 9
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Point-in-time health snapshot returned by
/// <see cref="ISourceAdapter.CheckHealthAsync"/> or
/// <see cref="ISinkAdapter.CheckHealthAsync"/>.
/// </summary>
public sealed record AdapterHealth
{
    /// <summary>Lifecycle state at the moment of the check.</summary>
    public required AdapterState State { get; init; }

    /// <summary>Overall health evaluation (Healthy, Degraded, Unhealthy).</summary>
    public required HealthLevel Level { get; init; }

    /// <summary>UTC timestamp of this health check.</summary>
    public required DateTime CheckedAt { get; init; }

    /// <summary>Timestamp of the last successful operation, if any.</summary>
    public DateTime? LastSuccessAt { get; init; }

    /// <summary>The most recent error, if the adapter has seen one.</summary>
    public AdapterError? LastError { get; init; }

    /// <summary>
    /// Protocol-specific health metrics. Should be JSON-serializable primitives.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metrics { get; init; }

    /// <summary>Optional free-text explanation of the current state.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Coarse health level used in dashboards and rollups.
/// </summary>
public enum HealthLevel
{
    /// <summary>Health cannot be determined (e.g., adapter not yet initialized).</summary>
    Unknown = 0,

    /// <summary>Adapter is operating normally with no observed issues.</summary>
    Healthy = 1,

    /// <summary>Adapter is operating but experiencing recoverable issues (retries in progress, elevated error rate).</summary>
    Degraded = 2,

    /// <summary>Adapter is unable to operate correctly and requires intervention.</summary>
    Unhealthy = 3,
}
