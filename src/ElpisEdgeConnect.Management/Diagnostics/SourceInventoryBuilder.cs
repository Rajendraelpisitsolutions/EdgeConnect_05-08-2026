// ============================================================================
// File: Diagnostics/SourceInventoryBuilder.cs
// Purpose: Pure, stateless builder that produces the source-inventory
//          list for /api/v1/sources by combining
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
//          Display-state precedence (top wins) per the ChatGPT review:
//
//              1. Disabled                 — operator's strongest intent
//              2. Faulted                  — fault in registry OR live
//                                            snapshot state == Faulted
//              3. Live state from snapshot — Running / Degraded / Stopped
//              4. Configured / Not running — enabled, route exists, no
//                                            snapshot yet (initialising)
//              5. Configured               — enabled, no route, no fault
//                                            (defensive; rare in practice)
//
//          Faulted-state rows carry the fault detail in the existing
//          LastErrorCode / LastErrorMessage / LastErrorAtUtc fields on
//          RouteSourceSummaryDto, so the Studio's tooltip shows the
//          same fields regardless of whether the fault is a boot-time
//          config fault or a runtime adapter failure.
//
//          Pure: no IO, no async, no DI, no mutation of inputs. Fully
//          unit-testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestones M.2b.1.1 + M.P2.1
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>
/// Pure builder that merges configured sources with live route
/// diagnostics and registered configuration faults into the wire-shape
/// <see cref="SourceListItemDto"/> list consumed by /api/v1/sources.
/// </summary>
public static class SourceInventoryBuilder
{
    /// <summary>State name for sources with <c>Enabled = false</c> in config. Highest precedence.</summary>
    public const string StateDisabled = "Disabled";

    /// <summary>State name for sources with a registered configuration fault OR runtime AdapterState.Faulted.</summary>
    public const string StateFaulted = "Faulted";

    /// <summary>State name for enabled sources that have no route wired in config (no fault). Rare in practice with M.P2.1 fail-soft.</summary>
    public const string StateConfigured = "Configured";

    /// <summary>State name for enabled sources whose route exists in config but has not produced a diagnostics snapshot yet.</summary>
    public const string StateConfiguredNotRunning = "Configured / Not running";

    /// <summary>
    /// Build the source-inventory list. Rows are returned in
    /// configuration order, so an operator who just appended a new
    /// source via the wizard sees it at the bottom of the list.
    /// </summary>
    /// <param name="config">Current gateway configuration. Must not be null.</param>
    /// <param name="snapshots">Live route diagnostics snapshots. Must not be null; empty is valid.</param>
    /// <param name="faults">
    /// Snapshot of configuration faults from
    /// <see cref="IConfigurationFaultRegistry.GetFaults"/>. May be null
    /// (treated as empty) for callers that don't surface fault state.
    /// Only faults with <c>Kind == Source</c> are consulted here.
    /// </param>
    /// <exception cref="ArgumentNullException">When config or snapshots is null.</exception>
    public static IReadOnlyList<SourceListItemDto> Build(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);

        // Index routes by source instance id. Today the contract is
        // one-source-per-route (see RouteConfig comments + B2 cross-
        // record validation), so picking the first match is correct.
        var routesBySourceId = new Dictionary<string, RouteConfig>(
            config.Routes.Count, StringComparer.Ordinal);
        foreach (var route in config.Routes)
        {
            routesBySourceId.TryAdd(route.SourceInstanceId, route);
        }

        // Index snapshots by route id.
        var snapshotsByRouteId = new Dictionary<string, RouteHealthSnapshot>(
            snapshots.Count, StringComparer.Ordinal);
        foreach (var snap in snapshots)
        {
            snapshotsByRouteId.TryAdd(snap.RouteId, snap);
        }

        // Index source-kind faults by instance id.
        var sourceFaultsById = new Dictionary<string, ConfigurationFault>(
            faults?.Count ?? 0, StringComparer.Ordinal);
        if (faults is not null)
        {
            foreach (var f in faults)
            {
                if (f.Kind == ConfigurationFaultKind.Source)
                {
                    sourceFaultsById.TryAdd(f.InstanceId, f);
                }
            }
        }

        var rows = new List<SourceListItemDto>(config.Sources.Count);
        foreach (var src in config.Sources)
        {
            string? routeId = null;
            if (routesBySourceId.TryGetValue(src.InstanceId, out var route))
            {
                routeId = route.RouteId;
            }

            sourceFaultsById.TryGetValue(src.InstanceId, out var configFault);
            var sourceDto = BuildSourceDto(src, routeId, snapshotsByRouteId, configFault);
            rows.Add(new SourceListItemDto
            {
                RouteId = routeId,
                Source = sourceDto,
            });
        }

