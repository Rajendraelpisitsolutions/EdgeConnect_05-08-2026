// ============================================================================
// File: Diagnostics/ReloadOutcomeRegistry.cs
// Purpose: Default in-memory implementation of IReloadOutcomeRegistry.
//          A private lock guards two small dictionaries: the bounded
//          cache of completed outcomes (FIFO on insertion order,
//          capacity 64) and the pending-waiters table keyed by
//          version id. Enqueue is synchronous and never awaits;
//          WaitForAsync awaits a TaskCompletionSource raced against
//          a Task.Delay timeout and a caller-supplied
//          CancellationToken.
//
//          Locked design properties enforced here (M.P2.2 phase 3
//          plan v2 guardrails K-N):
//
//             * K — Cache capacity is Capacity (64). Insertion order
//               is tracked in _insertionOrder so eviction is FIFO; an
//               LRU-like read-bumps-recency policy is intentionally
//               not used (writes are rare, reads are rarer, and a
//               read should not influence what gets evicted).
//             * L — All public methods are sync-completion paths. The
//               only async surface is WaitForAsync, which awaits a
//               TCS task + a Task.Delay — no I/O.
//             * M — The implementation does not invoke any callbacks
//               on the data it stores. It only forwards values through
//               the public surface; no events, no observers.
//             * N — No persistence. The dictionaries are field-only
//               and die with the singleton.
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
// Milestone: M.P2.2 phase 3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// In-memory <see cref="IReloadOutcomeRegistry"/>. Singleton-scoped in DI.
/// </summary>
public sealed class ReloadOutcomeRegistry : IReloadOutcomeRegistry
{
    /// <summary>
    /// Maximum number of completed outcomes retained for late lookups.
    /// On the 64th-plus apply since boot, the oldest outcome is dropped.
    /// Capacity is generous for the operational rate of Applies; the
    /// boundary is pinned by a unit test so future tuning is intentional.
    /// </summary>
    public const int Capacity = 64;

    private readonly object _lock = new();
    private readonly Dictionary<ConfigurationVersionId, ReloadOutcome> _cache = new();
    private readonly Queue<ConfigurationVersionId> _insertionOrder = new();
    private readonly Dictionary<ConfigurationVersionId, TaskCompletionSource<ReloadOutcome>> _pending = new();

    /// <inheritdoc/>
    public void EnqueueCompleted(ReloadOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        TaskCompletionSource<ReloadOutcome>? toComplete;
        lock (_lock)
        {
            if (_cache.ContainsKey(outcome.NewVersionId))
            {
                // Idempotent re-enqueue. First outcome wins; second is dropped.
                return;
            }

            _cache[outcome.NewVersionId] = outcome;
            _insertionOrder.Enqueue(outcome.NewVersionId);

            while (_insertionOrder.Count > Capacity)
            {
                var evicted = _insertionOrder.Dequeue();
                _cache.Remove(evicted);
            }

            _pending.Remove(outcome.NewVersionId, out toComplete);
        }

        // Setting the TCS result outside the lock so any synchronous
        // continuations don't run while we hold _lock. (Guardrail L: the
        // queue must not contaminate caller code paths beyond a brief
        // critical section.)
        toComplete?.TrySetResult(outcome);
    }

    /// <inheritdoc/>
    public void EnqueueSkipped(ConfigurationVersionId staleVersion, ConfigurationVersionId supersededBy)
    {
        var outcome = new ReloadOutcome
        {
            Status = ReloadStatus.Skipped,
            NewVersionId = staleVersion,
            AppliedInstances = Array.Empty<string>(),
            RestartedInstances = Array.Empty<string>(),
            FaultedInstances = Array.Empty<FaultedReloadEntry>(),
            SupersededBy = supersededBy,
            ElapsedMs = 0,
        };
        EnqueueCompleted(outcome);
    }

    /// <inheritdoc/>
    public async Task<ReloadOutcome?> WaitForAsync(
        ConfigurationVersionId versionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<ReloadOutcome> waitTask;
        lock (_lock)
        {
            if (_cache.TryGetValue(versionId, out var cached))
            {
                return cached;
            }

            if (!_pending.TryGetValue(versionId, out var tcs))
            {
                tcs = new TaskCompletionSource<ReloadOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[versionId] = tcs;
            }
            waitTask = tcs.Task;
        }

        // Race the TCS against the wait window and the caller's CT. A
        // linked CTS so we can cancel the Task.Delay once the TCS wins
        // (cleanup, not strictly necessary for correctness).
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = Task.Delay(timeout, linkedCts.Token);
        var winner = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);
        if (winner == waitTask)
        {
            linkedCts.Cancel();
            return await waitTask.ConfigureAwait(false);
        }

        // Timeout or caller cancellation: return null per the interface
        // contract, do not throw. The pending TCS remains; a later
        // EnqueueCompleted will set it and any other waiters (if any)
        // still observe the outcome.
        return null;
    }
}
