// ============================================================================
// File: Scanning/ModbusTagWidthTests.cs
// Purpose: Unit tests for ModbusTagWidth — datatype → register-width mapping.
// ============================================================================

using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Scanning;

public sealed class ModbusTagWidthTests
{
    private static ModbusTagDefinition RegTag(string? datatype) => new()
    {
        Name = "t",
        RegisterClass = ModbusRegisterClass.HoldingRegister,
        Address = 0,
        Datatype = datatype,
    };

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("bool", 1)]
    [InlineData("int16", 1)]
    [InlineData("uint16", 1)]
    [InlineData("int32", 2)]
    [InlineData("uint32", 2)]
    [InlineData("float32", 2)]
    [InlineData("int64", 4)]
    [InlineData("uint64", 4)]
    [InlineData("float64", 4)]
    [InlineData("UInt16", 1)]      // case insensitive
    [InlineData("FLOAT32", 2)]
    [InlineData("unknown-type", 1)] // unknown → default 1
    public void Resolve_RegisterDatatypes(string? datatype, int expectedRegisters)
    {
        ModbusTagWidth.Resolve(RegTag(datatype)).Should().Be((ushort)expectedRegisters);
    }

    [Theory]
    [InlineData("string8", 4)]   // 8 chars → 4 registers
    [InlineData("string16", 8)]
    [InlineData("string1", 1)]   // single char → 1 register (rounded up)
    [InlineData("string3", 2)]   // odd length rounds up
    public void Resolve_StringN_RoundsUp(string datatype, int expectedRegisters)
    {
        ModbusTagWidth.Resolve(RegTag(datatype)).Should().Be((ushort)expectedRegisters);
    }

    [Theory]
    [InlineData("string")]    // missing length
    [InlineData("string0")]   // non-positive
    [InlineData("string-5")]  // non-positive
    [InlineData("stringXYZ")] // non-numeric
    public void Resolve_InvalidStringN_Throws(string datatype)
    {
        var act = () => ModbusTagWidth.Resolve(RegTag(datatype));
        act.Should().Throw<System.ArgumentException>();
    }

    [Theory]
    [InlineData(ModbusRegisterClass.Coil)]
    [InlineData(ModbusRegisterClass.DiscreteInput)]
    public void Resolve_BitClass_AlwaysOne(ModbusRegisterClass rc)
    {
        var tag = new ModbusTagDefinition
        {
            Name = "bit",
            RegisterClass = rc,
            Address = 10,
            // Datatype is ignored for bit classes — width is always 1 bit.
            Datatype = "float64",
        };
        ModbusTagWidth.Resolve(tag).Should().Be((ushort)1);
    }
}
