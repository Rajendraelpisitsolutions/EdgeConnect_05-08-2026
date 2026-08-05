// ============================================================================
// File: Routing/ReplayRouteContext.cs
// Purpose: The neutral per-route replay context a Route carries when its single sink is
//          replay-aware (K1.3 slice 1). Built by RoutingEngine.RegisterRouteAsync from the
//          activation result at the registration commit boundary; null for an ordinary
//          route. Deliberately holds the protocol-neutral IReplayRouteBuffer capability +
//          the activation's providers + the fixed persisted generation — NOT a concrete
//          buffer type and NOT the K1.2d SqliteRouteStoreHandle directly — so the driver
//          (later slices) depends only on capability seams.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.2-amendment.md
//            §B2 / §C1.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// A registered replay route's context: the replay-capable buffer, its replay-aware sink and
/// stable sink id, the route's FIXED persisted generation (from activation — may be non-zero on
/// reopen; never assumed zero), and the boundary/session-state capture providers returned by
/// activation. Immutable; constructed once at registration.
/// </summary>
/// <param name="Buffer">The replay-capable buffer (tracked append + activation surface).</param>
/// <param name="Sink">The route's single replay-aware sink adapter.</param>
/// <param name="SinkId">The stable configured sink id (== the buffer replay-cursor / persisted replay sink id).</param>
/// <param name="Generation">The route's fixed persisted route-schema generation (cached from activation).</param>
/// <param name="BoundaryProvider">The replay-boundary capture provider (valid as of activation).</param>
/// <param name="SessionStateProvider">The atomic birth/cutover capture provider (valid as of activation).</param>
internal sealed record ReplayRouteContext(
    IReplayRouteBuffer Buffer,
    IReplayAwareSinkAdapter Sink,
    string SinkId,
    RouteSchemaGeneration Generation,
    IReplayBoundaryProvider BoundaryProvider,
    IReplaySessionStateProvider SessionStateProvider);
