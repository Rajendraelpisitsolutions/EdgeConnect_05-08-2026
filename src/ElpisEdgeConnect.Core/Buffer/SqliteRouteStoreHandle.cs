// ============================================================================
// File: Buffer/SqliteRouteStoreHandle.cs
// Purpose: The façade-anchored replay-capability handle for a route's SQLite store
//          (K1.2d — R12 step 4). Bundles the SqliteBuffer FAÇADE (the IMessageBuffer the
//          route already holds) with the two optional replay capabilities implemented by
//          the wrapped SqliteRouteStore owner. There is ONE owner behind all three slots —
//          one writer mutex, one <db>.lock, one connection set — and ONE disposal authority
//          (the façade's IMessageBuffer lifecycle disposes the owner once). The provider
//          slots are non-null IFF replay-state tracking is enabled at the moment the handle
//          is taken; the record is immutable, so a handle taken before activation keeps null
//          providers forever.
//
// IMPORTANT (v3 §R4): a replay route must obtain this handle through the façade/factory —
// NOT by opening SqliteRouteStore directly — so it keeps the factory's legacy-buffer
// migration + quarantine→diagnostics wiring. The factory extension that hands a route the
// (façade, handle) pair is the K1.2d↔K1.3 seam and is designed in K1.3, not here.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R4/§R5/§R12 step 4.
// ============================================================================

using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// A post-activation snapshot of a route store's capabilities: the durable buffer façade
/// plus the optional replay capabilities. The provider slots are both non-null when
/// replay-state tracking is enabled and both null otherwise — a
/// <see cref="SqliteBuffer.GetCapabilityHandle"/> reads the enabled state once so it can
/// never return one provider null and the other non-null.
/// </summary>
/// <param name="Buffer">The durable buffer façade the route uses for all <see cref="IMessageBuffer"/> operations.</param>
/// <param name="ReplayBoundaryProvider">The boundary-capture capability (the same owner the façade wraps), or null when tracking is disabled.</param>
/// <param name="ReplaySessionStateProvider">The atomic birth/cutover-capture capability (the same owner), or null when tracking is disabled.</param>
internal sealed record SqliteRouteStoreHandle(
    IMessageBuffer Buffer,
    IReplayBoundaryProvider? ReplayBoundaryProvider,
    IReplaySessionStateProvider? ReplaySessionStateProvider);
