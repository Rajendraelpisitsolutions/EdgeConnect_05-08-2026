// ============================================================================
// File: Buffer/SqliteBuffer.cs
// Purpose: Public façade over SqliteRouteStore — the per-route SQLite database
//          owner. Preserves the pre-K1.2a public surface (the OpenAsync factory,
//          FilePath, and the internal Head/Tail/LastReclaimError test seams) while
//          delegating every IMessageBuffer operation to the owning store. The façade
//          opens NO connections and starts NO reclaim loop of its own; SqliteRouteStore
//          is the sole owner of connections, the reclaim loop, disposal, and the
//          exclusive ownership lock.
// Reference: docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md (O5);
//            v3.1 §3 (single disposal authority). Behavior-neutral (K1.2a).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// SQLite-backed durable <see cref="IMessageBuffer"/>. As of K1.2a this is a thin
/// façade over <see cref="SqliteRouteStore"/> (the database owner); all buffer
/// behavior is unchanged. Construct via <see cref="OpenAsync"/>. Also implements the
/// optional <see cref="IReplayRouteBuffer"/> capability (K1.3) by delegating to the owner.
/// </summary>
public sealed class SqliteBuffer : IMessageBuffer, IReplayRouteBuffer
{
    private readonly SqliteRouteStore _store;

    private SqliteBuffer(SqliteRouteStore store) => _store = store;

    /// <summary>
    /// Open (or create) a durable buffer at <paramref name="filePath"/>, run startup
    /// recovery, and return a ready-to-use buffer. Delegates to
    /// <see cref="SqliteRouteStore.OpenAsync"/>.
    /// </summary>
    /// <param name="bufferId">Logical buffer id (typically the route id).</param>
    /// <param name="filePath">Absolute path to the SQLite database file.</param>
    /// <param name="policy">Runtime buffer policy.</param>
    /// <param name="utcNow">Optional clock for tests.</param>
    /// <param name="onQuarantine">Optional callback for points that could not be serialized.</param>
    /// <returns>A ready-to-use buffer façade.</returns>
    public static async Task<SqliteBuffer> OpenAsync(
        string bufferId,
        string filePath,
        BufferPolicy policy,
        Func<DateTime>? utcNow = null,
        Action<QuarantinedPoint>? onQuarantine = null)
    {
        var store = await SqliteRouteStore
            .OpenAsync(bufferId, filePath, policy, utcNow, onQuarantine)
            .ConfigureAwait(false);
        return new SqliteBuffer(store);
    }

    /// <inheritdoc/>
    public string BufferId => _store.BufferId;

    /// <summary>The on-disk database file path.</summary>
    public string FilePath => _store.FilePath;

    /// <summary>Test seam: in-process head sequence. See <see cref="SqliteRouteStore.Head"/>.</summary>
    internal long Head => _store.Head;

    /// <summary>Test seam: in-process tail sequence. See <see cref="SqliteRouteStore.Tail"/>.</summary>
    internal long Tail => _store.Tail;

    /// <summary>Test / diagnostics seam: the last unexpected reclaim-loop exception, or null.</summary>
    internal Exception? LastReclaimError => _store.LastReclaimError;

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(IReadOnlyList<CanonicalDataPoint> points, CancellationToken cancellationToken) =>
        _store.EnqueueAsync(points, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<BufferBatch> DequeueBatchAsync(string sinkId, int maxCount, CancellationToken cancellationToken) =>
        _store.DequeueBatchAsync(sinkId, maxCount, cancellationToken);

    /// <inheritdoc/>
    public ValueTask AckAsync(string sinkId, long upToSequence, CancellationToken cancellationToken) =>
        _store.AckAsync(sinkId, upToSequence, cancellationToken);

    /// <inheritdoc/>
    public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken) =>
        _store.RegisterSinkAsync(sinkId, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken) =>
        _store.DeregisterSinkAsync(sinkId, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<BufferStats> GetStatsAsync() => _store.GetStatsAsync();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _store.DisposeAsync();

    // ------------------------------------------------------------------------
    // Replay-capability surface (K1.2d) — delegates to the owner, keeping the
    // factory the sole construction path. K1.3 reaches these through the factory.
    // ------------------------------------------------------------------------

    /// <summary>
    /// Enable replay-state tracking on the owning store (drained-store, one-way transition).
    /// Delegates to <see cref="SqliteRouteStore.ActivateReplayStateTrackingAsync"/>.
    /// </summary>
    /// <param name="routeId">Must equal this buffer's route id.</param>
    /// <param name="replaySinkId">The replay sink to register at the activation head.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The activation result.</returns>
    internal ValueTask<ReplayTrackingActivationResult> ActivateReplayStateTrackingAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken) =>
        _store.ActivateReplayStateTrackingAsync(routeId, replaySinkId, cancellationToken);

    /// <summary>
    /// Take a point-in-time <see cref="SqliteRouteStoreHandle"/> exposing this façade plus the
    /// two replay capabilities. Reads the enabled state ONCE, so both provider slots are
    /// non-null iff tracking is enabled and never disagree. A handle taken before activation
    /// keeps null providers (the record is immutable); take a fresh handle after activating.
    /// </summary>
    /// <returns>The capability handle.</returns>
    internal SqliteRouteStoreHandle GetCapabilityHandle()
    {
        var enabled = _store.IsReplayStateTrackingEnabled;
        return new SqliteRouteStoreHandle(this, enabled ? _store : null, enabled ? _store : null);
    }

    // ------------------------------------------------------------------------
    // IReplayRouteBuffer (K1.3) — the route-facing replay capability. Delegates to
    // the owner; providers are returned FROM activation (lifecycle-honest), and
    // IsReplayTrackingEnabled reads the owner's already-loaded state (no 2nd connection).
    // ------------------------------------------------------------------------

    /// <inheritdoc/>
    bool IReplayRouteBuffer.IsReplayTrackingEnabled => _store.IsReplayStateTrackingEnabled;

    /// <inheritdoc/>
    async ValueTask<ReplayRouteActivation> IReplayRouteBuffer.ActivateReplayAsync(
        string routeId, string replaySinkId, CancellationToken cancellationToken)
    {
        // Enable (or confirm already-enabled) tracking; a mismatched persisted replay sink id
        // fails closed inside the owner (RouteStoreReplaySinkMismatch).
        await _store.ActivateReplayStateTrackingAsync(routeId, replaySinkId, cancellationToken).ConfigureAwait(false);

        // Post-activation the owner IS the (non-null) provider set and carries the persisted
        // generation — read it verbatim (may be non-zero on reopen; K1.3 never advances it).
        var generation = RouteSchemaGeneration.Create(_store.CurrentSchemaGeneration);
        return new ReplayRouteActivation(generation, _store, _store);
    }

    /// <inheritdoc/>
    ValueTask<AssignedSequenceRange> IReplayRouteBuffer.AppendTrackedAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        RouteSchemaGeneration expectedGeneration,
        CancellationToken cancellationToken) =>
        _store.AppendAsync(points, expectedGeneration.Value, cancellationToken);
}
