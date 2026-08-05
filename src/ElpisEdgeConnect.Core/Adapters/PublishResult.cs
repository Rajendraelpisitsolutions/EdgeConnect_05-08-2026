// ============================================================================
// File: Adapters/PublishResult.cs
// Purpose: Per-batch result returned by sink adapters.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.3, Section 19.4
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Adapters;

/// <summary>
/// Result of a single <see cref="ISinkAdapter.PublishAsync"/> call. The
/// routing engine uses this to advance the per-sink cursor and drive retry
/// behavior.
/// </summary>
/// <remarks>
/// Per Section 19.4, retry is tracked per-batch, not per-point. If a sink
/// partially accepts a batch, it must handle the internal bookkeeping itself
/// and report totals via <see cref="AcceptedCount"/> and
/// <see cref="RejectedCount"/>. The routing engine only cares about
/// <see cref="Success"/> for cursor advancement.
/// </remarks>
public sealed record PublishResult
{
    /// <summary>True if the entire batch was accepted by the sink.</summary>
    public bool Success { get; init; }

    /// <summary>Number of points the sink accepted.</summary>
    public int AcceptedCount { get; init; }

    /// <summary>Number of points the sink rejected (e.g., malformed, filtered).</summary>
    public int RejectedCount { get; init; }

    /// <summary>Error details if <see cref="Success"/> is false.</summary>
    public AdapterError? Error { get; init; }

    /// <summary>Wall-clock latency of the publish call.</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>Convenience: successful publish of all points in the batch.</summary>
    public static PublishResult Successful(int count, TimeSpan latency) =>
        new()
        {
            Success = true,
            AcceptedCount = count,
            RejectedCount = 0,
            Latency = latency,
        };

    /// <summary>
    /// Convenience: failed publish with an error. Per the contract documented
    /// on <see cref="Error"/>, a failed result must carry a non-null error;
    /// passing <c>null</c> for <paramref name="error"/> would silently produce
    /// an internally inconsistent result and is rejected.
    /// </summary>
    /// <param name="error">
    /// Structured error describing the failure. Must not be null.
    /// </param>
    /// <param name="latency">Wall-clock latency of the failed publish call.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="error"/> is null.
    /// </exception>
    public static PublishResult Failed(AdapterError error, TimeSpan latency)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new PublishResult
        {
            Success = false,
            AcceptedCount = 0,
            RejectedCount = 0,
            Error = error,
            Latency = latency,
        };
    }
}
