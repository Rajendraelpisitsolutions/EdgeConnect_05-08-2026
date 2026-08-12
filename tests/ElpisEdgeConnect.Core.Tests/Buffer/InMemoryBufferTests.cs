// ============================================================================
// File: Buffer/InMemoryBufferTests.cs
// Covers: capacity / drop policies / multi-sink cursors / ack idempotency /
//         sequence monotonicity / disposal.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class InMemoryBufferTests
{
    [Fact]
    public async Task Enqueue_Dequeue_Ack_RoundTrip()
    {
        await using var buf = NewBuffer(maxDepth: 8);
        await buf.RegisterSinkAsync("sink-1", default);

        await buf.EnqueueAsync(Batch(0, 4), default);
        var batch = await buf.DequeueBatchAsync("sink-1", 10, default);
        batch.Points.Should().HaveCount(4);
        batch.FirstSequence.Should().Be(0);
        batch.LastSequence.Should().Be(3);

        await buf.AckAsync("sink-1", batch.LastSequence, default);
        var stats = await buf.GetStatsAsync();
        stats.CurrentDepth.Should().Be(0);
        stats.TotalEnqueued.Should().Be(4);
        stats.TotalDrained.Should().Be(4);
    }

    [Fact]
    public async Task Dequeue_NotAdvancingCursor_ReturnsSameDataAgain()
    {
        await using var buf = NewBuffer(8);
        await buf.RegisterSinkAsync("sink-1", default);
        await buf.EnqueueAsync(Batch(0, 3), default);

        var first = await buf.DequeueBatchAsync("sink-1", 10, default);
        var second = await buf.DequeueBatchAsync("sink-1", 10, default);

        second.Points.Should().HaveCount(first.Points.Count);
        second.FirstSequence.Should().Be(first.FirstSequence);
    }

    [Fact]
    public async Task Capacity_DropOldest_EvictsInFifoOrder()
    {
        await using var buf = NewBuffer(maxDepth: 4, drop: DropPolicy.DropOldest);
        await buf.RegisterSinkAsync("sink-1", default);

        await buf.EnqueueAsync(Batch(0, 6), default); // 6 into capacity 4: drops first 2
        var stats = await buf.GetStatsAsync();
        stats.TotalDropped.Should().Be(2);
        stats.CurrentDepth.Should().Be(4);

        var batch = await buf.DequeueBatchAsync("sink-1", 10, default);
        batch.Points.Should().HaveCount(4);
        batch.FirstSequence.Should().Be(2);
        batch.LastSequence.Should().Be(5);
    }

    [Fact]
    public async Task Capacity_DropNewest_RejectsLatestWrites()
    {
        await using var buf = NewBuffer(maxDepth: 4, drop: DropPolicy.DropNewest);
        await buf.RegisterSinkAsync("sink-1", default);

        await buf.EnqueueAsync(Batch(0, 6), default);
        var stats = await buf.GetStatsAsync();
        stats.TotalDropped.Should().Be(2);
        stats.CurrentDepth.Should().Be(4);

        var batch = await buf.DequeueBatchAsync("sink-1", 10, default);
        batch.LastSequence.Should().Be(3, "newest two were dropped, oldest four kept");
    }

    [Fact]
    public async Task Capacity_Block_WaitsForAck()
    {
        await using var buf = NewBuffer(maxDepth: 2, drop: DropPolicy.Block);
        await buf.RegisterSinkAsync("sink-1", default);
        await buf.EnqueueAsync(Batch(0, 2), default); // fills capacity

        var producer = Task.Run(async () =>
        {
            await buf.EnqueueAsync(Batch(2, 1), default); // must block
        });

        // Give producer a beat to enter the wait loop.
        await Task.Delay(50);
        producer.IsCompleted.Should().BeFalse();

        // Drain one to free a slot.
        var batch = await buf.DequeueBatchAsync("sink-1", 1, default);
        await buf.AckAsync("sink-1", batch.LastSequence, default);

        await producer.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MultiSink_Divergence_FastAdvances_SlowLags_MinReleases()
    {
        await using var buf = NewBuffer(maxDepth: 8);
        await buf.RegisterSinkAsync("fast", default);
        await buf.RegisterSinkAsync("slow", default);

        await buf.EnqueueAsync(Batch(0, 4), default);

        // Fast drains everything.
        var fastBatch = await buf.DequeueBatchAsync("fast", 10, default);
        await buf.AckAsync("fast", fastBatch.LastSequence, default);

        // Slow has not acked → buffer still holds all 4 points.
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(4);

        // Slow drains and acks 2 of 4.
        var slowBatch = await buf.DequeueBatchAsync("slow", 2, default);
        slowBatch.LastSequence.Should().Be(1);
        await buf.AckAsync("slow", 1, default);

        // min(cursors) is now 2 → 2 slots freed.
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(2);

        // Slow finishes — depth drains to zero.
        await buf.AckAsync("slow", 3, default);
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(0);
    }

    [Fact]
    public async Task Ack_Idempotent_NoStateChangeOnRepeat()
    {
        await using var buf = NewBuffer(8);
        await buf.RegisterSinkAsync("sink-1", default);
        await buf.EnqueueAsync(Batch(0, 3), default);
        var batch = await buf.DequeueBatchAsync("sink-1", 10, default);

        await buf.AckAsync("sink-1", batch.LastSequence, default);
        var depthAfterFirst = (await buf.GetStatsAsync()).CurrentDepth;

        await buf.AckAsync("sink-1", batch.LastSequence, default); // same seq again
        await buf.AckAsync("sink-1", batch.LastSequence - 1, default); // older seq
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(depthAfterFirst);
    }

    [Fact]
    public async Task DequeueEmpty_ReturnsEmptyBatch()
    {
        await using var buf = NewBuffer(4);
        await buf.RegisterSinkAsync("sink-1", default);
        var batch = await buf.DequeueBatchAsync("sink-1", 10, default);
        batch.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task DeregisterSink_ReleasesPinnedData()
    {
        await using var buf = NewBuffer(4);
        await buf.RegisterSinkAsync("fast", default);
        await buf.RegisterSinkAsync("slow", default);
        await buf.EnqueueAsync(Batch(0, 4), default);

        var fastBatch = await buf.DequeueBatchAsync("fast", 10, default);
        await buf.AckAsync("fast", fastBatch.LastSequence, default);

        // Slow is pinning the data; depth should be 4.
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(4);

        await buf.DeregisterSinkAsync("slow", default);

        // Now only fast cursor exists, which is at head → buffer drains.
        (await buf.GetStatsAsync()).CurrentDepth.Should().Be(0);
    }

    [Fact]
    public async Task Sequences_AreMonotonic_UnderConcurrentEnqueue()
    {
        await using var buf = NewBuffer(maxDepth: 4096);
        await buf.RegisterSinkAsync("sink-1", default);

        var producers = new List<Task>();
        for (int p = 0; p < 4; p++)
        {
            int producerId = p;
            producers.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 250; i++)
                {
                    await buf.EnqueueAsync(new[] { Point(producerId * 1000 + i) }, default);
                }
            }));
        }
        await Task.WhenAll(producers);

        var batch = await buf.DequeueBatchAsync("sink-1", 10_000, default);
        batch.Points.Should().HaveCount(1000);
        // Sequence range MUST be exactly [0, 999] (1000 distinct buffer sequences).
        batch.FirstSequence.Should().Be(0);
        batch.LastSequence.Should().Be(999);
    }

    [Fact]
    public async Task DequeueAfterDispose_Throws()
    {
        var buf = NewBuffer(4);
        await buf.RegisterSinkAsync("s", default);
        await buf.DisposeAsync();

        Func<Task> act = async () => await buf.DequeueBatchAsync("s", 1, default);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // ========================================================================
    // C2a close-out pins (independent review mutation coverage)
    // ========================================================================

    /// <summary>
    /// R1 pin (Mutation 8): the contract requires that a sink registered AFTER
    /// data has been enqueued must replay the buffered backlog. This catches
    /// the regression where <c>RegisterSinkAsync</c> initializes the cursor at
    /// <c>_head</c> instead of <c>_tail</c>.
    /// </summary>
    [Fact]
    public async Task RegisterSinkAfterEnqueue_PlaysBackBacklog()
    {
        await using var buf = NewBuffer(maxDepth: 8);
        await buf.EnqueueAsync(Batch(0, 4), default);

        await buf.RegisterSinkAsync("late-sink", default);

        var batch = await buf.DequeueBatchAsync("late-sink", 10, default);
        batch.Points.Should().HaveCount(4, "newly registered sink must see the buffered backlog");
        batch.FirstSequence.Should().Be(0);
        batch.LastSequence.Should().Be(3);
    }

    /// <summary>
    /// R5 pin (Mutation 3): when DropOldest evicts data, any sink whose cursor
    /// pointed at the evicted slot must be fast-forwarded so it never observes
    /// stale data.
    /// </summary>
    [Fact]
    public async Task DropOldest_FastForwardsLaggingSink()
    {
        await using var buf = NewBuffer(maxDepth: 4, drop: DropPolicy.DropOldest);
        await buf.RegisterSinkAsync("slow", default);

        await buf.EnqueueAsync(Batch(0, 6), default); // forces 2 evictions

        // Slow sink should now be fast-forwarded to the new tail (sequence 2),
        // and the dequeue must return the surviving 4 points starting at 2.
        var batch = await buf.DequeueBatchAsync("slow", 10, default);
        batch.Points.Should().HaveCount(4);
        batch.FirstSequence.Should().Be(2,
            "evicted slots 0 and 1 must not appear; the slow cursor must skip them");
        batch.LastSequence.Should().Be(5);
    }

    /// <summary>
    /// R7 pin (Mutation 26): the empty <see cref="BufferBatch"/> singleton
    /// must report sequences as -1 / -1 so callers can sanity-check.
    /// </summary>
    [Fact]
    public async Task DequeueEmpty_BatchHasSentinelSequences()
    {
        await using var buf = NewBuffer(4);
        await buf.RegisterSinkAsync("s", default);
        var batch = await buf.DequeueBatchAsync("s", 10, default);

        batch.IsEmpty.Should().BeTrue();
        batch.FirstSequence.Should().Be(-1);
        batch.LastSequence.Should().Be(-1);
        batch.Should().BeSameAs(BufferBatch.Empty);
    }

    /// <summary>R8 pin: dequeue for an unregistered sink throws.</summary>
    [Fact]
    public async Task DequeueBatch_UnregisteredSink_Throws()
    {
        await using var buf = NewBuffer(4);
        Func<Task> act = async () => await buf.DequeueBatchAsync("ghost", 1, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>R8 pin: ack for an unregistered sink throws.</summary>
    [Fact]
    public async Task Ack_UnregisteredSink_Throws()
    {
        await using var buf = NewBuffer(4);
        Func<Task> act = async () => await buf.AckAsync("ghost", 0, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>R8 pin: GetStatsAsync after dispose throws.</summary>
    [Fact]
    public async Task GetStats_AfterDispose_Throws()
    {
        var buf = NewBuffer(4);
        await buf.DisposeAsync();
        Func<Task> act = async () => await buf.GetStatsAsync();
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>R8 pin: dequeue with maxCount = 0 returns Empty without touching state.</summary>
    [Fact]
    public async Task DequeueBatch_MaxCountZero_ReturnsEmpty()
    {
        await using var buf = NewBuffer(4);
        await buf.RegisterSinkAsync("s", default);
        await buf.EnqueueAsync(Batch(0, 2), default);

        var batch = await buf.DequeueBatchAsync("s", 0, default);
        batch.Should().BeSameAs(BufferBatch.Empty);
    }

    /// <summary>R8 pin: re-registering a sink at the buffer level preserves the cursor.</summary>
    [Fact]
    public async Task RegisterSink_Idempotent_PreservesCursor()
    {
        await using var buf = NewBuffer(8);
        await buf.RegisterSinkAsync("s", default);
        await buf.EnqueueAsync(Batch(0, 4), default);
        var batch = await buf.DequeueBatchAsync("s", 2, default);
        await buf.AckAsync("s", batch.LastSequence, default); // cursor at 2

        await buf.RegisterSinkAsync("s", default); // re-register

        var nextBatch = await buf.DequeueBatchAsync("s", 10, default);
        nextBatch.Points.Should().HaveCount(2, "re-register must not reset the cursor to tail");
        nextBatch.FirstSequence.Should().Be(2);
    }
}
