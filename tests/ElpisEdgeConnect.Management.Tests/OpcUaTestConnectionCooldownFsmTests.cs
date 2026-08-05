// ============================================================================
// Tests: OpcUaTestConnectionCooldownFsm — pin the PR 7c-4 Test
//        Connection button state machine.
//
//        Invariants pinned (per PR 7c plan + amendment #1,
//        user lock 2026-05-29):
//
//          1. Initial state is Idle, button enabled, Cancel hidden
//          2. BeginProbe Idle → Probing (button disabled, Cancel visible)
//          3. BeginProbe while already Probing is a no-op (single-active-probe)
//          4. CompleteProbe Probing → CoolingDown with CooldownEndsAtUtc set
//          5. AMENDMENT #1: CancelProbe Probing → Idle DIRECTLY (NO cooldown)
//          6. TickCooldown CoolingDown → Idle once now >= CooldownEndsAtUtc
//          7. ButtonEnabled / CancelVisible properties match the state
// Reference: PR 7c plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OpcUaTestConnectionCooldownFsmTests
{
    [Fact]
    public void InitialState_IsIdle_ButtonEnabled_CancelHidden()
    {
        var fsm = new OpcUaTestConnectionCooldownFsm();

        fsm.State.Should().Be(OpcUaTestConnectionProbeState.Idle);
        fsm.ButtonEnabled.Should().BeTrue();
        fsm.CancelVisible.Should().BeFalse();
        fsm.CooldownEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public void BeginProbe_FromIdle_TransitionsToProbing_ButtonDisabled_CancelVisible()
    {
        var fsm = new OpcUaTestConnectionCooldownFsm();

        fsm.BeginProbe();

        fsm.State.Should().Be(OpcUaTestConnectionProbeState.Probing);
        fsm.ButtonEnabled.Should().BeFalse();
        fsm.CancelVisible.Should().BeTrue();
    }

    [Fact]
    public void BeginProbe_WhileAlreadyProbing_IsNoOp()
    {
        // Single-active-probe per amendment refinement (replaces fixed-
        // duration cooldown with "one in flight at a time").
        var fsm = new OpcUaTestConnectionCooldownFsm();
        fsm.BeginProbe();

        fsm.BeginProbe();

        fsm.State.Should().Be(OpcUaTestConnectionProbeState.Probing,
            "second Begin while already Probing must NOT regress state");
    }

    [Fact]
    public void CompleteProbe_FromProbing_TransitionsToCoolingDown_WithCooldownEndsAtSet()
    {
        var fsm = new OpcUaTestConnectionCooldownFsm(TimeSpan.FromMilliseconds(500));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        fsm.BeginProbe();

        fsm.CompleteProbe(now);

        fsm.State.Should().Be(OpcUaTestConnectionProbeState.CoolingDown);
        fsm.ButtonEnabled.Should().BeFalse();
        fsm.CancelVisible.Should().BeFalse(
            "Cancel only shows while Probing; CoolingDown is non-interruptible");
        fsm.CooldownEndsAtUtc.Should().Be(now + TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void CancelProbe_FromProbing_TransitionsDirectlyToIdle_NoCooldown()
    {
        // Amendment #1 (user lock 2026-05-29) — the load-bearing test
        // for the most-debated UX detail. An operator who intentionally
        // aborted MUST NOT see a forced delay before retrying.
        var fsm = new OpcUaTestConnectionCooldownFsm();
        fsm.BeginProbe();

        fsm.CancelProbe();

        fsm.State.Should().Be(OpcUaTestConnectionProbeState.Idle,
            "amendment #1 — cancelled probe skips cooldown");
        fsm.ButtonEnabled.Should().BeTrue(
            "operator can immediately retry after cancelling");
        fsm.CooldownEndsAtUtc.Should().BeNull(
            "no cooldown anchor when cancelled");
    }

    [Fact]
    public void TickCooldown_BeforeCooldownEnds_DoesNotAdvance()
    {
        var fsm = new OpcUaTestConnectionCooldownFsm(TimeSpan.FromSeconds(1));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        fsm.BeginProbe();
        fsm.CompleteProbe(now);

        var advanced = fsm.TickCooldown(now + TimeSpan.FromMilliseconds(500));

        advanced.Should().BeFalse();
        fsm.State.Should().Be(OpcUaTestConnectionProbeState.CoolingDown);
    }

    [Fact]
    public void TickCooldown_AtOrAfterCooldownEnd_TransitionsToIdle()
    {
        var fsm = new OpcUaTestConnectionCooldownFsm(TimeSpan.FromSeconds(1));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        fsm.BeginProbe();
        fsm.CompleteProbe(now);

        var advanced = fsm.TickCooldown(now + TimeSpan.FromSeconds(1));

        advanced.Should().BeTrue();
        fsm.State.Should().Be(OpcUaTestConnectionProbeState.Idle);
        fsm.CooldownEndsAtUtc.Should().BeNull();
    }

    [Fact]
    public void DefaultCooldownDuration_IsOneSecond()
    {
        // Per amendment refinement — single active probe + 1 s cooldown.
        OpcUaTestConnectionCooldownFsm.DefaultCooldownDuration
            .Should().Be(TimeSpan.FromSeconds(1));
    }
}
