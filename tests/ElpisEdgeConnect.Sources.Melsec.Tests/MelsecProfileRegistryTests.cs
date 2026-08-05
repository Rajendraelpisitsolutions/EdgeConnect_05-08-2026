// ============================================================================
// Tests: MelsecProfiles registry (A-2 Gate A-2I) — pins:
//   1. The Modern entry mirrors the shipped constants EXACTLY (no drift).
//   2. Shipped Modern behavior is byte-identical through the registry refactor
//      (legacy parse ≡ profile-aware parse; request bytes unchanged).
//   3. Existing configs with no profile field resolve to Modern.
//   4. The iQ-F entry carries the audited facts: device set D/W/R/M/X/Y/B
//      (no ZR), X/Y octal operator labels -> numeric wire heads, 960 cap,
//      same 3E-binary wire shape, internal-only (not operator-selectable).
// Citations: docs/sessions/2026-07-03-melsec-a2d-fx5-audit.md
//   [COM] = SH(NA)-082625ENG-J — §37.1 (3E frame), §38.1 (0401 caps, p614),
//   §38.2 device range + footnote "Binary code: Hexadecimal" (p624),
//   SLMP accessible-device list (p81-82: X/Y "0 to 1777", octal).
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Profiles;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class MelsecProfileRegistryTests
{
    // ─── 1. Modern entry mirrors shipped constants ───────────────────────

    [Fact]
    public void Modern_entry_matches_shipped_constants()
    {
        var p = MelsecProfiles.Modern;

        p.Key.Should().Be(MelsecDeviceProfile.Modern);
        p.FrameMode.Should().Be(MelsecFrameMode.Mc3EBinary);
        p.Transport.Should().Be(MelsecTransportProtocol.Tcp);
        p.RouteDefaults.Should().Be(Slmp3ERoute.LocalCpu);
        p.DeviceCodeWidthBytes.Should().Be(1);
        p.HeadDeviceFieldWidthBytes.Should().Be(3);
        p.MaxWordsPerBatchRead.Should().Be(SlmpFrameCodec.MaxWordPoints).And.Be(MelsecScanPlanner.HardWordCap).And.Be(960);
        p.BitPointsPerWord.Should().Be(16);
        p.RequiresBitHeadAlignment.Should().BeFalse();
        p.DefaultWordOrder.Should().Be(MelsecWordOrder.LowWordFirst);
        p.IsOperatorSelectable.Should().BeTrue();
        p.SupportedList.Should().Be(MelsecDevices.SupportedList);
        p.ManualProvenance.Should().Contain(s => s.Contains("SH(NA)-080008-AB"));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("W")]
    [InlineData("R")]
    [InlineData("ZR")]
    [InlineData("M")]
    [InlineData("X")]
    [InlineData("Y")]
    [InlineData("B")]
    [InlineData("SM")]
    [InlineData("SD")]
    [InlineData("SB")]
    [InlineData("SW")]
    public void Modern_device_descriptors_are_identical_to_shipped_table(string symbol)
    {
        MelsecDevices.TryGet(symbol, out var shipped).Should().BeTrue();
        MelsecProfiles.Modern.TryGetDevice(symbol, out var registry).Should().BeTrue();

        registry.Should().Be(shipped); // record value-equality: symbol, code, radix, kind
    }

    // ─── 2. Modern byte-identity through the refactor ────────────────────

    [Theory]
    [InlineData("D100")]
    [InlineData("W1A")]
    [InlineData("R200")]
    [InlineData("ZR1F")]
    [InlineData("M100")]
    [InlineData("X20")]
    [InlineData("Y30")]
    [InlineData("B7")]
    [InlineData("D100.3")]
    public void Modern_legacy_and_profile_parse_are_identical(string address)
    {
        MelsecAddressParser.TryParse(address, out var legacy, out _).Should().BeTrue();
        MelsecAddressParser.TryParse(address, MelsecProfiles.Modern, out var viaProfile, out _).Should().BeTrue();

        viaProfile.Should().Be(legacy);
    }

    [Theory]
    [InlineData("D100", 100)]
    [InlineData("W1A", 0x1A)]
    [InlineData("R200", 200)]
    [InlineData("ZR1F", 0x1F)]
    [InlineData("M100", 100)]
    [InlineData("X20", 0x20)]
    [InlineData("Y30", 0x30)]
    [InlineData("B7", 0x07)]
    public void Modern_request_bytes_are_byte_identical_before_and_after_registry(string address, int expectedHead)
    {
        // Before: the shipped path (legacy parse -> codec). After: profile parse -> codec.
        MelsecAddressParser.TryParse(address, out var legacy, out _).Should().BeTrue();
        MelsecAddressParser.TryParse(address, MelsecProfiles.Modern, out var viaProfile, out _).Should().BeTrue();

        var before = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 4, legacy!.Device.Code, legacy.DeviceNumber, 1);
        var after = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 4, viaProfile!.Device.Code, viaProfile.DeviceNumber, 1);

        viaProfile.DeviceNumber.Should().Be(expectedHead);
        after.Should().Equal(before);
    }

    [Theory]
    [InlineData("L0")]
    [InlineData("Z3")]
    [InlineData("DX0")]
    public void Modern_unsupported_device_errors_are_unchanged(string address)
    {
        MelsecAddressParser.TryParse(address, out _, out var error).Should().BeFalse();

        error!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
        error.Message.Should().Contain($"(supported: {MelsecDevices.SupportedList})");
    }

    // ─── 3. Existing configs resolve to Modern by default ────────────────

    [Fact]
    public void Config_without_profile_field_resolves_to_Modern()
    {
        using var doc = JsonDocument.Parse("""{"host":"10.0.0.5","port":5007}""");
        var instance = new SourceInstanceConfig
        {
            InstanceId = "melsec-legacy",
            ProtocolName = "melsec",
            DeviceId = "plc-1",
            Enabled = true,
            Connection = doc.RootElement.Clone(),
        };

        var config = MelsecSourceConfiguration.FromSourceInstance(instance);

        config.DeviceProfile.Should().Be(MelsecDeviceProfile.Modern);
        MelsecProfiles.TryResolve(config.DeviceProfile, out var profile).Should().BeTrue();
        profile!.Should().BeSameAs(MelsecProfiles.Modern);
    }

    [Theory]
    [InlineData(MelsecDeviceProfile.QnA)]
    [InlineData(MelsecDeviceProfile.ACpu)]
    public void Families_without_registry_entries_do_not_resolve(MelsecDeviceProfile key)
    {
        MelsecProfiles.TryResolve(key, out _).Should().BeFalse();
    }

    // ─── 4. iQ-F entry — audited facts ────────────────────────────────────

    [Fact]
    public void IqF_entry_is_operator_selectable_with_audited_envelope()
    {
        var p = MelsecProfiles.IqF;

        p.Key.Should().Be(MelsecDeviceProfile.IqF);
        p.IsOperatorSelectable.Should().BeTrue("Gate A-2O shipped: wizard tiles, probe, and diagnostics are wired (flip-last commit)");
        // Same 3E-binary wire shape as Modern ([COM] §37.1).
        p.FrameMode.Should().Be(MelsecFrameMode.Mc3EBinary);
        p.Transport.Should().Be(MelsecTransportProtocol.Tcp);
        p.DeviceCodeWidthBytes.Should().Be(1);
        p.HeadDeviceFieldWidthBytes.Should().Be(3);
        // 960-word cap confirmed for FX5 ([COM] §38.1 p614 + SLMP processing table p107).
        p.MaxWordsPerBatchRead.Should().Be(960);
        p.RequiresBitHeadAlignment.Should().BeFalse();
        p.ManualProvenance.Should().Contain(s => s.Contains("SH(NA)-082625ENG-J"));
    }

    [Fact]
    public void IqF_device_set_is_the_audited_set_without_ZR()
    {
        var p = MelsecProfiles.IqF;

        p.Devices.Keys.Should().BeEquivalentTo(new[]
        {
            "D", "W", "R", "M", "X", "Y", "B", "SM", "SD", "SB", "SW",
            "TS", "TC", "TN", "STS", "STC", "STN", "CS", "CC", "CN",
        });
        p.TryGetDevice("ZR", out _).Should().BeFalse("ZR is not accessible on the FX5 CPU ([COM] SLMP accessible-device list)");
        p.SupportedList.Should().Be("D, W, R, M, X, Y, B, SM, SD, SB, SW, TS, TC, TN, STS, STC, STN, CS, CC, CN");
    }

    [Theory]
    [InlineData("D", 10)]
    [InlineData("R", 10)]
    [InlineData("M", 10)]
    [InlineData("W", 16)]
    [InlineData("B", 16)]
    [InlineData("X", 8)]
    [InlineData("Y", 8)]
    [InlineData("SM", 10)]
    [InlineData("SD", 10)]
    [InlineData("SB", 16)]
    [InlineData("SW", 16)]
    public void IqF_radix_follows_the_audit(string symbol, int radix)
    {
        MelsecProfiles.IqF.TryGetDevice(symbol, out var d).Should().BeTrue();
        d!.Radix.Should().Be(radix);
        // Wire code bytes are unchanged from Modern ([COM] §38.2 table).
        MelsecDevices.TryGet(symbol == "X" || symbol == "Y" ? symbol : symbol, out var shipped);
        d.Code.Should().Be(shipped!.Code);
    }

    // ─── iQ-F X/Y octal operator labels -> numeric wire heads ────────────

    [Theory]
    [InlineData("X0", 0)]
    [InlineData("X10", 8)]       // octal label 10 = point 8
    [InlineData("X17", 15)]
    [InlineData("X1777", 1023)]  // FX5U/FX5UC upper label ([COM] p81: X "0 to 1777")
    [InlineData("Y777", 511)]
    public void IqF_XY_parse_octal_labels_to_numeric_heads(string address, int expectedHead)
    {
        MelsecAddressParser.TryParse(address, MelsecProfiles.IqF, out var parsed, out _).Should().BeTrue();

        parsed!.DeviceNumber.Should().Be(expectedHead);
    }

    [Theory]
    [InlineData("X18")]  // 8 is not an octal digit
    [InlineData("X9")]
    [InlineData("Y1A")]  // hex digits invalid under octal labels
    public void IqF_XY_rejects_non_octal_digits(string address)
    {
        MelsecAddressParser.TryParse(address, MelsecProfiles.IqF, out _, out var error).Should().BeFalse();

        error!.Code.Should().Be(MelsecAddressParser.InvalidAddress);
        error.Message.Should().Contain("octal");
    }

    [Fact]
    public void IqF_XY_octal_label_round_trips_in_ToString()
    {
        MelsecAddressParser.TryParse("X10", MelsecProfiles.IqF, out var parsed, out _).Should().BeTrue();

        parsed!.ToString().Should().Be("X10");
    }

    [Fact]
    public void IqF_ZR_rejects_with_device_not_implemented()
    {
        MelsecAddressParser.TryParse("ZR100", MelsecProfiles.IqF, out _, out var error).Should().BeFalse();

        error!.Code.Should().Be(MelsecAddressParser.DeviceNotImplemented);
        error.Message.Should().Contain("supported: D, W, R, M, X, Y, B, SM, SD, SB, SW");
    }

    // ─── iQ-F golden request — same 3E-binary wire shape ─────────────────

    [Fact]
    public void IqF_X10_produces_the_same_wire_shape_with_numeric_head()
    {
        // Operator label X10 (octal) -> point 8 -> head 08 00 00, code 9C.
        // Wire shape identical to Modern: [COM] §37.1 3E frame.
        MelsecAddressParser.TryParse("X10", MelsecProfiles.IqF, out var parsed, out _).Should().BeTrue();

        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            MelsecProfiles.IqF.RouteDefaults, monitoringTimerUnits: 4,
            parsed!.Device.Code, parsed.DeviceNumber, points: 1);

        frame.Should().Equal(
            0x50, 0x00,             // 3E request subheader
            0x00, 0xFF, 0xFF, 0x03, 0x00, // route (LocalCpu defaults)
            0x0C, 0x00,             // request data length (12)
            0x04, 0x00,             // monitoring timer (4 units)
            0x01, 0x04,             // command 0x0401
            0x00, 0x00,             // subcommand 0x0000 (word units)
            0x08, 0x00, 0x00,       // head = numeric point 8 (from octal label 10)
            0x9C,                   // device code X
            0x01, 0x00);            // 1 point
    }

    // ─── A-2O: profile-aware planner ──────────────────────────────────────

    [Fact]
    public void Planner_iqf_rejects_ZR_and_plans_octal_X_labels()
    {
        var tags = new[]
        {
            new MelsecTagDefinition { Name = "recipe", Address = "ZR100", Datatype = "Int16", ScanRateMs = 1000 },
            new MelsecTagDefinition { Name = "input_a", Address = "X10", Datatype = "Bool", ScanRateMs = 1000 },
        };

        var plan = MelsecScanPlanner.Build(tags, maxGapWords: 8, maxPointsPerRequest: 480, MelsecProfiles.IqF);

        plan.Errors.Should().ContainSingle(e => e.TagName == "recipe"
            && e.Code == MelsecAddressParser.DeviceNotImplemented);
        // X10 (octal label) -> point 8 -> block head 8, one returned word.
        plan.Blocks.Should().ContainSingle();
        plan.Blocks[0].DeviceCode.Should().Be(MelsecDeviceCode.X);
        plan.Blocks[0].HeadDeviceNumber.Should().Be(8);
    }

    [Fact]
    public void Planner_default_overload_remains_Modern()
    {
        var tags = new[]
        {
            new MelsecTagDefinition { Name = "recipe", Address = "ZR100", Datatype = "Int16", ScanRateMs = 1000 },
        };

        // Legacy 3-arg overload: ZR is valid (Modern), head parsed as hex 0x100.
        var plan = MelsecScanPlanner.Build(tags, 8, 480);

        plan.Errors.Should().BeEmpty();
        plan.Blocks.Should().ContainSingle(b => b.HeadDeviceNumber == 0x100);
    }

    [Fact]
    public void Explicit_Modern_config_hydrates_Modern()
    {
        using var doc = JsonDocument.Parse("""{"host":"10.0.0.5","port":5007,"deviceProfile":"Modern"}""");
        var instance = new SourceInstanceConfig
        {
            InstanceId = "melsec-explicit",
            ProtocolName = "melsec",
            DeviceId = "plc-1",
            Enabled = true,
            Connection = doc.RootElement.Clone(),
        };

        var config = MelsecSourceConfiguration.FromSourceInstance(instance);

        config.DeviceProfile.Should().Be(MelsecDeviceProfile.Modern);
    }

    [Fact]
    public void IqF_D100_request_is_byte_identical_to_Modern()
    {
        // Devices with unchanged radix produce identical frames on both profiles.
        MelsecAddressParser.TryParse("D100", MelsecProfiles.IqF, out var iqf, out _).Should().BeTrue();
        MelsecAddressParser.TryParse("D100", MelsecProfiles.Modern, out var modern, out _).Should().BeTrue();

        var a = SlmpFrameCodec.BuildBatchReadWordRequest(Slmp3ERoute.LocalCpu, 4, iqf!.Device.Code, iqf.DeviceNumber, 2);
        var b = SlmpFrameCodec.BuildBatchReadWordRequest(Slmp3ERoute.LocalCpu, 4, modern!.Device.Code, modern.DeviceNumber, 2);

        a.Should().Equal(b);
    }

    // ─── A-3a: special-device golden vectors (both profiles) ─────────────

    [Theory]
    [InlineData("SM0", 0x91, 0, MelsecDeviceProfile.Modern)]
    [InlineData("SD0", 0xA9, 0, MelsecDeviceProfile.Modern)]
    [InlineData("SB1F", 0xA1, 0x1F, MelsecDeviceProfile.Modern)]
    [InlineData("SW10", 0xB5, 0x10, MelsecDeviceProfile.Modern)]
    [InlineData("SM400", 0x91, 400, MelsecDeviceProfile.IqF)]
    [InlineData("SD210", 0xA9, 210, MelsecDeviceProfile.IqF)]
    [InlineData("SB1F", 0xA1, 0x1F, MelsecDeviceProfile.IqF)]
    [InlineData("SW10", 0xB5, 0x10, MelsecDeviceProfile.IqF)]
    public void A3a_special_device_requests_use_audited_codes(
        string address, int codeByte, int head, MelsecDeviceProfile profileKey)
    {
        // Codes/radix from [MC] SH(NA)-080008-AB §8.1 (p68): SM=91 dec, SD=A9 dec,
        // SB=A1 hex, SW=B5 hex; FX5 availability per [COM] accessible list.
        MelsecProfiles.TryResolve(profileKey, out var profile).Should().BeTrue();
        MelsecAddressParser.TryParse(address, profile!, out var parsed, out _).Should().BeTrue();

        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 4, parsed!.Device.Code, parsed.DeviceNumber, 1);

        parsed.DeviceNumber.Should().Be(head);
        frame[18].Should().Be((byte)codeByte);                    // device-code byte
        (frame[15] | (frame[16] << 8) | (frame[17] << 16)).Should().Be(head); // 3-byte LE head
    }
}
