// ============================================================================
// File: FluentModbusRtuClient.cs
// Purpose: IModbusClient implementation for Modbus RTU framing, backed by
//          FluentModbus's ModbusRtuClient. Serves two encapsulations:
//            - SerialRtu  : RTU over a physical serial port (System.IO.Ports).
//            - RtuOverTcp : RTU over a TCP socket via TcpModbusRtuSerialPort.
//          Read + exception mapping is shared with the TCP client through
//          FluentModbusReads, so the wire format is the only difference.
// Reference: docs/sessions/2026-06-24-modbus-rtu-support-plan-v1.md
// ============================================================================

using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>Which byte transport the RTU framer runs over.</summary>
internal enum RtuTransportMode
{
    /// <summary>Physical serial port (RS-485 / RS-232).</summary>
    Serial,

    /// <summary>TCP socket to a serial-to-Ethernet gateway.</summary>
    Tcp,
}

/// <summary>
/// RTU <see cref="IModbusClient"/> wrapping FluentModbus's
/// <see cref="ModbusRtuClient"/>. Modbus RTU is always 8 data bits, so only
/// baud / parity / stop-bits / handshake are configurable (per the library).
/// </summary>
internal sealed class FluentModbusRtuClient : IModbusClient
{
    private readonly ModbusRtuClient _client = new();
    private readonly RtuTransportMode _mode;

    // Serial settings (used when _mode == Serial).
    private readonly string _serialPort;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly StopBits _stopBits;
    private readonly Handshake _handshake;

    // Injected port for tests (bypasses the real TCP/serial transport).
    private readonly IModbusRtuSerialPort? _injectedPort;

    private TcpModbusRtuSerialPort? _tcpPort;
    private string _host = string.Empty;
    private int _port;
    private bool _connected;
    private bool _disposed;

    /// <summary>Production constructor — reads serial settings from config.</summary>
    public FluentModbusRtuClient(RtuTransportMode mode, ModbusTcpSourceConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _mode = mode;
        _serialPort = config.SerialPort ?? string.Empty;
        _baudRate = config.BaudRate;
        _parity = config.SerialParity;
        _stopBits = config.SerialStopBits;
        _handshake = config.SerialHandshake;
    }

    /// <summary>
    /// Test constructor — injects a fake <see cref="IModbusRtuSerialPort"/> so the
    /// RTU framing path can be exercised without a real serial port or socket.
    /// </summary>
    internal FluentModbusRtuClient(IModbusRtuSerialPort injectedPort)
    {
        _mode = RtuTransportMode.Tcp;
        _injectedPort = injectedPort ?? throw new ArgumentNullException(nameof(injectedPort));
        _serialPort = string.Empty;
        _baudRate = 9600;
    }

    /// <inheritdoc/>
    public bool IsConnected => _connected && _client.IsConnected;

    /// <inheritdoc/>
    public string Host => _mode == RtuTransportMode.Serial ? _serialPort : _host;

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

        return Task.Run(() =>
        {
            try
            {
                var readMs = (int)readTimeout.TotalMilliseconds;
                _client.ReadTimeout = readMs;
                _client.WriteTimeout = readMs;

                if (_injectedPort is not null)
                {
                    _client.Initialize(_injectedPort, ModbusEndianness.BigEndian);
                }
                else if (_mode == RtuTransportMode.Serial)
                {
                    if (string.IsNullOrWhiteSpace(_serialPort))
                    {
                        throw new ModbusFatalException(
                            ModbusErrors.ConfigInvalid,
                            "SerialRtu encapsulation requires a 'serialPort' (e.g. COM3 or /dev/ttyUSB0).");
                    }
                    _client.BaudRate = _baudRate;
                    _client.Parity = _parity;
                    _client.StopBits = _stopBits;
                    _client.Handshake = _handshake;
                    _client.Connect(_serialPort, ModbusEndianness.BigEndian);
                }
                else // RtuOverTcp
                {
                    ArgumentException.ThrowIfNullOrEmpty(host);
                    _host = host;
                    _port = port;
                    var connectMs = connectTimeout is { } c ? (int)c.TotalMilliseconds : 1000;
                    _tcpPort = new TcpModbusRtuSerialPort(host, port, connectMs, readMs, readMs);
                    _client.Initialize(_tcpPort, ModbusEndianness.BigEndian);
                }

                _connected = true;
            }
            catch (ModbusFatalException)
            {
                throw;
            }
            catch (SocketException ex)
            {
                throw new ModbusFatalException(ModbusErrors.ConnectFailed, $"Modbus RTU connect failed: {ex.Message}", ex);
            }
            catch (TimeoutException ex)
            {
                throw new ModbusFatalException(ModbusErrors.Timeout, $"Modbus RTU connect timed out: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new ModbusFatalException(ModbusErrors.ConnectFailed, $"Modbus RTU connect failed: {ex.Message}", ex);
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
        try { _client.Close(); }
        catch { /* best-effort teardown — see FluentModbusClient.Disconnect */ }
        try { _tcpPort?.Dispose(); }
        catch { /* best-effort */ }
        _tcpPort = null;
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
        try { _client.Dispose(); }
        catch { /* best-effort */ }
        return ValueTask.CompletedTask;
    }

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
                $"Modbus RTU client for '{Host}' is not connected.");
        }
    }
}
