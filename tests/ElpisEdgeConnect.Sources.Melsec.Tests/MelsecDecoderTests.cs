// ============================================================================
// Tests: MelsecDecoder — little-endian word decode, per-tag word order for
//        32-bit values, bit extraction (word-bit + packed bit-device), scale/
//        offset, and typed/deterministic buffer/offset/datatype failures.
// ============================================================================

using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Decoding;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecDecoderTests
{
    // ---- 16-bit little-endian ---------------------------------------------

    [Fact]
    public void Decode_UInt16_little_endian()
    {
        var value = MelsecDecoder.Decode(new byte[] { 0x34, 0x12 }, 0, MelsecDatatype.UInt16, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be((ushort)0x1234);
    }

    [Fact]
    public void Decode_Int16_little_endian_negative()
    {
        var value = MelsecDecoder.Decode(new byte[] { 0x00, 0x80 }, 0, MelsecDatatype.Int16, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be((short)-32768);
    }

    // ---- 32-bit word order -------------------------------------------------

    [Fact]
    public void Decode_UInt32_low_word_first()
    {
        // word0=0x1234 (low), word1=0xABCD (high) -> 0xABCD1234
        var value = MelsecDecoder.Decode(new byte[] { 0x34, 0x12, 0xCD, 0xAB }, 0, MelsecDatatype.UInt32, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be(0xABCD1234u);
    }

    [Fact]
    public void Decode_UInt32_high_word_first()
    {
        // same bytes; word0 is now the HIGH word -> 0x1234ABCD
        var value = MelsecDecoder.Decode(new byte[] { 0x34, 0x12, 0xCD, 0xAB }, 0, MelsecDatatype.UInt32, MelsecWordOrder.HighWordFirst, null);
        value.Should().Be(0x1234ABCDu);
    }

    [Fact]
    public void Decode_Int32_low_word_first_negative()
    {
        var value = MelsecDecoder.Decode(new byte[] { 0x34, 0x12, 0xCD, 0xAB }, 0, MelsecDatatype.Int32, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be(unchecked((int)0xABCD1234));
    }

    [Fact]
    public void Decode_Float32_low_word_first()
    {
        // 1.0f = 0x3F800000; LE bytes 00 00 80 3F; low word 0x0000, high word 0x3F80.
        var value = MelsecDecoder.Decode(new byte[] { 0x00, 0x00, 0x80, 0x3F }, 0, MelsecDatatype.Float32, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be(1.0f);
    }

    [Fact]
    public void Decode_Float32_high_word_first()
    {
        // Words swapped on the wire; high-word-first reassembles 0x3F800000 = 1.0f.
        var value = MelsecDecoder.Decode(new byte[] { 0x80, 0x3F, 0x00, 0x00 }, 0, MelsecDatatype.Float32, MelsecWordOrder.HighWordFirst, null);
        value.Should().Be(1.0f);
    }

    [Fact]
    public void Decode_uses_byte_offset_within_block()
    {
        // Two words; decode the second one (offset 2).
        var value = MelsecDecoder.Decode(new byte[] { 0x00, 0x00, 0x34, 0x12 }, 2, MelsecDatatype.UInt16, MelsecWordOrder.LowWordFirst, null);
        value.Should().Be((ushort)0x1234);
    }

    // ---- Bit extraction ----------------------------------------------------

    [Theory]
    [InlineData(3, true)]   // bit 3 is set in 0x0008
    [InlineData(2, false)]
    public void Decode_Bool_from_word_bit(int bitIndex, bool expected)
    {
        // Containing word = 0x0008 (bit 3 set) — the D100.3 case.
        var value = MelsecDecoder.Decode(new byte[] { 0x08, 0x00 }, 0, MelsecDatatype.Bool, MelsecWordOrder.LowWordFirst, bitIndex);
        value.Should().Be(expected);
    }

    [Theory]
    [InlineData(15, true)]  // bit 15 set in 0x8000
    [InlineData(14, false)]
    [InlineData(0, false)]
    public void Decode_Bool_from_packed_bit_device_word(int bitIndex, bool expected)
    {
        // Word-unit read of a bit device (M/X/Y/B): 16 bits packed per word.
        // 0x8000 -> only the top bit (offset 15) is set.
        var value = MelsecDecoder.Decode(new byte[] { 0x00, 0x80 }, 0, MelsecDatatype.Bool, MelsecWordOrder.LowWordFirst, bitIndex);
        value.Should().Be(expected);
    }

    // ---- Scale / offset ----------------------------------------------------

    [Fact]
    public void ApplyScaleOffset_scales_and_offsets_numeric()
    {
        MelsecDecoder.ApplyScaleOffset((ushort)100, scale: 0.1, offset: 5.0).Should().Be(15.0);
    }

    [Fact]
    public void ApplyScaleOffset_passes_bool_through_unchanged()
    {
        MelsecDecoder.ApplyScaleOffset(true, scale: 0.1, offset: 5.0).Should().Be(true);
    }

    [Fact]
    public void ApplyScaleOffset_returns_original_when_no_scale_or_offset()
    {
        MelsecDecoder.ApplyScaleOffset((short)10, scale: null, offset: null).Should().Be((short)10);
    }

    // ---- Typed / deterministic failures -----------------------------------

    [Fact]
    public void Decode_short_buffer_for_word_throws_typed()
    {
        var act = () => MelsecDecoder.Decode(System.Array.Empty<byte>(), 0, MelsecDatatype.Int16, MelsecWordOrder.LowWordFirst, null);
        act.Should().Throw<MelsecDecodeException>().WithMessage("*too short*");
    }

    [Fact]
    public void Decode_short_buffer_for_dword_throws_typed()
    {
        var act = () => MelsecDecoder.Decode(new byte[] { 0x00, 0x00 }, 0, MelsecDatatype.UInt32, MelsecWordOrder.LowWordFirst, null);
        act.Should().Throw<MelsecDecodeException>().WithMessage("*too short*");
    }

    [Fact]
    public void Decode_bool_without_bit_index_throws_typed()
    {
        var act = () => MelsecDecoder.Decode(new byte[] { 0x00, 0x00 }, 0, MelsecDatatype.Bool, MelsecWordOrder.LowWordFirst, null);
        act.Should().Throw<MelsecDecodeException>().WithMessage("*bit index*");
    }

    [Fact]
    public void Decode_bool_bit_index_out_of_range_throws_typed()
    {
        var act = () => MelsecDecoder.Decode(new byte[] { 0x00, 0x00 }, 0, MelsecDatatype.Bool, MelsecWordOrder.LowWordFirst, 16);
        act.Should().Throw<MelsecDecodeException>();
    }

    [Fact]
    public void Decode_negative_offset_throws_typed()
    {
        var act = () => MelsecDecoder.Decode(new byte[] { 0x00, 0x00 }, -1, MelsecDatatype.UInt16, MelsecWordOrder.LowWordFirst, null);
        act.Should().Throw<MelsecDecodeException>();
    }
}
