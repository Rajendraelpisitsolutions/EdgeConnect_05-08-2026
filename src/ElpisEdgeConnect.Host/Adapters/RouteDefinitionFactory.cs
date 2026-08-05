// ============================================================================
// File: Adapters/RouteDefinitionFactory.cs
// Purpose: Builds IEnumerable<RouteDefinition> by correlating the loaded
//          GatewayConfiguration with the supervised source intakes and
//          the registered sink adapters. Single source of route wiring —
//          HostStartup.RegisterRoutes is the ONLY caller at boot time;
//          M.P2.2 phase 2.c's RuntimeReloadCoordinator will be the second
//          caller (via BuildOne) for per-route reconciliation.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D, ARCHITECTURE_BLUEPRINT.md §4.4
// Milestone: D — phase 6.
//
// LOCKED design rules:
//   * One and only one route-registration path. No test shortcut that
//     hand-wires RouteDefinitions outside this factory is permitted.
//   * Missing-source / missing-sink errors fail FAIL-SOFT at factory time:
//     register the route as faulted, skip it, continue. Per M.P2.1
//     fail-soft startup, the gateway must not crash on cross-record
//     validation failures.
//   * The factory is stateless — it takes the config, the source
//     registry, and the sink lookup as inputs and returns a list (or
//     a single RouteDefinition via BuildOne). The composition root
//     constructs it once; HostStartup calls Build once per start-up
//     pass; the coordinator (phase 2.c) will call BuildOne per
//     reconcile-add-route action.
//
//   * M.P2.2 phase 2.b additions: BuildOne extracted as a per-route
//     entry point. The Build method now iterates config.Routes and
//     delegates to BuildOne. Pure refactor — boot-time behavior is
//     bit-identical externally:
//        - Disabled routes return null (no fault registered).
//        - Missing-source routes register CONFIG.ROUTE_REFERENCES_MISSING_SOURCE
//          and return null.
//        - Missing-sink routes register CONFIG.ROUTE_REFERENCES_MISSING_SINK
//          and return null.
//     Each per-route branch matches the previous foreach body bit-for-bit.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Correlates a loaded <see cref="GatewayConfiguration"/> with the
/// supervised source intakes and registered sink adapters to produce
/// runtime <see cref="RouteDefinition"/> instances.
/// </summary>
public sealed class RouteDefinitionFactory
{
    /// <summary>
    /// Build one <see cref="RouteDefinition"/> per enabled route in the
    /// configuration. Routes referencing a missing source or missing sink
    /// are SKIPPED (per M.P2.1 fail-soft startup) — a
    /// <see cref="ConfigurationFault"/> is registered for each so the
    /// operator sees the problem in Studio, and the rest of the configuration
    /// boots cleanly. The previous fail-fast behavior (throw) crashed the
    /// gateway with no in-product recovery path.
    /// </summary>
    /// <param name="config">The loaded gateway configuration.</param>
    /// <param name="sourceRegistry">Supervisor-owned source intake lookup.</param>
    /// <param name="sinkLookup">Map of sink instance id → sink adapter.</param>
    /// <param name="faultRegistry">
    /// Optional fault registry. When supplied, routes that reference missing
    /// sources or sinks are skipped and a fault is registered; when null,
    /// the method silently skips invalid routes (still no throw).
    /// </param>
    public IReadOnlyList<RouteDefinition> Build(
        GatewayConfiguration config,
        ISupervisedSourceRegistry sourceRegistry,
        IReadOnlyDictionary<string, ISinkAdapter> sinkLookup,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sourceRegistry);
        ArgumentNullException.ThrowIfNull(sinkLookup);

        var result = new List<RouteDefinition>(config.Routes.Count);
        foreach (var route in config.Routes)
        {
            var def = BuildOne(route, config.Gateway, sourceRegistry, sinkLookup, faultRegistry);
            if (def is not null)
            {
                result.Add(def);
            }
        }

        return result;
    }

    /// <summary>
    /// M.P2.2 phase 2.b — per-route entry point. Build a
    /// <see cref="RouteDefinition"/> for one specific route, or return
    /// <c>null</c> when the route should be skipped:
    /// <list type="bullet">
    ///   <item><description>Disabled route → null, no fault registered.</description></item>
    ///   <item><description>Missing source → <c>CONFIG.ROUTE_REFERENCES_MISSING_SOURCE</c> registered, null returned.</description></item>
    ///   <item><description>Missing sink → <c>CONFIG.ROUTE_REFERENCES_MISSING_SINK</c> registered, null returned.</description></item>
    /// </list>
    /// Used by <see cref="Build"/> for boot-time wiring and by the
    /// <c>RuntimeReloadCoordinator</c> (phase 2.c) for per-route
    /// reconciliation.
    /// </summary>
    /// <param name="route">The single route to build.</param>
    /// <param name="gateway">Gateway-level settings (needed for <c>GatewayId</c> stamping).</param>
    /// <param name="sourceRegistry">Supervisor-owned source intake lookup.</param>
    /// <param name="sinkLookup">Map of sink instance id → sink adapter.</param>
    /// <param name="faultRegistry">Optional fault registry. When null, invalid routes return null silently.</param>
    public RouteDefinition? BuildOne(
        RouteConfig route,
        GatewaySettings gateway,
        ISupervisedSourceRegistry sourceRegistry,
        IReadOnlyDictionary<string, ISinkAdapter> sinkLookup,
        IConfigurationFaultRegistry? faultRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(sourceRegistry);
        ArgumentNullException.ThrowIfNull(sinkLookup);

        if (!route.Enabled)
        {
            return null;
        }

        var intake = sourceRegistry.GetIntake(route.SourceInstanceId);
        if (intake is null)
        {
            // M.P2.1 fail-soft: route references a source that didn't
            // make it into the supervisor's registry (either the source
            // was disabled in config, or it was faulted at registration
            // time and skipped). Register the route as faulted and skip.
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Route,
                InstanceId = route.RouteId,
                ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SOURCE",
                Message = $"Route '{route.RouteId}' references source '{route.SourceInstanceId}' but no such enabled source is registered. The source may be disabled, faulted, or absent from the config.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        var sinks = new List<ISinkAdapter>(route.SinkInstanceIds.Count);
        var missingSinkId = (string?)null;
        foreach (var sinkId in route.SinkInstanceIds)
        {
            if (!sinkLookup.TryGetValue(sinkId, out var sink))
            {
                missingSinkId = sinkId;
                break;
            }
            sinks.Add(sink);
        }

        if (missingSinkId is not null)
        {
            // Same pattern for missing destinations.
            faultRegistry?.Register(new ConfigurationFault
            {
                Kind = ConfigurationFaultKind.Route,
                InstanceId = route.RouteId,
                ErrorCode = "CONFIG.ROUTE_REFERENCES_MISSING_SINK",
                Message = $"Route '{route.RouteId}' references destination '{missingSinkId}' but no such enabled destination is registered. The destination may be disabled, faulted, or absent from the config.",
                ObservedAtUtc = DateTime.UtcNow,
            });
            return null;
        }

        return new RouteDefinition
        {
            RouteId = route.RouteId,
            GatewayId = gateway.GatewayId,
            Source = intake,
            Sinks = sinks,
            Filter = new TagFilter(route.Filter),
            BufferPolicy = BufferPolicy.FromConfig(route.Buffer),
            Delivery = route.Delivery,
            // Pipeline = null for phase 6; transform wiring is outside
            // the integration-scenario scope and lands with D4 / full
            // host hardening.
        };
    }
}
