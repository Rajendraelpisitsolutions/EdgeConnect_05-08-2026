// ============================================================================
// Tests: MelsecSourceWizardModel — Connection JSON shape (via MelsecConnectionKeys),
//        defaults sourced from the config record, real-parser validation (ZR=hex,
//        DEVICE_NOT_IMPLEMENTED, datatype mismatch), fixed-mode block + normalize,
//        route-field ranges, and Build/Hydrate round-trip stability.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.Melsec;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MelsecSourceWizardModelTests
{
    private static MelsecSourceWizardModel SampleModel() => new()
    {
        InstanceId = "melsec-press-2",
        DeviceId = "iQ-R-P2",
        DeviceName = "Press 2 PLC",
        DeviceClass = "plc",
        Host = "192.168.3.50",
        Port = 6000,
        Tags =
        {
            new MelsecTagWizardRow { Name = "stroke_count", Address = "D100", Datatype = "UInt16", ScanRateMs = 1000 },
        },
    };

    // ── BuildSourceInstance shape ─────────────────────────────────────────

    [Fact]
    public void BuildSourceInstance_PopulatesIdentity_AndProtocolIsMelsec()
    {
        var instance = SampleModel().BuildSourceInstance();

        instance.InstanceId.Should().Be("melsec-press-2");
        instance.ProtocolName.Should().Be("melsec");
        instance.DeviceId.Should().Be("iQ-R-P2");
        instance.DeviceName.Should().Be("Press 2 PLC");
        instance.DeviceClass.Should().Be("plc");
        instance.Enabled.Should().BeTrue();
    }

    [Fact]
    public void BuildSourceInstance_PacksConnectionKeys_WithDefaults()
    {
        var conn = SampleModel().BuildSourceInstance().Connection!.Value;

        conn.GetProperty("host").GetString().Should().Be("192.168.3.50");
        conn.GetProperty("port").GetInt32().Should().Be(6000);
        conn.GetProperty("transportProtocol").GetString().Should().Be("Tcp");
        conn.GetProperty("frameMode").GetString().Should().Be("Mc3EBinary");
        conn.GetProperty("deviceProfile").GetString().Should().Be("Modern");
        conn.GetProperty("networkNo").GetInt32().Should().Be(0x00);
        conn.GetProperty("pcNo").GetInt32().Should().Be(0xFF);
        conn.GetProperty("requestDestModuleIoNo").GetInt32().Should().Be(0x03FF);
        conn.GetProperty("requestDestModuleStationNo").GetInt32().Should().Be(0x00);
        conn.GetProperty("monitoringTimerMs").GetInt32().Should().Be(4000);
        conn.GetProperty("maxPointsPerRequest").GetInt32().Should().Be(480);
        conn.GetProperty("maxGapWords").GetInt32().Should().Be(8);
    }

    [Fact]
    public void BuildSourceInstance_EmitsTagsUnderTagsKey()
    {
        var conn = SampleModel().BuildSourceInstance().Connection!.Value;

        var tags = conn.GetProperty("tags");
        tags.GetArrayLength().Should().Be(1);
        var first = tags.EnumerateArray().First();
        first.GetProperty("name").GetString().Should().Be("stroke_count");
        first.GetProperty("address").GetString().Should().Be("D100");
        first.GetProperty("datatype").GetString().Should().Be("UInt16");
        first.GetProperty("scanRateMs").GetInt32().Should().Be(1000);
    }

    [Fact]
    public void BuildSourceInstance_WordOrder_OmittedForLowWordFirst_WrittenForHighWordFirst()
    {
        var model = SampleModel();
        model.Tags.Clear();
        model.Tags.Add(new MelsecTagWizardRow { Name = "a", Address = "D200", Datatype = "UInt32", WordOrder = "LowWordFirst", ScanRateMs = 1000 });
        model.Tags.Add(new MelsecTagWizardRow { Name = "b", Address = "D300", Datatype = "Float32", WordOrder = "HighWordFirst", ScanRateMs = 1000 });

        var tags = model.BuildSourceInstance().Connection!.Value.GetProperty("tags").EnumerateArray().ToList();

        tags[0].TryGetProperty("wordOrder", out _).Should().BeFalse("LowWordFirst is the default and is omitted");
        tags[1].GetProperty("wordOrder").GetString().Should().Be("HighWordFirst");
    }

    [Fact]
    public void BuildSourceInstance_OmitsOptionalTagFields_WhenNull()
    {
        var tag = SampleModel().BuildSourceInstance().Connection!.Value.GetProperty("tags").EnumerateArray().First();

        tag.TryGetProperty("unit", out _).Should().BeFalse();
        tag.TryGetProperty("scale", out _).Should().BeFalse();
        tag.TryGetProperty("offset", out _).Should().BeFalse();
        tag.TryGetProperty("wordOrder", out _).Should().BeFalse();
    }

    // ── Defaults sourced from the backend config record ───────────────────

    [Fact]
    public void Defaults_MatchBackendConfigRecord()
    {
        var backend = new MelsecSourceConfiguration { InstanceId = "", ProtocolName = "melsec", DeviceId = "", Host = "" };
        var model = new MelsecSourceWizardModel();

        model.MaxPointsPerRequest.Should().Be(backend.MaxPointsPerRequest).And.Be(480);
        model.MonitoringTimerMs.Should().Be(backend.MonitoringTimerMs);
        model.RequestTimeoutMs.Should().Be(backend.RequestTimeoutMs);
        model.PcNo.Should().Be(backend.PcNo);
        model.RequestDestModuleIoNo.Should().Be(backend.RequestDestModuleIoNo);
        model.CircuitBreakerThreshold.Should().Be(backend.CircuitBreakerThreshold);
        model.MaxGapWords.Should().Be(backend.MaxGapWords);
    }

    // ── Round-trip ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_HydrateThenBuild_IsByteStable()
    {
        var model = SampleModel();
        model.Tags.Add(new MelsecTagWizardRow { Name = "cyc", Address = "D200", Datatype = "UInt32", WordOrder = "HighWordFirst", ScanRateMs = 500, Unit = "cycles", Scale = 0.1 });
        model.Tags.Add(new MelsecTagWizardRow { Name = "run", Address = "D100.3", Datatype = "Bool", ScanRateMs = 500 });

        var first = model.BuildSourceInstance();
        var rebuilt = MelsecSourceWizardModel.HydrateFromExisting(first).BuildSourceInstance();

        rebuilt.Connection!.Value.GetRawText().Should().Be(first.Connection!.Value.GetRawText());
    }

    // ── Validation (real backend parser) ──────────────────────────────────

    [Theory]
    [InlineData("D26", "UInt16")]    // decimal
    [InlineData("R200", "Int16")]    // decimal
    [InlineData("W1A", "UInt16")]    // hex
    [InlineData("ZR1F", "UInt16")]   // ZR hex (corrected)
    [InlineData("X20", "Bool")]      // hex bit device
    [InlineData("D100.3", "Bool")]   // word-bit
    public void ValidateTag_accepts_valid_addresses(string address, string datatype)
    {
        var errors = MelsecSourceWizardModel.ValidateTag(
            new MelsecTagWizardRow { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 });
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_unsupported_device_is_DeviceNotImplemented()
    {
        var errors = MelsecSourceWizardModel.ValidateTag(
            new MelsecTagWizardRow { Name = "t", Address = "T5", Datatype = "Bool", ScanRateMs = 1000 }); // T = A-3b territory, still unsupported
        errors.Should().Contain(e => e.Code == "MELSEC.DEVICE_NOT_IMPLEMENTED" && e.Path == "Address");
    }

    [Fact]
    public void ValidateTag_malformed_address_is_InvalidAddress()
    {
        var errors = MelsecSourceWizardModel.ValidateTag(
            new MelsecTagWizardRow { Name = "t", Address = "Q1", Datatype = "UInt16", ScanRateMs = 1000 });
        errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_INVALID_ADDRESS" && e.Path == "Address");
    }

    [Theory]
    [InlineData("D100", "Bool")]     // Bool on a plain word device
    [InlineData("M100", "Int16")]    // non-Bool on a bit device
    public void ValidateTag_datatype_mismatch(string address, string datatype)
    {
        var errors = MelsecSourceWizardModel.ValidateTag(
            new MelsecTagWizardRow { Name = "t", Address = address, Datatype = datatype, ScanRateMs = 1000 });
        errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_DATATYPE_MISMATCH");
    }

    // ── Fixed-mode block + normalize (no silent normalize on hydrate) ─────

    [Fact]
    public void Validate_unsupported_transport_blocks_save()
    {
        var model = SampleModel();
        model.TransportProtocol = "Udp"; // as if hand-edited in gateway.json

        var result = model.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");
    }

    [Fact]
    public void Hydrate_preserves_unsupported_mode_then_NormalizeToSlice1_unblocks()
    {
        var handEdited = SampleModel();
        handEdited.FrameMode = "Mc4EBinary";
        var instance = handEdited.BuildSourceInstance();

        var hydrated = MelsecSourceWizardModel.HydrateFromExisting(instance);
        hydrated.FrameMode.Should().Be("Mc4EBinary", "hydrate must not silently normalize");
        hydrated.Validate().Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");

        hydrated.NormalizeToSlice1();
        hydrated.Validate().Errors.Should().NotContain(e => e.Code == "MELSEC.CONFIG_MODE_NOT_IMPLEMENTED");
    }

    // ── Connection-level validation ───────────────────────────────────────

    [Fact]
    public void Validate_route_field_out_of_range()
    {
        var model = SampleModel();
        model.PcNo = 300; // byte range is 0–255

        model.Validate().Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_ROUTE_RANGE" && e.Path == "PcNo");
    }

    [Fact]
    public void Validate_points_cap_over_960()
    {
        var model = SampleModel();
        model.MaxPointsPerRequest = 961;

        model.Validate().Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_POINTS_CAP");
    }

    [Fact]
    public void Validate_incoherent_request_timeout()
    {
        var model = SampleModel();
        model.MonitoringTimerMs = 4000;
        model.RequestTimeoutMs = 1000; // shorter than monitoring timer

        model.Validate().Errors.Should().Contain(e => e.Code == "MELSEC.CONFIG_TIMEOUT_INCOHERENT");
    }

    [Fact]
    public void Validate_duplicate_tag_name_blocks_duplicate_address_warns()
    {
        var model = SampleModel();
        model.Tags.Clear();
        model.Tags.Add(new MelsecTagWizardRow { Name = "dup", Address = "D100", Datatype = "UInt16", ScanRateMs = 1000 });
        model.Tags.Add(new MelsecTagWizardRow { Name = "dup", Address = "D100", Datatype = "UInt16", ScanRateMs = 1000 });

        var result = model.Validate();
        result.Errors.Should().Contain(e => e.Message.Contains("Tag name 'dup'"));
        result.Warnings.Should().Contain(e => e.Path == "Address");
    }

    [Fact]
    public void Validate_clean_model_is_valid()
    {
        SampleModel().Validate().IsValid.Should().BeTrue();
    }
}
