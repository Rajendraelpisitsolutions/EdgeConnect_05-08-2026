// ============================================================================
// File: Wire/ProtoWireDecoderTests.cs
// Purpose: Self-tests for the independent proto2 wire decoder against
//          HAND-COMPUTED byte vectors (never produced by any protobuf library —
//          the decoder's correctness must not be established by the code it is
//          meant to check). Field numbers in comments reference the pinned
//          sparkplug_b.proto layout where relevant (Payload.seq=3,
//          Metric.is_null=7, Metric.int_value=10, float_value=12,
//          double_value=13, string_value=15, bytes_value=16).
// Reference: plan v3 (frozen) §slice 2 exit evidence.
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Wire;

public sealed class ProtoWireDecoderTests
{
    [Fact]
    public void Decode_SingleByteVarintField_ReturnsFieldNumberWireTypeAndValue()
    {
        // Field 1, varint: tag (1<<3)|0 = 0x08; 150 = 0x96 0x01 (the canonical protobuf example).
        var fields = ProtoWireDecoder.Decode(new byte[] { 0x08, 0x96, 0x01 });

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(1);
        field.WireType.Should().Be(ProtoWireType.Varint);
        field.VarintValue.Should().Be(150UL);
    }

    [Fact]
    public void Decode_ZeroValuedVarint_IsPhysicallyPresent()
    {
        // Payload.seq (field 3), varint zero: tag 0x18, value 0x00 — presence, not default.
        var fields = ProtoWireDecoder.Decode(new byte[] { 0x18, 0x00 });

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(3);
        field.WireType.Should().Be(ProtoWireType.Varint);
        field.VarintValue.Should().Be(0UL);
    }

    [Fact]
    public void Decode_TenByteVarint_YieldsAllSixtyFourBitsSet()
    {
        // Field 1: nine 0xFF continuation bytes + terminal 0x01 = ulong.MaxValue
        // (the two's-complement pattern of long -1 — the Blocker-2 bit-preserving case).
        var bytes = new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 };

        var fields = ProtoWireDecoder.Decode(bytes);

