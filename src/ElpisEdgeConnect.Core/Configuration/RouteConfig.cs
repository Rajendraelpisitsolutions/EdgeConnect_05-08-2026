// ============================================================================
// File: Configuration/RouteConfig.cs
// Purpose: JSON DTO for a route — the primary product primitive per blueprint.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §19 (Route Execution Semantics)
// Milestone: B1
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// JSON DTO for a single route — a named data flow from one source to one
/// or more sinks, optionally with transforms and a buffer policy.
/// </summary>
/// <remarks>
/// Routes are the primary product primitive per blueprint §3 design principles.
/// At runtime (Phase 1 C3) the routing engine translates this DTO into a
/// <c>Route</c> runtime type that drives the per-route worker, fanout
/// dispatcher, and per-sink publishers per blueprint §19.
/// </remarks>
public sealed record RouteConfig
{
    /// <summary>
    /// Stable identifier for this route (e.g., <c>"jyoti17-to-eremos"</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [RegularExpression(
        "^[A-Za-z0-9][A-Za-z0-9._-]*$",
        ErrorMessage = "RouteId must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.")]
    [BundleTier(BundleTier.Include)]
    public required string RouteId { get; init; }

    /// <summary>Human-readable name for the route, shown in the admin UI.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256, MinimumLength = 1)]
    [BundleTier(BundleTier.Include)]
    public required string Name { get; init; }

    /// <summary>
    /// <see cref="SourceInstanceConfig.InstanceId"/> of the source feeding
    /// this route. Validation that this id resolves to a real configured
    /// source happens in B2 (cross-record validation).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(128, MinimumLength = 1)]
    [BundleTier(BundleTier.Include)]
    public required string SourceInstanceId { get; init; }

    /// <summary>
    /// Tag include / exclude filter. Default includes everything per blueprint §4.4.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public TagFilterConfig Filter { get; init; } = new();

    /// <summary>
    /// Optional transform pipeline configuration. When null, the route is
    /// identity (points pass through unchanged after filtering).
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public TransformProfileConfig? Transforms { get; init; }

    /// <summary>
    /// One or more sink instance ids that receive points from this route.
    /// Per blueprint §3 design principles, a single source can fan out to
    /// many sinks via a single route. The list must be non-empty; B2's
    /// validator enforces the non-empty constraint at apply time.
    /// </summary>
    [Required]
    [BundleTier(BundleTier.Include)]
    public required IReadOnlyList<string> SinkInstanceIds { get; init; }

    /// <summary>
    /// Per-route buffer policy. Default supplies sensible production values
    /// (StoreAndForward, 10k depth, 7 day retention, drop oldest on overflow).
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public BufferPolicyConfig Buffer { get; init; } = new();

    /// <summary>
    /// Per-route delivery policy. Default is AtLeastOnce with 5 retries and
    /// parallel fanout.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public DeliveryPolicyConfig Delivery { get; init; } = new();

    /// <summary>
    /// True if this route is enabled in the running gateway. Default <c>true</c>.
    /// </summary>
    [BundleTier(BundleTier.Include)]
    public bool Enabled { get; init; } = true;
}
