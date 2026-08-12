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

    // ═══ Pre-created entities (onboarding Save) ══════════════════════════
    // Save on a source/destination step writes the entity to the gateway
    // straight away, disabled, so it appears in the operator's list. Connect
    // then delivers its final enabled form — which means the id it is about to
    // "add" is already there. Without replace-in-place the ceremony would dead-
    // end: press Save, then Connect fails with "already exists".

    [Fact]
    public void BuildBundledOnboardingDraft_SourcePreCreatedBySave_IsReplacedNotRejected()
    {
        var saved = MakeSource("modbus-line1") with { Enabled = false };
        var current = MakeConfig(sources: new[] { saved });
        var final = MakeSource("modbus-line1");   // enabled, final form
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, final, sink, route, replaceSourceInPlace: true);

        result.Sources.Should().HaveCount(1, "the saved source and the connected one are the same device");
        result.Sources[0].Enabled.Should().BeTrue("Connect turns the saved source on");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_SinkPreCreatedBySave_IsReplacedNotRejected()
    {
        var saved = MakeSink("mqtt-eremos") with { Enabled = false };
        var current = MakeConfig(sinks: new[] { saved });
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, MakeSource("modbus-line1"), MakeSink("mqtt-eremos"), route,
            replaceSinkInPlace: true);

        result.Sinks.Should().HaveCount(1);
        result.Sinks[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void BuildBundledOnboardingDraft_ReplacedSource_KeepsItsPositionInTheList()
    {
        var current = MakeConfig(sources: new[]
        {
            MakeSource("first"),
            MakeSource("modbus-line1") with { Enabled = false },
            MakeSource("last"),
        });

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, MakeSource("modbus-line1"), MakeSink("mqtt-eremos"),
            MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"),
            replaceSourceInPlace: true);

        // A saved source should not jump to the end of the operator's list when
        // Connect commits its final form.
        result.Sources.Select(s => s.InstanceId)
            .Should().ContainInOrder("first", "modbus-line1", "last");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_DuplicateIdWithoutPreCreation_StillThrows()
    {
        // The guard still protects an UNRELATED existing entity from being
        // silently overwritten — only this ceremony's own Save earns a replace.
        var current = MakeConfig(sources: new[] { MakeSource("modbus-line1") });

        var act = () => WizardConfigMerger.BuildBundledOnboardingDraft(
            current, MakeSource("modbus-line1"), MakeSink("mqtt-eremos"),
            MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"));

        act.Should().Throw<ArgumentException>().WithMessage("*already exists*");
    }

    // ═══ Rename after Save (the orphan case) ═════════════════════════════
    // An operator saves a device, then corrects a typo in its instance id. The
    // entity written under the FIRST spelling must not survive: it is disabled,
    // referenced by no route, and nothing in the UI ever offers it again — a
    // permanent orphan in the customer's configuration.
    //
    // OnboardingFlow removes it inside the same draft as the re-Save, so the
    // rename is one atomic apply. These pin the merger half of that contract:
    // whatever the flow hands over, the bundle must end up with exactly one.

    [Fact]
    public void BuildBundledOnboardingDraft_RenamedSource_LeavesExactlyOneCopy()
    {
        // The flow has already dropped "modbus-typo" from the draft it passes in.
        var current = MakeConfig(sources: new[] { MakeSource("modbus-line1") with { Enabled = false } });

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, MakeSource("modbus-line1"), MakeSink("mqtt-eremos"),
            MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"),
            replaceSourceInPlace: true);

        result.Sources.Should().ContainSingle();
        result.Sources.Should().NotContain(s => s.InstanceId == "modbus-typo");
        result.Sources[0].Enabled.Should().BeTrue("Connect turns the saved device on");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_SavedThenRenamed_TreatsTheNewIdAsFresh()
    {
        // After a rename the new id has never been persisted, so the flow reports
        // it as NOT already-created and the bundle must create it rather than
        // expect an existing row to replace.
        var current = MakeConfig(sources: Array.Empty<SourceInstanceConfig>());

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, MakeSource("modbus-line1"), MakeSink("mqtt-eremos"),
            MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"));

        result.Sources.Should().ContainSingle(s => s.InstanceId == "modbus-line1");
    }

    [Fact]
    public void BuildBundledOnboardingDraft_EditedNonIdFieldAfterSave_StillReplaces()
    {
        // Editing a port or a poll interval leaves the id alone, so the entity on
        // the gateway is still THIS entity. The bundle must replace it — creating
        // it fresh would collide on the duplicate id and fail the whole ceremony.
        var saved = MakeSource("modbus-line1") with { Enabled = false };
        var current = MakeConfig(sources: new[] { saved });
        var edited = MakeSource("modbus-line1") with
        {
            Polling = new PollingSettings { IntervalMs = 5000 },
        };

        var result = WizardConfigMerger.BuildBundledOnboardingDraft(
            current, edited, MakeSink("mqtt-eremos"),
            MakeRoute("route-line1", "modbus-line1", "mqtt-eremos"),
            replaceSourceInPlace: true);

        result.Sources.Should().ContainSingle();
        result.Sources[0].Polling.IntervalMs.Should().Be(5000, "the operator's edit is what gets applied");
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
