// ============================================================================
// File: Api/MqttTestConnectionResultDto.cs
// Purpose: Wire shape for the POST /api/v1/sinks/test-connection/mqtt
//          response (M.2b.6). Mirrors Focas2BrowseResultDto's shape so
//          the Studio's Razor renders both probe results with the same
//          state-dot + remediation-hint pattern.
//
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §2.2 (Test Connection panel)
// ============================================================================

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of an MQTT Test Connection probe. Returned in every status-code
/// branch the API exposes (200/400/403/409) so the Razor caller can render
/// a consistent shape regardless of outcome.
/// </summary>
public sealed record MqttTestConnectionResultDto
{
    /// <summary>
    /// Stable correlation id (e.g. <c>"probe-3f7a"</c>). Appears on every
    /// server-side log line for this probe — operator can quote the ProbeId
    /// when filing a support ticket.
    /// </summary>
    public required string ProbeId { get; init; }

    /// <summary>True if the broker accepted the CONNECT frame and disconnected cleanly.</summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock duration of the probe, including license gate + lease wait.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>
    /// Structured error code (e.g. <c>MQTT.PROBE_TIMEOUT</c>,
    /// <c>MQTT.PROBE_AUTH_FAILED</c>, <c>MQTT.PROBE_REFUSED</c>). Null on
    /// success.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message. Null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Operator-facing remediation hint (e.g. "Check the broker URL and
    /// firewall rules"). Null on success.
    /// </summary>
    public string? RemediationHint { get; init; }
}
