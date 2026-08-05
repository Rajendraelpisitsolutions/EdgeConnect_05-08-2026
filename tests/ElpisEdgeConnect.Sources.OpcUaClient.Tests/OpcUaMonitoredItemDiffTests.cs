// ============================================================================
// Tests: OpcUaMonitoredItemDiffTests — pin the PR 6b pure-logic diff
//        for hot reconfigure. Substituting nothing — the diff is a static
//        function over MonitoredItemConfig records.
//
//        Invariants pinned (per PR 6b plan + amendments, user lock
//        2026-05-29):
//          1. Empty + Empty = IsIdempotent (amendment #5)
//          2. Add only: empty → 3 items = 3 Added
//          3. Remove only: 3 items → empty = 3 Removed
//          4. SamplingInterval change → 1 Modified with
//             SamplingIntervalChanged=true (amendment #3)
//          5. QueueSize change → 1 Modified with QueueSizeChanged=true
//          6. DeadbandPercent change → 1 Modified with
//             DeadbandPercentChanged=true
//          7. DisplayName-only change = Unchanged (cosmetic)
//          8. Mixed: 1 added + 1 removed + 1 modified + 1 unchanged
//          9. Idempotent identical lists = IsIdempotent=true
//         10. NodeId semantic canonicalisation — "ns=2;i=00000001"
//             matches "ns=2;i=1" (normalised parse-then-tostring per
//             amendment #4 refinement)
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
// ============================================================================

