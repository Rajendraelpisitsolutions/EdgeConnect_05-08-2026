// ============================================================================
// File: Diagnostics/RouteEventAggregator.cs
// Purpose: Default implementation of IRouteEventAggregator. Pulls each
//          of the three Core event-log streams for one route, flattens
//          them into the wire DiagnosticsEventDto shape, assigns event
//          codes from the central catalog, classifies severity, sorts
//          desc by timestamp, and caps the result.
//
//          Pure (no IO except the IDiagnosticsService reads), fully
//          unit-testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Pulls per-route event ring buffers from <see cref="IDiagnosticsService"/>
/// and projects them into the normalized <see cref="DiagnosticsEventDto"/>
/// wire shape consumed by the Studio + future automation surfaces.
/// </summary>
public sealed class RouteEventAggregator : IRouteEventAggregator
{
    private readonly IDiagnosticsService _diagnostics;

    /// <summary>Construct a route-event aggregator backed by Core's diagnostics surface.</summary>
    public RouteEventAggregator(IDiagnosticsService diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticsEventDto> GetRecentRouteEvents(string routeId, int limit)
    {
        if (string.IsNullOrWhiteSpace(routeId)) return Array.Empty<DiagnosticsEventDto>();
        if (limit <= 0) return Array.Empty<DiagnosticsEventDto>();

        // We need the route snapshot to know which sinks belong to this
        // route — sink events are stored per (routeId, sinkInstanceId).
        var snapshot = _diagnostics.GetRouteSnapshot(routeId);
        if (snapshot is null) return Array.Empty<DiagnosticsEventDto>();

        var merged = new List<DiagnosticsEventDto>(capacity: limit * 2);

        // (1) Route state changes
        var stateLog = _diagnostics.GetRouteStateEvents(routeId);
        if (stateLog is not null)
        {
            foreach (var evt in stateLog.Entries)
            {
                merged.Add(MapStateChange(evt));
            }
        }

        // (2) Per-sink lifecycle events
        foreach (var sink in snapshot.Sinks)
        {
            var sinkLog = _diagnostics.GetSinkEvents(routeId, sink.SinkInstanceId);
            if (sinkLog is null) continue;
            foreach (var evt in sinkLog.Entries)
            {
                merged.Add(MapSinkEvent(evt));
            }
        }

        // (3) Backpressure drops
        var bpLog = _diagnostics.GetBackpressureEvents(routeId);
        if (bpLog is not null)
        {
            foreach (var evt in bpLog.Entries)
            {
                merged.Add(MapBackpressure(evt));
            }
        }

        // (4) Point quarantines (un-serializable points skipped)
        var qLog = _diagnostics.GetQuarantineEvents(routeId);
        if (qLog is not null)
        {
            foreach (var evt in qLog.Entries)
            {
                merged.Add(MapQuarantine(evt));
            }
        }

        // Sort desc by OccurredAtUtc (newest first), then cap.
        merged.Sort(static (a, b) => b.OccurredAtUtc.CompareTo(a.OccurredAtUtc));
        if (merged.Count > limit)
        {
            merged.RemoveRange(limit, merged.Count - limit);
        }
        return merged;
    }

    private static DiagnosticsEventDto MapStateChange(RouteStateChangedEvent evt)
    {
        var severity = SeverityForState(evt.To);
        var summary = string.IsNullOrEmpty(evt.Reason)
            ? $"Route entered {evt.To} from {evt.From}"
            : $"Route entered {evt.To} from {evt.From} — {evt.Reason}";
        return new DiagnosticsEventDto
        {
            OccurredAtUtc = evt.ObservedAtUtc,
            EventCode = DiagnosticsEventCodes.RouteStateChanged,
            Severity = severity,
            Summary = summary,
            RouteId = evt.RouteId,
            FromState = evt.From.ToString(),
            ToState = evt.To.ToString(),
        };
    }

    private static DiagnosticsEventDto MapSinkEvent(SinkEventEntry evt)
    {
        var (code, severity, summary) = evt.Kind switch
        {
            SinkEventKind.Degraded => (
                DiagnosticsEventCodes.SinkDegraded,
                DiagnosticsSeverity.Warning,
                evt.Error is { } err
                    ? $"Sink {evt.SinkInstanceId} degraded — {err.Code}: {err.Message}"
                    : $"Sink {evt.SinkInstanceId} degraded"
            ),
            SinkEventKind.Draining => (
                DiagnosticsEventCodes.SinkDraining,
                DiagnosticsSeverity.Info,
                $"Sink {evt.SinkInstanceId} draining backlog"
            ),
            SinkEventKind.Recovered => (
                DiagnosticsEventCodes.SinkRecovered,
                DiagnosticsSeverity.Info,
                $"Sink {evt.SinkInstanceId} recovered"
            ),
            _ => (
                DiagnosticsEventCodes.SinkDegraded,
                DiagnosticsSeverity.Info,
                $"Sink {evt.SinkInstanceId} state change ({evt.Kind})"
            ),
        };

        return new DiagnosticsEventDto
        {
            OccurredAtUtc = evt.ObservedAtUtc,
            EventCode = code,
            Severity = severity,
            Summary = summary,
            RouteId = evt.RouteId,
            SinkInstanceId = evt.SinkInstanceId,
        };
    }

    private static DiagnosticsEventDto MapBackpressure(BackpressureDroppedEvent evt)
    {
        var summary = evt.DroppedCount == 1
            ? $"1 point dropped ({evt.Reason})"
            : $"{evt.DroppedCount:N0} points dropped ({evt.Reason})";
        return new DiagnosticsEventDto
        {
            OccurredAtUtc = evt.ObservedAtUtc,
            EventCode = DiagnosticsEventCodes.BackpressureDropped,
            Severity = DiagnosticsSeverity.Warning,
            Summary = summary,
            RouteId = evt.RouteId,
            DroppedCount = evt.DroppedCount,
        };
    }

    private static DiagnosticsEventDto MapQuarantine(RoutePointQuarantinedEvent evt)
    {
        return new DiagnosticsEventDto
        {
            OccurredAtUtc = evt.ObservedAtUtc,
            EventCode = DiagnosticsEventCodes.RoutePointQuarantined,
            Severity = DiagnosticsSeverity.Warning,
            Summary = $"Point '{evt.TagName}' quarantined (could not serialize) — {evt.Reason}",
            RouteId = evt.RouteId,
            DroppedCount = 1,
        };
    }

    private static DiagnosticsSeverity SeverityForState(RouteState target) => target switch
    {
        RouteState.Running => DiagnosticsSeverity.Info,
        RouteState.Degraded => DiagnosticsSeverity.Warning,
        RouteState.Failed => DiagnosticsSeverity.Error,
        RouteState.Blocked => DiagnosticsSeverity.Error,
        // Configured / Starting / Draining / Stopping / Stopped — lifecycle, not concern.
        _ => DiagnosticsSeverity.Info,
    };
}
