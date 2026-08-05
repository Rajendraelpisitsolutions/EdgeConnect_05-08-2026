// ============================================================================
// File: Buffer/SqliteRouteStoreCaptureConcurrencyTests.cs
// Covers: K1.2d R12 step 5 — deterministic capture-vs-mutation ordering. Because a
//         capture holds the writer mutex for its whole duration, an append or a
//         generation advance LAUNCHED during the capture (from the constructor-injected
//         critical-section hook) is blocked until the capture releases the lock — so the
//         captured state provably excludes the concurrent mutation, and a later capture
//         sees it. No timers / Thread.Sleep: the mutex + an awaited task give determinism.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R7/§R12 step 5.
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteRouteStoreCaptureConcurrencyTests
{
    private const string Route = "route-a";
    private const string ReplaySink = "sp";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime BaseUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CanonicalDataPoint Point(long seq, double value, string device) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW-TEST")
            .WithSource("src-1", "mock")
            .WithDevice(device)
            .WithTag("tag", "Spindle/Speed")
            .WithValue(value, CanonicalValueType.Double)
            .WithGoodQuality(BaseUtc.AddSeconds(seq))
            .WithSequence(seq)
            .Build();

    [Fact]
    public async Task Append_Launched_During_Capture_Is_Excluded_Then_Seen_Next_Capture()
    {
        var path = NewFilePath();
        SqliteRouteStore store = null!;
        var launched = 0;
        Task? appendTask = null;

        var hooks = new SqliteRouteStoreTestHooks(
            CaptureEnteredCriticalSection: () =>
            {
                if (Interlocked.Exchange(ref launched, 1) == 0)
                {
                    // Blocks on the writer mutex (held by the in-flight capture) until it releases.
                    appendTask = Task.Run(() => store.AppendAsync(new[] { Point(1, 20.0, "dev-2") }, 0, Ct).AsTask());
                }
            });

        try
        {
            store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy(), testHooks: hooks);
            await store.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            await store.AppendAsync(new[] { Point(0, 10.0, "dev-1") }, 0, Ct); // pre-append (no hook)

            var s1 = await store.CaptureRawStateAsync(ReplaySink, Ct); // launches the dev-2 append (blocked)
            s1.CutoffExclusive.Should().Be(1);
            s1.Manifest.Select(r => r.DeviceId).Should().Equal("dev-1"); // dev-2 excluded

            await appendTask!; // dev-2 now commits

            var s2 = await store.CaptureRawStateAsync(ReplaySink, Ct);
            s2.CutoffExclusive.Should().Be(2);
            s2.Manifest.Select(r => r.DeviceId).Should().BeEquivalentTo(new[] { "dev-1", "dev-2" });
        }
        finally
        {
            if (store is not null) { await store.DisposeAsync(); }
            TryDelete(path);
        }
    }

    [Fact]
    public async Task GenerationAdvance_Launched_During_Capture_Is_Excluded_Then_Seen_Next_Capture()
    {
        var path = NewFilePath();
        SqliteRouteStore store = null!;
        var launched = 0;
        Task? advanceTask = null;

        var hooks = new SqliteRouteStoreTestHooks(
            CaptureEnteredCriticalSection: () =>
            {
                if (Interlocked.Exchange(ref launched, 1) == 0)
                {
                    advanceTask = Task.Run(() => store.AdvanceGenerationAsync(0, 1, Ct).AsTask());
                }
            });

        try
        {
            store = await SqliteRouteStore.OpenAsync(Route, path, SmallSqlitePolicy(), testHooks: hooks);
            await store.ActivateReplayStateTrackingAsync(Route, ReplaySink, Ct);
            await store.AppendAsync(new[] { Point(0, 10.0, "dev-1") }, 0, Ct);
            await store.AckAsync(ReplaySink, 0, Ct); // drain the replay sink to the head so the advance can proceed

            var s1 = await store.CaptureRawStateAsync(ReplaySink, Ct); // launches the advance (blocked)
            s1.Generation.Should().Be(0);
            s1.Manifest.Should().ContainSingle(); // dev-1 at gen 0

            await advanceTask!; // generation now 1

            var s2 = await store.CaptureRawStateAsync(ReplaySink, Ct);
            s2.Generation.Should().Be(1);
            s2.Manifest.Should().BeEmpty(); // dev-1 is now a stale-generation row
        }
        finally
        {
            if (store is not null) { await store.DisposeAsync(); }
            TryDelete(path);
        }
    }
}
