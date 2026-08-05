// ============================================================================
// File: Api/MTConnectBrowseApi.cs
// Purpose: POST /api/v1/sources/browse/mtconnect — delegates the whole probe to
//          MTConnectBrowseService. The endpoint owns only: request null-guard,
//          unexpected-exception → 500 ProblemDetails, and result → HTTP status.
//          Every structured probe verdict returns 200 with the status in the
//          body (so the wizard renders the matching state inline); only a
//          missing license is 403. Mirrors the FOCAS2 browse endpoint.
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md §3.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the MTConnect agent browse probe.</summary>
public static class MTConnectBrowseApi
{
    /// <summary>Map the v1 MTConnect browse endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapMTConnectBrowseApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse").WithTags("Sources");

        group.MapPost("/mtconnect", async (
            MTConnectBrowseRequest? request,
            MTConnectBrowseService service,
            ILogger<MTConnectBrowseService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            MTConnectBrowseResultDto result;
            try
            {
                result = await service.BrowseAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving MTConnectBrowseService.BrowseAsync");
                return Results.Problem(
                    title: "MTConnect browse failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(result, statusCode: MTConnectBrowseStatusMapping.StatusCodeFor(result.Status));
        })
        .WithName("BrowseMTConnectAgent")
        .WithSummary("Probe an MTConnect agent's /probe and return which standard CNC tags it exposes plus discovered axes.")
        .Produces<MTConnectBrowseResultDto>(StatusCodes.Status200OK)
        .Produces<MTConnectBrowseResultDto>(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Pure status-code mapping — pinned by unit tests without a WebApplicationFactory.</summary>
internal static class MTConnectBrowseStatusMapping
{
    /// <summary>
    /// Every structured probe verdict is 200 (the wizard renders it inline);
    /// only a disabled license is 403.
    /// </summary>
    internal static int StatusCodeFor(MTConnectBrowseStatus status) => status switch
    {
        MTConnectBrowseStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status200OK,
    };
}
