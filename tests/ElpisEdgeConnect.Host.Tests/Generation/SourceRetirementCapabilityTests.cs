// ============================================================================
// File: Generation/SourceRetirementCapabilityTests.cs
// Purpose: Pin the FAIL-CLOSED retirement-capability discovery (F1/§9a gate 5):
//          an adapter that implements ISourceRetirement is discovered and its
//          capability returned; an adapter that does NOT implement it fails closed
//          with SourceLifecycleBlockReason.RetirementCapabilityUnsupported — never
//          treated as quiescent by method completion.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §3, §5, §9a.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Adapters.Retirement;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class SourceRetirementCapabilityTests
{
    [Fact]
    public void TryGet_AdapterImplementsISourceRetirement_ReturnsTrueAndCapability()
    {
        var adapter = Substitute.For<ISourceAdapter, ISourceRetirement>();

        var found = SourceRetirementCapability.TryGet(adapter, out var retirement);

        found.Should().BeTrue();
        retirement.Should().BeSameAs(adapter);
    }

    [Fact]
    public void TryGet_AdapterWithoutCapability_FailsClosed()
    {
        var adapter = Substitute.For<ISourceAdapter>();

        var found = SourceRetirementCapability.TryGet(adapter, out var retirement);

        found.Should().BeFalse();
        retirement.Should().BeNull();
    }

    [Fact]
    public void UnsupportedReason_IsRetirementCapabilityUnsupported()
    {
        SourceRetirementCapability.UnsupportedReason
            .Should().Be(SourceLifecycleBlockReason.RetirementCapabilityUnsupported);
    }
}
