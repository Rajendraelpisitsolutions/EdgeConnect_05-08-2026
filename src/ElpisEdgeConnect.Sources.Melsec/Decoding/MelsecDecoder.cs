// ============================================================================
// File: Decoding/MelsecDecoder.cs
// Purpose: Pure decoder from a raw little-endian word buffer to a typed value.
//          Slice-1 read types only (Bool, Int16/UInt16, Int32/UInt32, Float32).
//          32-bit values honor a per-tag word order (default low-word-first).
//          Bit values extract the bit from the containing word (word-bit form
//          and packed bit-device word-unit data use the same path). Buffer /
//          offset / datatype faults surface as a typed MelsecDecodeException.
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.Melsec.Decoding;

/// <summary>Thrown for a deterministic decode failure (short buffer, bad offset, unsupported type).</summary>
public sealed class MelsecDecodeException : Exception
{
    /// <summary>Create a decode exception with a diagnostic message.</summary>
    public MelsecDecodeException(string message) : base(message)
    {
    }
}

/// <summary>Stateless decoder for Slice-1 MELSEC read types.</summary>
public static class MelsecDecoder
{
    /// <summary>
    /// Decode one value from <paramref name="wordData"/> (raw little-endian word
    /// bytes, 2 bytes per word) starting at <paramref name="byteOffset"/>.
    /// </summary>
    /// <param name="wordData">The block's little-endian word payload.</param>
    /// <param name="byteOffset">Byte offset of this value within the block.</param>
    /// <param name="datatype">Value type to decode.</param>
    /// <param name="wordOrder">Word order for 32-bit values (ignored for 16-bit / bool).</param>
    /// <param name="bitIndex">Bit index (0..15) for <see cref="MelsecDatatype.Bool"/>; ignored otherwise.</param>
    /// <exception cref="MelsecDecodeException">On a short buffer, bad offset, or unsupported type.</exception>
    public static object Decode(
        ReadOnlySpan<byte> wordData,
        int byteOffset,
        MelsecDatatype datatype,
        MelsecWordOrder wordOrder,
        int? bitIndex)
    {
        if (byteOffset < 0)
        {
            throw new MelsecDecodeException($"negative byte offset {byteOffset}");
        }

        switch (datatype)
        {
            case MelsecDatatype.Bool:
                if (bitIndex is null)
                {
                    throw new MelsecDecodeException("Bool decode requires a bit index");
                }
                if (bitIndex is < 0 or > 15)
                {
                    throw new MelsecDecodeException($"bit index {bitIndex} out of range 0..15");
                }
                return ((ReadWord(wordData, byteOffset) >> bitIndex.Value) & 1) == 1;

            case MelsecDatatype.Int16:
                return unchecked((short)ReadWord(wordData, byteOffset));

            case MelsecDatatype.UInt16:
                return ReadWord(wordData, byteOffset);

            case MelsecDatatype.Int32:
                return unchecked((int)AssembleU32(wordData, byteOffset, wordOrder));

            case MelsecDatatype.UInt32:
                return AssembleU32(wordData, byteOffset, wordOrder);

            case MelsecDatatype.Float32:
                return BitConverter.Int32BitsToSingle(unchecked((int)AssembleU32(wordData, byteOffset, wordOrder)));

            default:
                throw new MelsecDecodeException($"unsupported datatype {datatype}");
        }
    }

    /// <summary>
    /// Apply linear scale/offset (<c>raw * scale + offset</c>) to a numeric value,
    /// mirroring the S7 adapter. Booleans pass through unchanged; when neither
    /// scale nor offset is set the original value is returned.
    /// </summary>
    public static object ApplyScaleOffset(object value, double? scale, double? offset)
    {
        if (value is bool)
        {
            return value;
        }
        if (scale is null && offset is null)
        {
            return value;
        }
        var raw = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return (raw * (scale ?? 1.0)) + (offset ?? 0.0);
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, int byteOffset)
    {
        EnsureLength(data, byteOffset, 2);
        return (ushort)(data[byteOffset] | (data[byteOffset + 1] << 8));
    }

    private static uint AssembleU32(ReadOnlySpan<byte> data, int byteOffset, MelsecWordOrder wordOrder)
    {
        EnsureLength(data, byteOffset, 4);
        ushort word0 = (ushort)(data[byteOffset] | (data[byteOffset + 1] << 8));
        ushort word1 = (ushort)(data[byteOffset + 2] | (data[byteOffset + 3] << 8));
        ushort low = wordOrder == MelsecWordOrder.LowWordFirst ? word0 : word1;
        ushort high = wordOrder == MelsecWordOrder.LowWordFirst ? word1 : word0;
        return (uint)(low | (high << 16));
    }

    private static void EnsureLength(ReadOnlySpan<byte> data, int byteOffset, int needed)
    {
        if (byteOffset + needed > data.Length)
        {
            throw new MelsecDecodeException(
                $"buffer too short: need {needed} bytes at offset {byteOffset}, have {data.Length}");
        }
    }
}
