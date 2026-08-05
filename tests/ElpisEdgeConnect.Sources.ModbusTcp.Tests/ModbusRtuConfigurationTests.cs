// ============================================================================
// Tests: Modbus RTU configuration — parsing serial fields from the connection
//        JSON and the host-vs-serial validation branch in the adapter.
// ============================================================================

using System.IO.Ports;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusRtuConfigurationTests
{
    private static ModbusTcpSourceConfiguration Parse(string connJson)
    {
        var instance = new SourceInstanceConfig
        {
            InstanceId = "rtu-1",
            ProtocolName = "modbustcp",
            DeviceId = "rtu1",
            Connection = JsonDocument.Parse(connJson).RootElement,
        };
        return ModbusTcpSourceConfiguration.FromSourceInstance(instance);
    }

    [Fact]
    public void FromSourceInstance_SerialRtu_ParsesSerialFields()
    {
        var cfg = Parse("""
        {
          "encapsulation": "serialRtu",
          "serialPort": "/dev/ttyUSB0",
          "baudRate": 19200,
          "parity": "even",
          "stopBits": "two",
          "handshake": "none"
        }
        """);

        cfg.Encapsulation.Should().Be(ModbusEncapsulation.SerialRtu);
        cfg.SerialPort.Should().Be("/dev/ttyUSB0");
        cfg.BaudRate.Should().Be(19200);
        cfg.SerialParity.Should().Be(Parity.Even);
        cfg.SerialStopBits.Should().Be(StopBits.Two);
        cfg.SerialHandshake.Should().Be(Handshake.None);
    }

    [Fact]
    public void FromSourceInstance_SerialRtu_DoesNotRequireHost()
    {
        // No "host" key at all — serial addresses the slave by serial port.
        var act = () => Parse("""{ "encapsulation": "serial", "serialPort": "COM3" }""");
        act.Should().NotThrow();
    }

    [Fact]
    public void FromSourceInstance_Tcp_StillRequiresHost()
    {
        var act = () => Parse("""{ "encapsulation": "tcp" }""");
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void FromSourceInstance_SerialDefaults_WhenOmitted()
    {
        var cfg = Parse("""{ "encapsulation": "serialRtu", "serialPort": "COM1" }""");
        cfg.BaudRate.Should().Be(9600);
        cfg.SerialParity.Should().Be(Parity.None);
        cfg.SerialStopBits.Should().Be(StopBits.One);
        cfg.SerialHandshake.Should().Be(Handshake.None);
    }

    // ---- Validation branch (host vs serial) ----

    private static ModbusTcpSourceAdapter Adapter() =>
        new("rtu-1", new FakeModbusClient(), NullLogger.Instance);

    private static ModbusTcpSourceConfiguration BaseConfig(ModbusEncapsulation enc) => new()
    {
        InstanceId = "rtu-1",
        // ADR-0033: RTU encapsulations belong to the 'modbusrtu' protocol;
        // native TCP belongs to 'modbustcp'.
        ProtocolName = enc == ModbusEncapsulation.Tcp ? "modbustcp" : "modbusrtu",
        DeviceId = "rtu1",
        DeviceClass = "plc",
        Host = string.Empty,
        Encapsulation = enc,
    };

    [Fact]
    public async Task Validate_SerialRtu_RequiresSerialPort()
    {
        var cfg = BaseConfig(ModbusEncapsulation.SerialRtu); // SerialPort null
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "SerialPort");
    }

    [Fact]
    public async Task Validate_SerialRtu_WithSerialPort_IsValid()
    {
        var cfg = BaseConfig(ModbusEncapsulation.SerialRtu) with { SerialPort = "COM3", BaudRate = 9600 };
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_Tcp_RequiresHost()
    {
        var cfg = BaseConfig(ModbusEncapsulation.Tcp); // Host empty
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "Host");
    }

    // ---- ADR-0033 protocol ↔ encapsulation pairing ----

    [Fact]
    public async Task Validate_ModbusTcpProtocol_RejectsRtuEncapsulation()
    {
        var cfg = new ModbusTcpSourceConfiguration
        {
            InstanceId = "x", ProtocolName = "modbustcp", DeviceId = "x", DeviceClass = "plc",
            Host = "10.0.0.1", Encapsulation = ModbusEncapsulation.SerialRtu, SerialPort = "COM3",
        };
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "Encapsulation");
    }

    [Fact]
    public async Task Validate_ModbusRtuProtocol_RejectsTcpEncapsulation()
    {
        var cfg = new ModbusTcpSourceConfiguration
        {
            InstanceId = "x", ProtocolName = "modbusrtu", DeviceId = "x", DeviceClass = "plc",
            Host = "10.0.0.1", Encapsulation = ModbusEncapsulation.Tcp,
        };
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "Encapsulation");
    }

    [Fact]
    public async Task Validate_ModbusRtuProtocol_RtuOverTcp_IsValid()
    {
        var cfg = new ModbusTcpSourceConfiguration
        {
            InstanceId = "x", ProtocolName = "modbusrtu", DeviceId = "x", DeviceClass = "plc",
            Host = "10.0.0.1", Encapsulation = ModbusEncapsulation.RtuOverTcp,
        };
        var result = await Adapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }
}
