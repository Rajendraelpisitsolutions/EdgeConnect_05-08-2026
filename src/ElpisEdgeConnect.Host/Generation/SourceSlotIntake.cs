// ============================================================================
// File: Generation/SourceSlotIntake.cs
// Purpose: The stable, slot-owned ISourceIntake the routing engine binds to.
//          Backed by the slot's channel reader, it SURVIVES generation swaps —
//          a restart/reconfigure replaces the generation, not the intake, so a
//          bound route never goes stale (the structural M1 fix).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §0, §1.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

using System.Threading.Channels;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>Stable <see cref="ISourceIntake"/> backed by the slot channel reader; survives generations.</summary>
internal sealed class SourceSlotIntake : ISourceIntake
{
    internal SourceSlotIntake(string sourceInstanceId, ChannelReader<CanonicalDataPoint> reader)
    {
        SourceInstanceId = sourceInstanceId;
        Reader = reader;
    }

    /// <inheritdoc/>
    public string SourceInstanceId { get; }

    /// <inheritdoc/>
    public ChannelReader<CanonicalDataPoint> Reader { get; }
}
