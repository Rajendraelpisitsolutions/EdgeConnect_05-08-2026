// ============================================================================
// File: Generation/GenerationRetirementCompletionTests.cs
// Purpose: Pin the composite quiescence contract with EXPLICIT applicability
//          (review C2-3): an omitted/not-applicable component never silently
//          counts as proof; a never-proven applicable component cannot yield
//          Proven; Active dominates Unproven dominates Proven; setting a
//          not-applicable component throws.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §3.
// ============================================================================

using System;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class GenerationRetirementCompletionTests
{
    private static GenerationRetirementCompletion PollGeneration() =>
        new(QuiescenceComponents.Pump | QuiescenceComponents.AdapterStop);

    [Fact]
    public void Evidence_DefaultsToActive_ForApplicableComponents()
    {
        PollGeneration().Evidence.Should().Be(QuiescenceEvidence.Active);
    }

    [Fact]
    public void Evidence_AllApplicableProven_IsProven_AndNotApplicableDoesNotBlock()
    {
        var completion = PollGeneration(); // callback-drain is NotApplicable
        completion.SetPump(QuiescenceComponentState.Proven);
        completion.SetAdapterStop(QuiescenceComponentState.Proven);

        completion.Evidence.Should().Be(QuiescenceEvidence.Proven);
    }

    [Fact]
    public void Evidence_OmittedRequiredComponent_CannotProduceProven()
    {
        var completion = PollGeneration();
        completion.SetPump(QuiescenceComponentState.Proven);
        // adapter-stop never proven → stays Active → never Proven

        completion.Evidence.Should().Be(QuiescenceEvidence.Active);
    }

    [Fact]
    public void Evidence_ApplicableUnproven_IsUnproven()
    {
        var completion = PollGeneration();
        completion.SetPump(QuiescenceComponentState.Proven);
        completion.SetAdapterStop(QuiescenceComponentState.Unproven);

        completion.Evidence.Should().Be(QuiescenceEvidence.Unproven);
    }

    [Fact]
    public void Evidence_ActiveDominatesUnproven()
    {
        var completion = PollGeneration();
        completion.SetPump(QuiescenceComponentState.Unproven);
        completion.SetAdapterStop(QuiescenceComponentState.Active);

        completion.Evidence.Should().Be(QuiescenceEvidence.Active);
    }

    [Fact]
    public void OnlyApplicableComponent_Proven_YieldsProven()
    {
        var completion = new GenerationRetirementCompletion(QuiescenceComponents.Pump);
        completion.SetPump(QuiescenceComponentState.Proven);

        completion.Evidence.Should().Be(QuiescenceEvidence.Proven);
    }

    [Fact]
    public void Setting_ANotApplicableComponent_Throws()
    {
        var completion = new GenerationRetirementCompletion(QuiescenceComponents.Pump);

        var act = () => completion.SetCallbackDrain(QuiescenceComponentState.Proven);

        act.Should().Throw<InvalidOperationException>();
    }
}
