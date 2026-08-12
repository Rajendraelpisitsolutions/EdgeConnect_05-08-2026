// ============================================================================
// File: Buffer/SqliteRouteStoreActivationTests.cs
// Covers: K1.2b-2b — drained-store replay activation. Activation is refused while the
//         store holds backlog (retained points, or a lagging sink whose backlog is
//         therefore retained) with RouteStoreReplayActivationBacklogPending; on a fully
//         drained store it succeeds, registers the replay sink at the head, persists the
//         meta (next_sequence / route_id / generation 0 / enabled), and is one-way
//         (reopen stays enabled; re-activation is idempotent AlreadyEnabled). Once
//         enabled, the legacy EnqueueAsync path is rejected. Activation lives on the
//         internal SqliteRouteStore (reached here via InternalsVisibleTo).
//
// Note: the pure "points empty + cursor behind head" state is unreachable after open —
// LoadCursors clamps a sub-tail cursor to tail (== head when drained), and reclaim
// cannot drain past a lagging cursor — so a lagging sink always manifests as retained
// points. CountCursorsBehind remains as defense-in-depth.
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

public sealed class SqliteRouteStoreActivationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Activation_Rejected_When_Points_Are_Retained()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            await store.EnqueueAsync(C2aTestFixtures.Batch(0, 3), Ct); // retained (unconsumed)

            var act = async () => await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreReplayActivationBacklogPending);
            store.IsReplayStateTrackingEnabled.Should().BeFalse();
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Activation_Rejected_When_A_Sink_Cursor_Lags()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            await store.RegisterSinkAsync("s", Ct);
            await store.EnqueueAsync(C2aTestFixtures.Batch(0, 3), Ct);
            await store.AckAsync("s", 0, Ct); // cursor at 1; sequences 1..2 still pending → backlog

            var act = async () => await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreReplayActivationBacklogPending);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Activation_Succeeds_On_A_Fully_Drained_Store_And_Registers_Replay_Sink_At_Head()
    {
        var path = NewFilePath();
        try
        {
            // Enqueue 3, ack all, then reopen so the synchronous reclaim drains to head=3.
            await using (var s1 = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                await s1.RegisterSinkAsync("s", Ct);
                await s1.EnqueueAsync(C2aTestFixtures.Batch(0, 3), Ct);
                await s1.AckAsync("s", 2, Ct);
            }

            await using (var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                store.Head.Should().Be(3); // fully drained

                var result = await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
                result.Outcome.Should().Be(ReplayTrackingActivationOutcome.Activated);
                result.ActivationHead.Should().Be(3);
                store.IsReplayStateTrackingEnabled.Should().BeTrue();
            }

            // Persisted state: enabled, route id, generation 0, next_sequence, replay cursor at head.
            ReadMeta(path, SqliteBufferSchema.ReplayStateTrackingKey).Should().Be(SqliteBufferSchema.ReplayTrackingEnabled);
            ReadMeta(path, SqliteBufferSchema.RouteIdKey).Should().Be("route-a");
            ReadMeta(path, SqliteBufferSchema.SchemaGenerationKey).Should().Be("0");
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("3");
            ReadCursor(path, "sp").Should().Be(3);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Activation_Rejects_Mismatched_Route_Id()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            var act = async () => await store.ActivateReplayStateTrackingAsync("WRONG", "sp", Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreRouteMismatch);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Activated_Store_Reopened_Is_Still_Enabled()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
            }

            await using var reopened = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            reopened.IsReplayStateTrackingEnabled.Should().BeTrue(); // one-way: stays enabled
        }
        finally
        {
            TryDelete(path);
        }
    }

    // One-way disabled → enabled: there is no disable path, and re-activation is an
    // idempotent AlreadyEnabled (never flips back to disabled).
    [Fact]
    public async Task Reactivation_Is_Idempotent_And_Never_Disables()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());

            var first = await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
            first.Outcome.Should().Be(ReplayTrackingActivationOutcome.Activated);

            var second = await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
            second.Outcome.Should().Be(ReplayTrackingActivationOutcome.AlreadyEnabled);

            store.IsReplayStateTrackingEnabled.Should().BeTrue();
            ReadMeta(path, SqliteBufferSchema.ReplayStateTrackingKey).Should().Be(SqliteBufferSchema.ReplayTrackingEnabled);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // Re-activation must not silently accept a different replay sink id (K1.3 locks
    // replacement-sink semantics; for now a mismatch is a typed conflict).
    [Fact]
    public async Task Reactivation_With_A_Different_Replay_Sink_Is_Rejected()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);

            var act = async () => await store.ActivateReplayStateTrackingAsync("route-a", "different-sink", Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreReplaySinkMismatch);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Legacy_Enqueue_Is_Rejected_Once_Enabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);

            var act = async () => await store.EnqueueAsync(C2aTestFixtures.Batch(0, 1), Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreLegacyAppendOnEnabledStore);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- gap 3: the designated replay-sink cursor is protected on an enabled store. ----
    [Fact]
    public async Task Deregistering_The_Active_Replay_Sink_Is_Rejected()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);

            var act = async () => await store.DeregisterSinkAsync("sp", Ct);
            var ex = (await act.Should().ThrowAsync<BufferException>()).Which;
            ex.Error.Code.Should().Be(CoreErrors.RouteStoreReplaySinkProtected);
        }
        finally
        {
            TryDelete(path);
        }
    }

    // ---- gap 4: real mid-transaction rollback of activation via a SQLite abort trigger. ----
    [Fact]
    public async Task Activation_Rollback_Before_Commit_Leaves_Store_Disabled_And_Retry_Succeeds()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy())) { } // fresh v2

            // Abort the enabled-flag write (the last statement of the activation transaction).
            RawExec(path,
                "CREATE TRIGGER t_block_enable BEFORE INSERT ON meta WHEN NEW.key = 'replay_state_tracking' " +
                "BEGIN SELECT RAISE(ABORT, 'injected'); END;");

            await using (var s = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                var act = async () => await s.ActivateReplayStateTrackingAsync("route-a", "sp", Ct);
                await act.Should().ThrowAsync<BufferException>();
                s.IsReplayStateTrackingEnabled.Should().BeFalse(); // in-memory not flipped
            }

            // The whole transaction rolled back: no enabled flag, no replay cursor.
            ReadMeta(path, SqliteBufferSchema.ReplayStateTrackingKey).Should().BeNull();
            ReadCursor(path, "sp").Should().BeNull();

            RawExec(path, "DROP TRIGGER t_block_enable;");

            await using (var s = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                await s.ActivateReplayStateTrackingAsync("route-a", "sp", Ct); // retry succeeds
                s.IsReplayStateTrackingEnabled.Should().BeTrue();
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    // --- raw-SQLite inspection helpers ---

    private static string ConnString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();

    private static void RawExec(string path, string sql)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string? ReadMeta(string path, string key)
    {
        using var conn = new SqliteConnection(ConnString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static long? ReadCursor(string path, string sinkId)
    {
        using var conn = new SqliteConnection(ConnString(path));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT next_unread FROM cursors WHERE sink_id = $s;";
        cmd.Parameters.AddWithValue("$s", sinkId);
        var raw = cmd.ExecuteScalar();
        return raw is null ? null : long.Parse(raw.ToString()!, CultureInfo.InvariantCulture);
    }
}
