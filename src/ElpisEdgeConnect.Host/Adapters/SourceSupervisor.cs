// ============================================================================
// File: Adapters/SourceSupervisor.cs
// Purpose: Owns the lifecycle of every registered ISourceAdapter, runs one
//          poll loop per source, pumps points into a per-source bounded
//          channel that surfaces as ISourceIntake to the routing engine,
//          and pushes source health into the diagnostics collector.
//
//          Driven explicitly by HostStartup during the StartSourceSupervisor
//          phase — NOT registered as IHostedService — so the locked startup
//          ordering remains the single source of truth.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D, ARCHITECTURE_BLUEPRINT.md §4.2
// Milestone: D — phase 4.
//
// LOCKED design rules:
//   * Channels are PRE-CREATED at construction time. The route definition
//     factory (phase 6) resolves ISourceIntake instances via this
//     supervisor BEFORE the routing engine starts; the actual poll loops
//     start later when StartAsync is called.
//   * Bounded channel uses BoundedChannelFullMode.Wait so the poll loop
//     blocks briefly if the routing engine has not yet drained, rather
//     than dropping points upstream of the buffer.
//   * Source health observations flow into ISourceHealthSink on every
//     successful poll batch. Adapter state changes flow on Initialize /
//     Start / Stop / Failure boundaries.
//   * Adapter exceptions in PollAsync mark the source Failed via the
//     diagnostics collector and stop that source's poll loop. The other
//     sources continue. Per-adapter isolation is preserved.
//   * No wall-clock retry inside the supervisor — pacing is the adapter's
//     responsibility (mock adapters use PollGate; production adapters use
//     their PollIntervalMs).
//   * An empty batch from PollAsync is "no data this tick", NOT "source is
//     done". The poll loop sleeps briefly and tries again. Per-group-timer
//     adapters (Modbus) routinely return empty between scheduled scans;
//     continuous-stream adapters (FOCAS2, MTConnect) typically don't, but
//     either way the contract is identical. The supervisor exits its loop
//     ONLY on cancellation (StopAsync) or an unrecoverable AdapterException.
//
//   * M.P2.2 phase 2.a additions (step 1, internal mechanics):
//       - Per-instance CancellationTokenSource on each SupervisedSource,
//         linked from the parent CT at StartInternal time. Replaces the
//         old shared _stopCts so phase 2.c can cancel ONE source's pump
//         without disturbing the others.
//       - _lifecycleGate (SemaphoreSlim(1,1)) serializes mutating public
//         methods (StartAsync, StopAsync, DisposeAsync, and the per-
//         instance Add/Remove/Restart that will be added in phase 2.a
//         step 2). Lock-free read paths (GetIntake, SourceInstanceIds)
//         remain unblocked.
//       - _disposed flag (Interlocked.CompareExchange) gives DisposeAsync
//         idempotency; ThrowIfDisposed guards mutating methods.
//       - StopInternal applies a 10s bounded timeout per instance so a
//         single stuck adapter cannot hang shutdown.
//     Public surface in step 1 is bit-identical externally to the prior
//     boot path; the new mechanics are internal-only.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Supervises the constructed source adapters: lifecycle, poll loop,
/// channel pump, and diagnostics push. Single instance per gateway.
/// </summary>
public sealed class SourceSupervisor : ISupervisedSourceRegistry, IAsyncDisposable
{
    private const int IntakeChannelCapacity = 1024;

    /// <summary>
    /// Per-instance bounded ceiling for adapter teardown work
    /// (pump await + Adapter.StopAsync). A single misbehaving adapter
    /// must never hang reconciliation or shutdown.
    /// </summary>
    private const int PerInstanceStopTimeoutMs = 10_000;

    private readonly ConcurrentDictionary<string, SupervisedSource> _supervised =
        new(StringComparer.Ordinal);
    private readonly ISourceHealthSink _healthSink;
    private readonly ILogger<SourceSupervisor> _logger;

    /// <summary>
    /// Serializes mutating public methods (Start/Stop/Dispose + the
    /// per-instance Add/Remove/Restart added in phase 2.a step 2).
    /// Read-only accessors (GetIntake, SourceInstanceIds) bypass this
    /// and rely on ConcurrentDictionary semantics.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    /// 0 = alive; 1 = DisposeAsync has run (or is running). Set via
    /// Interlocked.CompareExchange so DisposeAsync is idempotent.
    /// </summary>
    private int _disposed;

