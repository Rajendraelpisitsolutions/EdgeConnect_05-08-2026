// ============================================================================
// File: Api/OnboardingApi.cs
// Purpose: POST /api/v1/onboarding/apply — the bundled-apply endpoint for
//          the Connect-a-device meta-wizard (ADR-0016 Rule 6). Takes
//          source + sink + route as a single transaction and runs them
//          through the same draft → validate → apply pipeline that
//          single-entity Apply uses, preserving ADR-0014/0015 invariants.
//
//          Lives in its own file per the per-concern pattern used by
//          SourcesUpdateApi.cs / SinksUpdateApi.cs / RoutesUpdateApi.cs.
//
//          DispatchAsync is internal static for unit testability — same
//          pattern as the Edit-mode APIs.
// Reference: docs/decisions/0016-onboarding-meta-wizard.md Rule 6
//            docs/sessions/2026-05-27-connect-a-device-plan-v2.1.md §3.6
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
/// Bundled-apply endpoint for the Onboarding meta-wizard.
/// </summary>
public static class OnboardingApi
{
    /// <summary>Default actor stamped on the audit entry when the request omits one.</summary>
    public const string DefaultActor = "system";

    /// <summary>
    /// Map <c>POST /api/v1/onboarding/apply</c>. Accepts the three buildable
    /// entities from the meta-wizard, composes them via
    /// <see cref="WizardConfigMerger.BuildBundledOnboardingDraft"/>, creates a
    /// draft, applies it atomically. Validation failures = 400; apply-time
    /// conflicts = 409; success = 200 + <see cref="ApplyResultDto"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapOnboardingApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/onboarding").WithTags("Onboarding");

        group.MapPost("/apply", async (
            OnboardingApplyRequestDto? body,
            IConfigurationManager mgr,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            var outcome = await DispatchAsync(body, mgr, services, ct).ConfigureAwait(false);
            return outcome;
        })
        .WithName("OnboardingApply")
        .WithSummary("Atomic bundled apply for the Connect-a-device meta-wizard. Creates source + sink + route as one transaction.")
        .Produces<ApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

        return builder;
    }

    /// <summary>
    /// Dispatch the bundled apply: validate inputs → build bundled draft →
    /// create draft → apply → resolve reload outcome. Exposed for unit
    /// tests to pin every branch without standing up a WebApplicationFactory.
    /// </summary>
    internal static async Task<IResult> DispatchAsync(
        OnboardingApplyRequestDto? body,
        IConfigurationManager mgr,
        IServiceProvider services,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mgr);
        ArgumentNullException.ThrowIfNull(services);

        // ── Input validation ──────────────────────────────────────────
        if (body is null)
        {
            return Results.BadRequest(new { error = "Request body is required." });
        }
        if (body.Source is null || body.Sink is null || body.Route is null)
        {
            return Results.BadRequest(new
            {
                error = "Request body must include source, sink, and route.",
            });
        }

        // ── Load current ──────────────────────────────────────────────
        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);

        // ── Build bundled draft ───────────────────────────────────────
        GatewayConfiguration newDraft;
        try
        {
            newDraft = WizardConfigMerger.BuildBundledOnboardingDraft(
                current,
                body.Source,
                body.Sink,
                body.Route,
                body.GatewayIdOverride,
                body.GatewayNameOverride,
                body.SourceAlreadyCreated,
                body.SinkAlreadyCreated);
        }
        catch (ArgumentException ex)
        {
            // Merger-enforced invariant violation (duplicate id, missing
            // cross-reference, mismatched route source/sink). Surface as
            // 400 — the meta-wizard's Review step shows the message and
            // lets the operator go back to fix.
            return Results.BadRequest(new { error = ex.Message });
        }

        // ── Persist draft + apply atomically ──────────────────────────
        var actor = NormaliseActor(body.Actor);
        var draftId = await mgr.CreateDraftAsync(newDraft, actor, ct).ConfigureAwait(false);

        var previousVersion = mgr.CurrentVersionId;
        var applyResult = await mgr.ApplyDraftAsync(draftId, actor, ct).ConfigureAwait(false);
        if (!applyResult.Success)
        {
            // Apply-time revalidation failure. Mirrors SourcesUpdateApi
            // shape: 409 with the ValidationResultDto body so the
            // Review step can render specific field-level errors.
            return Results.Conflict(
                ConfigContractMapper.ToValidationResult(applyResult.ValidationResult, DateTime.UtcNow));
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
