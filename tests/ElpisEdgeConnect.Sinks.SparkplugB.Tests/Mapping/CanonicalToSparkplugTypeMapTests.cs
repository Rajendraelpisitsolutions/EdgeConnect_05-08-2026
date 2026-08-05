// ============================================================================
// File: Mapping/CanonicalToSparkplugTypeMapTests.cs
// Purpose: Locks the pinned datatype table (ADR-0035 Rule 5). The first-party
//          SparkplugDataType members are asserted EQUAL to the generated
//          .proto DataType enum values — that cross-check is what makes the
//          first-party pin meaningful. Array/Object/Null/undefined are typed
//          rejections, never silent drops.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Mapping;

public sealed class CanonicalToSparkplugTypeMapTests
{
    [Theory]
    [InlineData(CanonicalValueType.Boolean, SparkplugDataType.Boolean)]
    [InlineData(CanonicalValueType.Integer, SparkplugDataType.Int32)]
    [InlineData(CanonicalValueType.Long, SparkplugDataType.Int64)]
    [InlineData(CanonicalValueType.Float, SparkplugDataType.Float)]
    [InlineData(CanonicalValueType.Double, SparkplugDataType.Double)]
    [InlineData(CanonicalValueType.String, SparkplugDataType.String)]
    [InlineData(CanonicalValueType.DateTime, SparkplugDataType.DateTime)]
    [InlineData(CanonicalValueType.ByteArray, SparkplugDataType.Bytes)]
    public void Map_SupportedType_ReturnsPinnedSparkplugDatatype(CanonicalValueType canonical, SparkplugDataType expected)
    {
        CanonicalToSparkplugTypeMap.Map(canonical).Should().Be(expected);
    }

    [Fact]
    public void SparkplugDataType_EveryMember_EqualsItsGeneratedProtoEnumValue()
    {
        // The generated enum is internal (internal_access), so the pairs live in
        // the method body rather than a public theory signature. This is the
        // cross-check that makes the first-party pin meaningful.
        var pairs = new (SparkplugDataType FirstParty, Org.Eclipse.Tahu.Protobuf.DataType Generated)[]
        {
            (SparkplugDataType.Int32, Org.Eclipse.Tahu.Protobuf.DataType.Int32),
            (SparkplugDataType.Int64, Org.Eclipse.Tahu.Protobuf.DataType.Int64),
            (SparkplugDataType.Float, Org.Eclipse.Tahu.Protobuf.DataType.Float),
            (SparkplugDataType.Double, Org.Eclipse.Tahu.Protobuf.DataType.Double),
            (SparkplugDataType.Boolean, Org.Eclipse.Tahu.Protobuf.DataType.Boolean),
            (SparkplugDataType.String, Org.Eclipse.Tahu.Protobuf.DataType.String),
            (SparkplugDataType.DateTime, Org.Eclipse.Tahu.Protobuf.DataType.DateTime),
            (SparkplugDataType.Bytes, Org.Eclipse.Tahu.Protobuf.DataType.Bytes),
        };

        pairs.Should().HaveCount(Enum.GetValues<SparkplugDataType>().Length, "every first-party member must be cross-checked");
        foreach (var (firstParty, generated) in pairs)
        {
            ((uint)firstParty).Should().Be((uint)generated,
                $"the first-party datatype pin for {firstParty} must track the vendored .proto DataType enum");
        }
    }

    [Theory]
    [InlineData(CanonicalValueType.Null)]
    [InlineData(CanonicalValueType.Array)]
    [InlineData(CanonicalValueType.Object)]
    [InlineData((CanonicalValueType)999)]
    public void Map_UnmappableType_ThrowsTypedUnmappableDatatypeError(CanonicalValueType canonical)
    {
        var act = () => CanonicalToSparkplugTypeMap.Map(canonical);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeUnmappableDatatype);
    }

    [Fact]
    public void Map_ArrayType_ErrorIsNotRetryableConfigurationCategory()
    {
        var act = () => CanonicalToSparkplugTypeMap.Map(CanonicalValueType.Array);

        var error = act.Should().Throw<AdapterException>().Which.Error;
        error.Category.Should().Be(ErrorCategory.Configuration);
        error.Retryable.Should().BeFalse();
    }
}
