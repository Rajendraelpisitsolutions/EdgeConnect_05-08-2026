// ============================================================================
// File: Routing/ReplayRouteDriver.cs
// Purpose: The single serialized driver for a replay-aware route's sink (K1.3 slices 3-4).
//          One task owns ALL replay-aware sink lifecycle + publish calls — Begin, the
//          phase-tagged Publish loop across the two-watermark boundaries (H the birth
//          cutoff, C the catch-up cutoff), CompleteCatchUp into Live, and same-generation
//          operational Rebirth. Selected by RouteWorker for a replay route instead of the
//          legacy per-sink SinkPublisher. End-session is slice 5.
//
// Locked semantics (v3 §R5 / v3.1 §A2 / v3.2 §B5/§C3):
//   - Birth as of H, then Replay (seq < H) → capture C at H → CatchUp (H <= seq < C) →
//     CompleteCatchUp at C → Live (seq >= C).
//   - H and C are BARRIERS, not labels: a dequeued batch that crosses a barrier is split;
//     only the in-phase prefix is published + acked, then the remainder is re-dequeued
//     under the next phase. No in-memory remainder is retained across a boundary.
//   - STRICT ack: the cursor advances only on a FULL success (Success && Accepted==Count
//     && Rejected==0). A partial/failed publish acks nothing and the subrange is retried
//     (AtLeastOnce). The birth-before-DATA invariant holds: a batch is never acked before
//     it is fully published.
//   - REBIRTH (slice 4): a coalescing, epoch-gated ReplaySessionRebirthHost accepts a
//     sink's rebirth request for the current session/epoch. When a publish returns
//     not-full-success AND a rebirth is pending (A2/C3 ordering lock), the driver processes
//     the rebirth BEFORE re-attempting the subrange: capture a FRESH populated birth at a
//     NEW H (same generation), RebirthAsync under a candidate epoch, promote the epoch ONLY
//     on success, then re-drive Replay → Live from the un-acked cursor. An idle Live driver
//     is woken by a rebirth via a combined wait (buffer OR rebirth OR cancellation).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.3-route-wiring-plan-v3.md §R5.3-4;
//            …-v3.1-amendment.md §A2; …-v3.2-amendment.md §B5/§C3; ADR-0036 (replay → rebirth).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Drives the Core-owned replay lifecycle for one replay-aware route (single sink). Runs on
/// one task; no sink method is invoked concurrently with another. Slice 3 covers birth →
/// Replay → CatchUp → Live; a fault (capture / begin / cutover failure) propagates to the
/// worker-fault handler (→ Failed).
/// </summary>
internal sealed class ReplayRouteDriver
{
    private const int BatchSize = 256;

    /// <summary>Default bound on the graceful end-session emission (see <see cref="EndSessionTimeout"/>).</summary>
    internal static readonly TimeSpan DefaultEndSessionTimeout = TimeSpan.FromSeconds(30);

    private readonly Route _route;
    private readonly ReplayRouteContext _replay;
    private readonly FanoutDispatcher _dispatcher;
    private readonly ReplaySessionIdentitySource _sessionIdentity;
    private readonly DeliveryPolicyConfig _delivery;

    /// <summary>
    /// Bound on the graceful <see cref="IReplayAwareSinkAdapter.EndSessionAsync"/> emission. A
    /// non-cooperative sink must not be able to wedge shutdown, so End runs on a FRESH token (never the
    /// already-cancelled worker token) cancelled at this bound; exceeding it is reported like any other
    /// End failure and cleanup proceeds. Injected from the engine; settable for tests.
    /// </summary>
    internal TimeSpan EndSessionTimeout { get; set; } = DefaultEndSessionTimeout;

