// ============================================================================
// File: BrotherHttpSourceConfiguration.cs
// Purpose: Typed Brother HTTP source configuration record. Bridges the
//          opaque SourceInstanceConfig.Connection JSON into a strongly-typed
//          shape consumable by BrotherHttpHttpApi (step 3) and the adapter
//          (step 7). Includes:
//            * Protocol identifier + license module key constants (promoted
//              from step-2 AssemblyMarker per v3.1 §D)
//            * FromSourceInstance(SourceInstanceConfig) factory mirroring
//              Focas2SourceConfiguration's pattern
//            * NormalizeDataPoints + IsCatalogMember helpers for the
//              DataPoints validator per v3.1 §B.6
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §10 step 6,
//            v3.1 §B.6 (DataPoints normalization), Q10 (polling clamps)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Typed configuration for a single Brother HTTP source instance.
/// </summary>
public sealed record BrotherHttpSourceConfiguration : SourceConfiguration
{
    /// <summary>Protocol identifier used by config DTOs.</summary>
    public const string ProtocolNameConstant = "brother-http";

    /// <summary>
    /// License module key gating registration of this protocol's source
    /// adapters. Mirrors <see cref="ElpisEdgeConnect.Core.Licensing.LicenseModuleKeys.SourceFocas2"/>'s
    /// pattern. See <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "source-brother-http";

    /// <summary>
    /// Q10 polling-cadence rules (locked at v2 + v3):
    ///   * Default 3000 ms (matches the 100-CNC customer working assumption).
    ///   * Hard minimum 500 ms — validation rejects via <c>BROTHER.POLL_TOO_FAST</c>.
    ///   * Soft warning below 1000 ms — validation flags but accepts.
    /// </summary>
    public const int PollIntervalDefaultMs = 3000;

    /// <summary>Hard floor for polling-interval validation (Q10).</summary>
    public const int PollIntervalHardMinimumMs = 500;

    /// <summary>Soft warning threshold for polling-interval validation (Q10).</summary>
    public const int PollIntervalSoftWarningMs = 1000;

    /// <summary>
    /// Brother CNC HTTP base URL, e.g. <c>"http://192.168.2.110"</c>.
    /// Trailing slash optional — the API client normalises both forms.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>HTTP request timeout in seconds (default 10, per legacy).</summary>
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>
    /// Consecutive <c>HTTPD_MCNINFO</c> failures before the adapter transitions
    /// to <see cref="AdapterState.Failed"/> (Q4 lock: 3 consecutive).
    /// </summary>
    public int FaultThresholdConsecutiveFailures { get; init; } = 3;

    /// <summary>Initial delay in ms after first endpoint failure.</summary>
    public int InitialBackoffMs { get; init; } = 5000;

    /// <summary>Maximum backoff delay in ms.</summary>
    public int MaxBackoffMs { get; init; } = 120_000;

    /// <summary>Multiplier applied to backoff on each consecutive failure.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Hierarchical data-point paths controlling which catalog entries to
    /// emit. Empty list = collect everything in the catalog. Prefix matching
    /// supported (e.g. <c>"Tools/"</c> matches every <c>"Tools/..."</c> leaf).
    /// The raw list is preserved for round-trip fidelity; callers should
    /// consume <see cref="NormalizeDataPoints"/>'s output for the canonical
    /// post-validation form per v3.1 §B.6.
    /// </summary>
    public IReadOnlyList<string> DataPoints { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Translate a loaded <see cref="SourceInstanceConfig"/> DTO into a typed
    /// <see cref="BrotherHttpSourceConfiguration"/>. Reads the
    /// <c>brother-http</c>-specific fields out of the opaque
    /// <see cref="SourceInstanceConfig.Connection"/> JSON.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the instance's protocol is not <c>"brother-http"</c>, or
    /// when the <c>Connection</c> block is missing a required field.
    /// </exception>
    public static BrotherHttpSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                "Brother HTTP Connection object (must contain at least 'baseUrl').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        // Keys named via BrotherHttpConnectionKeys so they cannot be parsed
        // without a redaction tier (ADR-0020 M-B Q-B2); the drift guard asserts
        // BrotherHttpConnectionKeys.All == KnownKeys.
        var baseUrl = ReadString(conn, BrotherHttpConnectionKeys.BaseUrl)
            ?? throw new ArgumentException(
                $"Brother HTTP Connection for source '{instance.InstanceId}' is missing " +
                "the required 'baseUrl' field.", nameof(instance));

        return new BrotherHttpSourceConfiguration
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

            // BrotherHttp-specific fields
            BaseUrl = baseUrl,
            TimeoutSeconds = ReadInt(conn, BrotherHttpConnectionKeys.TimeoutSeconds, defaultValue: 10),
            FaultThresholdConsecutiveFailures = ReadInt(conn, BrotherHttpConnectionKeys.FaultThresholdConsecutiveFailures, defaultValue: 3),
            InitialBackoffMs = ReadInt(conn, BrotherHttpConnectionKeys.InitialBackoffMs, defaultValue: 5000),
            MaxBackoffMs = ReadInt(conn, BrotherHttpConnectionKeys.MaxBackoffMs, defaultValue: 120_000),
            BackoffMultiplier = ReadDouble(conn, BrotherHttpConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            DataPoints = ReadStringArray(conn, BrotherHttpConnectionKeys.DataPoints),
        };
    }

