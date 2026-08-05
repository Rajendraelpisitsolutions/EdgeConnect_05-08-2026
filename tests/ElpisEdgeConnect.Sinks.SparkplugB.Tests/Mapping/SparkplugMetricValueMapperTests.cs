// ============================================================================
// File: Mapping/SparkplugMetricValueMapperTests.cs
// Purpose: Locks the validated-model stage (plan v2 §3.8): bit-preserving
//          signed encoding (the Blocker-2 golden values), null invariant,
//          CLR type agreement, DateTime value conversion, empty-vs-null
//          distinctions, and quality/timestamp propagation. Every rejection
//          path carries its SPARKPLUG.* code.
// ============================================================================

using System.Collections.Immutable;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Mapping;

public sealed class SparkplugMetricValueMapperTests
{
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(42);

    private static SparkplugMetricValueModel Map(CanonicalValueType type, object? value, bool isNull = false, DataQuality quality = DataQuality.Good) =>
        SparkplugMetricValueMapper.Map(type, value, isNull, Timestamp, quality);

    // ==== Bit-preserving signed integers (plan v2 §3.2 golden values) ====

    [Theory]
    [InlineData(-1, 0xFFFFFFFFU)]
    [InlineData(int.MinValue, 0x80000000U)]
    [InlineData(int.MaxValue, 0x7FFFFFFFU)]
    [InlineData(0, 0x00000000U)]
    public void Map_Int32_IsBitPreservedIntoUnsignedArm(int value, uint expectedBits)
    {
        var model = Map(CanonicalValueType.Integer, value);

        model.DataType.Should().Be(SparkplugDataType.Int32);
        model.UInt32Bits.Should().Be(expectedBits);
    }

    [Theory]
    [InlineData(-1L, 0xFFFFFFFFFFFFFFFFUL)]
    [InlineData(long.MinValue, 0x8000000000000000UL)]
    [InlineData(long.MaxValue, 0x7FFFFFFFFFFFFFFFUL)]
    public void Map_Int64_IsBitPreservedIntoUnsignedArm(long value, ulong expectedBits)
    {
        var model = Map(CanonicalValueType.Long, value);

        model.DataType.Should().Be(SparkplugDataType.Int64);
        model.UInt64Bits.Should().Be(expectedBits);
    }

    // ==== Zero/false/empty are real present values, never null ====

    [Fact]
    public void Map_FalseBoolean_IsAPresentValue()
    {
        var model = Map(CanonicalValueType.Boolean, false);

        model.IsNull.Should().BeFalse();
        model.BooleanValue.Should().BeFalse();
    }

    [Fact]
    public void Map_ZeroFloatAndDouble_ArePresentValues()
    {
        Map(CanonicalValueType.Float, 0.0f).FloatValue.Should().Be(0.0f);
        Map(CanonicalValueType.Double, 0.0d).DoubleValue.Should().Be(0.0d);
    }

    [Fact]
    public void Map_EmptyString_IsAPresentValueDistinctFromNull()
    {
        var model = Map(CanonicalValueType.String, string.Empty);

        model.IsNull.Should().BeFalse();
        model.StringValue.Should().Be(string.Empty);
    }

