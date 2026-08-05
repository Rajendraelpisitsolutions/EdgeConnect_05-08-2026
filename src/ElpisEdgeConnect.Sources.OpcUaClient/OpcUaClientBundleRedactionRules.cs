// ============================================================================
// File: OpcUaClientBundleRedactionRules.cs
// Purpose: Per-protocol redaction metadata for the OPC UA Client connection
//          block (ADR-0020 Amendment 1, A1.2 mechanism 2 — "World 2a"). Metadata
//          only — no Management reference, no redaction logic.
// Reference: docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §3, §3a.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Redaction rules for the OPC UA Client source. The factory-read secret is the
/// user-identity <see cref="OpcUaClientConnectionKeys.Password"/> (MASK). The
/// endpoint, security, certificate-path, and tuning keys are benign and
/// included verbatim. Certificate / key <em>file paths</em> are INCLUDE (a path
/// is not key material); the credential <c>certificatePassword</c> — which the
/// factory does not currently parse — is masked via
/// <see cref="ExtraNameOverrides"/>.
/// </summary>
public sealed class OpcUaClientBundleRedactionRules : IBundleRedactionRules
{
    /// <inheritdoc />
    public string ProtocolName => OpcUaClientSourceConfiguration.ProtocolNameConstant;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
        new Dictionary<string, BundleTier>
        {
            [OpcUaClientConnectionKeys.EndpointUrl] = BundleTier.Include,
            [OpcUaClientConnectionKeys.ApplicationName] = BundleTier.Include,
            [OpcUaClientConnectionKeys.ApplicationUri] = BundleTier.Include,
            [OpcUaClientConnectionKeys.SessionTimeoutMs] = BundleTier.Include,
            [OpcUaClientConnectionKeys.SecurityMode] = BundleTier.Include,
            [OpcUaClientConnectionKeys.SecurityPolicyUri] = BundleTier.Include,
            [OpcUaClientConnectionKeys.AuthMode] = BundleTier.Include,
            [OpcUaClientConnectionKeys.Credentials] = BundleTier.Include,   // object; walker recurses in
            [OpcUaClientConnectionKeys.ApplicationCertificateStorePath] = BundleTier.Include, // path, not key material
            [OpcUaClientConnectionKeys.AutoAcceptUntrustedServerCertificate] = BundleTier.Include,
            [OpcUaClientConnectionKeys.Username] = BundleTier.Include,
            [OpcUaClientConnectionKeys.Password] = BundleTier.Mask,         // user-identity secret
            [OpcUaClientConnectionKeys.CertificatePath] = BundleTier.Include, // path, not key material
            [OpcUaClientConnectionKeys.PublishingIntervalMs] = BundleTier.Include,
            [OpcUaClientConnectionKeys.SamplingIntervalMs] = BundleTier.Include,
            [OpcUaClientConnectionKeys.KeepAliveCount] = BundleTier.Include,
            [OpcUaClientConnectionKeys.LifetimeCount] = BundleTier.Include,
            [OpcUaClientConnectionKeys.MaxNotificationsPerPublish] = BundleTier.Include,
            [OpcUaClientConnectionKeys.DefaultAnalogQueueSize] = BundleTier.Include,
            [OpcUaClientConnectionKeys.DefaultDiscreteQueueSize] = BundleTier.Include,
            [OpcUaClientConnectionKeys.NotificationChannelCapacity] = BundleTier.Include,
            [OpcUaClientConnectionKeys.MonitoredItems] = BundleTier.Include, // array; walker recurses in
            [OpcUaClientConnectionKeys.NodeId] = BundleTier.Include,
            [OpcUaClientConnectionKeys.DisplayName] = BundleTier.Include,
            [OpcUaClientConnectionKeys.QueueSize] = BundleTier.Include,
            [OpcUaClientConnectionKeys.DeadbandPercent] = BundleTier.Include,
        };

    /// <inheritdoc />
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>
        {
            // certificatePassword is a secret on OpcUaClientCredentials that the
            // current factory does NOT parse, so it is not a KnownKey. Mask it
            // defensively in case an operator hand-authors it into the JSON.
            ["certificatePassword"] = BundleTier.Mask,
        };
}
