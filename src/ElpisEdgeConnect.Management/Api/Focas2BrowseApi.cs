// ============================================================================
// File: Api/Focas2BrowseApi.cs
// Purpose: POST /api/v1/sources/browse/focas2 — accepts a canonical
//          SourceInstanceConfig and delegates the entire probe lifecycle to
//          Focas2BrowseService. The endpoint owns ONLY:
//              * request null-guard
//              * unexpected-exception → 500 + ProblemDetails
//              * service result → HTTP status code mapping
//          ALL probe behaviour (license gate, single-flight lease, throwaway
//          adapter, Locked S/M/N/P/Q invariants) lives in the service.
//
//          Status mapping (locked by user 2026-05-17):
//             Success=true                 → 200
//             LICENSE.MODULE_DISABLED      → 403
//             FOCAS2.BROWSE_IN_FLIGHT      → 409
//             FOCAS2.CONFIG_INVALID        → 400
//             controller/connect errors    → 200 (Success=false in body)
//             unexpected service exception → 500
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §6.4
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the FOCAS2 Browse Controller probe.
/// </summary>
public static class Focas2BrowseApi
{
    /// <summary>Map the v1 Browse Controller endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapFocas2BrowseApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse").WithTags("Sources");

        group.MapPost("/focas2", async (
            SourceInstanceConfig request,
            Focas2BrowseService service,
            ILogger<Focas2BrowseService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            Focas2BrowseResultDto result;
            try
            {
                result = await service.BrowseAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Defensive — the service catches AdapterException,
                // OperationCanceledException, DllNotFoundException, and
                // ArgumentException from FromSourceInstance. Anything else
                // is a bug; surface as 500 ProblemDetails so the wizard's
                // fetch error path runs.
                logger.LogError(ex, "Unexpected error driving Focas2BrowseService.BrowseAsync");
                return Results.Problem(
                    title: "Browse Controller failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = Focas2BrowseStatusMapping.StatusCodeFor(result);
            return Results.Json(result, statusCode: statusCode);
        })
        .WithName("BrowseFocas2Controller")
        .WithSummary("Probe a FOCAS2 controller — runs a throwaway adapter Initialize/Start/BrowseTags cycle and returns axes + tag definitions + CNC identity.")
        .Produces<Focas2BrowseResultDto>(StatusCodes.Status200OK)
        .Produces<Focas2BrowseResultDto>(StatusCodes.Status400BadRequest)
        .Produces<Focas2BrowseResultDto>(StatusCodes.Status403Forbidden)
        .Produces<Focas2BrowseResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>
/// Pure status-code mapping from a probe result to its HTTP status. Extracted
/// so it can be pinned by unit tests without standing up a WebApplicationFactory.
/// </summary>
internal static class Focas2BrowseStatusMapping
{
    /// <summary>
    /// Map a probe result to its HTTP status code per the v3 plan §6.4 contract.
    /// </summary>
    internal static int StatusCodeFor(Focas2BrowseResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Success)
        {
            return StatusCodes.Status200OK;
        }
        return result.ErrorCode switch
        {
            "LICENSE.MODULE_DISABLED" => StatusCodes.Status403Forbidden,
            "FOCAS2.BROWSE_IN_FLIGHT" => StatusCodes.Status409Conflict,
            "FOCAS2.CONFIG_INVALID" => StatusCodes.Status400BadRequest,
            // CONNECT_FAILED / BROWSE_TIMEOUT / NATIVE_LIBRARY_MISSING /
            // any future controller-side code → 200 with Success=false so
            // the wizard renders the structured error inline rather than
            // hitting the fetch error path.
            _ => StatusCodes.Status200OK,
        };
    }
}
