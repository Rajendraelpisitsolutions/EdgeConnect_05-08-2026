// ============================================================================
// File: Configuration/RuntimeReloadClassifierTests.cs
// Purpose: Pin the M.P2.2 classifier's contract — pure diff → plan
//          translation. The decisions this test set holds:
//
//             * Added / Removed / Modified entries map to
//               Add / Remove / Restart per ADR-0009 Decision 3.
//             * GatewaySettings changes do NOT produce runtime actions.
//             * Multiple field-level Modified entries for one entity
//               collapse into one Restart action (dedup invariant).
//             * Add and Restart actions carry the NEW entity config
//               looked up from the new GatewayConfiguration.
//             * Remove actions carry NewConfig = null.
//             * No-op diffs return ConfigurationReloadPlan.Empty.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public class RuntimeReloadClassifierTests
{
    // ── Empty / no-op paths ─────────────────────────────────────────

    [Fact]
    public void Classify_EmptyChanges_ReturnsEmptyPlan()
    {
        var plan = RuntimeReloadClassifier.Classify(MakeConfig(), Array.Empty<ConfigurationChange>());

        plan.Should().BeSameAs(ConfigurationReloadPlan.Empty);
        plan.IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void Classify_OnlyGatewaySettingsChange_ReturnsEmptyPlan()
    {
        // Per ADR-0009: gateway settings deltas do not produce runtime
        // work today. The plan is empty even though changes is not.
        var changes = new[] { GatewayChange() };

        var plan = RuntimeReloadClassifier.Classify(MakeConfig(), changes);

        plan.IsNoOp.Should().BeTrue();
        plan.Actions.Should().BeEmpty();
    }

    // ── Source operations ──────────────────────────────────────────

    [Fact]
    public void Classify_AddedSource_YieldsAddAction_WithNewConfig()
    {
        var newSource = MakeSource("plc-new");
        var newConfig = MakeConfig(sources: new[] { newSource });
        var changes = new[] { Added(ConfigurationEntityKind.Source, "plc-new") };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        var a = plan.Actions[0];
        a.Op.Should().Be(ReloadOp.Add);
        a.Kind.Should().Be(ConfigurationEntityKind.Source);
        a.EntityId.Should().Be("plc-new");
        a.NewConfig.Should().BeSameAs(newSource);
    }

    [Fact]
    public void Classify_RemovedSource_YieldsRemoveAction_WithNullNewConfig()
    {
        // Removed source is no longer in the new config — that's the
        // whole point. NewConfig is null.
        var changes = new[] { Removed(ConfigurationEntityKind.Source, "plc-gone") };

        var plan = RuntimeReloadClassifier.Classify(MakeConfig(), changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Remove);
        plan.Actions[0].EntityId.Should().Be("plc-gone");
        plan.Actions[0].NewConfig.Should().BeNull();
    }

    [Fact]
    public void Classify_ModifiedSource_YieldsRestartAction_WithNewConfig()
    {
        // Locked per ADR-0009 Decision 3: Modified always resolves to
        // Restart in v1. No in-place reconfigure path.
        var newSource = MakeSource("plc-1");
        var newConfig = MakeConfig(sources: new[] { newSource });
        var changes = new[] { Modified(ConfigurationEntityKind.Source, "plc-1", "Polling.IntervalMs") };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Restart);
        plan.Actions[0].EntityId.Should().Be("plc-1");
        plan.Actions[0].NewConfig.Should().BeSameAs(newSource);
    }

    [Fact]
    public void Classify_MultipleModifiedEntries_ForSameSource_CollapseToOneRestart()
    {
        // Real diffs can produce multiple field-level Modified entries
        // for the same entity (e.g., Polling.IntervalMs AND TimeoutMs
        // both changed). The coordinator should see ONE Restart, not
        // two — that's a per-instance reconcile, not per-field.
        var newSource = MakeSource("plc-1");
        var newConfig = MakeConfig(sources: new[] { newSource });
        var changes = new[]
        {
            Modified(ConfigurationEntityKind.Source, "plc-1", "Polling.IntervalMs"),
            Modified(ConfigurationEntityKind.Source, "plc-1", "Polling.TimeoutMs"),
            Modified(ConfigurationEntityKind.Source, "plc-1", "Connection.Address"),
        };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle("multiple field-level changes for one entity → one Restart");
        plan.Actions[0].Op.Should().Be(ReloadOp.Restart);
    }

    // ── Sink operations ────────────────────────────────────────────

    [Fact]
    public void Classify_AddedSink_YieldsAddAction_WithNewConfig()
    {
        var newSink = MakeSink("mqtt-new");
        var newConfig = MakeConfig(sinks: new[] { newSink });
        var changes = new[] { Added(ConfigurationEntityKind.Sink, "mqtt-new") };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Add);
        plan.Actions[0].Kind.Should().Be(ConfigurationEntityKind.Sink);
        plan.Actions[0].NewConfig.Should().BeSameAs(newSink);
    }

    [Fact]
    public void Classify_RemovedSink_YieldsRemoveAction()
    {
        var changes = new[] { Removed(ConfigurationEntityKind.Sink, "mqtt-gone") };

        var plan = RuntimeReloadClassifier.Classify(MakeConfig(), changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Remove);
        plan.Actions[0].Kind.Should().Be(ConfigurationEntityKind.Sink);
    }

    // ── Route operations ───────────────────────────────────────────

    [Fact]
    public void Classify_AddedRoute_YieldsAddAction_WithNewConfig()
    {
        var newRoute = MakeRoute("r-new", sourceId: "plc-1");
        var newConfig = MakeConfig(routes: new[] { newRoute });
        var changes = new[] { Added(ConfigurationEntityKind.Route, "r-new") };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Add);
        plan.Actions[0].Kind.Should().Be(ConfigurationEntityKind.Route);
        plan.Actions[0].NewConfig.Should().BeSameAs(newRoute);
    }

    [Fact]
    public void Classify_ModifiedRoute_YieldsRestartAction()
    {
        var newRoute = MakeRoute("r-1", sourceId: "plc-2"); // source changed
        var newConfig = MakeConfig(routes: new[] { newRoute });
        var changes = new[] { Modified(ConfigurationEntityKind.Route, "r-1", "SourceInstanceId") };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Op.Should().Be(ReloadOp.Restart);
        plan.Actions[0].NewConfig.Should().BeSameAs(newRoute);
    }

    // ── Mixed diffs ────────────────────────────────────────────────

    [Fact]
    public void Classify_MixedDiff_ProducesOneActionPerEntity()
    {
        // The realistic shape: add a source, remove an old one, modify
        // a route. All three actions appear in the plan.
        var newSource = MakeSource("plc-new");
        var newRoute = MakeRoute("r-1", sourceId: "plc-new");
        var newConfig = MakeConfig(sources: new[] { newSource }, routes: new[] { newRoute });
        var changes = new[]
        {
            Added(ConfigurationEntityKind.Source, "plc-new"),
            Removed(ConfigurationEntityKind.Source, "plc-old"),
            Modified(ConfigurationEntityKind.Route, "r-1", null),
        };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().HaveCount(3);
        plan.Actions.Select(a => (a.Op, a.Kind, a.EntityId))
            .Should().BeEquivalentTo(new[]
            {
                (ReloadOp.Add, ConfigurationEntityKind.Source, "plc-new"),
                (ReloadOp.Remove, ConfigurationEntityKind.Source, "plc-old"),
                (ReloadOp.Restart, ConfigurationEntityKind.Route, "r-1"),
            });
    }

    [Fact]
    public void Classify_GatewaySettingsChange_PlusSourceChange_DropsOnlyGateway()
    {
        // Mixed diff with a gateway-settings change mixed in: the
        // settings change is silently dropped, the source change
        // produces an action.
        var newSource = MakeSource("plc-1");
        var newConfig = MakeConfig(sources: new[] { newSource });
        var changes = new[]
        {
            GatewayChange(),
            Added(ConfigurationEntityKind.Source, "plc-1"),
        };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].EntityId.Should().Be("plc-1");
    }

    // ── Same id, different kinds — must not collide ─────────────────

    [Fact]
    public void Classify_SourceAndSinkWithSameId_ProducesTwoSeparateActions()
    {
        // The dedup key is (Kind, EntityId), not EntityId alone. A
        // Source named "shared" and a Sink named "shared" produce two
        // separate actions.
        var src = MakeSource("shared");
        var sink = MakeSink("shared");
        var newConfig = MakeConfig(sources: new[] { src }, sinks: new[] { sink });
        var changes = new[]
        {
            Added(ConfigurationEntityKind.Source, "shared"),
            Added(ConfigurationEntityKind.Sink, "shared"),
        };

        var plan = RuntimeReloadClassifier.Classify(newConfig, changes);

        plan.Actions.Should().HaveCount(2);
        plan.Actions.Should().Contain(a => a.Kind == ConfigurationEntityKind.Source);
        plan.Actions.Should().Contain(a => a.Kind == ConfigurationEntityKind.Sink);
    }

    // ── Defensive: missing-from-new-config on Add/Restart ─────────

    [Fact]
    public void Classify_AddedEntity_NotInNewConfig_IsSkipped()
    {
        // If a diff claims an Add but the new config doesn't contain
        // the entity (impossible in practice but defensive), the
        // classifier skips it rather than throw. The coordinator will
        // see no action for that entity; nothing happens to it.
        var changes = new[] { Added(ConfigurationEntityKind.Source, "ghost") };

        var plan = RuntimeReloadClassifier.Classify(MakeConfig(), changes);

        plan.Actions.Should().BeEmpty();
    }

    // ── Argument validation ───────────────────────────────────────

    [Fact]
    public void Classify_NullArguments_Throw()
    {
        ((Action)(() => RuntimeReloadClassifier.Classify(null!, Array.Empty<ConfigurationChange>())))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => RuntimeReloadClassifier.Classify(MakeConfig(), null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static GatewayConfiguration MakeConfig(
        IReadOnlyList<SourceInstanceConfig>? sources = null,
        IReadOnlyList<SinkInstanceConfig>? sinks = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = sources ?? Array.Empty<SourceInstanceConfig>(),
        Sinks = sinks ?? Array.Empty<SinkInstanceConfig>(),
        Routes = routes ?? Array.Empty<RouteConfig>(),
    };

    private static SourceInstanceConfig MakeSource(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "modbustcp",
        DeviceId = id,
        DeviceName = id,
        DeviceClass = "plc",
        Enabled = true,
    };

    private static SinkInstanceConfig MakeSink(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "mqtt",
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string routeId, string sourceId) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { "opcua-demo" },
    };

    private static ConfigurationChange Added(ConfigurationEntityKind kind, string id) => new()
    {
        Kind = ConfigurationChangeKind.Added,
        EntityKind = kind,
        EntityId = id,
        Summary = $"Added {kind} '{id}'",
    };

    private static ConfigurationChange Removed(ConfigurationEntityKind kind, string id) => new()
    {
        Kind = ConfigurationChangeKind.Removed,
        EntityKind = kind,
        EntityId = id,
        Summary = $"Removed {kind} '{id}'",
    };

    private static ConfigurationChange Modified(ConfigurationEntityKind kind, string id, string? path) => new()
    {
        Kind = ConfigurationChangeKind.Modified,
        EntityKind = kind,
        EntityId = id,
        Path = path,
        Summary = $"Modified {kind} '{id}'" + (path is null ? string.Empty : $" at {path}"),
    };

    private static ConfigurationChange GatewayChange() => new()
    {
        Kind = ConfigurationChangeKind.Modified,
        EntityKind = ConfigurationEntityKind.GatewaySettings,
        EntityId = "Gateway",
        Path = "GatewayName",
        Summary = "Modified gateway settings",
    };
}
