// ============================================================================
// File: Routing/ReplayCoordinatorTests.cs
// Purpose: Unit pin tests for the tiny ReplayCoordinator helper.
// Milestone: C3 Commit 2 (phase 4).
// ============================================================================

using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class ReplayCoordinatorTests
{
    [Fact]
    public void NewCoordinator_IsNotDraining()
    {
        var c = new ReplayCoordinator();
        c.IsDraining.Should().BeFalse();
    }

    [Fact]
    public void BeginDrain_SetsIsDraining()
    {
        var c = new ReplayCoordinator();
        c.BeginDrain();
        c.IsDraining.Should().BeTrue();
    }

    [Fact]
    public void TryCompleteDrain_ReturnsTrueOnlyWhenDraining()
    {
        var c = new ReplayCoordinator();
        c.TryCompleteDrain().Should().BeFalse();

        c.BeginDrain();
        c.TryCompleteDrain().Should().BeTrue();
        c.IsDraining.Should().BeFalse();

        // Idempotent second call.
        c.TryCompleteDrain().Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsDraining()
    {
        var c = new ReplayCoordinator();
        c.BeginDrain();
        c.Reset();
        c.IsDraining.Should().BeFalse();
    }
}
