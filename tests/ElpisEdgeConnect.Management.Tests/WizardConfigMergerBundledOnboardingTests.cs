// ============================================================================
// Tests: WizardConfigMerger.BuildBundledOnboardingDraft (Connect-a-device).
//        ADR-0016 Rule 6 — atomic bundled draft: source + sink + route
//        composed in one transaction with optional gateway-identity
//        override.
//
//        Pin the merger's invariants without standing up the API host —
//        the API tests cover endpoint dispatch separately.
// Reference: docs/decisions/0016-onboarding-meta-wizard.md Rule 6
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class WizardConfigMergerBundledOnboardingTests
{
    [Fact]
    public void BuildBundledOnboardingDraft_HappyPath_AppendsAllThreeEntities()
    {
        var current = MakeConfig();
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(current, source, sink, route);

        result.Sources.Should().HaveCount(1).And.Contain(source);
        result.Sinks.Should().HaveCount(1).And.Contain(sink);
        result.Routes.Should().HaveCount(1).And.Contain(route);
        // Gateway identity must be untouched when no override supplied
        result.Gateway.Should().Be(current.Gateway);
    }

    [Fact]
    public void BuildBundledOnboardingDraft_PreservesExistingEntities()
    {
        var existingSource = MakeSource("focas2-existing");
        var existingSink = MakeSink("opcua-existing");
        var existingRoute = MakeRoute("route-existing", "focas2-existing", "opcua-existing");
        var current = MakeConfig(
            sources: new[] { existingSource },
            sinks: new[] { existingSink },
            routes: new[] { existingRoute });

        var newSource = MakeSource("modbus-new");
        var newSink = MakeSink("mqtt-new");
        var newRoute = MakeRoute("route-new", "modbus-new", "mqtt-new");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(current, newSource, newSink, newRoute);

        result.Sources.Should().HaveCount(2).And.Contain(new[] { existingSource, newSource });
        result.Sinks.Should().HaveCount(2).And.Contain(new[] { existingSink, newSink });
        result.Routes.Should().HaveCount(2).And.Contain(new[] { existingRoute, newRoute });
    }

    [Fact]
    public void BuildBundledOnboardingDraft_AppliesGatewayIdOverride()
    {
        var current = MakeConfig(gatewayId: "gw-original");
        var source = MakeSource("modbus-1");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, source, sink, route,
            gatewayIdOverride: "gw-line1-edge",
            gatewayNameOverride: "Line 1 Edge");

        result.Gateway.GatewayId.Should().Be("gw-line1-edge");
        result.Gateway.GatewayName.Should().Be("Line 1 Edge");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_NullGatewayOverride_LeavesIdentityUnchanged()
    {
        var current = MakeConfig(gatewayId: "gw-prod", gatewayName: "Production");
        var source = MakeSource("modbus-1");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, source, sink, route,
            gatewayIdOverride: null,
            gatewayNameOverride: null);

        result.Gateway.GatewayId.Should().Be("gw-prod");
        result.Gateway.GatewayName.Should().Be("Production");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_SameAsCurrentOverride_IsNoOp()
    {
        var current = MakeConfig(gatewayId: "gw-same", gatewayName: "Same");
        var source = MakeSource("modbus-1");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, source, sink, route,
            gatewayIdOverride: "gw-same",
            gatewayNameOverride: "Same");

        result.Gateway.Should().Be(current.Gateway);
    }

    [Fact]
    public void BuildBundledOnboardingDraft_DuplicateSourceId_ThrowsArgumentException()
    {
        var existing = MakeSource("modbus-1");
        var current = MakeConfig(sources: new[] { existing });

        var dup = MakeSource("modbus-1"); // same id
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(current, dup, sink, route);

        act.Should().Throw<ArgumentException>().WithMessage("*modbus-1*already exists*");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_DuplicateSinkId_ThrowsArgumentException()
    {
        var existing = MakeSink("mqtt-1");
        var current = MakeConfig(sinks: new[] { existing });

        var source = MakeSource("modbus-1");
        var dup = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(current, source, dup, route);

        act.Should().Throw<ArgumentException>().WithMessage("*mqtt-1*already exists*");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_DuplicateRouteId_ThrowsArgumentException()
    {
        var existingSource = MakeSource("sX");
        var existingSink = MakeSink("sY");
        var existingRoute = MakeRoute("route-1", "sX", "sY");
        var current = MakeConfig(
            sources: new[] { existingSource },
            sinks: new[] { existingSink },
            routes: new[] { existingRoute });

        var newSource = MakeSource("modbus-1");
        var newSink = MakeSink("mqtt-1");
        var dupRoute = MakeRoute("route-1", "modbus-1", "mqtt-1");

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(current, newSource, newSink, dupRoute);

        act.Should().Throw<ArgumentException>().WithMessage("*route-1*already exists*");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_RouteSourceIdMismatch_ThrowsArgumentException()
    {
        var current = MakeConfig();
        var source = MakeSource("modbus-1");
        var sink = MakeSink("mqtt-1");
        var route = MakeRoute("route-1", "modbus-WRONG", "mqtt-1");

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(current, source, sink, route);

        act.Should().Throw<ArgumentException>().WithMessage("*SourceInstanceId*does not match*");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_RouteDoesNotReferenceNewSink_ThrowsArgumentException()
    {
        var current = MakeConfig();
        var source = MakeSource("modbus-1");
        var sink = MakeSink("mqtt-1");
        // route references a sink id that isn't the one we're creating
        var route = MakeRoute("route-1", "modbus-1", "mqtt-OTHER");

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(current, source, sink, route);

        act.Should().Throw<ArgumentException>().WithMessage("*SinkInstanceIds*does not include*mqtt-1*");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_RouteReferencesMultipleSinks_PassesWhenNewSinkIncluded()
    {
        var existingSink = MakeSink("mqtt-existing");
        var current = MakeConfig(sinks: new[] { existingSink });

        var source = MakeSource("modbus-1");
        var newSink = MakeSink("opcua-new");
        var route = new RouteConfig
        {
            RouteId = "route-1",
            Name = "route-1",
            SourceInstanceId = "modbus-1",
            SinkInstanceIds = new[] { "mqtt-existing", "opcua-new" }, // both
        };

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(current, source, newSink, route);

        result.Routes.Should().HaveCount(1);
        result.Routes[0].SinkInstanceIds.Should().BeEquivalentTo("mqtt-existing", "opcua-new");
    }

    // ═══ Helpers ═════════════════════════════════════════════════════════

    private static GatewayConfiguration MakeConfig(
        string gatewayId = "gw-test",
        string gatewayName = "Test",
        IReadOnlyList<SourceInstanceConfig>? sources = null,
        IReadOnlyList<SinkInstanceConfig>? sinks = null,
        IReadOnlyList<RouteConfig>? routes = null) => new()
    {
        Gateway = new GatewaySettings { GatewayId = gatewayId, GatewayName = gatewayName },
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
