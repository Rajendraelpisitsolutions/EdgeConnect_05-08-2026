// ============================================================================
// File: Api/ChecklistApi.cs
// Purpose: GET /api/v1/checklist — runs every check and returns a
//          consolidated ChecklistResponse. One endpoint, no filters
//          — the whole checklist is evaluated in a single call so
//          the Studio's page renders in one round trip.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1d
// ============================================================================

using System;
using System.Threading;
using ElpisEdgeConnect.Management.Checklist;
using ElpisEdgeConnect.Management.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration for the commissioning-checklist read API.
/// </summary>
public static class ChecklistApi
{
    /// <summary>Map the v1 checklist endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapChecklistApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/checklist").WithTags("Checklist");

        group.MapGet("/", async (
            IChecklistEvaluator evaluator,
            CancellationToken ct) =>
        {
            var response = await evaluator.EvaluateAsync(ct).ConfigureAwait(false);
            return Results.Ok(response);
        })
        .WithName("EvaluateChecklist")
        .WithSummary("Evaluate the commissioning checklist against current gateway state.")
        .Produces<ChecklistResponse>(StatusCodes.Status200OK);

        return builder;
    }
}
