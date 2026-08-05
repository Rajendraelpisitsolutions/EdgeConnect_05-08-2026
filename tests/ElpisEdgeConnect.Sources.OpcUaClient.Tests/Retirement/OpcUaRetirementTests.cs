// ============================================================================
// Tests: OpcUaRetirementTests — pin the OPC UA retirement orchestration via
//        injected delegates (no live session/stack):
//          * Worker is always NotApplicable (supervisor-owned pump);
//          * all surfaces quiesce → IsFullyProven, stable Proven detail code;
//          * a drain that is not fully-drained → CallbackDrain Unproven;
//          * a background-work (coordinator dispose) fault → BackgroundWork
//            Unproven, fail closed;
//          * a thrown drain → CallbackDrain Unproven, fail closed;
//          * close-ingress-flag failure still yields a DURABLE terminal operation;
//          * best-effort subscription unwire failure does NOT gate proof;
//          * sequencing: ingress flag → unwire → background → drain;
//          * Blocker 1 — the HOST observation token never terminates the durable
//            operation: cancelling it leaves Completion pending, and a LATE drain
//            after the host deadline still resolves Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7;
//            commit-3.0 complete-diff review (Blockers 1 & 2, 2026-06-26).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Sources.OpcUaClient.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests.Retirement;

public sealed class OpcUaRetirementTests
{
    private static AdapterRetirementContext Ctx(CancellationToken token = default) =>
        new() { ObservationToken = token };

    private static CallbackDrainResult Drained() => new()
    {
        FullyDrained = true,
        DroppedAtShutdown = 0,
        RejectedAfterRetirement = 0,
    };

    private static CallbackDrainResult NotDrained() => new()
    {
        FullyDrained = false,
        DroppedAtShutdown = 5,
        RejectedAfterRetirement = 0,
    };

    private static AdapterRetirementOperation Begin(
        Action? closeIngressFlag = null,
        Func<Task>? unwireSubscriptions = null,
        Func<Task>? stopBackgroundWork = null,
        Func<Task<CallbackDrainResult>>? drainCallbacks = null,
        AdapterRetirementContext? context = null) =>
        OpcUaRetirement.Begin(
            closeIngressFlag ?? (() => { }),
            unwireSubscriptions ?? (() => Task.CompletedTask),
            stopBackgroundWork ?? (() => Task.CompletedTask),
            drainCallbacks ?? (() => Task.FromResult(Drained())),
            context ?? Ctx());

    [Fact]
    public async Task AllSurfacesQuiesce_ResolvesFullyProven_WorkerNotApplicable()
    {
        var attestation = await Begin().Completion;

        attestation.IsFullyProven.Should().BeTrue();
        attestation.Worker.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.Proven);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.Proven);
        attestation.DetailCode.Should().Be(OpcUaRetirementDetailCodes.Proven);
    }

    [Fact]
    public void Snapshot_ReflectsSurfaceModel_WorkerNotApplicable_CallbackAndBackgroundApplicable()
    {
        var op = Begin();

        op.Snapshot.WorkerApplicable.Should().BeFalse();
        op.Snapshot.CallbackDrainApplicable.Should().BeTrue();
        op.Snapshot.BackgroundWorkApplicable.Should().BeTrue();
    }

    [Fact]
    public async Task DrainNotFullyDrained_CallbackUnproven_NotFullyProven()
    {
        var attestation = await Begin(drainCallbacks: () => Task.FromResult(NotDrained())).Completion;

        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.Unproven);
        attestation.IsFullyProven.Should().BeFalse();
        attestation.DetailCode.Should().Be(OpcUaRetirementDetailCodes.CallbackUndrained);
    }

    [Fact]
    public async Task DrainThrows_FailsClosed_CallbackUnproven()
    {
        var attestation = await Begin(
            drainCallbacks: () => Task.FromException<CallbackDrainResult>(new InvalidOperationException("drain boom"))).Completion;

        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.Unproven);
        attestation.IsFullyProven.Should().BeFalse();
    }

    [Fact]
    public async Task BackgroundWorkFaults_BackgroundUnproven_NotFullyProven()
    {
        var attestation = await Begin(
            stopBackgroundWork: () => Task.FromException(new InvalidOperationException("coordinator dispose boom"))).Completion;

        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.Unproven);
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.Proven);
        attestation.IsFullyProven.Should().BeFalse();
        attestation.DetailCode.Should().Be(OpcUaRetirementDetailCodes.BackgroundWorkFault);
    }

    [Fact]
    public async Task CloseIngressFlagThrows_StillDurableOperation_TerminalUnproven()
    {
        var drainRan = false;
        var attestation = await Begin(
            closeIngressFlag: () => throw new InvalidOperationException("ingress boom"),
            drainCallbacks: () => { drainRan = true; return Task.FromResult(Drained()); }).Completion;

        attestation.IsFullyProven.Should().BeFalse();
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.Unproven);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.Unproven);
        attestation.DetailCode.Should().Be(OpcUaRetirementDetailCodes.Faulted);
        drainRan.Should().BeFalse(); // aborted before relying on drain
    }

    [Fact]
    public async Task UnwireSubscriptionsThrows_BestEffort_DoesNotGateProof()
    {
        // The dispatcher ingress flag is authoritative; a stack-unwire failure must
        // not prevent a fully-proven attestation when drain + background succeed.
        var attestation = await Begin(
            unwireSubscriptions: () => Task.FromException(new InvalidOperationException("delete boom"))).Completion;

        attestation.IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task Sequencing_IngressFlag_Then_Unwire_Then_Background_Then_Drain()
    {
        var order = new List<string>();
        await Begin(
            closeIngressFlag: () => { lock (order) order.Add("ingress"); },
            unwireSubscriptions: () => { lock (order) order.Add("unwire"); return Task.CompletedTask; },
            stopBackgroundWork: () => { lock (order) order.Add("background"); return Task.CompletedTask; },
            drainCallbacks: () => { lock (order) order.Add("drain"); return Task.FromResult(Drained()); }).Completion;

        order.Should().Equal("ingress", "unwire", "background", "drain");
    }

    // ── Blocker 1 — host observation token is NOT terminal evidence ──────────

    [Fact]
    public async Task HostObservationTokenCancelled_LeavesCompletionPending_ThenLateDrainResolvesProven()
    {
        using var hostDeadline = new CancellationTokenSource();
        var drainGate = new TaskCompletionSource<CallbackDrainResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var op = Begin(drainCallbacks: () => drainGate.Task, context: Ctx(hostDeadline.Token));

        hostDeadline.Cancel(); // the host stops waiting at its deadline

        // The host deadline must NOT terminate the durable operation.
        op.Completion.IsCompleted.Should().BeFalse();

        drainGate.TrySetResult(Drained()); // late drain, after the host deadline
        (await op.Completion).IsFullyProven.Should().BeTrue(); // still resolves Proven
    }
}
