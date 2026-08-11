// ============================================================================
// File: Decoding/ModbusByteOrderReorder.cs
// Purpose: Reorder wire bytes into canonical big-endian layout so the
//          decoder can uniformly read them with BinaryPrimitives.ReadXxx.
//          Keeps the per-byte permutation tables in one place so
//          ModbusDecoder stays readable.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Decoding;

/// <summary>
/// Permute a span of raw wire bytes into "big-endian target byte order" so
/// downstream reinterpret calls can use <see cref="System.Buffers.Binary.BinaryPrimitives"/>
/// read-big-endian helpers uniformly. Pure, stateless, no allocation.
/// </summary>
internal static class ModbusByteOrderReorder
{
    /// <summary>
    /// Copy <paramref name="wire"/> into <paramref name="dest"/> with the
    /// permutation required by <paramref name="order"/>. Both spans must be
    /// the same length, matching <see cref="ModbusByteOrderExtensions.ByteCount"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="wire"/>'s length does not match the byte
    /// count expected by <paramref name="order"/>.
    /// </exception>
    public static void Reorder(ReadOnlySpan<byte> wire, Span<byte> dest, ModbusByteOrder order)
    {
        var expected = order.ByteCount();
        if (wire.Length != expected || dest.Length != expected)
        {
            throw new ArgumentException(
                $"Byte-order {order} expects {expected} bytes, got wire={wire.Length} dest={dest.Length}.");
        }

        switch (order)
        {
            case ModbusByteOrder.AB:
            case ModbusByteOrder.ABCD:
            case ModbusByteOrder.ABCDEFGH:
                // Already big-endian — straight copy.
                wire.CopyTo(dest);
                return;

            case ModbusByteOrder.BA:
                dest[0] = wire[1]; dest[1] = wire[0];
                return;

            case ModbusByteOrder.CDAB:
                // Swap word order, keep byte order within each word.
                // wire [A B C D] → dest [C D A B]
                dest[0] = wire[2]; dest[1] = wire[3];
                dest[2] = wire[0]; dest[3] = wire[1];
                return;

            case ModbusByteOrder.BADC:
                // Swap bytes within each word, keep word order.
                // wire [A B C D] → dest [B A D C]
                dest[0] = wire[1]; dest[1] = wire[0];
                dest[2] = wire[3]; dest[3] = wire[2];
                return;

            case ModbusByteOrder.DCBA:
                // Full reverse.
                for (var i = 0; i < 4; i++) dest[i] = wire[3 - i];
                return;

            case ModbusByteOrder.HGFEDCBA:
                for (var i = 0; i < 8; i++) dest[i] = wire[7 - i];
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(order), order, null);
        }
    }

    /// <summary>
    /// Copy the raw high-byte-then-low-byte wire layout from a span of
    /// registers into a flat byte span. Every register contributes two
    /// bytes: <c>(reg &gt;&gt; 8)</c> then <c>(reg &amp; 0xFF)</c>.
    /// </summary>
    public static void RegistersToWireBytes(ReadOnlySpan<ushort> registers, Span<byte> wire)
    {
        if (wire.Length != registers.Length * 2)
        {
            throw new ArgumentException(
                $"wire buffer must be {registers.Length * 2} bytes for {registers.Length} registers, got {wire.Length}.");
        }
        for (var i = 0; i < registers.Length; i++)
        {
            wire[i * 2] = (byte)(registers[i] >> 8);
            wire[i * 2 + 1] = (byte)(registers[i] & 0xFF);
        }
    }
}
