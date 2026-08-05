// ============================================================================
// File: Configuration/DeliveryMode.cs
// Purpose: Delivery semantics for a route.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.7 (Delivery Guarantees) — LOCKED
// Milestone: B1
//
// LOCKED: ExactlyOnce is intentionally absent from this enum. Per blueprint
// §19.7, ExactlyOnce is not supported in v1 because it requires idempotency
// keys and sink-side deduplication that most sinks do not provide. Adding
// ExactlyOnce here is an architectural change and requires blueprint
// revision. The structural test
// `EnumDeserializationTests.DeliveryMode_HasNoExactlyOnceMember` pins this
// guarantee.
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Delivery semantic for a route. Only the two modes supported in v1 per
/// blueprint §19.7 are present.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryMode>))]
public enum DeliveryMode
{
    /// <summary>
    /// Each point is published at most once. On failure the point is dropped.
    /// No retry, no buffer. Use for high-rate non-critical telemetry where
    /// freshness matters more than completeness. Compatible only with
    /// <see cref="BufferMode.None"/>.
    /// </summary>
    AtMostOnce = 0,

    /// <summary>
    /// Each point is published at least once. The routing engine retries
    /// until successful or the buffer retention is exhausted. Duplicates are
    /// possible on retry. Default for production routes; required for use
    /// with <see cref="BufferMode.StoreAndForward"/>.
    /// </summary>
    AtLeastOnce = 1,
}
