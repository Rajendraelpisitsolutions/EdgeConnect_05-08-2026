// ============================================================================
// File: Buffer/SqliteRouteStoreAppendTests.cs
// Covers: K1.2c atomic tracked AppendAsync on an enabled store — advances the
//         authoritative next_sequence and head, returns the assigned range, stamps and
//         upserts the latest_value manifest (keeping the greater route_buffer_sequence),
//         writes a manifest envelope the codec decodes back with the column cross-checks,
//         handles a known-null value, and fails closed (leaving the head untouched) on a
//         stale generation, tracking-disabled, or an unrepresentable point. All within
//         ONE transaction (points + manifest + next_sequence).
// Reference: plan v3 §10 (append path) / §6 (M2 AssignedSequenceRange); K1.2c handoff §3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreAppendTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<SqliteRouteStore> OpenActivatedAsync(string path)
    {
        var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
        await store.ActivateReplayStateTrackingAsync("route-a", "sp", Ct); // fresh store: head 0, gen 0
        return store;
    }

    private static CanonicalDataPoint Point(
        long seq,
        object? value,
        CanonicalValueType type,
        string tagPath = "Spindle/Speed",
        string device = "dev-1") =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice(device)
            .WithTag("tag", tagPath)
            .WithValue(value, type)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    // ---- next_sequence / head / range --------------------------------------

    [Fact]
    public async Task Append_Advances_NextSequence_And_Head()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(C2aTestFixtures.Batch(0, 4), 0, Ct);

            store.Head.Should().Be(4);
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("4");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Returns_Contiguous_Assigned_Range()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var first = await store.AppendAsync(C2aTestFixtures.Batch(0, 3), 0, Ct);
            first.FirstSequence.Should().Be(0);
            first.LastSequence.Should().Be(2);
            first.Count.Should().Be(3);

            var second = await store.AppendAsync(C2aTestFixtures.Batch(0, 2), 0, Ct);
            second.FirstSequence.Should().Be(3);
            second.LastSequence.Should().Be(4);
            second.Count.Should().Be(2);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Empty_Batch_Is_NoOp()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var range = await store.AppendAsync(Array.Empty<CanonicalDataPoint>(), 0, Ct);

            range.Count.Should().Be(0);
            store.Head.Should().Be(0);
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_NextSequence_Survives_Restart()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path))
            {
                await store.AppendAsync(C2aTestFixtures.Batch(0, 5), 0, Ct);
            }

            await using var reopened = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            reopened.Head.Should().Be(5);
            reopened.IsReplayStateTrackingEnabled.Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    // ---- manifest content ---------------------------------------------------

    [Fact]
    public async Task Append_Writes_Manifest_Row_The_Codec_Decodes()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(new[] { Point(0, 1234.5, CanonicalValueType.Double) }, 0, Ct);

            var row = ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed");
            row.Should().NotBeNull();
            row!.Value.ValueType.Should().Be((int)CanonicalValueType.Double);
            row.Value.RouteBufferSequence.Should().Be(0);
            row.Value.SchemaGeneration.Should().Be(0);

            var decoded = LatestValueEnvelopeV1.Decode(
                row.Value.Envelope,
                CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"),
                (CanonicalValueType)row.Value.ValueType,
                row.Value.RouteBufferSequence);

            decoded.Value.Should().Be(1234.5);
            decoded.IsNull.Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Manifest_Upsert_Keeps_Greater_Sequence()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            // Two appends to the SAME metric; the later (higher-sequence) value must win.
            await store.AppendAsync(new[] { Point(0, 10.0, CanonicalValueType.Double) }, 0, Ct);
            await store.AppendAsync(new[] { Point(1, 20.0, CanonicalValueType.Double) }, 0, Ct);

            var row = ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed")!.Value;
            row.RouteBufferSequence.Should().Be(1);

            var decoded = LatestValueEnvelopeV1.Decode(
                row.Envelope, CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"),
                (CanonicalValueType)row.ValueType, row.RouteBufferSequence);
            decoded.Value.Should().Be(20.0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Same_Metric_Twice_In_One_Batch_Keeps_Last()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double),
                Point(1, 2.0, CanonicalValueType.Double), // same metric, later in the batch → wins
            }, 0, Ct);

            var row = ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed")!.Value;
            row.RouteBufferSequence.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Distinct_Devices_Same_TagPath_Are_Separate_Rows()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double, device: "dev-1"),
                Point(1, 2.0, CanonicalValueType.Double, device: "dev-2"),
            }, 0, Ct);

            ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed").Should().NotBeNull();
            ReadManifestRow(path, "src-1", "dev-2", "Spindle/Speed").Should().NotBeNull();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Null_Value_With_Real_Type_Is_Persisted()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(new[] { Point(0, null, CanonicalValueType.Double) }, 0, Ct);

            var row = ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed")!.Value;
            var decoded = LatestValueEnvelopeV1.Decode(
                row.Envelope, CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"),
                (CanonicalValueType)row.ValueType, row.RouteBufferSequence);
            decoded.IsNull.Should().BeTrue();
            decoded.ValueType.Should().Be(CanonicalValueType.Double);
        }
        finally { TryDelete(path); }
    }

    // ---- acquisition-timestamp UTC policy (PR #183 review blocker) ----------

    private static CanonicalDataPoint PointWithDeviceTimestamp(DateTime deviceTs) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag("tag", "Spindle/Speed")
            .WithValue(1.0, CanonicalValueType.Double)
            .WithQuality(DataQuality.Good)
            .WithTimestamps(deviceTs, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(0)
            .Build();

    [Fact]
    public async Task Append_Utc_Timestamp_Is_Preserved_As_Utc_Instant()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            var deviceUtc = new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc);

            await store.AppendAsync(new[] { PointWithDeviceTimestamp(deviceUtc) }, 0, Ct);

            var row = ReadManifestRow(path, "src-1", "dev-1", "Spindle/Speed")!.Value;
            var decoded = LatestValueEnvelopeV1.Decode(
                row.Envelope, CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"),
                (CanonicalValueType)row.ValueType, row.RouteBufferSequence);

            decoded.TimestampUtc.Should().Be(new DateTimeOffset(deviceUtc));
            decoded.TimestampUtc.Offset.Should().Be(TimeSpan.Zero); // decoded value is UTC
        }
        finally { TryDelete(path); }
    }

    // The canonical model contract is UTC-only and the buffer layer's locked policy is to
    // fail loud on non-UTC rather than silently shift the instant (BinaryWriterFormat R2 /
    // MessagePackFormat). So a Local/Unspecified DeviceTimestamp fails the append closed —
    // it is NOT relabelled (which would corrupt the instant) nor silently converted.
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task Append_NonUtc_Timestamp_Fails_Closed_No_Mutation(DateTimeKind kind)
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            var deviceTs = new DateTime(2026, 7, 14, 10, 0, 0, kind);

            var act = async () => await store.AppendAsync(new[] { PointWithDeviceTimestamp(deviceTs) }, 0, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);

            store.Head.Should().Be(0);
            CountPoints(path).Should().Be(0);
            CountManifest(path).Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Mixed_Batch_With_One_NonUtc_Timestamp_Rejects_Whole_Batch()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            // A valid UTC point followed by a Local-timestamp point: preparation is
            // all-or-nothing, so the whole batch is rejected with no partial mutation.
            var batch = new[]
            {
                PointWithDeviceTimestamp(new DateTime(2026, 7, 14, 9, 0, 0, DateTimeKind.Utc)),
                PointWithDeviceTimestamp(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Local)),
            };

            var act = async () => await store.AppendAsync(batch, 0, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);

            store.Head.Should().Be(0);
            CountPoints(path).Should().Be(0);
            CountManifest(path).Should().Be(0);
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");
        }
        finally { TryDelete(path); }
    }

    // ---- fail-closed --------------------------------------------------------

    [Fact]
    public async Task Append_Rejected_When_Tracking_Disabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());

            var act = async () => await store.AppendAsync(C2aTestFixtures.Batch(0, 1), 0, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Rejected_When_Generation_Stale()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.AppendAsync(C2aTestFixtures.Batch(0, 1), 5, Ct); // store is at gen 0
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreStaleGeneration);

            store.Head.Should().Be(0); // no partial mutation
        }
        finally { TryDelete(path); }
    }

    [Theory]
    [InlineData(CanonicalValueType.Null)]
    [InlineData(CanonicalValueType.Array)]
    [InlineData(CanonicalValueType.Object)]
    public async Task Append_Rejected_For_Unrepresentable_ValueType_No_Partial_Mutation(CanonicalValueType type)
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            // Batch: one good point then an unrepresentable one — the whole append must fail
            // closed with NO row inserted (preparation happens before the transaction).
            object? value = type == CanonicalValueType.Null ? null : new object();
            var batch = new[]
            {
                Point(0, 1.0, CanonicalValueType.Double),
                Point(1, value, type),
            };

            var act = async () => await store.AppendAsync(batch, 0, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);

            store.Head.Should().Be(0);
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");
            CountPoints(path).Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Append_Rolls_Back_All_Three_Writes_On_MidTransaction_Failure()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path)) { } // activate: head 0, gen 0, next_sequence 0

            // Abort the manifest upsert mid-transaction — AFTER the points insert has already
            // run inside the same transaction — to prove the whole batch rolls back atomically.
            RawExec(path,
                "CREATE TRIGGER t_block_manifest BEFORE INSERT ON latest_value " +
                "BEGIN SELECT RAISE(ABORT, 'injected'); END;");

            await using (var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy()))
            {
                var act = async () => await store.AppendAsync(C2aTestFixtures.Batch(0, 2), 0, Ct);
                await act.Should().ThrowAsync<BufferException>();
                store.Head.Should().Be(0); // in-memory head unchanged (only advanced after commit)
            }

            // The points insert, the manifest upsert, and the next_sequence bump all reverted.
            CountPoints(path).Should().Be(0);
            CountManifest(path).Should().Be(0);
            ReadMeta(path, SqliteBufferSchema.NextSequenceKey).Should().Be("0");

            RawExec(path, "DROP TRIGGER t_block_manifest;");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task LegacyEnqueue_Still_Rejected_On_Enabled_Store()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.EnqueueAsync(C2aTestFixtures.Batch(0, 1), Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreLegacyAppendOnEnabledStore);
        }
        finally { TryDelete(path); }
    }

    // ---- raw readers --------------------------------------------------------

    private readonly record struct ManifestRow(
        int ValueType, long RouteBufferSequence, long SchemaGeneration, byte[] Envelope);

    private static ManifestRow? ReadManifestRow(string path, string src, string dev, string tag)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT value_type, route_buffer_sequence, schema_generation, envelope FROM latest_value " +
            "WHERE source_instance_id = $s AND device_id = $d AND tag_path = $t;";
        cmd.Parameters.AddWithValue("$s", src);
        cmd.Parameters.AddWithValue("$d", dev);
        cmd.Parameters.AddWithValue("$t", tag);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        return new ManifestRow((int)r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), (byte[])r["envelope"]);
    }

    private static long CountPoints(string path) => CountRows(path, "points");

    private static long CountManifest(string path) => CountRows(path, "latest_value");

    private static long CountRows(string path, string table)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)cmd.ExecuteScalar()!;
    }

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
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }
}
