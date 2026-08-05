// ============================================================================
// File: OpcUaClientSourceConfiguration.cs
// Purpose: Configuration record for a single OPC UA Client source-adapter
//          instance. Derives from SourceConfiguration. Includes the full
//          endpoint + security + auth surface per v2.1 §1.1 (operator
//          chooses; adapter supports all combinations).
//
//          Per-connection tuning defaults mirror LockedTuningKnobs from
//          the StackCeiling benchmark project — but are operator-overridable
//          at the source-instance level. The wizard surfaces these in
//          the "advanced" section per ADR-0015 §5 (sensible defaults,
//          advanced toggle).
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5, §5.1
//            docs/decisions/0015-wizard-contract.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Configuration for a single OPC UA Client source connection. Per v2.1
/// §1.1, supports the full endpoint specification surface: Anonymous /
/// UserName / X.509 auth × None / Sign / SignAndEncrypt security ×
/// per-endpoint policy.
/// </summary>
public sealed record OpcUaClientSourceConfiguration : SourceConfiguration
{
    /// <summary>The protocol name constant used by config DTOs.</summary>
    public const string ProtocolNameConstant = "opcua-client";

    /// <summary>
    /// License module key gating registration of this protocol's source
    /// adapters. Mirrors <c>LicenseModuleKeys.SourceOpcUaClient</c>;
    /// duplicated as a local <c>const</c> so the per-protocol project
    /// doesn't need a Core reference for the key string. See
    /// <c>docs/licensing/module-catalog.md</c>.
    /// </summary>
    public const string LicenseModuleKey = "source-opcua-client";

    // ---- Endpoint ---------------------------------------------------------

    /// <summary>
    /// OPC UA endpoint URL the client connects to. Must use the
    /// <c>opc.tcp://</c> scheme. Example:
    /// <c>opc.tcp://factorytalk.pilot.local:4840</c>.
    /// </summary>
    public required string EndpointUrl { get; init; }

    /// <summary>
    /// Operator-readable application name surfaced to the server during
    /// session creation. Shows up in server-side audit logs and admin
    /// UIs when the server tracks per-client identities.
    /// </summary>
    public string ApplicationName { get; init; } = "Elpis EdgeConnect";

    /// <summary>
    /// Application URI surfaced to the server (used as the
    /// <c>X509Subject</c> on the client's own certificate). MUST be
    /// stable across restarts — server admins use it as the trust
    /// anchor when authorising the client.
    /// </summary>
    public string ApplicationUri { get; init; } = "urn:elpis:edgeconnect:opcua-client";

    /// <summary>
    /// Session timeout (ms) negotiated with the server. v2.1 §2.5 locked
    /// default: 60,000 ms (reference-client default).
    /// </summary>
    public uint SessionTimeoutMs { get; init; } = 60_000;

    // ---- Security ---------------------------------------------------------

    /// <summary>
    /// Endpoint security mode. v2.1 §6 Q7 lab-baseline default:
    /// <see cref="OpcUaSecurityMode.SignAndEncrypt"/>. Operator may
    /// override per pilot site.
    /// </summary>
    public OpcUaSecurityMode SecurityMode { get; init; } = OpcUaSecurityMode.SignAndEncrypt;

    /// <summary>
    /// OPC UA security policy URI. Defaults to Basic256Sha256 per v2.1
    /// §6 Q7. Newer policies (Aes128_Sha256_RsaOaep, Aes256_Sha256_RsaPss)
    /// are forward-compatible — the adapter validates the value against
    /// the stack's supported list at start time.
    /// </summary>
    public string SecurityPolicyUri { get; init; } = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";

    /// <summary>
    /// User-identity / authentication mode. v2.1 §6 Q7 lab-baseline
    /// default: <see cref="OpcUaAuthMode.Anonymous"/>.
    /// </summary>
    public OpcUaAuthMode AuthMode { get; init; } = OpcUaAuthMode.Anonymous;

