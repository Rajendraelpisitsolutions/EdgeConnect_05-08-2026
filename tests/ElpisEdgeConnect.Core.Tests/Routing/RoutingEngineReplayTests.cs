// ============================================================================
// File: Routing/RoutingEngineReplayTests.cs
// Purpose: Phase 4 engine-level tests for replay semantics.
//          * Drain is strictly sequential per sink cursor.
//          * A recovered sink drains backlog before live data.
//          * No duplicate acked batches.
//          * Sink transitions degraded → draining → running in that order.
//          * Live traffic that arrives during drain waits its turn.
//          * A recovered sink does not affect a healthy sink on the same route.
// Milestone: C3 Commit 2 (phase 4).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineReplayTests
{
    private static RouteDefinition Definition(
        FakeSourceIntake source,
        IReadOnlyList<ISinkAdapter> sinks,
        DeliveryPolicyConfig delivery,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = sinks,
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = delivery,
        };

    private static DeliveryPolicyConfig FastRetry(int maxRetries = 100)
        => new()
        {
            Mode = DeliveryMode.AtLeastOnce,
            MaxRetries = maxRetries,
            InitialBackoffMs = 1,
            MaxBackoffMs = 10,
            BackoffMultiplier = 2.0,
            JitterPercent = 0,
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
    public async Task Replay_SequentialOrderMaintained()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailPermanently = true,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Push 50 points while the sink is failing. They accumulate in the
        // buffer behind a stuck cursor.
        for (var i = 0; i < 50; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(() => sink.AttemptCount > 0, TimeSpan.FromSeconds(2));

        // Heal the sink. Drain kicks in; cursor walks the backlog in order.
        sink.FailPermanently = false;
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == 50, TimeSpan.FromSeconds(5));

        var seqs = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        seqs.Should().BeInAscendingOrder();
        seqs.Should().Equal(Enumerable.Range(0, 50).Select(i => (long)i));

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task RecoveredSink_DrainsBacklogBeforeLive()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailPermanently = true,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // 20 backlog points arrive first while failing.
        for (var i = 0; i < 20; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(() => sink.AttemptCount > 0, TimeSpan.FromSeconds(2));

        // Heal sink, then immediately add live points. The live points are
        // appended AFTER the backlog in the buffer, so a correctly-ordered
        // drain must deliver backlog first.
        sink.FailPermanently = false;
        for (var i = 100; i < 105; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == 25, TimeSpan.FromSeconds(5));

        var delivered = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        delivered.Should().HaveCount(25);
        // Backlog (0..19) must all appear before any live point (100..104).
        var firstLiveIndex = Array.FindIndex(delivered, s => s >= 100);
        var lastBacklogIndex = Array.FindLastIndex(delivered, s => s < 20);
        lastBacklogIndex.Should().BeLessThan(firstLiveIndex,
            "backlog must drain entirely before any live point is delivered");
        // And the delivered stream overall is monotonic.
        delivered.Should().BeInAscendingOrder();

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task Replay_NoDuplicateAckedBatches()
    {
        // Transient failures on the first batch, then success. The same
        // sequence numbers must NEVER be delivered twice — the cursor
        // advances only after a successful publish, so retried batches do
        // not produce duplicates at the sink.
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailNext = 4,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 30;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == total, TimeSpan.FromSeconds(5));

        // AttemptCount includes retried batches (>= 4 failures + at least 1
        // success). PublishedCount counts actually-stored points and must
        // equal the unique sequence count.
        sink.AttemptCount.Should().BeGreaterThanOrEqualTo(5);
        var seqs = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        seqs.Should().OnlyHaveUniqueItems();
        seqs.Should().HaveCount(total);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task SinkRecovery_TransitionsDegradedToDrainingToRunning()
    {
        var diagnostics = new RecordingRoutingDiagnostics();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailPermanently = true,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), diagnostics);
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Create backlog while failing.
        for (var i = 0; i < 10; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(
            () => diagnostics.Events.Any(e => e.StartsWith("degraded:", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(2));

        // Heal — expect draining then recovered events.
        sink.FailPermanently = false;
        source.Complete();

        await WaitForAsync(
            () => diagnostics.Events.Any(e => e.StartsWith("recovered:", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5));

        var sinkEvents = diagnostics.Events
            .Where(e => e.EndsWith(":sink-1", StringComparison.Ordinal))
            .ToArray();

        // Must contain all three, in order: degraded → draining → recovered.
        var idxDegraded = Array.FindIndex(sinkEvents, e => e == "degraded:sink-1");
        var idxDraining = Array.FindIndex(sinkEvents, e => e == "draining:sink-1");
        var idxRecovered = Array.FindIndex(sinkEvents, e => e == "recovered:sink-1");
        idxDegraded.Should().BeGreaterThanOrEqualTo(0);
        idxDraining.Should().BeGreaterThan(idxDegraded);
        idxRecovered.Should().BeGreaterThan(idxDraining);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task LiveTraffic_DuringDrain_WaitsItsTurn()
    {
        // Slower variant of DrainsBacklogBeforeLive: we slow the sink down
        // so live data has ample time to interleave, then verify the
        // delivered stream is still strictly monotonic AND the backlog is
        // delivered before any live point.
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1")
        {
            FailPermanently = true,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { sink }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        for (var i = 0; i < 10; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(() => sink.AttemptCount > 0, TimeSpan.FromSeconds(2));

        // Heal and slow down — gives live data time to arrive mid-drain.
        sink.PublishDelay = TimeSpan.FromMilliseconds(20);
        sink.FailPermanently = false;

        // Feed live points spread out over a few hundred ms.
        for (var i = 100; i < 110; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
            await Task.Delay(10);
        }
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == 20, TimeSpan.FromSeconds(10));

        var delivered = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        delivered.Should().BeInAscendingOrder();
        var firstLiveIndex = Array.FindIndex(delivered, s => s >= 100);
        var lastBacklogIndex = Array.FindLastIndex(delivered, s => s < 20);
        lastBacklogIndex.Should().BeLessThan(firstLiveIndex);

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task RecoveredSink_DoesNotAffectHealthySink()
    {
        var source = new FakeSourceIntake("src-1");
        var healthy = new FakeSinkAdapter("sink-healthy");
        var cycling = new FakeSinkAdapter("sink-cycling")
        {
            FailPermanently = true,
        };

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            Definition(source, new ISinkAdapter[] { healthy, cycling }, FastRetry()),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // 30 points through both sinks — healthy one takes them all
        // immediately; cycling one is stuck retrying.
        for (var i = 0; i < 30; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(() => healthy.PublishedCount == 30, TimeSpan.FromSeconds(5));
        cycling.PublishedCount.Should().Be(0);

        // Heal the cycling sink. It must drain its backlog without
        // disturbing the healthy sink (which should still be at 30 and
        // happily receive new traffic).
        cycling.FailPermanently = false;
        for (var i = 100; i < 105; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(() => cycling.PublishedCount == 35, TimeSpan.FromSeconds(5));
        await WaitForAsync(() => healthy.PublishedCount == 35, TimeSpan.FromSeconds(5));

        healthy.PublishedPoints.Select(p => p.SequenceNumber).Should().BeInAscendingOrder();
        cycling.PublishedPoints.Select(p => p.SequenceNumber).Should().BeInAscendingOrder();

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }
}
