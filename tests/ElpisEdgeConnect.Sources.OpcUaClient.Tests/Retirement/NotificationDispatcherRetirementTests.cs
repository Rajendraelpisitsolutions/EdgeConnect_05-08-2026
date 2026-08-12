// ============================================================================
// Tests: NotificationDispatcherRetirementTests — pin the OPC UA dispatcher
//        proof seam for Slice 0 commit 3.0:
//          * ingress closes BEFORE drain — OnNotification after
//            BeginRetiringIngress is rejected + recorded, never enqueued;
//          * RetireAndDrainAsync returns a STRUCTURED result that distinguishes
//            fully-drained from timeout/dropped (a void StopAsync is not a valid
//            proof seam);
//          * accepted work queued before retirement drains to FullyDrained when a
//            consumer is present;
//          * queued work with NO consumer times out → not FullyDrained (the
//            "session closed but dispatcher still queued ⇒ not Proven" case).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7;
//            commit-3.0 OPC UA surface-model review (2026-06-26).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests.Retirement;

public sealed class NotificationDispatcherRetirementTests
{
    private static OpcUaTypeMapper Mapper() => new(
        gatewayId: "gw-test",
        sourceInstanceId: "opcua-test",
        protocolName: OpcUaClientSourceConfiguration.ProtocolNameConstant,
        deviceId: "factorytalk");

    private static NotificationDispatcher CreateDispatcher(TimeSpan? drainTimeout = null) =>
        new(1_000, Mapper(), NullLogger.Instance, drainTimeout);

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

    [Fact]
    public async Task OnNotification_AfterBeginRetiringIngress_IsRejectedAndRecorded_NeverEnqueued()
    {
        var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(3);
        var notification = BuildNotificationFor(items);

        dispatcher.BeginRetiringIngress();
        dispatcher.OnNotification(subscription, notification, new List<string>());

        var result = await dispatcher.RetireAndDrainAsync(CancellationToken.None);

        result.RejectedAfterRetirement.Should().Be(3);    // counted
        result.FullyDrained.Should().BeTrue();            // nothing was enqueued
        dispatcher.GetCounters().Received.Should().Be(0); // never entered the channel
    }

    [Fact]
    public async Task RetireAndDrainAsync_EmptyChannel_ReportsFullyDrained()
    {
        var dispatcher = CreateDispatcher();

        dispatcher.BeginRetiringIngress();
        var result = await dispatcher.RetireAndDrainAsync(CancellationToken.None);

        result.FullyDrained.Should().BeTrue();
        result.DroppedAtShutdown.Should().Be(0);
    }

    [Fact]
    public async Task RetireAndDrainAsync_QueuedWithNoConsumer_TimesOut_NotFullyDrained()
    {
        // The "subscription/session closed but dispatcher still has queued work"
        // case — must NOT report Proven.
        var dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        var (subscription, items) = BuildSubscriptionWithItems(2);

        dispatcher.OnNotification(subscription, BuildNotificationFor(items), new List<string>());

        dispatcher.BeginRetiringIngress();
        var result = await dispatcher.RetireAndDrainAsync(CancellationToken.None);

        result.FullyDrained.Should().BeFalse();
        result.DroppedAtShutdown.Should().Be(2);
    }

    [Fact]
    public async Task OnNotification_RacingChannelCompletion_IsCountedAsDropped_NotSilentlyLost()
    {
        // Race window: an in-flight callback passed the ingress-flag check a moment
        // before retirement, then the channel was completed by the drain. The
        // callback must be COUNTED (received + dropped), never silently vanish.
        var dispatcher = CreateDispatcher();
        var (subscription, items) = BuildSubscriptionWithItems(2);

        await dispatcher.RetireAndDrainAsync(CancellationToken.None); // completes the channel
        // Deliberately NOT setting the ingress flag — exercise the passed-flag /
        // closed-channel window where OnNotification still runs.
        dispatcher.OnNotification(subscription, BuildNotificationFor(items), new List<string>());

        var counters = dispatcher.GetCounters();
        counters.Received.Should().Be(2);                 // captured before the write attempt
        counters.DroppedDueToBackpressure.Should().Be(2); // closed channel → counted as dropped
        counters.Dispatched.Should().Be(0);               // nothing leaked through
    }

    [Fact]
    public async Task RetireAndDrainAsync_QueuedWithConsumer_DrainsToFullyDrained()
    {
        var dispatcher = CreateDispatcher(TimeSpan.FromSeconds(5));
        var (subscription, items) = BuildSubscriptionWithItems(4);

        dispatcher.OnNotification(subscription, BuildNotificationFor(items), new List<string>());

        // A consumer drains concurrently — the accepted work clears before timeout.
        using var consumerCts = new CancellationTokenSource();
        var drained = 0;
        var consumer = Task.Run(async () =>
        {
            await foreach (var _ in dispatcher.ConsumeAsync(consumerCts.Token))
            {
                drained++;
            }
        });

        dispatcher.BeginRetiringIngress();
        var result = await dispatcher.RetireAndDrainAsync(CancellationToken.None);
        await consumer; // channel completed → ConsumeAsync exits cleanly

        result.FullyDrained.Should().BeTrue();
        result.DroppedAtShutdown.Should().Be(0);
        drained.Should().Be(4);
    }
}
