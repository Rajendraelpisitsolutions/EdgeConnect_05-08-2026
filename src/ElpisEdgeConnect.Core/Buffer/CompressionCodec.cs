// ============================================================================
// File: Buffer/CompressionCodec.cs
// Purpose: Stateless LZ4 compress/decompress wrapper. Used by C2b's
//          persistent buffer; ships in C2a so its benchmarks land in the
//          phase-1 baseline numbers.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, PHASE1_EXECUTION_PLAN.md C2a
// ============================================================================

using System;
using K4os.Compression.LZ4;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// LZ4 compress/decompress wrapper. Stateless and thread-safe.
/// </summary>
/// <remarks>
/// The compressed payload format is:
///   <c>[int32 originalLength][lz4 block bytes...]</c>
/// The original length prefix lets <see cref="Decompress"/> allocate the
/// destination buffer exactly without scanning the LZ4 stream.
/// </remarks>
public static class CompressionCodec
{
    /// <summary>Compress raw bytes using LZ4 fast (default level).</summary>
    public static byte[] Compress(ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
        {
            return new byte[sizeof(int)];
        }

        var maxOutput = LZ4Codec.MaximumOutputSize(source.Length);
        var buffer = new byte[sizeof(int) + maxOutput];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, sizeof(int)), source.Length);

        var written = LZ4Codec.Encode(source, buffer.AsSpan(sizeof(int)));
        if (written < 0)
        {
            throw new InvalidOperationException("LZ4 encode failed.");
        }

        // Trim oversized buffer.
        var result = new byte[sizeof(int) + written];
        Array.Copy(buffer, result, result.Length);
        return result;
    }

    /// <summary>Decompress an LZ4-compressed payload produced by <see cref="Compress"/>.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length < sizeof(int))
        {
            throw new InvalidOperationException("Compressed payload is too short to contain a length prefix.");
        }

        var originalLength = BitConverter.ToInt32(compressed[..sizeof(int)]);
        if (originalLength == 0)
        {
            return Array.Empty<byte>();
        }
        if (originalLength < 0)
        {
            throw new InvalidOperationException($"Invalid original length in compressed payload: {originalLength}");
        }

        var dest = new byte[originalLength];
        var written = LZ4Codec.Decode(compressed[sizeof(int)..], dest);
        if (written != originalLength)
        {
            throw new InvalidOperationException($"LZ4 decode produced {written} bytes; expected {originalLength}.");
        }
        return dest;
    }
}
