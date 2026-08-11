// ============================================================================
// File: Generation/SourceRetirementCapability.cs
// Purpose: Host-side discovery of the ISourceRetirement capability. The instance
//          check is valid BEFORE resourceful initialization ONLY because adapter
//          construction is non-resourceful (transports open in InitializeAsync/
//          StartAsync, not the constructor) — so the host checks the capability
//          on the constructed-but-uninitialized adapter, before InitializeAsync
//          (F1). Absence fails closed via SourceLifecycleBlockReason.
//          RetirementCapabilityUnsupported.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3 (F1),
//            §9a gate 5 (unsupported fails closed).
// Slice 0 — commit 3.0 (inert; not yet consulted by the live supervisor).
// ============================================================================

using System.Diagnostics.CodeAnalysis;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>
/// Discovers whether a constructed source adapter supports durable retirement
/// attestation. Fails closed: an adapter without the capability yields
/// <see cref="SourceLifecycleBlockReason.RetirementCapabilityUnsupported"/> and is
/// never treated as quiescent by method completion.
/// </summary>
internal static class SourceRetirementCapability
{
    /// <summary>
    /// Try to obtain the adapter's retirement capability. MUST be called on the
    /// constructed-but-uninitialized adapter, before <c>InitializeAsync</c>
    /// opens any external resource (F1) — valid because construction is
    /// non-resourceful.
    /// </summary>
    public static bool TryGet(ISourceAdapter adapter, [NotNullWhen(true)] out ISourceRetirement? retirement)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        retirement = adapter as ISourceRetirement;
        return retirement is not null;
    }

    /// <summary>The block reason for an adapter that does not support retirement attestation.</summary>
    public static SourceLifecycleBlockReason UnsupportedReason => SourceLifecycleBlockReason.RetirementCapabilityUnsupported;
}
