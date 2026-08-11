// ============================================================================
// File: Buffer/SqliteRouteStoreSessionStateProviderTests.cs
// Covers: K1.2d R12 step 3 — IReplaySessionStateProvider on an enabled store.
//         CaptureBirthStateAsync (coherent boundary + birth snapshot) and
//         CaptureCutoverAsync (cutoff + snapshot, no sink) capture the raw state under
//         the lock, then decode the manifest OFF the lock into an immutable
//         LatestValueSnapshot. Also unit-tests the off-lock decoder
//         BuildSnapshotFromRawRows directly: deterministic entry + mid-loop cancellation
//         (no timing), and the §7 error families (undefined value_type, sequence at/beyond
//         cutoff, generation mismatch, invalid/duplicate identity, malformed/empty envelope).
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R6/§R9/§R12
//            step 3; K1.2d kickoff handoff §4/§5.
// ============================================================================

using System;
using System.Collections.Generic;
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

public sealed class SqliteRouteStoreSessionStateProviderTests
{
    private const string Route = "route-a";
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<SqliteRouteStore> OpenActivatedAsync(string path)
    {
        var store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy());
        await store.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
        return store;
    }

    private static CanonicalDataPoint Point(
        long seq, object? value, CanonicalValueType type, string device = "dev-1") =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice(device)
            .WithTag("tag", "Spindle/Speed")
            .WithValue(value, type)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    // ---- birth --------------------------------------------------------------

    [Fact]
    public async Task Birth_On_Fresh_Store_Is_Empty_And_Coherent()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var start = await store.CaptureBirthStateAsync(Route, ReplaySink, Ct);

            start.Boundary.FirstPendingSequence.Should().Be(0);
            start.Boundary.CutoffExclusive.Should().Be(0);
            start.Boundary.HasBacklog.Should().BeFalse();
            start.Snapshot.Count.Should().Be(0);
            start.Snapshot.Generation.Value.Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_After_Appends_Captures_Boundary_And_Snapshot()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[]
            {
                Point(0, 10.0, CanonicalValueType.Double, "dev-1"),
                Point(1, 20.0, CanonicalValueType.Double, "dev-2"),
            }, 0, Ct);

            var start = await store.CaptureBirthStateAsync(Route, ReplaySink, Ct);

            start.Boundary.FirstPendingSequence.Should().Be(0);
            start.Boundary.CutoffExclusive.Should().Be(2);
            start.Boundary.HasBacklog.Should().BeTrue();
            start.Snapshot.Count.Should().Be(2);
            start.Snapshot.MaxRouteBufferSequence.Should().Be(1);

            var v1 = start.Snapshot.TryGet(CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"));
            v1.Should().NotBeNull();
            v1!.Value.Should().Be(10.0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_Reflects_Acked_Cursor()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double, "dev-1"),
                Point(1, 2.0, CanonicalValueType.Double, "dev-2"),
            }, 0, Ct);
            await store.AckAsync(ReplaySink, 0, Ct); // cursor → 1

            var start = await store.CaptureBirthStateAsync(Route, ReplaySink, Ct);
            start.Boundary.FirstPendingSequence.Should().Be(1);
            start.Boundary.CutoffExclusive.Should().Be(2);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_Snapshot_Excludes_Stale_Generation_Rows()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[] { Point(0, 1.0, CanonicalValueType.Double, "dev-1") }, 0, Ct);
            await store.AckAsync(ReplaySink, 0, Ct);
            await store.AdvanceGenerationAsync(0, 1, Ct);
            await store.AppendAsync(new[] { Point(1, 2.0, CanonicalValueType.Double, "dev-2") }, 1, Ct);

            var start = await store.CaptureBirthStateAsync(Route, ReplaySink, Ct);

            start.Snapshot.Generation.Value.Should().Be(1);
            start.Snapshot.Count.Should().Be(1);
            start.Snapshot.TryGet(CanonicalMetricKey.Create("src-1", "dev-2", "Spindle/Speed")).Should().NotBeNull();
            start.Snapshot.TryGet(CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed")).Should().BeNull();
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_Survives_Restart()
    {
        var path = NewFilePath();
        try
        {
            await using (var store = await OpenActivatedAsync(path))
            {
                await store.AppendAsync(new[] { Point(0, 42.0, CanonicalValueType.Double) }, 0, Ct);
            }

            await using var reopened = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy());
            var start = await reopened.CaptureBirthStateAsync(Route, ReplaySink, Ct);

            start.Boundary.CutoffExclusive.Should().Be(1);
            start.Snapshot.Count.Should().Be(1);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_Route_Mismatch_Fails()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureBirthStateAsync("other-route", ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreRouteMismatch);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_On_Disabled_Store_Fails_TrackingNotEnabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy());

            var act = async () => await store.CaptureBirthStateAsync(Route, ReplaySink, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Birth_Unregistered_Sink_Fails_SinkCursorNotFound()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureBirthStateAsync(Route, "not-registered", Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreSinkCursorNotFound);
        }
        finally { TryDelete(path); }
    }

    // ---- cutover ------------------------------------------------------------

    [Fact]
    public async Task Cutover_On_Fresh_Store_Is_Empty()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var cutover = await store.CaptureCutoverAsync(Route, Ct);

            cutover.CutoffExclusive.Should().Be(0);
            cutover.Snapshot.Count.Should().Be(0);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Cutover_After_Appends_Captures_Cutoff_And_Snapshot()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(new[]
            {
                Point(0, 1.0, CanonicalValueType.Double, "dev-1"),
                Point(1, 2.0, CanonicalValueType.Double, "dev-2"),
                Point(2, 3.0, CanonicalValueType.Double, "dev-3"),
            }, 0, Ct);

            var cutover = await store.CaptureCutoverAsync(Route, Ct);

            cutover.CutoffExclusive.Should().Be(3);
            cutover.Snapshot.Count.Should().Be(3);
            cutover.Snapshot.MaxRouteBufferSequence.Should().Be(2);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Cutover_Route_Mismatch_Fails()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            var act = async () => await store.CaptureCutoverAsync("other-route", Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreRouteMismatch);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Cutover_On_Disabled_Store_Fails_TrackingNotEnabled()
    {
        var path = NewFilePath();
        try
        {
            await using var store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy());

            var act = async () => await store.CaptureCutoverAsync(Route, Ct);
            (await act.Should().ThrowAsync<BufferException>()).Which.Error.Code
                .Should().Be(CoreErrors.RouteStoreReplayTrackingNotEnabled);
        }
        finally { TryDelete(path); }
    }

    // ---- BuildSnapshotFromRawRows (off-lock decoder, unit) ------------------

    private static RawManifestRow Row(
        long seq, long gen = 0, double value = 1.0, string device = "dev-1", string tag = "Spindle/Speed")
    {
        var key = CanonicalMetricKey.Create("src-1", device, tag);
        var lmv = LatestMetricValue.Create(
            key, CanonicalValueType.Double, value, isNull: false,
            timestamp: DateTimeOffset.UnixEpoch, quality: DataQuality.Good, routeBufferSequence: seq);
        return new RawManifestRow("src-1", device, tag, (int)CanonicalValueType.Double, seq, gen, LatestValueEnvelopeV1.Encode(lmv));
    }

    [Fact]
    public void Decoder_Happy_Path_Builds_Snapshot()
    {
        var rows = new[] { Row(0, value: 1.0, device: "dev-1"), Row(1, value: 2.0, device: "dev-2") };

        var snapshot = SqliteRouteStore.BuildSnapshotFromRawRows(rows, generation: 0, cutoff: 2, CancellationToken.None);

        snapshot.Count.Should().Be(2);
        snapshot.Generation.Value.Should().Be(0);
        snapshot.TryGet(CanonicalMetricKey.Create("src-1", "dev-1", "Spindle/Speed"))!.Value.Should().Be(1.0);
    }

    [Fact]
    public void Decoder_Already_Canceled_Token_Throws_Before_Any_Decode()
    {
        var rows = new[] { Row(0) };
        var decoded = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(
            rows, 0, 1, cts.Token, _ => decoded++);

        act.Should().Throw<OperationCanceledException>();
        decoded.Should().Be(0);
    }

    [Fact]
    public void Decoder_Cancel_After_Row_255_Throws_At_Row_256()
    {
        // 300 distinct metrics; cancel after row 255 is decoded so the periodic check at
        // row 256 throws — proving PERIODIC (not just entry) cancellation, no timing.
        var rows = Enumerable.Range(0, 300).Select(i => Row(i, device: $"dev-{i}")).ToArray();
        var lastDecoded = -1;
        using var cts = new CancellationTokenSource();

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(
            rows, 0, 300, cts.Token,
            i =>
            {
                lastDecoded = i;
                if (i == 255)
                {
                    cts.Cancel();
                }
            });

        act.Should().Throw<OperationCanceledException>();
        lastDecoded.Should().Be(255); // row 256 threw before being decoded → no partial snapshot returned
    }

    [Fact]
    public void Decoder_Undefined_ValueType_Fails_BufferCorrupt()
    {
        var good = Row(0);
        var bad = good with { ValueType = 999 };

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { bad }, 0, 1, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
    }

    [Fact]
    public void Decoder_Sequence_At_Or_Beyond_Cutoff_Fails_CursorInconsistent()
    {
        var row = Row(5);

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { row }, 0, cutoff: 5, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.BufferCursorInconsistent);
    }

    [Fact]
    public void Decoder_Generation_Mismatch_Fails_BufferCorrupt()
    {
        var row = Row(0, gen: 7);

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { row }, generation: 0, cutoff: 1, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
    }

    [Fact]
    public void Decoder_Invalid_Identity_Fails_BufferCorrupt()
    {
        var row = Row(0) with { DeviceId = "" };

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { row }, 0, 1, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
    }

    [Fact]
    public void Decoder_Empty_Envelope_Fails_BufferCorrupt()
    {
        var row = Row(0) with { Envelope = Array.Empty<byte>() };

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { row }, 0, 1, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.BufferCorrupt);
    }

    [Fact]
    public void Decoder_Malformed_Envelope_Fails_EnvelopeUnsupported()
    {
        var row = Row(0) with { Envelope = new byte[] { 0x01, 0x02, 0x03 } };

        var act = () => SqliteRouteStore.BuildSnapshotFromRawRows(new[] { row }, 0, 1, CancellationToken.None);
        act.Should().Throw<BufferException>().Which.Error.Code.Should().Be(CoreErrors.RouteStoreEnvelopeUnsupported);
    }
}