        return rows;
    }

    /// <summary>
    /// Find one configured source by instance id and project it with
    /// runtime enrichment + fault overlay. Returns null when the source
    /// is not in config — callers map that to 404.
    /// </summary>
    public static SourceListItemDto? BuildOne(
        GatewayConfiguration config,
        IReadOnlyList<RouteHealthSnapshot> snapshots,
        string sourceInstanceId,
        IReadOnlyList<ConfigurationFault>? faults = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(sourceInstanceId);

        foreach (var row in Build(config, snapshots, faults))
        {
            if (string.Equals(row.Source.SourceInstanceId, sourceInstanceId, StringComparison.Ordinal))
            {
                return row;
            }
        }
        return null;
    }

    private static RouteSourceSummaryDto BuildSourceDto(
        SourceInstanceConfig src,
        string? routeId,
        Dictionary<string, RouteHealthSnapshot> snapshotsByRouteId,
        ConfigurationFault? configFault)
    {
        // ─── 1. Disabled wins over everything ────────────────────────
        // Operator intent is the strongest signal. A disabled source
        // doesn't get registered (the protocol extensions skip it
        // before the route-check), so in practice it won't carry a
        // fault either — but defensively keep this branch first.
        if (!src.Enabled)
        {
            return new RouteSourceSummaryDto
            {
                SourceInstanceId = src.InstanceId,
                ProtocolName = src.ProtocolName,
                StateName = StateDisabled,
            };
        }

        // Try to pull live diagnostics — only meaningful when the source
        // has a route AND the supervisor has produced a snapshot whose
        // Source side matches this instance id.
        SourceHealthSnapshot? live = null;
        if (routeId is not null
            && snapshotsByRouteId.TryGetValue(routeId, out var snap)
            && snap.Source is { } s
            && string.Equals(s.SourceInstanceId, src.InstanceId, StringComparison.Ordinal))
        {
            live = s;
        }

        // ─── 2. Faulted ──────────────────────────────────────────────
        // Either a cross-record config fault (in the registry) or a
        // runtime adapter failure (live snapshot state == Failed). Both
        // surface with the operator-facing state="Faulted" and the
        // error in the existing Last-error fields. Config-fault wins
        // over runtime-fault on the assumption that registration-time
        // problems are usually upstream of any runtime issue. The
        // synthetic "Faulted" label differs from Core's enum
        // AdapterState.Failed: operators see consistent vocabulary
        // ("Faulted") regardless of whether the fault originated in
        // config validation or runtime adapter behavior.
        var runtimeFaulted = live is not null && live.State == AdapterState.Failed;
        if (configFault is not null || runtimeFaulted)
        {
            return new RouteSourceSummaryDto
            {
                SourceInstanceId = src.InstanceId,
                ProtocolName = src.ProtocolName,
                StateName = StateFaulted,
                PointsObserved = live?.PointsObserved ?? 0,
                LastPointAtUtc = live?.LastPointAtUtc,
                LastErrorCode = configFault?.ErrorCode ?? live?.LastError?.Code,
                LastErrorMessage = configFault?.Message ?? live?.LastError?.Message,
                LastErrorAtUtc = configFault?.ObservedAtUtc ?? live?.LastErrorAtUtc,
            };
        }

        // ─── 3. Live state from snapshot ─────────────────────────────
        if (live is not null)
        {
            return new RouteSourceSummaryDto
            {
                SourceInstanceId = live.SourceInstanceId,
                ProtocolName = live.ProtocolName,
                StateName = live.State.ToString(),
                PointsObserved = live.PointsObserved,
                LastPointAtUtc = live.LastPointAtUtc,
                LastErrorCode = live.LastError?.Code,
                LastErrorMessage = live.LastError?.Message,
                LastErrorAtUtc = live.LastErrorAtUtc,
            };
        }

        // ─── 4 + 5. Config-only fallbacks ────────────────────────────
        // No snapshot, no fault — either supervisor hasn't reported yet
        // (route exists), or there's no route in config (rare with
        // M.P2.1 fail-soft, since enabled-no-route now produces a fault).
        var stateName = routeId is null ? StateConfigured : StateConfiguredNotRunning;
        return new RouteSourceSummaryDto
        {
            SourceInstanceId = src.InstanceId,
            ProtocolName = src.ProtocolName,
            StateName = stateName,
        };
    }
}
