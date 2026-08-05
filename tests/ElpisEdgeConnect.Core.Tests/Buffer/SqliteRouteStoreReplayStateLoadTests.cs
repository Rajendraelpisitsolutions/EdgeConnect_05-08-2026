// ============================================================================
// File: Buffer/SqliteRouteStoreReplayStateLoadTests.cs
// Covers: K1.2b (PR #182 review gap 2) — the strict replay-state loader. An enabled
//         store must have complete, consistent metadata; any unknown tracking flag or
//         missing/inconsistent enabled-state metadata fails closed on open (nothing is
//         seeded or defaulted). A reopen under a different route id is rejected.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreReplayStateLoadTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task ActivateAndDisposeAsync(string path)
    {
        await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
        await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct); // fresh: head 0, gen 0
    }

    private static async Task<BufferException> OpenExpectingFailureAsync(string path, string routeId = "route-a")
    {
        var act = async () => await SqliteRouteStore.OpenAsync(routeId, path, SmallSqlitePolicy());
        return (await act.Should().ThrowAsync<BufferException>()).Which;
    }

    [Fact]
    public async Task Reopen_Under_A_Different_Route_Id_Is_Rejected()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            var ex = await OpenExpectingFailureAsync(path, routeId: "route-b");
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreRouteMismatch);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Unknown_Tracking_Flag_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawUpsertMeta(path, SqliteBufferSchema.ReplayStateTrackingKey, "garbage");
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Missing_RouteId_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawDeleteMeta(path, SqliteBufferSchema.RouteIdKey);
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Missing_ReplaySinkId_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawDeleteMeta(path, SqliteBufferSchema.ReplaySinkIdKey);
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Missing_ReplaySink_Cursor_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawExec(path, "DELETE FROM cursors WHERE sink_id = 'sp';");
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Missing_NextSequence_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawDeleteMeta(path, SqliteBufferSchema.NextSequenceKey);
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Malformed_NextSequence_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawUpsertMeta(path, SqliteBufferSchema.NextSequenceKey, "xyz");
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_NextSequence_Inconsistent_With_Head_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            // tail_sequence pulls the recovered head to 10, but next_sequence says 5.
            RawUpsertMeta(path, SqliteBufferSchema.TailSequenceKey, "10");
            RawUpsertMeta(path, SqliteBufferSchema.NextSequenceKey, "5");
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Enabled_Missing_Generation_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await ActivateAndDisposeAsync(path);
            RawDeleteMeta(path, SqliteBufferSchema.SchemaGenerationKey);
            (await OpenExpectingFailureAsync(path)).Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    // --- raw helpers ---

    private static SqliteConnection OpenRaw(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());

    private static void RawUpsertMeta(string path, string key, string value)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static void RawDeleteMeta(string path, string key)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
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
