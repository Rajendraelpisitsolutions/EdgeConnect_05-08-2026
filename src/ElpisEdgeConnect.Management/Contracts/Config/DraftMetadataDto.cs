// ============================================================================
// File: Contracts/Config/DraftMetadataDto.cs
// Purpose: List-row shape for /api/v1/config/drafts — enough metadata
//          for the Studio's drafts table without re-shipping the full
//          GatewayConfiguration on every list call.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// One row in the drafts table. Per-row config bytes are fetched
/// separately via <c>GET /api/v1/config/drafts/{draftId}</c> so the
/// list endpoint stays cheap on large catalogs.
/// </summary>
/// <remarks>
/// <para>Reserved for future expansion (per the established
/// "ship contracts only when producers exist" discipline):</para>
/// <list type="bullet">
///   <item><c>Summary</c> — operator-authored note about the draft's
///         purpose. Requires a Core extension to persist on
///         <c>DraftRecord</c>; today there's nowhere for it to live.
///         When/if Core grows draft-note persistence, the field ships
///         with a real producer.</item>
///   <item><c>SupersedesDraftId</c> — link drafts together so
///         "I'm replacing draft A with draft B" is observable in the
///         audit trail. Reserved.</item>
///   <item><c>ExpectedCurrentVersionId</c> — optimistic-concurrency
///         token for safe multi-operator workflows. Reserved.</item>
/// </list>
/// </remarks>
public sealed record DraftMetadataDto
{
    /// <summary>Opaque draft identifier (e.g. <c>"draft-20260408T143022Z-a4f1b2"</c>).</summary>
    public required string DraftId { get; init; }

    /// <summary>UTC timestamp the draft was persisted.</summary>
    public required DateTime CreatedAtUtc { get; init; }

    /// <summary>
    /// Actor who created the draft. <c>"system"</c> when no body field was
    /// supplied; populated from <c>HttpContext.User</c> once auth lands.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>Size of the persisted draft file in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Gateway identity at draft-create time (same convention as
    /// DiagnosticsEventDto.GatewayId). Useful for cross-gateway draft
    /// inspection in future federation scenarios.
    /// </summary>
    public string? GatewayId { get; init; }
}
