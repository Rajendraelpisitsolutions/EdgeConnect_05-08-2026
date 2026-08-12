// ============================================================================
// File: MelsecSourceConfiguration.cs
// Purpose: Configuration record for the MELSEC source adapter. Mirrors
//          S7SourceConfiguration's shape:
//             - connection (Host, Port, transport, frame mode, profile)
//             - 3E route header (network/PC/module/station, monitoring timer)
//             - retry / backoff / circuit-breaker thresholds
//             - scan-planner knobs (MaxGapWords, MaxPointsPerRequest)
//             - typed tag definitions
//             - FromSourceInstance factory for gateway.json projection
//
//          Slice 1 (ADR-0033) supports only Tcp + Mc3EBinary + Modern profile,
//          read-only. Other modes are accepted here but rejected by the
//          adapter's config validation, so gateway.json stays forward-compatible.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
//            docs/licensing/module-catalog.md (source-melsec)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>
/// Configuration record for the Mitsubishi MELSEC source adapter (SLMP / MC 3E
/// binary over TCP).
/// </summary>
public sealed record MelsecSourceConfiguration : SourceConfiguration
{
    /// <summary>Protocol-name constant carried on <c>SourceInstanceConfig.ProtocolName</c>.</summary>
    public const string ProtocolNameConstant = "melsec";

    /// <summary>
    /// License module key that gates registration. Mirrors
    /// <c>LicenseModuleKeys.SourceMelsec</c> (<c>docs/licensing/module-catalog.md</c>).
    /// </summary>
    public const string LicenseModuleKey = "source-melsec";

    /// <summary>Word-batch-read point cap for the modern (iQ-R/iQ-L/Q/L) 3E profile.</summary>
    public const int ModernWordReadCap = 960;

    // ----- Connection -----

    /// <summary>PLC / Ethernet-module IP address or hostname.</summary>
    public required string Host { get; init; }

    /// <summary>
    /// MC / SLMP TCP port. There is no universal Mitsubishi default — the
    /// module's open setting defines it (commonly 5000/5001/5006/6000); the
    /// adapter requires an explicit positive value at validation time.
    /// </summary>
    public int Port { get; init; }

    /// <summary>Transport. Slice 1 supports <see cref="MelsecTransportProtocol.Tcp"/> only.</summary>
    public MelsecTransportProtocol TransportProtocol { get; init; } = MelsecTransportProtocol.Tcp;

    /// <summary>MC frame mode. Slice 1 supports <see cref="MelsecFrameMode.Mc3EBinary"/> only.</summary>
    public MelsecFrameMode FrameMode { get; init; } = MelsecFrameMode.Mc3EBinary;

    /// <summary>CPU family profile. Slice 1 supports <see cref="MelsecDeviceProfile.Modern"/> only.</summary>
    public MelsecDeviceProfile DeviceProfile { get; init; } = MelsecDeviceProfile.Modern;

    // ----- 3E route header -----

    /// <summary>Network number (3E header). Default 0x00 (local).</summary>
    public byte NetworkNo { get; init; }

    /// <summary>PC / station number (3E header). Default 0xFF (local CPU).</summary>
    public byte PcNo { get; init; } = 0xFF;

    /// <summary>Request destination module I/O number (3E header). Default 0x03FF.</summary>
    public ushort RequestDestModuleIoNo { get; init; } = 0x03FF;

    /// <summary>Request destination module station number (3E header). Default 0x00.</summary>
    public byte RequestDestModuleStationNo { get; init; }

    /// <summary>
    /// Device-side monitoring timer in milliseconds. Encoded on the wire in
    /// 250 ms units (0 = wait indefinitely); the adapter ceils a non-multiple to
    /// the next 250 ms (never shortens — ADR-0033 Rule 6) and logs when it does.
    /// </summary>
    public int MonitoringTimerMs { get; init; } = 4000;

    // ----- Timeouts -----

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 3000;

    /// <summary>
    /// Per-read socket timeout in milliseconds. Must be ≥ the encoded monitoring
    /// timer, else the client would abandon a read before the CPU could answer
    /// (validated; ADR-0033 Rule 6). Default 5000 to stay coherent with the
    /// 4000 ms monitoring-timer default.
    /// </summary>
    public int RequestTimeoutMs { get; init; } = 5000;

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
    /// Maximum gap (in words) the planner will coalesce into a single contiguous
    /// batch read of one device. Wider gaps amortize more tags per round-trip at
    /// the cost of reading words the operator didn't configure. Default 8 words.
    /// </summary>
    public int MaxGapWords { get; init; } = 8;

    /// <summary>
    /// Maximum device points per single batch read. Clamped to the profile cap
    /// (<see cref="ModernWordReadCap"/> = 960 for the modern profile); the planner
    /// splits demand larger than this into multiple reads. Default 480 (conservative).
    /// </summary>
    public int MaxPointsPerRequest { get; init; } = 480;

    // ----- Tag set -----

    /// <summary>
    /// Configured tag definitions. The adapter pre-parses each
    /// <see cref="MelsecTagDefinition.Address"/> at Initialize time and rejects
    /// the config on any parse failure or unsupported device.
    /// </summary>
    public IReadOnlyList<MelsecTagDefinition> TagDefinitions { get; init; } = [];

