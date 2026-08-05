// ============================================================================
// File: Api/MTConnectBrowseResultDto.cs
// Purpose: Wire shapes for POST /api/v1/sources/browse/mtconnect (M.2b.4). The
//          MTConnect add-source wizard is FOCAS2-style semantic onboarding — it
//          exposes the adapter's FIXED canonical tag map, enriched by /probe to
//          mark which tags this agent actually exposes and which axes it has.
//          Management owns these DTOs (OpenAPI independence); the protocol-side
//          parsing lives in Sources.MTConnect.MTConnectProbeParser.
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md §3.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>The verdict of an MTConnect browse probe.</summary>
public enum MTConnectBrowseStatus
{
    /// <summary>/probe parsed and at least one canonical tag's source is present.</summary>
    ReachableWithRecognisedTags,

    /// <summary>/probe parsed but none of the canonical CNC tags are present (source would produce no data).</summary>
    ReachableNoRecognisedTags,

    /// <summary>Could not connect (DNS / refused / network).</summary>
    Unreachable,

    /// <summary>The probe exceeded the fixed budget.</summary>
    Timeout,

    /// <summary>The agent demanded authentication (401/403).</summary>
    Unauthorized,

    /// <summary>The response was not a well-formed MTConnect /probe document.</summary>
    InvalidProbeDocument,

    /// <summary>A valid MTConnectDevices envelope with no devices to onboard.</summary>
    UnsupportedAgent,

    /// <summary>The <c>source-mtconnect</c> license module is not enabled.</summary>
    LicenseDisabled,
}

/// <summary>Availability of one canonical semantic tag for the probed agent.</summary>
public sealed record MTConnectSemanticTagAvailability
{
    /// <summary>Canonical tag name (e.g. <c>spindle/load</c>).</summary>
    public required string CanonicalTag { get; init; }

    /// <summary>True when the agent exposes a source dataItem for this tag.</summary>
    public required bool Available { get; init; }

    /// <summary>Why the tag is unavailable (when not available).</summary>
    public string? Reason { get; init; }

    /// <summary>The matched MTConnect dataItem <c>type</c> (when available).</summary>
    public string? SourceDataItemType { get; init; }

    /// <summary>The matched MTConnect dataItem <c>id</c> (when available).</summary>
    public string? SourceDataItemId { get; init; }
}

/// <summary>Result of an MTConnect browse probe.</summary>
public sealed record MTConnectBrowseResultDto
{
    /// <summary>The probe verdict.</summary>
    public required MTConnectBrowseStatus Status { get; init; }

    /// <summary>Convenience flag: the agent is reachable and produces at least one tag.</summary>
    public bool Success => Status == MTConnectBrowseStatus.ReachableWithRecognisedTags;

    /// <summary>The device these results describe (the requested one, or the first).</summary>
    public string? DeviceName { get; init; }

    /// <summary>Target device UUID, when present.</summary>
    public string? DeviceUuid { get; init; }

    /// <summary>Target device manufacturer, when present.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Every device the agent exposes — one source per device (multi-device handling).</summary>
    public IReadOnlyList<string> AvailableDevices { get; init; } = [];

    /// <summary>Per-canonical-tag availability for the target device.</summary>
    public IReadOnlyList<MTConnectSemanticTagAvailability> Tags { get; init; } = [];

    /// <summary>Discovered axis identities (capped).</summary>
    public IReadOnlyList<string> Axes { get; init; } = [];

    /// <summary>Operator-readable detail for the status (especially for error states).</summary>
    public string? Message { get; init; }

    /// <summary>Wall-clock probe duration.</summary>
    public long ElapsedMs { get; init; }
}

/// <summary>Request body for <c>POST /api/v1/sources/browse/mtconnect</c>.</summary>
/// <param name="AgentBaseUrl">The MTConnect agent root URL (e.g. <c>http://agent.local:5000</c>).</param>
/// <param name="AgentDeviceName">Optional device to target; blank selects the first device in /probe.</param>
public sealed record MTConnectBrowseRequest(string AgentBaseUrl, string? AgentDeviceName);
