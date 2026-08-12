// ============================================================================
// File: Api/SourcesDeleteApi.cs
// Purpose: DELETE /api/v1/sources/{instanceId} — remove a source (and any
//          routes that read from it) via the SAME draft → apply pipeline the
//          Edit-mode save uses (SourcesUpdateApi), so the config write-path
//          invariant (draft → validate → apply, audited, rollback-able) is
//          honored. Never mutates the live config directly.
//
//          Sibling to SourcesUpdateApi (per-concern file pattern). Public
//          DispatchAsync is exposed for unit testing without a
//          WebApplicationFactory, mirroring SourcesUpdateApi.DispatchAsync.
//
//          Route cascade: deleting a source removes every route whose
//          SourceInstanceId is that source (a route can have only one source,
//          so such routes would otherwise dangle). Sinks are left in place —
//          an unreferenced sink is a VALID configuration (CrossRecordValidator
//          only requires route→source / route→sink references to resolve).
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
/// Delete-source endpoint registration + dispatch.
/// </summary>
public static class SourcesDeleteApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>DELETE /api/v1/sources/{instanceId}</c> onto <paramref name="builder"/>.
    /// Optional <c>baseVersionId</c> query param enforces optimistic concurrency;
    /// optional <c>actor</c> query param stamps the audit entry.
    /// </summary>
    public static IEndpointRouteBuilder MapSourcesDeleteApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources").WithTags("Sources");

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
        .WithName("DeleteSource")
        .WithSummary("Delete a source and any routes that read from it, via the draft → apply pipeline (audited, rollback-able).")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the delete flow: optional version-check → existence check (404)
    /// → drop the source + its routes → CreateDraft → ApplyDraft → resolve
    /// reload outcome. Exposed <c>internal static</c> so unit tests can pin the
    /// branches without the full ASP.NET Core host.
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

        // Optional optimistic-concurrency check — only when the caller supplies a
        // base version. A mismatch means another session changed the config since
        // the operator's view was loaded.
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
        var exists = current.Sources.Any(s =>
            string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal));
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Source '{instanceId}' does not exist in the current configuration.",
            });
        }

        // Drop the source and every route that reads from it. Sinks are left in
        // place — an unreferenced sink is valid config.
        var newSources = current.Sources
            .Where(s => !string.Equals(s.InstanceId, instanceId, StringComparison.Ordinal))
            .ToList();
        var newRoutes = current.Routes
            .Where(r => !string.Equals(r.SourceInstanceId, instanceId, StringComparison.Ordinal))
            .ToList();
        var newDraft = current with { Sources = newSources, Routes = newRoutes };

        var normActor = NormaliseActor(actor);
        var draftId = await mgr.CreateDraftAsync(newDraft, normActor, ct).ConfigureAwait(false);

        var previousVersion = mgr.CurrentVersionId;
        var applyResult = await mgr.ApplyDraftAsync(draftId, normActor, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            // Apply-time revalidation failure — surface the structured reasons.
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
