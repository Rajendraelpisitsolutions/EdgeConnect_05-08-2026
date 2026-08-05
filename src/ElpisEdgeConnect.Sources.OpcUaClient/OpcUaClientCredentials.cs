// ============================================================================
// File: OpcUaClientCredentials.cs
// Purpose: User-identity credentials block carried inside the OPC UA
//          Client configuration. Empty (all-null) by default — the
//          Anonymous auth mode requires no credentials. UserName mode
//          requires Username + Password; Certificate mode requires
//          CertificatePath (and optionally CertificatePassword).
//
//          Secret handling: Password fields are plain strings on this
//          record — the secret-redaction layer at the Configuration
//          boundary (per ADR-0014 + the existing redaction infrastructure)
//          is responsible for masking these in logs, diagnostics, and
//          serialised audit entries. Adapters MUST NOT log raw passwords.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §6 Q7
//            docs/decisions/0014-config-vs-runtime-state.md
// ============================================================================

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Credentials block for non-Anonymous OPC UA Client auth modes. Cardinality
/// (which fields are populated) depends on the configured
/// <see cref="OpcUaAuthMode"/>; the adapter's <c>ValidateConfigAsync</c>
/// enforces the coherence.
/// </summary>
public sealed record OpcUaClientCredentials
{
    /// <summary>
    /// Username for <see cref="OpcUaAuthMode.UserName"/>. Required when
    /// that auth mode is selected. Null otherwise.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Password for <see cref="OpcUaAuthMode.UserName"/>. Required when
    /// that auth mode is selected. MUST be redacted in logs and
    /// diagnostics by the secret-redaction layer; adapter code never
    /// logs this directly.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Path to the client certificate file for
    /// <see cref="OpcUaAuthMode.Certificate"/>. Required when that auth
    /// mode is selected. Resolved by the cert manager at start time;
    /// missing path → start fails with <c>OPCUA.CERT_MISSING</c>.
    /// </summary>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// Optional password protecting the certificate file. Same redaction
    /// rules as <see cref="Password"/>.
    /// </summary>
    public string? CertificatePassword { get; init; }
}
