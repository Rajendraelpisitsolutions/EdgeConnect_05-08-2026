// ============================================================================
// File: Routing/RoutingEngine.cs
// Purpose: Default IRoutingEngine implementation. Owns registered routes,
//          creates each route's buffer via IRouteBufferFactory, starts/stops
//          per-route workers, and routes lifecycle transitions through the
//          pinned RouteStateTransitionValidator table.
// Reference: ARCHITECTURE_BLUEPRINT.md §19 (§19.9 lifecycle),
//            PHASE1_EXECUTION_PLAN.md C3.
// Milestone: C3 Commit 2 (phase 1 — happy path registration/start/stop).
//
// Phase scope note:
//   This implementation covers the happy-path lifecycle
//   (Configured → Starting → Running → Stopping → Stopped). Failure-driven
//   transitions (Running → Degraded → Draining → Running, Failed, Blocked)
//   are added in phase 5 alongside RouteLifecycleManager.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Default in-process routing engine.
/// </summary>
public sealed class RoutingEngine : IRoutingEngine, IAsyncDisposable
{
    private readonly IRouteBufferFactory _bufferFactory;
    private readonly IRoutingEngineDiagnostics _diagnostics;
    private readonly object _gate = new();
    private readonly Dictionary<string, Route> _routes = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = new();

    // Route ids reserved by an in-flight RegisterRouteAsync (before the route is published). A
    // reservation is taken under _gate BEFORE any resource creation or the one-way replay
    // activation, so a concurrent registration for the same id is rejected up front — never after
    // activation. Guarded by _gate.
    private readonly HashSet<string> _registrationsInProgress = new(StringComparer.Ordinal);

    // Process-wide replay-session identity: survives route stop→start AND unregister → re-register
    // (a new Route object), so every replay driver start mints a globally-unique session id and a
    // stale lifecycle callback from a previous session is always distinguishable. K1.3 slice 3.
    private readonly ReplaySessionIdentitySource _replaySessionIdentity = new();

    /// <summary>Construct a routing engine with the required seams.</summary>
    /// <param name="bufferFactory">The route buffer factory.</param>
    /// <param name="diagnostics">Optional lifecycle diagnostics.</param>
    /// <param name="bufferHealthSink">Optional buffer-health gauge sink.</param>
    /// <param name="tap">Optional live data tap.</param>
    /// <param name="replayEndSessionTimeout">
    /// Bound on a replay-aware sink's graceful <c>EndSessionAsync</c> during shutdown, so a
    /// non-cooperative sink cannot wedge the stop chain. Defaults to
    /// <see cref="ReplayRouteDriver.DefaultEndSessionTimeout"/>. K1.3 slice 5.
    /// </param>
    /// <param name="autoRestartFaultedRoutes">
    /// When <see langword="true"/> (default), a faulted route worker is restarted on a
    /// capped exponential backoff so a transient fault self-heals instead of wedging the
    /// route. Set <see langword="false"/> to leave a faulted route dead (test isolation).
    /// </param>
    /// <param name="routeRestartBaseDelay">Initial self-heal backoff. Defaults to 2s.</param>
    /// <param name="routeRestartMaxDelay">Cap on the self-heal backoff. Defaults to 60s.</param>
    public RoutingEngine(
        IRouteBufferFactory bufferFactory,
        IRoutingEngineDiagnostics? diagnostics = null,
        Diagnostics.IBufferHealthSink? bufferHealthSink = null,
        Diagnostics.IRouteTap? tap = null,
        TimeSpan? replayEndSessionTimeout = null,
        bool autoRestartFaultedRoutes = true,
        TimeSpan? routeRestartBaseDelay = null,
        TimeSpan? routeRestartMaxDelay = null)
    {
        ArgumentNullException.ThrowIfNull(bufferFactory);
        _bufferFactory = bufferFactory;
        _diagnostics = diagnostics ?? NullRoutingEngineDiagnostics.Instance;
        _bufferHealthSink = bufferHealthSink;
        _tap = tap;
        _replayEndSessionTimeout = replayEndSessionTimeout ?? ReplayRouteDriver.DefaultEndSessionTimeout;
        _autoRestartFaultedRoutes = autoRestartFaultedRoutes;
        _routeRestartBaseDelay = routeRestartBaseDelay ?? TimeSpan.FromSeconds(2);
        _routeRestartMaxDelay = routeRestartMaxDelay ?? TimeSpan.FromSeconds(60);
    }

