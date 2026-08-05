// ============================================================================
// File: Diagnostics/RouteHealthSnapshot.cs
// Purpose: Top-level immutable rollup of one route's three-way diagnostics:
//          source / pipeline / sink. Returned by IDiagnosticsService.
//          Always populated from a single locked snapshot in the collector
//          so the three sub-views are internally consistent.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.1 (Three-way diagnostics)
// Milestone: C4 — phase 1.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Atomic rollup of one route's diagnostics across all three dimensions
/// (source, pipeline, sink). Single-source-of-truth: no field is computed
/// from a parallel store; everything is derived from the same locked
/// region of the collector at <see cref="ObservedAtUtc"/>.
/// </summary>
public sealed record RouteHealthSnapshot
{
    /// <summary>The route id this snapshot is for.</summary>
    public required string RouteId { get; init; }

    /// <summary>UTC time at which the snapshot was assembled.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    /// <summary>Current route lifecycle state.</summary>
    public required Routing.RouteState State { get; init; }

    /// <summary>Lifetime count of route lifecycle transitions observed.</summary>
    public required long StateTransitionCount { get; init; }

    /// <summary>Lifetime count of points dropped by the backpressure controller.</summary>
    public required long BackpressureDropCount { get; init; }

    /// <summary>
    /// Lifetime count of points QUARANTINED — skipped because the buffer could
    /// not serialize them (a data-quality signal, distinct from a backpressure
    /// drop). Non-zero means an upstream adapter/mapping emitted values that
    /// cannot be represented on the wire; the rest of the route's data still
    /// flows. Defaults to 0 for snapshots assembled before this field existed.
    /// </summary>
    public long QuarantinedPointCount { get; init; }

    /// <summary>Source-side rollup, or null when no source health has been reported yet.</summary>
    public SourceHealthSnapshot? Source { get; init; }

    /// <summary>Pipeline-side rollup.</summary>
    public required PipelineHealthSnapshot Pipeline { get; init; }

    /// <summary>Sink-side rollups, one per registered sink on the route.</summary>
    public required IReadOnlyList<SinkHealthSnapshot> Sinks { get; init; }

    /// <summary>
    /// Per-route buffer rollup, or null when no buffer stats have been
    /// reported yet. Populated by the routing engine on every poll cycle
    /// from <c>IMessageBuffer.GetStatsAsync</c>; carries depth, oldest-
    /// unacked age, lifetime enqueue/drain/drop counters, and the sink
    /// (if any) currently pinning the tail. Surfaces on the metrics
    /// endpoint and the soak runner's per-minute heartbeat.
    /// </summary>
    public BufferHealthSnapshot? Buffer { get; init; }
}
