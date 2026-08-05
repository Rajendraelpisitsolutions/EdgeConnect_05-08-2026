// ============================================================================
// File: Diagnostics/BoundedEventLogTests.cs
// Purpose: Phase 1 pin tests for BoundedEventLog. Covers capacity, FIFO
//          ordering after wrap, exact drop accounting, the atomic
//          SnapshotWithCounters() path, and concurrent-writer correctness.
// Milestone: C4 — phase 1.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class BoundedEventLogTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveCapacity()
    {
        var act = () => new BoundedEventLog<int>(0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new BoundedEventLog<int>(-1);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EmptyLog_HasZeroCounts()
    {
        var log = new BoundedEventLog<int>(4);
        log.Count.Should().Be(0);
        log.TotalAdded.Should().Be(0);
        log.TotalDropped.Should().Be(0);
        log.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Add_BelowCapacity_PreservesOrder()
    {
        var log = new BoundedEventLog<int>(4);
        log.Add(1);
        log.Add(2);
        log.Add(3);

        log.Count.Should().Be(3);
        log.TotalAdded.Should().Be(3);
        log.TotalDropped.Should().Be(0);
        log.Snapshot().Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Add_AtCapacity_DoesNotDrop()
    {
        var log = new BoundedEventLog<int>(3);
        log.Add(1);
        log.Add(2);
        log.Add(3);

        log.Count.Should().Be(3);
        log.TotalDropped.Should().Be(0);
        log.Snapshot().Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Add_OverCapacity_EvictsOldestAndCountsDrop()
    {
        var log = new BoundedEventLog<int>(3);
        log.Add(1);
        log.Add(2);
        log.Add(3);
        log.Add(4); // evicts 1
        log.Add(5); // evicts 2

        log.Count.Should().Be(3);
        log.TotalAdded.Should().Be(5);
        log.TotalDropped.Should().Be(2);
        log.Snapshot().Should().Equal(new[] { 3, 4, 5 });
    }

    [Fact]
    public void SnapshotWithCounters_IsInternallyConsistent()
    {
        var log = new BoundedEventLog<string>(2);
        log.Add("a");
        log.Add("b");
        log.Add("c"); // evicts "a"

        var snap = log.SnapshotWithCounters();

        snap.Capacity.Should().Be(2);
        snap.LiveCount.Should().Be(2);
        snap.TotalAdded.Should().Be(3);
        snap.TotalDropped.Should().Be(1);
        snap.Entries.Should().Equal(new[] { "b", "c" });
    }

    [Fact]
    public async Task ConcurrentWriters_AllAddsAreAccountedFor()
    {
        // Many parallel writers must produce a self-consistent log: every
        // Add increments TotalAdded exactly once and any over-capacity Add
        // increments TotalDropped exactly once. The two counters together
        // give the lossless invariant TotalAdded == LiveCount + TotalDropped.
        const int Capacity = 64;
        const int WritersCount = 16;
        const int AddsPerWriter = 1_000;
        var log = new BoundedEventLog<int>(Capacity);

        var tasks = Enumerable.Range(0, WritersCount).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < AddsPerWriter; i++)
            {
                log.Add(w * AddsPerWriter + i);
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        var snap = log.SnapshotWithCounters();
        snap.TotalAdded.Should().Be(WritersCount * AddsPerWriter);
        snap.LiveCount.Should().Be(Capacity);
        snap.TotalDropped.Should().Be(WritersCount * AddsPerWriter - Capacity);
        snap.Entries.Should().HaveCount(Capacity);
    }

    [Fact]
    public void Snapshot_ReturnsFreshArrayPerCall()
    {
        var log = new BoundedEventLog<int>(4);
        log.Add(1);
        log.Add(2);

        var a = log.Snapshot();
        var b = log.Snapshot();
        a.Should().NotBeSameAs(b);
        a.Should().Equal(b);
    }
}