    // ─── Self-heal supervision ──────────────────────────────────────────
    // A route worker faults on any non-cooperative error (a transient buffer
    // I/O race, or a sink whose retries are exhausted). Without supervision the
    // route stays Failed forever and stops pumping data even after the underlying
    // condition clears — e.g. a momentary MQTT publish timeout would wedge the
    // route permanently. When enabled, ObserveWorkerFault schedules a restart on
    // a capped exponential backoff; a session that ran healthily before faulting
    // resets the backoff, a crash-loop escalates it up to the cap.
    private readonly bool _autoRestartFaultedRoutes;
    private readonly TimeSpan _routeRestartBaseDelay;
    private readonly TimeSpan _routeRestartMaxDelay;
    // A route is treated as "ran healthily" (backoff reset) once a session has
    // been up at least this long before faulting.
    private static readonly TimeSpan RestartBackoffResetAfter = TimeSpan.FromSeconds(30);
    private volatile bool _disposed;

    private readonly Diagnostics.IBufferHealthSink? _bufferHealthSink;
    private readonly Diagnostics.IRouteTap? _tap;
    private readonly TimeSpan _replayEndSessionTimeout;

    /// <inheritdoc />
    public IReadOnlyList<string> RegisteredRouteIds
    {
        get
        {
            lock (_gate)
            {
                return _registrationOrder.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public async Task RegisterRouteAsync(RouteDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Sinks.Count == 0)
        {
            throw new ArgumentException(
                $"Route '{definition.RouteId}' must have at least one sink.",
                nameof(definition));
        }

        // Replay-route classification + the cheap pre-buffer validation (cardinality + mode),
        // BEFORE any resource is created. Returns the single replay-aware sink, or null for an
        // ordinary route.
        var replayAwareSink = ClassifyReplayRoute(definition);

        // Reserve the route id under the gate BEFORE creating any resource or performing the
        // one-way replay activation. This makes the registry the single arbiter of the id up
        // front: a concurrent registration for the same id is rejected here — never after a
        // successful (irreversible) activation, which would otherwise strand a permanently-enabled
        // store behind a rolled-back registration or let a loser dispose the winner's buffer.
        // Correctness does NOT depend on the buffer factory's own locking (IRouteBufferFactory is
        // an abstraction; a custom/test factory may share a buffer or not lock at all).
        lock (_gate)
        {
            if (_routes.ContainsKey(definition.RouteId) || !_registrationsInProgress.Add(definition.RouteId))
            {
                throw new InvalidOperationException(
                    $"[{CoreErrors.RouteAlreadyRegistered}] Route '{definition.RouteId}' is already registered.");
            }
        }

        // From here the buffer (and dispatcher) are owned locally until the Route is published;
        // any failure disposes them (awaited) so no SQLite owner / lock / reclaim loop leaks.
        // Ownership transfers to the Route only on successful publish.
        IMessageBuffer? buffer = null;
        FanoutDispatcher? dispatcher = null;
        try
        {
            buffer = await _bufferFactory
                .CreateAsync(definition.RouteId, definition.BufferPolicy, ct)
                .ConfigureAwait(false);

            // Non-throwing preparation before activation.
            dispatcher = new FanoutDispatcher();
            foreach (var sink in definition.Sinks)
            {
                dispatcher.RegisterSink(sink.InstanceId);
            }

            // Capability gate + no-downgrade guard, then replay activation. Every fallible check
            // runs BEFORE the one-way activation, and the route id is already reserved — so this is
            // genuinely the FINAL fallible registration operation and the publish below has no
            // duplicate-race failure path.
            var replayContext = await EstablishReplayContextAsync(definition, buffer, replayAwareSink, ct)
                .ConfigureAwait(false);

            var lifecycle = new RouteLifecycleManager(definition.RouteId, _diagnostics);
            var route = new Route(definition, buffer, dispatcher, lifecycle, replayContext);

            lock (_gate)
            {
                // The reservation guarantees exclusivity — no duplicate check needed here.
                _routes[definition.RouteId] = route;
                _registrationOrder.Add(definition.RouteId);
            }

            // Ownership of the buffer + dispatcher has transferred to the published Route.
            buffer = null;
            dispatcher = null;
        }
        finally
        {
            // Release the reservation LAST — only after all locally-owned resources have finished
            // disposing. Releasing it before cleanup completes would let a retry (or a concurrent
            // registration) acquire the id while a failed attempt is still disposing a shared
            // buffer, reintroducing the very race the reservation closes. The outer finally still
            // releases the reservation even if disposal throws.
            try
            {
                // Dispose anything not transferred to a published Route. dispatcher.Dispose() is
                // wrapped so a throw there cannot skip the (awaited) buffer disposal — the SQLite
                // owner / lock / reclaim loop must never leak.
                try
                {
                    dispatcher?.Dispose();
                }
                finally
                {
                    if (buffer is not null)
                    {
                        await buffer.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _registrationsInProgress.Remove(definition.RouteId);
                }
            }
        }
    }

    /// <summary>
    /// Classify a route as replay-aware and run the cheap pre-buffer validation. A route is
    /// replay-aware when a sink implements <see cref="IReplayAwareSinkAdapter"/>; such a route
    /// must have exactly one sink (it owns the protected replay cursor) and use
    /// <see cref="BufferMode.StoreAndForward"/>. Returns the replay-aware sink, or null for an
    /// ordinary route. Fails closed (typed) on a cardinality/mode violation.
    /// </summary>
    private static IReplayAwareSinkAdapter? ClassifyReplayRoute(RouteDefinition definition)
    {
        IReplayAwareSinkAdapter? replayAware = null;
        foreach (var sink in definition.Sinks)
        {
            if (sink is IReplayAwareSinkAdapter ra)
            {
                replayAware = ra;
                break;
            }
        }

        if (replayAware is null)
        {
            return null;
        }

        if (definition.Sinks.Count != 1)
        {
            throw ReplayRouteConfigurationException.RequiresSingleSink(definition.RouteId);
        }

        if (definition.BufferPolicy.Mode != BufferMode.StoreAndForward)
        {
            throw ReplayRouteConfigurationException.RequiresStoreAndForward(
                definition.RouteId, definition.BufferPolicy.Mode);
        }

        return replayAware;
    }

    /// <summary>
    /// Establish the replay context for a route (or confirm an ordinary route is safe). For an
    /// ordinary route, fails closed if the resolved buffer's store is already replay-enabled (no
    /// silent downgrade). For a replay route, requires the buffer to be replay-capable and then
    /// activates — the commit boundary — returning the fixed generation + capture providers.
    /// </summary>
    private static async Task<ReplayRouteContext?> EstablishReplayContextAsync(
        RouteDefinition definition,
        IMessageBuffer buffer,
        IReplayAwareSinkAdapter? replayAwareSink,
        CancellationToken ct)
    {
        var replayBuffer = buffer as IReplayRouteBuffer;

        if (replayAwareSink is null)
        {
            // Ordinary route: an already-enabled replay store must not silently downgrade to the
            // legacy enqueue path (which the enabled store would later reject).
            if (replayBuffer is { IsReplayTrackingEnabled: true })
            {
                throw ReplayRouteConfigurationException.AutomaticDowngradeNotAllowed(definition.RouteId);
            }

            return null;
        }

        // Replay route: the buffer must be replay-capable.
        if (replayBuffer is null)
        {
            throw ReplayRouteConfigurationException.BufferNotReplayCapable(definition.RouteId);
        }

        var activation = await replayBuffer
            .ActivateReplayAsync(definition.RouteId, replayAwareSink.InstanceId, ct)
            .ConfigureAwait(false);

        // Internal-invariant guard: a successful activation returns both providers. This is
        // structurally impossible for SqliteBuffer (ReplayRouteActivation's fields are non-nullable
        // and it returns the owner for both), so it defends only against a broken capability
        // implementation. Note it runs AFTER the one-way activation, so it is NOT rollback-safe — a
        // buggy impl that returned a null provider would leave the store enabled; that is a
        // programming defect, not a recoverable configuration failure.
        if (activation.BoundaryProvider is null || activation.SessionStateProvider is null)
        {
            throw ReplayRouteConfigurationException.IncompleteActivation(definition.RouteId);
        }

        return new ReplayRouteContext(
            replayBuffer,
            replayAwareSink,
            replayAwareSink.InstanceId,
            activation.Generation,
            activation.BoundaryProvider,
            activation.SessionStateProvider);
    }

    /// <inheritdoc />
    public Task StartRouteAsync(string routeId, CancellationToken ct)
    {
        var route = GetRouteOrThrow(routeId);

        // The whole start sequence runs under _gate so concurrent Start
        // calls cannot both spawn a worker. Task.Run returns immediately;
        // we never await inside the lock.
        lock (_gate)
        {
            // Idempotent on already-starting / already-running.
            var current = route.State;
            if (current is RouteState.Starting or RouteState.Running)
            {
                return Task.CompletedTask;
            }

            // All transition enforcement lives in the lifecycle manager.
            // Invalid transitions (e.g. starting from Failed) throw from
            // TryTransitionTo.
            route.Lifecycle.TryTransitionTo(RouteState.Starting, "start requested");

            // Dispose any leftover CTS from a previous Start (resource
            // hygiene — prevents leaks across Stop → Start cycles).
            route.WorkerCts?.Dispose();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            route.WorkerCts = cts;

            // Reset the pending end reason to Stop at each session start, so a prior stop's
            // ConfigurationReplaced reason cannot leak into an unrelated later stop (defensive — a stop
            // always sets the reason atomically via TryBeginStop).
            route.PendingEndReason = ReplaySessionEndReason.Stop;

            var worker = new RouteWorker(
                route, _diagnostics, route.Dispatcher, _replaySessionIdentity, _replayEndSessionTimeout, _bufferHealthSink, _tap);

            // Stamp the session start so the self-heal supervisor can distinguish
            // a transient fault after a healthy run from an immediate crash-loop.
            route.WorkerStartedUtc = DateTime.UtcNow;

            // Worker-fault observation (Bug 2, P0). The worker is fire-and-
            // forget from the caller's perspective, so an unobserved
            // exception would silently leave the route reporting Running
            // while no data flows — load-bearing invariant violation.
            // Wrap RunAsync so any non-cancellation exception transitions
            // the route to Failed and the reason carries the exception
            // type + message for operator forensics.
            route.WorkerTask = Task.Run(async () =>
            {
                try
                {
                    await worker.RunAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Cooperative shutdown — Stop/Unregister/Dispose path.
                }
                catch (Exception ex)
                {
                    ObserveWorkerFault(route, ex);
                }
            }, cts.Token);

            route.Lifecycle.TryTransitionTo(RouteState.Running, "worker started");
        }

        return Task.CompletedTask;
    }

    private void ObserveWorkerFault(Route route, Exception ex)
    {
        // The worker task crashed outside cooperative cancellation. Push
        // the route to Failed so operators see "Running but actually
        // dead" become "Failed with reason" instead. Tolerate the
        // transition being illegal (e.g. the route happens to already be
        // Stopping because Stop was called concurrently) — losing the
        // race to a deliberate teardown is fine and not a re-throwable
        // error here.
        var reason = $"worker task faulted: {ex.GetType().Name}: {ex.Message}";
        try
        {
            route.Lifecycle.TryTransitionTo(RouteState.Failed, reason);
        }
        catch (InvalidOperationException)
        {
            // Lifecycle rejected the transition (race with shutdown);
            // the fault is still surfaced via the unhandled-exception
            // crash log on stderr below, so the operator isn't blind.
        }

        // Last-resort visibility: Core has no ILogger seam by design
        // (kept protocol- and host-agnostic). Write the stack to stderr
        // so the fault is captured by whatever log pipeline the host
        // attaches to standard streams. Host implementations that want
        // structured logs already get the route-state-changed event
        // through IRoutingEngineDiagnostics and can correlate from there.
        Console.Error.WriteLine(
            $"[routing] Route '{route.RouteId}' worker faulted: {ex}");

        // Supervised self-heal: bring the route back after a backoff instead of
        // leaving it dead. A transient fault (buffer I/O race, exhausted sink
        // retries) should not wedge the route permanently.
        ScheduleSelfHeal(route);
    }

    /// <summary>
    /// Schedule a backoff restart of a just-faulted route. Fire-and-forget: the
    /// delay + restart run off the fault-observation path so the worker task can
    /// unwind. No-op when auto-restart is disabled or the engine is disposing.
    /// </summary>
    private void ScheduleSelfHeal(Route route)
    {
        if (!_autoRestartFaultedRoutes || _disposed)
        {
            return;
        }

        // A session that ran at least RestartBackoffResetAfter before faulting is
        // a fresh transient fault — restart quickly. A route that faults sooner is
        // crash-looping, so escalate the backoff toward the cap.
        var ranFor = route.WorkerStartedUtc is { } started
            ? DateTime.UtcNow - started
            : TimeSpan.Zero;
        var attempt = ranFor >= RestartBackoffResetAfter ? 1 : route.RestartAttempts + 1;
        route.RestartAttempts = attempt;

        // base * 2^(attempt-1), capped. attempt is clamped so the shift can't overflow.
        var shift = Math.Min(attempt - 1, 16);
        var delayTicks = Math.Min(_routeRestartBaseDelay.Ticks * (1L << shift), _routeRestartMaxDelay.Ticks);
        var delay = TimeSpan.FromTicks(Math.Max(delayTicks, 0));

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay).ConfigureAwait(false); }
            catch { return; }
            await TryRestartFaultedRouteAsync(route.RouteId).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Restart a route that is still Failed. Resets it to Stopped via the legal
    /// lifecycle path (Failed → Stopping → Stopped) then starts a fresh worker.
    /// Skips if the engine is disposing, the route was unregistered, or it already
    /// left the Failed state (recovered, stopped, or replaced meanwhile).
    /// <para>Internal (not private) so the self-heal behaviour can be verified
    /// deterministically without racing the timed background scheduler.</para>
    /// </summary>
    internal async Task TryRestartFaultedRouteAsync(string routeId)
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || !_routes.TryGetValue(routeId, out var route))
            {
                return; // engine torn down or route removed
            }
            if (route.State != RouteState.Failed)
            {
                return; // deliberately stopped / replaced / already recovered
            }

            // Walk the route back to Configured via the only legal path
            // (Failed → Stopping → Stopped → Configured). StartRouteAsync starts
            // from Configured — Stopped → Starting is NOT a valid transition, so
            // resetting only as far as Stopped would leave the restart a no-op.
            try
            {
                route.Lifecycle.TryTransitionTo(RouteState.Stopping, "self-heal restart");
                route.Lifecycle.TryTransitionTo(RouteState.Stopped, "self-heal restart");
                route.Lifecycle.TryTransitionTo(RouteState.Configured, "self-heal restart");
            }
            catch (InvalidOperationException)
            {
                return; // lost a race with a concurrent stop/teardown
            }
        }

