// ============================================================================
// File: Contracts/Config/ReloadOutcomeDto.cs
// Purpose: Wire-shape mirror of Core.Diagnostics.ReloadOutcome. Carried
//          inside ApplyResultDto.Reload to surface the hot-reload
//          reconcile outcome to the operator alongside the apply
//          response (M.P2.2 phase 3).
//
//          DTO values:
//             * Status                            — "Completed" | "InProgress" | "Skipped"
//             * Kind (on FaultedReloadEntryDto)   — "Source" | "Sink" | "Route"
//
//          Wire-stable strings rather than serialized enums — the
//          mapper translates Core enum values, so additions to the
//          Core enum do not silently leak through to API consumers.
//
//          ApplyResultDto.Reload is null when:
//             * Management standalone (no IReloadOutcomeRegistry registered)
//             * In-process tests with no coordinator wired
//          That null is semantically distinct from Status="InProgress":
//             null       = runtime reload observation unavailable
//             InProgress = runtime reload still executing (wait window
//                          elapsed before reconcile finished)
//
//          Observation only (phase 3 guardrail M): no embedded actions,
//          callbacks, or control fields. Retry / rollback surfaces must
//          NOT be added here.
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// One per-instance failure observed during a hot-reload reconcile.
/// Mirrors <c>Core.Diagnostics.FaultedReloadEntry</c> for the wire.
/// </summary>
public sealed record FaultedReloadEntryDto
{
    /// <summary>The instance id (or route id) whose teardown or bring-up failed.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Configuration entity kind: <c>"Source"</c>, <c>"Sink"</c>, or <c>"Route"</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Structured error code (e.g. <c>"HOST.RECONCILE_FAILED"</c>, <c>"HOST.RECONCILE_TIMEOUT"</c>).</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Operator-readable description of what failed.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Hot-reload reconcile outcome correlated to a specific applied version.
/// Carried by <see cref="ApplyResultDto.Reload"/> on apply and rollback
/// success responses.
/// </summary>
public sealed record ReloadOutcomeDto
{
    /// <summary>
    /// Orchestration-level outcome: <c>"Completed"</c>, <c>"InProgress"</c>,
    /// or <c>"Skipped"</c>. <c>Completed</c> is the orchestration outcome —
    /// it does NOT imply all instances healthy; check
    /// <see cref="FaultedInstances"/> for the instance-level view.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The configuration version this reconcile targeted. Matches
    /// <see cref="ApplyResultDto.NewVersionId"/> for completed reconciles;
    /// for <c>Skipped</c> it is the abandoned version's id.
    /// </summary>
    public required string NewVersionId { get; init; }

    /// <summary>
    /// Instance ids that were freshly started in this reconcile (Add
    /// action). Empty for <c>InProgress</c> and <c>Skipped</c>.
    /// </summary>
    public required IReadOnlyList<string> AppliedInstances { get; init; }

    /// <summary>
    /// Instance ids that were stopped-then-started because their config
    /// changed in a non-runtime-applicable way (Restart action). Empty
    /// for non-Completed statuses.
    /// </summary>
    public required IReadOnlyList<string> RestartedInstances { get; init; }

    /// <summary>
    /// Per-instance fault entries observed during reconcile execution.
    /// May be partial — the authoritative runtime-fault surface is
    /// <c>/diagnostics/configuration-faults</c>.
    /// </summary>
    public required IReadOnlyList<FaultedReloadEntryDto> FaultedInstances { get; init; }

    /// <summary>
    /// For <c>Skipped</c> status, the id of the newer version that
    /// superseded this one. Null for other statuses.
    /// </summary>
    public string? SupersededBy { get; init; }

    /// <summary>
    /// Reconcile execution time in milliseconds. Excludes semaphore
    /// queue-wait time before this reconcile acquired the gate.
    /// </summary>
    public required long ElapsedMs { get; init; }
}
