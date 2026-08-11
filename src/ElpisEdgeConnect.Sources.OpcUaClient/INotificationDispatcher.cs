// ============================================================================
// File: INotificationDispatcher.cs
// Purpose: Test seam for the v2.1 §1.3 channel-based notification hot
//          path. Adapter takes this interface (default = real impl);
//          unit tests substitute the dispatcher to verify adapter
//          integration without the channel + worker complexity.
//
// LOCKED architectural decisions (per PR 4 plan sign-off 2026-05-29):
//   * Single bounded Channel<NotificationBatch> per ADAPTER (not per
//     subscription) — per-source ordering is the v2.1 §19.6 guarantee
//   * Bounded capacity locked at config time, validated 100..10,000
//   * DropOldest backpressure policy (NOT operator-configurable) —
//     consistent with the existing DiscardOldest=true MonitoredItem lock
//     and the edge-prefers-fresh-data philosophy
//   * FastDataChangeCallback wired via OnNotification — caller's
//     hot-path contract: do nothing but call this, return immediately
//   * Worker drains channel, translates via OpcUaTypeMapper, yields
//     through ConsumeAsync's IAsyncEnumerable
//   * 4 counters per PR 4 amendment #4 (user lock 2026-05-29):
//       Received, Dispatched, DroppedDueToBackpressure, DroppedAtShutdown
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3
//            PR 4 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using ElpisEdgeConnect.Core.Model;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Test-substitutable notification dispatcher.
/// </summary>
internal interface INotificationDispatcher
{
    /// <summary>
    /// Wired as the <see cref="Subscription.FastDataChangeCallback"/> on
    /// every subscription the adapter creates. MUST be non-blocking —
    /// the OPC stack invokes this on its single publish-processing
    /// thread per subscription, and a slow callback tails out the
    /// stack's notification queue.
    /// </summary>
    void OnNotification(Subscription subscription, DataChangeNotification notification, IList<string> stringTable);

    /// <summary>
    /// Drain the dispatcher's channel and yield translated CDPs.
    /// Consumers (the adapter's <c>SubscribeAsync</c>) iterate this
    /// until <paramref name="ct"/> fires.
    /// </summary>
    IAsyncEnumerable<CanonicalDataPoint> ConsumeAsync(CancellationToken ct);

    /// <summary>Snapshot of the 4 counters per PR 4 amendment #4.</summary>
    NotificationDispatcherCounters GetCounters();
}

/// <summary>
/// Per-amendment-#4 (user lock 2026-05-29) operator-visible counters.
/// Captured at <c>CheckHealthAsync</c> time and surfaced as
/// <see cref="Core.Adapters.AdapterHealth.Metrics"/>.
/// </summary>
public sealed record NotificationDispatcherCounters
{
    /// <summary>
    /// Total notifications received from the OPC stack — independent of
    /// whether they made it through the channel. Lets operators compute
    /// dispatched + dropped = received.
    /// </summary>
    public required long Received { get; init; }

    /// <summary>Total CDPs successfully yielded through <c>ConsumeAsync</c>.</summary>
    public required long Dispatched { get; init; }

    /// <summary>
    /// Total batches dropped because the bounded channel was full at
    /// callback time. DropOldest policy means the EARLIEST entries went;
    /// the operator's wizard surfaces this as a sustained-backpressure
    /// indicator.
    /// </summary>
    public required long DroppedDueToBackpressure { get; init; }

    /// <summary>
    /// Total batches dropped during shutdown when the drain-or-timeout
    /// window expired before the worker could yield them.
    /// </summary>
    public required long DroppedAtShutdown { get; init; }
}
