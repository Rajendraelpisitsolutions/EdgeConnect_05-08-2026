// ============================================================================
// File: Focas2Thread.cs
// Purpose: Dedicated thread with a work queue that guarantees all FOCAS2
//          P/Invoke calls run on the same OS thread. Required because the
//          G342 (0i-TF Plus) binds handles to their allocating thread.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2, Section 10
//
// LOCKED BEHAVIOR:
//   Per-adapter isolation — one Focas2Thread per source instance.
// ============================================================================

using System.Collections.Concurrent;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Runs a dedicated background thread that pumps work items from a
/// <see cref="BlockingCollection{T}"/>. All FOCAS2 calls for a single
/// adapter instance are dispatched through this thread to satisfy
/// the native library's thread-affinity requirement.
/// </summary>
internal sealed class Focas2Thread : IAsyncDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<WorkItem> _queue = new();
    private volatile bool _disposed;

    // Slice 0 commit 3.0 retirement: completes when the dedicated thread has
    // ACTUALLY exited its pump loop (true termination, not a Join timeout).
    private readonly TaskCompletionSource _threadExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Captured by the affine final-cleanup wrapper (FOCAS-G2); read only by the
    // pump thread at loop exit, so no synchronization is required.
    private Exception? _shutdownCleanupException;

    /// <summary>
    /// Create and start the dedicated FOCAS2 thread.
    /// </summary>
    /// <param name="instanceId">Adapter instance id, used for thread naming.</param>
    public Focas2Thread(string instanceId)
    {
        _thread = new Thread(PumpLoop)
        {
            Name = $"Focas2-{instanceId}",
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <summary>The managed thread id of the dedicated thread.</summary>
    internal int ManagedThreadId => _thread.ManagedThreadId;

    /// <summary>
    /// Dispatch a function to the dedicated thread and return its result.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // If already cancelled, short-circuit.
        if (ct.IsCancellationRequested)
        {
            tcs.SetCanceled(ct);
            return tcs.Task;
        }

        CancellationTokenRegistration ctr = default;
        if (ct.CanBeCanceled)
        {
            ctr = ct.Register(() => tcs.TrySetCanceled(ct));
        }

        var item = new WorkItem(() =>
        {
            // If cancelled before we dequeued, skip execution.
            if (tcs.Task.IsCanceled)
            {
                ctr.Dispose();
                return;
            }

            try
            {
                var result = work();
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException oce)
            {
                tcs.TrySetCanceled(oce.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                ctr.Dispose();
            }
        });

        try
        {
            if (!_queue.TryAdd(item))
            {
                tcs.TrySetException(new ObjectDisposedException(nameof(Focas2Thread)));
                ctr.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
            // The queue is completing (shutdown has begun): reject — no new fwlib
            // work is enqueued or executed after shutdown (FOCAS-G3).
            tcs.TrySetException(new ObjectDisposedException(nameof(Focas2Thread)));
            ctr.Dispose();
        }

        return tcs.Task;
    }

    /// <summary>
    /// Dispatch a void action to the dedicated thread.
    /// </summary>
    public Task RunAsync(Action work, CancellationToken ct = default)
    {
        return RunAsync<object?>(() =>
        {
            work();
            return null;
        }, ct);
    }

    /// <summary>
    /// A task that completes when the dedicated thread has ACTUALLY exited its
    /// pump loop — true termination, not a <see cref="Thread.Join(int)"/> timeout.
    /// Stays pending while a native fwlib call is wedged on the thread. This is the
    /// Slice 0 commit-3.0 retirement worker-quiescence proof.
    /// </summary>
    public Task WaitForThreadExitAsync() => _threadExited.Task;

    /// <summary>
    /// Signal the dedicated thread to exit after draining: optionally enqueue one
    /// final work item (e.g. free the handle ON the affine thread), then complete
    /// the queue. Non-blocking and thread-affinity-safe; it does NOT free native
    /// resources from another thread (unsafe while a native call is in flight) and
    /// it does NOT wait for the thread to exit.
    /// </summary>
    public void BeginShutdown(Action? finalWork)
    {
        try
        {
            if (finalWork is not null)
            {
                // Wrap so a final-cleanup exception is captured (the thread never
                // crashes) and mapped to a terminal Unproven attestation (FOCAS-G2).
                // TryAdd returns false on a second BeginShutdown (queue already
                // completing), so the final cleanup is enqueued at most once (FOCAS-G1).
                _queue.TryAdd(new WorkItem(() =>
                {
                    try
                    {
                        finalWork();
                    }
                    catch (Exception ex)
                    {
                        _shutdownCleanupException = ex;
                    }
                }));
            }
            _queue.CompleteAdding();
        }
        catch (InvalidOperationException)
        {
            // The queue is already completing (a prior BeginShutdown) or disposed
            // (ObjectDisposedException derives from InvalidOperationException). TryAdd
            // throws here, which is exactly how the final cleanup stays enqueued at
            // most once (FOCAS-G1). Nothing more to do.
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // Wait for the thread to drain. Use a bounded timeout to avoid
        // hanging forever if the thread is stuck in a native call.
        var drained = !_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(10));

        if (drained)
        {
            _queue.Dispose();
        }

        // If the Join TIMED OUT, the pump thread is still blocked inside a native
        // FOCAS call and will call MoveNext() on the queue the moment that call
        // returns. Disposing here made that MoveNext() throw ObjectDisposedException
        // on a thread with no exception handler, which terminates the PROCESS —
        // observed taking the whole gateway down when the demo-licence cutoff
        // stopped sources while a connect to an unreachable CNC was in flight
        // (5 attempts x 10s timeout comfortably outlasts this 10s Join).
        //
        // Leaking one BlockingCollection is the correct trade against killing every
        // other adapter (locked decision #10, per-adapter isolation). The thread is
        // a background thread, so it cannot keep the process alive on shutdown, and
        // CompleteAdding above guarantees the loop ends once the native call returns.
        return ValueTask.CompletedTask;
    }

    private void PumpLoop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                item.Execute();
            }
        }
        catch (ObjectDisposedException)
        {
            // Belt-and-braces companion to the Join-timeout handling in
            // DisposeAsync. This runs on a dedicated thread with no ambient
            // handler, so an escaping exception here does not fault a Task — it
            // terminates the process and takes every other adapter with it.
            // A disposed queue means shutdown, which is exactly what this loop
            // was about to do anyway, so fall through to the exit signal below.
        }

        // The thread has truly exited the pump loop — true termination, not a
        // Join timeout. This is the Slice 0 retirement quiescence signal: a
        // wedged native call keeps the foreach blocked inside item.Execute(), so
        // this never fires until the native call actually returns. If the affine
        // final cleanup threw, surface it as a faulted exit (mapped to terminal
        // Unproven by retirement, FOCAS-G2); otherwise signal a clean exit.
        if (_shutdownCleanupException is { } cleanupException)
        {
            _threadExited.TrySetException(cleanupException);
        }
        else
        {
            _threadExited.TrySetResult();
        }
    }

    /// <summary>Simple work item wrapper.</summary>
    private readonly struct WorkItem
    {
        private readonly Action _action;

        public WorkItem(Action action) => _action = action;

        public void Execute() => _action();
    }
}
