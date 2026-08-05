// ============================================================================
// File: Routing/RouteWorker.cs
// Purpose: Happy-path worker loop for a single route. Reads canonical points
//          from the route's source intake, applies the tag filter and
//          transform pipeline, enqueues results into the route buffer, and
//          runs one sink publisher loop per configured sink.
// Reference: ARCHITECTURE_BLUEPRINT.md §19 (§19.1 intake, §19.3 fanout,
//            §19.6 ordering), PHASE1_EXECUTION_PLAN.md C3.
// Milestone: C3 Commit 2 (phase 6 — retry + replay + lifecycle + backpressure
//            wired. Policy/mechanism split is locked: BackpressureController
//            decides, RouteChannelSpillover executes).
//
// Phase scope note:
//   The worker owns one FanoutDispatcher, one RouteLifecycleManager (via
//   Route), one SinkPublisher per sink, one BackpressureController, and
//   one RouteChannelSpillover per route. The intake pump reads from the
//   source intake, batches, filters, pipeline-transforms, then consults
//   the BackpressureController. Based on the decision it either enqueues
//   normally (Pass), spills via RouteChannelSpillover (Spill), or drops
//   and records a BackpressureDroppedEvent (Drop). The buffer remains the
//   single source of durable truth in all three paths.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Pipeline;

namespace ElpisEdgeConnect.Core.Routing;

internal sealed class RouteWorker
{
    // Batching knobs for the happy path. Conservative values; the benchmark
    // milestone will tune these.
    private const int IntakeBatchSize = 256;
    private const int SinkBatchSize = 256;

    private readonly Route _route;
    private readonly IRoutingEngineDiagnostics _diagnostics;
    private readonly FanoutDispatcher _dispatcher;
    private readonly ReplaySessionIdentitySource _replaySessionIdentity;
    private readonly TimeSpan _replayEndSessionTimeout;
    private readonly IBufferHealthSink? _bufferHealthSink;
    private readonly IRouteTap? _tap;

    public RouteWorker(
        Route route,
        IRoutingEngineDiagnostics diagnostics,
        FanoutDispatcher dispatcher,
        ReplaySessionIdentitySource replaySessionIdentity,
        TimeSpan replayEndSessionTimeout,
        IBufferHealthSink? bufferHealthSink = null,
        IRouteTap? tap = null)
    {
        _route = route;
        _diagnostics = diagnostics;
        _dispatcher = dispatcher;
        _replaySessionIdentity = replaySessionIdentity;
        _replayEndSessionTimeout = replayEndSessionTimeout;
        _bufferHealthSink = bufferHealthSink;
        _tap = tap;
    }

    // Period for the source-idle buffer-stats push. When the route's source
    // is quiet, the intake loop blocks in WaitToReadAsync — without a
    // periodic push, the diagnostics gauge would freeze at the last value
    // observed during a previous intake cycle, which is misleading when an
    // operator wants to see "is the buffer draining?" or "how full is it
    // now?". 1 second is fast enough that the soak runner's per-minute
    // heartbeat always sees a fresh value, slow enough that the cost is
    // negligible (one SQLite SELECT per route per second).
    private static readonly TimeSpan IdleStatsInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Entry point for the route's worker Task. Runs until
    /// <paramref name="ct"/> fires or the source intake completes.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var def = _route.Definition;

        // Register each sink cursor BEFORE the intake pump starts so that
        // any points written to the buffer are observed by every sink.
        // The dispatcher is pre-registered by the routing engine so a wake
        // issued during startup is not lost.
        foreach (var sink in def.Sinks)
        {
            await _route.Buffer.RegisterSinkAsync(sink.InstanceId, ct).ConfigureAwait(false);
        }