    private bool _started;

    // How long to sleep when an adapter returns an empty batch ("no data
    // this tick"). Short enough to keep latency low for per-group-timer
    // adapters that may have data ready on the very next tick; long
    // enough to avoid pinning a CPU when no source has anything to emit.
    private readonly int _idleSleepMs = 50;

    // Upper bound on how long a source poll/subscribe loop will wait to hand a
    // point to its bounded intake channel. The channel uses
    // BoundedChannelFullMode.Wait for backpressure, which is correct WHILE the
    // route worker is draining — but if the route worker stalls/exits, an
    // unbounded WriteAsync freezes the SOURCE (it stops reading) even though
    // store-and-forward is supposed to decouple it. On timeout we keep the
    // source alive and drop points rather than block forever.
    private const int IntakeWriteStallMs = 10_000;

    /// <summary>
    /// Construct the supervisor with the registrations and the diagnostics
    /// push seam. Channels for every registration are created here so the
    /// route definition factory can resolve intakes before the routing
    /// engine starts.
    /// </summary>
    public SourceSupervisor(
        IEnumerable<SourceRegistration> registrations,
        ISourceHealthSink healthSink,
        ILogger<SourceSupervisor> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(healthSink);
        ArgumentNullException.ThrowIfNull(logger);
        _healthSink = healthSink;
        _logger = logger;

        foreach (var reg in registrations)
        {
            RegisterInternal(reg);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SourceInstanceIds
    {
        get
        {
            // ConcurrentDictionary.Keys returns ICollection<T> which does
            // not implement IReadOnlyCollection<T> at runtime. Snapshot
            // to an array — also gives stable enumeration semantics for
            // callers across concurrent Add/Remove.
            return _supervised.Keys.ToArray();
        }
    }

    /// <inheritdoc/>
    public ISourceIntake? GetIntake(string sourceInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceInstanceId);
        return _supervised.TryGetValue(sourceInstanceId, out var sup)
            ? sup.Intake
            : null;
    }

    /// <inheritdoc/>
    public ISourceAdapter? GetAdapter(string sourceInstanceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceInstanceId);
        return _supervised.TryGetValue(sourceInstanceId, out var sup)
            ? sup.Registration.Adapter
            : null;
    }

