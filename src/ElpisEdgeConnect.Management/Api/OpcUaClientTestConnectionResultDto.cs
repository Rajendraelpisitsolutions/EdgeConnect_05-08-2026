// ============================================================================
// File: Api/OpcUaClientTestConnectionResultDto.cs
// Purpose: Wire shape for the POST /api/v1/sources/test-connection/opcua-client
//          response (PR 7c-2). Mirrors MqttTestConnectionResultDto's shape so
//          the wizard's Test Connection panel renders OPC UA Client probe
//          results with the same Success/ErrorCode/RemediationHint pattern
//          already in use across source/sink wizards.
//
//          The OPC UA Client probe carries one extra field over the MQTT
//          shape: ServerState (the server's UA-reported state — "Running",
//          "Failed", etc.) when the connect succeeded. The wizard surfaces
//          it on the success card so operators can verify they reached the
//          intended endpoint, not just *some* OPC UA server on the host:port.
//
// Reference: PR 7c plan + amendments (user lock 2026-05-29)
//            docs/decisions/0015-wizard-contract.md Rule 6 / Rule 11.1
// ============================================================================

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of an OPC UA Client Test Connection probe. Returned in every
/// status-code branch (200 / 400 / 403 / 409) so the wizard caller renders
/// a consistent shape regardless of outcome.
/// </summary>
public sealed record OpcUaClientTestConnectionResultDto
{
    /// <summary>
    /// Stable correlation id (e.g. <c>"probe-3f7a"</c>). Appears on every
    /// server-side log line for this probe — operators quote the ProbeId
    /// when filing a support ticket.
    /// </summary>
    public required string ProbeId { get; init; }

    /// <summary>
    /// True when the probe opened a session AND read the server's
    /// <c>ServerStatus.State</c> successfully (the
    /// <see cref="ServerState"/> field is populated).
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock duration of the probe, including license gate + lease wait.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>
    /// Endpoint URL the probe targeted — echoed back so the operator can
    /// double-check what was probed (especially useful when the wizard
    /// drives the URL through a draft-vs-applied workflow).
    /// </summary>
    public required string EndpointUrl { get; init; }

    /// <summary>
    /// Server's UA-reported state ("Running", "Failed", etc.) when the
    /// probe successfully read <c>ServerStatus.State</c>. <see langword="null"/>
    /// on failure.
    /// </summary>
    public string? ServerState { get; init; }

    /// <summary>
    /// Structured error code (e.g. <c>OPCUA.SERVER_CERT_UNTRUSTED</c>,
    /// <c>OPCUA.AUTH_DENIED</c>, <c>OPCUA.CONNECT_TIMEOUT</c>,
    /// <c>OPCUA.PROBE_LICENSE_DISABLED</c>, <c>OPCUA.PROBE_BUSY</c>).
    /// Null on success.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message. Null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Operator-facing remediation hint (e.g. "Check the endpoint URL and
    /// that the OPC UA server is reachable from this gateway"). Null on
    /// success.
    /// </summary>
    public string? RemediationHint { get; init; }
}
