// ============================================================================
// File: Api/MelsecDiagnosticsApi.cs
// Purpose: GET /api/v1/sources/{id}/melsec-diagnostics — observational MELSEC
//          diagnostics for the SourceDetail panel. Always 200 (the DTO carries
//          an Available flag + Reason); never exposes raw request/response bytes.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md (§Δ3, Δ9)
// ============================================================================

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the per-source MELSEC diagnostics read-API.</summary>
public static class MelsecDiagnosticsApi
{
    /// <summary>Map the MELSEC diagnostics endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapMelsecDiagnosticsApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/v1/sources/{sourceInstanceId}/melsec-diagnostics", (
            string sourceInstanceId,
            MelsecDiagnosticsService service) => Results.Ok(service.GetDiagnostics(sourceInstanceId)))
        .WithName("MelsecSourceDiagnostics")
        .WithTags("Sources")
        .WithSummary("Observational MELSEC diagnostics for a running source (route, scan blocks, last end code, affected tags, per-tag quality, latency). No raw payloads.")
        .Produces<MelsecDiagnosticsDto>(StatusCodes.Status200OK);

        return builder;
    }
}
