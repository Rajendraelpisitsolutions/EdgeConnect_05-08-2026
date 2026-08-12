// ============================================================================
// File: Buffer/SqliteBufferDisposalRaceTests.cs
// Purpose: Regression test for the disposal-order NRE observed during the
//          Phase 1 leak-harness kickoff. A background task hammers
//          DequeueBatchAsync while the main thread calls DisposeAsync.
//          The pre-fix code would throw NullReferenceException from
//          SqliteConnection.RemoveCommand; the post-fix code normalizes it
//          to ObjectDisposedException which the caller can handle gracefully.
//
// Pass conditions:
//   * No unhandled NullReferenceException
//   * Disposal path ends as either clean completion or ObjectDisposedException
//   * No other exception type escapes
//
// Reference: docs/phase1-exit/leak-harness-kickoff-summary.md anomaly #3
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

public sealed class SqliteBufferDisposalRaceTests
{
    /// <summary>
    /// Hammers DequeueBatchAsync from a background task while DisposeAsync
    /// runs on the main thread. The dequeue task must complete without
    /// throwing NullReferenceException — only clean completion or
    /// ObjectDisposedException are acceptable outcomes.
    /// </summary>
    [Fact]
    public async Task DequeueDuringDispose_DoesNotThrowNullReferenceException()
    {
        var path = NewFilePath();
        Exception? caught = null;
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 10_000,
                DropPolicy = DropPolicy.DropOldest,
                ReclaimInterval = TimeSpan.FromMilliseconds(50),
                MaxBatchSize = 1_000,
            };
            var buf = await SqliteBuffer.OpenAsync("race", path, policy);
            await buf.RegisterSinkAsync("s1", CancellationToken.None);
            await buf.EnqueueAsync(Batch(0, 500), CancellationToken.None);

            // Background task: loop DequeueBatchAsync as fast as possible.
            using var loopCts = new CancellationTokenSource();
            var dequeueTask = Task.Run(async () =>
            {
                try
                {
                    while (!loopCts.IsCancellationRequested)
                    {
                        try
                        {
                            var batch = await buf.DequeueBatchAsync(
                                "s1", 10, CancellationToken.None);
                            if (!batch.IsEmpty)
                            {
                                await buf.AckAsync(
                                    "s1", batch.LastSequence, CancellationToken.None);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // Expected during disposal race — the fix
                            // normalizes the NRE to ODE.
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Capture for assertion. If this is NRE, the fix is broken.
                    caught = ex;
                }
            });

            // Let the dequeue loop spin for a moment to build up race surface.
            await Task.Delay(50);

            // Dispose the buffer while the dequeue loop is running.
            await buf.DisposeAsync();
            loopCts.Cancel();

            // Wait for the dequeue task to observe disposal and exit.
            await dequeueTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            TryDelete(path);
        }

        // The critical assertion: no NullReferenceException escaped.
        if (caught is not null)
        {
            caught.Should().NotBeOfType<NullReferenceException>(
                "the disposal-race fix must normalize NRE to ObjectDisposedException; " +
                $"caught: {caught.GetType().Name}: {caught.Message}");
        }
    }

    /// <summary>
    /// After normal disposal (no racing), DequeueBatchAsync must throw
    /// ObjectDisposedException, not NRE.
    /// </summary>
    [Fact]
    public async Task DequeuAfterDispose_ThrowsObjectDisposedException()
    {
        var path = NewFilePath();
        try
        {
            var policy = new BufferPolicy
            {
                Mode = BufferMode.StoreAndForward,
                MaxDepth = 100,
                DropPolicy = DropPolicy.DropOldest,
                ReclaimInterval = TimeSpan.FromSeconds(60),
                MaxBatchSize = 100,
            };
            var buf = await SqliteBuffer.OpenAsync("post-dispose", path, policy);
            await buf.RegisterSinkAsync("s1", CancellationToken.None);
            await buf.EnqueueAsync(Batch(0, 10), CancellationToken.None);
            await buf.DisposeAsync();

            var act = async () => await buf.DequeueBatchAsync("s1", 10, CancellationToken.None);
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            TryDelete(path);
        }
    }
}
