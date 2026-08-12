// ============================================================================
// File: ModbusRtuSlave.cs
// Purpose: Hand-rolled Modbus RTU read-frame processor. Given an 8-byte RTU
//          read request (unit + FC + addr + qty + CRC16), produces the
//          CRC-correct RTU response. Supports FC01/FC02 (bits) and FC03/FC04
//          (registers). Register values are deterministic so tests assert
//          exact values; writes are out of scope (the adapter only reads).
// ============================================================================

namespace ElpisEdgeConnect.Tools.ModbusRtuSimulator;

internal static class ModbusRtuSlave
{
    /// <summary>
    /// Process one 8-byte RTU read request. Returns the response frame, or null
    /// when the CRC is invalid (a real slave stays silent on a bad frame).
    /// </summary>
    public static byte[]? ProcessReadRequest(byte[] req, int length)
    {
        if (length < 8)
        {
            return null;
        }

        // Validate request CRC (last two bytes, low byte first).
        var expectedCrc = (ushort)(req[6] | (req[7] << 8));
        if (Crc16(req, 0, 6) != expectedCrc)
        {
            return null;
        }

        var unit = req[0];
        var fc = req[1];
        var address = (ushort)((req[2] << 8) | req[3]);
        var quantity = (ushort)((req[4] << 8) | req[5]);

        return fc switch
        {
            0x01 or 0x02 => BuildBitResponse(unit, fc, address, quantity),
            0x03 or 0x04 => BuildRegisterResponse(unit, fc, address, quantity),
            _ => BuildException(unit, fc, exceptionCode: 0x01), // illegal function
        };
    }

    private static byte[] BuildRegisterResponse(byte unit, byte fc, ushort address, ushort quantity)
    {
        if (quantity is < 1 or > 125)
        {
            return BuildException(unit, fc, exceptionCode: 0x03); // illegal data value
        }

        var byteCount = (byte)(quantity * 2);
        var body = new byte[3 + byteCount];
        body[0] = unit;
        body[1] = fc;
        body[2] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            var value = RegisterValue((ushort)(address + i));
            body[3 + (i * 2)] = (byte)(value >> 8);   // big-endian hi
            body[3 + (i * 2) + 1] = (byte)(value & 0xFF);
        }
        return Frame(body);
    }

    private static byte[] BuildBitResponse(byte unit, byte fc, ushort address, ushort quantity)
    {
        if (quantity is < 1 or > 2000)
        {
            return BuildException(unit, fc, exceptionCode: 0x03);
        }

        var byteCount = (byte)((quantity + 7) / 8);
        var body = new byte[3 + byteCount];
        body[0] = unit;
        body[1] = fc;
        body[2] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            if (BitValue((ushort)(address + i)))
            {
                body[3 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }
        return Frame(body);
    }

    private static byte[] BuildException(byte unit, byte fc, byte exceptionCode)
        => Frame(new byte[] { unit, (byte)(fc | 0x80), exceptionCode });

    /// <summary>Deterministic holding/input register value for an address.</summary>
    private static ushort RegisterValue(ushort address) => (ushort)(1000 + address);

    /// <summary>Deterministic coil/discrete-input value for an address.</summary>
    private static bool BitValue(ushort address) => (address % 2) == 0;

    /// <summary>Append the CRC16 (low byte first) to a frame body.</summary>
    private static byte[] Frame(byte[] body)
    {
        var crc = Crc16(body, 0, body.Length);
        var frame = new byte[body.Length + 2];
        System.Array.Copy(body, frame, body.Length);
        frame[body.Length] = (byte)(crc & 0xFF);
        frame[body.Length + 1] = (byte)(crc >> 8);
        return frame;
    }

    /// <summary>Standard Modbus CRC16 (poly 0xA001, init 0xFFFF).</summary>
    public static ushort Crc16(byte[] data, int offset, int length)
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
}