    /// <summary>
    /// Initialize and start every registered adapter, then launch each
    /// poll loop. Idempotent on a second call.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_started)
            {
                return;
            }
            foreach (var sup in _supervised.Values)
            {
                await StartInternal(sup, cancellationToken).ConfigureAwait(false);
            }
            _started = true;
            _logger.LogInformation("Source supervisor started {Count} sources.", _supervised.Count);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Cancel every poll loop, complete each intake channel, and stop
    /// every adapter. Safe to call multiple times.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_started)
            {
                return;
            }
            foreach (var sup in _supervised.Values)
            {
                await StopInternal(sup, cancellationToken).ConfigureAwait(false);
            }
            _started = false;
            _logger.LogInformation("Source supervisor stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Add a brand-new source instance, initialize + start its adapter,
    /// and launch its poll loop. Used by the M.P2.2 hot-reload
    /// coordinator when a configuration apply introduces a new source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> if
    /// <paramref name="reg"/>'s instance id is already supervised.
    /// </para>
    /// <para>
    /// If <see cref="StartInternal"/> throws (adapter init/start
    /// failure), the partially-added map entry is rolled back so the
    /// supervisor's <c>_supervised == actually running</c> invariant
    /// is preserved. The exception then propagates to the caller —
    /// the coordinator catches it in its per-instance try/wrap and
    /// registers a <c>ConfigurationFault</c>.
    /// </para>
    /// </remarks>
    public async Task AddAsync(SourceRegistration reg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reg);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            // RegisterInternal throws on duplicate id. We propagate.
            RegisterInternal(reg);

            try
            {
                var sup = _supervised[reg.Adapter.InstanceId];
                await StartInternal(sup, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Rollback map entry so a half-started adapter doesn't
                // leave a phantom intake reachable via GetIntake.
                _supervised.TryRemove(reg.Adapter.InstanceId, out _);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stop the named source's poll loop + adapter and remove it from
    /// the supervisor. Idempotent: an unknown id is a silent no-op.
    /// Used by the M.P2.2 hot-reload coordinator when a configuration
    /// apply removes a source.
    /// </summary>
    public async Task RemoveAsync(string sourceInstanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceInstanceId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_supervised.TryGetValue(sourceInstanceId, out var sup))
            {
                return;
            }
            await StopInternal(sup, cancellationToken).ConfigureAwait(false);
            _supervised.TryRemove(sourceInstanceId, out _);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stop the existing instance with the same id (if any) and add
    /// the new registration in a single atomic supervisor-level
    /// operation. Used by the M.P2.2 hot-reload coordinator when a
    /// source's configuration is modified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method holds <c>_lifecycleGate</c> for the WHOLE remove +
    /// add sequence. It calls <see cref="StopInternal"/> and
    /// <see cref="RegisterInternal"/> / <see cref="StartInternal"/>
    /// directly — never <see cref="RemoveAsync"/> or
    /// <see cref="AddAsync"/>, which would attempt to re-enter the
    /// gate and deadlock.
    /// </para>
    /// <para>
    /// If the source id is not currently supervised, this degenerates
    /// to an Add — preserves the idempotent-on-unknown symmetry with
    /// <see cref="RemoveAsync"/>.
    /// </para>
    /// </remarks>
    public async Task RestartAsync(SourceRegistration newReg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newReg);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var instanceId = newReg.Adapter.InstanceId;

            // Teardown half (skipped if no current entry).
            if (_supervised.TryGetValue(instanceId, out var existing))
            {
                await StopInternal(existing, cancellationToken).ConfigureAwait(false);
                _supervised.TryRemove(instanceId, out _);
            }

            // Bring-up half. Same rollback discipline as AddAsync.
            RegisterInternal(newReg);
            try
            {
                var sup = _supervised[instanceId];
                await StartInternal(sup, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _supervised.TryRemove(instanceId, out _);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Idempotent: first caller wins; subsequent callers no-op.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // If StopAsync was already invoked, _started is false and
            // StopInternal was called for each entry already (adapters
            // are stopped + disposed). Skip the loop to avoid double-
            // dispose. Otherwise tear down what's running.
            if (_started)
            {
                foreach (var sup in _supervised.Values)
                {
                    await StopInternal(sup, CancellationToken.None).ConfigureAwait(false);
                }
                _started = false;
            }
            _supervised.Clear();
        }
        finally
        {
            _lifecycleGate.Release();
        }
        _lifecycleGate.Dispose();
    }

    /// <summary>
    /// Build channel + SupervisedSource entry and put it in the map.
    /// No I/O, no adapter touch. Throws on duplicate id.
    /// </summary>
    /// <remarks>
    /// Used by the constructor today; phase 2.a step 2 adds public
    /// AddAsync which calls this followed by StartInternal.
    /// </remarks>
    private void RegisterInternal(SourceRegistration reg)
    {
        var channel = Channel.CreateBounded<CanonicalDataPoint>(
            new BoundedChannelOptions(IntakeChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        var sup = new SupervisedSource(reg, channel);
        if (!_supervised.TryAdd(reg.Adapter.InstanceId, sup))
        {
            throw new InvalidOperationException(
                $"Duplicate source instance id '{reg.Adapter.InstanceId}' in supervisor registrations.");
        }
    }

    /// <summary>
    /// Initialize the adapter, start it, record Running health, and
    /// launch the per-instance poll loop. Caller already holds the
    /// lifecycle gate and the map entry. Exceptions bubble — the
    /// caller is responsible for rollback (clean up the partially-
    /// supervised state if appropriate).
    /// </summary>
    private async Task StartInternal(SupervisedSource sup, CancellationToken parentCt)
    {
        await sup.Registration.Adapter
            .InitializeAsync(sup.Registration.Config, parentCt)
            .ConfigureAwait(false);
        await sup.Registration.Adapter
            .StartAsync(parentCt)
            .ConfigureAwait(false);

        _healthSink.RecordSourceState(
            sup.Registration.RouteId,
            sup.Registration.Adapter.InstanceId,
            sup.Registration.Adapter.ProtocolName,
            AdapterState.Running,
            lastError: null);

        // Per-instance CTS linked from the parent. Cancelling sup.Cts
        // cancels only THIS source's pump, not any others.
        sup.Cts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        var captured = sup;
        sup.PumpTask = Task.Run(
            () => RunPumpLoopAsync(captured, captured.Cts.Token),
            captured.Cts.Token);
    }

    /// <summary>
    /// Capability-aware pump dispatcher. Adapters advertising
    /// <see cref="SourceCapabilities.Subscription"/> drive their data
    /// path through <see cref="ISourceAdapter.SubscribeAsync"/>; all
    /// others go through the classic <see cref="ISourceAdapter.PollAsync"/>
    /// loop.
    /// </summary>
    /// <remarks>
    /// Until this method existed, the supervisor unconditionally
    /// called <c>PollAsync</c>, which threw
    /// <see cref="NotSupportedException"/> on subscription-mode
    /// adapters (OPC UA Client) — the loop would terminate silently,
    /// the adapter sat in Running state with notifications stranded
    /// in its dispatcher, and no points reached the route. Surfaced
    /// during the multi-protocol pilot's first integration test
    /// (3rd-party OPC server → EdgeConnect OPC Client source →
    /// EdgeConnect OPC Server sink → UaExpert chain).
    /// </remarks>
    private async Task RunPumpLoopAsync(SupervisedSource sup, CancellationToken ct)
    {
        var adapter = sup.Registration.Adapter;
        if ((adapter.Capabilities & SourceCapabilities.Subscription) != 0)
        {
            await RunSubscribeLoopAsync(sup, ct).ConfigureAwait(false);
        }
        else
        {
            await RunPollLoopAsync(sup, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Subscription-mode pump. Drains the adapter's
    /// <see cref="ISourceAdapter.SubscribeAsync"/> IAsyncEnumerable and
    /// writes each <see cref="CanonicalDataPoint"/> to the supervised
    /// channel. Records source observations per-point so the diagnostic
    /// surface (pointsObserved + lastPointAtUtc) ticks in real time.
    /// </summary>
    private async Task RunSubscribeLoopAsync(SupervisedSource sup, CancellationToken ct)
    {
        var adapter = sup.Registration.Adapter;
        var routeId = sup.Registration.RouteId;
        try
        {
            await foreach (var point in adapter.SubscribeAsync(ct)
                .WithCancellation(ct).ConfigureAwait(false))
            {
                try
                {
                    if (!await TryWriteIntakeAsync(sup, point, adapter.InstanceId, ct).ConfigureAwait(false))
                    {
                        continue; // downstream stalled — drop this point, keep streaming
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                _healthSink.RecordSourceObservation(
                    routeId,
                    adapter.InstanceId,
                    adapter.ProtocolName,
                    1,
                    DateTime.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (AdapterException ex)
        {
            // Per-adapter isolation: report Failed and exit THIS source's
            // loop. Other sources continue.
            _healthSink.RecordSourceState(
                routeId,
                adapter.InstanceId,
                adapter.ProtocolName,
                AdapterState.Failed,
                ex.Error);
            _logger.LogError(ex,
                "Source {Source} failed in subscribe loop; supervisor exiting that loop.",
                adapter.InstanceId);
            sup.Channel.Writer.TryComplete(ex);
        }
        finally
        {
            sup.Channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Cancel this instance's CTS, complete the writer, await the
    /// pump task with a bounded timeout, then stop and dispose the
    /// adapter. Does NOT remove the entry from the map — the caller
    /// (StopAsync, DisposeAsync, or the future RemoveAsync) decides
    /// removal. Idempotent on an already-stopped instance.
    /// </summary>
    private async Task StopInternal(SupervisedSource sup, CancellationToken ct)
    {
        // Cancel CTS + complete writer concurrently. Readers see
        // end-of-stream immediately; pump exits via cancellation.
        // The pump's own finally { TryComplete() } will be a no-op.
        if (sup.Cts is { } cts)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { /* CTS already disposed — race */ }
        }
        sup.Channel.Writer.TryComplete();

        // Bounded await on the pump. If it does not exit within the
        // ceiling, log and orphan the task — the pump's writer is
        // already completed and refs to its intake are dead; GC
        // reclaims when the task finally exits.
        if (sup.PumpTask is not null)
        {
            try
            {
                await sup.PumpTask
                    .WaitAsync(TimeSpan.FromMilliseconds(PerInstanceStopTimeoutMs), ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "Source pump for {Source} did not exit within {Timeout}ms; abandoning task.",
                    sup.Registration.Adapter.InstanceId, PerInstanceStopTimeoutMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Source pump for {Source} threw on stop.",
                    sup.Registration.Adapter.InstanceId);
            }
            sup.PumpTask = null;
        }

        // Bounded adapter.StopAsync. Enforces the graceful-stop
        // ceiling at the supervisor layer regardless of adapter
        // compliance. Reports Stopped health on success.
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stopCts.CancelAfter(TimeSpan.FromMilliseconds(PerInstanceStopTimeoutMs));
        try
        {
            await sup.Registration.Adapter.StopAsync(stopCts.Token).ConfigureAwait(false);
            _healthSink.RecordSourceState(
                sup.Registration.RouteId,
                sup.Registration.Adapter.InstanceId,
                sup.Registration.Adapter.ProtocolName,
                AdapterState.Stopped,
                lastError: null);
        }
        catch (OperationCanceledException) when (stopCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Source adapter {Source} StopAsync exceeded {Timeout}ms.",
                sup.Registration.Adapter.InstanceId, PerInstanceStopTimeoutMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Source adapter {Source} StopAsync threw.",
                sup.Registration.Adapter.InstanceId);
        }

        // Best-effort adapter dispose. Never blocks map removal.
        if (sup.Registration.Adapter is IAsyncDisposable d)
        {
            try { await d.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Source adapter {Source} DisposeAsync threw.",
                    sup.Registration.Adapter.InstanceId);
            }
        }

        // Per-instance CTS cleanup. Subsequent StopInternal calls
        // on this entry are no-ops (Cts is null; PumpTask is null).
        sup.Cts?.Dispose();
        sup.Cts = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    /// <summary>
    /// Backpressure-safe intake write. Waits for channel room (normal
    /// backpressure while the route worker drains), but does NOT block the
    /// source forever if the route worker has stalled/exited — store-and-forward
    /// must decouple them. Returns <c>false</c> when the write timed out, so the
    /// caller can stop pushing this batch and keep the source alive.
    /// </summary>
    private async Task<bool> TryWriteIntakeAsync(
        SupervisedSource sup, CanonicalDataPoint point, string sourceId, CancellationToken ct)
    {
        var writeTask = sup.Channel.Writer.WriteAsync(point, ct).AsTask();
        var finished = await Task.WhenAny(
            writeTask, Task.Delay(IntakeWriteStallMs, ct)).ConfigureAwait(false);
        if (finished != writeTask)
        {
            // Observe the abandoned write so it isn't an unobserved fault.
            _ = writeTask.ContinueWith(static t => { _ = t.Exception; }, TaskScheduler.Default);
            _logger.LogError(
                "Source {Source}: intake channel full and not draining for {Seconds}s — the route " +
                "worker has stalled/exited. Keeping source alive; dropping points until it recovers.",
                sourceId, IntakeWriteStallMs / 1000);
            return false;
        }
        await writeTask.ConfigureAwait(false);
        return true;
    }

    private async Task RunPollLoopAsync(SupervisedSource sup, CancellationToken ct)
    {
        var adapter = sup.Registration.Adapter;
        var routeId = sup.Registration.RouteId;
        // We recorded Running when the pump started; track it so we can push
        // subsequent transitions (e.g. Running -> Degraded when the device
        // becomes unreachable, and back to Running when it recovers) into the
        // health sink — otherwise the diagnostics surface would stay "Running"
        // forever even while the source reads nothing.
        var lastRecordedState = AdapterState.Running;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                IReadOnlyList<CanonicalDataPoint> batch;
                try
                {
                    batch = await adapter.PollAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (AdapterException ex)
                {
                    // Per-adapter isolation: report Failed and exit THIS
                    // source's loop. Other sources continue.
                    _healthSink.RecordSourceState(
                        routeId,
                        adapter.InstanceId,
                        adapter.ProtocolName,
                        AdapterState.Failed,
                        ex.Error);
                    _logger.LogError(ex, "Source {Source} failed in poll loop; supervisor exiting that loop.", adapter.InstanceId);
                    sup.Channel.Writer.TryComplete(ex);
                    return;
                }

                // Reflect the adapter's own state into diagnostics whenever it
                // changes (poll-mode adapters degrade/recover internally without
                // throwing). This is what turns a silently-not-reading source
                // from a misleading "Running" into "Degraded" + the connect
                // error, and flips it back to "Running" once data resumes.
                if (adapter.State != lastRecordedState)
                {
                    AdapterError? lastError = null;
                    try
                    {
                        lastError = (await adapter.CheckHealthAsync(ct).ConfigureAwait(false)).LastError;
                    }
                    catch (Exception healthEx)
                    {
                        _logger.LogDebug(healthEx, "Health read for {Source} failed while recording state change.", adapter.InstanceId);
                    }

                    _healthSink.RecordSourceState(
                        routeId,
                        adapter.InstanceId,
                        adapter.ProtocolName,
                        adapter.State,
                        lastError);
                    lastRecordedState = adapter.State;
                }

                if (batch.Count == 0)
                {
                    // Empty batch = "no data this tick" — NOT a completion
                    // signal. Per-group-timer adapters (Modbus is the canonical
                    // example) routinely return empty between scheduled scans;
                    // continuous-stream adapters (FOCAS2, MTConnect) typically
                    // never do. Either way, we keep polling until the loop
                    // is cancelled via StopAsync.
                    //
                    // We sleep briefly here so an adapter that returns empty
                    // back-to-back doesn't pin a CPU. Mock adapters previously
                    // used empty as their "test data exhausted" signal — that
                    // pattern is replaced by simply letting tests StopAsync
                    // once they've drained the points they need (DrainAsync
                    // doesn't depend on channel completion).
                    try
                    {
                        await Task.Delay(_idleSleepMs, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    continue;
                }

                foreach (var point in batch)
                {
                    try
                    {
                        if (!await TryWriteIntakeAsync(sup, point, adapter.InstanceId, ct).ConfigureAwait(false))
                        {
                            break; // downstream stalled — drop rest of batch, keep polling
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                _healthSink.RecordSourceObservation(
                    routeId,
                    adapter.InstanceId,
                    adapter.ProtocolName,
                    batch.Count,
                    DateTime.UtcNow);
            }
        }
        finally
        {
            sup.Channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Internal pairing of a registration with its bounded channel and
    /// the running pump task. Not exposed.
    /// </summary>
    private sealed class SupervisedSource
    {
        public SupervisedSource(SourceRegistration registration, Channel<CanonicalDataPoint> channel)
        {
            Registration = registration;
            Channel = channel;
            Intake = new SupervisedSourceIntake(registration.Adapter.InstanceId, channel.Reader);
        }

        public SourceRegistration Registration { get; }
        public Channel<CanonicalDataPoint> Channel { get; }
        public ISourceIntake Intake { get; }
        public Task? PumpTask { get; set; }

        /// <summary>
        /// Per-instance CancellationTokenSource introduced in M.P2.2
        /// phase 2.a. Created in <see cref="StartInternal"/> linked
        /// from the parent CT; cancelled + disposed in
        /// <see cref="StopInternal"/>.
        /// </summary>
        public CancellationTokenSource? Cts { get; set; }
    }

    /// <summary>Concrete <see cref="ISourceIntake"/> backed by a channel reader.</summary>
    private sealed class SupervisedSourceIntake : ISourceIntake
    {
        public SupervisedSourceIntake(string sourceInstanceId, ChannelReader<CanonicalDataPoint> reader)
        {
            SourceInstanceId = sourceInstanceId;
            Reader = reader;
        }

        public string SourceInstanceId { get; }
        public ChannelReader<CanonicalDataPoint> Reader { get; }
    }
}
