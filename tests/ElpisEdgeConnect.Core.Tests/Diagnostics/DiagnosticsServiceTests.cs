// ============================================================================
// File: Diagnostics/DiagnosticsServiceTests.cs
// Purpose: Phase 4 tests for the IDiagnosticsService query façade.
//          Because the collector IS the service (single-state-store
//          guarantee), these tests exercise the service through its
//          typed interface to pin that the read surface remains a thin
//          façade with no parallel read model.
// Milestone: C4 — phase 4.
// ============================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class DiagnosticsServiceTests
{
    private static RouteStateChangedEvent Evt(string id, RouteState from, RouteState to) => new()
    {
        RouteId = id,
        From = from,
        To = to,
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static SinkDegradedEvent Degraded(string routeId, string sinkId) => new()
    {
        RouteId = routeId,
        SinkInstanceId = sinkId,
        LastError = new AdapterError
        {
            Code = "X.Y",
            Category = ErrorCategory.Network,
            Message = "fail",
        },
        RetryAttempts = 1,
        ObservedAtUtc = DateTime.UtcNow,
    };

    private static BackpressureDroppedEvent Bp(string routeId, long count) => new()
    {
        RouteId = routeId,
        DroppedCount = count,
        Reason = "test",
        ObservedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public void GetKnownRoutes_ReturnsStableSnapshot()
    {
        var collector = new RuntimeDiagnosticsCollector();
        IDiagnosticsService svc = collector;

        collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Starting));
        collector.OnRouteStateChanged(Evt("r2", RouteState.Configured, RouteState.Starting));
        collector.OnRouteStateChanged(Evt("r3", RouteState.Configured, RouteState.Starting));

        var routes = svc.GetKnownRoutes();
        routes.Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });

        // Must be a fresh array — not a live view.
        var second = svc.GetKnownRoutes();
        routes.Should().NotBeSameAs(second);
    }

    [Fact]
    public void GetRouteSnapshot_UnknownRoute_ReturnsNull()
    {
        IDiagnosticsService svc = new RuntimeDiagnosticsCollector();
        svc.GetRouteSnapshot("nope").Should().BeNull();
    }

    [Fact]
    public void GetAllRouteSnapshots_ReturnsEmptyWhenNoRoutesKnown()
    {
        IDiagnosticsService svc = new RuntimeDiagnosticsCollector();
        svc.GetAllRouteSnapshots().Should().BeEmpty();
    }

    [Fact]
    public void GetAllRouteSnapshots_ReturnsOnePerKnownRoute()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Running));
        collector.OnRouteStateChanged(Evt("r2", RouteState.Configured, RouteState.Running));

        IDiagnosticsService svc = collector;
        var snaps = svc.GetAllRouteSnapshots();
        snaps.Should().HaveCount(2);
        snaps.Select(s => s.RouteId).Should().BeEquivalentTo(new[] { "r1", "r2" });
        snaps.All(s => s.State == RouteState.Running).Should().BeTrue();
    }

    [Fact]
    public void GetRouteStateEvents_ExposesRetentionCounters()
    {
        var collector = new RuntimeDiagnosticsCollector(new DiagnosticsCollectorOptions
        {
            RouteEventRetention = 3,
        });
        IDiagnosticsService svc = collector;

        // Push 8 route state events against a log with capacity=3.
        for (var i = 0; i < 8; i++)
        {
            collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Starting));
        }

        var log = svc.GetRouteStateEvents("r1");
        log.Should().NotBeNull();
        log!.Capacity.Should().Be(3);
        log.LiveCount.Should().Be(3);
        log.TotalAdded.Should().Be(8);
        log.TotalDropped.Should().Be(5);
        log.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void GetRouteStateEvents_UnknownRoute_ReturnsNull()
    {
        IDiagnosticsService svc = new RuntimeDiagnosticsCollector();
        svc.GetRouteStateEvents("nope").Should().BeNull();
    }

    [Fact]
    public void GetSinkEvents_UnknownRouteOrSink_ReturnsNull_AsDocumented()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Running));

        IDiagnosticsService svc = collector;

        // Unknown route: null
        svc.GetSinkEvents("nope", "sink-1").Should().BeNull();
        // Known route but unknown sink: null
        svc.GetSinkEvents("r1", "nope").Should().BeNull();

        // After a sink event, the query returns a real snapshot.
        collector.OnSinkDegraded(Degraded("r1", "sink-1"));
        var log = svc.GetSinkEvents("r1", "sink-1");
        log.Should().NotBeNull();
        log!.LiveCount.Should().Be(1);
        log.Entries[0].Kind.Should().Be(SinkEventKind.Degraded);
    }

    [Fact]
    public void GetBackpressureEvents_UnknownRoute_ReturnsNull()
    {
        IDiagnosticsService svc = new RuntimeDiagnosticsCollector();
        svc.GetBackpressureEvents("nope").Should().BeNull();
    }

    [Fact]
    public void GetBackpressureEvents_ReflectsCollectorState()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(Bp("r1", 3));
        collector.OnBackpressureDropped(Bp("r1", 7));

        IDiagnosticsService svc = collector;
        var log = svc.GetBackpressureEvents("r1")!;
        log.LiveCount.Should().Be(2);
        log.TotalAdded.Should().Be(2);
        // Route snapshot's counter sums the drop counts, not the event count.
        svc.GetRouteSnapshot("r1")!.BackpressureDropCount.Should().Be(10);
    }

    [Fact]
    public void QuerySurface_ReflectsCollectorStateWithoutDrift()
    {
        // Single-source-of-truth pin: every query method must read the
        // same collector state. We mutate the collector then observe via
        // IDiagnosticsService and expect perfect consistency — no cached
        // snapshot lagging behind the writes.
        var collector = new RuntimeDiagnosticsCollector();
        IDiagnosticsService svc = collector;

        collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Starting));
        svc.GetRouteSnapshot("r1")!.State.Should().Be(RouteState.Starting);

        collector.OnRouteStateChanged(Evt("r1", RouteState.Starting, RouteState.Running));
        svc.GetRouteSnapshot("r1")!.State.Should().Be(RouteState.Running);

        collector.OnSinkDegraded(Degraded("r1", "s1"));
        svc.GetRouteSnapshot("r1")!.Sinks.Single().IsDegraded.Should().BeTrue();

        collector.OnBackpressureDropped(Bp("r1", 5));
        svc.GetRouteSnapshot("r1")!.BackpressureDropCount.Should().Be(5);

        // The recent-events queries must agree with the top-level snapshot.
        var stateLog = svc.GetRouteStateEvents("r1")!;
        stateLog.LiveCount.Should().Be(2);
        svc.GetRouteSnapshot("r1")!.StateTransitionCount.Should().Be(stateLog.LiveCount);

        var bpLog = svc.GetBackpressureEvents("r1")!;
        bpLog.LiveCount.Should().Be(1);
        svc.GetRouteSnapshot("r1")!.BackpressureDropCount.Should().Be(5);
    }

    [Fact]
    public async Task ConcurrentWrites_QueriesRemainConsistent()
    {
        // Writers and readers run in parallel. The writer increments the
        // backpressure counter; the reader must always observe a count
        // that is monotonically non-decreasing and bounded by TotalWrites.
        // (The previous version of this test compared observed <= a
        // separately-tracked writesCompleted counter, which had a race:
        // collector.OnBackpressureDropped happens before the counter
        // increment, so a reader between those two steps could see a
        // value 1 higher than writesCompleted. The invariant that matters
        // is monotonicity + final total, not per-step tracking.)
        var collector = new RuntimeDiagnosticsCollector();
        IDiagnosticsService svc = collector;

        const int TotalWrites = 5_000;
        long lastObserved = 0;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < TotalWrites; i++)
            {
                collector.OnBackpressureDropped(Bp("r1", 1));
            }
        });

        var reader = Task.Run(() =>
        {
            while (!writer.IsCompleted)
            {
                var snap = svc.GetRouteSnapshot("r1");
                if (snap is not null)
                {
                    snap.BackpressureDropCount.Should().BeGreaterThanOrEqualTo(lastObserved,
                        "reader must never observe the counter go backwards");
                    lastObserved = snap.BackpressureDropCount;
                    lastObserved.Should().BeLessThanOrEqualTo(TotalWrites,
                        "reader must never observe more writes than the writer can possibly make");
                }
            }
        });

        await Task.WhenAll(writer, reader);

        svc.GetRouteSnapshot("r1")!.BackpressureDropCount.Should().Be(TotalWrites);
    }
}
