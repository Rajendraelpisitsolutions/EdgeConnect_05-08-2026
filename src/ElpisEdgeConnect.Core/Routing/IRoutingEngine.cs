// ============================================================================
// File: Routing/IRoutingEngine.cs
// Purpose: Public surface of the routing engine. The host calls these methods
//          to register routes, start them, query their lifecycle state, and
//          stop them.
// Reference: ARCHITECTURE_BLUEPRINT.md §19, PHASE1_EXECUTION_PLAN.md C3
// Milestone: C3 Commit 2 (phase 1 — happy path)
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// The per-gateway routing engine. Owns a collection of registered routes,
/// starts and stops each one's worker, and exposes per-route lifecycle
/// state queries. Phase 1 of C3 Commit 2 implements the happy path
/// (register → start → run → stop); retry, replay, lifecycle transitions
/// under failure, and backpressure come in later phases.
/// </summary>
public interface IRoutingEngine
{
    /// <summary>
    /// Register a route. Must be called before the route can be started.
    /// Throws if a route with the same id is already registered.
    /// </summary>
    Task RegisterRouteAsync(RouteDefinition definition, CancellationToken ct);

    /// <summary>Start a previously registered route.</summary>
    Task StartRouteAsync(string routeId, CancellationToken ct);

    /// <summary>Stop a running route cleanly (graceful <see cref="ReplaySessionEndReason.Stop"/>).</summary>
    Task StopRouteAsync(string routeId, CancellationToken ct);

    /// <summary>
    /// Stop a running route cleanly with an EXPLICIT end reason. For a replay-aware route the reason is
    /// handed to the sink's <see cref="IReplayAwareSinkAdapter.EndSessionAsync"/> during shutdown — the
    /// reason is never inferred from the cancellation. Ordinary routes ignore it. K1.3 slice 5.
    /// </summary>
    Task StopRouteAsync(string routeId, ReplaySessionEndReason reason, CancellationToken ct);

    /// <summary>
    /// Stop the route (if running) and unregister it entirely — disposes its
    /// buffer, dispatcher, and worker, and removes it from the engine.
    /// Idempotent on an unknown route id (silent no-op, matching the
    /// tolerance of <c>IConfigurationManager.DiscardDraftAsync</c>).
    /// </summary>
    /// <remarks>
    /// Added for M.P2.2 runtime hot-reload (ADR-0009 Decision 2). The
    /// <c>RuntimeReloadCoordinator</c> calls this when reconciling a
    /// removed route, or when restarting a route whose definition
    /// changed deeply enough to require full re-registration.
    /// </remarks>
    Task UnregisterRouteAsync(string routeId, CancellationToken ct);

    /// <summary>
    /// Stop (with the given end reason) and unregister a route. The config-replace teardown path passes
    /// <see cref="ReplaySessionEndReason.ConfigurationReplaced"/> so a replay-aware sink ends its session
    /// with the correct reason before the route is re-registered. K1.3 slice 5.
    /// </summary>
    Task UnregisterRouteAsync(string routeId, ReplaySessionEndReason reason, CancellationToken ct);

    /// <summary>Start every registered route.</summary>
    Task StartAllAsync(CancellationToken ct);

    /// <summary>Stop every running route.</summary>
    Task StopAllAsync(CancellationToken ct);

    /// <summary>
    /// Current <see cref="RouteState"/> for the given route. Throws
    /// <see cref="System.Collections.Generic.KeyNotFoundException"/> when
    /// the route is not registered.
    /// </summary>
    RouteState GetRouteState(string routeId);

    /// <summary>Ids of all registered routes in registration order.</summary>
    IReadOnlyList<string> RegisteredRouteIds { get; }
}
