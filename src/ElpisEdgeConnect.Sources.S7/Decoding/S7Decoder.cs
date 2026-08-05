// ============================================================================
// File: Decoding/S7Decoder.cs
// Purpose: Decode raw S7-wire bytes (big-endian throughout per the
//          protocol) into typed values for CanonicalDataPoint emission.
//          Mirrors ModbusDecoder's static-class shape but is much
//          simpler because S7 is always big-endian — no byte-order
//          matrix to navigate.
//
//          S7 string format on the wire:
//            byte 0: declared MaxLength (M)
//            byte 1: current Length (L), 0..M
//            bytes 2..2+L-1: ASCII payload
//          The decoder honors the current-length field and trims to it.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

using System;
using System.Buffers.Binary;
using System.Text;

namespace ElpisEdgeConnect.Sources.S7.Decoding;

/// <summary>
/// Stateless decoder that converts S7-wire bytes (big-endian) into
/// typed .NET values for the canonical pipeline.
/// </summary>
public static class S7Decoder
{
    /// <summary>
    /// Decode a value of <paramref name="spec"/> from the buffer at
    /// <paramref name="byteOffset"/>. Returns the boxed value matching
    /// <see cref="S7DatatypeSpec.CanonicalType"/>.
    /// </summary>
    /// <param name="buffer">Raw S7-wire bytes (big-endian).</param>
    /// <param name="byteOffset">Offset of the value within <paramref name="buffer"/>.</param>
    /// <param name="bitOffset">Bit offset within the byte; only meaningful for <see cref="S7Datatype.Bool"/>.</param>
    /// <param name="spec">Datatype + (for strings) declared max length.</param>
    public static object Decode(
        ReadOnlySpan<byte> buffer,
        int byteOffset,
        int bitOffset,
        S7DatatypeSpec spec)
    {
        if (byteOffset < 0 || byteOffset + spec.ByteCount > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteOffset),
                $"Decoding {spec.Datatype} at offset {byteOffset} would read past the buffer (buffer={buffer.Length}, need={spec.ByteCount}).");
        }

        return spec.Datatype switch
        {
            S7Datatype.Bool => ((buffer[byteOffset] >> bitOffset) & 0x01) != 0,
            S7Datatype.Byte or S7Datatype.USInt => (int)buffer[byteOffset],
            S7Datatype.SInt => (int)(sbyte)buffer[byteOffset],
            S7Datatype.Char => ((char)buffer[byteOffset]).ToString(),
            S7Datatype.Int => (int)BinaryPrimitives.ReadInt16BigEndian(buffer.Slice(byteOffset, 2)),
            S7Datatype.Word => (int)BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(byteOffset, 2)),
            S7Datatype.DInt => BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(byteOffset, 4)),
            S7Datatype.DWord => (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(byteOffset, 4)),
            S7Datatype.Real => BinaryPrimitives.ReadSingleBigEndian(buffer.Slice(byteOffset, 4)),
            S7Datatype.LReal => BinaryPrimitives.ReadDoubleBigEndian(buffer.Slice(byteOffset, 8)),
            S7Datatype.LInt => BinaryPrimitives.ReadInt64BigEndian(buffer.Slice(byteOffset, 8)),
            S7Datatype.ULInt => (long)BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(byteOffset, 8)),
            S7Datatype.String => DecodeString(buffer.Slice(byteOffset, spec.ByteCount), spec.MaxStringChars),
            _ => throw new InvalidOperationException($"Decoder has no implementation for S7 datatype {spec.Datatype}."),
        };
    }

    private static string DecodeString(ReadOnlySpan<byte> slice, int maxChars)
    {
        // S7 STRING wire format: [maxLen][curLen][char0..charN]
        // We trust the current-length byte but clamp it to the declared
        // max (clamps protect against a malformed-PLC scenario where
        // curLen > maxLen — unlikely on conformant PLCs, but cheap to
        // guard).
        if (slice.Length < 2)
        {
            return string.Empty;
        }
        var current = slice[1];
        var clamped = current > maxChars ? maxChars : current;
        if (clamped <= 0 || 2 + clamped > slice.Length)
        {
            return string.Empty;
        }
        return Encoding.ASCII.GetString(slice.Slice(2, clamped)).TrimEnd();
    }
}
