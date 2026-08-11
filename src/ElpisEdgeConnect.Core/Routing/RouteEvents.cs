// ============================================================================
// File: Routing/RouteEvents.cs
// Purpose: Event records emitted by the routing engine to its diagnostics
//          seam (IRoutingEngineDiagnostics). C4 will replace the seam with
//          a real implementation; until then, NullRoutingEngineDiagnostics
//          drops them on the floor.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.9, PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Emitted when a route transitions between two
/// <see cref="RouteState"/> values per blueprint §19.9.
/// </summary>
public sealed record RouteStateChangedEvent
{
    /// <summary>The route id whose state changed.</summary>
    public required string RouteId { get; init; }

    /// <summary>State the route was in before the transition.</summary>
    public required RouteState From { get; init; }

    /// <summary>State the route entered.</summary>
    public required RouteState To { get; init; }

    /// <summary>UTC timestamp the transition was committed.</summary>
    public required DateTime ObservedAtUtc { get; init; }

    /// <summary>
    /// Optional human-readable reason ("max retries exhausted",
    /// "license check failed", "graceful shutdown requested").
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Emitted when a sink within a route crosses the retry-exhausted threshold
/// per blueprint §19.4 and is marked degraded.
/// </summary>
public sealed record SinkDegradedEvent
{
    /// <summary>The route id that owns the sink.</summary>
    public required string RouteId { get; init; }

    /// <summary>The sink instance id that became degraded.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>The error that caused the final retry to fail.</summary>
    public required AdapterError LastError { get; init; }

    /// <summary>Number of consecutive retry attempts that failed before degrading.</summary>
    public required int RetryAttempts { get; init; }

    /// <summary>UTC timestamp the sink was marked degraded.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}

/// <summary>
/// Emitted when a previously-degraded sink's first retry succeeds and it
/// enters the drain phase per blueprint §19.5. The sink stays in this
/// phase until its cursor catches up with the buffer tail.
/// </summary>
public sealed record SinkDrainingEvent
{
    /// <summary>The route id that owns the sink.</summary>
    public required string RouteId { get; init; }

    /// <summary>The sink instance id that entered the drain phase.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>UTC timestamp the drain phase was entered.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}

/// <summary>
/// Emitted when a previously-degraded sink starts succeeding again and the
/// replay coordinator begins draining the buffer for it per blueprint §19.5.
/// </summary>
public sealed record SinkRecoveredEvent
{
    /// <summary>The route id that owns the sink.</summary>
    public required string RouteId { get; init; }

    /// <summary>The sink instance id that recovered.</summary>
    public required string SinkInstanceId { get; init; }

    /// <summary>UTC timestamp the recovery was observed.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}

/// <summary>
/// Emitted when the backpressure controller drops one or more points per
/// blueprint §19.8. Carries an aggregated count for the drop event.
/// </summary>
public sealed record BackpressureDroppedEvent
{
    /// <summary>The route id whose buffer reached its drop threshold.</summary>
    public required string RouteId { get; init; }

    /// <summary>Number of points dropped in this event.</summary>
    public required long DroppedCount { get; init; }

    /// <summary>
    /// The drop reason ("channel-overflow", "buffer-capacity",
    /// "buffer-retention"). Free text — diagnostic only.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>UTC timestamp the drop event was observed.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}

/// <summary>
/// Emitted when the buffer skips a single point it could not serialize
/// ("quarantine-and-continue" policy). Distinct from
/// <see cref="BackpressureDroppedEvent"/>: a quarantine is a <b>data-quality</b>
/// signal (a malformed value / unsupported type the wire format cannot
/// represent), not a capacity signal. The rest of the batch is delivered
/// normally — one bad point can never strand a route.
/// </summary>
public sealed record RoutePointQuarantinedEvent
{
    /// <summary>The route whose buffer skipped the point.</summary>
    public required string RouteId { get; init; }

    /// <summary>The canonical tag name of the skipped point.</summary>
    public required string TagName { get; init; }

    /// <summary>
    /// The serialization failure — exception type and message
    /// (e.g. "InvalidDataException: Unsupported metadata value runtime type:
    /// Decimal"). Diagnostic only.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>UTC timestamp the quarantine was observed.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}
