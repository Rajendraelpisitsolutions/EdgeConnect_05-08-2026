// ============================================================================
// File: Api/RoutesUpdateApi.cs
// Purpose: PUT /api/v1/routes/{routeId} — Edit-mode wizard save endpoint
//          from M.2d.3 v2 §3. Verifies optimistic-concurrency token,
//          replaces the route via WizardConfigMerger.BuildEditedRouteDraft
//          (which preserves Sources and Sinks byte-identically), and
//          runs the new draft through the existing create → apply
//          pipeline to maintain audit-trail consistency.
//
//          Lives in its own file (not in RoutesApi.cs) per the per-concern
//          file pattern used throughout the API layer.
//
//          Public DispatchAsync is exposed for unit testing without
//          standing up a WebApplicationFactory; mirrors the
//          SourcesUpdateApi.DispatchAsync pattern.
// Reference: docs/sessions/2026-05-26-m2d3-sink-route-editors-plan-v2.md §3
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Configuration;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Contracts.Config;
using ElpisEdgeConnect.Management.Wizards;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Edit-mode wizard save endpoint registration + dispatch for routes.
/// </summary>
public static class RoutesUpdateApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>PUT /api/v1/routes/{routeId}</c> onto <paramref name="builder"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapRoutesUpdateApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/routes").WithTags("Routes");

        group.MapPut("/{routeId}", async (
            string routeId,
            UpdateRouteRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var outcome = await DispatchAsync(routeId, body, mgr, services, ct).ConfigureAwait(false);
            return outcome;
        })
        .WithName("UpdateRoute")
        .WithSummary("Edit-mode wizard save. Verifies optimistic-concurrency token, replaces the route while preserving sources and sinks byte-identically, and applies via the existing draft pipeline.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the Edit-mode save flow: version-check → existence check →
    /// BuildEditedRouteDraft → CreateDraft → ApplyDraft → resolve reload outcome.
    /// </summary>
    internal static async Task<IResult> DispatchAsync(
        string routeId,
        UpdateRouteRequestDto? body,
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
        if (body is null || body.RouteConfig is null || string.IsNullOrWhiteSpace(body.BaseVersionId))
        {
            return Results.BadRequest(new
            {
                error = "Request body with 'routeConfig' and 'baseVersionId' is required.",
            });
        }

        // Route param is truth; body mismatch is a programming error → 400.
        if (!string.Equals(body.RouteConfig.RouteId, routeId, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = $"Route parameter routeId '{routeId}' does not match body.routeConfig.RouteId '{body.RouteConfig.RouteId}'. The route parameter is the source of truth in Edit mode.",
            });
        }

        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);

        // Version mismatch → 409 + ConfigVersionMismatchDto.
        var currentVersionId = mgr.CurrentVersionId;
        if (!string.Equals(body.BaseVersionId, currentVersionId.Value, StringComparison.Ordinal))
        {
            var changedSinceUtc = await TryGetCurrentVersionAppliedAtUtcAsync(mgr, ct).ConfigureAwait(false);
            return Results.Conflict(new ConfigVersionMismatchDto
            {
                BaseVersionId = body.BaseVersionId,
                CurrentVersionId = currentVersionId.Value,
                ChangedSinceUtc = changedSinceUtc,
            });
        }

        // Existence check → 404 takes precedence over merger errors.
        var exists = false;
        for (var i = 0; i < current.Routes.Count; i++)
        {
            if (string.Equals(current.Routes[i].RouteId, routeId, StringComparison.Ordinal))
            {
                exists = true;
                break;
            }
        }
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Route '{routeId}' does not exist in the current configuration.",
            });
        }

        // BuildEditedRouteDraft — throws ArgumentException on missing source/sink references.
        GatewayConfiguration newDraft;
        try
        {
            newDraft = WizardConfigMerger.BuildEditedRouteDraft(current, body.RouteConfig);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var actor = NormaliseActor(body.Actor);
        var draftId = await mgr.CreateDraftAsync(newDraft, actor, ct).ConfigureAwait(false);

        var previousVersion = mgr.CurrentVersionId;
        var applyResult = await mgr.ApplyDraftAsync(draftId, actor, ct).ConfigureAwait(false);
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

    private static async Task<DateTime> TryGetCurrentVersionAppliedAtUtcAsync(
        IConfigurationManager mgr,
        CancellationToken ct)
    {
        try
        {
            var history = await mgr.GetHistoryAsync(ct).ConfigureAwait(false);
            for (var i = 0; i < history.Count; i++)
            {
                if (string.Equals(history[i].VersionId.Value, mgr.CurrentVersionId.Value, StringComparison.Ordinal))
                {
                    return history[i].AppliedAt;
                }
            }
        }
        catch
        {
            // History unavailable — fall back to UtcNow; 409 is still correct.
        }
        return DateTime.UtcNow;
    }
}
