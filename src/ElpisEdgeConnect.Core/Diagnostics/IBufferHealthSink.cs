// ============================================================================
// File: Diagnostics/IBufferHealthSink.cs
// Purpose: Push seam for per-route buffer stats. Called by RouteWorker on
//          every poll cycle right after it consults BufferStats for back-
//          pressure, so the same stats payload that drives Pass/Spill/Drop
//          decisions is also visible to the operator-facing diagnostics
//          surface (metrics endpoint, /health/ready, soak runner, future
//          management API).
// Reference: ARCHITECTURE_BLUEPRINT.md §6 (Store-and-Forward), §13.1
//            (Three-way diagnostics)
//
// Design note:
//   The buffer stats live in the buffer module (Buffer.BufferStats). The
//   diagnostics collector publishes a derived rollup (BufferHealthSnapshot)
//   that's read-side-only and stripped to the bits operators care about.
//   The collector merges this push under the same lock used by the source /
//   sink / pipeline push paths — single state store, no parallel cache.
// ============================================================================

using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Push-only seam for reporting per-route buffer stats into the diagnostics
/// collector. Implemented by <c>RuntimeDiagnosticsCollector</c>; called by
/// <c>RouteWorker</c> on every poll cycle.
/// </summary>
public interface IBufferHealthSink
{
    /// <summary>
    /// Record the latest buffer-stats observation for <paramref name="routeId"/>.
    /// Idempotent — overwrites any previous observation for the route. The
    /// <paramref name="mode"/> argument is the configured buffer mode for
    /// this route, copied onto the snapshot so dashboards can distinguish
    /// InMemory from StoreAndForward without joining to config.
    /// </summary>
    void RecordBufferStats(string routeId, BufferMode mode, BufferStats stats);
}
