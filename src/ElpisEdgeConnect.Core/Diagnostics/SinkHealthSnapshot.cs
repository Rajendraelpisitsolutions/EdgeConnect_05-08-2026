// ============================================================================
// File: Diagnostics/SinkHealthSnapshot.cs
// Purpose: Immutable point-in-time view of one sink's health, returned by
//          the diagnostics query surface.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 1.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Sink-side rollup populated by the routing engine and the host's sink
/// supervisor. All fields are populated from a single locked snapshot in
/// the collector to guarantee internal consistency.
/// </summary>
public sealed record SinkHealthSnapshot
{
    /// <summary>The sink instance id.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>The route id this sink belongs to.</summary>
    public required string RouteId { get; init; }

    /// <summary>True when the sink last reported a publish failure and has not yet recovered.</summary>
    public required bool IsDegraded { get; init; }

    /// <summary>True between SinkDraining and SinkRecovered events (i.e. drain phase).</summary>
    public required bool IsDraining { get; init; }

    /// <summary>Lifetime count of times this sink entered the degraded state.</summary>
    public required long DegradationEventCount { get; init; }

    /// <summary>Lifetime count of times this sink fully recovered (drain complete).</summary>
    public required long RecoveryEventCount { get; init; }

    /// <summary>Most recent publish error observed; null if the sink has never failed.</summary>
    public AdapterError? LastError { get; init; }

    /// <summary>UTC timestamp of <see cref="LastError"/>; null if there is no error.</summary>
    public DateTime? LastErrorAtUtc { get; init; }

    /// <summary>
    /// Adapter-level state reported via <see cref="ISinkHealthSink"/>; null
    /// when the host supervisor has not yet reported any state for this
    /// sink. Distinct from <see cref="IsDegraded"/>, which is a publish-
    /// level flag.
    /// </summary>
    public AdapterState? AdapterState { get; init; }

    /// <summary>
    /// Snapshot of the sink's currently-active client sessions, reported
    /// via <see cref="ISinkSessionHealthSink"/>. Null when the sink does
    /// not implement session tracking (most sinks) or the poller has not
    /// yet observed it. An empty list means the sink is tracked but no
    /// clients are connected.
    /// </summary>
    public IReadOnlyList<SinkSessionSummary>? ActiveSessions { get; init; }
}
