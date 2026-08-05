// ============================================================================
// File: Buffer/InMemoryBuffer.cs
// Purpose: Bounded in-memory IMessageBuffer implementation backed by a
//          ring buffer + SinkCursorTracker. Supports multiple sinks with
//          independent cursors per blueprint §19.2.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, §19; PHASE1_EXECUTION_PLAN.md C2a
//
// Design notes:
//   - Storage: CanonicalDataPoint[] sized to MaxDepth. Index = seq % capacity.
//   - _head is the next sequence to assign (monotonic, never wraps).
//   - _tail is the oldest sequence still held.
//   - The buffer is "full" when (_head - _tail) == capacity.
//   - Eviction: when min(cursors) advances past _tail, slots are freed.
//     With DropOldest, eviction also occurs to make room for new producers
//     (slow sinks lose data — a deliberate trade-off for buffer survival).
//   - Locking: a single object lock guards the ring + counters. The hot
//     path is short and lock-free benchmarks show this is acceptable for
//     the C2a target. Cursor reads are serialized through the same lock
//     (we use SinkCursorTracker.Min() under our own lock to avoid TOCTOU).
//   - Block mode uses a TaskCompletionSource that is replaced on every
//     space release; producers re-check after creating a waiter.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// In-memory <see cref="IMessageBuffer"/> backed by a fixed-capacity ring
/// buffer with per-sink cursor tracking. NOT durable across restarts —
/// suitable for routes configured with <see cref="BufferMode.InMemory"/>
/// or as the C2a stand-in until C2b's SQLite buffer lands.
/// </summary>
public sealed class InMemoryBuffer : IMessageBuffer
{
    private readonly object _lock = new();
    private readonly CanonicalDataPoint?[] _ring;
    private readonly int _capacity;
    private readonly DropPolicy _dropPolicy;
    private readonly SinkCursorTracker _cursors = new();

    private long _head;                  // next sequence to assign
    private long _tail;                  // oldest sequence still held
    private long _totalEnqueued;
    private long _totalDropped;          // backward-compat sum
    private long _droppedByCapacity;     // C2b split counter
    private long _sinksFastForwarded;    // C2b split counter (events, not points)
    private long _totalDrained;
    private TaskCompletionSource<bool>? _spaceWaiter;
    private bool _disposed;

    /// <summary>Create a new in-memory buffer.</summary>
    public InMemoryBuffer(string bufferId, BufferPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(bufferId);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.MaxDepth <= 0)
        {
            throw new ArgumentException("MaxDepth must be strictly positive for in-memory buffers.", nameof(policy));
        }

