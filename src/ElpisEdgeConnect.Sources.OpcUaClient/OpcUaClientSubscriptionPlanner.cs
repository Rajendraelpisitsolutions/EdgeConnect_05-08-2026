// ============================================================================
// File: OpcUaClientSubscriptionPlanner.cs
// Purpose: Pure-logic helper that decides how many Subscriptions to create
//          and how to distribute MonitoredItemConfig entries across them.
//          Per v2.1 §2.5 + OPC Foundation Issue #564, the universal
//          server-side ceiling is 1,000 monitored items per subscription;
//          larger configurations split into multiple subscriptions.
//
//          Per-session ceiling: 100 subscriptions (locked at user
//          sign-off 2026-05-29 PR 3 amendment #1, Option A). With 1,000
//          items/sub, that's 100,000 monitored items per session — well
//          above the v2.1 50K stretch target, leaving headroom for
//          future scaling without re-litigating the cap.
//
// LOCKED behaviour:
//   * Item order preserved across batches (subscription 0 gets the first
//     1,000 items in operator-defined order; subscription 1 the next, etc.)
//   * 0 items → 0 subscriptions (no empty-sub overhead)
//   * MaxItemsPerSubscription default = 1,000 (LockedTuningKnobs ceiling)
//   * MaxSubscriptionsPerSession default = 100 (per-session cap)
//   * Validation: total items > 100,000 throws InvalidOperationException
//     with the OPCUA.TOO_MANY_MONITORED_ITEMS error code
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
//            PR 3 amendment #1 (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Pure-logic batch planner for OPC UA Client subscriptions.
/// </summary>
internal static class OpcUaClientSubscriptionPlanner
{
    /// <summary>
    /// OPC Foundation Issue #564 universal server limit: 1,000 monitored
    /// items per subscription. v2.1 §2.5 lock.
    /// </summary>
    public const int MaxItemsPerSubscription = 1_000;

    /// <summary>
    /// Per-session subscription cap. v2.1 §5.1 + PR 3 amendment #1
    /// (user lock 2026-05-29 Option A). 100 × 1,000 = 100,000 monitored
    /// items per session — comfortably above the 50K stretch target.
    /// </summary>
    public const int MaxSubscriptionsPerSession = 100;

    /// <summary>
    /// Derived ceiling for total monitored items per session.
    /// </summary>
    public const int MaxMonitoredItemsPerSession =
        MaxItemsPerSubscription * MaxSubscriptionsPerSession;

    /// <summary>
    /// Plan the batches. Throws <see cref="InvalidOperationException"/>
    /// when <paramref name="items"/> exceeds the per-session ceiling.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<MonitoredItemConfig>> Plan(
        IReadOnlyList<MonitoredItemConfig> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count > MaxMonitoredItemsPerSession)
        {
            throw new InvalidOperationException(
                $"OPCUA.TOO_MANY_MONITORED_ITEMS: configured {items.Count} monitored items but the "
                + $"per-session ceiling is {MaxMonitoredItemsPerSession} "
                + $"({MaxSubscriptionsPerSession} subscriptions × {MaxItemsPerSubscription} items each). "
                + "Either reduce the tag count or split across multiple source instances.");
        }

        if (items.Count == 0)
        {
            return System.Array.Empty<IReadOnlyList<MonitoredItemConfig>>();
        }

        var batchCount = (items.Count + MaxItemsPerSubscription - 1) / MaxItemsPerSubscription;
        var batches = new List<IReadOnlyList<MonitoredItemConfig>>(batchCount);

        for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            var start = batchIndex * MaxItemsPerSubscription;
            var size = Math.Min(MaxItemsPerSubscription, items.Count - start);
            var batch = new MonitoredItemConfig[size];
            for (var i = 0; i < size; i++)
            {
                batch[i] = items[start + i];
            }
            batches.Add(batch);
        }

        return batches;
    }
}
