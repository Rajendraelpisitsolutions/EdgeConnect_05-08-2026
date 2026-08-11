// ============================================================================
// File: Configuration/TransformProfileConfig.cs
// Purpose: Transform pipeline configuration for a route.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §5 (Transform Pipeline)
// Milestone: B1 (model only — pipeline implementation lands in C1)
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Configuration for the transform pipeline applied by a route. All sub-blocks
/// are optional; an entirely null transform profile means the route is identity
/// (points pass through unchanged).
/// </summary>
/// <remarks>
/// Per blueprint §5, the transform pipeline runs in a fixed order:
/// tag mapping → filter → deadband → rate limit → enrichment → unit conversion → ...
/// In B1 only the four Phase 1 steps are configurable; later steps will
/// extend this record without breaking existing config files.
/// </remarks>
public sealed record TransformProfileConfig
{
    /// <summary>
    /// Source-tag-name → canonical-tag-name mapping. Applied first in the
    /// pipeline. Tags not in this map pass through with their original name.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyDictionary<string, string>? TagMapping { get; init; }

    /// <summary>
    /// Per-tag absolute deadband threshold. The routing engine suppresses
    /// subsequent readings of a tag whose value has not changed by at least
    /// the threshold. Interpreted as an absolute change in the numeric value.
    /// A tag may not appear in both <see cref="Deadband"/> and
    /// <see cref="DeadbandPercent"/>; cross-record validation enforces this.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyDictionary<string, double>? Deadband { get; init; }

    /// <summary>
    /// Per-tag percentage deadband threshold, expressed as a fraction in
    /// <c>(0, 1]</c> (e.g., <c>0.05</c> = 5%). The step passes a new reading
    /// only when <c>|current - last| / max(|last|, epsilon) ≥ threshold</c>.
    /// A tag may not appear in both <see cref="Deadband"/> and this map.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyDictionary<string, double>? DeadbandPercent { get; init; }

    /// <summary>
    /// Per-tag minimum interval (in milliseconds) between published values.
    /// Used to cap publish frequency on noisy or high-rate tags. Values must
    /// be strictly positive; cross-record validation enforces this.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyDictionary<string, int>? RateLimitMs { get; init; }

    /// <summary>
    /// Static metadata to attach to every point flowing through the route
    /// (e.g., site, line, shift). In C1 this field is <b>intentionally dormant</b>:
    /// it is accepted, validated, and persisted by the configuration subsystem,
    /// but the transform pipeline does not yet include an enrichment step.
    /// A later milestone (C1.5 or C3) will activate it. Until then, values in
    /// this map have <b>no runtime effect</b>.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyDictionary<string, object>? EnrichmentTags { get; init; }
}
