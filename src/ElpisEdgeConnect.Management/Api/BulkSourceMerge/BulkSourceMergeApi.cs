// ============================================================================
// File: Api/BulkSourceMerge/BulkSourceMergeApi.cs
// Purpose: POST /api/v1/sources/bulk-preview and /api/v1/sources/bulk-submit
//          endpoints delegating to BulkSourceMergeService. Optional
//          POST /api/v1/sources/bulk-probe exposes the per-row MTConnect
//          probe for the wizard's "Test connectivity" button.
//
//          Endpoints are thin: null-guard, call the service, return JSON.
//          Per v3.1 sec1, all three endpoints require the same Studio
//          authentication, role-based authorization, and anti-forgery
//          protection as other config-changing management endpoints —
//          enforced via .RequireAuthorization() so the central
//          UseAuthorization + UseAntiforgery middleware applies.
//
//          Submit-side actor identification: when the User is authenticated
//          we use User.Identity.Name; otherwise fall back to "studio-anonymous"
//          for the audit trail (the middleware would have already rejected
//          unauthenticated requests when auth is enabled).
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md sec1
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>Endpoint registration for the bulk-source-merge wizard handlers.</summary>
public static class BulkSourceMergeApi
{
    private const string DefaultActor = "studio-anonymous";

    /// <summary>Map the bulk-source-merge endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapBulkSourceMergeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources").WithTags("Bulk-Source-Merge");

        // ── Preview ──────────────────────────────────────────────────────────
        group.MapPost("/bulk-preview", async (
            BulkSourceMergePreviewRequest? request,
            BulkSourceMergeService service,
            ILogger<BulkSourceMergeService> logger,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            try
            {
                var response = await service.PreviewAsync(request, cancellationToken).ConfigureAwait(false);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving BulkSourceMergeService.PreviewAsync");
                return Results.Problem(
                    title: "Bulk-source-merge preview failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("BulkSourceMergePreview")
        .WithSummary("Preview the bulk-source-merge: parse CSV, validate, resolve sink, simulate merge, schema-validate.")
        .Produces<BulkSourceMergePreviewResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ── Submit ───────────────────────────────────────────────────────────
        group.MapPost("/bulk-submit", async (
            BulkSourceMergeSubmitRequest? request,
            BulkSourceMergeService service,
            HttpContext httpContext,
            ILogger<BulkSourceMergeService> logger,
            CancellationToken cancellationToken) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            var actor = httpContext.User?.Identity?.Name ?? DefaultActor;
            try
            {
                var response = await service.SubmitAsync(request, actor, cancellationToken).ConfigureAwait(false);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving BulkSourceMergeService.SubmitAsync");
                return Results.Problem(
                    title: "Bulk-source-merge submit failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("BulkSourceMergeSubmit")
        .WithSummary("Submit the bulk-source-merge: re-parse + re-validate + create draft via IConfigurationManager.")
        .Produces<BulkSourceMergeSubmitResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ── Probe (optional, operator-triggered) ─────────────────────────────
        group.MapPost("/bulk-probe", async (
            BulkProbeRequest? request,
            BulkMTConnectProbeService probeService,
            ILogger<BulkMTConnectProbeService> logger,
            CancellationToken cancellationToken) =>
        {
            if (request is null || request.BaseUrls is null)
            {
                return Results.BadRequest(new { error = "Request body with BaseUrls is required." });
            }

            try
            {
                var results = await probeService.ProbeAsync(request.BaseUrls, cancellationToken).ConfigureAwait(false);
                return Results.Ok(new BulkProbeResponse { Results = results });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving BulkMTConnectProbeService.ProbeAsync");
                return Results.Problem(
                    title: "Bulk MTConnect probe failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .RequireAuthorization()
        .WithName("BulkSourceMergeProbe")
        .WithSummary("Probe each row's MTConnect baseUrl. Informational; does not block submit.")
        .Produces<BulkProbeResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Probe-endpoint request body.</summary>
public sealed record BulkProbeRequest
{
    /// <summary>Per-row baseUrls to probe. Order preserved in the response.</summary>
    public required System.Collections.Generic.IReadOnlyList<string> BaseUrls { get; init; }
}

/// <summary>Probe-endpoint response body.</summary>
public sealed record BulkProbeResponse
{
    /// <summary>Per-row probe results in input order.</summary>
    public required System.Collections.Generic.IReadOnlyList<BulkProbeResult> Results { get; init; }
}
