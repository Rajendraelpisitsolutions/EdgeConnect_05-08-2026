// ============================================================================
// File: Api/ConfigApi.cs
// Purpose: The configuration write-path — the first set of POST/DELETE
//          endpoints in the management layer. Maps cleanly to
//          IConfigurationManager's draft / validate / apply / discard /
//          rollback lifecycle.
//
//          Every endpoint is thin: parse the body, call Core, project
//          the result via ConfigContractMapper, return the right
//          status code. No business logic lives here — Core owns the
//          state machine.
//
//          Concurrency is handled by Core's internal mutex; concurrent
//          POSTs to apply/rollback serialise correctly without extra
//          locking in the API layer.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Management.Configuration;
using ElpisEdgeConnect.Management.Contracts.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// Disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager
// and Microsoft.Extensions.Hosting.HostOptions (both brought into scope
// implicitly by the ASP.NET Core Web SDK global usings).
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the configuration draft / apply / rollback API.
/// </summary>
public static class ConfigApi
{
    private const string DefaultActor = "system";

    /// <summary>Map the v1 config endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapConfigApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/config").WithTags("Configuration");

        // ─── Read: current ──────────────────────────────────────────────
        group.MapGet("/", async (IConfigurationManager mgr, CancellationToken ct) =>
        {
            var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);
            return Results.Ok(current);
        })
        .WithName("GetCurrentConfig")
        .WithSummary("Get the current active gateway configuration (same shape as gateway.json).")
        .Produces<GatewayConfiguration>(StatusCodes.Status200OK);

        group.MapGet("/version", async (
            IConfigurationManager mgr,
            HostOptions host,
            CancellationToken ct) =>
        {
            // M.2b.6.2 §3.B — endpoint body extracted into the static
            // helper below so the config-path + override surfacing can
            // be unit-tested without standing up a WebApplicationFactory.
            // The env-var lookup is passed in so tests can drive it
            // deterministically without touching the process env.
            var dto = await BuildCurrentConfigVersionDtoAsync(
                mgr, host, Environment.GetEnvironmentVariable, ct).ConfigureAwait(false);
            return Results.Ok(dto);
        })
        .WithName("GetCurrentConfigVersion")
        .WithSummary("Get metadata about the currently active configuration version.")
        .Produces<CurrentConfigVersionDto>(StatusCodes.Status200OK);

        // ─── Read: drafts list ──────────────────────────────────────────
        group.MapGet("/drafts", async (
            IConfigurationManager mgr,
            HostOptions host,
            CancellationToken ct) =>
        {
            var draftIds = await mgr.ListDraftsAsync(ct).ConfigureAwait(false);
            var layout = new ConfigurationStorageLayout(host.ResolvedDataRoot);
            var auditLookup = await BuildDraftAuditLookupAsync(mgr, ct).ConfigureAwait(false);
            var gatewayId = await TryGetGatewayIdAsync(mgr, ct).ConfigureAwait(false);

            var items = new List<DraftMetadataDto>(draftIds.Count);
            foreach (var id in draftIds)
            {
                long sizeBytes = 0;
                var fallbackCreatedAt = DateTime.UtcNow;
                try
                {
                    var info = new FileInfo(layout.DraftPath(id));
                    if (info.Exists)
                    {
                        sizeBytes = info.Length;
                        fallbackCreatedAt = info.CreationTimeUtc;
                    }
                }
                catch { /* tolerate filesystem races — actor + audit-timestamp lookup is the source of truth anyway */ }

                items.Add(ConfigContractMapper.ToDraftMetadata(
                    id, sizeBytes, fallbackCreatedAt, auditLookup, gatewayId));
            }
            return Results.Ok(items);
        })
        .WithName("ListConfigDrafts")
        .WithSummary("List all persisted drafts with their metadata.")
        .Produces<DraftMetadataDto[]>(StatusCodes.Status200OK);

        // ─── Read: single draft body ────────────────────────────────────
        group.MapGet("/drafts/{draftId}", async (string draftId, IConfigurationManager mgr, CancellationToken ct) =>
        {
            if (!TryParseDraftId(draftId, out var typed, out var error))
            {
                return Results.BadRequest(new { error });
            }
            var draft = await mgr.GetDraftAsync(typed, ct).ConfigureAwait(false);
            if (draft is null)
            {
                return Results.NotFound(new { error = $"Draft '{draftId}' is not known." });
            }
            return Results.Ok(draft);
        })
        .WithName("GetConfigDraft")
        .WithSummary("Get the full configuration body of one draft.")
        .Produces<GatewayConfiguration>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Read: diff between draft and current ───────────────────────
        group.MapGet("/drafts/{draftId}/diff", async (string draftId, IConfigurationManager mgr, CancellationToken ct) =>
        {
            if (!TryParseDraftId(draftId, out var typed, out var error))
            {
                return Results.BadRequest(new { error });
            }
            var draft = await mgr.GetDraftAsync(typed, ct).ConfigureAwait(false);
            if (draft is null)
            {
                return Results.NotFound(new { error = $"Draft '{draftId}' is not known." });
            }
            var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);
            var changes = ConfigurationDiffer.Instance.Diff(current, draft);
            return Results.Ok(ConfigContractMapper.ToConfigChanges(changes));
        })
        .WithName("GetConfigDraftDiff")
        .WithSummary("Diff a draft against the current active configuration. Same shape the audit log records on apply.")
        .Produces<ConfigChangeDto[]>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Read: history list ─────────────────────────────────────────
        group.MapGet("/history", async (IConfigurationManager mgr, CancellationToken ct) =>
        {
            var history = await mgr.GetHistoryAsync(ct).ConfigureAwait(false);
            var currentVersion = mgr.CurrentVersionId;
            var items = history
                .Select(e => ConfigContractMapper.ToHistoryEntry(e, isCurrent: e.VersionId.Value == currentVersion.Value))
                .ToList();
            return Results.Ok(items);
        })
        .WithName("ListConfigHistory")
        .WithSummary("List applied configuration versions newest first.")
        .Produces<HistoryEntryDto[]>(StatusCodes.Status200OK);

        // ─── Write: create draft ────────────────────────────────────────
        group.MapPost("/drafts", async (
            CreateDraftRequestDto body,
            IConfigurationManager mgr,
            CancellationToken ct) =>
        {
            if (body is null || body.Configuration is null)
            {
                return Results.BadRequest(new { error = "Request body with 'configuration' is required." });
            }
            var actor = NormaliseActor(body.Actor);
            var draftId = await mgr.CreateDraftAsync(body.Configuration, actor, ct).ConfigureAwait(false);
            return Results.Created($"/api/v1/config/drafts/{draftId.Value}", new { draftId = draftId.Value });
        })
        .WithName("CreateConfigDraft")
        .WithSummary("Persist a new draft configuration. Draft is NOT validated at create time — call /validate to check.")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // ─── Write: validate draft ──────────────────────────────────────
        group.MapPost("/drafts/{draftId}/validate", async (
            string draftId,
            IConfigurationManager mgr,
            CancellationToken ct) =>
        {
            if (!TryParseDraftId(draftId, out var typed, out var error))
            {
                return Results.BadRequest(new { error });
            }
            if (await mgr.GetDraftAsync(typed, ct).ConfigureAwait(false) is null)
            {
                return Results.NotFound(new { error = $"Draft '{draftId}' is not known." });
            }
            var result = await mgr.ValidateDraftAsync(typed, ct).ConfigureAwait(false);
            return Results.Ok(ConfigContractMapper.ToValidationResult(result, DateTime.UtcNow));
        })
        .WithName("ValidateConfigDraft")
        .WithSummary("Run validation on a draft without applying. No state change.")
        .Produces<ValidationResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Write: apply draft ─────────────────────────────────────────
        group.MapPost("/drafts/{draftId}/apply", async (
            string draftId,
            ActorRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if (!TryParseDraftId(draftId, out var typed, out var error))
            {
                return Results.BadRequest(new { error });
            }
            if (await mgr.GetDraftAsync(typed, ct).ConfigureAwait(false) is null)
            {
                return Results.NotFound(new { error = $"Draft '{draftId}' is not known." });
            }
            var previousVersion = mgr.CurrentVersionId;
            var actor = NormaliseActor(body?.Actor);
            var result = await mgr.ApplyDraftAsync(typed, actor, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                // 409 Conflict — the draft failed revalidation inside the
                // apply mutex. Body carries the structured reasons.
                return Results.Conflict(ConfigContractMapper.ToValidationResult(result.ValidationResult, DateTime.UtcNow));
            }
            var gatewayId = await TryGetGatewayIdAsync(mgr, ct).ConfigureAwait(false);
            // M.P2.2 phase 3: await the bounded reload-outcome window so
            // the response carries Reload alongside the apply confirmation.
            var reloadDto = await ReloadOutcomeResolver
                .ResolveAsync(services, result.VersionId, ct)
                .ConfigureAwait(false);
            var apply = ConfigContractMapper.ToApplyResult(result, previousVersion, gatewayId);
            return Results.Ok(apply with { Reload = reloadDto });
        })
        .WithName("ApplyConfigDraft")
        .WithSummary("Apply a draft as the new current configuration. Re-validates under the apply mutex.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces<ValidationResultDto>(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Write: discard draft ──────────────────────────────────────
        group.MapDelete("/drafts/{draftId}", async (
            string draftId,
            IConfigurationManager mgr,
            CancellationToken ct) =>
        {
            if (!TryParseDraftId(draftId, out var typed, out var error))
            {
                return Results.BadRequest(new { error });
            }
            // Existence check before delegating — Core's DiscardDraftAsync
            // is a no-op for unknown ids, but the API surface returns 404
            // to make "wrong draft id" operator-visible.
            if (await mgr.GetDraftAsync(typed, ct).ConfigureAwait(false) is null)
            {
                return Results.NotFound(new { error = $"Draft '{draftId}' is not known." });
            }
            // Actor not pulled from body — DELETE method conventionally
            // doesn't carry one; the audit entry records "system" today.
            // When auth lands, HttpContext.User.Identity.Name flows in.
            await mgr.DiscardDraftAsync(typed, DefaultActor, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DiscardConfigDraft")
        .WithSummary("Discard a draft. Returns 404 when the draft id is not known (deliberately non-tolerant to surface operator mistakes).")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        // ─── Write: rollback ───────────────────────────────────────────
        group.MapPost("/history/{versionId}/rollback", async (
            string versionId,
            ActorRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            ConfigurationVersionId typed;
            try { typed = new ConfigurationVersionId(versionId); }
            catch (ArgumentException) { return Results.BadRequest(new { error = "Invalid versionId." }); }

            var previousVersion = mgr.CurrentVersionId;
            var actor = NormaliseActor(body?.Actor);
            var result = await mgr.RollbackAsync(typed, actor, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                return Results.Conflict(ConfigContractMapper.ToValidationResult(result.ValidationResult, DateTime.UtcNow));
            }
            var gatewayId = await TryGetGatewayIdAsync(mgr, ct).ConfigureAwait(false);
            // Rollback is an apply under the hood — same reload-outcome
            // surface as the apply endpoint above (phase 3 plan v2 §5.3).
            var reloadDto = await ReloadOutcomeResolver
                .ResolveAsync(services, result.VersionId, ct)
                .ConfigureAwait(false);
            var apply = ConfigContractMapper.ToApplyResult(result, previousVersion, gatewayId);
            return Results.Ok(apply with { Reload = reloadDto });
        })
        .WithName("RollbackConfig")
        .WithSummary("Roll back to a previous version. Treated as a new apply — produces a fresh version id and audit entry.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces<ValidationResultDto>(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status400BadRequest);

        return builder;
    }

    /// <summary>Normalise the actor field — null/whitespace becomes <c>"system"</c>.</summary>
    internal static string NormaliseActor(string? actor) =>
        string.IsNullOrWhiteSpace(actor) ? DefaultActor : actor.Trim();

    /// <summary>
    /// Build the <see cref="CurrentConfigVersionDto"/> the
    /// <c>GET /api/v1/config/version</c> endpoint returns. Extracted
    /// from the endpoint lambda so the config-path + override
    /// surfacing logic (M.2b.6.2 §3.B) is unit-testable without
    /// spinning up an in-process web host. The env-var lookup is
    /// passed in so callers can drive it deterministically.
    /// </summary>
    internal static async Task<CurrentConfigVersionDto> BuildCurrentConfigVersionDtoAsync(
        IConfigurationManager mgr,
        HostOptions host,
        Func<string, string?> envLookup,
        CancellationToken ct)
    {
        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);
        var layout = new ConfigurationStorageLayout(host.ResolvedDataRoot);
        long sizeBytes = 0;
        try { sizeBytes = new FileInfo(layout.CurrentConfigPath).Length; } catch { }

        var configOverride = envLookup("EDGECONNECT_DATA_ROOT") is not null
            ? "EDGECONNECT_DATA_ROOT"
            : null;

        return new CurrentConfigVersionDto
        {
            VersionId = mgr.CurrentVersionId.Value,
            AppliedAtUtc = current.Gateway is null ? DateTime.UtcNow : DateTime.UtcNow,  // Best-effort: see note below.
            Summary = $"Version {mgr.CurrentVersionId.Value}",
            SizeBytes = sizeBytes,
            ConfigPath = layout.CurrentConfigPath,
            Override = configOverride,
        };
    }

    /// <summary>Parse a route-param string into a typed <see cref="DraftId"/>.</summary>
    internal static bool TryParseDraftId(string raw, out DraftId draftId, out string error)
    {
        try
        {
            draftId = new DraftId(raw);
            error = string.Empty;
            return true;
        }
        catch (ArgumentException ex)
        {
            draftId = default;
            error = $"Invalid draftId: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Walk the audit log once and build a lookup of draft-id → (actor, createdAt)
    /// from <c>CONFIG.DRAFT_CREATED</c> entries. Drafts that never produced
    /// an audit entry (out-of-band file copies, edge cases) fall back to
    /// the file's creation time in <see cref="ConfigContractMapper.ToDraftMetadata"/>.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, (string Actor, DateTime CreatedAtUtc)>>
        BuildDraftAuditLookupAsync(IConfigurationManager mgr, CancellationToken ct)
    {
        var lookup = new Dictionary<string, (string, DateTime)>(StringComparer.Ordinal);
        try
        {
            await foreach (var entry in mgr.GetAuditLogAsync(verifyChain: false, ct).ConfigureAwait(false))
            {
                if (entry.Action == ConfigurationAuditAction.DraftCreated && entry.DraftId is { } draftId)
                {
                    // Last write wins if a draft id repeats (shouldn't happen,
                    // but defensive). Newer audit entries are appended to the
                    // log, so iteration order = creation order.
                    lookup[draftId.Value] = (entry.Actor, entry.Timestamp);
                }
            }
        }
        catch
        {
            // Audit log corrupt or missing — drafts list still renders,
            // just with Actor="unknown" + file-creation-time as fallback.
        }
        return lookup;
    }

    /// <summary>Resolve the gateway id for federation origin stamping. Null on uninitialised config.</summary>
    private static async Task<string?> TryGetGatewayIdAsync(IConfigurationManager mgr, CancellationToken ct)
    {
        try
        {
            var config = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);
            return config.Gateway.GatewayId;
        }
        catch
        {
            return null;
        }
    }
}
