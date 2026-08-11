// ============================================================================
// File: Api/MelsecProbeApi.cs
// Purpose: POST /api/v1/sources/probe/melsec/test-connection and
//          POST /api/v1/sources/probe/melsec/test-read — the MELSEC wizard's
//          two read-only probes. PROBE-ONLY: there is deliberately NO browse /
//          tag-discovery endpoint for MELSEC. Delegates to MelsecProbeService.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md (§Δ4)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the MELSEC test-connection / test-read probes.</summary>
public static class MelsecProbeApi
{
    /// <summary>Base route for the MELSEC probes (probe-only; no browse).</summary>
    public const string BaseRoute = "/api/v1/sources/probe/melsec";

    /// <summary>Map the v1 MELSEC probe endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapMelsecProbeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup(BaseRoute).WithTags("Sources");

        group.MapPost("/test-connection", async (
            MelsecTestConnectionRequest request,
            MelsecProbeService service,
            ILogger<MelsecProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host))
            {
                return Results.BadRequest(new { error = "host is required." });
            }
            try
            {
                var outcome = await service.TestConnectionAsync(request, ct).ConfigureAwait(false);
                return Results.Json(outcome.Result, statusCode: MelsecProbeStatusMapping.StatusCodeFor(outcome.Status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving MelsecProbeService.TestConnectionAsync");
                return Results.Problem(title: "MELSEC Test Connection failed unexpectedly.", detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ProbeMelsecConnection")
        .WithSummary("Test reachability of a MELSEC PLC (connect only, read-only).")
        .Produces<MelsecProbeResultDto>(StatusCodes.Status200OK)
        .Produces<MelsecProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<MelsecProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/test-read", async (
            MelsecTestReadRequest request,
            MelsecProbeService service,
            ILogger<MelsecProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host))
            {
                return Results.BadRequest(new { error = "host is required." });
            }
            try
            {
                var outcome = await service.TestReadAsync(request, ct).ConfigureAwait(false);
                return Results.Json(outcome.Result, statusCode: MelsecProbeStatusMapping.StatusCodeFor(outcome.Status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving MelsecProbeService.TestReadAsync");
                return Results.Problem(title: "MELSEC Test Read failed unexpectedly.", detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("ProbeMelsecRead")
        .WithSummary("Read the selected tag, or all valid tags via the scan planner (read-only). Invalid tags are skipped, never sent.")
        .Produces<MelsecProbeResultDto>(StatusCodes.Status200OK)
        .Produces<MelsecProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<MelsecProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Pure status-code mapping. Extracted so tests can pin the contract.</summary>
internal static class MelsecProbeStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(MelsecProbeStatus status) => status switch
    {
        MelsecProbeStatus.Success => StatusCodes.Status200OK,
        MelsecProbeStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
        MelsecProbeStatus.Busy => StatusCodes.Status409Conflict,
        MelsecProbeStatus.Failure => StatusCodes.Status200OK,
        _ => StatusCodes.Status500InternalServerError,
    };
}
