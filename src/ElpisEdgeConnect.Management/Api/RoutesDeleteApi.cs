// ============================================================================
// File: Api/RoutesDeleteApi.cs
// Purpose: DELETE /api/v1/routes/{routeId} — remove a route via the SAME
//          draft → apply pipeline the Edit-mode save uses (RoutesUpdateApi),
//          so the config write-path invariant (draft → validate → apply,
//          audited, rollback-able) is honored. Never mutates the live config
//          directly.
//
//          No cascade: a route is the link between a source and its sinks;
//          removing it leaves both endpoints in place (an unwired source/sink
//          is valid config, and they may be reused by other routes).
//
//          Sibling to RoutesUpdateApi (per-concern file pattern). Public
//          DispatchAsync is exposed for unit testing without a
//          WebApplicationFactory.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Configuration;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Contracts.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Delete-route endpoint registration + dispatch.
/// </summary>
public static class RoutesDeleteApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>DELETE /api/v1/routes/{routeId}</c> onto <paramref name="builder"/>.
    /// Optional <c>baseVersionId</c> query param enforces optimistic concurrency;
    /// optional <c>actor</c> query param stamps the audit entry.
    /// </summary>
    public static IEndpointRouteBuilder MapRoutesDeleteApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/routes").WithTags("Routes");

        group.MapDelete("/{routeId}", async (
            string routeId,
            string? baseVersionId,
            string? actor,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            return await DispatchAsync(routeId, baseVersionId, actor, mgr, services, ct).ConfigureAwait(false);
        })
        .WithName("DeleteRoute")
        .WithSummary("Delete a route (leaves its source + destinations in place), via the draft → apply pipeline (audited, rollback-able).")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the delete flow: optional version-check → existence check (404)
    /// → drop the route → CreateDraft → ApplyDraft → resolve reload outcome.
    /// Exposed <c>internal static</c> for unit tests.
    /// </summary>
    internal static async Task<IResult> DispatchAsync(
        string routeId,
        string? baseVersionId,
        string? actor,
        IConfigurationManager mgr,
        IServiceProvider services,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mgr);
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(routeId))
        {
            return Results.BadRequest(new { error = "Route id is required." });
        }

        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);

        // Optional optimistic-concurrency check.
        var currentVersionId = mgr.CurrentVersionId;
        if (!string.IsNullOrWhiteSpace(baseVersionId)
            && !string.Equals(baseVersionId, currentVersionId.Value, StringComparison.Ordinal))
        {
            return Results.Conflict(new ConfigVersionMismatchDto
            {
                BaseVersionId = baseVersionId!,
                CurrentVersionId = currentVersionId.Value,
                ChangedSinceUtc = DateTime.UtcNow,
            });
        }

        // Existence check → 404.
        var exists = current.Routes.Any(r =>
            string.Equals(r.RouteId, routeId, StringComparison.Ordinal));
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Route '{routeId}' does not exist in the current configuration.",
            });
        }

        // Drop the route. Sources and sinks are left untouched.
        var newRoutes = current.Routes
            .Where(r => !string.Equals(r.RouteId, routeId, StringComparison.Ordinal))
            .ToList();
        var newDraft = current with { Routes = newRoutes };

        var normActor = NormaliseActor(actor);
        var draftId = await mgr.CreateDraftAsync(newDraft, normActor, ct).ConfigureAwait(false);

        var previousVersion = mgr.CurrentVersionId;
        var applyResult = await mgr.ApplyDraftAsync(draftId, normActor, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            return Results.Conflict(ConfigContractMapper.ToValidationResult(applyResult.ValidationResult, DateTime.UtcNow));
        }

        var gatewayId = newDraft.Gateway.GatewayId;
        var reloadDto = await ReloadOutcomeResolver
            .ResolveAsync(services, applyResult.VersionId, ct)
            .ConfigureAwait(false);
        var apply = ConfigContractMapper.ToApplyResult(applyResult, previousVersion, gatewayId);
        return Results.Ok(apply with { Reload = reloadDto });
    }

    private static string NormaliseActor(string? actor) =>
        string.IsNullOrWhiteSpace(actor) ? DefaultActor : actor.Trim();
}
