// ============================================================================
// File: Contracts/DiagnosticsEventsResponse.cs
// Purpose: Wrapper-shape returned by /api/v1/routes/{id}/events and
//          /api/v1/diagnostics/events. Wraps the event list with
//          retention metadata so operators can answer "am I seeing
//          the full picture, or did older events rotate out?"
//
//          Shipping an envelope from day one (instead of a bare array)
//          gives us a clean expansion path for cursor-based pagination
//          when single-response payloads outgrow useful size, without
//          breaking existing API consumers.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.2
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Management.Contracts;

/// <summary>
/// Wrapped response from the diagnostics-event endpoints. Carries the
/// list of matching events plus retention metadata that lets operators
/// distinguish "seeing the complete window" from "newer events shown,
/// older ones rotated out of the ring buffer."
/// </summary>
public sealed record DiagnosticsEventsResponse
{
    /// <summary>
    /// Events matching the filter (server-side filtered, server-side sorted
    /// descending by <c>OccurredAtUtc</c>, server-side capped at the
    /// requested limit).
    /// </summary>
    public required IReadOnlyList<DiagnosticsEventDto> Events { get; init; }

    /// <summary>
    /// Earliest timestamp in the underlying event ring-buffer at query time.
    /// Answers "am I seeing complete history?" — if this is older than the
    /// query's <c>since</c> filter, no events were rotated out within the
    /// requested window. Null when the underlying buffer is empty or has
    /// not been observed yet.
    /// </summary>
    public DateTime? RetainedSinceUtc { get; init; }

    /// <summary>
    /// Approximate total events the aggregator scanned <em>before</em>
    /// applying the filter + limit cap. Lets operators tell "I'm seeing
    /// all 50" from "I'm seeing 50 of ~12,000 — older events rotated out
    /// or were filtered." Approximate because aggregation runs without
    /// holding locks across ring buffers; consistent within ±buffer-churn.
    /// </summary>
    public long? ApproximateTotalEvents { get; init; }
}
