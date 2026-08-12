// ============================================================================
// File: Retirement/ModbusTcpSourceRetirementTests.cs
// Purpose: Pin the adapter-level retirement capability: ModbusTcpSourceAdapter
//          implements ISourceRetirement; BeginRetirement is idempotent; a
//          constructed (uninitialized) adapter has nothing to quiesce -> Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §4.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Retirement;

public sealed class ModbusTcpSourceRetirementTests
{
    private static ModbusTcpSourceAdapter NewAdapter() =>
        new("a", new FakeModbusClient(), NullLogger.Instance);

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
        // Constructed but not initialized → no connection manager → nothing in flight.
        var op = ((ISourceRetirement)NewAdapter()).BeginRetirement(Ctx());

        (await op.Completion).IsFullyProven.Should().BeTrue();
    }
}