    /// <summary>
    /// Project a <see cref="SourceInstanceConfig"/> read from gateway.json into a
    /// typed <see cref="MelsecSourceConfiguration"/>. Mirrors
    /// <c>S7SourceConfiguration.FromSourceInstance</c>.
    /// </summary>
    public static MelsecSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the required MELSEC Connection object.",
                nameof(instance));
        }
        var conn = instance.Connection.Value;

        var host = ReadString(conn, MelsecConnectionKeys.Host)
            ?? throw new ArgumentException(
                $"MELSEC Connection for source '{instance.InstanceId}' is missing the required 'host' field.",
                nameof(instance));

        return new MelsecSourceConfiguration
        {
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            Enabled = instance.Enabled,
            DeviceId = ReadString(conn, MelsecConnectionKeys.DeviceId) ?? instance.InstanceId,
            DeviceName = ReadString(conn, MelsecConnectionKeys.DeviceName),
            DeviceClass = ReadString(conn, MelsecConnectionKeys.DeviceClass) ?? "plc",

            Host = host,
            Port = ReadInt(conn, MelsecConnectionKeys.Port, defaultValue: 0),
            TransportProtocol = ReadEnum(conn, MelsecConnectionKeys.TransportProtocol, MelsecTransportProtocol.Tcp),
            FrameMode = ReadEnum(conn, MelsecConnectionKeys.FrameMode, MelsecFrameMode.Mc3EBinary),
            DeviceProfile = ReadEnum(conn, MelsecConnectionKeys.DeviceProfile, MelsecDeviceProfile.Modern),

            NetworkNo = ReadByte(conn, MelsecConnectionKeys.NetworkNo, defaultValue: 0x00),
            PcNo = ReadByte(conn, MelsecConnectionKeys.PcNo, defaultValue: 0xFF),
            RequestDestModuleIoNo = ReadUShort(conn, MelsecConnectionKeys.RequestDestModuleIoNo, defaultValue: 0x03FF),
            RequestDestModuleStationNo = ReadByte(conn, MelsecConnectionKeys.RequestDestModuleStationNo, defaultValue: 0x00),
            MonitoringTimerMs = ReadInt(conn, MelsecConnectionKeys.MonitoringTimerMs, defaultValue: 4000),

            ConnectTimeoutMs = ReadInt(conn, MelsecConnectionKeys.ConnectTimeoutMs, defaultValue: 3000),
            RequestTimeoutMs = ReadInt(conn, MelsecConnectionKeys.RequestTimeoutMs, defaultValue: 5000),
            KeepAlive = ReadBool(conn, MelsecConnectionKeys.KeepAlive, defaultValue: true),

            MaxTransactionRetries = ReadInt(conn, MelsecConnectionKeys.MaxTransactionRetries, defaultValue: 2),
            InitialBackoffMs = ReadInt(conn, MelsecConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, MelsecConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, MelsecConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            CircuitBreakerThreshold = ReadInt(conn, MelsecConnectionKeys.CircuitBreakerThreshold, defaultValue: 5),
            CircuitBreakerResetMs = ReadInt(conn, MelsecConnectionKeys.CircuitBreakerResetMs, defaultValue: 10_000),

            MaxGapWords = ReadInt(conn, MelsecConnectionKeys.MaxGapWords, defaultValue: 8),
            MaxPointsPerRequest = ReadInt(conn, MelsecConnectionKeys.MaxPointsPerRequest, defaultValue: 480),

            TagDefinitions = ReadTagDefinitions(conn),
        };
    }

    private static IReadOnlyList<MelsecTagDefinition> ReadTagDefinitions(JsonElement conn)
    {
        if (!conn.TryGetProperty(MelsecConnectionKeys.Tags, out var tagsEl) || tagsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MelsecTagDefinition>();
        }

        var result = new List<MelsecTagDefinition>(tagsEl.GetArrayLength());
        foreach (var tagEl in tagsEl.EnumerateArray())
        {
            if (tagEl.ValueKind != JsonValueKind.Object) continue;
            var name = ReadString(tagEl, MelsecConnectionKeys.TagName);
            var address = ReadString(tagEl, MelsecConnectionKeys.Address);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
            {
                continue;
            }
            result.Add(new MelsecTagDefinition
            {
                Name = name,
                Address = address,
                Datatype = ReadString(tagEl, MelsecConnectionKeys.Datatype),
                WordOrder = ReadEnum(tagEl, MelsecConnectionKeys.WordOrder, MelsecWordOrder.LowWordFirst),
                ScanRateMs = ReadInt(tagEl, MelsecConnectionKeys.ScanRateMs, defaultValue: 1000),
                Unit = ReadString(tagEl, MelsecConnectionKeys.Unit),
                Scale = ReadOptionalDouble(tagEl, MelsecConnectionKeys.Scale),
                Offset = ReadOptionalDouble(tagEl, MelsecConnectionKeys.Offset),
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

    private static byte ReadByte(JsonElement obj, string name, byte defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i is >= 0 and <= 0xFF
            ? (byte)i
            : defaultValue;

    private static ushort ReadUShort(JsonElement obj, string name, ushort defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i is >= 0 and <= 0xFFFF
            ? (ushort)i
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

    private static TEnum ReadEnum<TEnum>(JsonElement obj, string name, TEnum defaultValue)
        where TEnum : struct, Enum =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && Enum.TryParse<TEnum>(v.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
}
