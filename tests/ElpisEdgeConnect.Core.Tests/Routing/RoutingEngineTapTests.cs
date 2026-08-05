// ============================================================================
// File: Routing/RoutingEngineTapTests.cs
// Purpose: Live Tap M2 — verify the capture hooks are wired into the real
//          route worker. When a subscriber is watching, source-side
//          (pre-transform) and per-sink batches are captured; when no one is
//          watching, NOTHING is captured (hot-path-clean at the integration
//          level, ADR-0017 Rule 1 / P1).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineTapTests
{
    private static RouteDefinition MakeDefinition(FakeSourceIntake source, FakeSinkAdapter sink)
        => new()
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        };

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) { return; }
            await Task.Delay(5).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    [Fact]
    public async Task Tap_WhenSubscribed_CapturesBothSourceAndSink()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        var tap = new RouteTap();
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), tap: tap);
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        using var sub = tap.Subscribe("route-1"); // activate before data flows
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 5;
        for (var i = 1; i <= total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }

        await WaitForAsync(() => sink.PublishedCount == total, TimeSpan.FromSeconds(5));
        await WaitForAsync(
            () => tap.ReadSince("route-1", 0).Count(c => c.Side == TapSide.Sink) == total,
            TimeSpan.FromSeconds(5));

        var caps = tap.ReadSince("route-1", 0);
        caps.Count(c => c.Side == TapSide.Source).Should().Be(total, "source-side captured pre-transform");
        caps.Count(c => c.Side == TapSide.Sink && c.SinkInstanceId == "sink-1").Should().Be(total);

        var status = tap.GetStatus("route-1");
        status.Active.Should().BeTrue();
        status.SourceCaptured.Should().Be(total);
        status.SinkCaptured.Should().Be(total);
    }

    [Fact]
    public async Task Tap_WhenNotSubscribed_CapturesNothing_HotPathClean()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        var tap = new RouteTap();
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), tap: tap);
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        // No tap.Subscribe — the worker's IsTapActive guard must short-circuit.
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        for (var i = 1; i <= 3; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(() => sink.PublishedCount == 3, TimeSpan.FromSeconds(5));

        tap.IsTapActive("route-1").Should().BeFalse();
        tap.ReadSince("route-1", 0).Should().BeEmpty("data flowed but no one was watching — zero capture");
    }
}
