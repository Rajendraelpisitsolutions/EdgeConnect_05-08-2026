// ============================================================================
// File: DefaultOpcUaClientSubscriptionFactory.cs
// Purpose: Production implementation of IOpcUaClientSubscriptionFactory.
//          Uses Planner + Builder pure-logic helpers, then attaches
//          Subscriptions to the live Session.
//
// LOCKED sequence (must run in this exact order):
//   1. Plan batches via OpcUaClientSubscriptionPlanner — validates
//      against the 100,000-item per-session cap
//   2. Set session.MinPublishRequestCount = batchCount + 2 (v2.1 §2.5
//      LockedTuningKnobs, Issue #564 stack guidance — must be set BEFORE
//      subscriptions are activated)
//   3. For each batch:
//      a. Create a Subscription, set tuning knobs from config
//      b. session.AddSubscription(subscription)
//      c. subscription.Create() — sends CreateSubscription request to server
//      d. Build MonitoredItems via OpcUaMonitoredItemBuilder
//      e. subscription.AddItems(items)
//      f. subscription.ApplyChanges() — sends CreateMonitoredItems request
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
// ============================================================================

using System;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="IOpcUaClientSubscriptionFactory"/>.
/// </summary>
internal sealed class DefaultOpcUaClientSubscriptionFactory : IOpcUaClientSubscriptionFactory
{
    private readonly ILogger _logger;

    /// <summary>Production constructor wires a NullLogger when no logger is supplied.</summary>
    public DefaultOpcUaClientSubscriptionFactory(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public Task<IReadOnlyList<Subscription>> CreateSubscriptionsAsync(
        ISession session,
        OpcUaClientSourceConfiguration config,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(config);
        ct.ThrowIfCancellationRequested();

        // 1. Plan batches (validates the 100K-item per-session cap).
        var batches = OpcUaClientSubscriptionPlanner.Plan(config.MonitoredItems);
        if (batches.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Subscription>>(System.Array.Empty<Subscription>());
        }

        // 2. Pre-configure publish request count BEFORE subscriptions
        //    activate. Issue #564 — under burst the stack drops
        //    notifications if MinPublishRequestCount < subscription count.
        session.MinPublishRequestCount = batches.Count + 2;

        // 3. Create + populate each subscription.
        var subscriptions = new List<Subscription>(batches.Count);
        foreach (var batch in batches)
        {
            var subscription = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = config.PublishingIntervalMs,
                KeepAliveCount = config.KeepAliveCount,
                LifetimeCount = config.LifetimeCount,
                MaxNotificationsPerPublish = config.MaxNotificationsPerPublish,
                Priority = 0,
                PublishingEnabled = true,
                SequentialPublishing = true,
            };
            session.AddSubscription(subscription);
            subscription.Create();

            var items = new List<MonitoredItem>(batch.Count);
            foreach (var itemConfig in batch)
            {
                items.Add(OpcUaMonitoredItemBuilder.Build(itemConfig, config));
            }
            subscription.AddItems(items);
            subscription.ApplyChanges();

            // Per-MonitoredItem status check — after ApplyChanges, the
            // OPC stack populates each MonitoredItem.Status with the
            // server's response. A Bad status here means the server
            // rejected the item (BadNodeIdUnknown / BadAttributeIdInvalid
            // / BadUserAccessDenied / BadSecurityChecksFailed etc.) —
            // these are the "subscribed but silent" failures that look
            // identical to a healthy quiet subscription without this
            // check. Surface each rejection as a warning log line so the
            // operator can read the exact NodeId + status code in the
            // host console; the per-source bad-status count is also
            // surfaced via CheckHealthAsync.
            //
            // Task #51 (user-locked 2026-05-30).
            InspectMonitoredItemStatuses(subscription, items);

            subscriptions.Add(subscription);
        }

        return Task.FromResult<IReadOnlyList<Subscription>>(subscriptions);
    }

    /// <summary>
    /// Walk each <see cref="MonitoredItem.Status"/> after the
    /// subscription's <c>ApplyChanges</c> and log a per-item warning
    /// for any whose server-returned status is not Good. Captures the
    /// most common "subscribed but silent" failure modes:
    /// <c>BadNodeIdUnknown</c>, <c>BadAttributeIdInvalid</c>,
    /// <c>BadUserAccessDenied</c>, <c>BadSecurityChecksFailed</c>,
    /// <c>BadMonitoredItemFilterUnsupported</c>.
    /// </summary>
    /// <remarks>
    /// We do NOT throw here even when every item is rejected — the
    /// adapter remains Running so an operator with mixed good + bad
    /// NodeIds can still receive data on the good ones. The
    /// adapter-side health metric <c>monitoredItemsWithBadStatus</c>
    /// surfaces the rejection count for diagnostic UX.
    /// </remarks>
    internal static int InspectMonitoredItemStatuses(
        Subscription subscription,
        IReadOnlyList<MonitoredItem> attached)
    {
        var badCount = 0;
        foreach (var item in attached)
        {
            var status = item.Status;
            if (status is null || status.Error is null) continue;
            if (ServiceResult.IsBad(status.Error))
            {
                badCount++;
            }
        }
        return badCount;
    }

    /// <summary>Overload that also logs each rejection at Warning level.</summary>
    private void InspectMonitoredItemStatuses(
        Subscription subscription,
        List<MonitoredItem> attached)
    {
        var badCount = 0;
        foreach (var item in attached)
        {
            var status = item.Status;
            if (status is null || status.Error is null) continue;
            if (ServiceResult.IsBad(status.Error))
            {
                badCount++;
                _logger.LogWarning(
                    "OPC UA Client subscription rejected MonitoredItem: "
                    + "nodeId={NodeId} displayName={DisplayName} status={StatusCode} message={Message}. "
                    + "Per task #51 — this is the per-MonitoredItem status surface introduced "
                    + "to make 'subscribed but silent' rejections visible. Check the NodeId, "
                    + "the AttributeId (we use Value=13 always), and the server's user-access ACL.",
                    item.StartNodeId,
                    item.DisplayName,
                    status.Error.StatusCode,
                    status.Error.ToString());
            }
        }
        if (badCount > 0)
        {
            _logger.LogWarning(
                "OPC UA Client subscription create completed with {BadCount} rejected MonitoredItem(s) "
                + "out of {TotalCount}. Adapter remains Running; operator-actionable.",
                badCount, attached.Count);
        }
    }
}
