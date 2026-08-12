// ============================================================================
// File: Routing/Route.cs
// Purpose: Runtime handle for a single registered route. Owns the route's
//          buffer, fanout dispatcher, worker task, cancellation plumbing,
//          and its RouteLifecycleManager. Never decides a state transition
//          on its own — all transitions go through the lifecycle manager.
// Reference: ARCHITECTURE_BLUEPRINT.md §19, PHASE1_EXECUTION_PLAN.md C3
// Milestone: C3 Commit 2 (phase 5 — lifecycle centralized on
//            RouteLifecycleManager; Route no longer holds raw state)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Buffer;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Internal runtime state of a registered route. Constructed by the routing
/// engine; not part of the public API surface.
/// </summary>
internal sealed class Route : IAsyncDisposable
{
    public Route(
        RouteDefinition definition,
        IMessageBuffer buffer,
        FanoutDispatcher dispatcher,
        RouteLifecycleManager lifecycle,
        ReplayRouteContext? replay = null)
    {
        Definition = definition;
        Buffer = buffer;
        Dispatcher = dispatcher;
        Lifecycle = lifecycle;
        Replay = replay;
    }

    public RouteDefinition Definition { get; }
    public IMessageBuffer Buffer { get; }
    public FanoutDispatcher Dispatcher { get; }
    public RouteLifecycleManager Lifecycle { get; }

    /// <summary>
    /// The replay context when this route's single sink is replay-aware (K1.3); null for an
    /// ordinary route. Established at registration from the activation commit.
    /// </summary>
    public ReplayRouteContext? Replay { get; }

    public string RouteId => Definition.RouteId;

    public RouteState State => Lifecycle.State;

    public CancellationTokenSource? WorkerCts { get; set; }
    public Task? WorkerTask { get; set; }

    /// <summary>
    /// UTC time the current worker session was started. Used by the engine's
    /// self-heal supervisor to tell a fresh transient fault (ran a while, reset
    /// backoff) from a crash-loop (faults immediately, escalate backoff).
    /// </summary>
    public DateTime? WorkerStartedUtc { get; set; }

    /// <summary>
    /// Consecutive self-heal restart attempts since the route last ran healthily.
    /// Drives the capped exponential backoff; reset once a session runs long enough.
    /// </summary>
    public int RestartAttempts { get; set; }

    private int _pendingEndReason = (int)ReplaySessionEndReason.Stop;

    /// <summary>
    /// The reason to hand to a replay-aware sink's <see cref="IReplayAwareSinkAdapter.EndSessionAsync"/>
    /// on the NEXT graceful stop. Set by the engine BEFORE cancelling the worker so the replay driver
    /// reads an explicit reason during shutdown — never inferred from the cancellation itself (K1.3
    /// slice 5). Defaults to <see cref="ReplaySessionEndReason.Stop"/>; a config-replace teardown sets
    /// <see cref="ReplaySessionEndReason.ConfigurationReplaced"/>. Volatile: written on the stop caller's
    /// thread, read on the driver's shutdown path.
    /// </summary>
    public ReplaySessionEndReason PendingEndReason
    {
        get => (ReplaySessionEndReason)Volatile.Read(ref _pendingEndReason);
        set => Volatile.Write(ref _pendingEndReason, (int)value);
    }

    private readonly object _stopGate = new();

    /// <summary>
    /// Atomically claim this route's shutdown AND publish its end reason as ONE operation, so only the
    /// caller that wins the transition to <see cref="RouteState.Stopping"/> selects the reason the replay
    /// driver reads — a racing/later stop must not overwrite it (e.g. an ordinary
    /// <see cref="ReplaySessionEndReason.Stop"/> must not clobber a
    /// <see cref="ReplaySessionEndReason.ConfigurationReplaced"/> already in flight). Returns false when
    /// the route is already Stopping/Stopped or never started — the caller should then await the
    /// in-flight worker WITHOUT mutating the reason. K1.3 slice 5.
    /// </summary>
    /// <param name="reason">The end reason to publish if this caller wins the claim.</param>
    /// <returns>True if this caller claimed the stop (and set the reason); false otherwise.</returns>
    internal bool TryBeginStop(ReplaySessionEndReason reason)
    {
        lock (_stopGate)
        {
            if (Lifecycle.State is RouteState.Stopping or RouteState.Stopped or RouteState.Configured)
            {
                return false;
            }

            PendingEndReason = reason;
            Lifecycle.TryTransitionTo(RouteState.Stopping, $"stop requested: {reason}");
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        WorkerCts?.Dispose();
        Dispatcher.Dispose();
        await Buffer.DisposeAsync().ConfigureAwait(false);
    }
}
