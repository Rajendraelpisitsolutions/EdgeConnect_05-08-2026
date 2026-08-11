// ============================================================================
// File: Buffer/SqliteBufferReclaimRaceTests.cs
// Purpose: D10 regression test for the reclaim-loop NRE race that caused the
//          4-hour leak-harness kickoff to stall at t≈960s. Hammers AckAsync
//          concurrently with reclaim loop wake-ups to make the pre-fix race
//          fire probabilistically, then asserts the loop is still alive and
//          _tail is still advancing.
//
//          Root-cause walkthrough: see
//          docs/phase1-exit/leak-harness-kickoff-summary.md
//          §"4-hour kickoff run — FAIL" → "Recommended remediation plan"
//
// Milestone: D10 — liveness stall diagnosis (Phase 1 unblock).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2aTestFixtures;
using static ElpisEdgeConnect.Core.Tests.Buffer.C2bTestFixtures;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class SqliteBufferReclaimRaceTests
{
    /// <summary>
    /// Drives AckAsync at high frequency with a tight ReclaimInterval so
    /// the reclaim loop cycles rapidly and the `_reclaimSignal ??= …; var
    /// signalTask = _reclaimSignal.Task;` window gets exercised many times
    /// per second. On the pre-fix code this would throw NullReferenceException
    /// within a few seconds and silently kill the reclaim loop. On the fixed
    /// code the local `signal` reference survives the Interlocked.Exchange
    /// and the loop stays alive.
    ///
    /// Pass conditions:
    ///   1. The reclaim loop is still alive after 2 seconds of ack-hammering
    ///      (verified by observing that a later enqueue + ack eventually
    ///      advances Tail past the hammering range).
    ///   2. LastReclaimError is null — nothing was caught by the defensive
    ///      catch. If a future change reintroduces an unexpected exception
    ///      type the defensive catch catches it AND this assertion fires.
    /// </summary>
    [Fact]
    public async Task ReclaimLoop_SurvivesHighFrequencyAckRace()
    {
        var path = NewFilePath();
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 100_000,
                DropPolicy = DropPolicy.DropOldest,
                // Tight reclaim cadence: the loop wakes every 1 ms either
                // from the signal OR the timer. Combined with the ack-driven
                // signal path this maximizes the number of `??=` + `.Task`
                // iterations per second.
                ReclaimInterval = TimeSpan.FromMilliseconds(1),
                MaxBatchSize = 10_000,
            };
            await using var buf = await SqliteBuffer.OpenAsync("race", path, policy);
            await buf.RegisterSinkAsync("s", default);

            // Pre-populate so AckAsync has cursors to advance.
            await buf.EnqueueAsync(Batch(0, 10_000), default);

            // Hammer AckAsync from the test thread for 2 seconds. Each ack
            // calls Interlocked.Exchange(ref _reclaimSignal, null) and then
            // TrySetResult, which is exactly the race vector the reclaim
            // loop needs to survive.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            var ackSeq = 0L;
            var ackCount = 0;
            while (DateTime.UtcNow < deadline && ackSeq < 9_999)
            {
                await buf.AckAsync("s", ackSeq, default);
                ackSeq++;
                ackCount++;
            }

            // Threshold is deliberately low (5, not 100) because each
            // AckAsync does a synchronous=FULL fsync. On spinning HDDs
            // (e.g., HP Z210 workstation) each ack takes ~125ms, yielding
            // only ~16 acks in 2 seconds. The test's purpose is to exercise
            // the reclaim-signal race, not to assert throughput.
            ackCount.Should().BeGreaterThan(5,
                "the ack hammer should have driven enough acks through the race window");

            // The reclaim loop must still be alive. Two ways to verify:
            //
            //   (a) LastReclaimError must remain null — the D10 defensive
            //       catch did NOT fire, meaning no unexpected exception
            //       reached it. With the local-capture fix in place, the
            //       NRE that the pre-fix code would throw never happens.
            //
            //   (b) Enqueueing new data and advancing the cursor past it
            //       must eventually cause Tail to advance, which only
            //       happens if the reclaim loop is running.
            buf.LastReclaimError.Should().BeNull(
                "the reclaim loop's D10 defensive catch should not have fired; " +
                $"if it did, the exception was: {buf.LastReclaimError}");

            await buf.EnqueueAsync(Batch(10_000, 100), default);
            await buf.AckAsync("s", 10_050, default);

            // Wait briefly for reclaim to pick up the signal and advance
            // the tail. The loop runs every 1 ms so this should be
            // near-instant, but give it 2 seconds of slack for CI noise.
            var tailAdvancedDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (buf.Tail <= 10 && DateTime.UtcNow < tailAdvancedDeadline)
            {
                await Task.Delay(10);
            }

            buf.Tail.Should().BeGreaterThan(10,
                "reclaim loop must still be advancing the tail after the ack hammer");
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// Documents that the D10 defensive catch exists and is reachable via
    /// the seam. We don't have a way to inject an exception into the
    /// reclaim loop without modifying production code, so this test just
    /// pins that <c>LastReclaimError</c> is accessible and null in the
    /// healthy steady state. If a future regression silently fills it,
    /// <see cref="ReclaimLoop_SurvivesHighFrequencyAckRace"/> asserts that
    /// the defensive catch did not fire.
    /// </summary>
    [Fact]
    public async Task LastReclaimError_IsNull_InHealthyState()
    {
        var path = NewFilePath();
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 100,
                DropPolicy = DropPolicy.DropOldest,
                ReclaimInterval = TimeSpan.FromMilliseconds(10),
                MaxBatchSize = 100,
            };
            await using var buf = await SqliteBuffer.OpenAsync("healthy", path, policy);
            await buf.RegisterSinkAsync("s", default);
            await buf.EnqueueAsync(Batch(0, 10), default);
            await Task.Delay(100); // let the reclaim loop cycle a few times
            buf.LastReclaimError.Should().BeNull();
        }
        finally
        {
            TryDelete(path);
        }
    }
}
