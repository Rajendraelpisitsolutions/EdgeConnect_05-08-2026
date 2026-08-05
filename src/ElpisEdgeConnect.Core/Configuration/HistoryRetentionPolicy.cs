// ============================================================================
// File: Configuration/HistoryRetentionPolicy.cs
// Purpose: Configurable retention policy for the history directory.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2 ("History retention: 20
//            most recent kept, older pruned on apply")
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Configurable retention policy for the per-version configuration history.
/// Invoked by <c>ConfigurationManager</c> after every successful apply or
/// rollback. The default keeps the 20 most recent versions per the Phase 1
/// plan B2 specification.
/// </summary>
public sealed class HistoryRetentionPolicy
{
    /// <summary>
    /// Maximum number of history files to retain. Older files are deleted
    /// after each successful apply.
    /// </summary>
    public int MaxRetained { get; }

    /// <summary>
    /// Construct a retention policy with a custom maximum. Default is 20
    /// per the Phase 1 plan.
    /// </summary>
    public HistoryRetentionPolicy(int maxRetained = 20)
    {
        if (maxRetained < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetained),
                maxRetained,
                "Maximum retained history versions must be at least 1.");
        }

        MaxRetained = maxRetained;
    }

    /// <summary>The default policy: keep the 20 most recent applied versions.</summary>
    public static HistoryRetentionPolicy Default { get; } = new(20);
}
