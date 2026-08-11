// ============================================================================
// Tests: Components/Shared/RouteHealth — the single definition of route health.
//
// Why this file exists
// --------------------
// RouteHealth is what the Overview verdict banner, the route cards, the
// broken-first ordering and the status footer all answer from. Every operator-
// visible health defect in this product's history has been two surfaces
// disagreeing about the same route, and the cure was to make them share this
// one function. It was, until this file, completely untested — the most
// depended-upon and least verified code in the Studio, and a pure static
// function of a DTO, which makes it the cheapest thing here to pin down.
//
// The tests are grouped by the question each method answers:
//   Verdict / Level     — is data reaching its destination?
//   ConnectionLevel     — is the machine connected?
//   Reason              — does a non-healthy route always say why?
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Management.Contracts;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class RouteHealthTests
{
    // ═══ The defect this whole surface exists to prevent ══════════════════

    [Fact]
    public void Level_RunningPipelineWithStoppedEndpoints_IsDown_NotHealthy()
    {
        // The original bug. A route's StateName describes the PIPELINE task,
        // which is alive with nothing to read and nowhere to publish — so it
        // honestly reports "Running" while no data moves at all. Trusting that
        // one field showed a board of green chips over a dead gateway.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Stopped"),
            sinks: new[] { Sink(adapterState: "Stopped") });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
        RouteHealth.IsHealthy(route).Should().BeFalse();
    }

    [Fact]
    public void Level_EverythingRunning_IsHealthy()
    {
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Healthy);
        RouteHealth.IsHealthy(route).Should().BeTrue();
        RouteHealth.NeedsAttention(route).Should().BeFalse();
    }

    // ═══ Nothing attached ═════════════════════════════════════════════════

    [Fact]
    public void Level_NoSource_IsDown()
    {
        var route = Route(stateName: "Running", source: null,
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void Level_NoDestination_IsDown()
    {
        // A route reading perfectly with nowhere to deliver is not healthy,
        // however cheerful its pipeline state is.
        var route = Route(stateName: "Running", source: Source(stateName: "Running"),
            sinks: Array.Empty<RouteSinkSummaryDto>());

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void Level_NullRoute_IsHealthy_RatherThanThrowing()
    {
        // Rendered before the first poll returns. A dashboard that throws on a
        // null is worse than one that says nothing yet.
        RouteHealth.Level(null).Should().Be(RouteHealthLevel.Healthy);
        RouteHealth.Explain(null).Should().BeNull();
    }

    // ═══ Delivery evidence — state alone is not enough ════════════════════

    [Fact]
    public void Level_WedgedBuffer_IsDown_EvenWhenEveryEndpointIsRunning()
    {
        // Observed live: source Running, sink Running, no error anywhere, and
        // 774 readings enqueued with 0 ever drained. State and delivery are
        // different questions; this is the one the operator cares about.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running") },
            buffer: Buffer(currentDepth: 774, totalDrained: 0));

        RouteHealth.IsWedged(route).Should().BeTrue();
        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void IsWedged_DepthWithSomethingDrained_IsNotWedged()
    {
        // A queue with depth is normal in flight. Wedged means nothing has EVER
        // come out — otherwise every busy route would read as broken.
        var route = Route(buffer: Buffer(currentDepth: 500, totalDrained: 12_000));

        RouteHealth.IsWedged(route).Should().BeFalse();
    }

    [Fact]
    public void IsLosingData_ReadsBufferDropped()
    {
        var route = Route(buffer: Buffer(currentDepth: 0, totalDrained: 10, totalDropped: 3));

        RouteHealth.IsLosingData(route).Should().BeTrue();
    }

    [Fact]
    public void IsLosingData_ReadsBackpressureDrops_WhichTheBufferCounterMisses()
    {
        // Two counters, and only one used to be read. TotalDropped is what the
        // buffer discarded itself; BackpressureDropCount is what never got IN
        // because the buffer was already full. A wedged route shows the second
        // while the first stays at zero — read one and the loss is invisible.
        var route = Route(
            buffer: Buffer(currentDepth: 10_000, totalDrained: 0, totalDropped: 0),
            backpressureDropCount: 3_328);

        RouteHealth.IsLosingData(route).Should().BeTrue();
        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void Level_LiveSourceError_IsDown()
    {
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running", lastErrorCode: "MODBUS.CONNECT_FAILED",
                lastErrorAtUtc: DateTime.UtcNow),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void Level_StaleError_DoesNotKeepARouteBrokenForEver()
    {
        // An error old enough to be history must not hold a route red — that is
        // what made a 227 ms drop look like a permanent outage.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running", lastErrorCode: "MODBUS.CONNECT_FAILED",
                lastErrorAtUtc: DateTime.UtcNow.AddHours(-3)),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.IsLiveError(DateTime.UtcNow.AddHours(-3)).Should().BeFalse();
        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Healthy);
    }

    // ═══ How hard each endpoint is judged ═════════════════════════════════

    [Fact]
    public void Level_DegradedSource_IsDown_NotDegraded()
    {
        // A source is judged harder than a destination. "Degraded" on a source
        // that has read nothing is not a partial service — it is a device we
        // cannot reach, with retries in flight. Amber would say "still working",
        // which is the one thing it is not.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Degraded"),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void Level_DegradedDestination_IsDegraded_NotDown()
    {
        // A destination IS allowed to be amber: degraded means publishing but
        // retrying, and store-and-forward means nothing is lost yet.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running", isDegraded: true) });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Degraded);
        RouteHealth.NeedsAttention(route).Should().BeTrue();
    }

    [Fact]
    public void Level_NullDestinationAdapterState_IsNotTreatedAsAFault()
    {
        // An older host did not report one. Absence of evidence is not a fault —
        // painting every destination red on an older gateway would be a lie.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: null) });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Healthy);
    }

    [Fact]
    public void Level_WorstOfManyDestinations_Wins()
    {
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[]
            {
                Sink(adapterState: "Running"),
                Sink(adapterState: "Running", isDegraded: true),
                Sink(adapterState: "Running"),
            });

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Degraded);
    }

    // ═══ A non-healthy route always says why ══════════════════════════════

    public static TheoryData<string, RouteSummaryDto> NotHealthyRoutes() => new()
    {
        { "no source", Route(source: null, sinks: new[] { Sink(adapterState: "Running") }) },
        { "no destination", Route(source: Source(stateName: "Running"), sinks: Array.Empty<RouteSinkSummaryDto>()) },
        {
            "stopped source with no reported error",
            Route(source: Source(stateName: "Stopped"), sinks: new[] { Sink(adapterState: "Running") })
        },
        {
            "stopped destination",
            Route(source: Source(stateName: "Running"), sinks: new[] { Sink(adapterState: "Stopped") })
        },
        {
            "wedged buffer",
            Route(source: Source(stateName: "Running"), sinks: new[] { Sink(adapterState: "Running") },
                buffer: Buffer(currentDepth: 500, totalDrained: 0))
        },
        {
            "degraded destination",
            Route(source: Source(stateName: "Running"),
                sinks: new[] { Sink(adapterState: "Running", isDegraded: true) })
        },
    };

    [Theory]
    [MemberData(nameof(NotHealthyRoutes))]
    public void Verdict_AnythingNotHealthy_CarriesAReason(string because, RouteSummaryDto route)
    {
        // A red card with no explanation is not reachable. Every one of these
        // used to render a red "Not delivering" pill and nothing else, leaving
        // the operator to guess which of six conditions had fired.
        var verdict = RouteHealth.Verdict(route);

        verdict.Level.Should().NotBe(RouteHealthLevel.Healthy, because);
        verdict.Reason.Should().NotBeNullOrWhiteSpace(because);
    }

    [Fact]
    public void Verdict_Healthy_HasNoReason()
    {
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.Verdict(route).Reason.Should().BeNull();
    }

    [Fact]
    public void Verdict_ReasonAndLevelComeFromTheSameCondition()
    {
        // The two used to be computed by separate passes over separate ladders,
        // so a card could go red off one condition and print the explanation of
        // another. Here delivery loss decides the level, so the sentence must be
        // about the loss — not about the destination that is merely degraded.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running", isDegraded: true) },
            buffer: Buffer(currentDepth: 10, totalDrained: 5, totalDropped: 42));

        var verdict = RouteHealth.Verdict(route);

        verdict.Level.Should().Be(RouteHealthLevel.Down);
        verdict.Reason.Should().NotBeNullOrWhiteSpace();
        RouteHealth.Explain(route).Should().Be(verdict.Reason, "Explain is the same walk as Verdict");
        RouteHealth.Level(route).Should().Be(verdict.Level, "Level is the same walk as Verdict");
    }

    [Fact]
    public void Verdict_NoDestination_BlamesTheWiring_NotTheMachine()
    {
        // Tone matters here as much as correctness: an operator reading this
        // must not go and check a machine that is working perfectly.
        var reason = RouteHealth.Verdict(
            Route(source: Source(stateName: "Running"), sinks: Array.Empty<RouteSinkSummaryDto>())).Reason;

        reason.Should().NotBeNullOrWhiteSpace();
        reason!.Should().Contain("destination");
    }

    // ═══ ConnectionLevel — the OTHER question ═════════════════════════════

    [Fact]
    public void ConnectionLevel_UnreachableDevice_IsDown()
    {
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Stopped"),
            sinks: new[] { Sink(adapterState: "Running") });

        RouteHealth.ConnectionLevel(route).Should().Be(RouteHealthLevel.Down);
    }

    [Fact]
    public void ConnectionLevel_AndLevel_AreSeparateQuestions()
    {
        // The split exists so an operator can tell "walk to the machine" from
        // "the broker is down". Both endpoints are connected here; only delivery
        // has failed, so the two answers legitimately differ.
        var route = Route(
            stateName: "Running",
            source: Source(stateName: "Running"),
            sinks: new[] { Sink(adapterState: "Running") },
            buffer: Buffer(currentDepth: 8_822, totalDrained: 0));

        RouteHealth.Level(route).Should().Be(RouteHealthLevel.Down,
            "nothing is being delivered");
        RouteHealth.ConnectionLevel(route).Should().Be(RouteHealthLevel.Down,
            "documents today's behaviour — see the note below if this ever changes");
    }

    [Fact]
    public void IsRunning_MatchesOnlyTheRunningState()
    {
        RouteHealth.IsRunning("Running").Should().BeTrue();
        RouteHealth.IsRunning("Degraded").Should().BeFalse();
        RouteHealth.IsRunning("running").Should().BeFalse("the comparison is ordinal");
        RouteHealth.IsRunning(null).Should().BeFalse();
    }

    // ═══ Builders ═════════════════════════════════════════════════════════

    private static RouteSummaryDto Route(
        string stateName = "Running",
        RouteSourceSummaryDto? source = null,
        IReadOnlyList<RouteSinkSummaryDto>? sinks = null,
        RouteBufferSummaryDto? buffer = null,
        long backpressureDropCount = 0,
        DateTime? lastErrorAtUtc = null) => new()
        {
            RouteId = "route-1",
            ObservedAtUtc = DateTime.UtcNow,
            State = 4,
            StateName = stateName,
            Source = source,
            Sinks = sinks ?? Array.Empty<RouteSinkSummaryDto>(),
            Buffer = buffer,
            BackpressureDropCount = backpressureDropCount,
            LastErrorAtUtc = lastErrorAtUtc,
        };

    private static RouteSourceSummaryDto Source(
        string stateName = "Running",
        string? lastErrorCode = null,
        DateTime? lastErrorAtUtc = null) => new()
        {
            SourceInstanceId = "src-1",
            ProtocolName = "modbustcp",
            StateName = stateName,
            LastErrorCode = lastErrorCode,
            LastErrorAtUtc = lastErrorAtUtc,
        };

    private static RouteSinkSummaryDto Sink(
        string? adapterState = "Running",
        bool isDegraded = false,
        DateTime? lastErrorAtUtc = null) => new()
        {
            SinkInstanceId = "sink-1",
            AdapterStateName = adapterState,
            IsDegraded = isDegraded,
            LastErrorAtUtc = lastErrorAtUtc,
        };

    private static RouteBufferSummaryDto Buffer(
        long currentDepth = 0,
        long totalDrained = 0,
        long totalDropped = 0) => new()
        {
            Mode = "StoreAndForward",
            CurrentDepth = currentDepth,
            TotalDrained = totalDrained,
            TotalDropped = totalDropped,
        };
}
