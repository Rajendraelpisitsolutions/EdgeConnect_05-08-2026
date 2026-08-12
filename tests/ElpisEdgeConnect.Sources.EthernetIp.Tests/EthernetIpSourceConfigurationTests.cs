using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpSourceConfigurationTests
{
    private static SourceInstanceConfig MakeInstance(string connectionJson, string protocol = "ethernetip")
    {
        using var doc = JsonDocument.Parse(connectionJson);
        return new SourceInstanceConfig
        {
            InstanceId = "eip-1",
            ProtocolName = protocol,
            DeviceId = "dev-1",
            DeviceName = "Line 3 PLC",
            DeviceClass = "plc",
            Enabled = true,
            Polling = new PollingSettings { IntervalMs = 250 },
            Connection = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public void FromSourceInstance_ParsesConnectionAndTags()
    {
        var json = """
        {
          "host": "10.0.0.50",
          "path": "1,0",
          "cpuFamily": "ControlLogix",
          "connectTimeoutMs": 1500,
          "tags": [
            { "name": "speed", "address": "Program:Main.Speed", "datatype": "DINT", "scanRateMs": 500, "unit": "rpm" }
          ]
        }
        """;

        var config = EthernetIpSourceConfiguration.FromSourceInstance(MakeInstance(json));

        config.Host.Should().Be("10.0.0.50");
        config.Path.Should().Be("1,0");
        config.CpuFamily.Should().Be(EthernetIpCpuFamily.ControlLogix);
        config.ConnectTimeoutMs.Should().Be(1500);
        config.PollIntervalMs.Should().Be(250);
        config.TagDefinitions.Should().HaveCount(1);
        config.TagDefinitions[0].Name.Should().Be("speed");
        config.TagDefinitions[0].Address.Should().Be("Program:Main.Speed");
        config.TagDefinitions[0].Datatype.Should().Be("DINT");
        config.TagDefinitions[0].ScanRateMs.Should().Be(500);
        config.TagDefinitions[0].Unit.Should().Be("rpm");
    }

    [Fact]
    public void FromSourceInstance_NoPath_DefaultsFromCpuFamily()
    {
        var controlLogix = EthernetIpSourceConfiguration.FromSourceInstance(
            MakeInstance("""{ "host": "10.0.0.1", "cpuFamily": "ControlLogix" }"""));
        controlLogix.Path.Should().Be("1,0");

        var micro800 = EthernetIpSourceConfiguration.FromSourceInstance(
            MakeInstance("""{ "host": "10.0.0.1", "cpuFamily": "Micro800" }"""));
        micro800.Path.Should().BeEmpty();
    }

    [Fact]
    public void FromSourceInstance_MissingHost_Throws()
    {
        Action act = () => EthernetIpSourceConfiguration.FromSourceInstance(
            MakeInstance("""{ "cpuFamily": "ControlLogix" }"""));
        act.Should().Throw<ArgumentException>().WithMessage("*host*");
    }

    [Fact]
    public void FromSourceInstance_WrongProtocol_Throws()
    {
        Action act = () => EthernetIpSourceConfiguration.FromSourceInstance(
            MakeInstance("""{ "host": "10.0.0.1" }""", protocol: "modbustcp"));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromSourceInstance_TagMissingAddress_Throws()
    {
        var json = """{ "host": "10.0.0.1", "tags": [ { "name": "x", "datatype": "DINT" } ] }""";
        Action act = () => EthernetIpSourceConfiguration.FromSourceInstance(MakeInstance(json));
        act.Should().Throw<ArgumentException>().WithMessage("*address*");
    }

    [Fact]
    public void LicenseModuleKey_IsStable()
    {
        EthernetIpSourceConfiguration.LicenseModuleKey.Should().Be("source-ethernet-ip");
        EthernetIpSourceConfiguration.ProtocolNameConstant.Should().Be("ethernetip");
    }
}
