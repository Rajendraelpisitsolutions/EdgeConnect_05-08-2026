// ============================================================================
// File: Diagnostics/IRouteTap.cs
// Purpose: The demand-driven Live Data Tap capture service (ADR-0018), owned by
//          Core because the capture points sit on the runtime data path
//          (RouteWorker intake + sink publish). Strictly observational (P1):
//          subscribers READ; nothing in the runtime reads from subscribers, and
//          capture never blocks or back-pressures the data path.
// Reference: ADR-0018 (Live Data Tap), ADR-0017 (demand-driven activation), P1.
//
// Activation contract (ADR-0017):
//   * Off by default. IsTapActive is the ONLY cost on the data path when no one
//     is watching — an O(1) lock-free read. When it returns false the runtime
//     does no capture work at all.
//   * Activated by subscriber presence, reference-counted, per-route (tapping
//     route A never captures route B), with a cooldown so brief navigate-away
//     doesn't drop the capture.
//   * Bounded capture: per-side / per-sink rings evict oldest silently.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>
/// Demand-driven Live Data Tap capture service. See <see cref="RouteTap"/>.
/// </summary>
public interface IRouteTap
{
    /// <summary>
    /// O(1), lock-free check the runtime data path makes once per batch before
    /// capturing. <c>false</c> means no subscriber is watching this route (and
    /// no cooldown is pending) — the caller must skip all capture work.
    /// </summary>
    bool IsTapActive(string routeId);

    /// <summary>
    /// Register a subscriber (typically the Studio tap page / SSE stream) for a
    /// route. Activates capture for THAT route only. Reference-counted; dispose
    /// the returned token to unsubscribe. After the last subscriber disposes,
    /// capture continues for a cooldown window, then deactivates and releases
    /// the route's capture rings.
    /// </summary>
    IDisposable Subscribe(string routeId);

    /// <summary>
    /// Capture source-side points (route intake, post-filter / pre-transform).
    /// No-op when the route is not active. Never throws, never blocks.
    /// </summary>
    void CaptureSource(string routeId, IReadOnlyList<CanonicalDataPoint> points);

    /// <summary>
    /// Capture the batch handed to a sink's <c>PublishAsync</c>, per sink.
    /// No-op when the route is not active. Never throws, never blocks.
    /// </summary>
    void CaptureSink(string routeId, string sinkInstanceId, IReadOnlyList<CanonicalDataPoint> points);

    /// <summary>
    /// Read captures (both sides, all sinks) for a route with
    /// <see cref="TapCapture.CaptureSequence"/> strictly greater than
    /// <paramref name="afterSequence"/>, in capture order. Pass <c>0</c> for the
    /// first read. Used by the SSE stream. Returns empty when the route is
    /// unknown / inactive.
    /// </summary>
    IReadOnlyList<TapCapture> ReadSince(string routeId, long afterSequence);

    /// <summary>
    /// Lightweight capture status for the Stream banner. Returns
    /// <see cref="RouteTapStatus.Inactive"/> when the route is unknown.
    /// </summary>
    RouteTapStatus GetStatus(string routeId);
}

/// <summary>
/// A snapshot of a route's tap capture state for the operator banner.
/// </summary>
public sealed record RouteTapStatus
{
    /// <summary>True while a subscriber is watching (or within cooldown).</summary>
    public required bool Active { get; init; }

    /// <summary>Lifetime source-side captures since this tap activated.</summary>
    public required long SourceCaptured { get; init; }

    /// <summary>Lifetime sink-side captures (all sinks) since this tap activated.</summary>
    public required long SinkCaptured { get; init; }

    /// <summary>
    /// True when at least one capture ring has evicted entries — the operator is
    /// viewing a bounded RECENT sample, not every captured point. Drives the
    /// "showing most recent — older captures evicted" banner. (True rate-based
    /// reservoir sampling for OPC-UA-scale rates is a deferred enhancement; at
    /// poll-cadence launch rates the ring rarely truncates.)
    /// </summary>
    public required bool Truncated { get; init; }

    /// <summary>The status for an unknown / inactive route.</summary>
    public static RouteTapStatus Inactive { get; } = new()
    {
        Active = false,
        SourceCaptured = 0,
        SinkCaptured = 0,
        Truncated = false,
    };
}
