// ============================================================================
// File: Configuration/ConfigurationStorageLayout.cs
// Purpose: Centralized filesystem layout for the configuration manager.
//          One place to change if the directory structure moves.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System.IO;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Centralizes the filesystem paths used by the configuration manager.
/// All path computation goes through this type so the layout can be
/// changed in one place.
/// </summary>
public sealed class ConfigurationStorageLayout
{
    /// <summary>
    /// Construct a layout rooted at the given gateway data directory
    /// (typically <c>data/</c> from <see cref="GatewaySettings.DataPath"/>).
    /// </summary>
    public ConfigurationStorageLayout(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new System.ArgumentException("Root path cannot be null, empty, or whitespace.", nameof(rootPath));
        }
        RootPath = rootPath;
    }

    /// <summary>The gateway data directory.</summary>
    public string RootPath { get; }

    /// <summary>The active configuration file: <c>{root}/config/current.json</c>.</summary>
    public string CurrentConfigPath => Path.Combine(RootPath, "config", "current.json");

    /// <summary>The drafts directory: <c>{root}/config/drafts/</c>.</summary>
    public string DraftsDirectory => Path.Combine(RootPath, "config", "drafts");

    /// <summary>The history directory: <c>{root}/config/history/</c>.</summary>
    public string HistoryDirectory => Path.Combine(RootPath, "config", "history");

    /// <summary>The audit log file: <c>{root}/config/history/audit.log</c>.</summary>
    public string AuditLogPath => Path.Combine(HistoryDirectory, "audit.log");

    /// <summary>Path to a draft file for the given draft id.</summary>
    public string DraftPath(DraftId draftId) =>
        Path.Combine(DraftsDirectory, $"{draftId.Value}.json");

    /// <summary>Path to a history file for the given version id.</summary>
    public string HistoryPath(ConfigurationVersionId versionId) =>
        Path.Combine(HistoryDirectory, $"{versionId.Value}.json");

    /// <summary>Ensures every directory in the layout exists, creating any that are missing.</summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Path.Combine(RootPath, "config"));
        Directory.CreateDirectory(DraftsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
    }
}
