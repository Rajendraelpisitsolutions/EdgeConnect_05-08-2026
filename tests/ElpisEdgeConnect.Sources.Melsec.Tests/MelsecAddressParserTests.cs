// ============================================================================
// Tests: MelsecAddressParser — per-device radix (ADR-0033 Rule 3, ZR = hex),
//        word-bit rules (Rule 4), and typed rejection of unsupported devices
//        (DEVICE_NOT_IMPLEMENTED) / malformed addresses (CONFIG_INVALID_ADDRESS).
// ============================================================================

using ElpisEdgeConnect.Sources.Melsec;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecAddressParserTests
{
    [Theory]
    [InlineData("D26", "D", 26)]     // D decimal
    [InlineData("R200", "R", 200)]   // R decimal
    [InlineData("M100", "M", 100)]   // M decimal
    [InlineData("W1A", "W", 0x1A)]   // W hex -> 26
    [InlineData("X20", "X", 0x20)]   // X hex -> 32
    [InlineData("Y20", "Y", 0x20)]   // Y hex -> 32
    [InlineData("B1F", "B", 0x1F)]   // B hex -> 31
    [InlineData("ZR1F", "ZR", 0x1F)] // ZR HEX (corrected) -> 31
    public void TryParse_applies_per_device_radix(string address, string symbol, int number)
    {
        var ok = MelsecAddressParser.TryParse(address, out var result, out var error);

        ok.Should().BeTrue(error?.Message);
        result!.Device.Symbol.Should().Be(symbol);
        result.DeviceNumber.Should().Be(number);
        result.BitIndex.Should().BeNull();
    }

    [Fact]
    public void TryParse_is_case_insensitive()
    {
        MelsecAddressParser.TryParse("zr1f", out var result, out _).Should().BeTrue();
        result!.Device.Symbol.Should().Be("ZR");
        result.DeviceNumber.Should().Be(0x1F);
    }

    [Theory]
    [InlineData("D100.3", "D", 100, 3)]
    [InlineData("W10.F", "W", 0x10, 15)]   // W10 hex = 16, bit F = 15
    [InlineData("R200.0", "R", 200, 0)]
    [InlineData("ZR1F.F", "ZR", 0x1F, 15)]
    public void TryParse_accepts_word_bit_on_word_devices(string address, string symbol, int number, int bit)
    {
        var ok = MelsecAddressParser.TryParse(address, out var result, out var error);

        ok.Should().BeTrue(error?.Message);
        result!.Device.Symbol.Should().Be(symbol);
        result.DeviceNumber.Should().Be(number);
        result.BitIndex.Should().Be(bit);
        result.ResolvesToBool.Should().BeTrue();
    }

    [Fact]
    public void TryParse_rejects_bit_suffix_on_bit_device()
    {
        // M is already bit-addressed, so M100.3 is invalid.
        var ok = MelsecAddressParser.TryParse("M100.3", out _, out var error);

        ok.Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.InvalidAddress);
        error.Message.Should().Contain("bit suffix");
    }

    [Fact]
    public void TryParse_rejects_out_of_range_bit_index()
    {
        // .10 = 0x10 = 16, outside 0..15.
        var ok = MelsecAddressParser.TryParse("D100.10", out _, out var error);

        ok.Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.InvalidAddress);
        error.Message.Should().Contain("0..F");
    }

    [Theory]
    [InlineData("DX0")]
    [InlineData("DY0")]
    [InlineData("T0")]
    [InlineData("C0")]
    [InlineData("L0")]
    [InlineData("F0")]
    [InlineData("V0")]
    [InlineData("S0")]
    [InlineData("Z0")]
    public void TryParse_rejects_recognized_but_unsupported_devices(string address)
    {
        var ok = MelsecAddressParser.TryParse(address, out _, out var error);

        ok.Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
    }

    // A-3a: special devices are now supported (audit: 2026-07-03-melsec-a3a-audit.md).
    [Theory]
    [InlineData("SM0", 0, MelsecDeviceKind.Bit)]     // decimal
    [InlineData("SM400", 400, MelsecDeviceKind.Bit)] // RUN monitor example
    [InlineData("SD210", 210, MelsecDeviceKind.Word)]
    [InlineData("SB1F", 0x1F, MelsecDeviceKind.Bit)] // hexadecimal
    [InlineData("SW10", 0x10, MelsecDeviceKind.Word)]
    public void TryParse_accepts_a3a_special_devices(string address, int number, MelsecDeviceKind kind)
    {
        var ok = MelsecAddressParser.TryParse(address, out var result, out _);

        ok.Should().BeTrue();
        result!.DeviceNumber.Should().Be(number);
        result.Device.Kind.Should().Be(kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("100")]      // no device letter
    [InlineData("D")]        // no number
    [InlineData("DA")]       // 'A' is not a valid decimal number for D
    [InlineData("Q100")]     // unknown device
    [InlineData("D1.2.3")]   // multiple '.' separators
    public void TryParse_rejects_malformed_addresses_with_typed_error(string address)
    {
        var ok = MelsecAddressParser.TryParse(address, out _, out var error);

        ok.Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.InvalidAddress);
    }

    [Fact]
    public void TryParse_rejects_null()
    {
        MelsecAddressParser.TryParse(null, out _, out var error).Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.InvalidAddress);
    }

    [Fact]
    public void ResolvesToBool_is_true_for_bit_device_without_suffix()
    {
        MelsecAddressParser.TryParse("M100", out var result, out _).Should().BeTrue();
        result!.BitIndex.Should().BeNull();
        result.ResolvesToBool.Should().BeTrue();
    }

    [Fact]
    public void ResolvesToBool_is_false_for_plain_word_device()
    {
        MelsecAddressParser.TryParse("D100", out var result, out _).Should().BeTrue();
        result!.ResolvesToBool.Should().BeFalse();
    }

    [Theory]
    [InlineData("D100", "D100")]
    [InlineData("W1A", "W1A")]
    [InlineData("ZR1F", "ZR1F")]
    [InlineData("D100.3", "D100.3")]
    [InlineData("W10.F", "W10.F")]
    public void ToString_round_trips_radix_correctly(string address, string expected)
    {
        MelsecAddressParser.TryParse(address, out var result, out _).Should().BeTrue();
        result!.ToString().Should().Be(expected);
    }
}
