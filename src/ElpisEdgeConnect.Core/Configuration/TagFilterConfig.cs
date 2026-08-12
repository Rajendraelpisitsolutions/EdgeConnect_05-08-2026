// ============================================================================
// File: Configuration/TagFilterConfig.cs
// Purpose: Tag filter configuration for a route.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4
// Milestone: B1
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Include / exclude glob patterns deciding which tags from the source flow
/// through the route. Per blueprint §4.4, the default <see cref="Include"/>
/// is <c>["*"]</c> — when no filter is specified, every tag from the source
/// passes through.
/// </summary>
/// <remarks>
/// Pattern syntax is glob: <c>*</c> matches any sequence, <c>?</c> matches
/// a single character. Case-insensitive matching is the routing engine's
/// responsibility (Phase 1 C3); B1 only validates that the lists are
/// well-formed and non-null entries.
/// </remarks>
public sealed record TagFilterConfig
{
    /// <summary>
    /// Glob patterns of tags to include. Default <c>["*"]</c> per blueprint §4.4
    /// — every tag from the source is included unless overridden.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<string> Include { get; init; } = ["*"];

    /// <summary>
    /// Glob patterns of tags to exclude. Applied after <see cref="Include"/>.
    /// Optional; null means no exclusions.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public IReadOnlyList<string>? Exclude { get; init; }
}
