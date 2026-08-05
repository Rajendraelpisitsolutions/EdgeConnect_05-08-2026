// ============================================================================
// File: Buffer/SqliteRouteStoreBoundaryProviderTests.cs
// Covers: K1.2d R12 step 2 — IReplayBoundaryProvider.CaptureReplayBoundaryAsync on an
//         enabled store. The boundary is captured from the boundary-only reads (sink
//         cursor + append cutoff) inside one snapshot transaction, WITHOUT scanning the
//         latest_value manifest. Asserts empty/backlog/acked boundaries, restart
//         stability, that the manifest is never scanned (via the query-kind hook), and
//         the §7 error families (disabled store, missing cursor, out-of-range cursor).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R5/§R12
//            step 2; K1.2d kickoff handoff §4/§7.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreBoundaryProviderTests
{
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteRouteStore> OpenActivatedAsync(
        string path, SqliteRouteStoreTestHooks? hooks = null)
    {
        var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy(), testHooks: hooks);
        await store.ActivateReplayStateTrackingAsync("route-a", ReplaySink, Ct);
        return store;
    }

    private static CanonicalDataPoint Point(long seq) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice("dev-1")
            .WithTag("tag", "Spindle/Speed")
            .WithValue((double)seq, CanonicalValueType.Double)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    private static CanonicalDataPoint[] Batch(long start, int count)
    {
        var b = new CanonicalDataPoint[count];
        for (int i = 0; i < count; i++)
        {
            b[i] = Point(start + i);
        }

        return b;
    }

    [Fact]
    public async Task Boundary_On_Fresh_Activated_Store_Is_Empty()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var boundary = await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            boundary.FirstPendingSequence.Should().Be(0);
            boundary.CutoffExclusive.Should().Be(0);
            boundary.HasBacklog.Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_After_Appends_Has_Backlog()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(Batch(0, 4), 0, Ct);

            var boundary = await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            boundary.FirstPendingSequence.Should().Be(0);
            boundary.CutoffExclusive.Should().Be(4);
            boundary.HasBacklog.Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Reflects_Acked_Cursor()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(Batch(0, 4), 0, Ct);
            await store.AckAsync(ReplaySink, 2, Ct); // cursor → 3

            var boundary = await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            boundary.FirstPendingSequence.Should().Be(3);
            boundary.CutoffExclusive.Should().Be(4);
            boundary.HasBacklog.Should().BeTrue();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Fully_Caught_Up_Has_No_Backlog()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(Batch(0, 4), 0, Ct);
            await store.AckAsync(ReplaySink, 3, Ct); // cursor → 4 == cutoff

            var boundary = await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            boundary.FirstPendingSequence.Should().Be(4);
            boundary.CutoffExclusive.Should().Be(4);
            boundary.HasBacklog.Should().BeFalse();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Survives_Restart()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path))
            {
                await store.AppendAsync(Batch(0, 5), 0, Ct);
                await store.AckAsync(ReplaySink, 1, Ct); // cursor → 2
            }

            await using var reopened = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());
            var boundary = await reopened.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            boundary.FirstPendingSequence.Should().Be(2);
            boundary.CutoffExclusive.Should().Be(5);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Capture_Never_Scans_The_Manifest()
    {
        var path = NewFilePath();
        try
        {
            var kinds = new List<CaptureQueryKind>();
            var hooks = new SqliteRouteStoreTestHooks(
                QueryExecuting: k => { lock (kinds) { kinds.Add(k); } });
            await using var store = await OpenActivatedAsync(path, hooks);
            await store.AppendAsync(Batch(0, 3), 0, Ct);

            await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);

            kinds.Should().Equal(CaptureQueryKind.Boundary);
            kinds.Should().NotContain(CaptureQueryKind.ManifestScan);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_On_Disabled_Store_Fails_TrackingNotEnabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync("route-a", path, SmallSqlitePolicy());

            var act = async () => await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Unregistered_Sink_Fails_SinkCursorNotFound()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureReplayBoundaryAsync("not-registered", Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreSinkCursorNotFound);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Boundary_Cursor_Above_Cutoff_Fails_CursorInconsistent()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            // Corrupt the persisted cursor to sit above the append cutoff (next_sequence == 0).
            RawExec(path, "UPDATE cursors SET next_unread = 5 WHERE sink_id = 'sp';");

            var act = async () => await store.CaptureReplayBoundaryAsync(ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.BufferCursorInconsistent);
        }
        finally { TryDelete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Boundary_Null_Or_Empty_SinkId_Throws_ArgumentException(string? sinkId)
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureReplayBoundaryAsync(sinkId!, Ct);
            await act.Should().ThrowAsync<ArgumentException>();
        }
        finally { TryDelete(path); }
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
