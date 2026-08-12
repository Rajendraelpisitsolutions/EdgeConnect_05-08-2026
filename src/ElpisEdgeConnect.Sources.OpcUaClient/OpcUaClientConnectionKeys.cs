// ============================================================================
// File: OpcUaClientConnectionKeys.cs
// Purpose: Single source of truth for the JSON key names inside an OPC UA
//          Client source's opaque connection block. Both the config factory
//          (OpcUaClientSourceConfiguration.FromSourceInstance) and the
//          redaction rules name keys ONLY via these constants — the "by
//          construction" guarantee of ADR-0020 M-B (Q-B2). Includes keys nested
//          under `credentials` and inside `monitoredItems[]`, because the
//          registry resolves opaque keys by leaf name at any depth.
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §3, §3a.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// JSON key-name constants for the OPC UA Client connection block. The factory
/// and the redaction rules both reference these; the redaction drift guard
/// asserts <see cref="All"/> equals the rules' declared <c>KnownKeys</c> set.
/// </summary>
public static class OpcUaClientConnectionKeys
{
    // ---- Endpoint ----
    /// <summary>OPC UA endpoint URL.</summary>
    public const string EndpointUrl = "endpointUrl";
    /// <summary>Operator-readable application name.</summary>
    public const string ApplicationName = "applicationName";
    /// <summary>Application URI (trust anchor).</summary>
    public const string ApplicationUri = "applicationUri";
    /// <summary>Session timeout in ms.</summary>
    public const string SessionTimeoutMs = "sessionTimeoutMs";

    // ---- Security ----
    /// <summary>Endpoint security mode.</summary>
    public const string SecurityMode = "securityMode";
    /// <summary>Security policy URI.</summary>
    public const string SecurityPolicyUri = "securityPolicyUri";
    /// <summary>User-identity auth mode.</summary>
    public const string AuthMode = "authMode";
    /// <summary>Credentials object (recursed into; nested keys below).</summary>
    public const string Credentials = "credentials";
    /// <summary>Application instance certificate store path.</summary>
    public const string ApplicationCertificateStorePath = "applicationCertificateStorePath";
    /// <summary>Auto-accept untrusted server certificate flag.</summary>
    public const string AutoAcceptUntrustedServerCertificate = "autoAcceptUntrustedServerCertificate";

    // ---- Credentials (nested under `credentials`) ----
    /// <summary>Username (nested).</summary>
    public const string Username = "username";
    /// <summary>Password (nested, secret).</summary>
    public const string Password = "password";
    /// <summary>Client certificate file path (nested).</summary>
    public const string CertificatePath = "certificatePath";

    // ---- Subscription tuning ----
    /// <summary>Publishing interval in ms.</summary>
    public const string PublishingIntervalMs = "publishingIntervalMs";
    /// <summary>Sampling interval in ms (top-level and per monitored item).</summary>
    public const string SamplingIntervalMs = "samplingIntervalMs";
    /// <summary>Keep-alive count.</summary>
    public const string KeepAliveCount = "keepAliveCount";
    /// <summary>Lifetime count.</summary>
    public const string LifetimeCount = "lifetimeCount";
    /// <summary>Max notifications per publish.</summary>
    public const string MaxNotificationsPerPublish = "maxNotificationsPerPublish";
    /// <summary>Default analog queue size.</summary>
    public const string DefaultAnalogQueueSize = "defaultAnalogQueueSize";
    /// <summary>Default discrete queue size.</summary>
    public const string DefaultDiscreteQueueSize = "defaultDiscreteQueueSize";
    /// <summary>Notification channel capacity.</summary>
    public const string NotificationChannelCapacity = "notificationChannelCapacity";

    // ---- Monitored items (array; nested keys below) ----
    /// <summary>Monitored items array.</summary>
    public const string MonitoredItems = "monitoredItems";
    /// <summary>Node id (nested per item).</summary>
    public const string NodeId = "nodeId";
    /// <summary>Display name (nested per item).</summary>
    public const string DisplayName = "displayName";
    /// <summary>Queue size (nested per item).</summary>
    public const string QueueSize = "queueSize";
    /// <summary>Deadband percent (nested per item).</summary>
    public const string DeadbandPercent = "deadbandPercent";

    /// <summary>
    /// The complete, ordered list of connection keys the OPC UA factory reads
    /// (top-level, nested credentials, and per-monitored-item). The redaction
    /// drift guard asserts the rules' <c>KnownKeys</c> covers exactly this set.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        EndpointUrl,
        ApplicationName,
        ApplicationUri,
        SessionTimeoutMs,
        SecurityMode,
        SecurityPolicyUri,
        AuthMode,
        Credentials,
        ApplicationCertificateStorePath,
        AutoAcceptUntrustedServerCertificate,
        Username,
        Password,
        CertificatePath,
        PublishingIntervalMs,
        SamplingIntervalMs,
        KeepAliveCount,
        LifetimeCount,
        MaxNotificationsPerPublish,
        DefaultAnalogQueueSize,
        DefaultDiscreteQueueSize,
        NotificationChannelCapacity,
        MonitoredItems,
        NodeId,
        DisplayName,
        QueueSize,
        DeadbandPercent,
    };
}
