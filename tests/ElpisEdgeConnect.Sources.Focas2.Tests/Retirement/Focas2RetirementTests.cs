// ============================================================================
// File: Retirement/Focas2RetirementTests.cs
// Purpose: Pin the FOCAS2 durable retirement pattern: prompt non-blocking
//          cleanup initiation; responsive thread exit -> Proven; wedged native
//          call -> Completion pending (no false Proven; a Join timeout is NOT
//          proof) and a late thread exit resolves Proven; thread-exit fault and
//          cleanup-initiation fault -> terminal Unproven (distinct codes).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Sources.Focas2.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Retirement;

public sealed class Focas2RetirementTests
{
    private static AdapterRetirementContext Ctx() =>
        new() { ObservationToken = CancellationToken.None };

    [Fact]
    public void Begin_InitiatesCleanupImmediately_AndDoesNotBlockOnThread()
    {
        var initiated = false;
        var threadExit = new TaskCompletionSource();

        var op = Focas2Retirement.Begin(() => initiated = true, () => threadExit.Task, Ctx());

        initiated.Should().BeTrue();
        op.Completion.IsCompleted.Should().BeFalse();
        op.Snapshot.WorkerApplicable.Should().BeTrue();
    }

    [Fact]
    public async Task ResponsiveThreadExit_ResolvesProven()
    {
        var threadExit = new TaskCompletionSource();
        var op = Focas2Retirement.Begin(() => { }, () => threadExit.Task, Ctx());
        op.Completion.IsCompleted.Should().BeFalse();

        threadExit.SetResult();

        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeTrue();
        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
        attestation.DetailCode.Should().Be(Focas2RetirementDetailCodes.ThreadExitedProven);
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.NotApplicable);
    }

    [Fact]
    public async Task WedgedNativeCall_StaysPending_ThenLateThreadExitResolvesProven()
    {
        var threadExit = new TaskCompletionSource();
        var op = Focas2Retirement.Begin(() => { }, () => threadExit.Task, Ctx());

        // A Join would time out here; the attestation must NOT be Proven.
        op.Completion.IsCompleted.Should().BeFalse();

        threadExit.SetResult(); // the thread truly terminates later
        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task ThreadExitFaults_ResolvesTerminalUnproven_CleanupFailed_FailClosed()
    {
        // The dedicated thread's exit task faults ONLY when the affine final cleanup
        // threw, so a faulted exit maps to the precise CleanupFailed code.
        var op = Focas2Retirement.Begin(
            () => { },
            () => Task.FromException(new InvalidOperationException("x")),
            Ctx());

        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeFalse();
        attestation.Worker.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(Focas2RetirementDetailCodes.CleanupFailed);
    }

    [Fact]
    public async Task CleanupInitiationThrows_StillReturnsDurableOperation_TerminalUnproven()
    {
        var op = Focas2Retirement.Begin(
            () => throw new InvalidOperationException("cleanup boom"),
            () => Task.CompletedTask,
            Ctx());

        op.Should().NotBeNull();
        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeFalse();
        attestation.DetailCode.Should().Be(Focas2RetirementDetailCodes.CleanupFailed);
    }
}
