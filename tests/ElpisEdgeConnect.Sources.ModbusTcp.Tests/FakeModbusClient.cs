// ============================================================================
// File: FakeModbusClient.cs
// Purpose: Deterministic IModbusClient for unit tests. Records every call,
//          lets tests seed canned results or exceptions, and simulates socket
//          drops by flipping IsConnected to false.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

/// <summary>
/// In-memory test double for <see cref="IModbusClient"/>. Not thread-safe;
/// tests should drive it from a single thread.
/// </summary>
internal sealed class FakeModbusClient : IModbusClient
{
    public bool IsConnected { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }

    public int ConnectCallCount { get; private set; }
    public int DisconnectCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    /// <summary>If non-null, the next ConnectAsync call throws this exception.</summary>
    public Exception? ConnectException { get; set; }

    /// <summary>Delay inserted into ConnectAsync, used to assert timeouts.</summary>
    public TimeSpan ConnectDelay { get; set; } = TimeSpan.Zero;

    public List<CallRecord> Calls { get; } = [];

    /// <summary>Queue of coil-read results. Pop front per call.</summary>
    public Queue<Func<bool[]>> CoilResults { get; } = new();
    public Queue<Func<bool[]>> DiscreteInputResults { get; } = new();
    public Queue<Func<ushort[]>> HoldingRegisterResults { get; } = new();
    public Queue<Func<ushort[]>> InputRegisterResults { get; } = new();

    /// <summary>
    /// If non-null, every read call throws this exception instead of
    /// consuming a queued result.
    /// </summary>
    public Exception? ReadException { get; set; }

    public async Task ConnectAsync(
        string host,
        int port,
        ModbusEncapsulation encapsulation,
        TimeSpan? connectTimeout,
        TimeSpan readTimeout,
        CancellationToken ct)
    {
        ConnectCallCount++;
        Host = host;
        Port = port;
        if (ConnectDelay > TimeSpan.Zero)
        {
            await Task.Delay(ConnectDelay, ct).ConfigureAwait(false);
        }
        if (ConnectException is not null)
        {
            throw ConnectException;
        }
        IsConnected = true;
    }

    public void Disconnect()
    {
        DisconnectCallCount++;
        IsConnected = false;
    }

    public Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
    {
        Record(0x01, unitId, startAddress, quantity);
        if (ReadException is not null) throw ReadException;
        if (CoilResults.Count == 0)
        {
            return Task.FromResult(new bool[quantity]);
        }
        return Task.FromResult(CoilResults.Dequeue()());
    }

    public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
    {
        Record(0x02, unitId, startAddress, quantity);
        if (ReadException is not null) throw ReadException;
        if (DiscreteInputResults.Count == 0)
        {
            return Task.FromResult(new bool[quantity]);
        }
        return Task.FromResult(DiscreteInputResults.Dequeue()());
    }

    public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
    {
        Record(0x03, unitId, startAddress, quantity);
        if (ReadException is not null) throw ReadException;
        if (HoldingRegisterResults.Count == 0)
        {
            return Task.FromResult(new ushort[quantity]);
        }
        return Task.FromResult(HoldingRegisterResults.Dequeue()());
    }

    public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort startAddress, ushort quantity, CancellationToken ct)
    {
        Record(0x04, unitId, startAddress, quantity);
        if (ReadException is not null) throw ReadException;
        if (InputRegisterResults.Count == 0)
        {
            return Task.FromResult(new ushort[quantity]);
        }
        return Task.FromResult(InputRegisterResults.Dequeue()());
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>Force the client into a disconnected state (simulates a socket drop).</summary>
    public void SimulateDisconnect() => IsConnected = false;

    private void Record(byte fc, byte unitId, ushort addr, ushort qty)
        => Calls.Add(new CallRecord(fc, unitId, addr, qty));

    public readonly record struct CallRecord(byte FunctionCode, byte UnitId, ushort StartAddress, ushort Quantity);
}
