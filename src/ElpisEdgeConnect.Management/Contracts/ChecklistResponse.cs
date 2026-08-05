// ============================================================================
// File: Contracts/ChecklistResponse.cs
// Purpose: Envelope returned from /api/v1/checklist. Wraps the list of
//          checklist items with a summary, evaluation timing, and
//          gateway-identity origin stamp.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1d
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Roll-up counts plus a single boolean operators key off:
/// "are we ready to hand this gateway over?"
/// </summary>
public sealed record ChecklistSummary
{
    /// <summary>Total checks evaluated (Pass + Fail + NotApplicable + Pending).</summary>
    public required int Total { get; init; }

    /// <summary>Count of checks evaluated <see cref="ChecklistStatus.Pass"/>.</summary>
    public required int Pass { get; init; }

    /// <summary>Count of checks evaluated <see cref="ChecklistStatus.Fail"/> — operator action required.</summary>
    public required int Fail { get; init; }

    /// <summary>Count of checks evaluated <see cref="ChecklistStatus.NotApplicable"/> — doesn't apply to this gateway's topology.</summary>
    public required int NotApplicable { get; init; }

    /// <summary>Count of checks evaluated <see cref="ChecklistStatus.Pending"/> — insufficient data yet, retry later.</summary>
    public required int Pending { get; init; }

    /// <summary>
    /// True when no checks failed AND no checks are pending — the
    /// gateway is operationally ready for handover. NotApplicable
    /// checks don't count against readiness; they reflect topology
    /// rather than problems.
    /// </summary>
    public bool Ready => Fail == 0 && Pending == 0;
}

/// <summary>
/// Wrapped response from <c>GET /api/v1/checklist</c>. Carries the per-
/// check evaluations plus a roll-up summary, evaluation timing, and
/// the originating gateway's identity.
/// </summary>
/// <remarks>
/// <para>Reserved for future expansion:</para>
/// <list type="bullet">
///   <item><c>TimedOut</c> — boolean flag set when one or more checks
///         gave up after exceeding a per-check timeout. Reserved for
///         when distributed / federation / HA-peer checks are added;
///         today all checks are synchronous in-memory reads with no
///         timeout logic, so the field has no producer.</item>
///   <item><c>SchemaVersion</c> — integer pinning the checklist catalog
///         version. Reserved for when exports, handover artifacts, or
///         historical comparisons need it; today the catalog version
///         is implicit in the EdgeConnect runtime version exposed via
///         <c>BackupManifest.ProductVersion</c> on backup downloads.</item>
/// </list>
/// <para>Both follow the established "ship contracts only when
/// producers exist" pattern from M.1c.* — documented intent here,
/// fields added when first producer lands.</para>
/// </remarks>
public sealed record ChecklistResponse
{
    /// <summary>Per-check evaluations, in stable catalog order.</summary>
    public required IReadOnlyList<ChecklistItemDto> Items { get; init; }

    /// <summary>Roll-up counts + the <c>Ready</c> boolean.</summary>
    public required ChecklistSummary Summary { get; init; }

    /// <summary>UTC timestamp the evaluation pass ran.</summary>
    public required DateTime CheckedAtUtc { get; init; }

    /// <summary>
    /// Wall-clock duration of the full evaluation, in milliseconds.
    /// Useful for noticing when checks start to slow down (e.g. audit-
    /// chain verification scaling linearly with log size). Null only
    /// if the evaluator didn't measure (defensive — should always be
    /// populated by <c>ChecklistEvaluator</c>).
    /// </summary>
    public int? EvaluationDurationMs { get; init; }

    /// <summary>
    /// Gateway identity at evaluation time (same convention as
    /// DiagnosticsEventDto.GatewayId). Critical for future fleet
    /// aggregation where multiple gateway checklists are merged.
    /// </summary>
    public string? GatewayId { get; init; }
}
