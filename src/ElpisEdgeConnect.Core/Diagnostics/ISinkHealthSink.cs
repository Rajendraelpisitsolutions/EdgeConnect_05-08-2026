// ============================================================================
// File: Diagnostics/ISinkHealthSink.cs
// Purpose: Push seam for sink adapter-level health (protocol/connection
//          state). Distinct from route-level sink outcomes captured via
//          IRoutingEngineDiagnostics.OnSinkDegraded etc — those track
//          publish failures, this tracks the adapter's own connection
//          lifecycle as reported by the host supervisor.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.1
// Milestone: C4 — phase 3.
//
// Design note:
//   Adapter-level state and publish-level state live on the SAME
//   SinkHealthSnapshot but come from different push paths. The collector
//   merges them under one lock; there is no parallel store.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Push-only seam for reporting sink adapter-level state into the
/// diagnostics collector.
/// </summary>
public interface ISinkHealthSink
{
    /// <summary>
    /// Record that a sink adapter reached a new lifecycle state
    /// (Initialized / Running / Faulted / Stopped). The optional error
    /// payload applies to the Faulted case.
    /// </summary>
    void RecordSinkAdapterState(
        string routeId,
        string sinkInstanceId,
        AdapterState state,
        AdapterError? lastError);
}
