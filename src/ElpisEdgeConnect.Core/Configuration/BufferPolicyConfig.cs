// ============================================================================
// File: Configuration/BufferPolicyConfig.cs
// Purpose: Per-route buffer policy configuration.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §6, §19.3, §19.8
// Milestone: B1
//
// NOTE on MaxAgeDays vs blueprint §4.4 TimeSpan MaxAge:
//   B1 uses MaxAgeDays (int) to match the blueprint §8.1 sample shape and
//   to keep config files human-friendly. The runtime types in C3 will map
//   this to a TimeSpan internally. This deviation from §4.4's signature is
//   tracked as a blueprint/sample clarification item — the §8.1 sample is
//   the authoritative shape for B1.
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Per-route buffer policy controlling buffering mode, capacity, retention,
/// and overflow behaviour. Per blueprint §4.4 and §6, every route has its own
/// buffer policy; per-route storage is per-blueprint §19.3 with per-sink
/// cursors.
/// </summary>
public sealed record BufferPolicyConfig
{
    /// <summary>
    /// Buffering mode for the route. Default <see cref="BufferMode.StoreAndForward"/>
    /// — production routes are durable by default per blueprint §6.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public BufferMode Mode { get; init; } = BufferMode.StoreAndForward;

    /// <summary>
    /// Maximum number of points the buffer holds before <see cref="OnOverflow"/>
    /// applies. Default 10,000 per blueprint §8.1 sample. 0 disables the depth cap.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "MaxDepth must be non-negative.")]
    [BundleTier(BundleTier.Include)]
    public int MaxDepth { get; init; } = 10_000;

    /// <summary>
    /// Maximum age in days for buffered points. Older points are evicted.
    /// Default 7 days per blueprint §8.1 sample. 0 disables the age cap.
    /// </summary>
    /// <remarks>
    /// B1 uses an integer-day representation rather than the
    /// <see cref="System.TimeSpan"/> from blueprint §4.4 to match the §8.1
    /// sample shape (<c>"MaxAgeDays": 7</c>). The runtime types in C3 will
    /// translate this to a <see cref="System.TimeSpan"/> internally.
    /// </remarks>
    [Range(0, 3650, ErrorMessage = "MaxAgeDays must be between 0 and 3650.")]
    [BundleTier(BundleTier.Include)]
    public int MaxAgeDays { get; init; } = 7;

    /// <summary>
    /// Behaviour when the buffer hits its capacity ceiling. Default
    /// <see cref="DropPolicy.DropOldest"/> — preserves freshness.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public DropPolicy OnOverflow { get; init; } = DropPolicy.DropOldest;
}
