// ============================================================================
// File: MqttPublishMode.cs
// Purpose: Publish strategy selection for the MQTT sink adapter.
// Reference: PHASE2_ENTRY.md — MQTT sink adapter plan
// ============================================================================

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Determines how <c>MqttSinkAdapter</c> publishes canonical data
/// points to the MQTT broker. Two explicit strategies — not a mode flag
/// layered on top of a single code path.
/// </summary>
public enum MqttPublishMode
{
    /// <summary>
    /// One MQTT message per batch. Payload is a JSON array of canonical
    /// points. Topic is resolved once per batch from
    /// <see cref="MqttSinkConfiguration.TopicTemplate"/>. Default mode.
    /// </summary>
    Batch = 0,

    /// <summary>
    /// One MQTT message per point. Payload is the raw scalar value as a
    /// UTF-8 string (formatted by <see cref="RawValueFormatter"/>). Topic
    /// is resolved per point from
    /// <see cref="MqttSinkConfiguration.PerTagTopicTemplate"/> with
    /// <c>{tagName}</c> substitution. EREMOS V2 compatibility mode.
    /// </summary>
    PerTag = 1,
}
