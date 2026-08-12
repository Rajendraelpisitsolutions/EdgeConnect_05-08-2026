// ============================================================================
// File: Api/BundleApi.cs
// Purpose: The diagnostic-bundle management API (ADR-0020 G4).
//            GET  /api/v1/bundle/preview  — dry-run manifest the operator confirms
//                                           (nothing is written).
//            POST /api/v1/bundle          — generate + stream the zip. Requires
//                                           acknowledgement when secret-shape
//                                           warnings exist (plan v2 §8a); the
//                                           optional reason flows into the
//                                           BUNDLE.GENERATED audit entry.
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §4/§8a.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Bundle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the diagnostic-bundle API.</summary>
public static class BundleApi
{
    /// <summary>MIME type for the bundle archive.</summary>
    public const string ZipContentType = "application/zip";

    /// <summary>Map the v1 bundle endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapBundleApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/bundle").WithTags("Bundle");

        group.MapGet("/preview", async (BundleBuilder bundleBuilder, CancellationToken ct) =>
        {
            try
            {
                var preview = await bundleBuilder.PreviewAsync(ct).ConfigureAwait(false);
                return Results.Ok(preview);
            }
            catch (InvalidOperationException ex)
            {
                // Most likely: no current.json on disk yet (uninitialised gateway).
                return Results.Problem(
                    title: "Bundle preview unavailable",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("PreviewDiagnosticBundle")
        .WithSummary("Preview the diagnostic-bundle manifest (dry-run — nothing is written or leaves the gateway).")
        .Produces<BundlePreview>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost("/", async (
            GenerateBundleRequest? request,
            BundleBuilder bundleBuilder,
            CancellationToken ct) =>
        {
            // Operator identity isn't wired into the management API yet; "system"
            // placeholder, consistent with the backup endpoint. Populated from
            // HttpContext.User once auth lands.
            const string invokedBy = "system";
            var reason = request?.Reason;
            var acknowledged = request?.AcknowledgedSecretShapeWarnings ?? false;

            await using var ms = new MemoryStream();
            string filename;
            try
            {
                filename = await bundleBuilder
                    .BuildAsync(ms, invokedBy, reason, acknowledged, ct)
                    .ConfigureAwait(false);
            }
            catch (BundleAcknowledgementRequiredException ex)
            {
                // The bundle has secret-shape warnings that were not acknowledged.
                // Nothing was generated. The Studio re-shows the preview's warning
                // and the acknowledgement checkbox.
                return Results.Problem(
                    title: "Acknowledgement required",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?>
                    {
                        ["secretShapeWarningCount"] = ex.SecretShapeWarningCount,
                    });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(
                    title: "Bundle unavailable",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
            catch (Exception ex)
            {
                // Any other failure (IO, audit-chain, config) must surface as a
                // real Problem body — never a bodyless 500 the operator can't act
                // on. Include the exception type so a support engineer can triage.
                return Results.Problem(
                    title: "Bundle generation failed",
                    detail: $"{ex.GetType().Name}: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            ms.Position = 0;
            // Results.File sets Content-Disposition (attachment; filename + RFC 5987
            // filename*) itself from fileDownloadName — no manual header needed.
            return Results.File(ms.ToArray(), ZipContentType, fileDownloadName: filename);
        })
        .WithName("GenerateDiagnosticBundle")
        .WithSummary("Generate and download the diagnostic bundle (zip). 400 when secret-shape warnings are unacknowledged.")
        .Produces(StatusCodes.Status200OK, contentType: ZipContentType)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Request body for <c>POST /api/v1/bundle</c>.</summary>
/// <param name="Reason">Optional operator-supplied reason for generating (e.g. a support ticket id).</param>
/// <param name="AcknowledgedSecretShapeWarnings">
/// True when the operator has acknowledged the secret-shape warnings shown in the preview. Required
/// (must be true) when the preview reported a non-zero secret-shape warning count.
/// </param>
public sealed record GenerateBundleRequest(string? Reason, bool AcknowledgedSecretShapeWarnings);
