// ============================================================================
// Tests: OpcUaSourceRetirementTests — pin the OPC UA adapter retirement
//        capability: OpcUaClientSourceAdapter implements ISourceRetirement;
//        BeginRetirement is idempotent (same durable operation under
//        concurrency); a constructed (uninitialized) adapter has no dispatcher /
//        coordinator / subscriptions → nothing to quiesce → fully Proven.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §4.
// ============================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests.Retirement;

public sealed class OpcUaSourceRetirementTests
{
    private static OpcUaClientSourceAdapter NewAdapter() =>
        new("opcua-a", NullLogger.Instance);

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
        // Constructed but not initialized → no dispatcher/coordinator/subscriptions.
        var op = ((ISourceRetirement)NewAdapter()).BeginRetirement(Ctx());

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
