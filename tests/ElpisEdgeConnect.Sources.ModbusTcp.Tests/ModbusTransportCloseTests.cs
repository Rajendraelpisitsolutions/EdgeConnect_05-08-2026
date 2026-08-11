// ============================================================================
// Tests: socket-leak regression for the Modbus transport teardown.
//
//        Field failure being locked down here: the connected flag was allowed
//        to gate the transport close. A faulted read clears that flag, and a
//        failed connect never sets it — so the two paths that exist to clean up
//        after a failure were exactly the paths where Disconnect() became a
//        no-op. Every reconnect cycle then opened a fresh socket and orphaned
//        the previous one, until the device hit its connection limit and refused
//        everything, which surfaced to the operator as
//        "MODBUS.CONNECT_FAILED — device is not reachable".
//
//        These tests fail against that behaviour: they assert the close happens
//        even when the flag says "not connected", at both the client level
//        (FluentModbusRtuClient over an injected port) and the manager level.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using FluentModbus;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusTransportCloseTests
{
    private static ModbusTcpSourceConfiguration Config(
        int initialBackoffMs = 1000,
        int circuitBreakerThreshold = 100)
        => new()
        {
            InstanceId = "close-test",
            ProtocolName = "modbustcp",
            DeviceId = "plc",
            Host = "127.0.0.1",
            Port = 502,
            InitialBackoffMs = initialBackoffMs,
            MaxBackoffMs = 60_000,
            BackoffMultiplier = 2.0,
            CircuitBreakerThreshold = circuitBreakerThreshold,
            CircuitBreakerResetMs = 5_000,
            ConnectTimeoutMs = 500,
            RequestTimeoutMs = 500,
        };

    private static Task ConnectInjectedAsync(FluentModbusRtuClient client) =>
        client.ConnectAsync(
            host: "ignored", port: 0, ModbusEncapsulation.RtuOverTcp,
            connectTimeout: null, readTimeout: TimeSpan.FromMilliseconds(200), CancellationToken.None);

    // =========================================================================
    // Client level — the flag must never gate the close
    // =========================================================================

    [Fact]
    public async Task Disconnect_AfterReadFault_ClosesTransport()
    {
        var port = new RecordingRtuSerialPort();
        await using var client = new FluentModbusRtuClient(port);
        await ConnectInjectedAsync(client);

        // The port serves no response bytes, so the read faults and the client
        // clears its connected flag — the state that used to skip the close.
        Func<Task> faultingRead = () => client.ReadHoldingRegistersAsync(
            unitId: 1, startAddress: 0, quantity: 1, CancellationToken.None);
        await faultingRead.Should().ThrowAsync<Exception>();

        client.Disconnect();

        port.CloseCallCount.Should().BeGreaterThan(0,
            "a faulted read leaves the socket open, so the follow-up Disconnect() is the only thing that reclaims it");
    }

    [Fact]
    public async Task Disconnect_CalledTwice_ClosesTransportBothTimes()
    {
        var port = new RecordingRtuSerialPort();
        await using var client = new FluentModbusRtuClient(port);
        await ConnectInjectedAsync(client);

        client.Disconnect();
        client.Disconnect();

        // The second call is the regression: with the connected flag cleared by
        // the first call, the old guard turned every subsequent close into a
        // no-op — the same condition a faulted read or a failed connect produces.
        port.CloseCallCount.Should().Be(2,
            "the connected flag tracks state only; it must never gate the transport close");
    }

    [Fact]
    public async Task ConnectAsync_TransportOpenFails_ClosesTransportBeforePropagating()
    {
        var port = new RecordingRtuSerialPort { OpenException = new IOException("port busy") };
        await using var client = new FluentModbusRtuClient(port);

        Func<Task> connect = () => ConnectInjectedAsync(client);

        var thrown = await connect.Should().ThrowAsync<ModbusFatalException>();
        thrown.Which.ErrorCode.Should().Be(ModbusErrors.ConnectFailed,
            "the caller classifies on the mapped fatal exception, so cleanup must not swallow or replace it");
        port.CloseCallCount.Should().BeGreaterThan(0,
            "a failed connect can leave a half-open transport that only Disconnect() can reclaim");
    }

    [Fact]
    public async Task ConnectAsync_AfterFaultedRead_ClosesPriorTransportBeforeReopening()
    {
        var port = new RecordingRtuSerialPort();
        await using var client = new FluentModbusRtuClient(port);
        await ConnectInjectedAsync(client);

        Func<Task> faultingRead = () => client.ReadHoldingRegistersAsync(
            unitId: 1, startAddress: 0, quantity: 1, CancellationToken.None);
        await faultingRead.Should().ThrowAsync<Exception>();

        await ConnectInjectedAsync(client);

        // Reconnecting on top of a live transport is how the sockets piled up:
        // the client keeps one port, so opening a new one without closing the
        // old one orphans it for good.
        port.CloseCallCount.Should().BeGreaterThan(0,
            "a reconnect must close the previous transport instead of orphaning it");
    }

    // =========================================================================
    // Manager level — every abandoned attempt closes its transport
    // =========================================================================

    [Fact]
    public async Task EnsureConnectedAsync_ConnectFails_ClosesTransportBeforeBackoff()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "connection refused"),
        };
        var mgr = new ModbusConnectionManager(client, Config(), "close-test", NullLogger.Instance);

        var result = await mgr.EnsureConnectedAsync(default);

        result.Should().BeFalse();
        client.DisconnectCallCount.Should().BeGreaterThan(0,
            "an abandoned connect attempt must not leave its socket behind while the manager waits out the backoff");
    }

    [Fact]
    public async Task EnsureConnectedAsync_RepeatedConnectFailures_ClosesTransportEveryAttempt()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "connection refused"),
        };
        // Zero backoff so consecutive attempts are not suppressed by the retry
        // gate; the circuit-breaker threshold is parked out of reach for the
        // same reason. This test is about cleanup, not about pacing.
        var mgr = new ModbusConnectionManager(client, Config(initialBackoffMs: 0), "close-test", NullLogger.Instance);

        await mgr.EnsureConnectedAsync(default);
        await mgr.EnsureConnectedAsync(default);
        await mgr.EnsureConnectedAsync(default);

        client.ConnectCallCount.Should().Be(3);
        client.DisconnectCallCount.Should().Be(3,
            "one orphaned socket per retry cycle is what exhausts the device's connection limit");
    }

    /// <summary>
    /// An <see cref="IModbusRtuSerialPort"/> that records opens and closes and
    /// never answers a read, so the RTU framer faults on the first response
    /// wait. Serves as the observable transport in these tests.
    /// </summary>
    private sealed class RecordingRtuSerialPort : IModbusRtuSerialPort
    {
        private bool _isOpen;

        /// <summary>If set, <see cref="Open"/> throws it (simulates a refused transport).</summary>
        public Exception? OpenException { get; init; }

        public int OpenCallCount { get; private set; }

        public int CloseCallCount { get; private set; }

        /// <summary>Bytes the RTU framer wrote (kept for diagnosability on failure).</summary>
        public List<byte> Written { get; } = [];

        public string PortName => "recording";

        public bool IsOpen => _isOpen;

        public void Open()
        {
            OpenCallCount++;
            if (OpenException is not null)
            {
                throw OpenException;
            }
            _isOpen = true;
        }

        public void Close()
        {
            CloseCallCount++;
            _isOpen = false;
        }

        public int Read(byte[] buffer, int offset, int count)
            => throw new TimeoutException("RecordingRtuSerialPort: no response bytes to serve.");

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => Task.FromException<int>(new TimeoutException("RecordingRtuSerialPort: no response bytes to serve."));

        public void Write(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                Written.Add(buffer[offset + i]);
            }
        }

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }
}
