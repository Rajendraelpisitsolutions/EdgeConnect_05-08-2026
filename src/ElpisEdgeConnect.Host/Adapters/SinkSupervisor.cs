// ============================================================================
// File: Adapters/SinkSupervisor.cs
// Purpose: Owns the lifecycle of every registered ISinkAdapter and pushes
//          adapter-level state into the diagnostics collector. The routing
//          engine is the one that calls PublishAsync; the supervisor only
//          owns Initialize / Start / Stop / health reporting.
//
//          Driven explicitly by HostStartup during the StartSourceSupervisor
//          phase (see HostStartup for the locked rationale around the phase
//          name covering both supervisors).
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D, ARCHITECTURE_BLUEPRINT.md §4.3
// Milestone: D — phase 4.
//
// LOCKED design rules:
//   * Per-adapter isolation: a failure initializing or starting one sink
//     reports it Failed via diagnostics and continues with the rest. The
//     supervisor never aborts the host on a single sink failure.
//   * The supervisor pushes ONE adapter-state event per state change
//     (Running on start, Stopped on stop, Failed on exception). Publish-
//     level state is reported separately by SinkPublisher inside the
//     routing engine via IRoutingEngineDiagnostics — single-source-of-
//     truth pin already enforced by C4.
//
//   * M.P2.2 phase 2.a additions (internal mechanics):
//       - _registrations is now ConcurrentDictionary<string,
//         SinkRegistration> keyed by InstanceId. Mirrors SourceSupervisor's
//         shape so AddAsync/RemoveAsync/RestartAsync semantics are
//         symmetric across both supervisors.
//       - _lifecycleGate (SemaphoreSlim(1,1)) serializes mutating public
//         methods: StartAsync, StopAsync, AddAsync, RemoveAsync,
//         RestartAsync, DisposeAsync. Read-only paths (Registrations)
//         remain unblocked.
//       - _disposed flag (Interlocked.CompareExchange) gives DisposeAsync
//         idempotency; ThrowIfDisposed guards mutating methods.
//       - StopInternal applies a 10s bounded timeout per adapter so a
//         single stuck Adapter.StopAsync cannot hang shutdown.
//       - Boot-time per-adapter isolation (the try/catch around the
//         Initialize+Start sequence) is preserved EXACTLY — moved
//         around the call to StartInternal. Hot-reload AddAsync does
//         NOT wrap StartInternal; exceptions bubble to the coordinator
//         (Phase 2.c) which catches in TryWithFaultAsync.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Errors;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host.Adapters;

/// <summary>
/// Supervises constructed sink adapters: lifecycle and adapter-level
/// health push. Single instance per gateway.
/// </summary>
public sealed class SinkSupervisor : IAsyncDisposable
{
    /// <summary>
    /// Per-adapter bounded ceiling for Adapter.StopAsync. Matches the
    /// SourceSupervisor ceiling; enforces the ISinkAdapter graceful-stop
    /// contract at the supervisor layer regardless of adapter compliance.
    /// </summary>
    private const int PerInstanceStopTimeoutMs = 10_000;

    private readonly ConcurrentDictionary<string, SinkRegistration> _registrations =
        new(StringComparer.Ordinal);
    private readonly ISinkHealthSink _healthSink;
    private readonly ILogger<SinkSupervisor> _logger;

    /// <summary>
    /// Serializes mutating public methods. Read-only access
    /// (<see cref="Registrations"/>) bypasses this and relies on
    /// ConcurrentDictionary semantics.
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    /// 0 = alive; 1 = DisposeAsync has run (or is running). Set via
    /// Interlocked.CompareExchange so DisposeAsync is idempotent.
    /// </summary>
    private int _disposed;

    private bool _started;

    /// <summary>Construct the supervisor with the registrations and the diagnostics push seam.</summary>
    public SinkSupervisor(
        IEnumerable<SinkRegistration> registrations,
        ISinkHealthSink healthSink,
        ILogger<SinkSupervisor> logger)
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

