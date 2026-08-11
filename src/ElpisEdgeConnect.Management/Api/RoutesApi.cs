// ============================================================================
// File: Api/RoutesApi.cs
// Purpose: GET /api/v1/routes and GET /api/v1/routes/{id} — the
//          read-only surface the Overview page (and external HTTP
//          consumers) bind to. Reads live from IDiagnosticsService.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

// Alias to disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager,
// which is brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the routes read-API. Mapped at host
/// configuration time by <c>ManagementHostingExtensions</c>.
/// </summary>
public static class RoutesApi
{
    /// <summary>Map the v1 routes endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapRoutesApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/routes")
            .WithTags("Routes");

        // M.P2.1 phase 3: rewired to config-driven inventory via
        // RouteInventoryBuilder. Configured routes now appear regardless
        // of whether a snapshot exists; faulted routes (cross-record
        // validation failures from the IConfigurationFaultRegistry)
        // surface with state="Faulted" and the fault detail in
        // LastError* fields. Mirrors the M.2b.1.1 SourcesApi rewire.
        group.MapGet("/", async (
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var summaries = RouteInventoryBuilder.Build(config, snapshots, faults);
            return Results.Ok(summaries);
        })
        .WithName("ListRoutes")
        .WithSummary("List every configured route, enriched with live diagnostics and configuration faults.")
        .Produces<RouteSummaryDto[]>(StatusCodes.Status200OK);

        group.MapGet("/{routeId}", async (
            string routeId,
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                return Results.BadRequest(new { error = "routeId is required." });
            }
            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var row = RouteInventoryBuilder.BuildOne(config, snapshots, routeId, faults);
            return row is null
                ? Results.NotFound(new { error = $"Route '{routeId}' is not in the current configuration." })
                : Results.Ok(row);
        })
        .WithName("GetRoute")
        .WithSummary("Get one configured route, enriched with live diagnostics and configuration faults.")
        .Produces<RouteSummaryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/routes/{id}/events — merged event timeline for one route.
        // The aggregator merges state transitions + per-sink lifecycle events +
        // backpressure drops, sorts desc by timestamp, and caps the result.
        // Razor's RouteDetail page consumes this; M.1c.2's /diagnostics page
        // consumes the same wire shape (DiagnosticsEventsResponse) system-wide.
        //
        // M.1c.2 migration: this endpoint now returns DiagnosticsEventsResponse
        // (envelope shape) instead of DiagnosticsEventDto[] so it stays
        // shape-consistent with the new /api/v1/diagnostics/events endpoint.
        // The envelope adds RetainedSinceUtc + ApproximateTotalEvents so
        // operators can tell "showing all 50" from "showing 50 of ~12,000".
        // The endpoint also now stamps GatewayId on each event for future
        // fleet-aggregation scenarios.
        group.MapGet("/{routeId}/events", async (
            string routeId,
            IDiagnosticsService diagnostics,
            IRouteEventAggregator aggregator,
            IConfigurationManager configManager,
            int? limit,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                return Results.BadRequest(new { error = "routeId is required." });
            }
            if (diagnostics.GetRouteSnapshot(routeId) is null)
            {
                return Results.NotFound(new { error = $"Route '{routeId}' is not known to the diagnostics surface." });
            }
            var cap = limit is > 0 and <= 500 ? limit.Value : 50;
            var events = aggregator.GetRecentRouteEvents(routeId, cap);

            // Origin-stamp + envelope-wrap. GatewayId resolution is best-effort —
            // a not-yet-initialised config doesn't fail the endpoint, just leaves
            // the events un-stamped (federation will fill in once config loads).
            string? gatewayId = null;
            try
            {
                var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
                gatewayId = config.Gateway.GatewayId;
            }
            catch { /* config not initialised yet — graceful */ }

            IReadOnlyList<DiagnosticsEventDto> stamped = events;
            if (gatewayId is not null)
            {
                var list = new List<DiagnosticsEventDto>(events.Count);
                foreach (var e in events) list.Add(e with { GatewayId = gatewayId });
                stamped = list;
            }

            DateTime? retainedSince = events.Count > 0
                ? events[events.Count - 1].OccurredAtUtc  // events come back desc-sorted; last is oldest
                : null;

            return Results.Ok(new DiagnosticsEventsResponse
            {
                Events = stamped,
                RetainedSinceUtc = retainedSince,
                ApproximateTotalEvents = events.Count,
            });
        })
        .WithName("GetRouteEvents")
        .WithSummary("Recent events for one route (state changes, sink lifecycle, backpressure drops), newest first.")
        .Produces<DiagnosticsEventsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return builder;
    }
}
