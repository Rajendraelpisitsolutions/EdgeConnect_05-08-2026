// ============================================================================
// File: Generation/GenerationLease.cs
// Purpose: A one-shot, gate-owned capability representing one source
//          generation's right to publish. Minted by exactly one SourceSlotGate;
//          cannot be forged from a GenerationKey; authorizable at most once;
//          never leaves the Retired state (review G1).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §1.
// Slice 0 — gate foundation (unused scaffolding; no runtime wiring).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Lease lifecycle states. Strictly one-way: Issued → Authorized → Retired.</summary>
internal enum GenerationLeaseState
{
    Issued = 0,
    Authorized = 1,
    Retired = 2,
}

/// <summary>
/// A one-shot, gate-owned capability representing one source generation's right
/// to publish. A lease is minted by exactly one <see cref="SourceSlotGate"/>,
/// cannot be forged from a <see cref="GenerationKey"/>, can be authorized at
/// most once, and can never leave the retired state. All state transitions are
/// performed by the owning gate under its synchronization boundary; an adapter
/// receives (at most) the immutable <see cref="Key"/> — never the gate or any
/// ability to mutate authority.
/// </summary>
internal sealed class GenerationLease
{
    private readonly SourceSlotGate _gate;
    private GenerationLeaseState _state;

    internal GenerationLease(SourceSlotGate gate, GenerationKey key)
    {
        _gate = gate;
        Key = key;
        _state = GenerationLeaseState.Issued;
    }

    /// <summary>The immutable correlation identity of this generation.</summary>
    public GenerationKey Key { get; }

    // ── Gate-only surface (read/mutated exclusively under the gate lock) ──
    internal bool IsMintedBy(SourceSlotGate gate) => ReferenceEquals(_gate, gate);

    internal GenerationLeaseState State => _state;

    internal void MarkAuthorized() => _state = GenerationLeaseState.Authorized;

    internal void MarkRetired() => _state = GenerationLeaseState.Retired;
}