        try
        {
            await StartRouteAsync(routeId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // A restart that itself fails re-enters ObserveWorkerFault (which
            // reschedules), or the route was concurrently removed. Either way the
            // supervisor stays best-effort and never throws on this path.
        }
    }

    /// <inheritdoc />
    public Task StopRouteAsync(string routeId, CancellationToken ct)
        => StopRouteAsync(routeId, ReplaySessionEndReason.Stop, ct);

    /// <inheritdoc />
    public async Task StopRouteAsync(string routeId, ReplaySessionEndReason reason, CancellationToken ct)
    {
        var route = GetRouteOrThrow(routeId);

        // Atomically claim the stop AND publish the reason (happens-before the Cancel below, so the
        // driver reads it on shutdown — never inferred from the cancellation). Only the WINNER selects
        // the reason: a racing/later caller (already Stopping/Stopped/never-started) falls through to
        // await the in-flight worker WITHOUT clobbering the winner's reason.
        if (!route.TryBeginStop(reason))
        {
            await AwaitWorkerQuietlyAsync(route).ConfigureAwait(false);
            return;
        }

        route.WorkerCts?.Cancel();
        // Nudge any sink loops currently parked on a dispatcher wait so they
        // observe the cancellation promptly.
        route.Dispatcher.NotifyAll();
        await AwaitWorkerQuietlyAsync(route).ConfigureAwait(false);

        // The worker can FAULT during shutdown — Stopping → Failed is a legal
        // transition — so by the time we get here the route may be Failed rather
        // than Stopping. Forcing Failed → Stopped is illegal and would throw an
        // unhandled InvalidOperationException that crashes the host mid-shutdown.
        // Reach Stopped via the legal path for a failed route (Failed → Stopping
        // → Stopped) instead. TryTransitionTo is a safe no-op if already Stopped.
        if (route.State == RouteState.Failed)
        {
            route.Lifecycle.TryTransitionTo(
                RouteState.Stopping, "stop requested (route faulted during shutdown)");
        }
        route.Lifecycle.TryTransitionTo(RouteState.Stopped, "worker stopped");
    }

