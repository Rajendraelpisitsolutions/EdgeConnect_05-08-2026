// ============================================================================
// File: Contracts/AuditChainStatus.cs
// Purpose: Result of re-verifying the SHA-256 hash chain on the
//          configuration audit log. Surfaced by
//          /api/v1/diagnostics/audit-chain and rendered as a banner on
//          the Studio's Diagnostics page.
//
//          Auditors and compliance reviewers explicitly want to see
//          "verified ✓" — silence is suspect. A banner that always
//          renders with current chain status is the right operational
//          shape.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
//            ARCHITECTURE_BLUEPRINT.md §17.7 (Audit integrity)
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Result of a configuration-audit hash-chain verification pass.
/// Returned by the diagnostics audit-chain endpoint; the Studio renders
/// either a green ✓ banner (Verified=true) or a red ✗ banner with the
/// failure detail.
/// </summary>
public sealed record AuditChainStatus
{
    /// <summary>True when every entry chained back to genesis without tampering.</summary>
    public required bool Verified { get; init; }

    /// <summary>
    /// Number of entries the verifier read before completing (or stopping
    /// at the broken link). Useful for operator context — "verified 142
    /// entries" vs "verified the first 12 then chain broke at #13".
    /// </summary>
    public required long EntriesChecked { get; init; }

    /// <summary>
    /// Human-readable failure reason. Null when <see cref="Verified"/> is
    /// true. On failure, contains the underlying
    /// <c>ConfigurationException</c> message (typically the broken entry
    /// index and the expected-vs-found PreviousHash values).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>UTC timestamp the verification ran. Lets the UI show "checked just now".</summary>
    public required System.DateTime CheckedAtUtc { get; init; }
}
