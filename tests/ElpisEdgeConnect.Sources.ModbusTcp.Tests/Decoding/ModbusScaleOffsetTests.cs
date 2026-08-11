// ============================================================================
// File: Decoding/ModbusScaleOffsetTests.cs
// Purpose: Unit tests for ModbusScaleOffset — linear scale + offset applied
//          to numeric values. Non-numeric inputs rejected.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.ModbusTcp.Decoding;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Decoding;

public sealed class ModbusScaleOffsetTests
{
    [Fact]
    public void Apply_BothNull_ReturnsRawUnchanged()
    {
        var raw = 42;
        var result = ModbusScaleOffset.Apply(raw, scale: null, offset: null);

        result.Should().Be(42).And.BeOfType<int>();
    }

    [Fact]
    public void Apply_ScaleOnly_PromotesToDouble()
    {
        // Raw 42 with scale 0.1 → 4.2
        var result = ModbusScaleOffset.Apply(42, scale: 0.1, offset: null);

        result.Should().BeOfType<double>().Which.Should().BeApproximately(4.2, 1e-9);
    }

    [Fact]
    public void Apply_OffsetOnly_PromotesToDouble()
    {
        // Raw 100 with offset -50 → 50
        var result = ModbusScaleOffset.Apply(100, scale: null, offset: -50.0);

        result.Should().BeOfType<double>().Which.Should().Be(50.0);
    }

    [Fact]
    public void Apply_ScaleAndOffset_CombinedLinear()
    {
        // (raw * scale) + offset — temperature sensor: raw 1000 * 0.1 + 273.15
        var result = ModbusScaleOffset.Apply(1000, scale: 0.1, offset: 273.15);

        result.Should().BeOfType<double>().Which.Should().BeApproximately(373.15, 1e-9);
    }

    [Fact]
    public void Apply_LongRaw_ConvertsCorrectly()
    {
        var result = ModbusScaleOffset.Apply(1_000_000L, scale: 0.001, offset: null);
        result.Should().BeOfType<double>().Which.Should().Be(1000.0);
    }

    [Fact]
    public void Apply_FloatRaw_ConvertsCorrectly()
    {
        var result = ModbusScaleOffset.Apply(1.5f, scale: 2.0, offset: 0.5);
        result.Should().BeOfType<double>().Which.Should().BeApproximately(3.5, 1e-9);
    }

    [Fact]
    public void Apply_DoubleRaw_ConvertsCorrectly()
    {
        var result = ModbusScaleOffset.Apply(3.14, scale: 2.0, offset: null);
        result.Should().BeOfType<double>().Which.Should().BeApproximately(6.28, 1e-9);
    }

    [Fact]
    public void Apply_NonNumeric_Throws()
    {
        Action act = () => ModbusScaleOffset.Apply("text", scale: 1.0, offset: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Only numeric datatypes support Scale/Offset*");
    }

    [Fact]
    public void Apply_Bool_Throws()
    {
        Action act = () => ModbusScaleOffset.Apply(true, scale: 1.0, offset: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Apply_NegativeScale_Works()
    {
        var result = ModbusScaleOffset.Apply(10, scale: -1.0, offset: 5.0);
        result.Should().BeOfType<double>().Which.Should().Be(-5.0);
    }
}
