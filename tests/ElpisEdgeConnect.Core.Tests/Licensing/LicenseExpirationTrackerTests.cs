// ============================================================================
// File: Licensing/LicenseExpirationTrackerTests.cs
// Covers: pure-function expiration evaluation including end-of-day UTC,
//         warn boundaries 30/7/1, grace, and post-grace.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseExpirationTrackerTests
{
    private static readonly LicenseEnforcementPolicy Policy = LicenseEnforcementPolicy.Default;

    // End-of-day UTC for the license date 2026-04-07.
    private static readonly DateTime ExpiresAt = new(2026, 4, 7, 23, 59, 59, 999, DateTimeKind.Utc);

    [Fact]
    public void BeforeExpiry_FarFromBoundary_IsValid()
    {
        var now = ExpiresAt.AddDays(-100);
        var snap = LicenseExpirationTracker.Evaluate(now, ExpiresAt, Policy);

        snap.Status.Should().Be(LicenseStatus.Valid);
        snap.IsWarningBoundary.Should().BeFalse();
        snap.RemainingGrace.Should().BeNull();
    }

    [Theory]
    [InlineData(30)]
    [InlineData(7)]
    [InlineData(1)]
    public void AtWarnBoundary_RaisesBoundaryFlag(int daysOut)
    {
        // Arrange so floor(expiresAt - now in days) == daysOut.
        var now = ExpiresAt.AddDays(-daysOut).AddMinutes(-1);
        var snap = LicenseExpirationTracker.Evaluate(now, ExpiresAt, Policy);

        snap.Status.Should().Be(LicenseStatus.Valid);
        snap.IsWarningBoundary.Should().BeTrue();
        snap.DaysUntilExpiry.Should().Be(daysOut);
    }

    [Fact]
    public void AtExactExpiryInstant_IsStillValid()
    {
        // R6 pin (Mutation 5): `utcNow <= expiresAt` must include equality.
        // Changing to `<` would silently enter grace one millisecond early.
        var snap = LicenseExpirationTracker.Evaluate(ExpiresAt, ExpiresAt, Policy);
        snap.Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public void EndOfDay_StillValid_ThroughoutLastDay()
    {
        // PIN: end-of-day UTC semantics. A license dated 2026-04-07 must be
        // Valid throughout 2026-04-07, and only enter grace at the next instant.
        var lastSecondOfDay = new DateTime(2026, 4, 7, 23, 59, 59, 998, DateTimeKind.Utc);
        var firstInstantAfter = new DateTime(2026, 4, 8, 0, 0, 0, 0, DateTimeKind.Utc);

        LicenseExpirationTracker.Evaluate(lastSecondOfDay, ExpiresAt, Policy)
            .Status.Should().Be(LicenseStatus.Valid);

        LicenseExpirationTracker.Evaluate(firstInstantAfter, ExpiresAt, Policy)
            .Status.Should().Be(LicenseStatus.InGracePeriod);
    }

    [Fact]
    public void DuringGrace_ReportsRemaining()
    {
        var now = ExpiresAt.AddDays(15);
        var snap = LicenseExpirationTracker.Evaluate(now, ExpiresAt, Policy);

        snap.Status.Should().Be(LicenseStatus.InGracePeriod);
        snap.RemainingGrace.Should().NotBeNull();
        snap.RemainingGrace!.Value.TotalDays.Should().BeApproximately(15.0, 0.01);
    }

    [Fact]
    public void AfterGrace_IsExpired()
    {
        var now = ExpiresAt.AddDays(31);
        var snap = LicenseExpirationTracker.Evaluate(now, ExpiresAt, Policy);

        snap.Status.Should().Be(LicenseStatus.Expired);
        snap.RemainingGrace.Should().BeNull();
        snap.WarningLevel.Should().Be(LicenseWarningLevel.Critical);
    }
}
