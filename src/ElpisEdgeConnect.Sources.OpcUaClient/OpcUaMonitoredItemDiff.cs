// ============================================================================
// File: OpcUaMonitoredItemDiff.cs
// Purpose: Pure-logic diff between the adapter's active monitored-item
//          set and a new (operator-requested) set. The executor consumes
//          the diff to surgically add / remove / modify items on the live
//          subscriptions, without tearing down the session or the
//          notification dispatcher.
//
// LOCKED behaviour (per PR 6b plan + amendments, user lock 2026-05-29):
//
//   1. Matching key = NodeId, normalised via Opc.Ua.NodeId.Parse + .ToString
//      so semantically-equal NodeIds (e.g., "ns=2;i=1" vs
//      "ns=2;i=00000001") are classified as the same item rather than
//      Added+Removed.
//   2. Normalisation happens ONCE at lookup-build time per amendment #4
//      refinement — at 30K+ items the diff loop must not re-parse on
//      every comparison.
//   3. Field-level modification flags so the executor can pick the
//      cheapest server-side mutation (e.g., SamplingInterval-only change
//      → ModifyMonitoredItem with new SamplingInterval; no Remove+Add).
//   4. DisplayName changes alone are treated as Unchanged (cosmetic,
//      no behavioural delta). The new MonitoredItemConfig carries the
//      new DisplayName forward; the next Recreate reconnect (or
//      operator restart) applies it server-side.
//   5. Idempotent path (amendment #5): identical old/new sets → all-
//      Unchanged, IsIdempotent=true, executor is a no-op.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
//            PR 6b plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Pure-logic diff between two monitored-item sets. Computes Added,
/// Removed, Modified, Unchanged buckets so the
/// <see cref="IOpcUaReconfigureExecutor"/> can apply the minimum
/// possible server-side mutation.
/// </summary>
internal static class OpcUaMonitoredItemDiff
{
    /// <summary>
    /// Compute the diff between <paramref name="oldSet"/> and
    /// <paramref name="newSet"/>. Both lists are interpreted as
    /// snapshots — order is not significant for classification.
    /// </summary>
    public static OpcUaMonitoredItemDiffResult Compute(
        IReadOnlyList<MonitoredItemConfig> oldSet,
        IReadOnlyList<MonitoredItemConfig> newSet)
    {
        ArgumentNullException.ThrowIfNull(oldSet);
        ArgumentNullException.ThrowIfNull(newSet);

        // Canonical NodeId → MonitoredItemConfig lookups. Build the
        // canonical key ONCE per item per amendment #4 refinement — at
        // 30K+ monitored items, repeated NodeId.Parse on every
        // comparison would be wasted work even though reconfigure
        // isn't a hot path.
        var oldLookup = BuildCanonicalLookup(oldSet);
        var newLookup = BuildCanonicalLookup(newSet);

        var added = new List<MonitoredItemConfig>();
        var removed = new List<MonitoredItemConfig>();
        var modified = new List<MonitoredItemModification>();
        var unchanged = new List<MonitoredItemConfig>();

        foreach (var (canonicalKey, oldItem) in oldLookup)
        {
            if (newLookup.TryGetValue(canonicalKey, out var newItem))
            {
                if (IsBehaviourallyEqual(oldItem, newItem))
                {
                    unchanged.Add(oldItem);
                }
                else
                {
                    modified.Add(new MonitoredItemModification
                    {
                        Old = oldItem,
                        New = newItem,
                        SamplingIntervalChanged = oldItem.SamplingIntervalMs != newItem.SamplingIntervalMs,
                        QueueSizeChanged = oldItem.QueueSize != newItem.QueueSize,
                        DeadbandPercentChanged = oldItem.DeadbandPercent != newItem.DeadbandPercent,
                    });
                }
            }
            else
            {
                removed.Add(oldItem);
            }
        }

        foreach (var (canonicalKey, newItem) in newLookup)
        {
            if (!oldLookup.ContainsKey(canonicalKey))
            {
                added.Add(newItem);
            }
        }

        return new OpcUaMonitoredItemDiffResult
        {
            Added = added,
            Removed = removed,
            Modified = modified,
            Unchanged = unchanged,
        };
    }

