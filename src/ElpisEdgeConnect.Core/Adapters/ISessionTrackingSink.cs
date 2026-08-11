// ============================================================================
// File: Adapters/ISessionTrackingSink.cs
// Purpose: Optional capability interface implemented by sink adapters
//          that declare SinkCapabilities.SessionTracking. Exposes a
//          uniform read-only view of active client sessions for the
//          host's diagnostics surface to poll.
//
//          Keeps the data path on ISinkAdapter narrow — only the
//          handful of sinks that have meaningful "sessions" (OPC UA
//          Server today) implement this seam. The poll loop in
//          ElpisEdgeConnect.Host periodically reads ActiveSessions and
//          publishes through ISinkSessionHealthSink.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.3 (sink capabilities)
//            docs/PHASE4_EXECUTION_PLAN.md Milestone H.2
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Diagnostics;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Implemented by sink adapters that track external client sessions
/// and want them surfaced on the diagnostics surface. Adapters that
/// declare <see cref="SinkCapabilities.SessionTracking"/> SHOULD
/// implement this interface; the host's session-poller checks for it
/// reflectively and skips adapters that don't.
/// </summary>
public interface ISessionTrackingSink
{
    /// <summary>
    /// Snapshot of the sink's currently-active client sessions. Cheap
    /// to call — returns the in-memory view, no I/O. An empty list is
    /// valid (no clients connected); a null return value is not.
    /// </summary>
    IReadOnlyList<SinkSessionSummary> ActiveSessions { get; }
}
