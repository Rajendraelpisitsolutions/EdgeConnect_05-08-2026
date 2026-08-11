// ============================================================================
// File: Api/OpcUaClientBrowseApi.cs
// Purpose: POST /api/v1/sources/browse/opcua-client — accepts an
//          OpcUaClientBrowseRequest, delegates the probe lifecycle to
//          OpcUaClientBrowseApiService, and surfaces a structured result.
//
//          Status mapping (PR 7c-2 plan, user lock 2026-05-29):
//             Success=true                  → 200
//             OPCUA.PROBE_LICENSE_DISABLED  → 403
//             OPCUA.BROWSE_IN_FLIGHT        → 409
//             OPCUA.BROWSE_CONFIG_INVALID   → 400
//             OPCUA.BROWSE_CONFIG_INCOMPLETE→ 400
//             OPCUA.BROWSE_* (browse-side)  → 200 (Success=false in body
//                                                  — wizard renders inline)
//             unexpected service exception  → 500
//
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
//            docs/decisions/0015-wizard-contract.md Rule 9
// ============================================================================

using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the OPC UA Client Browse probe.
/// </summary>
public static class OpcUaClientBrowseApi
{
    /// <summary>Map the v1 OPC UA Client Browse endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapOpcUaClientBrowseApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse").WithTags("Sources");

        group.MapPost("/opcua-client", async (
            OpcUaClientBrowseRequest request,
            OpcUaClientBrowseApiService service,
            ILogger<OpcUaClientBrowseApiService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            OpcUaClientBrowseProbeOutcome outcome;
            try
            {
                outcome = await service.BrowseAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving OpcUaClientBrowseApiService.BrowseAsync");
                return Results.Problem(
                    title: "OPC UA Client Browse failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = OpcUaClientBrowseStatusMapping.StatusCodeFor(outcome.Status);
            return Results.Json(outcome.Result, statusCode: statusCode);
        })
        .WithName("BrowseOpcUaClientEndpoint")
        .WithSummary("Probe an OPC UA Client endpoint's address space — opens a throwaway session, walks the tree from the supplied StartingNodeId, returns BrowseResult. Read-only per ADR-0015 Rule 9.")
        .Produces<OpcUaClientBrowseResultDto>(StatusCodes.Status200OK)
        .Produces<OpcUaClientBrowseResultDto>(StatusCodes.Status400BadRequest)
        .Produces<OpcUaClientBrowseResultDto>(StatusCodes.Status403Forbidden)
        .Produces<OpcUaClientBrowseResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>
/// Pure status-code mapping. Extracted so tests pin the contract
/// without standing up a WebApplicationFactory.
/// </summary>
internal static class OpcUaClientBrowseStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(OpcUaClientBrowseStatus status) =>
        status switch
        {
            OpcUaClientBrowseStatus.Success => StatusCodes.Status200OK,
            OpcUaClientBrowseStatus.ConfigInvalid => StatusCodes.Status400BadRequest,
            OpcUaClientBrowseStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            OpcUaClientBrowseStatus.Busy => StatusCodes.Status409Conflict,
            OpcUaClientBrowseStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
