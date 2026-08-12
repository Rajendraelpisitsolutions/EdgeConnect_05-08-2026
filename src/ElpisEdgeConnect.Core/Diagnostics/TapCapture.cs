// ============================================================================
// File: Diagnostics/TapCapture.cs
// Purpose: One captured point in the Live Data Tap (ADR-0018). Carries the
//          (already privacy-masked) canonical point plus the side it was
//          captured on, the sink id (sink side only), a stable correlation id
//          for Compare-mode joins, a monotonic per-route capture sequence (so
//          an SSE reader can resume with "give me captures after N"), and the
//          capture timestamp.
// Reference: ADR-0018 (Live Data Tap), ADR-0017 (demand-driven), P1.
//
// NOTE: a TapCapture only exists while a route is actively tapped (a subscriber
// is watching). At idle, nothing is captured and no TapCapture is allocated
// (ADR-0017 Rule 1).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>Which end of the route a <see cref="TapCapture"/> was taken on.</summary>
public enum TapSide
{
    /// <summary>Captured at route intake — post-filter, PRE-transform.</summary>
    Source = 0,

    /// <summary>Captured at the batch handed to a sink's <c>PublishAsync</c>.</summary>
    Sink = 1,
}

/// <summary>
/// A single captured point in the Live Data Tap. The <see cref="Point"/> has
/// already had any sensitive value masked at capture time (ADR-0018 Rule 6) —
/// a <c>TapCapture</c> never holds cleartext sensitive data.
/// </summary>
public sealed record TapCapture
{
    /// <summary>The route this capture belongs to.</summary>
    public required string RouteId { get; init; }

    /// <summary>Which end of the route the point was captured on.</summary>
    public required TapSide Side { get; init; }

    /// <summary>
    /// The sink instance id for a <see cref="TapSide.Sink"/> capture; <c>null</c>
    /// for a source capture. Distinguishes sinks on a fan-out route.
    /// </summary>
    public string? SinkInstanceId { get; init; }

    /// <summary>
    /// The captured canonical point, with sensitive values already masked.
    /// </summary>
    public required CanonicalDataPoint Point { get; init; }

    /// <summary>
    /// Stable join key for Compare mode:
    /// <c>gatewayId|routeId|sourceInstanceId|tagName|deviceTimestampTicks|sequenceNumber</c>.
    /// Identical on the source-side and sink-side capture of the same logical
    /// point, so the comparator can pair them. The <c>sequenceNumber</c>
    /// tie-breaker prevents mis-pairing when a poll-based adapter emits several
    /// points sharing one device timestamp.
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// Monotonic per-route capture sequence (across both sides + all sinks),
    /// assigned in capture order. An SSE reader resumes via "captures with
    /// sequence &gt; my cursor".
    /// </summary>
    public required long CaptureSequence { get; init; }

    /// <summary>Gateway UTC time the capture was taken.</summary>
    public required DateTime CapturedAtUtc { get; init; }
}
