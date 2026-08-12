// ============================================================================
// File: Api/SourcesApi.cs
// Purpose: GET /api/v1/sources and GET /api/v1/sources/{id} —
//          read-only surface for the Sources list + detail pages.
//
//          M.2b.1.1: inventory is now CONFIG-DRIVEN. The endpoint
//          walks IConfigurationManager.Current.Sources first and
//          enriches each row with the matching RouteHealthSnapshot
//          from IDiagnosticsService when one exists. Sources that
//          are configured but not wired into a route — or wired but
//          not yet snapshotted, or simply Enabled=false — still
//          appear in the list. The decision is captured in
//
//              Configuration = inventory truth
//              Diagnostics   = runtime enrichment
//
//          (see SourceInventoryBuilder for the full rationale).
//
//          404 on GET /{id} now means "not in current config" — the
//          previous "not in diagnostics" semantics is gone because
//          diagnostics is no longer the source of inventory truth.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b / M.2b.1.1
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

// Disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager
// brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the sources read-API.
/// </summary>
public static class SourcesApi
{
    /// <summary>Map the v1 sources endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapSourcesApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources").WithTags("Sources");

        group.MapGet("/", async (
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var rows = SourceInventoryBuilder.Build(config, snapshots, faults);
            return Results.Ok(rows);
        })
        .WithName("ListSources")
        .WithSummary("List every configured source, enriched with live route diagnostics and configuration faults.")
        .Produces<SourceListItemDto[]>(StatusCodes.Status200OK);

        group.MapGet("/{sourceInstanceId}", async (
            string sourceInstanceId,
            IConfigurationManager configManager,
            IDiagnosticsService diagnostics,
            IConfigurationFaultRegistry? faultRegistry,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId))
            {
                return Results.BadRequest(new { error = "sourceInstanceId is required." });
            }

            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            var snapshots = diagnostics.GetAllRouteSnapshots();
            var faults = faultRegistry?.GetFaults();
            var row = SourceInventoryBuilder.BuildOne(config, snapshots, sourceInstanceId, faults);

            return row is null
                ? Results.NotFound(new
                {
                    error = $"Source '{sourceInstanceId}' is not in the current configuration.",
                })
                : Results.Ok(row);
        })
        .WithName("GetSource")
        .WithSummary("Get one configured source, enriched with live diagnostics when available.")
        .Produces<SourceListItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ---- Per-source diagnostic logging (opt-in) ----
        // GET the log tail + enabled state; POST enable/disable. The adapter's
        // DataIssueLog reads the same logging-enabled.json, so toggling here
        // turns on recording of the source's device connectivity + data reads.
        group.MapGet("/{sourceInstanceId}/log", (string sourceInstanceId, int? tail) =>
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId))
            {
                return Results.BadRequest(new { error = "sourceInstanceId is required." });
            }
            var max = tail is > 0 and <= 5000 ? tail.Value : 300;
            return Results.Ok(new SourceLogDto
            {
                SourceInstanceId = sourceInstanceId,
                Enabled = SourceLogService.IsEnabled(sourceInstanceId),
                Lines = SourceLogService.ReadTail(sourceInstanceId, max),
            });
        })
        .WithName("GetSourceLog")
        .WithSummary("Recent lines of a source's diagnostic log plus whether logging is enabled.")
        .Produces<SourceLogDto>(StatusCodes.Status200OK);

        group.MapPost("/{sourceInstanceId}/logging/{state}", (string sourceInstanceId, string state) =>
        {
            if (string.IsNullOrWhiteSpace(sourceInstanceId))
            {
                return Results.BadRequest(new { error = "sourceInstanceId is required." });
            }
            var enable = string.Equals(state, "enable", StringComparison.OrdinalIgnoreCase);
            var disable = string.Equals(state, "disable", StringComparison.OrdinalIgnoreCase);
            if (!enable && !disable)
            {
                return Results.BadRequest(new { error = "state must be 'enable' or 'disable'." });
            }
            SourceLogService.SetEnabled(sourceInstanceId, enable);
            return Results.Ok(new { sourceInstanceId, enabled = enable });
        })
        .WithName("SetSourceLogging")
        .WithSummary("Enable or disable diagnostic logging for a source.")
        .Produces(StatusCodes.Status200OK);

        return builder;
    }
}
