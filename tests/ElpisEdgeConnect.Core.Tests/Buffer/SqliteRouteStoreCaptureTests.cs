// ============================================================================
// File: Buffer/SqliteRouteStoreCaptureTests.cs
// Covers: K1.2d R12 step 1 — the shared raw replay-state capture on an enabled store.
//         One deferred read transaction under the writer mutex: cutoff (next_sequence)
//         read FIRST to pin the snapshot, then generation, sink cursor, and the
//         current-generation latest_value manifest deep-copied into RawManifestRow.
//         Asserts coherent capture (cutoff/generation/cursor/manifest, stale-generation
//         rows excluded, envelope decodes via the codec), the §7 error families
//         (disabled store, malformed meta, SQLite failure, cancellation), and the
//         constructor-injected test-hook mechanics (critical-section hook fires once,
//         Boundary→ManifestScan query order, hook exceptions escape unchanged).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R2/§R7/§R12
//            step 1; K1.2d kickoff handoff §4/§5/§7.
// ============================================================================

using System;
using System.Linq;
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

public sealed class SqliteRouteStoreCaptureTests
{
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteRouteStore> OpenActivatedAsync(
        string path, SqliteRouteStoreTestHooks? hooks = null)
    {
        var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy(), testHooks: hooks);
        await store.ActivateReplayStateTrackingAsync("route-a", ReplaySink, Ct); // fresh store: head 0, gen 0
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
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    // ---- coherent raw capture ----------------------------------------------