    /// <summary>
    /// Build a NodeId-keyed lookup where the key is semantically
    /// canonical (parsed once via <see cref="OpcUaNodeIdCanonicalizer"/>).
    /// NodeIds that fail to parse fall back to the raw string —
    /// operator hand-entry errors surface as Add+Remove churn rather
    /// than silently mismatching.
    /// </summary>
    private static Dictionary<string, MonitoredItemConfig> BuildCanonicalLookup(
        IReadOnlyList<MonitoredItemConfig> items)
    {
        var lookup = new Dictionary<string, MonitoredItemConfig>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = OpcUaNodeIdCanonicalizer.Canonicalize(item.NodeId);
            // Duplicate NodeId in either set — keep the first occurrence
            // (operator config error; ValidateConfigAsync should reject
            // duplicates upstream, but the diff must not throw).
            lookup.TryAdd(key, item);
        }
        return lookup;
    }

    /// <summary>
    /// Two items with the same NodeId are behaviourally equal when
    /// every server-side attribute matches. DisplayName is cosmetic
    /// and intentionally excluded — see file header LOCKED rule #4.
    /// </summary>
    private static bool IsBehaviourallyEqual(MonitoredItemConfig a, MonitoredItemConfig b) =>
        a.SamplingIntervalMs == b.SamplingIntervalMs
        && a.QueueSize == b.QueueSize
        && a.DeadbandPercent == b.DeadbandPercent;
}

/// <summary>
/// Output of <see cref="OpcUaMonitoredItemDiff.Compute"/>. Operationally
/// surfaced as <c>lastReconfigureChangeCount</c> via the adapter's
/// health metrics.
/// </summary>
public sealed record OpcUaMonitoredItemDiffResult
{
    /// <summary>Items present only in the new set — will be added.</summary>
    public required IReadOnlyList<MonitoredItemConfig> Added { get; init; }

    /// <summary>Items present only in the old set — will be removed.</summary>
    public required IReadOnlyList<MonitoredItemConfig> Removed { get; init; }

    /// <summary>
    /// Items present in both sets with at least one behavioural attribute
    /// changed (SamplingInterval, QueueSize, DeadbandPercent).
    /// </summary>
    public required IReadOnlyList<MonitoredItemModification> Modified { get; init; }

    /// <summary>
    /// Items present in both sets with identical behavioural attributes.
    /// DisplayName-only deltas land here per locked rule #4.
    /// </summary>
    public required IReadOnlyList<MonitoredItemConfig> Unchanged { get; init; }

    /// <summary>
    /// True when no Add / Remove / Modify deltas exist. Adapter
    /// short-circuits the executor entirely in this case per PR 6b
    /// amendment #5 (user lock 2026-05-29).
    /// </summary>
    public bool IsIdempotent => Added.Count == 0 && Removed.Count == 0 && Modified.Count == 0;

    /// <summary>
    /// Total change count surfaced as
    /// <c>lastReconfigureChangeCount</c> in adapter health metrics.
    /// </summary>
    public int ChangeCount => Added.Count + Removed.Count + Modified.Count;
}

/// <summary>
/// Per-item modification record. Per-field flags let the executor pick
/// the cheapest server-side mutation per LOCKED rule #3.
/// </summary>
public sealed record MonitoredItemModification
{
    /// <summary>The item's previous configuration.</summary>
    public required MonitoredItemConfig Old { get; init; }

    /// <summary>The item's new configuration.</summary>
    public required MonitoredItemConfig New { get; init; }

    /// <summary>True when <see cref="MonitoredItemConfig.SamplingIntervalMs"/> changed.</summary>
    public required bool SamplingIntervalChanged { get; init; }

    /// <summary>True when <see cref="MonitoredItemConfig.QueueSize"/> changed.</summary>
    public required bool QueueSizeChanged { get; init; }

    /// <summary>True when <see cref="MonitoredItemConfig.DeadbandPercent"/> changed.</summary>
    public required bool DeadbandPercentChanged { get; init; }
}
