// ============================================================================
// File: EthernetIpSourceConfiguration.cs
// Purpose: Configuration record for an EtherNet/IP source adapter instance.
//          Derives from SourceConfiguration. Includes FromSourceInstance(…)
//          factory that parses the opaque SourceInstanceConfig.Connection JSON
//          into a typed config — same bridge pattern used by Modbus / S7.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §5.2;
//            ARCHITECTURE_BLUEPRINT.md §8
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Configuration for a single EtherNet/IP source connection to an Allen-Bradley
/// controller.
/// </summary>
public sealed record EthernetIpSourceConfiguration : SourceConfiguration
{
    /// <summary>The protocol name constant used by config DTOs.</summary>
    public const string ProtocolNameConstant = "ethernetip";

    /// <summary>
    /// License module key that gates registration of this protocol's source
    /// adapters. Mirrors <c>LicenseModuleKeys.SourceEthernetIp</c>; duplicated
    /// as a local <c>const</c> so the per-protocol project does not need a Core
    /// reference solely to expose the key string.
    /// </summary>
    public const string LicenseModuleKey = "source-ethernet-ip";

    // ---- Connection ----

    /// <summary>Gateway IP / hostname of the controller's Ethernet port.</summary>
    public required string Host { get; init; }

    /// <summary>
    /// CIP routing path to the CPU. Logix controllers route across the
    /// backplane (<c>"1,0"</c> — the L8x front-port default); embedded-port
    /// families take an empty path.
    /// </summary>
    public string Path { get; init; } = "1,0";

    /// <summary>Rockwell CPU family — selects the libplctag <c>plc</c> token.</summary>
    public EthernetIpCpuFamily CpuFamily { get; init; } = EthernetIpCpuFamily.ControlLogix;

    /// <summary>CIP session connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;

    /// <summary>Per-read response timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; init; } = 1000;

    // ---- Retry / Backoff / Breaker ----

    /// <summary>Max per-read retries before surfacing a failure.</summary>
    public int MaxTransactionRetries { get; init; } = 2;

    /// <summary>Initial backoff in ms after a fatal session failure.</summary>
    public int InitialBackoffMs { get; init; } = 2000;

    /// <summary>Maximum backoff in ms — caps exponential growth.</summary>
    public int MaxBackoffMs { get; init; } = 60_000;

    /// <summary>Exponential multiplier between consecutive failures.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Consecutive failures that flip the circuit breaker to OPEN.</summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>
    /// Breaker cool-down window in ms before the next probe. Defaults to 10s
    /// (matching Modbus) so a controller that drops off the network is retried
    /// quickly and the route recovers within seconds of the PLC coming back,
    /// rather than sitting idle for the old 30s window.
    /// </summary>
    public int CircuitBreakerResetMs { get; init; } = 10_000;

    // ---- Tag list ----

    /// <summary>Per-tag definitions. Operators author these manually in the MVP wizard.</summary>
    public IReadOnlyList<EthernetIpTagDefinition> TagDefinitions { get; init; } = [];

    /// <summary>
    /// Translate a loaded <see cref="SourceInstanceConfig"/> DTO into a typed
    /// <see cref="EthernetIpSourceConfiguration"/>. Reads EtherNet/IP-specific
    /// fields from the opaque <c>Connection</c> JSON block.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the protocol is not <c>"ethernetip"</c>, when the connection
    /// block is missing, or when <c>host</c> is absent.
    /// </exception>
    public static EthernetIpSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the required " +
                "EtherNet/IP Connection object (must contain at least 'host').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        var host = ReadString(conn, EthernetIpConnectionKeys.Host)
            ?? throw new ArgumentException(
                $"EtherNet/IP Connection for source '{instance.InstanceId}' is missing the required 'host' field.",
                nameof(instance));

        var cpuFamily = EthernetIpCpuFamilyExtensions.ParseOrNull(
            ReadString(conn, EthernetIpConnectionKeys.CpuFamily)) ?? EthernetIpCpuFamily.ControlLogix;

        // Path: explicit value wins; otherwise fall back to the family default
        // (Logix → "1,0", embedded-port families → "").
        var path = ReadString(conn, EthernetIpConnectionKeys.Path) ?? cpuFamily.DefaultPath();

