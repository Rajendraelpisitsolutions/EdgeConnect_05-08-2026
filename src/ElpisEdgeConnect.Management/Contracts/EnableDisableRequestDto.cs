// ============================================================================
// File: Contracts/EnableDisableRequestDto.cs
// Purpose: Request body for the Enable/Disable verb endpoints
//          (POST /api/v1/{kind}/{id}/enable | /disable).
//
//          Per Locked G (v2 §2): the request carries the operator's
//          observed configuration version so the server can detect a
//          stale view and refuse with 409 CONFIG.STALE_VIEW rather than
//          silently applying onto a moved baseline.
//
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md §2
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/sources/{id}/{enable|disable}</c>
/// (and the parallel sink + route endpoints).
/// </summary>
/// <remarks>
/// The body carries the configuration version the list-page client
/// observed at last poll. If the gateway's current version differs,
/// the server returns 409 with <c>CONFIG.STALE_VIEW</c> instead of
/// applying onto a moved baseline. The check fires BEFORE the no-op
/// check per Locked G ordering — operators must be told their view was
/// stale rather than that they performed a no-op (the latter is
/// operationally misleading).
/// </remarks>
public sealed record EnableDisableRequestDto
{
    /// <summary>
    /// The <c>ConfigurationVersionId</c> value the operator's page
    /// observed at most recent poll. Format is the opaque string the
    /// list endpoints already surface. <c>null</c> is treated as
    /// "no expectation" — the server skips the stale-view check; useful
    /// for scripted / automation use where the caller doesn't track
    /// versions.
    /// </summary>
    public string? ExpectedConfigurationVersion { get; init; }
}
