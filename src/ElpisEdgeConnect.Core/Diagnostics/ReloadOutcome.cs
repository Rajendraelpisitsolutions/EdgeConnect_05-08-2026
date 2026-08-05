// ============================================================================
// File: Diagnostics/ReloadOutcome.cs
// Purpose: Snapshot of a single hot-reload reconcile attempt — the
//          orchestration-level outcome of converging runtime state
//          onto an applied configuration version. Produced by the
//          M.P2.2 RuntimeReloadCoordinator, consumed by the
//          Management API's apply endpoint via IReloadOutcomeQueue.
//
//          Locked design properties (M.P2.2 phase 3 plan v2, §2 Q1-Q3 + K-N):
//             * Lives in Core.Diagnostics, alongside the
//               IConfigurationFaultRegistry it complements — runtime
//               observation, not configuration intent.
//             * Identity is the new applied version id (NewVersionId).
//               One outcome per Apply.
//             * Observation only (guardrail M): carries data, never
//               actions, callbacks, or control fields. Retry/rollback
//               surfaces must NOT be added here — the architectural
//               pin in §1 of the phase 3 plan protects Phase 2
//               invariants from erosion via this channel.
//             * Status is the orchestration-level outcome. The 3-state
//               enum (Completed / InProgress / Skipped) is locked at
//               v2 (Q3). Faulted / Partial / Cancelled are explicitly
//               REJECTED as top-level statuses — instance-level
//               outcomes belong in FaultedInstances[].
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
//            docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// Milestone: M.P2.2 phase 3
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Orchestration-level outcome of a hot-reload reconcile. Three terminal
/// states; absence of a <see cref="ReloadOutcome"/> on an apply response
/// (i.e. <c>Reload == null</c>) conveys "no reload semantics" — e.g.
/// Management standalone, or tests with no coordinator wired.
/// </summary>
public enum ReloadStatus
{
    /// <summary>
    /// The reconcile finished. AppliedInstances / RestartedInstances /
    /// FaultedInstances are populated.
    /// <para>
    /// <b>Important:</b> this is the <i>orchestration-level</i> outcome —
    /// it does <b>NOT</b> imply all instances are healthy. A reconcile
    /// with N faulted instances is still <c>Completed</c>. Check
    /// <see cref="ReloadOutcome.FaultedInstances"/> for the instance-level
    /// view.
    /// </para>
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The wait window on the apply response elapsed before the reconcile
    /// finished. Instance lists are empty for this status; the operator
    /// polls <c>/diagnostics/configuration-faults</c> for the terminal
    /// state.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// The reconcile for this version was abandoned because a newer
    /// Apply superseded it before the stale reconcile reached the head
    /// of the coordinator's semaphore queue. <see cref="ReloadOutcome.SupersededBy"/>
    /// carries the id of the winning version.
    /// </summary>
    Skipped = 2,
}

/// <summary>
/// Per-instance failure record carried inside a <see cref="ReloadOutcome.FaultedInstances"/>.
/// Mirrors the public surface of <see cref="ConfigurationFault"/> minus
/// the observed-at timestamp — the outcome is itself the timestamp anchor.
/// </summary>
public sealed record FaultedReloadEntry
{
    /// <summary>The id of the instance whose teardown or bring-up failed.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Which kind of configuration entity faulted (Source / Sink / Route).</summary>
    public required ConfigurationFaultKind Kind { get; init; }

    /// <summary>Structured error code (stable across versions for downstream tooling).</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Operator-readable description of what failed.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Snapshot of one hot-reload reconcile attempt. Produced by the
/// RuntimeReloadCoordinator and surfaced through the apply response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Observation only (guardrail M).</b> Carries data, never actions
/// or callbacks. Adding orchestration control fields here is rejected
/// by design — see the architectural pin in §1 of the M.P2.2 phase 3
/// plan v2.
/// </para>
/// </remarks>
public sealed record ReloadOutcome
{
    /// <summary>Orchestration-level outcome.</summary>
    public required ReloadStatus Status { get; init; }

    /// <summary>The configuration version this reconcile targeted.</summary>
    public required ConfigurationVersionId NewVersionId { get; init; }

    /// <summary>
    /// Instances that were freshly started in this reconcile (added,
    /// previously stopped, or restarted as part of a config-change action).
    /// Empty for <see cref="ReloadStatus.InProgress"/> and <see cref="ReloadStatus.Skipped"/>.
    /// </summary>
    public required IReadOnlyList<string> AppliedInstances { get; init; }

    /// <summary>
    /// Instances that were stopped-then-started because their config
    /// changed in a non-runtime-applicable way. Empty for non-Completed
    /// statuses.
    /// </summary>
    public required IReadOnlyList<string> RestartedInstances { get; init; }

    /// <summary>
    /// Instances whose teardown or bring-up failed. Each carries the
    /// error code and message captured at fault time.
    /// </summary>
    public required IReadOnlyList<FaultedReloadEntry> FaultedInstances { get; init; }

    /// <summary>
    /// For <see cref="ReloadStatus.Skipped"/>, the id of the newer version
    /// that superseded this one. Null for other statuses.
    /// </summary>
    public ConfigurationVersionId? SupersededBy { get; init; }

    /// <summary>
    /// Wall-clock duration of the reconcile in milliseconds. Zero for
    /// <see cref="ReloadStatus.Skipped"/> (the reconcile never ran) and
    /// for <see cref="ReloadStatus.InProgress"/> placeholders (use the
    /// wait-window length in that case, set by the caller).
    /// </summary>
    public required long ElapsedMs { get; init; }
}
