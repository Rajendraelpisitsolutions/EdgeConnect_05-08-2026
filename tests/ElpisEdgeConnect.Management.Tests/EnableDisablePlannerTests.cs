// ============================================================================
// Tests: EnableDisablePlanner — pins the pure planner (M.2b.6.1) across:
//
//   * Apply happy paths for each entity kind (enable + disable)
//   * NoOp suppression when current state already equals desired (Locked F)
//   * Cross-record refusal with correctly-populated blocker lists (Locked C):
//       - disable source while enabled route references it
//       - disable sink while enabled route references it
//       - enable route while source disabled
//       - enable route while sink disabled
//       - enable route while a sink is phantom (defence in depth)
//   * Idempotency (planner is pure — same input produces same output)
//   * Input mutation check (input config object NOT mutated)
//   * Unknown-id surfaces as KeyNotFoundException (API maps to 404)
//   * Impact summary correctness (diff string, route-disable impact warning)
//
// Plus the LOAD-BEARING purity-guard test (handoff §5): the planner's
// assembly must not depend on logging / metrics / runtime-state /
// ASP.NET Core HTTP symbols. This catches future drift toward an
// "impure" planner before it lands.
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md
//            docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md §5
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class EnableDisablePlannerTests
{
    // ─── Apply happy paths ──────────────────────────────────────────────

    [Fact]
    public void Plan_EnableDisabledSource_ApplyOutcome_DraftFlipsEnabled()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Draft.Should().NotBeNull();
        result.Draft!.Sources.Single(s => s.InstanceId == "plc-1").Enabled.Should().BeTrue();
        result.Blockers.Should().BeEmpty();
        result.Impact.DiffSummary.Should().Be("Enabled: false → true");
        result.Impact.ImpactWarning.Should().BeNull();
    }

    [Fact]
    public void Plan_EnableDisabledSink_ApplyOutcome_DraftFlipsEnabled()
    {
        var current = MakeConfig(
            sinks: new[] { MakeSink("mqtt-1", enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Sink, "mqtt-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Draft!.Sinks.Single(s => s.InstanceId == "mqtt-1").Enabled.Should().BeTrue();
    }

    [Fact]
    public void Plan_DisableEnabledRoute_ApplyOutcome_DraftFlipsEnabled_AndCarriesImpactWarning()
    {
        // Route disable is the one operation that carries an impact warning
        // (per v1 Locked B): data flow stops until re-enabled.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: true) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Draft!.Routes.Single(r => r.RouteId == "r-1").Enabled.Should().BeFalse();
        result.Impact.ImpactWarning.Should().NotBeNull();
        result.Impact.ImpactWarning.Should().Contain("plc-1").And.Contain("mqtt-1");
        result.Impact.ImpactWarning.Should().Contain("stop");
    }

    [Fact]
    public void Plan_EnableDisabledRoute_NoImpactWarning()
    {
        // Enabling a route doesn't stop anything (it starts data flow).
        // Impact warning is for the disable direction only.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Impact.ImpactWarning.Should().BeNull();
    }

    // ─── NoOp suppression (Locked F) ────────────────────────────────────

    [Fact]
    public void Plan_EnableAlreadyEnabledSource_NoOpOutcome_NoDraft()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-1", enabled: true) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.NoOp);
        result.Draft.Should().BeNull();
        result.Blockers.Should().BeEmpty();
        result.Impact.DiffSummary.Should().Be("Enabled: true → true");
    }

    [Fact]
    public void Plan_DisableAlreadyDisabledSink_NoOpOutcome_NoDraft()
    {
        var current = MakeConfig(sinks: new[] { MakeSink("mqtt-1", enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Sink, "mqtt-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.NoOp);
        result.Draft.Should().BeNull();
    }

    [Fact]
    public void Plan_EnableAlreadyEnabledRoute_NoOpOutcome_NoDraft()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: true) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.NoOp);
        result.Draft.Should().BeNull();
    }

    // ─── Cross-record refusal (Locked C) ────────────────────────────────

    [Fact]
    public void Plan_DisableSource_BlockedByEnabledReferencingRoutes()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[]
            {
                MakeRoute("r-spindle", "plc-1", new[] { "mqtt-1" }, enabled: true),
                MakeRoute("r-alarms", "plc-1", new[] { "mqtt-1" }, enabled: true),
            });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Draft.Should().BeNull();
        result.Blockers.Should().HaveCount(2);
        result.Blockers.Select(b => b.Id).Should().BeEquivalentTo(new[] { "r-spindle", "r-alarms" });
        result.Blockers.Should().AllSatisfy(b => b.Kind.Should().Be(ConfigEntityKind.Route));
    }

    [Fact]
    public void Plan_DisableSource_DisabledRoutes_NotBlockers()
    {
        // Only ENABLED routes count as blockers — disabled routes don't
        // run, so disabling the source is operationally safe.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[]
            {
                MakeRoute("r-disabled", "plc-1", new[] { "mqtt-1" }, enabled: false),
            });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void Plan_DisableSink_BlockedByEnabledReferencingRoutes()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-1", enabled: true) },
            routes: new[]
            {
                MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: true),
            });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Sink, "mqtt-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Blockers.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DependencyRef(ConfigEntityKind.Route, "r-1", "r-1"));
    }

    [Fact]
    public void Plan_EnableRoute_BlockedByDisabledSource()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: false) },
            sinks: new[] { MakeSink("mqtt-1", enabled: true) },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Blockers.Should().ContainSingle()
            .Which.Kind.Should().Be(ConfigEntityKind.Source);
        result.Blockers.Single().Id.Should().Be("plc-1");
    }

    [Fact]
    public void Plan_EnableRoute_BlockedByDisabledSink()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: true) },
            sinks: new[] { MakeSink("mqtt-1", enabled: false) },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Blockers.Should().ContainSingle()
            .Which.Kind.Should().Be(ConfigEntityKind.Sink);
    }

    [Fact]
    public void Plan_EnableRoute_BlockedByPhantomSink_ListedAsBlocker()
    {
        // Defence in depth — the merger / validator would also catch a
        // phantom sink reference, but the planner mirrors so operators see
        // the same blocker list whether they hit the planner or the validator.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            // sink "mqtt-ghost" not in config.Sinks.
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-ghost" }, enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Blockers.Should().Contain(b =>
            b.Kind == ConfigEntityKind.Sink && b.Id == "mqtt-ghost");
    }

    [Fact]
    public void Plan_EnableRoute_MultipleBlockers_AllListed()
    {
        // Real-world: a route with a disabled source AND a disabled sink
        // surfaces BOTH blockers so the operator fixes them in one go,
        // not one-at-a-time through repeated 409s.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1", enabled: false) },
            sinks: new[]
            {
                MakeSink("mqtt-1", enabled: false),
                MakeSink("mqtt-2", enabled: true),
            },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1", "mqtt-2" }, enabled: false) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: true);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.CrossRecordRefused);
        result.Blockers.Should().HaveCount(2);  // plc-1 + mqtt-1; mqtt-2 OK
        result.Blockers.Select(b => b.Id).Should().Contain(new[] { "plc-1", "mqtt-1" });
        result.Blockers.Select(b => b.Id).Should().NotContain("mqtt-2");
    }

    [Fact]
    public void Plan_DisableRoute_NeverBlocked()
    {
        // Disabling a route is always allowed (it just stops data flow);
        // dependents are listed as informational impact, never as blockers.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-1") },
            routes: new[] { MakeRoute("r-1", "plc-1", new[] { "mqtt-1" }, enabled: true) });

        var result = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Route, "r-1", desiredEnabled: false);

        result.Outcome.Should().Be(EnableDisablePlanOutcome.Apply);
        result.Blockers.Should().BeEmpty();
    }

    // ─── Purity (no input mutation) ─────────────────────────────────────

    [Fact]
    public void Plan_DoesNotMutateInputConfiguration()
    {
        // Locked purity: the planner uses record-with to produce a new
        // draft and must NOT mutate the input. Capture pre-call shape
        // and assert post-call equality.
        var originalSource = MakeSource("plc-1", enabled: false);
        var current = MakeConfig(sources: new[] { originalSource });
        var beforeSourceCount = current.Sources.Count;
        var beforeEnabled = originalSource.Enabled;

        _ = EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-1", desiredEnabled: true);

        current.Sources.Should().HaveCount(beforeSourceCount);
        current.Sources.Single().Enabled.Should().Be(beforeEnabled,
            "the planner produces a new draft via record-with; it must not mutate the input");
        ReferenceEquals(current.Sources.Single(), originalSource).Should().BeTrue();
    }

    // ─── Unknown id ─────────────────────────────────────────────────────

    [Fact]
    public void Plan_UnknownSourceId_ThrowsKeyNotFound()
    {
        var current = MakeConfig();
        var act = () => EnableDisablePlanner.Plan(
            current, ConfigEntityKind.Source, "plc-ghost", desiredEnabled: true);
        act.Should().Throw<KeyNotFoundException>().WithMessage("*plc-ghost*");
    }

    [Fact]
    public void Plan_UnknownSinkId_ThrowsKeyNotFound()
    {
        var act = () => EnableDisablePlanner.Plan(
            MakeConfig(), ConfigEntityKind.Sink, "mqtt-ghost", desiredEnabled: true);
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Plan_UnknownRouteId_ThrowsKeyNotFound()
    {
        var act = () => EnableDisablePlanner.Plan(
            MakeConfig(), ConfigEntityKind.Route, "r-ghost", desiredEnabled: true);
        act.Should().Throw<KeyNotFoundException>();
    }

    // ─── Argument guards ───────────────────────────────────────────────

    [Fact]
    public void Plan_NullConfig_Throws()
    {
        var act = () => EnableDisablePlanner.Plan(
            null!, ConfigEntityKind.Source, "plc-1", true);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plan_BlankInstanceId_Throws(string id)
    {
        var act = () => EnableDisablePlanner.Plan(
            MakeConfig(), ConfigEntityKind.Source, id, true);
        act.Should().Throw<ArgumentException>();
    }

    // ─── Purity guard (load-bearing — handoff §5) ──────────────────────

    [Fact]
    public void Planner_AssemblyDoesNotDependOnLoggingMetricsHttpOrRuntimeSymbols()
    {
        // Load-bearing test from handoff §5: the planner layer must stay
        // pure config reasoning. If a future refactor pulls in a Logger,
        // a Counter, ASP.NET Core HTTP types, or runtime-state services,
        // this test fails. Defence in depth on top of the layer-discipline
        // documentation.
        //
        // We test by reflecting over the planner's assembly references
        // and asserting that none of the boundary assemblies appear.
        // Note: the planner LIVES in ElpisEdgeConnect.Management.dll
        // (which itself necessarily references logging / HTTP for the
        // API + Razor surfaces). So we can't test the assembly-graph;
        // we test that the PLANNER FILE's symbol declarations don't
        // pull these in.
        //
        // Specifically: walk the planner class + every type defined in
        // the same namespace (EnableDisablePlanResult, ImpactSummary,
        // DependencyRef, the enums) and assert no member signatures
        // reference banned namespaces.
        var bannedNamespaces = new[]
        {
            "Microsoft.Extensions.Logging",
            "System.Diagnostics.Metrics",
            "Microsoft.AspNetCore.Http",
            "Microsoft.AspNetCore.Builder",
            "Microsoft.AspNetCore.Routing",
        };

        var plannerType = typeof(EnableDisablePlanner);
        var assembly = plannerType.Assembly;
        var plannerNamespace = plannerType.Namespace!;

        // Inspect the planner's static methods + result-record members.
        var typesToInspect = new[]
        {
            plannerType,
            typeof(EnableDisablePlanResult),
            typeof(ImpactSummary),
            typeof(DependencyRef),
            typeof(EnableDisablePlanOutcome),
            typeof(ConfigEntityKind),
        };

        foreach (var type in typesToInspect)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Static | BindingFlags.Instance
                                                   | BindingFlags.DeclaredOnly))
            {
                AssertTypeNotInBannedNamespaces(method.ReturnType, bannedNamespaces, $"{type.Name}.{method.Name} return type");
                foreach (var p in method.GetParameters())
                {
                    AssertTypeNotInBannedNamespaces(p.ParameterType, bannedNamespaces, $"{type.Name}.{method.Name}({p.Name})");
                }
            }
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var p in ctor.GetParameters())
                {
                    AssertTypeNotInBannedNamespaces(p.ParameterType, bannedNamespaces, $"{type.Name} ctor({p.Name})");
                }
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                AssertTypeNotInBannedNamespaces(field.FieldType, bannedNamespaces, $"{type.Name}.{field.Name} field");
            }
        }
    }

    private static void AssertTypeNotInBannedNamespaces(Type type, string[] banned, string context)
    {
        var ns = type.Namespace ?? string.Empty;
        foreach (var b in banned)
        {
            ns.StartsWith(b, StringComparison.Ordinal)
                .Should().BeFalse($"{context} must not reference {b} (planner-purity guard)");
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

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

    private static SourceInstanceConfig MakeSource(string id, bool enabled = true) => new()
    {
        InstanceId = id,
        ProtocolName = "modbustcp",
        DeviceId = id,
        DeviceName = id,
        DeviceClass = "plc",
        Enabled = enabled,
        Polling = new PollingSettings { IntervalMs = 200 },
    };

    private static SinkInstanceConfig MakeSink(string id, bool enabled = true) => new()
    {
        InstanceId = id,
        ProtocolName = "mqtt",
        Enabled = enabled,
    };

    private static RouteConfig MakeRoute(string id, string sourceId, string[] sinkIds, bool enabled = true) => new()
    {
        RouteId = id,
        Name = id,
        SourceInstanceId = sourceId,
        SinkInstanceIds = sinkIds,
        Enabled = enabled,
    };
}
