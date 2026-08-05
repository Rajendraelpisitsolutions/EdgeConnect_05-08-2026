// ============================================================================
// Tests: EthernetIpSourceWizardModel — BuildSourceInstance round-trips through
//        EthernetIpSourceConfiguration.FromSourceInstance, HydrateFromExisting
//        is the inverse, and Validate catches the structural problems.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class EthernetIpSourceWizardModelTests
{
    private static EthernetIpSourceWizardModel ValidModel()
    {
        var m = new EthernetIpSourceWizardModel
        {
            InstanceId = "plc-line-3",
            DeviceId = "dev-1",
            DeviceName = "Line 3 PLC",
            DeviceClass = "plc",
            Host = "10.0.0.50",
            Path = "1,0",
            CpuFamily = "ControlLogix",
        };
        m.Tags.Add(new EthernetIpTagWizardRow { Name = "speed", Address = "Program:Main.Speed", Datatype = "DINT", ScanRateMs = 500, Unit = "rpm" });
        return m;
    }

    [Fact]
    public void BuildSourceInstance_ProducesParsableConfig()
    {
        var instance = ValidModel().BuildSourceInstance();

        instance.ProtocolName.Should().Be("ethernetip");
        instance.InstanceId.Should().Be("plc-line-3");

        // Round-trips through the adapter's own parser.
        var config = EthernetIpSourceConfiguration.FromSourceInstance(instance);
        config.Host.Should().Be("10.0.0.50");
        config.CpuFamily.Should().Be(EthernetIpCpuFamily.ControlLogix);
        config.TagDefinitions.Should().ContainSingle();
        config.TagDefinitions[0].Address.Should().Be("Program:Main.Speed");
        config.TagDefinitions[0].Unit.Should().Be("rpm");
    }

    [Fact]
    public void HydrateFromExisting_IsInverseOfBuild()
    {
        var original = ValidModel();
        var hydrated = EthernetIpSourceWizardModel.HydrateFromExisting(original.BuildSourceInstance());

        hydrated.Host.Should().Be(original.Host);
        hydrated.CpuFamily.Should().Be(original.CpuFamily);
        hydrated.Path.Should().Be(original.Path);
        hydrated.Tags.Should().ContainSingle();
        hydrated.Tags[0].Name.Should().Be("speed");
        hydrated.Tags[0].Datatype.Should().Be("DINT");
        hydrated.Tags[0].Unit.Should().Be("rpm");
    }

    [Fact]
    public void Validate_Valid_Succeeds()
    {
        ValidModel().Validate().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingHost_Fails()
    {
        var m = ValidModel();
        m.Host = "";
        m.Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DuplicateTagNames_Fails()
    {
        var m = ValidModel();
        m.Tags.Add(new EthernetIpTagWizardRow { Name = "speed", Address = "Other", Datatype = "INT" });
        var result = m.Validate();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_UnknownDatatype_Fails()
    {
        var m = ValidModel();
        m.Tags[0].Datatype = "WIDGET";
        m.Validate().IsValid.Should().BeFalse();
    }

    [Fact]
    public void DefaultPathFor_LogixVsEmbedded()
    {
        EthernetIpSourceWizardModel.DefaultPathFor("ControlLogix").Should().Be("1,0");
        EthernetIpSourceWizardModel.DefaultPathFor("Micro800").Should().BeEmpty();
    }
}
