// ============================================================================
// File: Configuration/PollingSettings.cs
// Purpose: Polling configuration sub-record for source instances.
// Reference: ARCHITECTURE_BLUEPRINT.md §8.1 (sample), §18.6 (per-protocol envelopes)
// Milestone: B1
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Polling parameters for a source connector instance. Used by the routing
/// engine (Phase 1 C3) to drive periodic <see cref="Adapters.ISourceAdapter.PollAsync"/>
/// calls.
/// </summary>
public sealed record PollingSettings
{
    /// <summary>
    /// Polling interval in milliseconds. Default 1000 ms per blueprint §8.1 sample.
    /// Adapters may impose protocol-specific minimums (see §18.6) — for example,
    /// FOCAS2 should not be polled faster than ~3-5 seconds in production.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "PollIntervalMs must be at least 1.")]
    [BundleTier(BundleTier.Include)]
    public int IntervalMs { get; init; } = 1000;

    /// <summary>
    /// Number of consecutive polling failures after which the source is
    /// auto-disabled and marked <see cref="Adapters.AdapterState.Failed"/>.
    /// Default 3 per blueprint §8.1 sample. 0 disables the auto-disable.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "MaxConsecutiveErrors must be non-negative.")]
    [BundleTier(BundleTier.Include)]
    public int MaxConsecutiveErrors { get; init; } = 3;
}
