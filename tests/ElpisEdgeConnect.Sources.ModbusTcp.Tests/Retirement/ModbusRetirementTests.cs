// ============================================================================
// File: Retirement/ModbusRetirementTests.cs
// Purpose: Pin the Modbus durable retirement pattern: prompt non-blocking close;
//          responsive worker exit -> Proven; wedged worker -> Completion stays
//          pending (no false Proven) and a LATE exit resolves Proven; cleanup
//          fault -> terminal Unproven (fail closed).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Sources.ModbusTcp.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Retirement;

public sealed class ModbusRetirementTests
{
    private static AdapterRetirementContext Ctx() =>
        new() { ObservationToken = CancellationToken.None };

    [Fact]
    public void Begin_InitiatesCloseImmediately_AndDoesNotBlockOnWorker()
    {
        var closed = false;
        var workerExit = new TaskCompletionSource(); // never completes

        var op = ModbusRetirement.Begin(() => closed = true, () => workerExit.Task, Ctx());

        closed.Should().BeTrue();                     // close initiated promptly
        op.Completion.IsCompleted.Should().BeFalse(); // non-blocking; worker still in flight
        op.Snapshot.WorkerApplicable.Should().BeTrue();
        op.Snapshot.CallbackDrainApplicable.Should().BeFalse();
        op.Snapshot.BackgroundWorkApplicable.Should().BeFalse();
    }

    [Fact]
    public async Task Responsive_WorkerExits_ResolvesProven()
    {
        var workerExit = new TaskCompletionSource();
        var op = ModbusRetirement.Begin(() => { }, () => workerExit.Task, Ctx());
        op.Completion.IsCompleted.Should().BeFalse();

        workerExit.SetResult(); // read worker exited (wire idle)

        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeTrue();
        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
        attestation.DetailCode.Should().Be(ModbusRetirementDetailCodes.WireIdleProven);
        // M3: Modbus has no callbacks and no reconnect loop/timer/dispatcher.
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.NotApplicable);
    }

    [Fact]
    public async Task CloseInitiationThrows_StillReturnsDurableOperation_TerminalUnproven()
    {
        var op = ModbusRetirement.Begin(
            () => throw new InvalidOperationException("close boom"),
            () => Task.CompletedTask, // never consulted
            Ctx());

        // M1: the host receives a durable operation, not an exception.
        op.Should().NotBeNull();
        var attestation = await op.Completion;
        attestation.IsFullyProven.Should().BeFalse();
        attestation.Worker.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(ModbusRetirementDetailCodes.CloseFailed);
    }

    [Fact]
    public async Task Wedged_WorkerNeverExits_StaysPending_ThenLateExitResolvesProven()
    {
        var workerExit = new TaskCompletionSource();
        var op = ModbusRetirement.Begin(() => { }, () => workerExit.Task, Ctx());

        // At the host's deadline it records UnprovenAtDeadline; the adapter
        // operation must remain PENDING — never a false Proven.
        op.Completion.IsCompleted.Should().BeFalse();

        // A LATE worker exit resolves the RETAINED operation.
        workerExit.SetResult();
        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task WorkerExitFaults_ResolvesTerminalUnproven_FailClosed()
    {
        var op = ModbusRetirement.Begin(
            () => { },
            () => Task.FromException(new ObjectDisposedException("wire")),
            Ctx());

        var attestation = await op.Completion;

        attestation.IsFullyProven.Should().BeFalse();
        attestation.Worker.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(ModbusRetirementDetailCodes.Faulted);
    }
}
