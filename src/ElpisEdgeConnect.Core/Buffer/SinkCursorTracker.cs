// ============================================================================
// File: Buffer/SinkCursorTracker.cs
// Purpose: Per-sink monotonic cursor management. Used by both InMemoryBuffer
//          (C2a) and SqliteBuffer (C2b) — designed as a reusable seam.
// Reference: PHASE1_EXECUTION_PLAN.md C2a, ARCHITECTURE_BLUEPRINT.md §19.2
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Tracks a monotonic <c>long</c> cursor per sink id. All operations are
/// guarded by an internal lock; cursors can only advance.
/// </summary>
/// <remarks>
/// The cursor for a sink is interpreted as "the next unread sequence" —
/// i.e., points with sequence &lt; cursor have been delivered. After acking
/// up to sequence N inclusive, the cursor becomes N + 1.
/// </remarks>
public sealed class SinkCursorTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _cursors = new(StringComparer.Ordinal);

    /// <summary>Number of sinks currently registered.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cursors.Count;
            }
        }
    }

    /// <summary>
    /// Register a new sink with an initial cursor. If the sink is already
    /// registered, the existing cursor is preserved (idempotent).
    /// </summary>
    public void Register(string sinkId, long initialSequence)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        lock (_lock)
        {
            if (!_cursors.ContainsKey(sinkId))
            {
                _cursors[sinkId] = initialSequence;
            }
        }
    }

    /// <summary>Remove a sink. Idempotent.</summary>
    public void Deregister(string sinkId)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        lock (_lock)
        {
            _cursors.Remove(sinkId);
        }
    }

    /// <summary>True when the sink is registered.</summary>
    public bool IsRegistered(string sinkId)
    {
        lock (_lock)
        {
            return _cursors.ContainsKey(sinkId);
        }
    }

    /// <summary>Read the current cursor; throws if the sink is not registered.</summary>
    public long Get(string sinkId)
    {
        lock (_lock)
        {
            if (!_cursors.TryGetValue(sinkId, out var c))
            {
                throw new InvalidOperationException($"Sink '{sinkId}' is not registered.");
            }
            return c;
        }
    }

    /// <summary>
    /// Try to advance the cursor for <paramref name="sinkId"/> to
    /// <paramref name="newCursor"/>. Returns false if the sink is unknown
    /// or if <paramref name="newCursor"/> is not greater than the current
    /// value (cursors cannot regress).
    /// </summary>
    public bool TryAdvance(string sinkId, long newCursor)
    {
        ArgumentNullException.ThrowIfNull(sinkId);
        lock (_lock)
        {
            if (!_cursors.TryGetValue(sinkId, out var current))
            {
                return false;
            }
            if (newCursor <= current)
            {
                return false;
            }
            _cursors[sinkId] = newCursor;
            return true;
        }
    }

    /// <summary>
    /// Snapshot the smallest cursor across all registered sinks. Returns
    /// <paramref name="defaultIfEmpty"/> when no sinks are registered.
    /// Used by the buffer to decide which sequences may be physically
    /// released.
    /// </summary>
    public long Min(long defaultIfEmpty)
    {
        lock (_lock)
        {
            if (_cursors.Count == 0)
            {
                return defaultIfEmpty;
            }
            long min = long.MaxValue;
            foreach (var c in _cursors.Values)
            {
                if (c < min)
                {
                    min = c;
                }
            }
            return min;
        }
    }

    /// <summary>
    /// Find the sink with the smallest cursor and return its id + cursor.
    /// Returns <c>null</c> when no sinks are registered. Used by
    /// <see cref="IMessageBuffer.GetStatsAsync"/> to populate
    /// <see cref="BufferStats.OldestUnackedSinkId"/>.
    /// </summary>
    public (string SinkId, long Cursor)? MinEntry()
    {
        lock (_lock)
        {
            if (_cursors.Count == 0)
            {
                return null;
            }
            string minKey = null!;
            long minVal = long.MaxValue;
            foreach (var kvp in _cursors)
            {
                if (kvp.Value < minVal)
                {
                    minVal = kvp.Value;
                    minKey = kvp.Key;
                }
            }
            return (minKey, minVal);
        }
    }

    /// <summary>
    /// Fast-forward every sink whose cursor is less than
    /// <paramref name="newFloor"/> up to that floor. Used when the buffer
    /// evicts data (e.g., DropOldest) and a slow sink would otherwise
    /// be left pointing at a removed sequence.
    /// </summary>
    public void FastForwardBelow(long newFloor)
    {
        lock (_lock)
        {
            // Snapshot keys to avoid mutating the dictionary while iterating.
            string[] keys = new string[_cursors.Count];
            int i = 0;
            foreach (var k in _cursors.Keys)
            {
                keys[i++] = k;
            }
            for (int j = 0; j < keys.Length; j++)
            {
                if (_cursors[keys[j]] < newFloor)
                {
                    _cursors[keys[j]] = newFloor;
                }
            }
        }
    }
}
