// ============================================================================
// File: Mapping/SparkplugQualityTests.cs
// Purpose: Locks the frozen quality table (ADR-0035 Rule 5 as amended
//          2026-07-19): Good omits Quality (never 192), Bad=0, Stale=500,
//          Uncertain/Unknown = 0 + the CONTROLLED QualityReason wire values.
//          A violation of the owner-frozen mapping fails here by name.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Mapping;

public sealed class SparkplugQualityTests
{
    [Fact]
    public void Map_Good_OmitsQualityAndReason()
    {
        var result = SparkplugQuality.Map(DataQuality.Good);

        result.Quality.Should().BeNull("Good is expressed by omitting the Quality property, never by emitting 192");
        result.QualityReason.Should().BeNull();
    }

    [Fact]
    public void Map_Bad_IsZeroWithNoReason()
    {
        var result = SparkplugQuality.Map(DataQuality.Bad);

        result.Quality.Should().Be(0);
        result.QualityReason.Should().BeNull("Bad maps losslessly to code 0; no reason property in v1");
    }

    [Fact]
    public void Map_Stale_IsFiveHundredWithNoReason()
    {
        var result = SparkplugQuality.Map(DataQuality.Stale);

        result.Quality.Should().Be(500);
        result.QualityReason.Should().BeNull("Stale maps losslessly to code 500; no reason property in v1");
    }

    [Fact]
    public void Map_Uncertain_IsZeroWithControlledReason()
    {
        var result = SparkplugQuality.Map(DataQuality.Uncertain);

        result.Quality.Should().Be(0);
        result.QualityReason.Should().Be("quality uncertain");
    }

    [Fact]
    public void Map_Unknown_IsZeroWithControlledReason()
    {
        var result = SparkplugQuality.Map(DataQuality.Unknown);

        result.Quality.Should().Be(0);
        result.QualityReason.Should().Be("quality unknown");
    }

    [Fact]
    public void Map_UndefinedQuality_ThrowsTypedError()
    {
        var act = () => SparkplugQuality.Map((DataQuality)99);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeQualityUndefined);
    }

    [Theory]
    [InlineData(DataQuality.Good)]
    [InlineData(DataQuality.Bad)]
    [InlineData(DataQuality.Stale)]
    [InlineData(DataQuality.Uncertain)]
    [InlineData(DataQuality.Unknown)]
    public void Map_AnyDefinedQuality_NeverEmitsAnEmptyReasonOrCode192(DataQuality quality)
    {
        var result = SparkplugQuality.Map(quality);

        result.QualityReason.Should().NotBe(string.Empty, "the frozen contract forbids an empty-string QualityReason property");
        result.Quality.Should().NotBe(192, "GOOD is expressed by omission; 192 is never emitted");
    }
}
