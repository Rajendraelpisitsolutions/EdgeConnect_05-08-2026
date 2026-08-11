// ============================================================================
// File: Diagnostics/SinkEventEntry.cs
// Purpose: Discriminated record for sink lifecycle events captured in the
//          per-sink ring buffer. The three sink event records from
//          Routing/RouteEvents.cs (SinkDegradedEvent / SinkDrainingEvent /
//          SinkRecoveredEvent) don't share a base class, so the collector
//          normalizes them into this single shape for retention.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 2.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Sink-side lifecycle event normalized into a single record. <see cref="Kind"/>
/// is one of the <see cref="SinkEventKind"/> discriminators.
/// </summary>
public sealed record SinkEventEntry
{
    /// <summary>
    /// The sink lifecycle discriminator. Replaced the earlier stringly-typed
    /// value (C4 minor finding #1.C) so callers branch on the enum rather
    /// than comparing strings.
    /// </summary>
    public required SinkEventKind Kind { get; init; }

    /// <summary>The sink instance id this event is for.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>The route id that owns the sink.</summary>
    public required string RouteId { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    /// <summary>Error captured at degradation time, if any.</summary>
    public AdapterError? Error { get; init; }
}
