// ============================================================================
// File: Routing/FanoutDispatcher.cs
// Purpose: Wake-only per-route fanout coordinator. Holds one binary signal
//          per registered sink. The route worker calls NotifyAll() after
//          enqueuing a batch into the route buffer; each sink publisher loop
//          awaits its own signal before re-polling the buffer.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.3 (fanout), PHASE1_EXECUTION_PLAN.md C3.
// Milestone: C3 Commit 2 (phase 2 — wake-only fanout).
//
// LOCKED design contract:
//   * The dispatcher NEVER carries CanonicalDataPoint payloads. Points live
//     in the route buffer; the dispatcher only signals "go look again".
//     A structural test (FanoutDispatcherStructureTests) pins this.
//   * Each sink has an independent binary signal. A slow sink dropping signals
//     while still publishing never blocks a fast sink from making progress.
//   * Wakes are edge-coalesced: if two NotifyAll() calls arrive before the
//     sink observes the first, the second is collapsed. The sink still sees
//     the latest buffer contents on its next DequeueBatchAsync.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Per-route fanout coordinator. Wake-only: the dispatcher carries no data,
/// only signals that one or more sinks should re-check the route buffer.
/// </summary>
public sealed class FanoutDispatcher : IDisposable
{
    private readonly Dictionary<string, SemaphoreSlim> _signals =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Register a sink with the dispatcher. Idempotent. The sink begins in
    /// the "signaled" state so its first poll drains any pre-existing buffer
    /// contents before going to sleep.
    /// </summary>
    public void RegisterSink(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_signals.ContainsKey(sinkInstanceId))
            {
                return;
            }
            // initialCount=1, maxCount=1 — binary signal, coalesces extra releases.
            _signals[sinkInstanceId] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>
    /// Deregister a sink. Any sink loop currently waiting on the signal will
    /// observe an ObjectDisposedException on its next wait and must interpret
    /// that as "stop".
    /// </summary>
    public void DeregisterSink(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        SemaphoreSlim? removed;
        lock (_gate)
        {
            if (!_signals.Remove(sinkInstanceId, out removed))
            {
                return;
            }
        }
        removed.Dispose();
    }

    /// <summary>
    /// Wake every registered sink so its publisher loop re-polls the route
    /// buffer. Releases at most one permit per sink; additional concurrent
    /// calls coalesce. NEVER takes a payload — intentional.
    /// </summary>
    public void NotifyAll()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            foreach (var kvp in _signals)
            {
                TryRelease(kvp.Value);
            }
        }
    }

    /// <summary>Wake a single sink. Same coalescing rules as NotifyAll.</summary>
    public void Notify(string sinkInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        SemaphoreSlim? signal;
        lock (_gate)
        {
            if (_disposed || !_signals.TryGetValue(sinkInstanceId, out signal))
            {
                return;
            }
        }
        TryRelease(signal);
    }

    /// <summary>
    /// Await the next wake signal for <paramref name="sinkInstanceId"/>.
    /// Returns when NotifyAll/Notify has been called at least once since the
    /// last wait, or when <paramref name="cancellationToken"/> fires.
    /// </summary>
    public Task WaitForSignalAsync(string sinkInstanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        SemaphoreSlim signal;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_signals.TryGetValue(sinkInstanceId, out var s))
            {
                throw new InvalidOperationException(
                    $"Sink '{sinkInstanceId}' is not registered with this dispatcher.");
            }
            signal = s;
        }
        return signal.WaitAsync(cancellationToken);
    }

    /// <summary>Dispose — releases every signal and clears the table.</summary>
    public void Dispose()
    {
        List<SemaphoreSlim> toDispose;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            toDispose = new List<SemaphoreSlim>(_signals.Values);
            _signals.Clear();
        }
        foreach (var s in toDispose)
        {
            s.Dispose();
        }
    }

    private static void TryRelease(SemaphoreSlim signal)
    {
        try
        {
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Race: another notify slipped in. Already signaled — coalesced.
        }
        catch (ObjectDisposedException)
        {
            // Dispose raced with notify. Drop.
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
