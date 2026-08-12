// ============================================================================
// File: Api/MqttTestConnectionApi.cs
// Purpose: POST /api/v1/sinks/test-connection/mqtt — accepts a probe
//          request (broker host/port/auth/TLS) and delegates the probe
//          lifecycle to MqttTestConnectionService. Mirrors
//          Focas2BrowseApi's shape and status-code mapping.
//
//          Status mapping (M.2b.6 v1 plan §4.2):
//             Success=true                → 200
//             MQTT.PROBE_LICENSE_DISABLED → 403
//             MQTT.PROBE_BUSY             → 409
//             MQTT.PROBE_* (broker)       → 200 (Success=false in body — wizard renders inline)
//             unexpected service exception → 500
//
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
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
/// Endpoint registration for the MQTT Test Connection probe.
/// </summary>
public static class MqttTestConnectionApi
{
    /// <summary>Map the v1 MQTT Test Connection endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapMqttTestConnectionApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sinks/test-connection").WithTags("Sinks");

        group.MapPost("/mqtt", async (
            MqttTestConnectionRequest request,
            MqttTestConnectionService service,
            ILogger<MqttTestConnectionService> logger,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(request.BrokerHost))
            {
                return Results.BadRequest(new { error = "brokerHost is required." });
            }

            MqttTestConnectionProbeOutcome outcome;
            try
            {
                outcome = await service.ProbeAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving MqttTestConnectionService.ProbeAsync");
                return Results.Problem(
                    title: "MQTT Test Connection failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var statusCode = MqttTestConnectionStatusMapping.StatusCodeFor(outcome.Status);
            return Results.Json(outcome.Result, statusCode: statusCode);
        })
        .WithName("ProbeMqttBroker")
        .WithSummary("Probe an MQTT broker — runs a throwaway ConnectAsync/DisconnectAsync against the supplied broker config and returns success or a structured error code. Locked H: never publishes.")
        .Produces<MqttTestConnectionResultDto>(StatusCodes.Status200OK)
        .Produces<MqttTestConnectionResultDto>(StatusCodes.Status400BadRequest)
        .Produces<MqttTestConnectionResultDto>(StatusCodes.Status403Forbidden)
        .Produces<MqttTestConnectionResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>
/// Pure status-code mapping. Extracted so tests can pin the contract
/// without standing up a WebApplicationFactory.
/// </summary>
internal static class MqttTestConnectionStatusMapping
{
    /// <summary>Map a service-level status to its HTTP status code.</summary>
    internal static int StatusCodeFor(MqttTestConnectionStatus status) =>
        status switch
        {
            MqttTestConnectionStatus.Success => StatusCodes.Status200OK,
            MqttTestConnectionStatus.LicenseDisabled => StatusCodes.Status403Forbidden,
            MqttTestConnectionStatus.Busy => StatusCodes.Status409Conflict,
            // Broker-side rejection / timeout / refusal — return 200 with
            // Success=false in the body so the wizard renders the structured
            // error inline rather than hitting the fetch error path.
            MqttTestConnectionStatus.Failure => StatusCodes.Status200OK,
            _ => StatusCodes.Status500InternalServerError,
        };
}