using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaMonitoredItemDiffTests
{
    private static MonitoredItemConfig Item(
        string nodeId,
        string displayName = "Tag",
        int? sampling = null,
        uint? queueSize = null,
        double? deadband = null) => new()
    {
        NodeId = nodeId,
        DisplayName = displayName,
        SamplingIntervalMs = sampling,
        QueueSize = queueSize,
        DeadbandPercent = deadband,
    };

    // ─── Idempotent / empty cases ─────────────────────────────────────

    [Fact]
    public void Compute_EmptyToEmpty_IsIdempotent()
    {
        var result = OpcUaMonitoredItemDiff.Compute(
            new List<MonitoredItemConfig>(),
            new List<MonitoredItemConfig>());

        result.IsIdempotent.Should().BeTrue();
        result.ChangeCount.Should().Be(0);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Modified.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    [Fact]
    public void Compute_IdenticalLists_IsIdempotent_AllUnchanged()
    {
        // Amendment #5 — same config in/out short-circuits to "no executor work".
        var set = new[]
        {
            Item("ns=2;i=1", sampling: 50),
            Item("ns=2;i=2", sampling: 100, queueSize: 10),
            Item("ns=2;i=3", deadband: 2.5),
        };

        var result = OpcUaMonitoredItemDiff.Compute(set, set);

        result.IsIdempotent.Should().BeTrue();
        result.Unchanged.Should().HaveCount(3);
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Modified.Should().BeEmpty();
    }

    // ─── Add only ─────────────────────────────────────────────────────

    [Fact]
    public void Compute_AddThreeItems_ClassifiedAsAdded()
    {
        var newSet = new[]
        {
            Item("ns=2;i=1"),
            Item("ns=2;i=2"),
            Item("ns=2;i=3"),
        };

        var result = OpcUaMonitoredItemDiff.Compute(new List<MonitoredItemConfig>(), newSet);

        result.Added.Should().HaveCount(3);
        result.Removed.Should().BeEmpty();
        result.Modified.Should().BeEmpty();
        result.IsIdempotent.Should().BeFalse();
        result.ChangeCount.Should().Be(3);
    }

    // ─── Remove only ──────────────────────────────────────────────────

    [Fact]
    public void Compute_RemoveAllItems_ClassifiedAsRemoved()
    {
        var oldSet = new[]
        {
            Item("ns=2;i=1"),
            Item("ns=2;i=2"),
            Item("ns=2;i=3"),
        };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, new List<MonitoredItemConfig>());

        result.Removed.Should().HaveCount(3);
        result.Added.Should().BeEmpty();
        result.Modified.Should().BeEmpty();
        result.ChangeCount.Should().Be(3);
    }

    // ─── Modify — per-field flags ─────────────────────────────────────

    [Fact]
    public void Compute_SamplingIntervalChange_FlaggedAsModified()
    {
        // Amendment #3 — SamplingInterval-only change is hot-applicable.
        var oldSet = new[] { Item("ns=2;i=1", sampling: 50) };
        var newSet = new[] { Item("ns=2;i=1", sampling: 100) };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.Modified.Should().HaveCount(1);
        result.Modified[0].SamplingIntervalChanged.Should().BeTrue();
        result.Modified[0].QueueSizeChanged.Should().BeFalse();
        result.Modified[0].DeadbandPercentChanged.Should().BeFalse();
    }

    [Fact]
    public void Compute_QueueSizeChange_FlaggedAsModified()
    {
        var oldSet = new[] { Item("ns=2;i=1", queueSize: 2) };
        var newSet = new[] { Item("ns=2;i=1", queueSize: 10) };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.Modified.Should().HaveCount(1);
        result.Modified[0].QueueSizeChanged.Should().BeTrue();
        result.Modified[0].SamplingIntervalChanged.Should().BeFalse();
    }

    [Fact]
    public void Compute_DeadbandPercentChange_FlaggedAsModified()
    {
        var oldSet = new[] { Item("ns=2;i=1", deadband: 1.0) };
        var newSet = new[] { Item("ns=2;i=1", deadband: 5.0) };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.Modified.Should().HaveCount(1);
        result.Modified[0].DeadbandPercentChanged.Should().BeTrue();
    }

    // ─── DisplayName-only change = Unchanged (cosmetic) ───────────────

    [Fact]
    public void Compute_DisplayNameChangeOnly_TreatedAsUnchanged()
    {
        var oldSet = new[] { Item("ns=2;i=1", displayName: "OldName", sampling: 50) };
        var newSet = new[] { Item("ns=2;i=1", displayName: "NewName", sampling: 50) };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.IsIdempotent.Should().BeTrue(
            "DisplayName is cosmetic — no server-side mutation needed.");
        result.Unchanged.Should().HaveCount(1);
        result.Modified.Should().BeEmpty();
    }

    // ─── Mixed diff ───────────────────────────────────────────────────

    [Fact]
    public void Compute_Mixed_OneAddedOneRemovedOneModifiedOneUnchanged()
    {
        var oldSet = new[]
        {
            Item("ns=2;i=1", sampling: 50),       // unchanged
            Item("ns=2;i=2", sampling: 50),       // modified (sampling)
            Item("ns=2;i=3", sampling: 50),       // removed
        };
        var newSet = new[]
        {
            Item("ns=2;i=1", sampling: 50),       // unchanged
            Item("ns=2;i=2", sampling: 100),      // modified (sampling)
            Item("ns=2;i=4", sampling: 50),       // added
        };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.Unchanged.Should().HaveCount(1);
        result.Added.Should().HaveCount(1);
        result.Removed.Should().HaveCount(1);
        result.Modified.Should().HaveCount(1);
        result.ChangeCount.Should().Be(3);

        result.Added[0].NodeId.Should().Be("ns=2;i=4");
        result.Removed[0].NodeId.Should().Be("ns=2;i=3");
        result.Modified[0].New.NodeId.Should().Be("ns=2;i=2");
        result.Modified[0].SamplingIntervalChanged.Should().BeTrue();
    }

    // ─── NodeId semantic canonicalisation (amendment #4) ──────────────

    [Fact]
    public void Compute_SemanticallyEqualNodeIds_NotClassifiedAsAddRemove()
    {
        // "ns=2;i=00000001" and "ns=2;i=1" are semantically identical UA NodeIds.
        // Raw string equality would misclassify as Added+Removed. Canonical
        // normalisation must match them.
        var oldSet = new[] { Item("ns=2;i=1", sampling: 50) };
        var newSet = new[] { Item("ns=2;i=00000001", sampling: 50) };

        var result = OpcUaMonitoredItemDiff.Compute(oldSet, newSet);

        result.IsIdempotent.Should().BeTrue(
            "Canonical NodeId matching must treat semantically-equal IDs as the same item.");
        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
    }
}
