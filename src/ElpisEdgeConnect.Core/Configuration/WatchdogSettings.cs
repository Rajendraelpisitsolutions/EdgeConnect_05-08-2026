// ============================================================================
// File: Configuration/WatchdogSettings.cs
// Purpose: Gateway watchdog configuration sub-record.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample), §11 (Reliability)
// Milestone: B1
// ============================================================================

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Watchdog configuration controlling whether the host process restarts
/// itself on unrecoverable failure. Implementation is a Phase 4 host concern;
/// this record is the JSON-loadable shape only.
/// </summary>
public sealed record WatchdogSettings
{
    /// <summary>
    /// True if the watchdog is enabled. Default <c>true</c> per blueprint
    /// §11 reliability requirements.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// True if the host should restart on unrecoverable failure. Default
    /// <c>true</c> per blueprint §8.1 sample.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool RestartOnFailure { get; init; } = true;
}
