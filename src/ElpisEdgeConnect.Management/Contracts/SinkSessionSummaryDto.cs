// ============================================================================
// File: Contracts/SinkSessionSummaryDto.cs
// Purpose: Wire-side mirror of Core.Diagnostics.SinkSessionSummary —
//          one row of the "active client sessions" panel on the Sink
//          detail page. The OPC UA Server sink populates these via
//          ISessionTrackingSink → SinkSessionPoller → diagnostics
//          collector; the management layer re-shapes them here so the
//          Razor surface stays decoupled from Core's in-process types
//          (locked rule — Razor consumes management REST only).
//
//          MQTT sinks do NOT track sessions, so this list is null on
//          their SinkListItemDto. The detail page branches on sink
//          kind, not on the session-list shape.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b.3
//            docs/ARCHITECTURE_BLUEPRINT.md H.2 (OPC UA Server sink)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// One active client session on a session-tracking sink (today: OPC UA
/// Server). Protocol-agnostic shape so the detail UI can render any
/// future session-tracking sink (MTConnect agent, OPC UA PubSub broker, …)
/// against the same panel.
/// </summary>
public sealed record SinkSessionSummaryDto
{
    /// <summary>Stable per-session identifier assigned by the sink protocol.</summary>
    public required string SessionId { get; init; }

    /// <summary>Operator-readable session name (client-supplied, may be null).</summary>
    public string? SessionName { get; init; }

    /// <summary>
    /// Client application URI — for OPC UA, the <c>ApplicationUri</c> the
    /// client advertised during session-create. Null when the sink couldn't
    /// resolve it.
    /// </summary>
    public string? ClientApplicationUri { get; init; }

    /// <summary>Remote IP address as seen by the sink (may be null).</summary>
    public string? ClientIpAddress { get; init; }

    /// <summary>UTC timestamp the session was opened.</summary>
    public required DateTime ConnectedAtUtc { get; init; }

    /// <summary>UTC timestamp of the last observed activity on the session.</summary>
    public required DateTime LastActivityUtc { get; init; }

    /// <summary>
    /// Authentication token type. Protocol-specific string; for OPC UA:
    /// <c>"Anonymous"</c>, <c>"UserName"</c>, or <c>"Certificate"</c>.
    /// </summary>
    public string? UserTokenType { get; init; }

    /// <summary>
    /// Number of active subscriptions on this session. OPC UA: number of
    /// OPC UA subscriptions. MQTT: count of subscribed topic filters.
    /// </summary>
    public int SubscriptionCount { get; init; }

    /// <summary>
    /// Number of monitored items / observed values on this session. OPC UA:
    /// sum of monitored items across the session's subscriptions.
    /// </summary>
    public int MonitoredItemCount { get; init; }
}
