// ============================================================================
// File: Buffer/SqliteBufferReaderConcurrencyTests.cs
// Purpose: Regression test for the concurrent-reader corruption that faulted
//          a live route to Failed (red). The route worker runs two threads
//          against one SqliteRouteStore: the sink loop calls DequeueBatchAsync
//          and the intake pump calls GetStatsAsync — both use the SAME reader
//          SqliteConnection. Microsoft.Data.Sqlite's SqliteConnection is NOT
//          thread-safe; before the fix DequeueBatchAsync took no lock while
//          GetStatsAsync held the writer mutex, so their commands' create/
//          dispose lifecycles overlapped and corrupted the connection's
//          internal command list, throwing ArgumentOutOfRangeException /
//          IndexOutOfRangeException / NullReferenceException out of
//          SqliteConnection.RemoveCommand. That escaped the worker and faulted
//          the route. The fix serializes DequeueBatchAsync under the same
//          writer mutex GetStatsAsync uses.
//
// Pass condition: with both loops hammering the reader concurrently, NO
//   exception escapes (clean completion only). This test fails on the pre-fix
//   code (an index/NRE from RemoveCommand escapes within a short window).
//
// Reference: route-Test4 worker-fault stack trace,
//   SqliteRouteStore.DequeueBatchAsync / GetStatsAsync.
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

public sealed class SqliteBufferReaderConcurrencyTests
{
    /// <summary>
    /// Runs DequeueBatchAsync (sink-loop path) and GetStatsAsync (intake-pump
    /// path) concurrently in tight loops against one store. Both use the shared
    /// reader connection; the pre-fix code let their command lifecycles race and
    /// corrupt SqliteConnection, throwing from RemoveCommand. The post-fix code
    /// serializes both under the writer mutex, so nothing escapes.
    /// </summary>
    [Fact]
    public async Task ConcurrentDequeueAndGetStats_DoNotCorruptSharedReader()
    {
        const int Iterations = 5_000;
        var path = NewFilePath();
        Exception? caught = null;
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 200_000,
                DropPolicy = DropPolicy.DropOldest,
                ReclaimInterval = TimeSpan.FromSeconds(60),
                MaxBatchSize = 10_000,
            };
            var buf = await SqliteBuffer.OpenAsync("reader-race", path, policy);
            await buf.RegisterSinkAsync("s1", CancellationToken.None);
            await buf.EnqueueAsync(Batch(0, 2_000), CancellationToken.None);

            using var stopCts = new CancellationTokenSource();

            void Capture(Exception ex)
            {
                caught ??= ex;
                stopCts.Cancel();
            }

            // Intake-pump analogue: GetStatsAsync in a tight loop (uses _reader).
            var statsTask = Task.Run(async () =>
            {
                try
                {
                    for (var i = 0; i < Iterations && !stopCts.IsCancellationRequested; i++)
                    {
                        _ = await buf.GetStatsAsync();
                    }
                }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Capture(ex); }
            });

            // Sink-loop analogue: DequeueBatchAsync, also on _reader. Re-enqueue
            // whenever the buffer drains so the reader always has rows to scan,
            // keeping the overlap window with GetStatsAsync wide open.
            var drainTask = Task.Run(async () =>
            {
                long nextSeq = 2_000;
                try
                {
                    for (var i = 0; i < Iterations && !stopCts.IsCancellationRequested; i++)
                    {
                        var batch = await buf.DequeueBatchAsync("s1", 32, CancellationToken.None);
                        if (batch.IsEmpty)
                        {
                            await buf.EnqueueAsync(Batch(nextSeq, 500), CancellationToken.None);
                            nextSeq += 500;
                        }
                        else
                        {
                            await buf.AckAsync("s1", batch.LastSequence, CancellationToken.None);
                        }
                    }
                }
                catch (ObjectDisposedException) { }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Capture(ex); }
            });

            await Task.WhenAll(statsTask, drainTask).WaitAsync(TimeSpan.FromSeconds(60));
            await buf.DisposeAsync();
        }
        finally
        {
            TryDelete(path);
        }

        caught.Should().BeNull(
            "concurrent DequeueBatchAsync (sink loop) and GetStatsAsync (intake pump) " +
            "must not corrupt the shared reader SqliteConnection; caught " +
            $"{caught?.GetType().Name}: {caught?.Message}");
    }
}
