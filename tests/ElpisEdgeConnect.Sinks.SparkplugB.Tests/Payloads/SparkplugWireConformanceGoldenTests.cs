// ============================================================================
// File: Payloads/SparkplugWireConformanceGoldenTests.cs
// Purpose: The K2 WIRE-CONFORMANCE GOLDEN SUITE for the EdgeConnect node-only
//          Sparkplug 3.0 profile — the slice-5 closing gate over the full
//          plan-v2 §6 canary list. Every assertion reads encoder-produced
//          bytes through the INDEPENDENT proto2 decoder. This suite proves
//          conformance to the project's pinned subset and wire invariants; it
//          is NOT the official Sparkplug TCK and must never be described as
//          certified compatibility (plan v2 §5 wording).
//          Complements the slice-2/3/4 tests, closing the remaining canaries:
//          NDATA payload timestamp, byte-level signed min/max, empty-vs-null
//          arms, physically-present zero Float/Double, Stale/Unknown quality
//          bytes, acquisition-vs-publication timestamps, and exact
//          field-number sets against the pinned proto.
// Reference: ADR-0035 Rules 2/5 (as amended); plan v3 (frozen);
//            docs/compliance/sparkplug-b-wire-conformance.md.
// ============================================================================

using System.Text;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Tests.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Payloads;

public sealed class SparkplugWireConformanceGoldenTests
{
    private static readonly DateTimeOffset Publication = DateTimeOffset.UnixEpoch.AddSeconds(100);
    private static readonly DateTimeOffset Acquisition = DateTimeOffset.UnixEpoch.AddSeconds(50);
    private static readonly SparkplugSequenceNumber Seq0 = SparkplugSequenceNumber.Create(0);
    private static readonly SparkplugBirthDeathSequence BdSeq = SparkplugBirthDeathSequence.Create(3);
    private static readonly SparkplugAliasKey Key = SparkplugAliasKey.Create("cnc", "m1", "tag");

    private static readonly IReadOnlyDictionary<SparkplugAliasKey, ulong> Map =
        new Dictionary<SparkplugAliasKey, ulong> { [Key] = 5 };

    private static SparkplugMetricSample Sample(CanonicalValueType type, object? value, bool isNull = false,
        DataQuality quality = DataQuality.Good) => new()
    {
        Key = Key,
        ValueType = type,
        Value = value,
        IsNull = isNull,
        AcquisitionTimestamp = Acquisition,
        Quality = quality,
    };

    private static byte[] NData(SparkplugMetricSample sample) =>
        SparkplugPayloadEncoder.EncodeNData(Seq0, Publication, [sample], Map, isHistorical: false);