        fields.Should().ContainSingle().Which.VarintValue.Should().Be(ulong.MaxValue);
    }

    [Fact]
    public void Decode_Uint32MaxVarint_YieldsBitPreservedNegativeOnePattern()
    {
        // Metric.int_value (field 10): tag (10<<3)|0 = 0x50; uint32 0xFFFFFFFF
        // (bit-preserved Int32 -1) = varint FF FF FF FF 0F.
        var fields = ProtoWireDecoder.Decode(new byte[] { 0x50, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(10);
        field.VarintValue.Should().Be(uint.MaxValue);
    }

    [Fact]
    public void Decode_Fixed64Field_ReturnsRawLittleEndianBits()
    {
        // Metric.double_value (field 13), fixed64: tag (13<<3)|1 = 0x69; double 0.0 = eight zero bytes.
        var bytes = new byte[] { 0x69, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var fields = ProtoWireDecoder.Decode(bytes);

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(13);
        field.WireType.Should().Be(ProtoWireType.Fixed64);
        field.Fixed64Bits.Should().Be(0UL, "a physically-present 0.0 double is eight zero bytes, not absence");
    }

    [Fact]
    public void Decode_Fixed32Field_ReturnsRawLittleEndianBits()
    {
        // Metric.float_value (field 12), fixed32: tag (12<<3)|5 = 0x65; float 1.0f = 0x3F800000 LE.
        var bytes = new byte[] { 0x65, 0x00, 0x00, 0x80, 0x3F };

        var fields = ProtoWireDecoder.Decode(bytes);

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(12);
        field.WireType.Should().Be(ProtoWireType.Fixed32);
        field.Fixed32Bits.Should().Be(0x3F800000U);
        BitConverter.UInt32BitsToSingle(field.Fixed32Bits).Should().Be(1.0f);
    }

    [Fact]
    public void Decode_LengthDelimitedString_ReturnsPayloadBytes()
    {
        // Metric.string_value (field 15): tag (15<<3)|2 = 0x7A; "spBv1.0" (7 ASCII bytes).
        var bytes = new byte[] { 0x7A, 0x07, 0x73, 0x70, 0x42, 0x76, 0x31, 0x2E, 0x30 };

        var fields = ProtoWireDecoder.Decode(bytes);

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(15);
        field.WireType.Should().Be(ProtoWireType.LengthDelimited);
        System.Text.Encoding.UTF8.GetString(field.LengthDelimitedBytes).Should().Be("spBv1.0");
    }

    [Fact]
    public void Decode_EmptyLengthDelimited_IsPresentWithZeroLengthPayload()
    {
        // Metric.bytes_value (field 16): tag (16<<3)|2 = 130 = varint 0x82 0x01 (multi-byte tag), length 0.
        // Distinguishes an empty byte array (present, zero-length) from absence.
        var fields = ProtoWireDecoder.Decode(new byte[] { 0x82, 0x01, 0x00 });

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(16);
        field.WireType.Should().Be(ProtoWireType.LengthDelimited);
        field.LengthDelimitedBytes.Should().BeEmpty();
    }

    [Fact]
    public void Decode_RepeatedField_PreservesEncounterOrder()
    {
        // Field 2 three times with varint values 7, 5, 9: order must be preserved, not sorted or merged.
        var bytes = new byte[] { 0x10, 0x07, 0x10, 0x05, 0x10, 0x09 };

        var fields = ProtoWireDecoder.Decode(bytes);

        fields.Should().HaveCount(3);
        fields.Select(f => f.FieldNumber).Should().AllBeEquivalentTo(2);
        fields.Select(f => f.VarintValue).Should().ContainInOrder(7UL, 5UL, 9UL);
    }

    [Fact]
    public void Decode_UnknownFieldNumber_IsSkippedSafelyAndDecodingContinues()
    {
        // Unknown field 99 (tag (99<<3)|0 = 792 = 0x98 0x06) value 5, then known field 1 value 7:
        // the decoder must report both and never abort at the unknown number.
        var bytes = new byte[] { 0x98, 0x06, 0x05, 0x08, 0x07 };

        var fields = ProtoWireDecoder.Decode(bytes);

        fields.Should().HaveCount(2);
        fields[0].FieldNumber.Should().Be(99);
        fields[0].VarintValue.Should().Be(5UL);
        fields[1].FieldNumber.Should().Be(1);
        fields[1].VarintValue.Should().Be(7UL);
    }

    [Fact]
    public void Decode_NestedMessage_DecodesViaSecondPass()
    {
        // Payload.metrics (field 2, length-delimited): tag 0x12, length 2,
        // containing Metric.is_null (field 7) = true: tag 0x38, value 0x01.
        var bytes = new byte[] { 0x12, 0x02, 0x38, 0x01 };

        var outer = ProtoWireDecoder.Decode(bytes);
        var metric = outer.Should().ContainSingle().Subject;
        metric.FieldNumber.Should().Be(2);

        var inner = ProtoWireDecoder.Decode(metric.LengthDelimitedBytes);
        var isNull = inner.Should().ContainSingle().Subject;
        isNull.FieldNumber.Should().Be(7);
        isNull.WireType.Should().Be(ProtoWireType.Varint);
        isNull.VarintValue.Should().Be(1UL);
    }

    [Fact]
    public void Decode_EmptyBuffer_YieldsNoFields()
    {
        ProtoWireDecoder.Decode(ReadOnlySpan<byte>.Empty).Should().BeEmpty();
    }

    [Fact]
    public void Decode_TruncatedVarint_Throws()
    {
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x08, 0x96 });

        act.Should().Throw<InvalidDataException>().WithMessage("*Truncated varint*");
    }

    [Fact]
    public void Decode_LengthExceedingRemainingBytes_Throws()
    {
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x12, 0x05, 0x01 });

        act.Should().Throw<InvalidDataException>().WithMessage("*length 5*");
    }

    [Fact]
    public void Decode_TruncatedFixed64_Throws()
    {
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x69, 0x00, 0x00 });

        act.Should().Throw<InvalidDataException>().WithMessage("*Truncated fixed64*");
    }

    [Fact]
    public void Decode_VarintOverflowingSixtyFourBits_Throws()
    {
        // Ten continuation-style bytes where the 10th contributes more than bit 63.
        var bytes = new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02 };

        var act = () => ProtoWireDecoder.Decode(bytes);

        act.Should().Throw<InvalidDataException>().WithMessage("*overflows 64 bits*");
    }

    [Fact]
    public void Decode_FieldNumberZero_Throws()
    {
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x00 });

        act.Should().Throw<InvalidDataException>().WithMessage("*Invalid protobuf field number 0*");
    }

    [Fact]
    public void Decode_MaximumFieldNumber_IsAccepted()
    {
        // Field 536,870,911 ((1<<29)-1, the protobuf maximum), varint 0:
        // tag = 536870911<<3 = 0xFFFFFFF8 → varint F8 FF FF FF 0F; value 00.
        var fields = ProtoWireDecoder.Decode(new byte[] { 0xF8, 0xFF, 0xFF, 0xFF, 0x0F, 0x00 });

        var field = fields.Should().ContainSingle().Subject;
        field.FieldNumber.Should().Be(536_870_911);
        field.WireType.Should().Be(ProtoWireType.Varint);
        field.VarintValue.Should().Be(0UL);
    }

    [Fact]
    public void Decode_FieldNumberAboveProtobufMaximum_Throws()
    {
        // Field 536,870,912 ((1<<29), one past the maximum), varint 0:
        // tag = 536870912<<3 = 0x100000000 → varint 80 80 80 80 10; value 00.
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10, 0x00 });

        act.Should().Throw<InvalidDataException>().WithMessage("*Invalid protobuf field number 536870912*");
    }

    [Fact]
    public void Decode_TruncatedFixed32_Throws()
    {
        // Metric.float_value (field 12, wire type 5) with only two of four payload bytes.
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x65, 0x00, 0x00 });

        act.Should().Throw<InvalidDataException>().WithMessage("*Truncated fixed32*");
    }

    [Theory]
    [InlineData(new byte[] { 0x0E })] // field 1, wire type 6
    [InlineData(new byte[] { 0x0F })] // field 1, wire type 7
    public void Decode_IllegalWireType_Throws(byte[] bytes)
    {
        // Wire types 6 and 7 are not defined by the protobuf wire format at all —
        // unlike groups (3/4), which are defined but intentionally unsupported here.
        var act = () => ProtoWireDecoder.Decode(bytes);

        act.Should().Throw<InvalidDataException>().WithMessage("*Unknown wire type*");
    }

    [Fact]
    public void Decode_GroupWireType_ThrowsNotSupported()
    {
        // Field 1, StartGroup: tag (1<<3)|3 = 0x0B. sparkplug_b.proto never uses groups.
        var act = () => ProtoWireDecoder.Decode(new byte[] { 0x0B });

        act.Should().Throw<NotSupportedException>().WithMessage("*Group wire type*");
    }
}
