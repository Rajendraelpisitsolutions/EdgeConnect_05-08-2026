// ============================================================================
// File: EthernetIpConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in an EtherNet/IP
//          source's opaque connection block, including the per-tag keys nested
//          under `tags[]`. Factory + redaction rules name keys only via these
//          constants (ADR-0020 M-B Q-B2). EtherNet/IP has no authentication —
//          every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>JSON key-name constants for the EtherNet/IP source connection block.</summary>
public static class EthernetIpConnectionKeys
{
    // ---- Connection ----
    /// <summary>Gateway IP / hostname of the controller's Ethernet port.</summary>
    public const string Host = "host";
    /// <summary>CIP routing path to the CPU (e.g. <c>"1,0"</c>).</summary>
    public const string Path = "path";
    /// <summary>Rockwell CPU family (controllogix / compactlogix / micro800 / …).</summary>
    public const string CpuFamily = "cpuFamily";
    /// <summary>CIP session connect timeout in ms.</summary>
    public const string ConnectTimeoutMs = "connectTimeoutMs";
    /// <summary>Per-tag read timeout in ms.</summary>
    public const string RequestTimeoutMs = "requestTimeoutMs";

    // ---- Retry / backoff / breaker ----
    /// <summary>Max per-read retries.</summary>
    public const string MaxTransactionRetries = "maxTransactionRetries";
    /// <summary>Initial backoff in ms.</summary>
    public const string InitialBackoffMs = "initialBackoffMs";
    /// <summary>Maximum backoff in ms.</summary>
    public const string MaxBackoffMs = "maxBackoffMs";
    /// <summary>Backoff multiplier.</summary>
    public const string BackoffMultiplier = "backoffMultiplier";
    /// <summary>Circuit-breaker trip threshold.</summary>
    public const string CircuitBreakerThreshold = "circuitBreakerThreshold";
    /// <summary>Circuit-breaker reset window in ms.</summary>
    public const string CircuitBreakerResetMs = "circuitBreakerResetMs";

    // ---- Tag list ----
    /// <summary>Per-tag definitions array.</summary>
    public const string Tags = "tags";

    // ---- Tag definition (nested under tags[]) ----
    /// <summary>Canonical tag name (nested).</summary>
    public const string TagName = "name";
    /// <summary>Controller tag address / symbolic name (nested).</summary>
    public const string Address = "address";
    /// <summary>Element type hint (nested).</summary>
    public const string Datatype = "datatype";
    /// <summary>Scan rate in ms (nested).</summary>
    public const string ScanRateMs = "scanRateMs";
    /// <summary>Linear scale factor (nested).</summary>
    public const string Scale = "scale";
    /// <summary>Additive offset (nested).</summary>
    public const string Offset = "offset";
    /// <summary>Engineering unit (nested).</summary>
    public const string Unit = "unit";

    /// <summary>Ordered list of every connection key the EtherNet/IP factory reads (top-level + per-tag).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Host,
        Path,
        CpuFamily,
        ConnectTimeoutMs,
        RequestTimeoutMs,
        MaxTransactionRetries,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        CircuitBreakerThreshold,
        CircuitBreakerResetMs,
        Tags,
        TagName,
        Address,
        Datatype,
        ScanRateMs,
        Scale,
        Offset,
        Unit,
    };
}
