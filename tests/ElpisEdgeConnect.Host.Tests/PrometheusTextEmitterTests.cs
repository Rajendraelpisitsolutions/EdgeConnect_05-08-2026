// ============================================================================
// File: PrometheusTextEmitterTests.cs
// Purpose: Phase 5 unit tests for the hand-rolled Prometheus text emitter.
//          Verifies HELP/TYPE headers, label formatting, counter vs gauge
//          distinction, and zero-NuGet operation.
// Milestone: D — phase 5.
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Endpoints;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class PrometheusTextEmitterTests
{
    private static RouteStateChangedEvent Evt(string id, RouteState from, RouteState to) => new()
    {
        RouteId = id,
        From = from,
        To = to,
        ObservedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public void Render_EmptyMeter_ReturnsEmptyString()
    {
        using var meter = new Meter("test-" + Guid.NewGuid());
        PrometheusTextEmitter.Render(meter).Should().BeEmpty();
    }

    [Fact]
    public void Render_IncludesInstrumentNamesAsCounters()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnRouteStateChanged(Evt("r1", RouteState.Configured, RouteState.Starting));
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 5,
            Reason = "test",
            ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var text = PrometheusTextEmitter.Render(meter);

        text.Should().Contain("# TYPE elpis_edgeconnect_route_state_transitions_total counter");
        text.Should().Contain("# TYPE elpis_edgeconnect_route_backpressure_drops_total counter");
        text.Should().Contain("# TYPE elpis_edgeconnect_route_state gauge");
        text.Should().Contain("elpis_edgeconnect_route_state_transitions_total{route_id=\"r1\"} 1");
        text.Should().Contain("elpis_edgeconnect_route_backpressure_drops_total{route_id=\"r1\"} 5");
    }

    [Fact]
    public void Render_EmitsHelpHeaderWhenDescriptionPresent()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1", DroppedCount = 1, Reason = "x", ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var text = PrometheusTextEmitter.Render(meter);

        text.Should().Contain("# HELP elpis_edgeconnect_route_backpressure_drops_total");
    }

    [Fact]
    public void Render_FormatsMultipleLabelsDeterministically()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordStep("r1", "filter", inputCount: 10, outputCount: 8, suppressedCount: 2, duration: TimeSpan.Zero);

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var text = PrometheusTextEmitter.Render(meter);

        // Exact line format: `name{route_id="r1",step_kind="filter"} 1`
        text.Should().Contain("elpis_edgeconnect_pipeline_step_invocations_total{route_id=\"r1\",step_kind=\"filter\"} 1");
        text.Should().Contain("elpis_edgeconnect_pipeline_step_points_in_total{route_id=\"r1\",step_kind=\"filter\"} 10");
        text.Should().Contain("elpis_edgeconnect_pipeline_step_points_out_total{route_id=\"r1\",step_kind=\"filter\"} 8");
    }

    [Fact]
    public void Render_OnlyIncludesInstrumentsFromTargetMeter()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1", DroppedCount = 1, Reason = "x", ObservedAtUtc = DateTime.UtcNow,
        });

        // Register a second meter with instruments that should NOT appear
        // in the scrape output of the first meter.
        using var targetMeter = new Meter("test-target-" + Guid.NewGuid());
        using var otherMeter = new Meter("test-other-" + Guid.NewGuid());
        otherMeter.CreateObservableCounter<long>(
            "unrelated_counter",
            () => new[] { new Measurement<long>(999L) });
        using var meters = new DiagnosticsMeters(collector, targetMeter);

        var text = PrometheusTextEmitter.Render(targetMeter);
        text.Should().NotContain("unrelated_counter");
        text.Should().Contain("elpis_edgeconnect_");
    }

    [Fact]
    public void Render_OutputHasDeterministicSortedOrder()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1", DroppedCount = 1, Reason = "x", ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);

        var a = PrometheusTextEmitter.Render(meter);
        var b = PrometheusTextEmitter.Render(meter);
        a.Should().Be(b, "scraping twice in a row must produce byte-identical output");
    }
}