    /// <summary>
    /// Snapshot of the current sink registrations. Used by the coordinator
    /// (Phase 2.c) to inspect which sinks are currently supervised; not a
    /// live view (concurrent Add/Remove may produce a slightly stale list).
    /// </summary>
    public IReadOnlyList<SinkRegistration> Registrations => _registrations.Values.ToArray();

    /// <summary>
    /// Initialize and start every registered sink adapter. Per-adapter
    /// isolation: a failure on one sink does not stop the others.
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
            foreach (var reg in _registrations.Values)
            {
                // LOCKED: boot-time per-adapter isolation. A failed sink
                // records Failed health and the loop continues to the
                // next sink. Hot-reload AddAsync intentionally does NOT
                // wrap StartInternal — the coordinator catches there.
                try
                {
                    await StartInternal(reg, cancellationToken).ConfigureAwait(false);
                }
                catch (AdapterException ex)
                {
                    _healthSink.RecordSinkAdapterState(
                        reg.RouteId,
                        reg.Adapter.InstanceId,
                        AdapterState.Failed,
                        ex.Error);
                    _logger.LogError(ex, "Sink adapter {Sink} failed to start.", reg.Adapter.InstanceId);
                }
                catch (Exception ex)
                {
                    _healthSink.RecordSinkAdapterState(
                        reg.RouteId,
                        reg.Adapter.InstanceId,
                        AdapterState.Failed,
                        new AdapterError
                        {
                            Code = "HOST.SINK_START_THREW",
                            Category = ErrorCategory.Internal,
                            Message = ex.Message,
                        });
                    _logger.LogError(ex, "Sink adapter {Sink} threw on start.", reg.Adapter.InstanceId);
                }
            }
            _started = true;
            _logger.LogInformation("Sink supervisor started {Count} sinks.", _registrations.Count);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stop every registered sink adapter and report Stopped state to
    /// the diagnostics collector.
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
            foreach (var reg in _registrations.Values)
            {
                await StopInternal(reg, cancellationToken).ConfigureAwait(false);
            }
            _started = false;
            _logger.LogInformation("Sink supervisor stopped.");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Add a brand-new sink instance and start its adapter. Used by the
    /// M.P2.2 hot-reload coordinator when a configuration apply
    /// introduces a new destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> if
    /// <paramref name="reg"/>'s instance id is already supervised.
    /// </para>
    /// <para>
    /// Unlike boot-time <see cref="StartAsync"/>, this method does NOT
    /// catch adapter exceptions internally. Exceptions bubble to the
    /// coordinator which catches in its per-instance try/wrap and
    /// registers a <c>ConfigurationFault</c>. If
    /// <see cref="StartInternal"/> throws, the partially-added map
    /// entry is rolled back so the
    /// <c>_registrations == actually running</c> invariant is preserved.
    /// </para>
    /// </remarks>
    public async Task AddAsync(SinkRegistration reg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reg);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            // Throws on duplicate id (preserved from RegisterInternal).
            RegisterInternal(reg);

