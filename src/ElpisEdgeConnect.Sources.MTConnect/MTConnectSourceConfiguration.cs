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
    /// Trailing slash is tolerated. HTTPS is supported. A bare host, IP, or
    /// <c>host:port</c> is accepted too — see
    /// <see cref="TryNormalizeAgentBaseUrl"/>.
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

            // MTConnect-specific fields.
            // Normalised here so every downstream consumer sees the canonical
            // absolute form. An input that cannot be normalised is passed
            // through unchanged rather than throwing, so the adapter's
            // ValidateConfigurationAsync reports it as a config error the
            // operator can act on instead of a parse exception during load.
            AgentBaseUrl = TryNormalizeAgentBaseUrl(agentBaseUrl) ?? agentBaseUrl,
            AgentDeviceName = ReadString(conn, MTConnectConnectionKeys.AgentDeviceName),
            TimeoutSeconds = ReadInt(conn, MTConnectConnectionKeys.TimeoutSeconds, defaultValue: 10),
            InitialBackoffMs = ReadInt(conn, MTConnectConnectionKeys.InitialBackoffMs, defaultValue: 2000),
            MaxBackoffMs = ReadInt(conn, MTConnectConnectionKeys.MaxBackoffMs, defaultValue: 60_000),
            BackoffMultiplier = ReadDouble(conn, MTConnectConnectionKeys.BackoffMultiplier, defaultValue: 2.0),
            DegradeAfterConsecutiveFailures = ReadInt(conn, MTConnectConnectionKeys.DegradeAfterConsecutiveFailures, defaultValue: 3),
        };
    }

    // ── AgentBaseUrl normalization (bare host/IP → absolute URL) ──────────

    /// <summary>
    /// Scheme prepended to an Agent URL that arrives without one. MTConnect
    /// agents serve plain HTTP by default, so an operator typing the address
    /// of a machine on the plant network means <c>http://</c>; requiring them
    /// to type it only adds a failure mode.
    /// </summary>
    public const string ImpliedSchemePrefix = "http://";

    /// <summary>
    /// Normalize an operator- or config-supplied Agent URL into the canonical
    /// absolute form used for <c>/probe</c> and <c>/current</c> requests:
    /// <c>{scheme}://{lowercased host}[:port][/path]</c>, trailing slash
    /// stripped (both HTTP callers re-append it).
    /// <para>
    /// Rules: blank input is rejected; an input that already carries an
    /// <c>http://</c> or <c>https://</c> scheme keeps it; a bare host, IP, or
    /// <c>host:port</c> gets <see cref="ImpliedSchemePrefix"/> prepended. Only
    /// <c>http</c> and <c>https</c> are accepted, so an unusable address is
    /// rejected here with an operator-facing sentence rather than reaching
    /// <see cref="System.Net.Http.HttpClient"/> and throwing
    /// <see cref="NotSupportedException"/> mid-probe.
    /// </para>
    /// </summary>
    /// <param name="raw">The raw value as typed or as stored in config.</param>
    /// <returns>
    /// The canonical absolute URL, or <see langword="null"/> when the input is
    /// blank or still unparseable after the implied scheme is applied. Callers
    /// pair a <see langword="null"/> with
    /// <see cref="InvalidAgentBaseUrlMessage"/> for the operator-facing text.
    /// </returns>
    public static string? TryNormalizeAgentBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();

        // Scheme detection is textual, not Uri-based, on purpose:
        // Uri.TryCreate("agent.local:5000", Absolute) SUCCEEDS with scheme
        // "agent.local" and an empty Host, which is exactly how a schemeless
        // host:port used to slip past validation and fail later inside
        // HttpClient. Only a literal "<scheme>://" counts as already schemed.
        string candidate;
        if (HasExplicitScheme(trimmed))
        {
            candidate = trimmed;
        }
        else
        {
            // A leading "//" (protocol-relative paste) would otherwise produce
            // "http:////host"; drop it so the operator still gets what they meant.
            //
            // Only "//" — NOT a single leading slash. TrimStart('/') also ate the
            // one in "/relative/path", turning a path fragment into host
            // "relative", so a plainly malformed CSV cell was accepted and only
            // failed later against a machine that does not exist. Leaving the
            // single slash in place yields "http:///relative/path", whose empty
            // authority is rejected by the Host check below — which is the
            // correct answer for something that is not an address at all.
            var hostPart = trimmed.StartsWith("//", StringComparison.Ordinal)
                ? trimmed.TrimStart('/')
                : trimmed;
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

        // An http(s) URI with no authority (e.g. "http:///probe") can still
        // parse; it is never an agent we can reach.
        if (string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        // Lowercase scheme + host, preserve path casing (an agent scoped to a
        // device path is case-sensitive), strip the trailing slash so
        // "…:5000/" and "…:5000" produce one canonical string.
        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var portSegment = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        return $"{scheme}://{host}{portSegment}{path}";
    }

    /// <summary>
    /// Operator-facing explanation for an Agent URL that
    /// <see cref="TryNormalizeAgentBaseUrl"/> could not use. Shared by the
    /// Studio browse probe and the adapter so the operator reads the same
    /// sentence wherever the value is rejected.
    /// </summary>
    public static string InvalidAgentBaseUrlMessage(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? "Agent address is required. Enter the MTConnect agent's IP address or " +
              "host name — for example 192.168.1.10:5000."
            : $"'{raw.Trim()}' is not an address this gateway can reach. Enter the MTConnect " +
              "agent's IP address or host name — for example 192.168.1.10:5000, agent.local:5000, " +
              "or http://agent.local:5000/VMC-3Axis. http:// is added for you, so type a scheme " +
              "only if the agent serves over https://.";

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
