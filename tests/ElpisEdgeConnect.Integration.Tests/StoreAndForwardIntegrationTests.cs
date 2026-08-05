// ============================================================================
// File: StoreAndForwardIntegrationTests.cs
// Purpose: Phase 3 follow-up — formal CI-gateable integration tests for the
//          per-route durable buffer's two headline guarantees:
//
//            1. Sink-outage zero-loss replay-on-reconnect (Track 2.2)
//               When a sink fails mid-run, points accumulate durably in
//               the buffer; when the sink recovers, every queued point
//               drains to it in order. No data is lost.
//
//            2. Host-restart resumption from buffer (Track 2.3)
//               When a host stops with points still queued, the SQLite
//               buffer file persists on disk; the next host instance
//               reopens that file and the queued points drain to its
//               (newly healthy) sink. No data is lost across restart.
//
//          Both tests use MockSinkAdapter (not real MQTT) for
//          deterministic outage simulation. The real MQTT-sink-specific
//          behavior — partial-publish-failure during a TCP RST window,
//          MQTTnet auto-reconnect timing — is covered by the live-broker
//          smoke; that path is verified manually before each merge.
//
// Reference: ARCHITECTURE_BLUEPRINT.md §6 (Store-and-Forward),
//            shared-knowledge/architecture-overview.md, Phase 3 follow-up
//            from the S&F production-wire-up landing in master @ 2ece6f1
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.MockAdapters;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Integration.Tests.IntegrationTestData;

namespace ElpisEdgeConnect.Integration.Tests;

