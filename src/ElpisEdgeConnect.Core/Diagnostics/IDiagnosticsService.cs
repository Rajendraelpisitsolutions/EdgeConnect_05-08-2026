// ============================================================================
// File: Diagnostics/IDiagnosticsService.cs
// Purpose: The public query surface for the diagnostics collector. This
//          is a THIN typed façade over the single collector state store:
//          every method is implemented directly on
//          RuntimeDiagnosticsCollector and reads live from the same
//          locked region used by the routing/pipeline/source/sink push
//          paths. NO parallel cache. NO second read model.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 4.
//
// LOCKED design rules (phase 4 guardrails):
//   * No HTTP endpoint. This interface is consumed in-process; the
//     management API in Phase 4 of the overall project will wrap it.
//   * All return types are immutable records or read-only views.
//   * Unknown-route / unknown-sink queries return null — never throw,
//     never return a synthesized empty snapshot.
//   * Implementations MUST be the same object that owns the state
//     (RuntimeDiagnosticsCollector). A wrapper adapter is not permitted
//     because it would be a second read model.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Read-only query surface over the diagnostics collector. Returns
/// immutable snapshot records built live from the single collector state
/// store. Returns <c>null</c> for unknown-route / unknown-sink queries
/// (documented below per method).
/// </summary>
public interface IDiagnosticsService
{
    /// <summary>
    /// Ids of every route the collector has observed events for, in no
    /// particular order. Returns a fresh array on every call — safe to
    /// iterate without holding any lock.
    /// </summary>
    IReadOnlyList<string> GetKnownRoutes();

    /// <summary>
    /// Atomic three-way snapshot (source / pipeline / sink) of one route.
    /// Returns <c>null</c> when <paramref name="routeId"/> is unknown to
    /// the collector.
    /// </summary>
    RouteHealthSnapshot? GetRouteSnapshot(string routeId);

    /// <summary>
    /// Atomic snapshots of every known route. Convenience over
    /// <see cref="GetKnownRoutes"/> + <see cref="GetRouteSnapshot"/>.
    /// Never returns null; returns an empty list when no routes are known.
    /// </summary>
    IReadOnlyList<RouteHealthSnapshot> GetAllRouteSnapshots();

    /// <summary>
    /// Recent route lifecycle transitions for <paramref name="routeId"/>.
    /// Exposes the ring buffer's live entries together with lifetime added
    /// / dropped counters. Returns <c>null</c> when the route is unknown.
    /// </summary>
    BoundedEventLogSnapshot<RouteStateChangedEvent>? GetRouteStateEvents(string routeId);

    /// <summary>
    /// Recent sink lifecycle events (degraded / draining / recovered) for
    /// the given route + sink. Returns <c>null</c> when either the route
    /// or the sink is unknown — callers distinguish "no events yet" from
    /// "no such sink" via the null return.
    /// </summary>
    BoundedEventLogSnapshot<SinkEventEntry>? GetSinkEvents(string routeId, string sinkInstanceId);

    /// <summary>
    /// Recent backpressure drop events for <paramref name="routeId"/>.
    /// Returns <c>null</c> when the route is unknown.
    /// </summary>
    BoundedEventLogSnapshot<BackpressureDroppedEvent>? GetBackpressureEvents(string routeId);

    /// <summary>
    /// Recent point-quarantine events for <paramref name="routeId"/> — points
    /// the buffer could not serialize and skipped rather than failing the
    /// enqueue. Returns <c>null</c> when the route is unknown.
    /// </summary>
    BoundedEventLogSnapshot<RoutePointQuarantinedEvent>? GetQuarantineEvents(string routeId);
}
