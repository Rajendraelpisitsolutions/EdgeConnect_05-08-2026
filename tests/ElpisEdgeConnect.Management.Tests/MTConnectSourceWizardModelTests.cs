// ============================================================================
// Tests: MTConnectSourceWizardModel (M.2b.4 M3). Pins the canonical config the
//        wizard emits, the Edit-mode hydrate round-trip invariant, and the
//        AgentBaseUrl-required guard.
// ============================================================================

using System;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.MTConnect;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class MTConnectSourceWizardModelTests
{
    private static MTConnectSourceWizardModel Sample() => new()
    {
        InstanceId = "mt-1",
        AgentBaseUrl = "http://agent.local:5000",
        AgentDeviceName = "VCN-530C",
        TimeoutSeconds = 7,
        PollIntervalMs = 2000,
        DegradeAfterConsecutiveFailures = 4,
    };

    [Fact]
    public void Build_ProducesMtconnectConfig_WithConnectionBlock()
    {
        var config = Sample().BuildSourceInstance();

        config.ProtocolName.Should().Be("mtconnect");
        config.InstanceId.Should().Be("mt-1");
        config.DeviceId.Should().Be("mt-1");   // defaults to InstanceId
        config.DeviceName.Should().Be("mt-1"); // defaults to InstanceId
        config.Polling.IntervalMs.Should().Be(2000);

        config.Connection!.Value.GetProperty(MTConnectConnectionKeys.AgentBaseUrl).GetString()
            .Should().Be("http://agent.local:5000");
        config.Connection!.Value.GetProperty(MTConnectConnectionKeys.AgentDeviceName).GetString()
            .Should().Be("VCN-530C");
        config.Connection!.Value.GetProperty(MTConnectConnectionKeys.TimeoutSeconds).GetInt32()
            .Should().Be(7);
    }

    [Fact]
    public void Build_BlankAgentUrl_Throws()
    {
        var act = () => new MTConnectSourceWizardModel { InstanceId = "x" }.BuildSourceInstance();
        act.Should().Throw<InvalidOperationException>().WithMessage("*agentBaseUrl*");
    }

    [Fact]
    public void Build_OmitsAgentDeviceName_WhenBlank()
    {
        var config = new MTConnectSourceWizardModel { InstanceId = "x", AgentBaseUrl = "http://a:5000" }
            .BuildSourceInstance();

        config.Connection!.Value.TryGetProperty(MTConnectConnectionKeys.AgentDeviceName, out _)
            .Should().BeFalse("a blank device name is omitted, not written as empty");
    }

    [Fact]
    public void HydrateThenBuild_IsValueEquivalent()
    {
        var first = Sample().BuildSourceInstance();
        var roundTripped = MTConnectSourceWizardModel.HydrateFromExisting(first).BuildSourceInstance();

        Serialize(roundTripped).Should().Be(Serialize(first));
    }

    [Fact]
    public void Hydrate_WrongProtocol_Throws()
    {
        var modbus = new SourceInstanceConfig
        {
            InstanceId = "m", ProtocolName = "modbustcp", DeviceId = "m",
            Polling = new PollingSettings { IntervalMs = 1000 },
        };
        var act = () => MTConnectSourceWizardModel.HydrateFromExisting(modbus);
        act.Should().Throw<ArgumentException>().WithMessage("*expected 'mtconnect'*");
    }

    private static string Serialize(SourceInstanceConfig c) =>
        JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = false });
}