        return new EthernetIpSourceConfiguration
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

            // EtherNet/IP-specific fields
            Host = host,
            Path = path,
            CpuFamily = cpuFamily,
            ConnectTimeoutMs = ReadInt(conn, EthernetIpConnectionKeys.ConnectTimeoutMs, defaultValue: 2000),
            RequestTimeoutMs = ReadInt(conn, EthernetIpConnectionKeys.RequestTimeoutMs, defaultValue: 1000),
            MaxTransactionRetries = ReadInt(conn, EthernetIpConnectionKeys.MaxTransactionRetries, defaultValue: 2),
            InitialBackoffMs = ReadInt(conn, EthernetIpConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, EthernetIpConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, EthernetIpConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            CircuitBreakerThreshold = ReadInt(conn, EthernetIpConnectionKeys.CircuitBreakerThreshold, defaultValue: 5),
            CircuitBreakerResetMs = ReadInt(conn, EthernetIpConnectionKeys.CircuitBreakerResetMs, defaultValue: 10_000),
            TagDefinitions = ReadTagDefinitions(conn, EthernetIpConnectionKeys.Tags, instance.InstanceId),
        };
    }

    private static List<EthernetIpTagDefinition> ReadTagDefinitions(
        JsonElement conn, string name, string instanceId)
    {
        if (!conn.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var tags = new List<EthernetIpTagDefinition>(v.GetArrayLength());
        var i = 0;
        foreach (var element in v.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    $"EtherNet/IP source '{instanceId}' tags[{i}] must be a JSON object.",
                    nameof(conn));
            }

            var tagName = ReadString(element, EthernetIpConnectionKeys.TagName)
                ?? throw new ArgumentException(
                    $"EtherNet/IP source '{instanceId}' tags[{i}] is missing required field 'name'.");
            var address = ReadString(element, EthernetIpConnectionKeys.Address)
                ?? throw new ArgumentException(
                    $"EtherNet/IP source '{instanceId}' tags[{i}] ('{tagName}') is missing required field 'address'.");

            tags.Add(new EthernetIpTagDefinition
            {
                Name = tagName,
                Address = address,
                Datatype = ReadString(element, EthernetIpConnectionKeys.Datatype),
                ScanRateMs = ReadInt(element, EthernetIpConnectionKeys.ScanRateMs, defaultValue: 1000),
                Scale = ReadOptionalDouble(element, EthernetIpConnectionKeys.Scale),
                Offset = ReadOptionalDouble(element, EthernetIpConnectionKeys.Offset),
                Unit = ReadString(element, EthernetIpConnectionKeys.Unit),
            });
            i++;
        }
        return tags;
    }

    // ---- JSON helpers (mirror Modbus / S7 style for consistency) ----

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

    private static double? ReadOptionalDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}

/// <summary>
/// Configuration for a single EtherNet/IP controller tag. Operators author the
/// symbolic <see cref="Address"/> directly (e.g. <c>"Program:MainProgram.Speed"</c>).
/// </summary>
public sealed record EthernetIpTagDefinition
{
    /// <summary>Canonical tag name emitted on the pipeline (e.g. <c>"spindle_speed"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Controller tag address / symbolic name read over CIP.</summary>
    public required string Address { get; init; }

    /// <summary>
    /// Element-type hint (<c>BOOL</c>, <c>SINT</c>, <c>INT</c>, <c>DINT</c>,
    /// <c>LINT</c>, <c>REAL</c>, <c>LREAL</c>, <c>STRING</c>). Case-insensitive;
    /// the validator rejects unknown values.
    /// </summary>
    public string? Datatype { get; init; }

    /// <summary>Scan period in milliseconds.</summary>
    public int ScanRateMs { get; init; } = 1000;

    /// <summary>
    /// Optional linear scale factor: <c>scaled = (raw * Scale) + Offset</c>.
    /// Rejected for <c>BOOL</c> and <c>STRING</c> datatypes by the validator.
    /// </summary>
    public double? Scale { get; init; }

    /// <summary>Optional additive offset. See <see cref="Scale"/>.</summary>
    public double? Offset { get; init; }

    /// <summary>Optional engineering unit copied to <c>CanonicalDataPoint.Unit</c>.</summary>
    public string? Unit { get; init; }
}
