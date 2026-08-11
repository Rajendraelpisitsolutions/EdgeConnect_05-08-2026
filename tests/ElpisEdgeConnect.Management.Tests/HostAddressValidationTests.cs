// ============================================================================
// File: HostAddressValidationTests.cs
// Purpose: Guards the source-wizard address check. The anchor case is "1":
//          it was accepted by every network wizard and written into
//          gateway.json as a CNC address, because the only gate was
//          IsNullOrWhiteSpace. IPAddress.TryParse would NOT have caught it
//          either — on .NET 8 it expands "1" to 0.0.0.1 — so the shorthand
//          cases below are the ones that matter most.
// ============================================================================

using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class HostAddressValidationTests
{
    // ─── The reported defect ────────────────────────────────────────────

    [Theory]
    [InlineData("1")]        // observed in gateway.json as a FOCAS2 address
    [InlineData("10.1")]     // IPAddress.TryParse expands this to 10.0.0.1
    [InlineData("192.168")]
    [InlineData("192.168.1")]
    public void IsValid_ShorthandIPv4_IsRejected(string host)
    {
        HostAddressValidation.IsValid(host).Should().BeFalse();
    }

    [Fact]
    public void Describe_ShorthandIPv4_SaysAllFourPartsAreNeeded()
    {
        var message = HostAddressValidation.Describe("1");

        message.Should().NotBeNull();
        message.Should().Contain("all four parts");
        message.Should().Contain("192.168.1.101", "the operator's next action is to type an address");
    }

    // ─── Valid addresses ────────────────────────────────────────────────

    [Theory]
    [InlineData("192.168.1.101")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.5.7")]
    public void IsValid_CompleteIPv4_IsAccepted(string host)
    {
        HostAddressValidation.IsValid(host).Should().BeTrue();
    }

    [Theory]
    [InlineData("cnc-line-a")]
    [InlineData("cnc.factory.local")]
    [InlineData("cnc.factory.local.")]   // trailing dot is a legal FQDN form
    [InlineData("machine1")]
    [InlineData("a")]
    public void IsValid_HostName_IsAccepted(string host)
    {
        HostAddressValidation.IsValid(host).Should().BeTrue();
    }

    [Fact]
    public void IsValid_IPv6_IsAccepted()
    {
        HostAddressValidation.IsValid("fe80::1").Should().BeTrue();
    }

    // ─── Rejections ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_Blank_IsRejected(string? host)
    {
        HostAddressValidation.IsValid(host).Should().BeFalse();
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.101.5")]
    [InlineData("192.168.01.1")]      // leading zero reads as octal to some resolvers
    [InlineData("192.168..1")]
    [InlineData("-cnc")]
    [InlineData("cnc-")]
    [InlineData("cnc_line")]          // underscore is not legal in a host name
    [InlineData("192.168.1.101 ")]    // trailing space alone is fine…
    public void IsValid_MalformedAddress_IsRejectedOrTrimmed(string host)
    {
        // …so assert the trimmed-but-valid case separately from the rest.
        if (host.Trim() == "192.168.1.101")
        {
            HostAddressValidation.IsValid(host).Should().BeTrue();
            return;
        }

        HostAddressValidation.IsValid(host).Should().BeFalse();
    }

    [Fact]
    public void IsValid_EmbeddedSpace_IsRejected()
    {
        HostAddressValidation.IsValid("192.168.1.101 extra").Should().BeFalse();
    }

    [Fact]
    public void Describe_ValidAddress_ReturnsNull()
    {
        HostAddressValidation.Describe("192.168.1.101").Should().BeNull();
    }

    [Fact]
    public void Describe_Blank_AsksForAnAddress()
    {
        HostAddressValidation.Describe("").Should().Contain("192.168.1.101");
    }

    [Fact]
    public void Describe_UsesOperatorLanguage_NotParserVocabulary()
    {
        var message = HostAddressValidation.Describe("cnc_line");

        message.Should().NotBeNull();
        message.Should().NotContainAny("URI", "parse", "literal", "RFC");
    }
}
