// ============================================================================
// File: Routing/NullRoutingEngineDiagnostics.cs
// Purpose: No-op IRoutingEngineDiagnostics. Default for tests and for any
//          host that has not yet wired the C4 collector.
// Reference: PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// No-op <see cref="IRoutingEngineDiagnostics"/>. The routing engine
/// constructs with this implementation by default; the host can swap in a
/// real collector via the constructor when C4 lands.
/// </summary>
public sealed class NullRoutingEngineDiagnostics : IRoutingEngineDiagnostics
{
    /// <summary>Shared singleton.</summary>
    public static NullRoutingEngineDiagnostics Instance { get; } = new();

    private NullRoutingEngineDiagnostics() { }

    /// <inheritdoc/>
    public void OnRouteStateChanged(RouteStateChangedEvent evt) { /* intentionally empty */ }

    /// <inheritdoc/>
    public void OnSinkDegraded(SinkDegradedEvent evt) { /* intentionally empty */ }

    /// <inheritdoc/>
    public void OnSinkDraining(SinkDrainingEvent evt) { /* intentionally empty */ }

    /// <inheritdoc/>
    public void OnSinkRecovered(SinkRecoveredEvent evt) { /* intentionally empty */ }

    /// <inheritdoc/>
    public void OnBackpressureDropped(BackpressureDroppedEvent evt) { /* intentionally empty */ }
}