        // Worker-scoped CTS linked to the external token. The sink loops run on it so an intake-pump
        // FAILURE (e.g. a replay tracked-append fault) can signal them to exit — otherwise the finally
        // below would await sink loops that never observe cancellation and the fault would never
        // surface (the route would hang instead of transitioning to Failed). External stop cancels via
        // the link; a normal intake completion (source drained) does NOT cancel, so the sinks keep
        // draining until the route is stopped.
        using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var sinkTasks = new List<Task>(def.Sinks.Count);
        if (_route.Replay is { } replay)
        {
            // Replay route: ONE serialized driver owns the replay-aware sink's lifecycle + publish
            // (Begin → Replay/CatchUp/Live). The legacy context-free SinkPublisher is never used for
            // this sink.
            var driver = new ReplayRouteDriver(_route, replay, _dispatcher, _replaySessionIdentity)
            {
                EndSessionTimeout = _replayEndSessionTimeout,
            };
            sinkTasks.Add(Task.Run(() => driver.RunAsync(workerCts.Token), workerCts.Token));
        }
        else
        {
            foreach (var sink in def.Sinks)
            {
                var publisher = new SinkPublisher(
                    def.RouteId, sink, def.Delivery, _diagnostics, _route.Lifecycle);
                sinkTasks.Add(Task.Run(() => RunSinkLoopAsync(publisher, workerCts.Token), workerCts.Token));
            }
        }

        // Supervise the intake pump and the sink/driver task CONCURRENTLY. The first to FAULT — or a
        // sink/driver task that terminates while the route is still running — is the PRIMARY route
        // outcome and cancels its sibling. A NORMAL intake completion (source drained) is NOT a
        // fault: the sinks keep draining until the route is stopped. Deliberate cleanup cancellation
        // never replaces the primary fault (same discipline as slice 2's intake path).
        var intakeTask = RunIntakePumpAsync(workerCts.Token);
        var sinkTask = Task.WhenAll(sinkTasks);

        ExceptionDispatchInfo? primaryFailure = null;
        var firstCompleted = await Task.WhenAny(intakeTask, sinkTask).ConfigureAwait(false);

