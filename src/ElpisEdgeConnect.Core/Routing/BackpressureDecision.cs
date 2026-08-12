// ============================================================================
// File: Routing/BackpressureDecision.cs
// Purpose: Outcome of a BackpressureController.Decide call. The controller
//          decides; RouteChannelSpillover (and the buffer) executes.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.8 (Backpressure)
// Milestone: C3 Commit 2 (phase 6 — backpressure).
// ============================================================================

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// The three possible outcomes of a backpressure decision. The policy/mechanism
/// split is locked: <see cref="BackpressureController"/> returns one of these
/// values, and <see cref="RouteChannelSpillover"/> (or the buffer) carries
/// out the action. The controller never moves payloads itself.
/// </summary>
public enum BackpressureDecision
{
    /// <summary>
    /// Normal fast path. The intake channel has room — enqueue the batch
    /// into the route buffer via the usual path.
    /// </summary>
    Pass = 0,

    /// <summary>
    /// The intake channel cannot accept more and the buffer still has room.
    /// Spill the batch directly into the buffer via
    /// <see cref="RouteChannelSpillover"/>. The buffer remains the single
    /// source of durable truth.
    /// </summary>
    Spill = 1,

    /// <summary>
    /// Both the intake channel and the buffer are at capacity and the
    /// configured <see cref="Configuration.DropPolicy"/> allows dropping.
    /// The caller drops the batch and records a
    /// <see cref="BackpressureDroppedEvent"/>.
    /// </summary>
    Drop = 2,
}
