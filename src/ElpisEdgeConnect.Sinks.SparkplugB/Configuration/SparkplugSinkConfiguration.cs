// ============================================================================
// File: Configuration/SparkplugSinkConfiguration.cs
// Purpose: Sparkplug B-specific sink configuration extending the base
//          SinkConfiguration. Carries broker connection, the Edge Node namespace
//          identity (group/edge-node), keep-alive, and the bounded
//          transport-recovery budget (plan v3 §4.7). Validation lives in
//          SparkplugSinkConfigurationValidator. The gateway identity-store path
//          is NOT a config field — it is injected by composition (K4), never
//          authored in the connection JSON.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md
//            §1.3/§4.7/§9 (slice 1); ADR-0035 Rule 4.
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Configuration;

/// <summary>
/// Configuration for <see cref="SparkplugSinkAdapter"/>. Inherits identity and
/// batching fields from <see cref="SinkConfiguration"/>; adds broker, Sparkplug
/// namespace, and transport-recovery settings.
/// </summary>
public sealed record SparkplugSinkConfiguration : SinkConfiguration
{
    /// <summary>The protocol-name constant carried in <see cref="SinkInstanceConfig.ProtocolName"/>.</summary>
    public const string ProtocolNameConstant = SparkplugBProtocol.ProtocolName;

    /// <summary>License module key that gates registration of this sink (wired in K4).</summary>
    public const string LicenseModuleKey = "sink-sparkplug-b";

    /// <summary>Default bounded transport-recovery attempt count (plan v3 §4.7).</summary>
    public const int DefaultTransportRecoveryMaxAttempts = 3;

    /// <summary>Default transport-recovery initial backoff delay in milliseconds (plan v3 §4.7).</summary>
    public const int DefaultTransportRecoveryInitialDelayMs = 1000;

    /// <summary>Default transport-recovery backoff cap in milliseconds (plan v3 §4.7).</summary>
    public const int DefaultTransportRecoveryMaxDelayMs = 30000;

    /// <summary>Hostname or IP of the MQTT broker.</summary>
    public required string BrokerHost { get; init; }

    /// <summary>
    /// TCP port. <c>null</c> means "omitted" — the TLS-appropriate default (1883 plain,
    /// 8883 TLS) is applied by <see cref="ResolveBrokerEndpoint"/> (slice-2 review r2 / v3.2).
    /// Distinguishing omitted from an explicit 1883 is what preserves the TLS default port.
    /// </summary>
    public int? BrokerPort { get; init; }

    /// <summary>Enable TLS (server certificate validation only; mutual TLS deferred).</summary>
    public bool UseTls { get; init; }

    /// <summary>MQTT client identifier. When null, auto-generated from the instance id.</summary>
    public string? ClientId { get; init; }

    /// <summary>MQTT username for broker authentication.</summary>
    public string? Username { get; init; }

    /// <summary>MQTT password. Must be set if <see cref="Username"/> is set, and vice versa.</summary>
    public string? Password { get; init; }

    /// <summary>Sparkplug group id (topic level 2). Topic-safe: non-empty, no '/', '+', '#'.</summary>
    public required string GroupId { get; init; }

    /// <summary>Sparkplug edge node id (topic level 4). Topic-safe: non-empty, no '/', '+', '#'.</summary>
    public required string EdgeNodeId { get; init; }

    /// <summary>MQTT keep-alive interval in seconds.</summary>
    public int KeepAliveSeconds { get; init; } = 30;

    /// <summary>
    /// Bounded count of complete transport-suspect session-establishment attempts
    /// before the recovery gives up and faults the route (plan v3 §4.6/§4.7).
    /// </summary>
    public int TransportRecoveryMaxAttempts { get; init; } = DefaultTransportRecoveryMaxAttempts;

    /// <summary>First transport-recovery backoff delay in milliseconds (plan v3 §4.7).</summary>
    public int TransportRecoveryInitialDelayMs { get; init; } = DefaultTransportRecoveryInitialDelayMs;

    /// <summary>Transport-recovery backoff cap in milliseconds; must be >= the initial delay (plan v3 §4.7).</summary>
    public int TransportRecoveryMaxDelayMs { get; init; } = DefaultTransportRecoveryMaxDelayMs;

    /// <summary>
    /// Resolve the normalized broker endpoint via the shared Core
    /// <see cref="BrokerEndpoint"/> — the same type K4 route validation uses. An omitted
    /// <see cref="BrokerPort"/> yields the TLS-appropriate default (1883 plain, 8883 TLS).
    /// </summary>
    /// <returns>The normalized broker endpoint.</returns>
    public BrokerEndpoint ResolveBrokerEndpoint() => BrokerEndpoint.Create(BrokerHost, BrokerPort, UseTls);

