// ============================================================================
// File: Diagnostics/DiagnosticsEndToEndTests.cs
// Purpose: Phase 6 gate for C4. Composes all prior C4 pieces together with
//          a real RoutingEngine and verifies that IDiagnosticsService and
//          DiagnosticsMeters agree on the same single-source-of-truth
//          collector state under live event flow.
//
// Gate, not feature phase: if either test fires, fix the underlying bug
// and do not grow new abstractions here.
//
// Milestone: C4 — phase 6.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Core.Tests.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class DiagnosticsEndToEndTests
{
    private sealed record MeasurementRecord(
        string InstrumentName,
        long Value,
        Dictionary<string, object?> Tags);

    private static List<MeasurementRecord> RecordLong(Meter meter)
    {
        var records = new List<MeasurementRecord>();
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
            records.Add(new MeasurementRecord(instrument.Name, value, tagDict));
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return records;
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException("Predicate did not become true within the timeout.");
    }

    /// <summary>
    /// The main C4 gate. Drive a real routing engine through a full
    /// outage-and-recovery cycle with the collector wired in, plus a
    /// direct source-health push and a direct pipeline step recording,
    /// then verify that IDiagnosticsService and DiagnosticsMeters agree
    /// on every counter.
    /// </summary>
    [Fact]
    public async Task Diagnostics_EndToEnd_RouteOutageRecovery_SnapshotAndMetricsAgree()
    {
        var collector = new RuntimeDiagnosticsCollector();
        IDiagnosticsService service = collector;
        using var meter = new Meter("c4-gate-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(service, meter);

        var source = new FakeSourceIntake("src-1");
        var sink = new FakeSinkAdapter("sink-1");

        await using var engine = new RoutingEngine(new InMemoryRouteBufferFactory(), collector);
        await engine.RegisterRouteAsync(new RouteDefinition
        {
            RouteId = "route-1",
            GatewayId = RoutingTestData.GatewayId,
            Source = source,
            Sinks = new ISinkAdapter[] { sink },
            Filter = TagFilter.AcceptAll,
            BufferPolicy = RoutingTestData.DefaultBufferPolicy(),
            Delivery = new DeliveryPolicyConfig
            {
                Mode = DeliveryMode.AtLeastOnce,
                MaxRetries = 10_000,
                InitialBackoffMs = 1,
                MaxBackoffMs = 5,
                BackoffMultiplier = 2.0,
                JitterPercent = 0,
            },
        }, CancellationToken.None);
        await engine.StartRouteAsync("route-1", CancellationToken.None);

        // Pre-register the source so we can push adapter state + points.
        // The host supervisor will do this in D1; here we simulate it.
        collector.RecordSourceState(
            "route-1", "src-1", "mock", AdapterState.Running, lastError: null);

        // Healthy window: 50 points.
        for (var i = 0; i < 50; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
            collector.RecordSourceObservation("route-1", "src-1", "mock", 1, DateTime.UtcNow);
        }

        // Induce outage.
        sink.FailPermanently = true;
        for (var i = 50; i < 100; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
            collector.RecordSourceObservation("route-1", "src-1", "mock", 1, DateTime.UtcNow);
        }
        await WaitForAsync(
            () => service.GetRouteSnapshot("route-1")?.Sinks.Any(s => s.IsDegraded) == true,
            TimeSpan.FromSeconds(5));

        // Heal and drain.
        sink.FailPermanently = false;
        for (var i = 100; i < 150; i++)
        {
            await source.WriteAsync(RoutingTestData.MakePoint(i));
            collector.RecordSourceObservation("route-1", "src-1", "mock", 1, DateTime.UtcNow);
        }
        source.Complete();
        await WaitForAsync(() => sink.PublishedCount == 150, TimeSpan.FromSeconds(10));

        // Simulate two pipeline steps worth of activity (we don't run a
        // real pipeline, but the collector records steps the same way
        // RouteWorker will when D1 wires it up).
        collector.RecordStep("route-1", "filter", 150, 150, 0, TimeSpan.FromMilliseconds(3));
        collector.RecordStep("route-1", "rename", 150, 150, 0, TimeSpan.FromMilliseconds(1));

        await engine.StopRouteAsync("route-1", CancellationToken.None);

        // ---- Assertions ----

        // Service snapshot: the collector's own query API.
        var snap = service.GetRouteSnapshot("route-1")!;
        snap.State.Should().Be(RouteState.Stopped);
        snap.StateTransitionCount.Should().BeGreaterThanOrEqualTo(6);
        snap.Sinks.Should().HaveCount(1);
        snap.Sinks[0].DegradationEventCount.Should().BeGreaterThanOrEqualTo(1);
        snap.Sinks[0].RecoveryEventCount.Should().BeGreaterThanOrEqualTo(1);
        snap.Source.Should().NotBeNull();
        snap.Source!.PointsObserved.Should().Be(150);
        snap.Pipeline.Steps.Should().HaveCount(2);
        var filterStep = snap.Pipeline.Steps.Single(s => s.StepKind == "filter");
        filterStep.Invocations.Should().Be(1);
        filterStep.PointsIn.Should().Be(150);

        // Metrics surface: must agree with the snapshot, because both read
        // from the same collector state store.
        var metrics = RecordLong(meter);

        long FindMetric(string name, params (string Key, string Value)[] tags)
        {
            var matches = metrics.Where(m => m.InstrumentName == name);
            foreach (var (key, value) in tags)
            {
                matches = matches.Where(m => m.Tags.TryGetValue(key, out var v) && v as string == value);
            }
            return matches.Single().Value;
        }

        // Route-level: transitions and backpressure counts match snapshot.
        FindMetric(
            DiagnosticsMeters.RouteStateTransitionsInstrument,
            ("route_id", "route-1"))
            .Should().Be(snap.StateTransitionCount);
        FindMetric(
            DiagnosticsMeters.RouteBackpressureDropsInstrument,
            ("route_id", "route-1"))
            .Should().Be(snap.BackpressureDropCount);

        // Sink-level: degradation and recovery counts match snapshot.
        FindMetric(
            DiagnosticsMeters.SinkDegradationEventsInstrument,
            ("route_id", "route-1"), ("sink_id", "sink-1"))
            .Should().Be(snap.Sinks[0].DegradationEventCount);
        FindMetric(
            DiagnosticsMeters.SinkRecoveryEventsInstrument,
            ("route_id", "route-1"), ("sink_id", "sink-1"))
            .Should().Be(snap.Sinks[0].RecoveryEventCount);

        // Pipeline: step invocations and points match snapshot.
        FindMetric(
            DiagnosticsMeters.PipelineStepInvocationsInstrument,
            ("route_id", "route-1"), ("step_kind", "filter"))
            .Should().Be(filterStep.Invocations);
        FindMetric(
            DiagnosticsMeters.PipelineStepPointsInInstrument,
            ("route_id", "route-1"), ("step_kind", "filter"))
            .Should().Be(filterStep.PointsIn);

        // Source: observations match snapshot.
        FindMetric(
            DiagnosticsMeters.SourcePointsObservedInstrument,
            ("route_id", "route-1"), ("source_id", "src-1"))
            .Should().Be(snap.Source.PointsObserved);
    }

    /// <summary>
    /// Companion gate: saturate the ring buffers with a burst of events,
    /// then verify bounded retention is preserved and drop counters flow
    /// through both the IDiagnosticsService query surface and the metrics
    /// surface coherently.
    /// </summary>
    [Fact]
    public void Diagnostics_EndToEnd_EventBurst_RetentionAndDropCountsRemainCoherent()
    {
        // Tight retention so the burst drops a predictable number.
        var collector = new RuntimeDiagnosticsCollector(new DiagnosticsCollectorOptions
        {
            RouteEventRetention = 8,
            SinkEventRetention = 8,
            BackpressureEventRetention = 8,
        });
        IDiagnosticsService service = collector;
        using var meter = new Meter("c4-gate-burst-" + Guid.NewGuid());
        using var meters = new DiagnosticsMeters(service, meter);

        // Burst: 50 route state-change events, 50 sink degraded events, and
        // 50 backpressure drops each of size 3.
        for (var i = 0; i < 50; i++)
        {
            collector.OnRouteStateChanged(new RouteStateChangedEvent
            {
                RouteId = "r1",
                From = RouteState.Configured,
                To = RouteState.Starting,
                ObservedAtUtc = DateTime.UtcNow,
            });
            collector.OnSinkDegraded(new SinkDegradedEvent
            {
                RouteId = "r1",
                SinkInstanceId = "s1",
                LastError = new AdapterError
                {
                    Code = "X.Y",
                    Category = ErrorCategory.Network,
                    Message = "fail",
                },
                RetryAttempts = 1,
                ObservedAtUtc = DateTime.UtcNow,
            });
            collector.OnBackpressureDropped(new BackpressureDroppedEvent
            {
                RouteId = "r1",
                DroppedCount = 3,
                Reason = "burst",
                ObservedAtUtc = DateTime.UtcNow,
            });
        }

        // ---- Retention visibility (query surface) ----

        var stateLog = service.GetRouteStateEvents("r1")!;
        stateLog.Capacity.Should().Be(8);
        stateLog.LiveCount.Should().Be(8);
        stateLog.TotalAdded.Should().Be(50);
        stateLog.TotalDropped.Should().Be(42);

        var sinkLog = service.GetSinkEvents("r1", "s1")!;
        sinkLog.Capacity.Should().Be(8);
        sinkLog.LiveCount.Should().Be(8);
        sinkLog.TotalAdded.Should().Be(50);
        sinkLog.TotalDropped.Should().Be(42);

        var bpLog = service.GetBackpressureEvents("r1")!;
        bpLog.Capacity.Should().Be(8);
        bpLog.LiveCount.Should().Be(8);
        bpLog.TotalAdded.Should().Be(50);
        bpLog.TotalDropped.Should().Be(42);

        // ---- Counter coherence (snapshot vs metrics surface) ----

        var snap = service.GetRouteSnapshot("r1")!;
        snap.StateTransitionCount.Should().Be(50); // counter is monotonic, not bounded by retention
        snap.BackpressureDropCount.Should().Be(150); // 50 events × 3 points
        snap.Sinks[0].DegradationEventCount.Should().Be(50);

        var metrics = RecordLong(meter);

        long MetricFor(string name, params (string Key, string Value)[] tags)
        {
            var matches = metrics.Where(m => m.InstrumentName == name);
            foreach (var (key, value) in tags)
            {
                matches = matches.Where(m => m.Tags.TryGetValue(key, out var v) && v as string == value);
            }
            return matches.Single().Value;
        }

        MetricFor(
            DiagnosticsMeters.RouteStateTransitionsInstrument,
            ("route_id", "r1"))
            .Should().Be(50);
        MetricFor(
            DiagnosticsMeters.RouteBackpressureDropsInstrument,
            ("route_id", "r1"))
            .Should().Be(150);
        MetricFor(
            DiagnosticsMeters.SinkDegradationEventsInstrument,
            ("route_id", "r1"), ("sink_id", "s1"))
            .Should().Be(50);

        // Single-source-of-truth pin across all three surfaces:
        // query-surface counter == metrics-surface counter == snapshot counter.
        var metricBp = MetricFor(
            DiagnosticsMeters.RouteBackpressureDropsInstrument,
            ("route_id", "r1"));
        metricBp.Should().Be(snap.BackpressureDropCount);
    }
}
