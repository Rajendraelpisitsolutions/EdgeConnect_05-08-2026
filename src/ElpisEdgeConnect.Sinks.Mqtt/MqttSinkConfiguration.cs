// ============================================================================
// File: MqttSinkConfiguration.cs
// Purpose: MQTT-specific configuration extending the base SinkConfiguration.
//          Carries connection, topic, QoS, and reconnect settings. Validation
//          lives in MqttSinkAdapter.ValidateConfigAsync.
// Reference: PHASE2_ENTRY.md — MQTT sink adapter plan
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sinks.Mqtt;

/// <summary>
/// Configuration for <c>MqttSinkAdapter</c>. Inherits identity and
/// batching fields from <see cref="SinkConfiguration"/>; adds MQTT-specific
/// connection, topic, and publish-mode settings.
/// </summary>
public sealed record MqttSinkConfiguration : SinkConfiguration
{
    /// <summary>Hostname or IP of the MQTT broker.</summary>
    public required string BrokerHost { get; init; }

    /// <summary>TCP port. Default 1883 (unencrypted) or 8883 (TLS).</summary>
    public int BrokerPort { get; init; } = 1883;

    /// <summary>Enable TLS (server certificate validation only; mutual TLS deferred).</summary>
    public bool UseTls { get; init; }

    /// <summary>
    /// MQTT client identifier. When null, auto-generated as
    /// <c>"edgeconnect-{hostname}-{InstanceId}"</c> — see
    /// <c>MqttSinkAdapter.DefaultClientId</c> for why the hostname is in there.
    /// Set this explicitly when a broker's ACLs are keyed on a fixed id.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>MQTT username for broker authentication.</summary>
    public string? Username { get; init; }

    /// <summary>
    /// MQTT password. Must be set if <see cref="Username"/> is set, and
    /// vice versa — partial auth is a config error.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Publish strategy: <see cref="MqttPublishMode.Batch"/> (default,
    /// JSON array per batch) or <see cref="MqttPublishMode.PerTag"/>
    /// (raw scalar per point, EREMOS V2 compatibility).
    /// </summary>
    public MqttPublishMode PublishMode { get; init; } = MqttPublishMode.Batch;

    /// <summary>
    /// Topic template for <see cref="MqttPublishMode.Batch"/> mode.
    /// Supports placeholders: <c>{gatewayId}</c>, <c>{sourceId}</c>,
    /// <c>{routeId}</c>.
    /// </summary>
    public string TopicTemplate { get; init; } = "edgeconnect/{gatewayId}/data";

    /// <summary>
    /// Topic template for <see cref="MqttPublishMode.PerTag"/> mode.
    /// Supports all Batch placeholders plus <c>{tagName}</c> and
    /// <c>{deviceClass}</c>. Default matches the per-tag MQTT contract
    /// in <c>shared-knowledge/contracts/eremos-per-tag-mqtt.md</c>:
    /// <c>eremos/+/+/+/+</c> on the EREMOS V2 subscriber side. CNC adapters
    /// (FOCAS2, MTConnect) automatically supply <c>{deviceClass} = "cnc"</c>
    /// so this default produces the same topic shape they emitted before
    /// the contract change.
    /// </summary>
    public string PerTagTopicTemplate { get; init; } = "eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}";

    /// <summary>MQTT QoS level: 0 (at-most-once) or 1 (at-least-once).</summary>
    public int QosLevel { get; init; } = 1;

    /// <summary>MQTT retain flag default.</summary>
    public bool RetainDefault { get; init; }

    /// <summary>MQTT keep-alive interval in seconds.</summary>
    public int KeepAliveSeconds { get; init; } = 30;

    /// <summary>Initial reconnect delay in milliseconds.</summary>
    public int ReconnectDelayMs { get; init; } = 5000;

    /// <summary>Maximum reconnect delay in milliseconds (cap for exponential backoff).</summary>
    public int MaxReconnectDelayMs { get; init; } = 60000;

    /// <summary>Exponential backoff multiplier for reconnect delay.</summary>
    public double ReconnectMultiplier { get; init; } = 2.0;