    /// <summary>
    /// Translate a loaded <see cref="SinkInstanceConfig"/> DTO into a typed
    /// <see cref="SparkplugSinkConfiguration"/>. Mirrors
    /// <c>MqttSinkConfiguration.FromSinkInstance</c>. Structural presence is
    /// enforced here; semantic validation is
    /// <see cref="SparkplugSinkConfigurationValidator.Validate"/>.
    /// </summary>
    /// <param name="instance">The loaded sink instance DTO.</param>
    /// <returns>The typed configuration.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the protocol name is not <c>"sparkplug-b"</c>, the
    /// <see cref="SinkInstanceConfig.Connection"/> block is missing, or a required
    /// field (<c>brokerHost</c>, <c>groupId</c>, <c>edgeNodeId</c>) is absent.
    /// </exception>
    public static SparkplugSinkConfiguration FromSinkInstance(SinkInstanceConfig instance)
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
                $"SinkInstanceConfig '{instance.InstanceId}' is missing the required Sparkplug Connection " +
                "object (must contain at least 'brokerHost', 'groupId', 'edgeNodeId').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        var brokerHost = ReadString(conn, SparkplugConnectionKeys.BrokerHost)
            ?? throw new ArgumentException(
                $"Sparkplug Connection for sink '{instance.InstanceId}' is missing the required 'brokerHost' field.",
                nameof(instance));
        var groupId = ReadString(conn, SparkplugConnectionKeys.GroupId)
            ?? throw new ArgumentException(
                $"Sparkplug Connection for sink '{instance.InstanceId}' is missing the required 'groupId' field.",
                nameof(instance));
        var edgeNodeId = ReadString(conn, SparkplugConnectionKeys.EdgeNodeId)
            ?? throw new ArgumentException(
                $"Sparkplug Connection for sink '{instance.InstanceId}' is missing the required 'edgeNodeId' field.",
                nameof(instance));

        return new SparkplugSinkConfiguration
        {
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            Enabled = instance.Enabled,

            BrokerHost = brokerHost,
            BrokerPort = ReadNullableInt(conn, SparkplugConnectionKeys.BrokerPort),
            UseTls = ReadBool(conn, SparkplugConnectionKeys.UseTls, defaultValue: false),
            ClientId = ReadString(conn, SparkplugConnectionKeys.ClientId),
            Username = ReadString(conn, SparkplugConnectionKeys.Username),
            Password = ReadString(conn, SparkplugConnectionKeys.Password),
            GroupId = groupId,
            EdgeNodeId = edgeNodeId,
            KeepAliveSeconds = ReadInt(conn, SparkplugConnectionKeys.KeepAliveSeconds, defaultValue: 30),
            TransportRecoveryMaxAttempts = ReadInt(
                conn, SparkplugConnectionKeys.TransportRecoveryMaxAttempts, DefaultTransportRecoveryMaxAttempts),
            TransportRecoveryInitialDelayMs = ReadInt(
                conn, SparkplugConnectionKeys.TransportRecoveryInitialDelayMs, DefaultTransportRecoveryInitialDelayMs),
            TransportRecoveryMaxDelayMs = ReadInt(
                conn, SparkplugConnectionKeys.TransportRecoveryMaxDelayMs, DefaultTransportRecoveryMaxDelayMs),
        };
    }

    /// <summary>Redacted string form — never emits the <see cref="Username"/> or <see cref="Password"/> value.</summary>
    /// <returns>A secret-safe representation.</returns>
    public override string ToString() =>
        $"SparkplugSinkConfiguration {{ InstanceId = {InstanceId}, ProtocolName = {ProtocolName}, " +
        $"BrokerHost = {BrokerHost}, BrokerPort = {BrokerPort?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default"}, UseTls = {UseTls}, " +
        $"GroupId = {GroupId}, EdgeNodeId = {EdgeNodeId}, KeepAliveSeconds = {KeepAliveSeconds}, " +
        $"Username = {(Username is null ? "null" : "***")}, Password = {(Password is null ? "null" : "***")} }}";

    // ---- JSON helpers (mirror MqttSinkConfiguration; strict per slice-1 review B3) ----
    // Absent property -> documented default. Present-but-wrong-type / non-integral /
    // overflow -> reject with a stable SPARKPLUG.CONFIG_INVALID_FIELD_TYPE error (never a
    // silent coercion to the default, which could hide an operator typo on a connection,
    // TLS, or outage-policy field). Only the field NAME is surfaced, never its value.

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.String)
        {
            throw InvalidFieldType(name, "a string");
        }
        return v.GetString();
    }

    private static int ReadInt(JsonElement obj, string name, int defaultValue)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return defaultValue;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var parsed))
        {
            throw InvalidFieldType(name, "an integer");
        }
        return parsed;
    }

    // Absent -> null (omitted); present-but-wrong-type/overflow -> reject.
    private static int? ReadNullableInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var parsed))
        {
            throw InvalidFieldType(name, "an integer");
        }
        return parsed;
    }

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue)
    {
        if (!obj.TryGetProperty(name, out var v))
        {
            return defaultValue;
        }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidFieldType(name, "a boolean"),
        };
    }

    private static AdapterException InvalidFieldType(string name, string expected) =>
        AdapterException.Configuration(
            SparkplugErrors.ConfigInvalidFieldType,
            $"Sparkplug connection field '{name}' must be {expected}.");
}
