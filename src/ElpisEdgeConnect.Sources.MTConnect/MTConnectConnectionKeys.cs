// ============================================================================
// File: MTConnectConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in an MTConnect
//          source's opaque connection block. Factory + redaction rules name
//          keys only via these constants (ADR-0020 M-B Q-B2, plan §3a).
//          MTConnect Agents are polled over HTTP with no credentials in the
//          connection block — every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>JSON key-name constants for the MTConnect source connection block.</summary>
public static class MTConnectConnectionKeys
{
    /// <summary>Base URL of the MTConnect Agent.</summary>
    public const string AgentBaseUrl = "agentBaseUrl";
    /// <summary>Optional Agent-side device name.</summary>
    public const string AgentDeviceName = "agentDeviceName";
    /// <summary>HTTP request timeout in seconds.</summary>
    public const string TimeoutSeconds = "timeoutSeconds";
    /// <summary>Initial backoff in ms.</summary>
    public const string InitialBackoffMs = "initialBackoffMs";
    /// <summary>Maximum backoff in ms.</summary>
    public const string MaxBackoffMs = "maxBackoffMs";
    /// <summary>Backoff multiplier.</summary>
    public const string BackoffMultiplier = "backoffMultiplier";
    /// <summary>Consecutive failures before Degraded.</summary>
    public const string DegradeAfterConsecutiveFailures = "degradeAfterConsecutiveFailures";

    /// <summary>Ordered list of every connection key the MTConnect factory reads.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        AgentBaseUrl,
        AgentDeviceName,
        TimeoutSeconds,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        DegradeAfterConsecutiveFailures,
    };
}
