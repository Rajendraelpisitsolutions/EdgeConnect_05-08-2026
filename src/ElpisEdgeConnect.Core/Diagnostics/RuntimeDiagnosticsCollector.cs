// ============================================================================
// File: Diagnostics/RuntimeDiagnosticsCollector.cs
// Purpose: The single concrete diagnostics collector for the gateway.
//          Phase 2 implements IRoutingEngineDiagnostics and exposes a
//          synchronous snapshot query for route health. Phases 3-5 add
//          pipeline metrics, source/sink health push seams, and metric
//          instruments behind the same collector instance.
// Reference: ARCHITECTURE_BLUEPRINT.md §13
// Milestone: C4 — phase 2.
//
// LOCKED design rules:
//   * One collector per gateway. There is exactly ONE state store; routing
//     code emits events, the collector owns the truth, snapshot objects are
//     derived live from collector state.
//   * Hot-path methods (OnRouteStateChanged, OnSink*, OnBackpressureDropped)
//     are sync void, never block on I/O, never throw in steady state.
//   * Snapshot queries take a single short-lived lock and return immutable
//     records. They never observe torn state.
//   * Bounded retention is observed: each route/sink ring buffer surfaces
//     its drop counter through the snapshot tree.
//   * No parallel state stores anywhere. Tests pin this in phase 2.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Pipeline;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// In-process diagnostics collector implementing
/// <see cref="IRoutingEngineDiagnostics"/>. Phases 3-5 will extend this same
/// class with <see cref="Pipeline.ITransformMetricsRecorder"/> and the
/// source/sink health push seams.
/// </summary>
public sealed class RuntimeDiagnosticsCollector
    : IRoutingEngineDiagnostics,
      ITransformMetricsRecorder,
      ISourceHealthSink,
      ISinkHealthSink,
      IBufferHealthSink,
      ISinkSessionHealthSink,
      IDiagnosticsService
{
    private readonly object _gate = new();
    private readonly DiagnosticsCollectorOptions _options;

    /// <summary>
    /// Construct a collector with the given retention options. Pass
    /// <c>null</c> to use <see cref="DiagnosticsCollectorOptions.Default"/>.
    /// </summary>
    public RuntimeDiagnosticsCollector(DiagnosticsCollectorOptions? options = null)
    {
        _options = (options ?? DiagnosticsCollectorOptions.Default).Validate();
    }

    /// <summary>The retention configuration this collector is using.</summary>
    public DiagnosticsCollectorOptions Options => _options;

    /// <summary>
    /// Ids of every route the collector has observed events for.
    /// Convenience alias of <see cref="GetKnownRoutes"/> preserved for
    /// phase-2 callers.
    /// </summary>
    public IReadOnlyList<string> KnownRouteIds => GetKnownRoutes();

    /// <summary>
    /// Pre-register a route so its diagnostics state exists before any
    /// event arrives. The collector lazy-creates anyway on first event,
    /// but tests and the engine may call this to seed the snapshot.
    /// Idempotent.
    /// </summary>
    public void EnsureRoute(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        lock (_gate)
        {
            GetOrCreateLocked(routeId);
        }
    }

    /// <summary>
    /// Forget all diagnostics state for the given route. Called by the
    /// M.P2.2 <c>RuntimeReloadCoordinator</c> after
    /// <c>IRoutingEngine.UnregisterRouteAsync</c> so removed routes do not
    /// linger in <see cref="GetAllRouteSnapshots"/> as stale entries.
    /// Idempotent on an unknown route id (silent no-op).
    /// </summary>
    /// <remarks>
    /// Per ADR-0002 (Configuration = inventory truth), inventory builders
    /// already drop stale snapshots whose route id is no longer in the
    /// config. This method is the matching teardown on the producer side
    /// so the collector's <c>_routeStates</c> dictionary does not grow
    /// unbounded across hot-reload cycles.
    /// </remarks>
    public void RemoveRoute(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        lock (_gate)
        {
            _routeStates.Remove(routeId);
        }
    }

    /// <inheritdoc/>
    public void OnRouteStateChanged(RouteStateChangedEvent evt)
    {
        if (evt is null)
        {
            return; // never throw on the hot path
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            rs.CurrentState = evt.To;
            rs.StateTransitionCount++;
            rs.StateEvents.Add(evt);
        }
    }

    /// <inheritdoc/>
    public void OnSinkDegraded(SinkDegradedEvent evt)
    {
        if (evt is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            var sink = rs.GetOrCreateSink(evt.SinkInstanceId, _options.SinkEventRetention);
            sink.IsDegraded = true;
            sink.IsDraining = false;
            sink.DegradationEventCount++;
            sink.LastError = evt.LastError;
            sink.LastErrorAtUtc = evt.ObservedAtUtc;
            sink.Events.Add(new SinkEventEntry
            {
                Kind = SinkEventKind.Degraded,
                SinkInstanceId = evt.SinkInstanceId,
                RouteId = evt.RouteId,
                ObservedAtUtc = evt.ObservedAtUtc,
                Error = evt.LastError,
            });
        }
    }

    /// <inheritdoc/>
    public void OnSinkDraining(SinkDrainingEvent evt)
    {
        if (evt is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            var sink = rs.GetOrCreateSink(evt.SinkInstanceId, _options.SinkEventRetention);
            sink.IsDegraded = false;
            sink.IsDraining = true;
            sink.Events.Add(new SinkEventEntry
            {
                Kind = SinkEventKind.Draining,
                SinkInstanceId = evt.SinkInstanceId,
                RouteId = evt.RouteId,
                ObservedAtUtc = evt.ObservedAtUtc,
            });
        }
    }

    /// <inheritdoc/>
    public void OnSinkRecovered(SinkRecoveredEvent evt)
    {
        if (evt is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            var sink = rs.GetOrCreateSink(evt.SinkInstanceId, _options.SinkEventRetention);
            sink.IsDegraded = false;
            sink.IsDraining = false;
            sink.RecoveryEventCount++;
            sink.Events.Add(new SinkEventEntry
            {
                Kind = SinkEventKind.Recovered,
                SinkInstanceId = evt.SinkInstanceId,
                RouteId = evt.RouteId,
                ObservedAtUtc = evt.ObservedAtUtc,
            });
        }
    }

    /// <inheritdoc/>
    public void OnBackpressureDropped(BackpressureDroppedEvent evt)
    {
        if (evt is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            rs.BackpressureDropCount += evt.DroppedCount;
            rs.BackpressureEvents.Add(evt);
        }
    }

    /// <inheritdoc/>
    public void OnRoutePointQuarantined(RoutePointQuarantinedEvent evt)
    {
        if (evt is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(evt.RouteId);
            rs.QuarantinedPointCount++;
            rs.QuarantineEvents.Add(evt);
        }
    }

    // ----- ITransformMetricsRecorder (phase 3) -----

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The "first step" and "last step" used to compute the pipeline-level
    /// <c>PointsIn</c> / <c>PointsOut</c> rollups are determined by
    /// <em>first-invocation order</em>: whichever <paramref name="stepName"/>
    /// is first recorded for a given route sits at index <c>0</c>, the
    /// second at index <c>1</c>, and so on. <see cref="Pipeline.TransformPipeline"/>
    /// drives steps in a fixed configured order for every batch, so the
    /// first-seen order at this collector equals the pipeline declaration
    /// order by construction. Callers that record steps out of order (e.g.
    /// a test driver that invokes step <c>B</c> before step <c>A</c> on its
    /// first pass) will have their rollups attributed to whichever step
    /// appears first. The contract is "first-invoked wins its slot forever."
    /// </para>
    /// <para>
    /// <paramref name="suppressedCount"/> is added to the per-step
    /// <c>PointsSuppressed</c> counter (C4 finding #2.B) — before this was
    /// plumbed through, the parameter was silently dropped.
    /// </para>
    /// </remarks>
    public void RecordStep(
        string routeId,
        string stepName,
        int inputCount,
        int outputCount,
        int suppressedCount,
        TimeSpan duration)
    {
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(stepName))
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            var step = rs.Pipeline.GetOrAddStep(stepName);
            step.Invocations++;
            step.PointsIn += inputCount;
            step.PointsOut += outputCount;
            step.PointsSuppressed += suppressedCount;
            step.TotalDuration += duration;

            // The first step is the "entry" of the pipeline; its invocation
            // count equals the number of batches the pipeline processed.
            // Input/output point totals roll up from first/last step, where
            // "first" and "last" are first-invocation order — see the XML
            // remarks above for the exact contract.
            if (ReferenceEquals(rs.Pipeline.Steps[0], step))
            {
                rs.Pipeline.BatchesProcessed = step.Invocations;
                rs.Pipeline.PointsIn = step.PointsIn;
            }
            if (ReferenceEquals(rs.Pipeline.Steps[^1], step))
            {
                rs.Pipeline.PointsOut = step.PointsOut;
            }
        }
    }

    // ----- ISourceHealthSink (phase 3) -----

    /// <inheritdoc/>
    public void RecordSourceObservation(
        string routeId,
        string sourceInstanceId,
        string protocolName,
        int pointsObserved,
        DateTime observedAtUtc)
    {
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(sourceInstanceId))
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            var src = rs.EnsureSource(sourceInstanceId, protocolName);
            src.PointsObserved += pointsObserved;
            if (pointsObserved > 0)
            {
                src.LastPointAtUtc = observedAtUtc;
            }
        }
    }

    /// <inheritdoc/>
    public void RecordSourceState(
        string routeId,
        string sourceInstanceId,
        string protocolName,
        AdapterState state,
        AdapterError? lastError)
    {
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(sourceInstanceId))
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            var src = rs.EnsureSource(sourceInstanceId, protocolName);
            src.State = state;
            if (lastError is not null)
            {
                src.LastError = lastError;
                src.LastErrorAtUtc = DateTime.UtcNow;
            }
            else if (state == AdapterState.Running)
            {
                // Recovery: a source that reconnected and is reading again reports
                // Running with no current error. Clear the stale error so the
                // diagnostics/Overview surface stops showing e.g.
                // MODBUS.CONNECT_FAILED forever after the device came back.
                // (Without this, the guard above kept the last non-null error.)
                src.LastError = null;
                src.LastErrorAtUtc = null;
            }
        }
    }

    // ----- ISinkHealthSink (phase 3) -----

    /// <inheritdoc/>
    public void RecordSinkAdapterState(
        string routeId,
        string sinkInstanceId,
        AdapterState state,
        AdapterError? lastError)
    {
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(sinkInstanceId))
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            var sink = rs.GetOrCreateSink(sinkInstanceId, _options.SinkEventRetention);
            sink.AdapterState = state;
            if (lastError is not null)
            {
                sink.LastError = lastError;
                sink.LastErrorAtUtc = DateTime.UtcNow;
            }
            // NOTE: unlike a source, a sink's LastError is shared with the
            // publish-level dimension (OnSinkDegraded/OnSinkRecovered). The
            // adapter can be Running (broker connected) while delivery still
            // fails, so a Running adapter-state must NOT clear the error here —
            // publish-level recovery is signalled via OnSinkRecovered.
        }
    }

    // ----- IBufferHealthSink (S&F observability) -----

    /// <inheritdoc/>
    public void RecordBufferStats(string routeId, BufferMode mode, BufferStats stats)
    {
        if (string.IsNullOrEmpty(routeId) || stats is null)
        {
            return;
        }
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            // Latest-wins: the route worker calls this on every poll cycle.
            // We keep just the last observation — the soak runner / metrics
            // exporter want depth NOW, not a depth history (event logs are
            // for state transitions, not gauges).
            rs.Buffer = new BufferDiagnosticsState
            {
                Mode = mode,
                CurrentDepth = stats.CurrentDepth,
                TotalEnqueued = stats.TotalEnqueued,
                TotalDrained = stats.TotalDrained,
                TotalDropped = stats.TotalDropped,
                DroppedByCapacity = stats.DroppedByCapacity,
                DroppedByRetention = stats.DroppedByRetention,
                SizeBytes = stats.SizeBytes,
                OldestMessageAt = stats.OldestMessageAt,
                OldestUnackedSinkId = stats.OldestUnackedSinkId,
                OldestUnackedAt = stats.OldestUnackedAt,
                ObservedAtUtc = DateTime.UtcNow,
            };
        }
    }

    // ----- ISinkSessionHealthSink (Milestone H.2) -----

    /// <inheritdoc/>
    public void RecordActiveSessions(
        string routeId,
        string sinkInstanceId,
        IReadOnlyList<SinkSessionSummary> sessions)
    {
        if (string.IsNullOrEmpty(routeId) || string.IsNullOrEmpty(sinkInstanceId))
        {
            return;
        }
        // Latest-wins: the poller calls this on every tick (default ~5s).
        // We keep the latest snapshot only; no history. Null collapses to
        // empty so consumers always see a deterministic list.
        var sessionsToStore = sessions ?? Array.Empty<SinkSessionSummary>();
        lock (_gate)
        {
            var rs = GetOrCreateLocked(routeId);
            var sink = rs.GetOrCreateSink(sinkInstanceId, _options.SinkEventRetention);
            sink.ActiveSessions = sessionsToStore;
        }
    }

    // ----- Query surface (IDiagnosticsService — phase 4) -----

    /// <inheritdoc/>
    public IReadOnlyList<string> GetKnownRoutes()
    {
        lock (_gate)
        {
            return _routeStates.Keys.ToArray();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<RouteHealthSnapshot> GetAllRouteSnapshots()
    {
        lock (_gate)
        {
            var result = new List<RouteHealthSnapshot>(_routeStates.Count);
            foreach (var routeId in _routeStates.Keys)
            {
                result.Add(BuildSnapshotLocked(routeId));
            }
            return result;
        }
    }

    /// <summary>
    /// Atomic snapshot of the named route's three-way diagnostics. Returns
    /// <c>null</c> if the route is unknown to the collector.
    /// </summary>
    public RouteHealthSnapshot? GetRouteSnapshot(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        lock (_gate)
        {
            if (!_routeStates.ContainsKey(routeId))
            {
                return null;
            }
            return BuildSnapshotLocked(routeId);
        }
    }

    /// <summary>Recent route state-change events for tests / phase-4 query API.</summary>
    public BoundedEventLogSnapshot<RouteStateChangedEvent>? GetRouteStateEvents(string routeId)
    {
        lock (_gate)
        {
            if (!TryGetRouteLocked(routeId, out var rs))
            {
                return null;
            }
            return rs.StateEvents.SnapshotWithCounters();
        }
    }

    /// <summary>Recent sink lifecycle events for the given route + sink.</summary>
    public BoundedEventLogSnapshot<SinkEventEntry>? GetSinkEvents(string routeId, string sinkInstanceId)
    {
        lock (_gate)
        {
            if (!TryGetRouteLocked(routeId, out var rs))
            {
                return null;
            }
            if (!rs.Sinks.TryGetValue(sinkInstanceId, out var sink))
            {
                return null;
            }
            return sink.Events.SnapshotWithCounters();
        }
    }

    /// <summary>Recent backpressure drop events for the given route.</summary>
    public BoundedEventLogSnapshot<BackpressureDroppedEvent>? GetBackpressureEvents(string routeId)
    {
        lock (_gate)
        {
            if (!TryGetRouteLocked(routeId, out var rs))
            {
                return null;
            }
            return rs.BackpressureEvents.SnapshotWithCounters();
        }
    }

    /// <summary>
    /// Recent point-quarantine events for the given route — points the buffer
    /// could not serialize and skipped rather than failing the enqueue.
    /// </summary>
    public BoundedEventLogSnapshot<RoutePointQuarantinedEvent>? GetQuarantineEvents(string routeId)
    {
        lock (_gate)
        {
            if (!TryGetRouteLocked(routeId, out var rs))
            {
                return null;
            }
            return rs.QuarantineEvents.SnapshotWithCounters();
        }
    }

    // ----- Internals -----

    private RouteDiagnosticsState GetOrCreateLocked(string routeId)
    {
        // _routeStates is the SINGLE state store. No parallel dict.
        if (_routeStates.TryGetValue(routeId, out var existing))
        {
            return existing;
        }
        var fresh = new RouteDiagnosticsState(
            routeId,
            _options.RouteEventRetention,
            _options.BackpressureEventRetention);
        _routeStates[routeId] = fresh;
        return fresh;
    }

    private bool TryGetRouteLocked(string routeId, out RouteDiagnosticsState rs)
    {
        if (string.IsNullOrEmpty(routeId))
        {
            rs = null!;
            return false;
        }
        return _routeStates.TryGetValue(routeId, out rs!);
    }

    private RouteHealthSnapshot BuildSnapshotLocked(string routeId)
    {
        var rs = _routeStates[routeId];
        var sinks = new List<SinkHealthSnapshot>(rs.Sinks.Count);
        foreach (var (sinkId, sink) in rs.Sinks)
        {
            sinks.Add(new SinkHealthSnapshot
            {
                SinkInstanceId = sinkId,
                RouteId = routeId,
                IsDegraded = sink.IsDegraded,
                IsDraining = sink.IsDraining,
                DegradationEventCount = sink.DegradationEventCount,
                RecoveryEventCount = sink.RecoveryEventCount,
                LastError = sink.LastError,
                LastErrorAtUtc = sink.LastErrorAtUtc,
                AdapterState = sink.AdapterState,
                ActiveSessions = sink.ActiveSessions,
            });
        }

        // Pipeline rollup — built live from the per-step accumulators.
        var steps = new List<TransformStepStats>(rs.Pipeline.Steps.Count);
        foreach (var s in rs.Pipeline.Steps)
        {
            steps.Add(new TransformStepStats
            {
                StepKind = s.StepKind,
                Invocations = s.Invocations,
                PointsIn = s.PointsIn,
                PointsOut = s.PointsOut,
                PointsSuppressed = s.PointsSuppressed,
                TotalDuration = s.TotalDuration,
            });
        }
        var pipeline = new PipelineHealthSnapshot
        {
            RouteId = routeId,
            BatchesProcessed = rs.Pipeline.BatchesProcessed,
            PointsIn = rs.Pipeline.PointsIn,
            PointsOut = rs.Pipeline.PointsOut,
            Steps = steps,
        };

        // Source rollup — null if the host supervisor hasn't pushed yet.
        SourceHealthSnapshot? source = null;
        if (rs.Source is { } src)
        {
            source = new SourceHealthSnapshot
            {
                SourceInstanceId = src.SourceInstanceId,
                ProtocolName = src.ProtocolName,
                State = src.State,
                PointsObserved = src.PointsObserved,
                LastPointAtUtc = src.LastPointAtUtc,
                LastError = src.LastError,
                LastErrorAtUtc = src.LastErrorAtUtc,
            };
        }

        // Buffer rollup — null if the route worker hasn't pushed any
        // BufferStats observation yet (typical for the first ~tens of ms
        // after route start).
        BufferHealthSnapshot? buffer = null;
        if (rs.Buffer is { } b)
        {
            buffer = new BufferHealthSnapshot
            {
                RouteId = routeId,
                Mode = b.Mode.ToString(),
                CurrentDepth = b.CurrentDepth,
                TotalEnqueued = b.TotalEnqueued,
                TotalDrained = b.TotalDrained,
                TotalDropped = b.TotalDropped,
                DroppedByCapacity = b.DroppedByCapacity,
                DroppedByRetention = b.DroppedByRetention,
                SizeBytes = b.SizeBytes,
                OldestMessageAt = b.OldestMessageAt,
                OldestUnackedSinkId = b.OldestUnackedSinkId,
                OldestUnackedAt = b.OldestUnackedAt,
                ObservedAtUtc = b.ObservedAtUtc,
            };
        }

        return new RouteHealthSnapshot
        {
            RouteId = routeId,
            ObservedAtUtc = DateTime.UtcNow,
            State = rs.CurrentState,
            StateTransitionCount = rs.StateTransitionCount,
            BackpressureDropCount = rs.BackpressureDropCount,
            QuarantinedPointCount = rs.QuarantinedPointCount,
            Source = source,
            Pipeline = pipeline,
            Sinks = sinks,
            Buffer = buffer,
        };
    }

    // The full per-route state. Held in a private dictionary so the
    // collector remains the SINGLE state store: snapshots are computed
    // from this dict on demand and never cached anywhere else.
    private readonly Dictionary<string, RouteDiagnosticsState> _routeStates =
        new(StringComparer.Ordinal);

    /// <summary>Internal per-route bookkeeping. Not exposed.</summary>
    private sealed class RouteDiagnosticsState
    {
        public RouteDiagnosticsState(
            string routeId,
            int routeEventRetention,
            int backpressureEventRetention)
        {
            RouteId = routeId;
            StateEvents = new BoundedEventLog<RouteStateChangedEvent>(routeEventRetention);
            BackpressureEvents =
                new BoundedEventLog<BackpressureDroppedEvent>(backpressureEventRetention);
            // Quarantine events share the backpressure ring's retention — both
            // are bounded "points that didn't make it" logs.
            QuarantineEvents =
                new BoundedEventLog<RoutePointQuarantinedEvent>(backpressureEventRetention);
            Pipeline = new PipelineDiagnosticsState();
        }

        public string RouteId { get; }
        public RouteState CurrentState { get; set; } = RouteState.Configured;
        public long StateTransitionCount { get; set; }
        public long BackpressureDropCount { get; set; }
        public long QuarantinedPointCount { get; set; }
        public BoundedEventLog<RouteStateChangedEvent> StateEvents { get; }
        public BoundedEventLog<BackpressureDroppedEvent> BackpressureEvents { get; }
        public BoundedEventLog<RoutePointQuarantinedEvent> QuarantineEvents { get; }
        public Dictionary<string, SinkDiagnosticsState> Sinks { get; } =
            new(StringComparer.Ordinal);
        public PipelineDiagnosticsState Pipeline { get; }
        public SourceDiagnosticsState? Source { get; set; }
        public BufferDiagnosticsState? Buffer { get; set; }

        public SinkDiagnosticsState GetOrCreateSink(string sinkInstanceId, int retention)
        {
            if (!Sinks.TryGetValue(sinkInstanceId, out var existing))
            {
                existing = new SinkDiagnosticsState(retention);
                Sinks[sinkInstanceId] = existing;
            }
            return existing;
        }

        public SourceDiagnosticsState EnsureSource(string sourceInstanceId, string protocolName)
        {
            // One source per route (matches RouteDefinition shape).
            if (Source is null || Source.SourceInstanceId != sourceInstanceId)
            {
                Source = new SourceDiagnosticsState
                {
                    SourceInstanceId = sourceInstanceId,
                    ProtocolName = protocolName,
                    State = AdapterState.Created,
                };
            }
            return Source;
        }
    }

    /// <summary>Internal per-sink bookkeeping. Not exposed.</summary>
    private sealed class SinkDiagnosticsState
    {
        public SinkDiagnosticsState(int retention)
        {
            Events = new BoundedEventLog<SinkEventEntry>(retention);
        }

        public bool IsDegraded { get; set; }
        public bool IsDraining { get; set; }
        public long DegradationEventCount { get; set; }
        public long RecoveryEventCount { get; set; }
        public AdapterError? LastError { get; set; }
        public DateTime? LastErrorAtUtc { get; set; }
        public AdapterState? AdapterState { get; set; }
        public IReadOnlyList<SinkSessionSummary>? ActiveSessions { get; set; }
        public BoundedEventLog<SinkEventEntry> Events { get; }
    }

    /// <summary>
    /// Internal per-route buffer bookkeeping. Latest-wins (gauge semantics)
    /// — RouteWorker overwrites on every poll cycle. We do NOT keep a
    /// history because depth/age are point-in-time gauges, not events.
    /// </summary>
    private sealed class BufferDiagnosticsState
    {
        public required BufferMode Mode { get; init; }
        public required long CurrentDepth { get; init; }
        public required long TotalEnqueued { get; init; }
        public required long TotalDrained { get; init; }
        public required long TotalDropped { get; init; }
        public long DroppedByCapacity { get; init; }
        public long DroppedByRetention { get; init; }
        public long SizeBytes { get; init; }
        public DateTime? OldestMessageAt { get; init; }
        public string? OldestUnackedSinkId { get; init; }
        public DateTime? OldestUnackedAt { get; init; }
        public required DateTime ObservedAtUtc { get; init; }
    }

    /// <summary>Internal per-source bookkeeping. One source per route.</summary>
    private sealed class SourceDiagnosticsState
    {
        public required string SourceInstanceId { get; init; }
        public required string ProtocolName { get; init; }
        public AdapterState State { get; set; }
        public long PointsObserved { get; set; }
        public DateTime? LastPointAtUtc { get; set; }
        public AdapterError? LastError { get; set; }
        public DateTime? LastErrorAtUtc { get; set; }
    }

    /// <summary>Internal per-route pipeline bookkeeping.</summary>
    private sealed class PipelineDiagnosticsState
    {
        public long BatchesProcessed { get; set; }
        public long PointsIn { get; set; }
        public long PointsOut { get; set; }
        public List<TransformStepStatsState> Steps { get; } = new();

        public TransformStepStatsState GetOrAddStep(string stepName)
        {
            foreach (var s in Steps)
            {
                if (s.StepKind == stepName)
                {
                    return s;
                }
            }
            var fresh = new TransformStepStatsState { StepKind = stepName };
            Steps.Add(fresh);
            return fresh;
        }
    }

    /// <summary>Internal per-step accumulator (mutable counterpart to TransformStepStats).</summary>
    private sealed class TransformStepStatsState
    {
        public required string StepKind { get; init; }
        public long Invocations { get; set; }
        public long PointsIn { get; set; }
        public long PointsOut { get; set; }
        public long PointsSuppressed { get; set; }
        public TimeSpan TotalDuration { get; set; }
    }
}
