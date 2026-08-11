// ============================================================================
// File: Configuration/BufferMode.cs
// Purpose: Buffer mode for a route's per-route message buffer.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4 (BufferPolicy), §6 (Store-and-Forward)
// Milestone: B1
// ============================================================================

using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Buffering mode for a route. Locked at config-load time per
/// blueprint §4.4 and §6.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BufferMode>))]
public enum BufferMode
{
    /// <summary>
    /// No buffering. Points published directly; on sink failure, points are
    /// dropped per <see cref="DropPolicy"/>. Only valid with
    /// <see cref="DeliveryMode.AtMostOnce"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// In-memory bounded channel only. Survives transient sink slowness but
    /// is lost on gateway restart. Suitable for high-rate non-critical telemetry.
    /// </summary>
    InMemory = 1,

    /// <summary>
    /// Persistent SQLite-backed buffer per route, per blueprint §6. Survives
    /// gateway restart and sink outages. The default for production routes.
    /// </summary>
    StoreAndForward = 2,
}
