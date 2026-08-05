// ============================================================================
// File: MTConnectSourceConfiguration.cs
// Purpose: Configuration record for an MTConnect source adapter instance.
//          Derives from SourceConfiguration. Carries the Agent URL + optional
//          device name, timeouts, and backoff parameters. Also exposes the
//          FromSourceInstance(SourceInstanceConfig) static factory used by
//          the host to translate JSON-loaded DTOs into typed configs.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.2, §8
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Configuration for a single MTConnect Agent connection. The Agent exposes
/// <c>/probe</c> and <c>/current</c> endpoints; the adapter polls
/// <c>/current</c> on every <see cref="SourceConfiguration.PollIntervalMs"/>.
/// </summary>
public sealed record MTConnectSourceConfiguration : SourceConfiguration
{
    /// <summary>The protocol name constant used by config DTOs.</summary>
    public const string ProtocolNameConstant = "mtconnect";

    /// <summary>
    /// License module key that gates registration of this protocol's
    /// source adapters. Mirrors <c>LicenseModuleKeys.SourceMtconnect</c>;
    /// see <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "source-mtconnect";

    // ---- Connection ----

    /// <summary>
    /// Base URL of the MTConnect Agent, e.g. <c>http://192.168.1.10:5000/</c>.
    /// Trailing slash is tolerated. HTTPS is supported.
    /// </summary>
    public required string AgentBaseUrl { get; init; }

    /// <summary>
    /// Optional Agent-side device name. When non-empty, requests are scoped
    /// to <c>{AgentBaseUrl}{DeviceName}/current</c>. Leave blank to hit the
    /// root endpoint (the Agent's default device).
    /// </summary>
    public string? AgentDeviceName { get; init; }

    /// <summary>HTTP request timeout, seconds. Default 10.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    // ---- Backoff ----

    /// <summary>Initial delay in ms after the first collection failure.</summary>
    public int InitialBackoffMs { get; init; } = 2000;

    /// <summary>Maximum backoff delay in ms.</summary>
    public int MaxBackoffMs { get; init; } = 60_000;

    /// <summary>Multiplier applied to backoff on each consecutive failure.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Number of consecutive failures after which the adapter reports
    /// <see cref="AdapterState.Degraded"/>. The adapter never transitions
    /// to <c>Failed</c> on transient HTTP errors — the design assumes the
    /// Agent may be restarted without bringing the host down.
    /// </summary>
    public int DegradeAfterConsecutiveFailures { get; init; } = 3;

    /// <summary>
    /// Translate a loaded <see cref="SourceInstanceConfig"/> DTO into a
    /// typed <see cref="MTConnectSourceConfiguration"/>. Reads the
    /// protocol-specific fields out of <see cref="SourceInstanceConfig.Connection"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the protocol is not <c>"mtconnect"</c>, or when the
    /// <see cref="SourceInstanceConfig.Connection"/> block is missing the
    /// required <c>agentBaseUrl</c> field.
    /// </exception>
    public static MTConnectSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the " +
                "required MTConnect Connection object (must contain at least 'agentBaseUrl').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        // Keys named via MTConnectConnectionKeys so they cannot be parsed
        // without a redaction tier (ADR-0020 M-B Q-B2); the drift guard asserts
        // MTConnectConnectionKeys.All == KnownKeys.
        var agentBaseUrl = ReadString(conn, MTConnectConnectionKeys.AgentBaseUrl)
            ?? throw new ArgumentException(
                $"MTConnect Connection for source '{instance.InstanceId}' is missing " +
                "the required 'agentBaseUrl' field.", nameof(instance));

        return new MTConnectSourceConfiguration
        {
            // Base SourceConfiguration fields
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            DisplayName = instance.DeviceName,
            Enabled = instance.Enabled,
            PollIntervalMs = instance.Polling.IntervalMs,
            DeviceId = instance.DeviceId,
            DeviceName = instance.DeviceName,
            DeviceClass = instance.DeviceClass,
            Tags = instance.Tags,

            // MTConnect-specific fields
            AgentBaseUrl = agentBaseUrl,
            AgentDeviceName = ReadString(conn, MTConnectConnectionKeys.AgentDeviceName),
            TimeoutSeconds = ReadInt(conn, MTConnectConnectionKeys.TimeoutSeconds, defaultValue: 10),
            InitialBackoffMs = ReadInt(conn, MTConnectConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, MTConnectConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, MTConnectConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            DegradeAfterConsecutiveFailures = ReadInt(conn, MTConnectConnectionKeys.DegradeAfterConsecutiveFailures, defaultValue: 3),
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

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : defaultValue;
}
