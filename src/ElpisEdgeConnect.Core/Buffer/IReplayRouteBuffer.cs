// ============================================================================
// File: Buffer/IReplayRouteBuffer.cs
// Purpose: Optional internal capability a route buffer MAY implement to support a
//          replay-aware sink (K1.3 slice 1). A replay route obtains this capability by
//          testing the buffer the factory returned (`buffer as IReplayRouteBuffer`) —
//          NEVER by casting to a concrete buffer type — so replay support is
//          capability-gated, not implementation-gated, and the factory stays the sole
//          construction path. SqliteBuffer implements both IMessageBuffer and this.
//
// Lifecycle honesty (K1.2d snapshot semantics): the replay-capture providers do NOT
// exist as a promise before activation — a fresh store has null provider slots. So the
// providers are RETURNED FROM ActivateReplayAsync (in ReplayRouteActivation), never
// exposed as property getters that would have to throw before activation. IsReplay
// TrackingEnabled reads the owner's already-loaded state (no second connection / query)
// so registration can fail closed on an enabled store configured as a legacy route.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.2-amendment.md
//            §B2; v3 §R5 / v3.1 §A3.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// The optional replay capability of a route buffer: activate replay-state tracking, append
/// on the tracked (manifest-maintaining) path at a fixed generation, and — before that —
/// report whether tracking is already enabled so an ordinary route cannot silently downgrade
/// an enabled store. NOT part of the locked <see cref="IMessageBuffer"/> contract.
/// </summary>
internal interface IReplayRouteBuffer
{
    /// <summary>
    /// Whether the underlying store already has replay-state tracking enabled. A read of the
    /// owner's already-loaded state — it must NOT open a second connection or issue a
    /// side-channel query. Used at registration to fail closed on an enabled store configured
    /// with an ordinary (non-replay) sink (no automatic downgrade to the legacy path).
    /// </summary>
    bool IsReplayTrackingEnabled { get; }

    /// <summary>
    /// Enable replay-state tracking for the route (drained-store, one-way) and return the
    /// activated capability set — the persisted current generation plus the boundary and
    /// session-state providers, which are valid ONLY as of a successful activation. Idempotent
    /// when already enabled (returns the persisted generation + providers); a mismatched
    /// persisted replay sink id fails closed.
    /// </summary>
    /// <param name="routeId">Must equal the buffer's route id.</param>
    /// <param name="replaySinkId">The replay sink to register at the activation head.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The activated capability set (generation + both providers).</returns>
    ValueTask<ReplayRouteActivation> ActivateReplayAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken);

    /// <summary>
    /// Append a batch on the tracked path (points + latest-value manifest + authoritative
    /// next_sequence, atomically) at <paramref name="expectedGeneration"/> — the fixed
    /// generation the route caches from activation. A generation mismatch or a storage failure
    /// is an invariant breach that faults the route (never a silent drop). K1.3 never advances
    /// the generation, so a mismatch must not happen in practice.
    /// </summary>
    /// <param name="points">The batch (one transaction).</param>
    /// <param name="expectedGeneration">The route's fixed generation (from activation).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The contiguous assigned sequence range.</returns>
    ValueTask<AssignedSequenceRange> AppendTrackedAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        RouteSchemaGeneration expectedGeneration,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of a successful <see cref="IReplayRouteBuffer.ActivateReplayAsync"/>: the
/// route's fixed (persisted) generation and the two replay-capture providers, returned
/// together so a caller never observes a partially-activated capability. The generation is
/// the PERSISTED value — it may be non-zero after reopening an already-enabled store, and
/// K1.3 caches it verbatim (never assuming zero).
/// </summary>
/// <param name="Generation">The persisted route-schema generation (fixed for the route lifetime in K1.3).</param>
/// <param name="BoundaryProvider">The replay-boundary capture provider (valid as of this activation).</param>
/// <param name="SessionStateProvider">The atomic birth/cutover capture provider (valid as of this activation).</param>
internal sealed record ReplayRouteActivation(
    RouteSchemaGeneration Generation,
    IReplayBoundaryProvider BoundaryProvider,
    IReplaySessionStateProvider SessionStateProvider);
