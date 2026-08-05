// ============================================================================
// File: Generation/SourceSlotTests.cs
// Purpose: Pin the stable-slot model: two-phase prepare/activate (id consumed at
//          prepare, single atomic activation, abandon is terminal), stale/abandon
//          rejection, ordinary retirement (revoke+detach, channel NOT completed),
//          permanent removal (terminal, channel completed), and the
//          reference-stable intake across generation swaps (the structural M1 fix).
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §0, §1, §6, §10.
// ============================================================================

using System;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class SourceSlotTests
{
    private static SourceSlot NewSlot(string id = "src-1", int capacity = 4) =>
        new(RuntimeInstanceId.New(), id, new SourceGenerationAllocator(), capacity);

    private static SourceGeneration Activate(SourceSlot slot)
    {
        var prepared = slot.PrepareGeneration();
        prepared.IsPrepared.Should().BeTrue();
        var result = slot.TryActivate(prepared.Prepared!);
        result.IsActivated.Should().BeTrue();
        return result.Generation!;
    }

    private static CanonicalDataPoint MakePoint()
    {
        var factory = new CanonicalDataPointFactory(
            gatewayId: "gw",
            sourceInstanceId: "src-1",
            protocolName: "test",
            deviceId: "dev",
            deviceName: "Dev",
            deviceClass: "test");
        return factory.CreatePoint(
            tagName: "t",
            tagPath: "p",
            value: 1,
            valueType: CanonicalValueType.Integer,
            quality: DataQuality.Good,
            deviceTimestamp: DateTime.UtcNow,
            gatewayTimestamp: DateTime.UtcNow);
    }

    // ── Two-phase activation ────────────────────────────────────────

    [Fact]
    public void TryActivate_AuthorizesGeneration_NumberedFromOne()
    {
        var slot = NewSlot();

        var generation = Activate(slot);

        generation.Key.GenerationId.Value.Should().Be(1UL);
        slot.CurrentGeneration.Should().NotBeNull();
        slot.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void TryActivate_InstallsCurrentAndAuthorityAtomically_NoMixedState()
    {
        var slot = NewSlot();
        var prepared = slot.PrepareGeneration().Prepared!;

        // Prepared but not activated: not current, not authorized.
        slot.CurrentGeneration.Should().BeNull();
        slot.IsPublishAuthorized.Should().BeFalse();

        var generation = slot.TryActivate(prepared).Generation!;

        slot.CurrentGeneration.Should().BeSameAs(generation);
        slot.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void PrepareGeneration_ConsumesId_EvenWhenAbandoned()
    {
        var slot = NewSlot();

        var first = slot.PrepareGeneration();
        first.Prepared!.Key.GenerationId.Value.Should().Be(1UL);
        slot.AbandonPrepared(first.Prepared!, RetirementReason.ActivationRollback)
            .Should().Be(GenerationAbandonOutcome.Ok);

        // The failed candidate still consumed the id; the next prepare is 2.
        slot.PrepareGeneration().Prepared!.Key.GenerationId.Value.Should().Be(2UL);
    }

    [Fact]
    public void AbandonedPrepared_CanNeverActivate()
    {
        var slot = NewSlot();
        var prepared = slot.PrepareGeneration().Prepared!;
        slot.AbandonPrepared(prepared, RetirementReason.ActivationRollback);

        var result = slot.TryActivate(prepared);

        result.Outcome.Should().Be(ActivateGenerationOutcome.AuthorizationFailed);
        result.AuthorizationOutcome.Should().Be(GenerationAuthorizationOutcome.AlreadyRetired);
    }

    [Fact]
    public void StalePrepared_CannotActivate_AfterNewerBecameCurrent()
    {
        var slot = NewSlot();
        var older = slot.PrepareGeneration().Prepared!; // id 1
        var newer = slot.PrepareGeneration().Prepared!; // id 2
        slot.TryActivate(newer).IsActivated.Should().BeTrue();

        var stale = slot.TryActivate(older);

        stale.IsActivated.Should().BeFalse();
        stale.AuthorizationOutcome.Should().Be(GenerationAuthorizationOutcome.StaleGeneration);
    }

    // ── Ordinary retirement vs permanent removal ────────────────────

    [Fact]
    public void RetireCurrent_RevokesAuthority_AndDetaches_WithoutCompletingChannel()
    {
        var slot = NewSlot();
        var generation = Activate(slot);

        slot.RetireCurrent(RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.Ok);

        slot.CurrentGeneration.Should().BeNull();
        slot.IsPublishAuthorized.Should().BeFalse();
        slot.IsTerminal.Should().BeFalse();
        generation.AuthorityState.Should().Be(AuthorityState.Retired);
        Activate(slot); // a fresh generation can still be activated
    }

    [Fact]
    public void PermanentRemoval_IsTerminal_RejectsPrepareAndActivate()
    {
        var slot = NewSlot();
        var prepared = slot.PrepareGeneration().Prepared!; // prepared before removal

        slot.CompleteIntakeForPermanentRemoval().Should().BeTrue();

        slot.IsTerminal.Should().BeTrue();
        slot.PrepareGeneration().Outcome.Should().Be(PrepareGenerationOutcome.SlotTerminal);
        slot.TryActivate(prepared).Outcome.Should().Be(ActivateGenerationOutcome.SlotTerminal);
    }

    [Fact]
    public void PermanentRemoval_IsIdempotent()
    {
        var slot = NewSlot();

        slot.CompleteIntakeForPermanentRemoval().Should().BeTrue();
        slot.CompleteIntakeForPermanentRemoval().Should().BeFalse(); // harmless
        slot.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void PermanentRemoval_CompletesChannel_UnlikeOrdinaryRetirement()
    {
        var retired = NewSlot();
        Activate(retired);
        retired.RetireCurrent(RetirementReason.Stop);
        retired.Intake.Reader.Completion.IsCompleted.Should().BeFalse();

        var removed = NewSlot();
        Activate(removed);
        removed.CompleteIntakeForPermanentRemoval();
        removed.Intake.Reader.Completion.IsCompleted.Should().BeTrue();
    }

    // ── Stable intake (the M1 property) ─────────────────────────────

    [Fact]
    public void Intake_IsReferenceStable_AcrossGenerationSwaps()
    {
        var slot = NewSlot();
        var intakeBefore = slot.Intake;

        Activate(slot);
        slot.RetireCurrent(RetirementReason.Reconfigure);
        var second = Activate(slot);

        second.Key.GenerationId.Value.Should().Be(2UL);
        slot.Intake.Should().BeSameAs(intakeBefore);
        slot.Intake.Reader.Should().BeSameAs(intakeBefore.Reader);
    }

    [Fact]
    public async Task PointWrittenByCurrentGeneration_DrainsFromStableIntake()
    {
        var slot = NewSlot();
        var generation = Activate(slot);
        var point = MakePoint();

        (await generation.Writer.WriteAsync(point, default)).Should().Be(IntakeWriteOutcome.Committed);

        slot.Intake.Reader.TryRead(out var read).Should().BeTrue();
        read.Should().BeSameAs(point);
    }
}
