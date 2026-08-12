// ============================================================================
// File: Api/BrotherHttpProbeApi.cs
// Purpose: POST /api/v1/sources/browse/brother-http — accepts a probe
//          request (BaseUrl) and delegates to BrotherHttpProbeService.
//          Mirrors MqttTestConnectionApi's shape and status-code mapping.
//
//          Status mapping (M.2d.2 v2 §4.6):
//             Success=true                       → 200
//             BROTHER.PROBE_LICENSE_DISABLED     → 403
//             BROTHER.PROBE_BUSY                 → 409
//             BROTHER.PROBE_* (CNC reachability) → 200 (Success=false body — wizard renders inline)
//             unexpected service exception       → 500
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4.6
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the Brother HTTP Test Connection probe.
/// </summary>
public static class BrotherHttpProbeApi
{
    /// <summary>Map the v1 Brother HTTP probe endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapBrotherHttpProbeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse").WithTags("Sources");

        group.MapPost("/brother-http", async (
            BrotherHttpProbeRequest request,
            BrotherHttpProbeService service,
            ILogger<BrotherHttpProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.BaseUrl))
            {
                return Results.BadRequest(new { error = "baseUrl is required." });
            }

            BrotherHttpProbeOutcome outcome;
            try
            {
                outcome = await service.ProbeAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving BrotherHttpProbeService.ProbeAsync");
                return Results.Problem(
                    title: "Brother HTTP Test Connection failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = BrotherHttpProbeStatusMapping.StatusCodeFor(outcome.Status);
            return Results.Json(outcome.Result, statusCode: statusCode);
        })
        .WithName("ProbeBrotherHttpSource")
        .WithSummary("Probe a Brother CNC — runs a throwaway GET {BaseUrl}/HTTPD_MCNINFO against the supplied config and returns success or a structured error code. Closes M.P2.4 Q12.")
        .Produces<BrotherHttpProbeResultDto>(StatusCodes.Status200OK)
        .Produces<BrotherHttpProbeResultDto>(StatusCodes.Status400BadRequest)
        .Produces<BrotherHttpProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<BrotherHttpProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>
/// Pure status-code mapping. Extracted so tests can pin the contract
/// without standing up a WebApplicationFactory.
/// </summary>
internal static class BrotherHttpProbeStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(BrotherHttpProbeStatus status) =>
        status switch
        {
            BrotherHttpProbeStatus.Success => StatusCodes.Status200OK,
            BrotherHttpProbeStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            BrotherHttpProbeStatus.Busy => StatusCodes.Status409Conflict,
            // CNC-side rejection / timeout / unreachable — return 200 with
            // Success=false so the wizard renders inline rather than
            // hitting the fetch error path. v2 §4.6 invariant.
            BrotherHttpProbeStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
