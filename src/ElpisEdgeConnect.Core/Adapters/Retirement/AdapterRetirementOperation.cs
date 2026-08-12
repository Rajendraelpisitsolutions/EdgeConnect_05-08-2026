// ============================================================================
// File: Adapters/Retirement/AdapterRetirementOperation.cs
// Purpose: The durable handle returned by ISourceRetirement.BeginRetirement.
//          Completion stays observable: it resolves only when the adapter
//          reaches a TERMINAL quiescence determination, and remains pending
//          while cleanup is still in flight (so the host can give up waiting at
//          its deadline yet retain the operation and observe a later resolution).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2).
// Slice 0 — commit 3.0 (inert).
// ============================================================================

using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// A durable, observable retirement handle. <see cref="Snapshot"/> is available
/// immediately; <see cref="Completion"/> resolves to the terminal
/// <see cref="AdapterQuiescenceAttestation"/> only when the adapter has reached a
/// terminal determination, and stays pending while still cleaning up.
/// </summary>
/// <remarks>
/// The host observes <see cref="Completion"/> together with the pump against one
/// absolute monotonic deadline. Deadline expiry is the host's own
/// <c>UnprovenAtDeadline</c> evidence — it does NOT complete or fault this task;
/// the operation is retained so a later physical termination can still resolve it
/// to a proven attestation (clearing the source-id retirement barrier without a
/// process restart).
/// </remarks>
public sealed class AdapterRetirementOperation
{
    /// <summary>The applicable-surface view captured at initiation.</summary>
    public required AdapterRetirementSnapshot Snapshot { get; init; }

    /// <summary>Resolves to the terminal attestation; pending while cleanup is in flight.</summary>
    public required Task<AdapterQuiescenceAttestation> Completion { get; init; }
}
