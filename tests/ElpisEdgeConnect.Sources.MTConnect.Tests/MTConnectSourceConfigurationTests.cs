// ============================================================================
// File: MTConnectSourceConfigurationTests.cs
// Purpose: Parser tests for MTConnectSourceConfiguration.FromSourceInstance
//          — the JSON → typed config bridge used by the host's
//          AddMTConnectSourcesFromGatewayConfig extension.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

public sealed class MTConnectSourceConfigurationTests
{
    [Fact]
    public void FromSourceInstance_MinimalConnection_AppliesDefaults()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mtc-haas-1",
            ProtocolName = "mtconnect",
            DeviceId = "haas1",
            Connection = JsonDocument.Parse(
                """{ "agentBaseUrl": "http://192.168.1.50:5000/" }""").RootElement,
        };

        var typed = MTConnectSourceConfiguration.FromSourceInstance(instance);

        typed.InstanceId.Should().Be("mtc-haas-1");
        typed.AgentBaseUrl.Should().Be("http://192.168.1.50:5000/");
        typed.AgentDeviceName.Should().BeNull();
        typed.TimeoutSeconds.Should().Be(10);
        typed.InitialBackoffMs.Should().Be(2000);
        typed.MaxBackoffMs.Should().Be(60_000);
        typed.BackoffMultiplier.Should().Be(2.0);
        typed.DegradeAfterConsecutiveFailures.Should().Be(3);
        typed.PollIntervalMs.Should().Be(1000, "PollingSettings.IntervalMs default");
    }

    [Fact]
    public void FromSourceInstance_FullConnection_OverridesDefaults()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mtc-dmg-1",
            ProtocolName = "mtconnect",
            DeviceId = "dmg1",
            DeviceName = "DMG Mori NHX5000",
            Polling = new PollingSettings { IntervalMs = 2500, MaxConsecutiveErrors = 5 },
            Connection = JsonDocument.Parse("""
                {
                    "agentBaseUrl": "https://agents.example.com:5001/",
                    "agentDeviceName": "NHX5000",
                    "timeoutSeconds": 20,
                    "initialBackoffMs": 1000,
                    "maxBackoffMs": 30000,
                    "backoffMultiplier": 1.5,
                    "degradeAfterConsecutiveFailures": 5
                }
                """).RootElement,
        };

        var typed = MTConnectSourceConfiguration.FromSourceInstance(instance);

        typed.AgentBaseUrl.Should().Be("https://agents.example.com:5001/");
        typed.AgentDeviceName.Should().Be("NHX5000");
        typed.TimeoutSeconds.Should().Be(20);
        typed.InitialBackoffMs.Should().Be(1000);
        typed.MaxBackoffMs.Should().Be(30000);
        typed.BackoffMultiplier.Should().Be(1.5);
        typed.DegradeAfterConsecutiveFailures.Should().Be(5);
        typed.PollIntervalMs.Should().Be(2500);
        typed.DeviceName.Should().Be("DMG Mori NHX5000");
    }

    [Fact]
    public void FromSourceInstance_WrongProtocol_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mb-1",
            ProtocolName = "modbus",
            DeviceId = "d1",
            Connection = JsonDocument.Parse(
                """{ "agentBaseUrl": "http://foo/" }""").RootElement,
        };

        var act = () => MTConnectSourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*Expected protocolName 'mtconnect'*got 'modbus'*");
    }

    [Fact]
    public void FromSourceInstance_MissingConnection_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mtc-x",
            ProtocolName = "mtconnect",
            DeviceId = "x",
        };

        var act = () => MTConnectSourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required MTConnect Connection object*");
    }

    [Fact]
    public void FromSourceInstance_MissingAgentBaseUrl_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mtc-y",
            ProtocolName = "mtconnect",
            DeviceId = "y",
            Connection = JsonDocument.Parse("""{ "timeoutSeconds": 5 }""").RootElement,
        };

        var act = () => MTConnectSourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required 'agentBaseUrl' field*");
    }
}
