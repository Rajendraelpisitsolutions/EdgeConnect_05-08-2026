// ============================================================================
// File: OpcUaCredential.cs
// Purpose: One operator-configured username/password pair the OPC UA
//          Server accepts under the UserName user-token policy.
//
//          Stored as plain-text in gateway.json at MVP — operators
//          protect via file-system permissions on the config dir.
//          A future K' or M' milestone can introduce hashed
//          credentials and an admin-only management API for rotation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone K
//            docs/ops-runbook.md (credential lifecycle)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// One acceptable OPC UA username/password identity.
/// </summary>
public sealed record OpcUaCredential
{
    /// <summary>Operator-supplied username (case-sensitive).</summary>
    public required string Username { get; init; }

    /// <summary>
    /// Plain-text password at MVP. Operators MUST restrict
    /// gateway.json permissions appropriately (Windows: ACL the
    /// EdgeConnect data dir; Linux: <c>chmod 600</c> on the config
    /// file). K' will introduce hashed-password storage.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Optional human-readable display name for the audit trail.
    /// Surfaces in OPC UA session diagnostics when set.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Constant-time compare against <paramref name="candidate"/>.
    /// Prevents timing-side-channel attacks even though MVP stores
    /// passwords in plain text — defense in depth.
    /// </summary>
    public bool MatchesPassword(string? candidate)
    {
        if (candidate is null)
        {
            return false;
        }
        var configured = Password;
        if (configured.Length != candidate.Length)
        {
            // Length mismatch leaks one bit, but constant-time padding
            // is overkill at MVP. Operators with high-assurance needs
            // should use Sign+Encrypt + Certificate auth instead.
            return false;
        }
        var diff = 0;
        for (var i = 0; i < configured.Length; i++)
        {
            diff |= configured[i] ^ candidate[i];
        }
        return diff == 0;
    }
}
