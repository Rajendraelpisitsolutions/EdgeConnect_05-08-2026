// ============================================================================
// File: Configuration/BrokerEndpoint.cs
// Purpose: Canonical, normalized broker endpoint value type. A single
//          representation of "which broker" shared by later Edge-Node identity
//          state and cardinality/identity validation, so equivalent spellings
//          (case, explicit-vs-default port, IPv6 forms) compare equal and cannot
//          evade duplicate detection. A SEALED IMMUTABLE REFERENCE TYPE with a
//          private constructor: there is no `default` instance and no way to build
//          a non-canonical value — Create is the only path.
// Reference: docs/decisions/0036-*.md Rule 7; K0 WS3+WS8 / WS5 exits;
//            PR #180 review rounds 1-2 (item 3: no default-struct identity hole).
//
// Protocol-neutral: a broker endpoint (host + port + TLS) is a transport concept.
// ============================================================================

using System;
using System.Net;
using System.Net.Sockets;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// A normalized broker endpoint. Two endpoints are equal when their normalized host,
/// effective port, and TLS mode match — regardless of host case, whether a default
/// port was spelled out, or how an IPv6 address was written. It is a sealed reference
/// type with a private constructor, so it can only be built through <see cref="Create"/>
/// (which validates and canonicalizes) — there is no <c>default</c> instance and no
/// way to construct a non-canonical value.
/// </summary>
public sealed class BrokerEndpoint : IEquatable<BrokerEndpoint>
{
    /// <summary>Default plaintext MQTT port.</summary>
    public const int DefaultPlainPort = 1883;

    /// <summary>Default TLS MQTT port.</summary>
    public const int DefaultTlsPort = 8883;

    private BrokerEndpoint(string host, int port, bool tls)
    {
        Host = host;
        Port = port;
        Tls = tls;
    }

    /// <summary>Normalized host: lower-cased DNS name (trailing dot stripped) or canonical IP literal (unbracketed). Never null.</summary>
    public string Host { get; }

    /// <summary>Effective port (1..65535).</summary>
    public int Port { get; }

    /// <summary>Whether the endpoint uses TLS.</summary>
    public bool Tls { get; }

    /// <summary>True when <see cref="Host"/> is an IPv6 literal (requires bracketing when rendered with a port).</summary>
    public bool IsIPv6 => Host.Contains(':', StringComparison.Ordinal);

    /// <summary>
    /// Build a normalized endpoint. The host is validated and canonicalized (DNS
    /// names lower-cased with any trailing dot stripped; IP literals parsed to their
    /// canonical form). A scheme, path, or embedded port in <paramref name="host"/>
    /// is rejected — pass the port via <paramref name="port"/>. When
    /// <paramref name="port"/> is null the TLS-appropriate default is used.
    /// </summary>
    /// <param name="host">Broker host (DNS name or IP literal; may be bracketed IPv6).</param>
    /// <param name="port">Explicit port (1..65535), or null for the default.</param>
    /// <param name="tls">Whether the endpoint uses TLS.</param>
    /// <returns>The normalized endpoint.</returns>
    /// <exception cref="ArgumentException">Thrown when the host is null, empty, or malformed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1..65535.</exception>
    public static BrokerEndpoint Create(string host, int? port = null, bool tls = false)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Broker host must be non-empty.", nameof(host));
        }

        var raw = host.Trim();
        if (raw.Contains("://", StringComparison.Ordinal) || raw.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("Host must not contain a scheme or path.", nameof(host));
        }

        var normalizedHost = NormalizeHost(raw, nameof(host));

        var effectivePort = port ?? (tls ? DefaultTlsPort : DefaultPlainPort);
        if (effectivePort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), effectivePort, "Port must be in the range 1..65535.");
        }

        return new BrokerEndpoint(normalizedHost, effectivePort, tls);
    }

    private static string NormalizeHost(string raw, string paramName)
    {
        // Bracketed IPv6, e.g. "[::1]".
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var inner = raw[1..^1];
            if (!IPAddress.TryParse(inner, out var ip6) || ip6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new ArgumentException("Invalid bracketed IPv6 host.", paramName);
            }

            return ip6.ToString(); // canonical, lower-case, no brackets
        }

        // Bare IP literal (v4 or v6) — a bare IPv6 has colons but parses as an address.
        if (IPAddress.TryParse(raw, out var ip))
        {
            return ip.ToString();
        }

        // DNS host: no embedded port allowed; lower-case; strip a single trailing dot.
        if (raw.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Host must not contain an embedded port; pass the port separately.", paramName);
        }

        var dns = raw.TrimEnd('.').ToLowerInvariant();
        if (dns.Length == 0)
        {
            throw new ArgumentException("Broker host must be non-empty.", paramName);
        }

        return dns;
    }

    /// <summary>The canonical <c>scheme://host:port</c> string (IPv6 hosts are bracketed).</summary>
    public override string ToString() =>
        $"{(Tls ? "mqtts" : "mqtt")}://{(IsIPv6 ? $"[{Host}]" : Host)}:{Port}";

    /// <inheritdoc/>
    public bool Equals(BrokerEndpoint? other) =>
        other is not null &&
        Port == other.Port && Tls == other.Tls && string.Equals(Host, other.Host, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as BrokerEndpoint);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Host, Port, Tls);

    /// <summary>Value equality (null-safe).</summary>
    public static bool operator ==(BrokerEndpoint? left, BrokerEndpoint? right) =>
        ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <summary>Value inequality (null-safe).</summary>
    public static bool operator !=(BrokerEndpoint? left, BrokerEndpoint? right) => !(left == right);
}
