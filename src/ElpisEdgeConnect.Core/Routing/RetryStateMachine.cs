// ============================================================================
// File: Routing/RetryStateMachine.cs
// Purpose: Per-sink retry state: current attempt counter and the backoff it
//          should sleep for on the next retry. Explicit and small — the
//          routing engine never infers retry state from timers or timestamps.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.4
// Milestone: C3 Commit 2 (phase 3).
//
// Scope: Retry ONLY. Does not track persistent degraded state across route
//        restarts — that belongs to lifecycle (phase 5). A route restart
//        disposes the publisher and recreates this machine from zero, which
//        is exactly the semantics the phase 3 tests pin.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Tracks the 1-indexed retry attempt counter for a single sink publisher.
/// Successful publish resets the counter; each failure increments it and
/// returns the next backoff.
/// </summary>
public sealed class RetryStateMachine
{
    private readonly DeliveryPolicyConfig _policy;
    private readonly Func<double> _jitterSampler;
    private int _attempt;

    /// <summary>
    /// Create a retry state machine for the given delivery policy. The
    /// optional <paramref name="jitterSampler"/> must return values in
    /// <c>[0,1]</c>; <see cref="Random.Shared"/> is used by default.
    /// </summary>
    public RetryStateMachine(DeliveryPolicyConfig policy, Func<double>? jitterSampler = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
        _jitterSampler = jitterSampler ?? (() => Random.Shared.NextDouble());
    }

    /// <summary>Current 1-indexed attempt count. 0 means "no retry in flight".</summary>
    public int Attempt => _attempt;

    /// <summary>
    /// True when another retry is allowed under the configured
    /// <see cref="DeliveryPolicyConfig.MaxRetries"/>.
    /// </summary>
    public bool CanRetry => _attempt < _policy.MaxRetries;

    /// <summary>Record a failure and return the delay before the next attempt.</summary>
    public TimeSpan RecordFailureAndComputeBackoff()
    {
        _attempt++;
        return RetryBackoff.Compute(_attempt, _policy, _jitterSampler());
    }

    /// <summary>Record a successful publish. Resets the counter to 0.</summary>
    public void RecordSuccess() => _attempt = 0;

    /// <summary>Reset the machine to its initial state.</summary>
    public void Reset() => _attempt = 0;
}
