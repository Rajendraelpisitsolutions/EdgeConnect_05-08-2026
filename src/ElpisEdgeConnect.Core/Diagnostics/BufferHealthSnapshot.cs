// ============================================================================
// File: Diagnostics/BufferHealthSnapshot.cs
// Purpose: Diagnostics-layer rollup of one route's per-route message buffer.
//          Mirrors the bits of Buffer.BufferStats that operators care about
//          for production observability — depth, oldest-unacked age, lifetime
//          enqueue / drain / drop counters, approximate disk footprint,
//          which sink (if any) is currently pinning the tail.
//
//          Lives in the diagnostics namespace (not the buffer namespace)
//          because the buffer module is the data layer; the diagnostics
//          collector is the read-side rollup that the host endpoints,
//          metrics exporter, and soak runner all consume from.
// Reference: ARCHITECTURE_BLUEPRINT.md §6 (Store-and-Forward), §13.1 (Three-way diagnostics)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Atomic snapshot of one route's per-route buffer health. Carried as a
/// nullable field on <see cref="RouteHealthSnapshot"/> — null when the
/// route has not pushed any buffer stats yet (which is normal during the
/// first few milliseconds after route start).
/// </summary>
public sealed record BufferHealthSnapshot
{
    /// <summary>The route id this buffer belongs to.</summary>
    public required string RouteId { get; init; }

    /// <summary>Configured buffer mode (None, InMemory, StoreAndForward).</summary>
    public required string Mode { get; init; }

    /// <summary>Number of points currently held (not yet acked by all sinks).</summary>
    public required long CurrentDepth { get; init; }

    /// <summary>Total points enqueued since the buffer was opened.</summary>
    public required long TotalEnqueued { get; init; }

    /// <summary>Total points drained (acked by all sinks and physically released).</summary>
    public required long TotalDrained { get; init; }

    /// <summary>
    /// Lifetime sum of all drop reasons. Equals
    /// <see cref="DroppedByCapacity"/> + <see cref="DroppedByRetention"/>.
    /// </summary>
    public required long TotalDropped { get; init; }

    /// <summary>
    /// Points refused or evicted because the buffer hit MaxDepth. Counts
    /// both DropNewest refusals and DropOldest evictions.
    /// </summary>
    public long DroppedByCapacity { get; init; }

    /// <summary>
    /// Points evicted by an age-based retention policy. Always zero for
    /// the in-memory buffer — InMemory has no retention concept.
    /// </summary>
    public long DroppedByRetention { get; init; }

    /// <summary>Approximate storage footprint in bytes. Zero for InMemory.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gateway timestamp of the oldest point currently held. Null when empty.</summary>
    public DateTime? OldestMessageAt { get; init; }

    /// <summary>
    /// Sink id whose cursor pins the buffer tail (i.e., the sink furthest
    /// behind). Null when the buffer is empty or no sinks are registered.
    /// Diagnostics breadcrumb — alerts on lagging sinks key off this.
    /// </summary>
    public string? OldestUnackedSinkId { get; init; }

    /// <summary>
    /// Enqueue wall-clock time of the oldest sequence still held for the
    /// pinning sink. Null when the buffer is empty.
    /// </summary>
    public DateTime? OldestUnackedAt { get; init; }

    /// <summary>UTC time at which this snapshot was captured.</summary>
    public required DateTime ObservedAtUtc { get; init; }
}
