// ============================================================================
// File: Diagnostics/ISinkSessionHealthSink.cs
// Purpose: Push seam for per-sink active-session snapshots. Mirrors
//          IBufferHealthSink's shape: a single Record* method that
//          overwrites the previous snapshot for the (routeId,
//          sinkInstanceId) key, called by a periodic poller in the
//          host (SinkSessionPoller).
// Reference: ARCHITECTURE_BLUEPRINT.md §13 (diagnostics)
//            docs/PHASE4_EXECUTION_PLAN.md Milestone H.2
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Push-only seam for reporting per-sink active-session snapshots
/// into the diagnostics collector. Implemented by
/// <c>RuntimeDiagnosticsCollector</c>; called by the host's
/// <c>SinkSessionPoller</c> on every poll cycle.
/// </summary>
public interface ISinkSessionHealthSink
{
    /// <summary>
    /// Record the latest active-session snapshot for
    /// (<paramref name="routeId"/>, <paramref name="sinkInstanceId"/>).
    /// Idempotent — overwrites any previous snapshot for the pair.
    /// An empty list is a valid "no clients connected" observation;
    /// a null list is treated the same as an empty list.
    /// </summary>
    void RecordActiveSessions(
        string routeId,
        string sinkInstanceId,
        IReadOnlyList<SinkSessionSummary> sessions);
}
