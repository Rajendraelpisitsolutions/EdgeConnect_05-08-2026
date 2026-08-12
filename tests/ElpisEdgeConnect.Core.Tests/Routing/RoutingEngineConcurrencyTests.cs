// ============================================================================
// File: Routing/RoutingEngineConcurrencyTests.cs
// Purpose: Pin tests for review-finding 4.A — StartRouteAsync must be safe
//          under concurrent invocation. Two callers racing into Start must
//          NOT both spawn a worker.
// Milestone: C3 Commit 2 (review fixes).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineConcurrencyTests
{
    [Fact]
    public async Task StartRouteAsync_ConcurrentCalls_SpawnExactlyOneWorker()
    {
        // Race many concurrent Start callers against a freshly registered
        // route. Only ONE Configured→Starting→Running pair of events should
        // appear; the rest must hit the idempotent early-return.
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            diagnostics);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new ISinkAdapter[] { sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        }, CancellationToken.None);

        const int RaceCount = 32;
        using var startBarrier = new Barrier(RaceCount);
        var tasks = Enumerable.Range(0, RaceCount).Select(_ => Task.Run(async () =>
        {
            startBarrier.SignalAndWait();
            await engine.StartRouteAsync("route-1", CancellationToken.None);
        })).ToArray();
        await Task.WhenAll(tasks);

        // Push some data to confirm the route works after the race.
        for (var i = 0; i < 50; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (sink.PublishedCount < 50 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        sink.PublishedCount.Should().Be(50);

        await engine.StopRouteAsync("route-1", CancellationToken.None);

        // Exactly one Configured→Starting and one Starting→Running event.
        var routeEvents = diagnostics.Events
            .Where(e => e.StartsWith("route:route-1:", StringComparison.Ordinal))
            .ToArray();
        routeEvents.Count(e => e == "route:route-1:Configured->Starting").Should().Be(1);
        routeEvents.Count(e => e == "route:route-1:Starting->Running").Should().Be(1);
    }
}
