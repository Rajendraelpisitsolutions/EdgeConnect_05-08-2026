// ============================================================================
// File: Generation/SourceSlotGateTests.cs
// Purpose: Pin the Slice 0 gate foundation: one-shot leases, gate-linearized
//          authorize/retire/commit, structured outcomes, expected-lease
//          retirement (N can't retire N+1), commit fencing + re-entrancy guard.
// Reference: docs/sessions/2026-06-25-slice-0-implementation-plan-v2.md §10
//            (commit-1 gate tests).
// ============================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Generation;
using ElpisEdgeConnect.Host.Generation;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests.Generation;

public sealed class SourceSlotGateTests
{
    private const string Slot = "src-1";

    private static SourceSlotGate NewGate(
        string slot = Slot,
        SourceGenerationAllocator? allocator = null,
        RuntimeInstanceId? runtime = null)
        => new(runtime ?? RuntimeInstanceId.New(), slot, allocator ?? new SourceGenerationAllocator());

    private static GenerationLease Issue(SourceSlotGate gate)
    {
        var result = gate.IssueLease();
        result.IsOk.Should().BeTrue();
        return result.Lease!;
    }

    // ── Lease minting ───────────────────────────────────────────────

    [Fact]
    public void IssueLease_AllocatesMonotonicKeys_ForSameSlot()
    {
        var gate = NewGate();

        var first = Issue(gate);
        var second = Issue(gate);

        first.Key.SourceSlotId.Should().Be(Slot);
        first.Key.GenerationId.Value.Should().Be(1UL);
        second.Key.GenerationId.Value.Should().Be(2UL);
    }

    // ── Authorization ───────────────────────────────────────────────

    [Fact]
    public void TryAuthorize_FirstLease_BecomesCurrent()
    {
        var gate = NewGate();
        var lease = Issue(gate);

        gate.TryAuthorize(lease).Should().Be(GenerationAuthorizationOutcome.Ok);
        gate.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void TryAuthorize_SameLeaseTwice_IsRejectedAsAlreadyAuthorized()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);

