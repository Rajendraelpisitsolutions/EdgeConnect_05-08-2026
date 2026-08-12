// ============================================================================
// File: Api/SinksUpdateApi.cs
// Purpose: PUT /api/v1/sinks/{instanceId} — Edit-mode wizard save endpoint
//          from M.2d.3 v2 §3. Verifies optimistic-concurrency token,
//          replaces the sink via WizardConfigMerger.BuildEditedSinkDraft
//          (which preserves Sources and Routes byte-identically), and
//          runs the new draft through the existing create → apply
//          pipeline to maintain audit-trail consistency.
//
//          Lives in its own file (not in SinksApi.cs) per the per-concern
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
/// Edit-mode wizard save endpoint registration + dispatch for sinks.
/// </summary>
public static class SinksUpdateApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>PUT /api/v1/sinks/{instanceId}</c> onto <paramref name="builder"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapSinksUpdateApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sinks").WithTags("Sinks");

        group.MapPut("/{instanceId}", async (
            string instanceId,
            UpdateSinkRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var outcome = await DispatchAsync(instanceId, body, mgr, services, ct).ConfigureAwait(false);
            return outcome;
        })
        .WithName("UpdateSink")
        .WithSummary("Edit-mode wizard save. Verifies optimistic-concurrency token, replaces the sink while preserving sources and routes byte-identically, and applies via the existing draft pipeline.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the Edit-mode save flow: version-check → existence check →
    /// BuildEditedSinkDraft → CreateDraft → ApplyDraft → resolve reload outcome.
    /// </summary>
    internal static async Task<IResult> DispatchAsync(
        string instanceId,
        UpdateSinkRequestDto? body,
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
        if (body is null || body.SinkConfig is null || string.IsNullOrWhiteSpace(body.BaseVersionId))
        {
            return Results.BadRequest(new
            {
                error = "Request body with 'sinkConfig' and 'baseVersionId' is required.",
            });
        }

        // Route param is truth; body mismatch is a programming error → 400.
        if (!string.Equals(body.SinkConfig.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = $"Route parameter instanceId '{instanceId}' does not match body.sinkConfig.InstanceId '{body.SinkConfig.InstanceId}'. The route is the source of truth in Edit mode.",
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
        for (var i = 0; i < current.Sinks.Count; i++)
        {
            if (string.Equals(current.Sinks[i].InstanceId, instanceId, StringComparison.Ordinal))
            {
                exists = true;
                break;
            }
        }
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Sink '{instanceId}' does not exist in the current configuration.",
            });
        }

        // BuildEditedSinkDraft — throws ArgumentException on ProtocolName change.
        GatewayConfiguration newDraft;
        try
        {
            newDraft = WizardConfigMerger.BuildEditedSinkDraft(current, body.SinkConfig);
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
