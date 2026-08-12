// ============================================================================
// File: Adapters/ISinkReplayCapabilityClassifier.cs
// Purpose: Classify whether an INCOMING sink configuration resolves to a
//          replay-aware adapter (IReplayAwareSinkAdapter) WITHOUT instantiating
//          or starting it. Used by the M.P2.2 hot-reload coordinator's K1.3
//          replay-sink hot-replace guard to reject an in-place Restart when
//          EITHER the currently-live adapter OR the incoming configuration is
//          replay-aware (a route must not silently switch replay mode).
//
//          K1.3 has no replay-aware sink protocol yet (Sparkplug B lands in K2),
//          so no production classifier is registered and the incoming side is
//          inert; K2 registers the real classifier alongside the Sparkplug sink.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R8;
//            slice-5 review round 1 (blocker 3 — symmetric guard).
// ============================================================================

using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Decides, from configuration alone (no adapter instantiation), whether a sink instance would be a
/// replay-aware adapter. The coordinator's replay-sink hot-replace guard consults this for the INCOMING
/// side of a Sink Restart; the live side is read directly from the supervised adapter type.
/// </summary>
public interface ISinkReplayCapabilityClassifier
{
    /// <summary>True when <paramref name="config"/> resolves to a replay-aware adapter type.</summary>
    /// <param name="config">The incoming sink instance configuration.</param>
    /// <returns>True if the adapter for this config would implement <c>IReplayAwareSinkAdapter</c>.</returns>
    bool IsReplayAware(SinkInstanceConfig config);
}
