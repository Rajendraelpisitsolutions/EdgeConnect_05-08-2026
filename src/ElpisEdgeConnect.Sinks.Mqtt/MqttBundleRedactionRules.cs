// ============================================================================
// File: MqttBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the MQTT sink connection block
//          (ADR-0020 Amendment 1, A1.2 mechanism 2 — "World 2a"). Declares the
//          BundleTier for every connection key. Metadata only — no Management
//          reference, no redaction logic.
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §3.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Redaction rules for the MQTT sink. The only secret in the MQTT connection
/// block is the broker <see cref="MqttConnectionKeys.Password"/> (masked so a
/// restore round-trip can prompt for it); every other key is benign and
/// included verbatim.
/// </summary>
public sealed class MqttBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => MqttSinkConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        new Dictionary<string, BundleTier>
        {
            [MqttConnectionKeys.BrokerHost] = BundleTier.Include,
            [MqttConnectionKeys.BrokerPort] = BundleTier.Include,
            [MqttConnectionKeys.UseTls] = BundleTier.Include,
            [MqttConnectionKeys.ClientId] = BundleTier.Include,
            [MqttConnectionKeys.Username] = BundleTier.Include,
            [MqttConnectionKeys.Password] = BundleTier.Mask,
            [MqttConnectionKeys.PublishMode] = BundleTier.Include,
            [MqttConnectionKeys.TopicTemplate] = BundleTier.Include,
            [MqttConnectionKeys.PerTagTopicTemplate] = BundleTier.Include,
            [MqttConnectionKeys.QosLevel] = BundleTier.Include,
            [MqttConnectionKeys.RetainDefault] = BundleTier.Include,
            [MqttConnectionKeys.KeepAliveSeconds] = BundleTier.Include,
            [MqttConnectionKeys.ReconnectDelayMs] = BundleTier.Include,
            [MqttConnectionKeys.MaxReconnectDelayMs] = BundleTier.Include,
            [MqttConnectionKeys.ReconnectMultiplier] = BundleTier.Include,
        };

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
