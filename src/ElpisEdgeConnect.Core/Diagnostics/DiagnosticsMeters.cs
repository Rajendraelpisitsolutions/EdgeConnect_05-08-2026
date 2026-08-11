// ============================================================================
// File: Diagnostics/DiagnosticsMeters.cs
// Purpose: System.Diagnostics.Metrics instrument bindings for the diagnostics
//          collector. Constructs a Meter and publishes the C4 Prometheus-
//          exportable instrument set via observable instruments whose
//          callbacks read live from IDiagnosticsService. There is NO
//          parallel counter store: each scrape reads current collector
//          state through GetAllRouteSnapshots.
// Reference: ARCHITECTURE_BLUEPRINT.md §13, Week 1 metrics decision
// Milestone: C4 — phase 5.
//
// LOCKED design rules:
//   * Meter name and instrument names come from DiagnosticsConstants —
//     tests pin them.
//   * Tag keys are stable and minimal: route_id / sink_id / source_id /
//     step_kind. Tests pin them too.
//   * All instruments are ObservableCounter<long> or ObservableGauge<int>.
//     The hot path (collector writers) never touches a metrics API —
//     metrics are derived from collector state at scrape time only.
//   * No HTTP scrape endpoint. No OpenTelemetry dependency in Core. Any
//     host that wants to export these instruments can point an exporter
//     at the meter name; nothing in Core needs to change.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Publishes the diagnostics collector's state as <see cref="Meter"/>
/// instruments. Constructs a <see cref="Meter"/> named
/// <see cref="DiagnosticsConstants.MeterName"/> (unless one is supplied)
/// and registers observable instruments whose callbacks read live from
/// the supplied <see cref="IDiagnosticsService"/>.
/// </summary>
public sealed class DiagnosticsMeters : IDisposable
{
    private readonly IDiagnosticsService _service;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    // Instrument name constants — pinned by tests. Keep the prefix locked.
#pragma warning disable CS1591 // instrument name constants are self-documenting; locked-by-test.
    public const string RouteStateTransitionsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "route_state_transitions_total";
    public const string RouteBackpressureDropsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "route_backpressure_drops_total";
    public const string SinkDegradationEventsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "sink_degradation_events_total";
    public const string SinkRecoveryEventsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "sink_recovery_events_total";
    public const string PipelineStepInvocationsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "pipeline_step_invocations_total";
    public const string PipelineStepPointsInInstrument =
        DiagnosticsConstants.InstrumentPrefix + "pipeline_step_points_in_total";
    public const string PipelineStepPointsOutInstrument =
        DiagnosticsConstants.InstrumentPrefix + "pipeline_step_points_out_total";
    public const string SourcePointsObservedInstrument =
        DiagnosticsConstants.InstrumentPrefix + "source_points_observed_total";
    public const string RouteStateGaugeInstrument =
        DiagnosticsConstants.InstrumentPrefix + "route_state";

    // Sink session-tracking gauges (H.2). Protocol-agnostic at the
    // instrument level — the OPC UA Server sink is the only producer
    // in v1, but the names don't presume OPC UA so future
    // session-aware sinks (MQTT broker-side, HTTP keepalive) can
    // surface the same instruments without renaming. Sink-protocol
    // attribution comes from the sink_id tag.
    public const string SinkActiveSessionsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "sink_active_sessions";
    public const string SinkSubscriptionsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "sink_subscriptions";
    public const string SinkMonitoredItemsInstrument =
        DiagnosticsConstants.InstrumentPrefix + "sink_monitored_items";

    // Tag keys — pinned.
    public const string TagRouteId = "route_id";
    public const string TagSinkId = "sink_id";
    public const string TagSourceId = "source_id";
    public const string TagStepKind = "step_kind";
#pragma warning restore CS1591

    /// <summary>
    /// Construct a meter bindings object. When <paramref name="meter"/> is
    /// <c>null</c>, a new <see cref="Meter"/> is created with the locked
    /// name and owned by this instance (disposed with it).
    /// </summary>
    public DiagnosticsMeters(IDiagnosticsService service, Meter? meter = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _ownsMeter = meter is null;
        _meter = meter ?? new Meter(DiagnosticsConstants.MeterName);
        RegisterInstruments();
    }

    /// <summary>The underlying <see cref="Meter"/>.</summary>
    public Meter Meter => _meter;

