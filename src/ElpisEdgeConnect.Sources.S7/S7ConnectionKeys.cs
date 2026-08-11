// ============================================================================
// File: S7ConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in a Siemens S7
//          source's opaque connection block, including the per-tag keys nested
//          under `tags[]`. Factory + redaction rules name keys only via these
//          constants (ADR-0020 M-B Q-B2, plan §3a). S7comm (PG/OP/Basic) has no
//          credential exchange in this connection block — every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>JSON key-name constants for the Siemens S7 source connection block.</summary>
public static class S7ConnectionKeys
{
    // ---- Connection ----
    /// <summary>PLC host.</summary>
    public const string Host = "host";
    /// <summary>ISO-on-TCP port.</summary>
    public const string Port = "port";
    /// <summary>Hardware rack number.</summary>
    public const string Rack = "rack";
    /// <summary>CPU slot number.</summary>
    public const string Slot = "slot";
    /// <summary>Connection type (PG / OP / Basic).</summary>
    public const string ConnectionType = "connectionType";
    /// <summary>Optimized DB access flag.</summary>
    public const string OptimizedDbAccess = "optimizedDbAccess";
    /// <summary>Connect timeout in ms.</summary>
    public const string ConnectTimeoutMs = "connectTimeoutMs";
    /// <summary>Per-read timeout in ms.</summary>
    public const string RequestTimeoutMs = "requestTimeoutMs";
    /// <summary>Keep the session alive between scans.</summary>
    public const string KeepAlive = "keepAlive";

    // ---- Retry / backoff / breaker ----
    /// <summary>Max per-transaction retries.</summary>
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

    // ---- Scan planner ----
    /// <summary>Max byte gap the planner coalesces.</summary>
    public const string MaxGapBytes = "maxGapBytes";
    /// <summary>Max bytes per single read.</summary>
    public const string MaxReadBytes = "maxReadBytes";

    // ---- Identity (read from the connection block) ----
    /// <summary>Device id override.</summary>
    public const string DeviceId = "deviceId";
    /// <summary>Device name.</summary>
    public const string DeviceName = "deviceName";
    /// <summary>Device class.</summary>
    public const string DeviceClass = "deviceClass";

    // ---- Tag list ----
    /// <summary>Per-tag definitions array.</summary>
    public const string Tags = "tags";

    // ---- Tag definition (nested under tags[]) ----
    /// <summary>Canonical tag name (nested).</summary>
    public const string TagName = "name";
    /// <summary>S7 address string (nested).</summary>
    public const string Address = "address";
    /// <summary>Datatype hint (nested).</summary>
    public const string Datatype = "datatype";
    /// <summary>Scan rate in ms (nested).</summary>
    public const string ScanRateMs = "scanRateMs";
    /// <summary>Engineering unit (nested).</summary>
    public const string Unit = "unit";
    /// <summary>Linear scale factor (nested).</summary>
    public const string Scale = "scale";
    /// <summary>Additive offset (nested).</summary>
    public const string Offset = "offset";

    /// <summary>Ordered list of every connection key the S7 factory reads (top-level + per-tag).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Host,
        Port,
        Rack,
        Slot,
        ConnectionType,
        OptimizedDbAccess,
        ConnectTimeoutMs,
        RequestTimeoutMs,
        KeepAlive,
        MaxTransactionRetries,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        CircuitBreakerThreshold,
        CircuitBreakerResetMs,
        MaxGapBytes,
        MaxReadBytes,
        DeviceId,
        DeviceName,
        DeviceClass,
        Tags,
        TagName,
        Address,
        Datatype,
        ScanRateMs,
        Unit,
        Scale,
        Offset,
    };
}
