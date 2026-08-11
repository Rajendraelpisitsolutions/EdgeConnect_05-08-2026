// ============================================================================
// File: Buffer/SqliteBufferRecoveryTests.cs
// Covers: empty file, schema creation, schema mismatch, integrity check,
//         cursor clamping for below-tail, cursor inconsistency above-head.
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferRecoveryTests
{
    [Fact]
    public async Task OpenEmptyFile_CreatesSchema_AndIsReady()
    {
        var path = NewFilePath();
        try
        {
            await using var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var stats = await buf.GetStatsAsync();
            stats.CurrentDepth.Should().Be(0);
            stats.RegisteredSinks.Should().Be(0);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task OpenPopulatedFile_RecoversHeadAndTail()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("s", default);
                await buf.EnqueueAsync(C2aTestFixtures.Batch(0, 7), default);
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var stats = await reopened.GetStatsAsync();
            stats.CurrentDepth.Should().Be(7);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SchemaVersionMismatch_Throws()
    {
        var path = NewFilePath();
        try
        {
            // First open: schema is created at version 1.
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy())) { }

            // Tamper: bump schema_version to 99 directly.
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE meta SET value = '99' WHERE key = 'schema_version';";
                cmd.ExecuteNonQuery();
            }

            // Second open: must throw schema-mismatch.
            Func<Task> act = async () =>
                await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>())
                .Where(ex => ex.Error.Code == CoreErrors.BufferSchemaMismatch);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task QuickCheckFailure_OnGarbledFile_Throws()
    {
        var path = NewFilePath();
        try
        {
            // Write a few bytes that look nothing like a SQLite header.
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

            Func<Task> act = async () =>
                await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>())
                .Where(ex => ex.Error.Code == CoreErrors.BufferCorrupt
                          || ex.Error.Code == CoreErrors.BufferIoError);
            // Either is acceptable: SQLite may report NOTADB (corrupt) or
            // refuse to even open the file (io error). The contract is
            // that we surface a typed BufferException — not silently open.
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task CursorBelowTail_OnReopen_IsClampedAndCounted()
    {
        var path = NewFilePath();
        try
        {
            // Set up a state where the cursor is at sequence 0 but the
            // points table has had its low rows deleted.
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("slow", default);
                await buf.EnqueueAsync(C2aTestFixtures.Batch(0, 5), default);
            }

            // Tamper: delete sequences 0..2 directly to simulate a previous
            // DropOldest run that the slow sink missed.
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM points WHERE sequence < 3;";
                cmd.ExecuteNonQuery();
            }

            await using var reopened = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            var batch = await reopened.DequeueBatchAsync("slow", 10, default);
            batch.FirstSequence.Should().Be(3, "the slow cursor must have been clamped to the new tail");
            var stats = await reopened.GetStatsAsync();
            stats.SinksFastForwarded.Should().BeGreaterOrEqualTo(1);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task FullDrainThenRestart_RecoversHeadFromCursors_AndDoesNotThrow()
    {
        // Bug 2 (P0) follow-up — surfaced during the M.2b.6.2 smoke
        // reproduction after the worker-fault observation fix landed:
        // the sink ack'd all enqueued points, the reclaim loop deleted
        // them, the points table went empty. On the next OpenAsync,
        // ReadHeadTail returns head=0 (no rows) but the cursor row still
        // records 2264. The previous behavior threw
        // BufferCursorInconsistent — bricking restart on every
        // shutdown-after-drain. Expected: recover head from the
        // persisted cursors so the buffer reopens cleanly and future
        // enqueues continue the monotonic sequence.
        var path = NewFilePath();
        try
        {
            const int totalPoints = 2264;  // mirror the user-observed cursor
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy(maxDepth: totalPoints + 64)))
            {
                await buf.RegisterSinkAsync("sink-1", default);
                await buf.EnqueueAsync(C2aTestFixtures.Batch(0, totalPoints), default);

                // Drain everything: dequeue all, ack to the head.
                var batch = await buf.DequeueBatchAsync("sink-1", totalPoints, default);
                batch.Points.Count.Should().Be(totalPoints);
                await buf.AckAsync("sink-1", batch.LastSequence, default);

                // Give the reclaim loop time to delete the acked rows.
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    var stats = await buf.GetStatsAsync();
                    if (stats.CurrentDepth == 0) break;
                    await Task.Delay(20);
                }
                (await buf.GetStatsAsync()).CurrentDepth.Should().Be(0,
                    "reclaim must finish before we close — the bug repros only when the points table is genuinely empty on the next open");
            }

            // Reopen the same file. Without the fix this throws
            // BufferCursorInconsistent because head computes to 0 from
            // the empty points table while the cursor still reads 2264.
            await using var reopened = await SqliteBuffer.OpenAsync(
                "r", path, SmallSqlitePolicy(maxDepth: totalPoints + 64));

            // Head must have been recovered from the cursors table; new
            // enqueues continue the monotonic sequence past the high
            // water mark, NOT restart at 0.
            await reopened.EnqueueAsync(C2aTestFixtures.Batch(0, 5), default);
            var next = await reopened.DequeueBatchAsync("sink-1", 10, default);
            next.Points.Count.Should().Be(5);
            next.FirstSequence.Should().Be(totalPoints,
                "sequences must continue from the persisted cursor's high-water mark, "
                + "not wrap back to 0 (would otherwise be observed by the sink as ghost data)");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task CursorAboveHead_OnReopen_Throws()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.RegisterSinkAsync("bogus", default);
                await buf.EnqueueAsync(C2aTestFixtures.Batch(0, 3), default);
            }

            // Tamper: forge a cursor above the max sequence.
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE cursors SET next_unread = 9999 WHERE sink_id = 'bogus';";
                cmd.ExecuteNonQuery();
            }

            Func<Task> act = async () =>
                await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>())
                .Where(ex => ex.Error.Code == CoreErrors.BufferCursorInconsistent);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SchemaTablesAndIndex_CreatedOnFirstOpen()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy())) { }

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type IN ('table','index') ORDER BY name;";
            using var rdr = cmd.ExecuteReader();
            var names = new System.Collections.Generic.List<string>();
            while (rdr.Read())
            {
                names.Add(rdr.GetString(0));
            }

            names.Should().Contain("points");
            names.Should().Contain("cursors");
            names.Should().Contain("meta");
            names.Should().Contain("idx_points_expires_at");
        }
        finally
        {
            TryDelete(path);
        }
    }
}
