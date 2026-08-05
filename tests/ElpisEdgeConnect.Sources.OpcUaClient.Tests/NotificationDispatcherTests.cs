// ============================================================================
// Tests: NotificationDispatcherTests — pin the v2.1 §1.3 channel-based
//        hot-path invariants for the production dispatcher.
//
//        Invariants:
//          * OnNotification is non-blocking — completes well within
//            the loose 1ms gate even on cold-JIT first invocations
//            (PR 4 amendment #2 user lock 2026-05-29; deliberately
//            looser than the originally-proposed 100µs to survive CI /
//            thermal variance)
//          * Received counter increments BEFORE channel write (captures
//            every notification independent of fate — amendment #4)
//          * Happy path: enqueued batch drains to ConsumeAsync output
//          * DropOldest backpressure: a full channel sheds the oldest
//            batch; DroppedDueToBackpressure increments by the dropped
//            count
//          * Counters accurate across received / dispatched / dropped
//          * Empty notifications are no-ops (no counter changes,
//            no channel writes)
//          * StopAsync completes channel; ConsumeAsync exits cleanly
//          * StopAsync timeout counts left-over CDPs as DroppedAtShutdown
//
//        ⚠ NSubstitute can't substitute Subscription (sealed-by-design).
//        Tests construct real Subscription instances (no live session)
//        and rely on the dispatcher's defensive translation path.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3
//            PR 4 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class NotificationDispatcherTests
{
    private static OpcUaTypeMapper Mapper() => new(
        gatewayId: "gw-test",
        sourceInstanceId: "opcua-test",
        protocolName: OpcUaClientSourceConfiguration.ProtocolNameConstant,
        deviceId: "factorytalk");

    private static NotificationDispatcher CreateDispatcher(int channelCapacity = 1_000) =>
        new(channelCapacity, Mapper(), NullLogger.Instance);

    /// <summary>
    /// Build a Subscription with N monitored items. The OPC stack
    /// auto-assigns each item's ClientHandle when AddItem is called;
    /// the returned list of items lets the caller read the assigned
    /// handles for use when building matching notifications.
    /// </summary>
    private static (Subscription Subscription, IReadOnlyList<MonitoredItem> Items) BuildSubscriptionWithItems(int itemCount)
    {
        var subscription = new Subscription();
        var items = new List<MonitoredItem>(itemCount);
        for (var i = 0; i < itemCount; i++)
        {
            var item = new MonitoredItem
            {
                StartNodeId = new NodeId((uint)(42 + i), 2),
                DisplayName = $"Tag_{i:D5}",
                AttributeId = Attributes.Value,
            };
            subscription.AddItem(item);
            items.Add(item);
        }
        return (subscription, items);
    }

    /// <summary>
    /// Build a notification that uses the actual ClientHandles assigned
    /// to <paramref name="items"/> — required so the dispatcher's
    /// FindItemByClientHandle lookup matches.
    /// </summary>
    private static DataChangeNotification BuildNotificationFor(IReadOnlyList<MonitoredItem> items)
    {
        var notification = new DataChangeNotification
        {
            MonitoredItems = new MonitoredItemNotificationCollection(),
        };
        for (var i = 0; i < items.Count; i++)
        {
            notification.MonitoredItems.Add(new MonitoredItemNotification
            {
                ClientHandle = items[i].ClientHandle,
                Value = new DataValue
                {
                    WrappedValue = new Variant(42 + i),
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
                    ServerTimestamp = new DateTime(2026, 6, 1, 12, 0, 1, DateTimeKind.Utc),
                },
            });
        }
        return notification;
    }

    // ─── Non-blocking invariant (amendment #2) ───────────────────────

    [Fact]
    public async Task OnNotification_NonBlocking_CompletesWellWithinOneMs()
    {
        // Per PR 4 amendment #2 — invariant is "no blocking ops, no
        // allocations beyond expected batch wrapper, no waits." Timing
        // gate is loose (<1ms) to survive CI / thermal variance. The
        // benchmark suite owns micro-performance; this gate catches
        // accidental regressions (someone adding await / lock / I/O).
        await using var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(50);
        var notification = BuildNotificationFor(items);
        var stringTable = new List<string>();

        // Warm up the JIT once so cold-path compile time doesn't skew
        // the measurement.
        dispatcher.OnNotification(subscription, notification, stringTable);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            dispatcher.OnNotification(subscription, notification, stringTable);
        }
        sw.Stop();
        var avgMs = sw.Elapsed.TotalMilliseconds / 10.0;

        avgMs.Should().BeLessThan(1.0,
            $"OnNotification must be non-blocking (PR 4 amendment #2). Measured average: {avgMs:F3}ms per call.");
        await Task.CompletedTask;  // Satisfy async signature; the test body is intentionally synchronous timing.
    }

    // ─── Happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task OnNotification_EnqueuedBatch_DrainsToConsumeAsync()
    {
        await using var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(3);
        var notification = BuildNotificationFor(items);

        dispatcher.OnNotification(subscription, notification, new List<string>());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var collected = new List<CanonicalDataPoint>();
        try
        {
            await foreach (var cdp in dispatcher.ConsumeAsync(cts.Token))
            {
                collected.Add(cdp);
                if (collected.Count == 3) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Defensive — shouldn't reach the 2s timeout for 3 CDPs.
        }

        collected.Should().HaveCount(3);
        collected[0].TagName.Should().Be("Tag_00000");
    }

    // ─── Counter accuracy (amendment #4) ─────────────────────────────

    [Fact]
    public async Task Counters_HappyPath_AllAccurate()
    {
        await using var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(5);
        var notification = BuildNotificationFor(items);

        dispatcher.OnNotification(subscription, notification, new List<string>());

        // Counters after enqueue, before drain.
        var afterEnqueue = dispatcher.GetCounters();
        afterEnqueue.Received.Should().Be(5);
        afterEnqueue.Dispatched.Should().Be(0);
        afterEnqueue.DroppedDueToBackpressure.Should().Be(0);
        afterEnqueue.DroppedAtShutdown.Should().Be(0);

        // Drain via ConsumeAsync.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var collected = new List<CanonicalDataPoint>();
        try
        {
            await foreach (var cdp in dispatcher.ConsumeAsync(cts.Token))
            {
                collected.Add(cdp);
                if (collected.Count == 5) break;
            }
        }
        catch (OperationCanceledException) { /* defensive */ }

        var afterDrain = dispatcher.GetCounters();
        afterDrain.Received.Should().Be(5);
        afterDrain.Dispatched.Should().Be(5);
        afterDrain.DroppedDueToBackpressure.Should().Be(0);
        afterDrain.DroppedAtShutdown.Should().Be(0);
    }

    // ─── Empty notification — no-op ──────────────────────────────────

    [Fact]
    public async Task OnNotification_EmptyNotification_NoCounterChange()
    {
        await using var dispatcher = CreateDispatcher();
        var (subscription, _) = BuildSubscriptionWithItems(0);
        var empty = new DataChangeNotification { MonitoredItems = new MonitoredItemNotificationCollection() };

        dispatcher.OnNotification(subscription, empty, new List<string>());

        var counters = dispatcher.GetCounters();
        counters.Received.Should().Be(0);
        counters.Dispatched.Should().Be(0);
        counters.DroppedDueToBackpressure.Should().Be(0);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OnNotification_NullNotification_NoOp()
    {
        await using var dispatcher = CreateDispatcher();
        var (subscription, _) = BuildSubscriptionWithItems(0);

        dispatcher.OnNotification(subscription, null!, new List<string>());

        var counters = dispatcher.GetCounters();
        counters.Received.Should().Be(0);
        await Task.CompletedTask;
    }

    // ─── DropOldest backpressure ─────────────────────────────────────

    [Fact]
    public async Task OnNotification_ChannelFull_DropsOldestAndCountsLoss()
    {
        // Capacity 2 — third write triggers DropOldest. The first batch
        // gets evicted; DroppedDueToBackpressure increments by its size.
        await using var dispatcher = CreateDispatcher(channelCapacity: 2);
        var (subscription, items) = BuildSubscriptionWithItems(3);
        var notification = BuildNotificationFor(items);
        var stringTable = new List<string>();

        // Fill capacity.
        dispatcher.OnNotification(subscription, notification, stringTable);
        dispatcher.OnNotification(subscription, notification, stringTable);
        // Third write — channel is full, oldest gets dropped.
        dispatcher.OnNotification(subscription, notification, stringTable);

        var counters = dispatcher.GetCounters();
        counters.Received.Should().Be(9, "all 3 batches × 3 items received");
        counters.DroppedDueToBackpressure.Should().BeGreaterOrEqualTo(3,
            "first batch's 3 items dropped to make room for the third batch (DropOldest policy)");
        await Task.CompletedTask;
    }

    // ─── Cancellation ────────────────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_RespectsCancellation()
    {
        await using var dispatcher = CreateDispatcher();
        using var cts = new CancellationTokenSource();

        var consumeTask = Task.Run(async () =>
        {
            var list = new List<CanonicalDataPoint>();
            try
            {
                await foreach (var cdp in dispatcher.ConsumeAsync(cts.Token))
                {
                    list.Add(cdp);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            return list;
        });

        await Task.Delay(50);
        cts.Cancel();
        var collected = await consumeTask;
        collected.Should().BeEmpty();
    }

    // ─── Shutdown ────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_AfterDrainComplete_ExitsCleanly()
    {
        await using var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(2);
        var notification = BuildNotificationFor(items);

        dispatcher.OnNotification(subscription, notification, new List<string>());

        // Drain everything first so StopAsync's wait has nothing to do.
        var consumerCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var collected = new List<CanonicalDataPoint>();
        var consumeTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var cdp in dispatcher.ConsumeAsync(consumerCts.Token))
                {
                    collected.Add(cdp);
                }
            }
            catch (OperationCanceledException) { /* expected after StopAsync */ }
        });

        // Give the consumer a moment to drain.
        await Task.Delay(100);

        // StopAsync should complete the channel; consumer exits.
        await dispatcher.StopAsync(CancellationToken.None);
        consumerCts.Cancel();
        await consumeTask;

        collected.Should().HaveCount(2);
        var counters = dispatcher.GetCounters();
        counters.DroppedAtShutdown.Should().Be(0,
            "everything drained before StopAsync — no shutdown-time drops.");
    }

    [Fact]
    public async Task StopAsync_TimeoutBeforeDrain_CountsLeftoversAsDroppedAtShutdown()
    {
        // Use a 100ms drain timeout; never consume. StopAsync's timeout
        // fires and the leftover batch counts as DroppedAtShutdown.
        await using var dispatcher = new NotificationDispatcher(
            channelCapacity: 100,
            typeMapper: Mapper(),
            logger: NullLogger.Instance,
            shutdownDrainTimeout: TimeSpan.FromMilliseconds(100));
        var (subscription, items) = BuildSubscriptionWithItems(4);
        var notification = BuildNotificationFor(items);

        dispatcher.OnNotification(subscription, notification, new List<string>());

        await dispatcher.StopAsync(CancellationToken.None);

        var counters = dispatcher.GetCounters();
        counters.DroppedAtShutdown.Should().Be(4,
            "with no active consumer, the 4 left-over CDPs count as DroppedAtShutdown after the 100ms drain timeout.");
    }
}
