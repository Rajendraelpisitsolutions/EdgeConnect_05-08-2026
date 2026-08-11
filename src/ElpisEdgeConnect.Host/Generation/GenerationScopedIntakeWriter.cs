// ============================================================================
// File: Generation/GenerationScopedIntakeWriter.cs
// Purpose: The generation-fenced data-ingress writer (review B2/G5). Waits for
//          channel capacity OUTSIDE the gate, then commits a synchronous
//          TryWrite INSIDE the gate's lease fence, so only the current
//          authorized generation can enqueue. A point successfully enqueued
//          before retirement remains valid; pending or late writes are rejected.
//          Detach() is an idempotent reference-clear; it does NOT revoke
//          authority (the gate's TryRetire is the linearization point) and does
//          NOT complete the channel.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §2.
// Slice 0 — commit 2 scaffolding (unused; not wired into the supervisor).
// ============================================================================

using System.Threading.Channels;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Outcome of a generation-scoped intake write.</summary>
internal enum IntakeWriteOutcome
{
    /// <summary>The item was enqueued under the lease fence; it may drain even after retirement.</summary>
    Committed = 0,

    /// <summary>The generation lost (or never held) publish authority, or the writer was detached.</summary>
    RejectedRetired = 1,

    /// <summary>The stable channel is permanently closed.</summary>
    ChannelClosed = 2,
}

/// <summary>
/// Writes items into a stable slot channel on behalf of one generation, fenced
/// by that generation's lease. Generic over the payload so the write mechanism
/// is testable independently of the canonical model; the slot pins it to
/// <c>CanonicalDataPoint</c>.
/// </summary>
internal sealed class GenerationScopedIntakeWriter<T>
{
    private readonly SourceSlotGate _gate;
    private readonly GenerationLease _lease;
    private volatile ChannelWriter<T>? _writer;

    internal GenerationScopedIntakeWriter(SourceSlotGate gate, GenerationLease lease, ChannelWriter<T> writer)
    {
        _gate = gate;
        _lease = lease;
        _writer = writer;
    }

    /// <summary>
    /// Enqueue <paramref name="item"/> for this generation. Capacity is awaited
    /// outside the gate; the enqueue itself runs synchronously under the lease
    /// fence. A capacity race (another producer took the slot) re-enters
    /// <c>WaitToWriteAsync</c> — never a raw <c>TryWrite</c>-only spin (the wait
    /// itself may complete synchronously). Cancellation propagates as
    /// <see cref="OperationCanceledException"/> for the caller to map (a retired
    /// generation's pump simply exits). Returns a <see cref="ValueTask{T}"/> —
    /// this is a per-point hot path.
    /// </summary>
    public async ValueTask<IntakeWriteOutcome> WriteAsync(T item, CancellationToken ct)
    {
        while (true)
        {
            var writer = _writer;
            if (writer is null)
            {
                return IntakeWriteOutcome.RejectedRetired; // detached
            }

            // Normal channel completion → WaitToWriteAsync returns false. An
            // exceptional completion (Complete(ex)) faults the wait; we let that
            // propagate (no narrow catch), per the channel contract.
            var hasCapacity = await writer.WaitToWriteAsync(ct).ConfigureAwait(false);
            if (!hasCapacity)
            {
                return IntakeWriteOutcome.ChannelClosed; // permanently closed
            }

            var outcome = _gate.TryCommit(_lease, (writer, item), static s => s.writer.TryWrite(s.item));
            if (outcome == GenerationCommitOutcome.Committed)
            {
                return IntakeWriteOutcome.Committed;
            }
            if (outcome == GenerationCommitOutcome.Rejected)
            {
                return IntakeWriteOutcome.RejectedRetired; // lease not current — rejected-late
            }

            // NotCommitted: capacity was consumed between the wait and the commit;
            // re-await (never a synchronous spin).
        }
    }

    /// <summary>
    /// Idempotent reference-release of the inner writer (release-promptness only).
    /// It does NOT revoke authority and does NOT complete the channel: a write
    /// that already captured the writer may still commit while its lease remains
    /// authorized. <b>Retirement (the gate's <c>TryRetire</c>) is the fence</b>;
    /// Detach merely lets an orphaned writer drop its channel reference.
    /// </summary>
    public void Detach() => _writer = null;
}
