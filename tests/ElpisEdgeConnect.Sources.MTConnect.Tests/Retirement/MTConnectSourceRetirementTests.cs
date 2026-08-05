// ============================================================================
// Tests: MTConnectSourceRetirementTests — pin the MTConnect adapter retirement
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

namespace ElpisEdgeConnect.Sources.MTConnect.Tests.Retirement;

public sealed class MTConnectSourceRetirementTests
{
    private static MTConnectSourceAdapter NewAdapter() =>
        new("mtc-r", NullLogger<MTConnectSourceAdapter>.Instance,
            gatewayIdentity: null, clientFactory: _ => new FakeMTConnectClient());

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
    public async Task PollThenRetire_ResolvesProven_GateEnterExitPairedOnLivePath()
    {
        // Behaviour-neutral live poll-path smoke: a normal poll while not retiring
        // enters AND exits the gate (the in-flight count returns to zero). Proven
        // here is only possible if ExitPoll ran on the live path — if the gate
        // leaked an in-flight count, BeginRetirement would stay pending.
        var adapter = NewAdapter();
        await adapter.PollAsync(CancellationToken.None); // live poll: enter → body → exit

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