    // ── v3.1 §B.6 DataPoints normalization ────────────────────────────────

    /// <summary>
    /// Normalize a raw <c>DataPoints</c> list per v3.1 §B.6:
    /// trim → drop empty → strip trailing <c>/</c> → OrdinalIgnoreCase dedup →
    /// prefix-wins-over-leaf collapse. Returns the canonical post-validation
    /// form. Does NOT validate catalog membership — caller separately runs
    /// <see cref="IsCatalogMember"/> on each entry and emits issues for
    /// unknown paths.
    /// </summary>
    public static IReadOnlyList<string> NormalizeDataPoints(IEnumerable<string> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        // 1-3. Trim, drop empty, strip a single trailing slash.
        var trimmed = raw
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().TrimEnd('/'))
            .Where(s => s.Length > 0)
            .ToList();

        // 4. OrdinalIgnoreCase dedup, case-preserving first occurrence.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dedup = new List<string>(trimmed.Count);
        foreach (var entry in trimmed)
        {
            if (seen.Add(entry))
            {
                dedup.Add(entry);
            }
        }

        // 5. Prefix-wins-over-leaf — for each entry P, drop P if any OTHER
        // entry Q is a strict ancestor (P starts with Q + "/").
        var result = new List<string>(dedup.Count);
        foreach (var p in dedup)
        {
            var dropped = false;
            foreach (var q in dedup)
            {
                if (ReferenceEquals(p, q)) continue;
                if (p.StartsWith(q + "/", StringComparison.OrdinalIgnoreCase))
                {
                    dropped = true;
                    break;
                }
            }
            if (!dropped)
            {
                result.Add(p);
            }
        }

        return result;
    }

    /// <summary>
    /// Catalog-membership check on a single (normalized) entry per
    /// v3.1 §B.6. Delegates to <see cref="BrotherTagMap.IsKnownPathOrPrefix"/>.
    /// </summary>
    public static bool IsCatalogMember(string normalizedEntry) =>
        BrotherTagMap.IsKnownPathOrPrefix(normalizedEntry);

    // ── Q10 polling-cadence helpers ───────────────────────────────────────

    /// <summary>
    /// Q10 polling-cadence classification for the validation surface.
    /// </summary>
    public enum PollIntervalClassification
    {
        /// <summary>Below <see cref="PollIntervalHardMinimumMs"/> — must reject.</summary>
        TooFast,

        /// <summary>≥ hard minimum, &lt; <see cref="PollIntervalSoftWarningMs"/> — warn but accept.</summary>
        Warning,

        /// <summary>≥ <see cref="PollIntervalSoftWarningMs"/> — accepted without warning.</summary>
        Acceptable,
    }

    /// <summary>Classify a polling-interval value per Q10.</summary>
    public static PollIntervalClassification ClassifyPollInterval(int pollIntervalMs)
    {
        if (pollIntervalMs < PollIntervalHardMinimumMs)
        {
            return PollIntervalClassification.TooFast;
        }
        if (pollIntervalMs < PollIntervalSoftWarningMs)
        {
            return PollIntervalClassification.Warning;
        }
        return PollIntervalClassification.Acceptable;
    }

    // ── JSON helpers (mirroring Focas2SourceConfiguration's pattern) ──────

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

    private static List<string> ReadStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
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
