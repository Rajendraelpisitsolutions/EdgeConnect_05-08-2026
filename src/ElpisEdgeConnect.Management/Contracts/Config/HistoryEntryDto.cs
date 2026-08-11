// ============================================================================
// File: Contracts/Config/HistoryEntryDto.cs
// Purpose: One row of the version-history table on /config. Mirrors
//          Core.Configuration.ConfigurationHistoryEntry + a wire-level
//          IsCurrent flag computed against IConfigurationManager.CurrentVersionId.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// One past configuration version. Returned newest-first from
/// <c>GET /api/v1/config/history</c>.
/// </summary>
public sealed record HistoryEntryDto
{
    /// <summary>The version id (e.g. <c>"2026-05-14T18-05-58-001Z-042"</c>).</summary>
    public required string VersionId { get; init; }

    /// <summary>UTC timestamp the version became current.</summary>
    public required DateTime AppliedAtUtc { get; init; }

    /// <summary>Human-readable summary from the audit log.</summary>
    public required string Summary { get; init; }

    /// <summary>Size of the version's history file on disk, in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// True when this version equals the gateway's current active version
    /// id. The Studio uses this to decorate the row (and hide its Rollback
    /// button — rolling back to the current version is a no-op).
    /// </summary>
    public required bool IsCurrent { get; init; }
}

/// <summary>
/// Metadata about the currently active configuration. Returned from
/// <c>GET /api/v1/config/version</c> so the Studio doesn't have to
/// download the full config just to display "v3 — 9.4 KB — applied 2h ago."
/// </summary>
public sealed record CurrentConfigVersionDto
{
    /// <summary>The active version id.</summary>
    public required string VersionId { get; init; }

    /// <summary>UTC timestamp this version became current.</summary>
    public required DateTime AppliedAtUtc { get; init; }

    /// <summary>Human-readable summary from the audit log.</summary>
    public required string Summary { get; init; }

    /// <summary>Size of the active config file on disk, in bytes.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Absolute filesystem path of the active <c>current.json</c>. Added
    /// in M.2b.6.2 §3.B so operators can see at a glance which file the
    /// gateway just loaded — especially useful when an environment
    /// override is steering the path away from the default
    /// <c>%ProgramData%\EdgeConnect\config\current.json</c>.
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Which environment variable (if any) overrode the default
    /// configuration path. <see langword="null"/> means the default is
    /// in effect; a non-null value names the env var (e.g.
    /// <c>"EDGECONNECT_DATA_ROOT"</c>). The Studio renders this as a
    /// small chip on the Config page when set.
    /// </summary>
    public string? Override { get; init; }
}
