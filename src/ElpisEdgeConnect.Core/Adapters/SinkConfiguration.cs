// ============================================================================
// File: Adapters/SinkConfiguration.cs
// Purpose: Base class for all sink-adapter configuration objects.
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Base class for a sink adapter's configuration. Protocol-specific
/// configuration records derive from this.
/// </summary>
public abstract record SinkConfiguration
{
    /// <summary>Stable identifier of this sink connector instance.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Display name for the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Protocol module name (e.g., "mqtt"). Must match the adapter's declared protocol.</summary>
    public required string ProtocolName { get; init; }

    /// <summary>Whether this instance is enabled in the running gateway.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Target batch size for published messages.</summary>
    public int BatchSize { get; init; } = 100;

    /// <summary>Maximum time to wait before flushing a partial batch.</summary>
    public int BatchIntervalMs { get; init; } = 250;
}
