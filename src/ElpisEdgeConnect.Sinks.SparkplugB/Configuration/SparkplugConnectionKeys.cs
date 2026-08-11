// ============================================================================
// File: Configuration/SparkplugConnectionKeys.cs
// Purpose: The JSON property names read out of a SinkInstanceConfig.Connection
//          block by SparkplugSinkConfiguration.FromSinkInstance. Named constants
//          (mirroring MqttConnectionKeys) so the same key set can back an
//          ADR-0020 bundle-redaction drift guard when redaction is wired in K4.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.8, §9 (slice 1).
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Configuration;

/// <summary>
/// Stable JSON key names for the Sparkplug B sink's connection block. Adding a
/// key is allowed; renaming or removing one is a breaking configuration change.
/// </summary>
public static class SparkplugConnectionKeys
{
    /// <summary>Broker hostname or IP (required).</summary>
    public const string BrokerHost = "brokerHost";

    /// <summary>Broker TCP port.</summary>
    public const string BrokerPort = "brokerPort";

    /// <summary>Whether to connect over TLS.</summary>
    public const string UseTls = "useTls";

    /// <summary>MQTT client identifier override.</summary>
    public const string ClientId = "clientId";

    /// <summary>Broker username (secret pairing partner of <see cref="Password"/>).</summary>
    public const string Username = "username";

    /// <summary>Broker password.</summary>
    public const string Password = "password";

    /// <summary>Sparkplug group id (topic level 2).</summary>
    public const string GroupId = "groupId";

    /// <summary>Sparkplug edge node id (topic level 4).</summary>
    public const string EdgeNodeId = "edgeNodeId";

    /// <summary>MQTT keep-alive interval in seconds.</summary>
    public const string KeepAliveSeconds = "keepAliveSeconds";

    /// <summary>Bounded transport-recovery attempt count (plan v3 §4.7).</summary>
    public const string TransportRecoveryMaxAttempts = "transportRecoveryMaxAttempts";

    /// <summary>Transport-recovery initial backoff delay, milliseconds (plan v3 §4.7).</summary>
    public const string TransportRecoveryInitialDelayMs = "transportRecoveryInitialDelayMs";

    /// <summary>Transport-recovery backoff cap, milliseconds (plan v3 §4.7).</summary>
    public const string TransportRecoveryMaxDelayMs = "transportRecoveryMaxDelayMs";

    /// <summary>
    /// Every connection key, for redaction-drift and completeness checks. A
    /// <see cref="FrozenSet{T}"/> so a caller cannot cast it back to a mutable
    /// set and corrupt the catalog (slice-1 review, non-blocking).
    /// </summary>
    public static readonly FrozenSet<string> All = new[]
    {
        BrokerHost,
        BrokerPort,
        UseTls,
        ClientId,
        Username,
        Password,
        GroupId,
        EdgeNodeId,
        KeepAliveSeconds,
        TransportRecoveryMaxAttempts,
        TransportRecoveryInitialDelayMs,
        TransportRecoveryMaxDelayMs,
    }.ToFrozenSet(StringComparer.Ordinal);
}
