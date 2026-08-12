// ============================================================================
// File: Adapters/SinkCapabilities.cs
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.3
//
// Pull/Browse capabilities are present from day one to keep the contract
// forward-compatible with OPC UA Server (Phase 5), which exposes values for
// external clients to read rather than pushing them.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Capabilities declared by a sink adapter. Supports both push-mode sinks
/// (MQTT, HTTP, TCP) and pull-mode sinks (OPC UA Server).
/// </summary>
[Flags]
public enum SinkCapabilities
{
    /// <summary>No capabilities declared. A misconfigured adapter; the runtime will reject it.</summary>
    None = 0,

    /// <summary>
    /// Sink actively pushes data out (MQTT publish, HTTP POST, TCP write).
    /// Uses <c>PublishAsync</c>.
    /// </summary>
    Push = 1 << 0,

    /// <summary>
    /// Sink exposes current values for external clients to read on demand
    /// (OPC UA Server). Uses <c>UpdateCurrentValuesAsync</c>.
    /// </summary>
    Pull = 1 << 1,

    /// <summary>Sink exposes a browse-able node structure.</summary>
    Browse = 1 << 2,

    /// <summary>Sink supports batched publishing for efficiency.</summary>
    Batch = 1 << 3,

    /// <summary>Sink returns per-message ack/nack information.</summary>
    Transactional = 1 << 4,

    /// <summary>Sink supports a connectivity test without starting.</summary>
    TestConnect = 1 << 5,

    /// <summary>
    /// Sink exposes a list of currently-connected clients / sessions
    /// (OPC UA Server: active sessions + subscriptions; MQTT: broker
    /// connection state). UI surfaces this through the diagnostics
    /// surface so operators can see "who's reading from us right now."
    /// </summary>
    SessionTracking = 1 << 6,

    /// <summary>
    /// Sink lets external clients subscribe to receive updates as values
    /// change (OPC UA Server with monitored items). Distinct from
    /// <see cref="Pull"/> which is on-demand read-only.
    /// </summary>
    Subscription = 1 << 7,
}
