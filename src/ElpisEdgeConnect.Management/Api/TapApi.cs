// ============================================================================
// File: Api/TapApi.cs
// Purpose: Live Data Tap SSE endpoint (ADR-0018). GET streams a route's
//          source/sink captures as Server-Sent Events. Opening the stream
//          activates demand-driven capture for that route; closing it (client
//          disconnect → RequestAborted) deactivates it after the cooldown.
// Reference: ADR-0018, ADR-0017, platform principle P1 (observational only).
// ============================================================================

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the Live Data Tap SSE stream.</summary>
public static class TapApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Poll cadence for new captures. Fast enough to feel live; slow enough that
    // an idle tap (a watcher on a quiet route) costs almost nothing.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Map <c>GET /api/v1/diagnostics/tap/{routeId}</c> (SSE).</summary>
    public static IEndpointRouteBuilder MapTapApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/v1/diagnostics/tap/{routeId}", async (
            string routeId,
            HttpContext context,
            IRouteTap tap,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            // Disable proxy buffering so events flush immediately.
            context.Response.Headers["X-Accel-Buffering"] = "no";

            try
            {
                await TapStreamWriter.WriteStreamAsync(
                    tap,
                    routeId,
                    writeEvent: async (block, c) =>
                    {
                        await context.Response.WriteAsync(block, c).ConfigureAwait(false);
                        await context.Response.Body.FlushAsync(c).ConfigureAwait(false);
                    },
                    PollInterval,
                    JsonOptions,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal — the client disconnected. The stream writer's
                // subscription is disposed on the way out (cooldown begins).
            }
        })
        .WithName("TapStream")
        .WithTags("Diagnostics")
        .ExcludeFromDescription(); // SSE stream — not an OpenAPI request/response shape

        return builder;
    }
}
