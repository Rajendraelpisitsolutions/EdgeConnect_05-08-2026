// ============================================================================
// File: Api/S7ProbeApi.cs
// Purpose: POST /api/v1/sources/browse/s7/test-connection and
//          POST /api/v1/sources/browse/s7/test-read — the S7 source wizard's
//          two read-only probes. Delegates to S7ProbeService. Status mapping
//          mirrors the Modbus probe:
//             Success                       → 200
//             S7.PROBE_LICENSE_DISABLED     → 403
//             S7.PROBE_BUSY                 → 409
//             reachability/read failures    → 200 (Success=false body)
//             unexpected service exception  → 500
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md (M2)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the S7 Test Connection / Test Read probes.</summary>
public static class S7ProbeApi
{
    /// <summary>Map the v1 S7 probe endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapS7ProbeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse/s7").WithTags("Sources");

        group.MapPost("/test-connection", async (
            S7TestConnectionRequest request,
            S7ProbeService service,
            ILogger<S7ProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host))
            {
                return Results.BadRequest(new { error = "host is required." });
            }

            S7ProbeOutcome outcome;
            try
            {
                outcome = await service.TestConnectionAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving S7ProbeService.TestConnectionAsync");
                return Results.Problem(
                    title: "S7 Test Connection failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(outcome.Result, statusCode: S7ProbeStatusMapping.StatusCodeFor(outcome.Status));
        })
        .WithName("ProbeS7Connection")
        .WithSummary("Test reachability of an S7 PLC (connect only, read-only). Returns success or a structured S7.PROBE_* error.")
        .Produces<S7ProbeResultDto>(StatusCodes.Status200OK)
        .Produces<S7ProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<S7ProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/test-read", async (
            S7TestReadRequest request,
            S7ProbeService service,
            ILogger<S7ProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Address))
            {
                return Results.BadRequest(new { error = "host and address are required." });
            }

            S7ProbeOutcome outcome;
            try
            {
                outcome = await service.TestReadAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving S7ProbeService.TestReadAsync");
                return Results.Problem(
                    title: "S7 Test Read failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(outcome.Result, statusCode: S7ProbeStatusMapping.StatusCodeFor(outcome.Status));
        })
        .WithName("ProbeS7Read")
        .WithSummary("Read a single S7 tag once (read-only). Distinguishes read-ok / read-failed / DB-access-denied.")
        .Produces<S7ProbeResultDto>(StatusCodes.Status200OK)
        .Produces<S7ProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<S7ProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Pure status-code mapping. Extracted so tests can pin the contract.</summary>
internal static class S7ProbeStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(S7ProbeStatus status) =>
        status switch
        {
            S7ProbeStatus.Success => StatusCodes.Status200OK,
            S7ProbeStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            S7ProbeStatus.Busy => StatusCodes.Status409Conflict,
            // Connect/read failures — 200 with Success=false so the wizard
            // renders inline (mirrors the Modbus probe).
            S7ProbeStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
