// ============================================================================
// File: Adapters/SinkRegistration.cs
// Purpose: DI registration record that pairs a constructed ISinkAdapter
//          with the configuration to initialize it and the route id it
//          belongs to. The host's sink supervisor consumes a list of these.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 4.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Sink-side registration consumed by <see cref="SinkSupervisor"/>.
/// One instance per sink connector.
/// </summary>
public sealed record SinkRegistration
{
    /// <summary>The constructed adapter instance. Not yet initialized.</summary>
    public required ISinkAdapter Adapter { get; init; }

    /// <summary>The configuration to pass to <c>InitializeAsync</c>.</summary>
    public required SinkConfiguration Config { get; init; }

    /// <summary>The route id this sink belongs to.</summary>
    public required string RouteId { get; init; }
}
