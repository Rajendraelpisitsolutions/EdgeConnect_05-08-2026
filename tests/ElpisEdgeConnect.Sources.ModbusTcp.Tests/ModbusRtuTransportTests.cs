// ============================================================================
// Tests: Modbus RTU transport — the client factory's encapsulation routing and
//        the FluentModbusRtuClient's RTU framing (request encode + response
//        decode + CRC16) via an injected fake serial port. The round-trip
//        exercises FluentModbus's real RTU framer, de-risking the "does RTU
//        work through our wiring" question without hardware or a socket.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.ModbusTcp;
using FluentAssertions;
using FluentModbus;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusRtuTransportTests
{
    private static ModbusTcpSourceConfiguration Config(ModbusEncapsulation enc, string? serialPort = null) => new()
    {
        InstanceId = "rtu-1",
        ProtocolName = "modbustcp",
        DeviceId = "rtu1",
        DeviceClass = "plc",
        Host = enc == ModbusEncapsulation.SerialRtu ? string.Empty : "127.0.0.1",
        Encapsulation = enc,
        SerialPort = serialPort,
    };

    [Theory]
    [InlineData(ModbusEncapsulation.Tcp, typeof(FluentModbusClient))]
    [InlineData(ModbusEncapsulation.RtuOverTcp, typeof(FluentModbusRtuClient))]
    [InlineData(ModbusEncapsulation.SerialRtu, typeof(FluentModbusRtuClient))]
    public void Factory_SelectsClientByEncapsulation(ModbusEncapsulation enc, Type expected)
    {
        var client = new FluentModbusClientFactory().Create(Config(enc, serialPort: "COM1"));
        client.Should().BeOfType(expected);
    }

    [Fact]
    public async Task RtuClient_ReadHoldingRegister_DecodesValueAndFramesRequest()
    {
        const byte unitId = 1;
        const ushort value = 1234; // 0x04D2

        var port = new CannedRtuSerialPort(BuildReadHoldingResponse(unitId, value));
        await using var client = new FluentModbusRtuClient(port);

        await client.ConnectAsync(
            host: "ignored", port: 0, ModbusEncapsulation.RtuOverTcp,
            connectTimeout: null, readTimeout: TimeSpan.FromSeconds(1), CancellationToken.None);

        var regs = await client.ReadHoldingRegistersAsync(unitId, startAddress: 0, quantity: 1, CancellationToken.None);

        // Decoded value (FluentModbus parsed our CRC-correct RTU response).
        regs.Should().ContainSingle().Which.Should().Be(value);

        // Request was a well-formed RTU FC03 frame with a valid CRC.
        var req = port.Written.ToArray();
        req.Length.Should().Be(8); // addr + fc + start(2) + qty(2) + crc(2)
        req[0].Should().Be(unitId);
        req[1].Should().Be(0x03);
        Crc16(req, 0, 6).Should().Be((ushort)(req[6] | (req[7] << 8)));
    }

    [Fact]
    public async Task RtuClient_Serial_NonexistentPort_ThrowsFatal()
    {
        await using var client = new FluentModbusRtuClient(
            RtuTransportMode.Serial, Config(ModbusEncapsulation.SerialRtu, serialPort: "NOPE_NOT_A_REAL_PORT_999"));

        var act = () => client.ConnectAsync(
            host: string.Empty, port: 0, ModbusEncapsulation.SerialRtu,
            connectTimeout: null, readTimeout: TimeSpan.FromMilliseconds(200), CancellationToken.None);

        await act.Should().ThrowAsync<ModbusFatalException>();
    }

    // ---- helpers ----

    /// <summary>Build an RTU FC03 response frame: addr, fc, byteCount, hi, lo, CRC.</summary>
    private static byte[] BuildReadHoldingResponse(byte unitId, ushort value)
    {
        var frame = new byte[7];
        frame[0] = unitId;
        frame[1] = 0x03;
        frame[2] = 0x02;                 // byte count
        frame[3] = (byte)(value >> 8);   // big-endian hi
        frame[4] = (byte)(value & 0xFF); // lo
        var crc = Crc16(frame, 0, 5);
        frame[5] = (byte)(crc & 0xFF);   // CRC low byte first on the wire
        frame[6] = (byte)(crc >> 8);
        return frame;
    }

    /// <summary>Standard Modbus CRC16 (poly 0xA001, init 0xFFFF).</summary>
    private static ushort Crc16(byte[] data, int offset, int length)
    {
        ushort crc = 0xFFFF;
        for (var i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            for (var b = 0; b < 8; b++)
            {
                if ((crc & 1) != 0) { crc >>= 1; crc ^= 0xA001; }
                else { crc >>= 1; }
            }
        }
        return crc;
    }

    /// <summary>
    /// A fake IModbusRtuSerialPort that captures written request bytes and
    /// serves a pre-canned response on read. Lets the FluentModbus RTU framer
    /// run end-to-end with no real port.
    /// </summary>
    private sealed class CannedRtuSerialPort : IModbusRtuSerialPort
    {
        private readonly byte[] _response;
        private int _readPos;

        public CannedRtuSerialPort(byte[] response) => _response = response;

        public List<byte> Written { get; } = new();

        public string PortName => "canned";
        public bool IsOpen => true;
        public void Open() { }
        public void Close() { }

        public int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _response.Length - _readPos);
            if (n <= 0)
            {
                throw new TimeoutException("CannedRtuSerialPort: no more bytes to serve.");
            }
            Array.Copy(_response, _readPos, buffer, offset, n);
            _readPos += n;
            return n;
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => Task.FromResult(Read(buffer, offset, count));

        public void Write(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                Written.Add(buffer[offset + i]);
            }
        }

        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }
}
