// ============================================================================
// Tests: OnboardingRouteWiring — the Connect-a-device flow's cross-entity
//        wiring rule, which the onboarding flow now asks BEFORE the apply so a
//        mis-wired route cannot be POSTed at all.
//
//        The defect these pin: onboarding steps stay mounted across Back/Next
//        (ADR-0016 Rule 2), so a route built at step 5 kept holding the source
//        and destination ids as they were when that step first mounted. Rename
//        either endpoint afterwards and the flow still looked complete — right
//        up to the apply, which refused it with a message naming a parameter
//        rather than a step. Every rename case below is that defect.
//
//        The rule mirrored is WizardConfigMerger.BuildBundledOnboardingDraft's
//        two cross-entity invariants, and two tests at the bottom pin the two
//        against each other so they cannot drift apart.
// Reference: docs/decisions/0016-onboarding-meta-wizard.md Rule 6
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OnboardingRouteWiringTests
{
    // ═══ Wired correctly ═════════════════════════════════════════════════

    [Fact]
    public void IsWiredTo_RouteReferencesBothEndpoints_ReturnsTrue()
    {
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeTrue();
    }

    [Fact]
    public void DescribeMismatch_RouteReferencesBothEndpoints_ReturnsNull()
    {
        // Null is "nothing to say" — the review screen and the route step both
        // key their warning off this, so a false positive here nags an operator
        // whose configuration is already correct.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");

        OnboardingRouteWiring.DescribeMismatch(route, source, sink).Should().BeNull();
    }

    [Fact]
    public void IsWiredTo_RouteIdUnrelatedToEndpointNames_ReturnsTrue()
    {
        // The route id is a LABEL. `route-oldname` after a rename is not a
        // defect and must not be treated as one — re-deriving it would orphan a
        // route the operator may already have staged under that id. Only the
        // cross-references are load-bearing.
        var source = MakeSource("modbus-line7");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line7", "mqtt-eremos");

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeTrue();
    }

    // ═══ Source renamed after the route step was visited ═════════════════

    [Fact]
    public void IsWiredTo_SourceRenamedAfterRouteWasBuilt_ReturnsFalse()
    {
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var renamedSource = MakeSource("modbus-lathe-3");
        var sink = MakeSink("mqtt-eremos");

        OnboardingRouteWiring.IsWiredTo(route, renamedSource, sink).Should().BeFalse();
    }

    [Fact]
    public void DescribeMismatch_SourceRenamed_NamesTheStaleIdAndTheConfiguredOne()
    {
        // Both ids have to appear. The operator cannot see the disagreement
        // otherwise: the route and the renamed source each look perfectly valid
        // in isolation, and only the pair is wrong.
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var renamedSource = MakeSource("modbus-lathe-3");
        var sink = MakeSink("mqtt-eremos");

        var message = OnboardingRouteWiring.DescribeMismatch(route, renamedSource, sink);

        message.Should().NotBeNull();
        message.Should().Contain("modbus-line1", "the id the route still holds");
        message.Should().Contain("modbus-lathe-3", "the id the operator configured");
        message.Should().Contain("route step", "the operator needs to know where to go");
    }

    // ═══ Destination renamed after the route step was visited ════════════

    [Fact]
    public void IsWiredTo_DestinationRenamedAfterRouteWasBuilt_ReturnsFalse()
    {
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var source = MakeSource("modbus-line1");
        var renamedSink = MakeSink("mqtt-eremos-prod");

        OnboardingRouteWiring.IsWiredTo(route, source, renamedSink).Should().BeFalse();
    }

    [Fact]
    public void DescribeMismatch_DestinationRenamed_NamesTheStaleIdAndTheConfiguredOne()
    {
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var source = MakeSource("modbus-line1");
        var renamedSink = MakeSink("mqtt-eremos-prod");

        var message = OnboardingRouteWiring.DescribeMismatch(route, source, renamedSink);

        message.Should().NotBeNull();
        message.Should().Contain("mqtt-eremos", "the id the route still delivers to");
        message.Should().Contain("mqtt-eremos-prod", "the id the operator configured");
        message.Should().NotContain("modbus-line1", "the source agrees — saying so buries the one thing that doesn't");
    }

    // ═══ Both renamed ════════════════════════════════════════════════════

    [Fact]
    public void IsWiredTo_BothEndpointsRenamed_ReturnsFalse()
    {
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var renamedSource = MakeSource("modbus-lathe-3");
        var renamedSink = MakeSink("mqtt-eremos-prod");

        OnboardingRouteWiring.IsWiredTo(route, renamedSource, renamedSink).Should().BeFalse();
    }

    [Fact]
    public void DescribeMismatch_BothEndpointsRenamed_NamesAllFourIds()
    {
        // Reporting only the first failure would send the operator back to fix
        // the source, and then straight back again for the destination.
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var renamedSource = MakeSource("modbus-lathe-3");
        var renamedSink = MakeSink("mqtt-eremos-prod");

        var message = OnboardingRouteWiring.DescribeMismatch(route, renamedSource, renamedSink);

        message.Should().NotBeNull();
        message.Should().Contain("modbus-line1").And.Contain("mqtt-eremos");
        message.Should().Contain("modbus-lathe-3").And.Contain("mqtt-eremos-prod");
    }

    // ═══ Fan-out: several destinations on one route ══════════════════════

    [Fact]
    public void IsWiredTo_FanOutRouteStillIncludingTheCeremonyDestination_ReturnsTrue()
    {
        // A route may fan out to many destinations (blueprint §3). The merger
        // requires CONTAINS, not equals, so an operator who added a second
        // destination inside the route wizard must not be blocked — that is a
        // supported configuration, not a mistake.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-historian", "mqtt-eremos", "opcua-scada");

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeTrue();
        OnboardingRouteWiring.DescribeMismatch(route, source, sink).Should().BeNull();
    }

    [Fact]
    public void IsWiredTo_FanOutRouteWithTheCeremonyDestinationRemoved_ReturnsFalse()
    {
        // The un-tick case: the operator cleared this ceremony's destination
        // from the route's fan-out list. The apply would refuse the bundle, so
        // the step is blocked — and deliberately NOT auto-corrected, because
        // re-ticking it would overwrite a choice the operator made on purpose.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-historian", "opcua-scada");

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeFalse();
    }

    [Fact]
    public void DescribeMismatch_FanOutRouteWithTheCeremonyDestinationRemoved_ListsWhatItDoesDeliverTo()
    {
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-historian", "opcua-scada");

        var message = OnboardingRouteWiring.DescribeMismatch(route, source, sink);

        message.Should().NotBeNull();
        message.Should().Contain("mqtt-historian").And.Contain("opcua-scada",
            "the operator needs to see the list they actually left behind, not just the missing one");
        message.Should().Contain("mqtt-eremos", "the destination that has to be back in that list");
    }

    [Fact]
    public void DescribeMismatch_RouteWithNoDestinationsAtAll_UsesWordsNotEmptyQuotes()
    {
        // Guards the rendering rather than the rule: joining an empty list into
        // the sentence produces "It delivers to ''", which reads as a bug in the
        // screen and tells the operator nothing.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1");

        var message = OnboardingRouteWiring.DescribeMismatch(route, source, sink);

        message.Should().NotBeNull();
        message.Should().Contain("no destination at all");
        message.Should().NotContain("''");
    }

    // ═══ Ordinal comparison — same as the merger's ═══════════════════════

    [Theory]
    [InlineData("MODBUS-LINE1", "mqtt-eremos")]
    [InlineData("modbus-line1", "MQTT-Eremos")]
    public void IsWiredTo_IdsDifferingOnlyByCase_ReturnsFalse(string routeSourceId, string routeSinkId)
    {
        // Instance ids are ordinal throughout config. Accepting a case-insensitive
        // match here would pass the step and then fail the apply — the exact
        // failure mode this helper exists to remove.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", routeSourceId, routeSinkId);

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeFalse();
    }

    // ═══ Null inputs ═════════════════════════════════════════════════════

    [Fact]
    public void IsWiredTo_NullRoute_ReturnsFalse()
    {
        // "Is this safe to POST?" cannot be true of a ceremony missing an
        // entity. Returning false keeps the caller's gate fail-closed even
        // before the operator has finished a step.
        OnboardingRouteWiring.IsWiredTo(null, MakeSource("s"), MakeSink("k")).Should().BeFalse();
    }

    [Fact]
    public void IsWiredTo_NullSource_ReturnsFalse()
    {
        OnboardingRouteWiring.IsWiredTo(MakeRoute("r", "s", "k"), null, MakeSink("k")).Should().BeFalse();
    }

    [Fact]
    public void IsWiredTo_NullSink_ReturnsFalse()
    {
        OnboardingRouteWiring.IsWiredTo(MakeRoute("r", "s", "k"), MakeSource("s"), null).Should().BeFalse();
    }

    [Fact]
    public void IsWiredTo_AllThreeNull_ReturnsFalse()
    {
        OnboardingRouteWiring.IsWiredTo(null, null, null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void DescribeMismatch_AnyEntityMissing_ReturnsNull(bool hasRoute, bool hasSource, bool hasSink)
    {
        // Deliberately asymmetric with IsWiredTo, which says false for the same
        // input. A step the operator simply hasn't reached yet is not a
        // DISAGREEMENT to explain — it is already gated by its own step, and
        // telling them the route "does not point at what you configured" before
        // they have configured anything is noise.
        var route = hasRoute ? MakeRoute("route-line1", "modbus-line1", "mqtt-eremos") : null;
        var source = hasSource ? MakeSource("modbus-line1") : null;
        var sink = hasSink ? MakeSink("mqtt-eremos") : null;

        OnboardingRouteWiring.DescribeMismatch(route, source, sink).Should().BeNull();
    }

    // ═══ Parity with the merger — the two must never drift ═══════════════

    [Fact]
    public void IsWiredTo_WhenTrue_TheBundledDraftTheMergerBuildsSucceeds()
    {
        // The helper's only justification is that it answers the merger's
        // question early. If it ever says yes where the merger throws, the flow
        // is back to discovering the failure at the last click.
        var source = MakeSource("modbus-line1");
        var sink = MakeSink("mqtt-eremos");
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-historian", "mqtt-eremos");

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeTrue();

        var build = () => WizardConfigMerger.BuildBundledOnboardingDraft(MakeConfig(), source, sink, route);

        build.Should().NotThrow();
    }

    [Theory]
    [InlineData("modbus-renamed", "mqtt-eremos")]   // source renamed
    [InlineData("modbus-line1", "mqtt-renamed")]    // destination renamed
    [InlineData("modbus-renamed", "mqtt-renamed")]  // both renamed
    public void IsWiredTo_WhenFalse_TheMergerRejectsTheSameBundle(string sourceId, string sinkId)
    {
        // The other half of the parity: everything the helper blocks is
        // something the apply would have refused anyway, so blocking the step
        // costs the operator nothing they could otherwise have had.
        var route = MakeRoute("route-line1", "modbus-line1", "mqtt-eremos");
        var source = MakeSource(sourceId);
        var sink = MakeSink(sinkId);

        OnboardingRouteWiring.IsWiredTo(route, source, sink).Should().BeFalse();

        var build = () => WizardConfigMerger.BuildBundledOnboardingDraft(MakeConfig(), source, sink, route);

        build.Should().Throw<ArgumentException>();
    }

    // ═══ Helpers ═════════════════════════════════════════════════════════

    private static GatewayConfiguration MakeConfig() => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = Array.Empty<SourceInstanceConfig>(),
        Sinks = Array.Empty<SinkInstanceConfig>(),
        Routes = Array.Empty<RouteConfig>(),
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

    /// <summary>
    /// A route with zero or more destinations — the fan-out cases need several,
    /// and the empty-list case needs none.
    /// </summary>
    private static RouteConfig MakeRoute(string routeId, string sourceId, params string[] sinkIds) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new List<string>(sinkIds),
    };
}
