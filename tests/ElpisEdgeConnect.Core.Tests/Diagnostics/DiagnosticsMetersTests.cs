// ============================================================================
// File: Diagnostics/DiagnosticsMetersTests.cs
// Purpose: Phase 5 pin tests for DiagnosticsMeters. Uses MeterListener
//          directly — no OpenTelemetry dependency — to verify instrument
//          names, tag keys, and that every instrument reflects collector
//          state without a parallel counter store.
// Milestone: C4 — phase 5.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class DiagnosticsMetersTests
{
    /// <summary>
    /// Record all long-valued measurements from a given Meter by triggering
    /// a synchronous observable-instrument collection via MeterListener.
    /// </summary>
    private static List<MeasurementRecord<long>> RecordLongMeasurements(Meter meter)
    {
        var records = new List<MeasurementRecord<long>>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagDict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags)
            {
                tagDict[t.Key] = t.Value;
            }
            records.Add(new MeasurementRecord<long>(instrument.Name, value, tagDict));
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return records;
    }

    private static List<MeasurementRecord<int>> RecordIntMeasurements(Meter meter)
    {
        var records = new List<MeasurementRecord<int>>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter == meter)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
        {
            var tagDict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags)
            {
                tagDict[t.Key] = t.Value;
            }
            records.Add(new MeasurementRecord<int>(instrument.Name, value, tagDict));
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return records;
    }

    private sealed record MeasurementRecord<T>(
        string InstrumentName,
        T Value,
        Dictionary<string, object?> Tags);

    [Fact]
    public void MeterName_IsLocked()
    {
        var collector = new RuntimeDiagnosticsCollector();
        using var meters = new DiagnosticsMeters(collector);
        meters.Meter.Name.Should().Be("Elpis.EdgeConnect.Core");
        meters.Meter.Name.Should().Be(DiagnosticsConstants.MeterName);
    }

    [Fact]
    public void InstrumentNames_AreLocked()
    {
        var collector = new RuntimeDiagnosticsCollector();
        // Seed every dimension so every instrument emits at least one measurement.
        collector.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Configured,
            To = RouteState.Running,
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 1,
            Reason = "x",
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.OnSinkDegraded(new SinkDegradedEvent
        {
            RouteId = "r1",
            SinkInstanceId = "s1",
            LastError = new AdapterError { Code = "X.Y", Category = ErrorCategory.Network, Message = "fail" },
            RetryAttempts = 1,
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.RecordStep("r1", "filter", 10, 8, 2, TimeSpan.FromMilliseconds(1));
        collector.RecordSourceObservation("r1", "src-1", "mock", 10, DateTime.UtcNow);

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordLongMeasurements(meter);

        var names = records.Select(r => r.InstrumentName).Distinct().OrderBy(n => n).ToArray();
        names.Should().Contain(new[]
        {
            DiagnosticsMeters.RouteStateTransitionsInstrument,
            DiagnosticsMeters.RouteBackpressureDropsInstrument,
            DiagnosticsMeters.SinkDegradationEventsInstrument,
            DiagnosticsMeters.SinkRecoveryEventsInstrument,
            DiagnosticsMeters.PipelineStepInvocationsInstrument,
            DiagnosticsMeters.PipelineStepPointsInInstrument,
            DiagnosticsMeters.PipelineStepPointsOutInstrument,
            DiagnosticsMeters.SourcePointsObservedInstrument,
        });

        // Every instrument name must use the locked prefix.
        foreach (var name in names)
        {
            name.Should().StartWith(DiagnosticsConstants.InstrumentPrefix);
        }
    }

    [Fact]
    public void Metrics_Tags_AreStable()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnSinkDegraded(new SinkDegradedEvent
        {
            RouteId = "r1",
            SinkInstanceId = "s1",
            LastError = new AdapterError { Code = "X.Y", Category = ErrorCategory.Network, Message = "fail" },
            RetryAttempts = 1,
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.RecordStep("r1", "filter", 10, 10, 0, TimeSpan.Zero);
        collector.RecordSourceObservation("r1", "src-1", "mock", 5, DateTime.UtcNow);

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordLongMeasurements(meter);

        var sinkRecord = records.Single(r => r.InstrumentName == DiagnosticsMeters.SinkDegradationEventsInstrument);
        sinkRecord.Tags.Keys.Should().BeEquivalentTo(new[] { "route_id", "sink_id" });
        sinkRecord.Tags["route_id"].Should().Be("r1");
        sinkRecord.Tags["sink_id"].Should().Be("s1");

        var stepRecord = records.Single(r => r.InstrumentName == DiagnosticsMeters.PipelineStepInvocationsInstrument);
        stepRecord.Tags.Keys.Should().BeEquivalentTo(new[] { "route_id", "step_kind" });
        stepRecord.Tags["step_kind"].Should().Be("filter");

        var sourceRecord = records.Single(r => r.InstrumentName == DiagnosticsMeters.SourcePointsObservedInstrument);
        sourceRecord.Tags.Keys.Should().BeEquivalentTo(new[] { "route_id", "source_id" });
        sourceRecord.Tags["source_id"].Should().Be("src-1");
    }

    [Fact]
    public void BackpressureDropMetric_ReflectsCollectorCount()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 10,
            Reason = "x",
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 15,
            Reason = "x",
            ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordLongMeasurements(meter);

        var bp = records.Single(r =>
            r.InstrumentName == DiagnosticsMeters.RouteBackpressureDropsInstrument
            && r.Tags["route_id"] as string == "r1");
        bp.Value.Should().Be(25);
    }

    [Fact]
    public void RouteStateTransitionMetric_ReflectsCollectorCount()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Configured,
            To = RouteState.Starting,
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Starting,
            To = RouteState.Running,
            ObservedAtUtc = DateTime.UtcNow,
        });
        collector.OnRouteStateChanged(new RouteStateChangedEvent
        {
            RouteId = "r1",
            From = RouteState.Running,
            To = RouteState.Degraded,
            ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);

        var longs = RecordLongMeasurements(meter);
        var transitions = longs.Single(r =>
            r.InstrumentName == DiagnosticsMeters.RouteStateTransitionsInstrument);
        transitions.Value.Should().Be(3);

        var ints = RecordIntMeasurements(meter);
        var stateGauge = ints.Single(r =>
            r.InstrumentName == DiagnosticsMeters.RouteStateGaugeInstrument);
        stateGauge.Value.Should().Be((int)RouteState.Degraded);
    }

    [Fact]
    public void PipelineStepMetric_ReflectsPerStepCounters()
    {
        var collector = new RuntimeDiagnosticsCollector();
        collector.RecordStep("r1", "filter", 100, 80, 20, TimeSpan.FromMilliseconds(1));
        collector.RecordStep("r1", "filter", 50, 40, 10, TimeSpan.FromMilliseconds(1));
        collector.RecordStep("r1", "rename", 40, 40, 0, TimeSpan.FromMilliseconds(1));

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);
        var records = RecordLongMeasurements(meter);

        var filterInvocations = records.Single(r =>
            r.InstrumentName == DiagnosticsMeters.PipelineStepInvocationsInstrument
            && (string)r.Tags["step_kind"]! == "filter");
        filterInvocations.Value.Should().Be(2);

        var filterIn = records.Single(r =>
            r.InstrumentName == DiagnosticsMeters.PipelineStepPointsInInstrument
            && (string)r.Tags["step_kind"]! == "filter");
        filterIn.Value.Should().Be(150);

        var filterOut = records.Single(r =>
            r.InstrumentName == DiagnosticsMeters.PipelineStepPointsOutInstrument
            && (string)r.Tags["step_kind"]! == "filter");
        filterOut.Value.Should().Be(120);

        var renameInvocations = records.Single(r =>
            r.InstrumentName == DiagnosticsMeters.PipelineStepInvocationsInstrument
            && (string)r.Tags["step_kind"]! == "rename");
        renameInvocations.Value.Should().Be(1);
    }

    [Fact]
    public void MetricsSurface_DoesNotRequireExporter()
    {
        // Guardrail: phase 5 must not require OpenTelemetry or any HTTP
        // exporter to observe metrics. This test uses ONLY the built-in
        // System.Diagnostics.Metrics MeterListener — no external package.
        var collector = new RuntimeDiagnosticsCollector();
        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1",
            DroppedCount = 1,
            Reason = "x",
            ObservedAtUtc = DateTime.UtcNow,
        });

        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);

        var records = RecordLongMeasurements(meter);
        records.Should().NotBeEmpty();

        // Structural pin: DiagnosticsMeters has NO reference to any
        // OpenTelemetry type (checked via assembly metadata).
        var coreAssembly = typeof(DiagnosticsMeters).Assembly;
        var referenced = coreAssembly.GetReferencedAssemblies();
        referenced.Should().NotContain(a => a.Name!.Contains("OpenTelemetry", StringComparison.Ordinal));
        referenced.Should().NotContain(a => a.Name!.Contains("AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void MetricsCallback_ReadsLive_NoParallelCounterStore()
    {
        // The observable instrument callbacks must reflect the CURRENT
        // collector state at the moment of recording, not a cached value.
        // Mutate between two recordings; the second must show the new
        // totals with no staleness.
        var collector = new RuntimeDiagnosticsCollector();
        using var meter = new Meter("test-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(collector, meter);

        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1", DroppedCount = 5, Reason = "x", ObservedAtUtc = DateTime.UtcNow,
        });
        var r1 = RecordLongMeasurements(meter)
            .Single(r => r.InstrumentName == DiagnosticsMeters.RouteBackpressureDropsInstrument);
        r1.Value.Should().Be(5);

        collector.OnBackpressureDropped(new BackpressureDroppedEvent
        {
            RouteId = "r1", DroppedCount = 7, Reason = "x", ObservedAtUtc = DateTime.UtcNow,
        });
        var r2 = RecordLongMeasurements(meter)
            .Single(r => r.InstrumentName == DiagnosticsMeters.RouteBackpressureDropsInstrument);
        r2.Value.Should().Be(12); // 5 + 7, not cached
    }
}
