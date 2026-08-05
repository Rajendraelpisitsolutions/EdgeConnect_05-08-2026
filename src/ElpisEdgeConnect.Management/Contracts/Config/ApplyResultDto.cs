// ============================================================================
// File: Contracts/Config/ApplyResultDto.cs
// Purpose: Wire-shape result for POST /apply (and POST /rollback)
//          on successful application. Pulls the new version id,
//          change list, and audit metadata out of
//          Core.Configuration.ConfigurationApplyResult.
//
//          On failure the endpoint returns 409 with the body shaped
//          as ValidationResultDto instead — operators get one shape
//          for "what's wrong" and another for "what happened."
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// Successful-apply outcome. Carries the new current version id, the
/// diff that was applied, and audit metadata (actor + timestamp).
/// </summary>
public sealed record ApplyResultDto
{
    /// <summary>The new current configuration version id.</summary>
    public required string NewVersionId { get; init; }

    /// <summary>The previous current version id (the one being replaced). Empty for first apply.</summary>
    public required string PreviousVersionId { get; init; }

    /// <summary>UTC timestamp the apply landed.</summary>
    public required DateTime AppliedAtUtc { get; init; }

    /// <summary>Actor who triggered the apply (from the request body, defaulted to <c>"system"</c>).</summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Structured diff produced by <c>ConfigurationDiffer</c>. Same shape
    /// as the <c>/diff</c> endpoint returns prior to apply, so the UI's
    /// preview matches the post-apply confirmation byte-for-byte.
    /// </summary>
    public required IReadOnlyList<ConfigChangeDto> Changes { get; init; }

    /// <summary>
    /// Validation warnings carried through from the apply pipeline.
    /// Empty when there were none; populated when validation passed
    /// but flagged non-blocking issues (e.g. license-grace warnings).
    /// </summary>
    public required IReadOnlyList<ValidationProblemDto> Warnings { get; init; }

    /// <summary>Gateway identity at apply time (federation origin stamp).</summary>
    public string? GatewayId { get; init; }

    /// <summary>
    /// M.P2.2 phase 3 hot-reload reconcile outcome correlated to
    /// <see cref="NewVersionId"/>. Populated when the gateway's
    /// <c>IReloadOutcomeRegistry</c> resolves an outcome inside the
    /// apply endpoint's bounded wait window
    /// (<c>HostOptions.ReloadOutcomeWaitMs</c>); the response carries
    /// <c>Status = "InProgress"</c> when the window elapses first.
    /// <para>
    /// <b>Null distinct from InProgress.</b>
    /// <list type="bullet">
    ///   <item><c>null</c> — runtime reload observation unavailable
    ///       (Management standalone, in-process tests with no coordinator).</item>
    ///   <item><c>Status = "InProgress"</c> — reload still executing;
    ///       operator polls <c>/diagnostics/configuration-faults</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReloadOutcomeDto? Reload { get; init; }
}
