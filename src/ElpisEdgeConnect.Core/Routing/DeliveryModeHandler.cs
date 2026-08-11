// ============================================================================
// File: Routing/DeliveryModeHandler.cs
// Purpose: Explicit mapping from (DeliveryMode, publish failure) to the
//          cursor-advancement decision the sink publisher should take.
//          Small and deliberately not "smart" — the whole point of keeping
//          this as a switch is to keep the semantics visible at the call site.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.7 (Delivery Guarantees)
// Milestone: C3 Commit 2 (phase 3).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Outcome of a single publish attempt, from the sink publisher's perspective.
/// Drives cursor advancement and retry scheduling.
/// </summary>
public enum PublishOutcome
{
    /// <summary>Publish succeeded. Advance cursor past the batch.</summary>
    Acked,

    /// <summary>
    /// Publish failed and <see cref="DeliveryMode.AtMostOnce"/> says drop.
    /// Advance cursor past the batch without delivering.
    /// </summary>
    DroppedAndAdvanced,

    /// <summary>
    /// Publish failed, retries still available under the policy. Sleep for
    /// the backoff, then re-attempt the same batch. Cursor stays put.
    /// </summary>
    RetryScheduled,

    /// <summary>
    /// Publish failed and the retry budget is exhausted. Cursor stays put;
    /// the sink publisher breaks back to its wake loop and will re-attempt
    /// the same batch on the next signal (with a fresh retry budget). Phase 4
    /// replaces this with coordinated replay.
    /// </summary>
    RetriesExhausted,
}

/// <summary>
/// Pure decision helper for the sink publisher. Given the current delivery
/// mode and retry state, return the outcome.
/// </summary>
public static class DeliveryModeHandler
{
    /// <summary>
    /// Called after a failed publish to decide the next step. The caller is
    /// responsible for executing the returned action.
    /// </summary>
    public static PublishOutcome HandleFailure(DeliveryMode mode, RetryStateMachine retry)
    {
        ArgumentNullException.ThrowIfNull(retry);

        return mode switch
        {
            // At-most-once: failure is terminal for this batch. Drop and
            // advance the cursor so the sink keeps moving with fresh data.
            DeliveryMode.AtMostOnce => PublishOutcome.DroppedAndAdvanced,

            // At-least-once: retry until the budget is exhausted.
            DeliveryMode.AtLeastOnce => retry.CanRetry
                ? PublishOutcome.RetryScheduled
                : PublishOutcome.RetriesExhausted,

            // ExactlyOnce is deliberately absent from the enum by blueprint
            // §19.7. If it ever appears here at runtime the type system has
            // been compromised — fail loudly.
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported delivery mode. ExactlyOnce is not supported in v1."),
        };
    }
}
