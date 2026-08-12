// ============================================================================
// File: HostEndpointsServerTests.cs
// Purpose: Phase 5 integration tests for HostEndpointsServer. Boots a real
//          server on a free loopback port, drives it with HttpClient, and
//          pins the /health/live, /health/ready, and /metrics contracts.
// Milestone: D — phase 5.
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Endpoints;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class HostEndpointsServerTests
{
    private static int GetFreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private sealed record TestHarness(
        HostEndpointsServer Server,
        Meter Meter,
        DiagnosticsMeters Meters,
        RuntimeDiagnosticsCollector Collector,
        HostReadinessGate Readiness,
        string Url,
        HttpClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Server.DisposeAsync();
            Meters.Dispose();
            Meter.Dispose();
        }
    }

    private static async Task<TestHarness> StartHarnessAsync()
    {
        var port = GetFreeLoopbackPort();
        var url = $"http://localhost:{port}/";
        var collector = new RuntimeDiagnosticsCollector();
        var meter = new Meter("test-" + Guid.NewGuid());
        var meters = new DiagnosticsMeters(collector, meter);
        var readiness = new HostReadinessGate();
        var server = new HostEndpointsServer(
            readiness,
            meter,
            url,
            NullLogger<HostEndpointsServer>.Instance);
        await server.StartAsync(CancellationToken.None);
        var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
        return new TestHarness(server, meter, meters, collector, readiness, url, client);
    }

    [Fact]
    public async Task HealthLive_ReturnsAlways200()
    {
        await using var h = await StartHarnessAsync();

        // Not ready yet — but liveness should still be 200.
        h.Readiness.IsReady.Should().BeFalse();
        var response = await h.Client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("live");
    }

    [Fact]
    public async Task HealthReady_Returns503WhenGateClosed_200WhenOpen()
    {
        await using var h = await StartHarnessAsync();

        var notReady = await h.Client.GetAsync("/health/ready");
        notReady.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        h.Readiness.MarkReady();
        var ready = await h.Client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Contain("ready");

        h.Readiness.MarkNotReady();
        var notReadyAgain = await h.Client.GetAsync("/health/ready");
        notReadyAgain.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Metrics_ReturnsPrometheusTextForCollectorState()
    {
        await using var h = await StartHarnessAsync();

        // Drive some state into the collector.
        h.Collector.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Configured,
            To = RouteState.Running,
            ObservedAtUtc = DateTime.UtcNow,
        });
        h.Collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 12,
            Reason = "test",
            ObservedAtUtc = DateTime.UtcNow,
        });

        var response = await h.Client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("# TYPE elpis_edgeconnect_route_state_transitions_total counter");
        body.Should().Contain("elpis_edgeconnect_route_state_transitions_total{route_id=\"r1\"} 1");
        body.Should().Contain("elpis_edgeconnect_route_backpressure_drops_total{route_id=\"r1\"} 12");
    }

    [Fact]
    public async Task UnknownPath_Returns404()
    {
        await using var h = await StartHarnessAsync();

        var response = await h.Client.GetAsync("/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NonGetMethod_Returns405()
    {
        await using var h = await StartHarnessAsync();

        var req = new HttpRequestMessage(HttpMethod.Post, "/health/live");
        var response = await h.Client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        await using var h = await StartHarnessAsync();

        // Second StartAsync is a no-op (already listening).
        var act = async () => await h.Server.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Server still functional.
        var response = await h.Client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StopAsync_IsIdempotent()
    {
        var h = await StartHarnessAsync();
        await h.Server.StopAsync(CancellationToken.None);

        var act = async () => await h.Server.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Cleanup.
        await h.DisposeAsync();
    }
}
