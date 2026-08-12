// ============================================================================
// File: Diagnostics/RouteTap.cs
// Purpose: Default IRouteTap. Demand-driven, per-route capture into bounded
//          rings, strictly observational. The hot-path check (IsTapActive) is
//          an O(1) lock-free read; when no one is watching, capture is a no-op
//          and nothing is allocated (ADR-0017 Rule 1).
// Reference: ADR-0018 (Live Data Tap), ADR-0017 (demand-driven), P1.
//
// Design notes:
//   * No per-route timers — deactivation is clock-based (a route is active
//     while it has a subscriber OR is within its cooldown window). This keeps
//     the hot path branch-cheap AND makes cooldown deterministically testable
//     via an injected clock. Expired routes are reclaimed by Sweep(), called
//     opportunistically on Subscribe/Unsubscribe (and exposed for the host /
//     tests).
//   * Capture NEVER blocks or back-pressures the data path: a full ring evicts
//     its oldest entry silently (P1). Capture never throws.
//   * Value masking (ADR-0018 Rule 6) is applied at capture via the injected
//     masker. M1 ships with no masker (null); M1.5 / ADR-0018A injects the
//     real one. No capture hook is wired into the runtime until that exists.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Diagnostics;

/// <summary>Tunables for <see cref="RouteTap"/>.</summary>
public sealed record RouteTapOptions
{
    /// <summary>Max captures retained per side, per sink. Oldest evicted silently.</summary>
    public int RingCapacity { get; init; } = 1_000;

    /// <summary>
    /// How long capture continues after the last subscriber leaves, so a brief
    /// navigate-away doesn't drop the stream (ADR-0017 Rule 4).
    /// </summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(60);
}

