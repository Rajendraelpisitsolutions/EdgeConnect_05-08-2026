// ============================================================================
// File: Adapters/SourceRegistration.cs
// Purpose: DI registration record that pairs a constructed ISourceAdapter
//          with the configuration to initialize it and the route id whose
//          intake channel receives its points. The host's source supervisor
//          consumes a list of these.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 4.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Source-side registration consumed by <see cref="SourceSupervisor"/>.
/// One instance per source connector. The host's composition root (or a
/// test) populates an <c>IEnumerable&lt;SourceRegistration&gt;</c> in DI.
/// </summary>
public sealed record SourceRegistration
{
    /// <summary>The constructed adapter instance. Not yet initialized.</summary>
    public required ISourceAdapter Adapter { get; init; }

    /// <summary>The configuration to pass to <c>InitializeAsync</c>.</summary>
    public required SourceConfiguration Config { get; init; }

    /// <summary>The route id whose intake channel receives this source's points.</summary>
    public required string RouteId { get; init; }
}
