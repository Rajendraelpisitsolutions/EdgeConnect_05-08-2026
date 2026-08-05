// ============================================================================
// File: Adapters/Retirement/AdapterQuiescenceAttestationTests.cs
// Purpose: Pin the IsFullyProven contract: proven only when no surface is
//          Unproven (every applicable surface terminated; the rest NotApplicable).
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4.
// ============================================================================

using ElpisEdgeConnect.Core.Adapters.Retirement;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters.Retirement;

public sealed class AdapterQuiescenceAttestationTests
{
    private static AdapterQuiescenceAttestation Make(
        AdapterSurfaceState worker,
        AdapterSurfaceState callbackDrain,
        AdapterSurfaceState backgroundWork)
        => new()
        {
            Worker = worker,
            CallbackDrain = callbackDrain,
            BackgroundWork = backgroundWork,
        };

    [Fact]
    public void IsFullyProven_AllProvenOrNotApplicable_IsTrue()
    {
        Make(AdapterSurfaceState.Proven, AdapterSurfaceState.NotApplicable, AdapterSurfaceState.NotApplicable)
            .IsFullyProven.Should().BeTrue();
    }

    [Fact]
    public void IsFullyProven_AnyUnproven_IsFalse()
    {
        Make(AdapterSurfaceState.Proven, AdapterSurfaceState.Unproven, AdapterSurfaceState.NotApplicable)
            .IsFullyProven.Should().BeFalse();
    }

    [Fact]
    public void IsFullyProven_WorkerUnproven_IsFalse()
    {
        Make(AdapterSurfaceState.Unproven, AdapterSurfaceState.NotApplicable, AdapterSurfaceState.NotApplicable)
            .IsFullyProven.Should().BeFalse();
    }
}
