// ============================================================================
// File: OpcUaSessionTracker.cs
// Purpose: Records OPC UA session-lifecycle events for diagnostics.
//          Hooks the StandardServer's session events and maintains a
//          thread-safe snapshot of currently-active sessions.
//
//          H.1: in-memory snapshot of basic fields.
//          H.2: extended to capture subscription + monitored-item
//               counts on each snapshot read, and to surface the
//               protocol-agnostic SinkSessionSummary from Core so
//               the diagnostics surface and Prometheus instruments
//               see uniform data regardless of which sink produced
//               it.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.2
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Diagnostics;
using Opc.Ua;
using Opc.Ua.Server;

namespace ElpisEdgeConnect.Sinks.OpcUaServer;

/// <summary>
/// Tracks live OPC UA sessions on a <see cref="StandardServer"/>.
/// Subscribes to the server's <c>SessionManager</c> events and
/// maintains a thread-safe map keyed by session id.
/// </summary>
public sealed class OpcUaSessionTracker
{
    /// <summary>
    /// Bookkeeping the tracker keeps per session. Only the
    /// invariant-across-snapshots fields (session id, connect time,
    /// auth token type) live here; subscription + monitored-item
    /// counts are read live from the server at <see cref="Snapshot"/>
    /// time so we always reflect the most recent state.
    /// </summary>
    private sealed record SessionRecord
    {
        public required string SessionId { get; init; }
        public required NodeId NodeIdHandle { get; init; }
        public string? SessionName { get; init; }
        public string? ClientApplicationUri { get; init; }
        public required DateTime ConnectedAtUtc { get; init; }
        public DateTime LastActivityUtc { get; set; }
        public string? UserTokenType { get; init; }
    }

    private readonly ConcurrentDictionary<NodeId, SessionRecord> _sessions = new();
    private IServerInternal? _serverInternal;
    private ISessionManager? _sessionManager;

    /// <summary>Count of currently-active sessions.</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>
    /// Build a snapshot of all currently-active sessions, attaching
    /// the per-session subscription + monitored-item totals from the
    /// server's subscription manager.
    /// </summary>
    public IReadOnlyList<SinkSessionSummary> Snapshot()
    {
        var snapshot = new List<SinkSessionSummary>(_sessions.Count);
        var subscriptionManager = _serverInternal?.SubscriptionManager;

        foreach (var rec in _sessions.Values)
        {
            var (subs, items) = CountSubscriptionsForSession(subscriptionManager, rec.NodeIdHandle);
            snapshot.Add(new SinkSessionSummary
            {
                SessionId = rec.SessionId,
                SessionName = rec.SessionName,
                ClientApplicationUri = rec.ClientApplicationUri,
                ClientIpAddress = null,
                ConnectedAtUtc = rec.ConnectedAtUtc,
                LastActivityUtc = rec.LastActivityUtc,
                UserTokenType = rec.UserTokenType,
                SubscriptionCount = subs,
                MonitoredItemCount = items,
            });
        }

        return snapshot;
    }

    /// <summary>
    /// Attach to a started server's session manager. Idempotent —
    /// safe to call once during adapter start.
    /// </summary>
    public void Attach(StandardServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (_sessionManager is not null)
        {
            return;
        }

        _serverInternal = server.CurrentInstance;
        _sessionManager = _serverInternal.SessionManager;
        _sessionManager.SessionCreated += OnSessionCreated;
        _sessionManager.SessionActivated += OnSessionActivated;
        _sessionManager.SessionClosing += OnSessionClosing;
    }

    /// <summary>
    /// Detach from the session manager. Safe to call multiple times
    /// (idempotent). Invoked during adapter stop / dispose.
    /// </summary>
    public void Detach()
    {
        if (_sessionManager is null)
        {
            return;
        }

        _sessionManager.SessionCreated -= OnSessionCreated;
        _sessionManager.SessionActivated -= OnSessionActivated;
        _sessionManager.SessionClosing -= OnSessionClosing;
        _sessionManager = null;
        _serverInternal = null;
        _sessions.Clear();
    }

    private void OnSessionCreated(Session session, SessionEventReason reason) =>
        UpsertSession(session);

    private void OnSessionActivated(Session session, SessionEventReason reason) =>
        UpsertSession(session);

    private void OnSessionClosing(Session session, SessionEventReason reason)
    {
        if (session?.Id is { } id)
        {
            _sessions.TryRemove(id, out _);
        }
    }

    private void UpsertSession(Session? session)
    {
        if (session?.Id is not { } id)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var existing = _sessions.TryGetValue(id, out var prior) ? prior : null;

        if (existing is not null)
        {
            existing.LastActivityUtc = now;
            return;
        }

        _sessions[id] = new SessionRecord
        {
            SessionId = id.ToString(),
            NodeIdHandle = id,
            SessionName = session.SessionDiagnostics?.SessionName,
            ClientApplicationUri = null,
            ConnectedAtUtc = now,
            LastActivityUtc = now,
            UserTokenType = session.IdentityToken?.GetType().Name,
        };
    }

    /// <summary>
    /// Best-effort sum of (subscription count, monitored item count)
    /// for a session by walking the server's subscription manager.
    /// Returns (0, 0) when the subscription manager is unavailable.
    /// </summary>
    private static (int Subscriptions, int MonitoredItems) CountSubscriptionsForSession(
        ISubscriptionManager? subscriptionManager,
        NodeId sessionId)
    {
        if (subscriptionManager is null)
        {
            return (0, 0);
        }

        var subs = 0;
        var items = 0;
        try
        {
            foreach (var subscription in subscriptionManager.GetSubscriptions())
            {
                if (subscription is null)
                {
                    continue;
                }
                if (subscription.SessionId == sessionId)
                {
                    subs++;
                    items += subscription.MonitoredItemCount;
                }
            }
        }
        catch
        {
            // The subscription enumeration can race against subscription
            // teardown; treat as best-effort and return what we have.
        }

        return (subs, items);
    }
}
