// ============================================================================
// Tests: SinkInventoryBuilder — pure builder that merges configured sinks
//        with live diagnostics snapshots + configuration faults.
//
//        Mirrors SourceInventoryBuilderTests / RouteInventoryBuilderTests
//        decision-table coverage, with the additional dimension specific
//        to destinations:
//
//           * Sinks can be referenced by MANY routes (true 1-to-many).
//             SinkListItemDto.RouteIds is a list, possibly empty.
//           * When a sink is referenced by N routes, N RouteHealthSnapshot
//             entries can each carry a SinkHealthSnapshot for the same
//             sink instance. The builder picks a representative one,
//             preferring Failed when any snapshot reports it.
//
//        Decision-table precedence locked at M.P2.1 phase 3 review:
//           1. Disabled
//           2. Faulted   (config fault OR live AdapterState.Failed)
//           3. Live state from snapshot
//           4. Configured / Not running   (routes exist, no snapshot)
//           5. Configured                 (no routes reference it)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class SinkInventoryBuilderTests
{
    // ── Decision-table coverage ─────────────────────────────────────

    [Fact]
    public void Build_EnabledSink_NoRoute_NoSnapshot_YieldsConfigured()
    {
        // Row: enabled + zero routes reference it + no snapshot → "Configured"
        var config = MakeConfig(sinks: new[] { MakeSink("mqtt-1", enabled: true) });

        var rows = SinkInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Should().ContainSingle();
        rows[0].RouteIds.Should().BeEmpty();
        rows[0].Sink.SinkInstanceId.Should().Be("mqtt-1");
        rows[0].SinkKind.Should().Be("mqtt");
        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateConfigured);
    }

    [Fact]
    public void Build_EnabledSink_RouteExists_NoSnapshot_YieldsConfiguredNotRunning()
    {
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });

        var rows = SinkInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows[0].RouteIds.Should().ContainSingle().Which.Should().Be("r-1");
        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateConfiguredNotRunning);
    }

    [Fact]
    public void Build_EnabledSink_RouteExists_SnapshotExists_YieldsLiveState()
    {
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });
        var snap = MakeSnapshot("r-1", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "r-1", AdapterState.Running),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { snap });

        rows[0].Sink.AdapterStateName.Should().Be("Running");
    }

    [Fact]
    public void Build_DisabledSink_AlwaysYieldsDisabled_RegardlessOfRoutingOrSnapshot()
    {
        // Disabled is the operator's strongest intent signal — wins
        // even if a live snapshot still reports Running.
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1", enabled: false) },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });
        var snap = MakeSnapshot("r-1", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "r-1", AdapterState.Running),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { snap });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void Build_PreservesConfigSinkOrder()
    {
        var config = MakeConfig(sinks: new[]
        {
            MakeSink("mqtt-a"),
            MakeSink("mqtt-b"),
            MakeSink("opcua-c", protocolName: "opcua-server"),
        });

        var rows = SinkInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Select(r => r.Sink.SinkInstanceId)
            .Should().ContainInOrder("mqtt-a", "mqtt-b", "opcua-c");
    }

    [Fact]
    public void Build_StaleSnapshot_NotInConfig_IsIgnored()
    {
        // Sink was removed from gateway.json but the supervisor's
        // snapshot still has it. Config is inventory truth → no row.
        var staleSnap = MakeSnapshot("r-removed", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-removed", "r-removed", AdapterState.Running),
        });

        var rows = SinkInventoryBuilder.Build(MakeConfig(), new[] { staleSnap });

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_StaleStoppedSnapshotFromRemovedRoute_DoesNotShadowLiveRunning()
    {
        // Repro of the Destinations "Stopped (red)" bug: a sink is cycled
        // across routes (route-old removed, route-live current). The removed
        // route's snapshot lingers in GetAllRouteSnapshots reporting the sink
        // as Stopped. Config is inventory truth (ADR-0002): only the live
        // route's Running snapshot may drive the sink's state — the stale
        // Stopped record must not paint a healthy destination red.
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("route-live", sinkIds: new[] { "mqtt-1" }) });

        // Stale snapshot enumerated FIRST — the pre-fix builder picked it as
        // the representative 'live' state and rendered Stopped.
        var staleStopped = MakeSnapshot("route-old", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "route-old", AdapterState.Stopped),
        });
        var liveRunning = MakeSnapshot("route-live", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "route-live", AdapterState.Running),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { staleStopped, liveRunning });

        rows.Should().ContainSingle();
        rows[0].Sink.AdapterStateName.Should().Be("Running",
            "the live route's snapshot must drive the sink state; a stale " +
            "snapshot from a removed route must be ignored (ADR-0002)");
    }

    // ── 1-to-many: a sink can serve multiple routes ─────────────────

    [Fact]
    public void Build_SinkReferencedByMultipleRoutes_YieldsOneRowWithAllRouteIds()
    {
        // Locked: SinkListItemDto.RouteIds is plural; a sink wired into
        // many routes appears once with chips for each route.
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-shared") },
            routes: new[]
            {
                MakeRoute("r-a", sinkIds: new[] { "mqtt-shared" }),
                MakeRoute("r-b", sinkIds: new[] { "mqtt-shared" }),
                MakeRoute("r-c", sinkIds: new[] { "mqtt-shared" }),
            });

        var rows = SinkInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Should().ContainSingle();
        rows[0].RouteIds.Should().ContainInOrder("r-a", "r-b", "r-c");
    }

    [Fact]
    public void Build_SinkReferencedByMultipleRoutes_AnyFailedSnapshotWins()
    {
        // Defensive: if N routes each report a snapshot for the same
        // sink and one of them is Failed, the operator must see the
        // unhealthy state — a healthier sibling snapshot must not mask
        // it. In practice the underlying adapter is a singleton so all
        // snapshots should agree; this test pins the safety guarantee.
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-shared") },
            routes: new[]
            {
                MakeRoute("r-a", sinkIds: new[] { "mqtt-shared" }),
                MakeRoute("r-b", sinkIds: new[] { "mqtt-shared" }),
            });
        var snapA = MakeSnapshot("r-a", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-shared", "r-a", AdapterState.Running),
        });
        var snapB = MakeSnapshot("r-b", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-shared", "r-b", AdapterState.Failed,
                lastError: new AdapterError
                {
                    Code = "MQTT.BROKER_UNREACHABLE",
                    Category = ErrorCategory.Network,
                    Message = "connection refused",
                }),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { snapA, snapB });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateFaulted,
            "the Failed snapshot must surface even if a sibling Running snapshot exists");
        rows[0].Sink.LastErrorCode.Should().Be("MQTT.BROKER_UNREACHABLE");
    }

    // ── Faulted precedence ──────────────────────────────────────────

    [Fact]
    public void Build_ConfigFault_ProducesFaultedState_WithErrorDetail()
    {
        // Cross-record sink-kind fault — e.g., a sink referenced by a
        // route that doesn't itself exist in config.Sinks. Pre-M.P2.1
        // this crashed startup; now it gets a Faulted row.
        var config = MakeConfig(sinks: new[] { MakeSink("mqtt-orphan") });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Sink,
            InstanceId = "mqtt-orphan",
            ErrorCode = "CONFIG.SINK_MISCONFIGURED",
            Message = "sink-side test fault",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = SinkInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateFaulted);
        rows[0].Sink.LastErrorCode.Should().Be("CONFIG.SINK_MISCONFIGURED");
        rows[0].Sink.LastErrorMessage.Should().Be("sink-side test fault");
        rows[0].Sink.LastErrorAtUtc.Should().Be(fault.ObservedAtUtc);
    }

    [Fact]
    public void Build_RuntimeFailedSnapshot_ProducesFaultedState()
    {
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });
        var snap = MakeSnapshot("r-1", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "r-1", AdapterState.Failed,
                lastError: new AdapterError
                {
                    Code = "MQTT.BROKER_UNREACHABLE",
                    Category = ErrorCategory.Network,
                    Message = "connection refused",
                }),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { snap });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateFaulted);
        rows[0].Sink.LastErrorCode.Should().Be("MQTT.BROKER_UNREACHABLE");
    }

    [Fact]
    public void Build_DisabledSink_BeatsFault()
    {
        // Locked precedence: Disabled > Faulted. Stale fault entries
        // must not override the operator's explicit "off" intent.
        var config = MakeConfig(sinks: new[] { MakeSink("mqtt-1", enabled: false) });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Sink,
            InstanceId = "mqtt-1",
            ErrorCode = "CONFIG.SINK_MISCONFIGURED",
            Message = "stale fault",
            ObservedAtUtc = DateTime.UtcNow,
        };

        var rows = SinkInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateDisabled);
        rows[0].Sink.LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void Build_ConfigFaultAndRuntimeFault_ConfigFaultWins()
    {
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });
        var snap = MakeSnapshot("r-1", sinks: new[]
        {
            MakeSinkSnapshot("mqtt-1", "r-1", AdapterState.Failed,
                lastError: new AdapterError
                {
                    Code = "MQTT.BROKER_UNREACHABLE",
                    Category = ErrorCategory.Network,
                    Message = "runtime",
                }),
        });
        var configFault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Sink,
            InstanceId = "mqtt-1",
            ErrorCode = "CONFIG.SINK_MISCONFIGURED",
            Message = "config fault",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = SinkInventoryBuilder.Build(config, new[] { snap }, new[] { configFault });

        rows[0].Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateFaulted);
        rows[0].Sink.LastErrorCode.Should().Be("CONFIG.SINK_MISCONFIGURED",
            "config faults are upstream root cause; they win over runtime errors");
    }

    [Fact]
    public void Build_FaultsForOtherKinds_DoNotAffectSinks()
    {
        // Source-kind and Route-kind faults must not match against a
        // sink instance even if the InstanceId string collides.
        var config = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "mqtt-1" }) });
        var unrelated = new[]
        {
            new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = "mqtt-1",
                ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
                Message = "irrelevant",
                ObservedAtUtc = DateTime.UtcNow,
            },
            new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Route,
                InstanceId = "r-1",
                ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE",
                Message = "irrelevant",
                ObservedAtUtc = DateTime.UtcNow,
            },
        };

        var rows = SinkInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), unrelated);

        rows[0].Sink.AdapterStateName.Should().Be(
            SinkInventoryBuilder.StateConfiguredNotRunning,
            "non-sink-kind faults must not bleed into sink state");
    }

    // ── Session passthrough ─────────────────────────────────────────

    [Fact]
    public void Build_SessionTrackingSink_PassesSessionsThrough()
    {
        var config = MakeConfig(
            sinks: new[] { MakeSink("opcua-1", protocolName: "opcua-server") },
            routes: new[] { MakeRoute("r-1", sinkIds: new[] { "opcua-1" }) });
        var sessions = new List<SinkSessionSummary>
        {
            new SinkSessionSummary
            {
                SessionId = "s-1",
                ConnectedAtUtc = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc),
                LastActivityUtc = new DateTime(2026, 5, 15, 13, 30, 0, DateTimeKind.Utc),
                SubscriptionCount = 2,
                MonitoredItemCount = 17,
            },
        };
        var snap = MakeSnapshot("r-1", sinks: new[]
        {
            MakeSinkSnapshot("opcua-1", "r-1", AdapterState.Running, sessions: sessions),
        });

        var rows = SinkInventoryBuilder.Build(config, new[] { snap });

        rows[0].Sessions.Should().NotBeNull();
        rows[0].Sessions!.Should().HaveCount(1);
        rows[0].Sessions![0].SessionId.Should().Be("s-1");
    }

    // ── BuildOne ────────────────────────────────────────────────────

    [Fact]
    public void BuildOne_FindsConfiguredSink()
    {
        var config = MakeConfig(sinks: new[]
        {
            MakeSink("mqtt-1"),
            MakeSink("mqtt-2", enabled: false),
        });

        var row = SinkInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "mqtt-2");

        row.Should().NotBeNull();
        row!.Sink.SinkInstanceId.Should().Be("mqtt-2");
        row.Sink.AdapterStateName.Should().Be(SinkInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void BuildOne_UnknownSink_ReturnsNull()
    {
        var config = MakeConfig(sinks: new[] { MakeSink("mqtt-1") });

        var row = SinkInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "mqtt-not-there");

        row.Should().BeNull();
    }

    // ── Argument validation ─────────────────────────────────────────

    [Fact]
    public void Build_NullArguments_Throw()
    {
        ((Action)(() => SinkInventoryBuilder.Build(null!, Array.Empty<RouteHealthSnapshot>())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SinkInventoryBuilder.Build(MakeConfig(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildOne_NullArguments_Throw()
    {
        ((Action)(() => SinkInventoryBuilder.BuildOne(null!, Array.Empty<RouteHealthSnapshot>(), "x")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SinkInventoryBuilder.BuildOne(MakeConfig(), null!, "x")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SinkInventoryBuilder.BuildOne(MakeConfig(), Array.Empty<RouteHealthSnapshot>(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static GatewayConfiguration MakeConfig(
        IReadOnlyList<SinkInstanceConfig>? sinks = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sinks = sinks ?? Array.Empty<SinkInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static SinkInstanceConfig MakeSink(
        string id,
        bool enabled = true,
        string protocolName = "mqtt") => new()
    {
        InstanceId = id,
        ProtocolName = protocolName,
        Enabled = enabled,
    };

    private static RouteConfig MakeRoute(
        string routeId,
        IReadOnlyList<string>? sinkIds = null) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = "src-x",
        SinkInstanceIds = sinkIds ?? Array.Empty<string>(),
    };

    private static SinkHealthSnapshot MakeSinkSnapshot(
        string sinkInstanceId,
        string routeId,
        AdapterState state,
        AdapterError? lastError = null,
        IReadOnlyList<SinkSessionSummary>? sessions = null) => new()
    {
        SinkInstanceId = sinkInstanceId,
        RouteId = routeId,
        IsDegraded = false,
        IsDraining = false,
        DegradationEventCount = 0,
        RecoveryEventCount = 0,
        AdapterState = state,
        LastError = lastError,
        LastErrorAtUtc = lastError is null ? null : new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Utc),
        ActiveSessions = sessions,
    };

    private static RouteHealthSnapshot MakeSnapshot(
        string routeId,
        IReadOnlyList<SinkHealthSnapshot>? sinks = null) => new()
    {
        RouteId = routeId,
        ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        State = RouteState.Running,
        StateTransitionCount = 0,
        BackpressureDropCount = 0,
        Source = null,
        Pipeline = new PipelineHealthSnapshot
        {
            RouteId = routeId,
            BatchesProcessed = 0,
            PointsIn = 0,
            PointsOut = 0,
            Steps = Array.Empty<TransformStepStats>(),
        },
        Sinks = sinks ?? Array.Empty<SinkHealthSnapshot>(),
    };
}
