using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpElementTypeTests
{
    [Theory]
    [InlineData("BOOL", EthernetIpElementType.Bool)]
    [InlineData("bool", EthernetIpElementType.Bool)]
    [InlineData("SINT", EthernetIpElementType.Sint)]
    [InlineData("INT", EthernetIpElementType.Int)]
    [InlineData("DINT", EthernetIpElementType.Dint)]
    [InlineData("LINT", EthernetIpElementType.Lint)]
    [InlineData("REAL", EthernetIpElementType.Real)]
    [InlineData("lreal", EthernetIpElementType.Lreal)]
    [InlineData("STRING", EthernetIpElementType.String)]
    public void Parse_KnownTypes_ReturnsExpected(string input, EthernetIpElementType expected)
    {
        EthernetIpElementTypeExtensions.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("FOO")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseOrNull_UnknownOrEmpty_ReturnsNull(string? input)
    {
        EthernetIpElementTypeExtensions.ParseOrNull(input).Should().BeNull();
    }

    [Theory]
    [InlineData(EthernetIpElementType.Bool, CanonicalValueType.Boolean)]
    [InlineData(EthernetIpElementType.Sint, CanonicalValueType.Integer)]
    [InlineData(EthernetIpElementType.Int, CanonicalValueType.Integer)]
    [InlineData(EthernetIpElementType.Dint, CanonicalValueType.Integer)]
    [InlineData(EthernetIpElementType.Lint, CanonicalValueType.Long)]
    [InlineData(EthernetIpElementType.Real, CanonicalValueType.Float)]
    [InlineData(EthernetIpElementType.Lreal, CanonicalValueType.Double)]
    [InlineData(EthernetIpElementType.String, CanonicalValueType.String)]
    public void CanonicalType_MapsCorrectly(EthernetIpElementType type, CanonicalValueType expected)
    {
        type.CanonicalType().Should().Be(expected);
    }

    [Theory]
    [InlineData(EthernetIpElementType.Bool, false)]
    [InlineData(EthernetIpElementType.String, false)]
    [InlineData(EthernetIpElementType.Dint, true)]
    [InlineData(EthernetIpElementType.Real, true)]
    public void SupportsScaleOffset_OnlyNumeric(EthernetIpElementType type, bool expected)
    {
        type.SupportsScaleOffset().Should().Be(expected);
    }
}

public class EthernetIpCpuFamilyTests
{
    [Theory]
    [InlineData(EthernetIpCpuFamily.ControlLogix, "1,0")]
    [InlineData(EthernetIpCpuFamily.CompactLogix, "1,0")]
    [InlineData(EthernetIpCpuFamily.GuardLogix, "1,0")]
    [InlineData(EthernetIpCpuFamily.MicroLogix, "")]
    [InlineData(EthernetIpCpuFamily.Micro800, "")]
    public void DefaultPath_LogixGetsBackplanePath_EmbeddedGetsEmpty(EthernetIpCpuFamily family, string expected)
    {
        family.DefaultPath().Should().Be(expected);
    }

    [Theory]
    [InlineData(EthernetIpCpuFamily.ControlLogix, "controllogix")]
    [InlineData(EthernetIpCpuFamily.CompactLogix, "controllogix")]
    [InlineData(EthernetIpCpuFamily.Micro800, "micro800")]
    [InlineData(EthernetIpCpuFamily.MicroLogix, "micrologix")]
    public void LibPlcTagToken_MapsCorrectly(EthernetIpCpuFamily family, string expected)
    {
        family.LibPlcTagToken().Should().Be(expected);
    }

    [Theory]
    [InlineData("ControlLogix", EthernetIpCpuFamily.ControlLogix)]
    [InlineData("micro800", EthernetIpCpuFamily.Micro800)]
    [InlineData("bogus", null)]
    public void ParseOrNull_Works(string input, EthernetIpCpuFamily? expected)
    {
        EthernetIpCpuFamilyExtensions.ParseOrNull(input).Should().Be(expected);
    }
}
