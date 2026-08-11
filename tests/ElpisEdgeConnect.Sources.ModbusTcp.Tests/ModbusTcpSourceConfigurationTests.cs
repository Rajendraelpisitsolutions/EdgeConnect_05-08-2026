// ============================================================================
// File: ModbusTcpSourceConfigurationTests.cs
// Purpose: Unit tests for the JSON → ModbusTcpSourceConfiguration factory.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusTcpSourceConfigurationTests
{
    [Fact]
    public void FromSourceInstance_MinimalHost_AppliesDefaults()
    {
        var json = JsonDocument.Parse("""{ "host": "192.168.1.50" }""").RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "plc-1",
            ProtocolName = "modbustcp",
            DeviceId = "plc1",
            Connection = json,
        };

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.Host.Should().Be("192.168.1.50");
        cfg.Port.Should().Be(502);
        cfg.Encapsulation.Should().Be(ModbusEncapsulation.Tcp);
        cfg.DefaultUnitId.Should().Be(1);
        cfg.ConnectTimeoutMs.Should().Be(2000);
        cfg.RequestTimeoutMs.Should().Be(1000);
        cfg.KeepAlive.Should().BeTrue();
        cfg.MaxTransactionRetries.Should().Be(2);
        cfg.CircuitBreakerThreshold.Should().Be(5);
    }

    [Fact]
    public void FromSourceInstance_AllFields_Parses()
    {
        var json = JsonDocument.Parse("""
        {
          "host": "plc.internal",
          "port": 1502,
          "encapsulation": "rtuOverTcp",
          "defaultUnitId": 7,
          "connectTimeoutMs": 5000,
          "requestTimeoutMs": 2500,
          "keepAlive": false,
          "maxTransactionRetries": 4,
          "initialBackoffMs": 500,
          "maxBackoffMs": 30000,
          "backoffMultiplier": 3.0,
          "circuitBreakerThreshold": 10,
          "circuitBreakerResetMs": 90000
        }
        """).RootElement;

        var instance = new SourceInstanceConfig
        {
            InstanceId = "plc-2",
            ProtocolName = "modbustcp",
            DeviceId = "plc2",
            Connection = json,
        };

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.Host.Should().Be("plc.internal");
        cfg.Port.Should().Be((ushort)1502);
        cfg.Encapsulation.Should().Be(ModbusEncapsulation.RtuOverTcp);
        cfg.DefaultUnitId.Should().Be((byte)7);
        cfg.ConnectTimeoutMs.Should().Be(5000);
        cfg.RequestTimeoutMs.Should().Be(2500);
        cfg.KeepAlive.Should().BeFalse();
        cfg.MaxTransactionRetries.Should().Be(4);
        cfg.InitialBackoffMs.Should().Be(500);
        cfg.MaxBackoffMs.Should().Be(30000);
        cfg.BackoffMultiplier.Should().Be(3.0);
        cfg.CircuitBreakerThreshold.Should().Be(10);
        cfg.CircuitBreakerResetMs.Should().Be(90_000);
    }

    [Fact]
    public void FromSourceInstance_MissingConnection_Throws()
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "plc-3",
            ProtocolName = "modbustcp",
            DeviceId = "plc3",
            Connection = null,
        };

        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required Modbus Connection object*");
    }

    [Fact]
    public void FromSourceInstance_MissingHost_Throws()
    {
        var json = JsonDocument.Parse("""{ "port": 502 }""").RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "plc-4",
            ProtocolName = "modbustcp",
            DeviceId = "plc4",
            Connection = json,
        };

        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing the required 'host' field*");
    }

    [Fact]
    public void FromSourceInstance_WrongProtocol_Throws()
    {
        var json = JsonDocument.Parse("""{ "host": "x" }""").RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "p5",
            ProtocolName = "focas2",
            DeviceId = "d5",
            Connection = json,
        };

        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*Expected protocolName 'modbustcp'*");
    }

    [Theory]
    [InlineData("tcp", ModbusEncapsulation.Tcp)]
    [InlineData("TCP", ModbusEncapsulation.Tcp)]
    [InlineData("rtuOverTcp", ModbusEncapsulation.RtuOverTcp)]
    [InlineData("rtu-over-tcp", ModbusEncapsulation.RtuOverTcp)]
    [InlineData("unknown", ModbusEncapsulation.Tcp)]
    public void FromSourceInstance_Encapsulation_ParsesOrFallsBackToTcp(string raw, ModbusEncapsulation expected)
    {
        var json = JsonDocument.Parse($$$"""{ "host": "x", "encapsulation": "{{{raw}}}" }""").RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "p",
            ProtocolName = "modbustcp",
            DeviceId = "d",
            Connection = json,
        };

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.Encapsulation.Should().Be(expected);
    }

    // ============================================================
    // F4: tagDefinitions array parsing
    // ============================================================

    [Fact]
    public void FromSourceInstance_EmptyTagDefinitions_ReturnsEmpty()
    {
        var json = JsonDocument.Parse("""{ "host": "plc.x" }""").RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "p",
            ProtocolName = "modbustcp",
            DeviceId = "d",
            Connection = json,
        };

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.TagDefinitions.Should().BeEmpty();
    }

    [Fact]
    public void FromSourceInstance_TagDefinitionsArray_ParsesEveryField()
    {
        var json = JsonDocument.Parse("""
        {
          "host": "plc.x",
          "tagDefinitions": [
            {
              "name": "spindle_rpm",
              "unitId": 1,
              "registerClass": "HoldingRegister",
              "address": 0,
              "datatype": "uint16",
              "scanRateMs": 200,
              "unit": "rpm"
            },
            {
              "name": "feed_rate",
              "unitId": 1,
              "registerClass": "HoldingRegister",
              "address": 10,
              "datatype": "float32",
              "byteOrder": "ABCD",
              "scanRateMs": 200,
              "unit": "mm/min"
            },
            {
              "name": "temperature",
              "unitId": 1,
              "registerClass": "InputRegister",
              "address": 0,
              "datatype": "int16",
              "scanRateMs": 500,
              "scale": 0.1,
              "offset": 0.0,
              "unit": "C"
            }
          ]
        }
        """).RootElement;

        var instance = new SourceInstanceConfig
        {
            InstanceId = "p",
            ProtocolName = "modbustcp",
            DeviceId = "d",
            Connection = json,
        };

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.TagDefinitions.Should().HaveCount(3);
        var rpm = cfg.TagDefinitions[0];
        rpm.Name.Should().Be("spindle_rpm");
        rpm.RegisterClass.Should().Be(ModbusRegisterClass.HoldingRegister);
        rpm.Datatype.Should().Be("uint16");

        var feed = cfg.TagDefinitions[1];
        feed.Datatype.Should().Be("float32");
        feed.ByteOrder.Should().Be(ModbusByteOrder.ABCD);

        var temp = cfg.TagDefinitions[2];
        temp.Scale.Should().Be(0.1);
        temp.Offset.Should().Be(0.0);
        temp.Unit.Should().Be("C");
    }

    [Fact]
    public void FromSourceInstance_TagDefinitions_MissingName_Throws()
    {
        var json = JsonDocument.Parse("""
        { "host": "x", "tagDefinitions": [ { "registerClass": "Coil", "address": 0 } ] }
        """).RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "p",
            ProtocolName = "modbustcp",
            DeviceId = "d",
            Connection = json,
        };

        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*missing required field 'name'*");
    }

    [Fact]
    public void FromSourceInstance_TagDefinitions_BadRegisterClass_Throws()
    {
        var json = JsonDocument.Parse("""
        { "host": "x", "tagDefinitions": [ { "name": "t", "registerClass": "NotAClass", "address": 0 } ] }
        """).RootElement;
        var instance = new SourceInstanceConfig
        {
            InstanceId = "p",
            ProtocolName = "modbustcp",
            DeviceId = "d",
            Connection = json,
        };

        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*invalid registerClass 'NotAClass'*");
    }
}