/// <inheritdoc cref="IRouteTap"/>
public sealed class RouteTap : IRouteTap
{
    private readonly RouteTapOptions _options;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<CanonicalDataPoint, CanonicalDataPoint>? _masker;
    private readonly ConcurrentDictionary<string, RouteTapState> _states =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Construct the tap service.
    /// </summary>
    /// <param name="options">Ring/cooldown tunables; defaults when null.</param>
    /// <param name="utcNow">Clock seam (tests inject a controllable clock).</param>
    /// <param name="masker">
    /// Capture-time value masker (ADR-0018 Rule 6). Null = no masking (M1).
    /// M1.5 injects the sensitive-tag masker; no capture hook is wired into the
    /// runtime before then.
    /// </param>
    public RouteTap(
        RouteTapOptions? options = null,
        Func<DateTime>? utcNow = null,
        Func<CanonicalDataPoint, CanonicalDataPoint>? masker = null)
    {
        _options = options ?? new RouteTapOptions();
        if (_options.RingCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), _options.RingCapacity, "RingCapacity must be > 0.");
        }
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _masker = masker;
    }

    /// <inheritdoc/>
    public bool IsTapActive(string routeId)
    {
        if (routeId is null || !_states.TryGetValue(routeId, out var s))
        {
            return false;
        }
        // Fast path: a live subscriber. Lock-free volatile read.
        if (Volatile.Read(ref s.Subscribers) > 0)
        {
            return true;
        }
        // Cooldown path (no subscriber): active until the cooldown deadline.
        return _utcNow().Ticks < Volatile.Read(ref s.DeactivateAtTicks);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        var s = _states.GetOrAdd(routeId, _ => new RouteTapState(_options.RingCapacity));
        lock (s.Lock)
        {
            s.Subscribers++;
            // Cancel any pending cooldown — we're live again.
            Volatile.Write(ref s.DeactivateAtTicks, 0);
        }
        Sweep(); // reclaim any other routes whose cooldown expired
        return new Subscription(this, routeId);
    }

    private void Unsubscribe(string routeId)
    {
        if (!_states.TryGetValue(routeId, out var s))
        {
            return;
        }
        lock (s.Lock)
        {
            if (s.Subscribers > 0)
            {
                s.Subscribers--;
            }
            if (s.Subscribers == 0)
            {
                // Start the cooldown; capture continues until this deadline.
                Volatile.Write(
                    ref s.DeactivateAtTicks,
                    _utcNow().Add(_options.Cooldown).Ticks);
            }
        }
        Sweep();
    }

    /// <inheritdoc/>
    public void CaptureSource(string routeId, IReadOnlyList<CanonicalDataPoint> points)
    {
        if (points is null || points.Count == 0 || !_states.TryGetValue(routeId, out var s))
        {
            return;
        }
        if (!IsActiveState(s))
        {
            return;
        }
        for (var i = 0; i < points.Count; i++)
        {
            var seq = Interlocked.Increment(ref s.CaptureSeq);
            s.Source.Add(BuildCapture(routeId, TapSide.Source, sinkInstanceId: null, points[i], seq));
        }
    }

    /// <inheritdoc/>
    public void CaptureSink(string routeId, string sinkInstanceId, IReadOnlyList<CanonicalDataPoint> points)
    {
        if (points is null || points.Count == 0
            || string.IsNullOrEmpty(sinkInstanceId)
            || !_states.TryGetValue(routeId, out var s))
        {
            return;
        }
        if (!IsActiveState(s))
        {
            return;
        }
        var ring = s.Sinks.GetOrAdd(sinkInstanceId, _ => new BoundedEventLog<TapCapture>(_options.RingCapacity));
        for (var i = 0; i < points.Count; i++)
        {
            var seq = Interlocked.Increment(ref s.CaptureSeq);
            ring.Add(BuildCapture(routeId, TapSide.Sink, sinkInstanceId, points[i], seq));
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<TapCapture> ReadSince(string routeId, long afterSequence)
    {
        if (routeId is null || !_states.TryGetValue(routeId, out var s))
        {
            return Array.Empty<TapCapture>();
        }
        var result = new List<TapCapture>();
        foreach (var c in s.Source.Snapshot())
        {
            if (c.CaptureSequence > afterSequence) { result.Add(c); }
        }
        foreach (var ring in s.Sinks.Values)
        {
            foreach (var c in ring.Snapshot())
            {
                if (c.CaptureSequence > afterSequence) { result.Add(c); }
            }
        }
        result.Sort(static (a, b) => a.CaptureSequence.CompareTo(b.CaptureSequence));
        return result;
    }

    /// <inheritdoc/>
    public RouteTapStatus GetStatus(string routeId)
    {
        if (routeId is null || !_states.TryGetValue(routeId, out var s))
        {
            return RouteTapStatus.Inactive;
        }
        long sinkAdded = 0;
        var truncated = s.Source.TotalDropped > 0;
        foreach (var ring in s.Sinks.Values)
        {
            sinkAdded += ring.TotalAdded;
            if (ring.TotalDropped > 0)
            {
                truncated = true;
            }
        }
        return new RouteTapStatus
        {
            Active = IsTapActive(routeId),
            SourceCaptured = s.Source.TotalAdded,
            SinkCaptured = sinkAdded,
            Truncated = truncated,
        };
    }

    /// <summary>
    /// Reclaim routes whose cooldown has expired (no subscriber, past the
    /// deadline), releasing their capture rings. Idempotent and cheap. Called
    /// on Subscribe/Unsubscribe; exposed so the host can sweep periodically and
    /// tests can drive it deterministically.
    /// </summary>
    public void Sweep()
    {
        var nowTicks = _utcNow().Ticks;
        foreach (var kvp in _states)
        {
            var s = kvp.Value;
            if (Volatile.Read(ref s.Subscribers) > 0)
            {
                continue;
            }
            if (nowTicks < Volatile.Read(ref s.DeactivateAtTicks))
            {
                continue;
            }
            // Compare-and-remove: only drop THIS state, never a fresher one a
            // concurrent Subscribe may have just installed.
            ((ICollection<KeyValuePair<string, RouteTapState>>)_states).Remove(kvp);
        }
    }

    private bool IsActiveState(RouteTapState s) =>
        Volatile.Read(ref s.Subscribers) > 0
        || _utcNow().Ticks < Volatile.Read(ref s.DeactivateAtTicks);

    private TapCapture BuildCapture(
        string routeId, TapSide side, string? sinkInstanceId, CanonicalDataPoint point, long seq)
    {
        var masked = _masker is null ? point : _masker(point);
        return new TapCapture
        {
            RouteId = routeId,
            Side = side,
            SinkInstanceId = sinkInstanceId,
            Point = masked,
            CorrelationId = Correlate(routeId, point),
            CaptureSequence = seq,
            CapturedAtUtc = _utcNow(),
        };
    }

    // Identity-only fields → masking (which only touches the value) does not
    // affect correlation. The sequenceNumber tie-breaker disambiguates points
    // that share a deviceTimestamp under poll-based bursts.
    private static string Correlate(string routeId, CanonicalDataPoint p) =>
        string.Concat(
            p.GatewayId, "|", routeId, "|", p.SourceInstanceId, "|", p.TagName, "|",
            p.DeviceTimestamp.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "|",
            p.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private sealed class RouteTapState
    {
        public readonly object Lock = new();
        public int Subscribers;          // writes under Lock; hot-path reads via Volatile
        public long DeactivateAtTicks;   // 0 = no cooldown pending; via Volatile
        public long CaptureSeq;          // via Interlocked
        public readonly BoundedEventLog<TapCapture> Source;
        public readonly ConcurrentDictionary<string, BoundedEventLog<TapCapture>> Sinks =
            new(StringComparer.Ordinal);

        public RouteTapState(int ringCapacity)
        {
            Source = new BoundedEventLog<TapCapture>(ringCapacity);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly RouteTap _owner;
        private readonly string _routeId;
        private int _disposed;

        public Subscription(RouteTap owner, string routeId)
        {
            _owner = owner;
            _routeId = routeId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Unsubscribe(_routeId);
            }
        }
    }
}
