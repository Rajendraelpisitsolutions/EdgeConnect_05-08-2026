// ============================================================================
// File: Diagnostics/RouteInventoryBuilder.cs
// Purpose: Pure builder that merges configured routes with live route
//          diagnostics snapshots and registered configuration faults
//          into the wire-shape RouteSummaryDto list consumed by
//          /api/v1/routes.
//
//          Applies the same Configuration-as-inventory-truth pattern
//          that SourceInventoryBuilder applies for sources (M.2b.1.1
//          + M.P2.1 phase 3). Every route in the gateway config
//          appears in the list, regardless of whether a runtime
//          snapshot exists; faulted routes (cross-record missing-
//          source / missing-sink) get StateName="Faulted" with the
//          LastError* fields populated from the registry entry.
//
//          Display-state precedence (top wins):
//             1. Disabled                 — route.Enabled = false
//             2. Faulted                  — fault in registry for this
//                                            route id, OR live snapshot
//                                            state is Faulted
//             3. Live state from snapshot — Running / Degraded / Stopped
//             4. Configured / Not running — enabled, no snapshot yet
//                                            (supervisor pending; usually
//                                            because the source faulted)
//
//          The pre-M.2b.1.1 RoutesApi walked IDiagnosticsService.
//          GetAllRouteSnapshots() and projected per-snapshot via
//          RouteSummaryMapper. With M.P2.1 phase 3, the path becomes
//          config-driven, with the existing mapper used internally
//          for the live-state rows. Stale snapshots whose route ids
//          aren't in the current config no longer appear.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1 phase 3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Pure builder that merges configured routes with live diagnostics
/// snapshots and configuration faults into the wire-shape
/// <see cref="RouteSummaryDto"/> list consumed by /api/v1/routes.
/// </summary>
public static class RouteInventoryBuilder
{
    /// <summary>State name for routes with <c>Enabled = false</c> in config.</summary>
    public const string StateDisabled = "Disabled";

    /// <summary>State name for routes with a registered configuration fault OR runtime Faulted state.</summary>
    public const string StateFaulted = "Faulted";

    /// <summary>State name for enabled routes that have no diagnostics snapshot yet (initialising or upstream source faulted).</summary>
    public const string StateConfiguredNotRunning = "Configured / Not running";

    /// <summary>
    /// Build the route-inventory list. Rows are returned in configuration
    /// order — the order routes appear in <see cref="GatewayConfiguration.Routes"/>.
    /// </summary>
    public static IReadOnlyList<RouteSummaryDto> Build(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);

        // Index snapshots by route id.
        var snapshotsByRouteId = new Dictionary<string, RouteHealthSnapshot>(
            snapshots.Count, StringComparer.Ordinal);
        foreach (var snap in snapshots)
        {
            snapshotsByRouteId.TryAdd(snap.RouteId, snap);
        }

        // Index route-kind faults by route id.
        var routeFaultsById = new Dictionary<string, ConfigurationFault>(
            faults?.Count ?? 0, StringComparer.Ordinal);
        if (faults is not null)
        {
            foreach (var f in faults)
            {
                if (f.Kind == ConfigurationFaultKind.Route)
                {
                    routeFaultsById.TryAdd(f.InstanceId, f);
                }
            }
        }

        // Map each configured destination to its protocol so the Overview
        // card can show the destination TYPE (e.g. "mqtt") beside the name —
        // the sink diagnostics snapshot carries no protocol, but config does.
        var sinkProtocolById = new Dictionary<string, string>(
            config.Sinks.Count, StringComparer.Ordinal);
        foreach (var sink in config.Sinks)
        {
            sinkProtocolById[sink.InstanceId] = sink.ProtocolName;
        }

        var rows = new List<RouteSummaryDto>(config.Routes.Count);
        foreach (var route in config.Routes)
        {
            snapshotsByRouteId.TryGetValue(route.RouteId, out var snap);
            routeFaultsById.TryGetValue(route.RouteId, out var configFault);
            rows.Add(BuildRouteDto(route, snap, configFault, sinkProtocolById));
        }

