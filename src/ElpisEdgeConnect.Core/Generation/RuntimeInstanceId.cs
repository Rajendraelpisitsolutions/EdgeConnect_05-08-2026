// ============================================================================
// File: Generation/RuntimeInstanceId.cs
// Purpose: Identity of one gateway-process lifetime. Minted once at startup so
//          GenerationKey values never collide across process restarts.
// Reference: docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md §2,
//            docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md (commit 1).
// Slice 0 — gate foundation (unused scaffolding; no runtime wiring).
// ============================================================================

namespace ElpisEdgeConnect.Core.Generation;

/// <summary>
/// Identity of a single gateway-process lifetime. Minted once at process startup
/// so that <see cref="GenerationKey"/> values are unambiguous across restarts:
/// after a reboot a fresh <see cref="RuntimeInstanceId"/> guarantees no
/// generation key from a previous boot can collide with a new one.
/// </summary>
/// <param name="Value">The opaque per-process identifier.</param>
public readonly record struct RuntimeInstanceId(System.Guid Value)
{
    /// <summary>Mint a new, unique runtime-instance identity. Call once per process.</summary>
    public static RuntimeInstanceId New() => new(System.Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() =>
        Value.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}
