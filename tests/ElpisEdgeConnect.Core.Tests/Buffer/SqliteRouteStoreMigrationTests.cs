// ============================================================================
// File: Buffer/SqliteRouteStoreMigrationTests.cs
// Covers: K1.2b-2a schema migration hardened per PR #182 review — the version is
//         classified BEFORE any DDL: a future or malformed version is rejected without
//         mutating the database (no tables created); a v2 file with a missing required
//         table fails closed rather than being silently recreated; a v1 file with a real
//         point + cursor upgrades in place and the point remains dequeueable. The
//         tracking-disabled enqueue path does not maintain next_sequence.
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

public sealed class SqliteRouteStoreMigrationTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task V1_Database_With_A_Real_Point_Upgrades_And_The_Point_Remains_Dequeueable()
    {
        var path = NewFilePath();
        try
        {
            // Build a real v2 file with one buffered (unacked) point + a cursor, then
            // downgrade it to a v1-shaped file (drop latest_value, remove v2 meta keys,
            // stamp version 1) so migration has genuine points/cursors to preserve.
            await using (var s = await SqliteRouteStore.OpenAsync("route-m", path, SmallSqlitePolicy()))
            {
                await s.RegisterSinkAsync("s", Ct);
                await s.EnqueueAsync(C2aTestFixtures.Batch(0, 1), Ct); // seq 0, retained
            }

            RawExec(path, "DROP TABLE latest_value;");
            RawExec(path, $"DELETE FROM meta WHERE key = '{SqliteBufferSchema.NextSequenceKey}';");
            RawExec(path, $"UPDATE meta SET value = '1' WHERE key = '{SqliteBufferSchema.SchemaVersionKey}';");

            // Reopen → v1→v2 migration.
            await using (var migrated = await SqliteRouteStore.OpenAsync("route-m", path, SmallSqlitePolicy()))
            {
                var batch = await migrated.DequeueBatchAsync("s", 10, Ct);
                batch.FirstSequence.Should().Be(0); // the real point survived and is dequeueable
                batch.Points.Should().HaveCount(1);
            }

            ReadMeta(path, SqliteBufferSchema.SchemaVersionKey).Should().Be("2");
            TableExists(path, "latest_value").Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Fresh_Store_Is_V2_With_LatestValue_Table_And_Seeded_NextSequence()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                buf.Head.Should().Be(0);
            }

            ReadMeta(path, SqliteBufferSchema.SchemaVersionKey).Should().Be("2");
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");
            TableExists(path, "latest_value").Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Disabled_Enqueue_Does_Not_Advance_NextSequence()
    {
        var path = NewFilePath();
        try
        {
            await using (var buf = await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy()))
            {
                await buf.EnqueueAsync(C2aTestFixtures.Batch(0, 3), Ct);
            }

            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Future_Schema_Version_Is_Rejected_Without_Creating_Tables()
    {
        var path = NewFilePath();
        try
        {
            CreateMetaOnlyDatabase(path, "99");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferSchemaMismatch);

            // No DDL ran — the data tables were not created.
            TableExists(path, "points").Should().BeFalse();
            TableExists(path, "latest_value").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Malformed_Schema_Version_Is_Rejected_Without_Creating_Tables()
    {
        var path = NewFilePath();
        try
        {
            CreateMetaOnlyDatabase(path, "not-a-number");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);

            TableExists(path, "points").Should().BeFalse();
            TableExists(path, "latest_value").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2_With_Missing_LatestValue_Fails_Closed_And_Does_Not_Recreate_It()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("r", path, SmallSqlitePolicy())) { }

            RawExec(path, "DROP TABLE latest_value;"); // simulate a lost manifest table on a v2 file

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);

            // It must NOT have been silently recreated (that would mask manifest loss).
            TableExists(path, "latest_value").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    // ---- round-3: a damaged v1 must fail closed, not be silently recreated. ----
    [Fact]
    public async Task V1_Missing_Points_Fails_Closed_And_Is_Not_Recreated()
    {
        var path = NewFilePath();
        try
        {
            RawExec(path, "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            RawExec(path, "CREATE TABLE cursors (sink_id TEXT PRIMARY KEY, next_unread INTEGER NOT NULL, updated_at INTEGER NOT NULL);");
            RawExec(path, "INSERT INTO meta (key, value) VALUES ('schema_version', '1');");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            TableExists(path, "points").Should().BeFalse();          // not recreated
            ReadMeta(path, SqliteBufferSchema.SchemaVersionKey).Should().Be("1"); // version untouched
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V1_Missing_Cursors_Fails_Closed_And_Is_Not_Recreated()
    {
        var path = NewFilePath();
        try
        {
            RawExec(path, "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);");
            RawExec(path, "CREATE TABLE points (sequence INTEGER PRIMARY KEY, payload BLOB NOT NULL, enqueued_at INTEGER NOT NULL, expires_at INTEGER);");
            RawExec(path, "INSERT INTO meta (key, value) VALUES ('schema_version', '1');");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            TableExists(path, "cursors").Should().BeFalse();
            ReadMeta(path, SqliteBufferSchema.SchemaVersionKey).Should().Be("1");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task No_Meta_With_An_Application_Table_Is_Not_Fresh_And_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            // A file with latest_value (or any app table) but no meta must NOT be treated
            // as a fresh route store.
            RawExec(path, SqliteBufferSchema.LatestValueTableDdl);

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            TableExists(path, "points").Should().BeFalse();
            TableExists(path, "cursors").Should().BeFalse();
            TableExists(path, "meta").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2_Missing_A_Required_Column_Fails_Closed_Without_Modifying_Schema()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("r", path, SmallSqlitePolicy())) { } // full v2

            // Replace points with a version missing the expires_at column (still v2-stamped).
            RawExec(path, "DROP TABLE points;");
            RawExec(path, "CREATE TABLE points (sequence INTEGER PRIMARY KEY, payload BLOB NOT NULL, enqueued_at INTEGER NOT NULL);");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            // The schema was not "repaired" — points still lacks expires_at.
            ColumnExists(path, "points", "expires_at").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    // ---- round-4: validation is constraint-aware (type / NOT NULL / primary key), not name-only. ----
    private const string ValidCursors = "CREATE TABLE cursors (sink_id TEXT PRIMARY KEY, next_unread INTEGER NOT NULL, updated_at INTEGER NOT NULL);";
    private const string ValidPoints = "CREATE TABLE points (sequence INTEGER PRIMARY KEY, payload BLOB NOT NULL, enqueued_at INTEGER NOT NULL, expires_at INTEGER);";
    private const string ValidMeta = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";

    [Fact]
    public async Task V1_Points_Sequence_Not_Primary_Key_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            RawExec(path, ValidMeta);
            RawExec(path, ValidCursors);
            RawExec(path, "CREATE TABLE points (sequence INTEGER, payload BLOB NOT NULL, enqueued_at INTEGER NOT NULL, expires_at INTEGER);"); // no PK
            RawExec(path, "INSERT INTO meta (key, value) VALUES ('schema_version', '1');");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            ReadMeta(path, SqliteBufferSchema.SchemaVersionKey).Should().Be("1"); // unchanged
            TableExists(path, "latest_value").Should().BeFalse();                 // no v2 DDL
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V1_Meta_Key_Not_Primary_Key_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            RawExec(path, "CREATE TABLE meta (key TEXT, value TEXT NOT NULL);"); // key not PK
            RawExec(path, ValidPoints);
            RawExec(path, ValidCursors);
            RawExec(path, "INSERT INTO meta (key, value) VALUES ('schema_version', '1');");

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            TableExists(path, "latest_value").Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2_LatestValue_Without_Composite_PK_Fails_Closed_Without_Modification()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("r", path, SmallSqlitePolicy())) { }

            RawExec(path, "DROP TABLE latest_value;");
            RawExec(path,
                "CREATE TABLE latest_value (source_instance_id TEXT NOT NULL, device_id TEXT NOT NULL, tag_path TEXT NOT NULL, " +
                "value_type INTEGER NOT NULL, route_buffer_sequence INTEGER NOT NULL, schema_generation INTEGER NOT NULL, " +
                "envelope BLOB NOT NULL, updated_at INTEGER NOT NULL);"); // NO primary key

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);

            ReadPk(path, "latest_value", "source_instance_id").Should().Be(0); // not repaired
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2_LatestValue_Wrong_PK_Order_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("r", path, SmallSqlitePolicy())) { }

            RawExec(path, "DROP TABLE latest_value;");
            RawExec(path,
                "CREATE TABLE latest_value (source_instance_id TEXT NOT NULL, device_id TEXT NOT NULL, tag_path TEXT NOT NULL, " +
                "value_type INTEGER NOT NULL, route_buffer_sequence INTEGER NOT NULL, schema_generation INTEGER NOT NULL, " +
                "envelope BLOB NOT NULL, updated_at INTEGER NOT NULL, " +
                "PRIMARY KEY (device_id, source_instance_id, tag_path));"); // wrong order

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2_Column_Losing_NotNull_Fails_Closed()
    {
        var path = NewFilePath();
        try
        {
            await using (var s = await SqliteRouteStore.OpenAsync("r", path, SmallSqlitePolicy())) { }

            RawExec(path, "DROP TABLE points;");
            RawExec(path, "CREATE TABLE points (sequence INTEGER PRIMARY KEY, payload BLOB, enqueued_at INTEGER NOT NULL, expires_at INTEGER);"); // payload lost NOT NULL

            var act = async () => await SqliteBuffer.OpenAsync("r", path, SmallSqlitePolicy());
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    // --- raw-SQLite fixture helpers ---

    private static SqliteConnection OpenRaw(string path, bool readOnly = false) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

    private static void CreateMetaOnlyDatabase(string path, string schemaVersion)
    {
        using var conn = OpenRaw(path);
        conn.Open();
        using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);";
            ddl.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES ('schema_version', $v);";
        cmd.Parameters.AddWithValue("$v", schemaVersion);
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

    private static string? ReadMeta(string path, string key)
    {
        using var conn = OpenRaw(path, readOnly: true);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private static bool TableExists(string path, string table)
    {
        using var conn = OpenRaw(path, readOnly: true);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $t;";
        cmd.Parameters.AddWithValue("$t", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static int ReadPk(string path, string table, string column)
    {
        using var conn = OpenRaw(path, readOnly: true);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            if (string.Equals(rdr.GetString(1), column, System.StringComparison.Ordinal))
            {
                return (int)rdr.GetInt64(5); // 5 = pk ordinal
            }
        }

        return -1;
    }

    private static bool ColumnExists(string path, string table, string column)
    {
        using var conn = OpenRaw(path, readOnly: true);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});"; // table is a test-controlled literal
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            if (string.Equals(rdr.GetString(1), column, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
