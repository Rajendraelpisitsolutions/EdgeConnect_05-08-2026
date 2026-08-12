// ============================================================================
// File: Generation/SourceGenerationAllocator.cs
// Purpose: Process-wide, runtime-lifetime allocator of monotonic GenerationId
//          values per source-slot id. The per-slot high-water mark survives slot
//          removal (a tombstone), so a source id removed and later re-added in
//          the same process never reuses a prior generation key (review B4).
//          Allocation fails closed on counter exhaustion (no wrap).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §4.
// Slice 0 — gate foundation (unused scaffolding; no runtime wiring).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>
/// Process-wide, runtime-lifetime allocator of monotonic <see cref="GenerationId"/>
/// values per source-slot id. The per-slot high-water mark <b>survives slot
/// removal</b> (a tombstone), so a source instance id that is removed and later
/// re-added in the same process never reuses a prior generation key. Allocation
/// fails closed on counter exhaustion rather than wrapping.
/// </summary>
internal sealed class SourceGenerationAllocator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ulong> _highWaterMarks = new(StringComparer.Ordinal);

    /// <summary>
    /// Allocate the next generation id for <paramref name="sourceSlotId"/>.
    /// Ids are 1-based and strictly increasing for the lifetime of the process,
    /// even across slot removal and re-add.
    /// </summary>
    /// <param name="sourceSlotId">The stable source-slot (instance) id.</param>
    /// <param name="generationId">The allocated id on success; <see cref="GenerationId.None"/> on overflow.</param>
    /// <returns><c>true</c> if an id was allocated; <c>false</c> (fail-closed) if the counter is exhausted.</returns>
    public bool TryAllocateNext(string sourceSlotId, out GenerationId generationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceSlotId);
        lock (_sync)
        {
            var current = _highWaterMarks.TryGetValue(sourceSlotId, out var hw) ? hw : 0UL;
            if (current == ulong.MaxValue)
            {
                generationId = GenerationId.None;
                return false;
            }

            var next = current + 1UL;
            _highWaterMarks[sourceSlotId] = next;
            generationId = new GenerationId(next);
            return true;
        }
    }

    /// <summary>
    /// Test seam: seed a slot's high-water mark so overflow can be exercised
    /// deterministically without 2^64 allocations.
    /// </summary>
    internal void SeedHighWaterMarkForTesting(string sourceSlotId, ulong highWaterMark)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceSlotId);
        lock (_sync)
        {
            _highWaterMarks[sourceSlotId] = highWaterMark;
        }
    }
}
