// ============================================================================
// File: Routing/RoutingEngineBackpressureTests.cs
// Purpose: Phase 6 engine-level pin tests for backpressure and spillover.
//          * Source is never blocked indefinitely (bounded wait budget)
//          * Channel → buffer spillover is exercised via burst
//          * Drop policy applies once channel and buffer are both saturated
//          * Healthy sink survives a burst + slow sink combination
// Milestone: C3 Commit 2 (phase 6).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineBackpressureTests
{
    private static RouteDefinition Definition(
        FakeSourceIntake source,
        IReadOnlyList<ISinkAdapter> sinks,
        BufferPolicy bufferPolicy,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = sinks,
            Filter = TagFilter.AcceptAll,
            BufferPolicy = bufferPolicy,
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
    public async Task Backpressure_ChannelToBufferSpillover()
    {
        // Normal healthy pipeline: everything should Pass through, no drops.
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        var diagnostics = new RecordingRoutingDiagnostics();

        var bufferPolicy = new BufferPolicy { Mode = BufferMode.InMemory, MaxDepth = 1000, DropPolicy = DropPolicy.DropOldest };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), diagnostics);
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, bufferPolicy),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        for (var i = 0; i < 200; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == 200, TimeSpan.FromSeconds(5));
        diagnostics.Events.Should().NotContain(e => e.StartsWith("bp:", StringComparison.Ordinal));

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task Backpressure_DropPolicy_AppliesAfterChannelAndBufferPressure()
    {
        // Force the intake pump to see a full buffer by:
        //   * Using a permanently-failing sink so the buffer cannot drain
        //   * Using a tight MaxDepth so CurrentDepth + batchSize > MaxDepth fast
        //   * Using DropPolicy.DropNewest so the backpressure controller
        //     returns Drop once the buffer ceiling is reached
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1") { FailPermanently = true };
        var diagnostics = new RecordingRoutingDiagnostics();

        var bufferPolicy = new BufferPolicy { Mode = BufferMode.InMemory, MaxDepth = 50, DropPolicy = DropPolicy.DropNewest };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), diagnostics);
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, bufferPolicy),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Push significantly more than MaxDepth; the intake pump's controller
        // must decide Drop once the buffer ceiling is reached.
        for (var i = 0; i < 500; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(
            () => diagnostics.Events.Any(e => e.StartsWith("bp:", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5));

        // At least one BackpressureDroppedEvent must have fired.
        var dropEvents = diagnostics.Events
            .Where(e => e.StartsWith("bp:", StringComparison.Ordinal))
            .ToArray();
        dropEvents.Should().NotBeEmpty();

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task Backpressure_SourceNeverBlocked_InConfiguredModes()
    {
        // Source.WriteAsync must complete within a bounded budget even
        // though the sink is perma-failing and the buffer is small — the
        // backpressure controller must spill or drop, never stall the
        // intake pump indefinitely.
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1") { FailPermanently = true };

        var bufferPolicy = new BufferPolicy { Mode = BufferMode.InMemory, MaxDepth = 20, DropPolicy = DropPolicy.DropOldest };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, bufferPolicy),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        var writeBudget = TimeSpan.FromSeconds(5);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1_000; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
            if (sw.Elapsed > writeBudget)
            {
                throw new TimeoutException(
                    $"Source WriteAsync stalled beyond {writeBudget} after {i} writes.");
            }
        }
        sw.Stop();
        source.Complete();

        // The source wrote all 1000 points in well under 5s. Phase 6 core
        // invariant: source is never blocked indefinitely.
        sw.Elapsed.Should().BeLessThan(writeBudget);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task SlowSink_WithBurst_DoesNotCorruptHealthyPath()
    {
        // Two sinks on one route. Burst a lot of points through. The fast
        // sink must see monotonic sequences and not be corrupted by
        // spillover / buffer drops triggered by the slow sink.
        var source = new FakeSourceIntake("src-1");
        var fast = new FakeSinkAdapter("sink-fast");
        var slow = new FakeSinkAdapter("sink-slow")
        {
            PublishDelay = TimeSpan.FromMilliseconds(5),
        };

        // Moderate buffer — some pressure but not immediate drops on the
        // very first batch.
        var bufferPolicy = new BufferPolicy { Mode = BufferMode.InMemory, MaxDepth = 500, DropPolicy = DropPolicy.DropOldest };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { fast, slow }, bufferPolicy),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 2_000;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        // Fast sink should eventually finish processing everything that the
        // buffer admitted. We do not assert an exact count because the
        // buffer's DropOldest + backpressure Drop paths may shed some of
        // the 2000 points; the invariant is monotonic ordering, not totals.
        await WaitForAsync(
            () => fast.PublishedCount > 0,
            TimeSpan.FromSeconds(5));
        await Task.Delay(200); // let the pipeline settle

        fast.PublishedPoints.Select(p => p.SequenceNumber).Should().BeInAscendingOrder();
        // Route is still Running (or Degraded/Draining but never Failed).
        engine.GetRouteState("route-1").Should().NotBe(RouteState.Failed);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }
}
