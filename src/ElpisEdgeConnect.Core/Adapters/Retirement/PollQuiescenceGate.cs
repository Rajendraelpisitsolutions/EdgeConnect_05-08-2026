// ============================================================================
// File: Adapters/Retirement/PollQuiescenceGate.cs
// Purpose: Reusable quiescence primitive for SUPERVISOR-DRIVEN PULL adapters
//          (MTConnect, Brother HTTP, future HTTP sources). Such adapters own no
//          background loop/timer/thread/coordinator/callback — their only
//          transient execution is an in-flight poll bounded by the caller's
//          CancellationToken. This gate makes that in-flight poll a DURABLE,
//          provable Worker surface: once retirement begins, no new poll may
//          enter, and quiescence is proven only when the in-flight poll (if any)
//          has actually drained. A wedged poll keeps the drain task pending —
//          exactly the silent-stall class the diagnostic workstream targets.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// Linearizes poll admission against retirement for pull adapters. New polls are
/// admitted via <see cref="TryEnterPoll"/> (paired with <see cref="ExitPoll"/>)
/// until <see cref="BeginQuiescingAsync"/> is called; thereafter admission is
/// refused and the returned task completes when the in-flight poll has drained.
/// </summary>
public sealed class PollQuiescenceGate
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource _quiesced =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inFlight;
    private bool _quiescing;

    /// <summary>
    /// Try to admit a poll. Returns <c>false</c> once retirement has begun — the
    /// caller must NOT proceed with the poll. On <c>true</c>, the caller MUST call
    /// <see cref="ExitPoll"/> exactly once (use try/finally).
    /// </summary>
    public bool TryEnterPoll()
    {
        lock (_gate)
        {
            if (_quiescing)
            {
                return false;
            }
            _inFlight++;
            return true;
        }
    }

    /// <summary>
    /// Mark a previously-admitted poll complete. If retirement is in progress and
    /// this was the last in-flight poll, the drain signal is completed.
    /// </summary>
    public void ExitPoll()
    {
        lock (_gate)
        {
            if (_inFlight == 0)
            {
                // Misuse (ExitPoll without a matching admitted TryEnterPoll, or a
                // double-exit). Ignore rather than drive the counter negative — a
                // negative count would otherwise wedge quiescence forever.
                return;
            }

            _inFlight--;
            if (_quiescing && _inFlight == 0)
            {
                _quiesced.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Begin quiescing: refuse new polls and return a task that completes only when
    /// the in-flight poll (if any) has drained. Idempotent — repeated calls return
    /// the same drain task. Because admission and this transition share one lock, a
    /// poll can never slip past the guard after the drain task has completed.
    /// </summary>
    public Task BeginQuiescingAsync()
    {
        lock (_gate)
        {
            _quiescing = true;
            if (_inFlight == 0)
            {
                _quiesced.TrySetResult();
            }
            return _quiesced.Task;
        }
    }
}
