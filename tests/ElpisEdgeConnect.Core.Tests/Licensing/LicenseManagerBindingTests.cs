// ============================================================================
// File: LicenseManagerBindingTests.cs
// Purpose: Single-machine (node-locked) license binding (ADR-0036). A license
//          whose gatewayId does not match the running gateway's identity must
//          fail to load; "*" floats; a null expected id disables the check.
// Reference: docs/decisions/0036-single-machine-license-binding.md
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public class LicenseManagerBindingTests
{
    private static readonly DateTime FarFuture = new(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<LicenseManager> LoadAsync(string gatewayId, Func<string?> expectedProvider)
    {
        var json = B3TestFixtures.BuildSignedLicense(FarFuture, gatewayId: gatewayId);
        var manager = new LicenseManager(expectedProvider);
        await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);
        return manager;
    }

    [Fact]
    public async Task Load_MatchingGatewayId_Succeeds()
    {
        var manager = await LoadAsync("GW-MACHINE-A", () => "GW-MACHINE-A");

        manager.Status.Should().Be(LicenseStatus.Valid);
        manager.Current!.GatewayId.Should().Be("GW-MACHINE-A");
    }

    [Fact]
    public async Task Load_MismatchedGatewayId_ThrowsGatewayMismatch_AndDoesNotLoad()
    {
        var json = B3TestFixtures.BuildSignedLicense(FarFuture, gatewayId: "GW-OTHER-MACHINE");
        var manager = new LicenseManager(() => "GW-THIS-MACHINE");

        var act = async () => await manager.LoadAsync(B3TestFixtures.ToStream(json), CancellationToken.None);

        await act.Should().ThrowAsync<LicenseException>()
            .Where(e => e.Error.Code == CoreErrors.LicenseGatewayMismatch);
        manager.Status.Should().Be(LicenseStatus.NotLoaded);
        manager.Current.Should().BeNull("a wrong-gateway license must not take effect");
    }

    [Fact]
    public async Task Load_FloatingLicense_Star_LoadsOnAnyGateway()
    {
        var manager = await LoadAsync("*", () => "GW-ANY-MACHINE");

        manager.Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public async Task Load_NoExpectedIdentity_SkipsBinding()
    {
        // Before the identity is established the provider returns null; the
        // check is skipped and the license loads on signature + expiry alone.
        var manager = await LoadAsync("GW-WHATEVER", () => null);

        manager.Status.Should().Be(LicenseStatus.Valid);
    }

    [Fact]
    public async Task Load_Match_IsCaseInsensitive_AndTrimmed()
    {
        var manager = await LoadAsync("gw-machine-a", () => "  GW-MACHINE-A  ");

        manager.Status.Should().Be(LicenseStatus.Valid);
    }
}
