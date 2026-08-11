// ============================================================================
// File: Focas2ConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in a FOCAS2 source's
//          opaque connection block. Factory + redaction rules name keys only
//          via these constants (ADR-0020 M-B Q-B2, plan §3a). FOCAS2 has no
//          authentication in its connection block — every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>JSON key-name constants for the FOCAS2 source connection block.</summary>
public static class Focas2ConnectionKeys
{
    /// <summary>CNC controller IP address.</summary>
    public const string IpAddress = "ipAddress";
    /// <summary>FOCAS2 port.</summary>
    public const string Port = "port";
    /// <summary>Connection timeout in seconds.</summary>
    public const string TimeoutSeconds = "timeoutSeconds";
    /// <summary>Per-handle data-read time-out in seconds (0 = library default).</summary>
    public const string DataTimeoutSeconds = "dataTimeoutSeconds";
    /// <summary>Keep the handle open across polls.</summary>
    public const string KeepAlive = "keepAlive";
    /// <summary>Hierarchical data-point paths.</summary>
    public const string DataPoints = "dataPoints";
    /// <summary>Initial backoff in ms.</summary>
    public const string InitialBackoffMs = "initialBackoffMs";
    /// <summary>Maximum backoff in ms.</summary>
    public const string MaxBackoffMs = "maxBackoffMs";
    /// <summary>Backoff multiplier.</summary>
    public const string BackoffMultiplier = "backoffMultiplier";
    /// <summary>Max handle-allocation retries per connect.</summary>
    public const string MaxConnectRetries = "maxConnectRetries";

    /// <summary>Ordered list of every connection key the FOCAS2 factory reads.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        IpAddress,
        Port,
        TimeoutSeconds,
        DataTimeoutSeconds,
        KeepAlive,
        DataPoints,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        MaxConnectRetries,
    };
}
