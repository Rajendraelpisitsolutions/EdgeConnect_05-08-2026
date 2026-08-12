// ============================================================================
// File: Buffer/SqliteRouteStoreGenerationTests.cs
// Covers: K1.2b generation CAS/fencing — AdvanceGenerationAsync requires tracking
//         enabled, next == expectedCurrent + 1 (checked, overflow fails closed),
//         expectedCurrent == the persisted current generation (stale rejection), and
//         (drain fence) the PERSISTED replay sink's cursor at the head. The fence is
//         bound to the persisted replay sink (no caller-supplied sink), so a caught-up
//         ordinary sink cannot advance the generation. A failed transition leaves the
//         generation unchanged (incl. a real mid-transaction rollback via a SQLite abort
//         trigger); the committed generation survives restart; malformed generation
//         metadata fails closed on open.
//
// K1.2c re-enables the two fence cases the tracked AppendAsync makes reachable (an
// append advances next_sequence ahead of the not-yet-consumed replay cursor):
// "cursor behind head → GenerationBacklogPending" and "cursor beyond head → corruption".
// ============================================================================

using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreGenerationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<SqliteRouteStore> OpenActivatedAsync(string path)
    {
        var store = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy());
        await store.ActivateReplayStateTrackingAsync("route-g", "sp", Ct); // fresh store: head 0, gen 0
        return store;
    }

    [Fact]
    public async Task Advance_Rejected_When_Tracking_Disabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy());
            var act = async () => await store.AdvanceGenerationAsync(0, 1, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_Succeeds_When_Replay_Sink_Caught_Up()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AdvanceGenerationAsync(0, 1, Ct);
            store.CurrentSchemaGeneration.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_Rejected_When_Replay_Sink_Behind_Head_After_Append()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            // Append moves next_sequence to 3; the replay sink "sp" cursor stays at 0.
            await store.AppendAsync(C2aTestFixtures.Batch(0, 3), 0, Ct);

            var act = async () => await store.AdvanceGenerationAsync(0, 1, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreGenerationBacklogPending);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_Corruption_When_Replay_Sink_Cursor_Beyond_Head()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(C2aTestFixtures.Batch(0, 3), 0, Ct); // next_sequence = 3
            RawSetCursor(path, "sp", 5); // force the replay cursor beyond the head (corruption)

            var act = async () => await store.AdvanceGenerationAsync(0, 1, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCursorInconsistent);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_Rejected_When_Expected_Generation_Stale()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            var act = async () => await store.AdvanceGenerationAsync(5, 6, Ct); // store is at 0
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreStaleGeneration);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_Rejected_When_Next_Not_Current_Plus_One()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            var act = async () => await store.AdvanceGenerationAsync(0, 5, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreGenerationInvalid);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Advance_At_Long_MaxValue_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path)) { }
            RawSetMeta(path, SqliteBufferSchema.SchemaGenerationKey, long.MaxValue.ToString(CultureInfo.InvariantCulture));

            await using var reopened = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy());
            reopened.CurrentSchemaGeneration.Should().Be(long.MaxValue);

            var act = async () => await reopened.AdvanceGenerationAsync(long.MaxValue, long.MaxValue, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreGenerationInvalid); // overflow
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Failed_Advance_Leaves_Generation_Unchanged_And_Retry_Succeeds()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var bad = async () => await store.AdvanceGenerationAsync(5, 6, Ct); // stale expected → fails
            await bad.Should().ThrowAsync<BufferException>();
            store.CurrentSchemaGeneration.Should().Be(0);

            await store.AdvanceGenerationAsync(0, 1, Ct); // valid retry succeeds
            store.CurrentSchemaGeneration.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    // ---- gap 4: real mid-transaction rollback via a SQLite abort trigger. ----
    [Fact]
    public async Task Advance_Rollback_Before_Commit_Leaves_Generation_Unchanged_And_Retry_Succeeds()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path)) { } // gen 0 committed

            // Abort the generation write mid-transaction.
            RawExec(path,
                "CREATE TRIGGER t_block_gen BEFORE UPDATE ON meta WHEN NEW.key = 'current_schema_generation' " +
                "BEGIN SELECT RAISE(ABORT, 'injected'); END;");

            await using (var store = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy()))
            {
                var act = async () => await store.AdvanceGenerationAsync(0, 1, Ct);
                await act.Should().ThrowAsync<BufferException>();
                store.CurrentSchemaGeneration.Should().Be(0); // in-memory unchanged
            }

            ReadMeta(path, SqliteBufferSchema.SchemaGenerationKey).Should().Be("0"); // persisted unchanged

            RawExec(path, "DROP TRIGGER t_block_gen;");

            await using (var store = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy()))
            {
                store.CurrentSchemaGeneration.Should().Be(0);
                await store.AdvanceGenerationAsync(0, 1, Ct); // retry succeeds
                store.CurrentSchemaGeneration.Should().Be(1);
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Committed_Generation_Survives_Restart()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path))
            {
                await store.AdvanceGenerationAsync(0, 1, Ct);
                await store.AdvanceGenerationAsync(1, 2, Ct);
            }

            await using var reopened = await SqliteRouteStore.OpenAsync("route-g", path, SmallSqlitePolicy());
            reopened.CurrentSchemaGeneration.Should().Be(2);
            reopened.IsReplayStateTrackingEnabled.Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Malformed_Generation_Metadata_Fails_Closed_On_Open()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path)) { }
            RawSetMeta(path, SqliteBufferSchema.SchemaGenerationKey, "not-a-number");

            var act = async () => await SqliteBuffer.OpenAsync("route-g", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    private static SqliteConnection OpenRaw(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());

    private static void RawSetMeta(string path, string key, string value)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void RawSetCursor(string path, string sinkId, long value)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE cursors SET next_unread = $v, updated_at = 0 WHERE sink_id = $s;";
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$s", sinkId);
        cmd.ExecuteNonQuery();
    }

    private static string? ReadMeta(string path, string key)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void RawExec(string path, string sql)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
