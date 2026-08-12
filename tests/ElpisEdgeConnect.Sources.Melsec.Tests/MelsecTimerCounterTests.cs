// ============================================================================
// Tests: A-3b timers/counters — parser longest-prefix resolution, bare-prefix
// rejection with suggestions, single-word datatype coherence, planner
// separate-block grouping, and golden request vectors on both profiles.
// Facts pinned in docs/sessions/2026-07-03-melsec-a3b0-timers-counters-audit.md:
//   TS C1 / TC C0 / TN C2 / STS C7 / STC C6 / STN C8 / CS C4 / CC C3 / CN C5
//   (all decimal, both profiles; TN/STN/CN single-word current values).
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.Melsec.Profiles;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecTimerCounterTests
{
    // ─── Parser: longest-prefix resolution ───────────────────────────────

    [Theory]
    [InlineData("STN10", "STN", 10, MelsecDeviceCode.STN)]
    [InlineData("STS10", "STS", 10, MelsecDeviceCode.STS)]
    [InlineData("STC10", "STC", 10, MelsecDeviceCode.STC)]
    [InlineData("TN100", "TN", 100, MelsecDeviceCode.TN)]
    [InlineData("TS5", "TS", 5, MelsecDeviceCode.TS)]
    [InlineData("TC5", "TC", 5, MelsecDeviceCode.TC)]
    [InlineData("CN0", "CN", 0, MelsecDeviceCode.CN)]
    [InlineData("CS3", "CS", 3, MelsecDeviceCode.CS)]
    [InlineData("CC3", "CC", 3, MelsecDeviceCode.CC)]
    public void Parser_resolves_timer_counter_mnemonics_longest_prefix(
        string address, string symbol, int number, MelsecDeviceCode code)
    {
        MelsecAddressParser.TryParse(address, out var result, out _).Should().BeTrue();

        result!.Device.Symbol.Should().Be(symbol);
        result.DeviceNumber.Should().Be(number);
        result.Device.Code.Should().Be(code);
    }

    [Fact]
    public void Parser_S_does_not_steal_the_ST_prefix()
    {
        // Step relay S is unsupported; it must NOT consume STN10 / STS10.
        MelsecAddressParser.TryParse("STN10", out var stn, out _).Should().BeTrue();
        stn!.Device.Symbol.Should().Be("STN");

        // Bare S rejects on its own (recognized-but-unsupported), not as a prefix thief.
        MelsecAddressParser.TryParse("S10", out _, out var sErr).Should().BeFalse();
        sErr!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
    }

    // ─── Parser: bare-prefix rejection with suggestions ──────────────────

    [Theory]
    [InlineData("T100", "TN100", "TS100", "TC100")]
    [InlineData("C100", "CN100", "CS100", "CC100")]
    [InlineData("ST100", "STN100", "STS100", "STC100")]
    public void Parser_rejects_bare_prefix_with_suggestion(string address, string cur, string contact, string coil)
    {
        MelsecAddressParser.TryParse(address, out _, out var error).Should().BeFalse();

        error!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
        error.Message.Should().Contain("ambiguous")
            .And.Contain(cur).And.Contain(contact).And.Contain(coil);
    }

    // ─── Parser: long/extended families rejected ─────────────────────────

    [Theory]
    [InlineData("LTN0")]
    [InlineData("LTS0")]
    [InlineData("LSTN0")]
    [InlineData("LCN0")]
    [InlineData("LCS0")]
    [InlineData("LZ0")]
    public void Parser_rejects_long_families(string address)
    {
        MelsecAddressParser.TryParse(address, out _, out var error).Should().BeFalse();
        error!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
    }

    // ─── Datatype coherence ──────────────────────────────────────────────

    [Theory]
    [InlineData("TN100", "Bool")]
    [InlineData("STN0", "Bool")]
    [InlineData("CN0", "Bool")]
    [InlineData("TN100", "Int32")]
    [InlineData("STN0", "UInt32")]
    [InlineData("CN0", "Float32")]
    public void CurrentValue_rejects_non_16bit(string address, string datatype)
    {
        var tags = new[] { new MelsecTagDefinition { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 } };

        var plan = MelsecScanPlanner.Build(tags, 8, 960);

        plan.Errors.Should().ContainSingle(e => e.Code == MelsecScanPlanner.DatatypeMismatch);
    }

    [Theory]
    [InlineData("TN100", "Int16")]
    [InlineData("TN100", "UInt16")]
    [InlineData("CN0", "UInt16")]
    [InlineData("STN0", "Int16")]
    public void CurrentValue_accepts_16bit(string address, string datatype)
    {
        var tags = new[] { new MelsecTagDefinition { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 } };

        MelsecScanPlanner.Build(tags, 8, 960).Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("TS100", "Int16")]
    [InlineData("TC100", "UInt16")]
    [InlineData("CS0", "Int16")]
    [InlineData("STS0", "Float32")]
    public void Contact_coil_rejects_numeric(string address, string datatype)
    {
        var tags = new[] { new MelsecTagDefinition { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 } };

        MelsecScanPlanner.Build(tags, 8, 960).Errors.Should().ContainSingle(e => e.Code == MelsecScanPlanner.DatatypeMismatch);
    }

    [Theory]
    [InlineData("TS100")]
    [InlineData("CS0")]
    [InlineData("STS0")]
    public void Contact_coil_accepts_Bool(string address)
    {
        var tags = new[] { new MelsecTagDefinition { Name = "t", Address = address, Datatype = "Bool", ScanRateMs = 1000 } };

        MelsecScanPlanner.Build(tags, 8, 960).Errors.Should().BeEmpty();
    }

    // ─── Planner: separate blocks per sub-device code ────────────────────

    [Fact]
    public void Planner_produces_separate_blocks_per_sub_device()
    {
        var tags = new[]
        {
            new MelsecTagDefinition { Name = "contact", Address = "TS100", Datatype = "Bool", ScanRateMs = 1000 },
            new MelsecTagDefinition { Name = "coil", Address = "TC100", Datatype = "Bool", ScanRateMs = 1000 },
            new MelsecTagDefinition { Name = "current", Address = "TN100", Datatype = "UInt16", ScanRateMs = 1000 },
        };

        var plan = MelsecScanPlanner.Build(tags, 8, 960);

        plan.Errors.Should().BeEmpty();
        plan.Blocks.Should().HaveCount(3, "contact, coil, and current value are distinct device codes");
        var codes = plan.Blocks.Select(b => b.DeviceCode).ToList();
        codes.Should().Contain(new[] { MelsecDeviceCode.TS, MelsecDeviceCode.TC, MelsecDeviceCode.TN });
    }

    // ─── Golden request vectors (both profiles) ──────────────────────────

    [Theory]
    [InlineData("TS100", 0xC1, 100, MelsecDeviceProfile.Modern)]
    [InlineData("TC100", 0xC0, 100, MelsecDeviceProfile.Modern)]
    [InlineData("TN100", 0xC2, 100, MelsecDeviceProfile.Modern)]
    [InlineData("STS0", 0xC7, 0, MelsecDeviceProfile.Modern)]
    [InlineData("STC0", 0xC6, 0, MelsecDeviceProfile.Modern)]
    [InlineData("STN0", 0xC8, 0, MelsecDeviceProfile.Modern)]
    [InlineData("CS3", 0xC4, 3, MelsecDeviceProfile.Modern)]
    [InlineData("CC3", 0xC3, 3, MelsecDeviceProfile.Modern)]
    [InlineData("CN0", 0xC5, 0, MelsecDeviceProfile.Modern)]
    [InlineData("TN511", 0xC2, 511, MelsecDeviceProfile.IqF)]
    [InlineData("CN255", 0xC5, 255, MelsecDeviceProfile.IqF)]
    [InlineData("STN0", 0xC8, 0, MelsecDeviceProfile.IqF)]
    public void Golden_request_uses_audited_codes(string address, int codeByte, int head, MelsecDeviceProfile profileKey)
    {
        MelsecProfiles.TryResolve(profileKey, out var profile).Should().BeTrue();
        MelsecAddressParser.TryParse(address, profile!, out var parsed, out _).Should().BeTrue();

        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 4, parsed!.Device.Code, parsed.DeviceNumber, 1);

        frame[18].Should().Be((byte)codeByte);
        (frame[15] | (frame[16] << 8) | (frame[17] << 16)).Should().Be(head);
        parsed.DeviceNumber.Should().Be(head);
    }

    [Fact]
    public void Both_profiles_carry_all_nine_timer_counter_devices()
    {
        foreach (var sym in new[] { "TS", "TC", "TN", "STS", "STC", "STN", "CS", "CC", "CN" })
        {
            MelsecProfiles.Modern.TryGetDevice(sym, out _).Should().BeTrue($"Modern has {sym}");
            MelsecProfiles.IqF.TryGetDevice(sym, out _).Should().BeTrue($"iQ-F has {sym}");
        }
    }
}
