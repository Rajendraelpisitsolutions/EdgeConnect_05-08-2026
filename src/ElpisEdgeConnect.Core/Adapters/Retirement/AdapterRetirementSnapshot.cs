// ============================================================================
// File: Adapters/Retirement/AdapterRetirementSnapshot.cs
// Purpose: The immediate, pre-completion view of a retirement operation —
//          which surfaces apply, returned synchronously from BeginRetirement so
//          the host knows what the eventual attestation must cover.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2).
// Slice 0 — commit 3.0 (inert).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>The applicable-surface view captured when retirement is initiated.</summary>
public sealed record AdapterRetirementSnapshot
{
    /// <summary>Whether a worker/pump surface applies to this adapter.</summary>
    public required bool WorkerApplicable { get; init; }

    /// <summary>Whether a callback/dispatcher-drain surface applies.</summary>
    public required bool CallbackDrainApplicable { get; init; }

    /// <summary>Whether any background/reconnect/timer/dispatcher work applies.</summary>
    public required bool BackgroundWorkApplicable { get; init; }

    /// <summary>Stable, strongly-typed detail code; optional.</summary>
    public AdapterRetirementDetailCode? DetailCode { get; init; }
}
