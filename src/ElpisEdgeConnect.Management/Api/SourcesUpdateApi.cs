// ============================================================================
// File: Api/SourcesUpdateApi.cs
// Purpose: PUT /api/v1/sources/{instanceId} — Edit-mode wizard save endpoint
//          from M.2d.2 v2 §5.5. Verifies optimistic-concurrency token,
//          replaces the source via WizardConfigMerger.BuildUpdatedSourceDraft
//          (which preserves the Routes byte-identically per §5.5 invariant),
//          and runs the new draft through the existing create → apply
//          pipeline to maintain audit-trail consistency.
//
//          Lives in its own file (not in SourcesApi.cs) per v2 §0.1 Q4
//          verdict — the per-concern file pattern used by the probe APIs.
//
//          Public DispatchAsync is exposed for unit testing without
//          standing up a WebApplicationFactory; this mirrors the
//          EnableDisableApi.DispatchAsync pattern.
// Reference: docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md §2
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
/// Edit-mode wizard save endpoint registration + dispatch.
/// </summary>
public static class SourcesUpdateApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>PUT /api/v1/sources/{instanceId}</c> onto <paramref name="builder"/>.
    /// Sibling registration to <see cref="SourcesApi.MapSourcesApi"/>; both share
    /// the <c>/api/v1/sources</c> URL prefix but the read and write surfaces live
    /// in separate files (the per-concern pattern used by the probe APIs).
    /// </summary>
    public static IEndpointRouteBuilder MapSourcesUpdateApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources").WithTags("Sources");

        group.MapPut("/{instanceId}", async (
            string instanceId,
            UpdateSourceRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var outcome = await DispatchAsync(instanceId, body, mgr, services, ct).ConfigureAwait(false);
            return outcome;
        })
        .WithName("UpdateSource")
        .WithSummary("Edit-mode wizard save. Verifies optimistic-concurrency token, replaces the source while preserving routes byte-identically, and applies via the existing draft pipeline.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces<ConfigVersionMismatchDto>(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the Edit-mode save flow (v2 §2.4): version-check → existence
    /// check → BuildUpdatedSourceDraft → CreateDraft → ApplyDraft → resolve
    /// reload outcome. Returns the appropriate <see cref="IResult"/> per
    /// v2 §2.3 status code table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exposed as <c>internal static</c> so unit tests can pin all six branches
    /// (happy path, 400 instance-id-mismatch, 400 protocol-change, 404 not
    /// found, 409 version mismatch, route-preservation pin) without standing
    /// up the full ASP.NET Core test host.
    /// </para>
    /// </remarks>
    internal static async Task<IResult> DispatchAsync(
        string instanceId,
        UpdateSourceRequestDto? body,
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
        if (body is null || body.SourceConfig is null || string.IsNullOrWhiteSpace(body.BaseVersionId))
        {
            return Results.BadRequest(new
            {
                error = "Request body with 'sourceConfig' and 'baseVersionId' is required.",
            });
        }

        // ── v2 §2.7 Q-locked: route param is truth; mismatch is 400 ────
        if (!string.Equals(body.SourceConfig.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = $"Route parameter instanceId '{instanceId}' does not match body.sourceConfig.InstanceId '{body.SourceConfig.InstanceId}'. The route is the source of truth in Edit mode.",
            });
        }

        // ── v2 §2.4 step 3: load current ──────────────────────────────
        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);

        // ── v2 §2.4 step 4: version mismatch → 409 + ConfigVersionMismatchDto ──
        var currentVersionId = mgr.CurrentVersionId;
        if (!string.Equals(body.BaseVersionId, currentVersionId.Value, StringComparison.Ordinal))
        {
            // ChangedSinceUtc: best-effort via history lookup; falls back to
            // DateTime.UtcNow if history is unavailable (drift between hydrate
            // and save with no recorded apply in between is still a mismatch).
            var changedSinceUtc = await TryGetCurrentVersionAppliedAtUtcAsync(mgr, ct).ConfigureAwait(false);
            return Results.Conflict(new ConfigVersionMismatchDto
            {
                BaseVersionId = body.BaseVersionId,
                CurrentVersionId = currentVersionId.Value,
                ChangedSinceUtc = changedSinceUtc,
            });
        }

        // ── v2 §2.4 step 5: existence check → 404 takes precedence ────
        var exists = false;
        for (var i = 0; i < current.Sources.Count; i++)
        {
            if (string.Equals(current.Sources[i].InstanceId, instanceId, StringComparison.Ordinal))
            {
                exists = true;
                break;
            }
        }
        if (!exists)
        {
            return Results.NotFound(new
            {
                error = $"Source '{instanceId}' does not exist in the current configuration.",
            });
        }

        // ── v2 §2.4 step 6: BuildUpdatedSourceDraft (route-preserving) ─
        GatewayConfiguration newDraft;
        try
        {
            newDraft = WizardConfigMerger.BuildUpdatedSourceDraft(current, body.SourceConfig);
        }
        catch (ArgumentException ex)
        {
            // ProtocolName change or other merger-enforced invariant
            // violation. Surfaced as 400 — the wizard's Edit UI should
            // disable these inputs but server-side enforcement is the
            // defence-in-depth pin.
            return Results.BadRequest(new { error = ex.Message });
        }

        // ── v2 §2.4 step 7-8: persist draft + apply ───────────────────
        var actor = NormaliseActor(body.Actor);
        var draftId = await mgr.CreateDraftAsync(newDraft, actor, ct).ConfigureAwait(false);

        var previousVersion = mgr.CurrentVersionId;
        var applyResult = await mgr.ApplyDraftAsync(draftId, actor, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            // Apply-time revalidation failure (the other 409 case). Body
            // is ValidationResultDto so the wizard can distinguish from
            // the version-mismatch 409 by reading the conflictType
            // discriminator (absent on this shape).
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

    /// <summary>
    /// Best-effort lookup of the UTC timestamp the current version became
    /// active, used for the <see cref="ConfigVersionMismatchDto.ChangedSinceUtc"/>
    /// field. Falls back to <see cref="DateTime.UtcNow"/> when history is
    /// unavailable; the 409 is correct either way, just less precise on
    /// fallback.
    /// </summary>
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
            // Audit / history corrupt or not yet initialised — operator's
            // banner still informs them the config changed; the
            // timestamp is just less precise.
        }
        return DateTime.UtcNow;
    }
}
