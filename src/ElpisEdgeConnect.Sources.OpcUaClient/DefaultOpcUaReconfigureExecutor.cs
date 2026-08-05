// ============================================================================
// File: DefaultOpcUaReconfigureExecutor.cs
// Purpose: Production implementation of IOpcUaReconfigureExecutor.
//          Applies the diff to live subscriptions in three phases:
//          Remove → Modify → Add.
//
// LOCKED behaviour (per PR 6b plan + amendments, user lock 2026-05-29):
//
//   1. 100K-cap pre-validation runs BEFORE any subscription mutation.
//      Pinned by ReconfigureExecutor_OverCap_DoesNotMutateLiveSubscriptions.
//   2. Single ApplyChanges per touched subscription (batched at the end
//      of Remove + Modify phases). New subscriptions get their own
//      ApplyChanges as part of allocation.
//   3. Remove → Modify → Add ordering. Removing first frees capacity in
//      existing subs so Adds may fit without allocating new
//      subscriptions.
//   4. Per amendment #3 — SamplingInterval / QueueSize / Deadband changes
//      mutate the existing MonitoredItem in place; the OPC stack emits a
//      ModifyMonitoredItems request via ApplyChanges. No Remove+Add cycle.
//   5. Per strategy A (best-effort + Degraded) — mid-flight exceptions
//      surface as-is; the adapter catches and transitions to Degraded.
//      No partial rollback attempt.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
//            PR 6b plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="IOpcUaReconfigureExecutor"/>.
/// </summary>
internal sealed class DefaultOpcUaReconfigureExecutor : IOpcUaReconfigureExecutor
{
    private readonly ILogger _logger;

