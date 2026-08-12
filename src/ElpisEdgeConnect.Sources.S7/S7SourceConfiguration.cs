// ============================================================================
// File: S7SourceConfiguration.cs
// Purpose: Configuration record for the S7 source adapter. Mirrors
//          ModbusTcpSourceConfiguration's shape:
//             - connection (Host, Rack, Slot, ConnectionType, timeouts)
//             - retry / backoff / circuit breaker thresholds
//             - scan-planner knob (MaxGapBytes)
//             - typed tag definitions
//             - FromSourceInstance factory for gateway.json projection
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
//            docs/licensing/module-catalog.md (source-s7)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Configuration record for the Siemens S7 source adapter.
/// </summary>
public sealed record S7SourceConfiguration : SourceConfiguration
{
    /// <summary>Protocol-name constant carried on <c>SourceInstanceConfig.ProtocolName</c>.</summary>
    public const string ProtocolNameConstant = "s7";

    /// <summary>
    /// License module key that gates registration. Mirrors
    /// <c>LicenseModuleKeys.SourceS7</c> from the canonical catalog
    /// (<c>docs/licensing/module-catalog.md</c>).
    /// </summary>
    public const string LicenseModuleKey = "source-s7";

    // ----- Connection -----

    /// <summary>PLC IP address or hostname.</summary>
    public required string Host { get; init; }

    /// <summary>ISO-on-TCP port. S7 default is 102; rarely overridden.</summary>
    public int Port { get; init; } = 102;

    /// <summary>
    /// Hardware rack number. S7-300/400: typically 0. S7-1200/1500: 0.
    /// </summary>
    public int Rack { get; init; }

    /// <summary>
    /// CPU slot number. S7-1200: 1. S7-300: 2. S7-1500: 1.
    /// S7-400: depends on CPU placement.
    /// </summary>
    public int Slot { get; init; } = 1;

    /// <summary>
    /// Connection type — PG (programming device), OP (operator panel),
    /// or Basic (S7Basic, used by most third-party connections).
    /// Defaults to Basic which works against all S7 CPUs.
    /// </summary>
    public S7ConnectionType ConnectionType { get; init; } = S7ConnectionType.Basic;

    /// <summary>
    /// Whether the configured PLC uses Optimized DB Access (S7-1200/1500
    /// default in TIA Portal). When true, byte-offset addressing requires
    /// the operator to verify that each accessed DB has been switched to
    /// non-optimized OR that PUT/GET permissions are enabled. The flag
    /// is informational at MVP — it surfaces in diagnostics and
    /// validation messages.
    /// </summary>
    public bool OptimizedDbAccess { get; init; }

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;

    /// <summary>Per-read request timeout in milliseconds.</summary>
    public int RequestTimeoutMs { get; init; } = 1000;

    /// <summary>Whether to keep the TCP session alive between scans.</summary>
    public bool KeepAlive { get; init; } = true;

    // ----- Retry / Backoff / Circuit Breaker -----

    /// <summary>Maximum per-transaction retry attempts on transient errors.</summary>
    public int MaxTransactionRetries { get; init; } = 2;

    /// <summary>Initial backoff after a connect failure (milliseconds).</summary>
    public int InitialBackoffMs { get; init; } = 2000;

    /// <summary>Maximum backoff cap (milliseconds).</summary>
    public int MaxBackoffMs { get; init; } = 60_000;

    /// <summary>Backoff exponential multiplier.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Consecutive failures that trip the circuit breaker open.</summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>
    /// Reset timeout after which the breaker probes (HalfOpen). Defaults to 10s
    /// (matching Modbus) so a PLC that drops off the network is retried quickly
    /// and the route recovers within seconds of it coming back, rather than
    /// idling for the old 30s window.
    /// </summary>
    public int CircuitBreakerResetMs { get; init; } = 10_000;

    // ----- Scan-planner -----

    /// <summary>
    /// Maximum gap (in bytes) the planner will coalesce into a single
    /// contiguous read. Wider gaps let the planner amortize more tags
    /// per round-trip at the cost of reading bytes the operator didn't
    /// configure. Default 16 bytes — a good balance for typical S7
    /// programs.
    /// </summary>
    public int MaxGapBytes { get; init; } = 16;

    /// <summary>
    /// Maximum bytes per single read. S7 negotiates a PDU size with the
    /// CPU; conservative default of 200 bytes works against every CPU
    /// model (the smallest S7-300 CPUs negotiate ~240 bytes total PDU).
    /// The planner splits requests larger than this into multiple reads.
    /// </summary>
    public int MaxReadBytes { get; init; } = 200;

    // ----- Tag set -----

    /// <summary>
    /// Configured tag definitions. The adapter pre-parses each
    /// <see cref="S7TagDefinition.Address"/> via
    /// <see cref="S7AddressParser"/> at Initialize time and rejects the
    /// config on any parse failure.
    /// </summary>
    public IReadOnlyList<S7TagDefinition> TagDefinitions { get; init; } = [];

