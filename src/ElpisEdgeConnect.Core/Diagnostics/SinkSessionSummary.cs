// ============================================================================
// File: Diagnostics/SinkSessionSummary.cs
// Purpose: Protocol-agnostic summary of one external client session
//          observed by a session-tracking sink (OPC UA Server today;
//          potentially MQTT broker-side or HTTP keepalive in the
//          future). Surfaces on SinkHealthSnapshot.ActiveSessions so
//          operators can see "who is reading from us right now."
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.2
//            shared-knowledge/contracts/opcua-namespace-policy.md
//                (§ Sessions)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Operator-facing summary of one currently-active client session on a
/// session-tracking sink. Adapters that declare
/// <c>SinkCapabilities.SessionTracking</c> produce this record at poll
/// time; the diagnostics surface aggregates per-sink.
/// </summary>
public sealed record SinkSessionSummary
{
    /// <summary>Stable per-session identifier assigned by the sink protocol.</summary>
    public required string SessionId { get; init; }

    /// <summary>Operator-readable session name (client-supplied, may be null).</summary>
    public string? SessionName { get; init; }

    /// <summary>Client application URI (e.g., the OPC UA ApplicationUri the client advertised).</summary>
    public string? ClientApplicationUri { get; init; }

    /// <summary>Remote IP address as seen by the sink (may be null).</summary>
    public string? ClientIpAddress { get; init; }

    /// <summary>UTC timestamp the session was opened.</summary>
    public required DateTime ConnectedAtUtc { get; init; }

    /// <summary>UTC timestamp of the last observed activity on the session.</summary>
    public DateTime LastActivityUtc { get; init; }

    /// <summary>
    /// User-token type the session authenticated with — protocol-specific
    /// string. For OPC UA: <c>"Anonymous"</c>, <c>"UserName"</c>, or
    /// <c>"Certificate"</c>. For MQTT: <c>"Anonymous"</c> or
    /// <c>"UserName"</c>. Free-form; the diagnostics surface does not
    /// constrain the vocabulary.
    /// </summary>
    public string? UserTokenType { get; init; }

    /// <summary>
    /// Number of active subscriptions on this session, when meaningful for
    /// the protocol. OPC UA: number of OPC UA subscriptions. MQTT: count of
    /// subscribed topic filters when the broker reports them. <c>0</c> when
    /// the protocol has no subscription concept or the data is not yet
    /// available.
    /// </summary>
    public int SubscriptionCount { get; init; }

    /// <summary>
    /// Number of monitored items / observed values on this session, when
    /// meaningful for the protocol. OPC UA: sum of monitored items across
    /// the session's subscriptions. <c>0</c> when not yet available.
    /// </summary>
    public int MonitoredItemCount { get; init; }
}
