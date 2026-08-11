// ============================================================================
// File: Contracts/SourceListItemDto.cs
// Purpose: List-view DTO for /api/v1/sources — pairs each source's
//          summary with the route it belongs to. The route context
//          matters because operators jump between "which route is
//          this source on?" and "what is this source doing?" all
//          day during commissioning.
//
//          Reuses RouteSourceSummaryDto for the source fields so the
//          wire shape stays consistent with the Overview cards and
//          we don't surface a parallel "almost the same fields"
//          contract. The DataGrid binds directly to this shape;
//          sortable columns: routeId, sourceInstanceId, protocolName,
//          stateName, pointsObserved, lastPointAtUtc.
//
// M.2b.1.1 — RouteId became nullable. Inventory is now config-driven:
// a configured source that is not (yet) wired into a route still
// appears in the list with RouteId = null and a synthetic state
// (Configured / Disabled). The contract pattern this enforces is
//     Configuration = inventory truth
//     Diagnostics  = runtime enrichment
// and it generalises to sinks, routes, tags, and exposed OPC UA nodes.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1b / M.2b.1.1
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// One row in the Sources page's DataGrid. Wraps a
/// <see cref="RouteSourceSummaryDto"/> with the route id that
/// owns it, or <c>null</c> when the source is configured but not
/// wired to any route.
/// </summary>
public sealed record SourceListItemDto
{
    /// <summary>
    /// The route this source serves, or <c>null</c> when the source is
    /// configured but no route references it yet. The "Do not wire yet"
    /// branch of the Add-Source wizard produces this state intentionally.
    /// </summary>
    public string? RouteId { get; init; }

    /// <summary>Source-side summary fields (instance id, protocol, state, counters, last error).</summary>
    public required RouteSourceSummaryDto Source { get; init; }
}
