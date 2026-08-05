// ============================================================================
// Tests: S7AddressParser — pin the operator-facing address syntax.
//        This is the customer-facing config surface; changing accepted
//        syntax is a config-compat break.
// ============================================================================

using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

public class S7AddressParserTests
{
    [Theory]
    [InlineData("DB10.DBX0.0", S7MemoryArea.DataBlock, 10, 0, 0, S7AddressWidthHint.Bit)]
    [InlineData("DB10.DBX5.7", S7MemoryArea.DataBlock, 10, 5, 7, S7AddressWidthHint.Bit)]
    [InlineData("DB1.DBB3", S7MemoryArea.DataBlock, 1, 3, 0, S7AddressWidthHint.Byte)]
    [InlineData("DB10.DBW2", S7MemoryArea.DataBlock, 10, 2, 0, S7AddressWidthHint.Word)]
    [InlineData("DB10.DBD4", S7MemoryArea.DataBlock, 10, 4, 0, S7AddressWidthHint.DWord)]
    public void Parse_DbAddresses(string raw, S7MemoryArea area, int dbNumber, int byteOffset, int bitOffset, S7AddressWidthHint hint)
    {
        var addr = S7AddressParser.Parse(raw);
        addr.Area.Should().Be(area);
        addr.DbNumber.Should().Be(dbNumber);
        addr.ByteOffset.Should().Be(byteOffset);
        addr.BitOffset.Should().Be(bitOffset);
        addr.WidthHint.Should().Be(hint);
    }

    [Theory]
    [InlineData("M10.5", S7MemoryArea.Marker, 10, 5, S7AddressWidthHint.Bit)]
    [InlineData("MB10", S7MemoryArea.Marker, 10, 0, S7AddressWidthHint.Byte)]
    [InlineData("MW20", S7MemoryArea.Marker, 20, 0, S7AddressWidthHint.Word)]
    [InlineData("MD32", S7MemoryArea.Marker, 32, 0, S7AddressWidthHint.DWord)]
    public void Parse_MarkerAddresses(string raw, S7MemoryArea area, int byteOffset, int bitOffset, S7AddressWidthHint hint)
    {
        var addr = S7AddressParser.Parse(raw);
        addr.Area.Should().Be(area);
        addr.ByteOffset.Should().Be(byteOffset);
        addr.BitOffset.Should().Be(bitOffset);
        addr.WidthHint.Should().Be(hint);
    }

    [Theory]
    [InlineData("I0.0", S7MemoryArea.Input, 0, 0)]
    [InlineData("I4.7", S7MemoryArea.Input, 4, 7)]
    [InlineData("Q4.2", S7MemoryArea.Output, 4, 2)]
    [InlineData("E2.3", S7MemoryArea.Input, 2, 3)] // German "Eingang"
    [InlineData("A1.1", S7MemoryArea.Output, 1, 1)] // German "Ausgang"
    public void Parse_BitFormProcessAddresses(string raw, S7MemoryArea area, int byteOffset, int bitOffset)
    {
        var addr = S7AddressParser.Parse(raw);
        addr.Area.Should().Be(area);
        addr.ByteOffset.Should().Be(byteOffset);
        addr.BitOffset.Should().Be(bitOffset);
        addr.WidthHint.Should().Be(S7AddressWidthHint.Bit);
    }

    [Theory]
    [InlineData("IB0", S7MemoryArea.Input, 0, S7AddressWidthHint.Byte)]
    [InlineData("IW2", S7MemoryArea.Input, 2, S7AddressWidthHint.Word)]
    [InlineData("ID4", S7MemoryArea.Input, 4, S7AddressWidthHint.DWord)]
    [InlineData("QB1", S7MemoryArea.Output, 1, S7AddressWidthHint.Byte)]
    [InlineData("QW3", S7MemoryArea.Output, 3, S7AddressWidthHint.Word)]
    [InlineData("QD7", S7MemoryArea.Output, 7, S7AddressWidthHint.DWord)]
    public void Parse_WidthFormProcessAddresses(string raw, S7MemoryArea area, int byteOffset, S7AddressWidthHint hint)
    {
        var addr = S7AddressParser.Parse(raw);
        addr.Area.Should().Be(area);
        addr.ByteOffset.Should().Be(byteOffset);
        addr.WidthHint.Should().Be(hint);
    }

