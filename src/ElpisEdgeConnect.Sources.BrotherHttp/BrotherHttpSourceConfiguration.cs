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
//            * TryNormalizeBaseUrl + InvalidBaseUrlMessage — the single
//              shared home for "a bare IP/host means http://<that>", used by
//              this factory (hand-written gateway.json / bulk import),
//              BrotherHttpHttpApi, the Studio wizard model, and the Studio
//              Test-Connection probe service
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
    /// A bare host, IP, or <c>host:port</c> is accepted too — see
    /// <see cref="TryNormalizeBaseUrl"/>, which supplies the implied
    /// <c>http://</c>. Trailing slash optional.
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
        var rawBaseUrl = ReadString(conn, BrotherHttpConnectionKeys.BaseUrl)
            ?? throw new ArgumentException(
                $"Brother HTTP Connection for source '{instance.InstanceId}' is missing " +
                "the required 'baseUrl' field.", nameof(instance));

        // Normalise here rather than at any single caller: this factory is the
        // one funnel every entry path goes through (Studio wizard, hand-edited
        // gateway.json, bulk CSV import), so a bare "192.168.5.25" becomes a
        // usable URL no matter who wrote it. An input we cannot normalise is
        // passed through verbatim so the downstream rejection (adapter
        // validation / BrotherHttpHttpApi ctor) can quote what the operator
        // actually typed.
        var baseUrl = TryNormalizeBaseUrl(rawBaseUrl) ?? rawBaseUrl.Trim();

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

    // ── BaseUrl normalization (bare host/IP → absolute URL) ───────────────

    /// <summary>
    /// Scheme prepended to a base URL that arrives without one. Brother's
    /// web-monitoring interface is plain HTTP on port 80 — there is exactly
    /// one scheme an operator could sensibly mean, so requiring them to type
    /// it only adds a failure mode.
    /// </summary>
    public const string ImpliedSchemePrefix = "http://";

    /// <summary>
    /// Normalize an operator- or config-supplied Brother base URL into the
    /// canonical absolute form used for HTTP requests and probe lease keys:
    /// <c>{scheme}://{lowercased host}[:port][/path]</c>, trailing slash
    /// stripped.
    /// <para>
    /// Rules: blank input is rejected; an input that already carries a
    /// <c>http://</c> or <c>https://</c> scheme keeps it; a bare host, IP, or
    /// <c>host:port</c> gets <see cref="ImpliedSchemePrefix"/> prepended.
    /// Only <c>http</c> and <c>https</c> are accepted — Brother speaks
    /// neither FTP nor anything else, and rejecting here yields an operator
    /// error instead of a transport-layer exception mid-probe.
    /// </para>
    /// </summary>
    /// <param name="raw">The raw value as typed or as stored in config.</param>
    /// <returns>
    /// The canonical absolute URL, or <see langword="null"/> when the input is
    /// blank or still unparseable after the implied scheme is applied. Callers
    /// pair a <see langword="null"/> with
    /// <see cref="InvalidBaseUrlMessage"/> for the operator-facing text.
    /// </returns>
    public static string? TryNormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();

        // Scheme detection is textual, not Uri-based, on purpose:
        // Uri.TryCreate("cnc-01:8080", Absolute) succeeds with scheme
        // "cnc-01", and "//192.168.5.25" parses as a file:// UNC path on
        // Windows. Both are host-shaped inputs an operator can plausibly
        // type, so only a literal "<scheme>://" counts as "already schemed".
        string candidate;
        if (HasExplicitScheme(trimmed))
        {
            candidate = trimmed;
        }
        else
        {
            // A leading "//" (protocol-relative paste) would otherwise produce
            // "http:////host"; drop it so the operator still gets what they meant.
            var hostPart = trimmed.TrimStart('/');
            if (hostPart.Length == 0)
            {
                return null;
            }
            candidate = ImpliedSchemePrefix + hostPart;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // An http(s) URI with no authority (e.g. "http:///path") can still
        // parse; it is never a machine we can reach.
        if (string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        // Lowercase scheme + host, preserve path casing (Brother endpoint
        // paths are case-sensitive on most firmware), strip trailing slash so
        // "…/110/" and "…/110" produce one canonical string.
        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var portSegment = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{scheme}://{host}{portSegment}{path}";
    }

    /// <summary>
    /// Operator-facing explanation for a base URL that
    /// <see cref="TryNormalizeBaseUrl"/> could not use. Shared by the Studio
    /// probe and the adapter so the operator reads the same sentence wherever
    /// the value is rejected.
    /// </summary>
    public static string InvalidBaseUrlMessage(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? "Machine address is required. Enter the Brother CNC's IP address or " +
              "host name — for example 192.168.2.110."
            : $"'{raw.Trim()}' is not an address this gateway can reach. Enter the Brother " +
              "CNC's IP address or host name — for example 192.168.2.110, cnc-line-a, or " +
              "192.168.2.110:8080. http:// is added for you, so type a scheme only if the " +
              "CNC serves its web-monitoring page over https://.";

    /// <summary>
    /// True when <paramref name="value"/> starts with a syntactically valid
    /// URI scheme followed by <c>"://"</c>.
    /// </summary>
    private static bool HasExplicitScheme(string value)
    {
        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        for (var i = 0; i < separator; i++)
        {
            var c = value[i];
            var valid = i == 0
                ? char.IsAsciiLetter(c)
                : char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.';
            if (!valid)
            {
                return false;
            }
        }

        return true;
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
