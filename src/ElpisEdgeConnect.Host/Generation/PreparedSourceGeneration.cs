// ============================================================================
// File: Generation/PreparedSourceGeneration.cs
// Purpose: A generation candidate whose id is consumed and whose scoped writer
//          is built, but which is NOT yet current/authorized (review C2-1). This
//          is the "issue → initialize while unauthorized → atomically activate"
//          seam: the adapter is initialized against a prepared candidate; only
//          SourceSlot.TryActivate makes it current.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §6.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>
/// A prepared-but-unauthorized generation candidate. Its id is already consumed
/// (so an initialization failure still advances the allocator), but it holds no
/// publish authority until <see cref="SourceSlot.TryActivate"/>.
/// </summary>
internal sealed class PreparedSourceGeneration
{
    internal PreparedSourceGeneration(GenerationLease lease, GenerationScopedIntakeWriter<CanonicalDataPoint> writer)
    {
        Lease = lease;
        Writer = writer;
    }

    /// <summary>The candidate's (unauthorized) lease.</summary>
    public GenerationLease Lease { get; }

    /// <summary>The candidate's correlation key.</summary>
    public GenerationKey Key => Lease.Key;

    /// <summary>The generation-scoped writer bound to this candidate.</summary>
    internal GenerationScopedIntakeWriter<CanonicalDataPoint> Writer { get; }
}
