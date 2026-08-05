// ============================================================================
// File: Routing/RoutingEngineFanoutTests.cs
// Purpose: Phase 2 multi-sink happy-path tests. Pins that one route can feed
//          multiple sinks, the route worker enqueues once, each sink reads
//          through its own cursor, and a slow sink does not block fast sinks.
// Milestone: C3 Commit 2 (phase 2 — wake-only fanout).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineFanoutTests
{
    private static RouteDefinition MakeDefinition(
        FakeSourceIntake source,
        IReadOnlyList<ISinkAdapter> sinks,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = sinks,
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        };

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(5).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    [Fact]
    public async Task TwoSinks_BothReceiveAllPoints()
    {
        var source = new FakeSourceIntake("src-1");
        var s1 = new FakeSinkAdapter("sink-1");
        var s2 = new FakeSinkAdapter("sink-2");

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { s1, s2 }),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 400;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(
            () => s1.PublishedCount == total && s2.PublishedCount == total,
            TimeSpan.FromSeconds(5));

        await engine.StopRouteAsync("route-1", CancellationToken.None);

        s1.PublishedCount.Should().Be(total);
        s2.PublishedCount.Should().Be(total);
        s1.PublishedPoints.Select(p => p.SequenceNumber).Should().BeInAscendingOrder();
        s2.PublishedPoints.Select(p => p.SequenceNumber).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ThreeSinks_AllReceiveAllPoints()
    {
        var source = new FakeSourceIntake("src-1");
        var sinks = new[]
        {
            new FakeSinkAdapter("sink-1"),
            new FakeSinkAdapter("sink-2"),
            new FakeSinkAdapter("sink-3"),
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            MakeDefinition(source, sinks),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 200;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(
            () => sinks.All(s => s.PublishedCount == total),
            TimeSpan.FromSeconds(5));

        await engine.StopRouteAsync("route-1", CancellationToken.None);

        foreach (var sink in sinks)
        {
            sink.PublishedCount.Should().Be(total);
        }
    }

    [Fact]
    public async Task SlowSink_DoesNotBlockFastSink()
    {
        // D phase-1 stabilization: replaced wall-clock PublishDelay + stopwatch
        // assertion with a deterministic SemaphoreSlim gate on the slow sink.
        // The gate starts at zero permits (slow sink blocks on its first
        // publish). Fast sink drains all 200 points while slow is at zero.
        // Then we release the gate and let the slow sink catch up.
        var source = new FakeSourceIntake("src-1");
        var fast = new FakeSinkAdapter("sink-fast");
        using var slowGate = new SemaphoreSlim(initialCount: 0, maxCount: int.MaxValue);
        var slow = new FakeSinkAdapter("sink-slow")
        {
            PublishGate = slowGate,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { fast, slow }),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 200;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        // Fast sink must drain everything WHILE slow is blocked on its gate.
        await WaitForAsync(() => fast.PublishedCount == total, TimeSpan.FromSeconds(10));

        // Invariant: at the instant fast finished, slow had delivered nothing
        // because its gate was never released.
        slow.PublishedCount.Should().Be(0,
            "slow sink must be blocked on its gate while fast sink drained");

        // Release enough permits that the slow sink can drain.
        slowGate.Release(releaseCount: total);
        await WaitForAsync(() => slow.PublishedCount == total, TimeSpan.FromSeconds(10));

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task MultiSink_RouteStopsCleanly()
    {
        var source = new FakeSourceIntake("src-1");
        var s1 = new FakeSinkAdapter("sink-1");
        var s2 = new FakeSinkAdapter("sink-2");

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            MakeDefinition(source, new ISinkAdapter[] { s1, s2 }),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Don't complete the source — stop must still wake parked sink loops
        // via the dispatcher so they observe cancellation.
        await engine.StopRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);
    }
}