        gate.TryAuthorize(lease).Should().Be(GenerationAuthorizationOutcome.AlreadyAuthorized);
    }

    [Fact]
    public void TryAuthorize_SecondLeaseWhileOneCurrent_IsRejectedAsConflict()
    {
        var gate = NewGate();
        var first = Issue(gate);
        var second = Issue(gate);
        gate.TryAuthorize(first);

        gate.TryAuthorize(second).Should().Be(GenerationAuthorizationOutcome.AuthorizationConflict);
    }

    [Fact]
    public void TryAuthorize_AfterRetiringCurrent_AdmitsNewGeneration()
    {
        var gate = NewGate();
        var first = Issue(gate);
        var second = Issue(gate);
        gate.TryAuthorize(first);
        gate.TryRetire(first, RetirementReason.Reconfigure);

        gate.TryAuthorize(second).Should().Be(GenerationAuthorizationOutcome.Ok);
        gate.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void TryAuthorize_RetiredLease_CanNeverBeReauthorized()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);
        gate.TryRetire(lease, RetirementReason.Stop);

        gate.TryAuthorize(lease).Should().Be(GenerationAuthorizationOutcome.AlreadyRetired);
    }

    [Fact]
    public void TryAuthorize_LeaseFromAnotherGate_IsRejectedAsWrongGate()
    {
        var gateA = NewGate();
        var gateB = NewGate();
        var leaseFromA = Issue(gateA);

        gateB.TryAuthorize(leaseFromA).Should().Be(GenerationAuthorizationOutcome.WrongGate);
    }

    [Fact]
    public void TryAuthorize_KeyWithMismatchedRuntimeIdentity_IsRejected()
    {
        var gate = NewGate(runtime: RuntimeInstanceId.New());
        // Construct a lease whose gate is correct but whose key carries a
        // foreign runtime identity (internal ctor via InternalsVisibleTo).
        var foreignKey = new GenerationKey(RuntimeInstanceId.New(), Slot, new GenerationId(1UL));
        var forged = new GenerationLease(gate, foreignKey);

        gate.TryAuthorize(forged).Should().Be(GenerationAuthorizationOutcome.IdentityMismatch);
    }

    // ── Retirement ──────────────────────────────────────────────────

    [Fact]
    public void TryRetire_NonCurrentLease_IsRejectedAsNotCurrent()
    {
        var gate = NewGate();
        var current = Issue(gate);
        var other = Issue(gate);
        gate.TryAuthorize(current);

        gate.TryRetire(other, RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.NotCurrent);
    }

    [Fact]
    public void TryRetire_LateStopForN_CannotRetireSuccessorNPlus1()
    {
        var gate = NewGate();
        var n = Issue(gate);
        gate.TryAuthorize(n);
        gate.TryRetire(n, RetirementReason.Reconfigure);

        var nPlus1 = Issue(gate);
        gate.TryAuthorize(nPlus1);

        // A late stop for the already-retired N must not disturb N+1.
        gate.TryRetire(n, RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.AlreadyRetired);
        gate.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void TryRetire_IsIdempotent_OnAlreadyRetiredLease()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);
        gate.TryRetire(lease, RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.Ok);

        gate.TryRetire(lease, RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.AlreadyRetired);
    }

    [Fact]
    public void TryRetire_LeaseFromAnotherGate_IsRejectedAsWrongGate()
    {
        var gateA = NewGate();
        var gateB = NewGate();
        var leaseFromA = Issue(gateA);
        gateA.TryAuthorize(leaseFromA);

        gateB.TryRetire(leaseFromA, RetirementReason.Stop).Should().Be(GenerationRetirementOutcome.WrongGate);
    }

    // ── Commit fencing ──────────────────────────────────────────────

    [Fact]
    public void TryCommit_CurrentLease_RunsCommitAndReportsResult()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);

        gate.TryCommit(lease, true, static run => run).Should().Be(GenerationCommitOutcome.Committed);
        gate.TryCommit(lease, false, static run => run).Should().Be(GenerationCommitOutcome.NotCommitted);
    }

    [Fact]
    public void TryCommit_RetiredLease_IsRejected_AndCommitDoesNotRun()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);
        gate.TryRetire(lease, RetirementReason.Stop);

        var ran = false;
        var outcome = gate.TryCommit(lease, 0, _ => { ran = true; return true; });

        outcome.Should().Be(GenerationCommitOutcome.Rejected);
        ran.Should().BeFalse();
    }

    [Fact]
    public void TryCommit_UnauthorizedLease_IsRejected_AndCommitDoesNotRun()
    {
        var gate = NewGate();
        var lease = Issue(gate); // issued but never authorized

        var ran = false;
        var outcome = gate.TryCommit(lease, 0, _ => { ran = true; return true; });

        outcome.Should().Be(GenerationCommitOutcome.Rejected);
        ran.Should().BeFalse();
    }

    [Fact]
    public void TryCommit_CommitBodyThrows_ReleasesGate_WithoutChangingAuthority()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);

        var boom = () => gate.TryCommit(lease, 0, _ => throw new InvalidOperationException("boom"));
        boom.Should().Throw<InvalidOperationException>().WithMessage("boom");

        // Authority unchanged and the gate is usable again.
        gate.IsPublishAuthorized.Should().BeTrue();
        gate.TryCommit(lease, true, static run => run).Should().Be(GenerationCommitOutcome.Committed);
    }

    [Fact]
    public void TryCommit_CommitBodyReentersGate_Throws()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);

        var reenter = () => gate.TryCommit(lease, 0, _ =>
        {
            // Re-entering any gate transition from inside a commit is forbidden.
            gate.TryAuthorize(lease);
            return true;
        });

        reenter.Should().Throw<InvalidOperationException>();
    }

    // ── Activation-failure abandon (issued → retired) ───────────────

    [Fact]
    public void TryAbandonIssued_AfterInitializationFailure_LeaseCanNeverAuthorize()
    {
        var gate = NewGate();
        var lease = Issue(gate); // issued, then InitializeAsync fails before authorization

        gate.TryAbandonIssued(lease).Should().Be(GenerationAbandonOutcome.Ok);
        gate.TryAuthorize(lease).Should().Be(GenerationAuthorizationOutcome.AlreadyRetired);
    }

    [Fact]
    public void TryAbandonIssued_OnAuthorizedLease_IsRejected()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAuthorize(lease);

        gate.TryAbandonIssued(lease).Should().Be(GenerationAbandonOutcome.AlreadyAuthorized);
        gate.IsPublishAuthorized.Should().BeTrue();
    }

    [Fact]
    public void TryAbandonIssued_IsIdempotent()
    {
        var gate = NewGate();
        var lease = Issue(gate);
        gate.TryAbandonIssued(lease).Should().Be(GenerationAbandonOutcome.Ok);

        gate.TryAbandonIssued(lease).Should().Be(GenerationAbandonOutcome.AlreadyRetired);
    }

    // ── Stale / out-of-order authorization ──────────────────────────

    [Fact]
    public void TryAuthorize_StaleIssuedLease_AfterNewerAuthorizedAndRetired_IsRejected()
    {
        var gate = NewGate();
        var n = Issue(gate);       // generation 1
        var nPlus1 = Issue(gate);  // generation 2

        gate.TryAuthorize(nPlus1).Should().Be(GenerationAuthorizationOutcome.Ok);
        gate.TryRetire(nPlus1, RetirementReason.Reconfigure).Should().Be(GenerationRetirementOutcome.Ok);

        // The older, still-issued generation 1 must be permanently rejected.
        gate.TryAuthorize(n).Should().Be(GenerationAuthorizationOutcome.StaleGeneration);
        gate.IsPublishAuthorized.Should().BeFalse();
    }

    [Fact]
    public async Task TryAuthorize_ConcurrentIssueAndAuthorize_ExactlyOneSucceeds()
    {
        var gate = NewGate();
        var leaseA = Issue(gate);
        var leaseB = Issue(gate);
        using var barrier = new Barrier(2);
        var outcomes = new GenerationAuthorizationOutcome[2];

        var a = Task.Run(() => { barrier.SignalAndWait(); outcomes[0] = gate.TryAuthorize(leaseA); });
        var b = Task.Run(() => { barrier.SignalAndWait(); outcomes[1] = gate.TryAuthorize(leaseB); });
        await Task.WhenAll(a, b);

        // Exactly one wins; the loser is rejected (conflict or stale), never a
        // second authorization. The zero-or-one invariant holds.
        outcomes.Count(o => o == GenerationAuthorizationOutcome.Ok).Should().Be(1);
        outcomes.Count(o => o != GenerationAuthorizationOutcome.Ok).Should().Be(1);
        gate.IsPublishAuthorized.Should().BeTrue();
    }
}
