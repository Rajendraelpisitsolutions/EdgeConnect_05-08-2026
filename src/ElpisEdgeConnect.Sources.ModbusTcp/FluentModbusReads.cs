// ============================================================================
// File: FluentModbusReads.cs
// Purpose: Shared read + exception-mapping logic for the FluentModbus-backed
//          clients. Both the TCP client (FluentModbusClient) and the RTU client
//          (FluentModbusRtuClient) wrap a FluentModbus.ModbusClient (the common
//          base of ModbusTcpClient and ModbusRtuClient), so the function-code
//          reads and the library-exception → adapter-taxonomy mapping live here
//          once instead of being duplicated per transport.
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using FluentModbus;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Transport-neutral read helpers over a FluentModbus <see cref="ModbusClient"/>.
/// Throw <see cref="ModbusSlaveException"/> for slave exception codes (non-fatal)
/// and <see cref="ModbusFatalException"/> for transport failures (fatal). The
/// caller flips its connected flag on the fatal case.
/// </summary>
internal static class FluentModbusReads
{
    /// <summary>Read coils (FC01) or discrete inputs (FC02) and unpack to booleans.</summary>
    public static bool[] ReadBits(ModbusClient client, byte unitId, ushort startAddress, ushort quantity, bool bitwise)
    {
        try
        {
            var raw = bitwise
                ? client.ReadCoils(unitId, startAddress, quantity).ToArray()
                : client.ReadDiscreteInputs(unitId, startAddress, quantity).ToArray();
            return Unpack(raw, quantity);
        }
        catch (ModbusException ex)
        {
            throw MapSlaveException(bitwise ? (byte)0x01 : (byte)0x02, ex);
        }
        catch (Exception ex) when (IsFatal(ex))
        {
            throw MapFatal(ex);
        }
    }

    /// <summary>Read holding registers (FC03) or input registers (FC04) as raw words.</summary>
    public static ushort[] ReadRegisters(ModbusClient client, byte unitId, ushort startAddress, ushort quantity, bool holding)
    {
        try
        {
            var span = holding
                ? client.ReadHoldingRegisters<ushort>(unitId, startAddress, quantity)
                : client.ReadInputRegisters<ushort>(unitId, startAddress, quantity);
            return span.ToArray();
        }
        catch (ModbusException ex)
        {
            throw MapSlaveException(holding ? (byte)0x03 : (byte)0x04, ex);
        }
        catch (Exception ex) when (IsFatal(ex))
        {
            throw MapFatal(ex);
        }
    }

    /// <summary>True for exceptions that mean the transport is dead and a reconnect is needed.</summary>
    public static bool IsFatal(Exception ex)
        => ex is IOException
        || ex is SocketException
        || ex is TimeoutException
        || ex is OperationCanceledException;

    /// <summary>Map a fatal transport exception onto the adapter's error taxonomy.</summary>
    public static ModbusFatalException MapFatal(Exception ex) => ex switch
    {
        TimeoutException => new ModbusFatalException(ModbusErrors.Timeout, "Modbus request timed out.", ex),
        SocketException se => new ModbusFatalException(ModbusErrors.SocketError, $"Modbus socket error: {se.Message}", se),
        OperationCanceledException oce => new ModbusFatalException(ModbusErrors.SocketError, "Modbus request was cancelled.", oce),
        _ => new ModbusFatalException(ModbusErrors.SocketError, $"Modbus transport error: {ex.Message}", ex),
    };

    private static ModbusSlaveException MapSlaveException(byte functionCode, ModbusException ex)
        => new(functionCode, GetExceptionCode(ex), ex.Message);

    private static byte GetExceptionCode(ModbusException ex)
    {
        // FluentModbus exposes the exception code on its ExceptionCode property
        // in most versions; fall back to zero if the subclass used doesn't carry
        // one (the caller only uses this for diagnostics).
        var prop = ex.GetType().GetProperty("ExceptionCode");
        if (prop?.GetValue(ex) is { } value)
        {
            try { return Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { /* fall through */ }
        }
        return 0;
    }

    private static bool[] Unpack(byte[] raw, ushort quantity)
    {
        var bits = new bool[quantity];
        for (int i = 0; i < quantity; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            if (byteIndex < raw.Length)
            {
                bits[i] = (raw[byteIndex] & (1 << bitIndex)) != 0;
            }
        }
        return bits;
    }
}
