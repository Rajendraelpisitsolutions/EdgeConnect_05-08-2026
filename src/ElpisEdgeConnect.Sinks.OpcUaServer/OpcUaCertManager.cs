// ============================================================================
// File: OpcUaCertManager.cs
// Purpose: Centralizes the OPC UA application-instance certificate
//          lifecycle:
//             - Resolve the on-disk paths from OpcUaSecurityConfig.
//             - Build the SecurityConfiguration block consumed by the
//               OPC UA library (own / trusted-peer / rejected / issuer
//               stores).
//             - On first start (or when the existing cert is missing),
//               generate a self-signed cert via the library's
//               CheckApplicationInstanceCertificate hook.
//
//          Extracting this from OpcUaServerSinkAdapter keeps the
//          adapter focused on data-flow concerns and gives K and
//          future milestones one place to evolve cert handling
//          (rotation, HSM-backed keys, etc.).
//
//          Default path layout, under %LocalApplicationData% (or
//          /var/lib on Linux):
//             EdgeConnect/OpcUa/pki/own       — server's own cert
//             EdgeConnect/OpcUa/pki/trusted   — explicitly trusted clients
//             EdgeConnect/OpcUa/pki/rejected  — clients pending review
//             EdgeConnect/OpcUa/pki/issuer    — trusted CA issuers
//
//          Operators can override any path via OpcUaSecurityConfig
//          when the gateway runs in a non-standard data dir.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone K
//            docs/ops-runbook.md (cert lifecycle section)
//            shared-knowledge/contracts/opcua-namespace-policy.md
//                (§ Cert management)
// ============================================================================

using System;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Owns the OPC UA application-instance certificate lifecycle.
/// Stateless helper today; future milestones can add rotation +
/// HSM-backed key state here without touching the adapter.
/// </summary>
public sealed class OpcUaCertManager
{
    private readonly OpcUaSecurityConfig _security;
    private readonly string _applicationName;

    /// <summary>
    /// Construct a cert manager for the given security configuration.
    /// </summary>
    public OpcUaCertManager(OpcUaSecurityConfig security, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        _security = security;
        _applicationName = applicationName;
    }

    /// <summary>Path to the server's own (private-key-bearing) cert store.</summary>
    public string OwnStorePath => _security.ApplicationCertificatePath
        ?? DefaultPath("own");

    /// <summary>Path to the trusted-clients cert store.</summary>
    public string TrustedClientsPath => _security.TrustedClientsPath
        ?? DefaultPath("trusted");

    /// <summary>Path to the rejected-clients cert store (review-bucket).</summary>
    public string RejectedClientsPath => _security.RejectedClientsPath
        ?? DefaultPath("rejected");

    /// <summary>Path to the trusted-issuer (CA) store.</summary>
    public string IssuerStorePath => DefaultPath("issuer");

    /// <summary>
    /// Build the OPC UA <see cref="SecurityConfiguration"/> block for
    /// the application config. The library uses this to locate every
    /// cert store and to decide whether to accept untrusted clients.
    /// </summary>
    public SecurityConfiguration BuildSecurityConfiguration()
    {
        var allowAutoAccept = _security.Mode == OpcUaSecurityMode.None;
        return new SecurityConfiguration
        {
            ApplicationCertificate = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = OwnStorePath,
                SubjectName = $"CN={_applicationName}",
            },
            TrustedPeerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = TrustedClientsPath,
            },
            RejectedCertificateStore = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = RejectedClientsPath,
            },
            TrustedIssuerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = IssuerStorePath,
            },
            // Auto-accept ONLY in None mode. In Sign / SignAndEncrypt
            // every connecting client is verified against the trust
            // list; unknown certs land in the rejected store for
            // operator review and explicit promotion. This is the
            // locked K behavior.
            AutoAcceptUntrustedCertificates = allowAutoAccept,
            // Reasonable defaults — operators can override via
            // SecurityConfig if Customer B's MES demands different
            // policy. K MVP keeps these conservative.
            AddAppCertToTrustedStore = false,
            RejectSHA1SignedCertificates = true,
            RejectUnknownRevocationStatus = false,
            MinimumCertificateKeySize = 2048,
        };
    }

    /// <summary>
    /// Ensure the application-instance certificate exists on disk;
    /// generate a self-signed one on first start. Idempotent — safe
    /// to call every adapter start. Delegates to the OPC UA library's
    /// well-tested cert-generation pipeline (RSA-2048 by default).
    /// </summary>
    public async Task EnsureApplicationCertificateAsync(
        ApplicationInstance appInstance,
        System.Threading.CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(appInstance);
        _ = ct; // CS0618 overload doesn't take a ct; surface it for future signature changes.

        // CS0618: the OPCFoundation library deprecates the 2-arg
        // overload in favor of a key-size-by-cert-type variant. We
        // pin RSA-2048 here — the same baseline H.1 used. K can move
        // to per-policy key sizes when Customer B asks for ECC certs.
#pragma warning disable CS0618
        await appInstance
            .CheckApplicationInstanceCertificate(silent: true, minimumKeySize: 2048)
            .ConfigureAwait(false);
#pragma warning restore CS0618
    }

    private static string DefaultPath(string segment) =>
        System.IO.Path.Combine("%LocalApplicationData%", "EdgeConnect", "OpcUa", "pki", segment);
}
