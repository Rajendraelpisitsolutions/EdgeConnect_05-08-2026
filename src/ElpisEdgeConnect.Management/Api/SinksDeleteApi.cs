// ============================================================================
// File: Api/SinksDeleteApi.cs
// Purpose: DELETE /api/v1/sinks/{instanceId} — remove a destination (sink) via
//          the SAME draft → apply pipeline the Edit-mode save uses
//          (SinksUpdateApi), so the config write-path invariant (draft →
//          validate → apply, audited, rollback-able) is honored. Never mutates
//          the live config directly.
//
//          Route cascade: deleting a sink strips it from every route's
//          SinkInstanceIds. A route left with NO sinks is invalid (a route must
//          dispatch to at least one sink — CrossRecordValidator rule 3), so such
//          a route is removed entirely. Sources are left in place — an unwired
//          source is valid config.
//
//          Sibling to SinksUpdateApi (per-concern file pattern). Public
//          DispatchAsync is exposed for unit testing without a
//          WebApplicationFactory.
// ============================================================================

using System;
using System.Collections.Generic;
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
/// Delete-destination endpoint registration + dispatch.
/// </summary>
public static class SinksDeleteApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>DELETE /api/v1/sinks/{instanceId}</c> onto <paramref name="builder"/>.
    /// Optional <c>baseVersionId</c> query param enforces optimistic concurrency;
    /// optional <c>actor</c> query param stamps the audit entry.
    /// </summary>
    public static IEndpointRouteBuilder MapSinksDeleteApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sinks").WithTags("Sinks");

        group.MapDelete("/{instanceId}", async (
            string instanceId,
            string? baseVersionId,
            string? actor,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            return await DispatchAsync(instanceId, baseVersionId, actor, mgr, services, ct).ConfigureAwait(false);
        })
        .WithName("DeleteSink")
        .WithSummary("Delete a destination; strips it from routes and drops any route left with no destinations. Via the draft → apply pipeline (audited, rollback-able).")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the delete flow: optional version-check → existence check (404)
    /// → drop the sink + fix up routes → CreateDraft → ApplyDraft → resolve
    /// reload outcome. Exposed <c>internal static</c> for unit tests.
    /// </summary>
    internal static async Task<IResult> DispatchAsync(
        string instanceId,
        string? baseVersionId,
        string? actor,
        IConfigurationManager mgr,
        IServiceProvider services,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mgr);
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return Results.BadRequest(new { error = "Instance id is required." });
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
        var exists = current.Sinks.Any(s =>
            string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal));
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Destination '{instanceId}' does not exist in the current configuration.",
            });
        }

        // Drop the sink.
        var newSinks = current.Sinks
            .Where(s => !string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal))
            .ToList();

        // Fix up routes: strip the sink from each route's sink list; drop any
        // route that would be left with no sinks (invalid).
        var newRoutes = new List<RouteConfig>(current.Routes.Count);
        foreach (var route in current.Routes)
        {
            if (!route.SinkInstanceIds.Contains(instanceId, StringComparer.Ordinal))
            {
                newRoutes.Add(route);
                continue;
            }

            var remaining = route.SinkInstanceIds
                .Where(id => !string.Equals(id, instanceId, StringComparison.Ordinal))
                .ToList();
            if (remaining.Count == 0)
            {
                // Route would have no destinations — remove it entirely.
                continue;
            }
            newRoutes.Add(route with { SinkInstanceIds = remaining });
        }

        var newDraft = current with { Sinks = newSinks, Routes = newRoutes };

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
