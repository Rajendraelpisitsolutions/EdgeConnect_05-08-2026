// ============================================================================
// File: Pipeline/TransformStepKind.cs
// Purpose: Stable wire identifiers for the built-in transform step kinds.
//          The enum ordering defines the FIXED execution order per
//          blueprint §5 (tag mapping → filter → deadband → rate limit).
// Reference: ARCHITECTURE_BLUEPRINT.md §5, PHASE1_EXECUTION_PLAN.md C1
// ============================================================================

namespace ElpisEdgeConnect.Core.Pipeline;

/// <summary>
/// Identifier of a built-in transform step. New kinds are appended; the
/// numeric values are stable across versions so persisted configurations
/// remain readable.
/// </summary>
/// <remarks>
/// The numeric ordering also defines the fixed pipeline execution order
/// (locked by blueprint §5). Users cannot reorder the steps at runtime; they
/// can only control which are present via <c>TransformProfileConfig</c>.
/// </remarks>
public enum TransformStepKind
{
    /// <summary>Rename source tags to canonical names.</summary>
    TagMapping = 1,

    /// <summary>Include / exclude tags by glob pattern.</summary>
    Filter = 2,

    /// <summary>Suppress readings whose value has not changed by the threshold.</summary>
    Deadband = 3,

    /// <summary>Cap per-tag publish frequency by minimum interval.</summary>
    RateLimit = 4,
}
