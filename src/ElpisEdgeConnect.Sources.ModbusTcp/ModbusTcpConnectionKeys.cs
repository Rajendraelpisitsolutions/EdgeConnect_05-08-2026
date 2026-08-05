// ============================================================================
// File: ModbusTcpConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names in a Modbus TCP
//          source's opaque connection block, including the per-tag keys nested
//          under `tagDefinitions[]`. Factory + redaction rules name keys only
//          via these constants (ADR-0020 M-B Q-B2, plan §3a). Modbus TCP has no
//          authentication — every key is benign.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>JSON key-name constants for the Modbus TCP source connection block.</summary>
public static class ModbusTcpConnectionKeys
{
    // ---- Connection ----
    /// <summary>Slave / gateway host.</summary>
    public const string Host = "host";
    /// <summary>TCP port.</summary>
    public const string Port = "port";
    /// <summary>Wire encapsulation (tcp / rtuOverTcp / serialRtu).</summary>
    public const string Encapsulation = "encapsulation";
    /// <summary>Default slave unit id.</summary>
    public const string DefaultUnitId = "defaultUnitId";
    /// <summary>TCP handshake timeout in ms.</summary>
    public const string ConnectTimeoutMs = "connectTimeoutMs";
    /// <summary>Per-request timeout in ms.</summary>
    public const string RequestTimeoutMs = "requestTimeoutMs";
    /// <summary>Keep the socket open across polls.</summary>
    public const string KeepAlive = "keepAlive";

    // ---- Serial (SerialRtu encapsulation only) ----
    /// <summary>Serial port name (e.g. <c>COM3</c> / <c>/dev/ttyUSB0</c>).</summary>
    public const string SerialPort = "serialPort";
    /// <summary>Serial baud rate (e.g. 9600, 19200, 38400).</summary>
    public const string BaudRate = "baudRate";
    /// <summary>Serial parity (none / even / odd / mark / space).</summary>
    public const string Parity = "parity";
    /// <summary>Serial stop bits (one / two / onePointFive).</summary>
    public const string StopBits = "stopBits";
    /// <summary>Serial handshake (none / xOnXOff / requestToSend / requestToSendXOnXOff).</summary>
    public const string Handshake = "handshake";

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
    /// <summary>Max register gap the planner coalesces.</summary>
    public const string MaxGapRegisters = "maxGapRegisters";

    // ---- Addressing ----
    /// <summary>
    /// Notation used for the operator-entered tag addresses
    /// (<c>zeroBased</c> / <c>oneBased</c> / <c>modicon</c>). Default
    /// <c>zeroBased</c> — see <see cref="ModbusAddressBase"/>.
    /// </summary>
    public const string AddressBase = "addressBase";

    // ---- Tag list ----
    /// <summary>Per-tag definitions array.</summary>
    public const string TagDefinitions = "tagDefinitions";

    // ---- Tag definition (nested under tagDefinitions[]) ----
    /// <summary>Canonical tag name (nested).</summary>
    public const string TagName = "name";
    /// <summary>Register class (nested).</summary>
    public const string RegisterClass = "registerClass";
    /// <summary>Slave unit id (nested).</summary>
    public const string TagUnitId = "unitId";
    /// <summary>Register/coil address (nested).</summary>
    public const string Address = "address";
    /// <summary>Scan rate in ms (nested).</summary>
    public const string ScanRateMs = "scanRateMs";
    /// <summary>Datatype hint (nested).</summary>
    public const string Datatype = "datatype";
    /// <summary>Byte order (nested).</summary>
    public const string ByteOrder = "byteOrder";
    /// <summary>Linear scale factor (nested).</summary>
    public const string Scale = "scale";
    /// <summary>Additive offset (nested).</summary>
    public const string Offset = "offset";
    /// <summary>Engineering unit (nested).</summary>
    public const string Unit = "unit";

    /// <summary>Ordered list of every connection key the Modbus factory reads (top-level + per-tag).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Host,
        Port,
        Encapsulation,
        DefaultUnitId,
        ConnectTimeoutMs,
        RequestTimeoutMs,
        KeepAlive,
        SerialPort,
        BaudRate,
        Parity,
        StopBits,
        Handshake,
        MaxTransactionRetries,
        InitialBackoffMs,
        MaxBackoffMs,
        BackoffMultiplier,
        CircuitBreakerThreshold,
        CircuitBreakerResetMs,
        MaxGapRegisters,
        AddressBase,
        TagDefinitions,
        TagName,
        RegisterClass,
        TagUnitId,
        Address,
        ScanRateMs,
        Datatype,
        ByteOrder,
        Scale,
        Offset,
        Unit,
    };
}
