// ============================================================================
// File: Wire.cs
// Purpose: Minimal little-endian read/write helpers for the EtherNet/IP / CIP
//          wire format. EtherNet/IP is little-endian on the wire, so all
//          multi-byte fields use LE.
// ============================================================================

using System.Buffers.Binary;

namespace ElpisEdgeConnect.Tools.EthernetIpSimulator;

/// <summary>A small growable little-endian byte writer for building responses.</summary>
internal sealed class Wire
{
    private readonly List<byte> _bytes = new(64);

    public int Length => _bytes.Count;

    public Wire U8(byte v) { _bytes.Add(v); return this; }

    public Wire U16(ushort v)
    {
        _bytes.Add((byte)(v & 0xFF));
        _bytes.Add((byte)(v >> 8));
        return this;
    }

    public Wire U32(uint v)
    {
        _bytes.Add((byte)(v & 0xFF));
        _bytes.Add((byte)((v >> 8) & 0xFF));
        _bytes.Add((byte)((v >> 16) & 0xFF));
        _bytes.Add((byte)((v >> 24) & 0xFF));
        return this;
    }

    public Wire Bytes(ReadOnlySpan<byte> v)
    {
        foreach (var b in v)
        {
            _bytes.Add(b);
        }
        return this;
    }

    public byte[] ToArray() => _bytes.ToArray();

    // ---- Span readers (little-endian) ----

    public static ushort ReadU16(ReadOnlySpan<byte> s, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(s.Slice(offset, 2));

    public static uint ReadU32(ReadOnlySpan<byte> s, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(offset, 4));
}
