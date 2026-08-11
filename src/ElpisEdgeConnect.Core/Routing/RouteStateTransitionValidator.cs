// ============================================================================
// File: Routing/RouteStateTransitionValidator.cs
// Purpose: Pure-function validator for route lifecycle transitions per
//          blueprint §19.9. The transition table is the SINGLE SOURCE OF
//          TRUTH for legal state changes — RouteLifecycleManager (in C3
//          commit 2) consults this validator before every transition.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.9
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Stateless validator that decides whether a given route lifecycle
/// transition is permitted by blueprint §19.9.
/// </summary>
/// <remarks>
/// <para>
/// The transition table is encoded as <c>(from → allowed-to-set)</c>. The
/// shape is intentionally a static table rather than scattered if-else
/// logic so the entire lifecycle is auditable in one place.
/// </para>
/// <para>
/// Legal transitions per blueprint §19.9:
/// </para>
/// <list type="bullet">
/// <item><c>Configured → Starting | Stopping | Failed | Blocked</c></item>
/// <item><c>Starting → Running | Failed | Blocked | Stopping</c></item>
/// <item><c>Running → Degraded | Stopping | Failed</c></item>
/// <item><c>Degraded → Draining | Running | Stopping | Failed</c></item>
/// <item><c>Draining → Running | Degraded | Stopping | Failed</c></item>
/// <item><c>Stopping → Stopped | Failed</c></item>
/// <item><c>Stopped → Configured</c> (reserved for the re-init flow; v1 has
///     no UnregisterRoute API on <see cref="IRoutingEngine"/>, so this edge
///     is currently exercised only by tests of the validator table)</item>
/// <item><c>Failed → Stopping</c> (graceful shutdown of a failed route)</item>
/// <item><c>Blocked → Stopping</c> (license updated; host re-registers via the
///     same reserved re-init flow noted above)</item>
/// </list>
/// <para>
/// Identity transitions (e.g., <c>Running → Running</c>) are NOT allowed —
/// the lifecycle manager must short-circuit those before calling the
/// validator. This keeps the table free of identity self-edges.
/// </para>
/// </remarks>
public static class RouteStateTransitionValidator
{
    private static readonly Dictionary<RouteState, IReadOnlySet<RouteState>> Transitions =
        new()
        {
            [RouteState.Configured] = new HashSet<RouteState>
            {
                RouteState.Starting,
                RouteState.Stopping,
                RouteState.Failed,
                RouteState.Blocked,
            },
            [RouteState.Starting] = new HashSet<RouteState>
            {
                RouteState.Running,
                RouteState.Failed,
                RouteState.Blocked,
                RouteState.Stopping,
            },
            [RouteState.Running] = new HashSet<RouteState>
            {
                RouteState.Degraded,
                RouteState.Stopping,
                RouteState.Failed,
            },
            [RouteState.Degraded] = new HashSet<RouteState>
            {
                RouteState.Draining,
                RouteState.Running,
                RouteState.Stopping,
                RouteState.Failed,
            },
            [RouteState.Draining] = new HashSet<RouteState>
            {
                RouteState.Running,
                RouteState.Degraded,
                RouteState.Stopping,
                RouteState.Failed,
            },
            [RouteState.Stopping] = new HashSet<RouteState>
            {
                RouteState.Stopped,
                RouteState.Failed,
            },
            [RouteState.Stopped] = new HashSet<RouteState>
            {
                RouteState.Configured,
            },
            [RouteState.Failed] = new HashSet<RouteState>
            {
                RouteState.Stopping,
            },
            [RouteState.Blocked] = new HashSet<RouteState>
            {
                RouteState.Stopping,
            },
        };

    /// <summary>
    /// True if a route in <paramref name="from"/> may transition to
    /// <paramref name="to"/>. Identity transitions return false.
    /// </summary>
    public static bool IsAllowed(RouteState from, RouteState to)
    {
        if (from == to)
        {
            return false;
        }
        return Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    /// <summary>
    /// Snapshot the allowed transitions out of <paramref name="from"/>.
    /// Used by diagnostics tooling and tests; not on the hot path.
    /// </summary>
    public static IReadOnlySet<RouteState> AllowedFrom(RouteState from) =>
        Transitions.TryGetValue(from, out var allowed)
            ? allowed
            : new HashSet<RouteState>();
}
