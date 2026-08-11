// ============================================================================
// File: Diagnostics/RouteSummaryMapper.cs
// Purpose: Pure, stateless mapper that converts Core's
//          RouteHealthSnapshot into the public-API RouteSummaryDto
//          shape. Decoupling the wire shape from the in-process
//          snapshot lets us evolve internal diagnostics without
//          forcing API consumers (Blazor, curl, future Connectivity
//          Studio plugins) to recompile.
//
//          The mapper is pure (no IO, no library deps beyond Core)
//          so it's fully unit-testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Maps Core's <see cref="RouteHealthSnapshot"/> into the API
/// contract DTOs consumed by Connectivity Studio + external HTTP
/// clients.
/// </summary>
public static class RouteSummaryMapper
{
    /// <summary>Map a single snapshot to its summary DTO.</summary>
    public static RouteSummaryDto ToSummary(RouteHealthSnapshot snapshot)
    {
        System.ArgumentNullException.ThrowIfNull(snapshot);

        var sinks = new List<RouteSinkSummaryDto>(snapshot.Sinks.Count);
        foreach (var sink in snapshot.Sinks)
        {
            sinks.Add(MapSinkSummary(sink));
        }

        RouteSourceSummaryDto? source = null;
        if (snapshot.Source is { } src)
        {
            source = new RouteSourceSummaryDto
            {
                SourceInstanceId = src.SourceInstanceId,
                ProtocolName = src.ProtocolName,
                StateName = src.State.ToString(),
                PointsObserved = src.PointsObserved,
                LastPointAtUtc = src.LastPointAtUtc,
                LastErrorCode = src.LastError?.Code,
                LastErrorMessage = src.LastError?.Message,
                LastErrorAtUtc = src.LastErrorAtUtc,
            };
        }

        RouteBufferSummaryDto? buffer = null;
        if (snapshot.Buffer is { } b)
        {
            buffer = new RouteBufferSummaryDto
            {
                Mode = b.Mode,
                CurrentDepth = b.CurrentDepth,
                TotalEnqueued = b.TotalEnqueued,
                TotalDrained = b.TotalDrained,
                TotalDropped = b.TotalDropped,
                SizeBytes = b.SizeBytes,
            };
        }

        return new RouteSummaryDto
        {
            RouteId = snapshot.RouteId,
            ObservedAtUtc = snapshot.ObservedAtUtc,
            State = (int)snapshot.State,
            StateName = snapshot.State.ToString(),
            Source = source,
            Sinks = sinks,
            Buffer = buffer,
            Pipeline = MapPipeline(snapshot.Pipeline),
            StateTransitionCount = snapshot.StateTransitionCount,
            BackpressureDropCount = snapshot.BackpressureDropCount,
            QuarantinedPointCount = snapshot.QuarantinedPointCount,
        };
    }

    /// <summary>
    /// Project a pipeline snapshot into its wire-shape rollup. Derives
    /// per-step <c>AverageDurationMs</c> from cumulative duration over
    /// invocation count (null when a step has never been invoked).
    /// Returns null if the input is null — callers can pass through.
    /// </summary>
    public static RoutePipelineSummaryDto? MapPipeline(PipelineHealthSnapshot? pipeline)
    {
        if (pipeline is null)
        {
            return null;
        }

        var steps = new List<PipelineStepSummaryDto>(pipeline.Steps.Count);
        foreach (var step in pipeline.Steps)
        {
            double? avgMs = step.Invocations > 0
                ? step.TotalDuration.TotalMilliseconds / step.Invocations
                : null;
            steps.Add(new PipelineStepSummaryDto
            {
                Kind = step.StepKind,
                Invocations = step.Invocations,
                PointsIn = step.PointsIn,
                PointsOut = step.PointsOut,
                PointsSuppressed = step.PointsSuppressed,
                AverageDurationMs = avgMs,
            });
        }

        return new RoutePipelineSummaryDto
        {
            BatchesProcessed = pipeline.BatchesProcessed,
            PointsIn = pipeline.PointsIn,
            PointsOut = pipeline.PointsOut,
            Steps = steps,
        };
    }

    /// <summary>Map every known route to a summary DTO list.</summary>
    public static IReadOnlyList<RouteSummaryDto> ToSummaries(
        IReadOnlyList<RouteHealthSnapshot> snapshots)
    {
        System.ArgumentNullException.ThrowIfNull(snapshots);
        var result = new List<RouteSummaryDto>(snapshots.Count);
        foreach (var s in snapshots)
        {
            result.Add(ToSummary(s));
        }
        return result;
    }

    /// <summary>
    /// Project one sink snapshot into its wire-shape summary. Pulled
    /// out so the Sinks API (M.1b.3) can reuse the same projection
    /// without re-mapping a whole route.
    /// </summary>
    public static RouteSinkSummaryDto MapSinkSummary(SinkHealthSnapshot sink)
    {
        System.ArgumentNullException.ThrowIfNull(sink);
        return new RouteSinkSummaryDto
        {
            SinkInstanceId = sink.SinkInstanceId,
            IsDegraded = sink.IsDegraded,
            IsDraining = sink.IsDraining,
            DegradationEventCount = sink.DegradationEventCount,
            RecoveryEventCount = sink.RecoveryEventCount,
            AdapterStateName = sink.AdapterState?.ToString(),
            ActiveSessionCount = sink.ActiveSessions?.Count,
            LastErrorCode = sink.LastError?.Code,
            LastErrorMessage = sink.LastError?.Message,
            LastErrorAtUtc = sink.LastErrorAtUtc,
        };
    }

    /// <summary>
    /// Project a sink's <see cref="SinkHealthSnapshot.ActiveSessions"/>
    /// list into the wire-shape session DTOs. Returns null when the
    /// sink doesn't expose session tracking (most sinks); an empty
    /// list when the sink IS session-tracking but no clients are
    /// currently connected. The Sinks detail page uses this distinction
    /// to choose between "no panel" and "empty state on the panel".
    /// </summary>
    public static IReadOnlyList<SinkSessionSummaryDto>? MapSessions(SinkHealthSnapshot sink)
    {
        System.ArgumentNullException.ThrowIfNull(sink);
        if (sink.ActiveSessions is null)
        {
            return null;
        }
        var result = new List<SinkSessionSummaryDto>(sink.ActiveSessions.Count);
        foreach (var s in sink.ActiveSessions)
        {
            result.Add(new SinkSessionSummaryDto
            {
                SessionId = s.SessionId,
                SessionName = s.SessionName,
                ClientApplicationUri = s.ClientApplicationUri,
                ClientIpAddress = s.ClientIpAddress,
                ConnectedAtUtc = s.ConnectedAtUtc,
                LastActivityUtc = s.LastActivityUtc,
                UserTokenType = s.UserTokenType,
                SubscriptionCount = s.SubscriptionCount,
                MonitoredItemCount = s.MonitoredItemCount,
            });
        }
        return result;
    }
}
