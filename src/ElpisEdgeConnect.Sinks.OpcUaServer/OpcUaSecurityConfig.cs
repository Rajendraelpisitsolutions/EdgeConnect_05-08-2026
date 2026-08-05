// ============================================================================
// File: OpcUaSecurityConfig.cs
// Purpose: OPC UA security + user-authentication configuration.
//          Schema accepts the FULL set of forward-compat values from day
//          one (Sign / SignAndEncrypt modes; UserName / Certificate user
//          tokens) so customer configs authored today work unchanged
//          when Milestone K flips the implementation switch.
//          MVP enforcement (Milestone H.1): reject non-None mode and
//          non-Anonymous user tokens at adapter Initialize with a clear
//          OPCUA.SECURITY_NOT_YET_IMPLEMENTED error.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestones H, K
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// OPC UA endpoint security configuration. The schema is forward-compatible
/// (every field operators might want to set in Milestone K's hardened
/// release exists today); the MVP only honors None + Anonymous and rejects
/// the rest at validation time.
/// </summary>
public sealed record OpcUaSecurityConfig
{
    /// <summary>
    /// Endpoint security mode. MVP-implemented: <see cref="OpcUaSecurityMode.None"/>.
    /// <see cref="OpcUaSecurityMode.Sign"/> and
    /// <see cref="OpcUaSecurityMode.SignAndEncrypt"/> are accepted at
    /// config parse time but rejected at adapter Initialize until
    /// Milestone K lands.
    /// </summary>
    public OpcUaSecurityMode Mode { get; init; } = OpcUaSecurityMode.None;

    /// <summary>
    /// User-authentication policies offered by the endpoint. Multiple
    /// policies may be enabled per the OPC UA spec; clients pick one
    /// at session-open time. MVP-implemented:
    /// <see cref="OpcUaUserTokenPolicy.Anonymous"/>. Others rejected
    /// at Initialize until Milestone K.
    /// </summary>
    public IReadOnlyList<OpcUaUserTokenPolicy> UserTokenPolicies { get; init; } =
        new[] { OpcUaUserTokenPolicy.Anonymous };

    /// <summary>
    /// Path to the server's application certificate (X.509 PFX).
    /// Required for any <see cref="Mode"/> other than
    /// <see cref="OpcUaSecurityMode.None"/>. Null is acceptable in MVP
    /// (None mode) and triggers OpcUaCertManager's first-start
    /// self-signed-cert generation when Sign/Encrypt is enabled in
    /// Milestone K.
    /// </summary>
    public string? ApplicationCertificatePath { get; init; }

    /// <summary>
    /// Directory containing X.509 certificates of clients explicitly
    /// trusted by this server. Honored by Milestone K's cert manager.
    /// </summary>
    public string? TrustedClientsPath { get; init; }

    /// <summary>
    /// Directory where the server places rejected client certificates
    /// (so an operator can review and explicitly move accepted ones
    /// into <see cref="TrustedClientsPath"/>). Honored by Milestone K.
    /// </summary>
    public string? RejectedClientsPath { get; init; }

    /// <summary>
    /// Acceptable username/password pairs for clients connecting under
    /// the <see cref="OpcUaUserTokenPolicy.UserName"/> policy. Empty
    /// when UserName auth is not configured; non-empty when it is.
    /// The adapter rejects any UserName authentication attempt whose
    /// username + password don't match an entry on this list with
    /// <c>BadIdentityTokenRejected</c>.
    /// <para>
    /// MVP storage is plain-text in gateway.json. Operators MUST
    /// restrict the config directory's permissions accordingly. A
    /// future milestone will introduce hashed-credential storage and
    /// an admin API for rotation.
    /// </para>
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<OpcUaCredential> Credentials { get; init; } =
        System.Array.Empty<OpcUaCredential>();
}