    /// <summary>
    /// Credentials block. Required when <see cref="AuthMode"/> is
    /// <see cref="OpcUaAuthMode.UserName"/> or
    /// <see cref="OpcUaAuthMode.Certificate"/>; ignored when Anonymous.
    /// </summary>
    public OpcUaClientCredentials? Credentials { get; init; }

    /// <summary>
    /// Optional path to the client's own application instance certificate
    /// store. <see langword="null"/> = use the default per-user store
    /// resolved by the OPC Foundation stack
    /// (<c>%LocalApplicationData%/EdgeConnect/OpcUaClient/own</c>).
    /// </summary>
    public string? ApplicationCertificateStorePath { get; init; }

    /// <summary>
    /// Whether to auto-accept untrusted server certificates. v2.1 §6 Q7
    /// lab baseline: <c>true</c> for benchmark runs; production
    /// deployments MUST set this to <c>false</c> and pre-populate the
    /// trusted-server-certs store.
    /// </summary>
    public bool AutoAcceptUntrustedServerCertificate { get; init; } = true;

    // ---- Subscription tuning (v2.1 §2.5 locked defaults) ------------------

    /// <summary>
    /// Publishing interval (ms). v2.1 §2.5 locked default: 50 ms.
    /// </summary>
    public int PublishingIntervalMs { get; init; } = 50;

    /// <summary>
    /// Sampling interval (ms). v2.1 §2.5 locked default: 50 ms (matches
    /// publishing interval; per-item override available on
    /// <see cref="MonitoredItemConfig.SamplingIntervalMs"/>).
    /// </summary>
    public int SamplingIntervalMs { get; init; } = 50;

    /// <summary>
    /// Keep-alive count. v2.1 §2.5 locked default: 20 (≈ 1s wall at the
    /// 50ms publishing interval).
    /// </summary>
    public uint KeepAliveCount { get; init; } = 20;

    /// <summary>
    /// Lifetime count. v2.1 §2.5 locked default: 60 (≥ 3× keepalive).
    /// </summary>
    public uint LifetimeCount { get; init; } = 60;

    /// <summary>
    /// Max notifications per publish. v2.1 §2.5 locked default: 1,000.
    /// </summary>
    public uint MaxNotificationsPerPublish { get; init; } = 1_000;

    /// <summary>
    /// Default queue size for analog (numeric, continuous) monitored
    /// items. v2.1 §2.5 locked default: 2.
    /// </summary>
    public uint DefaultAnalogQueueSize { get; init; } = 2;

    /// <summary>
    /// Default queue size for discrete / event monitored items. v2.1
    /// §2.5 locked default: 10.
    /// </summary>
    public uint DefaultDiscreteQueueSize { get; init; } = 10;

    // ---- Notification dispatcher (v2.1 §1.3 hot path) --------------------

    /// <summary>
    /// Minimum operator-allowed notification channel capacity. Below
    /// this, the dispatcher's bounded channel cannot absorb even a single
    /// stack publish-cycle of burst at 30K/sec (~600 batches/sec).
    /// Locked at PR 4 plan amendment #1 (user lock 2026-05-29).
    /// </summary>
    public const int MinimumNotificationChannelCapacity = 100;

    /// <summary>
    /// Maximum operator-allowed notification channel capacity. Above this,
    /// a backpressure problem turns into a memory problem — a bounded
    /// channel with 10K batches at ~50 monitored items per batch can buffer
    /// up to ~500K pending value-change records. Locked at PR 4 plan
    /// amendment #1 (user lock 2026-05-29).
    /// </summary>
    public const int MaximumNotificationChannelCapacity = 10_000;

    /// <summary>
    /// Default channel capacity per source. 1,000 batches absorbs ~1.7
    /// seconds of headroom at the 30K/sec target before the DropOldest
    /// backpressure policy kicks in.
    /// </summary>
    public const int DefaultNotificationChannelCapacity = 1_000;

