// ============================================================================
// File: S7SourceAdapterStartTests.cs
// Purpose: Pin the TL-139 start contract — StartAsync must not touch the wire.
//          It is awaited by SourceSupervisor.AddAsync and therefore by the
//          hot-reload coordinator, so dialling a PLC that is not wired up yet
//          burned the connect budget and stalled the operator's config-apply
//          Save. Connecting belongs to the poll loop, which owns the backoff
//          and the circuit breaker. Also pins that lifecycle misuse (start
//          before initialize) is still a genuine, diagnosable start failure.
// Reference: commit 6c1d984 (same fix for FOCAS2 / Modbus TCP / EtherNet/IP)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public sealed class S7SourceAdapterStartTests
{
    /// <summary>
    /// Counting transport double. Every wire call is recorded so a test can
    /// assert that StartAsync made none of them.
    /// </summary>
    private sealed class CountingS7Client : IS7Client
    {
        public bool IsConnected { get; private set; }

        public int ConnectCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public Func<int, S7OperationResult>? ConnectBehavior { get; set; }

        public Task<S7OperationResult> ConnectAsync(
            string host,
            int port,
            int rack,
            int slot,
            S7ConnectionType connectionType,
            TimeSpan timeout,
            CancellationToken ct)
        {
            ConnectCalls++;
            var result = ConnectBehavior?.Invoke(ConnectCalls) ?? S7OperationResult.Ok;
            if (result.Success)
            {
                IsConnected = true;
            }

            return Task.FromResult(result);
        }

        public void Disconnect() => IsConnected = false;

        public Task<S7OperationResult> ReadAreaAsync(
            S7MemoryArea area,
            int dbNumber,
            int startByte,
            int byteCount,
            byte[] buffer,
            CancellationToken ct)
        {
            ReadCalls++;
            buffer.AsSpan(0, byteCount).Clear();
            buffer[0] = 0x00;
            buffer[1] = 0x2A; // big-endian 42 in the first word
            return Task.FromResult(S7OperationResult.Ok);
        }

        public void Dispose()
        {
        }
    }

    private static S7SourceConfiguration Config() => new()
    {
        InstanceId = "s7-1",
        ProtocolName = "s7",
        DeviceId = "plc",
        DeviceClass = "plc",
        Host = "127.0.0.1",
        TagDefinitions = new[]
        {
            new S7TagDefinition { Name = "t", Address = "DB10.DBW0", Datatype = "int" },
        },
    };

    [Fact]
    public async Task Start_does_not_dial_the_device()
    {
        var client = new CountingS7Client();
        var adapter = new S7SourceAdapter("s7-1", client, NullLogger.Instance);
        await adapter.InitializeAsync(Config(), default);

        await adapter.StartAsync(default);

        client.ConnectCalls.Should().Be(0);
        client.ReadCalls.Should().Be(0);
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task First_poll_owns_the_connect()
    {
        // Deferring costs nothing: the poll path calls EnsureConnectedAsync itself.
        var client = new CountingS7Client();
        var adapter = new S7SourceAdapter("s7-1", client, NullLogger.Instance);
        await adapter.InitializeAsync(Config(), default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        client.ConnectCalls.Should().Be(1);
        points.Should().ContainSingle().Which.Quality.Should().Be(DataQuality.Good);
    }

    [Fact]
    public async Task Unreachable_device_does_not_fail_the_start()
    {
        // The whole point of the change: an operator configuring ahead of
        // installation gets a Running adapter, not a Failed one.
        var client = new CountingS7Client
        {
            ConnectBehavior = _ => S7OperationResult.Fail(-2, "connection refused"),
        };
        var adapter = new S7SourceAdapter("s7-1", client, NullLogger.Instance);
        await adapter.InitializeAsync(Config(), default);

        await adapter.StartAsync(default);

        adapter.State.Should().Be(AdapterState.Running);
        (await adapter.CheckHealthAsync(default)).LastError.Should().BeNull();
    }

    [Fact]
    public async Task Start_before_initialize_is_still_a_genuine_start_failure()
    {
        // A silent device is not a start failure, but lifecycle misuse is: the
        // Failed transition and S7.START_FAILED stay reachable through it.
        var adapter = new S7SourceAdapter("s7-1", new CountingS7Client(), NullLogger.Instance);

        var act = async () => await adapter.StartAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
        var health = await adapter.CheckHealthAsync(default);
        health.LastError!.Code.Should().Be("S7.START_FAILED");
    }
}
