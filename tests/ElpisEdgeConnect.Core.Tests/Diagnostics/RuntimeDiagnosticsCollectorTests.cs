// ============================================================================
// File: Diagnostics/RuntimeDiagnosticsCollectorTests.cs
// Purpose: Phase 2 tests for the RuntimeDiagnosticsCollector. Pin direct
//          collector behavior (event capture, retention, snapshot
//          consistency) plus an end-to-end pin against a real RoutingEngine
//          to verify the collector observes everything C3 emits.
// Milestone: C4 — phase 2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Core.Tests.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class RuntimeDiagnosticsCollectorTests
{
    private static RouteStateChangedEvent StateEvt(string id, RouteState from, RouteState to)
        => new()
        {
            RouteId = id,
            From = from,
            To = to,
            ObservedAtUtc = DateTime.UtcNow,
        };

    private static SinkDegradedEvent DegradedEvt(string routeId, string sinkId, string code = "X.Y") => new()
    {
        RouteId = routeId,
        SinkInstanceId = sinkId,
        LastError = new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Network,
            Message = "fail",
        },
        RetryAttempts = 1,
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static SinkDrainingEvent DrainingEvt(string routeId, string sinkId) => new()
    {
        RouteId = routeId,
        SinkInstanceId = sinkId,
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static SinkRecoveredEvent RecoveredEvt(string routeId, string sinkId) => new()
    {
        RouteId = routeId,
        SinkInstanceId = sinkId,
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static BackpressureDroppedEvent BpEvt(string routeId, long count) => new()
    {
        RouteId = routeId,
        DroppedCount = count,
        Reason = "test",
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static RoutePointQuarantinedEvent QEvt(string routeId, string tag) => new()
    {
        RouteId = routeId,
        TagName = tag,
        Reason = "InvalidDataException: test",
        ObservedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public void RouteStateChange_IsCaptured_AndCounterIncrements()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.OnRouteStateChanged(StateEvt("r1", RouteState.Configured, RouteState.Starting));
        c.OnRouteStateChanged(StateEvt("r1", RouteState.Starting, RouteState.Running));

        var snap = c.GetRouteSnapshot("r1");
        snap.Should().NotBeNull();
        snap!.State.Should().Be(RouteState.Running);
        snap.StateTransitionCount.Should().Be(2);

        var events = c.GetRouteStateEvents("r1");
        events.Should().NotBeNull();
        events!.Entries.Select(e => $"{e.From}->{e.To}").Should().Equal(
            "Configured->Starting", "Starting->Running");
    }

    [Fact]
    public void SinkLifecycle_DegradedDrainingRecovered_OrderingPinned()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.OnSinkDegraded(DegradedEvt("r1", "s1"));
        c.OnSinkDraining(DrainingEvt("r1", "s1"));
        c.OnSinkRecovered(RecoveredEvt("r1", "s1"));

        var sinkEvents = c.GetSinkEvents("r1", "s1");
        sinkEvents.Should().NotBeNull();
        sinkEvents!.Entries.Select(e => e.Kind).Should().Equal(
            SinkEventKind.Degraded, SinkEventKind.Draining, SinkEventKind.Recovered);

        var snap = c.GetRouteSnapshot("r1")!;
        snap.Sinks.Should().HaveCount(1);
        var sink = snap.Sinks[0];
        sink.IsDegraded.Should().BeFalse();
        sink.IsDraining.Should().BeFalse();
        sink.DegradationEventCount.Should().Be(1);
        sink.RecoveryEventCount.Should().Be(1);
        sink.LastError.Should().NotBeNull();
        sink.LastError!.Code.Should().Be("X.Y");
    }

    [Fact]
    public void BackpressureDrop_AccumulatesCountAndEvent()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.OnBackpressureDropped(BpEvt("r1", 5));
        c.OnBackpressureDropped(BpEvt("r1", 10));
        c.OnBackpressureDropped(BpEvt("r1", 3));

        var snap = c.GetRouteSnapshot("r1")!;
        snap.BackpressureDropCount.Should().Be(18);

        var bp = c.GetBackpressureEvents("r1");
        bp!.Entries.Should().HaveCount(3);
        bp.TotalAdded.Should().Be(3);
    }

    [Fact]
    public void PointQuarantined_AccumulatesCountAndEvent_AndIsDistinctFromBackpressure()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.OnRoutePointQuarantined(QEvt("r1", "production/parts_count"));
        c.OnRoutePointQuarantined(QEvt("r1", "spindle/load"));
        // A backpressure drop on the same route must NOT bleed into the
        // quarantine count — they are distinct signals.
        c.OnBackpressureDropped(BpEvt("r1", 9));

        var snap = c.GetRouteSnapshot("r1")!;
        snap.QuarantinedPointCount.Should().Be(2);
        snap.BackpressureDropCount.Should().Be(9);

        var q = c.GetQuarantineEvents("r1");
        q!.Entries.Should().HaveCount(2);
        q.TotalAdded.Should().Be(2);
        q.Entries[0].TagName.Should().Be("production/parts_count");
    }

    [Fact]
    public void BoundedRetention_DropsOldestAndExposesDropCount()
    {
        // Tight retention: 4 state events. Push 10 — expect 6 drops, latest
        // 4 retained, drop count visible in the snapshot.
        var c = new RuntimeDiagnosticsCollector(new DiagnosticsCollectorOptions
        {
            RouteEventRetention = 4,
        });
        for (var i = 0; i < 10; i++)
        {
            c.OnRouteStateChanged(StateEvt("r1", RouteState.Configured, RouteState.Starting));
        }

        var ev = c.GetRouteStateEvents("r1")!;
        ev.LiveCount.Should().Be(4);
        ev.TotalAdded.Should().Be(10);
        ev.TotalDropped.Should().Be(6);
    }

    [Fact]
    public void GetRouteSnapshot_UnknownRoute_ReturnsNull()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.GetRouteSnapshot("nope").Should().BeNull();
    }

    [Fact]
    public void EnsureRoute_PreCreatesEntry()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.EnsureRoute("r1");
        c.KnownRouteIds.Should().Contain("r1");
        var snap = c.GetRouteSnapshot("r1");
        snap.Should().NotBeNull();
        snap!.State.Should().Be(RouteState.Configured); // default
        snap.StateTransitionCount.Should().Be(0);
    }

    [Fact]
    public void Snapshot_ReflectsCoherentSinglePointInTimeState()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.OnRouteStateChanged(StateEvt("r1", RouteState.Configured, RouteState.Starting));
        c.OnRouteStateChanged(StateEvt("r1", RouteState.Starting, RouteState.Running));
        c.OnSinkDegraded(DegradedEvt("r1", "s1"));
        c.OnSinkDegraded(DegradedEvt("r1", "s2"));
        c.OnRouteStateChanged(StateEvt("r1", RouteState.Running, RouteState.Degraded));
        c.OnBackpressureDropped(BpEvt("r1", 7));
        c.OnSinkDraining(DrainingEvt("r1", "s1"));

        var snap = c.GetRouteSnapshot("r1")!;
        // Single point in time: state, counters, sinks all derived from the
        // same locked region — no parallel store, no torn read.
        snap.State.Should().Be(RouteState.Degraded);
        snap.StateTransitionCount.Should().Be(3);
        snap.BackpressureDropCount.Should().Be(7);
        snap.Sinks.Should().HaveCount(2);
        var s1 = snap.Sinks.Single(s => s.SinkInstanceId == "s1");
        s1.IsDraining.Should().BeTrue();
        s1.IsDegraded.Should().BeFalse();
        var s2 = snap.Sinks.Single(s => s.SinkInstanceId == "s2");
        s2.IsDegraded.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentEventWrites_NeverThrow_AndAreFullyCounted()
    {
        var c = new RuntimeDiagnosticsCollector();
        const int Writers = 16;
        const int PerWriter = 500;

        var tasks = Enumerable.Range(0, Writers).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < PerWriter; i++)
            {
                c.OnBackpressureDropped(BpEvt("r1", 1));
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        var snap = c.GetRouteSnapshot("r1")!;
        snap.BackpressureDropCount.Should().Be(Writers * PerWriter);
    }

    // ── M.P2.2 RemoveRoute ──────────────────────────────────────────

    [Fact]
    public void RemoveRoute_ForgetsAllStateForThatRoute()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.EnsureRoute("route-1");
        collector.EnsureRoute("route-2");
        collector.OnRouteStateChanged(StateEvt("route-1", RouteState.Configured, RouteState.Starting));
        collector.OnSinkDegraded(DegradedEvt("route-1", "sink-1"));

        collector.RemoveRoute("route-1");

        collector.GetKnownRoutes().Should().BeEquivalentTo(new[] { "route-2" });
        collector.GetRouteSnapshot("route-1").Should().BeNull();
        collector.GetRouteSnapshot("route-2").Should().NotBeNull();
    }

    [Fact]
    public void RemoveRoute_UnknownId_IsSilentNoOp()
    {
        // Locked: idempotent on unknown ids — the coordinator may call
        // this for routes that never got a snapshot (boot-time faults).
        var collector = new RuntimeDiagnosticsCollector();

        var act = () => collector.RemoveRoute("never-seen");

        act.Should().NotThrow();
        collector.GetKnownRoutes().Should().BeEmpty();
    }

    [Fact]
    public void RemoveRoute_ThenEnsureRoute_ResurrectsFreshState()
    {
        // After RemoveRoute, re-using the same route id must produce a
        // fresh state — no stale counters. The coordinator uses this
        // path for Restart (= Remove + Add).
        var collector = new RuntimeDiagnosticsCollector();
        collector.EnsureRoute("route-1");
        collector.OnRouteStateChanged(StateEvt("route-1", RouteState.Configured, RouteState.Starting));
        collector.OnRouteStateChanged(StateEvt("route-1", RouteState.Starting, RouteState.Running));

        collector.RemoveRoute("route-1");
        collector.EnsureRoute("route-1");

        var snap = collector.GetRouteSnapshot("route-1");
        snap.Should().NotBeNull();
        snap!.State.Should().Be(RouteState.Configured, "fresh state, not leftover Running");
        snap.StateTransitionCount.Should().Be(0, "transition counter resets on remove + re-add");
    }

    [Fact]
    public void RemoveRoute_NullOrEmpty_Throws()
    {
        var collector = new RuntimeDiagnosticsCollector();

        ((Action)(() => collector.RemoveRoute(null!))).Should().Throw<ArgumentException>();
        ((Action)(() => collector.RemoveRoute(string.Empty))).Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// Phase 2 integration: drive a real <see cref="RoutingEngine"/> with the
/// collector wired in via <see cref="IRoutingEngineDiagnostics"/>. Verifies
/// every C3 event reaches the collector.
/// </summary>
[Collection(RoutingIntegrationCollection.Name)]
public sealed class RuntimeDiagnosticsCollectorEngineWiringTests
{
    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(5);
        }
        throw new TimeoutException("Predicate did not become true in time.");
    }

    [Fact]
    public async Task RoutingEngine_EmitsAllEventsIntoCollector()
    {
        // D phase-1 stabilization: use FailUntilSignaled TCS instead of the
        // FailPermanently flag toggle. Eliminates the race where a heal
        // lands between a retry backoff and the next publish attempt.
        var collector = new RuntimeDiagnosticsCollector();
        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(
            new InMemoryRouteBufferFactory(),
            collector);

        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new ISinkAdapter[] { sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = new DeliveryPolicyConfig
            {
                Mode = DeliveryMode.AtLeastOnce,
                MaxRetries = 1000,
                InitialBackoffMs = 1,
                MaxBackoffMs = 5,
                BackoffMultiplier = 2.0,
                JitterPercent = 0,
            },
        }, CancellationToken.None);

        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Healthy run.
        for (var i = 0; i < 50; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }

        // Induce a failure window with a deterministic TCS gate.
        var failureWindow = new TaskCompletionSource();
        sink.FailUntilSignaled = failureWindow;
        for (var i = 50; i < 100; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
        }
        await WaitForAsync(
            () => collector.GetRouteSnapshot("route-1")?.Sinks.Any(s => s.IsDegraded) == true,
            TimeSpan.FromSeconds(10));

        // Heal (deterministic signal) and finish.
        failureWindow.SetResult();
        source.Complete();

        await WaitForAsync(() => sink.PublishedCount == 100, TimeSpan.FromSeconds(10));
        await engine.StopRouteAsync("route-1", CancellationToken.None);

        // Final snapshot must reflect the full lifecycle walked through C3.
        var snap = collector.GetRouteSnapshot("route-1")!;
        snap.State.Should().Be(RouteState.Stopped);
        snap.StateTransitionCount.Should().BeGreaterThanOrEqualTo(6);
        snap.Sinks.Should().HaveCount(1);
        var s = snap.Sinks[0];
        s.DegradationEventCount.Should().BeGreaterThanOrEqualTo(1);
        s.RecoveryEventCount.Should().BeGreaterThanOrEqualTo(1);

        // The collector's state-event log should contain the full ordered
        // sequence of route transitions emitted by the engine.
        var stateEvents = collector.GetRouteStateEvents("route-1")!;
        var transitions = stateEvents.Entries.Select(e => $"{e.From}->{e.To}").ToArray();
        transitions.Should().Contain("Configured->Starting");
        transitions.Should().Contain("Starting->Running");
        transitions.Should().Contain("Running->Degraded");
        transitions.Should().Contain("Degraded->Draining");
        transitions.Should().Contain("Draining->Running");
        transitions.Should().Contain("Stopping->Stopped");
    }
}
