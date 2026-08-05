// ============================================================================
// File: Buffer/SqliteBufferDurabilityTests.cs
// Purpose: Prove that data, cursors, and sequence assignment all survive
//          process restart (modeled by DisposeAsync + OpenAsync against the
//          same file).
// ============================================================================

using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferDurabilityTests
{
    [Fact]
    public async Task EnqueuedData_SurvivesCloseReopen()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(Batch(0, 5), default);
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var batch = await reopened.DequeueBatchAsync("s", 10, default);
            batch.Points.Should().HaveCount(5);
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(4);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Cursor_SurvivesCloseReopen()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(Batch(0, 6), default);
                var batch = await buf.DequeueBatchAsync("s", 3, default);
                await buf.AckAsync("s", batch.LastSequence, default);
                // Cursor should now be at sequence 3.
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            // After reopen, the reclaim pass that runs at OpenAsync should
            // have deleted seqs 0..2 (only one cursor, ack'd up to 2).
            var stats = await reopened.GetStatsAsync();
            stats.CurrentDepth.Should().Be(3);

            var batch2 = await reopened.DequeueBatchAsync("s", 10, default);
            batch2.FirstSequence.Should().Be(3);
            batch2.LastSequence.Should().Be(5);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SequenceAssignment_ContinuesAcrossReopen()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(Batch(0, 4), default);
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            // Without acks, the previous run's data is still present.
            // New enqueue must continue past the existing max sequence (3).
            await reopened.EnqueueAsync(Batch(99, 2), default); // contents have seq 99, 100
                                                                 // but the BUFFER seqs are 4, 5.

            var batch = await reopened.DequeueBatchAsync("s", 10, default);
            batch.Points.Should().HaveCount(6);
            batch.FirstSequence.Should().Be(0);
            batch.LastSequence.Should().Be(5);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task NoAck_AllDataReplays_AfterReopen()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(Batch(0, 4), default);
                // Dequeue but DO NOT ack — simulates crash between dequeue
                // and ack.
                _ = await buf.DequeueBatchAsync("s", 10, default);
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var batch = await reopened.DequeueBatchAsync("s", 10, default);
            batch.Points.Should().HaveCount(4, "data must replay because ack never reached the disk");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DroppedByCapacity_DoesNotSurviveReopen_AsCounter()
    {
        // Counters are in-memory and reset on reopen. This is documented
        // behavior for the C2b stats: lifetime counters are per-process.
        // The CONTENT (whatever rows survived) does survive — that is the
        // durability promise.
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy(maxDepth: 4, drop: ElpisEdgeConnect.Core.Configuration.DropPolicy.DropOldest)))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(Batch(0, 6), default);
                var stats = await buf.GetStatsAsync();
                stats.DroppedByCapacity.Should().Be(2);
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy(maxDepth: 4));
            var reopenedStats = await reopened.GetStatsAsync();
            reopenedStats.DroppedByCapacity.Should().Be(0); // counters reset
            reopenedStats.CurrentDepth.Should().Be(4);       // content preserved
        }
        finally
        {
            TryDelete(path);
        }
    }
}
