// ============================================================================
// File: Routing/RouteDefinition.cs
// Purpose: Inputs handed to the routing engine when a route is registered.
//          The host (D1) resolves configuration IDs into concrete source
//          intakes, sink adapters, and a transform pipeline, then passes
//          this immutable definition to IRoutingEngine.RegisterRouteAsync.
// Reference: ARCHITECTURE_BLUEPRINT.md §19, PHASE1_EXECUTION_PLAN.md C3
// Milestone: C3 Commit 2 (phase 1 — happy path)
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Pipeline;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Immutable route registration payload. All identifiers are expected to
/// already be resolved — the routing engine does not walk configuration.
/// </summary>
public sealed record RouteDefinition
{
    /// <summary>Stable route identifier (matches <see cref="RouteConfig.RouteId"/>).</summary>
    public required string RouteId { get; init; }

    /// <summary>Owning gateway id — flows into <see cref="TransformContext.GatewayId"/>.</summary>
    public required string GatewayId { get; init; }

    /// <summary>Resolved source intake for this route.</summary>
    public required ISourceIntake Source { get; init; }

    /// <summary>
    /// Resolved sink adapters. Must contain at least one entry. Phase 1 of
    /// C3 Commit 2 exercises the single-sink happy path; fanout across
    /// multiple sinks comes in phase 2.
    /// </summary>
    public required IReadOnlyList<ISinkAdapter> Sinks { get; init; }

    /// <summary>Compiled tag filter. Defaults to accept-all.</summary>
    public TagFilter Filter { get; init; } = TagFilter.AcceptAll;

    /// <summary>
    /// Optional transform pipeline. When null, points pass through unchanged
    /// after the tag filter.
    /// </summary>
    public TransformPipeline? Pipeline { get; init; }

    /// <summary>Per-route runtime buffer policy (materialized from the DTO).</summary>
    public required BufferPolicy BufferPolicy { get; init; }

    /// <summary>Per-route delivery policy.</summary>
    public required DeliveryPolicyConfig Delivery { get; init; }
}
