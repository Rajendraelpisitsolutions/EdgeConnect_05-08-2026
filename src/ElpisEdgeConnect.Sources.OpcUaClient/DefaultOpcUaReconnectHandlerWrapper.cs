// ============================================================================
// File: DefaultOpcUaReconnectHandlerWrapper.cs
// Purpose: Production implementation wrapping the OPC Foundation stack's
//          SessionReconnectHandler. Translates the stack's event-driven
//          surface into our reactive ReconnectCompleted event.
//
//          The stack tries TransferSubscriptions first; if the server
//          returns BadNotImplemented (or the subscription set was
//          dropped server-side), it falls back to recreating the
//          subscriptions client-side. We detect which path was taken by
//          comparing the new session's SubscriptionCount to the prior
//          session's count — if the SAME subscriptions came across,
//          Transfer succeeded; otherwise Recreate happened.
//
// LOCKED behaviour:
//   * Reconnect period = 1,000ms (v2.1 §2.5 LockedTuningKnobs)
//   * Single-flight — a second BeginReconnectAsync while reconnect is
//     in flight is silently no-op (the stack's handler is already trying)
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §2.5
//            OPC UA Part 4 §6.7 — Re-establishing connections
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="IOpcUaReconnectHandlerWrapper"/>.
/// </summary>
internal sealed class DefaultOpcUaReconnectHandlerWrapper : IOpcUaReconnectHandlerWrapper
{
    private readonly ILogger _logger;
    private readonly int _reconnectPeriodMs;
    private readonly SessionReconnectHandler _handler;
    private ISession? _lostSession;
    private int _priorSubscriptionCount;
    private int _reconnectInFlight;

    public DefaultOpcUaReconnectHandlerWrapper(ILogger logger, int reconnectPeriodMs = 1_000)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _reconnectPeriodMs = reconnectPeriodMs;
        _handler = new SessionReconnectHandler();
    }

    /// <inheritdoc/>
    public event Action<ReconnectResult>? ReconnectCompleted;

    /// <inheritdoc/>
    public Task BeginReconnectAsync(ISession lostSession, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lostSession);

        // Single-flight — second call while reconnect is in flight is a
        // silent no-op (the stack's handler is already trying).
        if (Interlocked.CompareExchange(ref _reconnectInFlight, 1, 0) != 0)
        {
            _logger.LogDebug("Reconnect already in flight; ignoring duplicate BeginReconnectAsync.");
            return Task.CompletedTask;
        }

        try
        {
            _lostSession = lostSession;
            _priorSubscriptionCount = lostSession.SubscriptionCount;
            _handler.BeginReconnect(lostSession, _reconnectPeriodMs, OnReconnectComplete);
        }
        catch (Exception ex)
        {
            // Bootstrap failure (e.g., null session, handler disposed) —
            // surface as terminal failure and clear the in-flight flag.
            Interlocked.Exchange(ref _reconnectInFlight, 0);
            ReconnectCompleted?.Invoke(new ReconnectResult
            {
                Mode = ReconnectMode.Failed,
                Error = ex,
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stack callback when the reconnect handler finishes. The new
    /// session reference (or null on failure) lives on the handler.
    /// </summary>
    private void OnReconnectComplete(object? sender, EventArgs e)
    {
        try
        {
            var newSession = _handler.Session;
            if (newSession is null)
            {
                ReconnectCompleted?.Invoke(new ReconnectResult
                {
                    Mode = ReconnectMode.Failed,
                    Error = new InvalidOperationException(
                        "Reconnect handler returned a null session — the stack exhausted its retry budget."),
                });
                return;
            }

            // Discriminate Transfer vs Recreate. The OPC stack doesn't
            // surface this directly, but we can infer from the
            // subscription count — if the same number of subscriptions
            // came across, Transfer succeeded; otherwise the stack
            // recreated them (or the count drifted, which we conservatively
            // treat as Recreate).
            var mode = newSession.SubscriptionCount == _priorSubscriptionCount
                ? ReconnectMode.Transfer
                : ReconnectMode.Recreate;

            ReconnectCompleted?.Invoke(new ReconnectResult
            {
                Mode = mode,
                NewSession = newSession,
            });
        }
        catch (Exception ex)
        {
            ReconnectCompleted?.Invoke(new ReconnectResult
            {
                Mode = ReconnectMode.Failed,
                Error = ex,
            });
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectInFlight, 0);
            _lostSession = null;
            _priorSubscriptionCount = 0;
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        try { _handler.Dispose(); } catch { /* best-effort */ }
        return ValueTask.CompletedTask;
    }
}
