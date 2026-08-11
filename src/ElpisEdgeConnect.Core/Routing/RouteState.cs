// ============================================================================
// File: Routing/RouteState.cs
// Purpose: The 9 lifecycle states a route can be in.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.9
//
// LOCKED: this enum is the source of truth for the lifecycle. Adding or
// removing a member is an architectural change and requires blueprint
// revision (§19.9 update).
// ============================================================================

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Route lifecycle state per blueprint §19.9. Every route is in exactly
/// one of these states at any moment. Transitions are validated by
/// <see cref="RouteStateTransitionValidator"/>.
/// </summary>
public enum RouteState
{
    /// <summary>Route exists in config but has not been started.</summary>
    Configured = 0,

    /// <summary>Route is initializing (validating config, opening buffer, connecting sinks).</summary>
    Starting = 1,

    /// <summary>Route is processing data on the hot path. All sinks healthy.</summary>
    Running = 2,

    /// <summary>One or more sinks recovered, the route is draining buffered backlog before resuming hot path.</summary>
    Draining = 3,

    /// <summary>At least one sink is failing; the buffer is growing; data is preserved.</summary>
    Degraded = 4,

    /// <summary>Route is shutting down — graceful flush in progress.</summary>
    Stopping = 5,

    /// <summary>Route has stopped. The buffer file is retained for the next start.</summary>
    Stopped = 6,

    /// <summary>Route cannot run due to a config error or unrecoverable failure. Manual intervention required.</summary>
    Failed = 7,

    /// <summary>
    /// Route references an unlicensed or disabled module (e.g., a sink whose
    /// protocol module is not enabled by the active license). Terminal until
    /// the license is updated and the route is re-registered.
    /// </summary>
    Blocked = 8,
}
