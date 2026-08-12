// ============================================================================
// File: Retirement/Focas2ThreadShutdownGatesTests.cs
// Purpose: FOCAS2 checkpoint proof-safety gates:
//   G1 — idempotent shutdown: final cleanup enqueued at most once; second
//        shutdown after exit harmless.
//   G2 — a throwing affine final cleanup becomes a faulted thread-exit (terminal
//        Unproven at the retirement layer), observed, and never crashes the thread.
//   G3 — no fwlib work can be created after shutdown: RunAsync is rejected and the
//        work never executes.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §7;
//            commit-3.0 FOCAS2 checkpoint review (FOCAS-G1..G3).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Retirement;

public sealed class Focas2ThreadShutdownGatesTests
{
    // ── FOCAS-G1 ────────────────────────────────────────────────────

    [Fact]
    public async Task BeginShutdown_FinalWork_RunsAtMostOnce_AcrossRepeatedCalls()
    {
        var thread = new Focas2Thread("g1");
        var count = 0;

        thread.BeginShutdown(() => Interlocked.Increment(ref count));
        thread.BeginShutdown(() => Interlocked.Increment(ref count)); // queue completing → not enqueued

        await thread.WaitForThreadExitAsync();
        count.Should().Be(1);
        await thread.DisposeAsync();
    }

    [Fact]
    public async Task BeginShutdown_AfterThreadExit_IsHarmless()
    {
        var thread = new Focas2Thread("g1b");
        thread.BeginShutdown(null);
        await thread.WaitForThreadExitAsync();

        thread.BeginShutdown(null); // second shutdown after exit — no throw

        thread.WaitForThreadExitAsync().IsCompletedSuccessfully.Should().BeTrue();
        await thread.DisposeAsync();
    }

    // ── FOCAS-G2 ────────────────────────────────────────────────────

    [Fact]
    public async Task BeginShutdown_FinalCleanupThrows_ThreadExitFaults_Observed_ThreadDidNotCrash()
    {
        var thread = new Focas2Thread("g2");
        thread.BeginShutdown(() => throw new InvalidOperationException("cleanup boom"));

        var exit = thread.WaitForThreadExitAsync();
        var act = async () => await exit;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("cleanup boom");

        await thread.DisposeAsync(); // thread joined cleanly — it did not crash
    }

    // ── FOCAS-G3 ────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_AfterShutdown_IsRejected_AndWorkDoesNotExecute()
    {
        var thread = new Focas2Thread("g3");
        thread.BeginShutdown(null);

        var ran = false;
        var rejected = thread.RunAsync(() => { ran = true; return (object?)null; });

        var act = async () => await rejected;
        await act.Should().ThrowAsync<ObjectDisposedException>();
        ran.Should().BeFalse(); // no new fwlib work can be created after shutdown

        await thread.WaitForThreadExitAsync();
        await thread.DisposeAsync();
    }
}
