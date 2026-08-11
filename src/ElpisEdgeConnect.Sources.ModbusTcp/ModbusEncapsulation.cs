// ============================================================================
// File: ModbusEncapsulation.cs
// Purpose: How Modbus frames are carried to the slave. Native Modbus TCP uses
//          MBAP-framed PDUs over a TCP socket; RTU framing (slave addr + PDU +
//          CRC16) is carried either over a TCP socket (serial-to-Ethernet
//          gateways) or over a physical serial port (RS-485 / RS-232).
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md;
//            PHASE3_EXECUTION_PLAN.md §12 (Risks — pilot PLC has RTU-over-TCP)
// ============================================================================

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Wire encapsulation + transport for a Modbus connection. Selects which
/// underlying client the <see cref="IModbusClientFactory"/> builds: native TCP,
/// RTU framing over TCP, or RTU framing over a serial port.
/// </summary>
public enum ModbusEncapsulation
{
    /// <summary>
    /// Standard Modbus TCP (RFC-style): 7-byte MBAP header followed by a
    /// function-code PDU, no CRC, over a TCP socket. Default for virtually all
    /// native TCP slaves.
    /// </summary>
    Tcp = 0,

    /// <summary>
    /// Modbus RTU frames (slave address + PDU + CRC16) wrapped directly in
    /// a TCP stream, without an MBAP header. Used by serial-to-Ethernet
    /// gateways that expose an existing RTU bus over a TCP socket.
    /// </summary>
    RtuOverTcp = 1,

    /// <summary>
    /// Modbus RTU frames (slave address + PDU + CRC16) over a physical serial
    /// port (RS-485 / RS-232). Uses <c>serialPort</c> + <c>baudRate</c> /
    /// <c>parity</c> / <c>stopBits</c> / <c>handshake</c> instead of host/port.
    /// </summary>
    SerialRtu = 2,
}
