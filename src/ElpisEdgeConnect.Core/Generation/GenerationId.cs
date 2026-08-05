// ============================================================================
// File: Generation/GenerationId.cs
// Purpose: Monotonic per-source-slot generation counter value. Allocation,
//          monotonicity, and overflow handling are owned by the host allocator.
// Reference: docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md §2.
// Slice 0 — gate foundation (unused scaffolding).
// ============================================================================

namespace ElpisEdgeConnect.Core.Generation;

/// <summary>
/// Monotonic per-source-slot generation counter value (1-based; <see cref="None"/>
/// is the zero sentinel for "no generation"). Allocation, monotonicity, and
/// overflow handling are owned by the host generation allocator, not this value
/// type.
/// </summary>
/// <param name="Value">The 1-based counter value.</param>
public readonly record struct GenerationId(ulong Value)
{
    /// <summary>The zero sentinel meaning "no generation".</summary>
    public static GenerationId None => new(0UL);

    /// <inheritdoc/>
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