    private void RegisterInstruments()
    {
        // Route-level counters.
        _meter.CreateObservableCounter<long>(
            RouteStateTransitionsInstrument,
            ObserveRouteStateTransitions,
            unit: "transitions",
            description: "Total number of route lifecycle transitions observed per route.");

        _meter.CreateObservableCounter<long>(
            RouteBackpressureDropsInstrument,
            ObserveBackpressureDrops,
            unit: "points",
            description: "Total points dropped by the backpressure controller per route.");

        _meter.CreateObservableGauge<int>(
            RouteStateGaugeInstrument,
            ObserveRouteStateGauge,
            unit: "state",
            description: "Current route lifecycle state as a numeric enum value per route.");

        // Sink-level counters.
        _meter.CreateObservableCounter<long>(
            SinkDegradationEventsInstrument,
            ObserveSinkDegradationEvents,
            unit: "events",
            description: "Total sink degradation events per (route, sink).");

        _meter.CreateObservableCounter<long>(
            SinkRecoveryEventsInstrument,
            ObserveSinkRecoveryEvents,
            unit: "events",
            description: "Total sink recovery events per (route, sink).");

        // Pipeline-level counters.
        _meter.CreateObservableCounter<long>(
            PipelineStepInvocationsInstrument,
            ObserveStepInvocations,
            unit: "invocations",
            description: "Total pipeline step invocations per (route, step).");

        _meter.CreateObservableCounter<long>(
            PipelineStepPointsInInstrument,
            ObserveStepPointsIn,
            unit: "points",
            description: "Total points entering each pipeline step per (route, step).");

        _meter.CreateObservableCounter<long>(
            PipelineStepPointsOutInstrument,
            ObserveStepPointsOut,
            unit: "points",
            description: "Total points leaving each pipeline step per (route, step).");

        // Source-level counters.
        _meter.CreateObservableCounter<long>(
            SourcePointsObservedInstrument,
            ObserveSourcePointsObserved,
            unit: "points",
            description: "Total points observed from each source per (route, source).");

        // Sink session-tracking gauges (H.2).
        _meter.CreateObservableGauge<int>(
            SinkActiveSessionsInstrument,
            ObserveSinkActiveSessions,
            unit: "sessions",
            description: "Number of currently-active client sessions on each session-tracking sink.");

        _meter.CreateObservableGauge<int>(
            SinkSubscriptionsInstrument,
            ObserveSinkSubscriptions,
            unit: "subscriptions",
            description: "Total subscriptions across all active sessions on each session-tracking sink.");

        _meter.CreateObservableGauge<int>(
            SinkMonitoredItemsInstrument,
            ObserveSinkMonitoredItems,
            unit: "items",
            description: "Total monitored items across all active sessions on each session-tracking sink.");
    }

    // ----- Observable callbacks — read live from the service. -----

    private IEnumerable<Measurement<long>> ObserveRouteStateTransitions()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            yield return new Measurement<long>(
                snap.StateTransitionCount,
                new KeyValuePair<string, object?>(TagRouteId, snap.RouteId));
        }
    }

    private IEnumerable<Measurement<long>> ObserveBackpressureDrops()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            yield return new Measurement<long>(
                snap.BackpressureDropCount,
                new KeyValuePair<string, object?>(TagRouteId, snap.RouteId));
        }
    }

    private IEnumerable<Measurement<int>> ObserveRouteStateGauge()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            yield return new Measurement<int>(
                (int)snap.State,
                new KeyValuePair<string, object?>(TagRouteId, snap.RouteId));
        }
    }

    private IEnumerable<Measurement<long>> ObserveSinkDegradationEvents()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var sink in snap.Sinks)
            {
                yield return new Measurement<long>(
                    sink.DegradationEventCount,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagSinkId, sink.SinkInstanceId));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveSinkRecoveryEvents()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var sink in snap.Sinks)
            {
                yield return new Measurement<long>(
                    sink.RecoveryEventCount,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagSinkId, sink.SinkInstanceId));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveStepInvocations()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var step in snap.Pipeline.Steps)
            {
                yield return new Measurement<long>(
                    step.Invocations,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagStepKind, step.StepKind));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveStepPointsIn()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var step in snap.Pipeline.Steps)
            {
                yield return new Measurement<long>(
                    step.PointsIn,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagStepKind, step.StepKind));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveStepPointsOut()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var step in snap.Pipeline.Steps)
            {
                yield return new Measurement<long>(
                    step.PointsOut,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagStepKind, step.StepKind));
            }
        }
    }

    private IEnumerable<Measurement<long>> ObserveSourcePointsObserved()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            if (snap.Source is null)
            {
                continue;
            }
            yield return new Measurement<long>(
                snap.Source.PointsObserved,
                new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                new KeyValuePair<string, object?>(TagSourceId, snap.Source.SourceInstanceId));
        }
    }

    // ----- Sink session-tracking gauges (H.2) -----

    private IEnumerable<Measurement<int>> ObserveSinkActiveSessions()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var sink in snap.Sinks)
            {
                if (sink.ActiveSessions is null)
                {
                    continue;
                }
                yield return new Measurement<int>(
                    sink.ActiveSessions.Count,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagSinkId, sink.SinkInstanceId));
            }
        }
    }

    private IEnumerable<Measurement<int>> ObserveSinkSubscriptions()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var sink in snap.Sinks)
            {
                if (sink.ActiveSessions is null)
                {
                    continue;
                }
                var total = 0;
                foreach (var s in sink.ActiveSessions)
                {
                    total += s.SubscriptionCount;
                }
                yield return new Measurement<int>(
                    total,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagSinkId, sink.SinkInstanceId));
            }
        }
    }

    private IEnumerable<Measurement<int>> ObserveSinkMonitoredItems()
    {
        foreach (var snap in _service.GetAllRouteSnapshots())
        {
            foreach (var sink in snap.Sinks)
            {
                if (sink.ActiveSessions is null)
                {
                    continue;
                }
                var total = 0;
                foreach (var s in sink.ActiveSessions)
                {
                    total += s.MonitoredItemCount;
                }
                yield return new Measurement<int>(
                    total,
                    new KeyValuePair<string, object?>(TagRouteId, snap.RouteId),
                    new KeyValuePair<string, object?>(TagSinkId, sink.SinkInstanceId));
            }
        }
    }

    /// <summary>Dispose — releases the meter if this instance owns it.</summary>
    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }
}
