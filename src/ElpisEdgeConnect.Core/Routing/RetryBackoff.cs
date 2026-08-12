// ============================================================================
// File: Routing/RetryBackoff.cs
// Purpose: Pure exponential-backoff-with-jitter calculator used by the sink
//          retry loop. Kept as a static pure function so tests can pin the
//          doubling, capping, and jitter-range properties without touching
//          Random.
// Reference: ARCHITECTURE_BLUEPRINT.md §19.4 (Retry & Backoff)
// Milestone: C3 Commit 2 (phase 3 — retry / delivery modes).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Pure exponential-backoff-with-jitter calculation. Isolated from
/// <see cref="Random"/> so the doubling, cap, and jitter-range properties
/// can be unit-pinned deterministically.
/// </summary>
public static class RetryBackoff
{
    /// <summary>
    /// Compute the backoff interval for a given 1-indexed retry attempt.
    /// </summary>
    /// <param name="attempt">
    /// 1-indexed retry number. Attempt 1 is the first retry after the first
    /// failure; attempt 2 is the second, etc. Values &lt; 1 are treated as 1.
    /// </param>
    /// <param name="policy">Delivery policy whose backoff knobs are used.</param>
    /// <param name="jitterSample">
    /// A uniform sample in [0,1]. Scaled to [-1,+1] and multiplied by the
    /// configured jitter fraction. Tests pass fixed values (0, 0.5, 1) to
    /// pin the envelope.
    /// </param>
    /// <returns>The delay to sleep before the retry.</returns>
    public static TimeSpan Compute(int attempt, DeliveryPolicyConfig policy, double jitterSample)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (jitterSample < 0.0 || jitterSample > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterSample));
        }

        var safeAttempt = Math.Max(1, attempt);
        var exponent = safeAttempt - 1;
        var raw = policy.InitialBackoffMs * Math.Pow(policy.BackoffMultiplier, exponent);
        var capped = Math.Min(raw, policy.MaxBackoffMs);
        var jitterFraction = policy.JitterPercent / 100.0;
        // Map [0,1] → [-1,+1] so 0.5 yields no jitter.
        var jitterMultiplier = 1.0 + ((jitterSample * 2.0) - 1.0) * jitterFraction;
        var finalMs = capped * jitterMultiplier;
        return TimeSpan.FromMilliseconds(Math.Max(0, finalMs));
    }
}
