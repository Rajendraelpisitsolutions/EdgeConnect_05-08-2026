// ============================================================================
// Tests: PullAdapterRetirementTests — pin the pull-adapter retirement builder:
//          * snapshot marks only Worker applicable (callback/background NotApplicable);
//          * with no poll in-flight, Completion resolves fully Proven;
//          * with a poll in-flight, Completion stays PENDING until the poll
//            drains — a wedged poll is never falsely Proven (durable).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters.Retirement;

public sealed class PullAdapterRetirementTests
{
    private static readonly AdapterRetirementDetailCode Initiated = new("TEST.RETIRE_INITIATED");
    private static readonly AdapterRetirementDetailCode Proven = new("TEST.RETIRE_POLL_IDLE");

    private static AdapterRetirementContext Ctx() =>
        new() { ObservationToken = CancellationToken.None };

    [Fact]
    public void Begin_Snapshot_MarksOnlyWorkerApplicable()
    {
        var op = PullAdapterRetirement.Begin(new PollQuiescenceGate(), Initiated, Proven, Ctx());

        op.Snapshot.WorkerApplicable.Should().BeTrue();
        op.Snapshot.CallbackDrainApplicable.Should().BeFalse();
        op.Snapshot.BackgroundWorkApplicable.Should().BeFalse();
    }

    [Fact]
    public async Task Begin_WithNoPollInFlight_ResolvesFullyProven()
    {
        var op = PullAdapterRetirement.Begin(new PollQuiescenceGate(), Initiated, Proven, Ctx());

        var attestation = await op.Completion;

        attestation.IsFullyProven.Should().BeTrue();
        attestation.Worker.Should().Be(AdapterSurfaceState.Proven);
        attestation.CallbackDrain.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.BackgroundWork.Should().Be(AdapterSurfaceState.NotApplicable);
        attestation.DetailCode.Should().Be(Proven);
    }

    [Fact]
    public async Task Begin_WithPollInFlight_CompletionPendingUntilDrain()
    {
        var gate = new PollQuiescenceGate();
        gate.TryEnterPoll().Should().BeTrue(); // a poll is wedged in-flight

        var op = PullAdapterRetirement.Begin(gate, Initiated, Proven, Ctx());
        op.Completion.IsCompleted.Should().BeFalse(); // not proven while a poll runs

        gate.ExitPoll();
        (await op.Completion).IsFullyProven.Should().BeTrue();
    }
}
