// ============================================================================
// File: Generation/SourceGenerationAllocatorTests.cs
// Purpose: Pin the runtime-lifetime generation allocator: per-slot monotonic
//          ids, independence across slots, tombstone survival across remove/
//          re-add, and fail-closed overflow (review B4).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §4.
// ============================================================================

using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class SourceGenerationAllocatorTests
{
    [Fact]
    public void TryAllocateNext_ProducesMonotonicIds_PerSlot()
    {
        var allocator = new SourceGenerationAllocator();

        allocator.TryAllocateNext("a", out var first).Should().BeTrue();
        allocator.TryAllocateNext("a", out var second).Should().BeTrue();
        allocator.TryAllocateNext("a", out var third).Should().BeTrue();

        first.Value.Should().Be(1UL);
        second.Value.Should().Be(2UL);
        third.Value.Should().Be(3UL);
    }

    [Fact]
    public void TryAllocateNext_DifferentSlots_AreIndependent()
    {
        var allocator = new SourceGenerationAllocator();

        allocator.TryAllocateNext("a", out var a1);
        allocator.TryAllocateNext("b", out var b1);

        a1.Value.Should().Be(1UL);
        b1.Value.Should().Be(1UL);
    }

    [Fact]
    public void TryAllocateNext_AfterRemoveAndReAdd_DoesNotReuseAGenerationId()
    {
        var allocator = new SourceGenerationAllocator();

        // Live span 1: two generations for the slot.
        allocator.TryAllocateNext("a", out _);
        allocator.TryAllocateNext("a", out var beforeRemoval);

        // The slot is "removed" at the supervisor layer; the allocator's
        // high-water mark is a runtime-lifetime tombstone that survives.
        // A re-add allocates the NEXT id, never reusing a prior key.
        allocator.TryAllocateNext("a", out var afterReAdd).Should().BeTrue();

        beforeRemoval.Value.Should().Be(2UL);
        afterReAdd.Value.Should().Be(3UL);
    }

    [Fact]
    public void TryAllocateNext_AtCounterCeiling_FailsClosed()
    {
        var allocator = new SourceGenerationAllocator();
        allocator.SeedHighWaterMarkForTesting("a", ulong.MaxValue);

        allocator.TryAllocateNext("a", out var id).Should().BeFalse();
        id.Should().Be(GenerationId.None);
    }

    [Fact]
    public void TryAllocateNext_OneBelowCeiling_AllocatesThenFailsClosed()
    {
        var allocator = new SourceGenerationAllocator();
        allocator.SeedHighWaterMarkForTesting("a", ulong.MaxValue - 1UL);

        allocator.TryAllocateNext("a", out var last).Should().BeTrue();
        last.Value.Should().Be(ulong.MaxValue);

        allocator.TryAllocateNext("a", out _).Should().BeFalse();
    }
}