            try
            {
                await StartInternal(reg, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _registrations.TryRemove(reg.Adapter.InstanceId, out _);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stop the named sink's adapter and remove it from the supervisor.
    /// Idempotent: an unknown id is a silent no-op. Used by the M.P2.2
    /// hot-reload coordinator when a configuration apply removes a
    /// destination OR a sink becomes unreferenced.
    /// </summary>
    /// <remarks>
    /// The supervisor TRUSTS the caller: this method does NOT peek at
    /// configuration to decide "is this sink still referenced by other
    /// routes." That decision belongs to the coordinator (ADR-0009
    /// Decision 2; Phase 2 design correction #2). The supervisor only
    /// answers "stop this instance."
    /// </remarks>
    public async Task RemoveAsync(string sinkInstanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkInstanceId);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_registrations.TryGetValue(sinkInstanceId, out var reg))
            {
                return;
            }
            await StopInternal(reg, cancellationToken).ConfigureAwait(false);
            _registrations.TryRemove(sinkInstanceId, out _);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stop the existing instance with the same id (if any) and add the
    /// new registration in a single atomic supervisor-level operation.
    /// Used when a sink's configuration is modified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds <c>_lifecycleGate</c> for the WHOLE remove + add sequence.
    /// Calls <see cref="StopInternal"/> / <see cref="RegisterInternal"/>
    /// / <see cref="StartInternal"/> directly — never public
    /// <see cref="RemoveAsync"/> or <see cref="AddAsync"/>, which would
    /// attempt to re-enter the gate and deadlock.
    /// </para>
    /// <para>
    /// If the sink id is not currently supervised, this degenerates to
    /// an Add — preserves the idempotent-on-unknown symmetry with
    /// <see cref="RemoveAsync"/>.
    /// </para>
    /// </remarks>
    public async Task RestartAsync(SinkRegistration newReg, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newReg);
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var instanceId = newReg.Adapter.InstanceId;

            if (_registrations.TryGetValue(instanceId, out var existing))
            {
                await StopInternal(existing, cancellationToken).ConfigureAwait(false);
                _registrations.TryRemove(instanceId, out _);
            }

            RegisterInternal(newReg);
            try
            {
                await StartInternal(newReg, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _registrations.TryRemove(instanceId, out _);
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
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // If StopAsync was already invoked, _started is false and
            // StopInternal was called for each entry (adapters stopped
            // + disposed). Skip the loop to avoid double-dispose.
            if (_started)
            {
                foreach (var reg in _registrations.Values)
                {
                    await StopInternal(reg, CancellationToken.None).ConfigureAwait(false);
                }
                _started = false;
            }
            _registrations.Clear();
        }
        finally
        {
            _lifecycleGate.Release();
        }
        _lifecycleGate.Dispose();
    }

    /// <summary>
    /// Add the registration to the map. Throws on duplicate id with the
    /// same error message the constructor used to throw.
    /// </summary>
    private void RegisterInternal(SinkRegistration reg)
    {
        if (!_registrations.TryAdd(reg.Adapter.InstanceId, reg))
        {
            throw new InvalidOperationException(
                $"Duplicate sink instance id '{reg.Adapter.InstanceId}' in supervisor registrations.");
        }
    }

    /// <summary>
    /// Initialize the adapter, start it, and record Running health.
    /// Caller already holds the lifecycle gate. Exceptions bubble —
    /// boot <see cref="StartAsync"/> catches them for per-adapter
    /// isolation; <see cref="AddAsync"/> lets them propagate to the
    /// coordinator.
    /// </summary>
    private async Task StartInternal(SinkRegistration reg, CancellationToken cancellationToken)
    {
        await reg.Adapter.InitializeAsync(reg.Config, cancellationToken).ConfigureAwait(false);
        await reg.Adapter.StartAsync(cancellationToken).ConfigureAwait(false);
        _healthSink.RecordSinkAdapterState(
            reg.RouteId,
            reg.Adapter.InstanceId,
            AdapterState.Running,
            lastError: null);
    }

    /// <summary>
    /// Bounded adapter stop + record Stopped health + best-effort
    /// dispose. Does NOT remove the entry from the map — caller does.
    /// Idempotent on adapter failures (logs warning, continues).
    /// </summary>
    private async Task StopInternal(SinkRegistration reg, CancellationToken cancellationToken)
    {
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(TimeSpan.FromMilliseconds(PerInstanceStopTimeoutMs));
        try
        {
            await reg.Adapter.StopAsync(stopCts.Token).ConfigureAwait(false);
            _healthSink.RecordSinkAdapterState(
                reg.RouteId,
                reg.Adapter.InstanceId,
                AdapterState.Stopped,
                lastError: null);
        }
        catch (OperationCanceledException) when (stopCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Sink adapter {Sink} StopAsync exceeded {Timeout}ms.",
                reg.Adapter.InstanceId, PerInstanceStopTimeoutMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sink adapter {Sink} StopAsync threw.", reg.Adapter.InstanceId);
        }

        // Best-effort adapter dispose. Never blocks map removal.
        if (reg.Adapter is IAsyncDisposable d)
        {
            try { await d.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sink adapter {Sink} DisposeAsync threw.", reg.Adapter.InstanceId);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
