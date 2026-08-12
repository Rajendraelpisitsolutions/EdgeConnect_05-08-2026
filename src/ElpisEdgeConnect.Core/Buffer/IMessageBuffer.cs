// ============================================================================
// File: Buffer/IMessageBuffer.cs
// Purpose: FINAL C2a contract for per-route message buffering. The same
//          contract is consumed by InMemoryBuffer (C2a) and SqliteBuffer
//          (C2b) — no shape change permitted after C2a closes.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, §19; PHASE1_EXECUTION_PLAN.md C2a;
//            docs/core/buffer-contract.md (authoritative spec)
//
// LOCKED design refinements vs blueprint §6 sketch:
//   D1: DequeueBatchAsync and AckAsync are sink-aware (take sinkId). The
//       blueprint's single-cursor sketch cannot support fanout without
//       duplicating storage; the route engine in C3 multiplexes via the
//       sink id.
//   D6: RegisterSinkAsync / DeregisterSinkAsync explicitly model sink
//       lifecycle so eviction math is correct as sinks come and go.
//   D7: DequeueBatchAsync returns BufferBatch carrying the sequence range,
//       so callers can ack precisely without bookkeeping.
//   D15: ReconcileSinksAsync (retention fix) closes the orphaned-cursor leak.
//       DeregisterSinkAsync had no production caller, so a cursor written for
//       a sink that was later removed from the route was never deleted — it
//       was reloaded on every open and pinned the tail forever, producing
//       unbounded on-disk growth and a phantom backlog. Reconcile is the ONE
//       production entry point that both registers missing cursors and prunes
//       cursors that are no longer configured. It is a DEFAULT interface
//       method whose default body is exactly the historical register-only
//       behaviour, so existing test doubles keep compiling and keep their
//       previous semantics.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Per-route message buffer contract. Enqueue is many-producer; dequeue is
/// per-sink and may run from multiple sink workers concurrently. Ack
/// advances the per-sink cursor; storage is physically released only when
/// ALL sinks have acked.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sequence numbers</b> are assigned by the buffer at enqueue time and
/// are strictly monotonic per buffer instance. They are independent of any
/// upstream <see cref="CanonicalDataPoint.SequenceNumber"/>.
/// </para>
/// <para>
/// <b>Ordering</b> is preserved per the blueprint §19.6 invariant: a sink
/// observes its assigned points in strict sequence order. Cross-source
/// ordering is NOT promised.
/// </para>
/// <para>
/// <b>Drop semantics</b>: when the buffer is full and a producer attempts
/// to enqueue, the configured <see cref="Configuration.DropPolicy"/>
/// applies. Dropped points are counted in
/// <see cref="BufferStats.TotalDropped"/>; no exception is thrown.
/// </para>
/// </remarks>
public interface IMessageBuffer : IAsyncDisposable
{
    /// <summary>Stable identifier (typically the route id).</summary>
    string BufferId { get; }

