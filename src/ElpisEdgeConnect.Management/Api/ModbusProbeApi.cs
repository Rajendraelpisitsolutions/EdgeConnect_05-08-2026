// ============================================================================
// File: Api/ModbusProbeApi.cs
// Purpose: POST /api/v1/sources/browse/modbus — accepts a probe request
//          and delegates to ModbusProbeService. Status mapping mirrors
//          Brother HTTP + MQTT.
//
//          Status mapping (M.2d.2 v2 §4.6):
//             Success=true                    → 200
//             MODBUS.PROBE_LICENSE_DISABLED   → 403
//             MODBUS.PROBE_BUSY               → 409
//             MODBUS.PROBE_* (reachability)   → 200 (Success=false body — wizard renders inline)
//             unexpected service exception    → 500
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

/// <summary>Endpoint registration for the Modbus TCP Test Connection probe.</summary>
public static class ModbusProbeApi
{
    /// <summary>Map the v1 Modbus probe endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapModbusProbeApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse").WithTags("Sources");

        group.MapPost("/modbus", async (
            ModbusProbeRequest request,
            ModbusProbeService service,
            ILogger<ModbusProbeService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.IpAddress))
            {
                return Results.BadRequest(new { error = "ipAddress is required." });
            }

            ModbusProbeOutcome outcome;
            try
            {
                outcome = await service.ProbeAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving ModbusProbeService.ProbeAsync");
                return Results.Problem(
                    title: "Modbus Test Connection failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = ModbusProbeStatusMapping.StatusCodeFor(outcome.Status);
            return Results.Json(outcome.Result, statusCode: statusCode);
        })
        .WithName("ProbeModbusSource")
        .WithSummary("Probe a Modbus TCP slave — TCP connect then read either the first configured tag or the fallback test address. Returns success or a structured MODBUS.PROBE_* error.")
        .Produces<ModbusProbeResultDto>(StatusCodes.Status200OK)
        .Produces<ModbusProbeResultDto>(StatusCodes.Status400BadRequest)
        .Produces<ModbusProbeResultDto>(StatusCodes.Status403Forbidden)
        .Produces<ModbusProbeResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Pure status-code mapping. Extracted so tests can pin the contract.</summary>
internal static class ModbusProbeStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(ModbusProbeStatus status) =>
        status switch
        {
            ModbusProbeStatus.Success => StatusCodes.Status200OK,
            ModbusProbeStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            ModbusProbeStatus.Busy => StatusCodes.Status409Conflict,
            // Slave-side rejection / timeout / refusal — return 200 with
            // Success=false so the wizard renders inline. v2 §4.6.
            ModbusProbeStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