    public DefaultOpcUaReconfigureExecutor(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<OpcUaReconfigureExecutionResult> ApplyAsync(
        ISession session,
        IReadOnlyList<Subscription> existingSubscriptions,
        OpcUaMonitoredItemDiffResult diff,
        OpcUaClientSourceConfiguration newConfig,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(existingSubscriptions);
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(newConfig);

        ct.ThrowIfCancellationRequested();

        // ─── PRE-VALIDATION (no mutation past this point if it fails) ───
        //
        // The new total is the new config's total item count — not derived
        // from diff sums (which could understate if the old config had
        // duplicates). This is the single source of truth for the
        // post-reconfigure session item count.
        var newTotalItems = newConfig.MonitoredItems.Count;
        if (newTotalItems > OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession)
        {
            throw new InvalidOperationException(
                $"OPCUA.TOO_MANY_MONITORED_ITEMS_AFTER_RECONFIGURE: reconfigure would result in "
                + $"{newTotalItems} monitored items but the per-session ceiling is "
                + $"{OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession} "
                + $"({OpcUaClientSubscriptionPlanner.MaxSubscriptionsPerSession} subscriptions × "
                + $"{OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription} items each). Reduce the "
                + "tag count or split across multiple source instances.");
        }

        // Live map: canonical NodeId → (subscription, monitored-item).
        // Built once for the entire execution.
        var liveMap = BuildLiveLookup(existingSubscriptions);
        var touchedExistingSubs = new HashSet<Subscription>();

        // ─── REMOVE phase ────────────────────────────────────────────────
        var removedCount = 0;
        foreach (var removedConfig in diff.Removed)
        {
            var key = OpcUaNodeIdCanonicalizer.Canonicalize(removedConfig.NodeId);
            if (liveMap.TryGetValue(key, out var entry))
            {
                entry.Subscription.RemoveItem(entry.Item);
                touchedExistingSubs.Add(entry.Subscription);
                liveMap.Remove(key);
                removedCount++;
            }
        }

        ct.ThrowIfCancellationRequested();

        // ─── MODIFY phase ────────────────────────────────────────────────
        var modifiedCount = 0;
        foreach (var mod in diff.Modified)
        {
            var key = OpcUaNodeIdCanonicalizer.Canonicalize(mod.Old.NodeId);
            if (!liveMap.TryGetValue(key, out var entry))
            {
                _logger.LogWarning(
                    "OPC UA reconfigure: monitored item {NodeId} was flagged Modified but no live "
                    + "MonitoredItem matches the canonical key; skipping.",
                    mod.Old.NodeId);
                continue;
            }

            if (mod.SamplingIntervalChanged)
            {
                entry.Item.SamplingInterval =
                    mod.New.SamplingIntervalMs ?? newConfig.SamplingIntervalMs;
            }
            if (mod.QueueSizeChanged)
            {
                entry.Item.QueueSize =
                    mod.New.QueueSize ?? newConfig.DefaultAnalogQueueSize;
            }
            if (mod.DeadbandPercentChanged)
            {
                entry.Item.Filter = mod.New.DeadbandPercent.HasValue
                    ? new DataChangeFilter
                    {
                        Trigger = DataChangeTrigger.StatusValue,
                        DeadbandType = (uint)DeadbandType.Percent,
                        DeadbandValue = mod.New.DeadbandPercent.Value,
                    }
                    : null;
            }
            touchedExistingSubs.Add(entry.Subscription);
            modifiedCount++;
        }

        ct.ThrowIfCancellationRequested();

        // Flush Remove + Modify before Add. Doing this here means Adds see
        // the freed capacity from the Remove phase. The OPC stack issues
        // ModifyMonitoredItems / DeleteMonitoredItems in this round-trip.
        foreach (var sub in touchedExistingSubs)
        {
            sub.ApplyChanges();
        }

        // ─── ADD phase ───────────────────────────────────────────────────
        //
        // Fill existing subscriptions to MaxItemsPerSubscription first
        // (in subscription order); overflow goes into freshly-allocated
        // subscriptions. This keeps the post-reconfigure topology as close
        // as possible to the original — operators see the same subscription
        // boundaries unless they truly overflow.
        var addedCount = 0;
        var newSubscriptions = new List<Subscription>();
        var addQueue = new Queue<MonitoredItemConfig>(diff.Added);

        foreach (var sub in existingSubscriptions)
        {
            if (addQueue.Count == 0) break;
            var capacity = OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription - (int)sub.MonitoredItemCount;
            if (capacity <= 0) continue;

            var batch = new List<MonitoredItem>(Math.Min(capacity, addQueue.Count));
            while (batch.Count < capacity && addQueue.Count > 0)
            {
                batch.Add(OpcUaMonitoredItemBuilder.Build(addQueue.Dequeue(), newConfig));
            }
            sub.AddItems(batch);
            sub.ApplyChanges();
            addedCount += batch.Count;
        }

        // Overflow → new subscriptions (planner gives us batch sizes).
        if (addQueue.Count > 0)
        {
            var overflowConfigs = new List<MonitoredItemConfig>(addQueue.Count);
            while (addQueue.Count > 0) overflowConfigs.Add(addQueue.Dequeue());

            var overflowBatches = OpcUaClientSubscriptionPlanner.Plan(overflowConfigs);
            foreach (var batchConfigs in overflowBatches)
            {
                var newSub = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = newConfig.PublishingIntervalMs,
                    KeepAliveCount = newConfig.KeepAliveCount,
                    LifetimeCount = newConfig.LifetimeCount,
                    MaxNotificationsPerPublish = newConfig.MaxNotificationsPerPublish,
                    Priority = 0,
                    PublishingEnabled = true,
                    SequentialPublishing = true,
                };
                session.AddSubscription(newSub);
                newSub.Create();

                var items = new List<MonitoredItem>(batchConfigs.Count);
                foreach (var itemConfig in batchConfigs)
                {
                    items.Add(OpcUaMonitoredItemBuilder.Build(itemConfig, newConfig));
                }
                newSub.AddItems(items);
                newSub.ApplyChanges();
                addedCount += items.Count;
                newSubscriptions.Add(newSub);
            }
        }

        // ─── Final-list construction ─────────────────────────────────────
        //
        // Existing subs that ended up empty are emptied + Removed for
        // disposal by the adapter. Non-empty ones survive. New subs
        // append at the end.
        var finalSubscriptions = new List<Subscription>();
        var removedSubscriptions = new List<Subscription>();
        foreach (var sub in existingSubscriptions)
        {
            if (sub.MonitoredItemCount > 0)
            {
                finalSubscriptions.Add(sub);
            }
            else
            {
                removedSubscriptions.Add(sub);
            }
        }
        finalSubscriptions.AddRange(newSubscriptions);

        return Task.FromResult(new OpcUaReconfigureExecutionResult
        {
            FinalSubscriptions = finalSubscriptions,
            NewSubscriptions = newSubscriptions,
            RemovedSubscriptions = removedSubscriptions,
            ItemsAdded = addedCount,
            ItemsRemoved = removedCount,
            ItemsModified = modifiedCount,
        });
    }

    /// <summary>
    /// Build a NodeId-keyed lookup from the live subscription set. Each
    /// MonitoredItem's <c>StartNodeId</c> goes through the same
    /// canonicalisation as the diff via
    /// <see cref="OpcUaNodeIdCanonicalizer"/> so lookups match.
    /// </summary>
    private static Dictionary<string, LiveItemEntry> BuildLiveLookup(
        IReadOnlyList<Subscription> existingSubscriptions)
    {
        var lookup = new Dictionary<string, LiveItemEntry>(StringComparer.Ordinal);
        foreach (var sub in existingSubscriptions)
        {
            foreach (var item in sub.MonitoredItems)
            {
                var key = OpcUaNodeIdCanonicalizer.Canonicalize(item.StartNodeId);
                if (key is null) continue;
                lookup.TryAdd(key, new LiveItemEntry(sub, item));
            }
        }
        return lookup;
    }

    private readonly record struct LiveItemEntry(Subscription Subscription, MonitoredItem Item);
}