    [Theory]
    [InlineData("T5", S7MemoryArea.Timer, 5)]
    [InlineData("C3", S7MemoryArea.Counter, 3)]
    [InlineData("T128", S7MemoryArea.Timer, 128)]
    public void Parse_TimersAndCounters(string raw, S7MemoryArea area, int index)
    {
        var addr = S7AddressParser.Parse(raw);
        addr.Area.Should().Be(area);
        addr.ByteOffset.Should().Be(index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("DB10")] // missing inner specifier
    [InlineData("DB10.DBQ0")] // unknown specifier
    [InlineData("DB10.DBX0.9")] // bit > 7
    [InlineData("M10")] // bit-form without bit
    [InlineData("MX10")] // unknown width prefix
    public void Parse_RejectsMalformedAddresses(string raw)
    {
        var act = () => S7AddressParser.Parse(raw);
        act.Should().Throw<System.ArgumentException>();
    }

    // Plan v2 §"Address parser test matrix" — the explicit invalid list the
    // S7 source wizard (M.2b.2) relies on to block Save.
    [Theory]
    [InlineData("")]
    [InlineData("DB.DBW0")]     // DB number missing
    [InlineData("DB1.DBX0.8")]  // bit offset out of 0..7
    [InlineData("DB1.DBW-1")]   // negative byte offset
    [InlineData("DB1.DBZ0")]    // unknown DB width
    [InlineData("X1.DBW0")]     // unknown area prefix
    [InlineData("DB1.DBW")]     // width specifier without an offset
    [InlineData("DB1.DBX0")]    // bit-form missing the bit offset
    [InlineData("DB1.DBX0.a")]  // non-numeric bit offset
    public void Parse_RejectsPlanV2InvalidMatrix(string raw)
    {
        var act = () => S7AddressParser.Parse(raw);
        act.Should().Throw<System.ArgumentException>();
    }

    // Plan v2 §"Address parser test matrix" — the explicit valid list,
    // including German E/A mnemonics for EU operators.
    [Theory]
    [InlineData("DB1.DBX0.7")]
    [InlineData("DB1.DBB0")]
    [InlineData("M0.0")]
    [InlineData("MB4")]
    [InlineData("MW4")]
    [InlineData("MD4")]
    [InlineData("ID2")]
    [InlineData("QB0")]
    [InlineData("QD0")]
    [InlineData("E2.3")] // German Eingang
    [InlineData("A1.1")] // German Ausgang
    [InlineData("EB0")]
    [InlineData("AW2")]
    public void Parse_AcceptsPlanV2ValidMatrix(string raw)
    {
        var act = () => S7AddressParser.Parse(raw);
        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        S7AddressParser.Parse("db10.dbw2").Should().Be(S7AddressParser.Parse("DB10.DBW2"));
        S7AddressParser.Parse("m5.3").Should().Be(S7AddressParser.Parse("M5.3"));
    }

    [Theory]
    [InlineData("DB10.DBX0.0")]
    [InlineData("DB1.DBW2")]
    [InlineData("M5.3")]
    [InlineData("IB0")]
    [InlineData("QW6")]
    public void ToString_RoundTripsThroughParser(string raw)
    {
        var addr = S7AddressParser.Parse(raw);
        var formatted = addr.ToString();
        // Formatted output is parseable and equal to the original parse.
        var reparsed = S7AddressParser.Parse(formatted);
        reparsed.Should().Be(addr);
    }
}
