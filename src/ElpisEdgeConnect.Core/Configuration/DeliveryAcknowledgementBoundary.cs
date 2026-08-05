// ============================================================================
// File: Configuration/DeliveryAcknowledgementBoundary.cs
// Purpose: How far a destination protocol can acknowledge a publish. Governs
//          what an AtLeastOnce route can actually guarantee (blueprint §19.7,
//          ADR-0036). A route declares a required boundary; a sink advertises
//          the boundary it can offer; validation rejects a requirement that
//          exceeds the sink's boundary.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.7; docs/decisions/0036-*.md Rule 1.
//
// LOCKED: the numeric values are ordered and load-bearing (validation compares
//         required > available). Do not reorder or renumber.
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// The strength of acknowledgment a destination protocol provides for a publish,
/// as a totally-ordered scale. A route's
/// <see cref="DeliveryPolicyConfig.RequiredAcknowledgementBoundary"/> must not
/// exceed the boundary a sink advertises.
/// </summary>
// Persisted as its member name (like every other config enum, e.g. DeliveryMode /
// BufferMode). Without this the config round-trips inconsistently — written as a
// string but read as a number — which throws on load and crashes startup.
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryAcknowledgementBoundary>))]
public enum DeliveryAcknowledgementBoundary
{
    /// <summary>
    /// No acknowledgment and no durability requirement. The default for legacy
    /// configurations that predate this field, preserving their existing
    /// behavior (never rendered to operators as an unqualified guarantee).
    /// </summary>
    None = 0,

    /// <summary>
    /// The publish completed at the local transport with no observable error
    /// (e.g. MQTT QoS 0). Durable store-and-forward is possible, but broker
    /// receipt is NOT confirmed.
    /// </summary>
    LocalTransport = 1,

    /// <summary>The broker positively acknowledged receipt (e.g. MQTT QoS 1 PUBACK).</summary>
    Broker = 2,

    /// <summary>The receiving application positively acknowledged processing.</summary>
    Application = 3,
}