    private static async Task AwaitWorkerQuietlyAsync(Route route)
    {
        if (route.WorkerTask is not null)
        {
            try
            {
                await route.WorkerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — cooperative shutdown.
            }
        }
    }

    /// <inheritdoc />
    public Task UnregisterRouteAsync(string routeId, CancellationToken ct)
        => UnregisterRouteAsync(routeId, ReplaySessionEndReason.Stop, ct);

    /// <inheritdoc />
    public async Task UnregisterRouteAsync(string routeId, ReplaySessionEndReason reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);

        // Idempotent on unknown id — the coordinator calling us may emit
        // a Remove operation for a route that was never successfully
        // registered (e.g., boot-time fault). No-op + return.
        Route? route;
        lock (_gate)
        {
            if (!_routes.TryGetValue(routeId, out route))
            {
                return;
            }
        }

        // Stop first (transitions Running → Stopping → Stopped) with the given end reason. Reuses the
        // existing StopRouteAsync logic so we don't duplicate the worker-cancellation + dispatcher-
        // wakeup choreography. Safe to call when already Stopped (StopRouteAsync is idempotent).
        try
        {
            await StopRouteAsync(routeId, reason, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            // Lost a race with another concurrent unregister — the route
            // already left the engine. Continue to the dispose step
            // (which will no-op since the route was already removed).
        }

        // Remove from the registration map BEFORE disposing so a concurrent
        // RegisteredRouteIds query never observes a torn route.
        lock (_gate)
        {
            if (!_routes.Remove(routeId, out route))
            {
                return;
            }
            _registrationOrder.Remove(routeId);
        }

        // Dispose the route's buffer + dispatcher + worker resources.
        // DisposeAsync swallows OperationCanceledException internally.
        await route!.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAllAsync(CancellationToken ct)
    {
        foreach (var id in RegisteredRouteIds)
        {
            await StartRouteAsync(id, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync(CancellationToken ct)
    {
        foreach (var id in RegisteredRouteIds)
        {
            try
            {
                await StopRouteAsync(id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Per-route isolation on shutdown: one route that fails to stop
                // cleanly must NOT abort stopping the remaining routes or crash
                // the host's shutdown sequence (this path runs from
                // HostStartup.StopAsync). Surface and continue — mirrors the
                // resilient DisposeAsync teardown loop.
                Console.Error.WriteLine(
                    $"[routing] Route '{id}' failed to stop cleanly during StopAll: {ex}");
            }
        }
    }

    /// <inheritdoc />
    public RouteState GetRouteState(string routeId)
    {
        var route = GetRouteOrThrow(routeId);
        return route.State;
    }

    /// <summary>Dispose all routes and their buffers.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true; // stop the self-heal supervisor from restarting routes mid-teardown
        List<Route> routes;
        lock (_gate)
        {
            routes = new List<Route>(_routes.Values);
            _routes.Clear();
            _registrationOrder.Clear();
        }

        foreach (var route in routes)
        {
            try
            {
                route.WorkerCts?.Cancel();
                if (route.WorkerTask is not null)
                {
                    try
                    {
                        await route.WorkerTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (ObjectDisposedException)
                    {
                        // Defense-in-depth: if the buffer's ODE escapes the
                        // sink loop (RouteWorker is the primary handler),
                        // absorb it here so remaining routes still get
                        // disposed. This is a final cleanup shield, not the
                        // primary fix.
                    }
                }
            }
            finally
            {
                await route.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private Route GetRouteOrThrow(string routeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        lock (_gate)
        {
            if (!_routes.TryGetValue(routeId, out var route))
            {
                throw new KeyNotFoundException(
                    $"[{CoreErrors.RouteNotFound}] Route '{routeId}' is not registered.");
            }
            return route;
        }
    }

}
