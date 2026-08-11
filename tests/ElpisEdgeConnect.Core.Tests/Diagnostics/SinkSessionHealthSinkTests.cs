// ============================================================================
// File: Diagnostics/SinkSessionHealthSinkTests.cs
// Purpose: H.2 tests for the new ISinkSessionHealthSink seam:
//            * RecordActiveSessions stores per-(routeId, sinkId) latest
//            * The session list surfaces on SinkHealthSnapshot.ActiveSessions
//            * Snapshot construction merges adapter-state pushes and session
//              pushes into one SinkHealthSnapshot (single state store proof)
//            * Empty / null lists are valid "no clients connected" signals
//          Mirrors the structure of DiagnosticsCollectorPipelineAndHealthTests.
// ============================================================================

using System;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class SinkSessionHealthSinkTests
{
    private static SinkSessionSummary MakeSummary(
        string id,
        int subscriptions = 0,
        int monitoredItems = 0) => new()
        {
            SessionId = id,
            SessionName = $"client-{id}",
            ConnectedAtUtc = DateTime.UtcNow,
            SubscriptionCount = subscriptions,
            MonitoredItemCount = monitoredItems,
            UserTokenType = "Anonymous",
        };

    [Fact]
    public void RecordActiveSessions_AppearsOnSinkSnapshot()
    {
        var c = new RuntimeDiagnosticsCollector();
        c.RecordSinkAdapterState("r1", "opcua-1", AdapterState.Running, lastError: null);

        c.RecordActiveSessions("r1", "opcua-1", new[]
        {
            MakeSummary("s-A", subscriptions: 2, monitoredItems: 10),
            MakeSummary("s-B", subscriptions: 1, monitoredItems: 4),
        });

        var snap = c.GetRouteSnapshot("r1")!;
        var sink = snap.Sinks.Single();

        sink.SinkInstanceId.Should().Be("opcua-1");
        sink.ActiveSessions.Should().NotBeNull();
        var sessions = sink.ActiveSessions!;
        sessions.Should().HaveCount(2);
        sessions[0].SessionId.Should().Be("s-A");
        sessions[0].SubscriptionCount.Should().Be(2);
        sessions[1].MonitoredItemCount.Should().Be(4);
    }

    [Fact]
    public void RecordActiveSessions_AndRecordSinkAdapterState_ShareOneSnapshot()
    {
        // Proves the single-state-store invariant — both push paths land
        // on the same SinkHealthSnapshot for the same (routeId, sinkId).
        var c = new RuntimeDiagnosticsCollector();

        c.RecordSinkAdapterState("r1", "opcua-1", AdapterState.Running, lastError: null);
        c.RecordActiveSessions("r1", "opcua-1", new[] { MakeSummary("s1") });

        var sink = c.GetRouteSnapshot("r1")!.Sinks.Single();

        sink.AdapterState.Should().Be(AdapterState.Running);
        sink.ActiveSessions.Should().NotBeNull();
        var sessions = sink.ActiveSessions!;
        sessions.Should().HaveCount(1);
    }

    [Fact]
    public void RecordActiveSessions_LatestWins_OverwritesPriorSnapshot()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordActiveSessions("r1", "opcua-1", new[] { MakeSummary("s1"), MakeSummary("s2") });
        c.RecordActiveSessions("r1", "opcua-1", new[] { MakeSummary("s3") });

        var sink = c.GetRouteSnapshot("r1")!.Sinks.Single();

        sink.ActiveSessions.Should().NotBeNull();
        var sessions = sink.ActiveSessions!;
        sessions.Should().HaveCount(1);
        sessions[0].SessionId.Should().Be("s3");
    }

    [Fact]
    public void RecordActiveSessions_EmptyList_IsValidObservation()
    {
        // Empty != null. An empty list signals "tracked, no clients connected"
        // and should surface as a non-null empty ActiveSessions on the
        // snapshot — distinguishable from "no observation yet" (null).
        var c = new RuntimeDiagnosticsCollector();

        c.RecordActiveSessions("r1", "opcua-1", Array.Empty<SinkSessionSummary>());

        var sink = c.GetRouteSnapshot("r1")!.Sinks.Single();
        sink.ActiveSessions.Should().NotBeNull();
        sink.ActiveSessions!.Should().BeEmpty();
    }

    [Fact]
    public void RecordActiveSessions_NullList_IsTreatedAsEmpty()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordActiveSessions("r1", "opcua-1", sessions: null!);

        var sink = c.GetRouteSnapshot("r1")!.Sinks.Single();
        sink.ActiveSessions.Should().NotBeNull();
        sink.ActiveSessions!.Should().BeEmpty();
    }

    [Fact]
    public void RecordActiveSessions_SinkWithoutSessionPush_HasNullActiveSessions()
    {
        // No session-tracking poll occurred — ActiveSessions stays null so
        // metric callbacks can distinguish "no observation" from "zero
        // clients connected."
        var c = new RuntimeDiagnosticsCollector();
        c.RecordSinkAdapterState("r1", "mqtt-1", AdapterState.Running, lastError: null);

        var sink = c.GetRouteSnapshot("r1")!.Sinks.Single();
        sink.ActiveSessions.Should().BeNull();
    }

    [Fact]
    public void RecordActiveSessions_WithEmptyRouteOrSinkId_NoOps()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordActiveSessions("", "opcua-1", new[] { MakeSummary("s1") });
        c.RecordActiveSessions("r1", "", new[] { MakeSummary("s1") });

        c.GetKnownRoutes().Should().BeEmpty();
    }

    [Fact]
    public void MultipleSinks_TrackedIndependently()
    {
        var c = new RuntimeDiagnosticsCollector();

        c.RecordActiveSessions("r1", "opcua-1", new[] { MakeSummary("s-1A") });
        c.RecordActiveSessions("r1", "opcua-2", new[] { MakeSummary("s-2A"), MakeSummary("s-2B") });

        var snap = c.GetRouteSnapshot("r1")!;
        snap.Sinks.Should().HaveCount(2);

        var first = snap.Sinks.Single(s => s.SinkInstanceId == "opcua-1");
        first.ActiveSessions.Should().NotBeNull();
        first.ActiveSessions!.Should().HaveCount(1);

        var second = snap.Sinks.Single(s => s.SinkInstanceId == "opcua-2");
        second.ActiveSessions.Should().NotBeNull();
        second.ActiveSessions!.Should().HaveCount(2);
    }
}
