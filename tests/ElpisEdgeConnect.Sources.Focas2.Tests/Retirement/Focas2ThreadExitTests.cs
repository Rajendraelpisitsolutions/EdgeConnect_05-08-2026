// ============================================================================
// File: Retirement/Focas2ThreadExitTests.cs
// Purpose: The FOCAS2-specific proof (review): the worker-quiescence signal is
//          TRUE dedicated-thread termination, NOT a Join timeout. While a work
//          item is wedged on the affine thread (a Join would time out), the
//          thread-exit signal stays pending; only when the wedged call returns
//          and the thread actually exits does it resolve.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §7;
//            commit-3.0 FOCAS2 checkpoint (Join timeout != Proven).
// ============================================================================

using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Retirement;

public sealed class Focas2ThreadExitTests
{
    [Fact]
    public async Task ThreadExit_ResolvesWhenIdleThreadShutsDown()
    {
        var thread = new Focas2Thread("t-idle");

        thread.BeginShutdown(null);

        await thread.WaitForThreadExitAsync(); // an idle thread exits promptly
        await thread.DisposeAsync();
    }

    [Fact]
    public async Task ThreadExit_PendingWhileWedged_NotProvenByJoinTimeout_ThenResolvesOnTermination()
    {
        var thread = new Focas2Thread("t-wedged");
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        // A work item that BLOCKS the dedicated thread, like a wedged fwlib call.
        _ = thread.RunAsync(() =>
        {
            started.SetResult();
            release.Task.GetAwaiter().GetResult();
            return (object?)null;
        });
        await started.Task; // the thread is now inside the wedged work item

        thread.BeginShutdown(null);
        var exit = thread.WaitForThreadExitAsync();

        // The thread is still physically running the wedged work — a Join(10s)
        // would time out, but that is NOT termination, so the signal is pending.
        exit.IsCompleted.Should().BeFalse();

        // The wedged call returns → the thread drains and TRULY terminates.
        release.SetResult();
        await exit;
        exit.IsCompletedSuccessfully.Should().BeTrue();

        await thread.DisposeAsync();
    }
}
