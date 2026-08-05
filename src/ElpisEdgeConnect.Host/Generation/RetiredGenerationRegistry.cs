// ============================================================================
// File: Generation/RetiredGenerationRegistry.cs
// Purpose: Process/slot-lifetime accounting for retired generations whose work
//          did not quiesce within the cleanup deadline (review B3, §8). Tracks
//          the ACTIVE quarantined/orphaned set (increments on transition,
//          decrements on proven completion) plus CUMULATIVE quarantine/orphan
//          totals (lifetime events that a late completion never erases).
//          Transition driving (deadlines, task continuations) is the supervisor's
//          job at the cutover; this type is the deterministic accounting core.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §3, §8.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>
/// Accounting for retired generations whose cleanup exceeded its deadline.
/// </summary>
internal sealed class RetiredGenerationRegistry
{
    private readonly object _sync = new();
    private readonly HashSet<GenerationKey> _active = new();
    private long _cumulativeQuarantineTotal;
    private long _cumulativeOrphanTotal;

    /// <summary>Count of generations currently quarantined or orphaned (not yet proven complete).</summary>
    public int ActiveCount
    {
        get { lock (_sync) { return _active.Count; } }
    }

    /// <summary>Lifetime count of quarantine transitions.</summary>
    public long CumulativeQuarantineTotal
    {
        get { lock (_sync) { return _cumulativeQuarantineTotal; } }
    }

    /// <summary>Lifetime count of orphan transitions.</summary>
    public long CumulativeOrphanTotal
    {
        get { lock (_sync) { return _cumulativeOrphanTotal; } }
    }

    /// <summary>Record that a retired generation's cleanup did not complete in time (quarantined).</summary>
    public void Quarantine(GenerationKey key)
    {
        lock (_sync)
        {
            if (_active.Add(key))
            {
                _cumulativeQuarantineTotal++;
            }
        }
    }

    /// <summary>Record that a quarantined generation is still active past the orphan deadline.</summary>
    public void MarkOrphaned(GenerationKey key)
    {
        lock (_sync)
        {
            _active.Add(key);
            _cumulativeOrphanTotal++;
        }
    }

    /// <summary>
    /// Record proven completion of a quarantined/orphaned generation: it leaves
    /// the active set, but the cumulative lifetime totals are preserved.
    /// </summary>
    /// <returns><c>true</c> if the generation was in the active set.</returns>
    public bool MarkCompleted(GenerationKey key)
    {
        lock (_sync)
        {
            return _active.Remove(key);
        }
    }
}
