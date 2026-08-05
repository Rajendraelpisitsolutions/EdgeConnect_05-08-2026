// ============================================================================
// Tests: S7Decoder — wire-byte → typed value. S7 is big-endian
// throughout the protocol; these tests pin the byte interpretation.
// ============================================================================

using ElpisEdgeConnect.Sources.S7;
using ElpisEdgeConnect.Sources.S7.Decoding;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests.Decoding;

public class S7DecoderTests
{
    [Theory]
    [InlineData(0b0000_0001, 0, true)]
    [InlineData(0b0000_0001, 1, false)]
    [InlineData(0b1000_0000, 7, true)]
    [InlineData(0b0010_0000, 5, true)]
    [InlineData(0b0010_0000, 4, false)]
    public void Decode_Bool_RespectsBitOffset(byte raw, int bitOffset, bool expected)
    {
        var buf = new byte[] { raw };
        var value = S7Decoder.Decode(buf, byteOffset: 0, bitOffset: bitOffset,
            spec: new S7DatatypeSpec(S7Datatype.Bool));
        value.Should().Be(expected);
    }

    [Fact]
    public void Decode_Byte_AsInteger()
    {
        var buf = new byte[] { 0xFE };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.Byte));
        value.Should().Be(254);
    }

    [Fact]
    public void Decode_SInt_PreservesSignedness()
    {
        var buf = new byte[] { 0xFE }; // -2 signed
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.SInt));
        value.Should().Be(-2);
    }

    [Fact]
    public void Decode_Int_BigEndian()
    {
        // 0x1234 = 4660 (big-endian)
        var buf = new byte[] { 0x12, 0x34 };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.Int));
        value.Should().Be(0x1234);
    }

    [Fact]
    public void Decode_Int_NegativeBigEndian()
    {
        // 0xFFFE = -2 (two's complement, big-endian)
        var buf = new byte[] { 0xFF, 0xFE };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.Int));
        value.Should().Be(-2);
    }

    [Fact]
    public void Decode_Word_UnsignedBigEndian()
    {
        // 0xFFFE = 65534 unsigned
        var buf = new byte[] { 0xFF, 0xFE };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.Word));
        value.Should().Be(65534);
    }

    [Fact]
    public void Decode_DInt_BigEndian()
    {
        // 0x00010002 = 65538 big-endian
        var buf = new byte[] { 0x00, 0x01, 0x00, 0x02 };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.DInt));
        value.Should().Be(65538);
    }

    [Fact]
    public void Decode_Real_KnownValue()
    {
        // IEEE-754 big-endian for 250.5 = 0x43 7A 80 00
        var buf = new byte[] { 0x43, 0x7A, 0x80, 0x00 };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.Real));
        value.Should().BeOfType<float>().And.BeEquivalentTo(250.5f);
    }

    [Fact]
    public void Decode_Real_WithByteOffset_ReadsFromCorrectPosition()
    {
        // Padding bytes at the start; the value lives at offset 4.
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x43, 0x7A, 0x80, 0x00 };
        var value = S7Decoder.Decode(buf, byteOffset: 4, bitOffset: 0,
            new S7DatatypeSpec(S7Datatype.Real));
        value.Should().BeEquivalentTo(250.5f);
    }

    [Fact]
    public void Decode_String_TrimsToCurrentLength()
    {
        // S7 STRING(16): [16][5][H][E][L][L][O][...padding...]
        var buf = new byte[] {
            16, 5,
            (byte)'H', (byte)'E', (byte)'L', (byte)'L', (byte)'O',
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.String, MaxStringChars: 16));
        value.Should().Be("HELLO");
    }

    [Fact]
    public void Decode_String_EmptyWhenCurrentLengthZero()
    {
        var buf = new byte[18];
        buf[0] = 16; // max
        buf[1] = 0;  // current length
        var value = S7Decoder.Decode(buf, 0, 0, new S7DatatypeSpec(S7Datatype.String, MaxStringChars: 16));
        value.Should().Be(string.Empty);
    }

    [Fact]
    public void Decode_OutOfRange_Throws()
    {
        var buf = new byte[] { 0x00 };
        var act = () => S7Decoder.Decode(buf, byteOffset: 0, bitOffset: 0,
            new S7DatatypeSpec(S7Datatype.Real));
        act.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
