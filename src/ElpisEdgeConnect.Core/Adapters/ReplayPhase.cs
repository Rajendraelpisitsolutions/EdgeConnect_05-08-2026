// ============================================================================
// File: Adapters/ReplayPhase.cs
// Purpose: Protocol-neutral replay phase a batch belongs to. Core exposes only
//          these three; protocol-specific session states (e.g. a Sparkplug
//          actor's Birthing/Rebirthing) live in the sink adapter, never here.
// Reference: docs/decisions/0036-*.md Rule 6/7; plan v2.3 §1.
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// The replay phase of a published batch. This is the sole phase vocabulary Core
/// exposes to a replay-aware sink; the sink must not infer phase from timestamps.
/// </summary>
public enum ReplayPhase
{
    /// <summary>Historical backlog present at replay-epoch capture (buffer sequence &lt; cutoff).</summary>
    Replay = 0,

    /// <summary>Data that arrived after epoch capture while backlog was still draining.</summary>
    CatchUp = 1,

    /// <summary>Steady-state live data (no backlog gap).</summary>
    Live = 2,
}
