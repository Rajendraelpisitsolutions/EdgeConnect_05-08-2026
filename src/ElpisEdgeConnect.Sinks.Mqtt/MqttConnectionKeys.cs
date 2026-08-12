// ============================================================================
// File: MqttConnectionKeys.cs
// Purpose: The single source of truth for the JSON key names inside an MQTT
//          sink's opaque connection block. Both the config factory
//          (MqttSinkConfiguration.FromSinkInstance) and the redaction rules
//          (MqttBundleRedactionRules) name keys ONLY via these constants, so a
//          key cannot be parsed without also having a redaction tier — the
//          "by construction" guarantee of ADR-0020 M-B plan v2 (Q-B2).
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §3.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// JSON key-name constants for the MQTT sink connection block. The factory and
/// the redaction rules both reference these; the redaction drift guard asserts
/// <see cref="All"/> equals the rules' declared key set.
/// </summary>
public static class MqttConnectionKeys
{
    /// <summary>Hostname or IP of the broker.</summary>
    public const string BrokerHost = "brokerHost";

    /// <summary>TCP port.</summary>
    public const string BrokerPort = "brokerPort";

    /// <summary>Enable TLS.</summary>
    public const string UseTls = "useTls";

    /// <summary>MQTT client identifier.</summary>
    public const string ClientId = "clientId";

    /// <summary>Broker authentication username.</summary>
    public const string Username = "username";

    /// <summary>Broker authentication password (secret).</summary>
    public const string Password = "password";

    /// <summary>Publish strategy (Batch / PerTag).</summary>
    public const string PublishMode = "publishMode";

    /// <summary>Batch-mode topic template.</summary>
    public const string TopicTemplate = "topicTemplate";

    /// <summary>Per-tag-mode topic template.</summary>
    public const string PerTagTopicTemplate = "perTagTopicTemplate";

    /// <summary>QoS level.</summary>
    public const string QosLevel = "qosLevel";

    /// <summary>Retain flag default.</summary>
    public const string RetainDefault = "retainDefault";

    /// <summary>Keep-alive interval in seconds.</summary>
    public const string KeepAliveSeconds = "keepAliveSeconds";

    /// <summary>Initial reconnect delay in ms.</summary>
    public const string ReconnectDelayMs = "reconnectDelayMs";

    /// <summary>Maximum reconnect delay in ms.</summary>
    public const string MaxReconnectDelayMs = "maxReconnectDelayMs";

    /// <summary>Reconnect backoff multiplier.</summary>
    public const string ReconnectMultiplier = "reconnectMultiplier";

    /// <summary>
    /// The complete, ordered list of connection keys the MQTT factory reads.
    /// The redaction drift guard asserts the rules' <c>KnownKeys</c> covers
    /// exactly this set. Declared as an ordered list (not a set) so it renders
    /// deterministically in snapshots, manifests, and review diffs.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        BrokerHost,
        BrokerPort,
        UseTls,
        ClientId,
        Username,
        Password,
        PublishMode,
        TopicTemplate,
        PerTagTopicTemplate,
        QosLevel,
        RetainDefault,
        KeepAliveSeconds,
        ReconnectDelayMs,
        MaxReconnectDelayMs,
        ReconnectMultiplier,
    };
}
