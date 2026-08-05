// ============================================================================
// File: Buffer/SqliteBufferReclaimSloTests.cs
// Purpose: Pin the C2b SLO from buffer-durability.md §9 — ack latency must
//          NOT be extended by an actively running reclaim loop. The reclaim
//          loop signals via TCS only; it never enters a DELETE path inside
//          AckAsync.
// ============================================================================

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferReclaimSloTests
{
    /// <summary>
    /// Measures ack P50/P95 in two scenarios and asserts the actively-
    /// reclaiming case is not materially worse than the idle case. The
    /// purpose is to catch any regression where ack latency starts to
    /// depend on reclaim work.
    /// </summary>
    [Fact]
    public async Task ReclaimDoesNotExtendAckLatency()
    {
        const int Samples = 200;

        var idleP50 = await MeasureAckLatencyAsync(reclaimIntervalMs: 60_000, samples: Samples);
        var busyP50 = await MeasureAckLatencyAsync(reclaimIntervalMs: 1, samples: Samples);

        // The two distributions should be statistically equivalent. Allow a
        // 5x margin for noise — the actual delta should be near zero. If
        // the busy P50 is more than 5x the idle P50 AND the absolute delta
        // is non-trivial (>1 ms), something is mixing reclaim into the ack
        // path.
        var delta = busyP50 - idleP50;
        var ratio = idleP50 > TimeSpan.Zero
            ? busyP50.TotalMicroseconds / idleP50.TotalMicroseconds
            : 1.0;

        bool failed = delta > TimeSpan.FromMilliseconds(1) && ratio > 5.0;
        failed.Should().BeFalse(
            $"ack latency must not depend on reclaim activity. " +
            $"Idle P50={idleP50.TotalMicroseconds:F1}us, Busy P50={busyP50.TotalMicroseconds:F1}us, ratio={ratio:F2}x");
    }

    private static async Task<TimeSpan> MeasureAckLatencyAsync(int reclaimIntervalMs, int samples)
    {
        var path = NewFilePath();
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 100_000,
                DropPolicy = DropPolicy.Block,
                ReclaimInterval = TimeSpan.FromMilliseconds(reclaimIntervalMs),
                MaxBatchSize = 10_000,
            };
            await using var buf = await SqliteBuffer.OpenAsync("slo", path, policy);
            await buf.RegisterSinkAsync("s", default);

            // Pre-populate so reclaim has work to do on every tick.
            for (int i = 0; i < 50; i++)
            {
                await buf.EnqueueAsync(Batch(i * 100, 100), default);
            }

            // Warm up the JIT/SQL plan.
            for (int i = 0; i < 10; i++)
            {
                var b = await buf.DequeueBatchAsync("s", 1, default);
                if (!b.IsEmpty)
                {
                    await buf.AckAsync("s", b.LastSequence, default);
                }
            }

            // Measure.
            var times = new long[samples];
            for (int i = 0; i < samples; i++)
            {
                // Enqueue one new point so the cursor always has something to ack.
                await buf.EnqueueAsync(Batch(50_000 + i, 1), default);
                var b = await buf.DequeueBatchAsync("s", 1, default);
                if (b.IsEmpty)
                {
                    times[i] = 0;
                    continue;
                }

                var sw = Stopwatch.StartNew();
                await buf.AckAsync("s", b.LastSequence, default);
                sw.Stop();
                times[i] = sw.ElapsedTicks;
            }

            Array.Sort(times);
            var p50Ticks = times[samples / 2];
            return TimeSpan.FromTicks(p50Ticks);
        }
        finally
        {
            TryDelete(path);
        }
    }
}
