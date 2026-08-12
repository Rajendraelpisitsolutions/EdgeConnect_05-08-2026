// ============================================================================
// File: Diagnostics/SinkInventoryBuilder.cs
// Purpose: Pure, stateless builder that produces the sink-inventory
//          list for /api/v1/sinks by combining
//             (a) the current GatewayConfiguration  — inventory truth
//             (b) live RouteHealthSnapshot list      — runtime enrichment
//             (c) IConfigurationFaultRegistry faults — fault overrides
//
//          Architectural pattern (locked at M.2b.1.1, extended at M.P2.1):
//
//              Configuration = inventory truth
//              Diagnostics   = runtime enrichment
//              Faults        = registry overrides (highest-priority
//                              after operator-intent Disabled)
//
//          Display-state precedence (top wins) — mirrors source/route
//          builders so all three inventory surfaces behave identically:
//
//              1. Disabled                 — operator's strongest intent
//              2. Faulted                  — fault in registry OR live
//                                            snapshot state == Failed
//              3. Live state from snapshot — Running / Degraded / Stopped
//              4. Configured / Not running — enabled, route(s) exist, no
//                                            snapshot yet
//              5. Configured               — enabled, no route references
//                                            (rare; sink exists without
//                                            being referenced by a route)
//
//          ── Difference from SourceInventoryBuilder ──
//          A sink can be referenced by MANY routes (RouteConfig.
//          SinkInstanceIds is a list per route). Sources have at most
//          one route apiece (B2 cross-record validation), so the source
//          builder emits one row per source with at most one RouteId.
//          The sink builder emits one row per sink with a RouteIds
//          list — the rare but legal case of a sink wired into multiple
//          routes shows up as one row with chips for each route.
//
//          ── Runtime-state aggregation ──
//          When a sink is referenced by N routes, N RouteHealthSnapshot
//          entries can each carry a SinkHealthSnapshot for it. The
//          underlying adapter is a singleton instance, so the snapshots
//          report on the same runtime object — they should agree. We
//          pick the first matching snapshot in route-enumeration order
//          and document it; defensively, if the first snapshot is
//          Running and a later one is Failed, we still surface Failed
//          via the "any-Failed → Faulted" check below so an unhealthy
//          report never gets masked.
//
//          Pure: no IO, no async, no DI, no mutation of inputs. Fully
//          unit-testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.P2.1 phase 3b
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Pure builder that merges configured sinks with live route diagnostics
/// and registered configuration faults into the wire-shape
/// <see cref="SinkListItemDto"/> list consumed by /api/v1/sinks.
/// </summary>
public static class SinkInventoryBuilder
{
    /// <summary>State name for sinks with <c>Enabled = false</c> in config. Highest precedence.</summary>
    public const string StateDisabled = "Disabled";

    /// <summary>State name for sinks with a registered configuration fault OR runtime AdapterState.Failed.</summary>
    public const string StateFaulted = "Faulted";

    /// <summary>State name for enabled sinks with no route references (no fault). Rare in practice.</summary>
    public const string StateConfigured = "Configured";

    /// <summary>State name for enabled sinks whose route(s) exist in config but have not produced a diagnostics snapshot yet.</summary>
    public const string StateConfiguredNotRunning = "Configured / Not running";

    /// <summary>Marker for the protocol kind when nothing in config resolves it (defensive; same vocabulary as SinksApi pre-rewire).</summary>
    public const string UnknownKind = "unknown";

    /// <summary>
    /// Build the sink-inventory list. Rows are returned in configuration
    /// order so an operator who just appended a destination via the
    /// (future) wizard sees it at the bottom of the list.
    /// </summary>
    /// <param name="config">Current gateway configuration. Must not be null.</param>
    /// <param name="snapshots">Live route diagnostics snapshots. Must not be null; empty is valid.</param>
    /// <param name="faults">
    /// Snapshot of configuration faults from
    /// <see cref="IConfigurationFaultRegistry.GetFaults"/>. May be null
    /// (treated as empty) for callers that don't surface fault state.
    /// Only faults with <c>Kind == Sink</c> are consulted here.
    /// </param>
    /// <exception cref="ArgumentNullException">When config or snapshots is null.</exception>
    public static IReadOnlyList<SinkListItemDto> Build(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);

