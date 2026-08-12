// ============================================================================
// File: Licensing/LicenseManagerTests.cs
// Covers: load / verify / parse / fast-path checks / atomic snapshot.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseManagerTests
{
    private static LicenseManager NewManager(Func<DateTime>? clock = null)
    {
        var validator = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        return new LicenseManager(validator, LicenseEnforcementPolicy.Default, clock);
    }

    [Fact]
    public async Task LoadValidLicense_PopulatesSnapshot()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2027, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        manager.Current.Should().NotBeNull();
        manager.Current!.LicenseId.Should().Be("LIC-TEST-0001");
        manager.Current.GatewayId.Should().Be("GW-TEST-001");
        manager.Status.Should().Be(LicenseStatus.Valid);
        manager.IsModuleEnabled("source.focas2").Should().BeTrue();
        manager.IsModuleEnabled("source.s7").Should().BeFalse();
    }

    [Theory]
    [InlineData("CNC Pro", LicenseEdition.CncPro)]
    [InlineData("Trial Period", LicenseEdition.TrialPeriod)]
    [InlineData("Professional", LicenseEdition.Professional)]
    [InlineData("Starter", LicenseEdition.Starter)]
    [InlineData("Enterprise", LicenseEdition.Enterprise)]
    public async Task LoadLicense_ParsesDisplayEditionStringWithSpaces(string editionString, LicenseEdition expected)
    {
        // The License Generator writes the edition as the DISPLAY string with
        // spaces ("CNC Pro", "Trial Period"); ParsePayload must strip spaces so
        // these load rather than being rejected as unrecognised.
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(
            expires: new DateTime(2027, 4, 7),
            editionStringOverride: editionString);

        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        manager.Current.Should().NotBeNull();
        manager.Current!.Edition.Should().Be(expected);
    }

    [Fact]
    public async Task LoadLicense_UnrecognisedEdition_ThrowsLicenseFileCorrupt()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(
            expires: new DateTime(2027, 4, 7),
            editionStringOverride: "Totally Bogus");

        Func<Task> act = () => manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        await act.Should().ThrowAsync<LicenseException>()
            .Where(e => e.Error.Code == CoreErrors.LicenseFileCorrupt);
    }

    [Fact]
    public async Task LoadTamperedLicense_ThrowsAndKeepsPrevious()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var goodJson = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2027, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(goodJson), CancellationToken.None);
        var firstSnapshot = manager.Current;

        // Mutate a single character of the customer field; signature now invalid.
        var bad = goodJson.Replace("Test Customer", "EVIL Customer", StringComparison.Ordinal);

        Func<Task> act = () => manager.LoadAsync(B3TestFixtures.ToStream(bad), CancellationToken.None);
        await act.Should().ThrowAsync<LicenseException>()
            .Where(e => e.Error.Code == CoreErrors.LicenseSignatureInvalid);

        manager.Current.Should().BeSameAs(firstSnapshot);
    }

    [Fact]
    public async Task LoadCorruptJson_ThrowsLicenseFileCorrupt()
    {
        using var manager = NewManager();
        Func<Task> act = () => manager.LoadAsync(B3TestFixtures.ToStream("{not json"), CancellationToken.None);
        await act.Should().ThrowAsync<LicenseException>()
            .Where(e => e.Error.Code == CoreErrors.LicenseFileCorrupt);
    }

    [Fact]
    public async Task LoadMissingSignature_Throws()
    {
        using var manager = NewManager();
        Func<Task> act = () => manager.LoadAsync(
            B3TestFixtures.ToStream("{\"customer\":\"x\"}"),
            CancellationToken.None);
        await act.Should().ThrowAsync<LicenseException>()
            .Where(e => e.Error.Code == CoreErrors.LicenseSignatureInvalid);
    }

    [Fact]
    public async Task IsModuleEnabled_NotLoaded_ReturnsFalse()
    {
        using var manager = NewManager();
        manager.IsModuleEnabled("source.focas2").Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CheckInstanceLimit_BoundaryAt_Max_AllowAllowDeny()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2027, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        // source.focas2 has maxInstances = 20 in the default fixture.
        manager.CheckInstanceLimit("source.focas2", 19).Allowed.Should().BeTrue();
        manager.CheckInstanceLimit("source.focas2", 20).Allowed.Should().BeTrue();
        var deny = manager.CheckInstanceLimit("source.focas2", 21);
        deny.Allowed.Should().BeFalse();
        deny.Code.Should().Be(CoreErrors.LicenseInstanceLimitReached);
    }

    [Fact]
    public async Task CheckInstanceLimit_DisabledModule_DenyWithModuleCode()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2027, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        var result = manager.CheckInstanceLimit("source.s7", 1);
        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(CoreErrors.LicenseModuleDisabled);
    }

    [Fact]
    public async Task ExpiredLicense_TickEntersExpiredState()
    {
        var clock = new MutableClock(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        using var manager = NewManager(() => clock.Now);
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        manager.Status.Should().Be(LicenseStatus.Valid);

        // Cross into grace.
        clock.Now = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        manager.Tick();
        manager.Status.Should().Be(LicenseStatus.InGracePeriod);

        // Cross past grace.
        clock.Now = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc);
        manager.Tick();
        manager.Status.Should().Be(LicenseStatus.Expired);
    }

    [Fact]
    public async Task LoadValid_RaisesAtMostOneInfoWarningPerBoundary()
    {
        var clock = new MutableClock(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        using var manager = NewManager(() => clock.Now);
        int raised = 0;
        manager.WarningRaised += (_, _) => raised++;

        // 6 days from expiry → fires warn boundary at first load
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        var afterLoad = raised;
        manager.Tick();
        manager.Tick();
        // Subsequent ticks at the same boundary do not re-raise.
        raised.Should().Be(afterLoad);
    }

    // ========================================================================
    // B3 close-out pins (mutation coverage)
    // ========================================================================

    /// <summary>
    /// R1 pin (Mutation 4): the parser must promote date-only expiresAt values
    /// to end-of-day UTC (23:59:59.999). Pinned at the actual load path so a
    /// regression in LicenseManager.RequireDate cannot ship.
    /// </summary>
    [Fact]
    public async Task LoadLicense_ParsesExpiresAtAsEndOfDay()
    {
        using var manager = NewManager(() => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2027, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        manager.Current!.ExpiresAt.Year.Should().Be(2027);
        manager.Current.ExpiresAt.Month.Should().Be(4);
        manager.Current.ExpiresAt.Day.Should().Be(7);
        manager.Current.ExpiresAt.Hour.Should().Be(23);
        manager.Current.ExpiresAt.Minute.Should().Be(59);
        manager.Current.ExpiresAt.Second.Should().Be(59);
        manager.Current.ExpiresAt.Millisecond.Should().Be(999);
        manager.Current.ExpiresAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    /// <summary>
    /// R3 pin (Mutation 13): the parameterless LicenseManager constructor
    /// must actually use the embedded public key. Since TestRsaKeys matches
    /// EmbeddedPublicKey, a license signed with the test private key must
    /// load successfully through the default constructor.
    /// </summary>
    [Fact]
    public async Task ParameterlessConstructor_UsesEmbeddedKey()
    {
        using var manager = new LicenseManager();
        var json = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2099, 1, 1));
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        manager.Current.Should().NotBeNull();
        manager.Status.Should().Be(LicenseStatus.Valid);
    }

    /// <summary>
    /// R4 pin (Mutation 6): reloading a license must reset the warning dedupe
    /// set so boundary events can fire again for the new license.
    /// </summary>
    [Fact]
    public async Task LoadNewLicense_ResetsWarningDedupSet()
    {
        // Clock sits ~7.99 days before expiry end-of-day, so floor(days) == 7
        // and both loads land on the 7-day warn boundary.
        using var manager = NewManager(() => new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        int raised = 0;
        manager.WarningRaised += (_, _) => raised++;

        var json1 = B3TestFixtures.BuildSignedLicense(expires: new DateTime(2026, 4, 7));
        await manager.LoadAsync(B3TestFixtures.ToStream(json1), CancellationToken.None);
        var afterFirst = raised;

        var json2 = B3TestFixtures.BuildSignedLicense(
            expires: new DateTime(2026, 4, 7),
            licenseId: "LIC-TEST-0002");
        await manager.LoadAsync(B3TestFixtures.ToStream(json2), CancellationToken.None);

        raised.Should().BeGreaterThan(afterFirst,
            "reloading a license must reset the warning dedupe set");
    }

    private sealed class MutableClock
    {
        public DateTime Now;
        public MutableClock(DateTime initial) { Now = initial; }
    }
}
