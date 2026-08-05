// ============================================================================
// File: Focas2SourceConfiguration.cs
// Purpose: Configuration record for a FOCAS2 source adapter instance.
//          Derives from SourceConfiguration to participate in the Core
//          configuration system. Includes a FromSourceInstance(…) static
//          factory that parses the opaque SourceInstanceConfig.Connection
//          JSON block into a typed config — the bridge between the JSON
//          DTO layer (Core.Configuration) and the adapter runtime.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 8
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Configuration for a single FOCAS2 CNC source connection.
/// </summary>
public sealed record Focas2SourceConfiguration : SourceConfiguration
{
    /// <summary>The protocol name constant used by config DTOs.</summary>
    public const string ProtocolNameConstant = "focas2";

    /// <summary>
    /// License module key that gates registration of this protocol's
    /// source adapters. Mirrors <c>LicenseModuleKeys.SourceFocas2</c>;
    /// see <c>docs/licensing/module-catalog.md</c>. Note that customers
    /// must ALSO hold a Fanuc DLL license — that is a Fanuc commercial
    /// obligation distinct from this license-module gate.
    /// </summary>
    public const string LicenseModuleKey = "source-focas2";

    // ---- Connection ----

    /// <summary>CNC controller IP address (e.g., "192.168.1.101").</summary>
    public required string IpAddress { get; init; }

    /// <summary>FOCAS2 port (typically 8193).</summary>
    public ushort Port { get; init; } = 8193;

    /// <summary>Connection timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Per-handle data-read time-out in seconds, applied via
    /// <c>cnc_setdtimeout</c> after the handle is allocated. Bounds how long an
    /// individual fwlib read may wait, so a black-holed/non-progressing
    /// connection surfaces an error and reconnects instead of wedging the
    /// adapter's worker thread indefinitely (operational mitigation for the
    /// 2026-06-24 silent-stall incident). Default 30 s is a conservative coarse
    /// bound well above healthy read durations; <c>0</c> disables the call and
    /// leaves the library default in place. This is NOT the per-generation
    /// deadline of slice-0 commit 3.1 — that value comes from the QA field
    /// measurement and governs supervisor admission, not the library wait.
    /// </summary>
    public int DataTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When true, keeps the handle open across polls (~5ms per poll).
    /// When false, opens and closes per poll (~20-50ms per poll).
    /// </summary>
    public bool KeepAlive { get; init; } = true;

    // ---- Data selection ----

    /// <summary>
    /// Hierarchical data point paths controlling which data to collect.
    /// Prefix matching supported: "Axes/" collects all axis data.
    /// Examples: "Status/RunState", "Axes/", "Spindle/Speed", "Alarms/Active",
    /// "Program/MainProgram", "Production/CycleTime", "Tool/", "MtLinki/".
    /// Empty list collects all available data.
    /// </summary>
    public IReadOnlyList<string> DataPoints { get; init; } = [];

    // ---- Backoff ----

    /// <summary>Initial delay in ms after first connection failure.</summary>
    public int InitialBackoffMs { get; init; } = 5000;

    /// <summary>Maximum backoff delay in ms.</summary>
    public int MaxBackoffMs { get; init; } = 120_000;

    /// <summary>Multiplier applied to backoff on each consecutive failure.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Maximum number of handle allocation retries per connect attempt.</summary>
    public int MaxConnectRetries { get; init; } = 5;

    /// <summary>
    /// Translate a loaded <see cref="SourceInstanceConfig"/> DTO into a
    /// typed <see cref="Focas2SourceConfiguration"/>. Reads the
    /// protocol-specific fields out of <see cref="SourceInstanceConfig.Connection"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="instance"/>'s protocol is not
    /// <c>"focas2"</c>, or when the <see cref="SourceInstanceConfig.Connection"/>
    /// block is missing a required field such as <c>ipAddress</c>.
    /// </exception>
    public static Focas2SourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                "required FOCAS2 Connection object (must contain at least 'ipAddress').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        // Keys named via Focas2ConnectionKeys so they cannot be parsed without a
        // redaction tier (ADR-0020 M-B Q-B2); the drift guard asserts
        // Focas2ConnectionKeys.All == KnownKeys.
        var ipAddress = ReadString(conn, Focas2ConnectionKeys.IpAddress)
            ?? throw new ArgumentException(
                $"FOCAS2 Connection for source '{instance.InstanceId}' is missing " +
                "the required 'ipAddress' field.", nameof(instance));

        return new Focas2SourceConfiguration
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

            // Focas2-specific fields
            IpAddress = ipAddress,
            Port = ReadUShort(conn, Focas2ConnectionKeys.Port, defaultValue: 8193),
            TimeoutSeconds = ReadInt(conn, Focas2ConnectionKeys.TimeoutSeconds, defaultValue: 10),
            DataTimeoutSeconds = ReadInt(conn, Focas2ConnectionKeys.DataTimeoutSeconds, defaultValue: 30),
            KeepAlive = ReadBool(conn, Focas2ConnectionKeys.KeepAlive, defaultValue: true),
            DataPoints = ReadStringArray(conn, Focas2ConnectionKeys.DataPoints),
            InitialBackoffMs = ReadInt(conn, Focas2ConnectionKeys.InitialBackoffMs, defaultValue: 5000),
            MaxBackoffMs = ReadInt(conn, Focas2ConnectionKeys.MaxBackoffMs, defaultValue: 120_000),
            BackoffMultiplier = ReadDouble(conn, Focas2ConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            MaxConnectRetries = ReadInt(conn, Focas2ConnectionKeys.MaxConnectRetries, defaultValue: 5),
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

    private static ushort ReadUShort(JsonElement obj, string name, ushort defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (ushort)v.GetInt32()
            : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : defaultValue;

    private static double ReadDouble(JsonElement obj, string name, double defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : defaultValue;

    private static List<string> ReadStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var list = new List<string>(v.GetArrayLength());
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    list.Add(s);
                }
            }
        }
        return list;
    }
}
