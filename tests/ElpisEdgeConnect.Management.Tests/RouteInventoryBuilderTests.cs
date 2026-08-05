// ============================================================================
// Tests: RouteInventoryBuilder — pure builder that merges configured
//        routes with live diagnostics snapshots + configuration faults.
//        Mirrors SourceInventoryBuilderTests; the M.P2.1 phase 3 decision-
//        table for routes is:
//
//            1. Disabled                 — route.Enabled = false
//            2. Faulted                  — route-kind fault OR snap.State == Failed
//            3. Live state from snapshot — Running / Degraded / Stopped
//            4. Configured / Not running — enabled, no snapshot
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class RouteInventoryBuilderTests
{
    [Fact]
    public void Build_DisabledRoute_YieldsDisabled()
    {
        var config = MakeConfig(routes: new[] { MakeRoute("r-1", enabled: false) });

        var rows = RouteInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Should().ContainSingle();
        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void Build_EnabledRoute_NoSnapshot_NoFault_YieldsConfiguredNotRunning()
    {
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });

        var rows = RouteInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateConfiguredNotRunning);
    }

    [Fact]
    public void Build_EnabledRoute_LiveRunningSnapshot_YieldsRunning()
    {
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });
        var snap = MakeSnapshot(routeId: "r-1", state: RouteState.Running);

        var rows = RouteInventoryBuilder.Build(config, new[] { snap });

        rows[0].StateName.Should().Be("Running");
    }

    [Fact]
    public void Build_ConfigFault_NoSnapshot_YieldsFaulted_WithErrorDetail()
    {
        // The route was never built because its source was disabled or
        // missing — RouteDefinitionFactory.Build registers a fault.
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Route,
            InstanceId = "r-1",
            ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE",
            Message = "no such source registered",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = RouteInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateFaulted);
        rows[0].LastErrorCode.Should().Be("CONFIG.ROUTE_REFERENCES_MISSING_SOURCE");
        rows[0].LastErrorMessage.Should().Be("no such source registered");
        rows[0].LastErrorAtUtc.Should().Be(fault.ObservedAtUtc);
        rows[0].Source.Should().BeNull();
        rows[0].Sinks.Should().BeEmpty();
    }

    [Fact]
    public void Build_RuntimeFailedSnapshot_YieldsFaulted()
    {
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });
        var snap = MakeSnapshot(routeId: "r-1", state: RouteState.Failed);

        var rows = RouteInventoryBuilder.Build(config, new[] { snap });

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateFaulted);
    }

    [Fact]
    public void Build_DisabledRoute_BeatsFault()
    {
        // Locked precedence: Disabled > Faulted.
        var config = MakeConfig(routes: new[] { MakeRoute("r-1", enabled: false) });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Route,
            InstanceId = "r-1",
            ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE",
            Message = "stale fault",
            ObservedAtUtc = DateTime.UtcNow,
        };

        var rows = RouteInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateDisabled);
        rows[0].LastErrorCode.Should().BeNull();
    }

    [Fact]
    public void Build_PreservesConfigRouteOrder()
    {
        var config = MakeConfig(routes: new[]
        {
            MakeRoute("r-a"),
            MakeRoute("r-b"),
            MakeRoute("r-c"),
        });

        var rows = RouteInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Select(r => r.RouteId).Should().ContainInOrder("r-a", "r-b", "r-c");
    }

    [Fact]
    public void Build_StaleSnapshot_NotInConfig_IsIgnored()
    {
        // Config is inventory truth — diagnostics-only entries do not appear.
        var staleSnap = MakeSnapshot(routeId: "r-removed", state: RouteState.Running);

        var rows = RouteInventoryBuilder.Build(MakeConfig(), new[] { staleSnap });

        rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_ConfigFaultAndLiveSnapshot_OverlaysFaultDetail()
    {
        // Snapshot has live state info; config fault overlays the fault
        // detail on top so the operator sees the upstream root cause.
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });
        var snap = MakeSnapshot(routeId: "r-1", state: RouteState.Failed);
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Route,
            InstanceId = "r-1",
            ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SINK",
            Message = "missing destination",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = RouteInventoryBuilder.Build(config, new[] { snap }, new[] { fault });

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateFaulted);
        rows[0].LastErrorCode.Should().Be("CONFIG.ROUTE_REFERENCES_MISSING_SINK",
            "config faults overlay the snapshot's live state with the upstream root cause");
    }

    [Fact]
    public void Build_FaultsForOtherKinds_DoNotAffectRoutes()
    {
        // Source-kind and Sink-kind faults must not bleed into route state.
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });
        var unrelated = new[]
        {
            new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Source,
                InstanceId = "r-1",
                ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
                Message = "irrelevant",
                ObservedAtUtc = DateTime.UtcNow,
            },
        };

        var rows = RouteInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), unrelated);

        rows[0].StateName.Should().Be(RouteInventoryBuilder.StateConfiguredNotRunning,
            "source-kind faults must not match a route id");
    }

    [Fact]
    public void BuildOne_FindsConfiguredRoute()
    {
        var config = MakeConfig(routes: new[]
        {
            MakeRoute("r-1"),
            MakeRoute("r-2", enabled: false),
        });

        var row = RouteInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "r-2");

        row.Should().NotBeNull();
        row!.StateName.Should().Be(RouteInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void BuildOne_UnknownRoute_ReturnsNull()
    {
        var config = MakeConfig(routes: new[] { MakeRoute("r-1") });

        var row = RouteInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "r-not-there");

        row.Should().BeNull();
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        ((Action)(() => RouteInventoryBuilder.Build(null!, Array.Empty<RouteHealthSnapshot>())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => RouteInventoryBuilder.Build(MakeConfig(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static GatewayConfiguration MakeConfig(
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = Array.Empty<SourceInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static RouteConfig MakeRoute(string routeId, bool enabled = true) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = "plc-x",
        SinkInstanceIds = new[] { "opcua-demo" },
        Enabled = enabled,
    };

    private static RouteHealthSnapshot MakeSnapshot(
        string routeId,
        RouteState state) => new()
    {
        RouteId = routeId,
        ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        State = state,
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
        Sinks = Array.Empty<SinkHealthSnapshot>(),
    };
}
