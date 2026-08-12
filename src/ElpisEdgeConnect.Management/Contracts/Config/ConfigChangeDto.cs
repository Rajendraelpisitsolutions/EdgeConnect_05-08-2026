// ============================================================================
// File: Contracts/Config/ConfigChangeDto.cs
// Purpose: Wire-shape mirror of Core's ConfigurationChange for the
//          /diff endpoint and the changes[] array on ApplyResultDto.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.2a
// ============================================================================

namespace ElpisEdgeConnect.Management.Contracts.Config;

/// <summary>
/// One entry in a structured configuration diff. Pass-through shape of
/// <c>Core.Configuration.ConfigurationChange</c>; stable across Core
/// internal refactors.
/// </summary>
public sealed record ConfigChangeDto
{
    /// <summary>What kind of change occurred — <c>"Added"</c> / <c>"Removed"</c> / <c>"Modified"</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>What entity was changed — <c>"GatewaySettings"</c> / <c>"Source"</c> / <c>"Sink"</c> / <c>"Route"</c>.</summary>
    public required string EntityKind { get; init; }

    /// <summary>The entity's identifier (instance id, route id, or <c>"Gateway"</c> for the top-level block).</summary>
    public required string EntityId { get; init; }

    /// <summary>
    /// Optional dotted path to a specific changed field within the entity
    /// (e.g. <c>"Polling.IntervalMs"</c>). Null when the change is at the
    /// entity level — the whole entity was added or removed.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>Human-readable description of the change.</summary>
    public required string Summary { get; init; }
}
