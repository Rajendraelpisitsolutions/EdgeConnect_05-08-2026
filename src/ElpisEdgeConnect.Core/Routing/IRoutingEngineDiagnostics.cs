// ============================================================================
// File: Routing/IRoutingEngineDiagnostics.cs
// Purpose: The typed seam through which the routing engine reports lifecycle
//          and operational events. C3 ships only this seam plus the null
//          implementation; the real diagnostics collector lands in C4.
// Reference: ARCHITECTURE_BLUEPRINT.md §13, §19, PHASE1_EXECUTION_PLAN.md C3
//
// LOCKED:
//   - C3 owns this contract; C4 will provide the production implementation
//     against the same shape. C4 must not change the method signatures.
//   - The methods are sync void by design — diagnostics observation must
//     never block the routing engine's hot path. A real implementation
//     can fan out to async sinks internally.
// ============================================================================

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Sink for routing-engine lifecycle and operational events. The C3
/// routing engine constructs with one of these and reports state changes,
/// sink degradations, recoveries, and backpressure drops through it.
/// </summary>
/// <remarks>
/// All methods are intentionally <c>void</c> and synchronous to guarantee
/// the routing engine's hot path is never blocked by an observer. A real
/// implementation in C4 may dispatch to async sinks internally, but it
/// must return promptly from each method.
/// </remarks>
public interface IRoutingEngineDiagnostics
{
    /// <summary>Record a route lifecycle transition.</summary>
    void OnRouteStateChanged(RouteStateChangedEvent evt);

    /// <summary>Record that a sink crossed the retry-exhausted threshold.</summary>
    void OnSinkDegraded(SinkDegradedEvent evt);

    /// <summary>
    /// Record that a previously-degraded sink's first retry succeeded and
    /// it entered the drain phase. Fired before <see cref="OnSinkRecovered"/>,
    /// which fires when the drain finishes.
    /// </summary>
    void OnSinkDraining(SinkDrainingEvent evt);

    /// <summary>Record that a previously-degraded sink began succeeding again.</summary>
    void OnSinkRecovered(SinkRecoveredEvent evt);

    /// <summary>Record a backpressure-driven drop event.</summary>
    void OnBackpressureDropped(BackpressureDroppedEvent evt);

    /// <summary>
    /// Record that the buffer skipped a single point it could not serialize
    /// ("quarantine-and-continue"). Default no-op so existing implementers
    /// (null sink, test doubles) are unaffected; the production collector
    /// overrides it to surface the point on the route's diagnostics.
    /// </summary>
    void OnRoutePointQuarantined(RoutePointQuarantinedEvent evt) { }
}
