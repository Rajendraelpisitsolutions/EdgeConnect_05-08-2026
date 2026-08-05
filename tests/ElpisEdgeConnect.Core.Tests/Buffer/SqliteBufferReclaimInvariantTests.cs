// ============================================================================
// File: Buffer/SqliteBufferReclaimInvariantTests.cs
// Purpose: Pin the formal reclaim invariant from buffer-contract.md §4.4
//          across every observable state transition: enqueue, ack, register,
//          deregister, reclaim (post-ack), and DropOldest fast-forward.
//
// Reclaim invariant:
//   For every active sink s, after any committed mutation, s.next_unread
//   refers either to an existing held sequence in the buffer, or exactly
//   to _head.
// ============================================================================

using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferReclaimInvariantTests
{
    /// <summary>
    /// Read the durable cursors directly from SQLite and assert each
    /// satisfies the invariant against the buffer's in-process head/tail.
    /// We deliberately mix sources: in-memory head/tail for the bounds and
    /// the on-disk cursor table for the values, so any divergence between
    /// the in-memory tracker and the durable table is also visible.
    /// </summary>
    private static void AssertInvariant(SqliteBuffer buf)
    {
        var head = buf.Head;
        var tail = buf.Tail;

        using var conn = new SqliteConnection($"Data Source={buf.FilePath};Mode=ReadOnly");
        conn.Open();
        using var cur = conn.CreateCommand();
        cur.CommandText = "SELECT sink_id, next_unread FROM cursors;";
        using var crdr = cur.ExecuteReader();
        while (crdr.Read())
        {
            var sinkId = crdr.GetString(0);
            var c = crdr.GetInt64(1);

            // Cursor must satisfy tail <= c <= head. (== head means caught up.)
            c.Should().BeGreaterOrEqualTo(tail,
                $"sink '{sinkId}' cursor {c} below tail {tail}");
            c.Should().BeLessOrEqualTo(head,
                $"sink '{sinkId}' cursor {c} above head {head}");

            // If c < head, the row at sequence c MUST exist in points.
            // (If c == head the sink is caught up and no row is required.)
            if (c < head)
            {
                using var existsCmd = conn.CreateCommand();
                existsCmd.CommandText = "SELECT 1 FROM points WHERE sequence = $s;";
                existsCmd.Parameters.AddWithValue("$s", c);
                var exists = existsCmd.ExecuteScalar();
                exists.Should().NotBeNull(
                    $"sink '{sinkId}' cursor {c} must point at an existing row in [tail={tail}, head={head})");
            }
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterEnqueue()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await buf.RegisterSinkAsync("a", default);
            await buf.RegisterSinkAsync("b", default);

            await buf.EnqueueAsync(Batch(0, 1), default);
            AssertInvariant(buf);

            await buf.EnqueueAsync(Batch(1, 5), default);
            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterAck()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 4), default);

            var batch = await buf.DequeueBatchAsync("s", 10, default);
            await buf.AckAsync("s", batch.LastSequence, default);
            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterRegister()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await buf.RegisterSinkAsync("first", default);
            AssertInvariant(buf);

            await buf.EnqueueAsync(Batch(0, 3), default);
            AssertInvariant(buf);

            // Register a second sink AFTER data is in the buffer.
            await buf.RegisterSinkAsync("late", default);
            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterDeregister()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            await buf.RegisterSinkAsync("a", default);
            await buf.RegisterSinkAsync("b", default);
            await buf.EnqueueAsync(Batch(0, 4), default);

            await buf.DeregisterSinkAsync("b", default);
            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterDropOldestEviction()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxDepth: 4, drop: DropPolicy.DropOldest));
            await buf.RegisterSinkAsync("slow", default);
            await buf.EnqueueAsync(Batch(0, 6), default); // forces 2 evictions
            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Invariant_HoldsAfterRetentionSweep()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxAge: System.TimeSpan.FromMilliseconds(50)));
            await buf.RegisterSinkAsync("slow", default);
            await buf.EnqueueAsync(Batch(0, 4), default);

            // Wait for retention sweep to run and delete everything.
            var deadline = System.DateTime.UtcNow + System.TimeSpan.FromSeconds(3);
            while (System.DateTime.UtcNow < deadline)
            {
                var s = await buf.GetStatsAsync();
                if (s.DroppedByRetention >= 4)
                {
                    break;
                }
                await Task.Delay(20);
            }

            AssertInvariant(buf);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
