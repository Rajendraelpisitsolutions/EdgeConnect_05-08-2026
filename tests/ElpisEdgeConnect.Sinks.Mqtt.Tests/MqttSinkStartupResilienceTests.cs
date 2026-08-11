// ============================================================================
// File: MqttSinkStartupResilienceTests.cs
// Purpose: Pin the startup-isolation contract for the MQTT sink.
//
//          LOCKED DECISION #10 (per-adapter isolation): "one failing adapter
//          never affects any other adapter, route, or sink."
//
//          HostStartup awaits the SINK supervisor BEFORE the SOURCE supervisor
//          (HostStartup.cs), and SinkSupervisor's per-adapter isolation is
//          EXCEPTION-based — a StartAsync that *hangs* rather than throws slips
//          straight past it and stalls every source on the gateway. The MQTT
//          sink used to retry the initial connect forever, so an unreachable
//          broker meant the whole pipeline collected nothing.
//
//          These tests deliberately need NO broker: they point at a closed port,
//          so they run in every environment (unlike MqttSinkAdapterTests, which
//          is tagged RequiresMqttBroker).
// Reference: docs/decisions (locked decision #10); EdgeConnect network-resilience
//            analysis record, 2026-07-20
// ============================================================================

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

public sealed class MqttSinkStartupResilienceTests
{
    /// <summary>
    /// Bind port 0 to have the OS hand us a free port, then release it — so we
    /// have an address that is guaranteed routable but with nothing listening.
    /// Connecting there fails fast with "connection refused".
    /// </summary>
    private static int ClosedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MqttSinkConfiguration UnreachableConfig() => new()
    {
        InstanceId = "unreachable-sink",
        ProtocolName = "mqtt",
        BrokerHost = "127.0.0.1",
        BrokerPort = ClosedLoopbackPort(),
        PublishMode = MqttPublishMode.Batch,
        TopicTemplate = "test/{gatewayId}/data",
        QosLevel = 0,
        // Long delays so the background retry loop stays quiet during the test.
        ReconnectDelayMs = 30_000,
        MaxReconnectDelayMs = 60_000,
    };

    [Fact]
    public async Task StartAsync_BrokerUnreachable_ReturnsPromptly_InsteadOfBlocking()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(UnreachableConfig(), CancellationToken.None);

        var sw = Stopwatch.StartNew();
        var start = adapter.StartAsync(CancellationToken.None);
        // WhenAny rather than a bare await: if this regresses we want a FAILING
        // test, not a hanging test run.
        var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(30)));
        sw.Stop();

        finished.Should().BeSameAs(start,
            "a sink that cannot reach its broker must never block host startup — "
            + "sinks start before sources, so a hang here stalls every source (locked decision #10)");
        await start; // must complete without throwing

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_BrokerUnreachable_EntersDegraded_NotFailed()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(UnreachableConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        // Degraded (not Failed): the adapter is alive and retrying in the
        // background. Failed would be terminal and would never self-heal.
        adapter.State.Should().Be(AdapterState.Degraded);

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_BrokerUnreachable_SurfacesRetryableConnectError()
    {
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(UnreachableConfig(), CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);
        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.LastError.Should().NotBeNull("the operator must be able to see why the sink is degraded");
        health.LastError!.Code.Should().Be("MQTT.CONNECT_FAILED");
        health.LastError.Retryable.Should().BeTrue();

        await adapter.StopAsync(CancellationToken.None);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_AfterFailedStart_ShutsDownCleanly()
    {
        // The background reconnect loop must not keep the adapter alive or
        // deadlock shutdown when it never managed to connect.
        var adapter = new MqttSinkAdapter("s1", NullLogger<MqttSinkAdapter>.Instance);
        await adapter.InitializeAsync(UnreachableConfig(), CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        var stop = adapter.StopAsync(CancellationToken.None);
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(30)));

        finished.Should().BeSameAs(stop, "shutdown must not wait on the background reconnect loop");
        await stop;
        adapter.State.Should().Be(AdapterState.Stopped);

        await adapter.DisposeAsync();
    }
}
