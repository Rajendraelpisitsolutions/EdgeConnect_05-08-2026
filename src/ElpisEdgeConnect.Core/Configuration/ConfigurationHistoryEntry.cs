// ============================================================================
// File: Configuration/ConfigurationHistoryEntry.cs
// Purpose: Lightweight metadata record for one applied config version.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Lightweight metadata describing one applied configuration version.
/// Returned by <c>IConfigurationManager.GetHistoryAsync</c> for listing
/// UIs. Does NOT include the full config — that's loaded on demand by
/// the manager when rolling back.
/// </summary>
public sealed record ConfigurationHistoryEntry
{
    /// <summary>The version id this entry refers to.</summary>
    public required ConfigurationVersionId VersionId { get; init; }

    /// <summary>UTC timestamp when this version was applied.</summary>
    public required DateTime AppliedAt { get; init; }

    /// <summary>Human-readable summary from the audit log.</summary>
    public required string Summary { get; init; }

    /// <summary>Size of the history file on disk, in bytes.</summary>
    public long SizeBytes { get; init; }
}
