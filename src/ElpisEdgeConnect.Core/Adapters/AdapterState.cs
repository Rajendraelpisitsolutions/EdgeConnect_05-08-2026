// ============================================================================
// File: Adapters/AdapterState.cs
// Purpose: Lifecycle state of a source or sink adapter instance.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 19.9 (also applies to adapters)
//
// This is a simplified view focused on adapter-level state, separate from
// RouteState which is the broader route execution lifecycle. Every adapter
// moves through this state machine; the transitions allowed are enforced by
// AdapterStateTransitions below.
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Lifecycle state of a source or sink adapter instance.
/// </summary>
public enum AdapterState
{
    /// <summary>Instantiated but not yet initialized.</summary>
    Created = 0,

    /// <summary>Running <c>InitializeAsync</c>.</summary>
    Initializing = 1,

    /// <summary>Initialized successfully; ready to start.</summary>
    Initialized = 2,

    /// <summary>Running <c>StartAsync</c>.</summary>
    Starting = 3,

    /// <summary>Running normally.</summary>
    Running = 4,

    /// <summary>Running but experiencing transient failures; retries in progress.</summary>
    Degraded = 5,

    /// <summary>Running <c>StopAsync</c>.</summary>
    Stopping = 6,

    /// <summary>Stopped cleanly; can be restarted.</summary>
    Stopped = 7,

    /// <summary>Unrecoverable failure; requires user intervention.</summary>
    Failed = 8,

    /// <summary>Blocked by licensing or config policy; not activated.</summary>
    Blocked = 9,
}
