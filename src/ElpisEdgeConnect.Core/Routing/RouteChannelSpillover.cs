// ============================================================================
// File: Routing/RouteChannelSpillover.cs
// Purpose: MECHANISM for channel-to-buffer spillover. Owns a reference to
//          the route buffer; executes spill writes and keeps a
//          lifetime-scoped counter of spilled points. Does NOT decide when
//          to spill — that's BackpressureController's job.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.8 (Backpressure)
// Milestone: C3 Commit 2 (phase 6).
//
// LOCKED design rules:
//   * Mechanism only. This class contains no policy. It takes a batch that
//     the controller has already decided should spill, and writes it to
//     the buffer via IMessageBuffer.EnqueueAsync.
//   * The buffer remains the single source of durable truth — spillover
//     does NOT maintain a side queue, a staging table, or any other
//     storage. If the buffer rejects or drops during its own enqueue,
//     that's the buffer's drop policy at work, not the spillover's.
//   * The spilled counter exists for diagnostics and test pins only. It
//     never influences routing decisions.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Channel-to-buffer spillover mechanism. Writes overflow batches to the
/// route buffer; maintains a lifetime count of spilled points.
/// </summary>
public sealed class RouteChannelSpillover
{
    private readonly IMessageBuffer _buffer;
    private long _spilledPointCount;
    private long _spillEventCount;

    /// <summary>
    /// Construct a spillover writer that targets <paramref name="buffer"/>.
    /// </summary>
    public RouteChannelSpillover(IMessageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    /// <summary>Lifetime count of points spilled via this mechanism.</summary>
    public long SpilledPointCount => Interlocked.Read(ref _spilledPointCount);

    /// <summary>Lifetime count of spill EVENTS (one per SpillAsync call).</summary>
    public long SpillEventCount => Interlocked.Read(ref _spillEventCount);

    /// <summary>
    /// Spill a batch into the route buffer. Callers must only invoke this
    /// after <see cref="BackpressureController.Decide"/> has returned
    /// <see cref="BackpressureDecision.Spill"/>. The method does not
    /// re-check the controller.
    /// </summary>
    public async ValueTask SpillAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return;
        }

        await _buffer.EnqueueAsync(points, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _spilledPointCount, points.Count);
        Interlocked.Increment(ref _spillEventCount);
    }
}
