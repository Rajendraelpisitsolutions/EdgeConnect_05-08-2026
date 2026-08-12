// ============================================================================
// File: Api/S7AddressValidationApi.cs
// Purpose: POST /api/v1/sources/validate/s7-address — parser-only validation
//          of a single S7 address for the source wizard's live (debounced)
//          address field. Delegates to S7AddressValidationService. Always
//          returns 200 with the structured result; an invalid/unsupported
//          address is an IsValid=false body, not an HTTP error.
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md (M2)
// ============================================================================

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the S7 address-validation helper.</summary>
public static class S7AddressValidationApi
{
    /// <summary>Map the v1 S7 address-validation endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapS7AddressValidationApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/validate").WithTags("Sources");

        group.MapPost("/s7-address", (
            S7AddressValidationRequest request,
            S7AddressValidationService service) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }
            return Results.Ok(service.Validate(request));
        })
        .WithName("ValidateS7Address")
        .WithSummary("Validate a single S7 address string (parser-only, no PLC access). Returns the normalized address, parsed parts, and datatype suggestions, or a friendly error for a malformed/unsupported address.")
        .Produces<S7AddressValidationResult>(StatusCodes.Status200OK);

        return builder;
    }
}
