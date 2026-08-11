// ============================================================================
// File: Session/SparkplugProtocolState.cs
// Purpose: The Sparkplug session actor's fine-grained protocol substate
//          (ADR-0036 Rule 7; plan v3 §6). This is an INTERNAL diagnostic surface
//          — it is NOT the ISinkAdapter contract state (that stays the coarse
//          AdapterState). Active-session outage/rebirth is reported through
//          health/Degraded, not through the base AdapterState.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §6, §8.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>
/// The Sparkplug protocol/session substate held by the session actor for
/// diagnostics. The base <c>AdapterState</c> remains the contract surface.
/// </summary>
public enum SparkplugProtocolState
{
    /// <summary>No session; the actor is stopped or not yet started.</summary>
    Stopped = 0,

    /// <summary>Loading the persisted identity/session state (bdSeq, aliases).</summary>
    LoadingSession = 1,

    /// <summary>Establishing the MQTT connection (CONNECT).</summary>
    Connecting = 2,

    /// <summary>Subscribing the exact NCMD topic before birth.</summary>
    SubscribingNcmd = 3,

    /// <summary>Emitting NBIRTH.</summary>
    Birthing = 4,

    /// <summary>Publishing historical replay DATA (is_historical=true).</summary>
    Replaying = 5,

    /// <summary>Publishing catch-up DATA between the birth and cutover boundaries.</summary>
    CatchingUp = 6,

    /// <summary>Live: caught up, publishing current DATA.</summary>
    Live = 7,

    /// <summary>Emitting a same-session rebirth NBIRTH.</summary>
    Rebirthing = 8,

    /// <summary>Running the bounded transport-suspect recovery loop (plan v3 §4.6/§4.7).</summary>
    RecoveringTransport = 9,

    /// <summary>The transport is marked suspect and awaiting a Core-driven rebirth.</summary>
    Suspect = 10,

    /// <summary>Shutting the session down (graceful NDEATH + disconnect).</summary>
    Stopping = 11,

    /// <summary>An unrecoverable fault (store corruption, illegal transition, budget exhausted).</summary>
    Faulted = 12,
}
