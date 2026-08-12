// ============================================================================
// File: Api/SinksApi.cs
// Purpose: GET /api/v1/sinks and GET /api/v1/sinks/{id} — read-only
//          surface for the Destinations list + detail pages.
//
//          M.P2.1 phase 3b: inventory is now CONFIG-DRIVEN, mirroring
//          the M.2b.1.1 Sources rewire. The endpoint walks
//          IConfigurationManager.Current.Sinks first and enriches each
//          row with the matching SinkHealthSnapshot from
//          IDiagnosticsService + any registered ConfigurationFault.
//          Sinks that are configured but not wired into any route — or
//          wired but not yet snapshotted, or simply Enabled=false —
//          still appear in the list. The decision pattern is
//
//              Configuration = inventory truth
//              Diagnostics   = runtime enrichment
//              Faults        = registry overrides (Disabled > Faulted)
//
//          See SinkInventoryBuilder for the full rationale + decision
//          table.
//
//          404 on GET /{id} now means "not in current configuration"
//          (was "not known to the diagnostics surface" pre-M.P2.1).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b.3 + M.P2.1
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// Alias to disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager,
// which is brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the sinks (Destinations) read-API.
/// </summary>
public static class SinksApi
{
    /// <summary>Marker for sinks the loaded config doesn't recognise (typically a startup race).</summary>
    public const string UnknownKind = SinkInventoryBuilder.UnknownKind;

    /// <summary>Map the v1 sinks endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapSinksApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sinks").WithTags("Sinks");

        group.MapGet("/", async (
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var rows = SinkInventoryBuilder.Build(config, snapshots, faults);
            return Results.Ok(rows);
        })
        .WithName("ListSinks")
        .WithSummary("List every configured destination, enriched with live diagnostics and configuration faults.")
        .Produces<SinkListItemDto[]>(StatusCodes.Status200OK);

        group.MapGet("/{sinkInstanceId}", async (
            string sinkInstanceId,
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sinkInstanceId))
            {
                return Results.BadRequest(new { error = "sinkInstanceId is required." });
            }

            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var row = SinkInventoryBuilder.BuildOne(config, snapshots, sinkInstanceId, faults);

            return row is null
                ? Results.NotFound(new
                {
                    error = $"Destination '{sinkInstanceId}' is not in the current configuration.",
                })
                : Results.Ok(row);
        })
        .WithName("GetSink")
        .WithSummary("Get one configured destination, enriched with live diagnostics when available.")
        .Produces<SinkListItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return builder;
    }
}
