// ============================================================================
// File: Buffer/SqliteRouteStoreCapturePerfEvidenceTests.cs
// Covers: K1.2d R12 step 5 — O-D two-dataset capture perf EVIDENCE (measure first; no
//         schema_generation index in K1.2d). Reports, per capture: total latest_value
//         rows, current-generation rows returned, under-lock (raw capture) duration,
//         off-lock decode duration, and the total:current-gen ratio. Two datasets:
//         (1) no schema churn (total == current-gen); (2) many stale-generation rows +
//         a small current-generation subset (a metric removed in a later generation leaves
//         a permanent stale-gen row). Assertions are STRUCTURAL only (row counts / ratio),
//         NOT timing.
//
// SCOPE / HONEST LIMIT (review round 1): both datasets hold ~the same TOTAL row count
// (500 vs 510), so this isolates OFF-LOCK DECODE cost (which tracks the returned
// current-generation rows) — it does NOT measure how the UNDER-LOCK full scan grows with
// stale-row count (there is no schema_generation index; the WHERE is a filtered full scan
// over the PK). At ~500 total rows no capture concern was observed. The effect of a large
// stale-row population on the under-lock scan (fixed current, e.g. 10 current + 0 / 10k /
// 100k stale, with EXPLAIN QUERY PLAN) is a K1.2e measurement, and is the input to any
// future stale-row cleanup / index decision. Sub-millisecond one-shot timings here are
// dominated by cache/scheduling and are indicative only.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R10/§R12 step 5.
// ============================================================================

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreCapturePerfEvidenceTests
{
    private const string Route = "route-a";
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ITestOutputHelper _out;

    public SqliteRouteStoreCapturePerfEvidenceTests(ITestOutputHelper output) => _out = output;

    private static CanonicalDataPoint Point(long seq, string device) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice(device)
            .WithTag("tag", "Spindle/Speed")
            .WithValue((double)seq, CanonicalValueType.Double)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    private static CanonicalDataPoint[] DistinctBatch(long startSeq, int count, string devicePrefix)
    {
        var batch = new CanonicalDataPoint[count];
        for (int i = 0; i < count; i++)
        {
            batch[i] = Point(startSeq + i, $"{devicePrefix}-{i}");
        }

        return batch;
    }

    [Fact]
    public async Task Perf_Evidence_Dataset1_No_Churn_Total_Equals_CurrentGen()
    {
        const int n = 500;
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);
            await store.AppendAsync(DistinctBatch(0, n, "dev"), 0, Ct);

            var (raw, snapshot, underLock, offLock) = await MeasureAsync(store);
            var total = CountManifest(path);

            total.Should().Be(n);
            raw.Manifest.Count.Should().Be(n);          // current-gen == total (no churn)
            snapshot.Count.Should().Be(n);
            Report("dataset-1 (no churn)", total, raw.Manifest.Count, underLock, offLock);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Perf_Evidence_Dataset2_Many_Stale_Small_Current()
    {
        const int stale = 500; // gen-0 metrics that get orphaned
        const int current = 10; // gen-1 metrics
        var path = NewFilePath();
        try
        {
            await using var store = await OpenActivatedAsync(path);

            await store.AppendAsync(DistinctBatch(0, stale, "old"), 0, Ct);
            await store.AckAsync(ReplaySink, stale - 1, Ct);       // drain sp to the head
            await store.AdvanceGenerationAsync(0, 1, Ct);
            await store.AppendAsync(DistinctBatch(stale, current, "new"), 1, Ct);

            var (raw, snapshot, underLock, offLock) = await MeasureAsync(store);
            var total = CountManifest(path);

            total.Should().Be(stale + current);         // stale rows persist
            raw.Manifest.Count.Should().Be(current);    // capture returns only the current generation
            snapshot.Count.Should().Be(current);
            Report("dataset-2 (many stale)", total, raw.Manifest.Count, underLock, offLock);
        }
        finally { TryDelete(path); }
    }

    private static async Task<(RawCaptureState Raw, LatestValueSnapshot Snapshot, double UnderLockMs, double OffLockMs)>
        MeasureAsync(SqliteRouteStore store)
    {
        var sw1 = Stopwatch.StartNew();
        var raw = await store.CaptureRawStateAsync(ReplaySink, Ct); // under the lock
        sw1.Stop();

        var sw2 = Stopwatch.StartNew();
        var snapshot = SqliteRouteStore.BuildSnapshotFromRawRows(raw.Manifest, raw.Generation, raw.CutoffExclusive, Ct); // off the lock
        sw2.Stop();

        return (raw, snapshot, sw1.Elapsed.TotalMilliseconds, sw2.Elapsed.TotalMilliseconds);
    }

    private void Report(string label, long total, int currentGen, double underLockMs, double offLockMs)
    {
        var ratio = currentGen == 0 ? double.NaN : (double)total / currentGen;
        _out.WriteLine(
            $"{label}: total={total} currentGen={currentGen} ratio={ratio:F1} " +
            $"underLock={underLockMs:F2}ms offLockDecode={offLockMs:F2}ms total={underLockMs + offLockMs:F2}ms");
    }

    private static async Task<SqliteRouteStore> OpenActivatedAsync(string path)
    {
        var store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy(maxDepth: 4096));
        await store.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
        return store;
    }

    private static long CountManifest(string path)
    {
        using var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM latest_value;";
        return (long)cmd.ExecuteScalar()!;
    }
}
