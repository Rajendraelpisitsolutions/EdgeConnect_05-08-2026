// ============================================================================
// File: Api/DiagnosticsApi.cs
// Purpose: Two GET endpoints backing the M.1c.2 Diagnostics page:
//
//          GET /api/v1/diagnostics/events
//             System-wide event timeline with server-side filter +
//             retention metadata envelope. Reuses the same
//             DiagnosticsEventDto wire shape as M.1c.1's
//             /routes/{id}/events.
//
//          GET /api/v1/diagnostics/audit-chain
//             Configuration audit log hash-chain verification result.
//             Separated from the events endpoint so the 5-second event
//             polling loop doesn't re-verify SHA-256 chains on every tick.
//
//          GatewayId stamping: this endpoint resolves the gateway id
//          from the loaded configuration once per request, then stamps
//          every returned event before serialization. Aggregators stay
//          identity-agnostic; the API boundary owns federation
//          attribution.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

// Alias to disambiguate from Microsoft.Extensions.Configuration.IConfigurationManager,
// which is brought into scope implicitly by the ASP.NET Core Web SDK global usings.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the system-wide diagnostics read-API.
/// </summary>
public static class DiagnosticsApi
{
    /// <summary>Map the v1 diagnostics endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapDiagnosticsApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/diagnostics").WithTags("Diagnostics");

        // GET /api/v1/diagnostics/events
        //   Query params:
        //     route       (exact route id, optional)
        //     code        (zero or more — repeated param: ?code=A&code=B)
        //     severity    (Info|Warning|Error, optional)
        //     since       (ISO 8601 UTC timestamp, optional)
        //     limit       (1..500, default 100)
        group.MapGet("/events", async (
            HttpContext context,
            IConfigurationManager configManager,
            IDiagnosticsEventAggregator aggregator,
            CancellationToken ct) =>
        {
            var (filter, parseError) = ParseFilter(context.Request.Query);
            if (parseError is not null)
            {
                return Results.BadRequest(new { error = parseError });
            }

            var gatewayId = await TryGetGatewayIdAsync(configManager, ct).ConfigureAwait(false);
            var response = await aggregator.GetRecentEventsAsync(filter, ct).ConfigureAwait(false);
            var stamped = StampGatewayId(response, gatewayId);
            return Results.Ok(stamped);
        })
        .WithName("ListDiagnosticsEvents")
        .WithSummary("System-wide event timeline across every route + configuration audit, server-filtered.")
        .Produces<DiagnosticsEventsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        // GET /api/v1/diagnostics/audit-chain
        //   No query params. Returns the chain verification result.
        //   Expensive (full audit re-scan + SHA-256 per entry); poll
        //   sparingly from the UI.
        group.MapGet("/audit-chain", async (
            IDiagnosticsEventAggregator aggregator,
            CancellationToken ct) =>
        {
            var status = await aggregator.VerifyAuditChainAsync(ct).ConfigureAwait(false);
            return Results.Ok(status);
        })
        .WithName("VerifyAuditChain")
        .WithSummary("Re-verify the configuration audit log SHA-256 hash chain from genesis.")
        .Produces<AuditChainStatus>(StatusCodes.Status200OK);

        // GET /api/v1/diagnostics/configuration-faults
        //   No query params. Returns the live snapshot of cross-record
        //   configuration faults observed at gateway startup (M.P2.1)
        //   and at runtime hot-reload (M.P2.2, when that lands). The
        //   audit chain holds the durable record; this endpoint is the
        //   live view. Empty list when the gateway is healthy.
        group.MapGet("/configuration-faults", (IConfigurationFaultRegistry? faultRegistry) =>
        {
            if (faultRegistry is null)
            {
                // Pre-M.P2.1 hosts that didn't register the registry
                // shouldn't fail — return an empty list.
                return Results.Ok(Array.Empty<ConfigurationFaultDto>());
            }

            var faults = faultRegistry.GetFaults();
            var dtos = new List<ConfigurationFaultDto>(faults.Count);
            foreach (var f in faults)
            {
                dtos.Add(new ConfigurationFaultDto
                {
                    Kind = f.Kind.ToString(),
                    InstanceId = f.InstanceId,
                    ErrorCode = f.ErrorCode,
                    Message = f.Message,
                    ObservedAtUtc = f.ObservedAtUtc,
                });
            }
            return Results.Ok(dtos);
        })
        .WithName("ListConfigurationFaults")
        .WithSummary("List every cross-record configuration fault currently registered (M.P2.1).")
        .Produces<ConfigurationFaultDto[]>(StatusCodes.Status200OK);

        return builder;
    }

    /// <summary>
    /// Parse the query string into a <see cref="DiagnosticsEventFilter"/>.
    /// Returns the parsed filter plus a null error on success, or the
    /// default filter plus a non-null error message on failure.
    /// </summary>
    internal static (DiagnosticsEventFilter Filter, string? Error) ParseFilter(IQueryCollection query)
    {
        string? routeId = query["route"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(routeId)) routeId = null;

        // Multi-valued: ?code=A&code=B
        var codes = query["code"]
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
        if (codes.Count > DiagnosticsEventFilter.MaxEventCodes)
        {
            return (new DiagnosticsEventFilter(), $"Too many event codes ({codes.Count}); max is {DiagnosticsEventFilter.MaxEventCodes}.");
        }

        DiagnosticsSeverity? severity = null;
        var rawSev = query["severity"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rawSev))
        {
            if (!Enum.TryParse<DiagnosticsSeverity>(rawSev, ignoreCase: true, out var sev))
            {
                return (new DiagnosticsEventFilter(), $"Unrecognised severity '{rawSev}'. Expected Info / Warning / Error.");
            }
            severity = sev;
        }

        DateTime? since = null;
        var rawSince = query["since"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rawSince))
        {
            if (!DateTime.TryParse(rawSince, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return (new DiagnosticsEventFilter(), $"Unrecognised 'since' timestamp '{rawSince}'. Expected ISO 8601 UTC.");
            }
            since = parsed;
        }

        var limit = DiagnosticsEventFilter.DefaultLimit;
        var rawLimit = query["limit"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rawLimit))
        {
            if (!int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                return (new DiagnosticsEventFilter(), $"Unrecognised limit '{rawLimit}'. Expected positive integer.");
            }
            limit = parsed;
        }

        return (new DiagnosticsEventFilter
        {
            RouteId = routeId,
            EventCodes = codes.Count == 0 ? null : codes,
            Severity = severity,
            SinceUtc = since,
            Limit = limit,
        }, null);
    }

    /// <summary>
    /// Resolve the gateway id from the loaded configuration. Returns null
    /// if config isn't initialised yet — the events endpoint still serves,
    /// just without origin stamping (graceful degradation).
    /// </summary>
    private static async System.Threading.Tasks.Task<string?> TryGetGatewayIdAsync(
        IConfigurationManager configManager,
        CancellationToken ct)
    {
        try
        {
            var config = await configManager.GetCurrentAsync(ct).ConfigureAwait(false);
            return config.Gateway.GatewayId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stamp every event in the response with the gateway id. Uses record
    /// <c>with</c> to keep the operation allocation-cheap.
    /// </summary>
    internal static DiagnosticsEventsResponse StampGatewayId(DiagnosticsEventsResponse response, string? gatewayId)
    {
        if (gatewayId is null) return response;
        var stamped = new List<DiagnosticsEventDto>(response.Events.Count);
        foreach (var e in response.Events)
        {
            stamped.Add(e with { GatewayId = gatewayId });
        }
        return response with { Events = stamped };
    }
}
