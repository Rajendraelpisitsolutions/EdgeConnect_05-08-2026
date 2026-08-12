// ============================================================================
// File: Retirement/S7RetirementTests.cs
// Purpose: Pin the S7 durable retirement pattern (mirrors Modbus): prompt
//          non-blocking close; responsive worker exit -> Proven; wedged worker
//          -> Completion stays pending (no false Proven) and a late exit resolves
//          Proven; worker fault and close-initiation fault -> terminal Unproven
//          (distinct codes; fail closed).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Sources.S7.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Retirement;

public sealed class S7RetirementTests
{
    private static AdapterRetirementContext Ctx() =>
        new() { ObservationToken = CancellationToken.None };

    [Fact]
    public void Begin_InitiatesCloseImmediately_AndDoesNotBlockOnWorker()
    {
        var closed = false;
        var workerExit = new TaskCompletionSource();

        var op = S7Retirement.Begin(() => closed = true, () => workerExit.Task, Ctx());

        closed.Should().BeTrue();
        op.Completion.IsCompleted.Should().BeFalse();
        op.Snapshot.WorkerApplicable.Should().BeTrue();
    }

    [Fact]
    public async Task Responsive_WorkerExits_ResolvesProven()
    {
        var workerExit = new TaskCompletionSource();
        var op = S7Retirement.Begin(() => { }, () => workerExit.Task, Ctx());
        op.Completion.IsCompleted.Should().BeFalse();

        workerExit.SetResult();

        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeTrue();
        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
        attestation.DetailCode.Should().Be(S7RetirementDetailCodes.WireIdleProven);
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.NotApplicable);
    }

    [Fact]
    public async Task Wedged_WorkerNeverExits_StaysPending_ThenLateExitResolvesProven()
    {
        var workerExit = new TaskCompletionSource();
        var op = S7Retirement.Begin(() => { }, () => workerExit.Task, Ctx());

        op.Completion.IsCompleted.Should().BeFalse(); // host would record UnprovenAtDeadline here

        workerExit.SetResult();
        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task WorkerExitFaults_ResolvesTerminalUnproven_FailClosed()
    {
        var op = S7Retirement.Begin(
            () => { },
            () => Task.FromException(new ObjectDisposedException("wire")),
            Ctx());

        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeFalse();
        attestation.Worker.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(S7RetirementDetailCodes.Faulted);
    }

    [Fact]
    public async Task CloseInitiationThrows_StillReturnsDurableOperation_TerminalUnproven()
    {
        var op = S7Retirement.Begin(
            () => throw new InvalidOperationException("close boom"),
            () => Task.CompletedTask,
            Ctx());

        op.Should().NotBeNull();
        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeFalse();
        attestation.Worker.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(S7RetirementDetailCodes.CloseFailed);
    }
}
