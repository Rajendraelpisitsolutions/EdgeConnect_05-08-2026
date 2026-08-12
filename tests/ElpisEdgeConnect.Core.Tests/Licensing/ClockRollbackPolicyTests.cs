// ============================================================================
// Tests: ClockRollbackPolicy — pins the anti-"wind the clock back" rule that
//        stops an expired license from being resurrected by changing the
//        system date.
// ============================================================================

using System;
using System.Collections.Frozen;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class ClockRollbackPolicyTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);
    private static readonly DateTime Now = new(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);

    // ── IsRollback ────────────────────────────────────────────────────────

    [Fact]
    public void IsRollback_NoAnchorYet_ReturnsFalse()
    {
        ClockRollbackPolicy.IsRollback(Now, anchorUtc: null, Tolerance).Should().BeFalse();
    }

    [Fact]
    public void IsRollback_ClockMovedForward_ReturnsFalse()
    {
        var anchor = Now.AddDays(-1);

        ClockRollbackPolicy.IsRollback(Now, anchor, Tolerance).Should().BeFalse();
    }

    [Fact]
    public void IsRollback_SmallBackwardDriftWithinTolerance_ReturnsFalse()
    {
        // e.g. a legitimate NTP correction — must not trip the guard.
        var anchor = Now.AddMinutes(2);

        ClockRollbackPolicy.IsRollback(Now, anchor, Tolerance).Should().BeFalse();
    }

    [Fact]
    public void IsRollback_ClockWoundBackBeyondTolerance_ReturnsTrue()
    {
        // The attack: date moved back a month to un-expire a license.
        var anchor = Now.AddMonths(1);

        ClockRollbackPolicy.IsRollback(Now, anchor, Tolerance).Should().BeTrue();
    }

    // ── Advance (monotonic high-water mark) ───────────────────────────────

    [Fact]
    public void Advance_NoAnchor_UsesNow()
    {
        ClockRollbackPolicy.Advance(Now, anchorUtc: null, TimeSpan.Zero, Tolerance).Should().Be(Now);
    }

    [Fact]
    public void Advance_ClockAhead_MovesAnchorForwardWithinRealElapsed()
    {
        // Anchor 1 minute behind, real elapsed 1 minute → catches up to now.
        var anchor = Now.AddMinutes(-1);

        ClockRollbackPolicy.Advance(Now, anchor, TimeSpan.FromMinutes(1), Tolerance).Should().Be(Now);
    }

    [Fact]
    public void Advance_ClockBehind_AnchorNeverGoesBackwards()
    {
        var anchor = Now.AddDays(30);

        ClockRollbackPolicy.Advance(Now, anchor, TimeSpan.FromSeconds(30), Tolerance)
            .Should().Be(anchor, "the anchor is monotonic");
    }

    [Fact]
    public void Advance_ClockJumpsFarForward_AnchorCappedToRealElapsed_NoPoisoning()
    {
        // The clock is set MONTHS into the future, but only 30s of real time
        // elapsed. The anchor must NOT jump to the future — it advances at most
        // ~30s (+ tolerance), so a later correction won't be seen as a rollback.
        var anchor = Now;
        var wallClockNow = Now.AddDays(90);
        var realElapsed = TimeSpan.FromSeconds(30);

        var result = ClockRollbackPolicy.Advance(wallClockNow, anchor, realElapsed, Tolerance);

        result.Should().BeCloseTo(anchor + realElapsed, precision: Tolerance,
            "a far-forward wall clock must not poison the anchor");
        result.Should().BeBefore(Now.AddMinutes(10));
    }

    // ── IsBeforeIssue ─────────────────────────────────────────────────────

    [Fact]
    public void IsBeforeIssue_NoLicense_ReturnsFalse()
    {
        ClockRollbackPolicy.IsBeforeIssue(Now, license: null).Should().BeFalse();
    }

    [Fact]
    public void IsBeforeIssue_ClockAfterIssueDate_ReturnsFalse()
    {
        var license = License(issuedAt: Now.AddDays(-10), expiresAt: Now.AddDays(10));

        ClockRollbackPolicy.IsBeforeIssue(Now, license).Should().BeFalse();
    }

    [Fact]
    public void IsBeforeIssue_ClockBeforeIssueDate_ReturnsTrue()
    {
        // A license cannot legitimately be in use before it was issued.
        var license = License(issuedAt: Now.AddDays(10), expiresAt: Now.AddDays(400));

        ClockRollbackPolicy.IsBeforeIssue(Now, license).Should().BeTrue();
    }

    private static LicenseInfo License(DateTime issuedAt, DateTime expiresAt) => new()
    {
        LicenseId = "LIC-TEST",
        Customer = "Test",
        GatewayId = "gw-test",
        Edition = LicenseEdition.Professional,
        IssuedAt = issuedAt,
        ExpiresAt = expiresAt,
        Limits = new LicenseLimits { MaxSourceInstances = 10, MaxSinkInstances = 10, MaxRoutes = 10 },
        Modules = FrozenDictionary<string, LicenseModule>.Empty,
    };
}
