// ============================================================================
// File: Contracts/Config/CreateDraftRequestDto.cs
// Purpose: Request body for POST /api/v1/config/drafts. Wraps the
//          GatewayConfiguration with operator-attribution metadata so
//          the endpoint doesn't have to overload the config shape
//          with non-config fields.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// Body for <c>POST /api/v1/config/drafts</c>. Pairs the proposed
/// <see cref="GatewayConfiguration"/> (same shape as <c>gateway.json</c>
/// on disk) with operator-attribution metadata.
/// </summary>
public sealed record CreateDraftRequestDto
{
    /// <summary>
    /// The full gateway configuration to persist as a draft. Same shape
    /// as the on-disk <c>gateway.json</c>; an operator can paste their
    /// existing config verbatim. The draft is NOT validated at create
    /// time — drafts can be invalid while the operator iterates. Call
    /// <c>POST /drafts/{id}/validate</c> to check.
    /// </summary>
    public required GatewayConfiguration Configuration { get; init; }

    /// <summary>
    /// Operator identity recorded in the audit log alongside the
    /// <c>CONFIG.DRAFT_CREATED</c> entry. Defaults to <c>"system"</c>
    /// when null or empty; will be overridden by
    /// <c>HttpContext.User.Identity.Name</c> once auth middleware lands.
    /// </summary>
    public string? Actor { get; init; }
}

/// <summary>
/// Generic actor-only body shared by the discard / apply / rollback
/// endpoints. All three optionally accept just an actor identifier;
/// no other body fields are meaningful today.
/// </summary>
public sealed record ActorRequestDto
{
    /// <summary>Operator identity. Defaults to <c>"system"</c> when null or empty.</summary>
    public string? Actor { get; init; }
}
