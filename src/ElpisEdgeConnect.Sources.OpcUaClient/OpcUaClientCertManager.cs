// ============================================================================
// File: OpcUaClientCertManager.cs
// Purpose: Client-side certificate management for the OPC UA Client source
//          adapter. Owns the application-instance certificate store + the
//          trusted/rejected SERVER certificate stores (note: server side
//          tracks trusted/rejected CLIENT certificates instead — same
//          shape, different anchor).
//
//          INTENTIONALLY DOES NOT REUSE OpcUaCertManager FROM
//          Sinks.OpcUaServer. The original plan v2.1 §5.1 suggested
//          "Reuses OpcUaCertManager from Sinks.OpcUaServer; wraps for
//          client-side trust chain" — but that crosses the source/sink
//          protocol-module boundary in violation of CLAUDE.md §9 anti-
//          pattern #11 ("Referencing a protocol module from another
//          protocol module"). The two implementations share a SHAPE (both
//          manage cert stores via the OPC Foundation stack's
//          CertificateIdentifier / CertificateTrustList) but live in their
//          own assemblies. If duplication becomes a maintenance burden,
//          the right fix is extracting to a Core.OpcUa shared namespace —
//          NOT a cross-module reference.
//
// LOCKED design rules:
//   * Store paths resolve via the platform-standard %LocalApplicationData%
//     (Windows) / ~/.local/share (Linux) so the same code runs cross-
//     platform without configuration
//   * Operator override via OpcUaClientSourceConfiguration.ApplicationCertificateStorePath
//   * Default subject name baked from ApplicationName so a fresh deploy
//     auto-generates a self-signed cert on first start
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
//            docs/decisions/0015-wizard-contract.md (Rule 6 — Test Connection read-only)
//            CLAUDE.md §9 anti-pattern #11
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Build the client-side <see cref="SecurityConfiguration"/> shape and
/// ensure the application-instance certificate exists. Pure logic — no
/// network calls. Tests cover store path resolution + the
/// SecurityConfiguration shape against a temp directory.
/// </summary>
internal sealed class OpcUaClientCertManager
{
    private readonly OpcUaClientSourceConfiguration _config;

    public OpcUaClientCertManager(OpcUaClientSourceConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>
    /// Default root for the cert stores when the operator hasn't supplied
    /// an explicit path. Resolves to
    /// <c>%LocalApplicationData%/EdgeConnect/OpcUaClient/{InstanceId}</c>
    /// — per-instance isolation prevents two source instances with
    /// different security profiles from sharing trust state.
    /// </summary>
    public string DefaultStoreRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EdgeConnect",
            "OpcUaClient",
            _config.InstanceId);

    /// <summary>
    /// The cert-store root used for THIS instance. Resolves the operator
    /// override on <see cref="OpcUaClientSourceConfiguration.ApplicationCertificateStorePath"/>
    /// or falls back to <see cref="DefaultStoreRoot"/>.
    /// </summary>
    public string EffectiveStoreRoot =>
        string.IsNullOrWhiteSpace(_config.ApplicationCertificateStorePath)
            ? DefaultStoreRoot
            : _config.ApplicationCertificateStorePath;

    /// <summary>
    /// Build the <see cref="SecurityConfiguration"/> the OPC Foundation
    /// stack consumes. Honours the auto-accept-untrusted-server flag and
    /// applies sane defaults for cert-key size + SHA1 rejection.
    /// </summary>
    public SecurityConfiguration BuildSecurityConfiguration() =>
        new()
        {
            ApplicationCertificate = new CertificateIdentifier
            {
                StoreType = "Directory",
                StorePath = Path.Combine(EffectiveStoreRoot, "own"),
                SubjectName = _config.ApplicationName,
            },
            TrustedPeerCertificates = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = Path.Combine(EffectiveStoreRoot, "trusted"),
            },
            TrustedIssuerCertificates = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = Path.Combine(EffectiveStoreRoot, "issuers"),
            },
            RejectedCertificateStore = new CertificateTrustList
            {
                StoreType = "Directory",
                StorePath = Path.Combine(EffectiveStoreRoot, "rejected"),
            },
            AutoAcceptUntrustedCertificates = _config.AutoAcceptUntrustedServerCertificate,
            RejectSHA1SignedCertificates = false,
            MinimumCertificateKeySize = 2048,
            AddAppCertToTrustedStore = true,
        };

    /// <summary>
    /// Ensure the application instance certificate exists at the
    /// configured store path. Auto-creates a self-signed cert on first
    /// call. Throws <see cref="InvalidOperationException"/> with the
    /// <c>OPCUA.CERT_INIT_FAILED</c> error code if the certificate can't
    /// be established (e.g. read-only filesystem, permissions, etc.).
    /// </summary>
    public async Task EnsureApplicationCertificateAsync(
        ApplicationInstance applicationInstance,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(applicationInstance);
        ct.ThrowIfCancellationRequested();

        var hasAppCertificate = await applicationInstance
            .CheckApplicationInstanceCertificates(silent: false)
            .ConfigureAwait(false);
        if (!hasAppCertificate)
        {
            throw new InvalidOperationException(
                $"OPCUA.CERT_INIT_FAILED: could not establish application instance certificate "
                + $"at '{EffectiveStoreRoot}/own'. Check filesystem permissions or pre-provision "
                + "a certificate at the configured store path.");
        }
    }
}
