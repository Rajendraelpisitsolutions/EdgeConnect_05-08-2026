// ============================================================================
// File: Adapters/Retirement/AdapterRetirementContext.cs
// Purpose: Input the host passes to ISourceRetirement.BeginRetirement.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2).
// Slice 0 — commit 3.0 (inert).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// Context for a retirement operation. The <see cref="ObservationToken"/> signals
/// when the host has stopped actively waiting (its absolute deadline elapsed) — the
/// adapter MAY use it to stop spending effort, but cleanup that is already in flight
/// continues so the durable <c>Completion</c> can still resolve later.
/// </summary>
public sealed record AdapterRetirementContext
{
    /// <summary>The host's deadline-linked observation token (not a hard abort).</summary>
    public required System.Threading.CancellationToken ObservationToken { get; init; }
}
