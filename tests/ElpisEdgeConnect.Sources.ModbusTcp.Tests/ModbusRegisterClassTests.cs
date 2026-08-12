// ============================================================================
// File: ModbusRegisterClassTests.cs
// Purpose: Function-code and max-quantity helpers on ModbusRegisterClass.
//          Small but load-bearing — wrong FC or wrong max lights up a full
//          cascade of test failures elsewhere.
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusRegisterClassTests
{
    [Theory]
    [InlineData(ModbusRegisterClass.Coil, (byte)0x01)]
    [InlineData(ModbusRegisterClass.DiscreteInput, (byte)0x02)]
    [InlineData(ModbusRegisterClass.HoldingRegister, (byte)0x03)]
    [InlineData(ModbusRegisterClass.InputRegister, (byte)0x04)]
    public void ToFunctionCode_ReturnsSpecValue(ModbusRegisterClass rc, byte expected)
    {
        rc.ToFunctionCode().Should().Be(expected);
    }

    [Theory]
    [InlineData(ModbusRegisterClass.Coil, 2000)]
    [InlineData(ModbusRegisterClass.DiscreteInput, 2000)]
    [InlineData(ModbusRegisterClass.HoldingRegister, 125)]
    [InlineData(ModbusRegisterClass.InputRegister, 125)]
    public void MaxQuantity_MatchesProtocolLimit(ModbusRegisterClass rc, int expected)
    {
        rc.MaxQuantity().Should().Be(expected);
    }

    [Theory]
    [InlineData(ModbusRegisterClass.Coil, true)]
    [InlineData(ModbusRegisterClass.DiscreteInput, true)]
    [InlineData(ModbusRegisterClass.HoldingRegister, false)]
    [InlineData(ModbusRegisterClass.InputRegister, false)]
    public void IsBitRead_DistinguishesFc01Fc02FromFc03Fc04(ModbusRegisterClass rc, bool isBit)
    {
        rc.IsBitRead().Should().Be(isBit);
    }
}