    [Fact]
    public void Map_ZeroLengthByteArray_IsAPresentValueDistinctFromNull()
    {
        var model = Map(CanonicalValueType.ByteArray, Array.Empty<byte>());

        model.IsNull.Should().BeFalse();
        model.BytesValue.Should().NotBeNull();
        model.BytesValue!.Value.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Map_ImmutableByteArray_FromSnapshotStoredForm_IsAccepted()
    {
        var model = Map(CanonicalValueType.ByteArray, ImmutableArray.Create<byte>(1, 2, 3));

        model.BytesValue!.Value.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Map_MutableByteArrayInput_IsCopied_SoLaterCallerMutationCannotReachTheModel()
    {
        var input = new byte[] { 1, 2, 3 };

        var model = Map(CanonicalValueType.ByteArray, input);
        input[0] = 0xFF;

        model.BytesValue!.Value.Should().Equal(new byte[] { 1, 2, 3 }, "the validated model must hold an immutable copy");
    }

    // ==== DateTime values use the frozen range rules ====

    [Fact]
    public void Map_DateTimeValue_BecomesWholeUnixMilliseconds()
    {
        var model = Map(CanonicalValueType.DateTime, new DateTime(1970, 1, 1, 0, 0, 2, DateTimeKind.Utc));

        model.DataType.Should().Be(SparkplugDataType.DateTime);
        model.UInt64Bits.Should().Be(2000UL);
    }

    [Fact]
    public void Map_PreEpochDateTimeValue_ThrowsTypedPreEpochError()
    {
        var act = () => Map(CanonicalValueType.DateTime, new DateTime(1960, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampPreEpoch);
    }

    // ==== Null handling (is_null with declared type, no value arm) ====

    [Fact]
    public void Map_ExplicitNull_KeepsDeclaredTypeAndSetsNoValueArm()
    {
        var model = Map(CanonicalValueType.Integer, value: null, isNull: true, quality: DataQuality.Bad);

        model.IsNull.Should().BeTrue();
        model.DataType.Should().Be(SparkplugDataType.Int32, "a known-null metric still declares its real datatype");
        model.UInt32Bits.Should().BeNull();
        model.Quality.Should().Be(0);
    }

    [Theory]
    [InlineData(DataQuality.Uncertain)]
    [InlineData(DataQuality.Unknown)]
    public void Map_NullMetricWithLossyQuality_KeepsQualityCodeButNeverCarriesQualityReason(DataQuality quality)
    {
        var model = Map(CanonicalValueType.Integer, value: null, isNull: true, quality: quality);

        model.IsNull.Should().BeTrue();
        model.Quality.Should().Be(0);
        model.QualityReason.Should().BeNull("the frozen contract omits QualityReason for null handling (ADR-0035 Rule 5 amendment)");
    }

    [Fact]
    public void Map_NullInvariantViolation_ReportsBeforeTimestampConversion()
    {
        // Both violations present: null invariant AND a pre-epoch timestamp.
        // The fundamental invariant must attribute the error, not the timestamp.
        var act = () => SparkplugMetricValueMapper.Map(
            CanonicalValueType.Integer, value: 5, isNull: true,
            DateTimeOffset.UnixEpoch.AddTicks(-1), DataQuality.Good);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeNullInvariant);
    }

    [Fact]
    public void Map_IsNullTrueWithValue_ThrowsNullInvariantError()
    {
        var act = () => Map(CanonicalValueType.Integer, value: 5, isNull: true);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeNullInvariant);
    }

    [Fact]
    public void Map_IsNullFalseWithoutValue_ThrowsNullInvariantError()
    {
        var act = () => Map(CanonicalValueType.Integer, value: null, isNull: false);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeNullInvariant);
    }

    // ==== Type agreement and unmappable types ====

    [Fact]
    public void Map_ClrTypeMismatch_ThrowsTypedMismatchError()
    {
        var act = () => Map(CanonicalValueType.Integer, "not an int");

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeValueTypeMismatch);
    }

    [Fact]
    public void Map_LongValueForIntegerType_IsRejectedNotCoerced()
    {
        var act = () => Map(CanonicalValueType.Integer, 5L);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeValueTypeMismatch);
    }

    [Theory]
    [InlineData(CanonicalValueType.Array)]
    [InlineData(CanonicalValueType.Object)]
    public void Map_UnmappableType_ThrowsBeforeAnyOtherValidation(CanonicalValueType type)
    {
        var act = () => Map(type, new object());

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeUnmappableDatatype);
    }

    // ==== Quality and timestamp propagation ====

    [Fact]
    public void Map_UncertainQuality_CarriesControlledReasonAndBadCode()
    {
        var model = Map(CanonicalValueType.Double, 1.5, quality: DataQuality.Uncertain);

        model.Quality.Should().Be(0);
        model.QualityReason.Should().Be("quality uncertain");
    }

    [Fact]
    public void Map_GoodQuality_OmitsQualityAndReason()
    {
        var model = Map(CanonicalValueType.Double, 1.5);

        model.Quality.Should().BeNull();
        model.QualityReason.Should().BeNull();
    }

    [Fact]
    public void Map_AcquisitionTimestamp_BecomesWholeMillisecondMetricTimestamp()
    {
        var model = Map(CanonicalValueType.Boolean, true);

        model.TimestampMs.Should().Be(42_000UL);
    }
}
