// ============================================================================
// File: Licensing/LicenseEditionCatalogTests.cs
// Covers: the edition-to-offered-protocol matrix shared by the Studio pickers
//         and the License Generator. Pins the Sony scoping: CNC Pro / Trial
//         Period editions and Professional no longer offering OPC UA Client.
// ============================================================================

using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseEditionCatalogTests
{
    // The OPC UA Client source's REAL runtime module key (differs from the
    // LicenseModuleKeys.SourceOpcUaClient constant, which is "source-opc-ua-client").
    private const string OpcUaClientKey = "source-opcua-client";

    [Fact]
    public void CncPro_OffersExactlyFocas2BrotherAndMtconnect()
    {
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SourceFocas2).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SourceBrotherHttp).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SourceMtconnect).Should().BeTrue();

        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SourceModbusTcp).Should().BeFalse();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SourceS7).Should().BeFalse();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.CncPro, OpcUaClientKey).Should().BeFalse();
    }

    [Fact]
    public void CncPro_OffersMqttSinkOnly()
    {
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SinkMqtt).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.CncPro, LicenseModuleKeys.SinkOpcUaServer).Should().BeFalse();
    }

    [Fact]
    public void Professional_OffersEverythingExceptOpcUaClient()
    {
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Professional, OpcUaClientKey).Should().BeFalse();

        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SourceS7).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SourceModbusTcp).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SourceFocas2).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SourceMelsec).Should().BeTrue();
    }

    [Fact]
    public void Professional_OffersMqttSinkOnly()
    {
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SinkMqtt).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Professional, LicenseModuleKeys.SinkOpcUaServer).Should().BeFalse();
    }

    [Fact]
    public void Starter_OffersModbusTcpAndMqttOnly()
    {
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Starter, LicenseModuleKeys.SourceModbusTcp).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Starter, LicenseModuleKeys.SourceFocas2).Should().BeFalse();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Starter, LicenseModuleKeys.SinkMqtt).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Starter, LicenseModuleKeys.SinkOpcUaServer).Should().BeFalse();
    }

    [Fact]
    public void Enterprise_OffersAllSourcesAndMqttPlusOpcUaServer()
    {
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Enterprise, OpcUaClientKey).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.Enterprise, LicenseModuleKeys.SourceMelsec).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Enterprise, LicenseModuleKeys.SinkMqtt).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.Enterprise, LicenseModuleKeys.SinkOpcUaServer).Should().BeTrue();
    }

    [Fact]
    public void TrialPeriod_OffersAllSourcesAndAllSinks()
    {
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.TrialPeriod, OpcUaClientKey).Should().BeTrue();
        LicenseEditionCatalog.IsSourceModuleOffered(LicenseEdition.TrialPeriod, LicenseModuleKeys.SourceModbusTcp).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.TrialPeriod, LicenseModuleKeys.SinkMqtt).Should().BeTrue();
        LicenseEditionCatalog.IsSinkModuleOffered(LicenseEdition.TrialPeriod, LicenseModuleKeys.SinkOpcUaServer).Should().BeTrue();
    }

    [Theory]
    [InlineData(LicenseEdition.Starter, true)]
    [InlineData(LicenseEdition.CncPro, true)]
    // Professional restricts its source set (drops OPC UA Client) but returns
    // false here on purpose: the License Generator treats RestrictsSources as a
    // "Modbus-TCP-only" gate, and the exclusion is enforced via
    // IsSourceModuleOffered instead.
    [InlineData(LicenseEdition.Professional, false)]
    [InlineData(LicenseEdition.Enterprise, false)]
    [InlineData(LicenseEdition.TrialPeriod, false)]
    [InlineData(LicenseEdition.Unknown, false)]
    public void RestrictsSources_MatchesPolicy(LicenseEdition edition, bool expected)
    {
        LicenseEditionCatalog.RestrictsSources(edition).Should().Be(expected);
    }

    [Theory]
    [InlineData(LicenseEdition.Starter, true)]
    [InlineData(LicenseEdition.Professional, true)]
    [InlineData(LicenseEdition.CncPro, true)]
    [InlineData(LicenseEdition.Enterprise, true)]
    [InlineData(LicenseEdition.TrialPeriod, false)]
    [InlineData(LicenseEdition.Unknown, false)]
    public void RestrictsSinks_MatchesPolicy(LicenseEdition edition, bool expected)
    {
        LicenseEditionCatalog.RestrictsSinks(edition).Should().Be(expected);
    }
}
