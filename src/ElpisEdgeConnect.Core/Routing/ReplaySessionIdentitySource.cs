// ============================================================================
// File: Routing/ReplaySessionIdentitySource.cs
// Purpose: Process-wide monotonic source of ReplaySessionId for replay routes (K1.3).
//          Owned by the RoutingEngine (one per engine/runtime) and handed to each replay
//          driver, so every driver start — including across a route stop→start OR an
//          unregister → re-register (configuration replacement, which creates a NEW Route
//          object) — mints a fresh, globally-unique session id. A delayed lifecycle
//          callback from a previous session can therefore always be distinguished; a
//          per-Route counter would reset when the Route object is replaced.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5 slice 3;
//            K1.1 session-identity contract (ReplaySessionIdentifiers).
// ============================================================================

using System.Threading;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Mints monotonically-increasing, unique <see cref="ReplaySessionId"/> values for a runtime.
/// In-memory: a fresh process has a fresh sink (no callbacks survive sink teardown), so
/// cross-process persistence is not required for stale-callback isolation.
/// </summary>
internal sealed class ReplaySessionIdentitySource
{
    private long _next;

    /// <summary>Mint the next unique, monotonically-increasing session id.</summary>
    public ReplaySessionId Next() => ReplaySessionId.Create(Interlocked.Increment(ref _next));
}
