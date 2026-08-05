// ============================================================================
// File: Retirement/Focas2SourceRetirementTests.cs
// Purpose: Pin the FOCAS2 adapter retirement capability: Focas2SourceAdapter
//          implements ISourceRetirement; BeginRetirement is idempotent; a
//          constructed (uninitialized) adapter has no FOCAS thread -> Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §4.
// ============================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests.Retirement;

public sealed class Focas2SourceRetirementTests
{
    private static Focas2SourceAdapter NewAdapter() =>
        new("a", new FakeFocas2Api(), NullLogger.Instance);

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

        var first = retire.BeginRetirement(Ctx());
        var second = retire.BeginRetirement(Ctx());

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task BeginRetirement_OnConstructedAdapter_ResolvesProven_NothingToQuiesce()
    {
        // Constructed but not initialized → no FOCAS thread → nothing to quiesce.
        var op = ((ISourceRetirement)NewAdapter()).BeginRetirement(Ctx());

        (await op.Completion).IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public async Task BeginRetirement_ConcurrentCalls_ReturnTheSameOperation()
    {
        var retire = (ISourceRetirement)NewAdapter();

        var ops = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => retire.BeginRetirement(Ctx()))));

        ops.Distinct().Should().HaveCount(1); // FOCAS-G1: one durable operation
    }
}
