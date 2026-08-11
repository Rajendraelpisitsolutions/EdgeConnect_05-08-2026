using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpValidationTests
{
    private static EthernetIpSourceConfiguration BaseConfig(params EthernetIpTagDefinition[] tags) =>
        new()
        {
            InstanceId = "eip-1",
            ProtocolName = "ethernetip",
            DeviceId = "dev-1",
            DeviceClass = "plc",
            Host = "10.0.0.1",
            CpuFamily = EthernetIpCpuFamily.ControlLogix,
            TagDefinitions = tags,
        };

    private static EthernetIpSourceAdapter NewAdapter() =>
        new("eip-1", new FakeEthernetIpClient(), NullLogger.Instance);

    [Fact]
    public async Task ValidateConfigAsync_Valid_Succeeds()
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition { Name = "speed", Address = "Speed", Datatype = "DINT" });
        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingHost_Fails()
    {
        var cfg = BaseConfig() with { Host = "" };
        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "Host");
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingDeviceClass_Fails()
    {
        var cfg = BaseConfig() with { DeviceClass = null };
        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "DeviceClass");
    }

    [Fact]
    public async Task ValidateConfigAsync_UnknownDatatype_Fails()
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition { Name = "x", Address = "X", Datatype = "WIDGET" });
        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path!.Contains("Datatype"));
    }

    [Fact]
    public async Task ValidateConfigAsync_ScaleOnBool_Fails()
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition { Name = "x", Address = "X", Datatype = "BOOL", Scale = 2.0 });
        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path!.Contains("Scale"));
    }

    [Fact]
    public async Task ValidateConfigAsync_WrongConfigType_Fails()
    {
        var result = await NewAdapter().ValidateConfigAsync(new DummyConfig(), CancellationToken.None);
        result.IsValid.Should().BeFalse();
    }

    private sealed record DummyConfig : SourceConfiguration
    {
        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public DummyConfig()
        {
            InstanceId = "x";
            ProtocolName = "other";
            DeviceId = "d";
        }
    }
}

public class EthernetIpBundleRedactionRulesTests
{
    [Fact]
    public void AllKeys_AreIncludeTier()
    {
        var rules = new EthernetIpBundleRedactionRules();
        rules.ProtocolName.Should().Be("ethernetip");
        rules.KnownKeys.Should().NotBeEmpty();
        rules.KnownKeys.Values.Should().OnlyContain(t => t == BundleTier.Include);
        rules.KnownKeys.Keys.Should().Contain("host").And.Contain("path").And.Contain("cpuFamily");
    }
}
