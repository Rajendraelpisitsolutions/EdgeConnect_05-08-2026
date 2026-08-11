// ============================================================================
// File: Diagnostics/BoundedEventLog.cs
// Purpose: Lock-protected fixed-capacity ring buffer used by the diagnostics
//          collector to retain recent events per dimension (route, sink,
//          source, pipeline). When the buffer is full, the oldest entry is
//          evicted and a drop counter increments — drops are first-class
//          and observable in the snapshot.
// Reference: ARCHITECTURE_BLUEPRINT.md §13 (Diagnostics)
// Milestone: C4 (Diagnostics Collector) — phase 1.
//
// LOCKED design rules:
//   * No silent drops. Eviction increments TotalDropped exactly once per
//     evicted entry; the snapshot reports the live count, the lifetime
//     added count, and the lifetime dropped count.
//   * Snapshot reads NEVER block hot-path Add() callers indefinitely; we
//     use a single short-lived lock per call. Snapshot allocates a new
//     array — readers never see torn state.
//   * Single-source-of-truth: each route/sink/source has exactly one
//     BoundedEventLog instance per dimension. Tests pin this in phase 2.
//   * Add() is sync void with no exceptions in the steady state. Capacity
//     validation happens at construction time.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Fixed-capacity ring buffer of recent events with first-class drop
/// accounting. Used by <c>RuntimeDiagnosticsCollector</c> for per-route,
/// per-sink, per-source, and per-pipeline event retention.
/// </summary>
public sealed class BoundedEventLog<T>
{
    private readonly object _gate = new();
    private readonly T[] _buffer;
    private int _head;
    private int _count;
    private long _totalAdded;
    private long _totalDropped;

    /// <summary>
    /// Construct a bounded log with the given capacity. Capacity must be
    /// strictly positive.
    /// </summary>
    public BoundedEventLog(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "BoundedEventLog capacity must be > 0.");
        }
        _buffer = new T[capacity];
    }

    /// <summary>The fixed maximum number of entries retained.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Current live entry count (0..Capacity).</summary>
    public int Count
    {
        get { lock (_gate) { return _count; } }
    }

    /// <summary>Lifetime count of Add() calls (including dropped ones).</summary>
    public long TotalAdded
    {
        get { lock (_gate) { return _totalAdded; } }
    }

    /// <summary>
    /// Lifetime count of entries evicted by capacity overflow. Always
    /// reflects the number of entries that fell out of the live window.
    /// </summary>
    public long TotalDropped
    {
        get { lock (_gate) { return _totalDropped; } }
    }

    /// <summary>
    /// Add an entry. When the buffer is at capacity the oldest entry is
    /// evicted and <see cref="TotalDropped"/> is incremented.
    /// </summary>
    public void Add(T item)
    {
        lock (_gate)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
            else
            {
                // We just overwrote the oldest entry — count it as a drop.
                _totalDropped++;
            }
            _totalAdded++;
        }
    }

    /// <summary>
    /// Snapshot the live entries in chronological order (oldest first).
    /// Returns a fresh array each call so callers can iterate without
    /// holding any lock.
    /// </summary>
    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            var result = new T[_count];
            if (_count == 0)
            {
                return result;
            }
            // When the buffer is full, _head points at the oldest entry.
            // When it is partially filled, items 0.._count-1 are in order.
            if (_count == _buffer.Length)
            {
                for (var i = 0; i < _count; i++)
                {
                    result[i] = _buffer[(_head + i) % _buffer.Length];
                }
            }
            else
            {
                for (var i = 0; i < _count; i++)
                {
                    result[i] = _buffer[i];
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Capture a tuple of (live entries, lifetime added, lifetime dropped)
    /// in a single locked read so the snapshot is internally consistent.
    /// Used by the eventual <c>RuntimeDiagnosticsCollector.GetSnapshot()</c>.
    /// </summary>
    public BoundedEventLogSnapshot<T> SnapshotWithCounters()
    {
        lock (_gate)
        {
            // Inline the snapshot path so all three values come from the
            // same locked region — no chance of drift between Snapshot()
            // and TotalDropped under concurrent writers.
            var entries = new T[_count];
            if (_count > 0)
            {
                if (_count == _buffer.Length)
                {
                    for (var i = 0; i < _count; i++)
                    {
                        entries[i] = _buffer[(_head + i) % _buffer.Length];
                    }
                }
                else
                {
                    for (var i = 0; i < _count; i++)
                    {
                        entries[i] = _buffer[i];
                    }
                }
            }
            return new BoundedEventLogSnapshot<T>
            {
                Entries = entries,
                TotalAdded = _totalAdded,
                TotalDropped = _totalDropped,
                LiveCount = _count,
                Capacity = _buffer.Length,
            };
        }
    }
}

/// <summary>
/// Atomic snapshot of a <see cref="BoundedEventLog{T}"/>.
/// </summary>
public sealed record BoundedEventLogSnapshot<T>
{
    /// <summary>Live entries in chronological order (oldest first).</summary>
    public required IReadOnlyList<T> Entries { get; init; }

    /// <summary>Lifetime count of Add() calls.</summary>
    public required long TotalAdded { get; init; }

    /// <summary>Lifetime count of evicted entries.</summary>
    public required long TotalDropped { get; init; }

    /// <summary>Live entry count at snapshot time.</summary>
    public required int LiveCount { get; init; }

    /// <summary>Buffer capacity.</summary>
    public required int Capacity { get; init; }
}
