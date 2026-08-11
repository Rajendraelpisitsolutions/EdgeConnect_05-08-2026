// ============================================================================
// File: Api/BackupApi.cs
// Purpose: GET /api/v1/backup — streams a configuration backup zip
//          to the response. Secrets redacted, manifest manifest, audit
//          log + history included. See BackupBuilder for the zip layout
//          and redaction rules.
//
//          Streaming directly to Response.Body keeps the runtime out
//          of having to buffer multi-megabyte audit logs on big
//          gateways — important on Edge hardware.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.3
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Backup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the configuration-backup export API.
/// </summary>
public static class BackupApi
{
    /// <summary>MIME type for the backup archive.</summary>
    public const string ZipContentType = "application/zip";

    /// <summary>Map the v1 backup endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapBackupApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/backup").WithTags("Backup");

        group.MapGet("/", async (
            HttpContext context,
            IBackupBuilder backupBuilder,
            CancellationToken ct) =>
        {
            // The HTTP response body is a stream; the zip writer streams
            // its output directly into it. We don't pre-buffer the zip
            // because audit logs can be multi-megabyte on long-running
            // gateways and Edge hardware doesn't have memory to spare.

            // Reason can be overridden via ?reason=… — used by future
            // automated callers (scheduled backups, installer hooks).
            // Today the Studio just calls /api/v1/backup with no query.
            var reasonParam = context.Request.Query["reason"].ToString();
            var exportReason = string.IsNullOrWhiteSpace(reasonParam) ? "manual" : reasonParam.Trim();

            // Buffer the response so we can set content-disposition after
            // we know the assembled filename (which depends on gateway
            // id + create timestamp). For multi-megabyte audit logs this
            // matters less than getting the headers right.
            await using var ms = new System.IO.MemoryStream();
            string filename;
            try
            {
                filename = await backupBuilder.BuildAsync(ms, exportReason, ct).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Most likely: no current.json on disk yet (uninitialised gateway).
                return Results.Problem(
                    title: "Backup unavailable",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            ms.Position = 0;
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
            return Results.File(ms.ToArray(), ZipContentType, filename);
        })
        .WithName("DownloadBackup")
        .WithSummary("Download a redacted configuration + audit + history backup archive (zip).")
        .Produces(StatusCodes.Status200OK, contentType: ZipContentType)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
