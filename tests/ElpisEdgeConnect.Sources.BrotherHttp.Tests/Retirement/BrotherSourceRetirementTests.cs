// ============================================================================
// Tests: BrotherSourceRetirementTests — pin the Brother HTTP adapter retirement
//        capability: implements ISourceRetirement; BeginRetirement is idempotent;
//        a constructed adapter (no in-flight poll) resolves fully Proven; once
//        retirement begins, PollAsync refuses new polls (returns empty).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §4.
// ============================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests.Retirement;

public sealed class BrotherSourceRetirementTests
{
    private static BrotherHttpSourceAdapter NewAdapter() =>
        new("brother-r", new StubBrotherApi(), NullLogger.Instance, gatewayIdentity: null);

    private static AdapterRetirementContext Ctx() =>
        new() { ObservationToken = CancellationToken.None };

    [Fact]
    public void Adapter_ImplementsISourceRetirement_Capability()
    {
        (NewAdapter() is ISourceRetirement).Should().BeTrue();
    }

    [Fact]
    public void BeginRetirement_IsIdempotent_ReturnsSameOperation()
    {
        var retire = (ISourceRetirement)NewAdapter();

        retire.BeginRetirement(Ctx()).Should().BeSameAs(retire.BeginRetirement(Ctx()));
    }

    [Fact]
    public async Task BeginRetirement_OnConstructedAdapter_ResolvesProven_NoPollInFlight()
    {
        var op = ((ISourceRetirement)NewAdapter()).BeginRetirement(Ctx());

        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task PollAsync_AfterRetirementBegins_RefusesNewPoll_ReturnsEmpty()
    {
        var adapter = NewAdapter();
        ((ISourceRetirement)adapter).BeginRetirement(Ctx());

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().BeEmpty();
    }

    [Fact]
    public async Task PollAsync_WhileNotRetiring_ReachesPollBody_GateIsBehaviorNeutral()
    {
        // Behaviour-neutral live poll-path smoke: while not retiring the gate
        // admits the call straight into the real body (here: the not-initialised
        // guard throws). Contrast with the retiring case above, which short-circuits
        // to empty. Together these prove the gate wrapper changes nothing on the
        // live poll path until retirement begins.
        var adapter = NewAdapter(); // constructed, not initialised, not retiring

        var act = async () => await adapter.PollAsync(CancellationToken.None);

        await act.Should().ThrowAsync<System.InvalidOperationException>();
    }

    [Fact]
    public async Task PollThenRetire_ResolvesProven_GateReleasesEvenWhenPollThrows()
    {
        // The poll gate's finally must release the in-flight count even when the
        // poll body throws (here: not-initialised). Otherwise retirement would wedge.
        var adapter = NewAdapter();
        try { await adapter.PollAsync(CancellationToken.None); }
        catch (System.InvalidOperationException) { /* uninitialised body threw */ }

        var op = ((ISourceRetirement)adapter).BeginRetirement(Ctx());

        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task BeginRetirement_ConcurrentCalls_ReturnTheSameOperation()
    {
        var retire = (ISourceRetirement)NewAdapter();

        var ops = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => retire.BeginRetirement(Ctx()))));

        ops.Distinct().Should().HaveCount(1);
    }
}
