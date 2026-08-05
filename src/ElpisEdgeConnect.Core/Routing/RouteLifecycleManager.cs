// ============================================================================
// File: Routing/RouteLifecycleManager.cs
// Purpose: Centralized lifecycle state machine for ONE route. All route
//          state transitions in the runtime must go through this class.
//          Owns the current RouteState, enforces the legal transition table
//          via RouteStateTransitionValidator, and emits exactly one
//          RouteStateChangedEvent per real transition.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.9 (Lifecycle), PHASE1_EXECUTION_PLAN.md C3.
// Milestone: C3 Commit 2 (phase 5 — route lifecycle).
//
// LOCKED design rules (phase 5 guardrails):
//   * There is exactly ONE validator table (RouteStateTransitionValidator).
//   * There is exactly ONE lifecycle manager per route.
//   * RouteWorker, SinkPublisher, and RoutingEngine NEVER decide a route
//     state on their own. They call TryTransitionTo / NotifySink* and the
//     manager decides whether (and how) to transition.
//   * Idempotent calls (target == current state) are silent no-ops: they
//     return false and do NOT re-emit a state-changed event.
//   * Illegal transitions throw InvalidOperationException with the
//     CORE.ROUTE_INVALID_LIFECYCLE_TRANSITION error code. Invalid
//     transitions are bugs, not runtime conditions — fail loudly.
//   * Sink-driven transitions (Running ↔ Degraded ↔ Draining) are derived
//     from per-sink notifications, not invented by the sinks themselves.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// The single authority on route lifecycle state for a given route.
/// Constructed with the starting state (always <see cref="RouteState.Configured"/>
/// in production) and a diagnostics sink for state-change event emission.
/// </summary>
public sealed class RouteLifecycleManager
{
    private readonly string _routeId;
    private readonly IRoutingEngineDiagnostics _diagnostics;
    private readonly object _gate = new();

    // Sink health tracking for derived Running ↔ Degraded ↔ Draining
    // transitions. Sink ids are added/removed as the SinkPublisher emits
    // its degraded/draining/recovered notifications.
    private readonly HashSet<string> _degradedSinks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _drainingSinks = new(StringComparer.Ordinal);

    private RouteState _state;

    /// <summary>
    /// Construct a lifecycle manager in the <see cref="RouteState.Configured"/>
    /// state.
    /// </summary>
    public RouteLifecycleManager(string routeId, IRoutingEngineDiagnostics diagnostics)
        : this(routeId, diagnostics, RouteState.Configured)
    {
    }

    /// <summary>
    /// Test-only constructor that seeds an initial state directly. Used by
    /// unit tests to enumerate the validator table without walking every
    /// legal transition first. Do NOT call from production code.
    /// </summary>
    internal RouteLifecycleManager(
        string routeId,
        IRoutingEngineDiagnostics diagnostics,
        RouteState initialState)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _routeId = routeId;
        _diagnostics = diagnostics;
        _state = initialState;
    }

    /// <summary>The route's current state.</summary>
    public RouteState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <summary>
    /// Attempt a transition to <paramref name="target"/>. Returns
    /// <c>true</c> if a real transition occurred (a
    /// <see cref="RouteStateChangedEvent"/> was emitted). Returns
    /// <c>false</c> if the current state already equals
    /// <paramref name="target"/> (idempotent no-op; no event). Throws
    /// <see cref="InvalidOperationException"/> with the
    /// <c>CORE.ROUTE_INVALID_LIFECYCLE_TRANSITION</c> error code when the
    /// transition is rejected by
    /// <see cref="RouteStateTransitionValidator"/>.
    /// </summary>
    public bool TryTransitionTo(RouteState target, string? reason = null)
    {
        lock (_gate)
        {
            return TryTransitionToLocked(target, reason);
        }
    }

    /// <summary>
    /// Record that a sink within the route has become degraded (publish
    /// failure). If the route is currently <see cref="RouteState.Running"/>
    /// or <see cref="RouteState.Draining"/>, the route transitions to
    /// <see cref="RouteState.Degraded"/>. The Draining→Degraded path covers
    /// the "sink fails again mid-drain" case (e.g. network blip during
    /// recovery). No-op when the route is already in Degraded or any
    /// terminal state.
    /// </summary>
    public void NotifySinkDegraded(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        lock (_gate)
        {
            _drainingSinks.Remove(sinkInstanceId);
            _degradedSinks.Add(sinkInstanceId);

            if (_state == RouteState.Running || _state == RouteState.Draining)
            {
                TryTransitionToLocked(
                    RouteState.Degraded,
                    $"sink {sinkInstanceId} degraded");
            }
        }
    }

    /// <summary>
    /// Record that a sink has entered the drain phase (first successful
    /// publish after degradation). If the route is currently
    /// <see cref="RouteState.Degraded"/>, it transitions to
    /// <see cref="RouteState.Draining"/>.
    /// </summary>
    public void NotifySinkDraining(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        lock (_gate)
        {
            _degradedSinks.Remove(sinkInstanceId);
            _drainingSinks.Add(sinkInstanceId);

            // The route advances to Draining ONLY when every previously
            // degraded sink is at least draining. While any sink is still
            // in the failing state the route stays Degraded (the "worst"
            // state wins).
            if (_state == RouteState.Degraded && _degradedSinks.Count == 0)
            {
                TryTransitionToLocked(
                    RouteState.Draining,
                    $"sink {sinkInstanceId} draining; all degraded sinks now draining");
            }
        }
    }

    /// <summary>
    /// Record that a sink has fully recovered (drain completed). When no
    /// sinks remain degraded or draining, the route transitions back to
    /// <see cref="RouteState.Running"/> from Degraded or Draining.
    /// </summary>
    public void NotifySinkRecovered(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        lock (_gate)
        {
            _degradedSinks.Remove(sinkInstanceId);
            _drainingSinks.Remove(sinkInstanceId);

            if (_degradedSinks.Count == 0 &&
                _drainingSinks.Count == 0 &&
                (_state == RouteState.Draining || _state == RouteState.Degraded))
            {
                TryTransitionToLocked(
                    RouteState.Running,
                    $"sink {sinkInstanceId} recovered; all sinks healthy");
            }
        }
    }

    private bool TryTransitionToLocked(RouteState target, string? reason)
    {
        if (_state == target)
        {
            // Idempotent no-op. Do NOT emit a duplicate event.
            return false;
        }

        if (!RouteStateTransitionValidator.IsAllowed(_state, target))
        {
            throw new InvalidOperationException(
                $"[{CoreErrors.RouteInvalidLifecycleTransition}] Route '{_routeId}' " +
                $"cannot transition from {_state} to {target}.");
        }

        var from = _state;
        _state = target;
        _diagnostics.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = _routeId,
            From = from,
            To = target,
            ObservedAtUtc = DateTime.UtcNow,
            Reason = reason,
        });
        return true;
    }
}
