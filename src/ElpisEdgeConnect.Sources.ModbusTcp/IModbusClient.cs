// ============================================================================
// File: IModbusClient.cs
// Purpose: Thin abstraction over the underlying Modbus-TCP client library.
//          The adapter's connection manager and transaction executor talk
//          only to this interface; the FluentModbus binding is hidden in
//          FluentModbusClient. Enables:
//            1. Unit tests using FakeModbusClient (no socket needed).
//            2. Swapping the underlying library in the future without
//               touching adapter logic.
// Reference: PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Transport-neutral Modbus client used by the adapter. Implementations wrap
/// a concrete Modbus library (production: <c>FluentModbusClient</c>; tests:
/// <c>FakeModbusClient</c>).
/// </summary>
/// <remarks>
/// <para>
/// All read methods throw <see cref="ModbusFatalException"/> for transport
/// failures (socket reset, timeout without reply) and
/// <see cref="ModbusSlaveException"/> for slave-returned exception codes.
/// Callers can distinguish fatal-retry (reconnect) from non-fatal (rebuild
/// request) on that basis alone.
/// </para>
/// <para>
/// Implementations MUST be single-threaded at the wire level — a Modbus TCP
/// connection has no transaction-id multiplexing guarantees across libraries,
/// and serializing through one client is easier to reason about than chasing
/// race conditions. The connection manager enforces this by serializing calls
/// behind an <see cref="SemaphoreSlim"/>.
/// </para>
/// </remarks>
internal interface IModbusClient : IAsyncDisposable
{
    /// <summary>True if the client currently holds a live TCP connection.</summary>
    bool IsConnected { get; }

    /// <summary>Host the client is connected to (or will attempt to connect to).</summary>
    string Host { get; }

    /// <summary>TCP port the client is connected to.</summary>
    int Port { get; }

    /// <summary>
    /// Open a TCP connection to the slave / gateway. Throws
    /// <see cref="ModbusFatalException"/> on failure.
    /// </summary>
    /// <param name="host">IP address or hostname.</param>
    /// <param name="port">TCP port, typically 502.</param>
    /// <param name="encapsulation">Native Modbus TCP or RTU-over-TCP.</param>
    /// <param name="connectTimeout">
    /// Wall-clock timeout for the TCP handshake. A value of <c>null</c>
    /// falls back to the library's default (typically 1 second).
    /// </param>
    /// <param name="readTimeout">
    /// Per-request response timeout applied to subsequent read calls.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task ConnectAsync(
        string host,
        int port,
        ModbusEncapsulation encapsulation,
        TimeSpan? connectTimeout,
        TimeSpan readTimeout,
        CancellationToken ct);

    /// <summary>
    /// Close the TCP connection if open. Idempotent — safe to call on an
    /// already-disconnected client.
    /// </summary>
    void Disconnect();

    /// <summary>Read coils via FC01. Returns <paramref name="quantity"/> booleans.</summary>
    Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct);

    /// <summary>Read discrete inputs via FC02. Returns <paramref name="quantity"/> booleans.</summary>
    Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct);

    /// <summary>
    /// Read holding registers via FC03. Returns <paramref name="quantity"/>
    /// raw 16-bit words in the order the slave sent them; the adapter
    /// decoder (F3) is responsible for byte-order normalization.
    /// </summary>
    Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct);

    /// <summary>Read input registers via FC04. Same semantics as <see cref="ReadHoldingRegistersAsync"/>.</summary>
    Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct);
}