    /// <summary>The protocol-name constant carried in <see cref="SinkInstanceConfig.ProtocolName"/>.</summary>
    public const string ProtocolNameConstant = "mqtt";

    /// <summary>
    /// License module key that gates registration of this sink. Mirrors
    /// <c>LicenseModuleKeys.SinkMqtt</c>; see
    /// <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "sink-mqtt";

    /// <summary>
    /// Translate a loaded <see cref="SinkInstanceConfig"/> DTO into a typed
    /// <see cref="MqttSinkConfiguration"/>. Mirrors the pattern used by
    /// <c>Focas2SourceConfiguration.FromSourceInstance</c> on the source side.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the protocol name is not <c>"mqtt"</c> or when the
    /// <see cref="SinkInstanceConfig.Connection"/> block is missing the
    /// required <c>brokerHost</c> field.
    /// </exception>
    public static MqttSinkConfiguration FromSinkInstance(SinkInstanceConfig instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(instance.ProtocolName, ProtocolNameConstant, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected protocolName '{ProtocolNameConstant}', got '{instance.ProtocolName}'.",
                nameof(instance));
        }
        if (instance.Connection is null || instance.Connection.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"SinkInstanceConfig '{instance.InstanceId}' is missing the required " +
                "MQTT Connection object (must contain at least 'brokerHost').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        var brokerHost = ReadString(conn, MqttConnectionKeys.BrokerHost)
            ?? throw new ArgumentException(
                $"MQTT Connection for sink '{instance.InstanceId}' is missing the required 'brokerHost' field.",
                nameof(instance));

        return new MqttSinkConfiguration
        {
            // Base SinkConfiguration fields
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            Enabled = instance.Enabled,

            // MQTT-specific connection fields. Keys are named via
            // MqttConnectionKeys so they cannot be parsed without a redaction
            // tier (ADR-0020 M-B Q-B2 — "by construction"); the redaction drift
            // guard asserts MqttConnectionKeys.All == MqttBundleRedactionRules.KnownKeys.
            BrokerHost = brokerHost,
            BrokerPort = ReadInt(conn, MqttConnectionKeys.BrokerPort, defaultValue: 1883),
            UseTls = ReadBool(conn, MqttConnectionKeys.UseTls, defaultValue: false),
            ClientId = ReadString(conn, MqttConnectionKeys.ClientId),
            Username = ReadString(conn, MqttConnectionKeys.Username),
            Password = ReadString(conn, MqttConnectionKeys.Password),
            PublishMode = ReadPublishMode(conn, MqttConnectionKeys.PublishMode, MqttPublishMode.Batch),
            TopicTemplate = ReadString(conn, MqttConnectionKeys.TopicTemplate) ?? "edgeconnect/{gatewayId}/data",
            PerTagTopicTemplate = ReadString(conn, MqttConnectionKeys.PerTagTopicTemplate)
                ?? "eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}",
            QosLevel = ReadInt(conn, MqttConnectionKeys.QosLevel, defaultValue: 1),
            RetainDefault = ReadBool(conn, MqttConnectionKeys.RetainDefault, defaultValue: false),
            KeepAliveSeconds = ReadInt(conn, MqttConnectionKeys.KeepAliveSeconds, defaultValue: 30),
            ReconnectDelayMs = ReadInt(conn, MqttConnectionKeys.ReconnectDelayMs, defaultValue: 5000),
            MaxReconnectDelayMs = ReadInt(conn, MqttConnectionKeys.MaxReconnectDelayMs, defaultValue: 60000),
            ReconnectMultiplier = ReadDouble(conn, MqttConnectionKeys.ReconnectMultiplier, defaultValue: 2.0),
        };
    }

    // ---- JSON helpers ----

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : defaultValue;

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : defaultValue;

    private static MqttPublishMode ReadPublishMode(JsonElement obj, string name, MqttPublishMode defaultValue)
    {
        var raw = ReadString(obj, name);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        return Enum.TryParse<MqttPublishMode>(raw, ignoreCase: true, out var parsed) ? parsed : defaultValue;
    }
}
