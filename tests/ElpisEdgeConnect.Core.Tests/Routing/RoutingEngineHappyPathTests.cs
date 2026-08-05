// ============================================================================
// File: Routing/RoutingEngineHappyPathTests.cs
// Purpose: Phase 1 (happy path) coverage for RoutingEngine / RouteWorker.
//          Exercises register → start → pump → publish → ack → stop with
//          a fake intake, fake sink, in-memory buffer, and tag filter.
// Milestone: C3 Commit 2 (phase 1).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineHappyPathTests
{
    private static RouteDefinition MakeDefinition(
        FakeSourceIntake source,
        FakeSinkAdapter sink,
        TagFilter? filter = null,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (ElpisEdgeConnect.Core.Adapters.ISinkAdapter)sink },
            Filter = filter ?? TagFilter.AcceptAll,
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
    public async Task RegisterRoute_PutsRouteInConfiguredState()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        engine.RegisteredRouteIds.Should().ContainSingle().Which.Should().Be("route-1");
        engine.GetRouteState("route-1").Should().Be(RouteState.Configured);
    }

    [Fact]
    public async Task RegisterRoute_RejectsDuplicateId()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        var act = async () => await engine.RegisterRouteAsync(
            MakeDefinition(source, sink),
            CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StartRoute_TransitionsToRunning()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        engine.GetRouteState("route-1").Should().Be(RouteState.Running);
    }

    [Fact]
    public async Task HappyPath_EndToEnd_DeliversAllPointsToSink()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        const int total = 500;
        for (var i = 0; i < total; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == total, TimeSpan.FromSeconds(5));

        await engine.StopRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);

        sink.PublishedCount.Should().Be(total);
        sink.PublishedPoints.Select(p => p.SequenceNumber)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task HappyPath_TagFilter_SuppressesExcludedPoints()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        var filter = new TagFilter(new TagFilterConfig
        {
            Include = new[] { "Spindle/*" },
            Exclude = new[] { "Spindle/Temp" },
        });

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(
            MakeDefinition(source, sink, filter),
            CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        await source.WriteAsync(RoutingTestData.MakePoint(1, "Spindle/Load"));
        await source.WriteAsync(RoutingTestData.MakePoint(2, "Spindle/Temp"));  // excluded
        await source.WriteAsync(RoutingTestData.MakePoint(3, "Axis/Pos"));      // not included
        await source.WriteAsync(RoutingTestData.MakePoint(4, "Spindle/Rpm"));
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount >= 2, TimeSpan.FromSeconds(5));
        await Task.Delay(50); // give excluded points a chance to falsely appear

        sink.PublishedCount.Should().Be(2);
        sink.PublishedPoints.Select(p => p.TagName)
            .Should().BeEquivalentTo(new[] { "Spindle/Load", "Spindle/Rpm" });

        await engine.StopRouteAsync("route-1", CancellationToken.None);
    }

    [Fact]
    public async Task StopRoute_IsIdempotent_WhenAlreadyStopped()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);
        await engine.StopRouteAsync("route-1", CancellationToken.None);
        await engine.StopRouteAsync("route-1", CancellationToken.None); // no throw

        engine.GetRouteState("route-1").Should().Be(RouteState.Stopped);
    }

    [Fact]
    public void GetRouteState_ThrowsForUnknownRoute()
    {
        using var engineSync = new RoutingEngineNoDispose();
        var act = () => engineSync.Engine.GetRouteState("missing");
        act.Should().Throw<System.Collections.Generic.KeyNotFoundException>();
    }

    private sealed class RoutingEngineNoDispose : IDisposable
    {
        public RoutingEngine Engine { get; } = new(new InMemoryRouteBufferFactory());
        public void Dispose() => Engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

public sealed class TagFilterTests
{
    [Fact]
    public void AcceptAll_MatchesAnyTag()
    {
        TagFilter.AcceptAll.IsMatch("Anything").Should().BeTrue();
        TagFilter.AcceptAll.IsMatch("Spindle/Load").Should().BeTrue();
    }

    [Fact]
    public void Include_Glob_Matches()
    {
        var f = new TagFilter(new TagFilterConfig { Include = new[] { "Spindle/*" } });
        f.IsMatch("Spindle/Load").Should().BeTrue();
        f.IsMatch("Axis/Pos").Should().BeFalse();
    }

    [Fact]
    public void Exclude_Overrides_Include()
    {
        var f = new TagFilter(new TagFilterConfig
        {
            Include = new[] { "Spindle/*" },
            Exclude = new[] { "Spindle/Temp" },
        });
        f.IsMatch("Spindle/Load").Should().BeTrue();
        f.IsMatch("Spindle/Temp").Should().BeFalse();
    }

    [Fact]
    public void Matching_IsCaseInsensitive()
    {
        var f = new TagFilter(new TagFilterConfig { Include = new[] { "spindle/*" } });
        f.IsMatch("Spindle/Load").Should().BeTrue();
    }
}