    /// <summary>
    /// Project a <see cref="SourceInstanceConfig"/> read from gateway.json
    /// into a typed <see cref="S7SourceConfiguration"/>. Mirrors
    /// <c>ModbusTcpSourceConfiguration.FromSourceInstance</c>.
    /// </summary>
    public static S7SourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the required S7 Connection object.",
                nameof(instance));
        }
        var conn = instance.Connection.Value;

        // Keys named via S7ConnectionKeys so they cannot be parsed without a
        // redaction tier (ADR-0020 M-B Q-B2); the drift guard asserts
        // S7ConnectionKeys.All == KnownKeys.
        var host = ReadString(conn, S7ConnectionKeys.Host)
            ?? throw new ArgumentException(
                $"S7 Connection for source '{instance.InstanceId}' is missing the required 'host' field.",
                nameof(instance));

        var connectionTypeRaw = ReadString(conn, S7ConnectionKeys.ConnectionType) ?? "Basic";
        if (!Enum.TryParse<S7ConnectionType>(connectionTypeRaw, ignoreCase: true, out var connectionType))
        {
            connectionType = S7ConnectionType.Basic;
        }

        return new S7SourceConfiguration
        {
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            Enabled = instance.Enabled,
            DeviceId = ReadString(conn, S7ConnectionKeys.DeviceId) ?? instance.InstanceId,
            DeviceName = ReadString(conn, S7ConnectionKeys.DeviceName),
            DeviceClass = ReadString(conn, S7ConnectionKeys.DeviceClass) ?? "plc",

            Host = host,
            Port = ReadInt(conn, S7ConnectionKeys.Port, defaultValue: 102),
            Rack = ReadInt(conn, S7ConnectionKeys.Rack, defaultValue: 0),
            Slot = ReadInt(conn, S7ConnectionKeys.Slot, defaultValue: 1),
            ConnectionType = connectionType,
            OptimizedDbAccess = ReadBool(conn, S7ConnectionKeys.OptimizedDbAccess, defaultValue: false),
            ConnectTimeoutMs = ReadInt(conn, S7ConnectionKeys.ConnectTimeoutMs, defaultValue: 2000),
            RequestTimeoutMs = ReadInt(conn, S7ConnectionKeys.RequestTimeoutMs, defaultValue: 1000),
            KeepAlive = ReadBool(conn, S7ConnectionKeys.KeepAlive, defaultValue: true),

            MaxTransactionRetries = ReadInt(conn, S7ConnectionKeys.MaxTransactionRetries, defaultValue: 2),
            InitialBackoffMs = ReadInt(conn, S7ConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, S7ConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, S7ConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            CircuitBreakerThreshold = ReadInt(conn, S7ConnectionKeys.CircuitBreakerThreshold, defaultValue: 5),
            CircuitBreakerResetMs = ReadInt(conn, S7ConnectionKeys.CircuitBreakerResetMs, defaultValue: 10_000),

            MaxGapBytes = ReadInt(conn, S7ConnectionKeys.MaxGapBytes, defaultValue: 16),
            MaxReadBytes = ReadInt(conn, S7ConnectionKeys.MaxReadBytes, defaultValue: 200),

            // Tag definitions: read from the publishing.extras "tags" array
            // when present (mirrors the existing pattern for protocols whose
            // tag lists live outside the SinkInstanceConfig.Connection block).
            // For MVP we accept tags directly under Connection.tags as well.
            TagDefinitions = ReadTagDefinitions(conn),
        };
    }

    private static IReadOnlyList<S7TagDefinition> ReadTagDefinitions(JsonElement conn)
    {
        if (!conn.TryGetProperty(S7ConnectionKeys.Tags, out var tagsEl) || tagsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<S7TagDefinition>();
        }

        var result = new List<S7TagDefinition>(tagsEl.GetArrayLength());
        foreach (var tagEl in tagsEl.EnumerateArray())
        {
            if (tagEl.ValueKind != JsonValueKind.Object) continue;
            var name = ReadString(tagEl, S7ConnectionKeys.TagName);
            var address = ReadString(tagEl, S7ConnectionKeys.Address);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            {
                continue;
            }
            result.Add(new S7TagDefinition
            {
                Name = name,
                Address = address,
                Datatype = ReadString(tagEl, S7ConnectionKeys.Datatype),
                ScanRateMs = ReadInt(tagEl, S7ConnectionKeys.ScanRateMs, defaultValue: 1000),
                Unit = ReadString(tagEl, S7ConnectionKeys.Unit),
                Scale = ReadOptionalDouble(tagEl, S7ConnectionKeys.Scale),
                Offset = ReadOptionalDouble(tagEl, S7ConnectionKeys.Offset),
            });
        }
        return result;
    }

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

    private static double? ReadOptionalDouble(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;
}

/// <summary>
/// S7 connection type — TSAP slot used during ISO-on-TCP negotiation.
/// </summary>
public enum S7ConnectionType
{
    /// <summary>Programming device — matches TIA Portal's "PG" slot.</summary>
    PG = 1,

    /// <summary>Operator panel.</summary>
    OP = 2,

    /// <summary>Basic / generic third-party connection. Works against every CPU.</summary>
    Basic = 3,
}