        return rows;
    }

    /// <summary>
    /// Return <paramref name="sinks"/> with each destination's
    /// <see cref="RouteSinkSummaryDto.ProtocolName"/> filled from config
    /// (by instance id). Sinks with no config match are left unchanged.
    /// </summary>
    private static List<RouteSinkSummaryDto> WithSinkProtocols(
        IEnumerable<RouteSinkSummaryDto> sinks,
        IReadOnlyDictionary<string, string> protocolById)
    {
        var list = new List<RouteSinkSummaryDto>();
        foreach (var s in sinks)
        {
            list.Add(protocolById.TryGetValue(s.SinkInstanceId, out var proto)
                ? s with { ProtocolName = proto }
                : s);
        }
        return list;
    }

    /// <summary>
    /// Find one configured route by id and project it. Returns null
    /// when the route is not in config — callers map that to 404.
    /// </summary>
    public static RouteSummaryDto? BuildOne(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        string routeId,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(routeId);

        foreach (var row in Build(config, snapshots, faults))
        {
            if (string.Equals(row.RouteId, routeId, StringComparison.Ordinal))
            {
                return row;
            }
        }
        return null;
    }

    private static RouteSummaryDto BuildRouteDto(
        RouteConfig route,
        RouteHealthSnapshot? snap,
        ConfigurationFault? configFault,
        IReadOnlyDictionary<string, string> sinkProtocolById)
    {
        var observedAtUtc = snap?.ObservedAtUtc ?? configFault?.ObservedAtUtc ?? DateTime.UtcNow;

        // ─── 1. Disabled wins over everything ────────────────────────
        if (!route.Enabled)
        {
            return new RouteSummaryDto
            {
                RouteId = route.RouteId,
                ObservedAtUtc = observedAtUtc,
                State = 0,
                StateName = StateDisabled,
                Source = null,
                Sinks = Array.Empty<RouteSinkSummaryDto>(),
                Buffer = null,
                Pipeline = null,
            };
        }

        // ─── 2. Faulted ──────────────────────────────────────────────
        // Either a cross-record config fault, or a runtime adapter fault
        // reflected in the snapshot's state.
        var runtimeFaulted = snap is not null && snap.State == RouteState.Failed;
        if (configFault is not null || runtimeFaulted)
        {
            // Reuse the live mapper's projection of sub-DTOs where a
            // snapshot exists, but overlay the route-level fault detail.
            // If no snapshot exists (pure config fault), produce a row
            // with the route's identifying info + the fault message.
            if (snap is not null)
            {
                var liveDto = RouteSummaryMapper.ToSummary(snap);
                return liveDto with
                {
                    StateName = StateFaulted,
                    Sinks = WithSinkProtocols(liveDto.Sinks, sinkProtocolById),
                    LastErrorCode = configFault?.ErrorCode ?? liveDto.LastErrorCode,
                    LastErrorMessage = configFault?.Message ?? liveDto.LastErrorMessage,
                    LastErrorAtUtc = configFault?.ObservedAtUtc ?? liveDto.LastErrorAtUtc,
                };
            }

            return new RouteSummaryDto
            {
                RouteId = route.RouteId,
                ObservedAtUtc = observedAtUtc,
                State = (int)RouteState.Failed,
                StateName = StateFaulted,
                Source = null,
                Sinks = Array.Empty<RouteSinkSummaryDto>(),
                Buffer = null,
                Pipeline = null,
                LastErrorCode = configFault!.ErrorCode,
                LastErrorMessage = configFault.Message,
                LastErrorAtUtc = configFault.ObservedAtUtc,
            };
        }

        // ─── 3. Live state from snapshot ─────────────────────────────
        if (snap is not null)
        {
            var summary = RouteSummaryMapper.ToSummary(snap);

            // Configuration = inventory truth (ADR-0002): a route's destinations
            // are exactly its configured SinkInstanceIds. A hot reload that
            // removes a sink from a route but keeps the route leaves the removed
            // sink's entry in the diagnostics snapshot — the collector prunes
            // whole routes (RemoveRoute), never individual sinks. Filtering the
            // snapshot's sinks to the configured set is the sink-level analogue
            // of the route-level "drop snapshots not in config" reconciliation
            // this builder already does, and stops a deleted destination
            // surfacing as a stale (red) chip on the Routes grid.
            var configuredSinkIds = new HashSet<string>(route.SinkInstanceIds, StringComparer.Ordinal);
            summary = summary with
            {
                Sinks = WithSinkProtocols(
                    summary.Sinks.Where(s => configuredSinkIds.Contains(s.SinkInstanceId)),
                    sinkProtocolById),
            };
            return summary;
        }

        // ─── 4. Configured / Not running ─────────────────────────────
        // Route in config, enabled, no snapshot — supervisor hasn't
        // reported state yet. Usually means the upstream source faulted
        // (route can't assemble) or the gateway just started.
        return new RouteSummaryDto
        {
            RouteId = route.RouteId,
            ObservedAtUtc = observedAtUtc,
            State = 0,
            StateName = StateConfiguredNotRunning,
            Source = null,
            Sinks = Array.Empty<RouteSinkSummaryDto>(),
            Buffer = null,
            Pipeline = null,
        };
    }
}
