// ============================================================================
// File: Session/SparkplugMqttConnectRequest.cs
// Purpose: The complete, immutable, per-attempt MQTT connect the actor hands to the
//          transport (slice-2 review B2 / v3.1). It carries everything a single
//          session-establishment attempt needs — normalized endpoint, client id,
//          credentials, keep-alive, clean-session — AND the pre-encoded NDEATH Will
//          bytes built from the attempt's reserved bdSeq, so the Will and the paired
//          NBIRTH always share the same bdSeq. A sealed CLASS (not a record) with a
//          REDACTED ToString and a DEFENSIVELY-COPIED, immutable Will — credentials
//          and Will bytes never leak through logging/exceptions (slice-4 review B4).
//          The actor owns the connection-generation token separately.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.2, §4, §9.
// ============================================================================

using System;
using System.Collections.Immutable;
using System.Globalization;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>An immutable, complete single MQTT connect attempt (incl. the encoded NDEATH Will). Secret-safe.</summary>
internal sealed class SparkplugMqttConnectRequest
{
    private readonly ImmutableArray<byte> _willPayload;

    private SparkplugMqttConnectRequest(
        BrokerEndpoint endpoint, string clientId, string? username, string? password,
        int keepAliveSeconds, bool cleanSession, string willTopic, ImmutableArray<byte> willPayload)
    {
        Endpoint = endpoint;
        ClientId = clientId;
        Username = username;
        Password = password;
        KeepAliveSeconds = keepAliveSeconds;
        CleanSession = cleanSession;
        WillTopic = willTopic;
        _willPayload = willPayload;
    }

    /// <summary>The normalized broker endpoint.</summary>
    public BrokerEndpoint Endpoint { get; }

    /// <summary>The MQTT client id.</summary>
    public string ClientId { get; }

    /// <summary>The username, if any (paired with <see cref="Password"/>).</summary>
    public string? Username { get; }

    /// <summary>The password, if any.</summary>
    public string? Password { get; }

    /// <summary>The keep-alive interval in seconds.</summary>
    public int KeepAliveSeconds { get; }

    /// <summary>Whether to request a clean session (always true for Sparkplug v1).</summary>
    public bool CleanSession { get; }

    /// <summary>The NDEATH Will topic (published by the broker on ungraceful disconnect).</summary>
    public string WillTopic { get; }

    /// <summary>The encoded NDEATH Will payload (immutable; carries this attempt's bdSeq).</summary>
    public ReadOnlyMemory<byte> WillPayload => _willPayload.AsMemory();

    /// <summary>Create a validated connect request. The Will payload is defensively copied.</summary>
    /// <param name="endpoint">The normalized broker endpoint.</param>
    /// <param name="clientId">The MQTT client id (non-empty).</param>
    /// <param name="username">The username, if any.</param>
    /// <param name="password">The password, if any.</param>
    /// <param name="keepAliveSeconds">The keep-alive interval in seconds (1..65535).</param>
    /// <param name="cleanSession">Whether to request a clean session.</param>
    /// <param name="willTopic">The NDEATH Will topic (non-empty).</param>
    /// <param name="willPayload">The encoded NDEATH Will payload (copied).</param>
    /// <returns>The connect request.</returns>
    /// <exception cref="ArgumentException">Thrown on an empty client id / Will topic or a bad keep-alive.</exception>
    /// <exception cref="ArgumentNullException">Thrown on a null endpoint.</exception>
    public static SparkplugMqttConnectRequest Create(
        BrokerEndpoint endpoint, string clientId, string? username, string? password,
        int keepAliveSeconds, bool cleanSession, string willTopic, ReadOnlyMemory<byte> willPayload)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        ArgumentException.ThrowIfNullOrEmpty(willTopic);
        if (keepAliveSeconds is < 1 or > 65535)
        {
            throw new ArgumentException("Keep-alive must be 1..65535 seconds (MQTT 3.1.1).", nameof(keepAliveSeconds));
        }

        return new SparkplugMqttConnectRequest(
            endpoint, clientId, username, password, keepAliveSeconds, cleanSession, willTopic,
            ImmutableArray.Create(willPayload.Span)); // defensive copy — no caller-owned buffer escapes
    }

    /// <summary>Redacted string form — never emits credentials or Will bytes.</summary>
    /// <returns>A secret-safe representation.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"SparkplugMqttConnectRequest {{ Endpoint = {Endpoint}, ClientId = {ClientId}, " +
            $"HasCredentials = {!string.IsNullOrEmpty(Username)}, KeepAliveSeconds = {KeepAliveSeconds}, " +
            $"CleanSession = {CleanSession}, WillTopic = {WillTopic}, WillPayloadBytes = {_willPayload.Length} }}");
}
