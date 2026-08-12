// ============================================================================
// File: Buffer/SqliteBufferContractTests.cs
// Purpose: Contract-parity tests proving SqliteBuffer satisfies the
//          IMessageBuffer contract identically to InMemoryBuffer.
//          The single most important pin: register-at-tail behavior
//          (D12 reversal).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferContractTests
{
    private static async Task<SqliteBuffer> NewBufferAsync(
        int maxDepth = 64,
        DropPolicy drop = DropPolicy.Block,
        string? path = null)
    {
        path ??= NewFilePath();
        return await SqliteBuffer.OpenAsync("route-test", path, SmallSqlitePolicy(maxDepth, drop));
    }

    [Fact]
    public async Task Enqueue_Dequeue_Ack_RoundTrip()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("sink-1", default);

            await buf.EnqueueAsync(Batch(0, 4), default);
            var batch = await buf.DequeueBatchAsync("sink-1", 10, default);
            batch.Points.Should().HaveCount(4);
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(3);

            await buf.AckAsync("sink-1", batch.LastSequence, default);
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// **Critical contract pin (D12 reversal).** A sink registered AFTER
    /// data has been enqueued must replay the buffered backlog. This is the
    /// SqliteBuffer mirror of the C2a `RegisterSinkAfterEnqueue_PlaysBackBacklog`
    /// pin and the single most important parity check between the two
    /// implementations.
    /// </summary>
    [Fact]
    public async Task Register_NewSink_StartsAtTail_AndReplaysBacklog()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.EnqueueAsync(Batch(0, 4), default);

            await buf.RegisterSinkAsync("late-sink", default);

            var batch = await buf.DequeueBatchAsync("late-sink", 10, default);
            batch.Points.Should().HaveCount(4, "newly registered sink must replay the durable backlog");
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(3);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task RegisterSink_Idempotent_PreservesCursor()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 4), default);
            var batch = await buf.DequeueBatchAsync("s", 2, default);
            await buf.AckAsync("s", batch.LastSequence, default);

            await buf.RegisterSinkAsync("s", default); // re-register

            var nextBatch = await buf.DequeueBatchAsync("s", 10, default);
            nextBatch.Points.Should().HaveCount(2, "re-register must not reset the cursor to tail");
            nextBatch.FirstSequence.Should().Be(2);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Ack_Idempotent_NoStateChangeOnRepeat()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 3), default);
            var batch = await buf.DequeueBatchAsync("s", 10, default);

            await buf.AckAsync("s", batch.LastSequence, default);
            await buf.AckAsync("s", batch.LastSequence, default); // same seq again
            await buf.AckAsync("s", batch.LastSequence - 1, default); // older seq

            // Subsequent dequeue must see no points (all acked).
            var next = await buf.DequeueBatchAsync("s", 10, default);
            next.IsEmpty.Should().BeTrue();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DequeueEmpty_ReturnsEmptyBatch()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("s", default);
            var batch = await buf.DequeueBatchAsync("s", 10, default);
            batch.IsEmpty.Should().BeTrue();
            batch.FirstSequence.Should().Be(-1);
            batch.LastSequence.Should().Be(-1);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DequeueBatch_UnregisteredSink_Throws()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            Func<Task> act = async () => await buf.DequeueBatchAsync("ghost", 1, default);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Ack_UnregisteredSink_Throws()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            Func<Task> act = async () => await buf.AckAsync("ghost", 0, default);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DequeueBatch_MaxCountZero_ReturnsEmpty()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 2), default);
            var batch = await buf.DequeueBatchAsync("s", 0, default);
            batch.IsEmpty.Should().BeTrue();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task GetStats_AfterDispose_Throws()
    {
        var path = NewFilePath();
        try
        {
            var buf = await NewBufferAsync(path: path);
            await buf.DisposeAsync();
            Func<Task> act = async () => await buf.GetStatsAsync();
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DequeueAfterDispose_Throws()
    {
        var path = NewFilePath();
        try
        {
            var buf = await NewBufferAsync(path: path);
            await buf.RegisterSinkAsync("s", default);
            await buf.DisposeAsync();
            Func<Task> act = async () => await buf.DequeueBatchAsync("s", 1, default);
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Sequences_AreMonotonic_ReachableViaDequeue()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(maxDepth: 4096, path: path);
            await buf.RegisterSinkAsync("s", default);

            // Enqueue 1000 points sequentially (concurrent inserts under the
            // single writer mutex would still serialize; the contract pin
            // is that sequences are unique and strictly increasing).
            for (int i = 0; i < 10; i++)
            {
                await buf.EnqueueAsync(Batch(i * 100, 100), default);
            }

            var batch = await buf.DequeueBatchAsync("s", 10_000, default);
            batch.Points.Should().HaveCount(1000);
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(999);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Stats_NewFieldsPopulated()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await NewBufferAsync(maxDepth: 4, drop: DropPolicy.DropOldest, path: path);
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 6), default); // forces 2 evictions

            var stats = await buf.GetStatsAsync();
            stats.TotalDropped.Should().Be(2);
            stats.DroppedByCapacity.Should().Be(2);
            stats.DroppedByRetention.Should().Be(0);
            // C2b also exposes oldest unacked sink id.
            stats.OldestUnackedSinkId.Should().Be("s");
        }
        finally
        {
            TryDelete(path);
        }
    }
}
