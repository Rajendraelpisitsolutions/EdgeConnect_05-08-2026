// ============================================================================
// File: Routing/ReplayCoordinator.cs
// Purpose: Per-sink replay-state helper. Owned by SinkPublisher. Tracks the
//          three sink lifecycle phases that matter for replay:
//
//              Running  ← (failure)        →  Degraded
//              Degraded ← (first success)  →  Draining
//              Draining ← (buffer empty)   →  Running
//
//          There is NO separate queue, NO side buffer, and NO target
//          sequence number. The drain phase ends when the per-sink cursor
//          reaches the tail of the route buffer (DequeueBatch returns
//          empty). This is the single source of truth.
//
// Reference: ARCHITECTURE_BLUEPRINT.md §19.5 (Replay), §19.4 (Retry)
// Milestone: C3 Commit 2 (phase 4 — replay coordinator).
//
// Scope rules (phase 4 caution — protect the invariant):
//   * Replay is strictly sequential per sink cursor. The buffer + cursor
//     already enforce this; the coordinator adds no override path.
//   * A recovered sink drains backlog BEFORE resuming hot-path consumption
//     because the sink loop never dequeues out-of-order. Live arrivals that
//     wake the dispatcher mid-drain are physically appended AFTER the
//     backlog in the buffer, so the drain loop naturally sees them last.
//   * The coordinator NEVER short-circuits drain on new data. IsDraining
//     remains true until the worker reports an empty dequeue.
//   * No cursor batching optimization, no multi-sink shared drain — this is
//     a phase-4 guardrail.
// ============================================================================

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Helper state used by <see cref="SinkPublisher"/> to track whether a
/// previously-degraded sink is currently draining its backlog. Tiny on
/// purpose: the buffer and per-sink cursor own the actual replay truth.
/// </summary>
public sealed class ReplayCoordinator
{
    private bool _isDraining;

    /// <summary>
    /// True between the first successful publish after degradation and the
    /// moment the sink catches up (i.e., the route worker observes an empty
    /// dequeue while draining is set).
    /// </summary>
    public bool IsDraining => _isDraining;

    /// <summary>
    /// Mark the sink as entering the drain phase. Called by
    /// <see cref="SinkPublisher"/> on the first successful publish that
    /// follows a degraded period.
    /// </summary>
    public void BeginDrain() => _isDraining = true;

    /// <summary>
    /// Mark the sink as having caught up with the buffer tail. Called when
    /// the route worker observes an empty dequeue while <see cref="IsDraining"/>
    /// is true. Returns <c>true</c> if a drain just completed (so the caller
    /// can fire the recovered event), <c>false</c> if not draining.
    /// </summary>
    public bool TryCompleteDrain()
    {
        if (!_isDraining)
        {
            return false;
        }
        _isDraining = false;
        return true;
    }

    /// <summary>Reset to the initial state (used on route restart).</summary>
    public void Reset() => _isDraining = false;
}
