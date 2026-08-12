// ============================================================================
// File: Configuration/ConfigurationChange.cs
// Purpose: Immutable record describing a single diff entry between two
//          GatewayConfiguration instances.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2 (ConfigurationDiffer)
// ============================================================================

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// What kind of change occurred in a configuration diff.
/// </summary>
public enum ConfigurationChangeKind
{
    /// <summary>An entity was added in the new config.</summary>
    Added = 0,

    /// <summary>An entity that was in the old config is gone in the new one.</summary>
    Removed = 1,

    /// <summary>An entity exists in both configs but its content differs.</summary>
    Modified = 2,
}

/// <summary>
/// What kind of entity was changed.
/// </summary>
public enum ConfigurationEntityKind
{
    /// <summary>The top-level <see cref="GatewaySettings"/> block.</summary>
    GatewaySettings = 0,

    /// <summary>A <see cref="SourceInstanceConfig"/>.</summary>
    Source = 1,

    /// <summary>A <see cref="SinkInstanceConfig"/>.</summary>
    Sink = 2,

    /// <summary>A <see cref="RouteConfig"/>.</summary>
    Route = 3,
}

/// <summary>
/// One entry in a structured configuration diff. Produced by
/// <see cref="ConfigurationDiffer"/>; consumed by the audit log and the
/// future admin UI's diff view.
/// </summary>
public sealed record ConfigurationChange
{
    /// <summary>What kind of change.</summary>
    public required ConfigurationChangeKind Kind { get; init; }

    /// <summary>What kind of entity was changed.</summary>
    public required ConfigurationEntityKind EntityKind { get; init; }

    /// <summary>
    /// The entity's identifier (e.g., <c>"focas-jyoti17"</c> for a source,
    /// <c>"main-eremos-route"</c> for a route, <c>"Gateway"</c> for the
    /// top-level settings block).
    /// </summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Optional dotted path to a specific changed field within the entity
    /// (e.g., <c>"Polling.IntervalMs"</c>). Null when the change is at the
    /// entity level (the whole entity was added or removed).
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Human-readable description of the change suitable for inclusion in
    /// audit summaries and admin UI lists.
    /// </summary>
    public required string Summary { get; init; }
}
