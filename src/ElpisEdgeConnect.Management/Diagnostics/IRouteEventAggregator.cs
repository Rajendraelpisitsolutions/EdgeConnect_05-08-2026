// ============================================================================
// File: Diagnostics/IRouteEventAggregator.cs
// Purpose: Single seam between Core's specialized event ring buffers
//          (RouteStateChangedEvent / SinkEventEntry /
//          BackpressureDroppedEvent) and the management API's
//          normalized DiagnosticsEventDto wire shape.
//
//          Why this lives in Management, not Core:
//             * severity bucketing is a presentation taxonomy,
//             * event-code naming is a wire-contract concern,
//             * merging + sorting is a UI ergonomic.
//          Putting any of that in Core would couple Core to UI
//          decisions — violating CLAUDE.md locked rule #1.
//
//          Multiple consumers will use this seam:
//             * M.1c.1 /api/v1/routes/{id}/events (route-scoped)
//             * M.1c.2 /api/v1/diagnostics/events (system-wide)
//             * future alerting agent
//             * future EREMOS bridge / cloud federation
//          All of them work off DiagnosticsEventDto, never raw Core
//          event records.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.1
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Aggregates Core's per-dimension event ring buffers into a single
/// time-sorted list of normalized <c>DiagnosticsEventDto</c>s.
/// Implementations are stateless and side-effect free.
/// </summary>
public interface IRouteEventAggregator
{
    /// <summary>
    /// Return the most recent events for one route, merged across
    /// state transitions + per-sink lifecycle + backpressure drops,
    /// sorted descending by <c>OccurredAtUtc</c> (newest first),
    /// capped at <paramref name="limit"/>.
    /// </summary>
    /// <param name="routeId">The route to query.</param>
    /// <param name="limit">Maximum events to return; positive.</param>
    /// <returns>
    /// Empty list if the route is unknown to the diagnostics surface
    /// or has no events yet. Never returns null.
    /// </returns>
    IReadOnlyList<Contracts.DiagnosticsEventDto> GetRecentRouteEvents(string routeId, int limit);
}
