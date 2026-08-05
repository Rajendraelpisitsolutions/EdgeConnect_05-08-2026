// ============================================================================
// File: Diagnostics/SourceHealthSnapshot.cs
// Purpose: Immutable view of one source's health. Populated via the
//          ISourceHealthSink seam by the host's source supervisor (D1).
//          C4 ships the seam and the data shape; D1 wires the supervisor.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 1.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Source-side rollup. The host pushes lifecycle and observation events
/// into the collector via the source-health sink; this snapshot is the
/// queryable view.
/// </summary>
/// <remarks>
/// A <see cref="SourceHealthSnapshot"/> only materializes after the host's
/// source supervisor has pushed at least one observation — the enclosing
/// <see cref="RouteHealthSnapshot.Source"/> property is <see langword="null"/>
/// until that first push. Callers that receive a non-null snapshot can
/// therefore trust that every <c>required</c> field reflects a real
/// observation, not a default placeholder. If no source supervisor ever
/// pushes (e.g., in a degenerate test that bypasses the host), the
/// snapshot simply never exists; there is no "partially populated" state.
/// </remarks>
public sealed record SourceHealthSnapshot
{
    /// <summary>The source instance id.</summary>
    public required string SourceInstanceId { get; init; }

    /// <summary>The protocol module name (e.g. "focas2", "mtlinki", "mock").</summary>
    public required string ProtocolName { get; init; }

    /// <summary>
    /// Last reported adapter state. Always reflects a real state transition
    /// pushed by the source supervisor — see the type-level remarks on why
    /// this is not nullable and never defaulted.
    /// </summary>
    public required AdapterState State { get; init; }

    /// <summary>Lifetime count of points observed from this source.</summary>
    public required long PointsObserved { get; init; }

    /// <summary>UTC timestamp of the most recent point observation; null if none.</summary>
    public DateTime? LastPointAtUtc { get; init; }

    /// <summary>Most recent error observed; null if the source has never errored.</summary>
    public AdapterError? LastError { get; init; }

    /// <summary>UTC timestamp of <see cref="LastError"/>; null if no error.</summary>
    public DateTime? LastErrorAtUtc { get; init; }
}
