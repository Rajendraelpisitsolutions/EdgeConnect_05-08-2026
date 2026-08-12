// ============================================================================
// File: ModbusConnectionManagerTests.cs
// Purpose: Unit tests for ModbusConnectionManager — connect / disconnect,
//          exponential backoff, and the circuit breaker state machine.
// ============================================================================

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusConnectionManagerTests
{
    private static ModbusTcpSourceConfiguration Config(
        int initialBackoffMs = 1000,
        int maxBackoffMs = 60_000,
        int circuitBreakerThreshold = 3,
        int circuitBreakerResetMs = 5_000)
        => new()
        {
            InstanceId = "cm-test",
            ProtocolName = "modbustcp",
            DeviceId = "plc",
            Host = "127.0.0.1",
            Port = 502,
            InitialBackoffMs = initialBackoffMs,
            MaxBackoffMs = maxBackoffMs,
            BackoffMultiplier = 2.0,
            CircuitBreakerThreshold = circuitBreakerThreshold,
            CircuitBreakerResetMs = circuitBreakerResetMs,
            ConnectTimeoutMs = 500,
            RequestTimeoutMs = 500,
        };

    [Fact]
    public async Task EnsureConnectedAsync_Success_ReturnsTrueAndClearsFailureState()
    {
        var client = new FakeModbusClient();
        var mgr = new ModbusConnectionManager(client, Config(), "cm-test", NullLogger.Instance);

        var result = await mgr.EnsureConnectedAsync(default);

        result.Should().BeTrue();
        mgr.IsConnected.Should().BeTrue();
        mgr.ConsecutiveFailures.Should().Be(0);
        mgr.BreakerState.Should().Be(CircuitBreakerState.Closed);
        client.ConnectCallCount.Should().Be(1);
    }

    [Fact]
    public async Task EnsureConnectedAsync_ConnectFails_IncrementsFailures()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "no route"),
        };
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var mgr = new ModbusConnectionManager(client, Config(), "cm-test", NullLogger.Instance, time);

        var result = await mgr.EnsureConnectedAsync(default);

        result.Should().BeFalse();
        mgr.IsConnected.Should().BeFalse();
        mgr.ConsecutiveFailures.Should().Be(1);
    }

    [Fact]
    public async Task EnsureConnectedAsync_Backoff_SuppressesRetriesUntilWindowElapses()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "nope"),
        };
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cfg = Config(initialBackoffMs: 2000, circuitBreakerThreshold: 100);
        var mgr = new ModbusConnectionManager(client, cfg, "cm-test", NullLogger.Instance, time);

        await mgr.EnsureConnectedAsync(default); // attempt 1 — sets 2s backoff
        time.Advance(TimeSpan.FromMilliseconds(500));
        await mgr.EnsureConnectedAsync(default); // within backoff — suppressed

        client.ConnectCallCount.Should().Be(1);

        // Advance past backoff, should try again.
        time.Advance(TimeSpan.FromMilliseconds(2000));
        await mgr.EnsureConnectedAsync(default);

        client.ConnectCallCount.Should().Be(2);
        mgr.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdConsecutiveFailures()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "down"),
        };
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cfg = Config(initialBackoffMs: 100, circuitBreakerThreshold: 3);
        var mgr = new ModbusConnectionManager(client, cfg, "cm-test", NullLogger.Instance, time);

        // Fail three times, advancing past the backoff between each.
        for (var i = 0; i < 3; i++)
        {
            await mgr.EnsureConnectedAsync(default);
            time.Advance(TimeSpan.FromMilliseconds(1000));
        }

        mgr.BreakerState.Should().Be(CircuitBreakerState.Open);

        // Breaker-open: connect attempt suppressed and client NOT called.
        var before = client.ConnectCallCount;
        await mgr.EnsureConnectedAsync(default);
        client.ConnectCallCount.Should().Be(before);
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpenAfterResetWindow_SuccessClosesBreaker()
    {
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "down"),
        };
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cfg = Config(initialBackoffMs: 50, circuitBreakerThreshold: 2, circuitBreakerResetMs: 3000);
        var mgr = new ModbusConnectionManager(client, cfg, "cm-test", NullLogger.Instance, time);

        await mgr.EnsureConnectedAsync(default);
        time.Advance(TimeSpan.FromMilliseconds(500));
        await mgr.EnsureConnectedAsync(default);
        mgr.BreakerState.Should().Be(CircuitBreakerState.Open);

        // Peer recovers.
        client.ConnectException = null;

        // Before reset window elapses → still suppressed.
        time.Advance(TimeSpan.FromMilliseconds(1000));
        var beforeProbe = client.ConnectCallCount;
        var suppressed = await mgr.EnsureConnectedAsync(default);
        suppressed.Should().BeFalse();
        client.ConnectCallCount.Should().Be(beforeProbe);

        // After reset window → probe runs; success closes breaker.
        time.Advance(TimeSpan.FromMilliseconds(3000));
        var probe = await mgr.EnsureConnectedAsync(default);
        probe.Should().BeTrue();
        mgr.BreakerState.Should().Be(CircuitBreakerState.Closed);
        mgr.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task HandleFatalTransportError_DisconnectsAndStartsBackoff()
    {
        var client = new FakeModbusClient();
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var mgr = new ModbusConnectionManager(client, Config(), "cm-test", NullLogger.Instance, time);
        await mgr.EnsureConnectedAsync(default);
        mgr.IsConnected.Should().BeTrue();

        mgr.HandleFatalTransportError(
            new ModbusFatalException(ModbusErrors.SocketError, "peer reset"));

        mgr.IsConnected.Should().BeFalse();
        mgr.ConsecutiveFailures.Should().Be(1);
        client.DisconnectCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task WireLock_SerializesConcurrentAcquirers()
    {
        var client = new FakeModbusClient();
        var mgr = new ModbusConnectionManager(client, Config(), "cm-test", NullLogger.Instance);
        await mgr.EnsureConnectedAsync(default);

        var l1 = await mgr.AcquireWireLockAsync(default);
        var l2Task = mgr.AcquireWireLockAsync(default);

        // Second acquirer must be blocked until the first releases.
        l2Task.IsCompleted.Should().BeFalse();
        l1.Dispose();

        var l2 = await l2Task;
        l2.Should().NotBeNull();
        l2.Dispose();
    }

    /// <summary>Minimal manual-tick <see cref="TimeProvider"/> for deterministic tests.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }
}