    /// <summary>
    /// Bounded-channel capacity for the v2.1 §1.3 notification dispatcher.
    /// Must lie within
    /// <see cref="MinimumNotificationChannelCapacity"/> /
    /// <see cref="MaximumNotificationChannelCapacity"/>; validated by the
    /// adapter's <c>ValidateConfigAsync</c>.
    /// </summary>
    public int NotificationChannelCapacity { get; init; } = DefaultNotificationChannelCapacity;

    // ---- Monitored items -------------------------------------------------

    /// <summary>
    /// The set of OPC UA nodes the adapter subscribes to. Populated by
    /// the wizard via TagBrowseTreeView / auto-load (ADR-0015 Rules
    /// 9 / 10). Empty list = adapter starts in a valid state but emits
    /// no data — same shape as a brand-new instance before tags are
    /// added.
    /// </summary>
    public IReadOnlyList<MonitoredItemConfig> MonitoredItems { get; init; } =
        System.Array.Empty<MonitoredItemConfig>();

    /// <summary>
    /// Project a <see cref="SourceInstanceConfig"/> (the protocol-
    /// agnostic wire shape persisted in <c>gateway.json</c>) into a
    /// typed <see cref="OpcUaClientSourceConfiguration"/>. Mirrors the
    /// shape <c>OpcUaServerConfiguration.FromSinkInstance</c> uses for
    /// the OPC UA Server sink (the cref isn't a code-side reference
    /// because <c>Sinks.OpcUaServer</c> would be a forbidden cross-
    /// protocol-module reference per CLAUDE.md §9 #11).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="instance"/>'s protocol is not
    /// <c>"opcua-client"</c>, when
    /// <see cref="SourceInstanceConfig.Connection"/> is missing, or when
    /// <c>endpointUrl</c> is absent from the connection object.
    /// </exception>
    public static OpcUaClientSourceConfiguration FromSourceInstance(SourceInstanceConfig instance)
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
                $"SourceInstanceConfig '{instance.InstanceId}' is missing the required OPC UA Client "
                + "Connection object (must contain at least 'endpointUrl').",
                nameof(instance));
        }

        var conn = instance.Connection.Value;
        // Keys are named via OpcUaClientConnectionKeys so they cannot be parsed
        // without a redaction tier (ADR-0020 M-B Q-B2 — "by construction"); the
        // drift guard asserts OpcUaClientConnectionKeys.All == KnownKeys.
        var endpointUrl = ReadString(conn, OpcUaClientConnectionKeys.EndpointUrl)
            ?? throw new ArgumentException(
                $"OPC UA Client connection for source '{instance.InstanceId}' is missing the required "
                + "'endpointUrl' field.",
                nameof(instance));

        return new OpcUaClientSourceConfiguration
        {
            // Base SourceConfiguration fields
            InstanceId = instance.InstanceId,
            ProtocolName = instance.ProtocolName,
            DeviceId = instance.DeviceId,
            Enabled = instance.Enabled,

            // Endpoint
            EndpointUrl = endpointUrl,
            ApplicationName = ReadString(conn, OpcUaClientConnectionKeys.ApplicationName) ?? "Elpis EdgeConnect",
            ApplicationUri = ReadString(conn, OpcUaClientConnectionKeys.ApplicationUri) ?? "urn:elpis:edgeconnect:opcua-client",
            SessionTimeoutMs = ReadUInt(conn, OpcUaClientConnectionKeys.SessionTimeoutMs, 60_000),

            // Security
            SecurityMode = ReadEnum(conn, OpcUaClientConnectionKeys.SecurityMode, OpcUaSecurityMode.SignAndEncrypt),
            SecurityPolicyUri = ReadString(conn, OpcUaClientConnectionKeys.SecurityPolicyUri)
                ?? "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
            AuthMode = ReadEnum(conn, OpcUaClientConnectionKeys.AuthMode, OpcUaAuthMode.Anonymous),
            Credentials = ReadCredentials(conn),
            ApplicationCertificateStorePath = ReadString(conn, OpcUaClientConnectionKeys.ApplicationCertificateStorePath),
            AutoAcceptUntrustedServerCertificate = ReadBool(conn, OpcUaClientConnectionKeys.AutoAcceptUntrustedServerCertificate, true),

            // Tuning (v2.1 §2.5 locked defaults)
            PublishingIntervalMs = ReadInt(conn, OpcUaClientConnectionKeys.PublishingIntervalMs, 50),
            SamplingIntervalMs = ReadInt(conn, OpcUaClientConnectionKeys.SamplingIntervalMs, 50),
            KeepAliveCount = ReadUInt(conn, OpcUaClientConnectionKeys.KeepAliveCount, 20),
            LifetimeCount = ReadUInt(conn, OpcUaClientConnectionKeys.LifetimeCount, 60),
            MaxNotificationsPerPublish = ReadUInt(conn, OpcUaClientConnectionKeys.MaxNotificationsPerPublish, 1_000),
            DefaultAnalogQueueSize = ReadUInt(conn, OpcUaClientConnectionKeys.DefaultAnalogQueueSize, 2),
            DefaultDiscreteQueueSize = ReadUInt(conn, OpcUaClientConnectionKeys.DefaultDiscreteQueueSize, 10),
            NotificationChannelCapacity = ReadInt(conn, OpcUaClientConnectionKeys.NotificationChannelCapacity, DefaultNotificationChannelCapacity),

            // Monitored items
            MonitoredItems = ReadMonitoredItems(conn),
        };
    }

    // ─── JSON readers ────────────────────────────────────────────────

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int ReadInt(JsonElement obj, string name, int defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : defaultValue;

    private static uint ReadUInt(JsonElement obj, string name, uint defaultValue) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetUInt32()
            : defaultValue;

    private static bool ReadBool(JsonElement obj, string name, bool defaultValue) =>
        obj.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : defaultValue;

    private static T ReadEnum<T>(JsonElement obj, string name, T defaultValue) where T : struct, Enum
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String)
        {
            return defaultValue;
        }
        return Enum.TryParse<T>(v.GetString(), ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static OpcUaClientCredentials? ReadCredentials(JsonElement conn)
    {
        if (!conn.TryGetProperty(OpcUaClientConnectionKeys.Credentials, out var credEl) || credEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new OpcUaClientCredentials
        {
            Username = ReadString(credEl, OpcUaClientConnectionKeys.Username),
            Password = ReadString(credEl, OpcUaClientConnectionKeys.Password),
            CertificatePath = ReadString(credEl, OpcUaClientConnectionKeys.CertificatePath),
        };
    }

    private static IReadOnlyList<MonitoredItemConfig> ReadMonitoredItems(JsonElement conn)
    {
        if (!conn.TryGetProperty(OpcUaClientConnectionKeys.MonitoredItems, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<MonitoredItemConfig>();
        }
        var list = new List<MonitoredItemConfig>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var nodeId = ReadString(item, OpcUaClientConnectionKeys.NodeId);
            var displayName = ReadString(item, OpcUaClientConnectionKeys.DisplayName);
            if (nodeId is null || displayName is null) continue;
            list.Add(new MonitoredItemConfig
            {
                NodeId = nodeId,
                DisplayName = displayName,
                SamplingIntervalMs = item.TryGetProperty(OpcUaClientConnectionKeys.SamplingIntervalMs, out var si)
                    && si.ValueKind == JsonValueKind.Number ? si.GetInt32() : null,
                QueueSize = item.TryGetProperty(OpcUaClientConnectionKeys.QueueSize, out var qs)
                    && qs.ValueKind == JsonValueKind.Number ? qs.GetUInt32() : null,
                DeadbandPercent = item.TryGetProperty(OpcUaClientConnectionKeys.DeadbandPercent, out var db)
                    && db.ValueKind == JsonValueKind.Number ? db.GetDouble() : null,
            });
        }
        return list;
    }
}

