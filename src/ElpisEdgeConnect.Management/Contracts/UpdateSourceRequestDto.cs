// ============================================================================
// File: Contracts/UpdateSourceRequestDto.cs
// Purpose: PUT /api/v1/sources/{instanceId} request body. Edit-mode wizard
//          save round-trip from M.2d.2 v2 §5.5 — carries the updated
//          SourceInstanceConfig + the BaseVersionId captured at Edit
//          hydration time so the server can detect stale-view collisions
//          and reject with 409 + ConfigVersionMismatchDto.
//
//          Lives under Contracts/ (sibling to SourceListItemDto) per the
//          v2 §0.1 Q4 verdict — the matching API surface
//          (SourcesUpdateApi.cs) is a sibling under Api/.
// Reference: docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md §2 (locked DTOs)
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Body of <c>PUT /api/v1/sources/{instanceId}</c>: the wizard's
/// updated <see cref="SourceInstanceConfig"/> plus the optimistic-
/// concurrency token captured at Edit hydration time.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint compares <see cref="BaseVersionId"/> against
/// <c>IConfigurationManager.CurrentVersionId</c>; a mismatch returns
/// 409 + <c>ConfigVersionMismatchDto</c> (with <c>ConflictType =
/// "VersionMismatch"</c> per v2 §0.2). A match proceeds through
/// <c>WizardConfigMerger.BuildUpdatedSourceDraft</c> → draft create →
/// apply, returning the same <c>ApplyResultDto</c> shape as the
/// existing draft-apply endpoint.
/// </para>
/// <para>
/// <see cref="SourceConfig"/>.<c>InstanceId</c> MUST match the route
/// parameter; mismatches return 400 (v2 §2.7 verdict — the route is
/// truth, the body is a signature of operator intent, a mismatch is a
/// programming error worth surfacing).
/// </para>
/// </remarks>
public sealed record UpdateSourceRequestDto
{
    /// <summary>
    /// Updated source configuration. <c>InstanceId</c> must match the
    /// route parameter; <c>ProtocolName</c> must match the existing
    /// source's protocol (Edit mode is immutable on both per v2 §5.4).
    /// </summary>
    public required SourceInstanceConfig SourceConfig { get; init; }

    /// <summary>
    /// Optimistic-concurrency token: the value of
    /// <c>IConfigurationManager.CurrentVersionId</c> at the moment the
    /// wizard hydrated the source. The server rejects the save with a
    /// 409 + <c>ConfigVersionMismatchDto</c> when this does not match
    /// the manager's current version at save time.
    /// </summary>
    public required string BaseVersionId { get; init; }

    /// <summary>
    /// Optional actor for the audit entry. Defaults to <c>"system"</c>
    /// when omitted — matches the existing draft-create behaviour.
    /// </summary>
    public string? Actor { get; init; }
}