        if (firstCompleted.IsFaulted)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(firstCompleted.Exception!.GetBaseException());
            workerCts.Cancel();
        }
        else if (firstCompleted == sinkTask && !workerCts.IsCancellationRequested)
        {
            // The driver / sink loops finished while the route is still running — a replay driver
            // must not terminate before shutdown. Fault so the route never reports Running while
            // nothing is delivering.
            primaryFailure = ExceptionDispatchInfo.Capture(new InvalidOperationException(
                $"[{CoreErrors.RouteInvalidState}] Route '{def.RouteId}' sink/driver task terminated before shutdown."));
            workerCts.Cancel();
        }

        // Drain BOTH tasks. Deliberate cleanup cancellation (and a re-observed primary fault) must
        // not replace the primary outcome; when there is NO primary fault, a task fault here (a
        // driver fault after a normal intake completion) propagates and faults the route.
        foreach (var task in new[] { intakeTask, sinkTask })
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (primaryFailure is not null && workerCts.IsCancellationRequested)
            {
                // Expected — the sibling was cancelled as part of fault cleanup.
            }
            catch (Exception ex) when (primaryFailure is not null)
            {
                try
                {
                    Console.Error.WriteLine(
                        $"[route {def.RouteId}] fault observed during shutdown drain: {ex.GetType().Name}: {ex.Message}");
                }
                catch { /* diagnostics never override the primary fault */ }
            }
        }

        primaryFailure?.Throw();
    }

    private async Task RunIntakePumpAsync(CancellationToken ct)
    {
        var def = _route.Definition;
        var reader = def.Source.Reader;
        var filter = def.Filter;
        var pipeline = def.Pipeline;

        // Phase 6: policy and mechanism for backpressure. The controller is
        // pure; the spillover owns the buffer write path for overflow.
        var controller = new BackpressureController(def.BufferPolicy.DropPolicy);
        var spillover = new RouteChannelSpillover(_route.Buffer);
        var bufferMaxDepth = def.BufferPolicy.MaxDepth;

        var scratch = new List<CanonicalDataPoint>(IntakeBatchSize);
        var transformContext = new TransformContext
        {
            RouteId = def.RouteId,
            GatewayId = def.GatewayId,
            UtcNow = DateTime.UtcNow,
            Metrics = NullTransformMetricsRecorder.Instance,
        };

        // Drive a parallel idle-stats pump so the BufferHealthSnapshot stays current even
        // when the source is quiet (e.g. Modbus polling cycle is sub-second but each scan
        // emits 0 points). It runs on a LOCAL linked CTS cancelled in the finally, so the
        // pump loop exiting for ANY reason (external stop, source-channel completion, or an
        // intake fault) stops the idle-stats task too — otherwise the finally would await a
        // task that never observes cancellation and would swallow an intake fault. Failures
        // swallowed — diagnostics never tank the data path.
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var idleStatsTask = Task.Run(async () =>
        {
            while (!idleCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(IdleStatsInterval, idleCts.Token).ConfigureAwait(false);
                    if (_bufferHealthSink is null) { continue; }
                    var s = await _route.Buffer.GetStatsAsync().ConfigureAwait(false);
                    _bufferHealthSink.RecordBufferStats(
                        _route.RouteId,
                        _route.Definition.BufferPolicy.Mode,
                        s);
                }
                catch (OperationCanceledException) { return; }
                catch { /* never let diagnostics break the worker */ }
            }
        }, idleCts.Token);

        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                scratch.Clear();
                while (scratch.Count < IntakeBatchSize
                       && reader.TryRead(out var point))
                {
                    if (filter.IsMatch(point))
                    {
                        scratch.Add(point);
                    }
                }

                if (scratch.Count == 0)
                {
                    continue;
                }

                // Live Data Tap (ADR-0018): capture the SOURCE-side batch —
                // post-filter, PRE-transform — so Compare can see the transform
                // effect. The IsTapActive guard is an O(1) lock-free read; when
                // no operator is watching this is the only cost and capture is
                // skipped entirely (ADR-0017 Rule 1 — strictly observational, P1).
                if (_tap is not null && _tap.IsTapActive(def.RouteId))
                {
                    _tap.CaptureSource(def.RouteId, scratch);
                }

                IReadOnlyList<CanonicalDataPoint> toEnqueue = scratch;
                if (pipeline is not null)
                {
                    toEnqueue = pipeline.Execute(scratch, transformContext);
                }

                if (toEnqueue.Count == 0)
                {
                    continue;
                }

                // K1.3: a replay route appends on the TRACKED path (points + manifest +
                // next_sequence, atomically) at its FIXED generation. Depth backpressure
                // (Pass/Spill/Drop) does NOT apply — a replay route buffers every point until
                // the replay sink acks it (retention is by the replay-sink cursor + MaxAge,
                // not depth eviction). A storage failure or a stale-generation mismatch is an
                // invariant breach that FAULTS the route (never a silent drop): the exception
                // propagates out of the worker to the fault handler.
                if (_route.Replay is { } replay)
                {
                    await replay.Buffer
                        .AppendTrackedAsync(toEnqueue, replay.Generation, ct)
                        .ConfigureAwait(false);

                    _dispatcher.NotifyAll();

                    if (_bufferHealthSink is not null)
                    {
                        var postAppendStats = await _route.Buffer.GetStatsAsync().ConfigureAwait(false);
                        _bufferHealthSink.RecordBufferStats(
                            _route.RouteId,
                            _route.Definition.BufferPolicy.Mode,
                            postAppendStats);
                    }

                    continue;
                }

                // Phase 6: consult backpressure controller BEFORE writing.
                // The worker maps the two pressure signals as follows:
                //   * channelHasRoom = buffer is comfortably below its soft
                //     watermark (80% of MaxDepth). This is the fast path.
                //   * bufferHasRoom  = the batch fits under the hard ceiling.
                // This gives a clean three-state: Pass / Spill / Drop.
                var stats = await _route.Buffer.GetStatsAsync().ConfigureAwait(false);

                // Surface stats on the diagnostics layer (latest-wins gauge).
                // Same payload that drives backpressure is what the operator
                // sees on /metrics and the soak runner's per-minute heartbeat.
                _bufferHealthSink?.RecordBufferStats(
                    _route.RouteId,
                    _route.Definition.BufferPolicy.Mode,
                    stats);

                var softWatermark = (long)(bufferMaxDepth * 0.8);
                var channelHasRoom = stats.CurrentDepth < softWatermark;
                var bufferHasRoom = stats.CurrentDepth + toEnqueue.Count <= bufferMaxDepth;

                var decision = controller.Decide(channelHasRoom, bufferHasRoom);

                switch (decision)
                {
                    case BackpressureDecision.Pass:
                        await _route.Buffer
                            .EnqueueAsync(toEnqueue, ct)
                            .ConfigureAwait(false);
                        break;

                    case BackpressureDecision.Spill:
                        // Mechanism — writes through to the buffer with
                        // spillover accounting.
                        await spillover.SpillAsync(toEnqueue, ct).ConfigureAwait(false);
                        break;

                    case BackpressureDecision.Drop:
                        _diagnostics.OnBackpressureDropped(new BackpressureDroppedEvent
                        {
                            RouteId = def.RouteId,
                            DroppedCount = toEnqueue.Count,
                            Reason = "channel-and-buffer-full",
                            ObservedAtUtc = DateTime.UtcNow,
                        });
                        continue; // do NOT wake sinks — nothing new in buffer
                }

                // Wake every sink so their publisher loops drain the new
                // batch. NotifyAll carries NO payload — points live in the
                // buffer; the dispatcher is a pure signal.
                _dispatcher.NotifyAll();

                // Re-publish stats AFTER enqueue so the diagnostics gauge
                // reflects the post-write depth. Without this the recorded
                // depth always lags by one batch — fine for steady-state
                // telemetry but visible to tests that assert "buffer is
                // holding N points right now" without further intake to
                // trigger the next pre-write push.
                if (_bufferHealthSink is not null)
                {
                    var postEnqueueStats = await _route.Buffer.GetStatsAsync().ConfigureAwait(false);
                    _bufferHealthSink.RecordBufferStats(
                        _route.RouteId,
                        _route.Definition.BufferPolicy.Mode,
                        postEnqueueStats);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            // Stop the idle-stats pump regardless of WHY the intake loop exited (external stop,
            // source completion, or a fault) so this finally can never hang awaiting a task that
            // would otherwise run until external cancellation — that would swallow an intake fault.
            idleCts.Cancel();
            try { await idleStatsTask.ConfigureAwait(false); }
            catch { /* idle-stats pump never propagates */ }
        }
    }

    private async Task RunSinkLoopAsync(SinkPublisher publisher, CancellationToken ct)
    {
        var sinkId = publisher.SinkInstanceId;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Drain as many batches as the buffer currently holds before
                // going back to sleep. Each sink has its own publisher /
                // retry state / loop — a failing sink that is sleeping on a
                // backoff delay never blocks a healthy sink, because the
                // healthy sink is its own Task with its own dispatcher signal.
                var exhausted = false;
                while (!ct.IsCancellationRequested && !exhausted)
                {
                    var batch = await _route.Buffer
                        .DequeueBatchAsync(sinkId, SinkBatchSize, ct)
                        .ConfigureAwait(false);

                    if (batch.IsEmpty)
                    {
                        // Single exit path from the Draining phase: the
                        // cursor has caught up with the buffer tail. The
                        // publisher fires SinkRecovered if it was draining,
                        // or ignores the call otherwise.
                        publisher.NotifyBufferEmpty();
                        break;
                    }

                    // Live Data Tap (ADR-0018): capture what THIS sink receives,
                    // per sink (fan-out), immediately before publish. Same O(1)
                    // demand-driven guard as the source side.
                    if (_tap is not null && _tap.IsTapActive(_route.RouteId))
                    {
                        _tap.CaptureSink(_route.RouteId, sinkId, batch.Points);
                    }

                    PublishOutcome outcome;
                    try
                    {
                        outcome = await publisher
                            .PublishWithRetryAsync(batch.Points, ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    switch (outcome)
                    {
                        case PublishOutcome.Acked:
                        case PublishOutcome.DroppedAndAdvanced:
                            // Either delivered or dropped-per-policy —
                            // advance the cursor in both cases.
                            await _route.Buffer
                                .AckAsync(sinkId, batch.LastSequence, ct)
                                .ConfigureAwait(false);
                            break;

                        case PublishOutcome.RetriesExhausted:
                            // Leave the cursor put. Fall back to the wake
                            // loop; next signal re-attempts with fresh budget.
                            exhausted = true;
                            break;
                    }
                }

                // Buffer drained for this sink — await the next wake signal.
                try
                {
                    await _dispatcher
                        .WaitForSignalAsync(sinkId, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            // Buffer disposed during coordinated shutdown. Expected only
            // when RoutingEngine.DisposeAsync has cancelled the worker CTS
            // and Route.DisposeAsync is tearing down the buffer. The
            // SqliteBuffer normalizes a race-induced NRE to
            // ObjectDisposedException in DequeueBatchAsync. Do NOT widen
            // this suppression to non-shutdown paths — an ODE without
            // cancellation is a genuine bug.
        }
    }
}