        BufferId = bufferId;
        _capacity = policy.MaxDepth;
        _dropPolicy = policy.DropPolicy;
        _ring = new CanonicalDataPoint?[_capacity];
    }

    /// <inheritdoc/>
    public string BufferId { get; }

    /// <summary>The configured capacity (MaxDepth).</summary>
    public int Capacity => _capacity;

    /// <inheritdoc/>
    public ValueTask EnqueueAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        // Fast synchronous path when there is room (or drop policy is non-blocking).
        if (TryEnqueueSync(points))
        {
            return ValueTask.CompletedTask;
        }

        // Blocking path — only reached when DropPolicy == Block AND ring is full.
        return EnqueueBlockingAsync(points, cancellationToken);
    }

    private bool TryEnqueueSync(IReadOnlyList<CanonicalDataPoint> points)
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (!HasSpaceLocked())
                {
                    if (_dropPolicy == DropPolicy.Block)
                    {
                        // Caller must take the async path. Note: any points
                        // already written in this call are kept.
                        return false;
                    }

                    if (_dropPolicy == DropPolicy.DropNewest)
                    {
                        _totalDropped++;
                        _droppedByCapacity++;
                        continue;
                    }

                    // DropOldest: force-evict the oldest slot.
                    EvictOldestLocked();
                }

                WriteLocked(point);
            }
            return true;
        }
    }

    private async ValueTask EnqueueBlockingAsync(
        IReadOnlyList<CanonicalDataPoint> points,
        CancellationToken cancellationToken)
    {
        // Slow path: process points one at a time, awaiting space as needed.
        for (int i = 0; i < points.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = points[i];

            while (true)
            {
                Task waitTask;
                lock (_lock)
                {
                    ThrowIfDisposed();
                    if (HasSpaceLocked())
                    {
                        WriteLocked(point);
                        break;
                    }
                    _spaceWaiter ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waitTask = _spaceWaiter.Task;
                }

                using var reg = cancellationToken.Register(static state =>
                {
                    var tcs = (TaskCompletionSource<bool>)state!;
                    tcs.TrySetCanceled();
                }, _spaceWaiter);

                await waitTask.ConfigureAwait(false);
            }
        }
    }

    private bool HasSpaceLocked() => (_head - _tail) < _capacity;

    private void WriteLocked(CanonicalDataPoint point)
    {
        var idx = (int)(_head % _capacity);
        _ring[idx] = point;
        _head++;
        _totalEnqueued++;
    }

    private void EvictOldestLocked()
    {
        // Drop the oldest slot to make room. Any sink whose cursor pointed
        // at that sequence is fast-forwarded; the lost data is counted.
        var idx = (int)(_tail % _capacity);
        _ring[idx] = null;
        _tail++;
        _totalDropped++;
        _droppedByCapacity++;

        // Snapshot whether any cursor actually needed bumping before the
        // fast-forward call. The fast-forward counter counts events (passes
        // that bumped at least one cursor), not points. long.MaxValue is
        // the empty-sinks sentinel so the comparison evaluates false when
        // no cursors exist (see SqliteBuffer for the parity fix).
        var minBefore = _cursors.Min(defaultIfEmpty: long.MaxValue);
        _cursors.FastForwardBelow(_tail);
        if (minBefore < _tail)
        {
            _sinksFastForwarded++;
        }
    }

    /// <inheritdoc/>
    public ValueTask<BufferBatch> DequeueBatchAsync(
        string sinkId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        if (maxCount <= 0)
        {
            return new ValueTask<BufferBatch>(BufferBatch.Empty);
        }
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            ThrowIfDisposed();
            if (!_cursors.IsRegistered(sinkId))
            {
                throw new InvalidOperationException($"Sink '{sinkId}' is not registered.");
            }

            var cursor = _cursors.Get(sinkId);
            // Cursor may be behind tail if it was fast-forwarded; clamp.
            if (cursor < _tail)
            {
                cursor = _tail;
            }
            if (cursor >= _head)
            {
                return new ValueTask<BufferBatch>(BufferBatch.Empty);
            }

            var available = _head - cursor;
            var take = (int)Math.Min(available, maxCount);
            var list = new List<CanonicalDataPoint>(take);
            for (long s = cursor; s < cursor + take; s++)
            {
                var idx = (int)(s % _capacity);
                var p = _ring[idx];
                if (p is not null)
                {
                    list.Add(p);
                }
            }

            return new ValueTask<BufferBatch>(new BufferBatch
            {
                Points = list,
                FirstSequence = cursor,
                LastSequence = cursor + take - 1,
            });
        }
    }

    /// <inheritdoc/>
    public ValueTask AckAsync(
        string sinkId,
        long upToSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? toRelease = null;
        lock (_lock)
        {
            ThrowIfDisposed();
            if (!_cursors.IsRegistered(sinkId))
            {
                throw new InvalidOperationException($"Sink '{sinkId}' is not registered.");
            }

            var advanced = _cursors.TryAdvance(sinkId, upToSequence + 1);
            if (advanced)
            {
                ReleaseEvictableLocked();
                toRelease = Interlocked.Exchange(ref _spaceWaiter, null);
            }
        }

        toRelease?.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    private void ReleaseEvictableLocked()
    {
        // Compute the new tail = min(head, min(cursors)). Any slot strictly
        // below the new tail is freed.
        var minCursor = _cursors.Min(defaultIfEmpty: _head);
        if (minCursor <= _tail)
        {
            return;
        }
        var newTail = Math.Min(_head, minCursor);
        for (long s = _tail; s < newTail; s++)
        {
            var idx = (int)(s % _capacity);
            _ring[idx] = null;
            _totalDrained++;
        }
        _tail = newTail;
    }

    /// <inheritdoc/>
    public ValueTask RegisterSinkAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            ThrowIfDisposed();
            // New sinks start at the OLDEST currently held sequence so they
            // replay the buffered backlog. Idempotent on re-register.
            _cursors.Register(sinkId, _tail);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeregisterSinkAsync(string sinkId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<bool>? toRelease = null;
        lock (_lock)
        {
            ThrowIfDisposed();
            _cursors.Deregister(sinkId);
            // Removing a sink may release data that was only being held for it.
            ReleaseEvictableLocked();
            toRelease = Interlocked.Exchange(ref _spaceWaiter, null);
        }
        toRelease?.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<BufferStats> GetStatsAsync()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            DateTime? oldest = null;
            if (_head > _tail)
            {
                var idx = (int)(_tail % _capacity);
                oldest = _ring[idx]?.GatewayTimestamp;
            }

            string? oldestSinkId = null;
            DateTime? oldestAt = null;
            var minEntry = _cursors.MinEntry();
            if (minEntry is { } e && _head > _tail)
            {
                oldestSinkId = e.SinkId;
                // The row at this sink's cursor is the oldest the sink still
                // needs. Clamp defensively to the live range.
                var sinkSeq = Math.Max(e.Cursor, _tail);
                if (sinkSeq < _head)
                {
                    var idx = (int)(sinkSeq % _capacity);
                    oldestAt = _ring[idx]?.GatewayTimestamp;
                }
            }

            return new ValueTask<BufferStats>(new BufferStats
            {
                CurrentDepth = _head - _tail,
                TotalEnqueued = _totalEnqueued,
                TotalDrained = _totalDrained,
                TotalDropped = _totalDropped,
                DroppedByCapacity = _droppedByCapacity,
                DroppedByRetention = 0, // in-memory buffer has no retention policy
                SinksFastForwarded = _sinksFastForwarded,
                OldestMessageAt = oldest,
                OldestUnackedSinkId = oldestSinkId,
                OldestUnackedAt = oldestAt,
                SizeBytes = 0,
                RegisteredSinks = _cursors.Count,
            });
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource<bool>? toRelease = null;
        lock (_lock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }
            _disposed = true;
            toRelease = Interlocked.Exchange(ref _spaceWaiter, null);
        }
        toRelease?.TrySetCanceled();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
