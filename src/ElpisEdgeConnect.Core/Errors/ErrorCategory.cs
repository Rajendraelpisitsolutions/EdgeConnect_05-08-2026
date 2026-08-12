// ============================================================================
// File: Errors/ErrorCategory.cs
// Purpose: Error classification for the adapter error taxonomy.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 13.3
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// High-level classification for adapter and runtime errors. Drives retry
/// behavior and diagnostic rollups.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Bad configuration — the user must fix it. Not retryable.</summary>
    Configuration = 0,

    /// <summary>Bad credentials or missing auth. Retryable only after user intervention.</summary>
    Authentication = 1,

    /// <summary>Transient network issue (timeout, connection reset). Retryable.</summary>
    Network = 2,

    /// <summary>Protocol-level error (bad framing, unexpected response). Usually retryable with caution.</summary>
    Protocol = 3,

    /// <summary>Device in wrong state (powered off, in alarm). Retryable after device recovery.</summary>
    DeviceState = 4,

    /// <summary>Too many handles, rate-limited, slot exhaustion. Retryable with backoff.</summary>
    ResourceExhausted = 5,

    /// <summary>Blocked by licensing. Not retryable until license is updated.</summary>
    License = 6,

    /// <summary>Bug in the adapter or runtime. Not normally retryable; must be reported.</summary>
    Internal = 7,
}
