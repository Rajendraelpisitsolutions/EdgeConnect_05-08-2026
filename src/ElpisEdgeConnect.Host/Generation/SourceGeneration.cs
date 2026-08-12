// ============================================================================
// File: Generation/SourceGeneration.cs
// Purpose: Per-generation runtime-correctness holder: the lease, the
//          generation-scoped intake writer, the two orthogonal state axes
//          (authority vs retirement/cleanup), and the quiescence completion.
//          The adapter instance and pump task are attached by the supervisor at
//          the cutover (commit 3); this type carries only the fencing state.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §1, §8.
// Slice 0 — commit 2 scaffolding (unused).
// ============================================================================

using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Host.Generation;

/// <summary>One adapter execution lifetime for a <see cref="SourceSlot"/>.</summary>
internal sealed class SourceGeneration
{
    internal SourceGeneration(GenerationLease lease, GenerationScopedIntakeWriter<CanonicalDataPoint> writer)
    {
        Lease = lease;
        Writer = writer;
        AuthorityState = AuthorityState.Authorized;
        RetirementState = RetirementState.None;
        // Baseline applicability (pump + adapter stop/dispose). The supervisor
        // adds CallbackDrain for subscription adapters at the cutover.
        RetirementCompletion = new GenerationRetirementCompletion(
            QuiescenceComponents.Pump | QuiescenceComponents.AdapterStop);
    }

    /// <summary>The generation's publish-authority lease.</summary>
    public GenerationLease Lease { get; }

    /// <summary>The generation's correlation key.</summary>
    public GenerationKey Key => Lease.Key;

    /// <summary>The generation-fenced writer into the slot's stable channel.</summary>
    public GenerationScopedIntakeWriter<CanonicalDataPoint> Writer { get; }

    /// <summary>Whether this generation may affect current runtime state.</summary>
    public AuthorityState AuthorityState { get; private set; }

    /// <summary>The cleanup/retirement outcome, orthogonal to <see cref="AuthorityState"/>.</summary>
    public RetirementState RetirementState { get; private set; }

    /// <summary>The composite quiescence evidence for this generation's teardown.</summary>
    public GenerationRetirementCompletion RetirementCompletion { get; }

    internal void MarkRetired() => AuthorityState = AuthorityState.Retired;

    internal void SetRetirementState(RetirementState state) => RetirementState = state;
}
