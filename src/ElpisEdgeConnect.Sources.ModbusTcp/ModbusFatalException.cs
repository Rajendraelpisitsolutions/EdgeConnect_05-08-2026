// ============================================================================
// File: ModbusFatalException.cs
// Purpose: Exception signalling a connection-fatal Modbus error. The
//          connection manager must drop the TCP socket and re-open it before
//          the next transaction — a retry on the same client object would
//          almost certainly fail identically.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.3
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Thrown by <see cref="IModbusClient"/> implementations when a request fails
/// in a way that invalidates the underlying TCP connection (peer reset,
/// read/write I/O error, timeout that exceeded the whole retry budget).
/// </summary>
/// <remarks>
/// <para>
/// Slave exception responses (Illegal Function, Illegal Data Address, etc.)
/// are <b>not</b> fatal — they indicate the request was malformed but the
/// transport is healthy. Those surface as <see cref="ModbusSlaveException"/>
/// instead, and the connection stays open.
/// </para>
/// </remarks>
public sealed class ModbusFatalException : Exception
{
    /// <summary>Stable error code from <see cref="ModbusErrors"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>Create a fatal exception with the given code and message.</summary>
    public ModbusFatalException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Create a fatal exception wrapping an inner exception.</summary>
    public ModbusFatalException(string errorCode, string message, Exception inner)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when the slave responds with a Modbus exception code (0x01–0x0B).
/// Non-fatal: the TCP connection remains open and subsequent transactions may
/// succeed. Commonly surfaces in tag-mapping bugs (Illegal Data Address) or
/// transient RTU-gateway issues (GatewayTargetDeviceFailedToRespond).
/// </summary>
public sealed class ModbusSlaveException : Exception
{
    /// <summary>The Modbus exception code (0x01 Illegal Function, 0x02 Illegal Data Address, etc.).</summary>
    public byte ExceptionCode { get; }

    /// <summary>The function code that triggered the exception response.</summary>
    public byte FunctionCode { get; }

    /// <summary>Create a slave-exception error.</summary>
    public ModbusSlaveException(byte functionCode, byte exceptionCode, string message)
        : base(message)
    {
        FunctionCode = functionCode;
        ExceptionCode = exceptionCode;
    }
}
