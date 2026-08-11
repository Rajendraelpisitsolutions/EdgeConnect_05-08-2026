// ============================================================================
// File: Api/LicenseApi.cs
// Purpose: GET /api/license/status  — current license state for the UI.
//          POST /api/license/activate — validate + save + reload an uploaded
//          license. Backed by LicenseActivationService.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Request body for <c>POST /api/license/activate</c>.</summary>
public sealed record LicenseActivateRequest
{
    /// <summary>The full signed <c>license.json</c> content.</summary>
    public string? LicenseJson { get; init; }
}

/// <summary>Endpoint registration for the Studio license status + activation API.</summary>
public static class LicenseApi
{
    /// <summary>Map the license endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapLicenseApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/license").WithTags("License");

        group.MapGet("/status", (LicenseActivationService service) =>
                Results.Json(service.GetStatus()))
            .WithName("GetLicenseStatus")
            .WithSummary("Current license status (edition, expiry, feature visibility, buy URL).")
            .Produces<LicenseStatusDto>(StatusCodes.Status200OK);

        group.MapPost("/activate", async (
            LicenseActivateRequest request,
            LicenseActivationService service,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.LicenseJson))
            {
                return Results.BadRequest(new { error = "licenseJson is required." });
            }

            var result = await service.ActivateFromJsonAsync(request.LicenseJson, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        })
            .WithName("ActivateLicense")
            .WithSummary("Validate an uploaded license, save it to the license path, and hot-reload it.")
            .Produces<LicenseActivationResult>(StatusCodes.Status200OK)
            .Produces<LicenseActivationResult>(StatusCodes.Status400BadRequest);

        group.MapGet("/contact", (LicensePurchaseService service) =>
                Results.Json(service.GetContact()))
            .WithName("GetLicenseContact")
            .WithSummary("Elpis contact details for the Buy License dialog.")
            .Produces<LicenseContactDto>(StatusCodes.Status200OK);

        group.MapPost("/buy-request", async (
            LicensePurchaseRequest request,
            LicensePurchaseService service,
            CancellationToken ct) =>
        {
            if (request is null)
            {
                return Results.BadRequest(new { error = "Request body is required." });
            }

            var result = await service.SubmitAsync(request, ct).ConfigureAwait(false);
            return result.Error is not null && result.MailtoUrl is null
                ? Results.Json(result, statusCode: StatusCodes.Status400BadRequest)
                : Results.Json(result);
        })
            .WithName("SubmitLicensePurchase")
            .WithSummary("Submit a license purchase enquiry (emails Elpis via SMTP, or returns a mailto fallback).")
            .Produces<LicensePurchaseResult>(StatusCodes.Status200OK)
            .Produces<LicensePurchaseResult>(StatusCodes.Status400BadRequest);

        return builder;
    }
}
