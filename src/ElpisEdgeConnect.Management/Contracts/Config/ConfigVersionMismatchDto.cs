// ============================================================================
// File: Contracts/Config/ConfigVersionMismatchDto.cs
// Purpose: 409 Conflict response when an Edit-mode wizard tries to save
//          against a stale BaseVersionId — i.e. another session has
//          applied a newer config since this wizard opened.
//
//          M.2d.2 v2 §5.5 — optimistic-concurrency token. Activates
//          the reservation captured in DraftMetadataDto's XML doc.
//          ~1 DTO + 1 server guard + 1 wizard reload banner.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §5.5
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// 409 Conflict response body returned when a wizard attempts to save an
/// Edit-mode change whose <c>BaseVersionId</c> no longer matches the
/// gateway's current config version. The wizard renders a
/// <c>StaleEditWarningBanner</c> with a single Reload action — no
/// automatic merge in M.2d.2 (too risky).
/// </summary>
/// <remarks>
/// <para>
/// Sized intentionally minimal: only the three facts a wizard banner
/// needs to render. No diff payload, no merge hints, no field-level
/// change list. If/when the customer asks for "show me what changed",
/// the existing version-history page (already populated by
/// <c>HistoryEntryDto</c>) is the right surface — not this contract.
/// </para>
/// </remarks>
public sealed record ConfigVersionMismatchDto
{
    /// <summary>
    /// Locked discriminator: always <c>"VersionMismatch"</c>. The
    /// <c>PUT /api/v1/sources/{instanceId}</c> endpoint can return a
    /// 409 in two shapes (this DTO for optimistic-concurrency mismatch
    /// and <c>ValidationResultDto</c> for apply-time revalidation
    /// failure). Clients read this property before deserialising the
    /// rest of the body — absence of the property indicates the
    /// other shape. M.2d.2 v2 §0.2 review-pass correction.
    /// </summary>
    public string ConflictType { get; init; } = "VersionMismatch";

    /// <summary>
    /// The <c>BaseVersionId</c> the wizard sent on save — captured at
    /// Edit hydration time. Echoed back so the wizard can confirm the
    /// 409 is for its own request (e.g. when correlating logs).
    /// </summary>
    public required string BaseVersionId { get; init; }

    /// <summary>
    /// The gateway's actual current version id at save time, against
    /// which <see cref="BaseVersionId"/> failed to match. The wizard
    /// surfaces this in the reload banner so support can correlate
    /// against the version history.
    /// </summary>
    public required string CurrentVersionId { get; init; }

    /// <summary>
    /// UTC timestamp the current version became active. Operator-facing
    /// in the banner ("Configuration was updated at {time}"). Mirrors
    /// <c>CurrentConfigVersionDto.AppliedAtUtc</c>.
    /// </summary>
    public required DateTime ChangedSinceUtc { get; init; }
}
