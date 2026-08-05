// ============================================================================
// File: Retirement/S7SourceRetirementTests.cs
// Purpose: Pin the S7 adapter retirement capability: S7SourceAdapter implements
//          ISourceRetirement; BeginRetirement is idempotent; a constructed
//          (uninitialized) adapter has nothing to quiesce -> Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §4.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Retirement;

public sealed class S7SourceRetirementTests
{
    private static S7SourceAdapter NewAdapter() =>
        new("a", new S7DemoClient(), NullLogger.Instance);

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
        var op = ((ISourceRetirement)NewAdapter()).BeginRetirement(Ctx());

        (await op.Completion).IsFullyProven.Should().BeTrue();
    }
}
