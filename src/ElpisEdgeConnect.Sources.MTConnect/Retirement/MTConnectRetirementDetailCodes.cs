// ============================================================================
// File: Retirement/MTConnectRetirementDetailCodes.cs
// Purpose: Stable MTConnect retirement detail codes for the pull-adapter
//          quiescence attestation (Worker = in-flight poll drained via the
//          PollQuiescenceGate; callback/background NotApplicable — verified).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// Slice 0 — commit 3.0 (inert; wired at the 3.1 cutover).
// ============================================================================

using ElpisEdgeConnect.Core.Adapters.Retirement;

namespace ElpisEdgeConnect.Sources.MTConnect.Retirement;

/// <summary>Stable MTConnect retirement detail codes.</summary>
internal static class MTConnectRetirementDetailCodes
{
    public static readonly AdapterRetirementDetailCode Initiated = new("MTCONNECT.RETIRE_INITIATED");
    public static readonly AdapterRetirementDetailCode WorkerIdleProven = new("MTCONNECT.RETIRE_POLL_IDLE");
}