    public ReplayRouteDriver(
        Route route, ReplayRouteContext replay, FanoutDispatcher dispatcher, ReplaySessionIdentitySource sessionIdentity)
    {
        _route = route;
        _replay = replay;
        _dispatcher = dispatcher;
        _sessionIdentity = sessionIdentity;
        _delivery = route.Definition.Delivery;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var routeId = _route.RouteId;
        var sinkId = _replay.SinkId;
        var sink = _replay.Sink;

        // --- Birth as of H ---
        var start = await _replay.SessionStateProvider
            .CaptureBirthStateAsync(routeId, sinkId, ct).ConfigureAwait(false);

        // A NEW globally-unique session id per driver start (from the process-wide source, which
        // survives Route replacement), so a delayed lifecycle callback from a previous session is
        // distinguishable. The epoch starts at its base value 0 and advances only on a successful
        // rebirth. The coalescing, epoch-gated host is created per session start and governs the
        // reverse (sink → Core) rebirth handshake for this run.
        var sessionId = _sessionIdentity.Next();
        var epoch = ReplayEpochId.Create(0);
        using var host = new ReplaySessionRebirthHost(sessionId, epoch);

        await sink.BeginReplaySessionAsync(
            ReplaySessionStart.Create(sessionId, epoch, routeId, start, host), ct).ConfigureAwait(false);

        // Begin succeeded — the session is ESTABLISHED. From here a GRACEFUL stop (cooperative
        // cancellation) must end the session exactly once via EndSessionAsync, whereas a FAULT
        // (Begin/capture/publish failure) propagates and does NOT emit a graceful end. Begin failing
        // above skips this entirely (no session was established → no end).
        var h = start.Boundary.CutoffExclusive;
        var cursor = start.Boundary.FirstPendingSequence;
        long? c = null;
        ReplaySessionCutoverState? cutover = null;
        var live = false;

        try
        {
            // --- Phase loop ---
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Control-plane barrier: an accepted rebirth (an out-of-band Node command, or one
                // accepted between subranges) is processed at EVERY loop boundary — before capturing
                // cutover, dequeuing, or publishing the next subrange — so a busy Replay/CatchUp/Live
                // stream (with continuous intake that may never drain) cannot defer it. It finishes the
                // current in-flight publish decision, then pauses DATA and re-births before the next.
                if (host.TryTakePending(out _))
                {
                    (epoch, h, cursor) = await ProcessRebirthAsync(
                        sink, host, routeId, sinkId, sessionId, epoch, ct).ConfigureAwait(false);
                    c = null;
                    cutover = null;
                    live = false;
                    continue;
                }

                long? barrier;
                ReplayPhase phase;

                if (cursor < h)
                {
                    phase = ReplayPhase.Replay;
                    barrier = h;
                }
                else if (c is null)
                {
                    // Reached H — capture the catch-up cutoff C (and the snapshot as of it).
                    cutover = await _replay.SessionStateProvider
                        .CaptureCutoverAsync(routeId, ct).ConfigureAwait(false);
                    c = cutover.CutoffExclusive;
                    continue;
                }
                else if (cursor < c.Value)
                {
                    phase = ReplayPhase.CatchUp;
                    barrier = c.Value;
                }
                else if (!live)
                {
                    // Reached C — emit the final non-historical update and enter Live.
                    await sink.CompleteCatchUpAsync(
                        ReplaySessionCutover.Create(sessionId, epoch, cutover!), ct).ConfigureAwait(false);
                    live = true;
                    continue;
                }
                else
                {
                    phase = ReplayPhase.Live;
                    barrier = null;
                }

                var batch = await _route.Buffer.DequeueBatchAsync(sinkId, BatchSize, ct).ConfigureAwait(false);

                // The split derives per-point sequences arithmetically from First/Last, so a
                // non-contiguous batch must fail closed rather than be mis-split.
                if (!batch.IsEmpty && batch.LastSequence != batch.FirstSequence + batch.Points.Count - 1)
                {
                    throw new BufferException(
                        CoreErrors.BufferCursorInconsistent,
                        $"Non-contiguous dequeued batch [{batch.FirstSequence}, {batch.LastSequence}] with " +
                        $"{batch.Points.Count} points on route '{routeId}'.");
                }

                // Cursor continuity: a non-empty batch must BEGIN at the local cursor, or we'd publish +
                // ack a subrange while silently skipping [cursor, batch.FirstSequence). The one legitimate
                // gap is retention (MaxAge) fast-forwarding the PERSISTED cursor past expired data —
                // reconcile against the authoritative boundary and adopt it WITHOUT acking; a batch that
                // starts below the cursor (already-acked data) or a gap the persisted cursor does not
                // explain is corruption → fail closed. Re-evaluate the phase after adopting the new cursor.
                if (!batch.IsEmpty && batch.FirstSequence != cursor)
                {
                    var reconciled = await _replay.BoundaryProvider
                        .CaptureReplayBoundaryAsync(sinkId, ct).ConfigureAwait(false);
                    if (batch.FirstSequence > cursor && reconciled.FirstPendingSequence == batch.FirstSequence)
                    {
                        cursor = batch.FirstSequence; // legitimate retention fast-forward
                        continue;
                    }

                    throw new BufferException(
                        CoreErrors.BufferCursorInconsistent,
                        $"Dequeued batch begins at {batch.FirstSequence} but the local cursor is {cursor} and the " +
                        $"persisted cursor is {reconciled.FirstPendingSequence} on route '{routeId}'.");
                }

                // Split at the barrier (null in Live): publish only the in-phase prefix (seq < barrier).
                var first = batch.FirstSequence;
                var inPhaseCount = batch.IsEmpty
                    ? 0
                    : barrier is { } b ? (int)Math.Clamp(b - first, 0, batch.Points.Count) : batch.Points.Count;

                if (inPhaseCount == 0)
                {
                    if (phase == ReplayPhase.Live)
                    {
                        // Live and drained — sleep until the next append OR an accepted rebirth wakes this
                        // sink (combined wait). On wake the loop re-enters and the top-of-loop control-plane
                        // barrier processes any pending rebirth. Cancellation propagates out of the loop to
                        // the single shutdown handler below (which emits the graceful session end).
                        await WaitForWorkAsync(sinkId, host, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Replay/CatchUp with no below-barrier point available. This is normally only reached
                    // exactly at the barrier; the one legitimate way the local cursor can be below the
                    // barrier with no rows is retention (MaxAge) fast-forwarding the persisted cursor past
                    // expired data. Reconcile with the AUTHORITATIVE persisted cursor WITHOUT synthesizing
                    // an ack (never ack an unpublished range); if it did not advance, the expected rows are
                    // genuinely missing → fail closed.
                    var authoritative = await _replay.BoundaryProvider
                        .CaptureReplayBoundaryAsync(sinkId, ct).ConfigureAwait(false);
                    if (authoritative.FirstPendingSequence > cursor)
                    {
                        cursor = authoritative.FirstPendingSequence;
                        continue;
                    }

                    throw new BufferException(
                        CoreErrors.BufferCursorInconsistent,
                        $"Replay cursor {cursor} is below the barrier {barrier} on route '{routeId}' but no " +
                        "corresponding buffered rows exist (and retention has not advanced the persisted cursor).");
                }

                var points = inPhaseCount == batch.Points.Count
                    ? batch.Points
                    : batch.Points.Take(inPhaseCount).ToList();
                var lastSeq = first + inPhaseCount - 1;
                var context = PublishContext.Create(routeId, sessionId, epoch, phase, h, c, first, lastSeq);

                var rebirth = await PublishUntilFullSuccessOrRebirthAsync(sink, host, points, context, ct)
                    .ConfigureAwait(false);

                if (rebirth is not null)
                {
                    // A2/C3 ordering lock: a first-observed metric forced a rebirth. Process it BEFORE
                    // re-attempting this subrange (which was neither acked nor fully published) — the fresh
                    // birth announces the metric, then the SAME subrange is re-dequeued under the new epoch.
                    (epoch, h, cursor) = await ProcessRebirthAsync(
                        sink, host, routeId, sinkId, sessionId, epoch, ct).ConfigureAwait(false);
                    c = null;
                    cutover = null;
                    live = false;
                    continue;
                }

                // STRICT ack: only after a full success (enforced above) — never before publish.
                await _route.Buffer.AckAsync(sinkId, lastSeq, ct).ConfigureAwait(false);
                cursor = lastSeq + 1;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // GRACEFUL stop (cooperative cancellation). Fall through to emit the session end below.
            // A FAULT (any other exception) skips this and propagates → the worker faults the route
            // and NO graceful end is emitted.
        }

        // Graceful shutdown of a BEGUN session: emit EndSessionAsync exactly once, on the driver's own
        // task, BEFORE it completes — so the worker returns (and the Host stops the sink) only AFTER
        // Core has ended the session (Start(Host) → Begin(Core) … End(Core) → Stop(Host)). The reason is
        // the route's explicit pending reason (Stop | ConfigurationReplaced), never inferred from the
        // cancellation. An End failure is reported but never prevents the driver from completing.
        await EmitEndSessionAsync(sink, sessionId, routeId).ConfigureAwait(false);
    }

    /// <summary>
    /// Emit the graceful session end (e.g. a death certificate) exactly once for a begun session. Runs
    /// on a FRESH, BOUNDED token (<see cref="EndSessionTimeout"/>) — NOT the worker token that triggered
    /// the shutdown (already cancelled, and reusing it would suppress End immediately), and NOT
    /// unbounded (a non-cooperative sink must not wedge the whole shutdown chain: driver → worker →
    /// StopRouteAsync → Host reverse-phase cleanup). Exceeding the bound, or any other failure, is
    /// REPORTED (stderr — Core has no logger seam by design) and SWALLOWED so reverse-phase cleanup
    /// proceeds: the driver completes, the worker returns, and the route reaches Stopped.
    /// </summary>
    private async Task EmitEndSessionAsync(IReplayAwareSinkAdapter sink, ReplaySessionId sessionId, string routeId)
    {
        var reason = _route.PendingEndReason;
        using var endCts = new CancellationTokenSource(EndSessionTimeout);
        try
        {
            await sink.EndSessionAsync(
                ReplaySessionEnd.Create(sessionId, routeId, reason), endCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (endCts.IsCancellationRequested)
        {
            ReportEndFailure(routeId, reason,
                $"EndSessionAsync exceeded {EndSessionTimeout} — a non-cooperative sink must not wedge shutdown.");
        }
        catch (Exception ex)
        {
            ReportEndFailure(routeId, reason, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReportEndFailure(string routeId, ReplaySessionEndReason reason, string detail)
    {
        try
        {
            Console.Error.WriteLine(
                $"[routing] Route '{routeId}' EndSessionAsync ({reason}) failed during shutdown: {detail}");
        }
        catch
        {
            // Reporting must never override reverse-phase cleanup.
        }
    }

    /// <summary>
    /// Process an accepted same-generation rebirth: capture a FRESH, POPULATED birth state at a NEW H
    /// (the tracked append already folded any first-observed metric into the current-generation
    /// manifest, so the fresh snapshot announces it — no generation advance, no drain), re-announce on
    /// the SAME session under a candidate epoch, and promote the epoch ONLY on a successful re-birth.
    /// A <see cref="IReplayAwareSinkAdapter.RebirthAsync"/> failure propagates → the route faults and
    /// the previous epoch stands (the candidate is never promoted). Returns the new (epoch, H, cursor)
    /// for the caller to re-drive Replay → Live from the un-acked cursor.
    /// </summary>
    private async Task<(ReplayEpochId Epoch, long H, long Cursor)> ProcessRebirthAsync(
        IReplayAwareSinkAdapter sink,
        ReplaySessionRebirthHost host,
        string routeId,
        string sinkId,
        ReplaySessionId sessionId,
        ReplayEpochId currentEpoch,
        CancellationToken ct)
    {
        var fresh = await _replay.SessionStateProvider
            .CaptureBirthStateAsync(routeId, sinkId, ct).ConfigureAwait(false);
        var candidate = ReplayEpochId.Create(currentEpoch.Value + 1);

        await sink.RebirthAsync(
            ReplaySessionRebirth.Create(sessionId, candidate, fresh), ct).ConfigureAwait(false);

        // Promote ONLY after a successful re-birth: the host now epoch-gates on the new epoch and drops
        // any request still pending for the old one.
        host.Promote(sessionId, candidate);

        return (candidate, fresh.Boundary.CutoffExclusive, fresh.Boundary.FirstPendingSequence);
    }

    /// <summary>
    /// Await the next unit of work for an idle Live driver: buffer data (the fanout signal) OR an
    /// accepted rebirth (the host wake) OR cancellation. Both waits run on a per-call linked token so
    /// the loser is cancelled the moment the winner returns — no dangling waiter survives to silently
    /// consume a future permit. A released-but-unconsumed permit stays latched for the next call.
    /// </summary>
    private async Task WaitForWorkAsync(string sinkId, ReplaySessionRebirthHost host, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var bufferWait = _dispatcher.WaitForSignalAsync(sinkId, linked.Token);
        var rebirthWait = host.WaitForRebirthAsync(linked.Token);
        try
        {
            await Task.WhenAny(bufferWait, rebirthWait).ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
            await ObserveQuietlyAsync(bufferWait).ConfigureAwait(false);
            await ObserveQuietlyAsync(rebirthWait).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
    }

    private static async Task ObserveQuietlyAsync(Task wait)
    {
        try
        {
            await wait.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The losing waiter was cancelled once the winner returned — expected.
        }
        catch (ObjectDisposedException)
        {
            // The dispatcher/host was disposed during shutdown — expected.
        }
    }

    /// <summary>
    /// Publish one phase-tagged subrange, retrying the WHOLE subrange per the delivery policy until a
    /// FULL success — UNLESS a rebirth for the current session/epoch is pending, in which case this
    /// returns that request so the caller processes the rebirth BEFORE re-attempting the subrange
    /// (the A2/C3 ordering lock — birth-before-DATA for a first-observed metric). Returns
    /// <c>null</c> on a full success. Acks nothing either way (a partial result advances no cursor). A
    /// NON-retryable failure with NO pending rebirth faults immediately; a retryable/partial failure
    /// retries up to <see cref="DeliveryPolicyConfig.MaxRetries"/> and then faults (the subrange stays
    /// buffered — AtLeastOnce — for replay on restart). A thrown exception is treated as a bounded
    /// retryable transient (never an infinite loop).
    /// </summary>
    private async Task<RebirthRequest?> PublishUntilFullSuccessOrRebirthAsync(
        IReplayAwareSinkAdapter sink,
        ReplaySessionRebirthHost host,
        IReadOnlyList<CanonicalDataPoint> points,
        PublishContext context,
        CancellationToken ct)
    {
        var maxRetries = Math.Max(0, _delivery.MaxRetries);
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Control wins over the NEXT publish attempt: a rebirth accepted before this attempt (e.g.
            // during the preceding backoff delay) is handed back so the caller re-births BEFORE
            // (re-)publishing this subrange — never a retry ahead of an accepted control request.
            if (host.TryTakePending(out var pendingBeforeAttempt))
            {
                return pendingBeforeAttempt;
            }

            PublishResult result;
            try
            {
                result = await sink.PublishAsync(points, context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = PublishResult.Failed(
                    new AdapterError
                    {
                        Code = "CORE.SINK_PUBLISH_THREW",
                        Category = ErrorCategory.Internal,
                        Message = ex.Message,
                        Retryable = true,
                    },
                    TimeSpan.Zero);
            }

            if (result.Success && result.AcceptedCount == points.Count && result.RejectedCount == 0)
            {
                return null; // full success — the caller may now ack this subrange
            }

            // A2/C3 ordering lock: a pending rebirth for the current session/epoch takes precedence
            // over retry. The adapter awaited RequestRebirthAsync RETURNING before its not-full-success
            // result (C3 happens-before), so the request is visible here. Hand it back so the caller
            // re-births BEFORE re-attempting this (un-acked) subrange.
            if (host.TryTakePending(out var pending))
            {
                return pending;
            }

            // A non-retryable failure faults the route immediately (ack nothing).
            if (result.Error is { Retryable: false })
            {
                throw new InvalidOperationException(
                    $"[{CoreErrors.RouteDeliveryFailed}] Replay publish to route '{context.RouteId}' returned a " +
                    $"non-retryable failure: {result.Error.Code}: {result.Error.Message}");
            }

            // Retryable / partial: retry within the budget, then fault (the subrange stays buffered).
            if (attempt >= maxRetries)
            {
                throw new InvalidOperationException(
                    $"[{CoreErrors.RouteDeliveryFailed}] Replay publish to route '{context.RouteId}' exhausted " +
                    $"{maxRetries} retries; last: {result.Error?.Code ?? "partial-accept"}.");
            }

            attempt++;
            await Task.Delay(ComputeBackoff(attempt), ct).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var initial = Math.Max(1, _delivery.InitialBackoffMs);
        var max = Math.Max(initial, _delivery.MaxBackoffMs);
        var multiplier = _delivery.BackoffMultiplier <= 0 ? 1.0 : _delivery.BackoffMultiplier;
        var ms = initial * Math.Pow(multiplier, attempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(ms, max));
    }
}
