// ============================================================================
// File: Generation/GenerationKey.cs
// Purpose: Correlation identity of one source generation, written to every
//          generation-scoped snapshot and event.
// Reference: docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md §2.
// Slice 0 — gate foundation (unused scaffolding).
// ============================================================================

namespace ElpisEdgeConnect.Core.Generation;

/// <summary>
/// The correlation identity of one source generation:
/// <c>(RuntimeInstanceId, SourceSlotId, GenerationId)</c>. Written to every
/// generation-scoped snapshot and event so current and retired work stay
/// attributable and never collide — including across process restarts (via
/// <see cref="RuntimeInstanceId"/>) and same-process slot remove/re-add (via the
/// host allocator's runtime-lifetime high-water mark).
/// </summary>
/// <param name="RuntimeInstanceId">Identity of the owning process lifetime.</param>
/// <param name="SourceSlotId">Stable source-slot (instance) id.</param>
/// <param name="GenerationId">Monotonic generation counter within the slot.</param>
public readonly record struct GenerationKey(
    RuntimeInstanceId RuntimeInstanceId,
    string SourceSlotId,
    GenerationId GenerationId);