    /// <summary>
    /// Append <paramref name="points"/> to the buffer. Each point is
    /// assigned the next monotonic sequence. The returned ValueTask
    /// completes synchronously when there is room; for
    /// <see cref="Configuration.DropPolicy.Block"/> it may suspend until a
    /// sink ack frees space.
    /// </summary>
    ValueTask EnqueueAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken);

    /// <summary>
    /// Read up to <paramref name="maxCount"/> points starting from the
    /// cursor for <paramref name="sinkId"/>. Does NOT advance the cursor —
    /// the caller advances by calling <see cref="AckAsync"/> after a
    /// successful publish. Returns <see cref="BufferBatch.Empty"/> when no
    /// new data is available for the sink.
    /// </summary>
    ValueTask<BufferBatch> DequeueBatchAsync(
        string sinkId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Advance the cursor for <paramref name="sinkId"/> past
    /// <paramref name="upToSequence"/> (inclusive). Idempotent: a second
    /// ack at the same or earlier sequence is a no-op. Cursors cannot
    /// regress.
    /// </summary>
    ValueTask AckAsync(
        string sinkId,
        long upToSequence,
        CancellationToken cancellationToken);

    /// <summary>
    /// Register a sink. The new sink starts at the OLDEST currently held
    /// sequence so a freshly attached sink replays the buffered backlog.
    /// Calling Register on an existing sinkId is idempotent and leaves the
    /// existing cursor untouched.
    /// </summary>
    ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken);

    /// <summary>
    /// Remove a sink's cursor. Any data held only because of this sink may
    /// be released on the next eviction pass. Calling Deregister on an
    /// unknown sink is a no-op.
    /// </summary>
    ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken);

    /// <summary>
    /// Bring the buffer's sink-cursor set into agreement with the route's
    /// currently-configured sinks (D15). Every id in
    /// <paramref name="activeSinkIds"/> that has no cursor is registered (at
    /// the oldest held sequence, exactly as
    /// <see cref="RegisterSinkAsync"/> would); every persisted cursor whose
    /// sink id is NOT in <paramref name="activeSinkIds"/> is pruned, and the
    /// storage it was pinning becomes reclaimable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Store-and-forward guarantee.</b> ONLY ids absent from
    /// <paramref name="activeSinkIds"/> may be pruned. A configured sink that
    /// is merely lagging — however far behind — still pins the tail and still
    /// replays in order from its own cursor. Pruning a configured sink would
    /// silently discard data the operator is entitled to.
    /// </para>
    /// <para>
    /// <b>Replay sink.</b> An implementation that has a designated replay sink
    /// MUST force-add it to the active set; it is never pruned regardless of
    /// what the caller passes.
    /// </para>
    /// <para>
    /// <b>Not silent.</b> Pruning discards a sink's undelivered backlog, so the
    /// result reports every prune (sink id, the sequence it was pinning, and
    /// how many points it never received) and callers MUST surface it at
    /// warning severity. Depth dropping without an explanation is exactly the
    /// symptom this contract exists to make explicable.
    /// </para>
    /// <para>
    /// The default implementation is the historical register-only behaviour:
    /// it registers the active ids and prunes nothing. Durable implementations
    /// override it. Overriders MUST make the register + prune set change
    /// atomically (one transaction) and recompute the tail only over the
    /// committed set.
    /// </para>
    /// </remarks>
    /// <param name="activeSinkIds">The route's currently-configured sink instance ids.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was registered and what was pruned.</returns>
    async ValueTask<SinkReconciliationResult> ReconcileSinksAsync(
        IReadOnlyCollection<string> activeSinkIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeSinkIds);
        foreach (var sinkId in activeSinkIds)
        {
            await RegisterSinkAsync(sinkId, cancellationToken).ConfigureAwait(false);
        }

        return SinkReconciliationResult.Empty;
    }

    /// <summary>Take a snapshot of the buffer's observable counters.</summary>
    ValueTask<BufferStats> GetStatsAsync();
}

/// <summary>
/// One cursor removed by <see cref="IMessageBuffer.ReconcileSinksAsync"/>
/// because its sink is no longer configured on the route. Carries enough
/// detail for an operator to explain a sudden drop in buffer depth.
/// </summary>
public sealed record PrunedSinkCursor
{
    /// <summary>The sink instance id whose cursor was removed.</summary>
    public required string SinkId { get; init; }

    /// <summary>
    /// The sequence the removed cursor was pinning (its <c>next_unread</c>).
    /// While the cursor existed, no sequence at or above this value could be
    /// reclaimed.
    /// </summary>
    public required long PinnedSequence { get; init; }

    /// <summary>
    /// How many buffered points this sink had not yet received at prune time.
    /// These points are discarded for this sink — they are NOT redelivered
    /// anywhere.
    /// </summary>
    public required long UndeliveredPoints { get; init; }
}

/// <summary>
/// Outcome of one <see cref="IMessageBuffer.ReconcileSinksAsync"/> pass.
/// </summary>
public sealed record SinkReconciliationResult
{
    /// <summary>Nothing registered, nothing pruned.</summary>
    public static SinkReconciliationResult Empty { get; } = new()
    {
        Registered = Array.Empty<string>(),
        Pruned = Array.Empty<PrunedSinkCursor>(),
    };

    /// <summary>Sink ids that had no cursor and were newly registered.</summary>
    public required IReadOnlyList<string> Registered { get; init; }

    /// <summary>Cursors removed because their sink is no longer configured.</summary>
    public required IReadOnlyList<PrunedSinkCursor> Pruned { get; init; }

    /// <summary>True when the buffer's cursor set already matched the active set.</summary>
    public bool IsNoOp => Registered.Count == 0 && Pruned.Count == 0;
}

/// <summary>
/// Result of a single <see cref="IMessageBuffer.DequeueBatchAsync"/> call.
/// Carries the range of sequences covered so the caller can ack precisely.
/// </summary>
public sealed record BufferBatch
{
    /// <summary>Empty singleton — no data available.</summary>
    public static BufferBatch Empty { get; } = new()
    {
        Points = Array.Empty<CanonicalDataPoint>(),
        FirstSequence = -1,
        LastSequence = -1,
    };

    /// <summary>Points in this batch, in strict sequence order.</summary>
    public required IReadOnlyList<CanonicalDataPoint> Points { get; init; }

    /// <summary>Sequence of the first point in <see cref="Points"/>; -1 when empty.</summary>
    public required long FirstSequence { get; init; }

    /// <summary>Sequence of the last point in <see cref="Points"/>; -1 when empty.</summary>
    public required long LastSequence { get; init; }

    /// <summary>True when no points were available.</summary>
    public bool IsEmpty => Points.Count == 0;
}
