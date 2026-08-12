// ============================================================================
// File: Configuration/ConfigurationAuditEntry.cs
// Purpose: One immutable entry in the configuration audit log.
// Reference: ARCHITECTURE_BLUEPRINT.md §17.7 (Audit integrity), §8.2
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Diagnostics;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// What action the audit entry records.
/// </summary>
public enum ConfigurationAuditAction
{
    /// <summary>A draft was created.</summary>
    DraftCreated = 0,

    /// <summary>A draft was discarded without being applied.</summary>
    DraftDiscarded = 1,

    /// <summary>A draft was successfully applied.</summary>
    Applied = 2,

    /// <summary>A previous version was rolled back to.</summary>
    RolledBack = 3,

    /// <summary>
    /// A runtime observation against the current configuration — a
    /// cross-record validation failure detected at startup or hot-reload.
    /// Actor is always <c>"system"</c>. Carries the structured fault detail
    /// in <see cref="ConfigurationAuditEntry.RuntimeFault"/>. Added in
    /// M.P2.1 (fail-soft Core startup) so the audit chain preserves a
    /// forensic record of why specific instances did not come up.
    /// </summary>
    RuntimeConfigurationFault = 4,

    /// <summary>
    /// The configuration manager auto-provisioned an empty-state config at
    /// startup because no <c>current.json</c> was found. Actor is always
    /// <c>"system"</c>. Added by ADR-0016 (onboarding meta-wizard) — see
    /// Rule 5 (first-run self-provisioning). Captured in the audit chain
    /// as the seed "first version" event so the gateway's lifetime
    /// history is complete from version 0.
    /// </summary>
    AutoProvisioned = 5,

    /// <summary>
    /// A diagnostic bundle was generated and may have left the gateway
    /// (ADR-0020 Rule 5). Actor is the operator/session that initiated it;
    /// <see cref="ConfigurationAuditEntry.Summary"/> carries the manifest
    /// summary hash. Lives in the same audit chain as config changes so a
    /// customer can answer "did we ever ship a bundle from this gateway?".
    /// First non-config action — the audit stream is becoming a gateway-wide
    /// audit log (bundle plan v2 §6).
    /// </summary>
    BundleGenerated = 6,
}

/// <summary>
/// One entry in the configuration audit log. Append-only; entries are
/// chained by SHA-256 hash so tampering is detectable.
/// </summary>
public sealed record ConfigurationAuditEntry
{
    /// <summary>UTC timestamp when the action occurred.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// The version id this entry corresponds to. For <see cref="ConfigurationAuditAction.Applied"/>
    /// and <see cref="ConfigurationAuditAction.RolledBack"/>, this is the
    /// version id of the new current config. For draft actions this is
    /// <see cref="ConfigurationVersionId.Initial"/>.
    /// </summary>
    public required ConfigurationVersionId VersionId { get; init; }

    /// <summary>
    /// The previous version id (the one being replaced). Null for the
    /// first apply or for draft actions.
    /// </summary>
    public ConfigurationVersionId? PreviousVersionId { get; init; }

    /// <summary>What action was performed.</summary>
    public required ConfigurationAuditAction Action { get; init; }

    /// <summary>
    /// Free-form actor identifier. Set by the caller; B2 does not validate
    /// or authenticate. Defaults to <c>"system"</c> when not supplied.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>The draft id involved (for draft and apply actions). Null for rollback.</summary>
    public DraftId? DraftId { get; init; }

    /// <summary>Human-readable summary of the change.</summary>
    public required string Summary { get; init; }

    /// <summary>Number of changes in <see cref="Changes"/>.</summary>
    public int ChangeCount => Changes?.Count ?? 0;

    /// <summary>
    /// Structured list of changes. Empty for draft create/discard.
    /// Populated by <see cref="ConfigurationDiffer"/> for apply / rollback.
    /// </summary>
    public IReadOnlyList<ConfigurationChange> Changes { get; init; } = [];

    /// <summary>
    /// Structured runtime-fault detail. Populated only when
    /// <see cref="Action"/> is
    /// <see cref="ConfigurationAuditAction.RuntimeConfigurationFault"/>;
    /// null otherwise. Added in M.P2.1; older entries omit this property
    /// (serialized with WhenWritingNull so the hash chain remains stable
    /// across the schema extension).
    /// </summary>
    public ConfigurationFault? RuntimeFault { get; init; }

    /// <summary>
    /// SHA-256 (lowercase hex) of the previous audit entry's serialized
    /// JSON. The first entry uses 64 zeros. Used by
    /// <c>ConfigurationAuditLog</c> to detect tampering on read.
    /// </summary>
    public required string PreviousHash { get; init; }
}