        // Index routes by sink instance id — a sink can appear in many
        // routes' SinkInstanceIds lists. The result list per sink is
        // built in route-enumeration order so behaviour is deterministic.
        var routeIdsBySinkId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var route in config.Routes)
        {
            foreach (var sinkId in route.SinkInstanceIds)
            {
                if (!routeIdsBySinkId.TryGetValue(sinkId, out var list))
                {
                    list = new List<string>(1);
                    routeIdsBySinkId[sinkId] = list;
                }
                list.Add(route.RouteId);
            }
        }

        // Index sink snapshots by sink instance id. Multiple routes can
        // contribute a snapshot for the same sink; we accumulate them
        // and pick deterministically below.
        //
        // Drop snapshots whose route is no longer in config (ADR-0002:
        // configuration is inventory truth). A removed or renamed route
        // leaves a stale snapshot behind in GetAllRouteSnapshots until the
        // producer side (RuntimeDiagnosticsCollector.RemoveRoute) prunes it,
        // and that stale snapshot usually reports the sink as Stopped — the
        // route was stopped just before removal. Without this filter the sink
        // page picks the stale Stopped record over the live Running one and
        // paints a healthy destination red. RouteInventoryBuilder is already
        // immune because it joins snapshots to config routes.
        var liveRouteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in config.Routes)
        {
            liveRouteIds.Add(route.RouteId);
        }

        var snapshotsBySinkId = new Dictionary<string, List<SinkHealthSnapshot>>(StringComparer.Ordinal);
        foreach (var snap in snapshots)
        {
            if (!liveRouteIds.Contains(snap.RouteId))
            {
                continue; // stale snapshot from a route no longer in config
            }
            foreach (var sink in snap.Sinks)
            {
                if (!snapshotsBySinkId.TryGetValue(sink.SinkInstanceId, out var list))
                {
                    list = new List<SinkHealthSnapshot>(1);
                    snapshotsBySinkId[sink.SinkInstanceId] = list;
                }
                list.Add(sink);
            }
        }

        // Index sink-kind faults by instance id.
        var sinkFaultsById = new Dictionary<string, ConfigurationFault>(
            faults?.Count ?? 0, StringComparer.Ordinal);
        if (faults is not null)
        {
            foreach (var f in faults)
            {
                if (f.Kind == ConfigurationFaultKind.Sink)
                {
                    sinkFaultsById.TryAdd(f.InstanceId, f);
                }
            }
        }

        var rows = new List<SinkListItemDto>(config.Sinks.Count);
        foreach (var sink in config.Sinks)
        {
            routeIdsBySinkId.TryGetValue(sink.InstanceId, out var routeIds);
            snapshotsBySinkId.TryGetValue(sink.InstanceId, out var sinkSnapshots);
            sinkFaultsById.TryGetValue(sink.InstanceId, out var configFault);

            rows.Add(BuildOne(sink, routeIds, sinkSnapshots, configFault));
        }

        return rows;
    }

    /// <summary>
    /// Find one configured sink by instance id and project it with
    /// runtime enrichment + fault overlay. Returns null when the sink
    /// is not in config — callers map that to 404.
    /// </summary>
    public static SinkListItemDto? BuildOne(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        string sinkInstanceId,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(sinkInstanceId);

        foreach (var row in Build(config, snapshots, faults))
        {
            if (string.Equals(row.Sink.SinkInstanceId, sinkInstanceId, StringComparison.Ordinal))
            {
                return row;
            }
        }
        return null;
    }

    private static SinkListItemDto BuildOne(
        SinkInstanceConfig sink,
        List<string>? routeIds,
        List<SinkHealthSnapshot>? sinkSnapshots,
        ConfigurationFault? configFault)
    {
        var routeIdList = (IReadOnlyList<string>?)routeIds ?? Array.Empty<string>();

        // ─── 1. Disabled wins over everything ────────────────────────
        if (!sink.Enabled)
        {
            return new SinkListItemDto
            {
                RouteIds = routeIdList,
                SinkKind = sink.ProtocolName,
                Sink = new RouteSinkSummaryDto
                {
                    SinkInstanceId = sink.InstanceId,
                    AdapterStateName = StateDisabled,
                },
                Sessions = null,
            };
        }

        // Pick a representative live snapshot. We prefer any snapshot
        // reporting AdapterState.Failed so a degraded report never gets
        // hidden behind a healthier sibling snapshot; otherwise we take
        // the first one in route-enumeration order. Same instance under
        // multiple routes => snapshots should agree, but defensively
        // prefer worst.
        SinkHealthSnapshot? live = null;
        if (sinkSnapshots is not null)
        {
            foreach (var s in sinkSnapshots)
            {
                if (s.AdapterState == AdapterState.Failed)
                {
                    live = s;
                    break;
                }
                live ??= s;
            }
        }

        // ─── 2. Faulted ──────────────────────────────────────────────
        // Either a cross-record config fault, or a runtime adapter
        // failure reflected in the snapshot. Both surface as the
        // operator-facing "Faulted" label. Config-fault wins over
        // runtime-fault on the assumption that registration-time
        // problems are upstream of any runtime issue.
        var runtimeFaulted = live is not null && live.AdapterState == AdapterState.Failed;
        if (configFault is not null || runtimeFaulted)
        {
            return new SinkListItemDto
            {
                RouteIds = routeIdList,
                SinkKind = sink.ProtocolName,
                Sink = new RouteSinkSummaryDto
                {
                    SinkInstanceId = sink.InstanceId,
                    AdapterStateName = StateFaulted,
                    IsDegraded = live?.IsDegraded ?? false,
                    IsDraining = live?.IsDraining ?? false,
                    DegradationEventCount = live?.DegradationEventCount ?? 0,
                    RecoveryEventCount = live?.RecoveryEventCount ?? 0,
                    ActiveSessionCount = live?.ActiveSessions?.Count,
                    LastErrorCode = configFault?.ErrorCode ?? live?.LastError?.Code,
                    LastErrorMessage = configFault?.Message ?? live?.LastError?.Message,
                    LastErrorAtUtc = configFault?.ObservedAtUtc ?? live?.LastErrorAtUtc,
                },
                Sessions = live is not null ? RouteSummaryMapper.MapSessions(live) : null,
            };
        }

        // ─── 3. Live state from snapshot ─────────────────────────────
        if (live is not null)
        {
            return new SinkListItemDto
            {
                RouteIds = routeIdList,
                SinkKind = sink.ProtocolName,
                Sink = RouteSummaryMapper.MapSinkSummary(live),
                Sessions = RouteSummaryMapper.MapSessions(live),
            };
        }

        // ─── 4 + 5. Config-only fallbacks ────────────────────────────
        var stateName = routeIdList.Count == 0 ? StateConfigured : StateConfiguredNotRunning;
        return new SinkListItemDto
        {
            RouteIds = routeIdList,
            SinkKind = sink.ProtocolName,
            Sink = new RouteSinkSummaryDto
            {
                SinkInstanceId = sink.InstanceId,
                AdapterStateName = stateName,
            },
            Sessions = null,
        };
    }
}
