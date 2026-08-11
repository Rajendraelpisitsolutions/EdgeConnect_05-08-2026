// ============================================================================
// File: Api/OpcUaClientBrowseResultDto.cs
// Purpose: Wire shape for POST /api/v1/sources/browse/opcua-client. The
//          OPC UA browse is hierarchical (Objects folder → namespaces →
//          variables) — unlike FOCAS2's flat axis/tag list — so the DTO
//          carries a BrowseResult tree rather than a tags-array.
//
//          The protocol-side OpcUaClientBrowseService (PR 5) handles the
//          UA Browse / BrowseNext machinery + canonicalisation; this DTO
//          just wraps the BrowseResult with probe metadata (id, success
//          flag, elapsed, error code/message).
//
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Browse;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Result of an OPC UA Client Browse probe — wraps the protocol's
/// <see cref="BrowseResult"/> with API-level success / error metadata.
/// </summary>
public sealed record OpcUaClientBrowseResultDto
{
    /// <summary>
    /// 8-character correlation id (e.g. <c>"a1b2c3d4"</c>) for this probe.
    /// Logged at every phase boundary.
    /// </summary>
    public required string ProbeId { get; init; }

    /// <summary>True when the browse completed and the tree is populated.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The hierarchical browse result when <see cref="Success"/> is true.
    /// <see langword="null"/> on failure.
    /// </summary>
    public BrowseResult? Result { get; init; }

    /// <summary>
    /// Structured error code when <see cref="Success"/> is false. Examples:
    /// <c>OPCUA.PROBE_LICENSE_DISABLED</c>, <c>OPCUA.BROWSE_IN_FLIGHT</c>,
    /// <c>OPCUA.BROWSE_CONFIG_INVALID</c>, <c>OPCUA.BROWSE_CONFIG_INCOMPLETE</c>,
    /// <c>OPCUA.BROWSE_TIMEOUT</c>, <c>OPCUA.BROWSE_FAILED</c>.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Operator-facing error message (paired with <see cref="ErrorCode"/>).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Operator-facing remediation hint. Surfaced inline on the wizard's
    /// browse panel when <see cref="Success"/> is false.
    /// </summary>
    public string? RemediationHint { get; init; }

    /// <summary>Non-fatal warnings collected during the probe.</summary>
    public IList<string> Warnings { get; init; } = new List<string>();

    /// <summary>End-to-end probe duration in milliseconds.</summary>
    public required long ElapsedMs { get; init; }
}
