// ============================================================================
// Tests: WizardConfigMerger — pure transformation Current + NewSource +
//        RouteWiring → new draft GatewayConfiguration. The safety
//        invariants enforced here are the architectural backbone for
//        every future wizard (S7, FOCAS2, MTConnect, sinks), so the
//        tests pin them deliberately.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class WizardConfigMergerTests
{
    [Fact]
    public void AddNewSource_AppendsToSources_LeavesExistingUntouched()
    {
        // Disabled source + NotWired = legal per the merger invariant.
        // (Enabled-source-without-route is a separate test below.)
        var current = MakeConfig(sources: new[] { MakeSource("plc-existing") });
        var newSource = MakeSource("plc-new") with { Enabled = false };

        var result = WizardConfigMerger.BuildNewSourceDraft(current, newSource, RouteWiring.None);

        result.Sources.Should().HaveCount(2);
        result.Sources.Select(s => s.InstanceId).Should().ContainInOrder("plc-existing", "plc-new");
    }

    [Fact]
    public void AddNewSource_DuplicateInstanceId_Throws()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-line-7") });
        var newSource = MakeSource("plc-line-7");

        var act = () => WizardConfigMerger.BuildNewSourceDraft(current, newSource, RouteWiring.None);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*plc-line-7*already exists*");
    }

    [Fact]
    public void NotWired_DisabledSource_DoesNotTouchRoutes()
    {
        // "Do not wire yet" wizard branch: source is created as disabled,
        // routes are untouched. Operator activates later by editing JSON.
        var existingRoute = MakeRoute("r-existing", sourceId: "plc-existing");
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-existing") },
            routes: new[] { existingRoute });
        var newSource = MakeSource("plc-new") with { Enabled = false };

        var result = WizardConfigMerger.BuildNewSourceDraft(current, newSource, RouteWiring.None);

        result.Routes.Should().BeEquivalentTo(new[] { existingRoute });
        // And the new source landed as DISABLED — verify so future
        // wizard regressions can't silently flip it back on.
        result.Sources.Should().Contain(s =>
            s.InstanceId == "plc-new" && s.Enabled == false);
    }

    [Fact]
    public void NotWired_EnabledSource_Throws()
    {
        // The invariant the gateway-startup validator enforces: an
        // enabled source MUST have an enabled route referencing it. The
        // merger refuses to construct a draft that would crash the
        // gateway on next restart.
        var current = MakeConfig();
        var newSource = MakeSource("plc-new");  // MakeSource defaults Enabled=true

        var act = () => WizardConfigMerger.BuildNewSourceDraft(current, newSource, RouteWiring.None);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*enabled*no route*");
    }

    [Fact]
    public void NewRoute_AppendsRouteLinkedToNewSource()
    {
        var current = MakeConfig(sinks: new[] { MakeSink("opcua-demo") });
        var newSource = MakeSource("plc-line-7");
        var wiring = new RouteWiring.NewRoute(
            RouteId: "route-plc-line-7",
            Name: "Line 7 to OPC UA",
            Buffer: new BufferPolicyConfig { Mode = BufferMode.StoreAndForward, MaxDepth = 10_000 },
            SinkInstanceIds: new[] { "opcua-demo" });

        var result = WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);

        result.Routes.Should().HaveCount(1);
        var route = result.Routes[0];
        route.RouteId.Should().Be("route-plc-line-7");
        route.Name.Should().Be("Line 7 to OPC UA");
        route.SourceInstanceId.Should().Be("plc-line-7");
        route.SinkInstanceIds.Should().BeEquivalentTo(new[] { "opcua-demo" });
        route.Buffer.Mode.Should().Be(BufferMode.StoreAndForward);
        route.Buffer.MaxDepth.Should().Be(10_000);
    }

    [Fact]
    public void NewRoute_PreservesExistingRoutes()
    {
        var existingRoute = MakeRoute("r-existing", sourceId: "plc-existing");
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-existing") },
            routes: new[] { existingRoute });
        var newSource = MakeSource("plc-new");
        var wiring = new RouteWiring.NewRoute(
            "route-new", "New route",
            new BufferPolicyConfig { MaxDepth = 5_000 },
            new[] { "opcua-demo" });

        var result = WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);

        result.Routes.Should().HaveCount(2);
        result.Routes.Select(r => r.RouteId).Should().ContainInOrder("r-existing", "route-new");
    }

    [Fact]
    public void NewRoute_DuplicateRouteId_Throws()
    {
        var current = MakeConfig(routes: new[] { MakeRoute("route-taken", "plc-other") });
        var newSource = MakeSource("plc-new");
        var wiring = new RouteWiring.NewRoute(
            "route-taken", "Conflicting name",
            new BufferPolicyConfig(),
            new[] { "opcua-demo" });

        var act = () => WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*route-taken*already exists*");
    }

    [Fact]
    public void NewRoute_NoSinks_Throws()
    {
        // The wizard UI blocks this, but the merger enforces it too — defence in depth.
        var current = MakeConfig();
        var newSource = MakeSource("plc-new");
        var wiring = new RouteWiring.NewRoute(
            "route-new", "no sinks", new BufferPolicyConfig(), Array.Empty<string>());

        var act = () => WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least one sink*");
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var current = MakeConfig();
        var source = MakeSource("plc-x");

        ((Action)(() => WizardConfigMerger.BuildNewSourceDraft(null!, source, RouteWiring.None)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildNewSourceDraft(current, null!, RouteWiring.None)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildNewSourceDraft(current, source, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CurrentConfig_IsNotMutated()
    {
        // Purity check — the merger uses record-with to produce the new
        // config, and the inputs must remain referentially unchanged.
        var originalSource = MakeSource("plc-existing");
        var originalRoute = MakeRoute("r-existing", "plc-existing");
        var current = MakeConfig(
            sources: new[] { originalSource },
            routes: new[] { originalRoute });
        var beforeSourceCount = current.Sources.Count;
        var beforeRouteCount = current.Routes.Count;

        var newSource = MakeSource("plc-new");
        var wiring = new RouteWiring.NewRoute(
            "route-new", "n", new BufferPolicyConfig(), new[] { "opcua-demo" });

        _ = WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);

        current.Sources.Should().HaveCount(beforeSourceCount,
            "the merger must not mutate the input — Sources list size unchanged");
        current.Routes.Should().HaveCount(beforeRouteCount,
            "the merger must not mutate the input — Routes list size unchanged");
        current.Sources.Should().Contain(originalSource);
        current.Routes.Should().Contain(originalRoute);
    }

    [Fact]
    public void ResultIsValidGatewayConfiguration_ForRoundTrip()
    {
        // Wire-shape guard: the produced GatewayConfiguration JSON-
        // serialises and round-trips cleanly. Future wizard regressions
        // that produce non-serialisable shapes (e.g. cycles, unsupported
        // types) fail this test.
        var current = MakeConfig(sinks: new[] { MakeSink("opcua-demo") });
        var newSource = MakeSource("plc-new");
        var wiring = new RouteWiring.NewRoute(
            "route-new", "n",
            new BufferPolicyConfig { Mode = BufferMode.InMemory, MaxDepth = 1_000 },
            new[] { "opcua-demo" });

        var result = WizardConfigMerger.BuildNewSourceDraft(current, newSource, wiring);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("plc-new").And.Contain("route-new").And.Contain("opcua-demo");
    }

    // ───── Helpers ──────────────────────────────────────────────────────

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
        ProtocolName = "opcua-server",
        Enabled = true,
    };

    private static RouteConfig MakeRoute(string routeId, string sourceId) => new()
    {
        RouteId = routeId,
        Name = routeId,
        SourceInstanceId = sourceId,
        SinkInstanceIds = new[] { "opcua-demo" },
    };

    // ═══ BuildNewRouteDraft (M.2b.5) ═════════════════════════════════════
    //
    // The route-wizard merger overload appends a fully-formed RouteConfig
    // to the current configuration. Cross-record integrity (source exists
    // and is enabled, every sink exists, route-id unique) is enforced
    // eagerly here for defence-in-depth — the management API's
    // CrossRecordValidator enforces the same set lazily at draft-create
    // time, but the merger refuses to construct an invalid draft in the
    // first place.

    [Fact]
    public void BuildNewRouteDraft_HappyPath_AppendsRoute()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("mqtt-eremos"), MakeSink("opcua-demo") });
        var newRoute = new RouteConfig
        {
            RouteId = "line7-to-eremos",
            Name = "Line 7 to EREMOS",
            SourceInstanceId = "plc-line-7",
            SinkInstanceIds = new[] { "mqtt-eremos", "opcua-demo" },
        };

        var result = WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        result.Routes.Should().HaveCount(1);
        result.Routes[0].RouteId.Should().Be("line7-to-eremos");
        result.Sources.Should().BeEquivalentTo(current.Sources, "sources must remain untouched");
        result.Sinks.Should().BeEquivalentTo(current.Sinks, "sinks must remain untouched");
    }

    [Fact]
    public void BuildNewRouteDraft_DupRouteId_Rejected()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("opcua-demo") },
            routes: new[] { MakeRoute("route-taken", "plc-line-7") });
        var newRoute = new RouteConfig
        {
            RouteId = "route-taken",
            Name = "Conflicting",
            SourceInstanceId = "plc-line-7",
            SinkInstanceIds = new[] { "opcua-demo" },
        };

        var act = () => WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*route-taken*already exists*");
    }

    [Fact]
    public void BuildNewRouteDraft_UnknownSourceId_Rejected()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("opcua-demo") });
        var newRoute = new RouteConfig
        {
            RouteId = "r-new",
            Name = "Refers to ghost source",
            SourceInstanceId = "plc-ghost",
            SinkInstanceIds = new[] { "opcua-demo" },
        };

        var act = () => WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*plc-ghost*does not exist*");
    }

    [Fact]
    public void BuildNewRouteDraft_UnknownSinkId_Rejected_EvenIfOneSinkExists()
    {
        // Fanout integrity: even if some sinks resolve, a single phantom
        // sink id invalidates the whole route. Defends against typos in a
        // multi-sink wizard step that would otherwise produce a route that
        // silently drops a fanout target.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("mqtt-eremos") });
        var newRoute = new RouteConfig
        {
            RouteId = "r-new",
            Name = "Has one phantom sink",
            SourceInstanceId = "plc-line-7",
            SinkInstanceIds = new[] { "mqtt-eremos", "mqtt-ghost" },
        };

        var act = () => WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*mqtt-ghost*does not exist*");
    }

    [Fact]
    public void BuildNewRouteDraft_SourceDisabled_Rejected()
    {
        // Matches Core's startup invariant — an enabled route that points
        // at a disabled source fails registration. We surface it at the
        // merger so the draft never reaches the runtime.
        var disabledSource = MakeSource("plc-line-7") with { Enabled = false };
        var current = MakeConfig(
            sources: new[] { disabledSource },
            sinks: new[] { MakeSink("opcua-demo") });
        var newRoute = new RouteConfig
        {
            RouteId = "r-new",
            Name = "Enabled route over disabled source",
            SourceInstanceId = "plc-line-7",
            SinkInstanceIds = new[] { "opcua-demo" },
            Enabled = true,
        };

        var act = () => WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*disabled*");
    }

    [Fact]
    public void BuildNewRouteDraft_NullArguments_Throw()
    {
        var current = MakeConfig();
        var route = MakeRoute("r-x", "src-x");

        ((Action)(() => WizardConfigMerger.BuildNewRouteDraft(null!, route)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildNewRouteDraft(current, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildNewRouteDraft_PreservesExistingRoutes()
    {
        var existingRoute = MakeRoute("r-existing", "plc-existing");
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-existing"), MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("opcua-demo") },
            routes: new[] { existingRoute });
        var newRoute = new RouteConfig
        {
            RouteId = "r-new",
            Name = "New",
            SourceInstanceId = "plc-line-7",
            SinkInstanceIds = new[] { "opcua-demo" },
        };

        var result = WizardConfigMerger.BuildNewRouteDraft(current, newRoute);

        result.Routes.Should().HaveCount(2);
        result.Routes.Select(r => r.RouteId).Should().ContainInOrder("r-existing", "r-new");
    }

    // ═══ BuildNewSinkDraft (M.2b.6) ══════════════════════════════════════
    //
    // Symmetric with BuildNewSourceDraft (Locked G). NotWired forces the
    // new sink to be created with Enabled = false (defence in depth around
    // Core's startup validator); NewRoute appends a route that pulls from
    // an existing source and MUST include the new sink in its fanout.

    [Fact]
    public void BuildNewSinkDraft_NotWired_DisabledSink_AppendsSinkLeavesRoutes()
    {
        var existingRoute = MakeRoute("r-existing", sourceId: "plc-existing");
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-existing") },
            routes: new[] { existingRoute });
        var newSink = MakeSink("mqtt-new") with { Enabled = false };

        var result = WizardConfigMerger.BuildNewSinkDraft(current, newSink, RouteWiring.None);

        result.Sinks.Should().HaveCount(1);
        result.Sinks[0].InstanceId.Should().Be("mqtt-new");
        result.Sinks[0].Enabled.Should().BeFalse();
        result.Routes.Should().BeEquivalentTo(new[] { existingRoute });
    }

    [Fact]
    public void BuildNewSinkDraft_NotWired_EnabledSink_Throws()
    {
        var current = MakeConfig();
        var newSink = MakeSink("mqtt-new");  // Enabled = true by default

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, RouteWiring.None);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*enabled*no route*");
    }

    [Fact]
    public void BuildNewSinkDraft_DuplicateInstanceId_Throws()
    {
        var current = MakeConfig(sinks: new[] { MakeSink("mqtt-eremos") });
        var newSink = MakeSink("mqtt-eremos") with { Enabled = false };

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, RouteWiring.None);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*mqtt-eremos*already exists*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_AppendsRouteWithNewSinkInFanout()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-line-7") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            RouteId: "route-line7-to-mqtt",
            Name: "Line 7 to MQTT",
            Buffer: new BufferPolicyConfig(),
            SinkInstanceIds: new[] { "mqtt-new" },
            SourceInstanceId: "plc-line-7");

        var result = WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);

        result.Sinks.Should().HaveCount(1);
        result.Routes.Should().HaveCount(1);
        result.Routes[0].RouteId.Should().Be("route-line7-to-mqtt");
        result.Routes[0].SourceInstanceId.Should().Be("plc-line-7");
        result.Routes[0].SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-new" });
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_WithoutSourceInstanceId_Throws()
    {
        // Sink-wizard semantics require an explicit existing source — the
        // RouteWiring.NewRoute.SourceInstanceId field carries it. Null is
        // a misuse by the caller; the merger catches it.
        var current = MakeConfig(sources: new[] { MakeSource("plc-line-7") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            RouteId: "route-x", Name: "x",
            Buffer: new BufferPolicyConfig(),
            SinkInstanceIds: new[] { "mqtt-new" });
        // SourceInstanceId omitted — defaults to null.

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SourceInstanceId*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_PhantomSource_Throws()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-real") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-x", "x", new BufferPolicyConfig(),
            new[] { "mqtt-new" },
            SourceInstanceId: "plc-ghost");

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*plc-ghost*does not exist*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_DisabledSource_Throws()
    {
        var disabledSource = MakeSource("plc-disabled") with { Enabled = false };
        var current = MakeConfig(sources: new[] { disabledSource });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-x", "x", new BufferPolicyConfig(),
            new[] { "mqtt-new" },
            SourceInstanceId: "plc-disabled");

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*disabled*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_FanoutMissingNewSink_Throws()
    {
        // The new sink MUST appear in the route's fanout — otherwise the
        // sink we just added would be orphaned. Wizard UI enforces it by
        // pre-checking the new sink's row; merger enforces it for defence.
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("mqtt-existing") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-x", "x", new BufferPolicyConfig(),
            new[] { "mqtt-existing" },  // <-- missing "mqtt-new"
            SourceInstanceId: "plc-line-7");

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*does not include the new sink*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_DupRouteId_Throws()
    {
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            routes: new[] { MakeRoute("route-taken", "plc-line-7") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-taken", "Conflicting",
            new BufferPolicyConfig(),
            new[] { "mqtt-new" },
            SourceInstanceId: "plc-line-7");

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*route-taken*already exists*");
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_FanoutWithExistingAndNewSink_Roundtrips()
    {
        // Multi-sink fanout including both the new sink and an existing
        // one — the merger must accept it (a route can publish to multiple
        // sinks per blueprint §3).
        var current = MakeConfig(
            sources: new[] { MakeSource("plc-line-7") },
            sinks: new[] { MakeSink("mqtt-existing") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-multi", "Multi-sink",
            new BufferPolicyConfig(),
            new[] { "mqtt-existing", "mqtt-new" },
            SourceInstanceId: "plc-line-7");

        var result = WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);

        result.Sinks.Should().HaveCount(2);
        result.Routes.Should().HaveCount(1);
        result.Routes[0].SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-existing", "mqtt-new" });
    }

    [Fact]
    public void BuildNewSinkDraft_NewRoute_PhantomExistingSink_Throws()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-line-7") });
        var newSink = MakeSink("mqtt-new");
        var wiring = new RouteWiring.NewRoute(
            "route-x", "x", new BufferPolicyConfig(),
            new[] { "mqtt-new", "mqtt-ghost" },
            SourceInstanceId: "plc-line-7");

        var act = () => WizardConfigMerger.BuildNewSinkDraft(current, newSink, wiring);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*mqtt-ghost*does not exist*");
    }

    [Fact]
    public void BuildNewSinkDraft_NullArguments_Throw()
    {
        var current = MakeConfig();
        var sink = MakeSink("mqtt-x") with { Enabled = false };

        ((Action)(() => WizardConfigMerger.BuildNewSinkDraft(null!, sink, RouteWiring.None)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildNewSinkDraft(current, null!, RouteWiring.None)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildNewSinkDraft(current, sink, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ═══ BuildUpdatedSourceDraft (M.2d.2 §5.5) ════════════════════════════
    //
    // Edit-mode source replacement. Replaces the source body in place;
    // routes and sinks pass through byte-identical (the locked invariant
    // from §5.5 — Edit mode NEVER modifies routes, even when the source
    // is disabled or its connection changes). Optimistic-concurrency is
    // enforced at the API layer; the merger itself is pure.

    [Fact]
    public void BuildUpdatedSourceDraft_ReplacesMatchingSource_PreservesOthers()
    {
        var existing = MakeSource("plc-line-7");
        var sibling = MakeSource("plc-line-8");
        var current = MakeConfig(sources: new[] { existing, sibling });

        // Edit just the DeviceName — the operator-friendly label is
        // mutable per §5.4.
        var updated = existing with { DeviceName = "Renamed PLC 7" };

        var result = WizardConfigMerger.BuildUpdatedSourceDraft(current, updated);

        result.Sources.Should().HaveCount(2);
        result.Sources[0].DeviceName.Should().Be("Renamed PLC 7");
        result.Sources[1].Should().BeSameAs(sibling, "untouched sources must pass through unchanged");
    }

    [Fact]
    public void BuildUpdatedSourceDraft_PreservesRoutesByteIdentical()
    {
        // §5.5 locked invariant: Edit mode never modifies routes. Even if
        // the source is renamed, disabled, or its connection changes, the
        // route array must be the same reference (or at minimum
        // byte-identical) post-merge.
        var existing = MakeSource("plc-line-7");
        var route = MakeRoute("route-1", "plc-line-7");
        var current = MakeConfig(
            sources: new[] { existing },
            sinks: new[] { MakeSink("opcua-demo") },
            routes: new[] { route });

        var updated = existing with { DeviceName = "New name", DeviceClass = "daq" };

        var result = WizardConfigMerger.BuildUpdatedSourceDraft(current, updated);

        // Reference equality: the merger passes the same Routes list through.
        result.Routes.Should().BeSameAs(current.Routes, "Edit mode never modifies routes");
        result.Sinks.Should().BeSameAs(current.Sinks, "Edit mode never modifies sinks");
    }

    [Fact]
    public void BuildUpdatedSourceDraft_PreservesRoutes_EvenWhenSourceDisabled()
    {
        // Subtle case: disabling a source via Edit must NOT delete routes
        // referencing it. The Route wizard owns route lifecycle exclusively.
        var existing = MakeSource("plc-line-7");
        var route = MakeRoute("route-1", "plc-line-7");
        var current = MakeConfig(
            sources: new[] { existing },
            sinks: new[] { MakeSink("opcua-demo") },
            routes: new[] { route });

        var updated = existing with { Enabled = false };

        var result = WizardConfigMerger.BuildUpdatedSourceDraft(current, updated);

        result.Routes.Should().BeSameAs(current.Routes);
        result.Sources[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void BuildUpdatedSourceDraft_MissingInstanceId_Throws()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-existing") });
        var orphan = MakeSource("plc-not-in-config");

        var act = () => WizardConfigMerger.BuildUpdatedSourceDraft(current, orphan);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*plc-not-in-config*no source with that instance id exists*");
    }

    [Fact]
    public void BuildUpdatedSourceDraft_ChangedProtocolName_Throws()
    {
        // §5.4 mutability table: ProtocolName immutable in Edit. Switching
        // protocols requires delete + re-add, not Edit.
        var existing = MakeSource("plc-line-7"); // protocol = modbustcp from helper
        var current = MakeConfig(sources: new[] { existing });

        var updated = existing with { ProtocolName = "focas2" };

        var act = () => WizardConfigMerger.BuildUpdatedSourceDraft(current, updated);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot change ProtocolName*plc-line-7*modbustcp*focas2*");
    }

    [Fact]
    public void BuildUpdatedSourceDraft_NullArguments_Throw()
    {
        var current = MakeConfig(sources: new[] { MakeSource("plc-x") });
        var updated = MakeSource("plc-x");

        ((Action)(() => WizardConfigMerger.BuildUpdatedSourceDraft(null!, updated)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => WizardConfigMerger.BuildUpdatedSourceDraft(current, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildUpdatedSourceDraft_PreservesSourceOrdering()
    {
        // The merger replaces at the same index — sort order must be
        // stable so config diffs in the audit log don't reorder
        // unrelated entries.
        var a = MakeSource("a-first");
        var b = MakeSource("b-middle");
        var c = MakeSource("c-last");
        var current = MakeConfig(sources: new[] { a, b, c });

        var updatedB = b with { DeviceName = "B prime" };

        var result = WizardConfigMerger.BuildUpdatedSourceDraft(current, updatedB);

        result.Sources.Select(s => s.InstanceId).Should().ContainInOrder("a-first", "b-middle", "c-last");
        result.Sources[1].DeviceName.Should().Be("B prime");
    }
}
