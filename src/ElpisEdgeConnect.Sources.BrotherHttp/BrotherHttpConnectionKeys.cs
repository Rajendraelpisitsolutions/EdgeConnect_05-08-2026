// ============================================================================
// File: BrotherHttpConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in a Brother HTTP
//          source's opaque connection block. Factory + redaction rules name
//          keys only via these constants (ADR-0020 M-B Q-B2, plan §3a). The
//          Brother CNC HTTP endpoint is polled without credentials in the
//          connection block — every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>JSON key-name constants for the Brother HTTP source connection block.</summary>
public static class BrotherHttpConnectionKeys
{
    /// <summary>Brother CNC HTTP base URL.</summary>
    public const string BaseUrl = "baseUrl";
    /// <summary>HTTP request timeout in seconds.</summary>
    public const string TimeoutSeconds = "timeoutSeconds";
    /// <summary>Consecutive failures before Failed.</summary>
    public const string FaultThresholdConsecutiveFailures = "faultThresholdConsecutiveFailures";
    /// <summary>Initial backoff in ms.</summary>
    public const string InitialBackoffMs = "initialBackoffMs";
    /// <summary>Maximum backoff in ms.</summary>
    public const string MaxBackoffMs = "maxBackoffMs";
    /// <summary>Backoff multiplier.</summary>
    public const string BackoffMultiplier = "backoffMultiplier";
    /// <summary>Hierarchical data-point paths.</summary>
    public const string DataPoints = "dataPoints";

    /// <summary>Ordered list of every connection key the Brother HTTP factory reads.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        BaseUrl,
        TimeoutSeconds,
        FaultThresholdConsecutiveFailures,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        DataPoints,
    };
}
