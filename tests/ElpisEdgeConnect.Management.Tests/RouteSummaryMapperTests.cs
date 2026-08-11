// ============================================================================
// Tests: RouteSummaryMapper — pure mapper from Core's
//        RouteHealthSnapshot into the wire-shape RouteSummaryDto.
//        The DTO IS the public API contract, so its shape is
//        version-locked; these tests pin the mapper.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class RouteSummaryMapperTests
{
    [Fact]
    public void ToSummary_MapsTopLevelFields()
    {
        var snap = new RouteHealthSnapshot
        {
            RouteId = "r1",
            ObservedAtUtc = new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc),
            State = RouteState.Running,
            StateTransitionCount = 7,
            BackpressureDropCount = 2,
            Pipeline = new PipelineHealthSnapshot
            {
                RouteId = "r1",
                BatchesProcessed = 0,
                PointsIn = 0,
                PointsOut = 0,
                Steps = System.Array.Empty<TransformStepStats>(),
            },
            Sinks = System.Array.Empty<SinkHealthSnapshot>(),
        };

        var dto = RouteSummaryMapper.ToSummary(snap);

        dto.RouteId.Should().Be("r1");
        dto.ObservedAtUtc.Should().Be(snap.ObservedAtUtc);
        dto.State.Should().Be((int)RouteState.Running);
        dto.StateName.Should().Be("Running");
        dto.StateTransitionCount.Should().Be(7);
        dto.BackpressureDropCount.Should().Be(2);
        dto.Source.Should().BeNull("snapshot has no Source");
        dto.Sinks.Should().BeEmpty();
        dto.Buffer.Should().BeNull("snapshot has no Buffer");
    }

    [Fact]
    public void ToSummary_MapsSourceFields()
    {
        var snap = MakeSnapshot(source: new SourceHealthSnapshot
        {
            SourceInstanceId = "modbus-1",
            ProtocolName = "modbustcp",
            State = AdapterState.Running,
            PointsObserved = 1234,
            LastPointAtUtc = new DateTime(2026, 5, 14, 9, 59, 0, DateTimeKind.Utc),
            LastError = new AdapterError
            {
                Code = "MODBUS.SOCKET_ERROR",
                Category = ErrorCategory.Network,
                Message = "connection reset",
            },
            LastErrorAtUtc = new DateTime(2026, 5, 14, 9, 55, 0, DateTimeKind.Utc),
        });

        var dto = RouteSummaryMapper.ToSummary(snap);

        dto.Source.Should().NotBeNull();
        dto.Source!.SourceInstanceId.Should().Be("modbus-1");
        dto.Source.ProtocolName.Should().Be("modbustcp");
        dto.Source.StateName.Should().Be("Running");
        dto.Source.PointsObserved.Should().Be(1234);
        dto.Source.LastPointAtUtc.Should().Be(snap.Source!.LastPointAtUtc);
        dto.Source.LastErrorCode.Should().Be("MODBUS.SOCKET_ERROR");
        dto.Source.LastErrorMessage.Should().Be("connection reset");
        dto.Source.LastErrorAtUtc.Should().Be(snap.Source.LastErrorAtUtc);
    }

    [Fact]
    public void ToSummary_MapsSinksIncludingActiveSessions()
    {
        var sessions = new[]
        {
            new SinkSessionSummary { SessionId = "s-A", ConnectedAtUtc = DateTime.UtcNow },
            new SinkSessionSummary { SessionId = "s-B", ConnectedAtUtc = DateTime.UtcNow },
        };
        var snap = MakeSnapshot(sinks: new[]
        {
            new SinkHealthSnapshot
            {
                SinkInstanceId = "opcua-1",
                RouteId = "r1",
                IsDegraded = false,
                IsDraining = false,
                DegradationEventCount = 1,
                RecoveryEventCount = 1,
                AdapterState = AdapterState.Running,
                ActiveSessions = sessions,
            },
            new SinkHealthSnapshot
            {
                SinkInstanceId = "mqtt-1",
                RouteId = "r1",
                IsDegraded = true,
                IsDraining = true,
                DegradationEventCount = 5,
                RecoveryEventCount = 4,
                AdapterState = AdapterState.Degraded,
                LastError = new AdapterError
                {
                    Code = "MQTT.NOT_CONNECTED",
                    Category = ErrorCategory.Network,
                    Message = "broker down",
                },
            },
        });

        var dto = RouteSummaryMapper.ToSummary(snap);

        dto.Sinks.Should().HaveCount(2);

        var opc = dto.Sinks[0];
        opc.SinkInstanceId.Should().Be("opcua-1");
        opc.IsDegraded.Should().BeFalse();
        opc.AdapterStateName.Should().Be("Running");
        opc.ActiveSessionCount.Should().Be(2,
            "the OPC UA Server sink surfaces its live session count");

        var mqtt = dto.Sinks[1];
        mqtt.SinkInstanceId.Should().Be("mqtt-1");
        mqtt.IsDegraded.Should().BeTrue();
        mqtt.IsDraining.Should().BeTrue();
        mqtt.DegradationEventCount.Should().Be(5);
        mqtt.AdapterStateName.Should().Be("Degraded");
        mqtt.ActiveSessionCount.Should().BeNull("MQTT sink has no session-tracking surface");
        mqtt.LastErrorCode.Should().Be("MQTT.NOT_CONNECTED");
        mqtt.LastErrorMessage.Should().Be("broker down");
    }

    [Fact]
    public void ToSummary_MapsBuffer()
    {
        var snap = MakeSnapshot(buffer: new BufferHealthSnapshot
        {
            RouteId = "r1",
            Mode = "StoreAndForward",
            CurrentDepth = 42,
            TotalEnqueued = 1000,
            TotalDrained = 950,
            TotalDropped = 8,
            DroppedByCapacity = 5,
            DroppedByRetention = 3,
            SizeBytes = 4096,
            ObservedAtUtc = DateTime.UtcNow,
        });

        var dto = RouteSummaryMapper.ToSummary(snap);
        dto.Buffer.Should().NotBeNull();
        dto.Buffer!.Mode.Should().Be("StoreAndForward");
        dto.Buffer.CurrentDepth.Should().Be(42);
        dto.Buffer.TotalEnqueued.Should().Be(1000);
        dto.Buffer.TotalDrained.Should().Be(950);
        dto.Buffer.TotalDropped.Should().Be(8);
        dto.Buffer.SizeBytes.Should().Be(4096);
    }

    [Fact]
    public void ToSummaries_MapsEveryRoute()
    {
        var snaps = new[]
        {
            MakeSnapshot(routeId: "r1"),
            MakeSnapshot(routeId: "r2"),
        };
        var dtos = RouteSummaryMapper.ToSummaries(snaps);
        dtos.Should().HaveCount(2);
        dtos[0].RouteId.Should().Be("r1");
        dtos[1].RouteId.Should().Be("r2");
    }

    [Fact]
    public void MapSinkSummary_ProjectsAllScalarFields()
    {
        var snap = new SinkHealthSnapshot
        {
            SinkInstanceId = "opcua-1",
            RouteId = "r1",
            IsDegraded = true,
            IsDraining = false,
            DegradationEventCount = 3,
            RecoveryEventCount = 2,
            AdapterState = AdapterState.Degraded,
            LastError = new AdapterError
            {
                Code = "OPCUA.SESSION_TIMEOUT",
                Category = ErrorCategory.Network,
                Message = "client kicked",
            },
            LastErrorAtUtc = new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc),
        };

        var dto = RouteSummaryMapper.MapSinkSummary(snap);

        dto.SinkInstanceId.Should().Be("opcua-1");
        dto.IsDegraded.Should().BeTrue();
        dto.IsDraining.Should().BeFalse();
        dto.DegradationEventCount.Should().Be(3);
        dto.RecoveryEventCount.Should().Be(2);
        dto.AdapterStateName.Should().Be("Degraded");
        dto.ActiveSessionCount.Should().BeNull("ActiveSessions is null on this snapshot");
        dto.LastErrorCode.Should().Be("OPCUA.SESSION_TIMEOUT");
        dto.LastErrorMessage.Should().Be("client kicked");
        dto.LastErrorAtUtc.Should().Be(snap.LastErrorAtUtc);
    }

    [Fact]
    public void MapSessions_ReturnsNull_WhenSinkDoesNotTrackSessions()
    {
        var snap = new SinkHealthSnapshot
        {
            SinkInstanceId = "mqtt-1",
            RouteId = "r1",
            IsDegraded = false,
            IsDraining = false,
            DegradationEventCount = 0,
            RecoveryEventCount = 0,
            AdapterState = AdapterState.Running,
            ActiveSessions = null,
        };

        RouteSummaryMapper.MapSessions(snap).Should().BeNull(
            "MQTT sinks don't expose ISessionTrackingSink so the API surface must distinguish " +
            "'no panel at all' from 'panel showing zero sessions'");
    }

    [Fact]
    public void MapSessions_ReturnsEmpty_WhenSinkTracksSessionsButNoneConnected()
    {
        var snap = new SinkHealthSnapshot
        {
            SinkInstanceId = "opcua-1",
            RouteId = "r1",
            IsDegraded = false,
            IsDraining = false,
            DegradationEventCount = 0,
            RecoveryEventCount = 0,
            AdapterState = AdapterState.Running,
            ActiveSessions = System.Array.Empty<SinkSessionSummary>(),
        };

        var sessions = RouteSummaryMapper.MapSessions(snap);
        sessions.Should().NotBeNull();
        sessions!.Should().BeEmpty(
            "the OPC UA endpoint is listening but no SCADA/HMI client has subscribed yet");
    }

    [Fact]
    public void MapSessions_ProjectsAllSessionFields()
    {
        var connectedAt = new DateTime(2026, 5, 14, 8, 0, 0, DateTimeKind.Utc);
        var lastActivity = new DateTime(2026, 5, 14, 8, 5, 30, DateTimeKind.Utc);
        var snap = new SinkHealthSnapshot
        {
            SinkInstanceId = "opcua-1",
            RouteId = "r1",
            IsDegraded = false,
            IsDraining = false,
            DegradationEventCount = 0,
            RecoveryEventCount = 0,
            AdapterState = AdapterState.Running,
            ActiveSessions = new[]
            {
                new SinkSessionSummary
                {
                    SessionId = "i=12",
                    SessionName = "SCADA-LINE-7",
                    ClientApplicationUri = "urn:LinearProbe:HMI",
                    ClientIpAddress = "10.1.2.34",
                    ConnectedAtUtc = connectedAt,
                    LastActivityUtc = lastActivity,
                    UserTokenType = "Certificate",
                    SubscriptionCount = 2,
                    MonitoredItemCount = 47,
                },
            },
        };

        var sessions = RouteSummaryMapper.MapSessions(snap);

        sessions.Should().NotBeNull().And.HaveCount(1);
        var s = sessions![0];
        s.SessionId.Should().Be("i=12");
        s.SessionName.Should().Be("SCADA-LINE-7");
        s.ClientApplicationUri.Should().Be("urn:LinearProbe:HMI");
        s.ClientIpAddress.Should().Be("10.1.2.34");
        s.ConnectedAtUtc.Should().Be(connectedAt);
        s.LastActivityUtc.Should().Be(lastActivity);
        s.UserTokenType.Should().Be("Certificate");
        s.SubscriptionCount.Should().Be(2);
        s.MonitoredItemCount.Should().Be(47);
    }

    [Fact]
    public void MapPipeline_ReturnsNull_WhenInputIsNull()
    {
        RouteSummaryMapper.MapPipeline(null).Should().BeNull();
    }

    [Fact]
    public void MapPipeline_ProjectsTopLevelCounters()
    {
        var pipe = new PipelineHealthSnapshot
        {
            RouteId = "r1",
            BatchesProcessed = 142,
            PointsIn = 89_352,
            PointsOut = 89_352,
            Steps = System.Array.Empty<TransformStepStats>(),
        };

        var dto = RouteSummaryMapper.MapPipeline(pipe);

        dto.Should().NotBeNull();
        dto!.BatchesProcessed.Should().Be(142);
        dto.PointsIn.Should().Be(89_352);
        dto.PointsOut.Should().Be(89_352);
        dto.Steps.Should().BeEmpty();
    }

    [Fact]
    public void MapPipeline_DerivesAverageDurationFromTotalAndInvocations()
    {
        var pipe = new PipelineHealthSnapshot
        {
            RouteId = "r1",
            BatchesProcessed = 0,
            PointsIn = 0,
            PointsOut = 0,
            Steps = new[]
            {
                // 50 invocations × 2ms = 100ms → avg 2.0
                new TransformStepStats
                {
                    StepKind = "rate-limit",
                    Invocations = 50,
                    PointsIn = 100,
                    PointsOut = 80,
                    PointsSuppressed = 20,
                    TotalDuration = TimeSpan.FromMilliseconds(100),
                },
                // Never invoked → AverageDurationMs should be null,
                // NOT a divide-by-zero or zero.
                new TransformStepStats
                {
                    StepKind = "downsample",
                    Invocations = 0,
                    PointsIn = 0,
                    PointsOut = 0,
                    PointsSuppressed = 0,
                    TotalDuration = TimeSpan.Zero,
                },
            },
        };

        var dto = RouteSummaryMapper.MapPipeline(pipe);

        dto.Should().NotBeNull();
        dto!.Steps.Should().HaveCount(2);

        var rateLimit = dto.Steps[0];
        rateLimit.Kind.Should().Be("rate-limit");
        rateLimit.Invocations.Should().Be(50);
        rateLimit.PointsIn.Should().Be(100);
        rateLimit.PointsOut.Should().Be(80);
        rateLimit.PointsSuppressed.Should().Be(20);
        rateLimit.AverageDurationMs.Should().BeApproximately(2.0, precision: 0.0001);

        var downsample = dto.Steps[1];
        downsample.Invocations.Should().Be(0);
        downsample.AverageDurationMs.Should().BeNull(
            "a never-invoked step has no meaningful average; null beats 0 for the UI");
    }

    [Fact]
    public void ToSummary_NowIncludesPipelineMapping()
    {
        var snap = MakeSnapshot(routeId: "r1");
        // MakeSnapshot already attaches a non-null Pipeline (required field).
        // Confirm the dto carries it through after the M.1c.1 mapper extension.
        var dto = RouteSummaryMapper.ToSummary(snap);
        dto.Pipeline.Should().NotBeNull("M.1c.1 surfaces pipeline rollup on RouteSummaryDto");
    }

    [Fact]
    public void MapSessions_PreservesOrder()
    {
        var snap = new SinkHealthSnapshot
        {
            SinkInstanceId = "opcua-1",
            RouteId = "r1",
            IsDegraded = false,
            IsDraining = false,
            DegradationEventCount = 0,
            RecoveryEventCount = 0,
            AdapterState = AdapterState.Running,
            ActiveSessions = new[]
            {
                new SinkSessionSummary { SessionId = "i=10", ConnectedAtUtc = DateTime.UtcNow },
                new SinkSessionSummary { SessionId = "i=20", ConnectedAtUtc = DateTime.UtcNow },
                new SinkSessionSummary { SessionId = "i=30", ConnectedAtUtc = DateTime.UtcNow },
            },
        };

        var sessions = RouteSummaryMapper.MapSessions(snap);
        sessions.Should().NotBeNull();
        sessions![0].SessionId.Should().Be("i=10");
        sessions[1].SessionId.Should().Be("i=20");
        sessions[2].SessionId.Should().Be("i=30");
    }

    private static RouteHealthSnapshot MakeSnapshot(
        string routeId = "r1",
        SourceHealthSnapshot? source = null,
        IReadOnlyList<SinkHealthSnapshot>? sinks = null,
        BufferHealthSnapshot? buffer = null) =>
        new()
        {
            RouteId = routeId,
            ObservedAtUtc = DateTime.UtcNow,
            State = RouteState.Running,
            StateTransitionCount = 0,
            BackpressureDropCount = 0,
            Source = source,
            Sinks = sinks ?? System.Array.Empty<SinkHealthSnapshot>(),
            Buffer = buffer,
            Pipeline = new PipelineHealthSnapshot
            {
                RouteId = routeId,
                BatchesProcessed = 0,
                PointsIn = 0,
                PointsOut = 0,
                Steps = System.Array.Empty<TransformStepStats>(),
            },
        };
}
