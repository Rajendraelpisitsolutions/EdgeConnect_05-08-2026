// ============================================================================
// File: Routing/RoutingEngineUnregisterTests.cs
// Purpose: Cover the M.P2.2 hot-reload addition to IRoutingEngine:
//          UnregisterRouteAsync. The contract:
//             * Stops the route if running, then disposes its buffer +
//               dispatcher + worker.
//             * Removes it from the registration map so RegisteredRouteIds
//               no longer lists it.
//             * Idempotent on unknown / already-unregistered ids.
//             * Tolerant of mid-run state (worker actively pumping points).
//          Reference: docs/decisions/0009-runtime-hot-reload-instance-granularity.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class RoutingEngineUnregisterTests
{
    private static RouteDefinition MakeDefinition(
        FakeSourceIntake source,
        FakeSinkAdapter sink,
        string routeId = "route-1")
        => new()
        {
            RouteId = routeId,
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new[] { (ISinkAdapter)sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = RoutingTestData.DefaultDeliveryPolicy(),
        };

    [Fact]
    public async Task UnregisterRoute_RemovesIdFromRegisteredList()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        await engine.UnregisterRouteAsync("route-1", CancellationToken.None);

        engine.RegisteredRouteIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisterRoute_AfterStart_StopsAndRemoves()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);
        engine.GetRouteState("route-1").Should().Be(RouteState.Running);

        await engine.UnregisterRouteAsync("route-1", CancellationToken.None);

        engine.RegisteredRouteIds.Should().BeEmpty();
        // GetRouteState now throws KeyNotFoundException — the route is gone.
        ((Action)(() => engine.GetRouteState("route-1")))
            .Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public async Task UnregisterRoute_UnknownId_IsSilentNoOp()
    {
        // Locked: idempotent on unknown ids. The coordinator may emit
        // a Remove for a route that was never successfully registered
        // (e.g., boot-time fault → no route in engine).
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        var act = async () => await engine.UnregisterRouteAsync("never-existed", CancellationToken.None);

        await act.Should().NotThrowAsync();
        engine.RegisteredRouteIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisterRoute_CalledTwice_IsIdempotent()
    {
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);

        await engine.UnregisterRouteAsync("route-1", CancellationToken.None);
        var act = async () => await engine.UnregisterRouteAsync("route-1", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnregisterRoute_AllowsReRegisteringSameRouteId()
    {
        // After unregister, the route id is free again — re-register
        // must succeed. This is the path the coordinator uses for
        // Restart (= Remove + Add).
        var source1 = new FakeSourceIntake("src-1");
        var sink1 = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(MakeDefinition(source1, sink1), CancellationToken.None);
        await engine.UnregisterRouteAsync("route-1", CancellationToken.None);

        var source2 = new FakeSourceIntake("src-1");
        var sink2 = new FakeSinkAdapter("sink-1");
        var act = async () => await engine.RegisterRouteAsync(
            MakeDefinition(source2, sink2), CancellationToken.None);

        await act.Should().NotThrowAsync();
        engine.RegisteredRouteIds.Should().ContainSingle().Which.Should().Be("route-1");
    }

    [Fact]
    public async Task UnregisterRoute_WithInFlightPoints_DoesNotThrow()
    {
        // Risk path called out in the M.P2.2 kickoff doc: unregistering
        // mid-pump must not surface ObjectDisposedException or other
        // race-related noise out the public method.
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());
        await engine.RegisterRouteAsync(MakeDefinition(source, sink), CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Push 100 points (will mostly buffer and pump out to the sink).
        for (var i = 0; i < 100; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }

        var act = async () => await engine.UnregisterRouteAsync("route-1", CancellationToken.None);

        await act.Should().NotThrowAsync(
            "unregistering during in-flight pumping must surface no exceptions to the caller");
        engine.RegisteredRouteIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisterRoute_NullOrEmpty_Throws()
    {
        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory());

        await ((Func<Task>)(() => engine.UnregisterRouteAsync(null!, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => engine.UnregisterRouteAsync(string.Empty, CancellationToken.None)))
            .Should().ThrowAsync<ArgumentException>();
    }
}
