// ============================================================================
// File: Focas2SourceConfigurationTests.cs
// Purpose: Tests for the JSON → typed-config bridge
//          Focas2SourceConfiguration.FromSourceInstance. This is the
//          production path for config-driven launches (Program.cs eager
//          pre-load), and pinning the parsing shape stops a teammate from
//          silently breaking it.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

public sealed class Focas2SourceConfigurationTests
{
    [Fact]
    public void FromSourceInstance_MinimalConnection_PopulatesDefaults()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "focas-lathe-1",
            ProtocolName = "focas2",
            DeviceId = "lathe1",
            Connection = JsonDocument.Parse("""
                { "ipAddress": "192.168.1.101" }
                """).RootElement,
        };

        var typed = Focas2SourceConfiguration.FromSourceInstance(instance);

        typed.InstanceId.Should().Be("focas-lathe-1");
        typed.ProtocolName.Should().Be("focas2");
        typed.DeviceId.Should().Be("lathe1");
        typed.IpAddress.Should().Be("192.168.1.101");
        typed.Port.Should().Be(8193, "default FOCAS2 port");
        typed.TimeoutSeconds.Should().Be(10, "default timeout");
        typed.KeepAlive.Should().BeTrue("KeepAlive defaults to true");
        typed.DataPoints.Should().BeEmpty("empty DataPoints means 'collect all'");
        typed.MaxConnectRetries.Should().Be(5, "default retry count");
        typed.PollIntervalMs.Should().Be(1000, "default PollingSettings.IntervalMs");
    }

    [Fact]
    public void FromSourceInstance_FullConnection_OverridesDefaults()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "focas-vmc-1",
            ProtocolName = "focas2",
            DeviceId = "vmc1",
            DeviceName = "Mori Seiki VMC",
            Polling = new PollingSettings { IntervalMs = 3000, MaxConsecutiveErrors = 5 },
            Connection = JsonDocument.Parse("""
                {
                    "ipAddress": "10.0.0.55",
                    "port": 8194,
                    "timeoutSeconds": 15,
                    "keepAlive": false,
                    "dataPoints": ["Status/RunState", "Axes/", "Spindle/Speed"],
                    "initialBackoffMs": 2000,
                    "maxBackoffMs": 60000,
                    "backoffMultiplier": 1.5,
                    "maxConnectRetries": 10
                }
                """).RootElement,
        };

        var typed = Focas2SourceConfiguration.FromSourceInstance(instance);

        typed.IpAddress.Should().Be("10.0.0.55");
        typed.Port.Should().Be(8194);
        typed.TimeoutSeconds.Should().Be(15);
        typed.KeepAlive.Should().BeFalse();
        typed.DataPoints.Should().BeEquivalentTo("Status/RunState", "Axes/", "Spindle/Speed");
        typed.InitialBackoffMs.Should().Be(2000);
        typed.MaxBackoffMs.Should().Be(60000);
        typed.BackoffMultiplier.Should().Be(1.5);
        typed.MaxConnectRetries.Should().Be(10);
        typed.PollIntervalMs.Should().Be(3000, "PollingSettings.IntervalMs plumbs through");
        typed.DeviceName.Should().Be("Mori Seiki VMC");
    }

    [Fact]
    public void FromSourceInstance_WrongProtocol_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "mb-1",
            ProtocolName = "modbus",
            DeviceId = "d1",
            Connection = JsonDocument.Parse("""{ "ipAddress": "1.2.3.4" }""").RootElement,
        };

        var act = () => Focas2SourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*Expected protocolName 'focas2'*got 'modbus'*");
    }

    [Fact]
    public void FromSourceInstance_MissingConnection_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "focas-x",
            ProtocolName = "focas2",
            DeviceId = "x",
            // Connection is null
        };

        var act = () => Focas2SourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required FOCAS2 Connection object*");
    }

    [Fact]
    public void FromSourceInstance_MissingIpAddress_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "focas-y",
            ProtocolName = "focas2",
            DeviceId = "y",
            Connection = JsonDocument.Parse("""{ "port": 8193 }""").RootElement,
        };

        var act = () => Focas2SourceConfiguration.FromSourceInstance(instance);
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required 'ipAddress' field*");
    }

    [Fact]
    public void FromSourceInstance_BadDataPointEntries_AreSkipped()
    {
        // Mixed-type array — strings kept, non-strings silently dropped.
        var instance = new SourceInstanceConfig
        {
            InstanceId = "focas-mixed",
            ProtocolName = "focas2",
            DeviceId = "m1",
            Connection = JsonDocument.Parse("""
                {
                    "ipAddress": "192.168.1.10",
                    "dataPoints": ["Status/RunState", 123, null, "", "Axes/"]
                }
                """).RootElement,
        };

        var typed = Focas2SourceConfiguration.FromSourceInstance(instance);

        typed.DataPoints.Should().BeEquivalentTo("Status/RunState", "Axes/");
    }
}
