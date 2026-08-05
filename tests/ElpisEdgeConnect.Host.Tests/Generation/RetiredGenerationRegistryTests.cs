// ============================================================================
// File: Generation/RetiredGenerationRegistryTests.cs
// Purpose: Pin orphan accounting: quarantine adds active + cumulative; orphan
//          counts cumulatively while staying active; proven completion
//          decrements active but never erases the lifetime totals.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §3, §8.
// ============================================================================

using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class RetiredGenerationRegistryTests
{
    private static GenerationKey Key(ulong id) =>
        new(RuntimeInstanceId.New(), "src-1", new GenerationId(id));

    [Fact]
    public void Quarantine_AddsActive_AndCountsCumulative()
    {
        var registry = new RetiredGenerationRegistry();

        registry.Quarantine(Key(1));

        registry.ActiveCount.Should().Be(1);
        registry.CumulativeQuarantineTotal.Should().Be(1);
    }

    [Fact]
    public void Quarantine_SameKeyTwice_IsIdempotentForActiveAndCumulative()
    {
        var registry = new RetiredGenerationRegistry();
        var key = Key(1);

        registry.Quarantine(key);
        registry.Quarantine(key);

        registry.ActiveCount.Should().Be(1);
        registry.CumulativeQuarantineTotal.Should().Be(1);
    }

    [Fact]
    public void MarkOrphaned_CountsCumulativeOrphan_KeepsActive()
    {
        var registry = new RetiredGenerationRegistry();
        var key = Key(1);

        registry.Quarantine(key);
        registry.MarkOrphaned(key);

        registry.ActiveCount.Should().Be(1);
        registry.CumulativeOrphanTotal.Should().Be(1);
    }

    [Fact]
    public void MarkCompleted_DecrementsActive_PreservesCumulativeTotals()
    {
        var registry = new RetiredGenerationRegistry();
        var key = Key(1);
        registry.Quarantine(key);
        registry.MarkOrphaned(key);

        registry.MarkCompleted(key).Should().BeTrue();

        registry.ActiveCount.Should().Be(0);
        registry.CumulativeQuarantineTotal.Should().Be(1);
        registry.CumulativeOrphanTotal.Should().Be(1);
    }
}
