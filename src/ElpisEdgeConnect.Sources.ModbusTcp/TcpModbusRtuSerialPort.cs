// ============================================================================
// File: TcpModbusRtuSerialPort.cs
// Purpose: An IModbusRtuSerialPort backed by a TCP socket, so FluentModbus's
//          ModbusRtuClient (RTU framing + CRC16) can talk to a serial-to-
//          Ethernet gateway that tunnels RTU frames over TCP (the RtuOverTcp
//          encapsulation). The RTU framer reads/writes raw bytes; this adapter
//          simply pipes those bytes over a NetworkStream instead of a UART.
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md
// ============================================================================

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// <see cref="IModbusRtuSerialPort"/> implementation over a TCP connection.
/// Read/write timeouts are applied to the underlying <see cref="NetworkStream"/>
/// so a stalled gateway surfaces as an <see cref="System.IO.IOException"/>,
/// which the read helpers classify as a fatal transport error.
/// </summary>
internal sealed class TcpModbusRtuSerialPort : IModbusRtuSerialPort, IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly int _connectTimeoutMs;
    private readonly int _readTimeoutMs;
    private readonly int _writeTimeoutMs;

    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public TcpModbusRtuSerialPort(string host, int port, int connectTimeoutMs, int readTimeoutMs, int writeTimeoutMs)
    {
        _host = host;
        _port = port;
        _connectTimeoutMs = connectTimeoutMs > 0 ? connectTimeoutMs : 1000;
        _readTimeoutMs = readTimeoutMs > 0 ? readTimeoutMs : 1000;
        _writeTimeoutMs = writeTimeoutMs > 0 ? writeTimeoutMs : 1000;
    }

    /// <inheritdoc/>
    public string PortName => $"{_host}:{_port}";

    /// <inheritdoc/>
    public bool IsOpen => _tcp?.Connected == true;

    /// <inheritdoc/>
    public void Open()
    {
        var tcp = new TcpClient();
        try
        {
            if (!tcp.ConnectAsync(_host, _port).Wait(_connectTimeoutMs))
            {
                tcp.Dispose();
                throw new TimeoutException($"RTU-over-TCP connect to {_host}:{_port} timed out after {_connectTimeoutMs}ms.");
            }
        }
        catch
        {
            tcp.Dispose();
            throw;
        }

        _tcp = tcp;
        _stream = tcp.GetStream();
        _stream.ReadTimeout = _readTimeoutMs;
        _stream.WriteTimeout = _writeTimeoutMs;
    }

    /// <inheritdoc/>
    public void Close()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    /// <inheritdoc/>
    public int Read(byte[] buffer, int offset, int count)
        => Stream.Read(buffer, offset, count);

    /// <inheritdoc/>
    public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => Stream.ReadAsync(buffer.AsMemory(offset, count), token).AsTask();

    /// <inheritdoc/>
    public void Write(byte[] buffer, int offset, int count)
        => Stream.Write(buffer, offset, count);

    /// <inheritdoc/>
    public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => Stream.WriteAsync(buffer.AsMemory(offset, count), token).AsTask();

    public void Dispose() => Close();

    private NetworkStream Stream =>
        _stream ?? throw new InvalidOperationException("RTU-over-TCP serial port is not open.");
}
