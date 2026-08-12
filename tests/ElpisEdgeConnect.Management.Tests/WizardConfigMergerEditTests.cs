// ============================================================================
// Tests: WizardConfigMerger — Edit-mode transformations (M.2d.3).
//        BuildEditedSinkDraft: replaces an existing sink in-place;
//          sources + routes preserved byte-identically.
//        BuildEditedRouteDraft: replaces an existing route in-place;
//          sources + sinks preserved byte-identically.
//        Both mirror the defence-in-depth invariants of
//        BuildUpdatedSourceDraft (M.2d.2) and BuildNewRouteDraft (M.2b.5).
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3.1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class WizardConfigMergerEditTests
{
    // ═══ BuildEditedSinkDraft ════════════════════════════════════════════

    [Fact]
    public void BuildEditedSinkDraft_HappyPath_ReplacesSinkInPlace()
    {
        var original = MakeSink("mqtt-eremos");
        var sibling = MakeSink("opcua-demo");
        var current = MakeConfig(sinks: new[] { original, sibling });

        var updated = original with { Enabled = false };
        var result = WizardConfigMerger.BuildEditedSinkDraft(current, updated);

        result.Sinks.Should().HaveCount(2);
        result.Sinks[0].Should().Be(updated);
        result.Sinks[1].Should().Be(sibling, "sibling sink must be untouched");
    }

    [Fact]
    public void BuildEditedSinkDraft_PreservesSources_AndRoutes_ByReference()
    {
        // Routes and Sources must be the same object reference —
        // Edit-mode sink changes never touch them.
        var original = MakeSink("mqtt-eremos");
        var source = MakeSource("plc-1");
        var route = MakeRoute("route-1", "plc-1", "mqtt-eremos");
        var current = MakeConfig(
            sources: new[] { source },
            sinks: new[] { original },
            routes: new[] { route });

        var result = WizardConfigMerger.BuildEditedSinkDraft(current, original with { Enabled = false });

        ReferenceEquals(result.Sources, current.Sources).Should().BeTrue(
            "sources must be byte-identical (same reference) after sink edit");
        ReferenceEquals(result.Routes, current.Routes).Should().BeTrue(
            "routes must be byte-identical (same reference) after sink edit");
    }

    [Fact]
    public void BuildEditedSinkDraft_SinkNotFound_ThrowsArgumentException()
    {
        var current = MakeConfig(sinks: new[] { MakeSink("mqtt-eremos") });
        var phantom = MakeSink("sink-not-in-config");

        var act = () => WizardConfigMerger.BuildEditedSinkDraft(current, phantom);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*sink-not-in-config*");
    }

    [Fact]
    public void BuildEditedSinkDraft_ProtocolNameChanged_ThrowsArgumentException()
    {
        var original = MakeSink("mqtt-eremos"); // ProtocolName = "mqtt"
        var current = MakeConfig(sinks: new[] { original });
        var protocolSwitch = original with { ProtocolName = "opcua-server" };

        var act = () => WizardConfigMerger.BuildEditedSinkDraft(current, protocolSwitch);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ProtocolName*");
    }

    // ═══ BuildEditedRouteDraft ═══════════════════════════════════════════

    [Fact]
    public void BuildEditedRouteDraft_HappyPath_ReplacesRouteInPlace()
    {
        var source = MakeSource("plc-1");
        var sink = MakeSink("mqtt-eremos");
        var original = MakeRoute("route-1", "plc-1", "mqtt-eremos");
        var sibling = MakeRoute("route-2", "plc-1", "mqtt-eremos");
        var current = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { original, sibling });

        var updated = original with { Name = "Route One (renamed)" };
        var result = WizardConfigMerger.BuildEditedRouteDraft(current, updated);

        result.Routes.Should().HaveCount(2);
        result.Routes[0].Should().Be(updated);
        result.Routes[1].Should().Be(sibling, "sibling route must be untouched");
    }

    [Fact]
    public void BuildEditedRouteDraft_PreservesSources_AndSinks_ByReference()
    {
        var source = MakeSource("plc-1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "plc-1", "mqtt-eremos");
        var current = MakeConfig(
            sources: new[] { source },
            sinks: new[] { sink },
            routes: new[] { route });

        var result = WizardConfigMerger.BuildEditedRouteDraft(current, route with { Name = "updated" });

        ReferenceEquals(result.Sources, current.Sources).Should().BeTrue(
            "sources must be byte-identical (same reference) after route edit");
        ReferenceEquals(result.Sinks, current.Sinks).Should().BeTrue(
            "sinks must be byte-identical (same reference) after route edit");
    }

    [Fact]
    public void BuildEditedRouteDraft_RouteNotFound_ThrowsArgumentException()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-1") },
            sinks: new[] { MakeSink("mqtt-eremos") },
            routes: new[] { MakeRoute("route-1", "plc-1", "mqtt-eremos") });

        var phantom = MakeRoute("route-not-in-config", "plc-1", "mqtt-eremos");

        var act = () => WizardConfigMerger.BuildEditedRouteDraft(current, phantom);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*route-not-in-config*");
    }

    [Fact]
    public void BuildEditedRouteDraft_ReferencedSourceMissing_ThrowsArgumentException()
    {
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-1", "plc-1", "mqtt-eremos");
        // Config has NO source "plc-1"
        var current = MakeConfig(
            sources: Array.Empty<SourceInstanceConfig>(),
            sinks: new[] { sink },
            routes: new[] { route });

        var updated = route with { Name = "changed" };

        var act = () => WizardConfigMerger.BuildEditedRouteDraft(current, updated);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*plc-1*");
    }

    [Fact]
    public void BuildEditedRouteDraft_ReferencedSinkMissing_ThrowsArgumentException()
    {
        var source = MakeSource("plc-1");
        var route = MakeRoute("route-1", "plc-1", "mqtt-eremos");
        // Config has NO sink "mqtt-eremos"
        var current = MakeConfig(
            sources: new[] { source },
            sinks: Array.Empty<SinkInstanceConfig>(),
            routes: new[] { route });

        var updated = route with { Name = "changed" };

        var act = () => WizardConfigMerger.BuildEditedRouteDraft(current, updated);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*mqtt-eremos*");
    }

    // ═══ Helpers ════════════════════════════════════════════════════════

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

    private static SourceInstanceConfig MakeSource(string instanceId) => new()
    {
        InstanceId = instanceId,
        ProtocolName = "modbustcp",
        DeviceId = instanceId,
        DeviceName = instanceId,
        DeviceClass = "plc",
        Enabled = true,
        Polling = new PollingSettings { IntervalMs = 200 },
    };

    private static SinkInstanceConfig MakeSink(string instanceId) => new()
    {
        InstanceId = instanceId,
        ProtocolName = "mqtt",
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string routeId, string sourceId, string sinkId) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { sinkId },
    };
}
