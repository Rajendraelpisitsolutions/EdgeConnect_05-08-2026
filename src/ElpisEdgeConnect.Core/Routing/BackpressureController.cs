// ============================================================================
// File: Routing/BackpressureController.cs
// Purpose: POLICY decision maker for route backpressure. Given the current
//          channel and buffer pressure signals, returns one of
//          {Pass, Spill, Drop}. Pure — no payloads, no state, no side
//          effects. The execution side lives in RouteChannelSpillover and
//          the buffer.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.8 (Backpressure)
// Milestone: C3 Commit 2 (phase 6 — backpressure).
//
// LOCKED design rules (phase 6 guardrails):
//   * Policy vs mechanism split: this class DECIDES only. It never touches
//     CanonicalDataPoint payloads. A structural test
//     (BackpressureControllerStructureTests) pins this at reflection time.
//   * The controller reads two binary pressure signals (channel has room,
//     buffer has room) plus the configured DropPolicy. That's it.
//   * "Sources are never blocked indefinitely" per blueprint §19.8: the
//     controller may return Spill or Drop but never returns Pass when it
//     would cause the caller to block on a full channel.
//   * The buffer remains the single source of durable truth. Spill writes
//     go to the buffer; drops are counted but never re-routed anywhere.
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Pure backpressure policy decision-maker. Constructed with the buffer's
/// drop policy; decisions depend only on the two pressure signals supplied
/// to <see cref="Decide"/>.
/// </summary>
public sealed class BackpressureController
{
    private readonly DropPolicy _dropPolicy;

    /// <summary>
    /// Construct a controller for a route whose buffer is configured with
    /// <paramref name="dropPolicy"/>.
    /// </summary>
    public BackpressureController(DropPolicy dropPolicy)
    {
        _dropPolicy = dropPolicy;
    }

    /// <summary>The drop policy this controller was configured with.</summary>
    public DropPolicy DropPolicy => _dropPolicy;

    /// <summary>
    /// Decide what to do with a batch given the current pressure signals.
    /// </summary>
    /// <param name="channelHasRoom">
    /// <c>true</c> when the producer is in the fast-path comfort zone — the
    /// intake channel can accept the batch without backpressure. Callers
    /// MAY synthesize this signal from a soft watermark on the buffer
    /// (e.g. <c>CurrentDepth &lt; 0.8 * MaxDepth</c>) when no explicit
    /// channel exists between the source and the buffer; the controller
    /// only cares whether the producer is comfortable, not how the caller
    /// measured it.
    /// </param>
    /// <param name="bufferHasRoom">
    /// <c>true</c> if the route buffer is below its <c>MaxDepth</c> ceiling
    /// and the batch will fit under the hard cap.
    /// </param>
    /// <returns>
    /// <see cref="BackpressureDecision.Pass"/> when the channel is healthy;
    /// <see cref="BackpressureDecision.Spill"/> when the channel is full and
    /// the buffer can absorb the overflow, or when <see cref="Configuration.DropPolicy.Block"/>
    /// is configured (we defer the stalling decision to the buffer rather
    /// than dropping upstream);
    /// <see cref="BackpressureDecision.Drop"/> when the channel is full, the
    /// buffer is full, and the drop policy allows dropping.
    /// </returns>
    public BackpressureDecision Decide(bool channelHasRoom, bool bufferHasRoom)
    {
        // Fast path: channel has room, enqueue normally.
        if (channelHasRoom)
        {
            return BackpressureDecision.Pass;
        }

        // Channel is full. Can the buffer still absorb the overflow?
        if (bufferHasRoom)
        {
            return BackpressureDecision.Spill;
        }

        // Channel full AND buffer full. Drop is only legal when the operator
        // has opted into a drop policy. Under DropPolicy.Block we spill anyway
        // and let the buffer's own blocking behavior apply — the controller
        // does NOT silently drop when the operator asked for back-pressure.
        return _dropPolicy == DropPolicy.Block
            ? BackpressureDecision.Spill
            : BackpressureDecision.Drop;
    }
}
