// ============================================================================
// Tests: SourceEditRouter — pins the v2 §5.2 four-state dispatch matrix
//        and the locked protocol-name → display-name + license-module-key
//        mappings. Pure unit tests against the internal static members; the
//        Razor render path is exercised in the integration tests (step 12).
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §5.2
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Management.Components.Pages;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class SourceEditRouterTests
{
    // ─── ResolveUxState — four-state dispatch ────────────────────────────

    [Theory]
    [InlineData("focas2", true)]
    [InlineData("brother-http", true)]
    [InlineData("modbustcp", true)]
    [InlineData("opcua-client", true)]  // PR 7c-4 — wizard added
    [InlineData("mtconnect", true)]     // M.2b.4 — wizard added
    [InlineData("s7", true)]            // M.2b.2/M3 — wizard added
    [InlineData("melsec", true)]        // MELSEC Slice-1 UI — wizard added
    public void ResolveUxState_KnownProtocolLicensedAndHasWizard_RendersWizard(
        string protocol, bool licensed)
    {
        var state = SourceEditRouter.ResolveUxState(protocol, licensed);
        state.Should().Be(SourceEditRouter.UxState.RenderWizard);
    }

    [Theory]
    [InlineData("focas2")]
    [InlineData("brother-http")]
    [InlineData("modbustcp")]
    [InlineData("opcua-client")]  // PR 7c-4 — wizard added
    public void ResolveUxState_LicenseDisabled_PreemptsWizardCheck(string protocol)
    {
        var state = SourceEditRouter.ResolveUxState(protocol, isLicenseEnabled: false);
        state.Should().Be(SourceEditRouter.UxState.LicenseDisabled);
    }

    [Theory]
    [InlineData("ethernet-ip")]
    [InlineData("future-protocol")]
    [InlineData("")]
    public void ResolveUxState_UnknownProtocol_RendersUnsupportedPanel(string protocol)
    {
        var state = SourceEditRouter.ResolveUxState(protocol, isLicenseEnabled: true);
        state.Should().Be(SourceEditRouter.UxState.UnknownProtocol);
    }

    [Theory]
    [InlineData("mtlinki")]
    public void ResolveUxState_ProtocolRegisteredButNoWizard_RendersWizardMissingPanel(string protocol)
    {
        // Protocols whose adapters ship but whose typed wizards aren't
        // in this Studio build yet. M.2b.4 added MTConnect's wizard;
        // M.2b.2/M3 added S7's; MT-LINKi ships later.
        var state = SourceEditRouter.ResolveUxState(protocol, isLicenseEnabled: true);
        state.Should().Be(SourceEditRouter.UxState.WizardMissing);
    }

    [Fact]
    public void ResolveUxState_UnknownProtocolBeatsLicenseCheck()
    {
        // Unknown protocol takes precedence — even if isLicenseEnabled is
        // false, we render the "Studio doesn't recognise this protocol"
        // panel rather than the license-disabled panel. The license-key
        // mapping wouldn't produce a meaningful module key for an unknown
        // protocol anyway.
        var state = SourceEditRouter.ResolveUxState("future-protocol", isLicenseEnabled: false);
        state.Should().Be(SourceEditRouter.UxState.UnknownProtocol);
    }

    [Fact]
    public void ResolveUxState_CustomRegistries_DispatchAccordingly()
    {
        // Pinning the test-injection seam — future protocols can be added
        // to a custom set without touching the production constants.
        var registered = new HashSet<string> { "custom-proto" };
        var hasWizard = new HashSet<string> { "custom-proto" };

        var state = SourceEditRouter.ResolveUxState(
            "custom-proto", isLicenseEnabled: true,
            registeredProtocols: registered,
            protocolsWithWizard: hasWizard);

        state.Should().Be(SourceEditRouter.UxState.RenderWizard);
    }

    // ─── ProtocolDisplayNameFor ─────────────────────────────────────────

    [Theory]
    [InlineData("focas2", "FOCAS2")]
    [InlineData("brother-http", "Brother HTTP")]
    [InlineData("modbustcp", "Modbus TCP")]
    [InlineData("modbusrtu", "Modbus RTU")]
    [InlineData("mtconnect", "MTConnect")]
    [InlineData("mtlinki", "MT-LINKi")]
    [InlineData("s7", "Siemens S7")]
    [InlineData("melsec", "Mitsubishi MELSEC")]
    [InlineData("unknown-thing", "unknown-thing")]
    public void ProtocolDisplayNameFor_MapsCorrectly(string protocol, string expected)
    {
        SourceEditRouter.ProtocolDisplayNameFor(protocol).Should().Be(expected);
    }

    // ─── LicenseModuleKeyFor ────────────────────────────────────────────

    [Theory]
    [InlineData("focas2", "source-focas2")]
    [InlineData("brother-http", "source-brother-http")]
    [InlineData("modbustcp", "source-modbus-tcp")]
    [InlineData("modbusrtu", "source-modbus-tcp")]
    [InlineData("mtconnect", "source-mtconnect")]
    [InlineData("mtlinki", "source-mtlinki")]
    [InlineData("s7", "source-s7")]
    [InlineData("melsec", "source-melsec")]
    public void LicenseModuleKeyFor_MatchesCoreLicenseModuleKeysConstants(string protocol, string expected)
    {
        // Pin against the LicenseModuleKeys constants — if the Core
        // constant is ever renamed, this test fails and the router gets
        // updated atomically.
        SourceEditRouter.LicenseModuleKeyFor(protocol).Should().Be(expected);
    }

    [Fact]
    public void LicenseModuleKeyFor_UnknownProtocol_BuildsConventionalKey()
    {
        // For protocols not in the switch, fall back to "source-<protocol>".
        // Reasonable default that surfaces an obviously-fake key in the
        // license-disabled panel so support can identify the issue.
        SourceEditRouter.LicenseModuleKeyFor("future-proto").Should().Be("source-future-proto");
    }

    // ─── ProtocolsWithEditWizard set sanity ─────────────────────────────

    [Fact]
    public void ProtocolsWithEditWizard_MatchesShippedWizards()
    {
        // M.2d.2 shipped edit-mode wizards for focas2 / brother-http /
        // modbustcp; PR 7c-4 added opcua-client; M.2b.4 added mtconnect.
        // When a future protocol's wizard adoption lands, this assertion
        // fires and forces an explicit update — preventing a wizard from
        // "silently" being claimed by the router before its Edit-mode code
        // paths are actually wired.
        SourceEditRouter.ProtocolsWithEditWizard.Should().BeEquivalentTo(new[]
        {
            "focas2",
            "brother-http",
            "modbustcp",
            "modbusrtu",
            "opcua-client",
            "mtconnect",
            "s7",
            "ethernetip",
            "melsec",
        });
    }
}
