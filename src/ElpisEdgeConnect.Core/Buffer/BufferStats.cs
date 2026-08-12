// ============================================================================
// File: Buffer/BufferStats.cs
// Purpose: Snapshot of buffer health/throughput counters.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, PHASE1_EXECUTION_PLAN.md C2a
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Immutable snapshot of an <see cref="IMessageBuffer"/>'s observable counters.
/// Returned by <see cref="IMessageBuffer.GetStatsAsync"/>.
/// </summary>
public sealed record BufferStats
{
    /// <summary>Number of points currently held (not yet acked by all sinks).</summary>
    public required long CurrentDepth { get; init; }

    /// <summary>Total points enqueued since the buffer was created.</summary>
    public required long TotalEnqueued { get; init; }

    /// <summary>Total points drained (acked by ALL sinks and physically released).</summary>
    public required long TotalDrained { get; init; }

    /// <summary>
    /// Lifetime sum of all drop reasons. Retained for backward compatibility
    /// with the C2a shape; equals <see cref="DroppedByCapacity"/> +
    /// <see cref="DroppedByRetention"/> for any implementation that reports
    /// either.
    /// </summary>
    public required long TotalDropped { get; init; }

    /// <summary>
    /// Points refused or evicted because the buffer hit <c>MaxDepth</c>.
    /// Counts both <c>DropNewest</c> refusals and <c>DropOldest</c> evictions.
    /// (C2b addition — additive to the C2a shape. In-memory implementations
    /// populate this field alongside <see cref="TotalDropped"/>.)
    /// </summary>
    public long DroppedByCapacity { get; init; }

    /// <summary>
    /// Points evicted by an implementation-specific age- or size-based
    /// retention policy (e.g., <c>SqliteBuffer.MaxAge</c>). Represents
    /// <b>deliberate unread-data loss</b>: operators who opt into retention
    /// are agreeing that lagging sinks may lose data. Always zero for the
    /// in-memory buffer. (C2b addition.)
    /// </summary>
    public long DroppedByRetention { get; init; }

    /// <summary>
    /// Count of fast-forward <b>events</b> (not points) — one per eviction
    /// pass that had to advance at least one lagging sink cursor past a
    /// deleted row. (C2b addition.)
    /// </summary>
    public long SinksFastForwarded { get; init; }

    /// <summary>
    /// Points the buffer could not serialize and therefore SKIPPED rather than
    /// failing the whole enqueue ("quarantine-and-continue" policy). This is a
    /// <b>data-quality</b> signal (a malformed value / unsupported type), as
    /// distinct from <see cref="DroppedByCapacity"/> (a back-pressure signal).
    /// A non-zero value means the upstream adapter/mapping emitted points that
    /// cannot be represented on the wire — investigate the source, not capacity.
    /// Always zero for buffers that do not serialize (e.g. the in-memory buffer).
    /// </summary>
    public long Quarantined { get; init; }

    /// <summary>Gateway timestamp of the oldest point currently held; null when empty.</summary>
    public DateTime? OldestMessageAt { get; init; }

    /// <summary>
    /// Sink id whose cursor currently pins the buffer tail (i.e., the sink
    /// that is furthest behind). <c>null</c> when the buffer is empty or no
    /// sinks are registered. Diagnostics breadcrumb for C4 alerting on
    /// lagging sinks. (C2b addition.)
    /// </summary>
    public string? OldestUnackedSinkId { get; init; }

    /// <summary>
    /// Enqueue wall-clock time of the oldest sequence still held for the
    /// sink identified by <see cref="OldestUnackedSinkId"/>. (C2b addition.)
    /// </summary>
    public DateTime? OldestUnackedAt { get; init; }

    /// <summary>
    /// Approximate storage footprint in bytes. In-memory implementations
    /// return 0 (storage is by reference). Durable implementations MAY
    /// compute an approximate byte figure; exact accounting is not required
    /// by the contract.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>Number of sinks currently registered.</summary>
    public required int RegisteredSinks { get; init; }
}
