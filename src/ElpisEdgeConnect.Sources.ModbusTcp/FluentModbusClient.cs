// ============================================================================
// File: FluentModbusClient.cs
// Purpose: Native Modbus TCP IModbusClient implementation backed by the
//          FluentModbus ModbusTcpClient (MBAP framing). Wraps its synchronous
//          API in an async shape; read + exception-mapping logic is shared with
//          the RTU client via FluentModbusReads.
//
//          Transport selection: the IModbusClientFactory hands RtuOverTcp and
//          SerialRtu to FluentModbusRtuClient, so this client only ever sees
//          ModbusEncapsulation.Tcp. The RtuOverTcp guard below is a defensive
//          assert that is unreachable via the factory.
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md;
//            PHASE3_EXECUTION_PLAN.md §5
// ============================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Native Modbus TCP <see cref="IModbusClient"/> implementation wrapping
/// FluentModbus's synchronous <see cref="ModbusTcpClient"/>.
/// </summary>
/// <remarks>
/// FluentModbus 5.x exposes synchronous read methods that block on the
/// underlying network stream. We wrap each call in a <c>Task.Run</c> so the
/// calling async path does not monopolize its thread while waiting for a
/// slow slave, and so <see cref="OperationCanceledException"/> can be
/// propagated promptly via the cancellation token.
/// </remarks>
internal sealed class FluentModbusClient : IModbusClient
{
    private readonly ModbusTcpClient _client = new();
    private string _host = string.Empty;
    private int _port;
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsConnected => _connected && _client.IsConnected;

    /// <inheritdoc/>
    public string Host => _host;

    /// <inheritdoc/>
    public int Port => _port;

    /// <inheritdoc/>
    public Task ConnectAsync(
        string host,
        int port,
        ModbusEncapsulation encapsulation,
        TimeSpan? connectTimeout,
        TimeSpan readTimeout,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(host);

        if (encapsulation != ModbusEncapsulation.Tcp)
        {
            // Defensive: the factory routes non-TCP encapsulations to
            // FluentModbusRtuClient, so this client should only ever see Tcp.
            throw new ModbusFatalException(
                ModbusErrors.ConfigInvalid,
                $"FluentModbusClient handles only Modbus TCP; got encapsulation '{encapsulation}'. " +
                "RTU encapsulations are served by FluentModbusRtuClient via the client factory.");
        }

        _host = host;
        _port = port;

        return Task.Run(() =>
        {
            try
            {
                if (connectTimeout is { } ct0)
                {
                    _client.ConnectTimeout = (int)ct0.TotalMilliseconds;
                }
                _client.ReadTimeout = (int)readTimeout.TotalMilliseconds;
                _client.WriteTimeout = (int)readTimeout.TotalMilliseconds;

                if (!IPAddress.TryParse(host, out var ip))
                {
                    var entries = Dns.GetHostAddresses(host);
                    if (entries.Length == 0)
                    {
                        throw new ModbusFatalException(
                            ModbusErrors.ConnectFailed,
                            $"Modbus connect: host '{host}' did not resolve to any IP address.");
                    }
                    ip = entries[0];
                }

                _client.Connect(new IPEndPoint(ip, port), ModbusEndianness.BigEndian);
                _connected = true;
            }
            catch (ModbusFatalException)
            {
                throw;
            }
            catch (SocketException ex)
            {
                throw new ModbusFatalException(
                    ModbusErrors.ConnectFailed,
                    $"Modbus connect to {host}:{port} failed: {ex.Message}",
                    ex);
            }
            catch (TimeoutException ex)
            {
                throw new ModbusFatalException(
                    ModbusErrors.Timeout,
                    $"Modbus connect to {host}:{port} timed out after {_client.ConnectTimeout}ms.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new ModbusFatalException(
                    ModbusErrors.ConnectFailed,
                    $"Modbus connect to {host}:{port} failed: {ex.Message}",
                    ex);
            }
        }, ct);
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        if (!_connected)
        {
            return;
        }
        try
        {
            _client.Disconnect();
        }
        catch
        {
            // Disconnect is best-effort; suppressing here prevents a stuck
            // transport from cascading into the source supervisor. The
            // connection manager logs context.
        }
        _connected = false;
    }

    /// <inheritdoc/>
    public Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
        => ReadBitsAsync(unitId, startAddress, quantity, bitwise: true, ct);

    /// <inheritdoc/>
    public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
        => ReadBitsAsync(unitId, startAddress, quantity, bitwise: false, ct);

    /// <inheritdoc/>
    public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
        => ReadRegistersAsync(unitId, startAddress, quantity, holding: true, ct);

    /// <inheritdoc/>
    public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
        => ReadRegistersAsync(unitId, startAddress, quantity, holding: false, ct);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        Disconnect();
        return ValueTask.CompletedTask;
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private Task<bool[]> ReadBitsAsync(byte unitId, ushort startAddress, ushort quantity, bool bitwise, CancellationToken ct)
    {
        EnsureConnectedForRead();
        return Task.Run(() =>
        {
            try
            {
                return FluentModbusReads.ReadBits(_client, unitId, startAddress, quantity, bitwise);
            }
            catch (ModbusFatalException)
            {
                _connected = false;
                throw;
            }
        }, ct);
    }

    private Task<ushort[]> ReadRegistersAsync(byte unitId, ushort startAddress, ushort quantity, bool holding, CancellationToken ct)
    {
        EnsureConnectedForRead();
        return Task.Run(() =>
        {
            try
            {
                return FluentModbusReads.ReadRegisters(_client, unitId, startAddress, quantity, holding);
            }
            catch (ModbusFatalException)
            {
                _connected = false;
                throw;
            }
        }, ct);
    }

    private void EnsureConnectedForRead()
    {
        if (!IsConnected)
        {
            throw new ModbusFatalException(
                ModbusErrors.SocketError,
                $"Modbus client for {_host}:{_port} is not connected.");
        }
    }
}
