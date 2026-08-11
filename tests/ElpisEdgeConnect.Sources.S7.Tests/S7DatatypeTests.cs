// ============================================================================
// Tests: S7Datatype + S7DatatypeSpec + S7DatatypeParser
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public class S7DatatypeTests
{
    [Theory]
    [InlineData("bool", S7Datatype.Bool, 1, CanonicalValueType.Boolean)]
    [InlineData("byte", S7Datatype.Byte, 1, CanonicalValueType.Integer)]
    [InlineData("sint", S7Datatype.SInt, 1, CanonicalValueType.Integer)]
    [InlineData("usint", S7Datatype.USInt, 1, CanonicalValueType.Integer)]
    [InlineData("int", S7Datatype.Int, 2, CanonicalValueType.Integer)]
    [InlineData("word", S7Datatype.Word, 2, CanonicalValueType.Integer)]
    [InlineData("uint", S7Datatype.Word, 2, CanonicalValueType.Integer)]
    [InlineData("dint", S7Datatype.DInt, 4, CanonicalValueType.Integer)]
    [InlineData("dword", S7Datatype.DWord, 4, CanonicalValueType.Long)]
    [InlineData("udint", S7Datatype.DWord, 4, CanonicalValueType.Long)]
    [InlineData("real", S7Datatype.Real, 4, CanonicalValueType.Float)]
    [InlineData("lreal", S7Datatype.LReal, 8, CanonicalValueType.Double)]
    [InlineData("lint", S7Datatype.LInt, 8, CanonicalValueType.Long)]
    [InlineData("char", S7Datatype.Char, 1, CanonicalValueType.String)]
    public void Parse_Primitives(string raw, S7Datatype expectedType, int expectedBytes, CanonicalValueType expectedCanonical)
    {
        var spec = S7DatatypeParser.Parse(raw, default);
        spec.Datatype.Should().Be(expectedType);
        spec.ByteCount.Should().Be(expectedBytes);
        spec.CanonicalType.Should().Be(expectedCanonical);
    }

    [Fact]
    public void Parse_String_RespectsBracketedLength()
    {
        var spec = S7DatatypeParser.Parse("string[16]", default);
        spec.Datatype.Should().Be(S7Datatype.String);
        spec.MaxStringChars.Should().Be(16);
        spec.ByteCount.Should().Be(2 + 16); // 2-byte header + 16 chars
        spec.CanonicalType.Should().Be(CanonicalValueType.String);
    }

    [Fact]
    public void Parse_String_RequiresExplicitLength()
    {
        var act = () => S7DatatypeParser.Parse("string", default);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*string datatype must declare a length*");
    }

    [Fact]
    public void Parse_UnknownDatatype_Throws()
    {
        var act = () => S7DatatypeParser.Parse("widget", default);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*Unknown S7 datatype*");
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsDefault()
    {
        var fallback = new S7DatatypeSpec(S7Datatype.Real);
        S7DatatypeParser.Parse(null, fallback).Should().Be(fallback);
        S7DatatypeParser.Parse("", fallback).Should().Be(fallback);
        S7DatatypeParser.Parse("   ", fallback).Should().Be(fallback);
    }

    [Theory]
    [InlineData(S7Datatype.Bool, false)]
    [InlineData(S7Datatype.String, false)]
    [InlineData(S7Datatype.Char, false)]
    [InlineData(S7Datatype.Int, true)]
    [InlineData(S7Datatype.Real, true)]
    [InlineData(S7Datatype.LReal, true)]
    public void SupportsScaleOffset_IsTrueForNumericsOnly(S7Datatype dt, bool expected)
    {
        var spec = new S7DatatypeSpec(dt, dt == S7Datatype.String ? 8 : 0);
        spec.SupportsScaleOffset.Should().Be(expected);
    }
}
