// ============================================================================
// File: Contracts/UpdateRouteRequestDto.cs
// Purpose: PUT /api/v1/routes/{routeId} request body. Edit-mode wizard
//          save round-trip from M.2d.3 v2 §3.1 — carries the updated
//          RouteConfig + the BaseVersionId captured at Edit hydration
//          time so the server can detect stale-view collisions and
//          reject with 409 + ConfigVersionMismatchDto.
//
//          Mirrors UpdateSourceRequestDto exactly — same optimistic-
//          concurrency and audit-actor surface, different payload type.
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3.1
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Body of <c>PUT /api/v1/routes/{routeId}</c>: the wizard's
/// updated <see cref="RouteConfig"/> plus the optimistic-concurrency
/// token captured at Edit hydration time.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint compares <see cref="BaseVersionId"/> against
/// <c>IConfigurationManager.CurrentVersionId</c>; a mismatch returns
/// 409 + <c>ConfigVersionMismatchDto</c>. A match proceeds through
/// <c>WizardConfigMerger.BuildEditedRouteDraft</c> → draft create →
/// apply, returning the same <c>ApplyResultDto</c> shape as the
/// existing draft-apply endpoint.
/// </para>
/// <para>
/// <see cref="RouteConfig"/>.<c>RouteId</c> MUST match the route
/// parameter; mismatches return 400.
/// </para>
/// <para>
/// <c>SourceInstanceId</c> and all <c>SinkInstanceIds</c> must resolve
/// to existing config entries. The merger enforces this; violations
/// return 400.
/// </para>
/// </remarks>
public sealed record UpdateRouteRequestDto
{
    /// <summary>
    /// Updated route configuration. <c>RouteId</c> must match the
    /// route parameter. <c>SourceInstanceId</c> and all
    /// <c>SinkInstanceIds</c> must resolve to existing entries.
    /// </summary>
    public required RouteConfig RouteConfig { get; init; }

    /// <summary>
    /// Optimistic-concurrency token: the value of
    /// <c>IConfigurationManager.CurrentVersionId</c> at the moment the
    /// wizard hydrated the route. The server rejects the save with 409 +
    /// <c>ConfigVersionMismatchDto</c> when this does not match the
    /// manager's current version at save time.
    /// </summary>
    public required string BaseVersionId { get; init; }

    /// <summary>
    /// Optional actor for the audit entry. Defaults to <c>"system"</c>
    /// when omitted.
    /// </summary>
    public string? Actor { get; init; }
}
