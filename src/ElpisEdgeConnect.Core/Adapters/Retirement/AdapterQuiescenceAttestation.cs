// ============================================================================
// File: Adapters/Retirement/AdapterQuiescenceAttestation.cs
// Purpose: The adapter's terminal quiescence determination, covering EVERY
//          execution surface (worker + callback drain + background/reconnect),
//          not just a worker. Produced by the retirement operation's Completion
//          only when the adapter reaches a terminal determination; a still-
//          cleaning-up adapter leaves Completion pending instead.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2).
// Slice 0 — commit 3.0 (inert).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// An adapter's terminal quiescence determination across all its execution
/// surfaces. <see cref="IsFullyProven"/> is the only thing that admits a
/// replacement generation (the supervisor never manufactures this from method
/// completion). A surface that is missing/unknown must be reported
/// <see cref="AdapterSurfaceState.Unproven"/> — fail closed.
/// </summary>
public sealed record AdapterQuiescenceAttestation
{
    /// <summary>Poll/pump/worker surface state.</summary>
    public required AdapterSurfaceState Worker { get; init; }

    /// <summary>Callback/dispatcher-drain surface state (e.g. OPC UA notifications).</summary>
    public required AdapterSurfaceState CallbackDrain { get; init; }

    /// <summary>
    /// <b>All other adapter-owned work creators</b> — reconnect loops, timers,
    /// dispatchers, and anything not covered by <see cref="Worker"/> /
    /// <see cref="CallbackDrain"/>. Unknown or unenumerated work must fail closed
    /// (<see cref="AdapterSurfaceState.Unproven"/>), never silently omitted.
    /// </summary>
    public required AdapterSurfaceState BackgroundWork { get; init; }

    /// <summary>Stable, strongly-typed detail code; optional.</summary>
    public AdapterRetirementDetailCode? DetailCode { get; init; }

    /// <summary>
    /// True only when no surface is <see cref="AdapterSurfaceState.Unproven"/>
    /// (every applicable surface terminated; the rest not applicable).
    /// </summary>
    public bool IsFullyProven =>
        Worker != AdapterSurfaceState.Unproven
        && CallbackDrain != AdapterSurfaceState.Unproven
        && BackgroundWork != AdapterSurfaceState.Unproven;
}
