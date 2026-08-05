// ============================================================================
// File: Adapters/ISupervisedSourceRegistry.cs
// Purpose: Lookup of per-source ISourceIntake instances created by the
//          source supervisor. The route definition factory (D phase 6)
//          will resolve this registry to wire each route's
//          RouteDefinition.Source. Phase 4 ships the registry; phase 6
//          consumes it.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 4.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Routing;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Read-only lookup of <see cref="ISourceIntake"/> instances keyed by
/// source instance id. Populated by <see cref="SourceSupervisor"/> during
/// construction so the route definition factory can resolve intakes
/// before the routing engine starts.
/// </summary>
public interface ISupervisedSourceRegistry
{
    /// <summary>
    /// Resolve the intake for <paramref name="sourceInstanceId"/>. Returns
    /// <c>null</c> if no source with that id is registered.
    /// </summary>
    ISourceIntake? GetIntake(string sourceInstanceId);

    /// <summary>Ids of every supervised source.</summary>
    IReadOnlyCollection<string> SourceInstanceIds { get; }

    /// <summary>
    /// Resolve the live <see cref="ISourceAdapter"/> for
    /// <paramref name="sourceInstanceId"/> for OBSERVATIONAL reads only (e.g.
    /// protocol-specific diagnostics via a capability interface). Returns the
    /// current supervised instance (hot-reload-correct) or <c>null</c> if no
    /// source with that id is supervised. Callers MUST treat the adapter as
    /// read-only — do not Start/Stop/Poll/mutate it through this accessor.
    /// </summary>
    ISourceAdapter? GetAdapter(string sourceInstanceId);
}
