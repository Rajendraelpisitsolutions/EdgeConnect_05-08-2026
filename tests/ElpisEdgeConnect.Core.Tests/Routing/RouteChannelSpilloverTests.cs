// ============================================================================
// File: Routing/RouteChannelSpilloverTests.cs
// Purpose: Phase 6 pin tests for RouteChannelSpillover. Covers spill-to-
//          buffer execution, ordering preservation across spill writes,
//          empty batch handling, and counter accuracy.
// Milestone: C3 Commit 2 (phase 6).
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class RouteChannelSpilloverTests
{
    private static InMemoryBuffer MakeBuffer()
        => new("test-buffer", new BufferPolicy
        {
            Mode = BufferMode.InMemory,
            MaxDepth = 1000,
            DropPolicy = DropPolicy.DropOldest,
        });

    private static IReadOnlyList<CanonicalDataPoint> MakeBatch(int first, int count)
        => Enumerable.Range(first, count)
            .Select(i => RoutingTestData.MakePoint(i))
            .ToArray();

    [Fact]
    public async Task Spillover_WritesBatchToBuffer()
    {
        await using var buffer = MakeBuffer();
        await buffer.RegisterSinkAsync("sink-1", CancellationToken.None);
        var spillover = new RouteChannelSpillover(buffer);

        await spillover.SpillAsync(MakeBatch(0, 10), CancellationToken.None);

        spillover.SpilledPointCount.Should().Be(10);
        spillover.SpillEventCount.Should().Be(1);

        var batch = await buffer.DequeueBatchAsync("sink-1", 100, CancellationToken.None);
        batch.Points.Should().HaveCount(10);
    }

    [Fact]
    public async Task Spillover_PreservesOrdering()
    {
        await using var buffer = MakeBuffer();
        await buffer.RegisterSinkAsync("sink-1", CancellationToken.None);
        var spillover = new RouteChannelSpillover(buffer);

        // Three spills in sequence must produce an in-order merged stream.
        await spillover.SpillAsync(MakeBatch(0, 5), CancellationToken.None);
        await spillover.SpillAsync(MakeBatch(5, 5), CancellationToken.None);
        await spillover.SpillAsync(MakeBatch(10, 5), CancellationToken.None);

        spillover.SpilledPointCount.Should().Be(15);
        spillover.SpillEventCount.Should().Be(3);

        var batch = await buffer.DequeueBatchAsync("sink-1", 100, CancellationToken.None);
        batch.Points.Select(p => p.SequenceNumber)
            .Should().Equal(Enumerable.Range(0, 15).Select(i => (long)i));
    }

    [Fact]
    public async Task Spillover_EmptyBatch_IsNoOp()
    {
        await using var buffer = MakeBuffer();
        await buffer.RegisterSinkAsync("sink-1", CancellationToken.None);
        var spillover = new RouteChannelSpillover(buffer);

        await spillover.SpillAsync(System.Array.Empty<CanonicalDataPoint>(), CancellationToken.None);

        spillover.SpilledPointCount.Should().Be(0);
        spillover.SpillEventCount.Should().Be(0);
        var batch = await buffer.DequeueBatchAsync("sink-1", 100, CancellationToken.None);
        batch.IsEmpty.Should().BeTrue();
    }
}