    [Fact]
    public async Task Capture_On_Fresh_Activated_Store_Is_Empty_At_Zero()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);

            raw.CutoffExclusive.Should().Be(0);
            raw.Generation.Should().Be(0);
            raw.Cursor.Should().Be(0);
            raw.Manifest.Should().BeEmpty();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_After_Appends_Reports_Cutoff_Generation_And_Manifest()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double, device: "dev-1"),
                Point(1, 2.0, CanonicalValueType.Double, device: "dev-2"),
                Point(2, 3.0, CanonicalValueType.Double, device: "dev-3"),
            }, 0, Ct);

            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);

            raw.CutoffExclusive.Should().Be(3);          // == next_sequence / head
            raw.Generation.Should().Be(0);
            raw.Cursor.Should().Be(0);                    // append does not move the sink cursor
            raw.Manifest.Should().HaveCount(3);
            raw.Manifest.Select(r => r.DeviceId).Should().BeEquivalentTo(new[] { "dev-1", "dev-2", "dev-3" });
            raw.Manifest.Should().OnlyContain(r => r.SchemaGeneration == 0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Cursor_Reflects_Acked_Sink_Position()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double),
                Point(1, 2.0, CanonicalValueType.Double),
                Point(2, 3.0, CanonicalValueType.Double),
            }, 0, Ct);

            await store.AckAsync(ReplaySink, 1, Ct); // consumed through seq 1 → cursor 2

            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);
            raw.Cursor.Should().Be(2);
            raw.CutoffExclusive.Should().Be(3);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Unregistered_Sink_Reports_Null_Cursor()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var raw = await store.CaptureRawStateAsync("not-registered", Ct);
            raw.Cursor.Should().BeNull();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Manifest_Excludes_Stale_Generation_Rows()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            // Generation 0: metric dev-1.
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double, device: "dev-1") }, 0, Ct);

            // Drain the replay sink to the head so the generation can advance, then advance.
            await store.AckAsync(ReplaySink, 0, Ct); // cursor → 1 == head
            await store.AdvanceGenerationAsync(0, 1, Ct);

            // Generation 1: a DIFFERENT metric (dev-2), so dev-1's row stays as a stale-gen orphan.
            await store.AppendAsync(new[] { Point(1, 2.0, CanonicalValueType.Double, device: "dev-2") }, 1, Ct);

            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);

            raw.Generation.Should().Be(1);
            raw.Manifest.Should().ContainSingle();
            raw.Manifest[0].DeviceId.Should().Be("dev-2");
            raw.Manifest[0].SchemaGeneration.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Manifest_Envelope_Is_Owned_And_Decodes_Via_Codec()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1234.5, CanonicalValueType.Double) }, 0, Ct);

            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);
            var row = raw.Manifest.Should().ContainSingle().Subject;

            var decoded = LatestValueEnvelopeV1.Decode(
                row.Envelope,
                CanonicalMetricKey.Create(row.SourceInstanceId, row.DeviceId, row.TagPath),
                (CanonicalValueType)row.ValueType,
                row.RouteBufferSequence);

            decoded.Value.Should().Be(1234.5);
            decoded.IsNull.Should().BeFalse();

            // The captured envelope is an owned copy: mutating it does not corrupt the store,
            // and a second capture still decodes cleanly.
            row.Envelope[0] ^= 0xFF;
            var raw2 = await store.CaptureRawStateAsync(ReplaySink, Ct);
            var row2 = raw2.Manifest.Should().ContainSingle().Subject;
            LatestValueEnvelopeV1.Decode(
                    row2.Envelope,
                    CanonicalMetricKey.Create(row2.SourceInstanceId, row2.DeviceId, row2.TagPath),
                    (CanonicalValueType)row2.ValueType,
                    row2.RouteBufferSequence)
                .Value.Should().Be(1234.5);
        }
        finally { TryDelete(path); }
    }

    // ---- §7 error families --------------------------------------------------

    [Fact]
    public async Task Capture_On_Disabled_Store_Fails_TrackingNotEnabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Malformed_NextSequence_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            SetMeta(path, SqliteBufferSchema.NextSequenceKey, "not-a-number");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Malformed_Generation_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            SetMeta(path, SqliteBufferSchema.SchemaGenerationKey, "-3");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_SqliteFailure_Translates_To_BufferIoError()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double) }, 0, Ct);

            // Drop the manifest table out from under the (idle) store: the manifest scan then
            // raises a raw SqliteException that must be translated to BUFFER_IO_ERROR (§7).
            RawExec(path, "DROP TABLE latest_value;");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferIoError);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Already_Canceled_Token_Throws_And_Store_Stays_Usable()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var act = async () => await store.CaptureRawStateAsync(ReplaySink, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            // No durable mutation; capture on a live token still succeeds.
            var raw = await store.CaptureRawStateAsync(ReplaySink, Ct);
            raw.CutoffExclusive.Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Capture_Null_Or_Empty_SinkId_Throws_ArgumentException(string? sinkId)
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureRawStateAsync(sinkId!, Ct);
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally { TryDelete(path); }
    }

    // ---- corrupt storage classes fail closed (no CLR-cast leak) -------------

    [Fact]
    public async Task Capture_Corrupt_ValueType_NonInteger_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double) }, 0, Ct);
            RawExec(path, "UPDATE latest_value SET value_type = 'not-an-integer';");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Corrupt_ValueType_OutOfInt32Range_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double) }, 0, Ct);

            // 2^32 + a valid discriminator: an UNCHECKED (int) narrow would wrap to a valid
            // CanonicalValueType and pass Enum.IsDefined (a fail-open). The checked narrow rejects it.
            RawExec(path, $"UPDATE latest_value SET value_type = {4294967296L + (long)CanonicalValueType.Double};");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Corrupt_Envelope_NonBlob_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double) }, 0, Ct);
            RawExec(path, "UPDATE latest_value SET envelope = 'not-a-blob';");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Corrupt_Cursor_NonInteger_Fails_BufferCorrupt()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            RawExec(path, "UPDATE cursors SET next_unread = 'not-an-integer' WHERE sink_id = 'sp';");

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCorrupt);
        }
        finally { TryDelete(path); }
    }

    // ---- constructor-injected test hooks ------------------------------------

    [Fact]
    public async Task Capture_Invokes_CriticalSection_Hook_Once()
    {
        var path = NewFilePath();
        try
        {
            var entered = 0;
            var hooks = new SqliteRouteStoreTestHooks(
                CaptureEnteredCriticalSection: () => Interlocked.Increment(ref entered));
            await using var store = await OpenActivatedAsync(path, hooks);

            await store.CaptureRawStateAsync(ReplaySink, Ct);

            entered.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_Emits_Boundary_Then_ManifestScan()
    {
        var path = NewFilePath();
        try
        {
            var kinds = new System.Collections.Generic.List<CaptureQueryKind>();
            var hooks = new SqliteRouteStoreTestHooks(
                QueryExecuting: k => { lock (kinds) { kinds.Add(k); } });
            await using var store = await OpenActivatedAsync(path, hooks);

            await store.CaptureRawStateAsync(ReplaySink, Ct);

            kinds.Should().Equal(CaptureQueryKind.Boundary, CaptureQueryKind.ManifestScan);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_CriticalSection_Hook_Exception_Escapes_Unchanged()
    {
        var path = NewFilePath();
        try
        {
            var hooks = new SqliteRouteStoreTestHooks(
                CaptureEnteredCriticalSection: () => throw new InvalidOperationException("boom"));
            await using var store = await OpenActivatedAsync(path, hooks);

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("boom");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Capture_QueryExecuting_Hook_Exception_Escapes_Unchanged()
    {
        var path = NewFilePath();
        try
        {
            var hooks = new SqliteRouteStoreTestHooks(
                QueryExecuting: _ => throw new InvalidOperationException("query-boom"));
            await using var store = await OpenActivatedAsync(path, hooks);

            var act = async () => await store.CaptureRawStateAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("query-boom");
        }
        finally { TryDelete(path); }
    }

    // ---- raw helpers --------------------------------------------------------

    private static void SetMeta(string path, string key, string value)
    {
        RawExec(path,
            "INSERT INTO meta (key, value) VALUES ('" + key + "', '" + value + "') " +
            "ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
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
}
