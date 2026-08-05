// ============================================================================
// File: Api/OpcUaClientTestConnectionApi.cs
// Purpose: POST /api/v1/sources/test-connection/opcua-client — accepts an
//          OpcUaClientTestConnectionRequest, delegates the probe lifecycle
//          to OpcUaClientTestConnectionService, and surfaces a structured
//          result. Mirrors MqttTestConnectionApi's status-code mapping.
//
//          Status mapping (PR 7c-2 plan, user lock 2026-05-29):
//             Success=true                  → 200
//             OPCUA.PROBE_LICENSE_DISABLED  → 403
//             OPCUA.PROBE_BUSY              → 409
//             OPCUA.PROBE_CONFIG_INVALID    → 400
//             OPCUA.* (endpoint-side)       → 200 (Success=false in body —
//                                                 wizard renders inline)
//             unexpected service exception  → 500
//
// Reference: PR 7c-2 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the OPC UA Client Test Connection probe.
/// </summary>
public static class OpcUaClientTestConnectionApi
{
    /// <summary>Map the v1 OPC UA Client Test Connection endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapOpcUaClientTestConnectionApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/test-connection").WithTags("Sources");

        group.MapPost("/opcua-client", async (
            OpcUaClientTestConnectionRequest request,
            OpcUaClientTestConnectionService service,
            ILogger<OpcUaClientTestConnectionService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            OpcUaClientTestConnectionOutcome outcome;
            try
            {
                outcome = await service.ProbeAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving OpcUaClientTestConnectionService.ProbeAsync");
                return Results.Problem(
                    title: "OPC UA Client Test Connection failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = OpcUaClientTestConnectionStatusMapping.StatusCodeFor(outcome.Status);
            return Results.Json(outcome.Result, statusCode: statusCode);
        })
        .WithName("ProbeOpcUaClientEndpoint")
        .WithSummary("Probe an OPC UA Client endpoint — opens a throwaway session, reads ServerStatus.State, closes. Read-only per ADR-0015 Rule 6 / Rule 11.1.")
        .Produces<OpcUaClientTestConnectionResultDto>(StatusCodes.Status200OK)
        .Produces<OpcUaClientTestConnectionResultDto>(StatusCodes.Status400BadRequest)
        .Produces<OpcUaClientTestConnectionResultDto>(StatusCodes.Status403Forbidden)
        .Produces<OpcUaClientTestConnectionResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>
/// Pure status-code mapping. Extracted so tests pin the contract
/// without standing up a WebApplicationFactory.
/// </summary>
internal static class OpcUaClientTestConnectionStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(OpcUaClientTestConnectionStatus status) =>
        status switch
        {
            OpcUaClientTestConnectionStatus.Success => StatusCodes.Status200OK,
            OpcUaClientTestConnectionStatus.ConfigInvalid => StatusCodes.Status400BadRequest,
            OpcUaClientTestConnectionStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            OpcUaClientTestConnectionStatus.Busy => StatusCodes.Status409Conflict,
            // Endpoint-side rejection / timeout / refusal → 200 with
            // Success=false in the body so the wizard renders the
            // structured error inline rather than hitting the fetch
            // error path.
            OpcUaClientTestConnectionStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
