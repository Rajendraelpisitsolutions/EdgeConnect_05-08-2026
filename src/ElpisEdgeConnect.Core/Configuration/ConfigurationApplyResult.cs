// ============================================================================
// File: Configuration/ConfigurationApplyResult.cs
// Purpose: Result record returned by IConfigurationManager.ApplyDraftAsync
//          and RollbackAsync.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Result of an apply or rollback operation. On success the
/// <see cref="VersionId"/> identifies the new current configuration and the
/// <see cref="AuditEntry"/> is the entry written to the audit log. On
/// failure the <see cref="ValidationResult"/> describes why.
/// </summary>
public sealed record ConfigurationApplyResult
{
    /// <summary>True if the apply / rollback succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The new current version id, set on success. <see cref="ConfigurationVersionId.Initial"/>
    /// when <see cref="Success"/> is false.
    /// </summary>
    public required ConfigurationVersionId VersionId { get; init; }

    /// <summary>
    /// Validation result from the apply pipeline. Populated even on success
    /// (it will report any warnings). On failure this is the source of
    /// truth for what went wrong.
    /// </summary>
    public required ValidationResult ValidationResult { get; init; }

    /// <summary>The audit entry written for this operation. Null on failure.</summary>
    public ConfigurationAuditEntry? AuditEntry { get; init; }

    /// <summary>Number of changes detected by the differ.</summary>
    public int ChangeCount => AuditEntry?.ChangeCount ?? 0;

    /// <summary>Convenience factory for failed results.</summary>
    public static ConfigurationApplyResult Failed(ValidationResult validation) =>
        new()
        {
            Success = false,
            VersionId = ConfigurationVersionId.Initial,
            ValidationResult = validation,
        };
}
