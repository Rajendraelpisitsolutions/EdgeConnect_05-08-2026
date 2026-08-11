// ============================================================================
// File: OpcUaReconnectCoordinator.cs
// Purpose: Coordinates session-recovery: subscribes to Session
//          KeepAliveEvent, triggers reconnect via the wrapper, and
//          re-wires FastDataChangeCallback on the new subscriptions.
//          Emits state-transition events the adapter consumes for
//          AdapterState transitions Running → Degraded → Running.
//
// LOCKED architectural invariants (per PR 6a plan + amendments):
//
//   1. Single coordinator instance per adapter — lifetime tied to
//      adapter's StartAsync/StopAsync
//   2. Reconnect lifecycle:
//        Running → Degraded   (keep-alive failure detected)
//        Degraded → Running   (reconnect succeeds: Transfer or Recreate)
//        Degraded → Failed    (reconnect retries exhausted)
//   3. Counter discipline (separate Transfer / Recreate per amendment):
//        reconnectsViaTransfer  — happy path; no notification loss expected
//        reconnectsViaRecreate  — server-side state lost; some loss possible
//        currentlyReconnecting  — bool while in flight
//        lastSuccessfulReconnectUtc (amendment #1) — operator diagnostic
//        lastReconnectMode      (amendment #2) — Transfer / Recreate / Unknown
//   4. Notification continuity — the SAME dispatcher instance receives
//      callbacks from the reconnected subscriptions. Channel doesn't get
//      destroyed; backlog drains through during reconnect.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3, §1.3.5, §2.5
//            PR 6a plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Coordinates session-recovery for a running OPC UA Client adapter.
/// </summary>
internal sealed class OpcUaReconnectCoordinator : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly IOpcUaReconnectHandlerWrapper _wrapper;
    private readonly Action<ReconnectResult> _onReconnectCallback;

    private ISession? _session;

    // Counters — read via Interlocked.Read; written via Interlocked.Increment.
    private long _reconnectsViaTransfer;
    private long _reconnectsViaRecreate;
    private long _reconnectsFailed;
    private int _currentlyReconnecting;
    private DateTime? _lastSuccessfulReconnectUtc;
    private ReconnectMode _lastReconnectMode = ReconnectMode.Unknown;

    public OpcUaReconnectCoordinator(
        IOpcUaReconnectHandlerWrapper wrapper,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(wrapper);
        ArgumentNullException.ThrowIfNull(logger);
        _wrapper = wrapper;
        _logger = logger;
        _onReconnectCallback = OnReconnectCompleted;
        _wrapper.ReconnectCompleted += _onReconnectCallback;
    }

    /// <summary>
    /// Fired when the coordinator's state changes (entering or leaving
    /// reconnect). The adapter subscribes to drive its
    /// <see cref="Core.Adapters.AdapterState"/> transitions.
    /// </summary>
    public event Action<CoordinatorStateChange>? StateChanged;

    /// <summary>
    /// Attach the coordinator to a freshly-opened session. Subscribes
    /// to <c>ISession.KeepAlive</c> so the next bad keep-alive triggers
    /// a reconnect attempt.
    /// </summary>
    public void Attach(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        session.KeepAlive += OnKeepAlive;
    }

    /// <summary>
    /// Detach from the current session (called by the adapter's
    /// StopAsync before tearing down).
    /// </summary>
    public void Detach()
    {
        if (_session is { } session)
        {
            try { session.KeepAlive -= OnKeepAlive; } catch { /* best-effort */ }
            _session = null;
        }
    }

    /// <summary>Snapshot of counters surfaced as adapter health metrics.</summary>
    public ReconnectCoordinatorCounters GetCounters() => new()
    {
        ReconnectsViaTransfer = Interlocked.Read(ref _reconnectsViaTransfer),
        ReconnectsViaRecreate = Interlocked.Read(ref _reconnectsViaRecreate),
        ReconnectsFailed = Interlocked.Read(ref _reconnectsFailed),
        CurrentlyReconnecting = Interlocked.CompareExchange(ref _currentlyReconnecting, 0, 0) != 0,
        LastSuccessfulReconnectUtc = _lastSuccessfulReconnectUtc,
        LastReconnectMode = _lastReconnectMode,
    };

    /// <summary>
    /// Stack-side keep-alive event handler. A bad keep-alive (Status
    /// indicates connection problem) triggers a reconnect attempt.
    /// </summary>
    private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        if (ServiceResult.IsNotBad(e.Status))
        {
            // Healthy keep-alive — no action.
            return;
        }

        _logger.LogWarning(
            "OPC UA session keep-alive returned bad status {Status}; beginning reconnect.",
            e.Status);

        if (Interlocked.CompareExchange(ref _currentlyReconnecting, 1, 0) != 0)
        {
            // Already reconnecting — skip duplicate trigger.
            return;
        }

        StateChanged?.Invoke(new CoordinatorStateChange
        {
            EnteredReconnect = true,
            Result = null,
        });

        // Fire-and-forget — the wrapper drives the actual reconnect on
        // its own thread; we wait for the ReconnectCompleted event.
        _ = _wrapper.BeginReconnectAsync(session, CancellationToken.None);
    }

    /// <summary>
    /// Wrapper callback when reconnect attempt finishes (success or
    /// terminal failure). Updates counters, transitions out of
    /// reconnect state.
    /// </summary>
    private void OnReconnectCompleted(ReconnectResult result)
    {
        try
        {
            switch (result.Mode)
            {
                case ReconnectMode.Transfer:
                    Interlocked.Increment(ref _reconnectsViaTransfer);
                    _lastSuccessfulReconnectUtc = DateTime.UtcNow;
                    _lastReconnectMode = ReconnectMode.Transfer;
                    if (result.NewSession is { } transferredSession)
                    {
                        AttachToNewSession(transferredSession);
                    }
                    break;

                case ReconnectMode.Recreate:
                    Interlocked.Increment(ref _reconnectsViaRecreate);
                    _lastSuccessfulReconnectUtc = DateTime.UtcNow;
                    _lastReconnectMode = ReconnectMode.Recreate;
                    if (result.NewSession is { } recreatedSession)
                    {
                        AttachToNewSession(recreatedSession);
                    }
                    break;

                case ReconnectMode.Failed:
                    Interlocked.Increment(ref _reconnectsFailed);
                    _lastReconnectMode = ReconnectMode.Failed;
                    _logger.LogError(result.Error,
                        "OPC UA reconnect terminally failed; adapter will transition to Failed.");
                    break;
            }

            StateChanged?.Invoke(new CoordinatorStateChange
            {
                EnteredReconnect = false,
                Result = result,
            });
        }
        finally
        {
            Interlocked.Exchange(ref _currentlyReconnecting, 0);
        }
    }

    /// <summary>
    /// After a successful reconnect, re-wire the keep-alive handler on
    /// the new session and update our reference. The ADAPTER takes
    /// responsibility for re-wiring FastDataChangeCallback on the new
    /// subscriptions (since it owns the dispatcher).
    /// </summary>
    private void AttachToNewSession(ISession newSession)
    {
        if (_session is { } oldSession)
        {
            try { oldSession.KeepAlive -= OnKeepAlive; } catch { /* best-effort */ }
        }
        _session = newSession;
        newSession.KeepAlive += OnKeepAlive;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Detach();
        try { _wrapper.ReconnectCompleted -= _onReconnectCallback; } catch { /* best-effort */ }
        await _wrapper.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Coordinator counters surfaced through
/// <see cref="Core.Adapters.AdapterHealth.Metrics"/>.
/// </summary>
public sealed record ReconnectCoordinatorCounters
{
    /// <summary>Successful reconnects via <see cref="ReconnectMode.Transfer"/>.</summary>
    public required long ReconnectsViaTransfer { get; init; }

    /// <summary>Successful reconnects via <see cref="ReconnectMode.Recreate"/>.</summary>
    public required long ReconnectsViaRecreate { get; init; }

    /// <summary>Terminal reconnect failures (retry budget exhausted).</summary>
    public required long ReconnectsFailed { get; init; }

    /// <summary>True while a reconnect attempt is currently in flight.</summary>
    public required bool CurrentlyReconnecting { get; init; }

    /// <summary>
    /// UTC timestamp of the most-recent SUCCESSFUL reconnect (Transfer
    /// or Recreate). <see langword="null"/> until the first success.
    /// Operationally useful for diagnosing intermittent plant-network
    /// issues per PR 6a amendment #1 (user lock 2026-05-29).
    /// </summary>
    public DateTime? LastSuccessfulReconnectUtc { get; init; }

    /// <summary>
    /// Mode of the most-recent reconnect attempt (regardless of success).
    /// Avoids forcing operators to infer from cumulative counters per
    /// PR 6a amendment #2 (user lock 2026-05-29).
    /// </summary>
    public required ReconnectMode LastReconnectMode { get; init; }
}

/// <summary>
/// Event payload for <see cref="OpcUaReconnectCoordinator.StateChanged"/>.
/// </summary>
public sealed record CoordinatorStateChange
{
    /// <summary>
    /// True when the coordinator just entered reconnect (Running →
    /// Degraded). False when it left reconnect (Degraded → Running OR
    /// Failed).
    /// </summary>
    public required bool EnteredReconnect { get; init; }

    /// <summary>
    /// The <see cref="ReconnectResult"/> when leaving reconnect;
    /// <see langword="null"/> when entering. Adapter inspects to decide
    /// whether to transition back to Running or to Failed.
    /// </summary>
    public ReconnectResult? Result { get; init; }
}