public sealed class StoreAndForwardIntegrationTests
{
    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout, string description = "")
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException($"Predicate did not become true within {timeout}. {description}");
    }

    /// <summary>
    /// Track 2.2 — when a sink reports failures mid-run, points pile up in
    /// the durable buffer; when the sink heals, every queued point drains
    /// to it. Zero loss across the outage cycle.
    /// </summary>
    /// <remarks>
    /// This is the deterministic CI counterpart to the live-broker outage
    /// scenario captured during the S&amp;F pilot smoke (commit 2ece6f1's
    /// merge log). MockSinkAdapter.FailUntilSignaled lets us model the
    /// outage as a clean atomic event — no wall-clock disconnect window,
    /// no MQTTnet retry timing — so the test pins ONLY the routing-engine
    /// + buffer interaction we care about.
    /// </remarks>
    [Fact]
    public async Task StoreAndForward_SinkOutage_ZeroLossOnReconnect()
    {
        const int totalPoints = 200;
        const int healthyPhasePoints = 50;

        var src = new MockSourceAdapter("src-1") { PointsPerPoll = 1, StopAfterPoints = totalPoints };
        var sink = new MockSinkAdapter("sink-1");

        await using var h = HostHarness.Build(
            sources: new[] { SourceReg(src, "route-1") },
            sinks: new[] { SinkReg(sink, "route-1") },
            config: Config(Route("route-1", "src-1", new[] { "sink-1" },
                buffer: StoreAndForwardBuffer())));

        await h.StartAsync();

        // ----- Phase 1: healthy sink — first ~50 points publish cleanly -----
        await WaitForAsync(
            () => sink.PublishedCount >= healthyPhasePoints,
            DefaultWait,
            $"sink should receive at least {healthyPhasePoints} points before the outage");

        var publishedBeforeOutage = sink.PublishedCount;
        publishedBeforeOutage.Should().BeGreaterThanOrEqualTo(healthyPhasePoints);

        // ----- Phase 2: induce outage — every publish fails until signaled -----
        var outageGate = new TaskCompletionSource();
        sink.FailUntilSignaled = outageGate;

        // Wait until the buffer is durably absorbing points. The route
        // worker pushes BufferStats every poll cycle, so we can read
        // CurrentDepth from the diagnostics surface to confirm the
        // outage is having the right effect on the buffer.
        var diag = h.GetRequiredService<IDiagnosticsService>();
        await WaitForAsync(
            () =>
            {
                var snap = diag.GetRouteSnapshot("route-1");
                return snap?.Buffer is { CurrentDepth: > 0 };
            },
            DefaultWait,
            "buffer depth should grow while sink is failing");

        var bufferDuringOutage = diag.GetRouteSnapshot("route-1")!.Buffer!;
        bufferDuringOutage.CurrentDepth.Should().BeGreaterThan(0,
            "S&F buffer should hold points the failing sink can't accept");
        bufferDuringOutage.Mode.Should().Be("StoreAndForward");

        // ----- Phase 3: heal the sink — buffered + remaining live points drain -----
        sink.FailUntilSignaled = null;
        outageGate.TrySetResult();

        await WaitForAsync(
            () => sink.PublishedCount == totalPoints,
            DefaultWait,
            $"sink should receive all {totalPoints} points after healing");

        // Final invariants:
        //   * Sink got every source point exactly once
        //   * No drops at the buffer (capacity or retention)
        //   * No duplicates in the published stream
        sink.PublishedCount.Should().Be(totalPoints,
            "every source point must reach the sink across the outage cycle");

        var finalBuffer = diag.GetRouteSnapshot("route-1")!.Buffer!;
        finalBuffer.DroppedByCapacity.Should().Be(0,
            "buffer capacity is generous enough that no point should be evicted");
        finalBuffer.DroppedByRetention.Should().Be(0,
            "test runs well under MaxAgeDays so no age eviction should fire");

        var sequenceNumbers = sink.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
        sequenceNumbers.Should().OnlyHaveUniqueItems(
            "fanout dispatcher must not deliver the same point twice");
        sequenceNumbers.Should().BeInAscendingOrder(
            "per-source ordering is a Phase 1 LOCK; replay must preserve it");

        await h.StopAsync();
    }

    /// <summary>
    /// Track 2.3 — when a host stops with points still in its durable
    /// buffer, the SQLite file persists on disk. A second host opening
    /// the same data dir reopens that buffer and drains the queued
    /// points to its (healthy) sink. Pins the buffer's cross-restart
    /// durability promise.
    /// </summary>
    /// <remarks>
    /// Two HostHarness instances share a fixed data directory via
    /// <see cref="HostHarness.BuildWithDataDir"/>; cleanup is the test's
    /// responsibility (a try/finally around the dir, plus opt-out of
    /// the per-harness temp-dir cleanup). The first host's sink fails
    /// permanently so EVERY emitted point must end up on disk; the
    /// second host's sink is healthy so they all drain.
    /// </remarks>
    [Fact]
    public async Task StoreAndForward_HostRestart_ResumesFromBuffer()
    {
        const int queuedPoints = 50;
        const string routeId = "route-restart";
        const string srcId = "src-restart";
        const string sinkId = "sink-restart";

        var dataDir = Path.Combine(
            Path.GetTempPath(),
            "edgeconnect-snf-restart-" + Guid.NewGuid().ToString("N"));

        try
        {
            var routeConfig = Config(Route(routeId, srcId, new[] { sinkId },
                buffer: StoreAndForwardBuffer()));

            // ----- First host: sink fails so every point queues to disk -----
            var srcA = new MockSourceAdapter(srcId)
            {
                PointsPerPoll = 1,
                StopAfterPoints = queuedPoints,
            };
            var sinkA = new MockSinkAdapter(sinkId)
            {
                FailUntilSignaled = new TaskCompletionSource(),
            };

            await using (var h1 = HostHarness.BuildWithDataDir(
                sources: new[] { SourceReg(srcA, routeId) },
                sinks: new[] { SinkReg(sinkA, routeId) },
                config: routeConfig,
                dataDir: dataDir))
            {
                await h1.StartAsync();

                // Source should emit all 50 — they have nowhere to go but the buffer.
                await WaitForAsync(
                    () => srcA.EmittedCount == queuedPoints,
                    DefaultWait,
                    "source should emit every configured point even when sink is failing");

                // Buffer should hold all 50 — the always-failing sink never acks
                // its cursor.
                var diag1 = h1.GetRequiredService<IDiagnosticsService>();
                await WaitForAsync(
                    () =>
                    {
                        var snap = diag1.GetRouteSnapshot(routeId);
                        return snap?.Buffer is { CurrentDepth: >= queuedPoints };
                    },
                    DefaultWait,
                    $"buffer should hold at least {queuedPoints} points before host_1 stops");

                sinkA.PublishedCount.Should().Be(0,
                    "the always-failing sink never accepts a publish");

                await h1.StopAsync();
            }

            // ----- The buffer file is on disk — that's the whole point of S&F. -----
            //
            // Path convention (post Chip 4 / Bug 1 P3): DefaultRouteBufferFactory
            // roots SqliteBuffer files under `{dataRoot}/buffer/`, NOT
            // `{dataRoot}/config/buffer/`. The pre-Chip-4 doubled-config-dir
            // path was a bug; CompositionRoot now passes `options.ResolvedDataRoot`
            // to the factory and a migration shim moves any legacy triplet on
            // first open. See DefaultRouteBufferFactory.MigrateLegacyBufferIfPresent.
            var bufferFile = Path.Combine(dataDir, "buffer", routeId + ".db");
            File.Exists(bufferFile).Should().BeTrue(
                $"SqliteBuffer must persist its file across graceful shutdown (looked at {bufferFile})");
            new FileInfo(bufferFile).Length.Should().BeGreaterThan(0,
                "the persisted buffer file should be non-empty when points are queued");

            // ----- Second host: same data dir, healthy sink — buffer should drain -----
            var srcB = new MockSourceAdapter(srcId)
            {
                // Deliberately stop the source so the only points the sink
                // can possibly receive are the ones replayed from disk —
                // that keeps the assertion unambiguous about cross-restart
                // resumption being the source of the data.
                PointsPerPoll = 0,
                StopAfterPoints = 0,
            };
            var sinkB = new MockSinkAdapter(sinkId);

            await using (var h2 = HostHarness.BuildWithDataDir(
                sources: new[] { SourceReg(srcB, routeId) },
                sinks: new[] { SinkReg(sinkB, routeId) },
                config: routeConfig,
                dataDir: dataDir))
            {
                await h2.StartAsync();

                // Every queued point must drain to the healthy sink.
                await WaitForAsync(
                    () => sinkB.PublishedCount == queuedPoints,
                    DefaultWait,
                    "host_2 must replay every buffered point from the previous run");

                sinkB.PublishedCount.Should().Be(queuedPoints,
                    "exactly the points host_1 buffered should arrive at host_2's sink");

                // Buffer should be empty after drain; sequencing intact.
                var diag2 = h2.GetRequiredService<IDiagnosticsService>();
                await WaitForAsync(
                    () =>
                    {
                        var snap = diag2.GetRouteSnapshot(routeId);
                        return snap?.Buffer is { CurrentDepth: 0 };
                    },
                    DefaultWait,
                    "buffer should drain to zero once host_2's healthy sink absorbs the replay");

                var sequences = sinkB.PublishedPoints.Select(p => p.SequenceNumber).ToArray();
                sequences.Should().OnlyHaveUniqueItems(
                    "replay must not duplicate points");
                sequences.Should().BeInAscendingOrder(
                    "replay must preserve per-source ordering across the restart boundary");

                await h2.StopAsync();
            }
        }
        finally
        {
            try { Directory.Delete(dataDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
