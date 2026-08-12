// ============================================================================
// Tests: ModbusSourceWizardModel serial-RTU support — emitting the serial
//        connection block, parsing it back through the adapter's config
//        factory, the Build → Hydrate → Build round-trip, and that TCP sources
//        do not gain serial keys.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class ModbusRtuWizardModelTests
{
    private static ModbusSourceWizardModel SerialModel() => new()
    {
        InstanceId = "rtu-line-3",
        DeviceId = "dev3",
        DeviceClass = "plc",
        ProtocolName = "modbusrtu",
        Encapsulation = "SerialRtu",
        SerialPort = "/dev/ttyUSB0",
        BaudRate = 19200,
        Parity = "Even",
        StopBits = "Two",
        Handshake = "None",
    };

    [Fact]
    public void Build_SerialRtu_EmitsSerialConnectionBlock()
    {
        var conn = SerialModel().BuildSourceInstance().Connection!.Value;

        conn.GetProperty("encapsulation").GetString().Should().Be("SerialRtu");
        conn.GetProperty("serialPort").GetString().Should().Be("/dev/ttyUSB0");
        conn.GetProperty("baudRate").GetInt32().Should().Be(19200);
        conn.GetProperty("parity").GetString().Should().Be("Even");
        conn.GetProperty("stopBits").GetString().Should().Be("Two");
    }

    [Fact]
    public void Build_SerialRtu_ParsesIntoSerialConfig()
    {
        // The wizard's emitted connection JSON must round-trip through the
        // adapter's own config factory to a SerialRtu configuration.
        var instance = SerialModel().BuildSourceInstance();

        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(instance);

        cfg.Encapsulation.Should().Be(ModbusEncapsulation.SerialRtu);
        cfg.SerialPort.Should().Be("/dev/ttyUSB0");
        cfg.BaudRate.Should().Be(19200);
        cfg.SerialParity.Should().Be(System.IO.Ports.Parity.Even);
        cfg.SerialStopBits.Should().Be(System.IO.Ports.StopBits.Two);
    }

    [Fact]
    public void Hydrate_SerialRtu_RoundTripsThroughBuild()
    {
        var built = SerialModel().BuildSourceInstance();

        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(built);

        hydrated.Encapsulation.Should().Be("SerialRtu");
        hydrated.SerialPort.Should().Be("/dev/ttyUSB0");
        hydrated.BaudRate.Should().Be(19200);
        hydrated.Parity.Should().Be("Even");
        hydrated.StopBits.Should().Be("Two");
        hydrated.Handshake.Should().Be("None");

        // Re-emitting preserves the serial block (byte-stable round-trip).
        var conn1 = built.Connection!.Value.GetRawText();
        var conn2 = hydrated.BuildSourceInstance().Connection!.Value.GetRawText();
        conn2.Should().Be(conn1);
    }

    [Fact]
    public void Build_Tcp_DoesNotEmitSerialKeys()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "tcp-1",
            DeviceId = "dev1",
            Encapsulation = "Tcp",
            Host = "192.168.1.42",
        };

        var conn = model.BuildSourceInstance().Connection!.Value;

        conn.TryGetProperty("serialPort", out _).Should().BeFalse();
        conn.TryGetProperty("baudRate", out _).Should().BeFalse();
        conn.TryGetProperty("parity", out _).Should().BeFalse();
    }
}
