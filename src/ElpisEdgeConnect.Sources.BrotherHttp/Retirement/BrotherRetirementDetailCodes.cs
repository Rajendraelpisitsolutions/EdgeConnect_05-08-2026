// ============================================================================
// File: Retirement/BrotherRetirementDetailCodes.cs
// Purpose: Stable Brother HTTP retirement detail codes for the pull-adapter
//          quiescence attestation (Worker = in-flight poll drained via the
//          PollQuiescenceGate; callback/background NotApplicable — verified).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Retirement;

/// <summary>Stable Brother HTTP retirement detail codes.</summary>
internal static class BrotherRetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("BROTHER.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode WorkerIdleProven = new("BROTHER.RETIRE_POLL_IDLE");
}
