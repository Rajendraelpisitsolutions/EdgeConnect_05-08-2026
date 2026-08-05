// ============================================================================
// File: Decoding/ModbusDecoderTests.cs
// Purpose: Unit tests for ModbusDecoder — every datatype × every byte order
//          plus stringN edge cases (trimming, high-char-first, endianness).
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.ModbusTcp.Decoding;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Decoding;

public sealed class ModbusDecoderTests
{
    // -------------------------------------------------------------
    // Bits
    // -------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecodeBit_ReturnsRawBool(bool input)
    {
        ModbusDecoder.DecodeBit(input).Should().Be(input);
    }

    [Fact]
    public void DecodeRegisters_BoolDatatype_Throws()
    {
        var regs = new ushort[] { 1 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Bool);

        Action act = () => ModbusDecoder.DecodeRegisters(regs, 0, 1, spec, ModbusByteOrder.AB);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Bool tags must be read via Coil / DiscreteInput*");
    }

    // -------------------------------------------------------------
    // 2-byte datatypes (AB / BA)
    // -------------------------------------------------------------

    [Theory]
    [InlineData(ModbusByteOrder.AB, (ushort)0x1234, 0x1234)]
    [InlineData(ModbusByteOrder.BA, (ushort)0x1234, 0x3412)]
    public void DecodeRegisters_UInt16_AppliesByteOrder(ModbusByteOrder order, ushort raw, int expected)
    {
        var regs = new[] { raw };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt16);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 1, spec, order);

        result.Should().BeOfType<int>().And.Be(expected);
    }

    [Theory]
    [InlineData(ModbusByteOrder.AB, (ushort)0xFFFF, -1)]    // sign-extends
    [InlineData(ModbusByteOrder.AB, (ushort)0x8000, -32768)]
    [InlineData(ModbusByteOrder.AB, (ushort)0x7FFF, 32767)]
    public void DecodeRegisters_Int16_SignExtends(ModbusByteOrder order, ushort raw, int expected)
    {
        var regs = new[] { raw };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Int16);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 1, spec, order);

        result.Should().Be(expected);
    }

    // -------------------------------------------------------------
    // 4-byte datatypes — exhaust all 4 byte orders
    // -------------------------------------------------------------

    [Theory]
    // wire [0x12, 0x34, 0x56, 0x78] → value under each ordering
    [InlineData(ModbusByteOrder.ABCD, (long)0x12345678)]
    [InlineData(ModbusByteOrder.CDAB, (long)0x56781234)]
    [InlineData(ModbusByteOrder.BADC, (long)0x34127856)]
    [InlineData(ModbusByteOrder.DCBA, (long)0x78563412)]
    public void DecodeRegisters_UInt32_AllByteOrders(ModbusByteOrder order, long expected)
    {
        // regs[0] high=0x12, low=0x34 → register 0x1234
        // regs[1] high=0x56, low=0x78 → register 0x5678
        var regs = new ushort[] { 0x1234, 0x5678 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt32);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, order);

        result.Should().Be(expected);
    }

    [Fact]
    public void DecodeRegisters_Int32_NegativeValue()
    {
        // 0xFFFFFFFF in ABCD is -1 as Int32.
        var regs = new ushort[] { 0xFFFF, 0xFFFF };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Int32);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.ABCD);

        result.Should().BeOfType<int>().And.Be(-1);
    }

    [Fact]
    public void DecodeRegisters_Float32_ABCD()
    {
        // 1.0f big-endian bytes: 0x3F 0x80 0x00 0x00
        var regs = new ushort[] { 0x3F80, 0x0000 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Float32);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.ABCD);

        result.Should().BeOfType<float>().And.Be(1.0f);
    }

    [Fact]
    public void DecodeRegisters_Float32_CDAB_WordSwapped()
    {
        // CDAB word-swap: 1.0f arrives with low word first.
        var regs = new ushort[] { 0x0000, 0x3F80 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Float32);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.CDAB);

        result.Should().BeOfType<float>().And.Be(1.0f);
    }

    [Fact]
    public void DecodeRegisters_ByteOrder_WidthMismatch_Throws()
    {
        // UInt32 (4 bytes) with AB byte order (2-byte) must reject.
        var regs = new ushort[] { 0x1234, 0x5678 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt32);

        Action act = () => ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.AB);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*covers 2 bytes but datatype UInt32 requires 4*");
    }

    // -------------------------------------------------------------
    // 8-byte datatypes
    // -------------------------------------------------------------

    [Fact]
    public void DecodeRegisters_Int64_ABCDEFGH()
    {
        // 0x0102030405060708
        var regs = new ushort[] { 0x0102, 0x0304, 0x0506, 0x0708 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Int64);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 4, spec, ModbusByteOrder.ABCDEFGH);

        result.Should().BeOfType<long>().And.Be(0x0102030405060708L);
    }

    [Fact]
    public void DecodeRegisters_Float64_BigEndian()
    {
        // 1.0 double big-endian: 0x3FF0 0000 0000 0000
        var regs = new ushort[] { 0x3FF0, 0x0000, 0x0000, 0x0000 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.Float64);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 4, spec, ModbusByteOrder.ABCDEFGH);

        result.Should().BeOfType<double>().And.Be(1.0);
    }

    [Fact]
    public void DecodeRegisters_UInt64_UpperRange_BoxesAsNegativeLong()
    {
        // 2^63 + 1 is > long.MaxValue; unchecked cast produces a negative long.
        // Documented behavior — canonical model has no unsigned 64-bit carrier.
        var regs = new ushort[] { 0x8000, 0x0000, 0x0000, 0x0001 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt64);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 4, spec, ModbusByteOrder.ABCDEFGH);

        result.Should().BeOfType<long>();
        ((long)result).Should().BeLessThan(0);
    }

    // -------------------------------------------------------------
    // Offset within a block — multiple tags in one read
    // -------------------------------------------------------------

    [Fact]
    public void DecodeRegisters_OffsetIntoBlock_SkipsLeadingRegisters()
    {
        // Block has [0x1111, 0x2222, 0x3333, 0x4444]; decode uint16 at offset=2.
        var regs = new ushort[] { 0x1111, 0x2222, 0x3333, 0x4444 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt16);

        var result = ModbusDecoder.DecodeRegisters(regs, 2, 1, spec, ModbusByteOrder.AB);

        result.Should().Be(0x3333);
    }

    [Fact]
    public void DecodeRegisters_OffsetIntoBlock_ForUInt32_CoversTwoRegisters()
    {
        // UInt32 at offset=1, value should be registers [1..2] = 0x2222_3333
        var regs = new ushort[] { 0x1111, 0x2222, 0x3333, 0x4444 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.UInt32);

        var result = ModbusDecoder.DecodeRegisters(regs, 1, 2, spec, ModbusByteOrder.ABCD);

        result.Should().Be(0x22223333L);
    }

    // -------------------------------------------------------------
    // StringN
    // -------------------------------------------------------------

    [Fact]
    public void DecodeRegisters_StringN_HighCharFirst_TrimsPadding()
    {
        // "HI__" — 4 chars across 2 registers, PLC pads with spaces.
        // 'H'=0x48, 'I'=0x49, ' '=0x20
        var regs = new ushort[] { 0x4849, 0x2020 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.StringN, StringLengthChars: 4);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.AB);

        result.Should().Be("HI");
    }

    [Fact]
    public void DecodeRegisters_StringN_NullPadded_Trims()
    {
        // "OK" packed with trailing nulls.
        var regs = new ushort[] { 0x4F4B, 0x0000 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.StringN, StringLengthChars: 4);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.AB);

        result.Should().Be("OK");
    }

    [Fact]
    public void DecodeRegisters_StringN_LowCharFirstByteOrder_FlipsCharsPerRegister()
    {
        // BA order means low byte is the first char of the pair.
        // 0x4849 under BA → chars 'I', 'H'
        var regs = new ushort[] { 0x4849 };
        var spec = new ModbusDatatypeSpec(ModbusDatatype.StringN, StringLengthChars: 2);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 1, spec, ModbusByteOrder.BA);

        result.Should().Be("IH");
    }

    [Fact]
    public void DecodeRegisters_StringN_OddLength_UsesOnlyFirstCharOfLastRegister()
    {
        // 3-char string fits in 2 registers but only uses 3 of the 4 character slots.
        var regs = new ushort[] { 0x4142, 0x4300 }; // 'A','B','C',\0
        var spec = new ModbusDatatypeSpec(ModbusDatatype.StringN, StringLengthChars: 3);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.AB);

        result.Should().Be("ABC");
    }

    [Fact]
    public void DecodeRegisters_StringN_FullRange_NoTrimNeeded()
    {
        // Every char slot used — no trimming required.
        var regs = new ushort[] { 0x5041, 0x5254 }; // 'P','A','R','T'
        var spec = new ModbusDatatypeSpec(ModbusDatatype.StringN, StringLengthChars: 4);

        var result = ModbusDecoder.DecodeRegisters(regs, 0, 2, spec, ModbusByteOrder.AB);

        result.Should().Be("PART");
    }
}
