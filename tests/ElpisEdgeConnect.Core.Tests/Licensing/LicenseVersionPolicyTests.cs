// ============================================================================
// File: LicenseVersionPolicyTests.cs
// Purpose: Lock the version-coverage rule (LicenseVersionPolicy): a license
//          covers builds released on or before its expiry; after expiry a
//          newer build is version-restricted, and a valid license within 30
//          days of expiry is "expiring soon".
// ============================================================================

using System;
using System.Collections.Frozen;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseVersionPolicyTests
{
    private static LicenseInfo License(DateTime expiresAt) => new()
    {
        LicenseId = "LIC-TEST",
        Customer = "Test Customer",
        GatewayId = "gw-test",
        Edition = LicenseEdition.Professional,
        IssuedAt = expiresAt.AddYears(-1),
        ExpiresAt = expiresAt,
        Limits = new LicenseLimits { MaxSourceInstances = 100, MaxSinkInstances = 100, MaxRoutes = 100 },
        Modules = FrozenDictionary<string, LicenseModule>.Empty,
    };

    // ---- IsVersionRestricted --------------------------------------------

    [Fact]
    public void IsVersionRestricted_NoLicense_ReturnsFalse()
    {
        LicenseVersionPolicy.IsVersionRestricted(LicenseStatus.Expired, null, new DateTime(2030, 1, 1))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(LicenseStatus.Valid)]
    [InlineData(LicenseStatus.NotLoaded)]
    [InlineData(LicenseStatus.Invalid)]
    public void IsVersionRestricted_NotExpired_ReturnsFalse(LicenseStatus status)
    {
        // Even a build far in the future is not restricted while the license is not expired.
        var license = License(new DateTime(2026, 1, 1));

        LicenseVersionPolicy.IsVersionRestricted(status, license, new DateTime(2030, 1, 1))
            .Should().BeFalse();
    }

    [Fact]
    public void IsVersionRestricted_ExpiredButBuildOnOrBeforeExpiry_ReturnsFalse()
    {
        var license = License(new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc));

        // Build released the same day the license expires is still covered.
        LicenseVersionPolicy.IsVersionRestricted(LicenseStatus.Expired, license, new DateTime(2026, 6, 30))
            .Should().BeFalse();

        // An older build is obviously covered.
        LicenseVersionPolicy.IsVersionRestricted(LicenseStatus.Expired, license, new DateTime(2026, 1, 1))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(LicenseStatus.Expired)]
    [InlineData(LicenseStatus.InGracePeriod)]
    public void IsVersionRestricted_ExpiredAndBuildAfterExpiry_ReturnsTrue(LicenseStatus status)
    {
        var license = License(new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc));

        LicenseVersionPolicy.IsVersionRestricted(status, license, new DateTime(2026, 7, 10))
            .Should().BeTrue();
    }

    // ---- IsExpiringSoon --------------------------------------------------

    [Fact]
    public void IsExpiringSoon_NoLicense_ReturnsFalse()
    {
        LicenseVersionPolicy.IsExpiringSoon(LicenseStatus.Valid, null, new DateTime(2026, 6, 1))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(LicenseStatus.Expired)]
    [InlineData(LicenseStatus.InGracePeriod)]
    [InlineData(LicenseStatus.NotLoaded)]
    public void IsExpiringSoon_NotValid_ReturnsFalse(LicenseStatus status)
    {
        var license = License(new DateTime(2026, 6, 20));

        LicenseVersionPolicy.IsExpiringSoon(status, license, new DateTime(2026, 6, 1))
            .Should().BeFalse();
    }

    [Fact]
    public void IsExpiringSoon_ValidWithin30Days_ReturnsTrue()
    {
        var license = License(new DateTime(2026, 6, 20, 23, 59, 59, DateTimeKind.Utc));

        LicenseVersionPolicy.IsExpiringSoon(LicenseStatus.Valid, license, new DateTime(2026, 6, 1))
            .Should().BeTrue();
    }

    [Fact]
    public void IsExpiringSoon_ValidMoreThan30DaysOut_ReturnsFalse()
    {
        var license = License(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));

        LicenseVersionPolicy.IsExpiringSoon(LicenseStatus.Valid, license, new DateTime(2026, 6, 1))
            .Should().BeFalse();
    }
}
