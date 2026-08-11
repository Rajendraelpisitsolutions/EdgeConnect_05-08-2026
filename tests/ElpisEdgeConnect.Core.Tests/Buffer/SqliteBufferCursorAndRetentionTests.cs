// ============================================================================
// File: Buffer/SqliteBufferCursorAndRetentionTests.cs
// Covers: multi-sink divergence, min-cursor reclaim, DropOldest fast-forward,
//         deregister releases pinned data, MaxAge retention with explicit
//         counter attribution, MaxDepth + DropNewest reject.
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

[Collection(RoutingIntegrationCollection.Name)]
public sealed class SqliteBufferCursorAndRetentionTests
{
    private static async Task EventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new Xunit.Sdk.XunitException($"Condition not met within {timeout}");
    }

    [Fact]
    public async Task MultiSink_Divergence_FastDrains_SlowPinsTail()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy(maxDepth: 8));
            await buf.RegisterSinkAsync("fast", default);
            await buf.RegisterSinkAsync("slow", default);

            await buf.EnqueueAsync(Batch(0, 4), default);

            var fastBatch = await buf.DequeueBatchAsync("fast", 10, default);
            await buf.AckAsync("fast", fastBatch.LastSequence, default);

            // Even though the reclaim loop will run, slow still pins all 4.
            // Wait one tick to be safe, then assert.
            await Task.Delay(150);
            var stats = await buf.GetStatsAsync();
            stats.CurrentDepth.Should().Be(4, "slow sink still pins the data");
            stats.OldestUnackedSinkId.Should().Be("slow");

            // Slow drains and acks 2 of 4.
            var slowBatch = await buf.DequeueBatchAsync("slow", 2, default);
            slowBatch.LastSequence.Should().Be(1);
            await buf.AckAsync("slow", 1, default);

            // Eventually the reclaim loop runs and frees rows < 2.
            // D phase-1 stabilization: timeout relaxed 2 s → 10 s because the
            // reclaim loop runs on its own timer and can be starved under
            // full-suite parallel load. The test's meaning is "eventually",
            // not "within 2 seconds".
            await EventuallyAsync(async () => (await buf.GetStatsAsync()).CurrentDepth == 2, TimeSpan.FromSeconds(10));

            // Slow finishes.
            await buf.AckAsync("slow", 3, default);
            await EventuallyAsync(async () => (await buf.GetStatsAsync()).CurrentDepth == 0, TimeSpan.FromSeconds(10));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DropOldest_FastForwardsLaggingSink_AndIncrementsCounter()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxDepth: 4, drop: DropPolicy.DropOldest));
            await buf.RegisterSinkAsync("slow", default);

            await buf.EnqueueAsync(Batch(0, 6), default); // forces 2 evictions

            var stats = await buf.GetStatsAsync();
            stats.DroppedByCapacity.Should().Be(2);
            stats.DroppedByRetention.Should().Be(0);
            stats.SinksFastForwarded.Should().BeGreaterThan(0);

            var batch = await buf.DequeueBatchAsync("slow", 10, default);
            batch.Points.Should().HaveCount(4);
            batch.FirstSequence.Should().Be(2,
                "evicted slots 0 and 1 must not appear; the slow cursor must skip them");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DropNewest_RefusesLatestWrites_AndIncrementsCapacityCounter()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxDepth: 4, drop: DropPolicy.DropNewest));
            await buf.RegisterSinkAsync("s", default);

            await buf.EnqueueAsync(Batch(0, 6), default);

            var stats = await buf.GetStatsAsync();
            stats.DroppedByCapacity.Should().Be(2);
            stats.DroppedByRetention.Should().Be(0);
            stats.CurrentDepth.Should().Be(4);

            var batch = await buf.DequeueBatchAsync("s", 10, default);
            batch.LastSequence.Should().Be(3, "newest two were dropped, oldest four kept");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DeregisterSink_ReleasesPinnedData()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy(maxDepth: 8));
            await buf.RegisterSinkAsync("fast", default);
            await buf.RegisterSinkAsync("slow", default);
            await buf.EnqueueAsync(Batch(0, 4), default);

            var fastBatch = await buf.DequeueBatchAsync("fast", 10, default);
            await buf.AckAsync("fast", fastBatch.LastSequence, default);

            // slow is pinning the data; depth should still be 4.
            await Task.Delay(150);
            (await buf.GetStatsAsync()).CurrentDepth.Should().Be(4);

            await buf.DeregisterSinkAsync("slow", default);

            // D phase-1 stabilization: 2 s → 10 s per flaky-test disposition policy.
            await EventuallyAsync(async () => (await buf.GetStatsAsync()).CurrentDepth == 0, TimeSpan.FromSeconds(10));
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// MaxAge retention is **explicit unread-data loss**. The expired rows
    /// are deleted regardless of cursor position; the loss is counted under
    /// `DroppedByRetention`, NOT `DroppedByCapacity`.
    /// </summary>
    [Fact]
    public async Task MaxAge_RetentionExpiresUnackedRows_AndCountsSeparately()
    {
        var path = NewFilePath();
        try
        {
            // Very short MaxAge so we don't have to wait long. ReclaimInterval
            // is also short (50 ms) so the sweep runs quickly.
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxAge: TimeSpan.FromMilliseconds(50)));
            await buf.RegisterSinkAsync("slow", default);

            await buf.EnqueueAsync(Batch(0, 5), default);
            // Wait for the rows to age out and the reclaim loop to sweep.
            // D phase-1 stabilization: 3 s → 10 s per flaky-test disposition policy.
            await EventuallyAsync(async () => (await buf.GetStatsAsync()).DroppedByRetention >= 5, TimeSpan.FromSeconds(10));

            var stats = await buf.GetStatsAsync();
            stats.DroppedByRetention.Should().Be(5, "all 5 rows aged out under MaxAge");
            stats.DroppedByCapacity.Should().Be(0, "capacity was not the trigger");
            stats.CurrentDepth.Should().Be(0);
            stats.TotalDropped.Should().Be(stats.DroppedByCapacity + stats.DroppedByRetention);
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// C2b close-out R2 (catches mutation 20 from the independent review):
    /// SinksFastForwarded must increment ONLY when an eviction actually
    /// bumped at least one lagging cursor. An eviction that runs while no
    /// sinks are registered (or all sinks are caught up to head) must not
    /// increment the counter.
    /// </summary>
    [Fact]
    public async Task DropOldest_NoLaggingSinks_DoesNotIncrementSinksFastForwarded()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxDepth: 4, drop: DropPolicy.DropOldest));
            // Deliberately register NO sinks. Eviction will still run when
            // the buffer fills up because it has no min-cursor to honor.
            await buf.EnqueueAsync(Batch(0, 6), default); // forces 2 evictions

            var stats = await buf.GetStatsAsync();
            stats.DroppedByCapacity.Should().Be(2);
            stats.SinksFastForwarded.Should().Be(0,
                "no sinks were registered, so no cursor was actually fast-forwarded; " +
                "the SinksFastForwarded counter must remain at zero.");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task MaxAge_DefaultDisabled_DoesNotEvict()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy()); // MaxAge null
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 3), default);

            await Task.Delay(200);
            var stats = await buf.GetStatsAsync();
            stats.CurrentDepth.Should().Be(3);
            stats.DroppedByRetention.Should().Be(0);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
