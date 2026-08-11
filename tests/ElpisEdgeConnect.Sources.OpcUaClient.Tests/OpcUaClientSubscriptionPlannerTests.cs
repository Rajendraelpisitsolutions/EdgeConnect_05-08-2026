// ============================================================================
// Tests: OpcUaClientSubscriptionPlannerTests — pins the pure-logic
//        batching planner.
//
//        Invariants:
//          * 0 items → 0 batches (no empty-subscription overhead)
//          * 1 → 1 batch with 1 item
//          * 1000 → 1 batch with 1000 items
//          * 1001 → 2 batches (1000 + 1) — the failure mode that
//            silently overflows server limits if not caught here
//          * 5000 → 5 batches × 1000 items
//          * 5500 → 6 batches (5×1000 + 500)
//          * Order preserved across batches (subscription 0 gets the
//            first 1000 in operator order)
//          * 100,000 items → 100 batches (the locked per-session cap)
//          * 100,001 items → throws InvalidOperationException with
//            OPCUA.TOO_MANY_MONITORED_ITEMS (PR 3 amendment #1, user
//            lock 2026-05-29 Option A)
//          * Constants pinned (MaxItemsPerSubscription = 1000,
//            MaxSubscriptionsPerSession = 100,
//            MaxMonitoredItemsPerSession = 100,000)
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §2.5
//            PR 3 amendment #1 (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientSubscriptionPlannerTests
{
    private static IReadOnlyList<MonitoredItemConfig> MakeItems(int count)
    {
        var items = new MonitoredItemConfig[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new MonitoredItemConfig
            {
                NodeId = $"ns=2;i={1000 + i}",
                DisplayName = $"Tag_{i:D5}",
            };
        }
        return items;
    }

    // ─── Locked constants ─────────────────────────────────────────────

    [Fact]
    public void Constants_LockedAtV21AndPr3Amendment()
    {
        OpcUaClientSubscriptionPlanner.MaxItemsPerSubscription.Should().Be(1_000);
        OpcUaClientSubscriptionPlanner.MaxSubscriptionsPerSession.Should().Be(100);
        OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession.Should().Be(100_000);
    }

    // ─── Boundary batching ────────────────────────────────────────────

    [Fact]
    public void Plan_ZeroItems_ProducesZeroBatches()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(System.Array.Empty<MonitoredItemConfig>());

        result.Should().BeEmpty(
            "an empty configuration must produce zero subscriptions — no point creating empty ones.");
    }

    [Fact]
    public void Plan_OneItem_ProducesOneBatchOfOne()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(1));

        result.Should().HaveCount(1);
        result[0].Should().HaveCount(1);
    }

    [Fact]
    public void Plan_ExactlyOneBatchSize_ProducesOneBatch()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(1_000));

        result.Should().HaveCount(1);
        result[0].Should().HaveCount(1_000);
    }

    [Fact]
    public void Plan_OneOverBatchSize_ProducesTwoBatches()
    {
        // The exact failure mode that silently overflows server limits
        // when batching is naive.
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(1_001));

        result.Should().HaveCount(2);
        result[0].Should().HaveCount(1_000);
        result[1].Should().HaveCount(1);
    }

    [Fact]
    public void Plan_FiveThousand_ProducesFiveBatches()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(5_000));

        result.Should().HaveCount(5);
        result.Should().AllSatisfy(b => b.Should().HaveCount(1_000));
    }

    [Fact]
    public void Plan_FiveThousandFiveHundred_ProducesSixBatches()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(5_500));

        result.Should().HaveCount(6);
        result.Take(5).Should().AllSatisfy(b => b.Should().HaveCount(1_000));
        result[5].Should().HaveCount(500);
    }

    // ─── v2.1 §6 Q9 sequence boundaries ──────────────────────────────

    [Fact]
    public void Plan_ThirtyKItems_ProducesThirtySubscriptions()
    {
        // Primary target per v2.1 §6 Q9 — must produce exactly 30.
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(30_000));

        result.Should().HaveCount(30);
    }

    [Fact]
    public void Plan_FiftyKItems_ProducesFiftySubscriptions()
    {
        // Stretch target per v2.1 §6 Q9 — must produce exactly 50.
        var result = OpcUaClientSubscriptionPlanner.Plan(MakeItems(50_000));

        result.Should().HaveCount(50);
    }

    // ─── Per-session cap (PR 3 amendment #1, user lock 2026-05-29) ───

    [Fact]
    public void Plan_AtMaxItemsPerSession_ProducesMaxSubscriptions()
    {
        var result = OpcUaClientSubscriptionPlanner.Plan(
            MakeItems(OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession));

        result.Should().HaveCount(OpcUaClientSubscriptionPlanner.MaxSubscriptionsPerSession);
    }

    [Fact]
    public void Plan_OverMaxItemsPerSession_ThrowsTooManyMonitoredItems()
    {
        // The locked governance check — 100,001 items exceeds the
        // 100-subscription / 100,000-item per-session cap. Operator
        // must either reduce tag count or split across multiple source
        // instances.
        var items = MakeItems(OpcUaClientSubscriptionPlanner.MaxMonitoredItemsPerSession + 1);

        var act = () => OpcUaClientSubscriptionPlanner.Plan(items);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OPCUA.TOO_MANY_MONITORED_ITEMS*");
    }

    // ─── Order preservation ───────────────────────────────────────────

    [Fact]
    public void Plan_PreservesOperatorOrderAcrossBatches()
    {
        // 1500 items → subscription 0 gets items 0..999, subscription 1
        // gets items 1000..1499. Operators rely on this for wizard
        // re-render consistency.
        var items = MakeItems(1_500);

        var result = OpcUaClientSubscriptionPlanner.Plan(items);

        result.Should().HaveCount(2);
        result[0].First().NodeId.Should().Be(items[0].NodeId);
        result[0].Last().NodeId.Should().Be(items[999].NodeId);
        result[1].First().NodeId.Should().Be(items[1_000].NodeId);
        result[1].Last().NodeId.Should().Be(items[1_499].NodeId);
    }
}
