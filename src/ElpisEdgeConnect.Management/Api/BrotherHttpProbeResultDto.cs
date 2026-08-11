// ============================================================================
// File: Api/BrotherHttpProbeResultDto.cs
// Purpose: Response shape for POST /api/v1/sources/browse/brother-http.
//          Common probe fields (Success/ErrorCode/ErrorMessage/ProbeId/
//          ElapsedMs) match the M.2d.2 v2 §4.4 contract; Brother-specific
//          diagnostic fields (EndpointTested, HttpStatusObserved) added
//          for support troubleshooting per v2 §4.4.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4.4
// ============================================================================

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of a Brother HTTP Test Connection probe. Wire-level DTO
/// returned to the Studio wizard; renderable inline regardless of HTTP
/// status code (the wizard treats 200+Success=false as "render this
/// error" rather than as a fetch failure — see v2 §4.6).
/// </summary>
public sealed record BrotherHttpProbeResultDto
{
    /// <summary>Probe correlation id — surfaced in the wizard for support tickets.</summary>
    public required string ProbeId { get; init; }

    /// <summary>True when the probe succeeded; false when it ran but the CNC rejected or failed.</summary>
    public required bool Success { get; init; }

    /// <summary>Probe round-trip duration in milliseconds.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>
    /// Categorical error code when <see cref="Success"/> is false.
    /// <c>BROTHER.PROBE_*</c> namespace; <c>LICENSE.MODULE_DISABLED</c>
    /// and <c>*.PROBE_BUSY</c> follow the M.2d.2 v2 §4.6 shared shape.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Operator-facing one-line error message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Brother-specific diagnostic field (v2 §4.4): the full URL probed,
    /// e.g. <c>"http://192.168.2.110/HTTPD_MCNINFO"</c>. Surfaced in the
    /// wizard so an operator can confirm at a glance which URL was
    /// actually tested — especially useful when the configured BaseUrl
    /// has redirects or unexpected paths.
    /// </summary>
    public string? EndpointTested { get; init; }

    /// <summary>
    /// Brother-specific diagnostic field (v2 §4.4): the raw HTTP status
    /// code observed from the CNC (e.g. 200 / 401 / 404 / 503). Null
    /// when the probe failed before getting a response (e.g. timeout
    /// or unreachable).
    /// </summary>
    public int? HttpStatusObserved { get; init; }
}
