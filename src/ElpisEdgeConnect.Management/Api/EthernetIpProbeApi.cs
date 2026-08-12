// ============================================================================
// File: Api/EthernetIpProbeApi.cs
// Purpose: POST /api/v1/sources/browse/ethernetip/test-read — the EtherNet/IP
//          source wizard's read-only probe. Delegates to EthernetIpProbeService.
//          Status mapping mirrors the S7 / Modbus probes:
//             Success                              → 200
//             ETHERNETIP.PROBE_LICENSE_DISABLED    → 403
//             ETHERNETIP.PROBE_BUSY                → 409
//             connect / read failures             → 200 (Success=false body)
//             unexpected service exception        → 500
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §5.2
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the EtherNet/IP Test Read probe.</summary>
public static class EthernetIpProbeApi
{
    /// <summary>Map the v1 EtherNet/IP probe endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapEthernetIpProbeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse/ethernetip").WithTags("Sources");

        group.MapPost("/test-read", async (
            EthernetIpTestReadRequest request,
            EthernetIpProbeService service,
            ILogger<EthernetIpProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Address))
            {
                return Results.BadRequest(new { error = "host and address are required." });
            }

            EthernetIpProbeOutcome outcome;
            try
            {
                outcome = await service.TestReadAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving EthernetIpProbeService.TestReadAsync");
                return Results.Problem(
                    title: "EtherNet/IP Test Read failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(outcome.Result, statusCode: EthernetIpProbeStatusMapping.StatusCodeFor(outcome.Status));
        })
        .WithName("ProbeEthernetIpRead")
        .WithSummary("Read a single Allen-Bradley tag once (read-only). Distinguishes read-ok / tag-not-found / read-failed / connect-failed.")
        .Produces<EthernetIpProbeResultDto>(StatusCodes.Status200OK)
        .Produces<EthernetIpProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<EthernetIpProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Pure status-code mapping. Extracted so tests can pin the contract.</summary>
internal static class EthernetIpProbeStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(EthernetIpProbeStatus status) =>
        status switch
        {
            EthernetIpProbeStatus.Success => StatusCodes.Status200OK,
            EthernetIpProbeStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            EthernetIpProbeStatus.Busy => StatusCodes.Status409Conflict,
            EthernetIpProbeStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
