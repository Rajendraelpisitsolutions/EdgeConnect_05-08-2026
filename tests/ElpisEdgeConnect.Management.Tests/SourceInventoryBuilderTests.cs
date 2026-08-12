// ============================================================================
// Tests: SourceInventoryBuilder — pure builder that merges configured
//        sources with live route diagnostics.
//
//        The pattern these tests pin is architectural, not cosmetic:
//
//            Configuration = inventory truth
//            Diagnostics   = runtime enrichment
//
//        Every wizard added after M.2b.1 (S7, FOCAS2, MTConnect, sink
//        wizards) relies on the same merge rules. The test matrix
//        below mirrors the M.2b.1.1 decision table exactly so a future
//        regression that hides a configured source from /sources fails
//        loudly here rather than at customer-pilot time.
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

public class SourceInventoryBuilderTests
{
    // ── Decision-table coverage ─────────────────────────────────────

    [Fact]
    public void Build_EnabledSource_NoRoute_NoSnapshot_YieldsConfigured()
    {
        // Row: Yes / No / No → "Configured"
        var config = MakeConfig(sources: new[] { MakeSource("plc-1", enabled: true) });

        var rows = SourceInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Should().HaveCount(1);
        rows[0].RouteId.Should().BeNull();
        rows[0].Source.SourceInstanceId.Should().Be("plc-1");
        rows[0].Source.ProtocolName.Should().Be("modbustcp");
        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateConfigured);
        rows[0].Source.PointsObserved.Should().Be(0);
    }

    [Fact]
    public void Build_EnabledSource_RouteExists_NoSnapshot_YieldsConfiguredNotRunning()
    {
        // Row: Yes / Yes / No → "Configured / Not running"
        // Real cause today: hot-reload gap means a newly-Apply'd route
        // has no supervisor running yet. The /sources page must NOT
        // hide this row or operators think the wizard silently failed.
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            routes: new[] { MakeRoute("r-1", "plc-1") });

        var rows = SourceInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Should().HaveCount(1);
        rows[0].RouteId.Should().Be("r-1");
        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateConfiguredNotRunning);
    }

    [Fact]
    public void Build_EnabledSource_RouteExists_SnapshotExists_YieldsLiveState()
    {
        // Row: Yes / Yes / Yes → snapshot's state name passes through
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snap = MakeSnapshot(
            routeId: "r-1",
            source: new SourceHealthSnapshot
            {
                SourceInstanceId = "plc-1",
                ProtocolName = "modbustcp",
                State = AdapterState.Running,
                PointsObserved = 4242,
                LastPointAtUtc = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
            });

        var rows = SourceInventoryBuilder.Build(config, new[] { snap });

        rows.Should().HaveCount(1);
        rows[0].RouteId.Should().Be("r-1");
        rows[0].Source.StateName.Should().Be("Running");
        rows[0].Source.PointsObserved.Should().Be(4242);
        rows[0].Source.LastPointAtUtc.Should().Be(snap.Source!.LastPointAtUtc);
    }

    [Fact]
    public void Build_DisabledSource_AlwaysYieldsDisabled_RegardlessOfRoutingOrSnapshot()
    {
        // Row: Yes + disabled / Any / No → "Disabled"
        // And critically: Disabled wins even if a route + snapshot
        // somehow exist — Enabled=false is the operator's strongest
        // intent signal.
        var snap = MakeSnapshot(
            routeId: "r-1",
            source: new SourceHealthSnapshot
            {
                SourceInstanceId = "plc-1",
                ProtocolName = "modbustcp",
                State = AdapterState.Running,
                PointsObserved = 0,
            });

        // Case A: disabled + no route + no snapshot
        var configA = MakeConfig(sources: new[] { MakeSource("plc-1", enabled: false) });
        var rowsA = SourceInventoryBuilder.Build(configA, Array.Empty<RouteHealthSnapshot>());
        rowsA[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateDisabled);

        // Case B: disabled + route + snapshot still present (rare race
        // during hot-reload — snapshot lingers after operator disabled
        // the source). Disabled MUST still win.
        var configB = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: false) },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snapWithSource = MakeSnapshot(
            routeId: "r-1",
            source: snap.Source);
        var rowsB = SourceInventoryBuilder.Build(configB, new[] { snapWithSource });
        rowsB[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void Build_PreservesConfigSourceOrder()
    {
        // Operators who just added a source via the wizard expect it
        // at the bottom of the list (append-only mental model). Order
        // is configuration order, not diagnostics order.
        var config = MakeConfig(sources: new[]
        {
            MakeSource("plc-a"),
            MakeSource("plc-b"),
            MakeSource("plc-c"),
        });

        var rows = SourceInventoryBuilder.Build(config, Array.Empty<RouteHealthSnapshot>());

        rows.Select(r => r.Source.SourceInstanceId)
            .Should().ContainInOrder("plc-a", "plc-b", "plc-c");
    }

    [Fact]
    public void Build_EmptyConfig_YieldsEmptyList()
    {
        var rows = SourceInventoryBuilder.Build(
            MakeConfig(),
            Array.Empty<RouteHealthSnapshot>());
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Build_StaleSnapshot_NotInConfig_IsIgnored()
    {
        // Hot-reload gap surface: a source was removed from config but
        // the supervisor's snapshot still references it. The new
        // contract says config is inventory truth → the stale entry
        // must not appear.
        var staleSnap = MakeSnapshot(
            routeId: "r-stale",
            source: new SourceHealthSnapshot
            {
                SourceInstanceId = "plc-removed",
                ProtocolName = "modbustcp",
                State = AdapterState.Running,
                PointsObserved = 0,
            });

        var rows = SourceInventoryBuilder.Build(MakeConfig(), new[] { staleSnap });

        rows.Should().BeEmpty(
            "config is the inventory truth — diagnostics-only entries do not appear");
    }

    [Fact]
    public void Build_SnapshotForMismatchedSource_DoesNotEnrich()
    {
        // Defensive: if a snapshot's Source.SourceInstanceId disagrees
        // with the route's SourceInstanceId in config (impossible in
        // healthy runtime, possible during a config-swap race), do not
        // claim the snapshot's live state for the wrong source.
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snap = MakeSnapshot(
            routeId: "r-1",
            source: new SourceHealthSnapshot
            {
                SourceInstanceId = "plc-OTHER",  // does not match
                ProtocolName = "modbustcp",
                State = AdapterState.Running,
                PointsObserved = 0,
            });

        var rows = SourceInventoryBuilder.Build(config, new[] { snap });

        rows[0].RouteId.Should().Be("r-1");
        rows[0].Source.StateName.Should().Be(
            SourceInventoryBuilder.StateConfiguredNotRunning,
            "the snapshot does not belong to this source");
    }

    [Fact]
    public void Build_RunningSourceCarriesLastError_WhenPresent()
    {
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snap = MakeSnapshot(
            routeId: "r-1",
            source: new SourceHealthSnapshot
            {
                SourceInstanceId = "plc-1",
                ProtocolName = "modbustcp",
                State = AdapterState.Degraded,
                PointsObserved = 0,
                LastError = new AdapterError
                {
                    Code = "MODBUS.SOCKET_ERROR",
                    Category = ErrorCategory.Network,
                    Message = "connection reset by peer",
                },
                LastErrorAtUtc = new DateTime(2026, 5, 14, 11, 59, 0, DateTimeKind.Utc),
            });

        var rows = SourceInventoryBuilder.Build(config, new[] { snap });

        rows[0].Source.StateName.Should().Be("Degraded");
        rows[0].Source.LastErrorCode.Should().Be("MODBUS.SOCKET_ERROR");
        rows[0].Source.LastErrorMessage.Should().Be("connection reset by peer");
        rows[0].Source.LastErrorAtUtc.Should().Be(snap.Source!.LastErrorAtUtc);
    }

    // ── BuildOne ────────────────────────────────────────────────────

    [Fact]
    public void BuildOne_FindsConfiguredSource()
    {
        var config = MakeConfig(sources: new[]
        {
            MakeSource("plc-1"),
            MakeSource("plc-2", enabled: false),
        });

        var row = SourceInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "plc-2");

        row.Should().NotBeNull();
        row!.Source.SourceInstanceId.Should().Be("plc-2");
        row.Source.StateName.Should().Be(SourceInventoryBuilder.StateDisabled);
    }

    [Fact]
    public void BuildOne_UnknownSource_ReturnsNull()
    {
        var config = MakeConfig(sources: new[] { MakeSource("plc-1") });

        var row = SourceInventoryBuilder.BuildOne(
            config, Array.Empty<RouteHealthSnapshot>(), "plc-not-there");

        row.Should().BeNull();
    }

    // ── Argument validation ─────────────────────────────────────────

    [Fact]
    public void Build_NullArguments_Throw()
    {
        ((Action)(() => SourceInventoryBuilder.Build(null!, Array.Empty<RouteHealthSnapshot>())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SourceInventoryBuilder.Build(MakeConfig(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildOne_NullArguments_Throw()
    {
        ((Action)(() => SourceInventoryBuilder.BuildOne(null!, Array.Empty<RouteHealthSnapshot>(), "x")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SourceInventoryBuilder.BuildOne(MakeConfig(), null!, "x")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => SourceInventoryBuilder.BuildOne(MakeConfig(), Array.Empty<RouteHealthSnapshot>(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── M.P2.1 Phase 3a: Faulted precedence (Disabled > Faulted > live > Configured) ──

    [Fact]
    public void Build_ConfigFault_ProducesFaultedState_WithErrorDetail()
    {
        // Cross-record fault from IConfigurationFaultRegistry — e.g.,
        // the enabled-source-no-route case M.P2.1 phase 2 now produces
        // instead of crashing.
        var config = MakeConfig(sources: new[] { MakeSource("plc-orphan") });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Source,
            InstanceId = "plc-orphan",
            ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
            Message = "test fault message",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = SourceInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows.Should().HaveCount(1);
        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateFaulted);
        rows[0].Source.LastErrorCode.Should().Be("CONFIG.SOURCE_WITHOUT_ROUTE");
        rows[0].Source.LastErrorMessage.Should().Be("test fault message");
        rows[0].Source.LastErrorAtUtc.Should().Be(fault.ObservedAtUtc);
    }

    [Fact]
    public void Build_RuntimeFailedSnapshot_ProducesFaultedState_WithErrorDetail()
    {
        // Live AdapterState.Failed from the snapshot — e.g., the
        // adapter init failed at runtime (unreachable IP, etc.). The
        // synthetic state name is "Faulted" (operator-facing) even
        // though Core's enum is "Failed" (internal label).
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snap = MakeSnapshot(routeId: "r-1", source: new SourceHealthSnapshot
        {
            SourceInstanceId = "plc-1",
            ProtocolName = "modbustcp",
            State = AdapterState.Failed,
            PointsObserved = 0,
            LastError = new AdapterError
            {
                Code = "MODBUS.SOCKET_ERROR",
                Category = ErrorCategory.Network,
                Message = "connection refused 127.0.0.1:502",
            },
            LastErrorAtUtc = new DateTime(2026, 5, 15, 14, 1, 0, DateTimeKind.Utc),
        });

        var rows = SourceInventoryBuilder.Build(config, new[] { snap });

        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateFaulted,
            "live AdapterState.Failed maps to the operator-facing 'Faulted' synthetic label");
        rows[0].Source.LastErrorCode.Should().Be("MODBUS.SOCKET_ERROR");
        rows[0].Source.LastErrorMessage.Should().Contain("connection refused");
    }

    [Fact]
    public void Build_DisabledSource_BeatsFault_EvenWithStaleFaultInRegistry()
    {
        // Locked precedence (ChatGPT review): Disabled > Faulted.
        // Operator intent is the strongest signal; a disabled source
        // shouldn't show as Faulted even if a stale fault entry exists
        // (e.g., the operator just disabled the source but the registry
        // hasn't been cleared yet).
        var config = MakeConfig(sources: new[] { MakeSource("plc-1", enabled: false) });
        var fault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Source,
            InstanceId = "plc-1",
            ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
            Message = "stale fault from before operator disabled the source",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = SourceInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), new[] { fault });

        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateDisabled,
            "Disabled wins over Faulted per the locked precedence");
        rows[0].Source.LastErrorCode.Should().BeNull(
            "disabled instances should not expose stale fault detail");
    }

    [Fact]
    public void Build_ConfigFaultAndRuntimeFault_ConfigFaultWins()
    {
        // If both a config fault AND a runtime adapter failure exist for
        // the same source, the config fault's ErrorCode is surfaced
        // because registration-time problems are usually the upstream
        // root cause.
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            routes: new[] { MakeRoute("r-1", "plc-1") });
        var snap = MakeSnapshot(routeId: "r-1", source: new SourceHealthSnapshot
        {
            SourceInstanceId = "plc-1",
            ProtocolName = "modbustcp",
            State = AdapterState.Failed,
            PointsObserved = 0,
            LastError = new AdapterError
            {
                Code = "MODBUS.SOCKET_ERROR",
                Category = ErrorCategory.Network,
                Message = "runtime",
            },
        });
        var configFault = new ConfigurationFault
        {
            Kind = ConfigurationFaultKind.Source,
            InstanceId = "plc-1",
            ErrorCode = "CONFIG.SOURCE_WITHOUT_ROUTE",
            Message = "config fault",
            ObservedAtUtc = new DateTime(2026, 5, 15, 14, 0, 0, DateTimeKind.Utc),
        };

        var rows = SourceInventoryBuilder.Build(config, new[] { snap }, new[] { configFault });

        rows[0].Source.StateName.Should().Be(SourceInventoryBuilder.StateFaulted);
        rows[0].Source.LastErrorCode.Should().Be("CONFIG.SOURCE_WITHOUT_ROUTE",
            "config faults are upstream root cause; they win over runtime errors");
    }

    [Fact]
    public void Build_FaultsForOtherKinds_DoNotAffectSources()
    {
        // Sink-kind and Route-kind faults must not bleed into source
        // state. The builder filters faults by kind before applying.
        var config = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            routes: new[] { MakeRoute("r-1", "plc-1") });

        var unrelated = new[]
        {
            new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Sink,
                InstanceId = "plc-1",  // SAME id as the source, different kind
                ErrorCode = "CONFIG.SINK_WITHOUT_ROUTE",
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

        var rows = SourceInventoryBuilder.Build(
            config, Array.Empty<RouteHealthSnapshot>(), unrelated);

        rows[0].Source.StateName.Should().Be(
            SourceInventoryBuilder.StateConfiguredNotRunning,
            "no Source-kind faults match this source; sink/route faults must not bleed in");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static GatewayConfiguration MakeConfig(
        IReadOnlyList<SourceInstanceConfig>? sources = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = sources ?? Array.Empty<SourceInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static SourceInstanceConfig MakeSource(string id, bool enabled = true) => new()
    {
        InstanceId = id,
        ProtocolName = "modbustcp",
        DeviceId = id,
        DeviceName = id,
        DeviceClass = "plc",
        Enabled = enabled,
    };

    private static RouteConfig MakeRoute(string routeId, string sourceId) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { "opcua-demo" },
    };

    private static RouteHealthSnapshot MakeSnapshot(
        string routeId = "r-1",
        SourceHealthSnapshot? source = null) => new()
    {
        RouteId = routeId,
        ObservedAtUtc = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
        State = RouteState.Running,
        StateTransitionCount = 0,
        BackpressureDropCount = 0,
        Source = source,
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
