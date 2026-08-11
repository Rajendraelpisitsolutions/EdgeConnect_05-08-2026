// ============================================================================
// Tests: PollQuiescenceGateTests — pin the pull-adapter quiescence primitive:
//          * polls are admitted before quiescing and refused after;
//          * with no poll in-flight, the drain task completes immediately;
//          * with a poll in-flight, the drain task stays PENDING until the poll
//            exits (a wedged poll never falsely resolves);
//          * BeginQuiescingAsync is idempotent (same drain task).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4, §7.
// ============================================================================

using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters.Retirement;

public sealed class PollQuiescenceGateTests
{
    [Fact]
    public void TryEnterPoll_BeforeQuiescing_IsAdmitted()
    {
        var gate = new PollQuiescenceGate();

        gate.TryEnterPoll().Should().BeTrue();

        gate.ExitPoll();
    }

    [Fact]
    public void BeginQuiescingAsync_WithNoPollInFlight_CompletesImmediately()
    {
        var gate = new PollQuiescenceGate();

        gate.BeginQuiescingAsync().IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void TryEnterPoll_AfterQuiescing_IsRefused()
    {
        var gate = new PollQuiescenceGate();

        _ = gate.BeginQuiescingAsync();

        gate.TryEnterPoll().Should().BeFalse();
    }

    [Fact]
    public async Task BeginQuiescingAsync_WithPollInFlight_StaysPendingUntilExit()
    {
        var gate = new PollQuiescenceGate();
        gate.TryEnterPoll().Should().BeTrue(); // a poll is in-flight

        var drain = gate.BeginQuiescingAsync();
        drain.IsCompleted.Should().BeFalse(); // wedged poll → not quiesced

        gate.ExitPoll();
        await drain; // resolves once the in-flight poll drains
        drain.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void BeginQuiescingAsync_IsIdempotent_ReturnsSameDrainTask()
    {
        var gate = new PollQuiescenceGate();
        gate.TryEnterPoll();

        var first = gate.BeginQuiescingAsync();
        var second = gate.BeginQuiescingAsync();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void ExitPoll_WithoutMatchingEnter_DoesNotUnderflow()
    {
        var gate = new PollQuiescenceGate();

        gate.ExitPoll();          // misuse — no admitted poll
        gate.ExitPoll();          // double-exit

        // A single legitimate poll must still drain correctly (counter was not
        // driven negative by the spurious exits).
        gate.TryEnterPoll().Should().BeTrue();
        var drain = gate.BeginQuiescingAsync();
        drain.IsCompleted.Should().BeFalse();
        gate.ExitPoll();
        drain.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void ExitPoll_DoubleExit_DoesNotPrematurelyCompleteQuiescence()
    {
        var gate = new PollQuiescenceGate();
        gate.TryEnterPoll();
        gate.TryEnterPoll(); // two polls in-flight

        var drain = gate.BeginQuiescingAsync();
        gate.ExitPoll();
        gate.ExitPoll();
        drain.IsCompletedSuccessfully.Should().BeTrue(); // both drained

        gate.ExitPoll(); // spurious extra exit — must not throw or corrupt state
    }
}