    private static byte[] NBirth(SparkplugMetricSample sample) =>
        SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq, 1, Publication, [sample], Map);

    private static IReadOnlyList<ProtoWireField> SingleMetric(byte[] payload) =>
        ProtoWireDecoder.Decode(ProtoWireDecoder.Decode(payload).Single(f => f.FieldNumber == 2).LengthDelimitedBytes);

    private static IReadOnlyList<ProtoWireField> AppMetric(byte[] birthPayload) =>
        ProtoWireDecoder.Decode(ProtoWireDecoder.Decode(birthPayload).Where(f => f.FieldNumber == 2).Skip(2).Single().LengthDelimitedBytes);

    private static ProtoWireField? Field(IReadOnlyList<ProtoWireField> fields, int number) =>
        fields.SingleOrDefault(f => f.FieldNumber == number);

    // ==== Payload-level presence ====

    [Fact]
    public void NData_PayloadTimestamp_IsPhysicallyPresentWithPublicationInstant()
    {
        var fields = ProtoWireDecoder.Decode(NData(Sample(CanonicalValueType.Integer, 1)));

        Field(fields, 1)!.VarintValue.Should().Be(100_000UL, "the NDATA payload timestamp is the publication instant");
    }

    [Fact]
    public void NBirth_PayloadFieldOccurrences_MatchTheExactCardinalityProfile()
    {
        // One-application-metric fixture: the exact occurrence profile is
        // timestamp(1) x1, metrics(2) x3 (bdSeq, Node Control/Rebirth, app),
        // seq(3) x1, nothing else. Cardinality-aware (slice-5 review r1): a
        // duplicated singular field or an extra metric fails, not only an
        // unknown field number.
        var fields = ProtoWireDecoder.Decode(NBirth(Sample(CanonicalValueType.Integer, 1)));

        var occurrences = fields.GroupBy(f => f.FieldNumber)
            .ToDictionary(g => g.Key, g => g.Count());

        occurrences.Should().BeEquivalentTo(new Dictionary<int, int>
        {
            [1] = 1, // payload timestamp — exactly once
            [2] = 3, // metrics: bdSeq, Node Control/Rebirth, the application metric
            [3] = 1, // seq — exactly once
        }, "an NBIRTH payload carries exactly one timestamp, one seq, and the expected metric count — no uuid, no body, no duplicated singular fields");
    }

    [Fact]
    public void NBirth_AppMetric_CarriesAcquisitionTimestampDistinctFromPublication()
    {
        var metric = AppMetric(NBirth(Sample(CanonicalValueType.Integer, 1)));

        Field(metric, 3)!.VarintValue.Should().Be(50_000UL,
            "the metric timestamp is the ACQUISITION instant; the payload timestamp is the publication instant (ADR-0035 Rule 5)");
    }

    // ==== Byte-level signed integer canaries (plan v2 §3.2) ====

    [Theory]
    [InlineData(-1, 0xFFFFFFFFUL)]
    [InlineData(int.MinValue, 0x80000000UL)]
    [InlineData(int.MaxValue, 0x7FFFFFFFUL)]
    public void NData_Int32_IsBitPreservedInTheUint32ArmOnTheWire(int value, ulong expectedWireValue)
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Integer, value)));

        Field(metric, 10)!.VarintValue.Should().Be(expectedWireValue,
            "signed Int32 is written bit-preserving into uint32 int_value; the consumer interprets per the Sparkplug datatype");
        Field(metric, 4)!.VarintValue.Should().Be(3UL, "datatype Int32");
    }

    [Theory]
    [InlineData(-1L, 0xFFFFFFFFFFFFFFFFUL)]
    [InlineData(long.MinValue, 0x8000000000000000UL)]
    [InlineData(long.MaxValue, 0x7FFFFFFFFFFFFFFFUL)]
    public void NData_Int64_IsBitPreservedInTheUint64ArmOnTheWire(long value, ulong expectedWireValue)
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Long, value)));

        Field(metric, 11)!.VarintValue.Should().Be(expectedWireValue);
        Field(metric, 4)!.VarintValue.Should().Be(4UL, "datatype Int64");
    }

    // ==== Empty vs null; zero values physically present ====

    [Fact]
    public void NData_EmptyString_IsAPresentZeroLengthStringArm()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.String, string.Empty)));

        var arm = Field(metric, 15);
        arm.Should().NotBeNull("an empty string is a present value, not absence");
        arm!.LengthDelimitedBytes.Should().BeEmpty();
        Field(metric, 7).Should().BeNull("an empty string is not null");
    }

    [Fact]
    public void NData_ZeroLengthByteArray_IsAPresentZeroLengthBytesArm()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.ByteArray, Array.Empty<byte>())));

        var arm = Field(metric, 16);
        arm.Should().NotBeNull("a zero-length byte array is a present value, not absence");
        arm!.LengthDelimitedBytes.Should().BeEmpty();
        Field(metric, 7).Should().BeNull();
    }

    [Fact]
    public void NData_NullValue_HasIsNullAndNoValueArmBytes()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.String, null, isNull: true)));

        Field(metric, 7)!.VarintValue.Should().Be(1UL);
        metric.Select(f => f.FieldNumber).Should().NotContain([10, 11, 12, 13, 14, 15, 16],
            "a null metric carries no value arm of any kind");
    }

    [Fact]
    public void NData_ZeroFloat_IsAPhysicallyPresentFixed32Field()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Float, 0.0f)));

        var arm = Field(metric, 12);
        arm.Should().NotBeNull("0.0f must be physically encoded, never confused with absence");
        arm!.WireType.Should().Be(ProtoWireType.Fixed32);
        arm.Fixed32Bits.Should().Be(0U);
    }

    [Fact]
    public void NData_ZeroDouble_IsAPhysicallyPresentFixed64Field()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Double, 0.0d)));

        var arm = Field(metric, 13);
        arm.Should().NotBeNull("0.0d must be physically encoded, never confused with absence");
        arm!.WireType.Should().Be(ProtoWireType.Fixed64);
        arm.Fixed64Bits.Should().Be(0UL);
    }

    // ==== Quality bytes (frozen table, ADR-0035 Rule 5 as amended) ====

    [Fact]
    public void NData_StaleQuality_CarriesQuality500AndNoReason()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Integer, 1, quality: DataQuality.Stale)));

        var properties = ProtoWireDecoder.Decode(Field(metric, 9)!.LengthDelimitedBytes);
        var keys = properties.Where(f => f.FieldNumber == 1).Select(f => Encoding.UTF8.GetString(f.LengthDelimitedBytes));
        keys.Should().Equal(["Quality"], "Stale maps losslessly to its code; no QualityReason in v1");

        var value = ProtoWireDecoder.Decode(properties.Single(f => f.FieldNumber == 2).LengthDelimitedBytes);
        Field(value, 3)!.VarintValue.Should().Be(500UL);
    }

    [Fact]
    public void NData_UnknownQuality_CarriesExactlyTheOrderedQualityAndReasonPropertyPairs()
    {
        var metric = SingleMetric(NData(Sample(CanonicalValueType.Integer, 1, quality: DataQuality.Unknown)));

        var properties = ProtoWireDecoder.Decode(Field(metric, 9)!.LengthDelimitedBytes);
        var keys = properties.Where(f => f.FieldNumber == 1)
            .Select(f => Encoding.UTF8.GetString(f.LengthDelimitedBytes)).ToList();
        var values = properties.Where(f => f.FieldNumber == 2)
            .Select(f => (IReadOnlyList<ProtoWireField>)ProtoWireDecoder.Decode(f.LengthDelimitedBytes)).ToList();

        // Exact counts and ordered key/value PAIRING (slice-5 review r1): the
        // frozen deterministic property order is part of the wire contract,
        // and no extra property may slip through the closing gate.
        keys.Should().Equal(["Quality", "QualityReason"]);
        values.Should().HaveCount(2);

        Field(values[0], 1)!.VarintValue.Should().Be(3UL, "keys[0]=Quality pairs with an Int32-typed PropertyValue");
        Field(values[0], 3)!.VarintValue.Should().Be(0UL, "Unknown maps to BAD=0; the zero is physically present");

        Field(values[1], 1)!.VarintValue.Should().Be(12UL, "keys[1]=QualityReason pairs with a String-typed PropertyValue");
        Encoding.UTF8.GetString(Field(values[1], 8)!.LengthDelimitedBytes).Should().Be("quality unknown",
            "only the controlled wire values are ever published (owner-frozen contract)");
    }

    // ==== Exact metric field-number sets against the pinned proto ====

    [Fact]
    public void NBirth_FullyLoadedAppMetric_HasTheExactExpectedFieldNumberSet()
    {
        var metric = AppMetric(NBirth(Sample(CanonicalValueType.Integer, 9, quality: DataQuality.Bad)));

        metric.Select(f => f.FieldNumber).Should().BeEquivalentTo([1, 2, 3, 4, 9, 10],
            "name(1), alias(2), timestamp(3), datatype(4), properties(9), int_value(10) — and nothing else");
    }

    [Fact]
    public void NBirth_NullAppMetric_HasTheExactExpectedFieldNumberSet()
    {
        var metric = AppMetric(NBirth(Sample(CanonicalValueType.Integer, null, isNull: true, quality: DataQuality.Bad)));

        metric.Select(f => f.FieldNumber).Should().BeEquivalentTo([1, 2, 3, 4, 7, 9],
            "a null metric swaps the value arm for is_null(7); everything else is unchanged");
    }
}
