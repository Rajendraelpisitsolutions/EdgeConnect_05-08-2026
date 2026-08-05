// ============================================================================
// File: MelsecConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in a MELSEC source's
//          opaque connection block, including per-tag keys nested under tags[].
//          The factory + redaction rules name keys only via these constants
//          (ADR-0020). MC 3E carries no credential exchange in the connection
//          block — every key is benign / Include for redaction.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>JSON key-name constants for the MELSEC source connection block.</summary>
public static class MelsecConnectionKeys
{
    // ---- Connection ----
    /// <summary>PLC / Ethernet-module host.</summary>
    public const string Host = "host";
    /// <summary>MC/SLMP TCP port.</summary>
    public const string Port = "port";
    /// <summary>Transport protocol (Tcp / Udp).</summary>
    public const string TransportProtocol = "transportProtocol";
    /// <summary>MC frame mode (Mc3EBinary / ...).</summary>
    public const string FrameMode = "frameMode";
    /// <summary>CPU family profile (Modern / QnA / ACpu).</summary>
    public const string DeviceProfile = "deviceProfile";

    // ---- 3E route header ----
    /// <summary>Network number (default 0x00).</summary>
    public const string NetworkNo = "networkNo";
    /// <summary>PC (station) number (default 0xFF).</summary>
    public const string PcNo = "pcNo";
    /// <summary>Request destination module I/O number (default 0x03FF).</summary>
    public const string RequestDestModuleIoNo = "requestDestModuleIoNo";
    /// <summary>Request destination module station number (default 0x00).</summary>
    public const string RequestDestModuleStationNo = "requestDestModuleStationNo";
    /// <summary>Device-side monitoring timer in ms (encoded in 250 ms units).</summary>
    public const string MonitoringTimerMs = "monitoringTimerMs";

    // ---- Timeouts ----
    /// <summary>Connect timeout in ms.</summary>
    public const string ConnectTimeoutMs = "connectTimeoutMs";
    /// <summary>Per-read socket timeout in ms.</summary>
    public const string RequestTimeoutMs = "requestTimeoutMs";
    /// <summary>Keep the TCP session alive between scans.</summary>
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
    /// <summary>Max word gap the planner coalesces.</summary>
    public const string MaxGapWords = "maxGapWords";
    /// <summary>Max device points per single batch read.</summary>
    public const string MaxPointsPerRequest = "maxPointsPerRequest";

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
    /// <summary>MELSEC device address (nested).</summary>
    public const string Address = "address";
    /// <summary>Datatype hint (nested).</summary>
    public const string Datatype = "datatype";
    /// <summary>Word order for multi-word values (nested).</summary>
    public const string WordOrder = "wordOrder";
    /// <summary>Scan rate in ms (nested).</summary>
    public const string ScanRateMs = "scanRateMs";
    /// <summary>Engineering unit (nested).</summary>
    public const string Unit = "unit";
    /// <summary>Linear scale factor (nested).</summary>
    public const string Scale = "scale";
    /// <summary>Additive offset (nested).</summary>
    public const string Offset = "offset";

    /// <summary>Ordered list of every connection key the MELSEC factory reads (top-level + per-tag).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Host,
        Port,
        TransportProtocol,
        FrameMode,
        DeviceProfile,
        NetworkNo,
        PcNo,
        RequestDestModuleIoNo,
        RequestDestModuleStationNo,
        MonitoringTimerMs,
        ConnectTimeoutMs,
        RequestTimeoutMs,
        KeepAlive,
        MaxTransactionRetries,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        CircuitBreakerThreshold,
        CircuitBreakerResetMs,
        MaxGapWords,
        MaxPointsPerRequest,
        DeviceId,
        DeviceName,
        DeviceClass,
        Tags,
        TagName,
        Address,
        Datatype,
        WordOrder,
        ScanRateMs,
        Unit,
        Scale,
        Offset,
    };
}
